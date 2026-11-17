using UnityEngine;
// play mode: hands / forearms vs. the table top slab
public class Hands
{
    public static string Reset()
    {
        foreach (var l in Object.FindObjectsByType<OfficeListener>(FindObjectsSortMode.None)) l.ResetHandStats();
        Time.timeScale = 6f; return "reset, x6";
    }
    public static string ResetNormal()
    {
        Time.timeScale = 1f;
        foreach (var l in Object.FindObjectsByType<OfficeListener>(FindObjectsSortMode.None)) l.ResetHandStats();
        return "reset, x1";
    }
    public static string Stats()
    {
        string r = "";
        foreach (var l in Object.FindObjectsByType<OfficeListener>(FindObjectsSortMode.None))
            if (l.gameObject.activeInHierarchy)
                r += l.seat + " closest " + (l.minClearance * 100f).ToString("0.0") + " cm (" + l.minWhere + "), touching in " + (l.correctedShare * 100f).ToString("0.0") + "% of frames " + l.badMoments + " | biggest lift step " + (l.maxLiftStep * 1000f).ToString("0.0") + " mm/frame\n";
        Time.timeScale = 1f;
        return r;
    }

    // exact check of the current pose: forearm (radius 4 cm) and hand / finger bones (radius 1.3 cm) must not intersect the
    // table slab (top 0.74, 4.5 cm thick). Reports, per person, the worst overlap and where (negative clearance = inside).
    public static string Pose()
    {
        string r = "";
        foreach (var l in Object.FindObjectsByType<OfficeListener>(FindObjectsSortMode.None))
        {
            if (!l.gameObject.activeInHierarchy || !l.hasTable) continue;
            var an = l.GetComponent<Animator>(); var t = l.table;
            float worst = 9f; string where = "";
            foreach (bool left in new[] { true, false })
            {
                var el = an.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                var wr = an.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                for (int i = 0; i <= 10; i++)
                {
                    var p = Vector3.Lerp(el.position, wr.position, i / 10f);
                    float c = Clear(p, 0.04f, t); if (c < worst) { worst = c; where = (left ? "L" : "R") + " forearm " + i * 10 + "%"; }
                }
                int a = left ? (int)HumanBodyBones.LeftThumbProximal : (int)HumanBodyBones.RightThumbProximal;
                for (int b = a; b < a + 15; b++)
                {
                    var bt = an.GetBoneTransform((HumanBodyBones)b); if (!bt) continue;
                    float c = Clear(bt.position, 0.013f, t); if (c < worst) { worst = c; where = (left ? "L " : "R ") + (HumanBodyBones)b; }
                }
                float ch = Clear(wr.position, 0.025f, t); if (ch < worst) { worst = ch; where = (left ? "L" : "R") + " hand"; }
            }
            r += l.seat + (worst < 0f ? " INSIDE " : " ok ") + (worst * 100f).ToString("0.0") + " cm (" + where + ")\n";
        }
        return r;
    }

    // clearance of a sphere (centre p, radius rad) to the table slab box: < 0 = overlaps
    static float Clear(Vector3 p, float rad, Bounds t)
    {
        var slab = new Bounds(); slab.SetMinMax(new Vector3(t.min.x, t.max.y - 0.045f, t.min.z), t.max);
        return Mathf.Sqrt(slab.SqrDistance(p)) - rad;
    }
}
