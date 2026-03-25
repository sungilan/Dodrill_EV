using System;
using System.Collections.Generic;
using FishNet;
using UnityEngine;

// ============================================================
//  EVBaseModules.cs
//  기존 ScenarioRunner + InteractionEvents 구조를 유지하면서
//  "이동 + 행동"을 처리하는 EV 전용 모듈 베이스들.
//
//  ┌─────────────────────────────────────────────────────┐
//  │  TrainingFlowManager 교체 없이 사용 가능             │
//  │  InteractionEvents.FireZoneActivated /               │
//  │  FireItemUsed 를 그대로 활용                         │
//  └─────────────────────────────────────────────────────┘
//
//  포함된 베이스:
//    ZoneAndUseModule  — 존 진입 후 Squeeze(사용)로 완료
//    ZoneAndMeasureModule — 존 진입 후 멀티테스터 측정으로 완료
//    ZoneAndFillModule — 존 진입 후 도포량 100% 달성으로 완료
// ============================================================


// ──────────────────────────────────────────────────────────────
//  ZoneAndUseModule
//  "아이템을 들고 존에 진입 → 그 안에서 Squeeze(사용)"
//
//  흐름:
//    1. requiredItems 중 하나가 targetZone에 진입 → _inZone = true
//    2. 같은 아이템을 Squeeze → FireItemUsed 발행
//    3. _inZone 상태에서 Use → 완료
//
//  사용 예:
//    public class Voltage_VerificationModule : ZoneAndUseModule
//    {
//        public override string ModuleId => "Voltage_Verification";
//    }
// ──────────────────────────────────────────────────────────────
public abstract class ZoneAndUseModule : ITaskModule
{
    public abstract string ModuleId { get; }

    protected string       _targetZoneId;
    protected List<string> _requiredItems;
    protected Action       _onComplete;
    protected Action       _onFail;
    protected bool         _completed;
    protected bool         _inZone;          // 현재 도구가 존 안에 있는지
    protected string       _itemInZone;      // 존에 들어온 아이템 ID

    public virtual void OnStart(ModuleConfig config, Action onComplete, Action onFail)
    {
        _targetZoneId  = config?.targetZoneId ?? string.Empty;
        _requiredItems = config?.requiredItems ?? new List<string>();
        _onComplete    = onComplete;
        _onFail        = onFail;
        _completed     = false;
        _inZone        = false;
        _itemInZone    = string.Empty;

        bool isServerOnly = InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted;
        if (!isServerOnly)
        {
            var zone = TaskInteractionZone.Find(_targetZoneId);
            zone?.Activate();

            InteractionEvents.OnZoneActivated += HandleZoneActivated;
            InteractionEvents.OnItemUsed      += HandleItemUsed;
        }

        Debug.Log($"[{ModuleId}] 시작 — Zone:{_targetZoneId} / 도구:{string.Join(",", _requiredItems)}");
    }

    // 존 진입 — 아이템이 존에 들어오면 _inZone = true
    private void HandleZoneActivated(string zoneId, string itemId)
    {
        if (_completed) return;
        if (zoneId != _targetZoneId) return;

        bool validItem = _requiredItems.Count == 0 || _requiredItems.Contains(itemId);
        if (!validItem) return;

        _inZone     = true;
        _itemInZone = itemId;
        Debug.Log($"[{ModuleId}] 존 진입: {itemId} — 이제 사용(Squeeze)하세요");

        // PC: 두 번째 클릭(Squeeze) 대기 중임을 AI 가이드에 알릴 수 있음
        OnZoneEntered(itemId);
    }

    // 존 안에서 아이템 사용 → 완료
    private void HandleItemUsed(string itemId)
    {
        if (_completed || !_inZone) return;

        // 존에 들어온 아이템과 동일한 아이템이어야 함
        bool validItem = _requiredItems.Count == 0
                      || _requiredItems.Contains(itemId);
        if (!validItem) return;

        if (!string.IsNullOrEmpty(_itemInZone) && _itemInZone != itemId) return;

        _completed = true;
        Debug.Log($"[{ModuleId}] 완료 — {itemId} 사용");
        _onComplete?.Invoke();
    }

    // 서브클래스에서 "존 진입 시 추가 동작" 재정의 가능
    protected virtual void OnZoneEntered(string itemId) { }

    public void OnUpdate(float deltaTime) { }

    public virtual void OnComplete() => Cleanup();
    public virtual void OnFail()    => Cleanup();

    protected void Cleanup()
    {
        bool isServerOnly = InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted;
        if (!isServerOnly)
        {
            InteractionEvents.OnZoneActivated -= HandleZoneActivated;
            InteractionEvents.OnItemUsed      -= HandleItemUsed;
            TaskInteractionZone.Find(_targetZoneId)?.Deactivate();
        }
    }
}


