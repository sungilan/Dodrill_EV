using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XRAirpotrSecurity;

// ============================================================
//  MobileMeterReadout.cs
//
//  모바일·PC — 멀티미터·절연저항계 측정값 확대 표시. (VR 은 손에 든 기기를 직접 보므로 띄우지 않는다)
//  폰 화면에서는 기기 LCD 글씨가 너무 작아 값을 읽을 수 없었다.
//  PC(11/18): 단자를 화면 가운데로 겨누려고 다가가면 들고 있는 기기가 배터리 팩 메시에 묻혀 계기판이 안 보였다 → PC 에도 띄운다.
//  (공군 프로젝트 모바일 오실로스코프 확대 패널과 같은 방식)
//
//  · 내가 계측기나 그 프로브를 들고 있거나, 두 프로브가 단자에 닿아 있으면 화면 위쪽에 패널을 띄운다.
//  · 기기 LCD 텍스트(lcdDisplay)·모드(modeDisplay)를 그대로 옮겨 크게 보여 준다. 값 계산은 건드리지 않는다.
//  · 결함 상태(기기 빨간 화면)면 패널도 빨갛게.
//  · 패널을 누르면 작게 접히고, 다시 누르면 펼쳐진다.
//  씬 배치 불필요 — MultimeterMaster.Awake 에서 등록하면 자동 생성.
// ============================================================
public class MobileMeterReadout : MonoBehaviour
{
    private static readonly List<MultimeterMaster> s_meters = new List<MultimeterMaster>();
    private static MobileMeterReadout s_inst;

    public static void Register(MultimeterMaster m)
    {
        if (m == null || s_meters.Contains(m)) return;
        s_meters.Add(m);
        if (s_inst == null && !Application.isBatchMode)   // 플랫폼은 Game 씬에서 늦게 정해질 수 있어 만들어 두고 LateUpdate 에서 VR 이면 숨긴다
        {
            var go = new GameObject("[MobileMeterReadout]");
            s_inst = go.AddComponent<MobileMeterReadout>();
        }
    }

    public static void Unregister(MultimeterMaster m) => s_meters.Remove(m);

    private static bool IsMobile =>
        Application.isMobilePlatform || GameScenePlatformManager.CurrentPlatform == PlatformType.Mobile;

    private static bool IsVR => !Application.isMobilePlatform && GameScenePlatformManager.CurrentPlatform == PlatformType.VR;

    // ── UI ──
    private Canvas _canvas;
    private RectTransform _panel;
    private Image _bg;
    private TextMeshProUGUI _title;
    private TextMeshProUGUI _value;
    private TextMeshProUGUI _hint;
    private bool _collapsed;
    private bool _built;

    private static readonly Color NormalBg = new Color(0.06f, 0.09f, 0.12f, 0.88f);
    private static readonly Color FaultBg = new Color(0.55f, 0.05f, 0.05f, 0.92f);
    private static readonly Color LcdColor = new Color(0.72f, 1f, 0.62f);

    private void Awake()
    {
        if (s_inst != null && s_inst != this) { Destroy(gameObject); return; }
        s_inst = this;
    }

    private void OnDestroy()
    {
        if (s_inst == this) s_inst = null;
    }

