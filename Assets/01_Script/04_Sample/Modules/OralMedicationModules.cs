// ============================================================
//  OralMedicationModules.cs
//  경구/설하 투약 관련 모듈
// ============================================================

/// <summary>투약 처방 확인 — 투약 카드 집기</summary>
public class VerifyMedicationOrderModule : GrabAllItemsModule
{
    public override string ModuleId => "VerifyMedicationOrder";
}

/// <summary>구강 투약 준비 — 약물 + 투약 카드 모두 집기</summary>
public class PrepareMedicationModule : GrabAllItemsModule
{
    public override string ModuleId => "PrepareMedication";
}

/// <summary>투약 목적·방법 설명 (UI 확인)</summary>
public class ExplainMedicationModule : ConfirmTaskModule
{
    public override string ModuleId => "ExplainMedication";
}

/// <summary>설하 투여 — 니트로글리세린 → PatientMouth 존</summary>
public class AdministerSublingualModule : GrabZoneTaskModule
{
    public override string ModuleId => "AdministerSublingual";
}

/// <summary>경구 투여 — 약물 + 물컵 → PatientMouth 존</summary>
public class AdministerOralModule : GrabZoneTaskModule
{
    public override string ModuleId => "AdministerOral";
}
