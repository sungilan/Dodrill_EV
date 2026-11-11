using EPOOutline;
using FishNet.Object;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════════╗
/// ║  FreeLookController  —  카메라 이동/회전 + 오브젝트 클릭            ║
/// ╠══════════════════════════════════════════════════════════════════╣
/// ║  PC/모바일                                                        ║
/// ║    오브젝트 클릭/터치 → SyncGrab.OnPCClick() 호출                  ║
/// ║    → 오브젝트가 자체 holdPoint로 DOTween 이동                      ║
/// ║    → 다시 클릭 → 원래 자리로 복귀                                   ║
/// ║                                                                   ║
/// ║  VR                                                               ║
/// ║    AutoHand가 직접 처리 (이 스크립트 관여 없음)                      ║
/// ╚══════════════════════════════════════════════════════════════════╝
/// ★ 부착 위치: 플레이어 NetworkObject (캐릭터 프리팹)
/// </summary>
public class FreeLookController : NetworkBehaviour, IPcLookController
{
    // ═══════════════════════════════════════════════════════
    // 인스펙터 — FreeLook
    // ═══════════════════════════════════════════════════════

    [Header("이동")]
    public float moveSpeed = 5f;
    public float lookSensitivity = 2f;
    public float maxLookX = 80f;
    public float minLookX = -80f;

    [Header("수직 이동 스무스")]
    public float verticalSmoothTime = 0.12f;

    // ═══════════════════════════════════════════════════════
    // 인스펙터 — 클릭
    // ═══════════════════════════════════════════════════════

    [Header("클릭 설정")]
    [Tooltip("클릭 가능한 레이어 (SyncGrab이 붙은 오브젝트 레이어)")]
    public LayerMask clickLayers;

    [Tooltip("레이캐스트 최대 거리")]
    public float raycastMaxDistance = 20f;

    [Tooltip("들고 있는 도구를 사용(Use)하는 키 (기본: E)")]
    public KeyCode useKey = KeyCode.F;  // E키는 수직이동이 점유 — F키 사용

    [Tooltip("우클릭으로도 Use 가능 여부 (카메라 회전과 구분 주의)")]
    public bool rightClickUse = false;

    [Header("리프트 키")]
    [Tooltip("차량 리프트 올리기")]
    public KeyCode liftUpKey = KeyCode.Z;
    [Tooltip("차량 리프트 내리기")]
    public KeyCode liftDownKey = KeyCode.X;
    [Tooltip("배터리 잭 올리기")]
    public KeyCode batteryUpKey = KeyCode.C;
    [Tooltip("배터리 잭 내리기")]
    public KeyCode batteryDownKey = KeyCode.V;

    [Header("호버 가이드 UI")]
    [Tooltip("화면 중앙에 표시할 TMP 텍스트 (없으면 생략)")]
    public TMPro.TextMeshProUGUI hoverGuideText;
    [Tooltip("호버 시 표시할 UI 오브젝트 (crosshair 등, 없으면 생략)")]
    public GameObject hoverGuidePanel;
    [Tooltip("호버 오브젝트 위에 띄울 월드 스페이스 라벨 프리팹 (TextMeshPro World Space Canvas)")]
    public GameObject worldLabelPrefab;
    [Tooltip("라벨이 오브젝트 위에 뜨는 높이 오프셋")]
    public float labelHeightOffset = 0.3f;

    [Header("클릭 사운드 설정")]
    [Tooltip("일반적인 클릭/상호작용 시 소리")]
    public string clickSound = "UI_Click";
    [Tooltip("아이템을 집어올릴 때 소리")]
    public string grabSound = "Item_Grab";
    [Tooltip("아이템을 내려놓을 때 소리")]
    public string dropSound = "Item_Drop";
    [Tooltip("E키 등으로 아이템을 사용할 때 소리")]
    public string useSound = "Item_Use";

    [Header("디버그")]
    [SerializeField] private bool showDebugLog = true;
    [SerializeField] private bool showDebugRay = true;

    // ═══════════════════════════════════════════════════════
    // 내부 상태 — FreeLook
    // ═══════════════════════════════════════════════════════

    private float _rotX = 0f;
    private float _desiredVertical = 0f;
    private float _currentVerticalVelocity = 0f;

    // ── 아바타(PcMobileAvatarController) 연동 ──
    // 루트는 좌우(yaw)만 돌고 상하(pitch)는 cameraPivot만 돈다 → 몸통 아바타가 기울지 않는다.
    [Header("아바타 (PcMobileAvatarController 연동)")]
    [Tooltip("카메라 피치를 적용할 피벗. 비워 두면 자식 'CameraPivot'을 찾는다. 없으면 예전처럼 루트 전체를 기울인다.")]
    [SerializeField] private Transform cameraPivot;

    private Vector3 _planarVelocity = Vector3.zero;
    private float _crouchInput = 0f;
    private PcMobileAvatarController _avatarCtrl;
    public Vector3 CurrentPlanarVelocity => _planarVelocity;
    public float CurrentPitch => _rotX;
    public float CrouchInput => _crouchInput;
    public float MoveSpeed => moveSpeed;

    private bool UseAvatarRig => cameraPivot != null && _avatarCtrl != null;

    // ═══════════════════════════════════════════════════════
    // 내부 상태 — 카메라
    // ═══════════════════════════════════════════════════════

    private Camera _mainCamera = null;
    private bool _cameraReady = false;
    private bool _isVR = false;

    // ═══════════════════════════════════════════════════════
    // 내부 상태 — 클릭
    // ═══════════════════════════════════════════════════════

    /// <summary>현재 들고 있는 오브젝트</summary>
    [SerializeField]private SyncGrab _heldObject = null;

    // ── PC 레이저 그랩 설정 ──────────────────────────────────
    [Header("PC 레이저 그랩")]
    [Tooltip("레이저 라인 렌더러 (없으면 자동 생성)")]
    public LineRenderer laserLine;
    [Tooltip("곡선 버텍스 수 (많을수록 부드러움)")]
    public int laserVertexCount = 20;
    [Tooltip("레이저 색상 시작점 (손 쪽)")]
    public Color laserColorStart = new Color(0.3f, 0.8f, 1f, 1f);
    [Tooltip("레이저 색상 끝점 (부품 쪽)")]
    public Color laserColorEnd = new Color(0.3f, 0.8f, 1f, 0f);
    [Tooltip("레이저 너비 (시작)")]
    public float laserStartWidth = 0.006f;
    [Tooltip("레이저 너비 (끝)")]
    public float laserEndWidth = 0.002f;
    [Tooltip("레이저 시작점: 카메라 앞(m)")]
    public float laserStartForward = 0.35f;
    [Tooltip("레이저 시작점: 카메라 오른쪽(m)")]
    public float laserStartRight = 0.14f;
    [Tooltip("레이저 시작점: 카메라 아래(m)")]
    public float laserStartDown = 0.12f;

    // 레이저 호버/홀드 상태
    private SyncGrab _laserHoverTarget = null;
    private bool _isFlying = false;   // DOTween 날아오는 중
    private Vector3 _heldOriginalScale = Vector3.one;

    // 아웃라인 호버 상태 추적
    private GameObject _outlinedObject = null;   // 현재 아웃라인이 켜진 GO

    [Header("호버 아웃라인 (QuickOutline)")]
    [Tooltip("아웃라인 색상")]
    public Color outlineHoverColor = new Color(0.3f, 0.85f, 1f);
    [Tooltip("아웃라인 두께")]
    [Range(0f, 10f)]
    public float outlineWidth = 4f;

    // 내려놓기 키 — Update에서 직접 폴링 (HandleClick 진입 불필요)
    [Header("내려놓기 키")]
    public KeyCode dropKey = KeyCode.G;

    // 리프트 캐시 (OnStartClient에서 한 번만 탐색)
    private VehicleLiftController _vehicleLift = null;
    private BatteryLiftController _batteryLift = null;

    /// <summary>현재 호버 중인 오브젝트 정보</summary>
    private string _hoverGuideMsg = string.Empty;
    private Collider _lastHoveredCol = null;   // 직전 호버 콜라이더
    private GameObject _worldLabel = null;   // 생성된 월드 라벨 인스턴스

    // UI 레이캐스트 결과 재사용용
    private static readonly List<RaycastResult> _uiRaycastResults = new();

    // ═══════════════════════════════════════════════════════
    // 생명주기
    // ═══════════════════════════════════════════════════════

    private void Awake()
    {
        if (cameraPivot == null) cameraPivot = transform.Find("CameraPivot");
        _avatarCtrl = GetComponent<PcMobileAvatarController>();
        DisableAllChildCameras();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if(!IsOwner)
        {
            DisableAllChildCameras();
            return;
        }

        _isVR = IsVRDevice();
        Debug.Log($"[PC입력진단] OnStartClient isVR={_isVR} xrActive={UnityEngine.XR.XRSettings.isDeviceActive} xrName='{UnityEngine.XR.XRSettings.loadedDeviceName}'");
        EnableMyCamera();

        // ★ LocalNetworkTransform이 있으면 비활성화
        // FreeLookController가 직접 transform.position을 제어하기 때문에
        // LNT가 동시에 위치를 덮어쓰면 매 프레임 떨림 발생
        //var lnt = GetComponent<LocalNetworkTransform>();
        //if(lnt != null)
        //{
        //    lnt.enabled = false;
        //    Log("LocalNetworkTransform 비활성화 — FreeLookController가 위치 직접 제어");
        //}

        // 리프트 컨트롤러 캐시 (매 프레임 FindObjectOfType 방지)
        _vehicleLift = Object.FindFirstObjectByType<VehicleLiftController>();
        _batteryLift = Object.FindFirstObjectByType<BatteryLiftController>();

        if(!_isVR)
            StartCoroutine(InitCameraForDrag());
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        if(!IsOwner) return;
        StopAllCoroutines();
        _cameraReady = false;
        _heldObject = null;
    }

