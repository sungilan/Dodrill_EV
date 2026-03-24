using System.Collections.Generic;

// ============================================================
//  MultiStepModules.cs
//  다단계(Sequential) 간호 절차 모듈 모음
//
//  PDF 기반 절차:
//    경구투약 (베아제정) — 3차 시나리오 2
//    혈당 측정 (BST)    — 5차 시나리오
//    피하주사 (SC)      — 5차 시나리오
// ============================================================

// ──────────────────────────────────────────────────────────────
//  경구 투약 — 베아제정 (3단계)
//  Step 0: 약 집기 (GrabAll)
//  Step 1: 약 투여 — 환자 입에 (GrabZone)
//  Step 2: 물 투여 — 환자 입에 (GrabZone)
// ──────────────────────────────────────────────────────────────
public class AdministerOralSequenceModule : SequentialStepModule
{
    public override string ModuleId => "AdministerOralSequence";

    protected override List<StepConfig> Steps => new()
    {
        new StepConfig
        {
            type          = StepType.GrabAll,
            requiredItems = new[] { "Medication_Bearse" },
            description   = "약 집기",
        },
        new StepConfig
        {
            type          = StepType.GrabZone,
            requiredItems = new[] { "Medication_Bearse" },
            zoneId        = "PatientMouth",
            guideId       = "PatientMouth",
            description   = "약 투여",
        },
        new StepConfig
        {
            type          = StepType.GrabZone,
            requiredItems = new[] { "WaterCup" },
            zoneId        = "PatientMouth",
            guideId       = "PatientMouth",
            description   = "물 투여",
        },
    };
}

// ──────────────────────────────────────────────────────────────
//  혈당 측정 (BST) — 4단계
//  Step 0: 채혈기 + 채혈침 집기 (GrabAll)
//  Step 1: 시험지 집기 (GrabAll)
//  Step 2: 채혈 — 손가락에 (GrabZone)
//  Step 3: 혈액 시험지에 묻히기 (GrabZone)
// ──────────────────────────────────────────────────────────────
public class MeasureBSTSequenceModule : SequentialStepModule
{
    public override string ModuleId => "MeasureBSTSequence";

    protected override List<StepConfig> Steps => new()
    {
        new StepConfig
        {
            type          = StepType.GrabAll,
            requiredItems = new[] { "LancetDevice", "Lancet" },
            description   = "채혈기에 채혈침 삽입",
        },
        new StepConfig
        {
            type          = StepType.GrabAll,
            requiredItems = new[] { "TestStrip" },
            description   = "혈당기에 시험지 삽입",
        },
        new StepConfig
        {
            type          = StepType.GrabZone,
            requiredItems = new[] { "LancetDevice" },
            zoneId        = "PatientFinger",
            guideId       = "PatientFinger",
            description   = "채혈",
        },
        new StepConfig
        {
            type          = StepType.GrabZone,
            requiredItems = new[] { "TestStrip" },
            zoneId        = "BloodDrop",
            guideId       = "BloodDrop",
            description   = "시험지에 혈액 묻히기",
        },
    };
}

// ──────────────────────────────────────────────────────────────
//  피하주사 (SC) — 4단계
//  Step 0: 주사기 + 인슐린 준비 (GrabAll)
//  Step 1: 주사부위 소독 (GrabZone)
//  Step 2: 피하주사 시행 (GrabZone)
//  Step 3: 지혈 (GrabZone)
// ──────────────────────────────────────────────────────────────
public class SubcutaneousInjectionModule : SequentialStepModule
{
    public override string ModuleId => "SubcutaneousInjection";

    protected override List<StepConfig> Steps => new()
    {
        new StepConfig
        {
            type          = StepType.GrabAll,
            requiredItems = new[] { "Syringe", "Insulin" },
            description   = "주사기에 약물 준비",
        },
        new StepConfig
        {
            type          = StepType.GrabZone,
            requiredItems = new[] { "AlcoholSwab" },
            zoneId        = "InjectionSite",
            guideId       = "InjectionSite",
            description   = "주사부위 소독",
        },
        new StepConfig
        {
            type          = StepType.GrabZone,
            requiredItems = new[] { "Syringe" },
            zoneId        = "InjectionSite",
            guideId       = "InjectionSite",
            description   = "피하주사 시행",
        },
        new StepConfig
        {
            type          = StepType.GrabZone,
            requiredItems = new[] { "AlcoholSwab" },
            zoneId        = "InjectionSite",
            guideId       = "InjectionSite",
            description   = "지혈",
        },
    };
}
