using DG.Tweening;
using EPOOutline;
using FishNet.Object;
using PinePie.SimpleJoystick;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 파일명(FreeLockControllerMobile.cs)과 클래스명이 달라 프리팹의 스크립트가 Missing 으로 풀리던 것을 파일명에 맞춤
public class FreeLockControllerMobile : NetworkBehaviour, IPcLookController
{
    // ═══════════════════════════════════════════════════════
    // 이동 / 회전 설정
    // ═══════════════════════════════════════════════════════
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float lookSensitivity = 5f;
    public float maxLookX = 80f;
    public float minLookX = -80f;

    [Header("카메라")]
    [Tooltip("플레이어 카메라 near clip(m). 0 이하면 프리팹 값 사용.")]
    public float nearClip = 0.03f;

    [Header("Smooth Settings")]
    public float moveSmoothTime = 0.12f;
    public float verticalSmoothTime = 0.12f;
    private Vector3 currentVelocity = Vector3.zero;
    private float currentVerticalVelocity = 0f;
    private float desiredVertical = 0f;

    private float rotX;
    private float rotY;

    // ── 아바타(PcMobileAvatarController) 연동 ──
    [Header("아바타 (PcMobileAvatarController 연동)")]
    [Tooltip("카메라 피치를 적용할 피벗. 비워 두면 자식 'CameraPivot'을 찾는다. 없으면 예전처럼 루트 전체를 기울인다.")]
    [SerializeField] private Transform cameraPivot;

    private Vector3 _planarVelocity = Vector3.zero;
    private float _crouchInput = 0f;
    private PcMobileAvatarController _avatarCtrl;
    public Vector3 CurrentPlanarVelocity => _planarVelocity;
    public float CurrentPitch => rotX;
    public float CrouchInput => _crouchInput;
    public float MoveSpeed => moveSpeed;

    private bool UseAvatarRig => cameraPivot != null && _avatarCtrl != null;

    // 높이(크라우치) 버튼용 — MoveUp = 일어서기, MoveDown = 낮추기
    public void MoveUp() { _crouchInput = -1f; desiredVertical = 0f; }
    public void MoveDown() { _crouchInput = 1f; desiredVertical = 0f; }
    public void StopMove() { _crouchInput = 0f; desiredVertical = 0f; }

    [Header("Joystick/Touch")]
    public JoystickController moveJoyStick;
    private int lookTouchId = -1;
    private Vector2 lastLookTouchPos;

    // ═══════════════════════════════════════════════════════
    // 상호작용 설정
    // ═══════════════════════════════════════════════════════
    [Header("Interaction Settings")]
    public LayerMask clickLayers;
    public float raycastMaxDistance = 20f;

    [Header("Drop 설정 (모바일)")]
    [Tooltip("더블 탭 감지 시간 제한 (초)")]
    public float doubleTapTimeWindow = 0.3f;
    [Tooltip("더블 탭 판정 거리 (픽셀)")]
    public float doubleTapMaxDistance = 100f;
    [Tooltip("Drop 버튼을 자동으로 표시할지 여부")]
    public bool showDropButton = true;
    [Tooltip("Drop 버튼 프리팹 (Button 컴포넌트 필수)")]
    public GameObject dropButtonPrefab;

    [Header("UI/Visual References")]
    public TMPro.TextMeshProUGUI hoverGuideText;
    public GameObject hoverGuidePanel;
    public GameObject worldLabelPrefab;
    public GameObject touchEffectPrefab;
    public float labelHeightOffset = 0.3f;
    public Color outlineHoverColor = new Color(0.3f, 0.85f, 1f);
    public float outlineWidth = 4f;

    [Header("Sound")]
    public string grabSound = "Item_Grab";
    public string dropSound = "Item_Drop";
    public string touchSound = "UI_Click";

    // ═══════════════════════════════════════════════════════
    // 레이저 라인
    // ═══════════════════════════════════════════════════════
    [Header("Laser Grab (Mobile)")]
    public LineRenderer laserLine;
    public int laserVertexCount = 20;
    public Color laserColorStart = new Color(0.3f, 0.8f, 1f, 1f);
    public Color laserColorEnd = new Color(0.3f, 0.8f, 1f, 0f);
    public float laserStartWidth = 0.006f;
    public float laserEndWidth = 0.002f;

    // ═══════════════════════════════════════════════════════
    // 내부 상태
    // ═══════════════════════════════════════════════════════
    private Camera _mainCamera = null;
    private bool _cameraReady = false;

    private SyncGrab _heldObject = null;
    private Vector3 _heldOriginalScale = Vector3.one;
    private bool _isFlying = false;

