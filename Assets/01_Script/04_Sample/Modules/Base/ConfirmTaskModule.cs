using System;

/// <summary>
/// UI 완료 버튼으로 완료되는 Task의 베이스 클래스
///
/// 완료 방법:
///   TaskListPanel의 완료 버튼에서 InteractionEvents.FireTaskConfirmed(moduleId) 호출
///
/// 사용법:
///   public class MyModule : ConfirmTaskModule
///   {
///       public override string ModuleId => "MyModuleId";
///   }
/// </summary>
public abstract class ConfirmTaskModule : ITaskModule
{
    public abstract string ModuleId { get; }

    private Action _onComplete;
    private Action _onFail;

    public virtual void OnStart(ModuleConfig config, Action onComplete, Action onFail)
    {
        _onComplete = onComplete;
        _onFail = onFail;
        InteractionEvents.OnTaskConfirmed += HandleConfirm;
    }

    private void HandleConfirm(string moduleId)
    {
        if(moduleId != ModuleId) return;
        _onComplete?.Invoke();
    }

    public void OnUpdate(float deltaTime) { }

    public void OnComplete()
    {
        InteractionEvents.OnTaskConfirmed -= HandleConfirm;
    }

    public void OnFail()
    {
        InteractionEvents.OnTaskConfirmed -= HandleConfirm;
    }
}