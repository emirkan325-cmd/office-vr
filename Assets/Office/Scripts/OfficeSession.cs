using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Presentation in front of the team (all patient-facing text in German).
//   menu (Stufe 1-3, duration) -> anxiety 0-10 -> the presentation (slides on the wall screen, timer panel on the table,
//   participants react according to the level, questions / disturbances at fixed moments) -> closing words + knocking
//   -> anxiety 0-10 -> office_sessions.csv (numbers only, nothing is recorded) -> menu.
// Stufe 1 Freundlich: 4 attentive, nodding colleagues, one kind remark.
// Stufe 2 Neutral:    7 people, some look at their laptops, two factual questions.
// Stufe 3 Kritisch:   7 people, distracted / sceptical, phone buzzes, critical questions, time pressure.
// Doctor (PC window): F1-F3 start a level, F12 end, arrows = slides, Q/W/R... see the panel, Ctrl+0-9 anxiety, T subtitles, E English.
// Patient: right trigger / A = next slide, left trigger / X = previous slide, or the buttons on the table panel.
public class OfficeSession : MonoBehaviour
{
    public OfficeListener[] listeners;
    public MicInput mic;
    public Transform patientHead, phone;
    public MeshRenderer tv, laptop;
    public Texture2D[] slides;
    public bool subtitles = true, autoEvents = true;
    public static bool English;

    class LevelDef
    {
        public string name, en, hint, endLine;
        public float attention, nods, restless, tone;
        public bool whisper;
        public (float at, string id)[] events;
    }

    static readonly LevelDef[] Levels =
    {
        new LevelDef { name = "Freundlich", en = "Friendly", hint = "Kleine Runde, aufmerksame Kollegen", endLine = "end_good", attention = 0.92f, nods = 5f, restless = 0.04f, tone = 0.5f,
                       events = new[] { (0.5f, "praise") } },
        new LevelDef { name = "Neutral", en = "Neutral", hint = "Ganzes Team, sachliche Fragen", endLine = "end_neutral", attention = 0.62f, nods = 1.2f, restless = 0.25f, tone = 0.7f,
                       events = new[] { (0.35f, "q_repeat"), (0.7f, "q_team") } },
        new LevelDef { name = "Kritisch", en = "Critical", hint = "Ganzes Team, abgelenkt und kritisch", endLine = "end_crit", attention = 0.36f, nods = 0f, restless = 0.6f, tone = 1f, whisper = true,
                       events = new[] { (0.12f, "phone"), (0.28f, "q_numbers"), (0.45f, "q_louder"), (0.62f, "q_doubt"), (0.80f, "q_time") } },
    };

    // line -> who speaks, German, English (for the doctor / subtitles)
    static readonly Dictionary<string, (string role, string de, string en)> Lines = new Dictionary<string, (string, string, string)>
    {
        { "start", ("boss", "So, wir sind vollzählig. Bitte, fangen Sie an.", "Right, everyone is here. Please begin.") },
        { "praise", ("woman", "Das ist ein guter Punkt.", "That is a good point.") },
        { "q_repeat", ("woman", "Entschuldigung, können Sie den letzten Punkt bitte noch einmal erklären?", "Sorry, could you explain the last point again?") },
        { "q_team", ("woman", "Und was bedeutet das konkret für unser Team?", "And what does that mean for our team?") },
        { "q_numbers", ("man", "Woher stammen diese Zahlen eigentlich?", "Where do these numbers come from?") },
        { "q_louder", ("man", "Könnten Sie bitte etwas lauter sprechen? Man versteht Sie hier schlecht.", "Could you speak up, please? It is hard to hear you.") },
        { "q_doubt", ("boss", "Davon bin ich, ehrlich gesagt, noch nicht überzeugt.", "Honestly, I am not convinced of that yet.") },
        { "q_time", ("boss", "Wir haben nicht mehr viel Zeit. Kommen Sie bitte zum Punkt.", "We are running out of time. Please get to the point.") },
        { "end_good", ("boss", "Vielen Dank, das war eine sehr gute Präsentation.", "Thank you, that was a very good presentation.") },
        { "end_neutral", ("boss", "Danke für die Präsentation.", "Thank you for the presentation.") },
        { "end_crit", ("boss", "Gut, danke. Darüber müssen wir noch einmal sprechen.", "Fine, thanks. We will have to talk about this again.") },
    };
    static readonly string[] DoctorEvents = { "praise", "q_repeat", "q_team", "q_numbers", "q_louder", "q_doubt", "q_time" };
    static readonly int[] Durations = { 3, 5, 10 };

