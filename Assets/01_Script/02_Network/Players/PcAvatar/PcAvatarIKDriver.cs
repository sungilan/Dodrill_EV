using UnityEngine;

/// <summary>
/// 애니메이션 포즈가 적용된 뒤(모든 LateUpdate보다 먼저, order -100), FinalIK가 풀기 전에
/// IK "입력"만 준비한다:
///
///   1. 크라우치: 힙 낮춤 + LimbIK 발 타깃/가중치 설정 (PcCrouchIK.ApplyPreIK)
///   2. 머리 타깃 계산 (PcHeadAim.ComputeTarget)
///
/// 실제 풀이는 LimbIK/LookAtIK 컴포넌트가 각자의 LateUpdate(기본 order 0)에서 수행한다.
/// (예전에는 컴포넌트를 끄고 solver.Update()를 직접 불렀으나, 에디터에서
///  SetSystemInterested 관련 잡음이 있어 표준 방식으로 되돌림.)
///
/// 크라우치·머리 각도는 Animator 파라미터(Crouch/HeadPitch)에서 읽으므로
/// 소유자/원격이 같은 코드로 동작한다. (원격은 NetworkAnimator가 파라미터를 채움)
/// </summary>
[DefaultExecutionOrder(-100)]
public class PcAvatarIKDriver : MonoBehaviour
{
    [SerializeField] private Animator anim;
    [SerializeField] private PcCrouchIK crouch;
    [SerializeField] private PcHeadAim headAim;
    [SerializeField] private PcArmReach armReach;
    [Tooltip("물체를 잡았을 때 손가락을 쥐는 모양 (PcAvatarBuilder 가 만들어 연결)")]
    [SerializeField] private PcHandGrip handGrip;

    private PcMobileAvatarController _controller;

    // 잡고 있는 물체 (SyncGrab 이 소유권을 잡은 사람에게 넘기는 것을 이용 — 소유자/원격 모두 같은 코드로 동작)
    private SyncGrab _held;
    private bool _holdRightHand = true;
    private float _nextHeldSearch;
    private float _lastHeldSeen = -10f;
    [Tooltip("잡고 있다는 판정이 잠깐(소유권 전환·동기화 지연) 끊겨도 이 시간(초) 동안은 잡은 자세를 유지한다. 짧게 끊길 때마다 손이 기본 자세로 왔다갔다 하던 것을 막는다.")]
    [SerializeField] private float holdGraceSeconds = 0.5f;

    private void Start()
    {
        _controller = GetComponentInParent<PcMobileAvatarController>();
        if (anim == null) anim = GetComponent<Animator>();
        if (headAim != null) headAim.Configure();
        if (armReach != null) armReach.Configure();
    }

    private void LateUpdate()
    {
        if (anim == null) return;

        float crouch01 = anim.GetFloat(PcAvatarAnimParams.CrouchHash);
        float headPitch = anim.GetFloat(PcAvatarAnimParams.HeadPitchHash);
        bool isOwner = _controller == null || _controller.IsOwnerView;
        Quaternion bodyRot = _controller != null ? _controller.transform.rotation : transform.rotation;

        float scale = _controller != null ? _controller.AvatarScale : 1f;
        if (crouch != null) crouch.ApplyPreIK(crouch01, scale);
        if (headAim != null) headAim.ComputeTarget(isOwner, headPitch, bodyRot);
        if (armReach != null)
        {
            UpdateHold();
            armReach.Tick(Time.deltaTime);
            if (handGrip != null)
                handGrip.ApplyCurl(armReach.HoldUsesRightHand, Mathf.SmoothStep(0f, 1f, armReach.HoldWeight));
        }
    }

