using UnityEngine;
// play-mode helpers for testing the session flow from the editor
public class PlayTest
{
    static OfficeSession S() { return Object.FindAnyObjectByType<OfficeSession>(); }
    public static string L1() { S().StartLevel(1); return State(); }
    public static string L2() { S().StartLevel(2); return State(); }
    public static string L3() { S().StartLevel(3); return State(); }
    public static string Rate() { S().Rate(4); return State(); }
    public static string Next() { S().SetSlide(S().Slide + 1); return State(); }
    public static string Numbers() { S().Fire("q_numbers"); return State(); }
    public static string Phone() { S().Fire("phone"); return State(); }
    public static string Whisper() { S().Fire("whisper"); return State(); }
    public static string Fast() { Time.timeScale = 6f; return State(); }
    public static string Normal() { Time.timeScale = 1f; return State(); }
    public static string State()
    {
        var s = S(); int n = 0, look = 0;
        foreach (var l in s.listeners) if (l.gameObject.activeSelf) { n++; if (l.LooksAtPatient) look++; }
        return "phase " + s.Phase + " level " + s.level + " slide " + s.Slide + " listeners " + n + " looking " + look + " log: " + string.Join(" | ", s.log);
    }
}
