using UnityEngine;

// ExposiaKit - a humanoid character sitting on a chair, for ANY game (audience, restaurant guests, waiting room, train ...).
// It fixes the three things that made seated NPCs look wrong in every project:
//
// 1. SEAT FIT  Sitting clips are made for some unknown seat height / root offset. Played as they are, the body floats above
//              the chair or sinks into it. The average hips position of every clip is measured in the editor
//              (SeatedNpcSetup.Measure -> clipNames / clipHips); here the body is shifted so that the pelvis rests on OUR seat
//              and the feet are planted flat on the floor with IK. The clip's own motion stays.
// 2. POSTURE   Whatever the clip does, the trunk never leans further than the limits (no elbows-on-knees into a table).
// 3. GAZE      No Animator look-at (it bends the whole trunk sideways). A turn is split like a person does it:
//              chest max 18 deg, neck + head max 55 deg, never a sideways lean. Turn the CHAIR toward whatever the person
//              should mostly watch (20-25 deg) so the neck stays in a natural range.
//
// Use: put the character at the seat centre on the FLOOR, facing the way the chair faces; add this component;
// call SeatedNpcSetup.Measure(gameObject) in your scene builder; set seatHeight. Then LookAt(...) / LookAtClip() from your logic.
// Check the result with SeatedNpcSetup.Photograph() - it saves a side + front picture of every seated character.
[RequireComponent(typeof(Animator))]
public class SeatedNpc : MonoBehaviour
{
    [Header("Seat (set by the scene builder)")]
    public float seatHeight = 0.46f;            // seat surface above the character's floor
    public float pelvisAboveSeat = 0.10f;       // hips bone above the seat surface when sitting
    public float pelvisBack = -0.03f;           // hips bone relative to the seat centre (forward +)
    public bool plantFeet = true;

    [Header("Measured by SeatedNpcSetup.Measure")]
    public string[] clipNames = new string[0];
    public Vector3[] clipHips = new Vector3[0];             // average hips position of each clip, character space
    public Vector3 headForwardLocal = Vector3.forward;      // face direction in the head bone's space (bind pose)

    [Header("Posture limits (deg)")]
    public float maxLeanForward = 5f;
    public float maxLeanBack = 16f, maxLeanSide = 4f;

    [Header("Gaze limits (deg)")]
    public float maxChestYaw = 18f;
    public float maxHeadYaw = 55f, maxUp = 22f, maxDown = 32f;

    Animator an; Transform hips, head, neck; Transform[] spine;
    Vector3 fitOffset, lookPoint, lookSmooth; bool fitReady; Transform lookTarget;
    float lookW, lookWTarget, yawCur, pitchCur, yawVel, pitchVel;

    public Animator Animator => an;
    public Vector3 HeadPosition => head ? head.position : transform.position + Vector3.up * 1.2f;
    public bool LooksAtTarget => lookW > 0.5f;