    public int level = 1, duration = 5;
    public string Phase { get; private set; } = "menu";
    public int Slide { get; private set; }
    public readonly List<string> log = new List<string>();

    int pre = -1, post = -1, picked = -1, eventCount, slideMax;
    bool start, endTalk, asking, silenceStare;
    float talkStart; DateTime started; string midRatings = "";
    readonly HashSet<string> fired = new HashSet<string>();

    Canvas menu, rating, tablePanel;
    Text question, hint, tTime, tSlide, tState; Image speakDot;
    GameObject buttonsRow;
    readonly List<(Image img, Func<bool> on)> toggles = new List<(Image, Func<bool>)>();
    static readonly Color Sel = new Color(0.30f, 0.52f, 0.62f), Unsel = new Color(0.3f, 0.29f, 0.28f);
    TextMesh sub; float subUntil;
    AudioSource tone, fx;
    InputAction next, prev;
    GUIStyle big, small, btn;
    Vector2 scroll;

    LevelDef L => Levels[level - 1];
    OfficeListener Who(string role) => listeners.FirstOrDefault(l => l.role == role && l.gameObject.activeSelf) ?? listeners.First(l => l.gameObject.activeSelf);
    IEnumerable<OfficeListener> Active => listeners.Where(l => l.gameObject.activeSelf);

    IEnumerator Start()
    {
        if (Application.isEditor) Application.runInBackground = true;
        next = new InputAction("next"); next.AddBinding("<XRController>{RightHand}/triggerPressed"); next.AddBinding("<XRController>{RightHand}/primaryButton"); next.Enable();
        prev = new InputAction("prev"); prev.AddBinding("<XRController>{LeftHand}/triggerPressed"); prev.AddBinding("<XRController>{LeftHand}/primaryButton"); prev.Enable();
        tone = gameObject.AddComponent<AudioSource>(); tone.clip = OfficeSounds.RoomTone(); tone.loop = true; tone.spatialBlend = 0f; tone.volume = 0.12f; tone.Play();
        var fxGo = new GameObject("TableFx"); fxGo.transform.position = phone ? phone.position : transform.position;
        fx = fxGo.AddComponent<AudioSource>(); fx.spatialBlend = 1f; fx.minDistance = 1.5f; fx.maxDistance = 12f; fx.rolloffMode = AudioRolloffMode.Linear;
        ApplyLevel();
        SetSlide(0);
        yield return null; yield return new WaitForSeconds(0.6f);   // let the rig calibrate first
        MakeSubtitle(); BuildMenu(); BuildRating(); BuildTablePanel();
        rating.gameObject.SetActive(false);
        while (true)
        {
            yield return MenuPhase();
            yield return Talk();
        }
    }

    void OnDestroy() { next?.Dispose(); prev?.Dispose(); }

    // ================================================================= level
    public void ApplyLevel()
    {
        foreach (var l in listeners)
        {
            l.gameObject.SetActive(l.minLevel <= level);
            l.attention = L.attention; l.nodsPerMinute = L.nods; l.restless = L.restless; l.whisper = L.whisper;
            // the boss keeps an eye on the presenter in every level
            if (l.role == "boss") l.attention = Mathf.Max(L.attention, 0.7f);
            if (l.gameObject.activeSelf) l.Reseed(level);
        }
        if (tone) tone.volume = 0.12f;
        RefreshToggles();
    }

    // ================================================================= phases
    IEnumerator MenuPhase()
    {
        Phase = "menu"; SetSlide(0);
        SetPanel("", "", "Bitte wählen Sie die Stufe.");
        menu.gameObject.SetActive(true); RefreshToggles();
        start = false;
        while (!start) yield return null;
        menu.gameObject.SetActive(false);
    }

