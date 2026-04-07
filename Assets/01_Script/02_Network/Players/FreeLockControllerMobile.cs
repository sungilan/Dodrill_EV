//using FishNet.Object;
//using PinePie.SimpleJoystick;
//using UnityEngine;

//public class FreeLockControllerMobile : NetworkBehaviour
//{
//    public float moveSpeed = 5f;
//    public float lookSensitivity = 5f;
//    public float maxLookX = 80f;
//    public float minLookX = -80f;

//    [Header("Smooth")]
//    public float moveSmoothTime = 0.12f;
//    public float verticalSmoothTime = 0.12f;
//    private Vector3 currentVelocity = Vector3.zero;
//    private Vector3 desiredMove = Vector3.zero;
//    private float desiredVertical = 0f;
//    private float currentVerticalVelocity = 0f;

//    private float rotX;
//    private float rotY;

//    [Header("Joystick/Touch")]
//    public JoystickController moveJoyStick;

//    private int lookTouchId = -1;
//    private Vector2 lastLookTouchPos;

//    [Header("State")]
//    public bool isInteracting = false;
//    private bool isMoving = false;
//    private bool isLooking = false;

//    public override void OnStartClient()
//    {
//        base.OnStartClient();
//        if (IsOwner == false)
//            Destroy(this);

//        if(moveJoyStick == null)
//        {
//            // 씬에서 JoystickController 컴포넌트를 가진 UI를 찾아 자동으로 연결합니다.
//            moveJoyStick = Object.FindFirstObjectByType<JoystickController>();

//            if(moveJoyStick != null)
//            {
//                Debug.Log($"[MobileController] 조이스틱 자동 연결 성공: {moveJoyStick.name}");
//            }
//            else
//            {
//                Debug.LogError("[MobileController] 씬에서 조이스틱을 찾을 수 없습니다! UI가 켜져 있는지 확인하세요.");
//            }
//        }
//    }

//    void Update()
//    {
//        Move();
//        CameraLookTouch();

//        // 이동 중이거나 회전 중이면 true
//        isInteracting = isMoving || isLooking;
//    }

//    void Move()
//    {
//        Vector2 moveInput = Vector2.zero;
//        if (moveJoyStick != null)
//            moveInput = moveJoyStick.InputDirection;

//        // 입력이 거의 없으면 이동 아님
//        isMoving = moveInput.sqrMagnitude > 0.01f || Mathf.Abs(desiredVertical) > 0.01f;

//        Vector3 moveDir = transform.right * moveInput.x + transform.forward * moveInput.y;
//        desiredMove = moveDir * moveSpeed;

//        currentVelocity = Vector3.Lerp(currentVelocity, desiredMove, Time.deltaTime / Mathf.Max(0.0001f, moveSmoothTime));

//        float targetVerticalVel = desiredVertical * moveSpeed;
//        currentVerticalVelocity = Mathf.Lerp(currentVerticalVelocity, targetVerticalVel, Time.deltaTime / Mathf.Max(0.0001f, verticalSmoothTime));

//        Vector3 delta = (currentVelocity + Vector3.up * currentVerticalVelocity) * Time.deltaTime;
//        transform.position += delta;
//    }

//    public void MoveUp() => desiredVertical = 0.5f;
//    public void MoveDown() => desiredVertical = -0.5f;
//    public void StopMove() => desiredVertical = 0f;

//    void CameraLookTouch()
//    {
//        isLooking = false; // 기본값 초기화

//        if (Input.touchCount == 0)
//        {
//            lookTouchId = -1;
//            return;
//        }

//        for (int i = 0; i < Input.touchCount; i++)
//        {
//            Touch t = Input.GetTouch(i);

//            if (lookTouchId == -1 && t.phase == TouchPhase.Began && t.position.x > Screen.width * 0.5f)
//            {
//                lookTouchId = t.fingerId;
//                lastLookTouchPos = t.position;
//            }

//            if (t.fingerId == lookTouchId)
//            {
//                if (t.phase == TouchPhase.Moved)
//                {
//                    Vector2 delta = t.position - lastLookTouchPos;
//                    rotY += delta.x * lookSensitivity * 0.02f;
//                    rotX += delta.y * lookSensitivity * -0.02f;
//                    rotX = Mathf.Clamp(rotX, minLookX, maxLookX);
//                    transform.rotation = Quaternion.Euler(rotX, rotY, 0f);
//                    lastLookTouchPos = t.position;

