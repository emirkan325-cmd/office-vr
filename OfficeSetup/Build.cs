public static class Build
{
    public static string Execute()
    {
        string r;
        try { r = OfficeBuild.Execute(); } catch (System.Exception e) { r = "EXCEPTION " + e; }
        System.IO.File.WriteAllText("OfficeSetup/build_log.txt", r);
        return r;
    }
}
