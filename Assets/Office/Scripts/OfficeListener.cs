using UnityEngine;

// A participant of the meeting, sitting on a chair.
//
// SEAT FIT (the reason this component exists): sitting clips are authored for some unknown seat height and root offset.
// Played as they are, the body floats above (or sinks into) our chair. So for every clip the average hips position was
// measured in the editor (clipNames / clipHips); at runtime the body is shifted so that the pelvis rests exactly on the
// seat, and the feet are planted flat on the floor with IK. The clip's own motion (breathing, shifting weight) stays.
//
// GAZE: no Animator look-at (it bends the whole trunk sideways for large angles). The turn toward the target is split
// like a person does it: a little in the chest (max 18 deg), the rest in neck + head (max 55 deg), never any sideways lean.
// The chair itself is already turned a bit toward the presenter by the scene builder.
//
// DETERMINISTIC: all choices come from a random generator seeded with (seat, level), so a level always plays the same way.
[RequireComponent(typeof(Animator))]
public class OfficeListener : MonoBehaviour
{
    public string seat, role = "";
    public int minLevel = 1;
    public bool female, usesLaptop;
    public Transform patientHead;
    public Vector3 deskPoint, awayPoint;
    public OfficeListener[] neighbours = new OfficeListener[0];

    [Header("Seat fit (filled by the scene builder)")]
    public float seatHeight = 0.46f;
    public float pelvisAboveSeat = 0.10f;       // height of the hips bone above the seat surface when sitting
    public float pelvisBack = -0.03f;           // hips bone relative to the seat centre (forward +)
    public string[] clipNames = new string[0];
    public Vector3[] clipHips = new Vector3[0]; // average hips position of each clip in the character's local space
    public Vector3 headForwardLocal = Vector3.forward;   // the face direction in the head bone's space (measured in the bind pose)

    [Header("Posture limits (deg): the trunk never leans further than this, whatever the clip does")]
    public float maxLeanForward = 5f;
    public float maxLeanBack = 16f, maxLeanSide = 4f;

    [Header("Table (filled by the scene builder): hands never go into it")]
    public bool hasTable;
    public Bounds table;
    public float correctedShare;      // debug: share of frames a hand was inside the table since the last reset
    // 2026-11-16 user: "dursunlar yerlerinde bize baksınlar", then "bu kadar robot durmasınlar": calm body only — ONE looping
    // hands-in-lap idle (f_sit_chair_idle_relaxed_01, random start + speed per person; the only clip that never touched the table
    // in a per-frame forearm/finger check: neutral_01/02 and waiting lift a hand into the edge), head-only gaze + nods;
    // no chest turn, no clip changes, no clap motion (every arm motion brought hand / shoulder bugs).
    public static bool Still = true;
    public const string StillState = "Relaxed";
    public static string TestState;   // editor test only: everybody loops this one state
    public bool calmOnly;           // front seats: only the calm sitting idles (no look-around / waiting arm motion)

    // set by the session
    [Range(0, 1)] public float attention = 0.8f;     // share of the time the presenter is looked at
    public float nodsPerMinute = 3f;                 // friendly nodding while looking at the presenter
    [Range(0, 1)] public float restless = 0.1f;      // fidgety idles, looking away
    public bool whisper;                             // may turn to the neighbour

    static readonly string[] Calm = { "Sit", "Sit2" };
    static readonly string[] Lively = { "LookAround", "Waiting" };
    const float MaxChestYaw = 18f, MaxHeadYaw = 55f, MaxUp = 22f, MaxDown = 32f;

    Animator an; Transform head, neck, jaw; Transform[] spine; Quaternion jawRest;
    AudioSource voice; readonly float[] samples = new float[256];
    System.Random rng = new System.Random(1);
    float clock, nextState, nextLook, lookW, lookWTarget, forcedUntil, nextNod, nodT = -1f, nodDepth, jawOpen;
    float yawCur, pitchCur, yawVel, pitchVel, clapUntil;
    Vector3 lookTarget, lookSmooth, forcedPoint, fitOffset; bool fitReady;
    bool lookingAtPatient, forced, forcedPatient, clapping;
    readonly AnimatorClipInfo[] none = new AnimatorClipInfo[0];

