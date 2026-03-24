using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  SequentialStepModule.cs
//  순서가 있는 다단계 Task를 처리하는 베이스 클래스
//
//  GrabZone 스텝: ScenarioRunner가 IStepAwareModule 인터페이스로 처리
//  GrabAll / GrabUse / Confirm 스텝: 모듈이 InteractionEvents로 내부 처리
//
//  사용법:
//    public class MyModule : SequentialStepModule
//    {
//        public override string ModuleId => "MyModule";
//        protected override List<StepConfig> Steps => new()
//        {
//            new StepConfig { type = StepType.GrabAll,  requiredItems = new[]{"ItemA"}, description = "A 집기" },
//            new StepConfig { type = StepType.GrabZone, requiredItems = new[]{"ItemA"}, zoneId = "ZoneB", description = "A → ZoneB" },
//        };
//    }
// ============================================================

public enum StepType { GrabAll, GrabZone, GrabUse, Confirm }

public class StepConfig
{
    public StepType  type;
    public string[]  requiredItems = Array.Empty<string>();
    public string    zoneId        = string.Empty;  // GrabZone 대상 존 ID
    public string    guideId       = string.Empty;  // 스냅 애니메이션 목표 (비어있으면 zoneId 사용)
    public string    description   = string.Empty;  // 디버그/UI 표시용
    public float     snapDuration  = 0.5f;          // DOTween 이동 시간
    public float     snapDelay     = 1.5f;          // 스냅 후 완료까지 대기 시간
}

// ──────────────────────────────────────────────────────────────
//  ScenarioRunner가 GrabZone 스텝 조건을 조회·진행하는 인터페이스
// ──────────────────────────────────────────────────────────────
public interface IStepAwareModule : ITaskModule
{
    bool          IsCurrentStepGrabZone    { get; }
    string        CurrentStepZoneId        { get; }
    List<string>  CurrentStepRequiredItems { get; }
    float         CurrentStepSnapDuration  { get; }
    float         CurrentStepSnapDelay     { get; }
    string        CurrentStepGuideId       { get; }
    int           CurrentStepIndex         { get; }
    int           TotalSteps               { get; }

    /// <summary>
    /// ScenarioRunner가 GrabZone 검증 완료 후 호출.
    /// true  → 마지막 스텝: ScenarioRunner가 Task를 완료
    /// false → 중간 스텝: ScenarioRunner가 잠금 해제 후 계속 대기
    /// </summary>
    bool AdvanceZoneStep();
}

// ──────────────────────────────────────────────────────────────
//  SequentialStepModule 베이스
// ──────────────────────────────────────────────────────────────
public abstract class SequentialStepModule : IStepAwareModule
{
    public abstract string ModuleId { get; }
    protected abstract List<StepConfig> Steps { get; }

    private int              _stepIndex;
    private Action           _onComplete;
    private Action           _onFail;
    private bool             _taskCompleted;
    private HashSet<string>  _remainingGrabItems = new();

    // ── IStepAwareModule ──────────────────────────────────────

    public int  CurrentStepIndex  => _stepIndex;
    public int  TotalSteps        => Steps?.Count ?? 0;

    public bool IsCurrentStepGrabZone =>
        _stepIndex < TotalSteps && Steps[_stepIndex].type == StepType.GrabZone;

    public string CurrentStepZoneId =>
        IsCurrentStepGrabZone ? Steps[_stepIndex].zoneId : null;

    public List<string> CurrentStepRequiredItems
    {
        get
        {
            if (_stepIndex >= TotalSteps) return new List<string>();
            var items = Steps[_stepIndex].requiredItems;
            return items != null ? new List<string>(items) : new List<string>();
        }
    }

    public float CurrentStepSnapDuration =>
        _stepIndex < TotalSteps ? Steps[_stepIndex].snapDuration : 0.5f;

    public float CurrentStepSnapDelay =>
        _stepIndex < TotalSteps ? Steps[_stepIndex].snapDelay : 1.5f;

    public string CurrentStepGuideId =>
        _stepIndex < TotalSteps ? Steps[_stepIndex].guideId : string.Empty;

    /// <summary>ScenarioRunner가 GrabZone 검증 완료 후 호출</summary>
    public bool AdvanceZoneStep()
    {
        var completedStep = Steps[_stepIndex];
#if UNITY_EDITOR
        Debug.Log($"[{ModuleId}] Step[{_stepIndex}] GrabZone 완료 — zone={completedStep.zoneId}");
#endif
        // 완료된 존 비활성화 (HOST 모드 대응)
        TaskInteractionZone.Find(completedStep.zoneId)?.Deactivate();

        _stepIndex++;

        if (_stepIndex >= TotalSteps)
        {
            _taskCompleted = true;
            return true; // 마지막 스텝 → Task 완료
        }

        ActivateCurrentStep();
        return false; // 다음 스텝 진행 중
    }

    // ── ITaskModule ───────────────────────────────────────────

