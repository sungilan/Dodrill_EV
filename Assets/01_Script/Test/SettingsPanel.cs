using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using XRAirpotrSecurity;

// ============================================================
//  SettingsPanel.cs
//
//  (11/19) PC·모바일 '환경설정' 창 — 오른쪽 위 메뉴 줄의 설정(톱니) 버튼(PcHudLayout)으로 열고 닫는다. Esc 로도 닫힌다.
//  여기저기 흩어져 있던 켜고 끄기를 한곳에 모은다(값은 각 기능이 원래 쓰던 저장값을 그대로 쓴다).
//   · 전체 음량          AudioListener.volume            (opt.volume)
//   · 시선 감도(PC)       FreeLookController.lookSensitivity (opt.look)
//   · 단계 설명 팝업      GuideSystem.CenterPopupEnabled  (ev_step_popup)
//   · 집중 모드           FocusMode.Enabled                (focusmode_off_v2)
//   · 미니맵 / 키보드 안내 PcHudLayout                      (hud.minimap / hud.keyboard)
//  씬 배치 불필요 — 처음 열 때 화면 UI 를 코드로 만든다. VR 은 손바닥 메뉴를 쓰므로 만들지 않는다.
// ============================================================
public class SettingsPanel : MonoBehaviour
{
    private const string PrefVolume = "opt.volume", PrefLook = "opt.look";
    private const float LookMin = 0.5f, LookMax = 5f, LookDefault = 2f;

    private static SettingsPanel s_inst;
    private GameObject _canvas, _root;
    private readonly List<Action> _refreshers = new List<Action>();
    private float _nextApply;

