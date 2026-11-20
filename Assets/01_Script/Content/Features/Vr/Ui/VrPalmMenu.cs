using System.Collections.Generic;
using Autohand;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
//  VrPalmMenu.cs
//
//  VR 전용 — 손바닥 메뉴. 빈손의 손바닥을 얼굴 쪽으로 펴면 손 위에 작은 메뉴가 뜨고,
//  반대 손 검지로 눌러 쓴다. 정면 UI 를 찾아 고개를 돌리거나 레이저로 겨눌 필요가 없다.
//  (11/19) 손바닥으로 여는 방식은 껐다(PalmGesture=false) — 왼손 컨트롤러 메뉴(≡)/Y 버튼으로만 연다.
//  (11/19) 둥근 아이콘 격자 5+5+4 — 아이콘은 Resources/PalmIcons/<이름>.png(Tools > VR > 손바닥 메뉴 아이콘 생성). 홈·음소거·MR 전환·종료도 여기에 들어 있다.
//  (11/19) 컨트롤러 메뉴 버튼 — 왼손 메뉴(≡)/Y 버튼으로도 열린다(시선 앞에 떠 있는 고정 메뉴, 다시 누르면 닫힘). 손이 비어 있지 않아도 된다.
//  (11/19) 고전압 상태 — 자유 탈거에서는 패널 윗부분에 5단계 아이콘 + 지금 할 일(HvStatusHud 의 Snap* 값). 손목 패널은 없앴다.
//  여는 법 안내: 시작 10초 뒤 카드 + 선임 대사(한 번도 안 열었다면 90초마다 최대 3번), 열리는 동안 손바닥 위에 진행 링.
//  · 단계 목록   — 왼쪽 단계 목록 패널 열기/닫기 (VrTaskListToggle)
//  · 미니맵      — 미니맵 켜기/끄기
//  · 단계 넘김   — 정면 UI 의 '완료' 버튼과 같은 동작(누를 수 없는 단계면 회색)
//  · UI 정면으로 — 정면 UI 를 다시 시선 앞으로 (VRUIFollower 기본 따라오기로 복귀)
//  · 엑스레이   — 지금 다룰 부품을 차체 너머로 비춰 보기 (XRayMode, 평가 모드에서는 회색)
//  · 집중 모드   — 작업 대상 가까이에서 가리는 차체를 하얀 반투명으로 (FocusMode, 자동/끔)
//  · 측정 기록   — 측정 기록지 보이기/숨기기 (MeasurementLog)
//  · 버튼 안내   — 컨트롤러 버튼 설명을 항상 보이기/처음만 (VrControllerHints)
//  · 연습장     — 기본 동작 연습 튜토리얼 다시 하기/끝내기 (VrPracticeTutorial, 평가 모드에서는 회색)
//  · 부품 보관함 — 떼어낸 부품 보관함 창 열기/닫기 (VrPartTray, 맨 아래 넓은 버튼. 차 부품 작업이 없으면 회색)
//  누르면 손끝 진동 + 클릭음. 손바닥을 내리거나 물건을 잡으면 사라진다.
//  씬 배치 불필요 — 게임 시작 시 자동 생성되고, VR 기기가 아니면 아무것도 하지 않는다.
// ============================================================
public class VrPalmMenu : MonoBehaviour
{
    // 작업 중 손바닥이 잠깐 얼굴 쪽을 향할 때마다 메뉴가 떠서 오조작이 났다 → 조건을 엄격하게:
    // 손바닥을 얼굴 정면으로(약 37° 이내) 0.6초 동안 '가만히' 들고 있어야 열리고, 어느 손이든 물건을 들고 있으면 열지 않는다.
    /// <summary>손바닥을 펴서 여는 방식(11/19 사용자 요청으로 끔 — 컨트롤러 메뉴 버튼으로 통일). true 로 바꾸면 다시 켜진다.</summary>
    private const bool PalmGesture = false;
    private const float FacingDot = 0.8f;       // 손바닥이 얼굴을 향하는 정도(cos)
    private const float OpenDelay = 0.6f;
    private const float MaxPalmSpeed = 0.35f;   // 이보다 빨리 움직이는 손은 '보여 주는 손'이 아니다(m/s)
    private const float PressArmDelay = 0.4f;   // 열린 직후 이 시간 동안은 눌리지 않음
    private const float CloseDelay = 0.35f;
    private const float PressDepth = 0.006f;    // 버튼 면을 이만큼 지나면 눌림
    private const float ReleaseDepth = 0.02f;   // 이만큼 떨어지면 다시 누를 수 있음

    private static readonly Color OnColor = new Color(0.16f, 0.42f, 0.72f, 0.96f);
    private static readonly Color BgColor = new Color(0.15f, 0.18f, 0.24f, 0.97f);
    private static readonly Color PlateColor = new Color(0.02f, 0.03f, 0.04f, 0.93f);
    private Material _plateMat;
    private static readonly Color HoverColor = new Color(0.16f, 0.36f, 0.62f, 0.95f);
    private static readonly Color PressColor = new Color(0.25f, 0.75f, 0.4f, 0.95f);
    private static readonly Color DisabledColor = new Color(0.2f, 0.2f, 0.2f, 0.6f);

    private class Btn
    {
        public string id, label, iconId;
        public Vector2 center;
        public GameObject go;
        public Material mat, iconMat;
        public TextMeshPro text;
        public bool enabled = true, on;
        public float flashUntil;
    }

    private readonly List<Hand> _hands = new List<Hand>();
    private float _nextHandScan;

    private GameObject _root;
    private readonly List<Btn> _btns = new List<Btn>();
    private static VrPalmMenu s_inst;
    /// <summary>지금 손바닥 메뉴가 떠 있는가(연습장 튜토리얼이 확인).</summary>
    public static bool IsOpen => s_inst != null && (s_inst._menuHand != null || s_inst._pinned) && s_inst._root != null && s_inst._root.activeSelf;
    private Hand _menuHand;        // 메뉴가 붙은 손
    private float _facingSince = -1f, _notFacingSince = -1f;
    private Hand _candidate;
    private bool _pressLatched;
    private float _nextPress;
    private float _openedAt;
    private Vector3 _candLastPos;