    IEnumerator Talk()
    {
        ApplyLevel();
        Phase = "rating before";
        pre = -1; yield return Ask("Wie aufgeregt sind Sie gerade?", "0 = völlig ruhig   ·   10 = extrem aufgeregt", v => pre = v);

        Phase = "presentation"; started = DateTime.Now; endTalk = false; eventCount = 0; slideMax = 0; midRatings = ""; fired.Clear(); log.Clear();
        SetSlide(0);
        foreach (var l in Active) l.LookAtPatient(4f);
        yield return new WaitForSeconds(1.2f);
        talkStart = Time.time; yield return Speak("start");
        talkStart = Time.time;
        if (mic) { mic.ResetStats(); mic.counting = true; }
        float planned = duration * 60f;
        while (!endTalk)
        {
            float el = Time.time - talkStart, left = planned - el;
            SetPanel(Fmt(el) + " / " + duration + ":00", "Folie " + (Slide + 1) + " / " + slides.Length, left > 0 ? "" : "Zeit ist um – bitte zum Schluss kommen.");
            if (speakDot) speakDot.color = mic && mic.Speaking ? new Color(0.45f, 0.8f, 0.5f) : new Color(0.35f, 0.35f, 0.35f);
            if (autoEvents && !asking)
                foreach (var (at, id) in L.events)
                    if (el > at * planned && fired.Add(id)) { Fire(id); break; }
            // a long silence: from Stufe 2 on everybody looks up at the presenter
            if (level >= 2 && mic && mic.Available)
            {
                if (mic.SilentFor > 7f && !silenceStare) { silenceStare = true; foreach (var l in Active) l.LookAtPatient(4f); Log("silence > 7 s: everybody looks up"); }
                if (mic.Speaking) silenceStare = false;
            }
            if (left < -90f) endTalk = true;
            yield return null;
        }
        if (mic) mic.counting = false;
        float talked = Time.time - talkStart;
        while (asking) yield return null;

        // closing words of the boss, then applause
        Phase = "closing";
        SetPanel(Fmt(talked), "", "Vielen Dank!");
        yield return Speak(L.endLine);
        // applause (only with a real clap clip; otherwise the colleagues just nod)
        var clappers = level == 3 ? Active.Where(l => l.role == "boss" || l.seat == "N1").ToList() : Active.ToList();
        float secs = level == 1 ? 6.4f : 3.2f;      // the clap clip is 3.3 s long: two rounds for the friendly team, one otherwise
        bool canClap = !OfficeListener.Still && clappers.Count > 0 && clappers[0].CanClap;   // still mode: no applause at all, the boss' thanks closes it
        foreach (var k in clappers) { k.Clap(secs, UnityEngine.Random.Range(0.92f, 1.08f)); k.LookAtPatient(secs + 2f); }
        if (canClap)
        {
            fx.transform.position = phone ? phone.position : fx.transform.position;
            fx.PlayOneShot(OfficeSounds.Clapping(clappers.Count, secs), level == 3 ? 0.45f : 0.8f);
        }
        yield return new WaitForSeconds(canClap ? secs + 1.5f : 1.5f);

        Phase = "rating after";
        post = -1; yield return Ask("Und wie aufgeregt sind Sie jetzt?", "0 = völlig ruhig   ·   10 = extrem aufgeregt", v => post = v);
        Save(talked);
        ShowMessage("Gut gemacht!", "Die Sitzung wurde gespeichert.");
        yield return new WaitForSeconds(3f);
        rating.gameObject.SetActive(false);
    }

    IEnumerator Ask(string q, string h, Action<int> set)
    {
        question.text = q; hint.text = h; buttonsRow.SetActive(true);
        rating.gameObject.SetActive(true);
        picked = -1;
        while (picked < 0) yield return null;
        set(picked);
        yield return new WaitForSeconds(0.4f);
        rating.gameObject.SetActive(false);
    }

    void ShowMessage(string q, string h) { question.text = q; hint.text = h; buttonsRow.SetActive(false); rating.gameObject.SetActive(true); }
    static string Fmt(float s) { int m = (int)(s / 60f); return m + ":" + ((int)(s - m * 60)).ToString("00"); }

    // ================================================================= events
    public void Fire(string id)
    {
        if (Phase != "presentation") return;
        eventCount++; fired.Add(id);
        if (id == "phone") StartCoroutine(PhoneBuzz());
        else if (id == "stare") { foreach (var l in Active) l.LookAtPatient(5f); Log("everybody looks at the presenter"); }
        else if (id == "whisper") StartCoroutine(Whisper());
        else if (Lines.ContainsKey(id)) StartCoroutine(Speak(id));
    }

