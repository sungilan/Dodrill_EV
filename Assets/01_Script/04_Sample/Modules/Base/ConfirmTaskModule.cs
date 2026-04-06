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
//public abstract class ConfirmTaskModule : ITaskModule
//{
//    public abstract string ModuleId { get; }

//    private Action _onComplete;
//    private Action _onFail;

//    public virtual void OnStart(ModuleConfig config, Action onComplete, Action onFail)
//    {
//        _onComplete = onComplete;
//        _onFail = onFail;
//        InteractionEvents.OnTaskConfirmed += HandleConfirm;
//    }

//    private void HandleConfirm(string moduleId)
//    {
//        if(moduleId != ModuleId) return;
//        _onComplete?.Invoke();
//    }

//    public void OnUpdate(float deltaTime) { }

//    public void OnComplete()
//    {
//        InteractionEvents.OnTaskConfirmed -= HandleConfirm;
//    }

//    public void OnFail()
//    {
//        InteractionEvents.OnTaskConfirmed -= HandleConfirm;
//    }
//}
using System;
using UnityEngine;

public abstract class ConfirmTaskModule : ITaskModule
{
    public abstract string ModuleId { get; }

    protected string _targetObjName; // JSON의 config.targetObjName 저장
    private Action _onComplete;
    private Action _onFail;
    private bool _isCompleted = false;

    public virtual void OnStart(ModuleConfig config, Action onComplete, Action onFail)
    {
        _onComplete = onComplete;
        _onFail = onFail;
        _isCompleted = false;

        // JSON 설정에서 대상 오브젝트 이름을 가져옴 (예: "Conn_Front")
        _targetObjName = config?.targetObjName ?? string.Empty;

        // 1. UI 완료 버튼 이벤트 구독
        InteractionEvents.OnTaskConfirmed += HandleConfirm;

        // 2. ClickableAnimator 클릭(ZoneActivated) 이벤트 구독
        InteractionEvents.OnZoneActivated += HandleInteraction;

        Debug.Log($"[{ModuleId}] 시작 - 목표 오브젝트: {_targetObjName}");
    }

    // UI 버튼 전용 (ModuleId 기반)
    private void HandleConfirm(string moduleId)
    {
        if(_isCompleted || moduleId != ModuleId) return;
        Complete();
    }

    // 오브젝트 클릭 전용 (TargetObjName 기반)
    private void HandleInteraction(string id, string itemId)
    {
        if(_isCompleted) return;

        // ClickableAnimator가 보낸 uniqueId가 현재 모듈의 목표와 일치하면 완료
        if(!string.IsNullOrEmpty(_targetObjName) && id == _targetObjName)
        {
            Debug.Log($"[{ModuleId}] 대상 오브젝트({_targetObjName}) 클릭 확인됨. 단계를 완료합니다.");
            Complete();
        }
    }

    private void Complete()
    {
        _isCompleted = true;
        _onComplete?.Invoke();
        Cleanup();
    }

    public void OnUpdate(float deltaTime) { }

    public virtual void OnComplete() => Cleanup();
    public virtual void OnFail() => Cleanup();

    private void Cleanup()
    {
        InteractionEvents.OnTaskConfirmed -= HandleConfirm;
        InteractionEvents.OnZoneActivated -= HandleInteraction;
    }
}