//                    // 터치로 회전 중
//                    isLooking = true;
//                }

//                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
//                {
//                    lookTouchId = -1;
//                }
//            }
//        }
//    }
//}

using FishNet.Object;
using PinePie.SimpleJoystick;
using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;

public class FreeLookControllerMobile : NetworkBehaviour
{
    // ═══════════════════════════════════════════════════════
    // 기본 이동/회전
    // ═══════════════════════════════════════════════════════

    public float moveSpeed = 5f;
    public float lookSensitivity = 5f;
    public float maxLookX = 80f;
    public float minLookX = -80f;

    [Header("Smooth")]
    public float moveSmoothTime = 0.12f;
    public float verticalSmoothTime = 0.12f;
    private Vector3 currentVelocity = Vector3.zero;
    private Vector3 desiredMove = Vector3.zero;
    private float desiredVertical = 0f;
    private float currentVerticalVelocity = 0f;

    private float rotX;
    private float rotY;

    [Header("Joystick/Touch")]
    public JoystickController moveJoyStick;

    private int lookTouchId = -1;
    private Vector2 lastLookTouchPos;

    // ═══════════════════════════════════════════════════════
    // 클릭/상호작용
    // ═══════════════════════════════════════════════════════

    [Header("클릭 설정")]
    public LayerMask clickLayers;
    public float raycastMaxDistance = 20f;
    public KeyCode dropKey = KeyCode.G;
    public KeyCode useKey = KeyCode.E;

    [Header("호버 가이드 UI")]
    public TMPro.TextMeshProUGUI hoverGuideText;
    public GameObject hoverGuidePanel;
    public GameObject worldLabelPrefab;
    public float labelHeightOffset = 0.3f;

    [Header("사운드 설정")]
    public string clickSound = "UI_Click";
    public string grabSound = "Item_Grab";
    public string dropSound = "Item_Drop";
    public string useSound = "Item_Use";

    [Header("호버 아웃라인")]
    public Color outlineHoverColor = new Color(0.3f, 0.85f, 1f);
    [Range(0f, 10f)]
    public float outlineWidth = 4f;

    [Header("디버그")]
    [SerializeField] private bool showDebugLog = true;
    [SerializeField] private bool showDebugRay = true;

    // ═══════════════════════════════════════════════════════
    // 내부 상태
    // ═══════════════════════════════════════════════════════

    [Header("State")]
    public bool isInteracting = false;
    private bool isMoving = false;
    private bool isLooking = false;

    private Camera _mainCamera = null;
    private bool _cameraReady = false;
    private bool _isVR = false;

    /// <summary>현재 들고 있는 오브젝트</summary>
    private SyncGrab _heldObject = null;
    private Vector3 _heldOriginalScale = Vector3.one;
    private bool _isFlying = false;

    // 호버 상태 추적
    private GameObject _outlinedObject = null;
    private Collider _lastHoveredCol = null;
    private GameObject _worldLabel = null;
    private string _hoverGuideMsg = string.Empty;

    // UI 레이캐스트 결과 재사용용
    private static readonly List<RaycastResult> _uiRaycastResults = new();

    // 터치 ID 추적 (여러 터치 동시 처리)
    private int _interactionTouchId = -1;

    // ═══════════════════════════════════════════════════════
    // 생명주기
    // ═══════════════════════════════════════════════════════

