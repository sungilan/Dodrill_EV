using System;
using System.Collections.Generic;
using FishNet;
using UnityEngine;

/// <summary>
/// 필요 아이템을 들고 targetZone에 가져오면 완료되는 Task 베이스
///
/// requiredItems가 비어있으면 빈손으로 존 터치 시 완료 (SelectInjectionSite 등)
/// requiredItems가 있으면 그 중 하나라도 존에 들어오면 완료
///
/// 사용법:
///   public class MeasurePulseModule : GrabZoneTaskModule
///   {
///       public override string ModuleId => "MeasurePulse";
///   }
/// </summary>
public abstract class GrabZoneTaskModule : ITaskModule
{
    public abstract string ModuleId { get; }

    protected string _targetZoneId; // 자식 접근을 위해 private -> protected
    private List<string> _requiredItems;
    private Action _onComplete;
    private Action _onFail;
    private bool _completed;

    public void OnStart(ModuleConfig config, Action onComplete, Action onFail)
    {
        _targetZoneId = config?.targetZoneId ?? string.Empty;
        _requiredItems = config?.requiredItems ?? new List<string>();
        _onComplete = onComplete;
        _onFail = onFail;
        _completed = false;

        bool isServerOnly = InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted;
        if(!isServerOnly)
        {
            var zone = TaskInteractionZone.Find(_targetZoneId);
            zone?.Activate();
            InteractionEvents.OnZoneActivated += HandleZoneActivated;
        }
    }

    private void HandleZoneActivated(string zoneId, string itemId)
    {
        if(_completed) return;
        if(zoneId != _targetZoneId) return;

        bool validItem = _requiredItems.Count == 0 || _requiredItems.Contains(itemId);
        if(!validItem) return;

        _completed = true;

        // ★ 자식 클래스에서 "FinalModel" 활성화 등 추가 로직을 실행할 수 있도록 Hook 호출
        OnModuleSuccess(itemId);

        _onComplete?.Invoke();
    }

    // ★ 자식에서 필요 시 오버라이드 (기본은 빈 함수)
    protected virtual void OnModuleSuccess(string itemId) { }

    public void OnUpdate(float deltaTime) { }

    public void OnComplete() => Cleanup();
    public void OnFail() => Cleanup();

    private void Cleanup()
    {
        bool isServerOnly = InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted;
        if(!isServerOnly)
        {
            InteractionEvents.OnZoneActivated -= HandleZoneActivated;
            TaskInteractionZone.Find(_targetZoneId)?.Deactivate();
        }
    }
}
