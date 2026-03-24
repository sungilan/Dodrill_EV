// ============================================================
//  HypoglycemiaTreatmentModules.cs
//  저혈당 처치 및 기록 모듈
// ============================================================

/// <summary>10% D/W IV 시작 — IV백 → IVPort 존</summary>
public class Start10DW_IVModule : GrabZoneTaskModule
{
    public override string ModuleId => "Start10DW_IV";
}

/// <summary>IV 속도 조절 (UI 확인)</summary>
public class AdjustIVRateModule : ConfirmTaskModule
{
    public override string ModuleId => "AdjustIVRate";
}

/// <summary>밀착 관찰 유지 (UI 확인)</summary>
public class CloseObservationModule : ConfirmTaskModule
{
    public override string ModuleId => "CloseObservation";
}

/// <summary>환자 관찰 (UI 확인)</summary>
public class ObservePatientModule : ConfirmTaskModule
{
    public override string ModuleId => "ObservePatient";
}

/// <summary>투약 기록 (UI 확인)</summary>
public class RecordMedicationModule : ConfirmTaskModule
{
    public override string ModuleId => "RecordMedication";
}

/// <summary>측정 결과 기록 (UI 확인)</summary>
public class RecordResultsModule : ConfirmTaskModule
{
    public override string ModuleId => "RecordResults";
}