    private void OnDisable()
    {
        if(!IsOwner) return;
        StopAllCoroutines();
        _heldObject = null;
        _cameraReady = false;
        Log("OnDisable — 카메라 준비 상태를 초기화함 (다시 켜지면 Update 에서 복구)");
    }

    // 창 포커스를 잃어도(알트탭·녹화 프로그램·알림 팝업) 손에 든 물체를 놓지 않는다.
    // 예전에는 _heldObject만 null로 만들어서, 서버/SyncGrab은 여전히 '잡힌 상태'인데
    // 컨트롤러는 모르는 상태가 되어 물체를 다시 잡거나 내려놓을 수 없게 됐다(= 자주 놓치는 현상).
    private void OnApplicationFocus(bool hasFocus)
    {
        if(!IsOwner || hasFocus) return;
        // 눌려 있던 마우스 UI 상태만 정리
        if(_pressedLiftButton != null)
        {
            _pressedLiftButton.OnPointerUp(new UnityEngine.EventSystems.PointerEventData(
                UnityEngine.EventSystems.EventSystem.current));
            _pressedLiftButton = null;
        }
    }

    private void OnApplicationPause(bool isPaused)
    {
        // 모바일 백그라운드 전환도 동일하게 물체 상태는 유지
    }

    // ═══════════════════════════════════════════════════════
    // 카메라 초기화
    // ═══════════════════════════════════════════════════════

    private void DisableAllChildCameras()
    {
        foreach(var cam in GetComponentsInChildren<Camera>(includeInactive: true))
        {
            // 루트 오브젝트에 카메라가 있는 구버전 프리팹: 오브젝트를 끄면 플레이어 전체(아바타 포함)가 사라진다 → 컴포넌트만 끔
            if(cam.gameObject == gameObject) cam.enabled = false;
            else cam.gameObject.SetActive(false);
            cam.tag = "Untagged";
        }
    }

    [Header("카메라")]
    [Tooltip("플레이어 카메라의 near clip(m). 프리팹의 루트 카메라가 0.3m라 손에 든 물체·가까운 모델이 잘려 보이던 문제를 막는다. 0 이하면 프리팹 값을 그대로 사용.")]
    public float nearClip = 0.03f;

    private void ApplyNearClip(Camera cam)
    {
        if(cam == null || nearClip <= 0f) return;
        if(cam.nearClipPlane > nearClip)
        {
            Log($"카메라 near clip {cam.nearClipPlane:F2} → {nearClip:F2}");
            cam.nearClipPlane = nearClip;
        }
    }

    private void EnableMyCamera()
    {
        Camera found = GetComponentInChildren<Camera>(includeInactive: true);
        if(found == null) { Log("자식 카메라 없음"); return; }

        found.gameObject.SetActive(true);
        found.enabled = true;
        found.tag = "MainCamera";
        _mainCamera = found;
        ApplyNearClip(found);

        foreach(var cam in GetComponentsInChildren<Camera>(includeInactive: true))
        {
            if(cam.gameObject.name.Contains("UI"))
                cam.gameObject.SetActive(true);
        }

        Log($"내 카메라 활성화: {found.name}");
    }

    private IEnumerator InitCameraForDrag()
    {
        if(_mainCamera != null)
        {
            _cameraReady = true;
            Log($"카메라 준비 완료 (즉시) — {_mainCamera.name}");
            yield break;
        }

        yield return null;

        Camera found = GetComponentInChildren<Camera>(includeInactive: true);
        if(found != null)
        {
            found.gameObject.SetActive(true);
            found.tag = "MainCamera";
            _mainCamera = found;
            _cameraReady = true;
            Log($"카메라 준비 완료 (1프레임 후) — {_mainCamera.name}");
            yield break;
        }

        Log("자식 카메라 없음 — Camera.main 폴백");
        while(Camera.main == null) yield return null;
        _mainCamera = Camera.main;
        _cameraReady = true;
        Log($"카메라 준비 완료 (Camera.main 폴백) — {_mainCamera.name}");
    }

    private bool IsVRDevice()
    {
#if ENABLE_VR || UNITY_XR_MANAGEMENT
        if(UnityEngine.XR.XRSettings.isDeviceActive) return true;
        var d = UnityEngine.XR.XRSettings.loadedDeviceName;
        if(!string.IsNullOrEmpty(d) && d != "None") return true;
#endif
        return false;
    }

    // ═══════════════════════════════════════════════════════
    // Update
    // ═══════════════════════════════════════════════════════

    // [진단] 클릭했는데 상호작용이 안 될 때 어느 조건에서 막히는지 한 번에 보려고 클릭 순간의 내부 상태를 찍는다 (showDebugLog 와 무관).
    private void DiagnoseClick()
    {
        if(!Input.GetMouseButtonDown(0)) return;
        bool inv = EVInventoryUI.Instance != null && EVInventoryUI.Instance.panel != null && EVInventoryUI.Instance.panel.activeInHierarchy;
        Debug.Log($"[PC입력진단] 클릭 pos={Input.mousePosition} enabled={enabled} IsOwner={IsOwner} IsClientInitialized={IsClientInitialized} " +
                  $"isVR={_isVR} cameraReady={_cameraReady} mainCam={(_mainCamera != null)} inventoryOpen={inv} held={(_heldObject != null)} " +
                  $"xrActive={UnityEngine.XR.XRSettings.isDeviceActive} xrName='{UnityEngine.XR.XRSettings.loadedDeviceName}'");
    }

    private void Update()
    {
        DiagnoseClick();
        if(!IsOwner || !IsClientInitialized) return;

        // 카메라 이동/회전은 인벤토리가 켜져 있어도 작동하도록 위쪽 유지
        HandleMovement();
        HandleCameraLook();
        HandleVerticalInput();

        if(_isVR) return;

        // _cameraReady 는 OnStartClient 에서 한 번만 켜지고 OnDisable 에서 꺼진다. 컴포넌트/오브젝트가 한 번이라도
        // 껐다 켜지면 영영 false 로 남아 이동·시점만 되고 클릭 상호작용(해치·물체 잡기)이 전부 죽던 문제 → 여기서 복구한다.
        if(!_cameraReady)
        {
            if(_mainCamera == null) _mainCamera = GetComponentInChildren<Camera>(includeInactive: true);
            if(_mainCamera == null) return;
            _cameraReady = true;
            Log($"카메라 준비 상태 복구 — {_mainCamera.name} (껐다 켜진 뒤 상호작용이 막히던 문제 방지)");
        }

        if(_mainCamera == null)
        {
            _mainCamera = GetComponentInChildren<Camera>() ?? Camera.main;
            if(_mainCamera == null) return;
        }

        HandleLiftButtonRelease(); // LiftButton MouseUp 항상 체크
        CheckHoldScaleReady();     // 날아오기 완료 감지는 항상 체크 (애니메이션 끊김 방지)

        // ★ [핵심 추가] 인벤토리가 켜져 있는지 확인
        bool isInventoryOpen = EVInventoryUI.Instance != null &&
                               EVInventoryUI.Instance.panel != null &&
                               EVInventoryUI.Instance.panel.activeInHierarchy;

        if(isInventoryOpen)
        {
            // 인벤토리가 켜져 있다면, 뒤에 떠 있던 레이저와 아웃라인, 호버 텍스트를 모두 끕니다.
            if(laserLine != null) laserLine.enabled = false;
            SetOutline(null);
            SetGuideUI(string.Empty);

            if(_worldLabel != null)
            {
                var fader = _worldLabel.GetComponent<UIHoverFader>();
                if(fader != null) fader.FadeOut();
                else Destroy(_worldLabel);
                _worldLabel = null;
            }

            // 더 이상 아래쪽의 3D 월드 상호작용 코드를 실행하지 않고 여기서 끊어버립니다.
            return;
        }

        // 인벤토리가 꺼져 있을 때만 정상적으로 3D 상호작용 실행
        HandleHover();
        HandleUseInput();
        HandleClick();
        HandleDropKey();      // G키 내려놓기 
        UpdateLaserLine();    // 레이저 호버/홀드 상태 렌더링
    }

    // ═══════════════════════════════════════════════════════
    // FreeLook — 이동 / 회전 / 수직
    // ═══════════════════════════════════════════════════════

