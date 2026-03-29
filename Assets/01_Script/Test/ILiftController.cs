/// <summary>
/// 리프트 컨트롤러 공통 인터페이스.
/// VehicleLiftController, BatteryLiftController 모두 구현.
/// LiftButtonUI에서 타입 구분 없이 호출할 수 있도록.
/// </summary>
public interface ILiftController
{
    void OnUpButton();
    void OnDownButton();
    void OnStopButton();
}