    private GameObject _outlinedObject = null;
    private Collider _lastHoveredCol = null;
    private GameObject _worldLabel = null;

    private int _interactionTouchId = -1;
    private static readonly List<RaycastResult> _uiRaycastResults = new();

    // ★ 더블 탭 감지용
    private float _lastTapTime = 0f;
    private Vector2 _lastTapPos = Vector2.zero;
    private int _tapCount = 0;

    // ★ Drop 버튼
    private GameObject _dropButtonInstance = null;
    private Button _dropButton = null;

    [SerializeField] private bool showDebugLog = true;

    // ═══════════════════════════════════════════════════════
    // 생명주기
    // ═══════════════════════════════════════════════════════

    public override void OnStartClient()
    {
        base.OnStartClient();
        if(!IsOwner) { Destroy(this); return; }

        if(cameraPivot == null) cameraPivot = transform.Find("CameraPivot");
        _avatarCtrl = GetComponent<PcMobileAvatarController>();

        if(moveJoyStick == null)
            moveJoyStick = Object.FindFirstObjectByType<JoystickController>();

        StartCoroutine(InitCamera());
        StartCoroutine(EnsureUiTouchInput());
        MobileHeightButtons.Show(this);   // 서기/앉기 버튼 (PC 의 Q/E)
        EnlargeJoystick(1.5f);
    }

    /// <summary>조이스틱이 작아 1.5배로 키운다. 왼쪽·아래 모서리를 고정해 화면 밖으로 나가지 않게 한다(터치 범위·반지름은 로컬 단위라 함께 커진다).</summary>
    private void EnlargeJoystick(float factor)
    {
        if(moveJoyStick == null || moveJoyStick.joystickBase == null) return;
        var b = moveJoyStick.joystickBase;
        if(b.localScale.x > 1.01f) return;   // 이미 키움(중복 방지)
        var before = new Vector3[4]; b.GetWorldCorners(before);
        b.localScale *= factor;
        var after = new Vector3[4]; b.GetWorldCorners(after);
        b.position += before[0] - after[0];
        Log($"조이스틱 {factor}배 확대");
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        if(IsOwner) { MobileHeightButtons.Hide(); MobileWorkButton.SetVisible(false); }
    }

    // ───────────────────────────────────────────────────────
    // UI 터치 보강 (모바일에서 버튼이 거의 안 눌리던 문제)
    //  ① 이 컨트롤러는 시선·이동·상호작용을 레거시 Input.touches 로 읽는데, Game 씬의 EventSystem 은 Input System UI 모듈이라
    //     같은 손가락을 두 입력 시스템이 따로 해석한다 -> 모바일에서는 레거시 터치 기반 StandaloneInputModule 로 맞춘다.
    //  ② 오버레이 캔버스 중 GraphicRaycaster 가 없는 곳(TaskProgressUI_Mobile 등)은 버튼이 아예 터치를 못 받으므로 추가한다.
    // ───────────────────────────────────────────────────────
    private IEnumerator EnsureUiTouchInput()
    {
        for(int i = 0; i < 40; i++)
        {
            var es = EventSystem.current;
            if(es != null) { UseLegacyTouchModule(es); break; }
            yield return new WaitForSeconds(0.25f);
        }

        // UI 는 플랫폼 설정 뒤에 켜지는 경우가 있어 한동안 반복해서 점검
        for(int i = 0; i < 24; i++)
        {
            EnsureRaycasters();
            yield return new WaitForSeconds(2.5f);
        }
    }

    private void UseLegacyTouchModule(EventSystem es)
    {
        if(!Application.isMobilePlatform) return;   // 에디터·PC 에서는 기존 모듈 유지

        StandaloneInputModule legacy = es.GetComponent<StandaloneInputModule>();
        bool changed = false;
        foreach(var m in es.GetComponents<BaseInputModule>())
        {
            if(m == null || m is StandaloneInputModule) continue;
            if(m.GetType().Name == "InputSystemUIInputModule" && m.enabled) { m.enabled = false; changed = true; }
        }
        if(legacy == null) { legacy = es.gameObject.AddComponent<StandaloneInputModule>(); changed = true; }
        if(!legacy.enabled) { legacy.enabled = true; changed = true; }
        if(changed) Log("UI 입력 모듈을 레거시 터치(StandaloneInputModule)로 전환");
    }

    private void EnsureRaycasters()
    {
        foreach(var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if(!c.isRootCanvas || c.renderMode == RenderMode.WorldSpace) continue;
            if(c.GetComponent<GraphicRaycaster>() != null) continue;
            if(c.GetComponentInChildren<Selectable>(true) == null && c.GetComponentInChildren<ScrollRect>(true) == null) continue;
            c.gameObject.AddComponent<GraphicRaycaster>();
            Log($"GraphicRaycaster 없는 캔버스에 추가: {c.name}");
        }
    }

