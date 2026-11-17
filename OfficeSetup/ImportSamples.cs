using System.Linq;
using System.Text;
using UnityEditor.PackageManager.UI;

public static class ImportSamples
{
    public static string Execute()
    {
        var log = new StringBuilder();
        void Imp(string pkg, string name)
        {
            var s = Sample.FindByPackage(pkg, null).FirstOrDefault(x => x.displayName == name);
            if (s.displayName == null) { log.AppendLine("not found " + pkg + "/" + name); return; }
            if (s.isImported) { log.AppendLine("already " + name); return; }
            bool ok = s.Import(Sample.ImportOptions.OverridePreviousImports);
            log.AppendLine((ok ? "imported " : "FAILED ") + name);
        }
        Imp("com.unity.xr.hands", "HandVisualizer");
        Imp("com.unity.xr.interaction.toolkit", "XR Interaction Simulator");
        Imp("com.unity.xr.interaction.toolkit", "Hands Interaction Demo");
        return log.ToString();
    }
}
