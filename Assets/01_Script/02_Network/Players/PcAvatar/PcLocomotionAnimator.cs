using UnityEngine;

/// <summary>
/// 소유자 전용: 이번 프레임 수평 속도를 몸 로컬 좌표로 바꿔 Animator 2D 블렌드 트리에 넣는다.
/// 원격은 NetworkAnimator가 같은 파라미터를 채우므로 이 컴포넌트가 계산할 게 없다.
///
/// 정규화 기준 속도는 시점 컨트롤러의 실제 이동 속도(IPcLookController.MoveSpeed)를 쓴다
/// → 컨트롤러 moveSpeed를 바꿔도 블렌드가 항상 맞는다. (인스펙터 walkSpeed는 폴백)
/// </summary>
public class PcLocomotionAnimator : MonoBehaviour
{
    [Tooltip("MoveSpeed를 못 받을 때만 쓰는 폴백 기준 속도.")]
    [SerializeField] private float walkSpeed = 5f;
    [Tooltip("SetFloat 댐핑 시간(초). 클수록 부드럽고 느리게 반응.")]
    [SerializeField] private float damp = 0.10f;
    [Tooltip("MoveX/MoveZ/Speed 클램프 상한 (달리기 링 = 2)")]
    [SerializeField] private float maxMagnitude = 2f;

    /// <summary>PcMobileAvatarController.Update(소유자)에서 매 프레임 호출. referenceSpeed = 컨트롤러 최대 이동 속도.</summary>
    public void TickOwner(Animator anim, Vector3 planarVelocityWorld, Transform bodyRef, float dt, float referenceSpeed)
    {
        float refSpd = referenceSpeed > 0.01f ? referenceSpeed : walkSpeed;
        Vector3 local = bodyRef.InverseTransformDirection(planarVelocityWorld);
        float inv = 1f / Mathf.Max(0.01f, refSpd);

        float x = Mathf.Clamp(local.x * inv, -maxMagnitude, maxMagnitude);
        float z = Mathf.Clamp(local.z * inv, -maxMagnitude, maxMagnitude);
        float speed = Mathf.Clamp(new Vector2(local.x, local.z).magnitude * inv, 0f, maxMagnitude);

        anim.SetFloat(PcAvatarAnimParams.MoveXHash, x, damp, dt);
        anim.SetFloat(PcAvatarAnimParams.MoveZHash, z, damp, dt);
        anim.SetFloat(PcAvatarAnimParams.SpeedHash, speed, damp, dt);
    }
}
