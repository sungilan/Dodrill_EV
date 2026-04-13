using Autohand;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Meta OS 스타일 UI 재배치 (축 제한 버전).
///
/// ■ 좌우 : 항상 카메라 정면 고정 (X축 이동 없음)
/// ■ 상하 : 컨트롤러 Y 이동 반영
/// ■ 앞뒤 : 컨트롤러 Z(전후) 이동 반영 → distance 조절
///
/// ■ 홀드 중 커서가 버튼 밖으로 나가도 드래그 유지
/// ■ 어디서 놓아도 버튼 이미지가 OFF 상태로 복귀
/// </summary>
public class VRUiMetaRepositionDrag : MonoBehaviour
{
    [SerializeField] private VRUIFollower _follower;

    [Header("이동 버튼 (놓을 때 OFF 상태 복귀)")]
    [Tooltip("BTN_Move 오브젝트를 연결. 어디서 놓아도 버튼 이미지가 OFF로 복귀됩니다.")]
    [SerializeField] private GameObject _moveButtonObject;

    [Header("보간 속도")]
    [SerializeField] private float _posLerp = 20f;
    [SerializeField] private float _rotLerp = 12f;

    [Header("XR Trigger Axis (float 0~1)")]
    [Tooltip("Auto Hand/Trigger Axis (R) 등 float Axis 액션. 비우면 PointerUp으로만 종료.")]
    [SerializeField] private InputActionReference _triggerAction;
    [SerializeField] private float _releaseThreshold = 0.3f;

    [Header("드래그 앵커 (컨트롤러)")]
    [Tooltip("비우면 씬에서 R_Hand → L_Hand 순으로 자동 검색합니다.")]
    [SerializeField] private Transform _controllerDragAnchor;

    [Header("축 감도")]
    [Tooltip("컨트롤러 Y 이동 -> UI 상하 이동 배율")]
    [SerializeField] private float _verticalScale = 1.5f;
    [Tooltip("컨트롤러 이동을 시선 수평면 기준 앞·뒤(distance)로 투영할 때 배율")]
    [SerializeField] private float _depthScale = 1.5f;

    // ── 내부 상태 ──────────────────────────────────────
    private bool _holding;
    private bool _triggerConfirmedPressed;

    private Transform _head;
    private Transform _anchor;

    // 드래그 시작 시점 스냅샷
    private Vector3 _anchorStartWorld;
    private float _startDistance;
    private float _startHeight;

    void OnEnable()
    {
        if (_triggerAction != null && _triggerAction.action != null)
            _triggerAction.action.Enable();
    }

    void Update()
    {
        if (_holding)
            CheckTriggerRelease();

        if (!_holding) return;

        EnsureHead();
        if (_head == null) return;

        if (_anchor == null || !_anchor.gameObject.activeInHierarchy)
        {
            _anchor = FindDragAnchor() ?? _head;
            if (_anchor == null) return;
            _anchorStartWorld = _anchor.position;
        }

        Vector3 anchorDelta = _anchor.position - _anchorStartWorld;

        Vector3 flatForward = _head.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-6f) flatForward = Vector3.forward;
        flatForward.Normalize();

        float depthDelta = Vector3.Dot(anchorDelta, flatForward) * _depthScale;
        float heightDelta = anchorDelta.y * _verticalScale;

        float newDistance = Mathf.Max(0.3f, _startDistance + depthDelta);
        float newHeight = _startHeight + heightDelta;

        Vector3 targetPos = new Vector3(
            _head.position.x + flatForward.x * newDistance,
            newHeight,
            _head.position.z + flatForward.z * newDistance
        );

        Quaternion targetRot = Quaternion.LookRotation(flatForward, Vector3.up);