    public override void OnStartClient()
    {
        base.OnStartClient();
        if(IsOwner == false)
            Destroy(this);

        _isVR = IsVRDevice();

        if(moveJoyStick == null)
        {
            moveJoyStick = Object.FindFirstObjectByType<JoystickController>();
            if(moveJoyStick != null)
            {
                Debug.Log($"[MobileController] 조이스틱 자동 감지: {moveJoyStick.name}");
            }
            else
            {
                Debug.LogError("[MobileController] 조이스틱을 찾을 수 없습니다! UI에 있는지 확인하세요.");
            }
        }

        StartCoroutine(InitCamera());
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

    private IEnumerator InitCamera()
    {
        Camera found = GetComponentInChildren<Camera>(includeInactive: true);
        if(found == null)
        {
            Log("자식 카메라 없음");
            yield break;
        }

        found.gameObject.SetActive(true);
        found.tag = "MainCamera";
        _mainCamera = found;
        _cameraReady = true;

        Log($"카메라 준비 완료: {found.name}");
        yield break;
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

    void Update()
    {
        if(!IsOwner || !IsClientInitialized) return;

        Move();
        CameraLookTouch();

        if(_isVR || !_cameraReady) return;

        if(_mainCamera == null)
        {
            _mainCamera = GetComponentInChildren<Camera>() ?? Camera.main;
            if(_mainCamera == null) return;
        }

        // ★ 인벤토리 확인
        bool isInventoryOpen = EVInventoryUI.Instance != null &&
                               EVInventoryUI.Instance.panel != null &&
                               EVInventoryUI.Instance.panel.activeInHierarchy;

        if(isInventoryOpen)
        {
            SetGuideUI(string.Empty);
            if(_worldLabel != null)
            {
                var fader = _worldLabel.GetComponent<UIHoverFader>();
                if(fader != null) fader.FadeOut();
                else Destroy(_worldLabel);
                _worldLabel = null;
            }
            return;
        }

        // 정상 상호작용
        CheckHoldScaleReady();
        HandleHover();
        HandleTouchInteraction();
        HandleDropKey();
    }

    // ═══════════════════════════════════════════════════════
    // 이동 / 회전
    // ═══════════════════════════════════════════════════════

    void Move()
    {
        Vector2 moveInput = Vector2.zero;
        if(moveJoyStick != null)
            moveInput = moveJoyStick.InputDirection;

        isMoving = moveInput.sqrMagnitude > 0.01f || Mathf.Abs(desiredVertical) > 0.01f;

        Vector3 moveDir = transform.right * moveInput.x + transform.forward * moveInput.y;
        desiredMove = moveDir * moveSpeed;

        currentVelocity = Vector3.Lerp(currentVelocity, desiredMove, Time.deltaTime / Mathf.Max(0.0001f, moveSmoothTime));

        float targetVerticalVel = desiredVertical * moveSpeed;
        currentVerticalVelocity = Mathf.Lerp(currentVerticalVelocity, targetVerticalVel, Time.deltaTime / Mathf.Max(0.0001f, verticalSmoothTime));

        Vector3 delta = (currentVelocity + Vector3.up * currentVerticalVelocity) * Time.deltaTime;
        transform.position += delta;
    }

    public void MoveUp() => desiredVertical = 0.5f;
    public void MoveDown() => desiredVertical = -0.5f;
    public void StopMove() => desiredVertical = 0f;

    void CameraLookTouch()
    {
        isLooking = false;

        if(Input.touchCount == 0)
        {
            lookTouchId = -1;
            return;
        }

        for(int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);

            // 화면 오른쪽(카메라 회전용) + 인터랙션 터치와 다른 손가락
            if(lookTouchId == -1 && t.phase == TouchPhase.Began && t.position.x > Screen.width * 0.5f
                && t.fingerId != _interactionTouchId)
            {
                lookTouchId = t.fingerId;
                lastLookTouchPos = t.position;
            }

            if(t.fingerId == lookTouchId)
            {
                if(t.phase == TouchPhase.Moved)
                {
                    Vector2 delta = t.position - lastLookTouchPos;
                    rotY += delta.x * lookSensitivity * 0.02f;
                    rotX += delta.y * lookSensitivity * -0.02f;
                    rotX = Mathf.Clamp(rotX, minLookX, maxLookX);
                    transform.rotation = Quaternion.Euler(rotX, rotY, 0f);
                    lastLookTouchPos = t.position;

                    isLooking = true;
                }

                if(t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                {
                    lookTouchId = -1;
                }
            }
        }

        isInteracting = isMoving || isLooking;
    }

    // ═══════════════════════════════════════════════════════
    // 호버 가이드
    // ═══════════════════════════════════════════════════════

    private void HandleHover()
    {
        bool hit = false;
        RaycastHit hitInfo = default;

        // ★ 화면 중앙을 향해 레이캐스트 (모바일은 마우스 좌표 없음)
        if(_heldObject == null && _mainCamera != null)
        {
            Ray ray = _mainCamera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));

            hit = Physics.Raycast(ray, out hitInfo, raycastMaxDistance, clickLayers);
            if(!hit)
                hit = Physics.Raycast(ray, out hitInfo, raycastMaxDistance);
        }

        Collider hoveredCol = hit ? hitInfo.collider : null;

        // 같은 대상 계속 호버
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

        // 기존 라벨 페이드 아웃
        if(_worldLabel != null)
        {
            var fader = _worldLabel.GetComponent<UIHoverFader>();
            if(fader != null) fader.FadeOut();
            else Destroy(_worldLabel);
            _worldLabel = null;
        }

        if(hoveredCol == null)
        {
            SetGuideUI(string.Empty);
            return;
        }

        string objName = GetHoverTargetName(hoveredCol);
        string msg = GetHoverActionMsg(hoveredCol);

        Log($"호버: {objName} — {msg}");
        SetGuideUI(msg);

        SpawnWorldLabel(hitInfo.point, objName, msg);
    }