    private void HandleMovement()
    {
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        // 앞/뒤는 몸이 바라보는 수평면 기준 (아바타 리그에서는 루트가 기울지 않지만 구 프리팹 대비)
        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 moveDir = transform.right * x + flatForward * z;
        Vector3 horizontalVelocity = moveDir * moveSpeed;

        if(UseAvatarRig)
        {
            // 수직 자유 비행 대신 크라우치/높이 조절(PcMobileAvatarController)이 담당, 루트 Y는 접지 처리
            _planarVelocity = MoveWithCollision(horizontalVelocity * Time.deltaTime);
            return;
        }

        float targetVerticalVel = _desiredVertical * moveSpeed;
        _currentVerticalVelocity = Mathf.Lerp(
            _currentVerticalVelocity,
            targetVerticalVel,
            Time.deltaTime / Mathf.Max(0.0001f, verticalSmoothTime)
        );

        // 수직(자유 비행)은 그대로, 수평만 벽·가구에 막는다
        _planarVelocity = MoveWithCollision(horizontalVelocity * Time.deltaTime);
        transform.position += Vector3.up * (_currentVerticalVelocity * Time.deltaTime);
    }

    [Header("이동 충돌 (벽·가구)")]
    [Tooltip("작업장 벽·가구에 막히게 한다. 환경 콜라이더는 'Ignore Raycast' 레이어(Tools ▸ DoDrill ▸ 플레이어 충돌 만들기)에 있다.")]
    [SerializeField] private bool collideWithEnvironment = true;
    [SerializeField] private float collisionRadius = 0.28f;
    [SerializeField] private float collisionHeight = 1.7f;

    /// <summary>수평 이동량(이번 프레임)을 충돌을 적용해 옮기고, 실제로 움직인 속도(m/s)를 돌려준다(걷기 애니메이션용).</summary>
    private Vector3 MoveWithCollision(Vector3 wantDelta)
    {
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        if(!collideWithEnvironment)
        {
            transform.position += wantDelta;
            return wantDelta / dt;
        }

        float s = _avatarCtrl != null ? _avatarCtrl.AvatarScale : 1f;
        Vector3 before = transform.position;
        Vector3 after = PlayerCollision.Move(before, wantDelta, collisionRadius * s, collisionHeight * s);
        transform.position = after;

        Vector3 actual = (after - before); actual.y = 0f;
        Vector3 vel = actual / dt;
        float max = Mathf.Max(moveSpeed, 0.01f) * 1.5f;           // 겹침 해소로 튄 값이 걷기 속도를 부풀리지 않게
        return vel.magnitude > max ? vel.normalized * max : vel;
    }