    public void OnStart(ModuleConfig config, Action onComplete, Action onFail)
    {
        _stepIndex      = 0;
        _onComplete     = onComplete;
        _onFail         = onFail;
        _taskCompleted  = false;
        _remainingGrabItems.Clear();

        Debug.Log($"[{ModuleId}] 다단계 Task 시작 — 총 {TotalSteps}단계");
        ActivateCurrentStep();
    }

    public void OnUpdate(float deltaTime) { }

    public void OnComplete()
    {
        UnsubscribeAll();
        DeactivateCurrentZone();
    }

    public void OnFail()
    {
        _taskCompleted = false;
        UnsubscribeAll();
        DeactivateCurrentZone();
    }

    // ── 스텝 진행 ─────────────────────────────────────────────

    private void ActivateCurrentStep()
    {
        UnsubscribeAll();

        if (_stepIndex >= TotalSteps)
        {
#if UNITY_EDITOR
            Debug.Log($"[{ModuleId}] 모든 스텝 완료");
#endif
            _taskCompleted = true;
            _onComplete?.Invoke();
            return;
        }

        var step = Steps[_stepIndex];
#if UNITY_EDITOR
        Debug.Log($"[{ModuleId}] Step[{_stepIndex}/{TotalSteps - 1}] 활성화 — {step.type}" +
                  (!string.IsNullOrEmpty(step.description) ? $": {step.description}" : ""));
#endif

        switch (step.type)
        {
            case StepType.GrabAll:
                _remainingGrabItems = new HashSet<string>(step.requiredItems ?? Array.Empty<string>());
                if (_remainingGrabItems.Count == 0)
                {
                    Debug.LogWarning($"[{ModuleId}] Step[{_stepIndex}] GrabAll requiredItems 없음 → 스킵");
                    _stepIndex++;
                    ActivateCurrentStep();
                    return;
                }
                InteractionEvents.OnItemGrabbed += HandleItemGrabbed;
                break;

            case StepType.GrabUse:
                InteractionEvents.OnItemUsed += HandleItemUsed;
                break;

            case StepType.Confirm:
                InteractionEvents.OnTaskConfirmed += HandleTaskConfirmed;
                break;

            case StepType.GrabZone:
                // ScenarioRunner가 IStepAwareModule 인터페이스로 처리
                // HOST 모드를 위해 존 콜라이더 활성화
                TaskInteractionZone.Find(step.zoneId)?.Activate();
                break;
        }

        // ScenarioRunner에 스텝 인덱스 변경 알림 (TaskState 업데이트 + 클라이언트 브로드캐스트)
        InteractionEvents.FireStepAdvanced(_stepIndex, TotalSteps);
    }

    private void AdvanceNonZoneStep()
    {
        UnsubscribeAll();
        _stepIndex++;

        if (_stepIndex >= TotalSteps)
        {
            _taskCompleted = true;
            _onComplete?.Invoke();
        }
        else
        {
            ActivateCurrentStep();
        }
    }

    // ── 인터랙션 이벤트 핸들러 ────────────────────────────────

    private void HandleItemGrabbed(string prefabId)
    {
        if (_taskCompleted) return;
        if (_stepIndex >= TotalSteps || Steps[_stepIndex].type != StepType.GrabAll) return;
        if (!_remainingGrabItems.Remove(prefabId)) return;

#if UNITY_EDITOR
        Debug.Log($"[{ModuleId}] Step[{_stepIndex}] 집기: {prefabId} (남은 {_remainingGrabItems.Count}개)");
#endif

        if (_remainingGrabItems.Count == 0)
            AdvanceNonZoneStep();
    }

    private void HandleItemUsed(string prefabId)
    {
        if (_taskCompleted) return;
        if (_stepIndex >= TotalSteps || Steps[_stepIndex].type != StepType.GrabUse) return;

        var items = Steps[_stepIndex].requiredItems;
        bool valid = items == null || items.Length == 0 || Array.IndexOf(items, prefabId) >= 0;
        if (!valid) return;

#if UNITY_EDITOR
        Debug.Log($"[{ModuleId}] Step[{_stepIndex}] 사용: {prefabId}");
#endif
        AdvanceNonZoneStep();
    }

    private void HandleTaskConfirmed(string moduleId)
    {
        if (_taskCompleted) return;
        if (_stepIndex >= TotalSteps || Steps[_stepIndex].type != StepType.Confirm) return;

#if UNITY_EDITOR
        Debug.Log($"[{ModuleId}] Step[{_stepIndex}] Confirm");
#endif
        AdvanceNonZoneStep();
    }

    // ── 유틸리티 ──────────────────────────────────────────────

    private void UnsubscribeAll()
    {
        InteractionEvents.OnItemGrabbed   -= HandleItemGrabbed;
        InteractionEvents.OnItemUsed      -= HandleItemUsed;
        InteractionEvents.OnTaskConfirmed -= HandleTaskConfirmed;
    }

    private void DeactivateCurrentZone()
    {
        if (_stepIndex < TotalSteps && Steps[_stepIndex].type == StepType.GrabZone)
            TaskInteractionZone.Find(Steps[_stepIndex].zoneId)?.Deactivate();
    }
}
