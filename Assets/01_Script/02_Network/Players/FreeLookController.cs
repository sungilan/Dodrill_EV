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
public class FreeLookController : NetworkBehaviour
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
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if(!IsOwner || hasFocus) return;
        _heldObject = null;
    }

    private void OnApplicationPause(bool isPaused)
    {
        if(!IsOwner || !isPaused) return;
        _heldObject = null;
    }

    // ═══════════════════════════════════════════════════════
    // 카메라 초기화
    // ═══════════════════════════════════════════════════════

    private void DisableAllChildCameras()
    {
        foreach(var cam in GetComponentsInChildren<Camera>(includeInactive: true))
        {
            cam.gameObject.SetActive(false);
            cam.tag = "Untagged";
        }
    }

    private void EnableMyCamera()
    {
        Camera found = GetComponentInChildren<Camera>(includeInactive: true);
        if(found == null) { Log("자식 카메라 없음"); return; }

        found.gameObject.SetActive(true);
        found.tag = "MainCamera";
        _mainCamera = found;

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

    private void Update()
    {
        if(!IsOwner || !IsClientInitialized) return;

        // 카메라 이동/회전은 인벤토리가 켜져 있어도 작동하도록 위쪽 유지
        HandleMovement();
        HandleCameraLook();
        HandleVerticalInput();

        if(_isVR || !_cameraReady) return;

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

        Vector3 moveDir = transform.right * x + transform.forward * z;
        Vector3 horizontalVelocity = moveDir * moveSpeed;

        float targetVerticalVel = _desiredVertical * moveSpeed;
        _currentVerticalVelocity = Mathf.Lerp(
            _currentVerticalVelocity,
            targetVerticalVel,
            Time.deltaTime / Mathf.Max(0.0001f, verticalSmoothTime)
        );

        transform.position += (horizontalVelocity + Vector3.up * _currentVerticalVelocity) * Time.deltaTime;
    }

    private void HandleCameraLook()
    {
        if(!Input.GetMouseButton(1)) return;

        float y = Input.GetAxis("Mouse X") * lookSensitivity;
        _rotX += Input.GetAxis("Mouse Y") * lookSensitivity * -1f;
        _rotX = Mathf.Clamp(_rotX, minLookX, maxLookX);

        transform.eulerAngles = new Vector3(_rotX, transform.eulerAngles.y + y, 0f);
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

    public void MoveUp() => _desiredVertical = 0.5f;
    public void MoveDown() => _desiredVertical = -0.5f;
    public void StopVertical() => _desiredVertical = 0f;

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

        Log($"호버: {objName} — {msg}");
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

#if UNITY_IOS || UNITY_ANDROID
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            clicked = true;
            clickPos = Input.GetTouch(0).position;
        }
#else
        if(Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1))
        {
            clicked = true;
            clickPos = Input.mousePosition;
        }
#endif

        if(!clicked) return;

        // ★ UI 블로킹 체크 — Screen Space Overlay 캔버스가 클릭을 가로채는 경우 차단
        if(IsPointerOverUI(clickPos))
        {
            Log($"UI 위 클릭 — 무시 ({GetUIObjectName(clickPos)})");
            return;
        }

        // ── 1. 월드 UI 버튼(리프트 등) 클릭 시 사운드 ──
        if (HandleWorldSpaceUIClick(clickPos))
        {
            Managers.Sound.Play(clickSound); // 딸깍 소리
            return;
        }

        // ── 2. 태스크/존 상호작용 성공 시 사운드 ──
        if (HandleTaskClick(clickPos))
        {
            Managers.Sound.Play(clickSound);
            return;
        }

        // ── 3. 아이템 집기 성공 시 사운드 ──
        SyncGrab target = RaycastSyncGrab(clickPos);
        if (target == null || target.IsGrabbed) return;

        Log($"집기: {target.name}");
        _heldObject = target;
        target.OnPCClick();

        Managers.Sound.Play(grabSound); // 훅- 하는 집는 소리
        HeldItemUI.Instance?.UpdateUI(target.gameObject);

        // ── 들고 있는 상태 → 우클릭 = 내려놓기, 좌클릭 차단 ──
        // G키는 Update의 HandleDropKey()에서 독립 처리
        if (_heldObject != null)
        {
            if(Input.GetMouseButtonDown(1))
                DropHeldObject();
            // 좌클릭은 다른 오브젝트 클릭과 겹치므로 차단
            return;
        }

        // ── World Space UI 버튼 처리 (LiftButtonUI 등) ──
        // IsPointerOverUI는 Screen Space만 차단하므로 World Space 버튼은 여기서 처리
        if(HandleWorldSpaceUIClick(clickPos)) return;

        // ── 빈 상태 → Task 인터랙션 시스템 우선, 없으면 SyncGrab ──
        if(HandleTaskClick(clickPos)) return;
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

                // MeasurementPoint 있으면 멀티테스터 측정 트리거
                var mp = hit.collider.GetComponent<MeasurementPoint>()
                      ?? hit.collider.GetComponentInParent<MeasurementPoint>();
                if(mp != null)
                {
                    Log($"E키 측정: {mp.terminalId}");
                    InteractionEvents.FireZoneActivated(mp.terminalId, itemId);
                }
            }
        }
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

        Vector3 start = origin.position + origin.forward * 0.05f;
        Vector3 end = targetPoint; // ★ 오브젝트 중심이 아닌, 넘겨받은 정확한 좌표 사용

        // 중간 제어점 — 손 앞쪽으로 0.4m 뻗은 뒤 target 방향 평면에 투영
        Vector3 forwardPoint = start + origin.forward * 0.4f;
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
        laserLine.colorGradient = MakeGradient(laserColorStart, laserColorEnd);
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