    /// <summary>이 플레이어가 물체를 잡고 있으면 그 물체 쪽으로 손을 유지하고 손가락을 쥔다.</summary>
    private void UpdateHold()
    {
        if (_controller == null) { armReach.SetHold(false, true, Vector3.zero, Quaternion.identity); return; }

        if (Time.time >= _nextHeldSearch)
        {
            _nextHeldSearch = Time.time + 0.1f;
            var found = SyncGrab.FindHeldBy(_controller.HolderId);
            if (found != null) _lastHeldSeen = Time.time;
            // 잠깐 안 보이는 것이면 직전 물체를 유지
            if (found == null && _held != null && Time.time - _lastHeldSeen < holdGraceSeconds) found = _held;
            if (found != _held)
            {
                _held = found;
                if (_held != null)
                {
                    // 잡는 순간 몸 기준으로 물체가 왼쪽에 치우쳐 있으면 왼손, 아니면 오른손 (잡고 있는 동안은 유지)
                    float lateral = _controller.transform.InverseTransformPoint(_held.GripWorldPoint).x;
                    _holdRightHand = lateral > -0.15f;
                }
            }
        }

        bool graceOk = _held != null && Time.time - _lastHeldSeen < holdGraceSeconds;
        if (_held == null || (!_held.IsGrabbed && !graceOk))
        {
            armReach.SetHold(false, _holdRightHand, Vector3.zero, Quaternion.identity);
            DebugHold(false);
            return;
        }

        Quaternion yaw = Quaternion.Euler(0f, _controller.transform.eulerAngles.y, 0f);
        bool baked = handGrip != null && handGrip.IsBaked(_holdRightHand);
        Quaternion rot = baked ? handGrip.HandRotation(_holdRightHand, yaw) : Quaternion.identity;
        // 물체 위치가 몸에서 한참 떨어져 있으면(동기화 지연·잘못된 위치) 그쪽으로 팔을 쭉 펴지 말고
        // 몸 앞 가슴 높이에 든 자세를 유지한다. 가만히 서 있을 때 팔이 펴져 기본 자세처럼 보이던 문제 방지.
        Vector3 target = _held.GripWorldPoint;
        Transform body = _controller.transform;
        float s = Mathf.Max(0.01f, _controller.AvatarScale);
        if ((target - body.position).sqrMagnitude > (1.6f * s) * (1.6f * s))
            target = body.position + body.up * (1.05f * s) + body.forward * (0.35f * s) + body.right * ((_holdRightHand ? 0.18f : -0.18f) * s);
        armReach.SetHold(true, _holdRightHand, target, rot, baked);
        DebugHold(true);
    }

    // ── 디버그 ([HoldDebug]) — 원격 화면에서 '잡은 자세 ↔ 기본 자세'가 왔다갔다 하는 원인을 가리기 위한 기록 ──
    [Header("디버그")]
    [SerializeField] private bool debugLog = true;
    [SerializeField] private float debugInterval = 1f;
    private float _dbgNext;
    private bool _dbgLastHolding;
    private int _dbgToggles;
    private int _dbgLastReach;

    private void DebugHold(bool holding)
    {
        if (!debugLog || _controller == null || _controller.IsOwnerView) return;

        bool changed = holding != _dbgLastHolding;
        if (changed) _dbgToggles++;
        _dbgLastHolding = holding;
        if (!changed && Time.unscaledTime < _dbgNext) return;
        _dbgNext = Time.unscaledTime + debugInterval;

        int reach = armReach.DbgReachCount;
        string heldInfo = _held == null ? "held=null" :
            $"held={_held.name} grabbed={_held.IsGrabbed} ownerId={(_held.Owner != null && _held.Owner.IsValid ? _held.Owner.ClientId : -1)}";
        float speed = anim != null ? anim.GetFloat(PcAvatarAnimParams.SpeedHash) : -1f;
        Debug.Log($"[HoldDebug] holderId={_controller.HolderId} holding={holding}{(changed ? " (변경)" : "")} toggles={_dbgToggles} {heldInfo} " +
                  $"hand={(armReach.HoldUsesRightHand ? "R" : "L")} flips={armReach.DbgHoldSideFlips} holdW={armReach.HoldWeight:F2} " +
                  $"reach+={reach - _dbgLastReach} reaching={armReach.DbgReaching} dist={armReach.DbgHoldDist:F2}/{armReach.DbgArmLen:F2} clamped={armReach.DbgHoldClamped} speed={speed:F2}");
        _dbgLastReach = reach;
    }
}
