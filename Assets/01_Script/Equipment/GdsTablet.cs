using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Transporting;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XRAirpotrSecurity;

// ============================================================
//  GdsTablet.cs
//
//  GDS 진단기 태블릿(GIT GDS-Smart 류)의 화면과 기능. 태블릿 화면면(+Y)에 월드 캔버스를 만들어 그린다.
//  · 홈: 원클릭 전체 진단 / 고장 코드 / 센서 데이터 / 부가 기능 / 고전압 차단 가이드 / 차량 정보
//  · VCI: 태블릿을 차량 OBD-II 단자(운전석 하단)에 대면 연결(이후 무선 유지, 12m 밖이면 끊김)
//  · 고장 코드: 상세 · 고장 시 데이터(Freeze frame) · 매뉴얼 점검 절차 · 소거(원인이 남아 있으면 재검출)
//  · 센서 데이터: BMS 실시간 값 + 매뉴얼 규정값, 벗어나면 빨강
//  · 부가 기능: 절연파괴 부품 검사(배터리 릴레이 전단 → 릴레이 후단 → 모터 순) / 릴레이 융착 점검 / 셀 전압 점검
//  · 고전압 차단 가이드: 매뉴얼 9단계, 시나리오 진행에 맞춰 완료 표시
//  조작: VR — 검지로 화면 버튼을 누른다 / PC·모바일 — 들고 있으면 화면이 크게 열린다(Tab 또는 [진단기 화면] 버튼)
//  화면(페이지)은 서버를 거쳐 모든 사람에게 같게 보인다(GdsSync). 차량 값은 시나리오 진행으로 각자 계산.
// ============================================================
public class GdsTablet : MonoBehaviour
{
    public TMP_FontAsset font;
    [Tooltip("유리 중심(태블릿 로컬) — 유리면(0.0092)과 베젤 윗면(0.0105) 사이에 두어 비스듬히 보면 베젤이 가장자리를 덮는다")]
    public Vector3 screenCenter = new Vector3(0.02775f, 0.0097f, -0.0035f);
    [Tooltip("유리 크기(m) — 메시의 안쪽 유리면 실측")] public Vector2 screenSize = new Vector2(0.2247f, 0.1456f);
    [Tooltip("유리 모서리 반경(캔버스 px)")] public float cornerRadius = 22f;
    [Tooltip("VCI 연결 거리(m) — OBD 단자에 이만큼 가까이 대면 연결")] public float connectDistance = 0.45f;
    [Tooltip("VR: 연결되는 순간 손에서 떼어 OBD 단자 앞에 세워 고정한다(다시 잡으면 들 수 있음)")] public bool dockOnConnectVR = true;
    [Tooltip("거치 위치 = OBD 단자에서 플레이어 쪽으로 이만큼(m)")] public float dockOffset = 0.06f;
    [Tooltip("거치할 때 화면을 위로 젖히는 각도(도)")] public float dockTilt = 25f;

    private const float W = 1280f, H = 800f;
    private const float GlassW = 1300f;          // 유리 가로(px). 패널(1280×800)은 가운데, 남는 가장자리는 마스크 안쪽으로 이어진 배경
    private float GlassH => GlassW * screenSize.y / Mathf.Max(1e-4f, screenSize.x);
    private RectTransform _glass, _topBg, _botBg, _midBg;
    private static Sprite _roundSprite, _edgeSprite;

    // 색
    private static readonly Color Bg = new Color32(13, 20, 31, 255);
    private static readonly Color Bar = new Color32(22, 33, 50, 255);
    private static readonly Color Card = new Color32(30, 44, 66, 255);
    private static readonly Color CardHi = new Color32(40, 60, 90, 255);
    private static readonly Color Accent = new Color32(0, 160, 233, 255);
    private static readonly Color Ok = new Color32(46, 204, 113, 255);
    private static readonly Color Bad = new Color32(255, 82, 82, 255);
    private static readonly Color WarnC = new Color32(255, 176, 0, 255);
    private static readonly Color Dim = new Color32(150, 165, 185, 255);

    private Canvas _canvas;
    private CanvasScaler _scaler;
    private GraphicRaycaster _raycaster;
    private RectTransform _panel, _content;
    private TextMeshProUGUI _vciText, _clock, _title, _footer;
    private Image _vciDot;
    private GameObject _closeBtn;
    private readonly List<Button> _buttons = new List<Button>();
    private readonly Dictionary<Button, bool> _fingerIn = new Dictionary<Button, bool>();
    private float _pokeCd;

    private SyncGrab _sync;
    private TaskItem _item;
    private Transform _obd;
    private float _nextObdSearch;

    // 공유 상태(GdsSync)
    public string Screen { get; private set; } = "home";
    public string Arg { get; private set; } = "";
    public bool Vci { get; private set; }
    public bool Cleared { get; private set; }
    private int _seq;

    // 화면 내부 상태
    private string _shownScreen, _shownArg;
    private float _screenTime;
    private readonly List<Action> _live = new List<Action>();

    // PC·모바일 크게 보기
    private bool _overlay, _overlayWanted = true;
    private bool _prevHeld;
    private Vector3 _worldPos; private Quaternion _worldRot; private Vector3 _worldScale;
    private GameObject _openBtnCanvas;

    private PlatformType Plat => Utils.GetPlatformType();
    private bool LocalHeld => _sync != null && _sync.IsGrabbed && _sync.IsOwner;

    // ══════════════════════════════════════════
    //  생성
    // ══════════════════════════════════════════

    private void Awake()
    {
        _sync = GetComponent<SyncGrab>();
        _item = GetComponent<TaskItem>();
    }

    private void OnEnable() => GdsSync.Register(this);
    private void OnDisable()
    {
        GdsSync.Unregister(this);
        if (_overlay) SetOverlay(false);
        if (_openBtnCanvas != null) _openBtnCanvas.SetActive(false);
        ClearMirror();
    }

    private void OnDestroy() { if (_openBtnCanvas != null) Destroy(_openBtnCanvas); }

    private void Start()
    {
        if (font == null) font = TMP_Settings.defaultFontAsset;
        Build();
        Rebuild();
    }

    private void Build()
    {
        var holder = transform.Find("SM_GDS") ?? transform;
        var go = new GameObject("[GDS Screen]", typeof(RectTransform));
        go.layer = 1;   // TransparentFX — VR 카메라에서도 보이는 레이어
        go.transform.SetParent(holder, false);
        go.transform.localPosition = screenCenter;
        // 화면 위아래: VR 에서 손에 쥔 자세(GrabbablePose) 기준으로 똑바로 보이게 화면 법선 축으로 180도 돌린다
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, 0f, 180f);
        float s = screenSize.x / GlassW;
        go.transform.localScale = new Vector3(s, s, s);
        _worldPos = go.transform.localPosition; _worldRot = go.transform.localRotation; _worldScale = go.transform.localScale;

        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        ((RectTransform)go.transform).sizeDelta = new Vector2(GlassW, GlassH);
        _scaler = go.AddComponent<CanvasScaler>();
        _scaler.dynamicPixelsPerUnit = 2f;
        _raycaster = go.AddComponent<GraphicRaycaster>();
        _raycaster.enabled = false;   // 월드 화면은 VR 검지로만 누른다(PC 클릭이 화면에 걸려 물건을 못 내려놓는 일 방지)