    IEnumerator Speak(string id)
    {
        while (asking) yield return null;
        asking = true;
        var (role, de, en) = Lines[id];
        var who = Who(role);
        var clip = Resources.Load<AudioClip>("Voice/" + id);
        float len = who.Say(clip); if (len <= 0f) len = 2.5f;
        ShowSub(English ? en : de, len);
        Log((role == "boss" ? "Boss" : role == "woman" ? "Colleague (f)" : "Colleague (m)") + ": " + en);
        // the others turn to the speaker, then to the presenter: what will the answer be?
        foreach (var l in Active) if (l != who) l.LookAt(who.HeadPos, len * 0.8f);
        yield return new WaitForSeconds(len * 0.8f);
        if (id.StartsWith("q_")) foreach (var l in Active) l.LookAtPatient(level == 1 ? 3f : 5f);
        if (id == "praise") foreach (var l in Active) if (l != who) l.Nod();
        yield return new WaitForSeconds(len * 0.2f + 0.3f);
        asking = false;
    }

    IEnumerator PhoneBuzz()
    {
        Log("a phone buzzes on the table");
        var owner = Active.FirstOrDefault(l => l.seat == "S1") ?? Active.First();
        fx.transform.position = owner.deskPoint;
        fx.PlayOneShot(OfficeSounds.PhoneBuzz(), 0.8f);
        yield return new WaitForSeconds(0.5f);
        foreach (var l in Active) if (l != owner) l.LookAt(owner.deskPoint, 2.2f);
        owner.LookAt(owner.deskPoint + Vector3.down * 0.05f, 6f);
    }

    IEnumerator Whisper()
    {
        var a = Active.FirstOrDefault(l => l.seat == "N1"); var b = Active.FirstOrDefault(l => l.seat == "N2");
        if (a == null || b == null) yield break;
        Log("two colleagues whisper");
        a.LookAt(b.HeadPos, 5f); b.LookAt(a.HeadPos, 5f);
        fx.transform.position = a.HeadPos; fx.PlayOneShot(OfficeSounds.Whisper(4f), 0.5f);
        yield return new WaitForSeconds(4.2f);
        a.LookAtPatient(2.5f); b.LookAtPatient(2.5f);
    }

    void Log(string s)
    {
        float t = Phase == "presentation" || Phase == "closing" ? Time.time - talkStart : 0f;
        log.Add(Fmt(Mathf.Max(0f, t)) + "  " + s);
    }

    // ================================================================= slides
    public void SetSlide(int i)
    {
        if (slides == null || slides.Length == 0) return;
        Slide = Mathf.Clamp(i, 0, slides.Length - 1); slideMax = Mathf.Max(slideMax, Slide);
        if (tv) tv.material.SetTexture("_BaseMap", slides[Slide]);
        if (laptop) laptop.material.SetTexture("_BaseMap", slides[Slide]);
    }

    // ================================================================= input
    static bool Held(Key k) => Keyboard.current != null && Keyboard.current[k].isPressed;
    static bool Down(Key k) => Keyboard.current != null && Keyboard.current[k].wasPressedThisFrame;

    void Update()
    {
        if (sub && Time.time > subUntil) sub.text = "";
        if (next != null && next.WasPressedThisFrame() && Phase == "presentation") SetSlide(Slide + 1);
        if (prev != null && prev.WasPressedThisFrame() && Phase == "presentation") SetSlide(Slide - 1);
        if (Keyboard.current == null) return;
        bool ctrl = Held(Key.LeftCtrl) || Held(Key.RightCtrl);
        if (ctrl)
        {
            for (int d = 0; d <= 9; d++) if (Down(Key.Digit1 + (d + 9) % 10)) Rate(d == 0 ? 10 : d);
            if (Down(Key.Enter) && Phase == "menu") start = true;
            return;
        }
        for (int i = 0; i < 3; i++) if (Down(Key.F1 + i)) StartLevel(i + 1);
        if (Down(Key.F12)) endTalk = true;
        if (Down(Key.RightArrow) || Down(Key.PageDown)) SetSlide(Slide + 1);
        if (Down(Key.LeftArrow) || Down(Key.PageUp)) SetSlide(Slide - 1);
        if (Down(Key.T)) subtitles = !subtitles;
        if (Down(Key.E)) English = !English;
        if (Down(Key.A)) autoEvents = !autoEvents;
        for (int i = 0; i < DoctorEvents.Length; i++) if (Down(Key.Digit1 + i)) Fire(DoctorEvents[i]);
        if (Down(Key.P)) Fire("phone");
        if (Down(Key.S)) Fire("stare");
        if (Down(Key.W)) Fire("whisper");
    }

