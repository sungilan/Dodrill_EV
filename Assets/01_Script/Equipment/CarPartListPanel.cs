using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using XRAirpotrSecurity;

// ============================================================
//  CarPartListPanel.cs
//
//  PC·모바일 화면 왼쪽 위에 '부위 목록' 버튼(키 L)을 두고, 누르면 자유 실습 부위의 지금 상태를 세 탭으로 보여 준다.
//   · 풀 수 있음 — 지금 바로 볼트를 풀 수 있는 부위(어셈블리면 세부 부위 수 표시)
//   · 막힘      — 아직 못 푸는 부위와 그 이유(먼저 떼어야 하는 부위·휠·어셈블리)
//   · 떼어냄    — 떼어냈거나 끼우는 중인 부위(다시 끼울 수 있는지)
//  자유 탈거 모드에서 '지금 뭘 더 분해할 수 있지?'를 바로 알 수 있게 한다. VR 은 쓰지 않는다. 씬에 둘 필요 없이 시작할 때 스스로 붙는다.
// ============================================================
public class CarPartListPanel : MonoBehaviour
{
    private GameObject _canvas;
    private GameObject _panel;
    private TMP_Text _body;
    private TMP_Text[] _tabLabels;
    private ScrollRect _scroll;
    private bool _open;
    private int _tab;
    private float _nextRefresh, _nextCheck;
    private readonly int[] _count = new int[3];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindAnyObjectByType<CarPartListPanel>() != null) return;
        var go = new GameObject("[CarPartListPanel]");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        go.AddComponent<CarPartListPanel>();
    }

    private void Update()
    {
        if (Time.unscaledTime >= _nextCheck)
        {
            _nextCheck = Time.unscaledTime + 1f;
            var svc = CarPartService.Instance;
            bool want = svc != null && svc.Ready && Utils.GetPlatformType() != PlatformType.VR;
            if (want && _canvas == null) Build();
            else if (!want && _canvas != null) { Destroy(_canvas); _canvas = null; _panel = null; _open = false; }
        }
        if (_canvas == null) return;
        if (Input.GetKeyDown(KeyCode.L)) SetOpen(!_open);
        if (_open && Time.unscaledTime >= _nextRefresh) { _nextRefresh = Time.unscaledTime + 0.5f; Refresh(); }
    }

    private void OnDestroy() { if (_canvas != null) Destroy(_canvas); }

    private void SetOpen(bool open)
    {
        _open = open;
        if (_panel != null) _panel.SetActive(open);
        if (open) { _nextRefresh = 0f; Refresh(); }
    }

    // ── UI 만들기 ──
    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    private TMP_Text NewText(Transform parent, string name, float size, TextAlignmentOptions align)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        var kf = MobileWorkButton.KoreanFont();
        if (kf != null) t.font = kf;
        t.fontSize = size; t.alignment = align; t.color = Color.white; t.raycastTarget = false;
        return t;
    }

    private Button NewButton(Transform parent, string name, Vector2 pos, Vector2 size, out TMP_Text label)
    {
        var rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(0.1f, 0.14f, 0.2f, 0.9f);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        label = NewText(rt, "Text", 21f, TextAlignmentOptions.Center);
        var lr = label.rectTransform; lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = lr.offsetMax = Vector2.zero;
        return btn;
    }

    private void Build()
    {
        _canvas = new GameObject("[CarPartListPanel_Canvas]");
        var cv = _canvas.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 41;
        var scaler = _canvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _canvas.AddComponent<GraphicRaycaster>();
        if (EventSystem.current == null) _canvas.AddComponent<EventSystem>();

        // 열기 버튼 (단계 팝업 버튼 바로 아래)
        var open = NewButton(_canvas.transform, "OpenButton", new Vector2(14f, -296f), new Vector2(250f, 40f), out var openLabel);
        openLabel.text = "부위 목록 (L)";
        open.onClick.AddListener(() => SetOpen(!_open));

        // 패널
        var prt = NewRect("Panel", _canvas.transform);
        prt.anchorMin = prt.anchorMax = new Vector2(0f, 1f); prt.pivot = new Vector2(0f, 1f);
        prt.anchoredPosition = new Vector2(14f, -342f); prt.sizeDelta = new Vector2(600f, 640f);
        var bg = prt.gameObject.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.08f, 0.12f, 0.92f);
        _panel = prt.gameObject;

        // 탭 3개
        _tabLabels = new TMP_Text[3];
        string[] names = { "풀 수 있음", "막힘", "떼어냄" };
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var tb = NewButton(prt, "Tab" + i, new Vector2(8f + i * 194f, -8f), new Vector2(190f, 40f), out _tabLabels[i]);
            _tabLabels[i].text = names[i];
            tb.onClick.AddListener(() => { _tab = idx; _nextRefresh = 0f; if (_scroll != null) _scroll.verticalNormalizedPosition = 1f; Refresh(); });
        }

        // 스크롤 영역
        var vp = NewRect("Viewport", prt);
        vp.anchorMin = Vector2.zero; vp.anchorMax = Vector2.one;
        vp.offsetMin = new Vector2(10f, 10f); vp.offsetMax = new Vector2(-10f, -56f);
        vp.gameObject.AddComponent<RectMask2D>();
        var content = NewRect("Content", vp);
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(0f, 100f);
        _body = content.gameObject.AddComponent<TextMeshProUGUI>();
        var kf = MobileWorkButton.KoreanFont();
        if (kf != null) _body.font = kf;
        _body.fontSize = 22f; _body.alignment = TextAlignmentOptions.TopLeft; _body.color = Color.white;
        _body.richText = true; _body.textWrappingMode = TextWrappingModes.Normal; _body.raycastTarget = false;
        var fit = content.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _scroll = prt.gameObject.AddComponent<ScrollRect>();
        _scroll.viewport = vp; _scroll.content = content;
        _scroll.horizontal = false; _scroll.vertical = true; _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 40f;

        _panel.SetActive(false);
    }

    // ── 내용 ──
    private void Refresh()
    {
        var svc = CarPartService.Instance;
        if (svc == null || !svc.Ready || _body == null) return;
        var defs = CarPartService.Defs;
        int n = defs.Length;

        var kids = new int[n];
        for (int p = 0; p < n; p++)
        {
            string par = defs[p].parent;
            if (string.IsNullOrEmpty(par)) continue;
            for (int q = 0; q < n; q++) if (defs[q].id == par) { kids[q]++; break; }
        }

        var lines = new List<string>[3] { new List<string>(), new List<string>(), new List<string>() };
        for (int p = 0; p < n; p++)
        {
            var d = defs[p];
            int st = svc.StateOf(p);
            string tag = "";
            if (kids[p] > 0) tag += $" · 어셈블리(세부 {kids[p]}개)";
            if (!string.IsNullOrEmpty(d.parent)) tag += $" · {NameOf(defs, d.parent)} 안";
            if (d.hv) tag += " · 고전압";
            string su = CarPartRules.SingleUseOf(d.id);
            if (su != null) tag += $" · 재사용 금지: {su}";
            string tagText = tag.Length > 0 ? $" <size=70%><color=#9FB4C8>{tag.Substring(3)}</color></size>" : "";

            if (st == (int)CarPartService.PartState.Tight)
            {
                if (svc.CanRemove(p)) lines[0].Add($"<color=#7CFF8A>●</color> {d.name}{tagText}");
                else lines[1].Add($"<color=#8A95A3>○</color> {d.name}{tagText}\n    <size=72%><color=#FFC870>먼저: {svc.RemoveBlockersOf(p)}</color></size>");
            }
            else if (st == (int)CarPartService.PartState.Off)
            {
                string blk = svc.MountBlockersOf(p);
                string sub = string.IsNullOrEmpty(blk) ? "<color=#9FE0B0>다시 끼울 수 있음</color>" : $"<color=#FFC870>먼저 달기: {blk}</color>";
                lines[2].Add($"<color=#FFD54A>◆</color> {d.name}{tagText}\n    <size=72%>{sub}</size>");
            }
            else
            {
                lines[2].Add($"<color=#7FD3FF>◇</color> {d.name}{tagText}\n    <size=72%><color=#9FE0B0>장착됨 — 볼트를 규정 토크로 조이세요</color></size>");
            }
        }

        for (int i = 0; i < 3; i++) _count[i] = lines[i].Count;
        string[] names = { "풀 수 있음", "막힘", "떼어냄" };
        for (int i = 0; i < 3; i++) if (_tabLabels != null && _tabLabels[i] != null) _tabLabels[i].text = $"{(i == _tab ? "▶ " : "")}{names[i]} {_count[i]}";

        var sb = new StringBuilder();
        if (_tab == 0) sb.Append("<size=76%><color=#9FB4C8>지금 바로 볼트를 풀 수 있는 부위입니다. 어셈블리를 떼면 그 안의 세부 부위가 열립니다.</color></size>\n");
        else if (_tab == 1) sb.Append("<size=76%><color=#9FB4C8>아직 풀 수 없는 부위와 먼저 떼어야 하는 것입니다.</color></size>\n");
        else sb.Append("<size=76%><color=#9FB4C8>떼어낸 부위를 들어 원래 자리로 가져가면 다시 끼울 수 있습니다.</color></size>\n");
        if (lines[_tab].Count == 0) sb.Append("\n(없음)");
        foreach (var l in lines[_tab]) sb.Append(l).Append('\n');
        if (CarPartRules.CoolantAirValue == 1) sb.Append("\n<size=76%><color=#FFB000>냉각 회로가 열려 있습니다 — 조립 후 GDS 에어빼기 필요</color></size>");
        else if (CarPartRules.CoolantAirValue == 2) sb.Append("\n<size=76%><color=#FFB000>냉각 회로 에어빼기 필요 (GDS → 부가 기능)</color></size>");
        if (ManualInspection.Summary != null) sb.Append($"\n<size=76%><color=#9FD3FF>{ManualInspection.Summary}</color></size>");
        if (SuspensionInspection.Summary != null) sb.Append($"\n<size=76%><color=#9FD3FF>{SuspensionInspection.Summary}</color></size>");
        string pend = CarPartRules.PendingLearnNames();
        if (pend != null) sb.Append($"\n<size=76%><color=#FFB000>GDS 학습·조정 필요: {pend}</color></size>");
        if (CarPartRules.ReuseCount > 0) sb.Append($"\n<size=76%><color=#FF6B6B>재사용 금지 부품을 그대로 끼운 횟수: {CarPartRules.ReuseCount}</color></size>");
        _body.text = sb.ToString();
    }

    private static string NameOf(CarPartService.PartDef[] defs, string id)
    {
        foreach (var d in defs) if (d.id == id) return d.name;
        return id;
    }
}
