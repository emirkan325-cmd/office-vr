using UnityEngine;

// Plays a given Animator state when the scene starts, with a FIXED per-object offset (derived from the name)
// so nobody moves in sync but every start looks the same.
public class StartState : MonoBehaviour
{
    public string state = "Sit";

    void Start()
    {
        var a = GetComponent<Animator>();
        if (!a || !a.HasState(0, Animator.StringToHash(state))) return;
        int h = 17; unchecked { foreach (var c in name) h = h * 31 + c; }
        a.Play(state, 0, (h & 0xffff) / 65535f);
    }
}
