using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  DTCData.cs
//  전기차 고장코드(DTC) 한 개에 해당하는 ScriptableObject.
//
//  기존 ScenarioData.json 과의 대응 관계:
//    ScenarioData.scenarioId  ↔  dtcCode
//    TaskDef.moduleId         ↔  RepairStep.targetObjName
//    ModuleConfig.extra       ↔  RepairStep.stepDescription / targetValue
//
//  새 시나리오 추가 방법:
//    Project 창 우클릭 → Create → EV_Repair → DTC Data
//    repairSteps 리스트에 단계를 순서대로 추가
//    TrainingFlowManager.scenarioDatabase 에 드래그
// ============================================================

// ──────────────────────────────────────────────────────────────
//  정비 행위 타입 (RepairActionType)
//  TrainingFlowManager 가 어떤 도구/행동을 기대하는지 정의
// ──────────────────────────────────────────────────────────────
public enum RepairActionType
{
    Swap,              // 부품 탈거 또는 교체 (InteractablePart 상태 전이)
    Disassemble,       // 볼트/커버 분해 (Bolt.isloosened 체크)
    MeasureVoltage,    // 전압 측정 (MultimeterMaster 연동)
    MeasureResistance, // 저항 측정 (MultimeterMaster 연동)
    Lubricate,         // 구리스/세척 도포 (GreaseTool 연동)
    RefillFluid,       // 냉각수/오일 보충 (FluidTool 연동)
    ScanDTC,           // 진단기 연결 및 고장코드 판독
}

// ──────────────────────────────────────────────────────────────
//  단일 정비 단계 (RepairStep)
// ──────────────────────────────────────────────────────────────
[System.Serializable]
public class RepairStep
{
    [Tooltip("화면에 표시될 작업 설명 (AI 가이드에도 사용)")]
    public string stepDescription;

    [Tooltip("이 단계에서 필요한 행동 타입")]
    public RepairActionType action;

    [Tooltip("전압/저항 측정 시 목표 수치 (오차 ±5% 허용). 탈거·도포는 0")]
    public float targetValue;

    [Tooltip("상호작용해야 할 씬 오브젝트 이름 (NetworkObjectFinder 키)")]
    public string targetObjName;

    [Tooltip("허용 오차 비율 (기본 0.05 = 5%). 측정 단계에만 적용")]
    [Range(0f, 0.5f)]
    public float toleranceRatio = 0.05f;

    [Tooltip("절연 장갑 + MSD 차단이 반드시 필요한 단계인지")]
    public bool requiresSafetyCheck = true;

    // 런타임 캐시 — DTCGenerator 또는 NetworkObjectFinder 가 할당
    [System.NonSerialized]
    public GameObject cachedTarget;

    /// <summary>측정값이 목표치 허용 범위 내인지 검사</summary>
    public bool IsValueValid(float measuredValue)
    {
        if(targetValue == 0f) return true; // 수치 검증 불필요한 단계
        float diff = Mathf.Abs(measuredValue - targetValue);
        return diff <= targetValue * toleranceRatio;
    }
}

// ──────────────────────────────────────────────────────────────
//  DTCData — 고장코드 한 개의 전체 정비 시나리오
// ──────────────────────────────────────────────────────────────
[CreateAssetMenu(fileName = "New DTCData", menuName = "EV_Repair/DTC Data")]
public class DTCData : ScriptableObject
{
    [Header("식별 정보")]
    [Tooltip("국제 표준 고장코드 (예: P0AA6)")]
    public string dtcCode;

    [Tooltip("유저에게 보여줄 고장 명칭")]
    public string faultName;

    [Tooltip("고장 부위 부품명 (NetworkObjectFinder 키 — 랜덤 고장 선택에 사용)")]
    public string partName;

    [Tooltip("유저에게 보여줄 고장 증상 설명")]
    [TextArea(2, 4)]
    public string symptomDescription;

    [Header("안전 설정")]
    [Tooltip("MSD 차단 + 절연 장갑 없이 작업 시작 불가 여부")]
    public bool requiresLOTO = true;

    [Tooltip("고전압 잔류 전압 대기 시간 (초). 0이면 즉시 통과")]
    public float dischargeWaitSeconds = 10f;

    [Header("정비 단계")]
    [Tooltip("순서대로 완료해야 하는 정비 단계 목록")]
    public List<RepairStep> repairSteps = new();

    // ── 유틸리티 ──────────────────────────────

    public int TotalSteps => repairSteps?.Count ?? 0;

    /// <summary>인덱스 범위 검사 후 RepairStep 반환</summary>
    public RepairStep GetStep(int index)
    {
        if(repairSteps == null || index < 0 || index >= repairSteps.Count)
            return null;
        return repairSteps[index];
    }
}