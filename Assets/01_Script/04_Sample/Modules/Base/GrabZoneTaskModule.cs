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

    private string         _targetZoneId;
    private List<string>   _requiredItems;
    private Action         _onComplete;
    private Action         _onFail;

    // 존 활성화 여부 추적 (중복 호출 방지)
    private bool _completed;

    public void OnStart(ModuleConfig config, Action onComplete, Action onFail)
    {
        _targetZoneId  = config?.targetZoneId ?? string.Empty;
        _requiredItems = config?.requiredItems ?? new List<string>();
        _onComplete    = onComplete;
        _onFail        = onFail;
        _completed     = false;

        // 데디케이티드 서버에서는 존 활성화/이벤트 구독 스킵
        // → 서버는 OnReceiveZoneSignal로 완료 처리, 클라이언트/호스트만 로컬 물리 경로 사용
        bool isServerOnly = InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted;
        if (!isServerOnly)
        {
            var zone = TaskInteractionZone.Find(_targetZoneId);
            zone?.Activate();
            InteractionEvents.OnZoneActivated += HandleZoneActivated;
        }

        if (string.IsNullOrEmpty(_targetZoneId))
            Debug.LogWarning($"[{ModuleId}] targetZoneId가 비어있음 — GrabZoneTaskModule 의도적 사용인지 확인");
    }

    private void HandleZoneActivated(string zoneId, string itemId)
    {
        if (_completed) return;
        if (zoneId != _targetZoneId) return;

        bool validItem = _requiredItems.Count == 0       // 빈손 모드
                      || _requiredItems.Contains(itemId); // 필요 아이템 보유

        if (!validItem) return;

        _completed = true;
        _onComplete?.Invoke();
    }

    public void OnUpdate(float deltaTime) { }

    public void OnComplete()
    {
        bool isServerOnly = InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted;
        if (!isServerOnly)
        {
            InteractionEvents.OnZoneActivated -= HandleZoneActivated;
            TaskInteractionZone.Find(_targetZoneId)?.Deactivate();
        }
    }

    public void OnFail()
    {
        bool isServerOnly = InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted;
        if (!isServerOnly)
        {
            InteractionEvents.OnZoneActivated -= HandleZoneActivated;
            TaskInteractionZone.Find(_targetZoneId)?.Deactivate();
        }
    }
}