    private void SpawnWorldLabel(Vector3 worldPos, string objName, string actionMsg)
    {
        if(worldLabelPrefab == null) return;

        Vector3 pos = worldPos + Vector3.up * labelHeightOffset;
        _worldLabel = Instantiate(worldLabelPrefab, pos, Quaternion.identity);

        SetLayerRecursively(_worldLabel, 2);

        var label = _worldLabel.GetComponent<WorldHoverLabel>();
        if(label != null)
        {
            label.SetText(objName, actionMsg);
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

        _worldLabel.AddComponent<UIHoverFader>();
    }

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

    private string GetHoverActionMsg(Collider col)
    {
        if(col == null) return string.Empty;

        if(_heldObject != null) return "클릭하여 내려놓기";

        var anim = col.GetComponent<ClickableAnimator>() ?? col.GetComponentInParent<ClickableAnimator>();
        if(anim != null) return anim.IsOpen ? "클릭하여 닫기" : "클릭하여 열기";

        var item = col.GetComponent<TaskItem>() ?? col.GetComponentInParent<TaskItem>();
        if(item != null)
        {
            var sg = item.GetComponent<SyncGrab>() ?? item.GetComponentInParent<SyncGrab>();
            if(sg != null) return sg.IsGrabbed ? "다른 플레이어가 사용 중" : "클릭하여 집기";
            return "클릭";
        }

        var zone = col.GetComponent<TaskInteractionZone>() ?? col.GetComponentInParent<TaskInteractionZone>();
        if(zone != null && zone.gameObject.activeInHierarchy) return "클릭하여 상호작용";

        var part = col.GetComponent<InteractablePart>() ?? col.GetComponentInParent<InteractablePart>();
        if(part != null) return "클릭하여 조작";

        return string.Empty;
    }

    // ═══════════════════════════════════════════════════════
    // 터치 상호작용
    // ═══════════════════════════════════════════════════════

    private void HandleTouchInteraction()
    {
        if(Input.touchCount == 0)
        {
            _interactionTouchId = -1;
            return;
        }

        for(int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);

            // 카메라 회전(오른쪽)이 아닌, 왼쪽 터치만 상호작용
            if(_interactionTouchId == -1 && t.phase == TouchPhase.Began
                && t.position.x < Screen.width * 0.5f
                && t.fingerId != lookTouchId)
            {
                _interactionTouchId = t.fingerId;
                HandleInteractionTouch(t.position);
            }

            if(t.fingerId == _interactionTouchId && t.phase == TouchPhase.Ended)
            {
                _interactionTouchId = -1;
            }
        }
    }

    private void HandleInteractionTouch(Vector2 touchPos)
    {
        // UI 블로킹 체크
        if(IsPointerOverUI(touchPos))
        {
            Log($"UI 위 터치 — 무시 ({GetUIObjectName(touchPos)})");
            return;
        }

        // ClickableAnimator (문/후드)
        if(HandleTaskClick(touchPos)) return;

        // SyncGrab 집기
        SyncGrab target = RaycastSyncGrab(touchPos);
        if(target == null || target.IsGrabbed) return;

        Log($"집기: {target.name}");
        _heldObject = target;
        target.OnPCClick();

        Managers.Sound.Play(grabSound);
        HeldItemUI.Instance?.UpdateUI(target.gameObject);

        _heldObject.OnReleased += OnHeldObjectReleased;
    }

