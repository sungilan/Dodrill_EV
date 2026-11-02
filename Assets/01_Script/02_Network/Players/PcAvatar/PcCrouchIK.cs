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
    [SerializeField] private float footYOffset = 0.02f;
    [Tooltip("서 있을 때도 유지할 최소 접지 가중치(0이면 크라우치 때만 IK)")]
    [SerializeField] [Range(0f, 1f)] private float groundBaseWeight = 0f;
    [SerializeField] [Range(0f, 1f)] private float footRotationFactor = 0.5f;

    /// <summary>
    /// Animator 포즈가 적용된 뒤, LimbIK 솔버를 돌리기 직전에 호출.
    /// scale = 아바타 월드 스케일 (힙 낮춤·레이캐스트 거리를 이걸로 곱함).
    /// </summary>
    public void ApplyPreIK(float crouch01, float scale = 1f)
    {
        if (hips != null && (crouch01 > 0f || hipDrop == 0f))
            hips.position += Vector3.down * (crouch01 * hipDrop * scale);

        SolveFoot(leftLeg, crouch01, scale);
        SolveFoot(rightLeg, crouch01, scale);
    }

    private void SolveFoot(LimbIK leg, float crouch01, float scale)
    {
        if (leg == null) return;
        var s = leg.solver;
        if (s == null || s.bone3 == null || s.bone3.transform == null) return;

        Transform foot = s.bone3.transform;
        float up = footProbeUp * scale;
        float down = footProbeDown * scale;
        Vector3 origin = foot.position + Vector3.up * up;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                up + down, groundMask, QueryTriggerInteraction.Ignore))
        {
            float w = Mathf.Max(crouch01, groundBaseWeight);
            s.IKPosition = hit.point + Vector3.up * (footYOffset * scale);
            s.IKRotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * foot.rotation;
            s.IKPositionWeight = w;
            s.IKRotationWeight = w * footRotationFactor;
        }
        else
        {
            s.IKPositionWeight = 0f;
            s.IKRotationWeight = 0f;
        }
    }
}
