using System.Collections.Generic;
using System.Text;
using FishNet.Object;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using XRAirpotrSecurity;

// ============================================================
//  PartTrayPanel.cs — 분해 부품 보관함 화면 (PC·모바일, 11/19)
//
//  쓰이지 않던 인벤토리 창(EVInventoryUI — 메뉴의 툴박스 버튼, I 키)을 이 보관함으로 연결한다.
//  · 떼어낸 부품·휠을 손에 든 채 N 키(모바일: '보관함에 넣기' 버튼) → 보관함에 넣는다.
//  · 창에는 떼어낸 부품이 '떼어낸 순서(#번호)'와 함께 나온다. 조립은 보통 역순이라 마지막에 뗀 것이 맨 위.
//    부품마다 체결 공구와 규정 토크(kgf·m)를 함께 보여 준다 — 조립할 때 바로 참고.
//  · '꺼내기'를 누르면 내 앞에 그 부품이 다시 생긴다.
//  · 크레인·배터리 리프트가 필요한 대형 부품은 넣을 수 없다(창에 '대형'으로 표시).
//  보관 상태는 서버(CarPartService.Tray.cs)가 들고 있어 같은 방 모두가 같은 보관함을 본다.
//  VR 은 VrPartTray.cs(손바닥 메뉴 '부품 보관함' + 든 손 위 '보관함에 넣기' 버튼).
// ============================================================
public partial class PartTrayPanel : MonoBehaviour
{
    public const KeyCode StoreKey = KeyCode.N;   // K 는 집중 모드(FocusMode)가 쓴다

    private static PartTrayPanel s_inst;
    private GameObject _canvas, _root;
    private RectTransform _content;
    private TMP_Text _empty, _summary;
    private float _nextRefresh, _nextHeldScan;
    private string _sig;
    private SyncGrab _held;             // 지금 내 손에 든 떼어낸 부품·휠(없으면 null)
    private int _heldCode = -1;         // 그 항목 코드(PartTray — 휠은 WheelBase)
    private int _hintedFor = -1;

