using RootMotion.FinalIK;
using UnityEngine;

/// <summary>
/// 상호작용 클릭 시 그 지점으로 손을 뻗었다 되돌아오는 짧은 리치 동작.
/// 타깃이 몸 오른쪽이면 오른팔, 왼쪽이면 왼팔. 자체 Update 없음 — PcAvatarIKDriver가
/// FinalIK 팔 solve 직전에 Tick()을 부른다.
///
/// 팔 LimbIK 는 bendModifier = Arm (자연스러운 팔꿈치) 권장, IKRotationWeight = 0
/// (손목 회전은 애니메이션 유지).
/// </summary>
public class PcArmReach : MonoBehaviour
{
    [SerializeField] private LimbIK leftArm;
    [SerializeField] private LimbIK rightArm;
    [Tooltip("좌/우 판정 기준 (보통 플레이어 루트 — yaw만 도는 프레임).")]
    [SerializeField] private Transform bodyRef;

    [Header("타이밍(초)")]
    [SerializeField] private float reachTime = 0.22f;
    [SerializeField] private float holdTime = 0.12f;
    [SerializeField] private float retractTime = 0.33f;

    [Header("거리")]
    [Tooltip("어깨에서 이 거리(m, 아바타 스케일 곱)보다 멀면 그만큼만 뻗음.")]
    [SerializeField] private float maxReachDistance = 1.15f;
    [Tooltip("표면 살짝 앞에서 손이 멈추도록 당기는 양(m, 스케일 곱).")]
    [SerializeField] private float surfacePullback = 0.04f;

    [Header("물체 잡기 (잡고 있는 동안 손을 유지)")]
    [Tooltip("잡기 자세로 들어가고 나가는 데 걸리는 시간(초)")]
    [SerializeField] private float holdBlendTime = 0.18f;
    [Tooltip("손목 방향 IK 가중치(1 = 손바닥이 지정 방향을 완전히 따름)")]
    [SerializeField] [Range(0f, 1f)] private float holdRotationWeight = 0.85f;

    private bool _holding;
    private bool _holdUsesRight = true;
    private Vector3 _holdPos;
    private Quaternion _holdRot = Quaternion.identity;
    private bool _holdUseRot = true;
    private float _holdW;
    private float _armLenRight, _armLenLeft;

    /// <summary>0..1 — 지금 잡기 자세가 얼마나 반영됐는지(손가락 쥠 가중치로도 쓴다).</summary>
    public float HoldWeight => _holdW;
    public bool HoldUsesRightHand => _holdUsesRight;

    /// <summary>해당 손(손목 본). 없으면 null.</summary>
    public Transform HandTransform(bool right)
    {
        var arm = right ? rightArm : leftArm;
        if (arm == null || arm.solver == null || arm.solver.bone3 == null) return null;
        return arm.solver.bone3.transform;
    }

    /// <summary>매 프레임: 잡고 있으면 true + 손이 가야 할 월드 위치/회전. 잡지 않으면 false.</summary>
    public void SetHold(bool holding, bool useRightHand, Vector3 worldPos, Quaternion handRot, bool useRotation = true)
    {
        _holding = holding;
        if (!holding) return;

        // 잡기 시작할 때 같은 손으로 뻗기가 진행 중이면 뻗기를 끝낸다 — 안 그러면 뻗기가 끝나며 가중치가 0으로 돌아가
        // 잡은 손이 기본 자세로 갔다가 다시 돌아오는 '왔다갔다'가 생긴다.
        if (_active != null && _active == (useRightHand ? rightArm : leftArm)) _active = null;

        // 손 쪽이 바뀌면(가중치가 남아 있을 때) 이전 손은 먼저 풀리도록 새 손은 0부터 올린다
        if (_holdUsesRight != useRightHand) { _holdW = 0f; _holdUsesRight = useRightHand; }

        // 팔 길이를 넘는 위치는 팔이 닿는 데까지만 (손이 허공에서 뻗어 어색해지는 것 방지)
        var arm = useRightHand ? rightArm : leftArm;
        if (arm != null && arm.solver.bone1 != null && arm.solver.bone1.transform != null)
        {
            Vector3 shoulder = arm.solver.bone1.transform.position;
            float len;
            if (useRightHand) len = ArmLength(arm, ref _armLenRight); else len = ArmLength(arm, ref _armLenLeft);
            Vector3 d = worldPos - shoulder;
            float max = len * 0.97f;
            if (len > 0.01f && d.magnitude > max) worldPos = shoulder + d.normalized * max;
        }
        _holdPos = worldPos;
        _holdRot = handRot;
        _holdUseRot = useRotation;
    }

