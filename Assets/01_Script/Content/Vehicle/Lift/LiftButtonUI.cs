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

    // ── PC/모바일 화면 클릭용 레지스트리 ────────────────────
    // 이 버튼들은 월드 스페이스 캔버스 위에 있고 콜라이더가 없다.
    // 물리 레이캐스트(FreeLookController)로는 맞출 수 없으므로,
    // 활성 버튼을 등록해 두고 화면 좌표 ↔ 캔버스 사각형 교차로 직접 판정한다.
    private static readonly System.Collections.Generic.List<LiftButtonUI> s_active = new();

    private void OnEnable()
    {
        if (!s_active.Contains(this)) s_active.Add(this);
    }

    /// <summary>
    /// 화면 좌표가 이 버튼의 사각형(월드 캔버스)에 닿는지 검사한다.
    /// 콜라이더·EventSystem·canvas.worldCamera 설정과 무관하게 동작한다.
    /// </summary>
    public bool TryHitFromScreen(Camera cam, Vector2 screenPos, float maxDistance, out float distance)
    {
        distance = 0f;
        if (cam == null || liftTarget == null || !isActiveAndEnabled) return false;

        var rt = transform as RectTransform;
        if (rt == null) return false;

        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null || !canvas.isActiveAndEnabled) return false;
        if (canvas.rootCanvas.renderMode != RenderMode.WorldSpace) return false;

        Ray ray = cam.ScreenPointToRay(screenPos);
        var plane = new Plane(rt.forward, rt.position);
        if (!plane.Raycast(ray, out float enter) || enter <= 0f || enter > maxDistance) return false;

        Vector3 local = rt.InverseTransformPoint(ray.GetPoint(enter));
        if (!rt.rect.Contains(new Vector2(local.x, local.y))) return false;

        distance = enter;
        return true;
    }

    // 가림 판정용 버퍼 (할당 방지)
    private static readonly RaycastHit[] s_occlusionBuf = new RaycastHit[16];

    /// <summary>
    /// 화면 좌표 아래에 있는 가장 가까운 버튼을 반환한다 (PC 클릭·모바일 터치 공용).
    /// 버튼보다 앞쪽에 (ignoreRoot 플레이어 자신이 아닌) 솔리드 콜라이더가 있으면 가려진 것으로 본다.
    /// </summary>
    public static LiftButtonUI FindAtScreen(Camera cam, Vector2 screenPos, float maxDistance, Transform ignoreRoot, out float distance)
    {
        distance = 0f;
        LiftButtonUI best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < s_active.Count; i++)
        {
            var b = s_active[i];
            if (b == null) continue;
            if (b.TryHitFromScreen(cam, screenPos, maxDistance, out float d) && d < bestDist)
            {
                best = b;
                bestDist = d;
            }
        }

        if (best == null) return null;

        int n = Physics.RaycastNonAlloc(cam.ScreenPointToRay(screenPos), s_occlusionBuf, Mathf.Max(0f, bestDist - 0.02f),
                                        ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var col = s_occlusionBuf[i].collider;
            if (col == null) continue;
            if (ignoreRoot != null && col.transform.IsChildOf(ignoreRoot)) continue;
            return null;
        }

        distance = bestDist;
        return best;
    }

    // ───────────────────────────────────────────

    private void Start()
    {
        if (liftTarget == null)
            Debug.LogWarning($"[LiftButtonUI] '{gameObject.name}' — liftTarget이 연결되지 않았습니다. Inspector에서 드래그 연결하세요.");
    }

    [Tooltip("누른 채 이 시간(초)을 넘기면 강제 정지. VR 에서 PointerUp 이 유실돼 리프트가 끝까지 움직이는 것을 막는 안전망. 0 이하면 끔.")]
    public float maxHoldSeconds = 30f;
    private float _pressStartTime;

    private void Update()
    {
        // 안전망: 아래 vrAutoStopSeconds 는 누르는 동안 매 프레임 갱신되어 사실상 만료되지 않으므로 별도 상한을 둔다
        if (_isPressed && maxHoldSeconds > 0f && Time.time - _pressStartTime > maxHoldSeconds)
        {
            ForceStop("최대 유지 시간 초과");
            return;
        }

        if (!_isPressed || vrAutoStopSeconds <= 0f) return;

        if(_isPressed)
        {
            _lastActiveTime = Time.time;
        }

        if (Time.time - _lastActiveTime > vrAutoStopSeconds)
            ForceStop("VR 타임아웃");
    }

    // ── 누름 시작 ─────────────────────────────
    public void OnPointerDown(PointerEventData _)
    {
        _lastActiveTime = Time.time;
        if (_isPressed) return;
        _isPressed = true;
        _pressStartTime = Time.time;

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
    private void OnDisable()
    {
        s_active.Remove(this);
        ForceStop("비활성화");
    }

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