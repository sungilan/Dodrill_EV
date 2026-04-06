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

    protected string _targetZoneId;
    protected List<string> _requiredItems;
    protected Action _onComplete;
    protected Action _onFail;
    protected bool _completed;
    protected bool _inZone;          // 현재 도구가 존 안에 있는지
    protected string _itemInZone;      // 존에 들어온 아이템 ID

    public virtual void OnStart(ModuleConfig config, Action onComplete, Action onFail)
    {
        _targetZoneId = config?.targetZoneId ?? string.Empty;
        _requiredItems = config?.requiredItems ?? new List<string>();
        _onComplete = onComplete;
        _onFail = onFail;
        _completed = false;
        _inZone = false;
        _itemInZone = string.Empty;

        bool isServerOnly = InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted;
        if(!isServerOnly)
        {
            var zone = TaskInteractionZone.Find(_targetZoneId);
            zone?.Activate();

            InteractionEvents.OnZoneActivated += HandleZoneActivated;
            InteractionEvents.OnItemUsed += HandleItemUsed;
        }

        Debug.Log($"[{ModuleId}] 시작 — Zone:{_targetZoneId} / 도구:{string.Join(",", _requiredItems)}");
    }

    // 존 진입 — 아이템이 존에 들어오면 _inZone = true
    private void HandleZoneActivated(string zoneId, string itemId)
    {
        if(_completed) return;
        if(zoneId != _targetZoneId) return;

        bool validItem = _requiredItems.Count == 0 || _requiredItems.Contains(itemId);
        if(!validItem) return;

        _inZone = true;
        _itemInZone = itemId;
        Debug.Log($"[{ModuleId}] 존 진입: {itemId} — 이제 사용(Squeeze)하세요");

        // PC: 두 번째 클릭(Squeeze) 대기 중임을 AI 가이드에 알릴 수 있음
        OnZoneEntered(itemId);
    }

    // 존 안에서 아이템 사용 → 완료
    private void HandleItemUsed(string itemId)
    {
        if(_completed || !_inZone) return;

        // 존에 들어온 아이템과 동일한 아이템이어야 함
        bool validItem = _requiredItems.Count == 0
                      || _requiredItems.Contains(itemId);
        if(!validItem) return;

        if(!string.IsNullOrEmpty(_itemInZone) && _itemInZone != itemId) return;

        _completed = true;
        Debug.Log($"[{ModuleId}] 완료 — {itemId} 사용");
        _onComplete?.Invoke();
    }

    // 서브클래스에서 "존 진입 시 추가 동작" 재정의 가능
    protected virtual void OnZoneEntered(string itemId) { }

    public void OnUpdate(float deltaTime) { }

    public virtual void OnComplete() => Cleanup();
    public virtual void OnFail() => Cleanup();

    protected void Cleanup()
    {
        bool isServerOnly = InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted;
        if(!isServerOnly)
        {
            InteractionEvents.OnZoneActivated -= HandleZoneActivated;
            InteractionEvents.OnItemUsed -= HandleItemUsed;
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

    protected string _targetZoneId;    // 이동해야 할 작업 구역 (Inverter_Check_Point 등)
    protected string _targetObjName;   // 측정 대상 단자 ID (HV_Cable_Term 등)
    protected float _targetValue;      // 목표 측정값 (0V, 10MΩ 등)
    protected float _tolerance;        // 허용 오차 범위
    protected List<string> _requiredItems;
    protected Action _onComplete;
    protected Action _onFail;
    protected bool _completed;
    protected bool _inZone;

    public virtual void OnStart(ModuleConfig config, Action onComplete, Action onFail)
    {
        _targetZoneId = config?.targetZoneId ?? string.Empty;
        _targetObjName = config?.targetObjName ?? string.Empty; // JSON의 targetObjName 사용
        _targetValue = config?.targetValue ?? 0f;
        _tolerance = (config?.targetValue ?? 0f) * (config?.toleranceRatio ?? 0.05f);
        _requiredItems = config?.requiredItems ?? new List<string>();

        _onComplete = onComplete;
        _onFail = onFail;
        _completed = false;
        _inZone = false;

        bool isServerOnly = InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted;
        if(!isServerOnly)
        {
            // 1. 작업 구역 가이드 활성화
            var zone = TaskInteractionZone.Find(_targetZoneId);
            zone?.Activate();

            // 2. 이벤트 구독
            InteractionEvents.OnZoneActivated += HandleZoneActivated;
            // OnValueMeasured 구독 안 함 — ScenarioRunner가 직접 HandleValueMeasured() 호출
        }

        Debug.Log($"[{ModuleId}] 시작 — 구역:{_targetZoneId} / 단자:{_targetObjName} / 목표값:{_targetValue}");
    }

    // ── 구역 진입 판정 ──────────────────────────────────────────
    private void HandleZoneActivated(string zoneId, string itemId)
    {
        if(_completed) return;

        // 플레이어 또는 멀티미터가 작업 구역(Zone)에 들어왔는지 체크
        if(zoneId == _targetZoneId)
        {
            bool validItem = _requiredItems.Count == 0 || _requiredItems.Contains(itemId);
            if(validItem)
            {
                _inZone = true;
                Debug.Log($"[{ModuleId}] 작업 구역 진입 완료. 단자를 측정하세요.");
            }
        }
    }

    // ── 측정값 판정 (핵심 수정 부분) ─────────────────────────────
    /// <summary>
    /// ScenarioRunner.OnReceiveValueMeasured에서 직접 호출.
    /// 판정만 수행 — 완료 처리(딜레이)는 ScenarioRunner가 담당.
    /// </summary>
    public void HandleValueMeasured(string terminalId, float measuredValue)
    {
        if(_completed) return;

        // terminalId 매칭: targetObjName 또는 targetZoneId와 일치하면 OK
        bool terminalMatch = terminalId == _targetObjName
                          || terminalId == _targetZoneId
                          || (!string.IsNullOrEmpty(_targetObjName) && _targetObjName.Contains(terminalId))
                          || (!string.IsNullOrEmpty(_targetZoneId) && _targetZoneId.Contains(terminalId));
        if(!terminalMatch)
        {
            Debug.Log($"[{ModuleId}] 단자 불일치 — terminal={terminalId} / 기대={_targetObjName}|{_targetZoneId}");
            return;
        }

        // targetValue == 0 이면 5V 이하 = 방전완료 판정
        bool valueMatch;
        if(_targetValue == 0f)
            valueMatch = measuredValue <= 5f;
        else
            valueMatch = Mathf.Abs(measuredValue - _targetValue) <= _tolerance;

        Debug.Log($"[{ModuleId}] 값 비교 — 측정:{measuredValue:F2} / 목표:{_targetValue} / {(valueMatch ? "✅ 성공" : "❌ 실패")}");

        if(valueMatch)
            _completed = true;
        // ★ _onComplete() 호출 금지 — ScenarioRunner가 IsCompleted 체크 후 딜레이 처리
    }

    /// <summary>ScenarioRunner가 딜레이 처리 여부 판단에 사용</summary>
    public bool IsCompleted => _completed;
    protected virtual string GetTargetObjName() => string.Empty;

    public void OnUpdate(float deltaTime) { }

    public virtual void OnComplete() => Cleanup();
    public virtual void OnFail() => Cleanup();

    protected void Cleanup()
    {
        bool isServerOnly = InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted;
        if(!isServerOnly)
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