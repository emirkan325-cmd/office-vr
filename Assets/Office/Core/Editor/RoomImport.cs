using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Any room: texture import settings + URP Lit materials built from the room's materials.json
// (exported from Blender) + remapping the FBX's materials onto them.
public static class RoomImport
{
    static string Root, TexDir, GenDir, MatDir, Fbx;

    // root = Assets/Relax/Rooms/<Room>, the FBX is <root>/<Room>.fbx
    public static string Run(string root)
    {
        Root = root; TexDir = Root + "/Textures"; GenDir = Root + "/Textures/Generated"; MatDir = Root + "/Materials";
        Fbx = Root + "/" + System.IO.Path.GetFileName(root) + ".fbx";
        if (!AssetDatabase.IsValidFolder(TexDir)) AssetDatabase.CreateFolder(Root, "Textures");
        return Execute();
    }

    [System.Serializable] class MatEntry
    {
        public string name, @base, normal, rough, metal, alpha;
        public float[] color, emission;
        public float roughness, metallic, emission_strength, tiling, coat;
        public float[] tint;      // Blender "Tint" multiply node (facade colours etc.)
        public int drop_base;     // use the tint as a flat colour instead of the colour map
    }

    static string Execute()
    {
        var log = new StringBuilder();
        if (!AssetDatabase.IsValidFolder(GenDir)) AssetDatabase.CreateFolder(TexDir, "Generated");
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(Root, "Materials");

        SetupTextures(log);
        var mats = ReadMaterials();
        log.AppendLine("materials in json: " + mats.Count);

        var built = new Dictionary<string, Material>();
        foreach (var m in mats) built[m.name] = BuildMaterial(m, log);
        AssetDatabase.SaveAssets();

        // fbx: lightmap UVs + remap materials by name
        var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
        mi.generateSecondaryUV = true;
        mi.secondaryUVHardAngle = 88; mi.secondaryUVPackMargin = 8;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.importCameras = false; mi.importLights = false; mi.importAnimation = false;
        mi.animationType = ModelImporterAnimationType.None;
        int remapped = 0;
        foreach (var kv in built)
        {
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            remapped++;
        }
        mi.SaveAndReimport();
        log.AppendLine("fbx reimported, remapped " + remapped);

        // report fbx materials that found no match
        foreach (var r in AssetDatabase.LoadAllAssetRepresentationsAtPath(Fbx))
            if (r is Material) log.AppendLine("UNMAPPED embedded material: " + r.name);
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        var missing = new HashSet<string>();
        foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
            foreach (var sm in rend.sharedMaterials)
                if (sm == null || AssetDatabase.GetAssetPath(sm) == Fbx) missing.Add(rend.name);
        if (missing.Count > 0) log.AppendLine("renderers without remapped material: " + string.Join(", ", missing));
        return log.ToString();
    }

    static List<MatEntry> ReadMaterials()
    {
        // materials.json is { "name": {...}, ... }; JsonUtility can't read dictionaries, so split by hand
        var text = File.ReadAllText(Root + "/materials.json");
        var list = new List<MatEntry>();
        int i = 0;
        while (true)
        {
            int q1 = text.IndexOf("\"", i); if (q1 < 0) break;
            int q2 = text.IndexOf("\"", q1 + 1);
            string key = text.Substring(q1 + 1, q2 - q1 - 1);
            int b1 = text.IndexOf("{", q2); if (b1 < 0) break;
            int depth = 0, b2 = b1;
            for (; b2 < text.Length; b2++) { if (text[b2] == '{') depth++; else if (text[b2] == '}' && --depth == 0) break; }
            var body = text.Substring(b1, b2 - b1 + 1);
            var e = JsonUtility.FromJson<MatEntry>(body);   // C# field @base serializes as "base"
            e.name = key;
            list.Add(e);
            i = b2 + 1;
        }
        return list;
    }

