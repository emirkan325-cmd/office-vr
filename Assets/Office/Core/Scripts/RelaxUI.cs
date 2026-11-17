using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

// Shared world-space UI building blocks (session panel, hub menu): same look everywhere,
// usable with controller rays, hand rays and poke.
public static class RelaxUI
{
    public static readonly Color PanelColor = new Color(0.12f, 0.1f, 0.09f, 0.82f);
    public static readonly Color TextColor = new Color(0.97f, 0.93f, 0.86f);
    public static readonly Color HintColor = new Color(0.85f, 0.8f, 0.72f);
    public static readonly Color Calm = new Color(0.45f, 0.62f, 0.5f);
    public static readonly Color Neutral = new Color(0.42f, 0.4f, 0.38f);

    static Font font;
    public static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

    public static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
    }

    // canvas in front of 'anchor' (eye), 1 unit = 0.75 mm
    public static Canvas Panel(string name, Transform anchor, float distance, float drop, Vector2 size, bool background = true)
    {
        EnsureEventSystem();
        var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster));
        var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = Camera.main;
        ((RectTransform)go.transform).sizeDelta = size;
        var fwd = Vector3.ProjectOnPlane(anchor.forward, Vector3.up).normalized;
        go.transform.position = anchor.position + fwd * distance + Vector3.down * drop;
        go.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        go.transform.localScale = Vector3.one * 0.00075f;
        if (background) { var bg = New("Background", go.transform, typeof(Image)); Stretch(bg); bg.GetComponent<Image>().color = PanelColor; }
        return canvas;
    }

    public static Text Label(Transform parent, string name, string text, Vector2 pos, Vector2 size, int fontSize, Color color, TextAnchor align = TextAnchor.MiddleCenter)
    {
        var t = New(name, parent, typeof(Text)).GetComponent<Text>();
        Place(t.rectTransform, pos, size);
        t.font = Font; t.fontSize = fontSize; t.alignment = align; t.color = color; t.text = text;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    public static Button Button(Transform parent, string name, string text, Vector2 pos, Vector2 size, Color color, Action onClick, int fontSize = 44)
    {
        var b = New(name, parent, typeof(Image), typeof(Button));
        Place((RectTransform)b.transform, pos, size);
        b.GetComponent<Image>().color = color;
        var btn = b.GetComponent<Button>();
        var cb = btn.colors; cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f); cb.pressedColor = new Color(0.8f, 0.8f, 0.8f); btn.colors = cb;
        btn.onClick.AddListener(() => onClick());
        if (!string.IsNullOrEmpty(text))
        {
            var l = Label(b.transform, "Label", text, Vector2.zero, size, fontSize, Color.white);
            Stretch(l.gameObject);
        }
        return btn;
    }

    public static GameObject New(string name, Transform parent, params Type[] comps)
    {
        var all = new Type[comps.Length + 1]; all[0] = typeof(RectTransform); comps.CopyTo(all, 1);
        var g = new GameObject(name, all); g.transform.SetParent(parent, false); return g;
    }
    public static void Place(RectTransform r, Vector2 pos, Vector2 size) { r.anchoredPosition = pos; r.sizeDelta = size; }
    public static void Stretch(GameObject g) { var r = (RectTransform)g.transform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
}
