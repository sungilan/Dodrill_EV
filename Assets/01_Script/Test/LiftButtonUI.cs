using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 리프트 버튼 UI — 누르는 동안 작동, 떼면 정지.
///
/// [연결 방법]
///   liftTarget 에 VehicleLiftController 또는 BatteryLiftController 를
///   Inspector에서 직접 드래그 연결.
///   → FindObjectOfType 사용 안 함 → 씬에 리프트가 여러 개여도 정확히 동작.
///
/// [플랫폼 호환]
///   PC     : 마우스 OnPointerDown/Up
///   모바일  : 터치 OnPointerDown/Up (EventSystem 자동 변환)
///   VR     : AutoHand HandUIInput → OnPointerDown/Up/Enter/Exit
///            OnPointerEnter로 타임아웃 갱신 → 누르는 동안 정지 안 됨
/// </summary>
public class LiftButtonUI : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler,
    IPointerEnterHandler, IPointerExitHandler
{
    public enum LiftDirection { Up, Down }

    [Header("방향")]
    public LiftDirection direction = LiftDirection.Up;

    [Header("리프트 연결 — Inspector에서 직접 드래그")]
    [Tooltip("VehicleLiftController 또는 BatteryLiftController 를 직접 연결.\n" +
             "씬에 리프트가 여러 개일 때 FindObjectOfType 대신 직접 참조 사용.")]
    public LiftControllerBase liftTarget;

    [Header("VR 안전망")]
    [Tooltip("HandUIInput PointerUp 누락 대비 자동 정지 시간(초).\n" +
             "OnPointerEnter가 갱신하므로 누르는 동안은 정지 안 됨.\n" +
             "PC/모바일 전용이면 0.")]
    public float vrAutoStopSeconds = 0.3f;

    // ── 내부 상태 ──────────────────────────────

    private bool _isPressed = false;
    private float _lastActiveTime = 0f;

    // ───────────────────────────────────────────

    private void Start()
    {
        if (liftTarget == null)
            Debug.LogWarning($"[LiftButtonUI] '{gameObject.name}' — liftTarget이 연결되지 않았습니다. Inspector에서 드래그 연결하세요.");
    }

    private void Update()
    {
        if (!_isPressed || vrAutoStopSeconds <= 0f) return;
        if (Time.time - _lastActiveTime > vrAutoStopSeconds)
            ForceStop("VR 타임아웃");
    }

    // ── 누름 시작 ─────────────────────────────
    public void OnPointerDown(PointerEventData _)
    {
        _lastActiveTime = Time.time;
        if (_isPressed) return;
        _isPressed = true;

        if (direction == LiftDirection.Up) liftTarget?.OnUpButton();
        else liftTarget?.OnDownButton();

        Log("누름");
    }

    // ── VR: 버튼 위에 머무는 동안 타임아웃 갱신 ──
    public void OnPointerEnter(PointerEventData _)
    {
        if (_isPressed) _lastActiveTime = Time.time;
    }

    // ── 뗄 때 ────────────────────────────────
    public void OnPointerUp(PointerEventData _) => ForceStop("PointerUp");

    // ── 버튼 밖으로 이탈 ─────────────────────
    public void OnPointerExit(PointerEventData _) => ForceStop("PointerExit");

    private void OnApplicationFocus(bool hasFocus) { if (!hasFocus) ForceStop("포커스 해제"); }
    private void OnDisable() => ForceStop("비활성화");

    // ── VR 직접 연결용 (AutoHand Interactable 이벤트에 Inspector 연결) ──
    public void VRPress() => OnPointerDown(null);
    public void VRRelease() => ForceStop("VRRelease");

    // ─────────────────────────────────────────

    private void ForceStop(string reason)
    {
        if (!_isPressed) return;
        _isPressed = false;
        liftTarget?.OnStopButton();
        Log($"정지 — {reason}");
    }

    private void Log(string msg)
    {
#if UNITY_EDITOR
        Debug.Log($"[LiftButtonUI:{gameObject.name}] {msg}");
#endif
    }
}