    void Awake()
    {
        an = GetComponent<Animator>();
        hips = an.GetBoneTransform(HumanBodyBones.Hips);
        head = an.GetBoneTransform(HumanBodyBones.Head); neck = an.GetBoneTransform(HumanBodyBones.Neck);
        var list = new System.Collections.Generic.List<Transform>();
        foreach (var b in new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest }) { var t = an.GetBoneTransform(b); if (t) list.Add(t); }
        spine = list.ToArray();
        lookSmooth = lookPoint = transform.position + transform.forward * 2f + Vector3.up * 1.1f;
    }

    // ------------------------------------------------------------------ gaze API
    public void LookAt(Transform target, float weight = 1f) { lookTarget = target; lookWTarget = weight; }
    public void LookAt(Vector3 point, float weight = 1f) { lookTarget = null; lookPoint = point; lookWTarget = weight; }
    public void LookAtClip() { lookWTarget = 0f; }      // head as animated by the clip

    void Update()
    {
        if (lookTarget) lookPoint = lookTarget.position;
        lookW = Mathf.MoveTowards(lookW, lookWTarget, Time.deltaTime * 1.2f);
        lookSmooth = Vector3.Lerp(lookSmooth, lookPoint, 1f - Mathf.Exp(-Time.deltaTime * 4f));
    }

    // ------------------------------------------------------------------ seat fit
    Vector3 Want => new Vector3(0f, seatHeight + pelvisAboveSeat, pelvisBack);

    Vector3 HipsOf(AnimationClip c)
    {
        for (int i = 0; i < clipNames.Length; i++) if (clipNames[i] == c.name) return clipHips[i];
        return Want;
    }

    Vector3 CurrentClipHips()
    {
        Vector3 sum = Vector3.zero; float w = 0f;
        foreach (var ci in an.GetCurrentAnimatorClipInfo(0)) { sum += HipsOf(ci.clip) * ci.weight; w += ci.weight; }
        if (an.IsInTransition(0)) foreach (var ci in an.GetNextAnimatorClipInfo(0)) { sum += HipsOf(ci.clip) * ci.weight; w += ci.weight; }
        return w > 0.001f ? sum / w : Want;
    }

    // needs "IK Pass" on the Animator layer
    void OnAnimatorIK(int layer)
    {
        if (clipNames.Length == 0) return;
        var off = transform.TransformVector(Want - CurrentClipHips());
        fitOffset = fitReady ? Vector3.Lerp(fitOffset, off, 1f - Mathf.Exp(-Time.deltaTime * 6f)) : off; fitReady = true;
        an.bodyPosition += fitOffset;
        if (!plantFeet) return;
        Foot(AvatarIKGoal.LeftFoot, an.leftFeetBottomHeight);
        Foot(AvatarIKGoal.RightFoot, an.rightFeetBottomHeight);
    }

    void Foot(AvatarIKGoal goal, float bottom)
    {
        var p = an.GetIKPosition(goal) + fitOffset; p.y = transform.position.y + bottom;
        var f = Vector3.ProjectOnPlane(an.GetIKRotation(goal) * Vector3.forward, Vector3.up);
        if (f.sqrMagnitude < 0.01f) f = transform.forward;
        an.SetIKPositionWeight(goal, 1f); an.SetIKRotationWeight(goal, 1f);
        an.SetIKPosition(goal, p); an.SetIKRotation(goal, Quaternion.LookRotation(f.normalized, Vector3.up));
    }

    // ------------------------------------------------------------------ posture + gaze (after the animation)
    void LateUpdate()
    {
        if (hips && neck && spine.Length > 0)
        {
            var v = transform.InverseTransformDirection(neck.position - hips.position);
            float lean = Mathf.Atan2(v.z, v.y) * Mathf.Rad2Deg, sideLean = Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg;
            float dLean = Mathf.Clamp(lean, -maxLeanBack, maxLeanForward) - lean;
            float dSide = Mathf.Clamp(sideLean, -maxLeanSide, maxLeanSide) - sideLean;
            if (Mathf.Abs(dLean) > 0.05f || Mathf.Abs(dSide) > 0.05f)
                spine[0].rotation = Quaternion.AngleAxis(dLean, transform.right) * Quaternion.AngleAxis(-dSide, transform.forward) * spine[0].rotation;
        }
        if (!head || lookW < 0.001f) return;
        var local = transform.InverseTransformDirection(lookSmooth - head.position);
        float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        float pitch = -Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;   // + = down
        yaw = Mathf.Clamp(yaw, -(maxChestYaw + maxHeadYaw), maxChestYaw + maxHeadYaw);
        if (pitch < 0f) pitch *= 0.7f;      // looking up: the eyes do part of it
        pitch = Mathf.Clamp(pitch, -maxUp, maxDown);
        yawCur = Mathf.SmoothDampAngle(yawCur, yaw, ref yawVel, 0.30f);
        pitchCur = Mathf.SmoothDampAngle(pitchCur, pitch, ref pitchVel, 0.30f);

        if (spine.Length > 0)
        {
            float chest = Mathf.Clamp(yawCur * 0.26f, -maxChestYaw, maxChestYaw) * lookW;
            var q = Quaternion.AngleAxis(chest / spine.Length, Vector3.up);
            foreach (var b in spine) b.rotation = q * b.rotation;
        }
        var want = transform.rotation * (Quaternion.Euler(pitchCur, yawCur, 0f) * Vector3.forward);
        if (neck)
        {
            var d1 = Quaternion.FromToRotation(head.TransformDirection(headForwardLocal), want);
            neck.rotation = Quaternion.Slerp(Quaternion.identity, d1, 0.4f * lookW) * neck.rotation;
        }
        var d2 = Quaternion.FromToRotation(head.TransformDirection(headForwardLocal), want);
        head.rotation = Quaternion.Slerp(Quaternion.identity, d2, lookW) * head.rotation;
    }
}