    public static bool IsOpen => s_inst != null && s_inst._root != null && s_inst._root.activeSelf;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isBatchMode || FindAnyObjectByType<SettingsPanel>() != null) return;
        var go = new GameObject("[SettingsPanel]");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        s_inst = go.AddComponent<SettingsPanel>();
        // 저장해 둔 음량은 시작할 때 한 번 적용한다
        try { if (PlayerPrefs.HasKey(PrefVolume)) AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefVolume, 1f)); } catch { }
    }

    /// <summary>메뉴 줄의 설정 버튼이 부른다.</summary>
    public static void Toggle()
    {
        if (s_inst == null) return;
        s_inst.SetOpen(!IsOpen);
    }

    private void Update()
    {
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) SetOpen(false);
        // 시선 감도는 플레이어가 나중에 생기므로(씬 이동·재접속) 1초마다 맞춘다
        if (Time.unscaledTime >= _nextApply)
        {
            _nextApply = Time.unscaledTime + 1f;
            if (PlayerPrefs.HasKey(PrefLook)) ApplyLook(PlayerPrefs.GetFloat(PrefLook, LookDefault));
        }
    }

    private void SetOpen(bool open)
    {
        if (open && _canvas == null) Build();
        if (_root == null) return;
        _root.SetActive(open);
        if (open) foreach (var r in _refreshers) r();
    }

    private static void ApplyLook(float v)
    {
        foreach (var c in FindObjectsByType<FreeLookController>(FindObjectsSortMode.None)) c.lookSensitivity = v;
        foreach (var c in FindObjectsByType<FreeLookControllerLocal>(FindObjectsSortMode.None)) c.lookSensitivity = v;
    }

    // ── UI ──
    private const float W = 620f, RowH = 64f;

    private void Build()
    {
        bool mobile = Utils.GetPlatformType() == PlatformType.Mobile;

        _canvas = new GameObject("[SettingsPanel_Canvas]");
        DontDestroyOnLoad(_canvas);
        var cv = _canvas.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 75;
        var sc = _canvas.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920f, 1080f); sc.matchWidthOrHeight = 0.5f;
        _canvas.AddComponent<GraphicRaycaster>();
        if (EventSystem.current == null) _canvas.AddComponent<EventSystem>();

        var root = NewRect("Root", _canvas.transform);
        Stretch(root);
        _root = root.gameObject;
        // 화면 전체를 살짝 어둡게 — 바깥을 누르면 닫히고, 뒤의 물건이 잡히지 않는다.
        // 판(Panel)은 어둡게 깐 판의 자식이 아니라 형제로 둔다 — 자식이면 판 위 클릭이 부모 버튼까지 올라가 창이 닫혔다
        var dim = NewRect("Dim", root);
        Stretch(dim);
        var dimImg = dim.gameObject.AddComponent<Image>(); dimImg.color = new Color(0f, 0f, 0f, 0.45f);
        dim.gameObject.AddComponent<Button>().onClick.AddListener(() => SetOpen(false));

        // 줄 목록
        var rows = new List<Action<RectTransform>>();
        rows.Add(p => SliderRow(p, "전체 음량", 0f, 1f,
            () => AudioListener.volume,
            v => { AudioListener.volume = v; PlayerPrefs.SetFloat(PrefVolume, v); },
            v => Mathf.RoundToInt(v * 100f) + "%"));
        if (!mobile)
            rows.Add(p => SliderRow(p, "시선 감도", LookMin, LookMax,
                () => PlayerPrefs.GetFloat(PrefLook, LookDefault),
                v => { PlayerPrefs.SetFloat(PrefLook, v); ApplyLook(v); },
                v => v.ToString("0.0")));
        rows.Add(p => ToggleRow(p, "단계 설명 팝업", "새 단계가 시작될 때 가운데 설명 창", () => GuideSystem.CenterPopupEnabled, GuideSystem.SetCenterPopupEnabled));
        rows.Add(p => ToggleRow(p, "집중 모드", "작업 대상을 가리는 차체를 반투명하게", () => FocusMode.Enabled, v => FocusMode.Enabled = v));
        rows.Add(p => ToggleRow(p, "미니맵", "왼쪽 위 지도", () => PcHudLayout.MapOn, v => PcHudLayout.MapOn = v));
        if (!mobile)
            rows.Add(p => ToggleRow(p, "키보드 안내", "왼쪽 아래 조작 키 그림", () => PcHudLayout.KeysOn, v => PcHudLayout.KeysOn = v));

        float h = 84f + rows.Count * RowH + 24f;
        var panel = NewRect("Panel", root);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(W, h);
        var bg = panel.gameObject.AddComponent<Image>();
        bg.sprite = WorldHoverLabel.RoundedSprite(); bg.type = Image.Type.Sliced;
        bg.color = new Color(0.06f, 0.08f, 0.12f, 0.97f);   // 판 위를 눌러도 닫히지 않게(레이캐스트 받음)

        var title = NewText(panel, "<b>환경설정</b>", 30f, TextAlignmentOptions.Left);
        Place(title.rectTransform, new Vector2(28f, -22f), new Vector2(W - 120f, 44f));

        var close = NewRect("Close", panel);
        close.anchorMin = close.anchorMax = close.pivot = new Vector2(1f, 1f);
        close.anchoredPosition = new Vector2(-16f, -16f); close.sizeDelta = new Vector2(52f, 52f);
        var cimg = close.gameObject.AddComponent<Image>(); cimg.sprite = WorldHoverLabel.RoundedSprite(); cimg.type = Image.Type.Sliced;
        cimg.color = new Color(0.16f, 0.2f, 0.27f, 1f);
        close.gameObject.AddComponent<Button>().onClick.AddListener(() => SetOpen(false));
        var ct = NewText(close, "<b>X</b>", 24f, TextAlignmentOptions.Center); Stretch(ct.rectTransform);

        var line = NewRect("Line", panel);
        Place(line, new Vector2(24f, -78f), new Vector2(W - 48f, 2f));
        line.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);

        for (int i = 0; i < rows.Count; i++)
        {
            var row = NewRect("Row" + i, panel);
            Place(row, new Vector2(28f, -88f - i * RowH), new Vector2(W - 56f, RowH));
            rows[i](row);
        }
        _root.SetActive(false);
    }

    private void ToggleRow(RectTransform row, string label, string hint, Func<bool> get, Action<bool> set)
    {
        var t = NewText(row, $"<b>{label}</b>\n<size=70%><color=#9AA6B5>{hint}</color></size>", 22f, TextAlignmentOptions.Left);
        Place(t.rectTransform, Vector2.zero, new Vector2(W - 220f, RowH));

        // 켜짐/꺼짐 스위치
        var sw = NewRect("Switch", row);
        sw.anchorMin = sw.anchorMax = sw.pivot = new Vector2(1f, 0.5f);
        sw.anchoredPosition = Vector2.zero; sw.sizeDelta = new Vector2(110f, 44f);
        var img = sw.gameObject.AddComponent<Image>(); img.sprite = WorldHoverLabel.RoundedSprite(); img.type = Image.Type.Sliced;
        var knob = NewRect("Knob", sw);
        knob.anchorMin = knob.anchorMax = knob.pivot = new Vector2(0f, 0.5f); knob.sizeDelta = new Vector2(36f, 36f);
        var kimg = knob.gameObject.AddComponent<Image>(); kimg.sprite = HvStatusHud.MakeCircle(false); kimg.raycastTarget = false;
        var st = NewText(sw, "", 17f, TextAlignmentOptions.Center); Stretch(st.rectTransform);

        Action refresh = () =>
        {
            bool on = get();
            img.color = on ? new Color(0.13f, 0.55f, 0.95f, 1f) : new Color(0.25f, 0.28f, 0.33f, 1f);
            knob.anchoredPosition = new Vector2(on ? 70f : 4f, 0f);
            st.text = on ? "<b>켜짐</b>   " : "   <b>꺼짐</b>";
            st.alignment = on ? TextAlignmentOptions.Left : TextAlignmentOptions.Right;
            st.margin = new Vector4(12f, 0f, 12f, 0f);
        };
        sw.gameObject.AddComponent<Button>().onClick.AddListener(() => { set(!get()); try { PlayerPrefs.Save(); } catch { } refresh(); });
        _refreshers.Add(refresh);
        refresh();
    }

    private void SliderRow(RectTransform row, string label, float min, float max, Func<float> get, Action<float> set, Func<float, string> fmt)
    {
        var t = NewText(row, $"<b>{label}</b>", 22f, TextAlignmentOptions.Left);
        Place(t.rectTransform, Vector2.zero, new Vector2(170f, RowH));
        var val = NewText(row, "", 20f, TextAlignmentOptions.Right);
        val.rectTransform.anchorMin = val.rectTransform.anchorMax = val.rectTransform.pivot = new Vector2(1f, 0.5f);
        val.rectTransform.anchoredPosition = Vector2.zero; val.rectTransform.sizeDelta = new Vector2(70f, RowH);

        var sr = NewRect("Slider", row);
        sr.anchorMin = sr.anchorMax = new Vector2(0f, 0.5f); sr.pivot = new Vector2(0f, 0.5f);
        sr.anchoredPosition = new Vector2(180f, 0f); sr.sizeDelta = new Vector2(W - 56f - 180f - 86f, 28f);
        var track = NewRect("Track", sr); Stretch(track);
        track.offsetMin = new Vector2(0f, 9f); track.offsetMax = new Vector2(0f, -9f);
        var ti = track.gameObject.AddComponent<Image>(); ti.sprite = WorldHoverLabel.RoundedSprite(); ti.type = Image.Type.Sliced; ti.color = new Color(0.22f, 0.25f, 0.3f, 1f);
        var fillArea = NewRect("FillArea", track); Stretch(fillArea);
        var fill = NewRect("Fill", fillArea); Stretch(fill);
        var fi = fill.gameObject.AddComponent<Image>(); fi.sprite = WorldHoverLabel.RoundedSprite(); fi.type = Image.Type.Sliced; fi.color = new Color(0.13f, 0.55f, 0.95f, 1f);
        var handleArea = NewRect("HandleArea", sr); Stretch(handleArea);
        var handle = NewRect("Handle", handleArea); handle.sizeDelta = new Vector2(28f, 0f);
        var hi = handle.gameObject.AddComponent<Image>(); hi.sprite = HvStatusHud.MakeCircle(false); hi.color = Color.white;

        var s = sr.gameObject.AddComponent<Slider>();
        s.fillRect = fill; s.handleRect = handle; s.targetGraphic = hi;
        s.direction = Slider.Direction.LeftToRight; s.minValue = min; s.maxValue = max;
        bool init = true;
        s.onValueChanged.AddListener(v => { val.text = fmt(v); if (!init) set(v); });
        Action refresh = () => { init = true; s.value = Mathf.Clamp(get(), min, max); val.text = fmt(s.value); init = false; };
        _refreshers.Add(refresh);
        refresh();
    }

    // ── 유틸 ──
    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    private static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
    }

    private static void Place(RectTransform r, Vector2 topLeft, Vector2 size)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = topLeft; r.sizeDelta = size;
    }

    private static TMP_Text NewText(Transform parent, string text, float size, TextAlignmentOptions align)
    {
        var rt = NewRect("Text", parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        var kf = MobileWorkButton.KoreanFont();
        if (kf != null) t.font = kf;
        t.text = text; t.fontSize = size; t.alignment = align; t.color = Color.white;
        t.richText = true; t.raycastTarget = false; t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }
}
