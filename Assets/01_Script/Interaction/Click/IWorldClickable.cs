// ============================================================
//  IWorldClickable.cs — 눌러서 쓰는 월드 물체의 공통 인터페이스 (VehicleSync.cs 에서 Interaction 으로 이동, 11/20)
//  PC 클릭·모바일 터치·VR 손가락 찌르기의 대상. 컨트롤러가 이 인터페이스로 호버 이름·안내·클릭을 처리한다.
// ============================================================
/// <summary>눌러서 쓰는 월드 물체(문·덮개·경적 등). 컨트롤러가 호버 이름·안내·클릭을 이 인터페이스로 처리한다.</summary>
public interface IWorldClickable
{
    string DisplayName { get; }
    string HoverMessage(bool touch);
    void OnPCClick();
    /// <summary>VR 에서 손가락으로 찔러 누를 수 있는가(뚜껑처럼 돌려야 하는 물체는 false).</summary>
    bool VrPokeable { get; }
}
