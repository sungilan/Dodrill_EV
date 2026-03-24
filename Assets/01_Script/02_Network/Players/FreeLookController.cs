using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using FishNet.Object;

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
    private SyncGrab _heldObject = null;

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

        if (!IsOwner)
        {
            DisableAllChildCameras();
            return;
        }

        _isVR = IsVRDevice();
        EnableMyCamera();

        if (!_isVR)
            StartCoroutine(InitCameraForDrag());
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        if (!IsOwner) return;
        StopAllCoroutines();
        _cameraReady = false;
        _heldObject = null;
    }

    private void OnDisable()
    {
        if (!IsOwner) return;
        StopAllCoroutines();
        _heldObject = null;
        _cameraReady = false;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!IsOwner || hasFocus) return;
        _heldObject = null;
    }

    private void OnApplicationPause(bool isPaused)
    {
        if (!IsOwner || !isPaused) return;
        _heldObject = null;
    }

    // ═══════════════════════════════════════════════════════
    // 카메라 초기화
    // ═══════════════════════════════════════════════════════

    private void DisableAllChildCameras()
    {
        foreach (var cam in GetComponentsInChildren<Camera>(includeInactive: true))
        {
            cam.gameObject.SetActive(false);
            cam.tag = "Untagged";
        }
    }

    private void EnableMyCamera()
    {
        Camera found = GetComponentInChildren<Camera>(includeInactive: true);
        if (found == null) { Log("자식 카메라 없음"); return; }

        found.gameObject.SetActive(true);
        found.tag = "MainCamera";
        _mainCamera = found;

        foreach (var cam in GetComponentsInChildren<Camera>(includeInactive: true))
        {
            if (cam.gameObject.name.Contains("UI"))
                cam.gameObject.SetActive(true);
        }

        Log($"내 카메라 활성화: {found.name}");
    }

    private IEnumerator InitCameraForDrag()
    {
        if (_mainCamera != null)
        {
            _cameraReady = true;
            Log($"카메라 준비 완료 (즉시) — {_mainCamera.name}");
            yield break;
        }

        yield return null;

        Camera found = GetComponentInChildren<Camera>(includeInactive: true);
        if (found != null)
        {
            found.gameObject.SetActive(true);
            found.tag = "MainCamera";
            _mainCamera = found;
            _cameraReady = true;
            Log($"카메라 준비 완료 (1프레임 후) — {_mainCamera.name}");
            yield break;
        }

        Log("자식 카메라 없음 — Camera.main 폴백");
        while (Camera.main == null) yield return null;
        _mainCamera = Camera.main;
        _cameraReady = true;
        Log($"카메라 준비 완료 (Camera.main 폴백) — {_mainCamera.name}");
    }

    private bool IsVRDevice()
    {
#if ENABLE_VR || UNITY_XR_MANAGEMENT
        if (UnityEngine.XR.XRSettings.isDeviceActive) return true;
        var d = UnityEngine.XR.XRSettings.loadedDeviceName;
        if (!string.IsNullOrEmpty(d) && d != "None") return true;
#endif
        return false;
    }

    // ═══════════════════════════════════════════════════════
    // Update
    // ═══════════════════════════════════════════════════════

    private void Update()
    {
        if (!IsOwner || !IsClientInitialized) return;

        HandleMovement();
        HandleCameraLook();
        HandleVerticalInput();

        if (_isVR || !_cameraReady) return;

        if (_mainCamera == null)
        {
            _mainCamera = GetComponentInChildren<Camera>() ?? Camera.main;
            if (_mainCamera == null) return;
        }

        HandleClick();
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
        if (!Input.GetMouseButton(1)) return;

        float y = Input.GetAxis("Mouse X") * lookSensitivity;
        _rotX += Input.GetAxis("Mouse Y") * lookSensitivity * -1f;
        _rotX = Mathf.Clamp(_rotX, minLookX, maxLookX);

        transform.eulerAngles = new Vector3(_rotX, transform.eulerAngles.y + y, 0f);
    }

    private void HandleVerticalInput()
    {
        if (Input.GetKeyDown(KeyCode.Q)) MoveUp();
        if (Input.GetKeyDown(KeyCode.E)) MoveDown();
        if (Input.GetKeyUp(KeyCode.Q)) StopVertical();
        if (Input.GetKeyUp(KeyCode.E)) StopVertical();
    }

    public void MoveUp() => _desiredVertical = 0.5f;
    public void MoveDown() => _desiredVertical = -0.5f;
    public void StopVertical() => _desiredVertical = 0f;

    // ═══════════════════════════════════════════════════════
    // 클릭 처리
    // ═══════════════════════════════════════════════════════

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
        if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1))
        {
            clicked = true;
            clickPos = Input.mousePosition;
        }
