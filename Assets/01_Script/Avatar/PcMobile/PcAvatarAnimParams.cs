using UnityEngine;

/// <summary>
/// Swat 로코모션 Animator Controller의 파라미터 이름/해시.
/// 컨트롤러 재구성 스크립트(Editor/PcAvatarRig)와 런타임이 같은 상수를 쓴다.
///
/// 전부 float. MoveX/MoveZ/Speed = 2D 블렌드 트리 입력. Crouch = 크라우치 0..1(IK가 소비).
/// HeadPitch = 어떤 상태도 쓰지 않는 "데이터 캐리어" — NetworkAnimator가 원격에 실어 나른다.
/// </summary>
public static class PcAvatarAnimParams
{
    public const string MoveX = "MoveX";
    public const string MoveZ = "MoveZ";
    public const string Speed = "Speed";
    public const string Crouch = "Crouch";
    public const string HeadPitch = "HeadPitch";

    public static readonly int MoveXHash = Animator.StringToHash(MoveX);
    public static readonly int MoveZHash = Animator.StringToHash(MoveZ);
    public static readonly int SpeedHash = Animator.StringToHash(Speed);
    public static readonly int CrouchHash = Animator.StringToHash(Crouch);
    public static readonly int HeadPitchHash = Animator.StringToHash(HeadPitch);
}
