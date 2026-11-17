using UnityEngine;
// play mode: everybody loops one state (then Hands.Stats after a while); Off = normal behaviour again
public class PoseTry
{
    static string Set(string state)
    {
        OfficeListener.TestState = state;
        foreach (var l in Object.FindObjectsByType<OfficeListener>(FindObjectsSortMode.None))
        {
            var an = l.GetComponent<Animator>();
            if (state != null && !an.HasState(0, Animator.StringToHash(state))) return "no state " + state;
            if (state != null) an.Play(state, 0, Random.value);
            l.ResetHandStats();
        }
        Time.timeScale = 6f;
        return "looping " + state;
    }
    public static string Sit() { return Set("Sit"); }
    public static string Sit2() { return Set("Sit2"); }
    public static string Relaxed() { return Set("Relaxed"); }
    public static string LookAround() { return Set("LookAround"); }
    public static string Waiting() { return Set("Waiting"); }
    public static string Off() { OfficeListener.TestState = null; Time.timeScale = 1f; return "off"; }
    public static string Lengths()
    {
        var l = Object.FindAnyObjectByType<OfficeListener>(); string r = "";
        foreach (var c in l.GetComponent<Animator>().runtimeAnimatorController.animationClips) r += c.name + " " + c.length.ToString("0.0") + "s; ";
        return r;
    }
}
