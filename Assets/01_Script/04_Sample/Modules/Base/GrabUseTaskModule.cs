using System;
using System.Collections.Generic;

/// <summary>
/// 특정 아이템을 잡고 Use(펌프/사용)하면 완료되는 Task 베이스
/// 주 용도: HandHygiene (손 소독제 펌프)
/// </summary>
public abstract class GrabUseTaskModule : ITaskModule
{
    public abstract string ModuleId { get; }

    private List<string> _requiredItems;
    private Action       _onComplete;
    private Action       _onFail;
    private bool         _completed;

    public void OnStart(ModuleConfig config, Action onComplete, Action onFail)
    {
        _requiredItems = config?.requiredItems ?? new List<string>();
        _onComplete    = onComplete;
        _onFail        = onFail;
        _completed     = false;

        InteractionEvents.OnItemUsed += HandleItemUsed;
    }

    private void HandleItemUsed(string prefabId)
    {
        if (_completed) return;

        bool valid = _requiredItems.Count == 0 || _requiredItems.Contains(prefabId);
        if (!valid) return;

        _completed = true;
        _onComplete?.Invoke();
    }

    public void OnUpdate(float deltaTime) { }

    public void OnComplete() => InteractionEvents.OnItemUsed -= HandleItemUsed;
    public void OnFail()     => InteractionEvents.OnItemUsed -= HandleItemUsed;
}
