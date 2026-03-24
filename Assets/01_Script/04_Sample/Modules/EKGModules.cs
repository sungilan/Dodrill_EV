// ============================================================
//  EKGModules.cs
//  EKG(심전도) 관련 모듈
// ============================================================

/// <summary>EKG 준비 — 장비 확인 (UI 확인)</summary>
public class PrepareEKGModule : ConfirmTaskModule
{
    public override string ModuleId => "PrepareEKG";
}

/// <summary>EKG 전극 부착 — 전극 → PatientChest 존</summary>
public class AttachEKGElectrodes_LeadIIModule : GrabZoneTaskModule
{
    public override string ModuleId => "AttachEKGElectrodes_LeadII";
}

/// <summary>EKG 파형 확인 (UI 확인)</summary>
public class ConfirmEKGReadingModule : ConfirmTaskModule
{
    public override string ModuleId => "ConfirmEKGReading";
}
