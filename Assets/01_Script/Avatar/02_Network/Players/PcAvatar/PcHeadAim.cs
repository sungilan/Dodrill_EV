using RootMotion.FinalIK;
using UnityEngine;

/// <summary>
/// 머리/상체가 시선을 따라가게 한다 (몸통은 이미 yaw로 시선 쪽을 향함 → 주로 상하 + 약간의 좌우 지연).
/// 자체 Update 없음 — PcAvatarIKDriver가 순서를 잡아 호출한다.
///
///  소유자 : 타깃 = 실제 카메라 forward 상의 먼 점
///  원격   : 타깃 = 동기화된 HeadPitch(Animator) + 몸통 yaw 로 복원
/// </summary>
public class PcHeadAim : MonoBehaviour
{
    [SerializeField] private LookAtIK lookAt;
    [Tooltip("소유자 시점 카메라(피벗 아래 Camera). 원격에서는 사용 안 함.")]
    [SerializeField] private Transform ownerCamera;

    [Header("가중치 (LookAtIK.solver)")]
    [SerializeField] [Range(0f, 1f)] private float weight = 1f;
    [SerializeField] [Range(0f, 1f)] private float bodyWeight = 0.35f;
    [SerializeField] [Range(0f, 1f)] private float headWeight = 1f;
    [SerializeField] [Range(0f, 1f)] private float eyesWeight = 0f;
    [SerializeField] [Range(0f, 1f)] private float clampWeight = 0.5f;

    [SerializeField] private float aimDistance = 10f;

    /// <summary>PcAvatarIKDriver.Start()에서 1회.</summary>
    public void Configure()
    {
        if (lookAt == null) return;
        var s = lookAt.solver;
        s.target = null;               // IKPosition 을 직접 쓴다
        s.bodyWeight = bodyWeight;
        s.headWeight = headWeight;
        s.eyesWeight = eyesWeight;
        s.clampWeight = clampWeight;
        s.IKPositionWeight = weight;
    }

    /// <summary>PcAvatarIKDriver.LateUpdate()에서 매 프레임, LookAtIK 풀기 직전.</summary>
    public void ComputeTarget(bool isOwner, float headPitchDeg, Quaternion bodyRotation)
    {
        if (lookAt == null) return;
        var s = lookAt.solver;

        Vector3 headPos = (s.head != null && s.head.transform != null)
            ? s.head.transform.position
            : transform.position;

        if (isOwner && ownerCamera != null)
        {
            s.IKPosition = ownerCamera.position + ownerCamera.forward * aimDistance;
        }
        else
        {
            Quaternion look = bodyRotation * Quaternion.Euler(headPitchDeg, 0f, 0f);
            s.IKPosition = headPos + look * Vector3.forward * aimDistance;
        }

        s.IKPositionWeight = weight;
    }
}