        // 유리: 둥근 모서리 마스크. 안의 그림은 유리 모양대로 잘리고, 바깥은 베젤 아래로 들어간 것처럼 보인다
        _glass = Px(go.transform, "Glass", 0, 0, GlassW, GlassH);
        _glass.anchorMin = _glass.anchorMax = _glass.pivot = new Vector2(0.5f, 0.5f);
        _glass.anchoredPosition = Vector2.zero;
        var gi = Img(_glass, Bg);
        gi.sprite = RoundSprite(); gi.type = Image.Type.Sliced; gi.pixelsPerUnitMultiplier = 32f / Mathf.Max(1f, cornerRadius);
        gi.raycastTarget = true;   // 크게 보기에서 빈 곳을 눌러도 뒤의 월드 클릭(내려놓기)으로 새지 않게
        _glass.gameObject.AddComponent<RectMask2D>();   // 유리 밖으로 나가는 그림은 잘라낸다

        // 상·하단 바 배경: 유리 모서리와 같은 둥근 모양으로 유리 끝까지 채우고, 가운데는 본문 배경으로 덮는다
        _topBg = Px(_glass, "TopBarBg", 0, 0, 0, 0); RoundImg(_topBg, Bar);
        _botBg = Px(_glass, "BottomBarBg", 0, 0, 0, 0); RoundImg(_botBg, Bar);
        _midBg = Px(_glass, "BodyBg", 0, 0, 0, 0); Img(_midBg, Bg);