    // ── 컨트롤러 메뉴 버튼으로 열기 ──
    // 왼손 컨트롤러 메뉴(≡) 또는 Y 버튼 → 손바닥 대신 시선 앞(약 55cm)에 떠 있는 메뉴. 한 번 더 누르면 닫힌다. 손을 펴지 않아도 되어 가장 쉽다.
    private UnityEngine.InputSystem.InputAction _menuAction;
    private bool _pinned, _pinMoving;
    private Vector3 _pinPos;
    private Hand _pokeHand;
    private TextMeshPro _title;

    // 정면 UI 참조(필요할 때 찾음)
    private VRUIFollower _follower;
    private GameObject _miniMap;
    private Button _confirmBtn;
    private float _nextRefFind;

    // ── 정면 UI 켜기/끄기 (VR 만) ─────────────────────────────
    // 26.11.16 VR 테스트: 정면의 메뉴바·미니맵·설명 카드가 시야를 가린다는 의견 → 기본은 전부 끄고 손바닥 메뉴에서 켜 보며 비교.
    //  · 메뉴바  : 정면 UI 의 BottomBar(진행 n/전체 · 단계 설명 · 홈/소리/종료). TaskProgressUI 가 붙어 있어 SetActive 대신 투명 처리 + 손가락 콜라이더 끔
    //  · 미니맵  : CanvasMiniMap_PC_01
    //  · 설명창  : EquipFx.Notice 로 뜨는 장비 사용법·매뉴얼 카드(사다리·리프트·타이어 교체 안내 등). 고전압·남은 시간 경고는 그대로 뜬다
    public static bool ShowMenuBar = false;
    public static bool ShowMiniMap = false;
    public static bool ShowInfoCards { get => InfoCardPolicy.ShowInfoCards; set => InfoCardPolicy.ShowInfoCards = value; }   // (11/20) InfoCardPolicy 로 옮김
    /// <summary>VR 에서 설명 카드를 띄울지(EquipFx.Notice 가 확인). VR 이 아니면 항상 true.</summary>
    public static bool InfoCardsAllowed => InfoCardPolicy.Allowed;

