using System.Collections.Generic;
using System.Text;
using Autohand;
using FishNet.Object;
using TMPro;
using UnityEngine;

// ============================================================
//  VrPartTray.cs — 분해 부품 보관함 VR 판 (11/19)
//
//  PC·모바일 보관함(PartTrayPanel)과 같은 서버 상태(CarPartService.Tray.cs)를 VR 손으로 쓴다.
//  · 넣기 : 떼어낸 부품·휠을 들고 있으면 그 손 위에 작은 '보관함에 넣기' 버튼이 뜬다 → 반대 손 검지로 누르면 손에서 놓고 보관함에 들어간다.
//           (손바닥 메뉴는 물건을 든 동안 열리지 않으므로 따로 띄운다. 대형 부품은 버튼이 뜨지 않는다.)
//  · 보기 : 손바닥 메뉴의 '부품 보관함' 버튼 → 눈앞 가슴 높이에 보관함 창이 공중에 고정된다.
//           떼어낸 순서 역순(마지막에 뗀 것이 위)으로 5개씩, 부품마다 공구·규정 토크를 보여 준다.
//  · 꺼내기: 줄 오른쪽 '꺼내기'를 검지로 누르면 내 앞 바닥 쪽에 그 부품이 다시 생긴다.
//  창은 '닫기'를 누르거나 2.5 m 이상 멀어지면 닫힌다. 누르는 방식(손끝 진동·클릭음)은 손바닥 메뉴와 같다.
// ============================================================
public class VrPartTray : MonoBehaviour
{
    private const int PageSize = 5;
    private const float PanelW = 0.44f, PanelH = 0.44f, RowH = 0.05f;
    private const float PressDepth = 0.006f, ReleaseDepth = 0.02f;

    private static readonly Color PanelColor = new Color(0.06f, 0.08f, 0.12f, 0.92f);
    private static readonly Color RowColor = new Color(0.11f, 0.13f, 0.17f, 0.95f);
    private static readonly Color RowStoredColor = new Color(0.09f, 0.17f, 0.24f, 0.95f);
    private static readonly Color BtnColor = new Color(0.13f, 0.45f, 0.8f, 0.95f);
    private static readonly Color NeutralBtn = new Color(0.16f, 0.2f, 0.27f, 0.95f);
    private static readonly Color HoverColor = new Color(0.2f, 0.55f, 0.95f, 1f);
    private static readonly Color PressColor = new Color(0.25f, 0.75f, 0.4f, 0.95f);
    private static readonly Color DisabledColor = new Color(0.2f, 0.2f, 0.2f, 0.6f);

    private static VrPartTray s_inst;
    public static bool IsOpen => s_inst != null && s_inst._panel != null && s_inst._panel.root.activeSelf;
    /// <summary>손바닥 메뉴의 '부품 보관함' 버튼이 부른다.</summary>
    public static void Toggle() { if (s_inst != null) s_inst.SetOpen(!IsOpen); }
    public static bool Available => VrProbeHandling.IsVRDevice && CarPartService.Instance != null && CarPartService.Instance.Ready;

    private readonly List<Hand> _hands = new List<Hand>();
    private float _nextHandScan, _nextHeldScan, _nextRefresh;
    private TMP_FontAsset _font;

    private PokeSurface _panel, _store;
    private TextMeshPro _summary, _pageText, _empty, _subtitle, _safety;
    private int _tab;   // 0 보관함 · 1 안전·조립 상태 · 2 특수공구
    private static readonly string[] TabLabels = { "부품 보관함", "안전·조립", "특수공구" };
    private readonly List<GameObject> _rowObjs = new List<GameObject>();
    private readonly List<int> _list = new List<int>();
    private int _page;
    private string _sig;