    static void SetupTextures(StringBuilder log)
    {
        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var ti = AssetImporter.GetAtPath(path) as TextureImporter; if (ti == null) continue;
            var file = Path.GetFileNameWithoutExtension(path).ToLower();
            bool isNormal = file.Contains("_nor");
            bool isData = file.Contains("rough") || file.Contains("metal") || file.Contains("opacity") || file.EndsWith("_ms");
            ti.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !(isNormal || isData);
            ti.isReadable = false;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 2;
            bool big = file.Contains("wood_floor") || file.Contains("white_stucco") || file.Contains("atlas") || file.Contains("wingpanel");
            ti.maxTextureSize = 2048;
            var android = ti.GetPlatformTextureSettings("Android");
            android.overridden = true; android.maxTextureSize = big ? 2048 : 1024;
            android.format = isNormal ? TextureImporterFormat.ASTC_5x5 : TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(android);
            ti.SaveAndReimport(); n++;
        }
        log.AppendLine("textures configured: " + n);
    }

    static Texture2D Tex(string file)
    {
        if (string.IsNullOrEmpty(file)) return null;
        return AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + file);
    }

    static Color Gamma(float[] c, float a = 1f) => new Color(c[0], c[1], c[2], a).gamma;

    static Material BuildMaterial(MatEntry e, StringBuilder log)
    {
        string path = MatDir + "/" + Safe(e.name) + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = Shader.Find("Universal Render Pipeline/Lit");
        mat.shaderKeywords = new string[0];

        // base colour (+ alpha from a separate opacity map, merged into the base alpha)
        // Blender sometimes routes an SSS/other map into Base Color; prefer the real _diff map when present
        if (!string.IsNullOrEmpty(e.@base) && !e.@base.Contains("_diff") && !e.@base.ToLower().Contains("diffuse") && !e.@base.Contains("baseColor"))
        {
            var guess = Regex.Replace(e.@base, "_(sss|disp|rough|nor_gl)_", "_diff_");
            foreach (var ext in new[] { "", ".png" })
                if (guess != e.@base && File.Exists(TexDir + "/" + guess)) { e.@base = guess; break; }
        }
        var baseTex = Tex(e.@base);
        if (!string.IsNullOrEmpty(e.alpha) && baseTex != null)
        {
            // the colour map may already carry the alpha (e.g. tree impostor atlas): no merge needed
            if (e.alpha == e.@base)
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(TexDir + "/" + e.@base);
                if (!ti.alphaIsTransparency) { ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = true; ti.SaveAndReimport(); }
            }
            else baseTex = MergeAlpha(e.@base, e.alpha);
            mat.SetFloat("_AlphaClip", 1); mat.SetFloat("_Cutoff", 0.5f); mat.EnableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }
        bool hasTint = e.tint != null && e.tint.Length == 3;
        if (e.drop_base == 1) baseTex = null;
        mat.SetTexture("_BaseMap", baseTex);
        mat.SetColor("_BaseColor", hasTint ? new Color(e.tint[0], e.tint[1], e.tint[2]) : (baseTex != null ? Color.white : Gamma(e.color)));
        var tiling = new Vector2(e.tiling > 0 ? e.tiling : 1f, e.tiling > 0 ? e.tiling : 1f);
        mat.SetTextureScale("_BaseMap", tiling);

        var nrm = Tex(e.normal);
        if (nrm != null) { mat.SetTexture("_BumpMap", nrm); mat.SetFloat("_BumpScale", 1f); mat.EnableKeyword("_NORMALMAP"); }

        // metallic (R) + smoothness (A) packed from Blender's metal/roughness maps
        if (!string.IsNullOrEmpty(e.rough) || !string.IsNullOrEmpty(e.metal))
        {
            var ms = PackMetalSmooth(e);
            mat.SetTexture("_MetallicGlossMap", ms); mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Smoothness", 1f); mat.SetFloat("_Metallic", 1f);
        }
        else
        {
            mat.SetFloat("_Metallic", e.metallic);
            float smooth = 1f - e.roughness;
            if (e.coat > 0.3f) smooth = Mathf.Max(smooth, 0.88f);   // glazed tiles: URP Lit has no clear coat
            mat.SetFloat("_Smoothness", smooth);
        }

        if (e.emission_strength > 0.01f && (e.emission[0] + e.emission[1] + e.emission[2]) > 0.01f && string.IsNullOrEmpty(e.@base))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Gamma(e.emission) * e.emission_strength * 0.6f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }
        else mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;

        // blanket: cream wool (keep the fleece normal map, drop the orange colour map)
        if (e.name == "caban") { mat.SetTexture("_BaseMap", null); mat.SetColor("_BaseColor", new Color(0.86f, 0.80f, 0.70f)); }
        if (e.name.StartsWith("quatrefoil_jacquard")) mat.SetColor("_BaseColor", new Color(0.78f, 0.68f, 0.66f));   // calmer rug
        if (e.name == "M_Glass") MakeGlass(mat);
        if (e.name.EndsWith("_glass"))   // picture / lantern glass: was transmissive in Blender, must not hide what is behind it
        {
            mat.SetTexture("_BaseMap", null); mat.SetTexture("_MetallicGlossMap", null); mat.SetTexture("_BumpMap", null);
            mat.DisableKeyword("_METALLICSPECGLOSSMAP"); mat.DisableKeyword("_NORMALMAP");
            MakeGlass(mat); mat.SetColor("_BaseColor", new Color(0.9f, 0.92f, 0.95f, 0.06f));
        }
        if (e.name == "M_ShadeGlow") { mat.SetFloat("_Surface", 1); MakeTransparent(mat, new Color(1f, 0.72f, 0.42f, 0.18f)); }

        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void MakeGlass(Material mat)
    {
        MakeTransparent(mat, new Color(0.75f, 0.82f, 0.9f, 0.10f));
        mat.SetFloat("_Smoothness", 0.96f);
        mat.SetFloat("_Metallic", 0f);
    }

    public static void MakeTransparent(Material mat, Color c)
    {
        mat.SetFloat("_Surface", 1); mat.SetFloat("_Blend", 0);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetColor("_BaseColor", c);
    }

    static string Safe(string s) { foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_'); return s; }

    static Texture2D LoadRaw(string file)
    {
        if (string.IsNullOrEmpty(file)) return null;
        var full = Path.Combine(Directory.GetCurrentDirectory(), TexDir, file);
        if (!File.Exists(full)) return null;
        if (file.ToLower().EndsWith(".exr"))
        {
            // LoadImage can't read EXR: go through the importer with CPU access instead
            var p = TexDir + "/" + file;
            var ti = (TextureImporter)AssetImporter.GetAtPath(p);
            if (!ti.isReadable || ti.textureCompression != TextureImporterCompression.Uncompressed)
            { ti.isReadable = true; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.SaveAndReimport(); }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        t.LoadImage(File.ReadAllBytes(full));
        return t;
    }

    static Texture2D PackMetalSmooth(MatEntry e)
    {
        string outName = Safe(e.name) + "_MS.png";
        string outPath = GenDir + "/" + outName;
        var rough = LoadRaw(e.rough); var metal = LoadRaw(e.metal);
        // glTF-style packed map (Sketchfab models): G = roughness, B = metallic
        bool gltf = (e.rough ?? "").Contains("metallicRoughness") || (e.metal ?? "").Contains("metallicRoughness");
        if (gltf) { var packed = rough ?? metal; rough = packed; metal = packed; }
        int w = rough != null ? rough.width : metal.width, h = rough != null ? rough.height : metal.height;
        int size = Mathf.Min(Mathf.Max(w, h), 1024);
        var outTex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float r = rough != null ? (gltf ? rough.GetPixelBilinear(u, v).g : rough.GetPixelBilinear(u, v).r) : e.roughness;
                float m = metal != null ? (gltf ? metal.GetPixelBilinear(u, v).b : metal.GetPixelBilinear(u, v).r) : e.metallic;
                px[y * size + x] = new Color(m, 0, 0, 1f - r);
            }
        outTex.SetPixels(px); outTex.Apply();
        File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), outPath), outTex.EncodeToPNG());
        AssetDatabase.ImportAsset(outPath);
        var ti = (TextureImporter)AssetImporter.GetAtPath(outPath);
        ti.sRGBTexture = false; ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = false;
        var android = ti.GetPlatformTextureSettings("Android"); android.overridden = true; android.maxTextureSize = 1024; android.format = TextureImporterFormat.ASTC_6x6;
        ti.SetPlatformTextureSettings(android); ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
    }

    static Texture2D MergeAlpha(string baseFile, string alphaFile)
    {
        string outPath = GenDir + "/" + Path.GetFileNameWithoutExtension(baseFile) + "_A.png";
        var b = LoadRaw(baseFile); var a = LoadRaw(alphaFile);
        var px = b.GetPixels();
        for (int y = 0; y < b.height; y++)
            for (int x = 0; x < b.width; x++)
                px[y * b.width + x].a = a.GetPixelBilinear((x + 0.5f) / b.width, (y + 0.5f) / b.height).r;
        var o = new Texture2D(b.width, b.height, TextureFormat.RGBA32, false, false); o.SetPixels(px); o.Apply();
        File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), outPath), o.EncodeToPNG());
        AssetDatabase.ImportAsset(outPath);
        var ti = (TextureImporter)AssetImporter.GetAtPath(outPath); ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = true;
        var android = ti.GetPlatformTextureSettings("Android"); android.overridden = true; android.maxTextureSize = 1024; android.format = TextureImporterFormat.ASTC_6x6;
        ti.SetPlatformTextureSettings(android); ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
    }
}
