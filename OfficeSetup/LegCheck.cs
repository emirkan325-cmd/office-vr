using UnityEngine;
// play mode: sanity numbers of the hand-over-thigh lift (OfficeListener.ApplyHandLift): measured thigh radii and how much each
// hand gets lifted over one full idle loop (48 samples, the pose exactly as in the game)
public class LegCheck
{
    const int N = 48;
    static readonly System.Reflection.MethodInfo Late = typeof(OfficeListener).GetMethod("LateUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    public static string Execute()
    {
        string r = ""; OfficeListener.InstantLift = true;
        foreach (var l in Object.FindObjectsByType<OfficeListener>(FindObjectsSortMode.None))
        {
            if (!l.gameObject.activeInHierarchy) continue;
            var an = l.GetComponent<Animator>();
            float keepT = an.GetCurrentAnimatorStateInfo(0).normalizedTime, keepSpeed = an.speed;
            float maxL = 0f, maxR = 0f, sumL = 0f, sumR = 0f; int onL = 0, onR = 0;
            for (int k = 0; k < N; k++)
            {
                l.liftL = l.liftR = 0f;   // no history: the lift each sample needs on its own
                an.speed = 1f; an.Play(OfficeListener.StillState, 0, (k + 0.5f) / N); an.Update(0f); Late.Invoke(l, null);
                maxL = Mathf.Max(maxL, l.liftL); maxR = Mathf.Max(maxR, l.liftR); sumL += l.liftL; sumR += l.liftR;
                if (l.liftL > 0.001f) onL++; if (l.liftR > 0.001f) onR++;
            }
            an.Play(OfficeListener.StillState, 0, keepT); an.speed = keepSpeed; an.Update(0f);
            string legs = "";
            for (int i = 0; i < l.legTop.Length; i++) legs += (l.legTop[i] * 100f).ToString("0") + "/" + (l.legSide[i] * 100f).ToString("0") + " ";
            r += l.seat + ": thighs top/side cm " + legs + "| lift L max " + (maxL * 100f).ToString("0.0") + " avg " + (sumL / N * 100f).ToString("0.0") + " (" + onL + "/" + N + ")"
               + ", R max " + (maxR * 100f).ToString("0.0") + " avg " + (sumR / N * 100f).ToString("0.0") + " (" + onR + "/" + N + ")\n";
        }
        OfficeListener.InstantLift = false;
        return r;
    }
}
