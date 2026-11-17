using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

// The patient STANDS at the head of the table: turn the rig so the real "forward" faces the audience and
// slide it so the head is over the speaker spot. The real body height is kept (floor tracking);
// only without floor tracking (or in the editor simulator) the eye goes to a default height.
// Runs once tracking starts, again when the headset is recentered, and on the C key.
public class StandingCalibration : MonoBehaviour
{
    public XROrigin origin;
    public Transform speakerFloor;     // point on the stage floor at the head of the table, forward = toward the audience
    public float defaultEyeHeight = 1.65f;
    public Key recalibrateKey = Key.C;

    readonly List<XRInputSubsystem> inputs = new List<XRInputSubsystem>();

    IEnumerator Start()
    {
        if (origin == null) origin = GetComponent<XROrigin>();
        float t = 0f;
        while (t < 5f && origin.Camera.transform.localPosition.sqrMagnitude < 1e-6f) { t += Time.deltaTime; yield return null; }
        yield return null;
        Calibrate();
        SubsystemManager.GetSubsystems(inputs);
        foreach (var s in inputs) s.trackingOriginUpdated += OnRecenter;
    }

    void OnDestroy() { foreach (var s in inputs) s.trackingOriginUpdated -= OnRecenter; }
    void OnRecenter(XRInputSubsystem s) => StartCoroutine(CalibrateNextFrame());
    IEnumerator CalibrateNextFrame() { yield return null; Calibrate(); }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb[recalibrateKey].wasPressedThisFrame) Calibrate();
    }

    public void Calibrate()
    {
        if (origin == null || speakerFloor == null) return;
        var fwd = Vector3.ProjectOnPlane(speakerFloor.forward, Vector3.up).normalized;
        origin.MatchOriginUpCameraForward(Vector3.up, fwd);
        var cam = origin.Camera.transform;
        // head height above the real floor (floor tracking) or the default
        float h = cam.position.y - origin.transform.position.y;
        bool floorTracked = origin.CurrentTrackingOriginMode == TrackingOriginModeFlags.Floor && h > 0.9f && h < 2.3f;
        if (!floorTracked) h = defaultEyeHeight;
        origin.MoveCameraToWorldLocation(new Vector3(speakerFloor.position.x, speakerFloor.position.y + h, speakerFloor.position.z));
        Debug.Log($"[Office] calibration: eye at {cam.position:F2} (floor tracked {floorTracked})");
    }
}
