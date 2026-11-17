using System.Linq;
public class PkgCheck
{
    public static string Execute()
    {
        var names = new[] { "Company.License.MqttCommandManager", "Company.License.LicenseGateBehaviour", "OfficeRemote" };
        var all = System.AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => { try { return a.GetTypes(); } catch { return new System.Type[0]; } }).Select(t => t.FullName).ToHashSet();
        return string.Join(", ", names.Select(n => n + "=" + all.Contains(n)));
    }
}
