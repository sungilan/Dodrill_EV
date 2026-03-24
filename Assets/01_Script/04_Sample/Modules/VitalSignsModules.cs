// ============================================================
//  VitalSignsModules.cs
//  활력징후 측정 모듈
// ============================================================

/// <summary>고막 체온계 + 캡 → PatientEar 존으로 가져와서 측정</summary>
public class MeasureBodyTemperatureModule : GrabZoneTaskModule
{
    public override string ModuleId => "MeasureBodyTemperature";
}

/// <summary>초침시계 → PatientRadialArtery 존으로 가져와서 요골동맥 측정</summary>
public class MeasurePulseModule : GrabZoneTaskModule
{
    public override string ModuleId => "MeasurePulse";
}

/// <summary>초침시계 → PatientChest 존으로 가져와서 호흡 측정</summary>
public class MeasureRespirationModule : GrabZoneTaskModule
{
    public override string ModuleId => "MeasureRespiration";
}

/// <summary>혈압계 + 청진기 → PatientUpperArm 존에서 혈압 측정</summary>
public class MeasureBloodPressureModule : GrabZoneTaskModule
{
    public override string ModuleId => "MeasureBloodPressure";
}

/// <summary>활력징후 전체 확인 (UI 확인)</summary>
public class CheckVitalSignsModule : ConfirmTaskModule
{
    public override string ModuleId => "CheckVitalSigns";
}

/// <summary>활력징후 기록 (UI 확인)</summary>
public class RecordVitalSignsModule : ConfirmTaskModule
{
    public override string ModuleId => "RecordVitalSigns";
}