    private IEnumerator InitCamera()
    {
        _mainCamera = GetComponentInChildren<Camera>(true);
        if(_mainCamera != null)
        {
            _mainCamera.gameObject.SetActive(true);
            _mainCamera.enabled = true;
            _mainCamera.tag = "MainCamera";
            // 프리팹 루트 카메라의 near clip(0.3m) 때문에 가까운 물체가 잘려 보이는 문제 방지
            if(nearClip > 0f && _mainCamera.nearClipPlane > nearClip)
                _mainCamera.nearClipPlane = nearClip;
            // PcMobileAvatarController 가 모든 카메라를 꺼 두므로 'UI' 카메라(자식)도 다시 켠다
            foreach(var cam in GetComponentsInChildren<Camera>(true))
                if(cam.gameObject.name.Contains("UI")) cam.gameObject.SetActive(true);
            _cameraReady = true;
            Log("카메라 준비 완료");
        }
        yield break;
    }

    private void Update()
    {
        if(!IsOwner || !IsClientInitialized || !_cameraReady) return;

        // 인벤토리 열림 체크
        if(EVInventoryUI.Instance != null && EVInventoryUI.Instance.panel.activeInHierarchy)
        {
            ClearHoverVisuals();
            HideDropButton();  // ★ 인벤토리 열리면 Drop 버튼 숨기기
            ReleaseLiftButton();
            return;
        }

        Move();
        HandleLiftButtonTouch();  // 시점·상호작용보다 먼저: 버튼을 누른 손가락은 둘 다에서 제외
        CameraLookTouch();
        HandleHover();
        HandleTouchInteraction();
        HandleDoubleTapDrop();  // ★ 더블 탭 감지
        CheckHoldScaleReady();
        UpdateLaserLine();
        UpdateDropButton();  // ★ Drop 버튼 표시/숨김
    }

    // ═══════════════════════════════════════════════════════
    // 이동 및 회전
    // ═══════════════════════════════════════════════════════

    private void Move()
    {
        Vector2 moveInput = (moveJoyStick != null) ? moveJoyStick.InputDirection : Vector2.zero;
        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 moveDir = transform.right * moveInput.x + flatForward * moveInput.y;
        Vector3 desiredMove = moveDir * moveSpeed;
        currentVelocity = Vector3.Lerp(currentVelocity, desiredMove, Time.deltaTime / moveSmoothTime);

        if(UseAvatarRig)
        {
            transform.position += currentVelocity * Time.deltaTime;
            _planarVelocity = currentVelocity;
            return;
        }

        float targetVerticalVel = desiredVertical * moveSpeed;
        currentVerticalVelocity = Mathf.Lerp(currentVerticalVelocity, targetVerticalVel, Time.deltaTime / verticalSmoothTime);

        transform.position += (currentVelocity + Vector3.up * currentVerticalVelocity) * Time.deltaTime;
        _planarVelocity = currentVelocity;
    }

    // 오른쪽 절반은 시선 회전 영역이지만, 짧게 톡 치면(이동 거의 없음) 상호작용으로 처리한다.
    // (예전에는 상호작용이 화면 '왼쪽 절반'에서만 됐고 조이스틱과 겹쳐 터치가 잘 안 먹었다)
    private const float TapMaxSeconds = 0.30f;
    private const float TapMaxMovePixels = 28f;
    private float _lookTouchStartTime;
    private Vector2 _lookTouchStartPos;
    private bool _lookTouchMoved;