    private void Build(TMP_FontAsset labelFont, TMP_FontAsset valueFont)
    {
        _built = true;
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 300;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        gameObject.AddComponent<GraphicRaycaster>();

        var pgo = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(Button));
        pgo.transform.SetParent(transform, false);
        _panel = (RectTransform)pgo.transform;
        if (IsMobile)
        {
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 1f);
            _panel.pivot = new Vector2(0.5f, 1f);
            _panel.anchoredPosition = new Vector2(0f, -110f);
        }
        else
        {
            // (11/20) PC: 위쪽 토스트 알림과 겹치지 않게 조준점 바로 위 화면 가운데로(아래 가장자리가 가운데에서 약 100px 위 = 조준점 위)
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0f);
            _panel.anchoredPosition = new Vector2(0f, 100f);
        }
        _bg = pgo.GetComponent<Image>();
        WorldHoverLabel.StyleCard(_bg);
        _bg.color = NormalBg;
        var btn = pgo.GetComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => { _collapsed = !_collapsed; Layout(); });

        _title = MakeText("Title", labelFont, 34f, new Color(1f, 1f, 1f, 0.85f), FontStyles.Bold);
        _value = MakeText("Value", valueFont != null ? valueFont : labelFont, 150f, LcdColor, FontStyles.Bold);
        _hint = MakeText("Hint", labelFont, 24f, new Color(1f, 1f, 1f, 0.5f), FontStyles.Normal);
        _value.enableAutoSizing = true;   // '493.6 MΩ' 처럼 길어도 한 줄 안에서 글자만 줄인다
        _value.fontSizeMin = 36f;
        _hint.text = IsMobile ? "눌러서 접기" : "클릭해서 접기";
        // PC 는 화면이 넓고 가까이 보므로 모바일보다 작게, 기본은 접힌(값만) 상태 — 작업 화면을 덜 가린다
        if (!IsMobile) { _panel.localScale = Vector3.one * 0.8f; _collapsed = true; }
        Layout();
    }

    private TextMeshProUGUI MakeText(string name, TMP_FontAsset font, float size, Color color, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(_panel, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.color = color;
        t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        return t;
    }

    private static void Place(TextMeshProUGUI t, float top, float height)
    {
        var rt = t.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -top);
        rt.sizeDelta = new Vector2(-40f, height);
    }

    private void Layout()
    {
        if (_collapsed)
        {
            _panel.sizeDelta = new Vector2(420f, 120f);
            _title.gameObject.SetActive(false);
            _hint.gameObject.SetActive(false);
            _value.fontSizeMax = 72f;
            Place(_value, 14f, 92f);
        }
        else
        {
            _panel.sizeDelta = new Vector2(720f, 330f);
            _title.gameObject.SetActive(true);
            _hint.gameObject.SetActive(true);
            _value.fontSizeMax = 150f;
            Place(_title, 18f, 48f);
            Place(_value, 70f, 190f);
            Place(_hint, 278f, 36f);
        }
    }

    private void LateUpdate()
    {
        var meter = IsVR ? null : PickMeter(out bool connected);
        bool show = meter != null && meter.lcdDisplay != null;

        if (!show)
        {
            if (_built && _panel.gameObject.activeSelf) _panel.gameObject.SetActive(false);
            return;
        }

        if (!_built) Build(FindLabelFont(meter), meter.lcdDisplay.font);
        if (!_panel.gameObject.activeSelf) _panel.gameObject.SetActive(true);

        string device = meter.name.Contains("Megger") ? "절연저항계" : "멀티미터";
        string mode = meter.modeDisplay != null ? meter.modeDisplay.text : "";
        _title.text = string.IsNullOrEmpty(mode) ? device : $"{device}  ·  {mode}";

        // 기기 LCD 에 값이 떠 있으면 그대로 옮긴다.
        // 예전에는 '두 프로브가 모두 단자에 닿아 있을 때'만 값을 보여 줘서, 검정 프로브를 접지로 자동 처리하는 실제 측정
        // (빨간 프로브만 단자에 대기, PC 의 E 키 측정)에서는 기기 LCD 값이 바뀌어도 패널은 계속 '----' 였다.
        _value.text = HasLiveValue(meter) ? meter.lcdDisplay.text : "----";

        _bg.color = meter.IsShowingFault ? FaultBg : NormalBg;
    }

    /// <summary>화면에 띄울 계측기: 두 프로브가 닿아 있는 것 우선, 없으면 내가 들고 있는 것.</summary>
    private static MultimeterMaster PickMeter(out bool connected)
    {
        connected = false;
        SyncGrab held = null;
        var cm = FishNet.InstanceFinder.ClientManager;
        if (cm != null && cm.Connection != null && cm.Connection.IsValid)
            held = SyncGrab.FindHeldBy(cm.Connection.ClientId);

        MultimeterMaster heldMeter = null;
        for (int i = s_meters.Count - 1; i >= 0; i--)
        {
            var m = s_meters[i];
            if (m == null) { s_meters.RemoveAt(i); continue; }
            if (!m.isActiveAndEnabled) continue;

            bool red = m.redProbe != null && !string.IsNullOrEmpty(m.redProbe.currentTerminalId);
            bool mine = held != null && IsPartOf(held, m);
            // 내가 든 계측기에 값이 떠 있거나, 빨간 프로브가 단자에 닿아 있는 계측기를 우선 보여 준다
            if ((mine || red) && HasLiveValue(m)) { connected = true; return m; }

            if (mine && heldMeter == null) heldMeter = m;
        }
        return heldMeter;
    }

    /// <summary>기기 LCD 에 측정값이 떠 있는지(꺼져 가는 중이거나 '----' 가 아님).</summary>
    private static bool HasLiveValue(MultimeterMaster m)
    {
        if (m == null || m.lcdDisplay == null) return false;
        string v = m.lcdDisplay.text;
        return m.lcdDisplay.alpha > 0.1f && !string.IsNullOrWhiteSpace(v) && v != "----";
    }

    private static bool IsPartOf(SyncGrab held, MultimeterMaster m)
    {
        Transform t = held.transform;
        if (t == m.transform || t.IsChildOf(m.transform) || m.transform.IsChildOf(t)) return true;
        if (m.redProbe != null && m.redProbe.GetComponentInParent<SyncGrab>() == held) return true;
        if (m.blackProbe != null && m.blackProbe.GetComponentInParent<SyncGrab>() == held) return true;
        return false;
    }

    /// <summary>한글이 들어 있는 폰트를 화면 UI 에서 빌려 온다(기본 TMP 폰트에는 한글이 없다).</summary>
    private static TMP_FontAsset FindLabelFont(MultimeterMaster m)
    {
        if (m.modeDisplay != null && m.modeDisplay.font != null && m.modeDisplay.font.HasCharacter('멀')) return m.modeDisplay.font;
        foreach (var t in FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (t.font != null && t.font.HasCharacter('멀', true)) return t.font;
        return m.modeDisplay != null ? m.modeDisplay.font : null;
    }
}