    public Vector3 HeadPos => head ? head.position : transform.position + Vector3.up * 1.2f;
    public bool LooksAtPatient => lookingAtPatient && lookW > 0.5f;
    public bool Speaking => voice && voice.isPlaying;
    public bool CanClap => an && an.HasState(0, Animator.StringToHash("Clap"));

    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    int RI(int n) => rng.Next(n);
    static int Hash(string s) { unchecked { int h = 23; foreach (var c in s) h = h * 31 + c; return h; } }

    void Awake()
    {
        an = GetComponent<Animator>();
        head = an.GetBoneTransform(HumanBodyBones.Head); neck = an.GetBoneTransform(HumanBodyBones.Neck);
        var list = new System.Collections.Generic.List<Transform>();
        foreach (var b in new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest }) { var t = an.GetBoneTransform(b); if (t) list.Add(t); }
        spine = list.ToArray();
        armL = Arm(true); armR = Arm(false);
        foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name.EndsWith("MJaw")) { jaw = t; jawRest = t.localRotation; break; }   // Rocketbox jaw bone (not humanoid)
        voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false; voice.spatialBlend = 1f; voice.minDistance = 1.2f; voice.maxDistance = 12f; voice.rolloffMode = AudioRolloffMode.Linear;
        lookSmooth = lookTarget = deskPoint;
    }

    // same level -> same sequence, every time
    public void Reseed(int level)
    {
        rng = new System.Random(Hash(seat) * 7 + level * 1009);
        clock = 0f; forced = false; nodT = -1f; clapping = false;
        an.speed = R(0.94f, 1.06f);
        if (an.isActiveAndEnabled) an.Play(Still ? StillState : Calm[RI(Calm.Length)], 0, R(0f, 1f));
        nextState = R(6f, 14f); nextLook = R(0.2f, 1.5f); nextNod = R(4f, 12f);
        PickLook(true);
    }

    void PickLook(bool first = false)
    {
        double r = rng.NextDouble();
        lookingAtPatient = false;
        if (patientHead && (first || r < attention)) { lookingAtPatient = true; lookWTarget = 1f; nextLook = clock + R(5f, 12f) * Mathf.Lerp(0.5f, 1.4f, attention); }
        else
        {
            double q = rng.NextDouble();
            if (whisper && neighbours.Length > 0 && q < 0.25) { lookTarget = neighbours[RI(neighbours.Length)].HeadPos; lookWTarget = 0.8f; nextLook = clock + R(2.5f, 5f); }
            else if (q < 0.25 + restless * 0.45f) { lookTarget = awayPoint; lookWTarget = 0.8f; nextLook = clock + R(2f, 4.5f); }
            else { lookTarget = deskPoint; lookWTarget = 0.9f; nextLook = clock + R(2.5f, 7f) * (usesLaptop ? 1.4f : 1f); }
        }
    }

    void Update()
    {
        clock += Time.deltaTime;
        if (clapping && Time.time > clapUntil) { clapping = false; an.CrossFadeInFixedTime(Calm[RI(Calm.Length)], 0.5f); nextState = clock + R(8f, 16f); }
        if (TestState != null) { if (!an.GetCurrentAnimatorStateInfo(0).IsName(TestState) && !an.IsInTransition(0)) an.Play(TestState, 0, 0f); }
        else if (!Still && !clapping && clock > nextState)
        {
            string s = !Still && !calmOnly && rng.NextDouble() < restless ? Lively[RI(Lively.Length)] : Calm[RI(Calm.Length)];
            an.CrossFadeInFixedTime(s, Still ? 2.5f : 1.5f);
            nextState = clock + R(9f, 20f);
        }
        if (forced && Time.time > forcedUntil) { forced = false; nextLook = clock; }
        if (forced) { lookingAtPatient = forcedPatient; lookTarget = forcedPatient && patientHead ? patientHead.position : forcedPoint; lookWTarget = 1f; }
        else
        {
            if (clock > nextLook) PickLook();
            if (lookingAtPatient && patientHead) lookTarget = patientHead.position;
        }
        lookW = Mathf.MoveTowards(lookW, lookWTarget, Time.deltaTime * 1.2f);
        lookSmooth = Vector3.Lerp(lookSmooth, lookTarget, 1f - Mathf.Exp(-Time.deltaTime * 4f));

        // friendly nod now and then while looking at the presenter
        if (nodT < 0f && nodsPerMinute > 0.01f && clock > nextNod)
        {
            nextNod = clock + R(0.6f, 1.4f) * 60f / nodsPerMinute;
            if (LooksAtPatient) { nodT = 0f; nodDepth = R(5f, 9f); }
        }
        if (nodT >= 0f) { nodT += Time.deltaTime; if (nodT > 1.3f) nodT = -1f; }
        MouthUpdate();
    }

    // mouth follows the loudness of the own voice line
    void MouthUpdate()
    {
        float target = 0f;
        if (voice.isPlaying)
        {
            voice.GetOutputData(samples, 0);
            float sum = 0f; foreach (var v in samples) sum += v * v;
            target = Mathf.Clamp(Mathf.Sqrt(sum / samples.Length) * 30f, 0f, 1f);
        }
        jawOpen = Mathf.Lerp(jawOpen, target, 1f - Mathf.Exp(-Time.deltaTime * 22f));
    }

    // ------------------------------------------------------------------ seat fit
    Vector3 HipsOf(AnimationClip c)
    {
        for (int i = 0; i < clipNames.Length; i++) if (clipNames[i] == c.name) return clipHips[i];
        return new Vector3(0f, seatHeight + pelvisAboveSeat, pelvisBack);
    }

    // where the playing clip(s) put the hips, blended across a cross-fade
    Vector3 CurrentClipHips()
    {
        Vector3 sum = Vector3.zero; float w = 0f;
        foreach (var ci in an.GetCurrentAnimatorClipInfo(0)) { sum += HipsOf(ci.clip) * ci.weight; w += ci.weight; }
        if (an.IsInTransition(0)) foreach (var ci in an.GetNextAnimatorClipInfo(0)) { sum += HipsOf(ci.clip) * ci.weight; w += ci.weight; }
        return w > 0.001f ? sum / w : new Vector3(0f, seatHeight + pelvisAboveSeat, pelvisBack);
    }

    void OnAnimatorIK(int layer)
    {
        if (clipNames.Length == 0) return;
        var want = new Vector3(0f, seatHeight + pelvisAboveSeat, pelvisBack);
        var off = transform.TransformVector(want - CurrentClipHips());
        fitOffset = fitReady ? Vector3.Lerp(fitOffset, off, 1f - Mathf.Exp(-Time.deltaTime * 6f)) : off; fitReady = true;
        an.bodyPosition += fitOffset;
        Foot(AvatarIKGoal.LeftFoot, an.leftFeetBottomHeight);
        Foot(AvatarIKGoal.RightFoot, an.rightFeetBottomHeight);
    }

    // ------------------------------------------------------------------ hands vs. own legs
    // The idle clip was made for a slimmer body: at some moments the fingers sink into the thighs / knees / jacket.
    // Each thigh = an elliptic cylinder hip -> knee (top + side radius measured ONCE from the own baked mesh, at mid thigh and
    // at the knee). Every frame the animated fingers are tested against it and the hand is raised just enough (+5 mm):
    // pure geometry on the animated pose (no feedback), raised at once, lowered slowly -> smooth, never on/off.
    // Done ONLY by bending the elbow a little (forearm rotation): shoulder and upper arm are never touched.
    public static bool HandLiftOn = true;
    public float[] legTop = new float[0], legSide = new float[0];   // [leg * 2 + station], station 0 = mid thigh, 1 = knee (m)
    public float liftL, liftR;                                       // current lift of each hand (m), also for the editor check
    public static bool InstantLift;                                  // editor checks only: no smoothing
    public float maxLiftStep;                                        // debug: biggest lift change in one frame (m)
    bool legsMeasured;
    const float MaxLift = 0.08f;

    public void ApplyHandLift()
    {
        if (!HandLiftOn || armL == null) return;
        if (!legsMeasured) { legsMeasured = true; MeasureLegs(); }
        if (legTop.Length < 4) return;
        // the table wins: a hand is never raised into the underside of the table top
        // target = what the thighs need, but only as much as the table lets the bent arm go (checked on the bent result)
        float wl = FitUnderTable(armL, Mathf.Min(NeedLift(true), TableCap(true)));
        float wr = FitUnderTable(armR, Mathf.Min(NeedLift(false), TableCap(false)));
        float l0 = liftL, r0 = liftR;
        // always smooth: up 25 cm/s, down 3 cm/s, or 25 cm/s when the table needs room
        liftL = InstantLift ? wl : Mathf.MoveTowards(liftL, wl, Time.deltaTime * (wl > liftL || liftL - wl > 0.01f && TableTight(armL, liftL) ? 0.25f : 0.03f));
        liftR = InstantLift ? wr : Mathf.MoveTowards(liftR, wr, Time.deltaTime * (wr > liftR || liftR - wr > 0.01f && TableTight(armR, liftR) ? 0.25f : 0.03f));
        maxLiftStep = Mathf.Max(maxLiftStep, Mathf.Abs(liftL - l0), Mathf.Abs(liftR - r0));
        BendForearm(armL, Vector3.up * liftL);
        BendForearm(armR, Vector3.up * liftR);
    }

    // would this lift bring the hand within 1.5 cm of the table?
    bool TableTight(Transform[] arm, float lift)
    {
        if (!hasTable || lift <= 0f || arm[1] == null) return false;
        var rot = arm[1].rotation; BendForearm(arm, Vector3.up * lift);
        float c = TableClearance(arm); arm[1].rotation = rot;
        return c < 0.01f;
    }

    float FitUnderTable(Transform[] arm, float lift)
    {
        if (!hasTable || lift <= 0f || arm[1] == null) return lift;
        var rot = arm[1].rotation;
        for (int i = 0; i < 4; i++)
        {
            arm[1].rotation = rot; BendForearm(arm, Vector3.up * lift);
            float c = TableClearance(arm);
            if (c >= 0f) break;
            lift = Mathf.Max(0f, lift + c - 0.002f);
        }
        arm[1].rotation = rot;
        return lift;
    }

    // smallest gap (m) between this hand / forearm end and the table top slab (5 mm margin); > 0 = free
    float TableClearance(Transform[] arm)
    {
        var slab = new Bounds(); slab.SetMinMax(new Vector3(table.min.x, table.max.y - 0.045f, table.min.z), table.max);
        float c = 9f;
        c = Mathf.Min(c, Mathf.Sqrt(slab.SqrDistance(arm[2].position)) - 0.025f - 0.005f);
        c = Mathf.Min(c, Mathf.Sqrt(slab.SqrDistance(Vector3.Lerp(arm[1].position, arm[2].position, 0.8f))) - 0.04f - 0.005f);
        int a0 = arm == armL ? 0 : 15;
        for (int b = a0; b < a0 + 15; b++) if (fingers[b]) c = Mathf.Min(c, Mathf.Sqrt(slab.SqrDistance(fingers[b].position)) - 0.013f - 0.005f);
        return c;
    }

    // how far (m) this hand may go up before a hand / finger bone sphere (or the wrist end of the forearm) gets within 5 mm of the
    // underside of the table top
    float TableCap(bool left)
    {
        if (!hasTable) return MaxLift;
        var arm = left ? armL : armR; if (arm[2] == null) return MaxLift;
        float bottom = table.max.y - 0.045f, cap = MaxLift;
        void Pt(Vector3 p, float r)
        {
            if (p.x < table.min.x - r || p.x > table.max.x + r || p.z < table.min.z - r || p.z > table.max.z + r) return;
            cap = Mathf.Min(cap, bottom - 0.005f - r - p.y);
        }
        Pt(arm[2].position, 0.025f);
        Pt(Vector3.Lerp(arm[1].position, arm[2].position, 0.8f), 0.04f);
        int a0 = left ? 0 : 15;
        for (int b = a0; b < a0 + 15; b++) if (fingers[b]) Pt(fingers[b].position, 0.013f);
        return Mathf.Max(0f, cap);
    }

    // how far (m) this hand has to go up so that no hand / finger bone sphere is inside a thigh
    float NeedLift(bool left)
    {
        var arm = left ? armL : armR; if (arm[2] == null) return 0f;
        float need = 0f;
        need = Mathf.Max(need, PointLift(arm[2].position, 0.015f));
        int a0 = left ? (int)HumanBodyBones.LeftThumbProximal : (int)HumanBodyBones.RightThumbProximal;
        for (int b = a0; b < a0 + 15; b++) { var t = fingers[b - (int)HumanBodyBones.LeftThumbProximal]; if (t) need = Mathf.Max(need, PointLift(t.position, 0.008f)); }
        return Mathf.Min(need, MaxLift);
    }

    float PointLift(Vector3 p, float r)
    {
        float need = 0f;
        for (int leg = 0; leg < 2; leg++)
        {
            Vector3 A = legBones[leg * 2].position, B = legBones[leg * 2 + 1].position, ax = B - A;
            float t = Vector3.Dot(p - A, ax) / ax.sqrMagnitude; if (t < 0.1f || t > 1.08f) continue;
            Frame(ax, out var up, out var side);
            float k = Mathf.InverseLerp(0.45f, 0.95f, t);
            float a = Mathf.Lerp(legTop[leg * 2], legTop[leg * 2 + 1], k) + r, b = Mathf.Lerp(legSide[leg * 2], legSide[leg * 2 + 1], k) + r;
            var d = p - (A + ax * Mathf.Clamp01(t)); float w = Vector3.Dot(d, up), u = Vector3.Dot(d, side);
            if (w < 0f || Mathf.Abs(u) >= b) continue;              // under the thigh axis / beside the leg: nothing to do
            float wExit = a * Mathf.Sqrt(1f - (u / b) * (u / b));
            if (w >= wExit) continue;
            need = Mathf.Max(need, (wExit - w) / Mathf.Max(0.5f, up.y) + 0.005f);
        }
        return need;
    }

    static void Frame(Vector3 ax, out Vector3 up, out Vector3 side)
    {
        var dir = ax.normalized; up = Vector3.ProjectOnPlane(Vector3.up, dir);
        up = up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.forward; side = Vector3.Cross(dir, up);
    }

    Transform[] legBones, fingers;

    // top / side radius of both thighs from the own mesh (arm and hand vertices ignored), once
    void MeasureLegs()
    {
        legBones = new[] { an.GetBoneTransform(HumanBodyBones.LeftUpperLeg), an.GetBoneTransform(HumanBodyBones.LeftLowerLeg),
                           an.GetBoneTransform(HumanBodyBones.RightUpperLeg), an.GetBoneTransform(HumanBodyBones.RightLowerLeg) };
        fingers = new Transform[30];
        for (int i = 0; i < 30; i++) fingers[i] = an.GetBoneTransform((HumanBodyBones)((int)HumanBodyBones.LeftThumbProximal + i));
        foreach (var t in legBones) if (!t) return;
        SkinnedMeshRenderer smr = null;
        foreach (var s in GetComponentsInChildren<SkinnedMeshRenderer>()) if (smr == null || s.sharedMesh.vertexCount > smr.sharedMesh.vertexCount) smr = s;
        if (!smr) return;
        var bones = smr.bones; var arm = new bool[bones.Length];
        var ua = new[] { an.GetBoneTransform(HumanBodyBones.LeftUpperArm), an.GetBoneTransform(HumanBodyBones.RightUpperArm) };
        for (int i = 0; i < bones.Length; i++) foreach (var u in ua) if (bones[i] && u && bones[i].IsChildOf(u)) arm[i] = true;
        var bw = smr.sharedMesh.boneWeights;
        var mesh = new Mesh(); smr.BakeMesh(mesh, true); var verts = mesh.vertices; Destroy(mesh);
        var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
        legTop = new float[4]; legSide = new float[4];
        for (int i = 0; i < verts.Length; i++)
        {
            var w8 = bw[i]; float aw = (arm[w8.boneIndex0] ? w8.weight0 : 0f) + (arm[w8.boneIndex1] ? w8.weight1 : 0f) + (arm[w8.boneIndex2] ? w8.weight2 : 0f) + (arm[w8.boneIndex3] ? w8.weight3 : 0f);
            if (aw > 0.2f) continue;
            var v = m.MultiplyPoint3x4(verts[i]);
            // which leg is this vertex nearer to
            int leg = -1; float best = 9f, bt = 0f;
            for (int g = 0; g < 2; g++)
            {
                Vector3 A = legBones[g * 2].position, ax = legBones[g * 2 + 1].position - A;
                float t = Vector3.Dot(v - A, ax) / ax.sqrMagnitude; var c = A + ax * Mathf.Clamp01(t);
                float dd = (v - c).sqrMagnitude; if (dd < best) { best = dd; leg = g; bt = t; }
            }
            int st = bt >= 0.3f && bt <= 0.6f ? 0 : bt >= 0.85f && bt <= 1.0f ? 1 : -1; if (st < 0) continue;
            Vector3 A2 = legBones[leg * 2].position, ax2 = legBones[leg * 2 + 1].position - A2;
            Frame(ax2, out var up, out var side);
            var d = v - (A2 + ax2 * bt); float w = Vector3.Dot(d, up), u = Vector3.Dot(d, side);
            if (Mathf.Abs(u) < 0.025f && w > 0f && w < 0.2f) legTop[leg * 2 + st] = Mathf.Max(legTop[leg * 2 + st], w);
            if (Mathf.Abs(w) < 0.025f && Mathf.Abs(u) < 0.2f) legSide[leg * 2 + st] = Mathf.Max(legSide[leg * 2 + st], Mathf.Abs(u));
        }
        for (int i = 0; i < 4; i++)
        {
            if (legSide[i] <= 0f) legSide[i] = 0.08f;
            // a thigh is about round: with crossed legs the other leg lies on top and would count as "thigh top"
            if (legTop[i] <= 0f || legTop[i] > legSide[i] + 0.015f) legTop[i] = legSide[i] + 0.015f;
        }
    }

    static void BendForearm(Transform[] arm, Vector3 lift)
    {
        if (arm[1] == null || arm[2] == null || lift.sqrMagnitude < 1e-8f) return;
        var fore = arm[2].position - arm[1].position;
        arm[1].rotation = Quaternion.FromToRotation(fore, fore + lift) * arm[1].rotation;
    }



    // foot where the clip has it (moved with the body), but flat on the floor
    void Foot(AvatarIKGoal goal, float bottom)
    {
        var p = an.GetIKPosition(goal) + fitOffset; p.y = transform.position.y + bottom;
        var f = Vector3.ProjectOnPlane(an.GetIKRotation(goal) * Vector3.forward, Vector3.up);
        if (f.sqrMagnitude < 0.01f) f = transform.forward;
        an.SetIKPositionWeight(goal, 1f); an.SetIKRotationWeight(goal, 1f);
        an.SetIKPosition(goal, p); an.SetIKRotation(goal, Quaternion.LookRotation(f.normalized, Vector3.up));
    }

    // ------------------------------------------------------------------ posture, gaze, nod, mouth (after the animation)
    void LateUpdate()
    {
        // 1. posture: straighten the trunk if the clip leans too far (men's clips rest the elbows on the knees -> into the table)
        var hips = an.GetBoneTransform(HumanBodyBones.Hips);
        if (hips && neck && spine.Length > 0)
        {
            var v = transform.InverseTransformDirection(neck.position - hips.position);
            float lean = Mathf.Atan2(v.z, v.y) * Mathf.Rad2Deg, sideLean = Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg;
            float dLean = Mathf.Clamp(lean, -maxLeanBack, maxLeanForward) - lean;
            float dSide = Mathf.Clamp(sideLean, -maxLeanSide, maxLeanSide) - sideLean;
            if (Mathf.Abs(dLean) > 0.05f || Mathf.Abs(dSide) > 0.05f)
                spine[0].rotation = Quaternion.AngleAxis(dLean, transform.right) * Quaternion.AngleAxis(-dSide, transform.forward) * spine[0].rotation;
        }
        if (head)
        {
            // 2. gaze: a little chest, the rest neck + head; the head really faces the target (clamped to what a neck can do)
            var local = transform.InverseTransformDirection(lookSmooth - head.position);
            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;   // + = down
            float w = clapping ? Mathf.Min(lookW, 0.6f) : lookW;
            float maxYaw = Still ? MaxHeadYaw : MaxChestYaw + MaxHeadYaw;
            yaw = Mathf.Clamp(yaw, -maxYaw, maxYaw);
            if (pitch < 0f) pitch *= 0.7f;      // looking up at a standing person: the eyes do part of it, the chin does not go all the way
            pitch = Mathf.Clamp(pitch, -MaxUp, MaxDown);
            yawCur = Mathf.SmoothDampAngle(yawCur, yaw, ref yawVel, 0.30f);
            pitchCur = Mathf.SmoothDampAngle(pitchCur, pitch, ref pitchVel, 0.30f);

            float chest = Still ? 0f : Mathf.Clamp(yawCur * 0.26f, -MaxChestYaw, MaxChestYaw) * w;   // still: the chest (and so the arms) never turns
            var q = Quaternion.AngleAxis(chest / spine.Length, Vector3.up);
            foreach (var b in spine) b.rotation = q * b.rotation;

            // wanted face direction in world space
            var want = transform.rotation * (Quaternion.Euler(pitchCur, yawCur, 0f) * Vector3.forward);
            if (neck)
            {
                var d1 = Quaternion.FromToRotation(head.TransformDirection(headForwardLocal), want);
                neck.rotation = Quaternion.Slerp(Quaternion.identity, d1, 0.4f * w) * neck.rotation;
            }
            var d2 = Quaternion.FromToRotation(head.TransformDirection(headForwardLocal), want);
            head.rotation = Quaternion.Slerp(Quaternion.identity, d2, w) * head.rotation;
            // keep the head level (FromTo can roll it)
            var f = head.TransformDirection(headForwardLocal);
            var levelRight = Vector3.Cross(Vector3.up, f).normalized;
            if (nodT >= 0f)
            {
                float a = Mathf.Sin(nodT / 1.3f * Mathf.PI * 2f) * Mathf.Sin(nodT / 1.3f * Mathf.PI) * nodDepth;
                head.rotation = Quaternion.AngleAxis(Mathf.Max(a, -2f), levelRight) * head.rotation;
            }
        }
        // 3. hands: only the small elbow bend that keeps the fingers out of the own legs (see ApplyHandLift); then measured
        ApplyHandLift();
        if (hasTable && armL != null) MeasureHands();
        if (jaw)
        {
            jaw.localRotation = jawRest;
            if (jawOpen > 0.01f) jaw.rotation = Quaternion.AngleAxis(jawOpen * 7f, transform.right) * jaw.rotation;
        }
    }

    // ------------------------------------------------------------------ hands vs. table
    // arm = upper arm, forearm, hand, index tip, middle tip, middle knuckle
    Transform[] armL, armR; int frames, badFrames;

    Transform[] Arm(bool l)
    {
        return new[]
        {
            an.GetBoneTransform(l ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm),
            an.GetBoneTransform(l ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm),
            an.GetBoneTransform(l ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand),
            an.GetBoneTransform(l ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal),
            an.GetBoneTransform(l ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal),
            an.GetBoneTransform(l ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal),
        };
    }

    // debug (editor only): exact clearance of forearms (r 4 cm), hands (r 2.5 cm) and finger bones (r 1.3 cm) to the table
    // top slab (4.5 cm thick), every frame; minClearance < 0 = something went into the table
    public float minClearance = 9f; public string minWhere = "", badMoments = "";
    void MeasureHands()
    {
#if UNITY_EDITOR
        var slab = new Bounds(); slab.SetMinMax(new Vector3(table.min.x, table.max.y - 0.045f, table.min.z), table.max);
        bool bad = false;
        foreach (bool left in new[] { true, false })
        {
            var arm = left ? armL : armR; if (arm[1] == null || arm[2] == null) continue;
            for (int i = 0; i <= 10; i++) Check(slab, Vector3.Lerp(arm[1].position, arm[2].position, i / 10f), 0.04f, left ? "L forearm" : "R forearm", ref bad);
            Check(slab, arm[2].position, 0.025f, left ? "L hand" : "R hand", ref bad);
            int a = left ? (int)HumanBodyBones.LeftThumbProximal : (int)HumanBodyBones.RightThumbProximal;
            for (int b = a; b < a + 15; b++) { var t = an.GetBoneTransform((HumanBodyBones)b); if (t) Check(slab, t.position, 0.013f, (left ? "L " : "R ") + "finger", ref bad); }
        }
        frames++; if (bad) badFrames++;
        correctedShare = badFrames / (float)frames;
        if (bad)
        {
            var st = an.GetCurrentAnimatorStateInfo(0); var ci = an.GetCurrentAnimatorClipInfo(0);
            string k = (ci.Length > 0 ? ci[0].clip.name : "?") + " @" + (st.normalizedTime % 1f).ToString("0.00") + (an.IsInTransition(0) ? " (fade)" : "");
            if (!badMoments.Contains(k) && badMoments.Length < 400) badMoments += k + "; ";
        }
#endif
    }
    void Check(Bounds slab, Vector3 p, float r, string where, ref bool bad)
    {
        float c = Mathf.Sqrt(slab.SqrDistance(p)) - r;
        if (c < minClearance) { minClearance = c; minWhere = where; }
        if (c < 0f) bad = true;
    }

    public void ResetHandStats() { maxLiftStep = 0f; minClearance = 9f; minWhere = badMoments = ""; frames = badFrames = 0; correctedShare = 0f; }

    // ------------------------------------------------------------------ events from the session
    public void LookAtPatient(float seconds) { forced = true; forcedPatient = true; forcedUntil = Time.time + seconds; }
    public void LookAt(Vector3 point, float seconds) { forced = true; forcedPatient = false; forcedPoint = point; forcedUntil = Time.time + seconds; }

    public float Say(AudioClip clip, float volume = 1f)
    {
        if (!clip) return 0f;
        voice.Stop(); voice.clip = clip; voice.volume = volume; voice.Play();
        LookAtPatient(clip.length + 3f);
        return clip.length;
    }

    public void Nod() { if (nodT < 0f) { nodT = 0f; nodDepth = 8f; } }

    // applause: a real sitting clap clip (if the project has one), never hand IK; still mode: no motion (the sound still plays)
    public void Clap(float seconds, float speed = 1f)
    {
        if (Still) return;
        if (!CanClap) { Nod(); return; }
        clapping = true; clapUntil = Time.time + seconds;
        an.speed = speed; an.CrossFadeInFixedTime("Clap", 0.35f, 0, 0f);
    }
}
