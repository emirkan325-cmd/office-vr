using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Company.License
{
    // Lisans gecersizken gosterilen, tasarim gerektirmeyen panel. Prefab yok; tamamen
    // koddan olusturulur. Iki modu var:
    //  - Mesaj modu: sadece uyari; panel kafayi takip eder (kullanici kacamaz).
    //  - Aktivasyon modu: uygulama icinden lisans anahtari girilir.
    //
    // Aktivasyon modunda Quest'in sistem klavyesi yerine panelin icinde kendi tus
    // takimimiz var: anahtar formati sabit (LIC-XXXXXXXX-XXXXXXXX, yalnizca 0-9 A-F),
    // yani 16 tus yeterli. Sistem klavyesi OpenXR/el takibinde guvenilir acilmiyor ve
    // bazi projelerde XR Interaction Toolkit yok; bu yontem hicbir pakete bagli degil.
    //
    // Etkilesim: kontrolcuyle isaret + tetik. Kontrolcu yoksa (el takibi) tusa bakmak
    // yeterli -- ~1 sn bakinca basilir.
    public class SimpleLockScreen : MonoBehaviour
    {
        private const float Distance = 1.5f;
        private const float VerticalOffset = -0.1f;
        private const float Scale = 0.0011f;
        private const int WarmupFrames = 20;
        private const float DwellSeconds = 1.1f;
        private const int KeyLength = 16;
        private const float ReplaceAngle = 55f;

        private static readonly InputFeatureUsage<Vector3> PointerPosition = new InputFeatureUsage<Vector3>("PointerPosition");
        private static readonly InputFeatureUsage<Quaternion> PointerRotation = new InputFeatureUsage<Quaternion>("PointerRotation");

        private static readonly Color BgColor = new Color(0.05f, 0.05f, 0.07f, 0.97f);
        private static readonly Color KeyColor = new Color(0.17f, 0.19f, 0.25f, 1f);
        private static readonly Color KeyHover = new Color(0.22f, 0.42f, 0.88f, 1f);
        private static readonly Color OkColor = new Color(0.12f, 0.45f, 0.25f, 1f);
        private static readonly Color OkHover = new Color(0.18f, 0.62f, 0.34f, 1f);

        private class KeyButton
        {
            public RectTransform Rect;
            public Image Bg;
            public string Value;
            public bool IsOk;
        }

        private Canvas _canvas;
        private RectTransform _root;
        private Text _title;
        private Text _keyText;
        private Text _status;
        private GameObject _keypad;
        private Image _cursor;
        private readonly List<KeyButton> _keys = new List<KeyButton>();
        private readonly List<InputDevice> _devices = new List<InputDevice>();

        private bool _following;
        private bool _snapped;
        private bool _entryMode;
        private bool _busy;
        private string _digits = "";
        private Action<string> _onSubmit;

        private KeyButton _hovered;
        private float _dwell;
        private bool _prevTrigger;

        public static SimpleLockScreen Create()
        {
            var go = new GameObject("LicenseLockScreen");
            return go.AddComponent<SimpleLockScreen>();
        }

        private void Awake()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 32000;
            gameObject.AddComponent<CanvasScaler>();

            _root = (RectTransform)transform;
            _root.localScale = Vector3.one * Scale;

            var bg = MakeImage(_root, "Background", BgColor);
            Stretch(bg.rectTransform);

            _title = MakeText(_root, "Message", 34, TextAnchor.MiddleCenter);
            _keyText = MakeText(_root, "KeyDisplay", 54, TextAnchor.MiddleCenter);
            _keyText.color = new Color(0.85f, 0.92f, 1f);
            _status = MakeText(_root, "Status", 28, TextAnchor.MiddleCenter);

            _keypad = new GameObject("Keypad", typeof(RectTransform));
            var keypadRt = (RectTransform)_keypad.transform;
            keypadRt.SetParent(_root, false);
            keypadRt.sizeDelta = Vector2.zero;

            string hex = "0123456789ABCDEF";
            for (int i = 0; i < 16; i++)
            {
                int row = i / 4, col = i % 4;
                AddKey(keypadRt, hex[i].ToString(), hex[i].ToString(),
                    -249f + col * 166f, 150f - row * 106f, 150f, 90f, false);
            }
            AddKey(keypadRt, LicenseText.T("ZURÜCK", "SIL", "BACK"), "DEL", -300f, -290f, 200f, 90f, false);
            AddKey(keypadRt, LicenseText.T("LEEREN", "TEMIZLE", "CLEAR"), "CLR", -80f, -290f, 200f, 90f, false);
            AddKey(keypadRt, LicenseText.T("AKTIVIEREN", "AKTIVE ET", "ACTIVATE"), "OK", 220f, -290f, 340f, 90f, true);

            _cursor = MakeImage(_root, "Cursor", new Color(1f, 1f, 1f, 0.9f));
            _cursor.rectTransform.sizeDelta = new Vector2(22f, 22f);
            _cursor.raycastTarget = false;
            _cursor.gameObject.SetActive(false);
        }

        // --- Disa acik API ---

        public void Show(string message)
        {
            _entryMode = false;
            _onSubmit = null;
            LayoutMessageMode();
            _title.text = message;
            Begin();
        }

        public void ShowActivation(string message, Action<string> onSubmit)
        {
            _entryMode = true;
            _onSubmit = onSubmit;
            _busy = false;
            LayoutEntryMode();
            _title.text = message;
            SetStatus("", false);
            RefreshKeyText();
            Begin();
        }

        public void SetStatus(string message, bool busy)
        {
            _busy = busy;
            if (_status != null) _status.text = message;
        }

        // Programatik giris (gelistirici araclari/test icin). Tam anahtari alir.
        public void EnterKey(string fullKey)
        {
            if (string.IsNullOrEmpty(fullKey)) return;
            string cleaned = fullKey.ToUpperInvariant().Replace("LIC-", "").Replace("-", "");
            _digits = cleaned.Length > KeyLength ? cleaned.Substring(0, KeyLength) : cleaned;
            RefreshKeyText();
            Submit();
        }

        public void Hide()
        {
            StopAllCoroutines();
            _following = false;
            _entryMode = false;
            _cursor.gameObject.SetActive(false);
            gameObject.SetActive(false);
        }

        // --- Yerlesim ---

        private void Begin()
        {
            gameObject.SetActive(true);
            StopAllCoroutines();
            StartCoroutine(PlaceInFrontOfUser());
        }

        private IEnumerator PlaceInFrontOfUser()
        {
            // Ilk karelerde XR kamera pozu henuz gecerli olmuyor; o sure boyunca paneli
            // gosterme (yoksa sahnenin ortasinda bir an belirip kayar).
            _canvas.enabled = false;
            _snapped = false;
            _following = false;
            for (int i = 0; i < WarmupFrames; i++) yield return null;
            _following = true;
            LateUpdate();
            _canvas.enabled = true;
        }

        private void LateUpdate()
        {
            if (!_following) return;
            var cam = Camera.main;
            if (cam == null) return;
            Transform head = cam.transform;

            if (_entryMode)
            {
                // Aktivasyon modunda panel sabit durur (yoksa bakisla tus secilemez).
                // Kullanici cok uzaga donerse tekrar onune alinir.
                Vector3 toPanel = transform.position - head.position;
                bool needPlace = !_snapped || Vector3.Angle(head.forward, toPanel) > ReplaceAngle;
                if (needPlace) PlaceStatic(head);
                return;
            }

            // Mesaj modu: kafayi takip et, kullanici kilidin arkasina bakamasin.
            Vector3 targetPos = head.position + head.forward * Distance + head.up * VerticalOffset;
            Quaternion targetRot = Quaternion.LookRotation(head.forward, head.up);
            float t = _snapped ? 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime) : 1f;
            _snapped = true;
            transform.position = Vector3.Lerp(transform.position, targetPos, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, t);
        }

        private void PlaceStatic(Transform head)
        {
            Vector3 forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();
            transform.position = head.position + forward * Distance + Vector3.up * VerticalOffset;
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            _snapped = true;
        }

        // --- Giris ---

        private void Update()
        {
            if (!_entryMode || !_following || !_canvas.enabled) return;

            GetPointer(out Ray ray, out bool pressed, out bool isController);

            KeyButton hover = null;
            var plane = new Plane(transform.forward, transform.position);
            if (plane.Raycast(ray, out float enter))
            {
                Vector3 hit = ray.GetPoint(enter);
                _cursor.gameObject.SetActive(true);
                _cursor.rectTransform.position = hit - transform.forward * 0.002f;
                _cursor.rectTransform.rotation = transform.rotation;

                foreach (var k in _keys)
                {
                    Vector3 local = k.Rect.InverseTransformPoint(hit);
                    if (k.Rect.rect.Contains(new Vector2(local.x, local.y))) { hover = k; break; }
                }
            }
            else
            {
                _cursor.gameObject.SetActive(false);
            }

            if (hover != _hovered) { _hovered = hover; _dwell = 0f; }

            if (isController)
            {
                if (pressed && !_prevTrigger && _hovered != null) Press(_hovered);
                _dwell = 0f;
            }
            else if (_hovered != null)
            {
                // Bakarak secim (el takibi / kontrolcu yok). Unscaled: oyun duraklatilmis olabilir.
                _dwell += Time.unscaledDeltaTime;
                if (_dwell >= DwellSeconds)
                {
                    Press(_hovered);
                    _dwell = -0.4f; // ayni tusa tekrar basmadan once kisa bekleme
                }
            }
            _prevTrigger = pressed;

            foreach (var k in _keys)
            {
                Color normal = k.IsOk ? OkColor : KeyColor;
                Color hi = k.IsOk ? OkHover : KeyHover;
                if (k != _hovered) { k.Bg.color = normal; continue; }
                float p = isController ? 1f : Mathf.Clamp01(_dwell / DwellSeconds);
                k.Bg.color = Color.Lerp(normal, hi, isController ? 1f : 0.35f + 0.65f * p);
            }
        }

        // Once tracking'li bir kontrolcu arar; yoksa bakis yonunu kullanir.
        private void GetPointer(out Ray ray, out bool pressed, out bool isController)
        {
            pressed = false;
            isController = false;
            var cam = Camera.main;
            Transform trackingSpace = cam != null ? cam.transform.parent : null;

            foreach (var side in new[] { InputDeviceCharacteristics.Right, InputDeviceCharacteristics.Left })
            {
                _devices.Clear();
                InputDevices.GetDevicesWithCharacteristics(
                    InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.TrackedDevice | side, _devices);

                foreach (var d in _devices)
                {
                    if (!d.isValid) continue;
                    if (d.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && !tracked) continue;

                    if (!(d.TryGetFeatureValue(PointerPosition, out Vector3 pos) && d.TryGetFeatureValue(PointerRotation, out Quaternion rot)))
                    {
                        if (!d.TryGetFeatureValue(CommonUsages.devicePosition, out pos)) continue;
                        if (!d.TryGetFeatureValue(CommonUsages.deviceRotation, out rot)) continue;
                    }

                    Vector3 worldPos = trackingSpace != null ? trackingSpace.TransformPoint(pos) : pos;
                    Quaternion worldRot = trackingSpace != null ? trackingSpace.rotation * rot : rot;
                    ray = new Ray(worldPos, worldRot * Vector3.forward);
                    d.TryGetFeatureValue(CommonUsages.triggerButton, out pressed);
                    isController = true;
                    return;
                }
            }

            ray = cam != null
                ? new Ray(cam.transform.position, cam.transform.forward)
                : new Ray(Vector3.zero, Vector3.forward);
        }

        private void Press(KeyButton key)
        {
            if (_busy) return;
            switch (key.Value)
            {
                case "DEL":
                    if (_digits.Length > 0) _digits = _digits.Substring(0, _digits.Length - 1);
                    break;
                case "CLR":
                    _digits = "";
                    break;
                case "OK":
                    Submit();
                    return;
                default:
                    if (_digits.Length < KeyLength) _digits += key.Value;
                    break;
            }
            SetStatus("", false);
            RefreshKeyText();
        }

        private void Submit()
        {
            if (_busy || _onSubmit == null) return;
            if (_digits.Length != KeyLength)
            {
                SetStatus(LicenseText.T($"Der Schlüssel muss {KeyLength} Zeichen haben (jetzt {_digits.Length}).",
                                        $"Anahtar {KeyLength} karakter olmali (su an {_digits.Length}).",
                                        $"The key must have {KeyLength} characters (now {_digits.Length})."), false);
                return;
            }
            _onSubmit("LIC-" + _digits.Substring(0, 8) + "-" + _digits.Substring(8, 8));
        }

        private void RefreshKeyText()
        {
            string a = _digits.Length > 8 ? _digits.Substring(0, 8) : _digits;
            string b = _digits.Length > 8 ? _digits.Substring(8) : "";
            _keyText.text = "LIC-" + a.PadRight(8, '_') + "-" + b.PadRight(8, '_');
        }

        // --- Duzenler ---

        private void LayoutMessageMode()
        {
            _root.sizeDelta = new Vector2(1000f, 420f);
            Place(_title.rectTransform, 0f, 0f, 880f, 300f);
            _keyText.gameObject.SetActive(false);
            _status.gameObject.SetActive(false);
            _keypad.SetActive(false);
            _cursor.gameObject.SetActive(false);
        }

        private void LayoutEntryMode()
        {
            _root.sizeDelta = new Vector2(1000f, 880f);
            Place(_title.rectTransform, 0f, 360f, 900f, 130f);
            Place(_keyText.rectTransform, 0f, 255f, 900f, 90f);
            Place(_status.rectTransform, 0f, -385f, 900f, 60f);
            _keyText.gameObject.SetActive(true);
            _status.gameObject.SetActive(true);
            _keypad.SetActive(true);
        }

        // --- UI yardimcilari ---

        private void AddKey(RectTransform parent, string label, string value, float x, float y, float w, float h, bool isOk)
        {
            var img = MakeImage(parent, "Key_" + value, isOk ? OkColor : KeyColor);
            Place(img.rectTransform, x, y, w, h);
            var txt = MakeText(img.rectTransform, "Label", 32, TextAnchor.MiddleCenter);
            Stretch(txt.rectTransform);
            txt.text = label;
            txt.raycastTarget = false;
            _keys.Add(new KeyButton { Rect = img.rectTransform, Bg = img, Value = value, IsOk = isOk });
        }

        private static Image MakeImage(RectTransform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private static Text MakeText(RectTransform parent, string name, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = size;
            t.alignment = anchor;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