        _panel = Px(_glass, "Panel", 0, 0, W, H);
        _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 0.5f);
        _panel.anchoredPosition = Vector2.zero;

        // 상단 바
        var bar = Px(_panel, "TopBar", 0, 0, W, 64);
        T(bar, "<b><color=#00A0E9>GDS</color></b> Smart", 30, Color.white, TextAlignmentOptions.Left, 20, 0, 200, 64);
        T(bar, "IONIQ 5 (NE)  2026  ·  EV 160kW / 270kW", 20, Dim, TextAlignmentOptions.Center, 300, 0, 560, 64);
        var dotRt = Px(bar, "VciDot", 900, 24, 16, 16); _vciDot = Img(dotRt, Bad);
        _vciText = T(bar, "VCI 미연결", 20, Color.white, TextAlignmentOptions.Left, 924, 0, 220, 64);
        _clock = T(bar, "", 20, Dim, TextAlignmentOptions.Right, 1120, 0, 140, 64);

        // 제목줄
        _title = T(_panel, "", 30, Color.white, TextAlignmentOptions.Left, 30, 70, 900, 56);
        _title.fontStyle = FontStyles.Bold;

        _content = Px(_panel, "Content", 0, 128, W, 600);

        // 하단 바
        var foot = Px(_panel, "Bottom", 0, 736, W, 64);
        Btn(foot, "◀ 뒤로", Card, 16, 8, 150, 48, Back);
        Btn(foot, "홈", Card, 176, 8, 120, 48, () => Go("home", ""));
        _footer = T(foot, "", 18, Dim, TextAlignmentOptions.Right, 320, 0, 940, 64);

        var close = Btn(_panel, "× 화면 닫기", new Color32(90, 30, 30, 255), 1080, 72, 180, 48, () => { _overlayWanted = false; SetOverlay(false); });
        _closeBtn = close.gameObject;
        _closeBtn.SetActive(false);

        // 유리 가장자리 안쪽 그늘 — 화면이 베젤 아래에서 시작하는 느낌
        var edge = Px(_glass, "EdgeShade", 0, 0, 0, 0);
        edge.anchorMin = Vector2.zero; edge.anchorMax = Vector2.one; edge.offsetMin = edge.offsetMax = Vector2.zero;
        var ei = Img(edge, new Color(0f, 0f, 0f, 0.55f));
        ei.sprite = EdgeSprite(); ei.type = Image.Type.Sliced; ei.pixelsPerUnitMultiplier = 1f;

        LayoutGlass(GlassW, GlassH);
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 1;
    }

    private void RoundImg(RectTransform rt, Color c)
    {
        var i = Img(rt, c);
        i.sprite = RoundSprite(); i.type = Image.Type.Sliced; i.pixelsPerUnitMultiplier = 32f / Mathf.Max(1f, cornerRadius);
    }

    /// <summary>유리 크기를 정하고 바 배경을 유리 가장자리까지 늘린다(패널 1280×800 은 가운데 고정)</summary>
    private void LayoutGlass(float gw, float gh) => LayoutGlass(_glass, _topBg, _botBg, _midBg, gw, gh);

    private void LayoutGlass(RectTransform _glass, RectTransform _topBg, RectTransform _botBg, RectTransform _midBg, float gw, float gh)
    {
        _glass.sizeDelta = new Vector2(gw, gh);
        float dy = (gh - H) * 0.5f;
        float over = cornerRadius + 4f;   // 둥근 아래(위)쪽 모서리는 본문 배경이 덮도록 겹쳐 둔다
        void Band(RectTransform rt, bool top, float h)
        {
            rt.anchorMin = new Vector2(0f, top ? 1f : 0f); rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, h);
        }
        Band(_topBg, true, dy + 64f + over);
        Band(_botBg, false, dy + 64f + over);
        _midBg.anchorMin = new Vector2(0f, 0.5f); _midBg.anchorMax = new Vector2(1f, 0.5f); _midBg.pivot = new Vector2(0.5f, 0.5f);
        _midBg.anchoredPosition = Vector2.zero;
        _midBg.sizeDelta = new Vector2(0f, H - 128f);
    }

    /// <summary>둥근 사각형(반경 32px, 9-슬라이스) — 유리 마스크</summary>
    private static Sprite RoundSprite()
    {
        if (_roundSprite != null) return _roundSprite;
        const int n = 64; const float r = 32f;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, r, n - r), cy = Mathf.Clamp(y + 0.5f, r, n - r);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(r - d + 0.5f)));
            }
        tex.SetPixels32(px); tex.Apply();
        _roundSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        _roundSprite.hideFlags = HideFlags.HideAndDontSave;
        return _roundSprite;
    }

    /// <summary>가장자리 안쪽 그늘(바깥 진하고 안으로 옅어짐, 9-슬라이스) — 반경 22px 모서리에 맞춤</summary>
    private static Sprite EdgeSprite()
    {
        if (_edgeSprite != null) return _edgeSprite;
        const int n = 64; const float b = 30f, r = 22f, fade = 18f;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // 둥근 사각형 테두리까지의 안쪽 거리
                float fx = x + 0.5f, fy = y + 0.5f;
                float cx = Mathf.Clamp(fx, r, n - r), cy = Mathf.Clamp(fy, r, n - r);
                float inside = r - Vector2.Distance(new Vector2(fx, fy), new Vector2(cx, cy));
                float t = 1f - Mathf.Clamp01(inside / fade);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * t * t));
            }
        tex.SetPixels32(px); tex.Apply();
        _edgeSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        _edgeSprite.hideFlags = HideFlags.HideAndDontSave;
        return _edgeSprite;
    }

    /// <summary>에디터 확인용 — 화면을 만들고 지정 페이지를 그린다(플레이 모드 아닐 때)</summary>
    public void PreviewInEditor(string screen, string arg, bool vci, float elapsed)
    {
        if (font == null) font = TMP_Settings.defaultFontAsset;
        if (_canvas == null) Build();
        ApplyState(screen, arg, vci, false);
        Rebuild();
        _screenTime = Time.time - elapsed;
        _vciDot.color = Vci ? Ok : Bad;
        _vciText.text = Vci ? "VCI 연결 (무선)" : "VCI 미연결";
        _clock.text = DateTime.Now.ToString("HH:mm");
        foreach (var a in _live) a();
    }

    // ══════════════════════════════════════════
    //  UI 헬퍼 (좌상단 기준 픽셀 좌표)
    // ══════════════════════════════════════════

    private static RectTransform Px(Transform parent, string name, float x, float y, float w, float h)
    {
        var g = new GameObject(name, typeof(RectTransform));
        g.layer = 1;
        var rt = (RectTransform)g.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    private static Image Img(RectTransform rt, Color c)
    {
        var i = rt.gameObject.AddComponent<Image>();
        i.color = c;
        i.raycastTarget = false;
        return i;
    }

    private TextMeshProUGUI T(Transform parent, string text, float size, Color c, TextAlignmentOptions al, float x, float y, float w, float h)
    {
        var rt = Px(parent, "T", x, y, w, h);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = font; t.text = text; t.fontSize = size; t.color = c; t.alignment = al;
        t.richText = true; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    private Button Btn(Transform parent, string label, Color c, float x, float y, float w, float h, Action onClick, float fontSize = 22)
    {
        var rt = Px(parent, "Btn_" + label, x, y, w, h);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c;
        var b = rt.gameObject.AddComponent<Button>();
        var cb = b.colors; cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f); cb.pressedColor = new Color(0.75f, 0.75f, 0.75f); b.colors = cb;
        b.onClick.AddListener(() => { Click(); onClick(); });
        var t = T(rt, label, fontSize, Color.white, TextAlignmentOptions.Center, 0, 0, w, h);
        t.textWrappingMode = TextWrappingModes.Normal;
        _buttons.Add(b);
        return b;
    }

    private void Click()
    {
        AudioSource.PlayClipAtPoint(EquipFx.Click, transform.position, 0.35f);
    }

    // ══════════════════════════════════════════
    //  상태 · 이동
    // ══════════════════════════════════════════

    private readonly Stack<string> _history = new Stack<string>();

    public void Go(string screen, string arg)
    {
        if (screen != Screen || arg != Arg) _history.Push(Screen + "|" + Arg);
        GdsSync.Request(screen, arg, Vci, Cleared);
    }

    private void Back()
    {
        string prev = _history.Count > 0 ? _history.Pop() : "home|";
        int bar = prev.IndexOf('|');
        GdsSync.Request(prev.Substring(0, bar), prev.Substring(bar + 1), Vci, Cleared);
    }

    public void ApplyState(string screen, string arg, bool vci, bool cleared)
    {
        Screen = string.IsNullOrEmpty(screen) ? "home" : screen;
        Arg = arg ?? "";
        Vci = vci;
        Cleared = cleared;
    }

    // ══════════════════════════════════════════
    //  매 프레임
    // ══════════════════════════════════════════

    private void Update()
    {
        if (_canvas == null) return;

        UpdateVci();
        UpdateOverlay();

        if (Screen != _shownScreen || Arg != _shownArg) Rebuild();

        _vciDot.color = Vci ? (GdsModel.CommLost ? WarnC : Ok) : Bad;
        _vciText.text = Vci ? (GdsModel.CommLost ? "VCI 연결 · 차량 통신 없음" : "VCI 연결 (무선)") : "VCI 미연결";
        _clock.text = DateTime.Now.ToString("HH:mm");
        foreach (var a in _live) a();

        if (!_overlay) PokeVR();
    }

    private void UpdateVci()
    {
        if (!LocalHeld) return;   // 들고 있는 사람이 판정해서 모두에게 알린다
        if (_obd == null && Time.unscaledTime >= _nextObdSearch)
        {
            _nextObdSearch = Time.unscaledTime + 2f;
            _obd = FindObd();
        }
        if (_obd == null) return;
        float d = Vector3.Distance(transform.position, _obd.position);
        bool want = Vci ? d < 12f : d < connectDistance;
        if (want == Vci) return;
        GdsSync.Request(Screen, Arg, want, Cleared);
        if (want)
        {
            EquipFx.Notice("gds_vci", "VCI 연결됨", "차량과 통신을 시작합니다 — 원클릭 진단으로 전체 시스템을 검사하세요", EquipFx.Info, 3f, 5f);
            if (dockOnConnectVR && Plat == PlatformType.VR) DockVR();
            // 연결만으로 '진단기 연결' 단계를 끝내지 않는다 — 원클릭 진단을 마치면(PageScan) 단계 신호를 보낸다
        }
    }

    // ── VR: 연결 순간 OBD 단자 자리에 거치 ──
    // 연결된 진단기를 계속 들고 있어야 해서 두 손이 묶였다 — 단자 앞에 화면이 나를 향하게 세워 두고 검지로 누른다
    private static GdsTablet s_docking;
    private static bool s_dockReg;
    private static float s_dockAt;
    private Vector3 _dockPos;
    private Quaternion _dockRot;

    private void DockVR()
    {
        if (_obd == null || _sync == null || !LocalHeld) return;
        var cam = Camera.main;
        Vector3 to = cam != null ? cam.transform.position - _obd.position : -_obd.forward;
        to.y = 0f;
        if (to.sqrMagnitude < 1e-4f) to = Vector3.forward;
        to.Normalize();
        float a = dockTilt * Mathf.Deg2Rad;
        Vector3 normal = (to * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)).normalized;   // 화면면(+Y)이 향할 방향
        Vector3 screenUp = Vector3.ProjectOnPlane(Vector3.up, normal).normalized;      // 화면 글자 위쪽 = 태블릿 -Z
        _dockRot = Quaternion.LookRotation(-screenUp, normal);
        _dockPos = _obd.position + to * dockOffset;

        if (!s_dockReg)
        {
            s_dockReg = true;
            // 도구 벨트·자석 받침보다 먼저 가로챈다
            SyncGrab.VrReleaseInterceptors.Insert(0, (sg, hand) =>
            {
                var me = s_docking;
                if (me == null || sg == null || sg.gameObject != me.gameObject) return false;
                if (Time.time - s_dockAt > 1f) { s_docking = null; return false; }   // 연결 직후 강제로 뗀 경우만
                s_docking = null;
                me.PlaceAtDock();
                return true;
            });
        }
        s_docking = this;
        s_dockAt = Time.time;
        var g = GetComponent<Autohand.Grabbable>() ?? GetComponentInParent<Autohand.Grabbable>();
        if (g != null) g.ForceHandsRelease();
        EquipFx.Notice("gds_dock", "진단기 거치", "OBD 단자 앞에 진단기를 세워 두었습니다 — 화면을 검지로 누르세요. 다시 잡으면 들 수 있습니다", EquipFx.Info, 3.5f, 5f);
    }

    private void PlaceAtDock()
    {
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            rb.isKinematic = true;
        }
        transform.SetPositionAndRotation(_dockPos, _dockRot);
        _sync.ReleaseAndPlace(_dockPos, _dockRot);
        AudioSource.PlayClipAtPoint(EquipFx.Click, _dockPos, 0.7f);
    }

    private static Transform FindObd()
    {
        foreach (var z in FindObjectsByType<TaskInteractionZone>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (z != null && z.zoneId == "OBD_Zone") return z.transform;
        foreach (var n in new[] { "OBD_Port", "Guide_OBD_Port", "OBD_Zone" })
        {
            var g = GameObject.Find(n);
            if (g != null) return g.transform;
        }
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == "OBD_Port" || t.name == "Guide_OBD_Port") return t;
        return null;
    }

    // ── VR: 검지로 누르기 ──
    private void PokeVR()
    {
        _pokeCd -= Time.deltaTime;
        var hands = LocalHands.All;
        if (hands == null || hands.Count == 0) return;
        float s = Mathf.Max(1e-6f, _canvas.transform.lossyScale.x);
        foreach (var b in _buttons)
        {
            if (b == null || !b.isActiveAndEnabled || !b.interactable) continue;
            var rt = (RectTransform)b.transform;
            bool inside = false;
            foreach (var h in hands)
            {
                if (h == null) continue;
                var tip = LocalHands.IndexTip(h);
                if (tip == null) continue;
                Vector3 lp = rt.InverseTransformPoint(tip.position);
                float depth = -lp.z * s;   // 화면 앞쪽(+) 거리(m)
                if (depth < -0.01f || depth > 0.012f) continue;
                if (!rt.rect.Contains(new Vector2(lp.x, lp.y))) continue;
                inside = true;
                if (!(_fingerIn.TryGetValue(b, out bool was) && was) && _pokeCd <= 0f)
                {
                    _pokeCd = 0.3f;
                    EquipFx.Haptic(h, 0.02f, 0.35f);
                    _fingerIn[b] = true;
                    b.onClick.Invoke();
                    return;   // 화면이 바뀌었을 수 있다
                }
            }
            _fingerIn[b] = inside;
        }
    }

    // ── PC·모바일: 크게 보기 동안 손에 든 모델 화면 ──
    // 화면 캔버스 하나를 크게 보기(오버레이)로 옮겨 쓰기 때문에, 그동안 모델 유리는 비어 보였다.
    // 크게 보기 중엔 지금 화면을 복사해 모델 유리에 붙여 둔다(0.5초마다 갱신, 누르기는 원래 화면에서만).
    private GameObject _mirror;
    private float _mirrorAt;

    private void UpdateMirror()
    {
        if (!_overlay) { ClearMirror(); return; }
        if (_mirror != null && Time.unscaledTime < _mirrorAt) return;
        _mirrorAt = Time.unscaledTime + 0.5f;
        ClearMirror();

        var src = _canvas.transform;
        var holder = src.parent;
        string pg = RelPath(src, _glass), pt = RelPath(src, _topBg), pb = RelPath(src, _botBg), pm = RelPath(src, _midBg), pc = _closeBtn != null ? RelPath(src, _closeBtn.transform) : null;

        _mirror = Instantiate(src.gameObject, holder, false);
        _mirror.name = "[GDS Screen Mirror]";
        foreach (var r in _mirror.GetComponents<GraphicRaycaster>()) Destroy(r);
        var c = _mirror.GetComponent<Canvas>();
        c.renderMode = RenderMode.WorldSpace;
        c.sortingOrder = 0;
        var sc = _mirror.GetComponent<CanvasScaler>();
        if (sc != null) sc.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        var rt = (RectTransform)_mirror.transform;
        rt.sizeDelta = new Vector2(GlassW, GlassH);
        rt.localPosition = _worldPos; rt.localRotation = _worldRot; rt.localScale = _worldScale;
        var g = rt.Find(pg) as RectTransform;
        if (g != null) LayoutGlass(g, rt.Find(pt) as RectTransform, rt.Find(pb) as RectTransform, rt.Find(pm) as RectTransform, GlassW, GlassH);
        if (pc != null) { var cb = rt.Find(pc); if (cb != null) cb.gameObject.SetActive(false); }
        foreach (var t in _mirror.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 1;
    }

    private void ClearMirror()
    {
        if (_mirror != null) Destroy(_mirror);
        _mirror = null;
    }

    private static string RelPath(Transform root, Transform t)
    {
        if (t == null || t == root) return "";
        string path = t.name;
        for (var p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    // ── PC·모바일: 크게 보기 ──
    private void UpdateOverlay()
    {
        bool held = LocalHeld && Plat != PlatformType.VR;
        if (held && !_prevHeld) _overlayWanted = true;
        _prevHeld = held;
        if (held && Plat == PlatformType.PC && Input.GetKeyDown(KeyCode.Tab)) _overlayWanted = !_overlayWanted;
        bool want = held && _overlayWanted;
        if (want != _overlay) SetOverlay(want);
        UpdateMirror();

        bool showOpen = held && !_overlay;
        if (showOpen && _openBtnCanvas == null) BuildOpenButton();
        if (_openBtnCanvas != null && _openBtnCanvas.activeSelf != showOpen) _openBtnCanvas.SetActive(showOpen);
    }

    private void SetOverlay(bool on)
    {
        _overlay = on;
        var rt = (RectTransform)_canvas.transform;
        if (on)
        {
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 60;
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1600f, 1000f);
            _scaler.matchWidthOrHeight = 0.5f;
            _raycaster.enabled = true;
            _closeBtn.SetActive(true);
            LayoutGlass(W, H);   // 크게 보기: 패널 크기 그대로(둥근 모서리만 남김)
            if (Plat == PlatformType.PC) EquipFx.Notice("gds_tab", "GDS 진단기", "화면을 마우스로 누르세요. Tab = 화면 닫기/열기", EquipFx.Info, 3f, 30f);
        }
        else
        {
            _canvas.renderMode = RenderMode.WorldSpace;
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            _raycaster.enabled = false;
            _closeBtn.SetActive(false);
            rt.sizeDelta = new Vector2(GlassW, GlassH);
            LayoutGlass(GlassW, GlassH);
            rt.localPosition = _worldPos; rt.localRotation = _worldRot; rt.localScale = _worldScale;
        }
    }

    private void BuildOpenButton()
    {
        _openBtnCanvas = new GameObject("[GDS Open]", typeof(RectTransform));
        var c = _openBtnCanvas.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 55;
        var sc = _openBtnCanvas.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1600f, 1000f); sc.matchWidthOrHeight = 0.5f;
        _openBtnCanvas.AddComponent<GraphicRaycaster>();
        var rt = Px(_openBtnCanvas.transform, "Open", 0, 0, 230, 64);
        if (Plat == PlatformType.Mobile)
        {
            // 모바일: 왼쪽 아래는 조이스틱 자리 → 왼쪽 가운데 위쪽으로
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(24f, 170f);
        }
        else
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(24f, 150f);
        }
        var img = rt.gameObject.AddComponent<Image>(); img.color = Accent;
        var b = rt.gameObject.AddComponent<Button>();
        b.onClick.AddListener(() => { _overlayWanted = true; });
        T(rt, Plat == PlatformType.PC ? "진단기 화면 (Tab)" : "진단기 화면", 24, Color.white, TextAlignmentOptions.Center, 0, 0, 230, 64);
    }

    // ══════════════════════════════════════════
    //  화면 구성
    // ══════════════════════════════════════════

    private void Rebuild()
    {
        _shownScreen = Screen; _shownArg = Arg;
        _screenTime = Time.time;
        _live.Clear();
        for (int i = _content.childCount - 1; i >= 0; i--)
        {
            var c = _content.GetChild(i);
            foreach (var b in c.GetComponentsInChildren<Button>(true)) { _buttons.Remove(b); _fingerIn.Remove(b); }
            if (Application.isPlaying) Destroy(c.gameObject); else DestroyImmediate(c.gameObject);
        }

        switch (Screen)
        {
            case "scan": PageScan(); break;
            case "dtc": PageDtc(); break;
            case "dtcinfo": PageDtcInfo(); break;
            case "data": PageData(); break;
            case "func": PageFunc(); break;
            case "run": PageRun(); break;
            case "guide": PageGuide(); break;
            case "info": PageInfo(); break;
            default: PageHome(); break;
        }
        foreach (var t in _content.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 1;
    }

    private bool NeedVci(string what)
    {
        if (!Vci)
        {
            Box(_content, 140, 90, 1000, 360, $"<size=34><b>VCI 미연결</b></size>\n\n{what}을(를) 하려면 진단기를 차량에 연결해야 합니다.\n\n" +
                "태블릿을 <color=#00A0E9><b>운전석 하단 OBD-II 자기 진단 커넥터(DLC)</b></color>에 가까이 대세요.\n연결 후에는 무선으로 유지됩니다.", WarnC);
            _footer.text = "매뉴얼: 진단 기기를 자기 진단 커넥터(DLC)에 연결한다";
            return false;
        }
        if (GdsModel.CommLost)
        {
            Box(_content, 140, 90, 1000, 380, "<size=34><b>차량 통신 없음</b></size>\n\n고전압 차단 상태(서비스 플러그 분리 · 12V 배터리 (−) 분리)라 제어기와 통신할 수 없습니다.\n\n" +
                "통신이 필요하면 복원 후 <b>POWER ON (EV READY 금지)</b> 상태에서 다시 시도하세요.", WarnC);
            _footer.text = "고전압 차단 중에는 진단 통신이 되지 않습니다";
            return false;
        }
        return true;
    }

    private void Box(Transform parent, float x, float y, float w, float h, string text, Color edge)
    {
        var bg = Px(parent, "Box", x, y, w, h); Img(bg, Card);
        var e = Px(bg, "Edge", 0, 0, 8, h); Img(e, edge);
        T(bg, text, 24, Color.white, TextAlignmentOptions.TopLeft, 36, 24, w - 60, h - 40);
    }

    // ── 홈 ──
    private void PageHome()
    {
        _title.text = "메인 메뉴";
        _footer.text = Vci ? "차량 연결됨 — 원하는 기능을 고르세요" : "태블릿을 차량 OBD-II 단자에 대면 VCI 가 연결됩니다";
        var tiles = new (string t, string s, string go, Color c)[]
        {
            ("원클릭 전체 진단", "모든 제어기 고장 코드 검색", "scan", Accent),
            ("고장 코드 (DTC)", "코드 · 고장 시 데이터 · 소거", "dtc", new Color32(231, 76, 60, 255)),
            ("센서 데이터", "BMS 실시간 값 · 규정값 비교", "data", new Color32(39, 174, 96, 255)),
            ("부가 기능", "절연파괴 부품 검사 · 융착 점검", "func", new Color32(142, 68, 173, 255)),
            ("고전압 차단 가이드", "정비 매뉴얼 순서 · 진행 확인", "guide", new Color32(230, 126, 34, 255)),
            ("차량 정보", "VIN · 배터리 · 제어기 버전", "info", new Color32(52, 73, 94, 255)),
        };
        for (int i = 0; i < tiles.Length; i++)
        {
            int col = i % 3, row = i / 3;
            float x = 40 + col * 404, y = 20 + row * 290;
            var tile = tiles[i];
            var b = Btn(_content, "", Card, x, y, 384, 270, () => Go(tile.go, ""));
            var bar = Px(b.transform, "Stripe", 0, 0, 384, 12); Img(bar, tile.c);
            T(b.transform, $"<b>{tile.t}</b>", 34, Color.white, TextAlignmentOptions.Center, 10, 70, 364, 70);
            T(b.transform, tile.s, 22, Dim, TextAlignmentOptions.Center, 10, 150, 364, 60);
        }
    }

    // ── 원클릭 전체 진단 ──
    private void PageScan()
    {
        _title.text = "원클릭 전체 진단";
        if (!NeedVci("전체 진단")) return;
        string code = GdsModel.FaultPresent || !Cleared ? GdsModel.DtcCode : "";
        bool fault = GdsModel.FaultPresent || (!Cleared && !string.IsNullOrEmpty(code));
        var dtc = GdsModel.CurrentDtc;

        var rows = new List<TextMeshProUGUI>();
        for (int i = 0; i < GdsModel.Systems.Length; i++)
        {
            int col = i < 6 ? 0 : 1, row = i < 6 ? i : i - 6;
            var bg = Px(_content, "Row", 40 + col * 610, 10 + row * 70, 590, 60); Img(bg, Card);
            T(bg, GdsModel.Systems[i], 22, Color.white, TextAlignmentOptions.Left, 20, 0, 380, 60);
            rows.Add(T(bg, "대기", 22, Dim, TextAlignmentOptions.Right, 380, 0, 190, 60));
        }
        var prog = Px(_content, "Prog", 40, 440, 1200, 14); Img(prog, Card);
        var fill = Px(prog, "Fill", 0, 0, 0, 14); Img(fill, Accent);
        var summary = T(_content, "", 26, Color.white, TextAlignmentOptions.Left, 40, 470, 900, 110);
        Button go = null;
        if (fault)
        {
            go = Btn(_content, "고장 코드 보기 ▶", new Color32(231, 76, 60, 255), 980, 480, 260, 80, () => Go("dtc", ""));
            go.gameObject.SetActive(false);
        }
        float per = 0.28f;
        bool doneSound = false;
        _live.Add(() =>
        {
            float t = Time.time - _screenTime;
            int n = Mathf.Min(GdsModel.Systems.Length, Mathf.FloorToInt(t / per));
            fill.sizeDelta = new Vector2(1200f * Mathf.Clamp01(t / (per * GdsModel.Systems.Length)), 14);
            for (int i = 0; i < rows.Count; i++)
            {
                bool isFaultSys = fault && GdsModel.Systems[i].StartsWith(dtc.system);
                if (i < n) { rows[i].text = isFaultSys ? "<color=#FF5252><b>고장 1</b></color>" : "<color=#2ECC71>정상</color>"; }
                else if (i == n) rows[i].text = "<color=#00A0E9>검사 중…</color>";
            }
            if (n >= rows.Count)
            {
                summary.text = fault
                    ? $"<color=#FF5252><b>고장 코드 1개 검출</b></color>  ·  {dtc.system}\n<size=22>{code}  {dtc.name}</size>"
                    : "<color=#2ECC71><b>모든 시스템 정상</b></color>\n<size=22>검출된 고장 코드가 없습니다</size>";
                if (go != null) go.gameObject.SetActive(true);
                if (!doneSound)
                {
                    doneSound = true;
                    AudioSource.PlayClipAtPoint(EquipFx.Click, transform.position, 0.6f);
                    // 다음 단계로 넘어가는 조건을 화면 밖에서도 알려 준다(무엇을 눌러야 끝나는지 모르겠다는 의견)
                    if (fault && GdsModel.CurrentModule == "InitialDiagnosis")
                        EquipFx.Notice("gds_next", "진단 결과 확인", $"[고장 코드 보기] → {code} 를 눌러 상세 정보를 열면 다음 단계로 넘어갑니다", EquipFx.Info, 5f, 3f);
                }
                _footer.text = fault ? "검사 완료 — 고장 코드를 눌러 원인을 확인하세요" : "검사 완료";
            }
            else _footer.text = $"제어기 통신 중… {n + 1}/{rows.Count}";
        });
    }

    // ── 고장 코드 ──
    private void PageDtc()
    {
        _title.text = "고장 코드 (DTC)";
        if (!NeedVci("고장 코드 판독")) return;
        var dtc = GdsModel.CurrentDtc;
        bool hasCode = !string.IsNullOrEmpty(dtc.code) && (GdsModel.FaultPresent || !Cleared);
        if (!hasCode)
        {
            Box(_content, 140, 80, 1000, 240, "<size=34><color=#2ECC71><b>고장 코드 없음</b></color></size>\n\n모든 제어기에 저장된 고장 코드가 없습니다." +
                (Cleared ? "\n소거 후 재검색 결과 재발생하지 않았습니다 — 정비 완료." : ""), Ok);
            Btn(_content, "재검색", Accent, 140, 360, 240, 80, () => Go("scan", ""));
            _footer.text = "System NORMAL";
            if (GdsModel.CurrentModule == "FinalDiagnosis")
                FireObd();
            return;
        }

        // 헤더
        var head = Px(_content, "Head", 40, 0, 1200, 50); Img(head, Bar);
        T(head, "<b>코드</b>", 22, Dim, TextAlignmentOptions.Left, 20, 0, 140, 50);
        T(head, "<b>고장 내용</b>", 22, Dim, TextAlignmentOptions.Left, 170, 0, 640, 50);
        T(head, "<b>제어기</b>", 22, Dim, TextAlignmentOptions.Left, 830, 0, 140, 50);
        T(head, "<b>상태</b>", 22, Dim, TextAlignmentOptions.Left, 990, 0, 200, 50);

        var row = Btn(_content, "", Card, 40, 56, 1200, 76, () => Go("dtcinfo", dtc.code));
        T(row.transform, $"<b><color=#FF5252>{dtc.code}</color></b>", 26, Color.white, TextAlignmentOptions.Left, 20, 0, 140, 76);
        T(row.transform, dtc.name, 24, Color.white, TextAlignmentOptions.Left, 170, 0, 640, 76);
        T(row.transform, dtc.system, 22, Color.white, TextAlignmentOptions.Left, 830, 0, 140, 76);
        T(row.transform, GdsModel.FaultPresent ? "<color=#FF5252>현재 고장</color> ▶" : "과거 고장 ▶", 22, Color.white, TextAlignmentOptions.Left, 990, 0, 200, 76);

        if (Cleared && GdsModel.FaultPresent)
            Box(_content, 40, 150, 1200, 120, "<color=#FFB000><b>소거 후 재검출되었습니다.</b></color>  고장 원인이 그대로 남아 있습니다 — 점검 절차대로 원인 부품을 찾아 수리한 뒤 소거하세요.", WarnC);
        else
            T(_content, "코드를 누르면 상세 정보 · 고장 시 데이터 · 점검 절차를 볼 수 있습니다.", 22, Dim, TextAlignmentOptions.Left, 40, 160, 1200, 50);

        Btn(_content, "상세 정보", Accent, 40, 480, 240, 80, () => Go("dtcinfo", dtc.code));
        Btn(_content, "고장 코드 소거", new Color32(192, 57, 43, 255), 300, 480, 260, 80, ClearCodes);
        Btn(_content, "재검색", Card, 580, 480, 200, 80, () => Go("scan", ""));
        _footer.text = "소거는 수리를 마친 뒤 — 원인이 남아 있으면 바로 다시 검출됩니다";
    }

    /// <summary>OBD 존(진단 단계) 완료 신호 — 이 진단기 화면에서 낸 신호만 통과한다(NetworkInteractionBridge 가 OBD 접촉·E키 신호는 막는다)</summary>
    public static bool ObdSignalOpen { get; private set; }
    private void FireObd()
    {
        ObdSignalOpen = true;
        try { InteractionEvents.FireZoneActivated("OBD_Zone", _item != null ? _item.prefabId : "GDS_Tablet"); }
        finally { ObdSignalOpen = false; }
    }

    private void ClearCodes()
    {
        bool ok = !GdsModel.FaultPresent;
        GdsSync.Request("dtc", "", Vci, true);
        if (ok)
        {
            EquipFx.Notice("gds_clear", "고장 코드 소거 완료", "재검색에서도 고장 코드가 나오지 않으면 정비 완료입니다", EquipFx.Info, 3f, 2f);
            if (GdsModel.CurrentModule == "FinalDiagnosis")
                FireObd();
        }
        else
            EquipFx.Notice("gds_clear", "소거 직후 재검출", "고장 원인이 남아 있어 코드가 다시 기록됐습니다 — 먼저 수리하세요", EquipFx.Warn, 3.5f, 2f);
    }

    private void PageDtcInfo()
    {
        var d = GdsModel.CurrentDtc;
        _title.text = $"{d.code}  {d.name}";
        if (!NeedVci("고장 코드 상세")) return;

        // 시나리오 '초기 진단' 단계: 연결 → 원클릭 진단 → 고장 코드 상세(원인·점검 절차)까지 확인해야 완료
        if (GdsModel.CurrentModule == "InitialDiagnosis" && GdsModel.FaultPresent)
        {
            FireObd();
            EquipFx.Notice("gds_cause", "고장 원인 확인", $"{d.code} {d.name} — 점검 절차대로 원인 부품을 수리한 뒤 고장 코드를 소거하세요", EquipFx.Info, 4f, 5f);
        }

        // 왼쪽: 정보 + 고장 시 데이터
        var info = Px(_content, "Info", 40, 0, 560, 580); Img(info, Card);
        T(info, $"<b>제어기</b>  {d.system}\n<b>상태</b>  {(GdsModel.FaultPresent ? "<color=#FF5252>현재 고장</color>" : "과거 고장")}\n<b>증상</b>  {d.symptom}\n<b>판정 조건</b>  {d.condition}",
            22, Color.white, TextAlignmentOptions.TopLeft, 24, 18, 520, 190);
        T(info, "<b><color=#00A0E9>고장 시 데이터 (Freeze Frame)</color></b>", 22, Color.white, TextAlignmentOptions.Left, 24, 210, 520, 40);
        string ff =
            $"배터리 충전 상태(SOC) ..... {GdsModel.Soc:0.0} %\n" +
            $"배터리 팩 전압 ................. {GdsModel.PackVoltage:0.0} V\n" +
            $"절연 저항 .......................... <color=#FF5252>{(d.code == "P0AA6" ? 176 : 2400)} kΩ</color>\n" +
            "배터리 최고/최저 온도 ....... 26 / 23 °C\n" +
            "차속 ...................................... 0 km/h\n" +
            "누적 주행 거리 ..................... 18,452 km";
        T(info, ff, 21, Color.white, TextAlignmentOptions.TopLeft, 24, 250, 520, 320);

        // 오른쪽: 점검 절차(매뉴얼)
        var proc = Px(_content, "Proc", 620, 0, 620, 580); Img(proc, Card);
        T(proc, "<b><color=#FFB000>점검 절차 (정비 매뉴얼)</color></b>", 22, Color.white, TextAlignmentOptions.Left, 24, 12, 580, 40);
        string steps = d.code == "P0AA6"
            ? "1. 부가기능 <b>'절연파괴 부품 검사'</b>로 부위 확인\n    (배터리 릴레이 전단 → 릴레이 후단 → 모터)\n" +
              "2. 서비스 데이터 '절연 저항' 확인  <size=18>규정 500 kΩ 이상</size>\n" +
              "3. <b>고전압 차단 절차</b> 수행 (3분 이상 대기)\n" +
              "4. 배터리 시스템 탈거 → 상부 케이스 탈거\n" +
              "5. 메가옴 테스터 1000V, (−) 케이스 / (+) PRA 단자\n    약 1분 후 <b>2 MΩ 이상</b> (20°C)\n" +
              "6. 불량 부품(PRA) 교환 → 조립\n" +
              "7. 고장 코드 소거 → 재검색으로 재발생 확인"
            : "1. 서비스 데이터로 관련 값 확인\n2. 고전압 차단 절차 수행\n3. 관련 커넥터·배선 점검\n4. 불량 부품 교환\n5. 고장 코드 소거 후 재검색";
        T(proc, steps, 20, Color.white, TextAlignmentOptions.TopLeft, 24, 56, 580, 440);
        Btn(proc, "부가 기능 ▶", new Color32(142, 68, 173, 255), 24, 500, 270, 64, () => Go("func", ""));
        Btn(proc, "센서 데이터 ▶", new Color32(39, 174, 96, 255), 314, 500, 280, 64, () => Go("data", ""));
        _footer.text = "매뉴얼: 배터리 제어 시스템 > 절연 저항 점검";
    }

    // ── 센서 데이터 ──
    private void PageData()
    {
        _title.text = "센서 데이터 — BMS (배터리 관리 시스템)";
        if (!NeedVci("센서 데이터 확인")) return;
        var head = Px(_content, "Head", 40, 0, 1200, 44); Img(head, Bar);
        T(head, "<b>항목</b>", 20, Dim, TextAlignmentOptions.Left, 20, 0, 480, 44);
        T(head, "<b>값</b>", 20, Dim, TextAlignmentOptions.Right, 500, 0, 300, 44);
        T(head, "<b>단위</b>", 20, Dim, TextAlignmentOptions.Left, 820, 0, 100, 44);
        T(head, "<b>규정값</b>", 20, Dim, TextAlignmentOptions.Left, 940, 0, 240, 44);

        var items = GdsModel.BmsData();
        var vals = new List<TextMeshProUGUI>();
        var names = new List<TextMeshProUGUI>();
        for (int i = 0; i < items.Count; i++)
        {
            float y = 48 + i * 33;
            var bg = Px(_content, "R", 40, y, 1200, 31); Img(bg, i % 2 == 0 ? Card : (Color)new Color32(26, 38, 57, 255));
            names.Add(T(bg, items[i].name, 19, Color.white, TextAlignmentOptions.Left, 20, 0, 480, 31));
            vals.Add(T(bg, items[i].value, 19, Color.white, TextAlignmentOptions.Right, 500, 0, 300, 31));
            T(bg, items[i].unit, 19, Dim, TextAlignmentOptions.Left, 820, 0, 100, 31);
            T(bg, items[i].spec, 19, Dim, TextAlignmentOptions.Left, 940, 0, 240, 31);
        }
        float next = 0f;
        _live.Add(() =>
        {
            if (Time.time < next) return;
            next = Time.time + 0.5f;
            var cur = GdsModel.BmsData();
            for (int i = 0; i < cur.Count && i < vals.Count; i++)
            {
                vals[i].text = cur[i].bad ? $"<color=#FF5252><b>{cur[i].value}</b></color>" : cur[i].value;
                names[i].color = cur[i].bad ? Bad : Color.white;
            }
        });
        _footer.text = "POWER ON (EV READY 금지) 상태 · 0.5초마다 갱신 · 규정값을 벗어나면 빨강";
    }

    // ── 부가 기능 ──
    private void PageFunc()
    {
        _title.text = "부가 기능";
        if (!NeedVci("부가 기능")) return;
        var funcs = new (string id, string t, string s)[]
        {
            ("insul", "절연파괴 부품 검사", "고전압 절연파괴(P0AA6) 발생 부위를 3구역으로 구별"),
            ("weld", "BMS 릴레이 융착 점검", "메인 릴레이·프리차지 릴레이 융착 여부"),
            ("cell", "셀 전압 · 편차 점검", "192셀 최대/최소 전압과 편차(40 mV 이하)"),
        };
        for (int i = 0; i < funcs.Length; i++)
        {
            var f = funcs[i];
            var b = Btn(_content, "", Card, 40, 10 + i * 130, 1200, 116, () => Go("run", f.id + ":0"));
            T(b.transform, $"<b>{f.t}</b>", 30, Color.white, TextAlignmentOptions.Left, 30, 10, 900, 56);
            T(b.transform, f.s, 22, Dim, TextAlignmentOptions.Left, 30, 62, 1000, 44);
            T(b.transform, "실행 ▶", 24, Accent, TextAlignmentOptions.Right, 1000, 0, 170, 116);
        }
        _footer.text = "부가 기능은 POWER ON (EV READY 금지) 상태에서 실행합니다";
    }

    private void PageRun()
    {
        string id = Arg, phase = "0";
        int c = Arg.IndexOf(':');
        if (c >= 0) { id = Arg.Substring(0, c); phase = Arg.Substring(c + 1); }
        string name = id == "insul" ? "절연파괴 부품 검사" : id == "weld" ? "BMS 릴레이 융착 점검" : "셀 전압 · 편차 점검";
        _title.text = name;
        if (!NeedVci(name)) return;

        if (phase == "0")
        {
            Box(_content, 140, 40, 1000, 300, "<size=30><b>실행 조건 확인</b></size>\n\n• 차량 상태: <b>POWER ON</b> (EV READY 금지)\n• 변속 레버 P, 주차 브레이크 작동\n• 검사 중에는 차량 조작 금지\n\n조건을 만족하면 [확인]을 누르세요.", Accent);
            Btn(_content, "확인", Accent, 440, 380, 200, 80, () => Go("run", id + ":1"));
            Btn(_content, "취소", Card, 660, 380, 200, 80, Back);
            _footer.text = "매뉴얼: 진단 기기를 DLC 에 연결 → POWER ON (EV READY 금지)";
            return;
        }

        // 실행 중 → 결과
        var stages = id == "insul"
            ? new[] { "고전압 배터리 (릴레이 전단)", "고전압 입력부 (릴레이 후단)", "구동 모터" }
            : id == "weld" ? new[] { "메인 릴레이 (+)", "메인 릴레이 (−)", "프리차지 릴레이" }
            : new[] { "모듈 1~4", "모듈 5~8", "모듈 9~12" };
        var rows = new List<TextMeshProUGUI>();
        for (int i = 0; i < stages.Length; i++)
        {
            var bg = Px(_content, "S", 140, 20 + i * 80, 1000, 68); Img(bg, Card);
            T(bg, $"{i + 1}.  {stages[i]}", 26, Color.white, TextAlignmentOptions.Left, 24, 0, 600, 68);
            rows.Add(T(bg, "대기", 24, Dim, TextAlignmentOptions.Right, 600, 0, 370, 68));
        }
        var prog = Px(_content, "Prog", 140, 270, 1000, 14); Img(prog, Card);
        var fill = Px(prog, "Fill", 0, 0, 0, 14); Img(fill, Accent);
        var result = T(_content, "", 24, Color.white, TextAlignmentOptions.TopLeft, 140, 300, 1000, 280);
        const float per = 2.2f;
        bool fault = GdsModel.FaultPresent && GdsModel.DtcCode == "P0AA6";
        bool done = false;
        _live.Add(() =>
        {
            float t = Time.time - _screenTime;
            int n = Mathf.Min(stages.Length, Mathf.FloorToInt(t / per));
            fill.sizeDelta = new Vector2(1000f * Mathf.Clamp01(t / (per * stages.Length)), 14);
            for (int i = 0; i < rows.Count; i++)
            {
                if (i < n)
                {
                    bool bad = id == "insul" && fault && i == 0;
                    string v = id == "insul" ? (bad ? $"<color=#FF5252><b>절연파괴 검출</b> ({GdsModel.InsulationK:0} kΩ)</color>" : "<color=#2ECC71>정상 (3000 kΩ 이상)</color>")
                             : id == "weld" ? "<color=#2ECC71>융착 없음</color>"
                             : $"<color=#2ECC71>편차 {12 + i * 3} mV · 정상</color>";
                    rows[i].text = v;
                }
                else if (i == n) rows[i].text = id == "insul" ? "<color=#00A0E9>절연 저항 측정 중…</color>" : "<color=#00A0E9>검사 중…</color>";
            }
            _footer.text = n < stages.Length ? "검사 중 — 차량을 조작하지 마세요" : "검사 완료";
            if (n >= stages.Length && !done)
            {
                done = true;
                AudioSource.PlayClipAtPoint(EquipFx.Click, transform.position, 0.6f);
                if (id == "insul")
                    result.text = fault
                        ? "<color=#FF5252><b>판정: 고전압 배터리(릴레이 전단) 절연파괴</b></color>\n배터리 팩 내부(PRA 쪽) 절연 불량이 의심됩니다.\n→ 고전압 차단 후 배터리 탈거 · 상부 케이스 탈거 → 메가옴 테스터로 PRA 단자 절연 저항 측정(1000V, 2 MΩ 이상)"
                        : "<color=#2ECC71><b>판정: 절연파괴 부위 없음</b></color>\n세 구역 모두 정상입니다.";
                else if (id == "weld")
                    result.text = "<color=#2ECC71><b>판정: Relay Welding not detection</b></color>\n릴레이 융착이 없습니다 — 고전압 차단 절차를 진행할 수 있습니다.";
                else
                    result.text = $"<color=#2ECC71><b>판정: 정상</b></color>\n최대 {GdsModel.CellMax:0.000} V · 최소 {GdsModel.CellMin:0.000} V · 편차 {(GdsModel.CellMax - GdsModel.CellMin) * 1000f:0} mV (규정 40 mV 이하)";
            }
        });
    }

    // ── 고전압 차단 가이드 ──
    private void PageGuide()
    {
        _title.text = "고전압 차단 가이드 (정비 매뉴얼)";
        var steps = new (string text, string module)[]
        {
            ("진단기를 자기 진단 커넥터(DLC)에 연결", "InitialDiagnosis"),
            ("POWER ON → BMS 융착 상태 확인 (Relay Welding not detection)", "InitialDiagnosis"),
            ("서비스 데이터 '절연 저항' 점검 (500 kΩ 이상)", "InitialDiagnosis"),
            ("개인 보호 장비 착용 · 스마트키 잠금 보관", "LOTO_Gloves"),
            ("POWER OFF → 12V 배터리 (−) 터미널 분리", "Battery_12V_Cover_Removal"),
            ("고전압 차단 스위치(서비스 플러그) 분리", "MSD_Removal"),
            ("캐패시터 방전 — 3분 이상 대기", "DischargeWait"),
            ("리프트로 차량 상승 → 언더커버 · 커넥터 커버 탈거", "Connector_Cover_Removal"),
            ("배터리 프런트 · 리어 커넥터 분리", "HV_Rear_Disconnect"),
            ("인버터 단자 간 전압 30V 이하 · 배터리 단자 0V 확인", "HV_Rear_Voltage"),
        };
        var marks = new List<(TextMeshProUGUI m, string mod)>();
        for (int i = 0; i < steps.Length; i++)
        {
            var bg = Px(_content, "G", 40, 4 + i * 57, 1200, 52); Img(bg, Card);
            marks.Add((T(bg, "", 24, Color.white, TextAlignmentOptions.Center, 0, 0, 70, 52), steps[i].module));
            T(bg, $"{i + 1}.  {steps[i].text}", 22, Color.white, TextAlignmentOptions.Left, 80, 0, 1100, 52);
        }
        _live.Add(() =>
        {
            bool scen = GdsModel.HasScenario;
            foreach (var (m, mod) in marks)
            {
                bool done = scen ? GdsModel.Done(mod) : false;
                bool cur = scen && GdsModel.CurrentModule == mod;
                m.text = done ? "<color=#2ECC71>✓</color>" : cur ? "<color=#00A0E9>▶</color>" : "<color=#5A6B80>○</color>";
            }
            _footer.text = scen ? "시나리오 진행에 맞춰 완료 단계가 표시됩니다" : "자유 실습 — 순서를 확인하며 진행하세요";
        });
    }

    // ── 차량 정보 ──
    private void PageInfo()
    {
        _title.text = "차량 정보";
        var l = Px(_content, "L", 40, 0, 590, 580); Img(l, Card);
        T(l, "<b><color=#00A0E9>차량</color></b>\n" +
             $"차대번호(VIN) ........ {GdsModel.Vin}\n" +
             "차종 ......................... IONIQ 5 (NE)\n" +
             "연식 ......................... 2026\n" +
             "구동 모터 ................. 전륜 160 kW / 후륜 270 kW\n" +
             "구동 배터리 ............. 84.0 kWh · 셀 192개\n" +
             $"충전 상태(SOC) ........ {GdsModel.Soc:0} %\n" +
             "누적 주행 거리 ........ 18,452 km",
            22, Color.white, TextAlignmentOptions.TopLeft, 24, 20, 550, 540);
        var r = Px(_content, "R", 650, 0, 590, 580); Img(r, Card);
        T(r, "<b><color=#00A0E9>제어기 소프트웨어</color></b>\n" +
             "VCU  ........ NE.EV.VCU.26.0412\n" +
             "BMS  ........ NE.BMS.84K.26.0308\n" +
             "MCU F ....... NE.MCU.F160.26.0215\n" +
             "MCU R ....... NE.MCU.R270.26.0215\n" +
             "ICCU ........ NE.ICCU.11K.26.0127\n" +
             "IEB ......... NE.IEB.26.0110\n\n" +
             "<size=18><color=#96A5B9>VCI 3 · CAN FD / DoIP</color></size>",
            22, Color.white, TextAlignmentOptions.TopLeft, 24, 20, 550, 540);
        _footer.text = Vci ? "VCI 연결됨" : "VCI 미연결 — 차량 정보는 마지막 연결 기준";
    }
}