    private void HandleCameraLook()
    {
        if(!Input.GetMouseButton(1)) return;

        float y = Input.GetAxis("Mouse X") * lookSensitivity;
        _rotX += Input.GetAxis("Mouse Y") * lookSensitivity * -1f;
        _rotX = Mathf.Clamp(_rotX, minLookX, maxLookX);

        if(UseAvatarRig)
        {
            // 루트에는 yaw만 (몸이 기울지 않음), 피치는 카메라 피벗에만
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y + y, 0f);
            cameraPivot.localRotation = Quaternion.Euler(_rotX, 0f, 0f);
        }
        else
        {
            transform.eulerAngles = new Vector3(_rotX, transform.eulerAngles.y + y, 0f);
        }
    }

    private void HandleVerticalInput()
    {
        if(Input.GetKeyDown(KeyCode.Q)) MoveUp();
        if(Input.GetKeyDown(KeyCode.E)) MoveDown();
        if(Input.GetKeyUp(KeyCode.Q)) StopVertical();
        if(Input.GetKeyUp(KeyCode.E)) StopVertical();

        // ── 리프트 키 ──────────────────────────────
        // 캐시 없으면 재탐색 (씬 전환 등 대비)
        if(_vehicleLift == null) _vehicleLift = Object.FindFirstObjectByType<VehicleLiftController>();
        if(_batteryLift == null) _batteryLift = Object.FindFirstObjectByType<BatteryLiftController>();

        if(_vehicleLift != null)
        {
            if(Input.GetKeyDown(liftUpKey)) { _vehicleLift.OnUpButton(); Log($"차량 리프트 ▲ ({liftUpKey})"); }
            if(Input.GetKeyDown(liftDownKey)) { _vehicleLift.OnDownButton(); Log($"차량 리프트 ▼ ({liftDownKey})"); }
            if(Input.GetKeyUp(liftUpKey) || Input.GetKeyUp(liftDownKey)) _vehicleLift.OnStopButton();
        }

        if(_batteryLift != null)
        {
            if(Input.GetKeyDown(batteryUpKey)) { _batteryLift.OnUpButton(); Log($"배터리 잭 ▲ ({batteryUpKey})"); }
            if(Input.GetKeyDown(batteryDownKey)) { _batteryLift.OnDownButton(); Log($"배터리 잭 ▼ ({batteryDownKey})"); }
            if(Input.GetKeyUp(batteryUpKey) || Input.GetKeyUp(batteryDownKey)) _batteryLift.OnStopButton();
        }
    }

    // 아바타 리그: Q/E(와 UI 버튼)는 자유 비행이 아니라 높이(크라우치) 방향 입력.
    //  MoveUp = 일어서기(-1), MoveDown = 낮추기(+1), Stop = 현재 높이 유지(0)
    // 구 프리팹(아바타 없음)은 기존 수직 비행 그대로.
    public void MoveUp() { if(UseAvatarRig) { _crouchInput = -1f; _desiredVertical = 0f; } else _desiredVertical = 0.5f; }
    public void MoveDown() { if(UseAvatarRig) { _crouchInput = 1f; _desiredVertical = 0f; } else _desiredVertical = -0.5f; }
    public void StopVertical() { _crouchInput = 0f; _desiredVertical = 0f; }

    // ═══════════════════════════════════════════════════════
    // 클릭 처리
    // ═══════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════
    // 호버 가이드
    // ═══════════════════════════════════════════════════════

    private void HandleHover()
    {
        bool hit = false;
        RaycastHit hitInfo = default;

        if(_heldObject == null && _mainCamera != null)
        {
            // 실제 마우스 위치를 향해 레이캐스트
            Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);

            hit = Physics.Raycast(ray, out hitInfo, raycastMaxDistance, clickLayers);
            if(!hit)
                hit = Physics.Raycast(ray, out hitInfo, raycastMaxDistance);
        }

        Collider hoveredCol = hit ? hitInfo.collider : null;

        // ── 같은 대상을 계속 호버 중일 때 (위치만 부드럽게 마우스를 따라감) ──
        if(hoveredCol == _lastHoveredCol)
        {
            if(_worldLabel != null && hit)
            {
                Vector3 targetPos = hitInfo.point + Vector3.up * labelHeightOffset;
                _worldLabel.transform.position = Vector3.Lerp(_worldLabel.transform.position, targetPos, Time.deltaTime * 15f);
            }
            return;
        }

        _lastHoveredCol = hoveredCol;

        // ── 대상이 바뀌었을 때: 기존 라벨 스스로 사라지게 명령 ──
        if(_worldLabel != null)
        {
            var fader = _worldLabel.GetComponent<UIHoverFader>();
            if(fader != null) fader.FadeOut();
            else Destroy(_worldLabel); // 혹시 fader가 없다면 강제 즉시 파괴

            _worldLabel = null;
        }

        if(hoveredCol == null)
        {
            SetGuideUI(string.Empty);
            return;
        }

        // 오브젝트 이름 + 메시지 결정
        string objName = GetHoverTargetName(hoveredCol);
        string msg = GetHoverActionMsg(hoveredCol);

        // ★ 추가: 메시지가 비어있다면(상호작용 대상이 아니라면) 가이드를 띄우지 않음
        if(string.IsNullOrEmpty(msg))
        {
            SetGuideUI(string.Empty);

            // 기존에 떠있던 라벨이 있다면 지워줌
            if(_worldLabel != null)
            {
                var fader = _worldLabel.GetComponent<UIHoverFader>();
                if(fader != null) fader.FadeOut();
                else Destroy(_worldLabel);
                _worldLabel = null;
            }
            return; // 여기서 중단하여 새 라벨 생성을 막음
        }

        //Log($"호버: {objName} — {msg}");
        SetGuideUI(msg);

        // 새 라벨 생성
        SpawnWorldLabel(hitInfo.point, objName, msg);
    }

    private void SpawnWorldLabel(Vector3 worldPos, string objName, string actionMsg)
    {
        if(worldLabelPrefab == null) return;

        Vector3 pos = worldPos + Vector3.up * labelHeightOffset;
        _worldLabel = Instantiate(worldLabelPrefab, pos, Quaternion.identity);

        // ★ [핵심 1] 라벨이 마우스 광선을 막아서 잔상을 유발하지 않도록 레이어를 Ignore Raycast(2)로 강제 고정
        SetLayerRecursively(_worldLabel, 2);

        // 텍스트 설정
        var label = _worldLabel.GetComponent<WorldHoverLabel>();
        if(label != null)
        {
            label.SetText(objName, actionMsg);
            // return; <--- 이전 코드의 치명적 버그! (삭제됨)
        }
        else
        {
            var tmpUGUI = _worldLabel.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if(tmpUGUI != null)
            {
                tmpUGUI.text = $"<b>{objName}</b>\n<size=80%><color=#AAFFAA>{actionMsg}</color></size>";
            }
            else
            {
                var tmp3D = _worldLabel.GetComponentInChildren<TMPro.TextMeshPro>();
                if(tmp3D != null)
                    tmp3D.text = $"<b>{objName}</b>\n<size=80%><color=#AAFFAA>{actionMsg}</color></size>";
            }
        }

        // ★ [핵심 2] 외부 코루틴 대신, 라벨 스스로 페이드를 관리하는 컴포넌트 부착
        _worldLabel.AddComponent<UIHoverFader>();
    }

    // 오브젝트와 그 자식들의 레이어를 일괄 변경하는 유틸리티
    private void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach(Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private void SetGuideUI(string msg)
    {
        bool hasMsg = !string.IsNullOrEmpty(msg);
        _hoverGuideMsg = msg;

        if(hoverGuideText != null)
            hoverGuideText.text = hasMsg ? msg : string.Empty;

        if(hoverGuidePanel != null)
            hoverGuidePanel.SetActive(hasMsg);
    }

    private string GetHoverTargetName(Collider col)
    {
        //TODO. 수정 필요(로컬라이징)
        // 우선순위 순으로 이름 결정
        var item = col.GetComponent<TaskItem>() ?? col.GetComponentInParent<TaskItem>();
        if(item != null) return item.prefabId;

        var anim = col.GetComponent<ClickableAnimator>() ?? col.GetComponentInParent<ClickableAnimator>();
        if(anim != null) return anim.gameObject.name;

        var zone = col.GetComponent<TaskInteractionZone>() ?? col.GetComponentInParent<TaskInteractionZone>();
        if(zone != null) return zone.zoneId;

        var part = col.GetComponent<InteractablePart>() ?? col.GetComponentInParent<InteractablePart>();
        if(part != null) return col.gameObject.name;

        return col.gameObject.name;
    }

    /// <summary>콜라이더 기준으로 액션 안내 메시지 반환</summary>
    private string GetHoverActionMsg(Collider col)
    {
        if(col == null) return string.Empty;

        // 들고 있는 상태
        if(_heldObject != null) return "클릭하여 내려놓기";

        // ClickableAnimator
        var anim = col.GetComponent<ClickableAnimator>() ?? col.GetComponentInParent<ClickableAnimator>();
        if(anim != null) return anim.IsOpen ? "클릭하여 닫기" : "클릭하여 열기";

        // TaskItem
        var item = col.GetComponent<TaskItem>() ?? col.GetComponentInParent<TaskItem>();
        if(item != null)
        {
            var sg = item.GetComponent<SyncGrab>() ?? item.GetComponentInParent<SyncGrab>();
            if(sg != null) return sg.IsGrabbed ? "다른 플레이어가 사용 중" : "클릭하여 집기";
            return "클릭";
        }

        // TaskInteractionZone
        var zone = col.GetComponent<TaskInteractionZone>() ?? col.GetComponentInParent<TaskInteractionZone>();
        if(zone != null && zone.gameObject.activeInHierarchy) return "클릭하여 상호작용";

        // InteractablePart
        var part = col.GetComponent<InteractablePart>() ?? col.GetComponentInParent<InteractablePart>();
        if(part != null) return "클릭하여 조작";

        return string.Empty;
    }

    /// <summary>
    /// Update에서 매 프레임 MouseUp을 체크해 LiftButton 해제.
    /// HandleClick()은 GetMouseButtonDown에서만 호출되므로
    /// MouseUp은 별도 처리 필요.
    /// </summary>
    private void HandleLiftButtonRelease()
    {
        if(_pressedLiftButton == null) return;
        if(!Input.GetMouseButtonUp(0)) return;

        _pressedLiftButton.OnPointerUp(new UnityEngine.EventSystems.PointerEventData(
            UnityEngine.EventSystems.EventSystem.current));
        Log($"리프트 버튼 해제: {_pressedLiftButton.name}");
        _pressedLiftButton = null;
    }

    // 현재 눌리고 있는 LiftButtonUI 추적 (MouseUp 처리용)
    private LiftButtonUI _pressedLiftButton = null;

    /// <summary>
    /// World Space Canvas 위의 버튼(LiftButtonUI 등)을 처리.
    /// MouseDown → OnPointerDown / MouseUp → OnPointerUp 으로 분리 호출.
    /// LiftButtonUI는 "누르는 동안 작동" 방식이므로 Up도 반드시 전달해야 함.
    /// </summary>
    private bool HandleWorldSpaceUIClick(Vector2 screenPos)
    {
        if(_mainCamera == null) return false;

        // ── MouseUp: 누르고 있던 버튼 해제 ──────────────────────────
        bool isMouseUp = Input.GetMouseButtonUp(0);
        if(isMouseUp && _pressedLiftButton != null)
        {
            _pressedLiftButton.OnPointerUp(new UnityEngine.EventSystems.PointerEventData(
                UnityEngine.EventSystems.EventSystem.current));
            Log($"리프트 버튼 해제: {_pressedLiftButton.name}");
            _pressedLiftButton = null;
            return true;
        }

        // ── MouseDown: 새 버튼 감지 ──────────────────────────────────
        Ray ray = _mainCamera.ScreenPointToRay(screenPos);

        // 리프트 버튼은 월드 캔버스 위에 있고 콜라이더가 없어 물리 레이캐스트로는 맞지 않는다.
        // 화면 좌표 ↔ 캔버스 사각형 교차로 직접 판정한다 (EventSystem 경로가 실패해도 동작).
        var uiLift = LiftButtonUI.FindAtScreen(_mainCamera, screenPos, raycastMaxDistance, transform, out float uiLiftDist);
        // 버튼보다 가까이에 다른 물체(차체·MSD·커넥터 등)가 있으면 그 물체를 누른 것이다.
        // 예전에는 화면상 위치만 겹치면 버튼이 눌려, MSD 를 닫으려 클릭할 때 리프트가 계속 내려갔다.
        if(uiLift != null && Physics.Raycast(ray, out RaycastHit blockHit, uiLiftDist - 0.05f, ~0, QueryTriggerInteraction.Ignore)
           && !blockHit.collider.transform.IsChildOf(transform)
           && blockHit.collider.GetComponentInParent<LiftButtonUI>() == null)
        {
            Log($"리프트 버튼 무시 — 앞에 {blockHit.collider.name}({blockHit.distance:F2}m) 이(가) 있음");
            uiLift = null;
        }
        if(uiLift != null)
        {
            _pressedLiftButton = uiLift;
            uiLift.OnPointerDown(new UnityEngine.EventSystems.PointerEventData(
                UnityEngine.EventSystems.EventSystem.current));
            Log($"리프트 버튼 누름(UI 판정, {uiLiftDist:F2}m): {uiLift.name}");
            return true;
        }

        if(!Physics.Raycast(ray, out RaycastHit hit, raycastMaxDistance)) return false;

        // LiftButtonUI 확인
        var liftBtn = hit.collider.GetComponent<LiftButtonUI>()
                   ?? hit.collider.GetComponentInParent<LiftButtonUI>();
        if(liftBtn != null)
        {
            _pressedLiftButton = liftBtn;
            liftBtn.OnPointerDown(new UnityEngine.EventSystems.PointerEventData(
                UnityEngine.EventSystems.EventSystem.current));
            Log($"리프트 버튼 누름: {hit.collider.gameObject.name}");
            return true;
        }

        // ClickableAnimator (문/후드) 확인
        var anim = hit.collider.GetComponent<ClickableAnimator>()
                ?? hit.collider.GetComponentInParent<ClickableAnimator>();
        if(anim != null)
        {
            Log($"ClickableAnimator 클릭: {anim.gameObject.name}");
            anim.OnPCClick();
            return true;
        }

        return false;
    }

    private void HandleClick()
    {
        bool clicked = false;
        Vector2 clickPos = Vector2.zero;

        // 예전에는 '#if UNITY_IOS || UNITY_ANDROID' 로 컴파일 때 갈랐다. 그러면 Active Build Target 이 Android 일 때
        // (모바일 APK 를 빌드하느라 에디터를 Android 로 바꿔 둔 상태) 에디터의 PC 플레이어가 마우스 클릭을 전혀 읽지 못했다.
        // → 실행 중에 판정: 터치가 있으면 터치, 아니면 마우스.
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            clicked = true;
            clickPos = Input.GetTouch(0).position;
        }
        else if (!Application.isMobilePlatform && Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1))
        {
            clicked = true;
            clickPos = Input.mousePosition;
        }

        if (!clicked) return;

        // ★ UI 블로킹 체크
        bool overUI = IsPointerOverUI(clickPos);
        Log($"[클릭] pos={clickPos} overUI={overUI} held={(_heldObject != null)} ES={(EventSystem.current != null ? EventSystem.current.name : "없음")}" +
            $"/{(EventSystem.current != null && EventSystem.current.currentInputModule != null ? EventSystem.current.currentInputModule.GetType().Name : "모듈없음")}");
        if (overUI) { FallbackUIClick(clickPos); return; }

        // ── 0. 지금 단계의 클릭 대상이 레이 경로 바로 뒤에 있으면 그것을 우선 ──
        // (접지 케이블 재연결 때 차 문·후드 래치 콜라이더가 앞을 막아 커넥터가 안 눌리던 문제)
        if (_heldObject == null && TryClickCurrentTaskTarget(clickPos))
        {
            Managers.Sound.Play(clickSound);
            return;
        }

        // ── 1. 월드 UI 버튼(리프트 등) 처리 ──
        if (HandleWorldSpaceUIClick(clickPos))
        {
            Managers.Sound.Play(clickSound);
            return;
        }

        // ── 2. 이미 무언가를 들고 있는 상태라면? ──
        if (_heldObject != null)
        {
            // 들고 있는 상태에서는 "새로 집기" 로직이 실행되지 않도록 여기서 처리를 끝냅니다.
            // PC 버전에서 우클릭(1)으로 내려놓기를 구현하고 싶다면 아래 조건을 유지하세요.
            if (Input.GetMouseButtonDown(1))
            {
                DropHeldObject();
            }
            return; // ★ 중요: 들고 있을 때는 아래의 "집기" 로직을 타지 않고 종료
        }

        // ── 3. 태스크/존 상호작용 (문 열기, 버튼 누르기 등) ──
        if (HandleTaskClick(clickPos))
        {
            Managers.Sound.Play(clickSound);
            return;
        }

        // ── 4. 아이템 새로 집기 (빈 손일 때만 여기까지 도달) ──
        SyncGrab target = RaycastSyncGrab(clickPos);
        if (target != null && !target.IsGrabbed)
        {
            Log($"집기 성공: {target.name}");
            _heldObject = target;
            target.OnPCClick();

            Managers.Sound.Play(grabSound);
            HeldItemUI.Instance?.UpdateUI(target.gameObject);

            // ★ 핵심 수정: 집기에 성공했다면 이번 프레임은 여기서 완전히 종료!
            // 이 return이 없으면 아래 중복된 로직들에 의해 그랩이 꼬일 수 있습니다.
            return;
        }
    }

    // TaskItem / Zone 처리. 인터랙션이 발생하면 true 반환
    private bool HandleTaskClick(Vector2 screenPos)
    {
        Ray ray = _mainCamera.ScreenPointToRay(screenPos);

        if (!Physics.Raycast(ray, out RaycastHit hit, raycastMaxDistance, clickLayers))
            return false;

        Log($"[Raycast] 클릭 감지됨: {hit.collider.name}");

        // ── 0순위: 사고 감지 (가장 먼저 체크) ──
        var part = hit.collider.GetComponent<InteractablePart>()
                   ?? hit.collider.GetComponentInParent<InteractablePart>();

        if (part != null)
        {
            Log($"InteractablePart 발견: {part.name}. 사고 체크 시작.");

            // ★ 핵심: OnPCClick이 bool을 반환하도록 수정하거나, 
            // 내부의 사고 체크 함수를 직접 호출해서 "안전할 때만" 통과시킵니다.
            // 만약 사고가 발생했다면(TriggerAccident 호출됨) 여기서 return true로 로직을 종료합니다.
            if (!part.CheckSafetyAndTriggerAccident())
            {
                Log("사고 발생! 상호작용을 중단합니다.");
                return true; // 사고 연출이 시작되었으므로 클릭 로직 종료
            }

            Log("안전 확인됨. 다음 상호작용으로 진행합니다.");
            // 안전하다면 return하지 않고 아래의 TaskItem/SyncGrab 로직으로 내려갑니다.
        }

        // ── 1순위: ClickableAnimator (문, 후드 등) ──
        var clickableAnim = hit.collider.GetComponent<ClickableAnimator>()
                          ?? hit.collider.GetComponentInParent<ClickableAnimator>();

        if (clickableAnim != null)
        {
            Log($"[애니메이션] {clickableAnim.uniqueId} 작동");
            clickableAnim.OnPCClick();
            return true;
        }

        // ── 2순위: TaskItem / SyncGrab (아이템 집기) ──
        var item = hit.collider.GetComponent<TaskItem>()
                ?? hit.collider.GetComponentInParent<TaskItem>();

        if (item != null)
        {
            Log($"TaskItem 클릭: {item.prefabId}");

            var syncGrab = item.GetComponent<SyncGrab>()
                        ?? item.GetComponentInParent<SyncGrab>();

            if (syncGrab != null)
            {
                if (syncGrab.IsGrabbed) return true;

                Log($"SyncGrab 레이저 그랩 시작: {item.prefabId}");
                StartLaserPull(syncGrab);
                return true;
            }

            //InteractionEvents.FireItemGrabbed(item.prefabId);
            //InteractionEvents.FireItemUsed(item.prefabId);
            return true;
        }

        // ── 3순위: TaskInteractionZone (빈손 터치) ──
        var zone = hit.collider.GetComponent<TaskInteractionZone>()
                ?? hit.collider.GetComponentInParent<TaskInteractionZone>();

        if (zone != null && zone.gameObject.activeInHierarchy)
        {
            InteractionEvents.FireZoneActivated(zone.zoneId, string.Empty);
            return true;
        }

        return false;
    }


    /// <summary>레이캐스트로 TaskInteractionZone 감지</summary>
    private TaskInteractionZone RaycastZone(Vector2 screenPos)
    {
        if(_mainCamera == null) return null;
        Ray ray = _mainCamera.ScreenPointToRay(screenPos);
        if(!Physics.Raycast(ray, out RaycastHit hit, raycastMaxDistance)) return null;

        return hit.collider.GetComponent<TaskInteractionZone>()
            ?? hit.collider.GetComponentInParent<TaskInteractionZone>();
    }

    // ═══════════════════════════════════════════════════════
    // Use 입력 (E키 or 우클릭) — 들고 있는 도구 사용
    // ═══════════════════════════════════════════════════════

    private void HandleUseInput()
    {
        // E키 또는 우클릭(rightClickUse=true일 때, 카메라 회전과 구분 필요)
        bool usePressed = Input.GetKeyDown(useKey)
                       || (rightClickUse && Input.GetMouseButtonDown(1)
                           && Input.GetAxis("Mouse X") == 0f
                           && Input.GetAxis("Mouse Y") == 0f);

        if(!usePressed) return;

        if(_heldObject == null)
        {
            // ── 4. 아이템 사용 시 사운드 ──
            Managers.Sound.Play(useSound);
            // 들고 있지 않을 때 F키 → 바라보는 Zone 직접 활성화 (빈손 터치)
            if (_mainCamera == null) return;
            Ray ray = _mainCamera.ScreenPointToRay(
                new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            if(Physics.Raycast(ray, out RaycastHit hit, raycastMaxDistance))
            {
                var zone = hit.collider.GetComponent<TaskInteractionZone>()
                        ?? hit.collider.GetComponentInParent<TaskInteractionZone>();
                if(zone != null && zone.gameObject.activeInHierarchy)
                {
                    Log($"E키 빈손 Zone 터치: {zone.zoneId}");
                    InteractionEvents.FireZoneActivated(zone.zoneId, string.Empty);
                    return;
                }
            }
            return;
        }

        // 들고 있을 때 E키 → FireItemUsed 발행
        var item = _heldObject.GetComponent<TaskItem>();
        string itemId = item != null ? item.prefabId : _heldObject.name;

        Log($"E키 Use: {itemId}");
        InteractionEvents.FireItemUsed(itemId);

        // 메거·멀티미터: 화면 가운데로 본 단자에 빨간 프로브를 댄 것으로 처리 (본체를 들고 작은 단자에 프로브 끝을 맞추기 어려움)
        var meter = _heldObject.GetComponent<MultimeterMaster>();
        if(meter != null && _mainCamera != null && CurrentTaskIsMeasure())
        {
            var mpHit = FindMeasurementPointAtCenter(_heldObject.transform);
            if(mpHit != null)
            {
                Log($"E키 프로브 측정: {mpHit.terminalId}");
                meter.ProbeRedAt(mpHit.terminalId);
                return;
            }
        }

        // 바라보는 Zone에도 함께 발행 (ZoneAndUseModule 완료용)
        if(_mainCamera != null)
        {
            Ray ray = _mainCamera.ScreenPointToRay(
                new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            if(Physics.Raycast(ray, out RaycastHit hit, raycastMaxDistance))
            {
                var zone = hit.collider.GetComponent<TaskInteractionZone>()
                        ?? hit.collider.GetComponentInParent<TaskInteractionZone>();
                if(zone != null)
                {
                    Log($"E키 ZoneActivated: {zone.zoneId} ← {itemId}");
                    InteractionEvents.FireZoneActivated(zone.zoneId, itemId);
                }

                // MeasurementPoint 있으면 멀티테스터 측정 트리거 — 지금 단계가 측정 단계일 때만.
                // (예전: 드릴로 볼트를 풀다 F 를 누르면 옆의 고전압 단자까지 '드릴로 측정'으로 보내져 단계가 실패하고 드릴이 내려놓였다)
                var mp = hit.collider.GetComponent<MeasurementPoint>()
                      ?? hit.collider.GetComponentInParent<MeasurementPoint>();
                if(mp != null && CurrentTaskIsMeasure())
                {
                    Log($"E키 측정: {mp.terminalId}");
                    InteractionEvents.FireZoneActivated(mp.terminalId, itemId);
                }
            }
        }
    }

    private static ScenarioReceiver s_scenarioReceiver;

    /// <summary>화면 가운데 방향의 측정점(단자). 들고 있는 물체 자신의 콜라이더는 건너뛴다. 단자는 트리거라 트리거도 맞힌다.</summary>
    private MeasurementPoint FindMeasurementPointAtCenter(Transform ignoreRoot)
    {
        Ray ray = _mainCamera.ScreenPointToRay(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
        var hits = Physics.RaycastAll(ray, raycastMaxDistance, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach(var h in hits)
        {
            if(ignoreRoot != null && h.collider.transform.IsChildOf(ignoreRoot)) continue;
            var mp = h.collider.GetComponent<MeasurementPoint>() ?? h.collider.GetComponentInParent<MeasurementPoint>();
            if(mp != null) return mp;
            if(!h.collider.isTrigger) return null; // 단자보다 앞에 벽·차체가 있으면 측정하지 않는다
        }
        return null;
    }

    /// <summary>지금 단계의 동작이 측정(MeasureVoltage/MeasureResistance 등)인지. 시나리오 정보가 없으면 예전처럼 true.</summary>
    [Tooltip("지금 단계의 클릭 대상이 앞을 가린 물체(문·차체 등) 뒤 이 거리(m) 안에 있으면 대상을 누른 것으로 본다.")]
    public float taskTargetPassThrough = 1.0f;
    private static readonly RaycastHit[] s_taskClickHits = new RaycastHit[32];

    private bool TryClickCurrentTaskTarget(Vector2 screenPos)
    {
        var cfg = CurrentTaskConfig();
        string target = cfg?.targetObjName;
        if(string.IsNullOrEmpty(target) || _mainCamera == null) return false;

        Ray ray = _mainCamera.ScreenPointToRay(screenPos);
        int n = Physics.RaycastNonAlloc(ray, s_taskClickHits, raycastMaxDistance, ~0, QueryTriggerInteraction.Collide);
        if(n == 0) return false;
        System.Array.Sort(s_taskClickHits, 0, n, s_hitDistanceComparer);

        float first = -1f;
        for(int i = 0; i < n; i++)
        {
            var col = s_taskClickHits[i].collider;
            if(col == null || col.transform.IsChildOf(transform)) continue;
            if(first < 0f && !col.isTrigger) first = s_taskClickHits[i].distance;
            if(first >= 0f && s_taskClickHits[i].distance > first + taskTargetPassThrough) break;

            var anim = col.GetComponentInParent<ClickableAnimator>();
            if(anim == null || (anim.uniqueId != target && anim.gameObject.name != target)) continue;
            if(first < 0f || s_taskClickHits[i].distance <= first + 0.001f) return false; // 가리는 것이 없으면 기존 흐름대로
            Log($"단계 대상 우선 클릭: {anim.uniqueId} (앞을 가린 물체 {first:F2}m 무시)");
            anim.OnPCClick();
            return true;
        }
        return false;
    }

    private sealed class HitDistanceComparer : System.Collections.Generic.IComparer<RaycastHit>
    {
        public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
    }
    private static readonly HitDistanceComparer s_hitDistanceComparer = new HitDistanceComparer();

    /// <summary>지금 진행 중인 단계의 설정. 시나리오 정보가 없으면 null.</summary>
    private static ModuleConfig CurrentTaskConfig()
    {
        var r = ScenarioStateReceiver.Instance;
        if(r == null) return null;
        var sc = r.CurrentScenario;
        if(sc == null)
        {
            if(s_scenarioReceiver == null) s_scenarioReceiver = FindFirstObjectByType<ScenarioReceiver>();
            if(s_scenarioReceiver != null) sc = s_scenarioReceiver.ReceivedData;
        }
        if(sc == null || sc.scenario == null || sc.scenario.tasks == null) return null;
        int i = r.CurrentTaskState.taskIndex;
        if(i < 0 || i >= sc.scenario.tasks.Count) return null;
        return sc.GetModuleConfig(sc.scenario.tasks[i].moduleId);
    }

    private static bool CurrentTaskIsMeasure()
    {
        var r = ScenarioStateReceiver.Instance;
        if(r == null) return true;
        // 단계 진행 상태는 ScenarioStateReceiver, 시나리오 데이터는 스냅샷이 없으면 ScenarioReceiver 에서 받는다
        // (스냅샷 없이 시작한 세션은 CurrentScenario 가 비어 있어, 예전에는 검사를 건너뛰고 드릴로도 측정 신호가 나갔다)
        var sc = r.CurrentScenario;
        if(sc == null)
        {
            if(s_scenarioReceiver == null) s_scenarioReceiver = FindFirstObjectByType<ScenarioReceiver>();
            if(s_scenarioReceiver != null) sc = s_scenarioReceiver.ReceivedData;
        }
        if(sc == null || sc.scenario == null || sc.scenario.tasks == null) return true;
        int i = r.CurrentTaskState.taskIndex;
        if(i < 0 || i >= sc.scenario.tasks.Count) return false;
        var cfg = sc.GetModuleConfig(sc.scenario.tasks[i].moduleId);
        return cfg != null && !string.IsNullOrEmpty(cfg.actionType)
            && cfg.actionType.StartsWith("Measure", System.StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════
    // UI 블로킹 체크
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// 해당 스크린 좌표에 EventSystem이 감지하는 UI가 있으면 true
    /// World Space 캔버스는 3D 레이캐스트로 처리되므로 여기서는 무시됨
    /// → Screen Space Overlay/Camera 캔버스만 걸림
    /// </summary>
    private bool IsPointerOverUI(Vector2 screenPos)
    {
        if(EventSystem.current == null) return false;

        var pointerData = new PointerEventData(EventSystem.current) { position = screenPos };
        _uiRaycastResults.Clear();
        EventSystem.current.RaycastAll(pointerData, _uiRaycastResults);

        foreach(var result in _uiRaycastResults)
        {
            var canvas = result.gameObject.GetComponentInParent<Canvas>();
            if(canvas == null) continue;

            // World Space 캔버스는 3D 오브젝트처럼 동작 → 무시
            if(canvas.renderMode == RenderMode.WorldSpace) continue;

            return true; // Screen Space 캔버스 위 → 클릭 차단
        }

        return false;
    }

    /// <summary>
    /// PC 빌드에서 화면 UI 버튼(가이드 '확인', 상단 아이콘)이 눌리지 않던 문제 대비.
    /// UI 레이캐스트에는 버튼이 잡히는데 현재 EventSystem 입력 모듈이 포인터를 추적하지 못하고 있으면
    /// (모듈 비활성·액션 꺼짐·다른 EventSystem 이 current 인 경우) 클릭 이벤트를 직접 전달한다.
    /// 모듈이 정상이면 아무 것도 하지 않아 두 번 눌리는 일은 없다.
    /// </summary>
    private void FallbackUIClick(Vector2 screenPos)
    {
        var es = EventSystem.current;
        if (es == null) return;

        bool moduleTracks = es.currentInputModule != null && es.currentInputModule.isActiveAndEnabled
                            && es.IsPointerOverGameObject();
        if (moduleTracks) return;

        GameObject top = null;
        foreach (var r in _uiRaycastResults)
        {
            var c = r.gameObject.GetComponentInParent<Canvas>();
            if (c != null && c.renderMode != RenderMode.WorldSpace) { top = r.gameObject; break; }
        }
        if (top == null) return;

        var handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(top);
        Debug.Log($"[UI 입력] 모듈이 클릭을 못 받음 → 직접 전달: {GetGameObjectPath(top)} " +
                  $"(ES={es.name}, module={(es.currentInputModule ? es.currentInputModule.GetType().Name : "없음")}, handler={(handler ? handler.name : "없음")})");
        if (handler == null) return;

        var data = new PointerEventData(es) { position = screenPos, button = PointerEventData.InputButton.Left,
                                              pointerPress = handler, rawPointerPress = top, clickCount = 1 };
        ExecuteEvents.Execute(handler, data, ExecuteEvents.pointerClickHandler);
    }

    /// <summary>디버그용 — UI 위일 때 어떤 오브젝트인지 이름 반환</summary>
    private string GetUIObjectName(Vector2 screenPos)
    {
        if (EventSystem.current == null) return "EventSystem 없음";

        var pointerData = new PointerEventData(EventSystem.current) { position = screenPos };
        _uiRaycastResults.Clear();
        EventSystem.current.RaycastAll(pointerData, _uiRaycastResults);

        foreach (var result in _uiRaycastResults)
        {
            var canvas = result.gameObject.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode != RenderMode.WorldSpace)
            {
                // ★ 수정: 단순 이름 대신 부모 경로까지 다 찍어버립니다.
                return GetGameObjectPath(result.gameObject);
            }
        }
        return "없음";
    }

    // 부모 경로를 찾아주는 헬퍼 함수 추가
    private string GetGameObjectPath(GameObject obj)
    {
        string path = obj.name;
        while (obj.transform.parent != null)
        {
            obj = obj.transform.parent.gameObject;
            path = obj.name + "/" + path;
        }
        return path;
    }

    // ═══════════════════════════════════════════════════════
    // 레이캐스트
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// 레이캐스트 후 SyncGrab 반환.
    /// SyncGrab 없어도 ClickableAnimator가 있으면 처리하고 null 반환.
    /// </summary>
    private SyncGrab RaycastSyncGrab(Vector2 screenPos)
    {
        Ray ray = _mainCamera.ScreenPointToRay(screenPos);

        if(showDebugRay)
            Debug.DrawRay(ray.origin, ray.direction * raycastMaxDistance, Color.red, 0.3f);

        if(!Physics.Raycast(ray, out RaycastHit hit, raycastMaxDistance, clickLayers))
        {
            Log("레이캐스트 히트 없음");
            return null;
        }

        Log($"히트: {hit.collider.gameObject.name}");

        // SyncGrab 확인
        SyncGrab sg = hit.collider.GetComponentInParent<SyncGrab>();
        if(sg != null) return sg;

        // SyncGrab 없음 — ClickableAnimator 확인
        var anim = hit.collider.GetComponent<ClickableAnimator>()
                ?? hit.collider.GetComponentInParent<ClickableAnimator>();
        if(anim != null)
        {
            Log($"ClickableAnimator 클릭 (clickLayers 경로): {anim.gameObject.name}");
            anim.OnPCClick();
            return null; // SyncGrab 없으므로 null, 하지만 이미 처리됨
        }

        // InteractablePart 확인 (볼트, MSD 등 씬 고정 오브젝트)
        var part = hit.collider.GetComponent<InteractablePart>()
                ?? hit.collider.GetComponentInParent<InteractablePart>();
        if(part != null)
        {
            Log($"InteractablePart 클릭: {hit.collider.gameObject.name}");
            part.OnPCClick();
            return null;
        }

        Log("SyncGrab 없음");
        return null;
    }

    // ═══════════════════════════════════════════════════════
    // 유틸
    // ═══════════════════════════════════════════════════════

    private void Log(string msg)
    {
        if(showDebugLog) Debug.Log($"[FreeLook] {msg}");
    }
    // ═══════════════════════════════════════════════════════
    // 레이저 그랩 — PC DistanceGrab 느낌
    // XR Starter Kit DistanceGrabberLineBender 참고 — 베지어 곡선 레이저
    //
    // 흐름:
    //   [호버] 레이캐스트 → InteractablePart 또는 SyncGrab 감지
    //          카메라 → 부품 방향 곡선 레이저 표시
    //   [좌클릭] StartLaserPull → SyncGrab.OnPCClick() → PCFlyTo(holdPoint)
    //            레이저 즉시 OFF / _isFlying=true
    //   [홀드완료] IsGrabbed 감지 → ApplyHoldScale
    //   [G키/우클릭] HandleDropKey/HandleClick → DropHeldObject
    //               스케일 복원 → SyncGrab.OnPCClick() 두 번째 호출 → 내려놓기
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Update에서 독립 폴링 — G키 내려놓기.
    /// HandleClick은 마우스 클릭 시만 진입하므로 키보드는 여기서 처리.
    /// </summary>
    private void HandleDropKey()
    {
        if(_heldObject == null) return;
        if(Input.GetKeyDown(dropKey))
            DropHeldObject();
    }

    /// <summary>매 프레임 — 호버/홀드 상태에 맞게 곡선 레이저 갱신.</summary>
    /// <summary>매 프레임 — 호버/홀드 상태에 맞게 곡선 레이저 갱신.</summary>
    private void UpdateLaserLine()
    {
        EnsureLaserLine();

        // 집고 있는 상태 → 레이저 OFF + 아웃라인 OFF
        if(_heldObject != null)
        {
            laserLine.enabled = false;
            SetOutline(null);
            return;
        }

        SyncGrab hovered = null;
        Transform hoveredTransform = null;
        GameObject hoveredGO = null;
        Vector3 exactHitPoint = Vector3.zero; // ★ 정확한 충돌 지점 저장용

        if(_mainCamera != null)
        {
            // ★ 화면 중앙이 아닌 실제 마우스 포인터 위치를 향해 레이를 쏩니다
            Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);

            bool isHit = Physics.Raycast(ray, out RaycastHit hit, raycastMaxDistance, clickLayers);
            if(!isHit)
            {
                isHit = Physics.Raycast(ray, out hit, raycastMaxDistance);
            }

            if(isHit)
            {
                // ★ 충돌한 정확한 표면의 좌표를 저장
                exactHitPoint = hit.point;

                hovered = hit.collider.GetComponent<SyncGrab>()
                       ?? hit.collider.GetComponentInParent<SyncGrab>();

                if(hovered == null)
                {
                    var part = hit.collider.GetComponent<InteractablePart>()
                            ?? hit.collider.GetComponentInParent<InteractablePart>();
                    if(part != null && part.currentState != InteractablePart.PartState.Assembled)
                    {
                        hoveredTransform = part.transform;
                        hoveredGO = part.gameObject;
                    }
                }
                else
                {
                    hoveredTransform = hovered.transform;
                    hoveredGO = hovered.gameObject;
                }
            }
        }

        _laserHoverTarget = hovered;
        SetOutline(hoveredGO);

        // 호버 대상이 있고 정확한 충돌 지점이 확보되었다면 레이저 표시
        if(hoveredTransform != null && _mainCamera != null)
        {
            laserLine.enabled = true;
            // ★ 오브젝트의 중심점(hoveredTransform) 대신, 정확한 좌표(exactHitPoint)를 넘김
            DrawCurvedLaser(_mainCamera.transform, exactHitPoint);
        }
        else
        {
            laserLine.enabled = false;
        }
    }

    /// <summary>
    /// XR Starter Kit LineBender 방식 — 베지어 곡선 레이저.
    /// origin(카메라) → targetPoint(정확한 레이캐스트 충돌 지점) 사이를 아치 형태로 잇는다.
    /// </summary>
    private void DrawCurvedLaser(Transform origin, Vector3 targetPoint)
    {
        int count = Mathf.Max(laserVertexCount, 2);
        laserLine.positionCount = count;

        // 예전: 눈(카메라) 5cm 앞에서 시작해 굵기 6mm 선이 화면을 가로지르는 큰 삼각형(원뿔)처럼 보였다.
        // 지금: 화면 오른쪽 아래(오른손 위치쯤)에서 시작하고, 시작 부분은 투명하게 서서히 나타나게 한다.
        Vector3 start = origin.position + origin.forward * laserStartForward + origin.right * laserStartRight - origin.up * laserStartDown;
        Vector3 end = targetPoint; // ★ 오브젝트 중심이 아닌, 넘겨받은 정확한 좌표 사용

        // 중간 제어점 — 손 앞쪽으로 뻗은 뒤 target 방향 평면에 투영 (살짝 휘는 정도)
        Vector3 forwardPoint = start + origin.forward * Mathf.Min(0.4f, Vector3.Distance(start, end) * 0.5f);
        Vector3 itemNormal = (start - end).normalized;
        Vector3 v = forwardPoint - end;
        Vector3 projected = forwardPoint - Vector3.Project(v, itemNormal);
        Vector3 midPoint = projected;                 // 베지어 제어점

        for(int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            // 이차 베지어: B(t) = (1-t)²P0 + 2(1-t)tP1 + t²P2
            Vector3 p = (1 - t) * (1 - t) * start
                      + 2 * (1 - t) * t * midPoint
                      + t * t * end;
            laserLine.SetPosition(i, p);
        }
    }

    /// <summary>매 프레임 — DOTween 완료(IsGrabbed) 감지 → 스케일 축소</summary>
    private void CheckHoldScaleReady()
    {
        if(!_isFlying || _heldObject == null) return;
        if(_heldObject.IsGrabbed)
        {
            _isFlying = false;
            ApplyHoldScale(_heldObject.transform);
        }
    }

    /// <summary>부품 클릭 — 레이저 OFF 후 SyncGrab 기존 집기 흐름 실행</summary>
    //private void StartLaserPull(SyncGrab target)
    //{
    //    if(_heldObject != null) return;

    //    _heldOriginalScale = target.transform.localScale;
    //    _isFlying = true;
    //    _heldObject = target;

    //    HeldItemUI.Instance?.UpdateUI(target.gameObject);

    //    laserLine.enabled = false;
    //    _laserHoverTarget = null;
    //    SetOutline(null);

    //    // SyncGrab이 강제 해제되거나 내려놓을 때 _heldObject 자동 정리
    //    target.OnReleased += OnHeldObjectReleased;

    //    // SyncGrab.OnPCClick() → RequestGrab → PCFlyTo(holdPoint)
    //    target.OnPCClick();
    //    Log($"[LaserGrab] 집기: {target.name}");
    //}

    private void StartLaserPull(SyncGrab target)
    {
        if(_heldObject != null) return;

        _heldOriginalScale = target.transform.localScale;
        _isFlying = true;
        _heldObject = target;

        HeldItemUI.Instance?.UpdateUI(target.gameObject);

        laserLine.enabled = false;
        _laserHoverTarget = null;
        SetOutline(null);

        target.OnReleased += OnHeldObjectReleased;

        // ★ 단순히 OnPCClick만 호출하면 SyncGrab 내부에서 
        // 자신의 인스펙터에 설정된 오프셋을 사용하여 날아갑니다.
        target.OnPCClick();
        Log($"[LaserGrab] 집기: {target.name}");
    }

    private void OnHeldObjectReleased()
    {
        if(_heldObject == null) return;
        _heldObject.OnReleased -= OnHeldObjectReleased;
        // 스케일 복원 (DropHeldObject를 안 거쳤을 경우 대비)
        if(_heldObject != null)
            _heldObject.transform.localScale = _heldOriginalScale;
        _heldObject = null;
        _isFlying = false;
        _laserHoverTarget = null;
        _heldOriginalScale = Vector3.one;
        Log("[LaserGrab] 외부 릴리즈 감지 → _heldObject 정리");

        HeldItemUI.Instance?.ClearUI();
    }

    /// <summary>내려놓기 — 스케일 복원 후 SyncGrab 해제</summary>

    private void DropHeldObject()
    {
        if(_heldObject == null) return;

        // ── 5. 아이템 내려놓기 시 사운드 ──
        Managers.Sound.Play(dropSound);

        var dropping = _heldObject;
        var part = dropping.GetComponent<InteractablePart>();
        var data = dropping.GetComponent<FreeModePartAttachment>()?.partData;

        // ① 구독 해제 및 로컬 변수 초기화
        dropping.OnReleased -= OnHeldObjectReleased;
        _heldObject = null;
        _isFlying = false;
        _laserHoverTarget = null;
        SetOutline(null);

        HeldItemUI.Instance?.ClearUI();

        // 스케일 원복
        dropping.transform.localScale = Vector3.one;

        // ② 상태 파악 (조립 존 안에 있는지)
        bool isInsideSnapZone = part != null && part.currentState == InteractablePart.PartState.Detached && part._isInsideSnapZone;

        // ③ [핵심 버그 수정] PC 홀드 중지 및 "서버 소유권 즉시 해제"
        // 이 코드가 인벤토리 이동보다 반드시 먼저 실행되어야 좀비 상태(먹통)가 안 됩니다.
        dropping.StopPCHold();
        dropping.RequestRelease();

        // ④ 조립 위치면? -> InteractablePart.OnSyncReleased()가 알아서 조립해줌
        if(isInsideSnapZone)
        {
            Log($"[LaserGrab] {dropping.name} 조립 위치에서 놓음 -> 조립 시도");
            return;
        }
        else
        {
            // 인벤토리에 넣지 않고 그냥 바닥으로 떨어지게 둡니다.
            Log("[LaserGrab] 허공에서 놓음 -> 바닥으로 추락");
        }
        //// ⑤ 조립 위치가 아니면? -> 인벤토리로
        //if(EVInventoryUI.Instance != null && part != null)
        //{
        //    EVInventoryUI.Instance.AddPart(part, data);
        //    Log("[LaserGrab] 허공에서 놓음 -> 인벤토리로 보관");
        //    return;
        //}

        Log("[LaserGrab] 일반 내려놓기 완료");
    }

    /// <summary>홀드 스케일 적용 — 렌더러 바운드 실제 크기 기준 축소</summary>
    private void ApplyHoldScale(Transform t)
    {
        float maxDim = 0f;
        foreach(var r in t.GetComponentsInChildren<Renderer>())
        {
            float d = Mathf.Max(r.bounds.size.x, r.bounds.size.y, r.bounds.size.z);
            if(d > maxDim) maxDim = d;
        }
        if(maxDim < 0.001f) return;

        // 카메라 앞 0.8m 거리에서 화면의 40% 이하를 차지하도록 제한
        const float maxAllowed = 0.4f;
        if(maxDim <= maxAllowed) return;

        float ratio = Mathf.Clamp(maxAllowed / maxDim, 0.05f, 1f);
        t.localScale = _heldOriginalScale * ratio;
        Log($"[LaserGrab] 스케일 {maxDim:F2}m → {ratio:F2}배");
    }

    /// <summary>
    /// 인벤토리에서 꺼냈을 때 자동으로 물건을 집고 UI를 갱신합니다.
    /// </summary>
    public void ForceGrabFromInventory(SyncGrab target)
    {
        // 이미 다른 걸 들고 있다면 무시하거나 버려야 함
        if(_heldObject != null) return;

        _heldOriginalScale = target.transform.localScale;
        _isFlying = true;
        _heldObject = target;

        // ★ 여기서 UI를 띄워줍니다!
        HeldItemUI.Instance?.UpdateUI(target.gameObject);

        laserLine.enabled = false;
        _laserHoverTarget = null;
        SetOutline(null);

        // G키를 누를 때 정상적으로 해제되도록 이벤트 연결
        target.OnReleased += OnHeldObjectReleased;

        // 실제 물리 이동 명령
        target.OnPCClick();
        Log($"[LaserGrab] 인벤토리에서 꺼내 자동 집기: {target.name}");
    }

    /// <summary>
    /// 대상 GO에 Outline 컴포넌트를 켜고, 이전 대상은 끈다.
    /// - 대상이 같으면 아무것도 하지 않음 (매 프레임 갱신 방지)
    /// - targetGO == null 이면 현재 아웃라인 해제
    /// QuickOutline(Outline.cs)이 프로젝트에 있어야 작동.
    /// 없으면 조용히 스킵 (컴파일 오류 없음 — #if 사용).
    /// </summary>
    private void SetOutline(GameObject targetGO)
    {
        if(targetGO == _outlinedObject) return;

        // 이전 대상 아웃라인 OFF
        if(_outlinedObject != null)
        {
            var old = _outlinedObject.GetComponent<Outlinable>();
            if(old != null) old.enabled = false;
            _outlinedObject = null;
        }

        if(targetGO == null) return;

        // 새 아웃라인 ON
        var outlinable = targetGO.GetComponent<Outlinable>();
        if(outlinable == null)
        {
            outlinable = targetGO.AddComponent<Outlinable>();
            outlinable.AddAllChildRenderersToRenderingList();
        }

        outlinable.RenderStyle = RenderStyle.Single;
        outlinable.OutlineParameters.Color = outlineHoverColor;
        outlinable.OutlineParameters.BlurShift = outlineWidth; // EPO의 두께 조절 필드
        outlinable.OutlineParameters.Enabled = true;
        outlinable.enabled = true;

        _outlinedObject = targetGO;
    }

    /// <summary>LineRenderer 없으면 자동 생성 (URP/HDRP 호환 셰이더 사용)</summary>
    private void EnsureLaserLine()
    {
        if(laserLine != null) return;

        var go = new GameObject("PC_LaserLine");
        go.transform.SetParent(transform);
        laserLine = go.AddComponent<LineRenderer>();
        laserLine.positionCount = laserVertexCount;
        laserLine.startWidth = laserStartWidth;
        laserLine.endWidth = laserEndWidth;
        laserLine.useWorldSpace = true;
        laserLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        laserLine.receiveShadows = false;
        laserLine.numCapVertices = 4;

        // URP/HDRP/Built-in 모두 작동하는 셰이더 순서로 시도
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                     ?? Shader.Find("Particles/Standard Unlit")
                     ?? Shader.Find("Sprites/Default")
                     ?? Shader.Find("Unlit/Color");

        var mat = new Material(shader);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 3000;
        laserLine.material = mat;
        laserLine.startColor = laserColorStart;
        laserLine.endColor = laserColorEnd;
        // 시작은 투명 → 금방 진해지고 → 끝(가리킨 지점)까지 보이게. 예전에는 끝이 투명이라 어디를 가리키는지 잘 안 보였다.
        var g = new Gradient();
        Color c = laserColorStart;
        g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.2f), new GradientAlphaKey(0.85f, 1f) });
        laserLine.colorGradient = g;
        laserLine.startWidth = 0.0025f;
        laserLine.endWidth = 0.0025f;
        laserLine.enabled = false;

        Log("[LaserGrab] LineRenderer 자동 생성");
    }

    private static Gradient MakeGradient(Color start, Color end)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
            new[] { new GradientAlphaKey(start.a, 0f), new GradientAlphaKey(end.a, 1f) });
        return g;
    }
}