#endif

        if (!clicked) return;

        // ★ UI 블로킹 체크 — Screen Space Overlay 캔버스가 클릭을 가로채는 경우 차단
        if (IsPointerOverUI(clickPos))
        {
            Log($"UI 위 클릭 — 무시 ({GetUIObjectName(clickPos)})");
            return;
        }

        // ── 들고 있는 상태 → 다시 클릭하면 내려놓기 ──
        if (_heldObject != null)
        {
            Log($"내려놓기: {_heldObject.name}");
            _heldObject.OnPCClick();
            _heldObject = null;
            return;
        }

        // ── 빈 상태 → Task 인터랙션 시스템 우선, 없으면 SyncGrab ──
        if (HandleTaskClick(clickPos)) return;

        SyncGrab target = RaycastSyncGrab(clickPos);
        if (target == null) return;

        if (target.IsGrabbed)
        {
            Log($"{target.name} 이미 점유 중 — 무시");
            return;
        }

        Log($"집기: {target.name}");
        _heldObject = target;
        target.OnPCClick();
    }

    // TaskItem / Zone 처리. 인터랙션이 발생하면 true 반환
    private bool HandleTaskClick(Vector2 screenPos)
    {
        Ray ray = _mainCamera.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit, raycastMaxDistance, clickLayers))
            return false;

        // TaskItem 클릭 → 집기 + 사용 동시 발행
        var item = hit.collider.GetComponent<TaskItem>()
                ?? hit.collider.GetComponentInParent<TaskItem>();
        if (item != null)
        {
            Log($"TaskItem 클릭: {item.prefabId}");
            InteractionEvents.FireItemGrabbed(item.prefabId);
            InteractionEvents.FireItemUsed(item.prefabId);
            return true;
        }

        // TaskInteractionZone 클릭 → 빈손 터치 대체
        var zone = hit.collider.GetComponent<TaskInteractionZone>()
                ?? hit.collider.GetComponentInParent<TaskInteractionZone>();
        if (zone != null && zone.gameObject.activeInHierarchy)
        {
            Log($"TaskInteractionZone 클릭: {zone.zoneId}");
            InteractionEvents.FireZoneActivated(zone.zoneId, string.Empty);
            return true;
        }

        return false;
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
        if (EventSystem.current == null) return false;

        var pointerData = new PointerEventData(EventSystem.current) { position = screenPos };
        _uiRaycastResults.Clear();
        EventSystem.current.RaycastAll(pointerData, _uiRaycastResults);

        foreach (var result in _uiRaycastResults)
        {
            var canvas = result.gameObject.GetComponentInParent<Canvas>();
            if (canvas == null) continue;

            // World Space 캔버스는 3D 오브젝트처럼 동작 → 무시
            if (canvas.renderMode == RenderMode.WorldSpace) continue;

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
                return result.gameObject.name;
        }

        return "없음";
    }

    // ═══════════════════════════════════════════════════════
    // 레이캐스트
    // ═══════════════════════════════════════════════════════

    private SyncGrab RaycastSyncGrab(Vector2 screenPos)
    {
        Ray ray = _mainCamera.ScreenPointToRay(screenPos);

        if (showDebugRay)
            Debug.DrawRay(ray.origin, ray.direction * raycastMaxDistance, Color.red, 0.3f);

        if (!Physics.Raycast(ray, out RaycastHit hit, raycastMaxDistance, clickLayers))
        {
            Log("레이캐스트 히트 없음");
            return null;
        }

        Log($"히트: {hit.collider.gameObject.name}");

        SyncGrab sg = hit.collider.GetComponentInParent<SyncGrab>();
        if (sg == null)
        {
            Log("SyncGrab 없음");
            return null;
        }

        return sg;
    }

    // ═══════════════════════════════════════════════════════
    // 유틸
    // ═══════════════════════════════════════════════════════

    private void Log(string msg)
    {
        if (showDebugLog) Debug.Log($"[FreeLook] {msg}");
    }
}