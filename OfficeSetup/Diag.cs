public class Diag
{
    public static string MaleChair() { return OfficeDiag.Run("S1", "m_sit_chair"); }
    public static string FemaleChair() { return OfficeDiag.Run("N1", "f_sit_chair"); }
    public static string MaleSeated() { return OfficeDiag.Run("S1", "Seated_"); }
    public static string FemaleSeated() { return OfficeDiag.Run("N1", "Seated_"); }
    public static string MaleWithFemaleClips() { return OfficeDiag.Run("S1", "f_sit_chair"); }
    public static string ClapNumbers() { return OfficeDiag.Measure("S1", "Seated_Clap"); }
    public static string Numbers() { return OfficeDiag.Measure("S1", "m_sit_chair") + OfficeDiag.Measure("N1", "f_sit_chair") + OfficeDiag.Measure("S1", "Seated_"); }
}
