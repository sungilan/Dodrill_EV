using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 필요 아이템을 모두 집으면 완료되는 Task 베이스
/// 주 용도: PrepareMedication (약물 준비), VerifyMedicationOrder 등
/// </summary>
public abstract class GrabAllItemsModule : ITaskModule
{
    public abstract string ModuleId { get; }

    private HashSet<string> _remainingItems;
    private Action          _onComplete;
    private Action          _onFail;
    private bool            _completed;

    public void OnStart(ModuleConfig config, Action onComplete, Action onFail)
    {
        _remainingItems = new HashSet<string>(config?.requiredItems ?? new List<string>());
        _onComplete     = onComplete;
        _onFail         = onFail;
        _completed      = false;

        if (_remainingItems.Count == 0)
        {
            Debug.LogWarning($"[{ModuleId}] requiredItems가 비어있음 — 즉시 완료 처리");
            onComplete?.Invoke();
            return;
        }

        InteractionEvents.OnItemGrabbed += HandleItemGrabbed;
    }

    private void HandleItemGrabbed(string prefabId)
    {
        if (_completed) return;
        if (!_remainingItems.Remove(prefabId)) return;

        Debug.Log($"[{ModuleId}] 아이템 획득: {prefabId} (남은 항목: {_remainingItems.Count})");

        if (_remainingItems.Count == 0)
        {
            _completed = true;
            _onComplete?.Invoke();
        }
    }

    public void OnUpdate(float deltaTime) { }

    public void OnComplete() => InteractionEvents.OnItemGrabbed -= HandleItemGrabbed;
    public void OnFail()     => InteractionEvents.OnItemGrabbed -= HandleItemGrabbed;
}