public class UIHoverFader : MonoBehaviour
{
    public float fadeSpeed = 10f; // 숫자가 클수록 빠름
    private CanvasGroup _cg;
    private TMPro.TextMeshPro _tmp3D;
    private bool _isFadingOut = false;

    void Awake()
    {
        // 1. 일반 UI용 CanvasGroup 세팅
        _cg = GetComponent<CanvasGroup>();
        if(_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();

        // 2. 3D TextMeshPro용 세팅 (CanvasGroup이 먹히지 않으므로 직접 제어)
        _tmp3D = GetComponentInChildren<TMPro.TextMeshPro>();

        _cg.alpha = 0f;
        if(_tmp3D != null) _tmp3D.alpha = 0f;
    }

    void Update()
    {
        if(_isFadingOut)
        {
            bool isDone = true;
            if(_cg != null)
            {
                _cg.alpha -= Time.deltaTime * fadeSpeed;
                if(_cg.alpha > 0f) isDone = false;
            }
            if(_tmp3D != null)
            {
                _tmp3D.alpha -= Time.deltaTime * fadeSpeed;
                if(_tmp3D.alpha > 0f) isDone = false;
            }

            // 알파값이 완전히 0이 되면 스스로 파괴
            if(isDone) Destroy(gameObject);
        }
        else
        {
            // 나타날 때
            if(_cg != null && _cg.alpha < 1f) _cg.alpha += Time.deltaTime * fadeSpeed;
            if(_tmp3D != null && _tmp3D.alpha < 1f) _tmp3D.alpha += Time.deltaTime * fadeSpeed;
        }
    }

    public void FadeOut()
    {
        _isFadingOut = true;
    }
}