    private void CameraLookTouch()
    {
        if(Input.touchCount == 0) { lookTouchId = -1; return; }

        foreach(Touch t in Input.touches)
        {
            if(t.fingerId == _liftTouchId) continue;

            // 시선 손가락이 떼어졌는데 다른 손가락(조이스틱)이 남아 있으면 ID 가 안 풀려 다음 시선 조작이 막히던 문제
            if(t.fingerId == lookTouchId && (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled))
            {
                bool isTap = t.phase == TouchPhase.Ended && !_lookTouchMoved && Time.time - _lookTouchStartTime <= TapMaxSeconds;
                lookTouchId = -1;
                if(isTap && _cameraReady)
                {
                    SpawnTouchVisual(t.position);
                    Managers.Sound.Play(touchSound);
                    if(!DetectDoubleTap(t.position)) ProcessInteraction(t.position);
                }
                continue;
            }

            if(lookTouchId == -1 && t.phase == TouchPhase.Began && t.position.x > Screen.width * 0.5f && !IsPointerOverUI(t.position))
            {
                lookTouchId = t.fingerId;
                lastLookTouchPos = t.position;
                _lookTouchStartPos = t.position;
                _lookTouchStartTime = Time.time;
                _lookTouchMoved = false;
            }

            if(t.fingerId == lookTouchId && t.phase == TouchPhase.Moved)
            {
                if((t.position - _lookTouchStartPos).sqrMagnitude > TapMaxMovePixels * TapMaxMovePixels) _lookTouchMoved = true;
                Vector2 delta = t.position - lastLookTouchPos;
                rotY += delta.x * lookSensitivity * 0.02f;
                rotX = Mathf.Clamp(rotX + (delta.y * lookSensitivity * -0.02f), minLookX, maxLookX);
                if(UseAvatarRig)
                {
                    transform.rotation = Quaternion.Euler(0f, rotY, 0f);
                    cameraPivot.localRotation = Quaternion.Euler(rotX, 0f, 0f);
                }
                else
                {
                    transform.rotation = Quaternion.Euler(rotX, rotY, 0f);
                }
                lastLookTouchPos = t.position;
            }
        }
    }

    // ═══════════════════════════════════════════════════════
    // 터치 상호작용
    // ═══════════════════════════════════════════════════════

    // 리프트 버튼은 콜라이더 없는 월드 캔버스 위에 있어 ProcessInteraction 의 레이캐스트로는 맞지 않는다.
    // 누르는 동안 작동하는 버튼이라 손가락을 추적해 뗄 때 반드시 놓아 준다.
    private LiftButtonUI _pressedLiftButton;
    private int _liftTouchId = -1;