    private static float ArmLength(LimbIK arm, ref float cache)
    {
        if (cache > 0.01f) return cache;
        var s = arm.solver;
        if (s.bone1?.transform == null || s.bone2?.transform == null || s.bone3?.transform == null) return 0f;
        cache = Vector3.Distance(s.bone1.transform.position, s.bone2.transform.position) + Vector3.Distance(s.bone2.transform.position, s.bone3.transform.position);
        return cache;
    }

    private LimbIK _active;
    private Vector3 _target;
    private float _t;
    private float _total;

    /// <summary>PcAvatarIKDriver.Start()에서 1회.</summary>
    public void Configure()
    {
        Zero(leftArm);
        Zero(rightArm);
    }

    private static void Zero(LimbIK arm)
    {
        if (arm == null) return;
        arm.solver.IKPositionWeight = 0f;
        arm.solver.IKRotationWeight = 0f;
    }

    /// <summary>리치 시작. worldPoint = 클릭이 맞은 지점. scale = 아바타 월드 스케일.</summary>
    public void Reach(Vector3 worldPoint, float scale = 1f)
    {
        Vector3 local = bodyRef != null ? bodyRef.InverseTransformPoint(worldPoint) : worldPoint;
        var arm = local.x >= 0f ? rightArm : leftArm;
        if (arm == null) arm = rightArm != null ? rightArm : leftArm;
        // 물체를 잡고 있는 손으로는 뻗지 않는다 — 반대 손으로
        if (_holding || _holdW > 0.05f)
        {
            var holdArm = _holdUsesRight ? rightArm : leftArm;
            var other = _holdUsesRight ? leftArm : rightArm;
            if (arm == holdArm && other != null) arm = other;
        }
        if (arm == null) return;

        var s = arm.solver;
        Vector3 shoulder = (s.bone1 != null && s.bone1.transform != null) ? s.bone1.transform.position : transform.position;
        Vector3 dir = worldPoint - shoulder;
        float max = Mathf.Max(0.1f, maxReachDistance * scale);
        if (dir.magnitude > max) worldPoint = shoulder + dir.normalized * max;
        else if (dir.sqrMagnitude > 0.0001f) worldPoint -= dir.normalized * (surfacePullback * scale);

        _active = arm;
        _target = worldPoint;
        _t = 0f;
        _total = reachTime + holdTime + retractTime;
    }

    /// <summary>PcAvatarIKDriver.LateUpdate()에서 매 프레임, FinalIK 팔 solve 직전.</summary>
    public void Tick(float dt)
    {
        float w = CurrentWeight();
        SetPos(leftArm, _active == leftArm ? w : 0f);
        SetPos(rightArm, _active == rightArm ? w : 0f);

        if (_active != null)
        {
            _active.solver.IKPosition = _target;
            _t += dt;
            if (_t >= _total) _active = null;
        }

        TickHold(dt);
    }

    private void TickHold(float dt)
    {
        _holdW = Mathf.MoveTowards(_holdW, _holding ? 1f : 0f, dt / Mathf.Max(0.01f, holdBlendTime));
        if (_holdW <= 0.001f) return;

        var arm = _holdUsesRight ? rightArm : leftArm;
        if (arm == null || arm == _active) return;   // 이 손으로 뻗는 중이면 뻗기가 우선(위에서 다른 손을 고르므로 드문 경우)

        float w = Mathf.SmoothStep(0f, 1f, _holdW);
        arm.solver.IKPosition = _holdPos;
        arm.solver.IKPositionWeight = w;
        arm.solver.IKRotation = _holdRot;
        arm.solver.IKRotationWeight = _holdUseRot ? w * holdRotationWeight : 0f;
    }

    private float CurrentWeight()
    {
        if (_active == null) return 0f;
        if (_t < reachTime) return Mathf.SmoothStep(0f, 1f, _t / Mathf.Max(0.01f, reachTime));
        if (_t < reachTime + holdTime) return 1f;
        float k = (_t - reachTime - holdTime) / Mathf.Max(0.01f, retractTime);
        return Mathf.SmoothStep(1f, 0f, k);
    }

    private static void SetPos(LimbIK arm, float w)
    {
        if (arm != null) arm.solver.IKPositionWeight = w;
    }
}
