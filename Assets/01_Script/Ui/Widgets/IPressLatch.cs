/// <summary>
/// 버튼을 '눌린 모습'으로 계속 유지한다(토글형 버튼 — 대기실 준비 완료 등).
/// 그래픽을 가진 버튼 컴포넌트(ClickButton, Content.Shell)가 구현하고, 상위 모듈을 모르는 화면(LobbyPanel)이 이 인터페이스로 부른다.
/// </summary>
public interface IPressLatch
{
    void SetLatched(bool on);
}