    public static bool IsOpen => s_inst != null && s_inst._root != null && s_inst._root.activeSelf;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isBatchMode || FindAnyObjectByType<PartTrayPanel>() != null) return;
        var go = new GameObject("[PartTrayPanel]");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        s_inst = go.AddComponent<PartTrayPanel>();
    }

    private static bool Usable
    {
        get
        {
            var svc = CarPartService.Instance;
            return svc != null && svc.Ready && Utils.GetPlatformType() != PlatformType.VR;
        }
    }

    /// <summary>
    /// 인벤토리 창(EVInventoryUI.TogglePanel — 툴박스 버튼·I 키)이 먼저 부른다.
    /// 차 부품 작업 중(PC·모바일)이면 보관함을 열고 닫은 뒤 true(기존 인벤토리 창은 열지 않음).
    /// </summary>
    public static bool TryToggle()
    {
        if (s_inst == null || !Usable) return false;
        s_inst.SetOpen(!IsOpen);
        return true;
    }

    private void Update()
    {
        if (!Usable)
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
            MobileQuickButtons.Set("tray", false);
            return;
        }

        if (Time.unscaledTime >= _nextHeldScan) { _nextHeldScan = Time.unscaledTime + 0.2f; _held = PartTray.FindHeld(out _heldCode); }
        int hp = _held != null ? _heldCode : -1;
        bool canStore = PartTray.CanStore(_held, hp);

        // 처음 집었을 때 한 번 알려 준다
        if (canStore && _hintedFor != hp)
        {
            _hintedFor = hp;
            bool mobile = Utils.GetPlatformType() == PlatformType.Mobile;
            if (PartTray.Allowed(hp))
                EquipFx.Notice("tray_hint", $"{(PartTray.IsWheel(hp) ? "휠" : PartTray.Name(hp))} — 보관함에 넣을 수 있습니다",
                    mobile ? "'보관함에 넣기' 버튼을 누르면 보관함(툴박스 메뉴)에 들어갑니다" : "N 키: 보관함에 넣기 · I 키(툴박스 메뉴): 보관함 보기", EquipFx.Info, 3f, 20f);
        }
        if (hp < 0) _hintedFor = -1;

        if (canStore && Input.GetKeyDown(StoreKey)) Store();
        MobileQuickButtons.Set("tray", canStore && PartTray.Allowed(hp), Utils.GetPlatformType() == PlatformType.Mobile ? "보관함에 넣기" : "보관함에 넣기 (N)", MobileQuickButtons.SlotUpper, Store);

        if (IsOpen)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) SetOpen(false);
            if (Time.unscaledTime >= _nextRefresh) { _nextRefresh = Time.unscaledTime + 0.4f; RefreshCurrent(); }
        }
        UpdateBadge();
    }

    private void Store()
    {
        int code = _heldCode;
        if (_held == null || code < 0) return;
        if (!PartTray.Allowed(code))
        {
            EquipFx.Notice("tray_heavy", $"{PartTray.Name(code)}은(는) 보관함에 넣을 수 없습니다", "크레인·배터리 리프트로 옮겨 바닥이나 받침대에 내려놓으세요", EquipFx.Warn, 3f, 3f);
            return;
        }
        string name = PartTray.Store(_held, code);
        _held = null; _heldCode = -1;
        if (name == null) return;
        EquipFx.Notice("tray_store", $"{name} 보관함에 넣음", "조립 때 툴박스 메뉴(I)에서 꺼내세요", EquipFx.Info, 2.5f, 0.5f);
        _nextRefresh = 0f;
    }

    private void Take(int p)
    {
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 at = cam.transform.position + cam.transform.forward * 1.0f + Vector3.down * 0.35f;
        PartTray.Take(p, at);
        EquipFx.Notice("tray_take", $"{PartTray.Name(p)} 꺼냄", "앞에 내려놓았습니다 — 들고 가서 원래 자리에 맞추세요", EquipFx.Info, 2.5f, 0.5f);
        _nextRefresh = 0f;
    }

    // ── 툴박스 메뉴 버튼의 숫자 배지(보관 중인 개수) ──
    private int _badgeShown = -1;

    private void UpdateBadge()
    {
        var inv = EVInventoryUI.Instance;
        var svc = CarPartService.Instance;
        if (inv == null || inv.totalCountText == null || inv.countBadgeGroup == null || svc == null) return;
        int n = 0;
        for (int p = 0; p < CarPartService.Defs.Length; p++) if (svc.TrayHas(p)) n++;
        var ts = TireService.Instance;
        if (ts != null) for (int w = 0; w < TireService.Wheels; w++) if (ts.TrayHas(w)) n++;
        if (n == _badgeShown) return;
        _badgeShown = n;
        inv.totalCountText.text = n.ToString();
        inv.countBadgeGroup.gameObject.SetActive(n > 0);
        inv.countBadgeGroup.alpha = n > 0 ? 1f : 0f;
    }

    // ── 창 ──
    private void SetOpen(bool open)
    {
        if (open && _canvas == null) Build();
        if (_root == null) return;
        _root.SetActive(open);
        if (open) { _sig = null; RefreshCurrent(); }
    }

    private const float W = 1000f, H = 660f, RowH = 70f;

    private void Build()
    {
        _canvas = new GameObject("[PartTrayPanel_Canvas]");
        DontDestroyOnLoad(_canvas);
        var cv = _canvas.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 74;
        var sc = _canvas.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920f, 1080f); sc.matchWidthOrHeight = 0.5f;
        _canvas.AddComponent<GraphicRaycaster>();
        if (EventSystem.current == null) _canvas.AddComponent<EventSystem>();

        var root = NewRect("Root", _canvas.transform); Stretch(root);
        _root = root.gameObject;
        var dim = NewRect("Dim", root); Stretch(dim);
        dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
        dim.gameObject.AddComponent<Button>().onClick.AddListener(() => SetOpen(false));

        var panel = NewRect("Panel", root);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(W, H);
        var bg = panel.gameObject.AddComponent<Image>();
        bg.sprite = WorldHoverLabel.RoundedSprite(); bg.type = Image.Type.Sliced; bg.color = new Color(0.06f, 0.08f, 0.12f, 0.97f);

        BuildTabs(panel);

        var close = NewRect("Close", panel);
        close.anchorMin = close.anchorMax = close.pivot = new Vector2(1f, 1f);
        close.anchoredPosition = new Vector2(-16f, -16f); close.sizeDelta = new Vector2(52f, 52f);
        var ci = close.gameObject.AddComponent<Image>(); ci.sprite = WorldHoverLabel.RoundedSprite(); ci.type = Image.Type.Sliced; ci.color = new Color(0.16f, 0.2f, 0.27f, 1f);
        close.gameObject.AddComponent<Button>().onClick.AddListener(() => SetOpen(false));
        var ct = NewText(close, "<b>X</b>", 24f, TextAlignmentOptions.Center); Stretch(ct.rectTransform);

        // ── 탭 1: 부품 보관함 ──
        var page = NewPage(panel, "TrayPage");
        _pages[0] = page.gameObject;
        bool mobile = Utils.GetPlatformType() == PlatformType.Mobile;
        var sub = NewText(page, mobile
                ? "떼어낸 부품·휠을 든 채 '보관함에 넣기' 버튼으로 넣습니다. 조립은 보통 뗀 순서의 역순 — 위에서부터 꺼내 장착하세요."
                : "떼어낸 부품·휠을 든 채 <b>N</b> 키로 넣습니다. 조립은 보통 뗀 순서의 역순 — 위에서부터 꺼내 장착하세요.",
            18f, TextAlignmentOptions.Left);
        sub.color = new Color(0.7f, 0.76f, 0.84f);
        Place(sub.rectTransform, new Vector2(28f, -4f), new Vector2(W - 340f, 28f));
        _summary = NewText(page, "", 18f, TextAlignmentOptions.Right);
        _summary.color = new Color(0.55f, 0.85f, 1f);
        _summary.rectTransform.anchorMin = _summary.rectTransform.anchorMax = _summary.rectTransform.pivot = new Vector2(1f, 1f);
        _summary.rectTransform.anchoredPosition = new Vector2(-28f, -4f); _summary.rectTransform.sizeDelta = new Vector2(300f, 28f);

        // 머리줄
        var head = NewText(page, "<b>순서</b>", 16f, TextAlignmentOptions.Left);
        head.color = new Color(0.6f, 0.65f, 0.72f);
        Place(head.rectTransform, new Vector2(40f, -40f), new Vector2(80f, 24f));
        var head2 = NewText(page, "<b>부품 · 조립할 때 공구 / 규정 토크</b>", 16f, TextAlignmentOptions.Left);
        head2.color = head.color;
        Place(head2.rectTransform, new Vector2(124f, -40f), new Vector2(500f, 24f));
        var head3 = NewText(page, "<b>위치</b>", 16f, TextAlignmentOptions.Left);
        head3.color = head.color;
        Place(head3.rectTransform, new Vector2(W - 290f, -40f), new Vector2(120f, 24f));

        _content = NewScroll(page, 68f);

        _empty = NewText(page, "아직 떼어낸 부품이 없습니다.\n<size=80%><color=#8C97A6>볼트를 다 풀어 떼어낸 부품·휠이 여기 순서대로 쌓입니다.</color></size>", 22f, TextAlignmentOptions.Center);
        _empty.textWrappingMode = TextWrappingModes.Normal;
        Place(_empty.rectTransform, new Vector2(0f, -200f), new Vector2(W, 100f));

        // ── 탭 2·3 (PartTrayPanel.Tabs.cs) ──
        _pages[1] = BuildSafetyPage(panel);
        _pages[2] = BuildToolsPage(panel);
        ShowTab(_tab);

        _root.SetActive(false);
    }

    private readonly List<int> _list = new List<int>();

    private void Refresh()
    {
        var svc = CarPartService.Instance;
        if (svc == null || _content == null) return;

        // 떼어낸 부품(지금 차에서 빠져 있는 것)만 — 마지막에 뗀 것이 위
        PartTray.Collect(_list);

        var sb = new StringBuilder();
        int stored = 0;
        foreach (int p in _list) { bool t = PartTray.Stored(p); if (t) stored++; sb.Append(p).Append(t ? 'T' : 'F').Append(PartTray.Order(p)).Append(','); }
        _summary.text = $"보관 중 {stored} · 떼어냄 {_list.Count}";
        _empty.gameObject.SetActive(_list.Count == 0);
        string sig = sb.ToString();
        if (sig == _sig) return;
        _sig = sig;

        for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
        for (int i = 0; i < _list.Count; i++) BuildRow(svc, _list[i], i);
        _content.sizeDelta = new Vector2(0f, _list.Count * RowH + 4f);
    }

    private void BuildRow(CarPartService svc, int p, int i)
    {
        bool inTray = PartTray.Stored(p);
        bool heavy = !PartTray.Allowed(p);

        var row = NewRect("Row" + p, _content);
        row.anchorMin = new Vector2(0f, 1f); row.anchorMax = new Vector2(1f, 1f); row.pivot = new Vector2(0.5f, 1f);
        row.anchoredPosition = new Vector2(0f, -i * RowH); row.sizeDelta = new Vector2(0f, RowH - 6f);
        var rbg = row.gameObject.AddComponent<Image>();
        rbg.sprite = WorldHoverLabel.RoundedSprite(); rbg.type = Image.Type.Sliced;
        rbg.color = inTray ? new Color(0.09f, 0.17f, 0.24f, 1f) : new Color(0.11f, 0.13f, 0.17f, 1f);

        // 순서 배지
        var badge = NewRect("Order", row);
        badge.anchorMin = badge.anchorMax = new Vector2(0f, 0.5f); badge.pivot = new Vector2(0f, 0.5f);
        badge.anchoredPosition = new Vector2(16f, 0f); badge.sizeDelta = new Vector2(64f, 40f);
        var bi = badge.gameObject.AddComponent<Image>(); bi.sprite = WorldHoverLabel.RoundedSprite(); bi.type = Image.Type.Sliced;
        bi.color = new Color(0.2f, 0.25f, 0.33f, 1f);
        int ord = PartTray.Order(p);
        var bt = NewText(badge, ord > 0 ? $"<b>#{ord}</b>" : "-", 20f, TextAlignmentOptions.Center); Stretch(bt.rectTransform);

        // 이름 + 공구·토크
        var name = NewText(row, $"<b>{PartTray.Name(p)}</b>\n<size=74%><color=#8FD3FF>{PartTray.ToolLine(p)}</color></size>", 22f, TextAlignmentOptions.Left);
        name.overflowMode = TextOverflowModes.Ellipsis;
        name.rectTransform.anchorMin = name.rectTransform.anchorMax = new Vector2(0f, 0.5f); name.rectTransform.pivot = new Vector2(0f, 0.5f);
        name.rectTransform.anchoredPosition = new Vector2(100f, 0f); name.rectTransform.sizeDelta = new Vector2(W - 440f, RowH - 6f);

        // 위치
        string where = inTray ? "<color=#7CE3A0><b>보관 중</b></color>"
                     : heavy ? "<color=#FFB347><b>대형</b></color>\n<size=70%><color=#B9A27E>크레인·리프트</color></size>"
                     : "<color=#C9D1DC>바닥·손</color>";
        var wt = NewText(row, where, 19f, TextAlignmentOptions.Left);
        wt.rectTransform.anchorMin = wt.rectTransform.anchorMax = new Vector2(1f, 0.5f); wt.rectTransform.pivot = new Vector2(0f, 0.5f);
        wt.rectTransform.anchoredPosition = new Vector2(-270f, 0f); wt.rectTransform.sizeDelta = new Vector2(120f, RowH - 6f);

        if (inTray)
        {
            var b = NewRect("Take", row);
            b.anchorMin = b.anchorMax = b.pivot = new Vector2(1f, 0.5f);
            b.anchoredPosition = new Vector2(-14f, 0f); b.sizeDelta = new Vector2(124f, 44f);
            var img = b.gameObject.AddComponent<Image>(); img.sprite = WorldHoverLabel.RoundedSprite(); img.type = Image.Type.Sliced;
            img.color = new Color(0.13f, 0.55f, 0.95f, 1f);
            int pp = p;
            b.gameObject.AddComponent<Button>().onClick.AddListener(() => Take(pp));
            var t = NewText(b, "<b>꺼내기</b>", 20f, TextAlignmentOptions.Center); Stretch(t.rectTransform);
        }
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
