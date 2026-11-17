using System.Collections;
using UnityEngine;
using UnityEngine.XR;

// Editor/desktop testing: turns on the XR Interaction Simulator (keyboard + mouse drive the
// head and controllers) only when no headset is running. Never active in a Quest build.
public class SimulatorWhenNoHeadset : MonoBehaviour
{
    public GameObject simulator;

    IEnumerator Start()
    {
        if (!Application.isEditor || simulator == null) yield break;
        yield return null; yield return null;   // give the XR loader a couple of frames
        if (!XRSettings.isDeviceActive) simulator.SetActive(true);
    }
}
