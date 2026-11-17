using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

// Is the patient speaking? Headset microphone level with an adaptive noise floor.
// Nothing is recorded or stored: only the current loudness is used (DSGVO-friendly).
public class MicInput : MonoBehaviour
{
    public float Level { get; private set; }          // 0..1 smoothed loudness for the meter
    public bool Speaking { get; private set; }
    public float SpeakingSeconds { get; private set; } // while counting
    public float SilentFor { get; private set; }
    public bool Available => clip != null;
    public float Rms { get; private set; }             // raw loudness (for "too quiet" checks)
    public float Floor => floor;
    public bool counting;                             // set by the session during the speech

    AudioClip clip; string device; float floor = 0.004f; float hold;
    readonly float[] buf = new float[1024];

    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone)) Permission.RequestUserPermission(Permission.Microphone);
#endif
        TryStart();
    }

    void TryStart()
    {
        if (Microphone.devices.Length == 0) return;
        device = Microphone.devices[0];
        clip = Microphone.Start(device, true, 1, 16000);
    }

    void Update()
    {
        if (clip == null) { if (Time.frameCount % 120 == 0) TryStart(); return; }
        int pos = Microphone.GetPosition(device) - buf.Length; if (pos < 0) pos += clip.samples;
        clip.GetData(buf, pos);
        float sum = 0f; foreach (var s in buf) sum += s * s;
        float rms = Mathf.Sqrt(sum / buf.Length);
        Rms = rms;
        // noise floor follows quiet passages quickly and loud ones very slowly
        floor = rms < floor ? Mathf.Lerp(floor, rms, 0.05f) : Mathf.Lerp(floor, rms, 0.0005f);
        floor = Mathf.Clamp(floor, 0.001f, 0.05f);
        bool loud = rms > floor * 3.2f + 0.006f;
        hold = loud ? 0.6f : hold - Time.deltaTime;     // bridge the gaps between words
        Speaking = hold > 0f;
        float lvl = Mathf.Clamp01(Mathf.Log10(1f + rms * 60f));
        Level = Mathf.Lerp(Level, lvl, 1f - Mathf.Exp(-Time.deltaTime * 12f));
        if (counting) { if (Speaking) SpeakingSeconds += Time.deltaTime; }
        SilentFor = Speaking ? 0f : SilentFor + Time.deltaTime;
    }

    public void ResetStats() { SpeakingSeconds = 0f; SilentFor = 0f; }

    void OnDestroy() { if (clip != null) Microphone.End(device); }
}