    private void HandleLiftButtonTouch()
    {
        if(_pressedLiftButton != null)
        {
            for(int i = 0; i < Input.touchCount; i++)
            {
                Touch held = Input.GetTouch(i);
                if(held.fingerId == _liftTouchId && held.phase != TouchPhase.Ended && held.phase != TouchPhase.Canceled)
                    return;
            }
            ReleaseLiftButton();
            return;
        }

        for(int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);
            if(t.phase != TouchPhase.Began || t.fingerId == lookTouchId || t.fingerId == _interactionTouchId) continue;
            if(IsPointerOverUI(t.position)) continue;

            var button = LiftButtonUI.FindAtScreen(_mainCamera, t.position, raycastMaxDistance, transform, out _);
            if(button == null) continue;

            _pressedLiftButton = button;
            _liftTouchId = t.fingerId;
            button.OnPointerDown(null);
            Log($"리프트 버튼 누름: {button.name}");
            return;
        }
    }

    private void ReleaseLiftButton()
    {
        _liftTouchId = -1;
        if(_pressedLiftButton == null) return;

        _pressedLiftButton.OnPointerUp(null);
        Log($"리프트 버튼 해제: {_pressedLiftButton.name}");
        _pressedLiftButton = null;
    }

    private void HandleTouchInteraction()
    {
        if(Input.touchCount == 0) { _interactionTouchId = -1; return; }

        for(int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);
            if(t.fingerId == _liftTouchId) continue;

            if(_interactionTouchId == -1 && t.phase == TouchPhase.Began &&
                t.position.x < Screen.width * 0.5f && t.fingerId != lookTouchId)
            {
                _interactionTouchId = t.fingerId;

                // ★ 더블 탭 카운트 증가 — 더블 탭(내려놓기)이면 같은 탭으로 다시 집기/상호작용이 걸리지 않게 건너뛴다
                bool isDouble = DetectDoubleTap(t.position);

                SpawnTouchVisual(t.position);
                Managers.Sound.Play(touchSound);
                if(!isDouble) ProcessInteraction(t.position);
            }

            if(t.fingerId == _interactionTouchId && (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled))
            {
                _interactionTouchId = -1;
            }
        }
    }

    /// <summary>
    /// 더블 탭 감지 로직.
    /// 첫 번째 탭: 카운트=1, 타이머 시작
    /// 두 번째 탭 (0.3초 이내, 거리 100픽셀 이내): 더블 탭 실행
    /// 시간 초과 또는 거리 초과: 리셋
    /// </summary>
    private bool DetectDoubleTap(Vector2 tapPos)
    {
        float timeSinceLastTap = Time.time - _lastTapTime;
        float distanceSinceLastTap = Vector2.Distance(tapPos, _lastTapPos);

        // ★ 첫 번째 탭
        if(_tapCount == 0)
        {
            _tapCount = 1;
            _lastTapTime = Time.time;
            _lastTapPos = tapPos;
            Log($"[DoubleTap] 첫 번째 탭 @ {tapPos}");
            return false;
        }

        // ★ 두 번째 탭 (조건 확인)
        if(_tapCount == 1)
        {
            // 시간 초과: 리셋
            if(timeSinceLastTap > doubleTapTimeWindow)
            {
                _tapCount = 1;
                _lastTapTime = Time.time;
                _lastTapPos = tapPos;
                Log($"[DoubleTap] 시간 초과, 리셋");
                return false;
            }

            // 거리 초과: 리셋
            if(distanceSinceLastTap > doubleTapMaxDistance)
            {
                _tapCount = 1;
                _lastTapTime = Time.time;
                _lastTapPos = tapPos;
                Log($"[DoubleTap] 거리 초과, 리셋");
                return false;
            }

            // ★ 성공: 더블 탭!
            _tapCount = 0;
            Log($"[DoubleTap] 더블 탭 감지! 내려놓기 실행");
            DropHeldObject();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 매 프레임 더블 탭 타임아웃 체크.
    /// </summary>
    private void HandleDoubleTapDrop()
    {
        if(_tapCount == 0) return;

        float timeSinceLastTap = Time.time - _lastTapTime;
        if(timeSinceLastTap > doubleTapTimeWindow)
        {
            _tapCount = 0;
            Log("[DoubleTap] 타임아웃");
        }
    }

    private void SpawnTouchVisual(Vector2 screenPos)
    {
        if(touchEffectPrefab == null) return;

        Canvas canvas = GetComponentInParent<Canvas>() ?? Object.FindFirstObjectByType<Canvas>();
        if(canvas == null) return;

        GameObject effect = Instantiate(touchEffectPrefab, canvas.transform);
        RectTransform rectTrans = effect.GetComponent<RectTransform>();
        if(rectTrans != null)
        {
            rectTrans.position = screenPos;
        }
        else
        {
            effect.transform.position = screenPos;
        }

        Image img = effect.GetComponent<Image>();
        if(img != null)
        {
            effect.transform.localScale = Vector3.one * 0.5f;
            Sequence seq = DOTween.Sequence();
            seq.Join(effect.transform.DOScale(1.5f, 0.4f));
            seq.Join(img.DOFade(0, 0.4f));
            seq.OnComplete(() => Destroy(effect));
        }
        else
        {
            Destroy(effect, 0.5f);
        }

        Log($"[Touch] 잔상 생성 @ {screenPos}");
    }

    private void ProcessInteraction(Vector2 screenPos)
    {
        if(IsPointerOverUI(screenPos))
        {
            Log($"[Touch] UI 위 터치 — 무시");
            return;
        }

        Ray ray = _mainCamera.ScreenPointToRay(screenPos);
        if(!Physics.Raycast(ray, out RaycastHit hit, raycastMaxDistance, clickLayers))
        {
            if(!Physics.Raycast(ray, out hit, raycastMaxDistance))
            {
                Log("[Touch] 히트 없음");
                return;
            }
        }

        Log($"[Touch] 레이캐스트 히트: {hit.collider.name}");

        if(HandleSpecialInteractions(hit.collider)) return;

        SyncGrab target = hit.collider.GetComponent<SyncGrab>()
                       ?? hit.collider.GetComponentInParent<SyncGrab>();

        if(target != null && !target.IsGrabbed)
        {
            Log($"[Touch] 레이저 그랩 시작: {target.name}");
            StartLaserPull(target, hit.point);
            return;
        }

        if(target != null && target.IsGrabbed)
        {
            Log($"[Touch] 다른 플레이어가 사용 중: {target.name}");
            return;
        }
    }

    private bool HandleSpecialInteractions(Collider col)
    {
        var part = col.GetComponent<InteractablePart>() ?? col.GetComponentInParent<InteractablePart>();
        if(part != null && !part.CheckSafetyAndTriggerAccident())
        {
            Log($"[Interaction] 사고 발생! {part.name}");
            return true;
        }

        var anim = col.GetComponent<ClickableAnimator>() ?? col.GetComponentInParent<ClickableAnimator>();
        if(anim != null)
        {
            Log($"[Interaction] 애니메이션: {anim.uniqueId}");
            anim.OnPCClick();
            return true;
        }

        var zone = col.GetComponent<TaskInteractionZone>() ?? col.GetComponentInParent<TaskInteractionZone>();
        if(zone != null && zone.gameObject.activeInHierarchy)
        {
            Log($"[Interaction] 존 활성화: {zone.zoneId}");
            InteractionEvents.FireZoneActivated(zone.zoneId, string.Empty);
            return true;
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════
    // Drop 버튼 UI 관리
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// 집고 있을 때만 Drop 버튼을 표시합니다.
    /// </summary>
    private void UpdateDropButton()
    {
        if(_heldObject == null)
        {
            HideDropButton();
            return;
        }

        if(showDropButton)
        {
            ShowDropButton();
        }
        else
        {
            HideDropButton();
        }
    }

    /// <summary>
    /// Drop 버튼을 화면에 표시합니다.
    /// </summary>
    private void ShowDropButton()
    {
        if(_dropButtonInstance != null) return;

        if(dropButtonPrefab == null)
        {
            Log("[Drop Button] 프리팹 없음 — 자동 생성");
            CreateDefaultDropButton();
            return;
        }

        Canvas canvas = GetComponentInParent<Canvas>() ?? Object.FindFirstObjectByType<Canvas>();
        if(canvas == null)
        {
            Log("[Drop Button] Canvas 없음");
            return;
        }

        _dropButtonInstance = Instantiate(dropButtonPrefab, canvas.transform);
        _dropButton = _dropButtonInstance.GetComponent<Button>();
        if(_dropButton != null)
        {
            _dropButton.onClick.AddListener(OnDropButtonClicked);
            Log("[Drop Button] 표시");
        }
    }

    /// <summary>
    /// 기본 Drop 버튼을 자동 생성합니다.
    /// </summary>
    private void CreateDefaultDropButton()
    {
        Canvas canvas = GetComponentInParent<Canvas>() ?? Object.FindFirstObjectByType<Canvas>();
        if(canvas == null)
        {
            Log("[Drop Button] Canvas 없음 — 생성 불가");
            return;
        }

        // ★ 버튼 게임오브젝트 생성
        GameObject btnGO = new GameObject("DropButton");
        RectTransform btnRect = btnGO.AddComponent<RectTransform>();
        btnRect.SetParent(canvas.transform, false);

        // ★ 위치: 우측 하단
        btnRect.anchorMin = new Vector2(1f, 0f);
        btnRect.anchorMax = new Vector2(1f, 0f);
        btnRect.offsetMin = new Vector2(-120f, 20f);
        btnRect.offsetMax = new Vector2(-20f, 100f);

        // ★ 배경 이미지
        Image btnImage = btnGO.AddComponent<Image>();
        btnImage.color = new Color(1f, 0.3f, 0.3f, 0.8f);  // 반투명 빨강

        // ★ 버튼 컴포넌트
        Button btn = btnGO.AddComponent<Button>();
        btn.onClick.AddListener(OnDropButtonClicked);

        // ★ 텍스트 레이블
        GameObject txtGO = new GameObject("Text");
        txtGO.transform.SetParent(btnGO.transform, false);
        RectTransform txtRect = txtGO.AddComponent<RectTransform>();
        txtRect.anchorMin = Vector2.zero;
        txtRect.anchorMax = Vector2.one;
        txtRect.offsetMin = Vector2.zero;
        txtRect.offsetMax = Vector2.zero;

        TMPro.TextMeshProUGUI txt = txtGO.AddComponent<TMPro.TextMeshProUGUI>();
        txt.text = "DROP";
        txt.fontSize = 36;
        txt.alignment = TMPro.TextAlignmentOptions.Center;
        txt.color = Color.white;

        _dropButtonInstance = btnGO;
        _dropButton = btn;

        Log("[Drop Button] 자동 생성 및 표시");
    }

    /// <summary>
    /// Drop 버튼을 숨깁니다.
    /// </summary>
    private void HideDropButton()
    {
        if(_dropButtonInstance == null) return;

        Destroy(_dropButtonInstance);
        _dropButtonInstance = null;
        _dropButton = null;

        Log("[Drop Button] 숨김");
    }

    /// <summary>
    /// Drop 버튼 클릭 콜백.
    /// </summary>
    private void OnDropButtonClicked()
    {
        Log("[Drop Button] 클릭됨");
        DropHeldObject();
    }

    // ═══════════════════════════════════════════════════════
    // 호버 가이드
    // ═══════════════════════════════════════════════════════

    private void HandleHover()
    {
        // 물체를 잡은 뒤에도 잡기 직전의 '○○ 터치하여 집기' 라벨이 남아 있던 문제 → 잡는 순간 지운다
        if(_heldObject != null)
        {
            if(_worldLabel != null || _lastHoveredCol != null) { ClearHoverVisuals(); _lastHoveredCol = null; }
            return;
        }

        Vector2 screenPoint = (Input.touchCount > 0) ?
                              Input.GetTouch(0).position :
                              new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        Ray ray = _mainCamera.ScreenPointToRay(screenPoint);
        bool hit = Physics.Raycast(ray, out RaycastHit hitInfo, raycastMaxDistance, clickLayers);
        if(!hit) hit = Physics.Raycast(ray, out hitInfo, raycastMaxDistance);

        Collider hoveredCol = hit ? hitInfo.collider : null;

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
        ClearHoverVisuals();

        if(hoveredCol == null) return;

        string msg = GetHoverActionMsg(hoveredCol);
        if(string.IsNullOrEmpty(msg)) return;

        string objName = GetHoverTargetName(hoveredCol);
        SetGuideUI(msg);
        SpawnWorldLabel(hitInfo.point, objName, msg);
        SetOutline(hoveredCol.gameObject);
    }

    private string GetHoverTargetName(Collider col)
    {
        var item = col.GetComponent<TaskItem>() ?? col.GetComponentInParent<TaskItem>();
        if(item != null) return item.prefabId;

        var anim = col.GetComponent<ClickableAnimator>() ?? col.GetComponentInParent<ClickableAnimator>();
        if(anim != null) return anim.uniqueId;

        var zone = col.GetComponent<TaskInteractionZone>() ?? col.GetComponentInParent<TaskInteractionZone>();
        if(zone != null) return zone.zoneId;

        return col.gameObject.name;
    }

    private string GetHoverActionMsg(Collider col)
    {
        if(col == null) return string.Empty;
        if(_heldObject != null) return "더블 탭 또는 DROP 버튼으로 내려놓기";

        var anim = col.GetComponent<ClickableAnimator>() ?? col.GetComponentInParent<ClickableAnimator>();
        if(anim != null) return anim.IsOpen ? "터치하여 닫기" : "터치하여 열기";

        var item = col.GetComponent<TaskItem>() ?? col.GetComponentInParent<TaskItem>();
        if(item != null)
        {
            var sg = item.GetComponent<SyncGrab>() ?? item.GetComponentInParent<SyncGrab>();
            if(sg != null) return sg.IsGrabbed ? "다른 플레이어가 사용 중" : "터치하여 집기";
            return "터치";
        }

        var zone = col.GetComponent<TaskInteractionZone>() ?? col.GetComponentInParent<TaskInteractionZone>();
        if(zone != null && zone.gameObject.activeInHierarchy) return "터치하여 상호작용";

        return string.Empty;
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
        }

        _worldLabel.AddComponent<UIHoverFader>();
    }

    private void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach(Transform child in obj.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    private void SetGuideUI(string msg)
    {
        if(hoverGuideText != null) hoverGuideText.text = msg;
        if(hoverGuidePanel != null) hoverGuidePanel.SetActive(!string.IsNullOrEmpty(msg));
    }

    private void ClearHoverVisuals()
    {
        SetGuideUI(string.Empty);
        if(_worldLabel != null)
        {
            var fader = _worldLabel.GetComponent<UIHoverFader>();
            if(fader != null) fader.FadeOut();
            else Destroy(_worldLabel);
            _worldLabel = null;
        }
        SetOutline(null);
    }

    // ═══════════════════════════════════════════════════════
    // 레이저 그랩
    // ═══════════════════════════════════════════════════════

    private void StartLaserPull(SyncGrab target, Vector3 hitPoint)
    {
        if(_heldObject != null) return;

        _heldOriginalScale = target.transform.localScale;
        _isFlying = true;
        _heldObject = target;

        Managers.Sound.Play(grabSound);
        HeldItemUI.Instance?.UpdateUI(target.gameObject);

        target.OnReleased += OnHeldObjectReleased;
        target.OnPCClick();

        Log($"[LaserGrab] 시작: {target.name}");
    }

    private void OnHeldObjectReleased()
    {
        if(_heldObject == null) return;
        _heldObject.OnReleased -= OnHeldObjectReleased;
        _heldObject.transform.localScale = _heldOriginalScale;
        _heldObject = null;
        _isFlying = false;
        HeldItemUI.Instance?.ClearUI();
        Log("[LaserGrab] 외부 릴리즈");
    }

    private void UpdateLaserLine()
    {
        EnsureLaserLine();

        if(_heldObject != null)
        {
            laserLine.enabled = false;
            SetOutline(null);
            return;
        }

        Vector2 screenPoint = (Input.touchCount > 0) ?
                              Input.GetTouch(0).position :
                              new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        Ray ray = _mainCamera.ScreenPointToRay(screenPoint);
        bool hit = Physics.Raycast(ray, out RaycastHit hitInfo, raycastMaxDistance, clickLayers);
        if(!hit) hit = Physics.Raycast(ray, out hitInfo, raycastMaxDistance);

        if(!hit)
        {
            laserLine.enabled = false;
            SetOutline(null);
            return;
        }

        SyncGrab syncGrab = hitInfo.collider.GetComponent<SyncGrab>()
                         ?? hitInfo.collider.GetComponentInParent<SyncGrab>();
        var part = hitInfo.collider.GetComponent<InteractablePart>()
                ?? hitInfo.collider.GetComponentInParent<InteractablePart>();

        GameObject targetGO = null;
        if(syncGrab != null && !syncGrab.IsGrabbed)
        {
            targetGO = syncGrab.gameObject;
        }
        else if(part != null && part.currentState != InteractablePart.PartState.Assembled)
        {
            targetGO = part.gameObject;
        }

        SetOutline(targetGO);

        if(targetGO != null)
        {
            laserLine.enabled = true;
            DrawCurvedLaser(_mainCamera.transform, hitInfo.point);
        }
        else
        {
            laserLine.enabled = false;
        }
    }

    private void DrawCurvedLaser(Transform origin, Vector3 targetPoint)
    {
        int count = Mathf.Max(laserVertexCount, 2);
        laserLine.positionCount = count;

        Vector3 start = origin.position + origin.forward * 0.05f;
        Vector3 end = targetPoint;

        Vector3 forwardPoint = start + origin.forward * 0.4f;
        Vector3 itemNormal = (start - end).normalized;
        Vector3 v = forwardPoint - end;
        Vector3 projected = forwardPoint - Vector3.Project(v, itemNormal);
        Vector3 midPoint = projected;

        for(int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            Vector3 p = (1 - t) * (1 - t) * start
                      + 2 * (1 - t) * t * midPoint
                      + t * t * end;
            laserLine.SetPosition(i, p);
        }
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
        Log($"[LaserGrab] 스케일 축소: {maxDim:F2}m → {ratio:F2}배");
    }

    private void DropHeldObject()
    {
        if(_heldObject == null) return;

        Managers.Sound.Play(dropSound);

        var dropping = _heldObject;
        dropping.OnReleased -= OnHeldObjectReleased;

        _heldObject = null;
        _isFlying = false;
        SetOutline(null);
        HeldItemUI.Instance?.ClearUI();

        dropping.transform.localScale = _heldOriginalScale;
        dropping.StopPCHold();
        dropping.RequestRelease();

        Log("[LaserGrab] 내려놓기 완료");
    }

    // ═══════════════════════════════════════════════════════
    // 유틸
    // ═══════════════════════════════════════════════════════

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

    private void EnsureLaserLine()
    {
        if(laserLine != null) return;

        var go = new GameObject("Mobile_LaserLine");
        go.transform.SetParent(transform);
        laserLine = go.AddComponent<LineRenderer>();
        laserLine.positionCount = laserVertexCount;
        laserLine.startWidth = laserStartWidth;
        laserLine.endWidth = laserEndWidth;
        laserLine.useWorldSpace = true;
        laserLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        laserLine.receiveShadows = false;

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                     ?? Shader.Find("Particles/Standard Unlit")
                     ?? Shader.Find("Sprites/Default");

        var mat = new Material(shader);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 3000;
        laserLine.material = mat;
        laserLine.colorGradient = MakeGradient(laserColorStart, laserColorEnd);
        laserLine.enabled = false;

        Log("레이저 라인 자동 생성");
    }

    private static Gradient MakeGradient(Color start, Color end)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
            new[] { new GradientAlphaKey(start.a, 0f), new GradientAlphaKey(end.a, 1f) });
        return g;
    }

    private bool IsPointerOverUI(Vector2 pos)
    {
        if(EventSystem.current == null) return false;
        PointerEventData data = new PointerEventData(EventSystem.current) { position = pos };
        _uiRaycastResults.Clear();
        EventSystem.current.RaycastAll(data, _uiRaycastResults);
        foreach(var r in _uiRaycastResults)
        {
            Canvas c = r.gameObject.GetComponentInParent<Canvas>();
            if(c == null || c.renderMode == RenderMode.WorldSpace) continue;

            // 버튼·슬라이더·스크롤·조이스틱은 항상 UI 로 취급
            if(r.gameObject.GetComponentInParent<Selectable>() != null || r.gameObject.GetComponentInParent<ScrollRect>() != null
               || r.gameObject.GetComponentInParent<JoystickController>() != null) return true;

            // 눈에 보이는 패널만 막는다 — 투명한(알파 ~0) 레이캐스트 영역은 3D 터치를 통과시킨다
            var g = r.gameObject.GetComponent<Graphic>();
            if(g != null && g.color.a * g.canvasRenderer.GetInheritedAlpha() > 0.05f) return true;
        }
        return false;
    }

    private void Log(string msg)
    {
        if(showDebugLog) Debug.Log($"[Mobile] {msg}");
    }
}
