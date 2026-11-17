using UnityEngine;

// Small synthesized sounds of the meeting room (no recordings needed): ventilation hum, knuckles knocking on the table,
// a phone vibrating on the table top, indistinct whispering.
public static class OfficeSounds
{
    const int SR = 22050;

    static AudioClip Make(string name, float[] d)
    {
        var c = AudioClip.Create(name, d.Length, 1, SR, false); c.SetData(d, 0); return c;
    }

    // soft air-conditioning noise, loops seamlessly (low-passed noise, cross-faded ends)
    public static AudioClip RoomTone()
    {
        var rng = new System.Random(11); int n = SR * 6; var d = new float[n];
        float lp = 0f, lp2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float w = (float)rng.NextDouble() * 2f - 1f;
            lp += (w - lp) * 0.02f; lp2 += (lp - lp2) * 0.05f;
            d[i] = lp2 * 2.2f + Mathf.Sin(i * 2f * Mathf.PI * 50f / SR) * 0.012f;
        }
        int f = SR / 2;
        for (int i = 0; i < f; i++) { float t = i / (float)f; d[i] = d[i] * t + d[n - f + i] * (1f - t); }
        var o = new float[n - f]; System.Array.Copy(d, o, n - f);
        return Make("RoomTone", o);
    }

    static void Thump(float[] d, int at, float amp, float pitch, System.Random rng)
    {
        int len = (int)(SR * 0.09f);
        for (int i = 0; i < len && at + i < d.Length; i++)
        {
            float t = i / (float)SR, env = Mathf.Exp(-t * 55f);
            float s = Mathf.Sin(2f * Mathf.PI * pitch * t * (1f - t * 2.5f)) * 0.8f + ((float)rng.NextDouble() * 2f - 1f) * 0.35f * Mathf.Exp(-t * 220f);
            d[at + i] += s * env * amp;
        }
    }

    // several people rapping their knuckles on a wooden table
    public static AudioClip Knocking(int people, float seconds, float perSecond)
    {
        var rng = new System.Random(5 + people); int n = (int)(SR * (seconds + 0.5f)); var d = new float[n];
        for (int p = 0; p < Mathf.Max(1, people); p++)
        {
            float rate = perSecond * (0.85f + (float)rng.NextDouble() * 0.3f), t = (float)rng.NextDouble() * 0.25f;
            float pitch = 150f + (float)rng.NextDouble() * 90f, amp = 0.35f + (float)rng.NextDouble() * 0.25f;
            float stop = seconds * (0.8f + (float)rng.NextDouble() * 0.2f);
            while (t < stop) { Thump(d, (int)(t * SR), amp * (t > stop - 0.6f ? 0.6f : 1f), pitch, rng); t += 1f / rate * (0.92f + (float)rng.NextDouble() * 0.16f); }
        }
        for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(d[i] * 0.7f, -1f, 1f);
        return Make("Knocking", d);
    }

    // a few people clapping (each clap = a short filtered noise burst)
    public static AudioClip Clapping(int people, float seconds)
    {
        var rng = new System.Random(17 + people); int n = (int)(SR * (seconds + 0.6f)); var d = new float[n];
        for (int p = 0; p < Mathf.Max(1, people); p++)
        {
            float rate = 3.2f + (float)rng.NextDouble() * 1.4f, t = (float)rng.NextDouble() * 0.35f;
            float tone = 0.25f + (float)rng.NextDouble() * 0.4f, amp = 0.28f + (float)rng.NextDouble() * 0.2f;
            float stop = seconds * (0.75f + (float)rng.NextDouble() * 0.25f);
            while (t < stop)
            {
                int at = (int)(t * SR), len = (int)(SR * 0.05f); float lp = 0f;
                for (int i = 0; i < len && at + i < n; i++)
                {
                    float w = (float)rng.NextDouble() * 2f - 1f; lp += (w - lp) * tone;
                    d[at + i] += (w - lp * 0.6f) * Mathf.Exp(-i / (float)SR * 70f) * amp * (t > stop - 0.7f ? 0.6f : 1f);
                }
                t += 1f / rate * (0.9f + (float)rng.NextDouble() * 0.2f);
            }
        }
        for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(d[i] * 0.8f, -1f, 1f);
        return Make("Clapping", d);
    }

    // phone on silent: three vibration bursts rattling on the table
    public static AudioClip PhoneBuzz()
    {
        int n = (int)(SR * 3.6f); var d = new float[n]; var rng = new System.Random(3);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR, ph = t % 1.2f;
            if (ph > 0.7f) continue;
            float env = Mathf.Clamp01(ph * 30f) * Mathf.Clamp01((0.7f - ph) * 30f);
            float buzz = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 165f * t)) * 0.18f + Mathf.Sin(2f * Mathf.PI * 330f * t) * 0.12f;
            float rattle = ((float)rng.NextDouble() * 2f - 1f) * 0.10f * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 165f * t));
            d[i] = (buzz + rattle) * env;
        }
        return Make("PhoneBuzz", d);
    }

    // unintelligible whispering: band-passed noise in syllable-sized puffs
    public static AudioClip Whisper(float seconds)
    {
        int n = (int)(SR * seconds); var d = new float[n]; var rng = new System.Random(9);
        float lp = 0f, hp = 0f, env = 0f, target = 0f; int next = 0;
        for (int i = 0; i < n; i++)
        {
            if (i >= next) { target = rng.NextDouble() < 0.75 ? 0.4f + (float)rng.NextDouble() * 0.6f : 0f; next = i + (int)(SR * (0.07f + (float)rng.NextDouble() * 0.16f)); }
            env += (target - env) * 0.004f;
            float w = (float)rng.NextDouble() * 2f - 1f;
            lp += (w - lp) * 0.45f; hp += (lp - hp) * 0.08f;
            float fade = Mathf.Clamp01(i / (SR * 0.3f)) * Mathf.Clamp01((n - i) / (SR * 0.4f));
            d[i] = (lp - hp) * env * fade * 0.5f;
        }
        return Make("Whisper", d);
    }
}