// ============================================================
//  GdsSync — 진단기 화면(페이지)·VCI 연결·소거 여부를 서버를 거쳐 모두에게 같게
// ============================================================
public struct GdsBroadcast : IBroadcast
{
    public string screen;   // null: 클라이언트 → 서버 '현재 상태 보내 줘'
    public string arg;
    public bool vci;
    public bool cleared;
}

public static class GdsSync
{
    private static readonly List<GdsTablet> s_all = new List<GdsTablet>();
    private static GdsBroadcast s_state = new GdsBroadcast { screen = "home", arg = "" };
    private static bool s_serverReg, s_clientReg, s_asked;

    public static void Register(GdsTablet t)
    {
        if (t == null) return;
        if (!s_all.Contains(t)) s_all.Add(t);
        EnsureRegistered();
        t.ApplyState(s_state.screen, s_state.arg, s_state.vci, s_state.cleared);
    }

    public static void Unregister(GdsTablet t) => s_all.Remove(t);

    private static void EnsureRegistered()
    {
        var sm = InstanceFinder.ServerManager;
        var cm = InstanceFinder.ClientManager;
        if (!s_serverReg && sm != null) { sm.RegisterBroadcast<GdsBroadcast>(OnServer); s_serverReg = true; }
        if (!s_clientReg && cm != null) { cm.RegisterBroadcast<GdsBroadcast>(OnClient); s_clientReg = true; }
        if (!s_asked && cm != null && cm.Started && !InstanceFinder.IsServerStarted)
        {
            s_asked = true;
            cm.Broadcast(new GdsBroadcast { screen = null });
        }
    }

