using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using XRAirpotrSecurity;

// ============================================================
//  PcToolBeltBar.cs — PC·모바일 화면 공구 벨트 (11/19)
//
//  B 키 허리 벨트(FreeLookController, 2칸)를 화면에 칸으로 보여 준다 — 무엇이 꽂혀 있는지 눈에 안 보여 잊기 쉬웠다.
//  · 칸에는 꽂힌 공구 이름. 도구를 들고 있으면 칸 테두리가 파랗게 — '누르면 넣기'.
//  · 칸을 누르면: 들고 있으면 그 칸에 넣기(차 있으면 바꿔 들기), 빈손이면 그 칸 것을 꺼내 들기.
//  · PC 는 B 키도 그대로(마지막에 넣은 것 기준). 모바일은 이 칸이 유일한 벨트 조작이다.
//  자리: PC 왼쪽 아래 / 모바일 왼쪽, 조이스틱 위. VR 은 허리 벨트(VrToolBelt)가 있어 만들지 않는다.
// ============================================================
public class PcToolBeltBar : MonoBehaviour
{
    private const float Slot = 104f, Gap = 10f;
    private static readonly Color SlotBg = new Color(0.07f, 0.09f, 0.13f, 0.86f);
    private static readonly Color SlotFull = new Color(0.1f, 0.2f, 0.3f, 0.92f);
    private static readonly Color Ring = new Color(1f, 1f, 1f, 0.16f);
    private static readonly Color RingReady = new Color(0.25f, 0.65f, 1f, 1f);

    private GameObject _canvas;
    private RectTransform _bar;
    private Image[] _bg, _ring;
    private TMP_Text[] _name;
    private TMP_Text _hint;
    private float _next;
    private string _sig;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isBatchMode || FindAnyObjectByType<PcToolBeltBar>() != null) return;
        var go = new GameObject("[PcToolBeltBar]");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        go.AddComponent<PcToolBeltBar>();
    }

    private void OnDestroy() { if (_canvas != null) Destroy(_canvas); }

    private void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.1f;

        var fl = FreeLookController.Local;
        bool show = fl != null && fl.isActiveAndEnabled && Utils.GetPlatformType() != PlatformType.VR && !GameScenePlatformManager.IsVR;
        if (!show) { if (_canvas != null && _canvas.activeSelf) _canvas.SetActive(false); return; }
        if (_canvas == null) Build();
        if (!_canvas.activeSelf) _canvas.SetActive(true);

        var held = fl.HeldItem;
        bool holding = held != null;
        var sb = new System.Text.StringBuilder(holding ? "H" : "-");
        for (int i = 0; i < _bg.Length; i++)
        {
            var it = fl.BeltItemAt(i);
            sb.Append('|').Append(it != null ? it.GetInstanceID() : 0);
        }
        string sig = sb.ToString();
        if (sig == _sig) return;
        _sig = sig;

        bool mobile = Utils.GetPlatformType() == PlatformType.Mobile;
        for (int i = 0; i < _bg.Length; i++)
        {
            var it = fl.BeltItemAt(i);
            _bg[i].color = it != null ? SlotFull : SlotBg;
            _ring[i].color = holding ? RingReady : Ring;
            _name[i].text = it != null ? $"<b>{FreeLookController.BeltItemName(it)}</b>" : "<color=#7F8896>비어 있음</color>";
        }
        _hint.text = holding
            ? "<b>칸을 눌러 벨트에 넣기</b>"
            : mobile ? "벨트 — 칸을 눌러 꺼내기" : "벨트 <color=#8FD3FF>B</color> · 칸을 눌러 꺼내기";
    }

    private void Build()
    {
        bool mobile = Utils.GetPlatformType() == PlatformType.Mobile;
        _canvas = new GameObject("[PcToolBeltBar_Canvas]");
        DontDestroyOnLoad(_canvas);
        var cv = _canvas.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 45;
        var sc = _canvas.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920f, 1080f); sc.matchWidthOrHeight = 0.5f;
        _canvas.AddComponent<GraphicRaycaster>();
        if (EventSystem.current == null) _canvas.AddComponent<EventSystem>();

        int n = FreeLookController.BeltSlotCount;
        float w = n * Slot + (n - 1) * Gap;
        _bar = NewRect("Bar", _canvas.transform);
        _bar.anchorMin = _bar.anchorMax = _bar.pivot = new Vector2(0f, 0f);
        _bar.anchoredPosition = mobile ? new Vector2(40f, 430f) : new Vector2(32f, 32f);
        _bar.sizeDelta = new Vector2(w, Slot + 34f);

        _hint = NewText(_bar, "", 17f, TextAlignmentOptions.Left);
        var hr = _hint.rectTransform;
        hr.anchorMin = hr.anchorMax = hr.pivot = new Vector2(0f, 1f);
        hr.anchoredPosition = new Vector2(4f, 0f); hr.sizeDelta = new Vector2(w + 120f, 28f);

        _bg = new Image[n]; _ring = new Image[n]; _name = new TMP_Text[n];
        for (int i = 0; i < n; i++)
        {
            var s = NewRect("Slot" + i, _bar);
            s.anchorMin = s.anchorMax = s.pivot = new Vector2(0f, 0f);
            s.anchoredPosition = new Vector2(i * (Slot + Gap), 0f); s.sizeDelta = new Vector2(Slot, Slot);

            var ring = s.gameObject.AddComponent<Image>();
            ring.sprite = WorldHoverLabel.RoundedSprite(); ring.type = Image.Type.Sliced; ring.color = Ring;
            _ring[i] = ring;
            int k = i;
            s.gameObject.AddComponent<Button>().onClick.AddListener(() => FreeLookController.Local?.PressBeltSlot(k));

            var inner = NewRect("Bg", s);
            inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one; inner.offsetMin = new Vector2(3f, 3f); inner.offsetMax = new Vector2(-3f, -3f);
            var bg = inner.gameObject.AddComponent<Image>();
            bg.sprite = WorldHoverLabel.RoundedSprite(); bg.type = Image.Type.Sliced; bg.color = SlotBg; bg.raycastTarget = false;
            _bg[i] = bg;

            var num = NewText(s, $"<b>{i + 1}</b>", 16f, TextAlignmentOptions.TopLeft);
            num.color = new Color(1f, 1f, 1f, 0.55f);
            var nr = num.rectTransform; nr.anchorMin = Vector2.zero; nr.anchorMax = Vector2.one; nr.offsetMin = new Vector2(10f, 6f); nr.offsetMax = new Vector2(-6f, -6f);

            var name = NewText(s, "", 16f, TextAlignmentOptions.Center);
            name.textWrappingMode = TextWrappingModes.Normal;
            name.overflowMode = TextOverflowModes.Ellipsis;
            name.enableAutoSizing = true; name.fontSizeMin = 11f; name.fontSizeMax = 17f;
            var r = name.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(8f, 8f); r.offsetMax = new Vector2(-8f, -22f);
            _name[i] = name;
        }
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    private static TMP_Text NewText(Transform parent, string text, float size, TextAlignmentOptions align)
    {
        var rt = NewRect("Text", parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        var kf = MobileWorkButton.KoreanFont();
        if (kf != null) t.font = kf;
        t.text = text; t.fontSize = size; t.alignment = align; t.color = Color.white;
        t.richText = true; t.raycastTarget = false; t.textWrappingMode = TextWrappingModes.NoWrap;
        t.outlineWidth = 0.15f; t.outlineColor = new Color32(0, 0, 0, 200);
        return t;
    }
}
