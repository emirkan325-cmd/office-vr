using System;
using System.Collections;
using System.Globalization;
using System.Text;
using Company.License;
using UnityEngine;
using UnityEngine.Networking;

// ExposiaVR license + web panel (MQTT) for the presentation app (product OFFICE_PRESENTATION).
//
// Not part of the scene on purpose: OfficeBuild.Execute() regenerates Besprechung.unity, so this boots itself
// after the scene has loaded (license gate + lock screen + panel commands). Config: Resources/ExposiaLicenseConfig.
// In the Editor the license gate is skipped (no activation, no lock screen) unless EditorLicense is set to true.
//
// Panel commands (backend: license-system backend/src/mqtt/commands.ts, OFFICE_PRESENTATION):
//   office.level {level 1-3} / office.duration {minutes 3|5|10}   -> only in the menu
//   office.start {} (menu) / office.end {} (presentation)
//   office.slide {dir next|prev} / office.event {kind phone|whisper|stare|auto_on|auto_off}
//   office.line {index 1-7} / office.subtitles {on} / office.ask {text, voice, audioUrl}
// Telemetry section "office": phase, level, minutes, slide, slideCount, autoEvents, subtitles.
public class OfficeRemote : MonoBehaviour
{
    public static bool EditorLicense = false;
    const string ConfigResource = "ExposiaLicenseConfig";
    const string Tag = "[OfficeRemote]";

    OfficeSession session;
    string lastState;