    public void StartLevel(int lv)
    {
        if (Phase != "menu") return;
        level = Mathf.Clamp(lv, 1, 3); ApplyLevel(); start = true;
    }

    // the doctor's rating: answers an open question, otherwise it is noted with the time
    public void Rate(int v)
    {
        if (rating && rating.gameObject.activeSelf && buttonsRow.activeSelf) picked = v;
        else if (Phase == "presentation") { midRatings += Fmt(Time.time - talkStart) + "=" + v + " "; Log("anxiety " + v); }
    }

    // ================================================================= save
    void Save(float talked)
    {
        try
        {
            var path = Path.Combine(Application.persistentDataPath, "office_sessions.csv");
            bool header = !File.Exists(path);
            var inv = CultureInfo.InvariantCulture;
            float speak = mic ? mic.SpeakingSeconds : 0f;
            using (var w = new StreamWriter(path, true))
            {
                if (header) w.WriteLine("date;start;level;minutes_planned;minutes_talked;speaking_seconds;speaking_share;slides_shown;anxiety_before;anxiety_after;anxiety_during;events");
                w.WriteLine(string.Join(";", started.ToString("yyyy-MM-dd", inv), started.ToString("HH:mm", inv), level + " " + L.name, duration,
                    (talked / 60f).ToString("0.0", inv), speak.ToString("0", inv), (talked > 0 ? speak / talked : 0f).ToString("0.00", inv),
                    (slideMax + 1) + "/" + slides.Length, pre, post, midRatings.Trim(), eventCount));
            }
            Debug.Log("[Office] session saved to " + path);
        }
        catch (Exception e) { Debug.LogWarning("[Office] could not save session: " + e.Message); }
    }

