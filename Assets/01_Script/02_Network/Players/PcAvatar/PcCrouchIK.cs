using RootMotion.FinalIK;
using UnityEngine;

/// <summary>
/// 크라우치 0..1 에 맞춰 힙을 내리고, 두 다리 LimbIK가 발을 바닥에 붙들어 무릎을 접는다.
/// 자체 Update 없음 — PcAvatarIKDriver가 Animator 포즈 이후에 ApplyPreIK()를 부르고,
/// 이어서 LimbIK 솔버를 돌린다.
///
/// LimbIK 컴포넌트에는 bendModifier=Goal + bendGoal(무릎 앞 빈 오브젝트)을 프리팹에서 지정.
/// bone3(발)은 raycast 원점으로도 재사용한다.
/// </summary>
public class PcCrouchIK : MonoBehaviour
{
    [SerializeField] private Transform hips;
    [SerializeField] private LimbIK leftLeg;
    [SerializeField] private LimbIK rightLeg;

    [Header("힙 낮춤")]
    [Tooltip("크라우치 1.0에서 힙이 내려가는 거리(m). crouchDepth와 비슷하게.")]
    [SerializeField] private float hipDrop = 0.55f;

    [Header("발 접지")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float footProbeUp = 0.5f;
    [SerializeField] private float footProbeDown = 1.5f;
    [Tooltip("발 본(발목)을 바닥 위 몇 m 에 둘지. 아래 '발목 높이 자동 측정'이 켜져 있고 측정에 성공하면 이 값은 쓰지 않는다(측정 실패 시 대체값).")]
    [SerializeField] private float footYOffset = 0.02f;
    [Tooltip("시작할 때 서 있는 자세에서 (발 본 높이 - 바닥 높이)를 재서 앉을 때 발 본을 그 높이에 둔다.\n"
             + "예전에는 발 본을 바닥+2cm 에 둬서 발목~밑창 거리(약 9cm)만큼 신발이 바닥에 묻혔다.")]
    [SerializeField] private bool autoAnkleHeight = true;
    [Tooltip("자동 측정 결과가 이 범위(m, 아바타 스케일 1 기준)를 벗어나면 측정 실패로 보고 footYOffset 만 쓴다.")]
    [SerializeField] private Vector2 ankleHeightRange = new Vector2(0.02f, 0.25f);
    [Header("가상 바닥")]
    [Tooltip("바닥 충돌체가 없거나(이 프로젝트의 정비소 씬은 바닥 콜라이더가 없다) 발 아래에서 못 찾을 때, 플레이어 루트 높이를 바닥으로 보고 발을 붙인다.\n"
             + "끄면 예전처럼 충돌체만 사용 -> 충돌체가 없을 때 무릎이 안 굽고 몸만 가라앉는다.")]
    [SerializeField] private bool useVirtualGround = true;
    [Tooltip("가상 바닥 사용 시: 레이가 맞은 면이 루트보다 이만큼(m, 스케일 곱) 이상 높으면 책상·보닛 같은 것으로 보고 무시")]
    [SerializeField] private float maxStepUp = 0.35f;

    [Tooltip("서 있을 때도 유지할 최소 접지 가중치(0이면 크라우치 때만 IK)")]
    [SerializeField] [Range(0f, 1f)] private float groundBaseWeight = 0f;
    [SerializeField] [Range(0f, 1f)] private float footRotationFactor = 0.5f;

    [Header("신발 묻힘 방지 (261111)")]
    [Tooltip("켜면 발 방향 목표를 '서 있을 때의 발 방향(아바타 기준) + 바닥 법선'으로 잡고 가중치를 그대로 쓴다(footRotationFactor 무시).\n"
             + "끄면 예전처럼 지금 애니메이션 발 방향을 footRotationFactor 비율로만 반영 -> 깊이 앉으면 발끝이 약 34° 숙여져 신발 앞이 바닥에 묻혔다.")]
    [SerializeField] private bool flatFoot = true;
    // 접지(IK) 가중치: 힙이 내려가기 시작하면(크라우치>0) 바로 1 로 올린다. 예전엔 크라우치 값(0..1)을 그대로 가중치로 써서
    // 반쯤 앉았을 때 발이 절반만 바닥에 붙어 바닥 아래로 가라앉았다(0.5 에서 신발이 약 28cm 묻힘).
    // 서 있을 때(크라우치 0)와 이어지도록 0..이 값 구간에서만 부드럽게 올린다. (직렬화 필드로 두면 프리팹에 옛 기본값이 굳어 코드 값이 안 먹는다 → 상수)
    private const float GroundFullAtCrouch = 0.02f;

    // 각 발의 '발 본 높이 - 아바타 루트 높이' (스케일 1 기준). 프리팹 기본 포즈(서 있는 자세)에서 Awake 때 잰다.
    private float _ankleL = -1f, _ankleR = -1f;
    // 서 있을 때 발 방향(아바타 기준)
    private Quaternion _restL = Quaternion.identity, _restR = Quaternion.identity;
    private bool _hasRestL, _hasRestR;

    private void Awake()
    {
        _hasRestL = MeasureRest(leftLeg, out _restL);
        _hasRestR = MeasureRest(rightLeg, out _restR);
        if (!autoAnkleHeight) return;
        _ankleL = MeasureAnkle(leftLeg);
        _ankleR = MeasureAnkle(rightLeg);
    }

    private bool MeasureRest(LimbIK leg, out Quaternion rest)
    {
        rest = Quaternion.identity;
        if (leg == null || leg.solver == null || leg.solver.bone3 == null || leg.solver.bone3.transform == null) return false;
        rest = Quaternion.Inverse(transform.rotation) * leg.solver.bone3.transform.rotation;
        return true;
    }

    private float MeasureAnkle(LimbIK leg)
    {
        if (leg == null || leg.solver == null || leg.solver.bone3 == null || leg.solver.bone3.transform == null) return -1f;
        float s = Mathf.Max(0.01f, transform.lossyScale.y);
        float h = (leg.solver.bone3.transform.position.y - transform.position.y) / s;
        return (h >= ankleHeightRange.x && h <= ankleHeightRange.y) ? h : -1f;
    }

    /// <summary>
    /// Animator 포즈가 적용된 뒤, LimbIK 솔버를 돌리기 직전에 호출.
    /// scale = 아바타 월드 스케일 (힙 낮춤·레이캐스트 거리를 이걸로 곱함).
    /// </summary>
    public void ApplyPreIK(float crouch01, float scale = 1f)
    {
        if (hips != null && (crouch01 > 0f || hipDrop == 0f))
            hips.position += Vector3.down * (crouch01 * hipDrop * scale);

        SolveFoot(leftLeg, crouch01, scale, _ankleL, _hasRestL, _restL);
        SolveFoot(rightLeg, crouch01, scale, _ankleR, _hasRestR, _restR);
    }

    private void SolveFoot(LimbIK leg, float crouch01, float scale, float ankle, bool hasRest, Quaternion rest)
    {
        if (leg == null) return;
        var s = leg.solver;
        if (s == null || s.bone3 == null || s.bone3.transform == null) return;

        Transform foot = s.bone3.transform;
        float up = footProbeUp * scale;
        float down = footProbeDown * scale;
        Vector3 origin = foot.position + Vector3.up * up;

        bool found = Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                up + down, groundMask, QueryTriggerInteraction.Ignore);

        // 충돌체가 없거나, 맞은 곳이 발보다 한참 위(차체·선반 등)면 루트 높이의 가상 바닥을 쓴다
        if (useVirtualGround && (!found || hit.point.y > transform.position.y + maxStepUp * scale))
        {
            hit.point = new Vector3(foot.position.x, transform.position.y, foot.position.z);
            hit.normal = Vector3.up;
            found = true;
        }

        if (found)
        {
            float w = Mathf.Max(Mathf.Clamp01(crouch01 / GroundFullAtCrouch), groundBaseWeight);
            // 발 본은 발목이므로 바닥 위 '발목 높이'에 둔다(측정 실패 시 예전처럼 footYOffset 만)
            float lift = ankle >= 0f ? ankle : footYOffset;
            s.IKPosition = hit.point + Vector3.up * (lift * scale);
            bool flat = flatFoot && hasRest;
            Quaternion baseRot = flat ? transform.rotation * rest : foot.rotation;
            s.IKRotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * baseRot;
            s.IKPositionWeight = w;
            s.IKRotationWeight = flat ? w : w * footRotationFactor;
        }
        else
        {
            s.IKPositionWeight = 0f;
            s.IKRotationWeight = 0f;
        }
    }
}