// ──────────────────────────────────────────────────────────────
//  ZoneAndMeasureModule
//  "도구를 들고 존에 진입 → 멀티테스터로 측정값 확인 → 완료"
//
//  흐름:
//    1. requiredItems(TwoPoleTester/MultimeterKit)가 존 진입
//    2. MeasurementPoint 단자 접촉 → MultimeterMaster.EvaluateConnection()
//    3. InteractionEvents.FireZoneActivated(targetObjName, ...) 발행
//    4. targetObjName 일치 + 존 안에 있으면 완료
//
//  사용 예:
//    public class Voltage_VerificationModule : ZoneAndMeasureModule
//    {
//        public override string ModuleId => "Voltage_Verification";
//    }
// ──────────────────────────────────────────────────────────────
public abstract class ZoneAndMeasureModule : ITaskModule
{
    public abstract string ModuleId { get; }

    protected string       _targetZoneId;   // 이동해야 할 존
    protected string       _targetObjName;  // 측정해야 할 단자 이름 (extra에서 읽거나 서브클래스 오버라이드)
    protected List<string> _requiredItems;
    protected Action       _onComplete;
    protected Action       _onFail;
    protected bool         _completed;
    protected bool         _inZone;

    public virtual void OnStart(ModuleConfig config, Action onComplete, Action onFail)
    {
        _targetZoneId  = config?.targetZoneId ?? string.Empty;
        _targetObjName = config?.GetExtraRaw("targetObjName") ?? string.Empty;
        _requiredItems = config?.requiredItems ?? new List<string>();
        _onComplete    = onComplete;
        _onFail        = onFail;
        _completed     = false;
        _inZone        = false;

        // targetObjName이 extra에 없으면 서브클래스에서 직접 지정
        if (string.IsNullOrEmpty(_targetObjName))
            _targetObjName = GetTargetObjName();

        bool isServerOnly = InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted;
        if (!isServerOnly)
        {
            var zone = TaskInteractionZone.Find(_targetZoneId);
            zone?.Activate();

            InteractionEvents.OnZoneActivated += HandleZoneActivated;
        }

        Debug.Log($"[{ModuleId}] 시작 — Zone:{_targetZoneId} / 측정단자:{_targetObjName}");
    }

    private void HandleZoneActivated(string zoneId, string itemId)
    {
        if (_completed) return;

        // 1. 존 진입 감지
        if (zoneId == _targetZoneId)
        {
            bool validItem = _requiredItems.Count == 0 || _requiredItems.Contains(itemId);
            if (validItem)
            {
                _inZone = true;
                Debug.Log($"[{ModuleId}] 존 진입 — 이제 단자를 측정하세요 ({_targetObjName})");
                return;
            }
        }

        // 2. 측정 완료 감지 (MultimeterMaster가 FireZoneActivated(targetObjName, ...) 발행)
        if (zoneId == _targetObjName || itemId == _targetObjName)
        {
            // 존에 진입한 상태여야 함
            // (존 없는 경우 _targetZoneId가 비어있으면 바로 통과)
            bool zoneOk = _inZone || string.IsNullOrEmpty(_targetZoneId);
            if (!zoneOk) return;

            _completed = true;
            Debug.Log($"[{ModuleId}] 측정 완료 — {_targetObjName}");
            _onComplete?.Invoke();
        }
    }

    /// <summary>
    /// targetObjName을 extra 대신 서브클래스에서 직접 지정할 때 오버라이드
    /// </summary>
    protected virtual string GetTargetObjName() => string.Empty;

    public void OnUpdate(float deltaTime) { }

    public virtual void OnComplete() => Cleanup();
    public virtual void OnFail()    => Cleanup();

    protected void Cleanup()
    {
        bool isServerOnly = InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted;
        if (!isServerOnly)
        {
            InteractionEvents.OnZoneActivated -= HandleZoneActivated;
            TaskInteractionZone.Find(_targetZoneId)?.Deactivate();
        }
    }
}


// ──────────────────────────────────────────────────────────────
//  ZoneAndFillModule
//  "도구를 들고 존에 진입 → 에어건/구리스로 도포 100% → 완료"
//
//  흐름:
//    1. requiredItems(AirGun)가 존 진입
//    2. GreaseTool이 GreaseTarget 도포 완료 시
//       InteractionEvents.FireZoneActivated(targetObjName, "AirGun") 발행
//    3. 완료
//
//  사용 예:
//    public class Contamination_CleanModule : ZoneAndFillModule
//    {
//        public override string ModuleId => "Contamination_Clean";
//        protected override string GetTargetObjName() => "Busbar_A1";
//    }
// ──────────────────────────────────────────────────────────────
public abstract class ZoneAndFillModule : ZoneAndMeasureModule
{
    // ZoneAndMeasureModule과 동일 로직 — GreaseTool도 FireZoneActivated를 발행하므로 그대로 동작
    // 서브클래스에서 GetTargetObjName()만 오버라이드
}