    public static void Request(string screen, string arg, bool vci, bool cleared)
    {
        EnsureRegistered();
        var b = new GdsBroadcast { screen = screen, arg = arg ?? "", vci = vci, cleared = cleared };
        if (InstanceFinder.IsServerStarted) { ServerSet(b); return; }
        if (InstanceFinder.IsClientStarted) { InstanceFinder.ClientManager.Broadcast(b); Apply(b); return; }   // 내 화면은 바로 바꾼다
        Apply(b);   // 오프라인
    }

    private static void Apply(GdsBroadcast b)
    {
        s_state = b;
        foreach (var t in s_all) if (t != null) t.ApplyState(b.screen, b.arg, b.vci, b.cleared);
    }

    private static void ServerSet(GdsBroadcast b)
    {
        Apply(b);
        InstanceFinder.ServerManager.Broadcast(b);
    }

    private static void OnServer(NetworkConnection conn, GdsBroadcast b, Channel ch)
    {
        if (b.screen == null) { InstanceFinder.ServerManager.Broadcast(conn, s_state); return; }
        if (b.screen.Length > 32 || (b.arg != null && b.arg.Length > 64)) return;
        ServerSet(b);
    }

    private static void OnClient(GdsBroadcast b, Channel ch)
    {
        if (InstanceFinder.IsServerStarted || b.screen == null) return;
        Apply(b);
    }
}