        float dt = Time.unscaledDeltaTime;
        transform.position = Vector3.Lerp(transform.position, targetPos,
            1f - Mathf.Exp(-_posLerp * dt));
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot,
            1f - Mathf.Exp(-_rotLerp * dt));
    }

    // ── public API ──────────────────────────────────────

    public void BeginHoldReposition()
    {
        if (_follower == null) _follower = GetComponent<VRUIFollower>();
        EnsureHead();

        _anchor = FindDragAnchor() ?? _head;

        if (_anchor != null)
            _anchorStartWorld = _anchor.position;

        if (_head != null)
        {
            Vector3 toUi = transform.position - _head.position;
            Vector3 flatFwd = _head.forward;
            flatFwd.y = 0f;
            if (flatFwd.sqrMagnitude < 1e-6f) flatFwd = Vector3.forward;
            flatFwd.Normalize();

            Vector3 toUiFlat = new Vector3(toUi.x, 0f, toUi.z);
            _startDistance = Mathf.Max(0.3f, Vector3.Dot(toUiFlat, flatFwd));
        }

        _startHeight = transform.position.y;

        _holding = true;
        _triggerConfirmedPressed = false;
        _follower?.BeginManualReposition();
    }

    public void EndHoldReposition()
    {
        if (!_holding) return;
        _holding = false;
        _triggerConfirmedPressed = false;
        _anchor = null;
        _follower?.EndManualReposition();

        // 어디서 놓아도 버튼 시각 상태를 OFF로 복귀
        ResetButtonVisual();
    }

    // EventTrigger (BaseEventData) 오버로드
    public void BeginHoldReposition(BaseEventData _) => BeginHoldReposition();
    public void EndHoldReposition(BaseEventData _) => EndHoldReposition();

    // Autohand FingerTriggerAreaEvents 오버로드
    public void BeginHoldReposition(Finger f, FingerTriggerAreaEvents a) => BeginHoldReposition();
    public void EndHoldReposition(Finger f, FingerTriggerAreaEvents a) => EndHoldReposition();

    void OnDisable() { if (_holding) EndHoldReposition(); }

    // ── 버튼 시각 상태 리셋 ─────────────────────────────

    private void ResetButtonVisual()
    {
        if (_moveButtonObject == null) return;

        var eventData = new PointerEventData(EventSystem.current);

        // PointerUp → PointerExit 순서로 전달해 Pressed 상태 해제
        ExecuteEvents.Execute(_moveButtonObject,
            eventData, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(_moveButtonObject,
            eventData, ExecuteEvents.pointerExitHandler);

        // ClickButton 커스텀 스크립트가 있다면 직접 Normal 상태로 전환 시도
        var clickButton = _moveButtonObject.GetComponent<ClickButton>();
        if (clickButton != null)
            clickButton.OnPointerUp(eventData);
    }

    // ── 트리거 릴리즈 감지 ──────────────────────────────

    private void CheckTriggerRelease()
    {
        if (_triggerAction == null || _triggerAction.action == null) return;

        float value = _triggerAction.action.ReadValue<float>();

        if (!_triggerConfirmedPressed)
        {
            if (value >= _releaseThreshold)
                _triggerConfirmedPressed = true;
            return;
        }

        if (value < _releaseThreshold)
            EndHoldReposition();
    }

    // ── 유틸 ────────────────────────────────────────────

    private void EnsureHead()
    {
        if (_head == null && Camera.main != null)
            _head = Camera.main.transform;
    }

    private Transform FindDragAnchor()
    {
        if (_controllerDragAnchor != null && _controllerDragAnchor.gameObject.activeInHierarchy)
            return _controllerDragAnchor;

        var ownerRoot = FindOwnerPlayerRoot();

        if (ownerRoot != null)
        {
            var t = FindChildByName(ownerRoot, "R_Hand");
            if (t != null) return t;

            t = FindChildByName(ownerRoot, "L_Hand");
            if (t != null) return t;
        }

        var hands = FindObjectsByType<Autohand.Hand>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (var h in hands)
            if (h != null && !h.left)
                return h.follow ? h.follow : h.transform;

        foreach (var h in hands)
            if (h != null)
                return h.follow ? h.follow : h.transform;

        return null;
    }

    private static Transform FindOwnerPlayerRoot()
    {
        var players = FindObjectsByType<BasePlayer>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (var p in players)
            if (p.IsOwner)
                return p.transform;

        return null;
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        if (root.name == childName) return root;

        foreach (Transform child in root)
        {
            var found = FindChildByName(child, childName);
            if (found != null) return found;
        }

        return null;
    }
}