    [Serializable] class LevelPayload { public int level; }
    [Serializable] class DurationPayload { public int minutes; }
    [Serializable] class SlidePayload { public string dir; }
    [Serializable] class EventPayload { public string kind; }
    [Serializable] class LinePayload { public int index; }
    [Serializable] class SubtitlesPayload { public bool on; }
    [Serializable] class AskPayload { public string text; public string voice; public string audioUrl; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<OfficeRemote>() != null) return;
        var go = new GameObject("ExposiaLicense");
        go.SetActive(false);   // the gate reads its config in Awake
        DontDestroyOnLoad(go);
        if (!Application.isEditor || EditorLicense)
        {
            var config = Resources.Load<LicenseConfig>(ConfigResource);
            if (config == null) Debug.LogError($"{Tag} Resources/{ConfigResource} missing - license gate NOT started.");
            else go.AddComponent<LicenseGateBehaviour>().config = config;
        }
        go.AddComponent<OfficeRemote>();
        go.SetActive(true);
    }

    void OnEnable()
    {
        Register("office.level", cmd => Session.RemoteSetLevel(cmd.PayloadAs<LevelPayload>().level) ? Ok : InMenuOnly);
        Register("office.duration", cmd => Session.RemoteSetDuration(cmd.PayloadAs<DurationPayload>().minutes) ? Ok : InMenuOnly);
        Register("office.start", cmd => Session.RemoteStart() ? Ok : InMenuOnly);
        Register("office.end", cmd => Session.RemoteEnd() ? Ok : PresentingOnly);
        Register("office.slide", cmd => Session.RemoteSlide(cmd.PayloadAs<SlidePayload>().dir == "prev" ? -1 : 1) ? Ok : PresentingOnly);
        Register("office.event", OnEvent);
        Register("office.line", cmd => Session.RemoteLine(cmd.PayloadAs<LinePayload>().index) ? Ok : PresentingOnly);
        Register("office.subtitles", cmd => { Session.subtitles = cmd.PayloadAs<SubtitlesPayload>().on; return Ok; });
        Register("office.ask", OnAsk);
        MqttCommandManager.SetTelemetrySection("office", TelemetryJson);
    }

    void OnDisable()
    {
        foreach (var t in new[] { "office.level", "office.duration", "office.start", "office.end", "office.slide", "office.event", "office.line", "office.subtitles", "office.ask" })
            MqttCommandManager.UnregisterHandler(t);
        MqttCommandManager.RemoveTelemetrySection("office");
    }

    static CommandResult Ok => CommandResult.Success;
    static CommandResult InMenuOnly => CommandResult.Fail("not_in_menu");
    static CommandResult PresentingOnly => CommandResult.Fail("not_presenting");

    OfficeSession Session => session ? session : (session = FindAnyObjectByType<OfficeSession>());

    void Register(string type, Func<MqttCommand, CommandResult> run)
    {
        MqttCommandManager.RegisterHandler(type, cmd =>
        {
            if (Session == null) return CommandResult.Fail("session_not_found");
            var r = run(cmd);
            MqttCommandManager.RequestTelemetry();
            return r;
        });
    }

    CommandResult OnEvent(MqttCommand cmd)
    {
        string kind = cmd.PayloadAs<EventPayload>().kind;
        if (kind == "auto_on" || kind == "auto_off") { Session.autoEvents = kind == "auto_on"; return Ok; }
        return Session.RemoteEvent(kind) ? Ok : PresentingOnly;
    }

    // ack right away, then download the mp3 (it lives 1 h on the server) and let a listener ask it
    CommandResult OnAsk(MqttCommand cmd)
    {
        if (Session.Phase != "presentation") return PresentingOnly;
        var p = cmd.PayloadAs<AskPayload>();
        if (p == null || string.IsNullOrEmpty(p.audioUrl) || !p.audioUrl.StartsWith("https://")) return CommandResult.Fail("no_audio");
        StartCoroutine(DownloadAndSay(p));
        return Ok;
    }

    IEnumerator DownloadAndSay(AskPayload p)
    {
        using (var req = UnityWebRequestMultimedia.GetAudioClip(p.audioUrl, AudioType.MPEG))
        {
            req.timeout = 15;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"{Tag} question audio download failed: {req.error}");
                MqttCommandManager.PublishEvent("office.ask_failed", "{\"reason\":\"download\"}");
                yield break;
            }
            var clip = DownloadHandlerAudioClip.GetContent(req);
            bool female = p.voice == "katja";
            int index = p.voice == "killian" ? 1 : 0;
            if (!Session.RemoteSay(clip, female, index, p.text ?? ""))
                MqttCommandManager.PublishEvent("office.ask_failed", "{\"reason\":\"not_presenting\"}");
            else
                Debug.Log($"{Tag} question asked ({p.voice}, {clip.length:0.0} s, {(p.text ?? "").Length} chars)");
        }
    }

    void Update()
    {
        if (Session == null) return;
        string state = Session.Phase + "|" + Session.level + "|" + Session.duration + "|" + Session.Slide + "|" + Session.autoEvents + "|" + Session.subtitles;
        if (state == lastState) return;
        lastState = state;
        MqttCommandManager.RequestTelemetry();   // update the panel without waiting for the 10 s interval
    }

    // OfficeSession phases -> panel phases (idle / ready / running / ended)
    static string PanelPhase(string phase)
    {
        switch (phase)
        {
            case "menu": return "idle";
            case "presentation": return "running";
            case "closing": return "ended";
            default: return phase;
        }
    }

    string TelemetryJson()
    {
        var s = Session;
        if (s == null) return null;
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(200);
        sb.Append("{\"phase\":\"").Append(PanelPhase(s.Phase)).Append('"');
        sb.Append(",\"level\":").Append(s.level.ToString(inv));
        sb.Append(",\"minutes\":").Append(s.duration.ToString(inv));
        sb.Append(",\"slide\":").Append((s.Slide + 1).ToString(inv));
        sb.Append(",\"slideCount\":").Append(s.SlideCount.ToString(inv));
        sb.Append(",\"autoEvents\":").Append(s.autoEvents ? "true" : "false");
        sb.Append(",\"subtitles\":").Append(s.subtitles ? "true" : "false");
        sb.Append('}');
        return sb.ToString();
    }
}