    private SyncGrab _held;
    private int _heldCode = -1;
    private Hand _heldHand;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isBatchMode || FindAnyObjectByType<VrPartTray>() != null) return;
        var go = new GameObject("[VrPartTray]");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        s_inst = go.AddComponent<VrPartTray>();
    }

    private void OnDestroy()
    {
        _panel?.Destroy();
        _store?.Destroy();
    }

    private void LateUpdate()
    {
        if (!VrProbeHandling.IsVRDevice) return;
        if (!Available)
        {
            if (IsOpen) SetOpen(false);
            if (_store != null && _store.root.activeSelf) _store.root.SetActive(false);
            return;
        }
        if (Time.unscaledTime >= _nextHandScan) { _nextHandScan = Time.unscaledTime + 2f; ScanHands(); }
        var cam = Camera.main;
        if (cam == null) return;

        UpdateStoreButton(cam.transform);

        if (IsOpen)
        {
            if (Vector3.Distance(cam.transform.position, _panel.root.transform.position) > 2.5f) { SetOpen(false); return; }
            if (Time.unscaledTime >= _nextRefresh) { _nextRefresh = Time.unscaledTime + 0.4f; Refresh(); }
            string id = _panel.Update(NearestTip(_panel.root.transform, null), out Hand by);
            if (id != null) RunPanel(id, by);
        }
    }

    // ══════════════════════════════════════════════════════
    // 넣기 — 든 손 위의 작은 버튼
    // ══════════════════════════════════════════════════════

    private void UpdateStoreButton(Transform cam)
    {
        if (Time.unscaledTime >= _nextHeldScan) { _nextHeldScan = Time.unscaledTime + 0.2f; FindHeld(); }
        int hp = _held != null ? _heldCode : -1;
        bool show = hp >= 0 && _heldHand != null && _heldHand.palmTransform != null
                    && PartTray.Allowed(hp) && PartTray.CanStore(_held, hp);
        if (show)
        {
            // 시야 안에 손이 있을 때만(작업 중 시야 밖에서 버튼이 떠다니지 않게)
            Vector3 toHand = _heldHand.palmTransform.position - cam.position;
            show = toHand.magnitude < 0.9f && Vector3.Dot(cam.forward, toHand.normalized) > 0.35f;
        }
        if (!show)
        {
            if (_store != null && _store.root.activeSelf) _store.root.SetActive(false);
            return;
        }

        if (_store == null) BuildStore();
        if (!_store.root.activeSelf) { _store.root.SetActive(true); _store.Arm(); }

        Vector3 palm = _heldHand.palmTransform.position;
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, (palm - cam.position).normalized).normalized;
        Vector3 pos = palm + up * 0.13f;
        Vector3 look = pos - cam.position;
        if (look.sqrMagnitude < 1e-6f) look = cam.forward;
        _store.root.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look, Vector3.up));

        string id = _store.Update(NearestTip(_store.root.transform, _heldHand), out Hand by);
        if (id == "store") Store(by);
    }

    private void FindHeld()
    {
        _heldHand = null;
        _held = PartTray.FindHeld(out _heldCode);
        if (_held == null) return;
        var g = _held.GetComponentInChildren<Grabbable>();
        var by = g != null ? g.GetHeldBy() : null;
        if (by == null || by.Count == 0 || by[0] == null) { _held = null; _heldCode = -1; return; }
        _heldHand = by[0];
    }

    private void BuildStore()
    {
        LoadFont();
        _store = new PokeSurface("[VrPartTray_Store]", _font);
        _store.AddButton("store", "보관함에 넣기", Vector2.zero, new Vector2(0.12f, 0.038f), BtnColor);
        _store.root.SetActive(false);
    }

    private void Store(Hand poker)
    {
        if (_held == null || _heldCode < 0) return;
        Haptic(poker, 0.03f, 0.5f);
        Haptic(_heldHand, 0.05f, 0.4f);
        string name = PartTray.Store(_held, _heldCode);
        WorldSfx.PlayAt("UI_Click", _store.root.transform.position, 0.6f);
        if (name != null)
            WarningPopup.Show("tray", $"{name} 보관함에 넣음", "손바닥 메뉴 '부품 보관함'에서 꺼냅니다", EquipFx.Info, 2.2f);
        _held = null; _heldCode = -1; _heldHand = null;
        _store.root.SetActive(false);
        _nextHeldScan = Time.unscaledTime + 0.5f;
        _nextRefresh = 0f;
    }

    // ══════════════════════════════════════════════════════
    // 보관함 창
    // ══════════════════════════════════════════════════════

    private void SetOpen(bool open)
    {
        if (open && _panel == null) BuildPanel();
        if (_panel == null) return;
        if (!open) { _panel.root.SetActive(false); return; }

        var cam = Camera.main;
        if (cam == null) return;
        // 눈앞 0.5 m · 가슴 높이에 고정(따라다니지 않음 — 돌아서 부품을 집으러 가도 창은 제자리)
        Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = cam.transform.up;
        fwd.Normalize();
        Vector3 pos = cam.transform.position + fwd * 0.5f + Vector3.down * 0.22f;
        Vector3 look = pos - cam.transform.position;
        _panel.root.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look, Vector3.up));
        _panel.root.SetActive(true);
        _panel.Arm();
        _page = 0; _sig = null;
        Refresh();
    }

    private void BuildPanel()
    {
        LoadFont();
        _panel = new PokeSurface("[VrPartTray_Panel]", _font);
        float top = PanelH * 0.5f;
        _panel.AddQuad(Vector2.zero, new Vector2(PanelW, PanelH), PanelColor, 0.002f, 3980);
        for (int t = 0; t < TabLabels.Length; t++)
            _panel.AddButton("tab" + t, TabLabels[t], new Vector2(-0.145f + t * 0.145f, top - 0.03f), new Vector2(0.137f, 0.038f), NeutralBtn);
        _subtitle = _panel.AddText("", new Vector2(-0.065f, top - 0.072f), new Vector2(0.28f, 0.018f), TextAlignmentOptions.Left, new Color(0.7f, 0.76f, 0.84f));
        _summary = _panel.AddText("", new Vector2(0.145f, top - 0.072f), new Vector2(0.12f, 0.018f), TextAlignmentOptions.Right, new Color(0.55f, 0.85f, 1f));
        _empty = _panel.AddText("아직 떼어낸 부품이 없습니다", new Vector2(0f, 0.0f), new Vector2(0.3f, 0.03f), TextAlignmentOptions.Center, new Color(0.7f, 0.76f, 0.84f));
        _safety = _panel.AddText("", new Vector2(0f, -0.02f), new Vector2(PanelW - 0.04f, 0.29f), TextAlignmentOptions.TopLeft, Color.white);
        _safety.textWrappingMode = TextWrappingModes.Normal;
        _safety.overflowMode = TextOverflowModes.Truncate;
        _safety.fontSizeMax = 0.03f;

        float by = -top + 0.03f;
        _panel.AddButton("prev", "이전", new Vector2(-0.16f, by), new Vector2(0.08f, 0.034f), NeutralBtn);
        _pageText = _panel.AddText("1 / 1", new Vector2(-0.075f, by), new Vector2(0.07f, 0.024f), TextAlignmentOptions.Center, Color.white);
        _panel.AddButton("next", "다음", new Vector2(0.01f, by), new Vector2(0.08f, 0.034f), NeutralBtn);
        _panel.AddButton("close", "닫기", new Vector2(0.16f, by), new Vector2(0.08f, 0.034f), NeutralBtn);
        SetTab(0);
        _panel.root.SetActive(false);
    }

    private void SetTab(int t)
    {
        _tab = Mathf.Clamp(t, 0, TabLabels.Length - 1);
        for (int k = 0; k < TabLabels.Length; k++) _panel.SetColor("tab" + k, k == _tab ? BtnColor : NeutralBtn);
        SetText(_subtitle, _tab == 0 ? "조립은 뗀 순서의 역순 — 위에서부터 꺼내 장착"
                          : _tab == 1 ? "고전압 차단 절차 · 조립 필요 부품 · 측정 기록"
                          : "매뉴얼 특수공구 — '위치'를 누르면 빛기둥");
        _page = 0; _sig = null;
    }

    private void Refresh()
    {
        if (_panel == null) return;
        _safety.gameObject.SetActive(_tab == 1);
        if (_tab == 1) { RefreshSafety(); return; }
        if (_tab == 2) { RefreshTools(); return; }
        RefreshTray();
    }

    private void ClearRows()
    {
        foreach (var go in _rowObjs) if (go != null) _panel.Remove(go);
        _rowObjs.Clear();
    }

    private void RefreshSafety()
    {
        _empty.gameObject.SetActive(false);
        SetText(_summary, "");
        SetText(_pageText, "1 / 1");
        _panel.SetEnabled("prev", false);
        _panel.SetEnabled("next", false);
        if (_sig != "safety") { _sig = "safety"; ClearRows(); }
        SetText(_safety, PartTrayPanel.SafetyText(3));
    }

    private void RefreshTools()
    {
        _empty.gameObject.SetActive(false);
        var data = PartTrayPanel.ToolData;
        int pages = Mathf.Max(1, (data.Length + PageSize - 1) / PageSize);
        _page = Mathf.Clamp(_page, 0, pages - 1);
        SetText(_summary, $"{data.Length}종");
        SetText(_pageText, $"{_page + 1} / {pages}");
        _panel.SetEnabled("prev", _page > 0);
        _panel.SetEnabled("next", _page < pages - 1);

        var cam = Camera.main;
        Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;
        var sb = new StringBuilder("tools").Append(_page).Append('|');
        for (int i = 0; i < PageSize; i++)
        {
            int idx = _page * PageSize + i;
            if (idx >= data.Length) break;
            var t = PartTrayPanel.NearestTool(data[idx][0], eye, out int n, out float d);
            sb.Append(t != null ? n : 0).Append(':').Append(t != null ? Mathf.RoundToInt(d) : -1).Append(',');
        }
        string sig = sb.ToString();
        if (sig == _sig) return;
        _sig = sig;
        ClearRows();

        float y0 = PanelH * 0.5f - 0.115f;
        for (int i = 0; i < PageSize; i++)
        {
            int idx = _page * PageSize + i;
            if (idx >= data.Length) break;
            var row = data[idx];
            float y = y0 - i * RowH;
            var t = PartTrayPanel.NearestTool(row[0], eye, out int n, out float d);
            string no = row[2] == "-" ? "" : $" <color=#8C97A6>{row[2]}</color>";
            _rowObjs.Add(_panel.AddQuad(new Vector2(0f, y), new Vector2(PanelW - 0.03f, RowH - 0.006f), RowColor, 0.001f, 3985));
            _rowObjs.Add(_panel.AddText($"<b>{row[1]}</b>{no}\n<size=78%><color=#8FD3FF>{row[3]}</color></size>",
                new Vector2(-0.05f, y), new Vector2(0.3f, RowH - 0.01f), TextAlignmentOptions.Left, Color.white).gameObject);
            if (t != null)
                _rowObjs.Add(_panel.AddButton("ping" + idx, $"위치 {d:0.#}m", new Vector2(0.16f, y), new Vector2(0.08f, 0.034f), BtnColor).go);
            else
                _rowObjs.Add(_panel.AddText("<color=#8C97A6>없음</color>", new Vector2(0.16f, y), new Vector2(0.08f, 0.022f), TextAlignmentOptions.Center, Color.white).gameObject);
        }
    }

    private void Ping(int idx)
    {
        var data = PartTrayPanel.ToolData;
        if (idx < 0 || idx >= data.Length) return;
        var cam = Camera.main;
        var t = PartTrayPanel.NearestTool(data[idx][0], cam != null ? cam.transform.position : Vector3.zero, out _, out float d);
        if (t == null) return;
        PartTrayPanel.ShowBeacon(t.transform);
        WarningPopup.Show("tray", $"{data[idx][1]} — {d:0.#} m", "주황 빛기둥이 선 곳에 있습니다", EquipFx.Info, 2.5f);
    }

    private void RefreshTray()
    {
        if (_panel == null) return;
        PartTray.Collect(_list);

        int pages = Mathf.Max(1, (_list.Count + PageSize - 1) / PageSize);
        _page = Mathf.Clamp(_page, 0, pages - 1);

        int stored = 0;
        var sb = new StringBuilder().Append(_page).Append('|');
        foreach (int p in _list) { bool t = PartTray.Stored(p); if (t) stored++; sb.Append(p).Append(t ? 'T' : 'F').Append(PartTray.Order(p)).Append(','); }
        SetText(_summary, $"보관 중 {stored} · 떼어냄 {_list.Count}");
        SetText(_pageText, $"{_page + 1} / {pages}");
        _empty.gameObject.SetActive(_list.Count == 0);
        _panel.SetEnabled("prev", _page > 0);
        _panel.SetEnabled("next", _page < pages - 1);

        string sig = sb.ToString();
        if (sig == _sig) return;
        _sig = sig;

        ClearRows();

        float y0 = PanelH * 0.5f - 0.115f;
        for (int i = 0; i < PageSize; i++)
        {
            int idx = _page * PageSize + i;
            if (idx >= _list.Count) break;
            int p = _list[idx];
            bool inTray = PartTray.Stored(p);
            bool heavy = !PartTray.Allowed(p);
            float y = y0 - i * RowH;

            _rowObjs.Add(_panel.AddQuad(new Vector2(0f, y), new Vector2(PanelW - 0.03f, RowH - 0.006f), inTray ? RowStoredColor : RowColor, 0.001f, 3985));
            int ord = PartTray.Order(p);
            _rowObjs.Add(_panel.AddText(ord > 0 ? $"<b>#{ord}</b>" : "-", new Vector2(-0.185f, y), new Vector2(0.035f, 0.022f), TextAlignmentOptions.Center, Color.white).gameObject);
            _rowObjs.Add(_panel.AddText($"<b>{PartTray.Name(p)}</b>\n<size=78%><color=#8FD3FF>{PartTray.ToolLine(p)}</color></size>",
                new Vector2(-0.045f, y), new Vector2(0.23f, RowH - 0.01f), TextAlignmentOptions.Left, Color.white).gameObject);
            if (inTray)
                _rowObjs.Add(_panel.AddButton("take" + p, "꺼내기", new Vector2(0.16f, y), new Vector2(0.08f, 0.034f), BtnColor).go);
            else
                _rowObjs.Add(_panel.AddText(heavy ? "<color=#FFB347>대형</color>" : "<color=#C9D1DC>바닥·손</color>",
                    new Vector2(0.16f, y), new Vector2(0.08f, 0.022f), TextAlignmentOptions.Center, Color.white).gameObject);
        }
    }

    private void RunPanel(string id, Hand by)
    {
        Haptic(by, 0.03f, 0.5f);
        WorldSfx.PlayAt("UI_Click", _panel.root.transform.position, 0.6f);
        switch (id)
        {
            case "close": SetOpen(false); return;
            case "prev": _page--; _sig = null; Refresh(); return;
            case "next": _page++; _sig = null; Refresh(); return;
        }
        if (id.StartsWith("tab") && int.TryParse(id.Substring(3), out int tb)) { SetTab(tb); Refresh(); return; }
        if (id.StartsWith("ping") && int.TryParse(id.Substring(4), out int pi)) { Ping(pi); return; }
        if (id.StartsWith("take") && int.TryParse(id.Substring(4), out int p)) Take(p);
    }

    private void Take(int p)
    {
        var cam = Camera.main;
        if (cam == null || !PartTray.Stored(p)) return;
        // 창 아래·앞쪽에 떨어지게 — 창과 겹치지 않고 바로 집을 수 있는 곳
        Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 at = cam.transform.position + fwd * 0.45f + right * 0.3f + Vector3.down * 0.55f;
        PartTray.Take(p, at);
        WarningPopup.Show("tray", $"{PartTray.Name(p)} 꺼냄", "오른쪽 앞에 내려놓았습니다 — 들고 가서 원래 자리에 맞추세요", EquipFx.Info, 2.2f);
        _nextRefresh = 0f;
    }

    // ══════════════════════════════════════════════════════
    // 손
    // ══════════════════════════════════════════════════════

    private void ScanHands()
    {
        _hands.RemoveAll(h => h == null);
        foreach (var hand in FindObjectsByType<Hand>(FindObjectsSortMode.None))
        {
            if (hand == null || _hands.Contains(hand) || hand.name.StartsWith("HighlightHand")) continue;
            var nob = hand.GetComponentInParent<NetworkObject>();
            if (nob != null && nob.IsSpawned && !nob.IsOwner) continue;
            _hands.Add(hand);
        }
    }

    /// <summary>면에 가장 가까운 검지 끝(except 손 제외 — 부품을 든 손).</summary>
    private Transform NearestTip(Transform surface, Hand except)
    {
        Transform best = null; float bestD = 0.25f;
        foreach (var h in _hands)
        {
            if (h == null || !h.isActiveAndEnabled || h.fingers == null) continue;
            if (except != null && h.left == except.left) continue;
            Transform tip = null;
            foreach (var f in h.fingers) if (f != null && f.fingerType == FingerEnum.index && f.tip != null) { tip = f.tip; break; }
            if (tip == null) continue;
            float d = Vector3.Distance(tip.position, surface.position);
            if (d < bestD) { bestD = d; best = tip; }
        }
        return best;
    }

    private static void Haptic(Hand h, float duration, float amp)
    {
        if (h == null) return;
        try { h.PlayHapticVibration(duration, amp); } catch { }
    }

    private void LoadFont()
    {
        if (_font != null) return;
        var tagPrefab = Resources.Load<GameObject>("ItemNameTagLabel");
        var t = tagPrefab != null ? tagPrefab.GetComponentInChildren<TMP_Text>(true) : null;
        if (t != null) _font = t.font;
    }

    private static void SetText(TMP_Text t, string s) { if (t != null && t.text != s) t.text = s; }

    // ══════════════════════════════════════════════════════
    // 검지로 누르는 공중 면(손바닥 메뉴와 같은 방식: 면 z=0, 보는 쪽이 -z)
    // ══════════════════════════════════════════════════════
    internal class PokeSurface
    {
        private class Btn
        {
            public string id; public Vector2 c, size; public GameObject go; public Material mat; public TextMeshPro text;
            public Color baseColor; public bool enabled = true; public float flashUntil;
        }

        public readonly GameObject root;
        private readonly TMP_FontAsset _font;
        private readonly List<Btn> _btns = new List<Btn>();
        private readonly List<Material> _mats = new List<Material>();
        private bool _latched;
        private float _nextPress, _armedAt;

        public PokeSurface(string name, TMP_FontAsset font)
        {
            _font = font;
            root = new GameObject(name) { layer = 1 };
            DontDestroyOnLoad(root);
        }

        public void Destroy()
        {
            foreach (var m in _mats) if (m != null) Object.Destroy(m);
            if (root != null) Object.Destroy(root);
        }

        /// <summary>열린 직후 잠깐은 눌리지 않게(손끝이 이미 면 안쪽이면 한 번 빠져야 함).</summary>
        public void Arm() { _latched = true; _armedAt = Time.unscaledTime; }

        public GameObject AddQuad(Vector2 c, Vector2 size, Color color, float z, int queue)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var col = q.GetComponent<Collider>();
            if (col != null) { col.enabled = false; Object.Destroy(col); }
            q.layer = 1;
            q.transform.SetParent(root.transform, false);
            q.transform.localPosition = new Vector3(c.x, c.y, z);
            q.transform.localScale = new Vector3(size.x, size.y, 1f);
            var mr = q.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var mat = new Material(Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default")) { renderQueue = queue, color = color };
            mr.sharedMaterial = mat;
            _mats.Add(mat);
            return q;
        }

        public TextMeshPro AddText(string text, Vector2 c, Vector2 size, TextAlignmentOptions align, Color color)
        {
            var go = new GameObject("Text") { layer = 1 };
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(c.x, c.y, -0.001f);
            var tmp = go.AddComponent<TextMeshPro>();
            if (_font != null) tmp.font = _font;
            tmp.rectTransform.sizeDelta = size;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.01f; tmp.fontSizeMax = 0.2f;
            tmp.alignment = align;
            tmp.color = color;
            tmp.richText = true;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.text = text;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (tmp.fontMaterial != null) tmp.fontMaterial.renderQueue = 4000;
            return tmp;
        }

        public (GameObject go, TextMeshPro text) AddButton(string id, string label, Vector2 c, Vector2 size, Color color)
        {
            var q = AddQuad(c, size, color, 0f, 3990);
            q.name = id;
            var t = AddText(label, c, size * 0.8f, TextAlignmentOptions.Center, Color.white);
            t.transform.SetParent(q.transform, true);   // 줄을 지울 때 함께 지워지게
            _btns.Add(new Btn { id = id, c = c, size = size, go = q, mat = q.GetComponent<MeshRenderer>().sharedMaterial, text = t, baseColor = color });
            return (q, t);
        }

        public void SetColor(string id, Color c)
        {
            foreach (var b in _btns) if (b.id == id) b.baseColor = c;
        }

        public void SetEnabled(string id, bool on)
        {
            foreach (var b in _btns) if (b.id == id) b.enabled = on;
        }

        /// <summary>줄 하나(버튼 포함)를 지운다.</summary>
        public void Remove(GameObject go)
        {
            for (int i = _btns.Count - 1; i >= 0; i--) if (_btns[i].go == go) _btns.RemoveAt(i);
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && mr.sharedMaterial != null && _mats.Remove(mr.sharedMaterial)) Object.Destroy(mr.sharedMaterial);
            Object.Destroy(go);
        }

        /// <summary>손끝 위치로 hover·누름을 처리한다. 눌린 버튼 id(없으면 null).</summary>
        public string Update(Transform tip, out Hand by)
        {
            by = null;
            float now = Time.unscaledTime;
            Btn over = null;
            Vector3 lp = Vector3.zero;
            if (tip != null)
            {
                lp = root.transform.InverseTransformPoint(tip.position);
                foreach (var b in _btns)
                    if (Mathf.Abs(lp.x - b.c.x) <= b.size.x * 0.5f && Mathf.Abs(lp.y - b.c.y) <= b.size.y * 0.5f) { over = b; break; }
                if (lp.z < -ReleaseDepth || over == null) _latched = false;
            }
            else _latched = false;

            foreach (var b in _btns)
            {
                if (now < b.flashUntil) continue;
                b.mat.color = !b.enabled ? DisabledColor : b == over && lp.z > -0.03f ? HoverColor : b.baseColor;
                b.text.color = b.enabled ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            }

            if (over == null || _latched || now < _nextPress || now - _armedAt < 0.35f) return null;
            if (lp.z < -PressDepth || lp.z > 0.03f) return null;
            _latched = true;
            _nextPress = now + 0.35f;
            by = tip.GetComponentInParent<Hand>();
            if (!over.enabled)
            {
                if (by != null) { try { by.PlayHapticVibration(0.02f, 0.15f); } catch { } }
                WorldSfx.PlayAt("Beep_Error", tip.position, 0.35f);
                return null;
            }
            over.mat.color = PressColor;
            over.flashUntil = now + 0.15f;
            return over.id;
        }
    }
}