    private GameObject _menuBar;
    private CanvasGroup _menuBarGroup;
    private readonly List<Collider> _menuBarCols = new List<Collider>();
    // 메뉴바와 같이 켜고 끄는 정면 UI 잔재 — 이동 버튼(BTN_Move)·접속자 목록(UserInfos). 메뉴바가 꺼져 있으면 허공에 떠 있어 거슬렸다(11/19)
    private readonly List<GameObject> _extras = new List<GameObject>();
    private readonly List<Collider> _extraCols = new List<Collider>();
    private float _nextHudApply;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindAnyObjectByType<VrPalmMenu>() != null) return;
        var go = new GameObject("[VrPalmMenu]");
        DontDestroyOnLoad(go);
        go.AddComponent<VrPalmMenu>();
    }

    private void Awake() => s_inst = this;

    private void OnDestroy()
    {
        foreach (var b in _btns) { if (b.mat != null) Destroy(b.mat); if (b.iconMat != null) Destroy(b.iconMat); }
        foreach (var m in _hvMats) if (m != null) Destroy(m);
        if (_menuAction != null) { _menuAction.Disable(); _menuAction.Dispose(); _menuAction = null; }
        if (_root != null) Destroy(_root);
        if (_ring != null) Destroy(_ring.gameObject);
    }

    private void LateUpdate()
    {
        if (!VrDevice.IsActive) return;
        if (Time.unscaledTime >= _nextHudApply) { _nextHudApply = Time.unscaledTime + 0.5f; ApplyHud(); }
        if (Time.unscaledTime >= _nextHandScan) { _nextHandScan = Time.unscaledTime + 2f; ScanHands(); }
        var cam = Camera.main;
        if (cam == null) { Close(); return; }

        EnsureMenuAction();
        if (_menuAction != null && _menuAction.WasPressedThisFrame())
        {
            if (_root != null && _root.activeSelf) Close(); else OpenPinned(cam.transform);
        }
        if (_pinned)
        {
            _candidate = null;
            UpdateGuidance(Time.unscaledTime, cam.transform);
            PlacePinned(cam.transform);
            RefreshStates();
            HandlePoke();
            return;
        }

        if (!PalmGesture)
        {
            if (_root != null && _root.activeSelf) Close();   // 손바닥으로 열린 잔재가 있으면 닫는다
            _candidate = null;
            UpdateGuidance(Time.unscaledTime, cam.transform);
            return;
        }

        // 어느 손이 손바닥을 얼굴 쪽으로 폈나
        Hand facing = null;
        bool anyHolding = false;
        foreach (var h in _hands)
            if (h != null && h.isActiveAndEnabled && (h.GetHeld() != null || h.holdingObj != null)) { anyHolding = true; break; }
        if (!anyHolding)
            foreach (var h in _hands)
                if (IsPalmFacing(h, cam.transform)) { facing = h; if (h == _menuHand) break; }

        float now = Time.unscaledTime;
        if (_menuHand == null)
        {
            if (facing != null)
            {
                Vector3 pp = facing.palmTransform.position;
                if (_candidate != facing) { _candidate = facing; _facingSince = now; }
                else
                {
                    float spd = Time.unscaledDeltaTime > 1e-4f ? (pp - _candLastPos).magnitude / Time.unscaledDeltaTime : 0f;
                    if (spd > MaxPalmSpeed) _facingSince = now;          // 움직이는 중이면 처음부터 다시
                    else if (now - _facingSince >= OpenDelay) Open(facing);
                }
                _candLastPos = pp;
            }
            else _candidate = null;
        }
        else if (anyHolding) Close();
        else
        {
            bool keep = facing == _menuHand || PokerNear();
            if (keep) _notFacingSince = -1f;
            else
            {
                if (_notFacingSince < 0f) _notFacingSince = now;
                if (now - _notFacingSince >= CloseDelay) Close();
            }
        }

        UpdateGuidance(now, cam.transform);
        if (_menuHand == null) return;
        Place(cam.transform);
        RefreshStates();
        HandlePoke();
    }

    // ── 열고 닫기 ─────────────────────────────

    private bool IsPalmFacing(Hand h, Transform cam)
    {
        if (h == null || !h.isActiveAndEnabled || h.palmTransform == null || h.name.StartsWith("HighlightHand")) return false;
        if (h.GetHeld() != null || h.holdingObj != null) return false;
        Vector3 p = h.palmTransform.position;
        Vector3 toCam = cam.position - p;
        float dist = toCam.magnitude;
        if (dist < 0.2f || dist > 0.6f) return false;
        if (Vector3.Dot(cam.forward, -toCam / dist) < 0.5f) return false;   // 시야 안에 있을 때만
        return Vector3.Dot(h.palmTransform.forward, toCam / dist) > FacingDot;
    }

    private void Open(Hand h)
    {
        if (_root == null) Build();
        if (_root == null) return;
        _menuHand = h;
        _pinned = false;
        SetTitle(false);
        _everOpened = true;
        _notFacingSince = -1f;
        _pressLatched = true;   // 열자마자 눌리지 않게(손끝이 이미 안쪽이면 한 번 빠져야 함)
        _openedAt = Time.unscaledTime;
        _root.SetActive(true);
        Haptic(h, 0.02f, 0.2f);
    }

    private void Close()
    {
        _pinned = false; _pinMoving = false;
        _menuHand = null;
        _candidate = null;
        if (_root != null && _root.activeSelf) _root.SetActive(false);
    }

    private void EnsureMenuAction()
    {
        if (_menuAction != null) return;
        // 같은 컨트롤에 여러 경로가 겹쳐도 한 프레임에 한 번만 읽는다. 맞지 않는 경로는 그냥 무시된다
        _menuAction = new UnityEngine.InputSystem.InputAction("PalmMenuButton", UnityEngine.InputSystem.InputActionType.Button);
        UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(_menuAction, "<XRController>{LeftHand}/{MenuButton}");
        UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(_menuAction, "<XRController>{LeftHand}/menuButton");
        UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(_menuAction, "<XRController>{LeftHand}/start");
        UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(_menuAction, "<OculusTouchController>{LeftHand}/start");
        UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(_menuAction, "<XRController>{LeftHand}/{SecondaryButton}");   // Y
        UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(_menuAction, "<XRController>{LeftHand}/secondaryButton");
        _menuAction.Enable();
    }

    private static Vector3 PinTarget(Transform cam)
    {
        Vector3 f = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
        f = f.sqrMagnitude < 1e-4f ? cam.forward : f.normalized;
        return cam.position + f * 0.55f + Vector3.up * -0.07f;
    }

    private void OpenPinned(Transform cam)
    {
        if (_root == null) Build();
        if (_root == null) return;
        _pinned = true; _pinMoving = false;
        _menuHand = null; _candidate = null;
        _everOpened = true;
        _pressLatched = true;
        _openedAt = Time.unscaledTime;
        SetTitle(true);
        _pinPos = PinTarget(cam);
        _root.SetActive(true);
        PlacePinned(cam);
        foreach (var h in _hands) if (h != null && h.left) { Haptic(h, 0.02f, 0.2f); break; }
    }

    /// <summary>시선 앞 고정 메뉴 — 고개를 많이 돌리면 천천히 따라온다(움직이는 동안만). 평소엔 제자리에 있어 누르기 쉽다.</summary>
    private void PlacePinned(Transform cam)
    {
        Vector3 target = PinTarget(cam);
        float d = Vector3.Distance(_pinPos, target);
        if (d > 0.22f) _pinMoving = true; else if (d < 0.03f) _pinMoving = false;
        if (_pinMoving) _pinPos = Vector3.Lerp(_pinPos, target, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime));
        Vector3 look = _pinPos - cam.position;
        if (look.sqrMagnitude < 1e-6f) look = cam.forward;
        _root.transform.SetPositionAndRotation(_pinPos, Quaternion.LookRotation(look, Vector3.up));
    }

    private void SetTitle(bool pinned)
    {
        if (_title == null) return;
        _title.text = pinned ? "메뉴(≡) 버튼을 다시 누르면 닫힙니다" : "손바닥을 내리면 닫힙니다";
    }

    private void Place(Transform cam)
    {
        var palm = _menuHand.palmTransform;
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, (palm.position - cam.position).normalized).normalized;
        Vector3 pos = palm.position + palm.forward * 0.03f + up * 0.17f;   // 5×4 격자 — 맨 아래 줄이 손바닥 바로 위에 오게
        Vector3 look = pos - cam.position;
        if (look.sqrMagnitude < 1e-6f) look = cam.forward;
        _root.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look, Vector3.up));
    }

    // ── 버튼 (둥근 아이콘 격자 5×4) ─────────────────────────────

    private const int Cols = 5;
    private const float Radius = 0.021f, PitchX = 0.056f, PitchY = 0.068f;
    // 3줄 격자 — 가운데 줄이 원점. 바닥 줄 글자 아래에 닫는 법 한 줄, 그 아래까지 검은 판을 깐다
    private const float GridTop = PitchY + Radius + 0.008f, GridBottom = -(PitchY + Radius + 0.045f), TitleY = GridBottom + 0.009f;

    private static Texture2D s_disc;
    internal static Texture2D DiscTex()
    {
        if (s_disc != null) return s_disc;
        const int n = 64;
        s_disc = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f));
                s_disc.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(n * 0.5f - 1f - d + 0.5f)));
            }
        s_disc.Apply();
        return s_disc;
    }

    private static Texture IconTex(string id)
    {
        var sp = Resources.Load<Sprite>("PalmIcons/" + id);
        return sp != null ? sp.texture : null;
    }

    private static Vector2 FitSize(Texture t)
    {
        if (t == null || t.height <= 0) return Vector2.one;
        float a = (float)t.width / t.height;
        return a >= 1f ? new Vector2(1f, 1f / a) : new Vector2(a, 1f);
    }

    private static Material UiMat(Texture tex, int queue)
    {
        var m = new Material(Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default"));
        m.mainTexture = tex;
        m.renderQueue = queue;
        return m;
    }

    private void Build()
    {
        TMP_FontAsset font = null;
        var tagPrefab = Resources.Load<GameObject>("ItemNameTagLabel");
        if (tagPrefab != null)
        {
            var t = tagPrefab.GetComponentInChildren<TMP_Text>(true);
            if (t != null) font = t.font;
        }
        _font = font;

        _root = new GameObject("[VrPalmMenu_Panel]");
        _root.layer = 1;
        DontDestroyOnLoad(_root);

        // 검은 바탕 판 — 흰 글자가 정비소 배경에 묻히지 않게(11/19)
        var plate = UiMat(Texture2D.whiteTexture, 3984); plate.color = PlateColor;
        _plateMat = plate;
        var pq = MakeQuad("Plate", new Vector2(0f, (GridTop + GridBottom) * 0.5f), 1f, 0.0008f);
        pq.transform.localScale = new Vector3(PitchX * Cols + 0.02f, GridTop - GridBottom, 1f);
        pq.GetComponent<MeshRenderer>().sharedMaterial = plate;

        // 14개(안 쓰던 메뉴바·단계 팝업·UI 정면은 뺐다) — 진행 · 보기 · 시스템
        AddBtn("tasks", "단계 목록", "tasks", 0, 0);
        AddBtn("skip", "단계 넘김", "skip", 1, 0);
        AddBtn("minimap", "미니맵", "minimap", 2, 0);
        AddBtn("tray", "부품 보관함", "tray", 3, 0);
        AddBtn("xray", "엑스레이", "xray", 4, 0);
        AddBtn("focus", "집중 모드", "focus", 0, 1);
        AddBtn("record", "측정 기록", "record", 1, 1);
        AddBtn("hints", "버튼 안내", "hints", 2, 1);
        AddBtn("practice", "연습장", "practice", 3, 1);
        AddBtn("infocards", "설명창", "infocards", 4, 1);
        AddBtn("sound", "음소거", "sound_on", 0.5f, 2);
        AddBtn("mr", "MR 전환", "mr", 1.5f, 2);
        AddBtn("home", "홈", "home", 2.5f, 2);
        AddBtn("exit", "종료", "exit", 3.5f, 2);

        BuildHv(font);

        // 맨 아래 한 줄 — 닫는 법
        var tgo = new GameObject("Title");
        tgo.layer = 1;
        tgo.transform.SetParent(_root.transform, false);
        tgo.transform.localPosition = new Vector3(0f, TitleY, -0.001f);
        _title = tgo.AddComponent<TextMeshPro>();
        if (font != null) _title.font = font;
        _title.rectTransform.sizeDelta = new Vector2(PitchX * Cols, 0.016f);
        _title.enableAutoSizing = true; _title.fontSizeMin = 0.01f; _title.fontSizeMax = 0.05f;
        _title.alignment = TextAlignmentOptions.Center;
        _title.color = new Color(0.7f, 0.8f, 0.9f, 0.9f);
        if (_title.fontMaterial != null) _title.fontMaterial.renderQueue = 4000;
        SetTitle(false);
        _root.SetActive(false);
    }

    private TMP_FontAsset _font;

    // ── 고전압 상태(자유 탈거에서만) ─────────────────────────────
    // 예전엔 왼손 손목에 따로 패널(HvStatusHud)이 떴다 → 손바닥 메뉴와 한 패널로 합쳤다. 아이콘 5단계 + 완료 방패, 지금 할 단계 이름, 안내 한 줄.
    private const int HvN = 5;
    // 격자 판 바로 위에 붙는 별도 판 — 아래에서 위로: 안내(최대 3줄) · 단계 이름 · 아이콘 줄
    private const float HvR = 0.015f, HvPitch = 0.0485f;
    private const float HvBottom = GridTop, HvNextY = HvBottom + 0.004f + 0.026f, HvLabelY = HvBottom + 0.004f + 0.052f + 0.013f, HvCircleY = HvLabelY + 0.013f + 0.006f + HvR, HvTop = HvCircleY + HvR + 0.007f;
    private GameObject _hvRoot;
    private readonly List<Material> _hvMats = new List<Material>();
    private readonly Material[] _hvRing = new Material[HvN + 1], _hvDisc = new Material[HvN + 1], _hvIcon = new Material[HvN + 1], _hvLink = new Material[HvN];
    private TextMeshPro _hvLabel, _hvNext;
    private string _hvLabelShown, _hvNextShown;
    private static readonly Color HvDone = new Color(0.20f, 0.62f, 0.30f, 1f), HvNow = new Color(1f, 0.6f, 0.1f, 1f), HvIdle = new Color(0.30f, 0.33f, 0.38f, 1f);
    private static readonly Color HvDoneDisc = new Color(0.10f, 0.30f, 0.15f, 0.95f), HvNowDisc = new Color(0.32f, 0.20f, 0.06f, 0.95f), HvIdleDisc = new Color(0.13f, 0.15f, 0.18f, 0.95f);

    private Material HvMat(Texture tex, int queue) { var m = UiMat(tex, queue); _hvMats.Add(m); return m; }

    private GameObject HvQuad(string name, Vector2 center, Vector2 size, float z, Material m)
    {
        var q = MakeQuad(name, center, 1f, z);
        q.transform.SetParent(_hvRoot.transform, false);
        q.transform.localPosition = new Vector3(center.x, center.y, z);
        q.transform.localScale = new Vector3(size.x, size.y, 1f);
        q.GetComponent<MeshRenderer>().sharedMaterial = m;
        return q;
    }

    private TextMeshPro HvText(string name, float y, float w, float h, float maxSize, float minSize)
    {
        var tgo = new GameObject(name);
        tgo.layer = 1;
        tgo.transform.SetParent(_hvRoot.transform, false);
        tgo.transform.localPosition = new Vector3(0f, y, -0.001f);
        var t = tgo.AddComponent<TextMeshPro>();
        if (_font != null) t.font = _font;
        t.rectTransform.sizeDelta = new Vector2(w, h);
        t.enableAutoSizing = true; t.fontSizeMin = minSize; t.fontSizeMax = maxSize;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.Normal;
        t.richText = true;
        if (t.fontMaterial != null) t.fontMaterial.renderQueue = 4000;
        var mr = tgo.GetComponent<MeshRenderer>();
        if (mr != null) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return t;
    }

    private void BuildHv(TMP_FontAsset font)
    {
        _font = font;
        _hvRoot = new GameObject("HvStatus");
        _hvRoot.layer = 1;
        _hvRoot.transform.SetParent(_root.transform, false);

        // 어두운 판 — 정비소 배경 위에서도 읽히게
        var plate = HvMat(Texture2D.whiteTexture, 3984); plate.color = PlateColor;
        HvQuad("Plate", new Vector2(0f, (HvBottom + HvTop) * 0.5f), new Vector2(PitchX * Cols + 0.02f, HvTop - HvBottom), 0.0008f, plate);

        float x0 = -HvPitch * HvN * 0.5f;
        for (int i = 0; i < HvN; i++)   // 선을 먼저(원 뒤에 깔린다)
        {
            _hvLink[i] = HvMat(Texture2D.whiteTexture, 3988);
            HvQuad("Link" + i, new Vector2(x0 + (i + 0.5f) * HvPitch, HvCircleY), new Vector2(HvPitch - HvR * 2f + 0.004f, 0.0025f), 0.0002f, _hvLink[i]);
        }
        for (int i = 0; i <= HvN; i++)
        {
            var c = new Vector2(x0 + i * HvPitch, HvCircleY);
            _hvRing[i] = HvMat(DiscTex(), 3989);
            HvQuad("Ring" + i, c, Vector2.one * HvR * 2f, 0f, _hvRing[i]);
            _hvDisc[i] = HvMat(DiscTex(), 3990);
            HvQuad("Disc" + i, c, Vector2.one * (HvR * 2f - 0.0036f), -0.0002f, _hvDisc[i]);
            var sp = Resources.Load<Sprite>("HvSteps/" + HvStatusHud.StepIcon(i));
            if (sp != null)
            {
                _hvIcon[i] = HvMat(sp.texture, 3995);
                HvQuad("Icon" + i, c, FitSize(sp.texture) * HvR * 1.35f, -0.0005f, _hvIcon[i]);
            }
        }
        _hvLabel = HvText("Label", HvLabelY, PitchX * Cols, 0.026f, 0.022f, 0.012f);
        _hvNext = HvText("Next", HvNextY, PitchX * Cols - 0.008f, 0.052f, 0.017f, 0.01f);
        _hvRoot.SetActive(false);
    }

    private void UpdateHv()
    {
        if (_hvRoot == null) return;
        bool on = HvStatusHud.SnapActive;
        if (_hvRoot.activeSelf != on) _hvRoot.SetActive(on);
        if (!on) return;

        bool all = HvStatusHud.SnapAll;
        int cur = HvStatusHud.SnapCur;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
        for (int i = 0; i < HvN; i++)
        {
            bool done = HvStatusHud.SnapDone[i];
            Color ring, disc, icon = Color.white;
            if (done) { ring = HvDone; disc = HvDoneDisc; }
            else if (i == cur) { ring = Color.Lerp(HvNow, Color.white, pulse * 0.35f); disc = HvNowDisc; }
            else { ring = HvIdle; disc = HvIdleDisc; icon = new Color(1f, 1f, 1f, 0.35f); }
            SetHv(i, ring, disc, icon);
            if (_hvLink[i] != null) _hvLink[i].color = done ? HvDone : HvIdle;
        }
        if (all) SetHv(HvN, HvDone, HvDoneDisc, Color.white);
        else SetHv(HvN, HvIdle, HvIdleDisc, new Color(1f, 1f, 1f, 0.25f));

        string label = HvStatusHud.SnapLabel;
        if (_hvLabel != null && label != _hvLabelShown) { _hvLabelShown = label; _hvLabel.text = label; }
        if (_hvLabel != null) _hvLabel.color = all ? new Color(0.6f, 1f, 0.65f) : new Color(1f, 0.82f, 0.45f);
        string next = HvStatusHud.SnapNextShort;
        if (_hvNext != null && next != _hvNextShown) { _hvNextShown = next; _hvNext.text = next; }
        if (_hvNext != null) _hvNext.color = all ? new Color(0.6f, 1f, 0.65f) : new Color(1f, 0.85f, 0.5f);
    }

    private void SetHv(int i, Color ring, Color disc, Color icon)
    {
        if (_hvRing[i] != null) _hvRing[i].color = ring;
        if (_hvDisc[i] != null) _hvDisc[i].color = disc;
        if (_hvIcon[i] != null) _hvIcon[i].color = icon;
    }

    private void AddBtn(string id, string label, string iconId, float col, int row)
    {
        Vector2 center = new Vector2((col - (Cols - 1) * 0.5f) * PitchX, (1f - row) * PitchY);
        var b = new Btn { id = id, label = label, iconId = iconId, center = center };

        b.go = MakeQuad(id, center, Radius * 2f, 0f);
        b.mat = UiMat(DiscTex(), 3990);
        b.mat.color = BgColor;
        b.go.GetComponent<MeshRenderer>().sharedMaterial = b.mat;

        var icon = IconTex(iconId);
        if (icon != null)
        {
            var ig = MakeQuad(id + "_Icon", center, Radius * 1.3f, -0.0006f);
            var fit = FitSize(icon);   // 정사각형이 아닌 그림(MR 고글 160×120)이 찌그러지지 않게
            ig.transform.localScale = new Vector3(Radius * 1.3f * fit.x, Radius * 1.3f * fit.y, 1f);
            b.iconMat = UiMat(icon, 3995);
            ig.GetComponent<MeshRenderer>().sharedMaterial = b.iconMat;
        }

        var tgo = new GameObject(id + "_Text");
        tgo.layer = 1;
        tgo.transform.SetParent(_root.transform, false);
        tgo.transform.localPosition = new Vector3(center.x, center.y - Radius - 0.0115f, -0.001f);
        var tmp = tgo.AddComponent<TextMeshPro>();
        if (_font != null) tmp.font = _font;
        tmp.rectTransform.sizeDelta = new Vector2(PitchX * 1.0f, 0.014f);
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.01f;
        tmp.fontSizeMax = 0.06f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.color = new Color(0.85f, 0.9f, 0.97f);
        tmp.text = label;
        var tr = tgo.GetComponent<MeshRenderer>();
        if (tr != null) tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (tmp.fontMaterial != null) tmp.fontMaterial.renderQueue = 4000;
        b.text = tmp;
        _btns.Add(b);
    }

    private GameObject MakeQuad(string name, Vector2 center, float size, float z)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var col = q.GetComponent<Collider>();
        if (col != null) { col.enabled = false; Destroy(col); }
        q.name = name;
        q.layer = 1;
        q.transform.SetParent(_root.transform, false);
        q.transform.localPosition = new Vector3(center.x, center.y, z);
        q.transform.localScale = new Vector3(size, size, 1f);
        var mr = q.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return q;
    }

    private float _nextTaskFind;
    private VrTaskListToggle _taskToggle;

    private void RefreshStates()
    {
        FindRefs();
        float now = Time.unscaledTime;
        if (now >= _nextTaskFind) { _nextTaskFind = now + 1f; _taskToggle = FindAnyObjectByType<VrTaskListToggle>(); }
        foreach (var b in _btns)
        {
            b.enabled = true; b.on = false;
            switch (b.id)
            {
                case "minimap": b.enabled = _miniMap != null; b.on = ShowMiniMap; break;
                case "menubar": b.enabled = _menuBar != null; b.on = ShowMenuBar; break;
                case "infocards": b.on = ShowInfoCards; break;
                case "steppopup": b.on = GuideSystem.CenterPopupEnabled; break;
                case "skip": b.enabled = _confirmBtn != null && _confirmBtn.gameObject.activeInHierarchy && _confirmBtn.interactable; break;
                case "tasks": b.enabled = _taskToggle != null; break;
                case "recenter": b.enabled = _follower != null; break;
                case "xray": b.enabled = XRayMode.Available; b.on = XRayMode.Active; break;
                case "focus": b.on = FocusMode.Enabled; break;
                case "record": b.on = MeasurementLog.Visible; break;
                case "hints": b.on = VrControllerHints.AlwaysShow; break;
                case "tray": b.enabled = VrPartTray.Available; b.on = VrPartTray.IsOpen; break;
                case "practice": b.enabled = !EvaluationMode.ClientIsEvaluation; b.on = VrPracticeTutorial.Running; break;
                case "sound":
                    {
                        bool muted = AudioListener.volume <= 0.001f;
                        b.on = muted;
                        SetIcon(b, muted ? "sound_off" : "sound_on");
                        break;
                    }
                case "mr": b.enabled = FindVrBtn("BTN_MR") != null; break;
                case "home": b.enabled = FindVrBtn("BTN_Home") != null; break;
                case "exit": b.enabled = FindVrBtn("BTN_Exit") != null; break;
            }
            if (now < b.flashUntil) continue;
            b.mat.color = b.enabled ? (b.on ? OnColor : BgColor) : DisabledColor;
            if (b.iconMat != null) b.iconMat.color = b.enabled ? Color.white : new Color(1f, 1f, 1f, 0.4f);
            b.text.color = b.enabled ? new Color(0.85f, 0.9f, 0.97f) : new Color(1f, 1f, 1f, 0.4f);
        }
        UpdateHv();
    }

    private static void SetIcon(Btn b, string iconId)
    {
        if (b.iconId == iconId || b.iconMat == null) return;
        var t = IconTex(iconId);
        if (t == null) return;
        b.iconId = iconId; b.iconMat.mainTexture = t;
    }

    private readonly Dictionary<string, Button> _vrButtons = new Dictionary<string, Button>();
    private ToggleButton _muteToggle;
    private Button FindVrBtn(string n) => _vrButtons.TryGetValue(n, out var b) && b != null ? b : null;

    // ── 여는 법 안내 ─────────────────────────────
    private bool _everOpened;
    private int _hintShown;
    private float _hintDue = -1f;
    private LineRenderer _ring;

    private void UpdateGuidance(float now, Transform cam)
    {
        // 열리는 중 — 손바닥 위에 진행 링(0.6초 동안 차오른다). 손이 움직이면 처음부터 다시
        float p = 0f;
        var h = _candidate;
        if (_menuHand == null && h != null && h.palmTransform != null && _facingSince >= 0f) p = Mathf.Clamp01((now - _facingSince) / OpenDelay);
        if (p > 0.03f && p < 1f) ShowRing(h.palmTransform, cam, p); else if (_ring != null && _ring.gameObject.activeSelf) _ring.gameObject.SetActive(false);

        // 한 번도 안 열었으면 시작 10초 뒤, 이후 90초마다 최대 3번 알려 준다
        if (_everOpened) return;
        if (_hintDue < 0f) _hintDue = now + 10f;
        if (now >= _hintDue && _hintShown < 3 && _menuHand == null)
        {
            _hintShown++; _hintDue = now + 90f;
            WarningPopup.Show("vrpalm", "손바닥 메뉴", "왼손 컨트롤러의 메뉴(≡) 버튼을 누르면 단계 목록·엑스레이·부품 보관함 같은 메뉴가 눈앞에 열립니다", new Color(0.08f, 0.22f, 0.36f, 0.92f), 9f);
            MentorDialogue.Say("palm_menu", 80f);
        }
    }

    private void ShowRing(Transform palm, Transform cam, float p)
    {
        if (_ring == null)
        {
            var go = new GameObject("[VrPalmMenu_Ring]");
            go.layer = 1;
            DontDestroyOnLoad(go);
            _ring = go.AddComponent<LineRenderer>();
            _ring.useWorldSpace = true;
            _ring.widthMultiplier = 0.006f;
            _ring.numCapVertices = 4;
            _ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var m = new Material(Shader.Find("Sprites/Default")); m.renderQueue = 3995;
            _ring.material = m;
            _ring.startColor = _ring.endColor = new Color(0.45f, 0.85f, 1f, 0.95f);
        }
        if (!_ring.gameObject.activeSelf) _ring.gameObject.SetActive(true);
        Vector3 c = palm.position + palm.forward * 0.07f;
        Vector3 toCam = (cam.position - c).normalized;
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, toCam).normalized;
        Vector3 right = Vector3.Cross(toCam, up).normalized;
        int n = Mathf.Max(2, Mathf.RoundToInt(p * 48f));
        _ring.positionCount = n + 1;
        for (int i = 0; i <= n; i++)
        {
            float a = -(i / 48f) * Mathf.PI * 2f;
            _ring.SetPosition(i, c + (up * Mathf.Cos(a) + right * Mathf.Sin(a)) * 0.05f);
        }
    }

    private static void SetText(Btn b, string s) { if (b.text.text != s) b.text.text = s; }

    // ── 누르기 ─────────────────────────────

    private static Transform IndexTipOf(Hand h)
    {
        if (h == null || h.fingers == null) return null;
        foreach (var f in h.fingers)
            if (f != null && f.fingerType == FingerEnum.index && f.tip != null) return f.tip;
        if (h.fingers.Length > 1 && h.fingers[1] != null && h.fingers[1].tip != null) return h.fingers[1].tip;
        return null;
    }

    private Transform PokerTip()
    {
        if (_pinned)
        {
            // 시선 앞 고정 메뉴 — 메뉴에 가장 가까운 빈손 검지(물건을 든 손은 제외)
            Transform best = null; float bd = float.MaxValue; _pokeHand = null;
            foreach (var h in _hands)
            {
                if (h == null || !h.isActiveAndEnabled || h.name.StartsWith("HighlightHand") || h.GetHeld() != null || h.holdingObj != null) continue;
                var t = IndexTipOf(h);
                if (t == null) continue;
                float d = (t.position - _root.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = t; _pokeHand = h; }
            }
            return best;
        }
        foreach (var h in _hands)
        {
            // 반대쪽 손만 누를 수 있다 — 같은 쪽 손(손 뼈와 손 투영 'HighlightHand')이 둘이라 메뉴 든 손 검지가 버튼을 계속 눌렀다
            if (h == null || _menuHand == null || h.left == _menuHand.left || !h.isActiveAndEnabled || h.fingers == null) continue;
            if (h.name.StartsWith("HighlightHand")) continue;
            var t = IndexTipOf(h);
            if (t != null) return t;
        }
        return null;
    }

    private Hand PokerHand()
    {
        if (_pinned) return _pokeHand;
        foreach (var h in _hands) if (h != null && _menuHand != null && h.left != _menuHand.left && h.isActiveAndEnabled && !h.name.StartsWith("HighlightHand")) return h;
        return null;
    }

    private bool PokerNear()
    {
        if (_root == null || !_root.activeSelf) return false;
        var tip = PokerTip();
        return tip != null && Vector3.Distance(tip.position, _root.transform.position) < 0.12f;
    }

    private void HandlePoke()
    {
        var tip = PokerTip();
        if (tip == null) return;
        Vector3 lp = _root.transform.InverseTransformPoint(tip.position);   // 버튼 면 z=0, 보는 쪽이 -z

        Btn over = null;
        foreach (var b in _btns)
            if (Vector2.Distance(new Vector2(lp.x, lp.y), b.center) <= Radius * 1.08f) { over = b; break; }

        float now = Time.unscaledTime;
        if (lp.z < -ReleaseDepth || over == null) _pressLatched = false;

        foreach (var b in _btns)
            if (now >= b.flashUntil && b.enabled) b.mat.color = b == over && lp.z > -0.03f ? HoverColor : (b.on ? OnColor : BgColor);

        if (over == null || _pressLatched || now < _nextPress || now - _openedAt < PressArmDelay) return;
        if (lp.z < -PressDepth || lp.z > 0.03f) return;

        _pressLatched = true;
        _nextPress = now + 0.35f;
        var hand = PokerHand();
        if (!over.enabled) { Haptic(hand, 0.02f, 0.15f); WorldSfx.PlayAt("Beep_Error", tip.position, 0.35f); return; }

        over.mat.color = PressColor;
        over.flashUntil = now + 0.15f;
        Haptic(hand, 0.03f, 0.5f);
        WorldSfx.PlayAt("UI_Click", tip.position, 0.6f);
        Run(over.id);
    }

    private void Run(string id)
    {
        switch (id)
        {
            case "tasks":
                FindAnyObjectByType<VrTaskListToggle>()?.Toggle();
                break;
            case "sound":
                if (_muteToggle != null) _muteToggle.SetSelected(!_muteToggle.IsSelected);   // VrUiMasterVolumeToggle 이 볼륨을 맞춘다
                else AudioListener.volume = AudioListener.volume > 0.001f ? 0f : 1f;
                break;
            case "home":
            case "exit":
            case "mr":
                var vb = FindVrBtn(id == "home" ? "BTN_Home" : id == "exit" ? "BTN_Exit" : "BTN_MR");
                if (vb != null) { Close(); vb.onClick.Invoke(); }   // 확인 팝업·화면 전환이 눈앞에 뜨므로 메뉴는 닫는다
                break;
            case "minimap":
                ShowMiniMap = !ShowMiniMap;
                ApplyHud();
                break;
            case "menubar":
                ShowMenuBar = !ShowMenuBar;
                ApplyHud();
                break;
            case "infocards":
                ShowInfoCards = !ShowInfoCards;
                if (!ShowInfoCards) WarningPopup.Hide("equip");
                else WarningPopup.Show("vrinfo", "설명창 켜짐", "장비 사용법 · 매뉴얼 카드가 다시 뜹니다", new Color(0.2f, 0.24f, 0.3f, 0.92f), 1.6f);
                break;
            case "steppopup":
                GuideSystem.SetCenterPopupEnabled(!GuideSystem.CenterPopupEnabled);
                break;
            case "skip":
                if (_confirmBtn != null && _confirmBtn.interactable) _confirmBtn.onClick.Invoke();
                break;
            case "recenter":
                if (_follower != null) _follower.ResetToDefaultFollow();
                break;
            case "xray":
                XRayMode.Toggle();
                break;
            case "focus":
                FocusMode.Toggle();
                break;
            case "record":
                MeasurementLog.Toggle();
                break;
            case "hints":
                VrControllerHints.AlwaysShow = !VrControllerHints.AlwaysShow;
                WarningPopup.Show("vrhints", VrControllerHints.AlwaysShow ? "버튼 안내 항상 표시" : "버튼 안내 끔",
                    VrControllerHints.AlwaysShow ? "도구를 잡으면 손 옆에 버튼 설명이 나옵니다" : "", new Color(0.2f, 0.24f, 0.3f, 0.92f), 1.6f);
                break;
            case "tray":
                bool opening = !VrPartTray.IsOpen;
                VrPartTray.Toggle();
                if (opening) Close();   // 창이 눈앞에 뜨므로 메뉴는 닫는다
                break;
            case "practice":
                if (VrPracticeTutorial.Running) VrPracticeTutorial.Stop();
                else { Close(); VrPracticeTutorial.Restart(); }
                break;
        }
    }

    // ── 정면 UI 찾기 ─────────────────────────────

    private void FindRefs()
    {
        if (_follower != null && _confirmBtn != null && _miniMap != null && _menuBar != null) return;
        if (Time.unscaledTime < _nextRefFind) return;
        _nextRefFind = Time.unscaledTime + 1.5f;

        if (_follower == null) _follower = FindAnyObjectByType<VRUIFollower>(FindObjectsInactive.Include);
        if (_follower == null) return;
        foreach (var t in _follower.GetComponentsInChildren<Transform>(true))
        {
            if (_miniMap == null && t.name == "CanvasMiniMap_PC_01") _miniMap = t.gameObject;
            if (_confirmBtn == null && t.name == "ConfirmButton_01" && t.parent == _follower.transform) _confirmBtn = t.GetComponent<Button>();
            if (_menuBar == null && t.name == "BottomBar" && t.parent == _follower.transform) _menuBar = t.gameObject;
            if (t.name == "BTN_Home" || t.name == "BTN_Exit" || t.name == "BTN_MR") { var bb = t.GetComponent<Button>(); if (bb != null) _vrButtons[t.name] = bb; }
            if (t.name == "BTN_SoundMute" && _muteToggle == null) _muteToggle = t.GetComponent<ToggleButton>();
            if ((t.name == "BTN_Move" || t.name == "UserInfos") && t.parent == _follower.transform && !_extras.Contains(t.gameObject)) _extras.Add(t.gameObject);
        }
    }

    private CanvasGroup _confirmGroup;
    private readonly List<Collider> _confirmCols = new List<Collider>();

    /// <summary>정면 위쪽에 떠 있던 파란 '단계 넘김(완료)' 버튼은 VR 에서 가린다 — 같은 동작이 메뉴 '단계 넘김'에 있다(11/19). 투명 + 손가락 콜라이더 끔.</summary>
    private void HideConfirmButton()
    {
        if (_confirmBtn == null) return;
        if (_confirmGroup == null)
        {
            _confirmGroup = _confirmBtn.GetComponent<CanvasGroup>();
            if (_confirmGroup == null) _confirmGroup = _confirmBtn.gameObject.AddComponent<CanvasGroup>();
        }
        if (!Mathf.Approximately(_confirmGroup.alpha, 0f)) { _confirmGroup.alpha = 0f; _confirmGroup.blocksRaycasts = false; }
        foreach (var c in _confirmBtn.GetComponentsInChildren<Collider>(true))
            if (c != null && c.enabled) { c.enabled = false; if (!_confirmCols.Contains(c)) _confirmCols.Add(c); }
    }

    /// <summary>메뉴바·미니맵 표시 상태를 설정값에 맞춘다(다른 코드가 다시 켜도 0.5초마다 되돌림).</summary>
    private void ApplyHud()
    {
        FindRefs();
        if (_miniMap != null && _miniMap.activeSelf != ShowMiniMap) _miniMap.SetActive(ShowMiniMap);

        foreach (var ex in _extras)
        {
            if (ex == null) continue;
            var g = ex.GetComponent<CanvasGroup>() ?? ex.AddComponent<CanvasGroup>();
            float ea = ShowMenuBar ? 1f : 0f;
            if (!Mathf.Approximately(g.alpha, ea)) { g.alpha = ea; g.interactable = ShowMenuBar; g.blocksRaycasts = ShowMenuBar; }
            foreach (var c in ex.GetComponentsInChildren<Collider>(true))
            {
                if (c == null) continue;
                if (!ShowMenuBar) { if (c.enabled) { c.enabled = false; if (!_extraCols.Contains(c)) _extraCols.Add(c); } }
            }
        }
        if (ShowMenuBar && _extraCols.Count > 0) { foreach (var c in _extraCols) if (c != null) c.enabled = true; _extraCols.Clear(); }

        HideConfirmButton();

        if (_menuBar == null) return;
        if (_menuBarGroup == null)
        {
            _menuBarGroup = _menuBar.GetComponent<CanvasGroup>();
            if (_menuBarGroup == null) _menuBarGroup = _menuBar.AddComponent<CanvasGroup>();
        }
        float a = ShowMenuBar ? 1f : 0f;
        if (!Mathf.Approximately(_menuBarGroup.alpha, a))
        {
            _menuBarGroup.alpha = a;
            _menuBarGroup.interactable = ShowMenuBar;
            _menuBarGroup.blocksRaycasts = ShowMenuBar;
        }
        // 손가락으로 누르는 버튼(FingerTriggerAreaEvents)은 콜라이더로 동작 → 숨긴 동안 꺼 둔다
        if (!ShowMenuBar)
        {
            foreach (var c in _menuBar.GetComponentsInChildren<Collider>(true))
                if (c != null && c.enabled) { c.enabled = false; if (!_menuBarCols.Contains(c)) _menuBarCols.Add(c); }
        }
        else if (_menuBarCols.Count > 0)
        {
            foreach (var c in _menuBarCols) if (c != null) c.enabled = true;
            _menuBarCols.Clear();
        }
    }

    private void ScanHands()
    {
        _hands.RemoveAll(h => h == null);
        foreach (var hand in FindObjectsByType<Hand>(FindObjectsSortMode.None))
        {
            if (hand == null || _hands.Contains(hand)) continue;
            var nob = hand.GetComponentInParent<FishNet.Object.NetworkObject>();
            if (nob != null && nob.IsSpawned && !nob.IsOwner) continue;   // 다른 사람 손 제외
            _hands.Add(hand);
        }
    }

    private static void Haptic(Hand h, float duration, float amp)
    {
        if (h == null) return;
        try { h.PlayHapticVibration(duration, amp); } catch { }
    }
}
