using System;
using UnityEngine;

// Wall clock showing the real time.
public class WallClock : MonoBehaviour
{
    public Transform hour, minute;
    public Vector3 faceNormal = Vector3.forward;   // world direction the clock face looks into the room
    public bool audible;
    Quaternion hRest, mRest; AudioSource src;

    void Awake() { if (hour) hRest = hour.rotation; if (minute) mRest = minute.rotation; }

    public void Setup(AudioClip tick)
    {
        if (!audible || tick == null) return;
        src = gameObject.AddComponent<AudioSource>(); src.clip = tick; src.loop = true; src.spatialBlend = 1f;
        src.minDistance = 0.6f; src.maxDistance = 8f; src.volume = 0.35f; src.Play();
    }

    void Update()
    {
        var now = DateTime.Now;
        float m = now.Minute + now.Second / 60f, h = (now.Hour % 12) + m / 60f;
        // clockwise as seen from the room = negative turn about the face normal (left-handed Unity)
        if (hour) hour.rotation = Quaternion.AngleAxis(h * 30f, faceNormal) * hRest;
        if (minute) minute.rotation = Quaternion.AngleAxis(m * 6f, faceNormal) * mRest;
    }
}
