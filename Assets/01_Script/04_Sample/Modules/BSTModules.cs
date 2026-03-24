// ============================================================
//  BSTModules.cs
//  혈당 측정 및 저혈당 관련 모듈
// ============================================================

/// <summary>혈당 측정 — 채혈기 등 도구 → PatientFinger 존</summary>
public class MeasureBSTModule : GrabZoneTaskModule
{
    public override string ModuleId => "MeasureBST";
}

/// <summary>SpO2 측정 — 산소포화도계 → PatientFinger 존</summary>
public class MeasureSpO2Module : GrabZoneTaskModule
{
    public override string ModuleId => "MeasureSpO2";
}

/// <summary>저혈당 사정 (UI 확인)</summary>
public class AssessHypoglycemiaModule : ConfirmTaskModule
{
    public override string ModuleId => "AssessHypoglycemia";
}

/// <summary>DM 약물 투여 중단 (UI 확인)</summary>
public class StopDMMedicationModule : ConfirmTaskModule
{
    public override string ModuleId => "StopDMMedication";
}

/// <summary>30분 후 혈당 재측정 (UI 확인)</summary>
public class RecheckBST_30minModule : ConfirmTaskModule
{
    public override string ModuleId => "RecheckBST_30min";
}