    private bool HandleTaskClick(Vector2 screenPos)
    {
        Ray ray = _mainCamera.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));

        if(!Physics.Raycast(ray, out RaycastHit hit, raycastMaxDistance, clickLayers))
            return false;

        Log($"[Raycast] 클릭 감지됨: {hit.collider.name}");

        // 사고 감지
        var part = hit.collider.GetComponent<InteractablePart>()
                   ?? hit.collider.GetComponentInParent<InteractablePart>();

        if(part != null)
        {
            Log($"InteractablePart 발견: {part.name}. 사고 체크 시작.");
            if(!part.CheckSafetyAndTriggerAccident())
            {
                Log("사고 발생! 상호작용을 중단합니다.");
                return true;
            }
        }

        // ClickableAnimator (문, 후드 등)
        var clickableAnim = hit.collider.GetComponent<ClickableAnimator>()
                          ?? hit.collider.GetComponentInParent<ClickableAnimator>();

        if(clickableAnim != null)
        {
            Log($"[애니메이션] {clickableAnim.uniqueId} 작동");
            clickableAnim.OnPCClick();
            return true;
        }

        // TaskItem / SyncGrab (아이템 집기)
        var item = hit.collider.GetComponent<TaskItem>()
                ?? hit.collider.GetComponentInParent<TaskItem>();

        if(item != null)
        {
            Log($"TaskItem 클릭: {item.prefabId}");

            var syncGrab = item.GetComponent<SyncGrab>()
                        ?? item.GetComponentInParent<SyncGrab>();

            if(syncGrab != null)
            {
                if(syncGrab.IsGrabbed) return true;

                Log($"SyncGrab 레이저 그랩 시작: {item.prefabId}");
                StartLaserPull(syncGrab);
                return true;
            }

            InteractionEvents.FireItemGrabbed(item.prefabId);
            InteractionEvents.FireItemUsed(item.prefabId);
            return true;
        }

        // TaskInteractionZone (빈손 터치)
        var zone = hit.collider.GetComponent<TaskInteractionZone>()
                ?? hit.collider.GetComponentInParent<TaskInteractionZone>();

        if(zone != null && zone.gameObject.activeInHierarchy)
        {
            InteractionEvents.FireZoneActivated(zone.zoneId, string.Empty);
            return true;
        }

        return false;
    }

    private SyncGrab RaycastSyncGrab(Vector2 screenPos)
    {
        Ray ray = _mainCamera.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));

        if(showDebugRay)
            Debug.DrawRay(ray.origin, ray.direction * raycastMaxDistance, Color.red, 0.3f);

        if(!Physics.Raycast(ray, out RaycastHit hit, raycastMaxDistance, clickLayers))
        {
            Log("레이캐스트 히트 없음");
            return null;
        }

        Log($"히트: {hit.collider.gameObject.name}");

        SyncGrab sg = hit.collider.GetComponentInParent<SyncGrab>();
        if(sg != null) return sg;

        var anim = hit.collider.GetComponent<ClickableAnimator>()
                ?? hit.collider.GetComponentInParent<ClickableAnimator>();
        if(anim != null)
        {
            Log($"ClickableAnimator 클릭 (clickLayers 경로): {anim.gameObject.name}");
            anim.OnPCClick();
            return null;
        }

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
    // UI 블로킹
    // ═══════════════════════════════════════════════════════

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

            if(canvas.renderMode == RenderMode.WorldSpace) continue;

            return true;
        }

        return false;
    }

    private string GetUIObjectName(Vector2 screenPos)
    {
        if(EventSystem.current == null) return "EventSystem 없음";

        var pointerData = new PointerEventData(EventSystem.current) { position = screenPos };
        _uiRaycastResults.Clear();
        EventSystem.current.RaycastAll(pointerData, _uiRaycastResults);

        foreach(var result in _uiRaycastResults)
        {
            var canvas = result.gameObject.GetComponentInParent<Canvas>();
            if(canvas != null && canvas.renderMode != RenderMode.WorldSpace)
            {
                return GetGameObjectPath(result.gameObject);
            }
        }
        return "없음";
    }

    private string GetGameObjectPath(GameObject obj)
    {
        string path = obj.name;
        while(obj.transform.parent != null)
        {
            obj = obj.transform.parent.gameObject;
            path = obj.name + "/" + path;
        }
        return path;
    }

    // ═══════════════════════════════════════════════════════
    // 레이저 그랩
    // ═══════════════════════════════════════════════════════

    private void StartLaserPull(SyncGrab target)
    {
        if(_heldObject != null) return;

        _heldOriginalScale = target.transform.localScale;
        _isFlying = true;
        _heldObject = target;

        HeldItemUI.Instance?.UpdateUI(target.gameObject);
        SetOutline(null);

        target.OnReleased += OnHeldObjectReleased;
        target.OnPCClick();
        Log($"[LaserGrab] 집기: {target.name}");
    }

    private void OnHeldObjectReleased()
    {
        if(_heldObject == null) return;
        _heldObject.OnReleased -= OnHeldObjectReleased;
        if(_heldObject != null)
            _heldObject.transform.localScale = _heldOriginalScale;
        _heldObject = null;
        _isFlying = false;
        _heldOriginalScale = Vector3.one;
        Log("[LaserGrab] 외부 릴리즈 감지 → _heldObject 정리");

        HeldItemUI.Instance?.ClearUI();
    }

    private void CheckHoldScaleReady()
    {
        if(!_isFlying || _heldObject == null) return;
        if(_heldObject.IsGrabbed)
        {
            _isFlying = false;
            ApplyHoldScale(_heldObject.transform);
        }
    }

    private void ApplyHoldScale(Transform t)
    {
        float maxDim = 0f;
        foreach(var r in t.GetComponentsInChildren<Renderer>())
        {
            float d = Mathf.Max(r.bounds.size.x, r.bounds.size.y, r.bounds.size.z);
            if(d > maxDim) maxDim = d;
        }
        if(maxDim < 0.001f) return;

        const float maxAllowed = 0.4f;
        if(maxDim <= maxAllowed) return;

        float ratio = Mathf.Clamp(maxAllowed / maxDim, 0.05f, 1f);
        t.localScale = _heldOriginalScale * ratio;
        Log($"[LaserGrab] 스케일 {maxDim:F2}m → {ratio:F2}배");
    }

    // ═══════════════════════════════════════════════════════
    // 내려놓기
    // ═══════════════════════════════════════════════════════

    private void HandleDropKey()
    {
        if(_heldObject == null) return;
        if(Input.GetKeyDown(dropKey))
            DropHeldObject();
    }

    private void DropHeldObject()
    {
        if(_heldObject == null) return;

        Managers.Sound.Play(dropSound);

        var dropping = _heldObject;
        var part = dropping.GetComponent<InteractablePart>();
        var data = dropping.GetComponent<FreeModePartAttachment>()?.partData;

        dropping.OnReleased -= OnHeldObjectReleased;
        _heldObject = null;
        _isFlying = false;
        SetOutline(null);

        HeldItemUI.Instance?.ClearUI();

        dropping.transform.localScale = Vector3.one;

        bool isInsideSnapZone = part != null && part.currentState == InteractablePart.PartState.Detached && part._isInsideSnapZone;

        dropping.StopPCHold();
        dropping.RequestRelease();

        if(isInsideSnapZone)
        {
            Log($"[LaserGrab] {dropping.name} 조립 위치에서 놓음 -> 조립 시도");
            return;
        }
        else
        {
            Log("[LaserGrab] 허공에서 놓음 -> 바닥으로 추락");
        }

        Log("[LaserGrab] 일반 내려놓기 완료");
    }

    // ═══════════════════════════════════════════════════════
    // 아웃라인
    // ═══════════════════════════════════════════════════════

    private void SetOutline(GameObject targetGO)
    {
        if(targetGO == _outlinedObject) return;

        if(_outlinedObject != null)
        {
            var old = _outlinedObject.GetComponent<MikeNspired.XRIStarterKit.ChrisNolet.Outline>();
            if(old != null) old.enabled = false;
            _outlinedObject = null;
        }

        if(targetGO == null) return;

        var outline = targetGO.GetComponent<MikeNspired.XRIStarterKit.ChrisNolet.Outline>();
        if(outline == null)
            outline = targetGO.AddComponent<MikeNspired.XRIStarterKit.ChrisNolet.Outline>();

        outline.OutlineColor = outlineHoverColor;
        outline.OutlineWidth = outlineWidth;
        outline.OutlineMode = MikeNspired.XRIStarterKit.ChrisNolet.Outline.Mode.OutlineAll;
        outline.enabled = true;

        _outlinedObject = targetGO;
    }

    // ═══════════════════════════════════════════════════════
    // 유틸
    // ═══════════════════════════════════════════════════════

    private void Log(string msg)
    {
        if(showDebugLog) Debug.Log($"[FreeLookMobile] {msg}");
    }

    public void ForceGrabFromInventory(SyncGrab target)
    {
        if(_heldObject != null) return;

        _heldOriginalScale = target.transform.localScale;
        _isFlying = true;
        _heldObject = target;

        HeldItemUI.Instance?.UpdateUI(target.gameObject);
        SetOutline(null);

        target.OnReleased += OnHeldObjectReleased;
        target.OnPCClick();
        Log($"[LaserGrab] 인벤토리에서 꺼내 자동 집기: {target.name}");
    }
}