    // ================================================================= patient UI
    Transform EyeAnchor()
    {
        var a = new GameObject("UIAnchor").transform; var cam = Camera.main.transform;
        a.position = cam.position; a.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(cam.forward, Vector3.up), Vector3.up);
        return a;
    }

    void Toggle(Button b, Func<bool> on) { toggles.Add((b.GetComponent<Image>(), on)); }
    void RefreshToggles() { foreach (var (img, on) in toggles) if (img) img.color = on() ? Sel : Unsel; }

    void BuildMenu()
    {
        menu = RelaxUI.Panel("MenuPanel", EyeAnchor(), 1.05f, 0.05f, new Vector2(1300, 820));
        var r = menu.transform;
        RelaxUI.Label(r, "Title", "Präsentation im Team", new Vector2(0, 330), new Vector2(1200, 90), 66, RelaxUI.TextColor);
        RelaxUI.Label(r, "Sub", "Sie stellen den Quartalsbericht vor. Wählen Sie die Stufe.", new Vector2(0, 262), new Vector2(1200, 50), 32, RelaxUI.HintColor);
        for (int i = 0; i < 3; i++)
        {
            int v = i + 1; float x = -400 + i * 400;
            var b = RelaxUI.Button(r, "Level" + v, "Stufe " + v + "\n" + Levels[i].name, new Vector2(x, 120), new Vector2(370, 150), Unsel, () => { level = v; ApplyLevel(); }, 40);
            Toggle(b, () => level == v);
            RelaxUI.Label(r, "Hint" + v, Levels[i].hint, new Vector2(x, 10), new Vector2(370, 70), 24, RelaxUI.HintColor);
        }
        RelaxUI.Label(r, "DurL", "Dauer", new Vector2(-500, -110), new Vector2(200, 60), 34, RelaxUI.HintColor, TextAnchor.MiddleLeft);
        for (int i = 0; i < Durations.Length; i++)
        {
            int d = Durations[i];
            var b = RelaxUI.Button(r, "Dur" + d, d + " Minuten", new Vector2(-220 + i * 290, -110), new Vector2(270, 80), Unsel, () => { duration = d; RefreshToggles(); }, 30);
            Toggle(b, () => duration == d);
        }
        RelaxUI.Button(r, "Start", "Präsentation beginnen", new Vector2(0, -290), new Vector2(620, 110), RelaxUI.Calm, () => start = true, 44);
    }

    void BuildRating()
    {
        rating = RelaxUI.Panel("RatingPanel", EyeAnchor(), 1.0f, 0.0f, new Vector2(1100, 520));
        var root = rating.transform;
        question = RelaxUI.Label(root, "Question", "", new Vector2(0, 150), new Vector2(1040, 120), 54, RelaxUI.TextColor);
        hint = RelaxUI.Label(root, "Hint", "", new Vector2(0, 60), new Vector2(1040, 60), 32, RelaxUI.HintColor);
        buttonsRow = new GameObject("Buttons", typeof(RectTransform)); buttonsRow.transform.SetParent(root, false);
        RelaxUI.Place((RectTransform)buttonsRow.transform, new Vector2(0, -110), new Vector2(1040, 120));
        for (int i = 0; i <= 10; i++)
        {
            int v = i;
            var c = Color.Lerp(new Color(0.45f, 0.62f, 0.5f), new Color(0.72f, 0.42f, 0.35f), i / 10f);
            RelaxUI.Button(buttonsRow.transform, "B" + i, i.ToString(), new Vector2(-460 + i * 92, 0), new Vector2(82, 100), c, () => picked = v);
        }
    }

    // small presenter panel lying on the table to the right of the laptop: time, slide number, slide buttons, end
    void BuildTablePanel()
    {
        RelaxUI.EnsureEventSystem();
        var go = new GameObject("TablePanel", typeof(Canvas), typeof(CanvasScaler), typeof(UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster));
        tablePanel = go.GetComponent<Canvas>(); tablePanel.renderMode = RenderMode.WorldSpace; tablePanel.worldCamera = Camera.main;
        ((RectTransform)go.transform).sizeDelta = new Vector2(560, 360);
        go.transform.position = new Vector3(-2.29f, 0.7445f, -1.96f);
        go.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.left);     // faces up, text reads from the presenter's side
        go.transform.localScale = Vector3.one * 0.0005f;
        var bg = RelaxUI.New("Bg", go.transform, typeof(Image)); RelaxUI.Stretch(bg); bg.GetComponent<Image>().color = new Color(0.06f, 0.07f, 0.08f, 1f);
        tTime = RelaxUI.Label(go.transform, "Time", "", new Vector2(0, 110), new Vector2(540, 100), 78, Color.white);
        tSlide = RelaxUI.Label(go.transform, "Slide", "", new Vector2(20, 30), new Vector2(480, 50), 34, RelaxUI.TextColor);
        tState = RelaxUI.Label(go.transform, "State", "", new Vector2(0, -25), new Vector2(540, 40), 22, RelaxUI.HintColor);
        var dot = RelaxUI.New("SpeakDot", go.transform, typeof(Image)); RelaxUI.Place((RectTransform)dot.transform, new Vector2(-235, 32), new Vector2(26, 26));
        speakDot = dot.GetComponent<Image>(); speakDot.color = new Color(0.35f, 0.35f, 0.35f);
        RelaxUI.Button(go.transform, "Prev", "◀", new Vector2(-200, -110), new Vector2(130, 110), Unsel, () => { if (Phase == "presentation") SetSlide(Slide - 1); }, 54);
        RelaxUI.Button(go.transform, "Next", "▶", new Vector2(-50, -110), new Vector2(130, 110), Sel, () => { if (Phase == "presentation") SetSlide(Slide + 1); }, 54);
        RelaxUI.Button(go.transform, "End", "Beenden", new Vector2(160, -110), new Vector2(210, 110), new Color(0.55f, 0.35f, 0.3f), () => { if (Phase == "presentation") endTalk = true; }, 34);
    }

    void SetPanel(string time, string slide, string state)
    {
        if (tTime) tTime.text = time; if (tSlide) tSlide.text = slide; if (tState) tState.text = state;
    }

    void MakeSubtitle()
    {
        var go = new GameObject("Subtitle"); go.transform.SetParent(patientHead, false);
        go.transform.localPosition = new Vector3(0f, -0.30f, 1.4f);
        sub = go.AddComponent<TextMesh>();
        sub.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        sub.GetComponent<MeshRenderer>().sharedMaterial = sub.font.material;
        sub.characterSize = 0.012f; sub.fontSize = 48; sub.anchor = TextAnchor.MiddleCenter; sub.alignment = TextAlignment.Center;
        sub.color = new Color(1f, 1f, 0.92f); sub.text = "";
    }

    void ShowSub(string text, float seconds)
    {
        if (!subtitles || !sub) return;
        var sb = new System.Text.StringBuilder(); int col = 0;
        foreach (var w in text.Split(' ')) { if (col + w.Length > 42 && col > 0) { sb.Append('\n'); col = 0; } sb.Append(w).Append(' '); col += w.Length + 1; }
        sub.text = sb.ToString().TrimEnd(); subUntil = Time.time + Mathf.Max(3f, seconds + 1f);
    }

    // ================================================================= doctor panel (PC window, English)
    void OnGUI()
    {
        if (big == null)
        {
            big = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, wordWrap = true };
            small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            btn = new GUIStyle(GUI.skin.button) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
        }
        string m = mic && mic.Available ? (mic.Speaking ? "● patient is SPEAKING" : "○ silent " + mic.SilentFor.ToString("0") + " s") : "no microphone";
        GUILayout.BeginArea(new Rect(10, 10, 430, Screen.height - 20), GUI.skin.box);
        GUILayout.Label("Presentation  ·  Level " + level + " (" + L.en + ")  ·  " + Phase, big);
        if (Phase == "presentation") GUILayout.Label(Fmt(Time.time - talkStart) + " / " + duration + ":00    slide " + (Slide + 1) + "/" + slides.Length + "    " + m, small);
        else GUILayout.Label(m, small);

        if (Phase == "menu")
        {
            GUILayout.BeginHorizontal();
            for (int i = 1; i <= 3; i++) if (GUILayout.Button("F" + i + "  Start level " + i + "\n" + Levels[i - 1].en, GUILayout.Height(44))) StartLevel(i);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Minutes:", small, GUILayout.Width(60));
            foreach (var d in Durations) if (GUILayout.Button((duration == d ? "● " : "") + d, GUILayout.Height(24))) { duration = d; RefreshToggles(); }
            GUILayout.EndHorizontal();
        }
        if (Phase == "presentation")
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("←  previous slide", GUILayout.Height(28))) SetSlide(Slide - 1);
            if (GUILayout.Button("→  next slide", GUILayout.Height(28))) SetSlide(Slide + 1);
            GUILayout.EndHorizontal();
            GUILayout.Label("— participants say —", small);
            for (int i = 0; i < DoctorEvents.Length; i++)
            {
                var (role, de, en) = Lines[DoctorEvents[i]];
                if (GUILayout.Button((i + 1) + "  " + (role == "boss" ? "Boss: " : role == "woman" ? "Woman: " : "Man: ") + en, btn, GUILayout.Height(26))) Fire(DoctorEvents[i]);
            }
            GUILayout.Label("— disturbances —", small);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("P  phone buzzes", GUILayout.Height(26))) Fire("phone");
            if (GUILayout.Button("W  whispering", GUILayout.Height(26))) Fire("whisper");
            if (GUILayout.Button("S  all stare", GUILayout.Height(26))) Fire("stare");
            GUILayout.EndHorizontal();
            if (GUILayout.Button("F12  End the presentation", GUILayout.Height(30))) endTalk = true;
        }
        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Anxiety 0-10:", small, GUILayout.Width(80));
        for (int s = 0; s <= 10; s++) if (GUILayout.Button(s.ToString(), GUILayout.Width(28))) Rate(s);
        GUILayout.EndHorizontal();
        if (GUILayout.Button("A  automatic events of the level: " + (autoEvents ? "ON" : "OFF"), GUILayout.Height(22))) autoEvents = !autoEvents;
        GUILayout.Label("T subtitles " + (subtitles ? "on" : "off") + "  ·  E subtitles " + (English ? "English" : "German") + "  ·  Ctrl+0-9 anxiety  ·  Ctrl+Enter start", small);
        scroll = GUILayout.BeginScrollView(scroll);
        for (int i = log.Count - 1; i >= Mathf.Max(0, log.Count - 14); i--) GUILayout.Label(log[i], small);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
}
