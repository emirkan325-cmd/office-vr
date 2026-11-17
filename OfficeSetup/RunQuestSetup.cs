public static class RunQuestSetup
{
    public static string Execute()
    {
        ExposiaFirstRun.RunAll();
        return System.IO.File.ReadAllText("ProjectSettings/ExposiaKit.log");
    }
}
