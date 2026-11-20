using UnityEngine;

/// <summary>
/// PC / 모바일 시점 컨트롤러(FreeLookController, FreeLockControllerMobile)가 공통으로 노출하는
/// "이번 프레임 의도값". PcMobileAvatarController가 이걸 읽어 아바타 애니메이터·IK를 구동한다.
/// </summary>
public interface IPcLookController
{
    /// <summary>이번 프레임 수평 이동 속도(월드 공간, m/s). 애니메이터 블렌드 트리 입력.</summary>
    Vector3 CurrentPlanarVelocity { get; }

    /// <summary>카메라 피치 각도(도). 위를 보면 음수/양수는 구현에 맞춤 — 원격 머리 재현에 사용.</summary>
    float CurrentPitch { get; }

    /// <summary>크라우치 입력 방향. +1 = 낮추기, -1 = 일어서기, 0 = 유지.</summary>
    float CrouchInput { get; }

    /// <summary>최대 이동 속도(m/s). 로코모션 블렌드 정규화 기준 — 이 속도에서 블렌드 값 1.0.</summary>
    float MoveSpeed { get; }
}
