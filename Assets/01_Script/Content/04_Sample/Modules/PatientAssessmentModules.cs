// ============================================================
//  PatientAssessmentModules.cs
//  환자 확인 및 시술 설명 관련 모듈
// ============================================================

/// <summary>개방형 질문으로 환자 이름·나이·주소지 확인</summary>
public class PatientIdentificationModule : ConfirmTaskModule
{
    public override string ModuleId => "PatientIdentification";
}

/// <summary>의사소통 불가 환자 — 손목밴드 및 보호자 확인</summary>
public class PatientIdentification_NonVerbalModule : ConfirmTaskModule
{
    public override string ModuleId => "PatientIdentification_NonVerbal";
}

/// <summary>체온 측정 전 목적·방법 설명</summary>
public class ExplainProcedure_BTModule : ConfirmTaskModule
{
    public override string ModuleId => "ExplainProcedure_BT";
}

/// <summary>맥박 측정 전 목적·방법 설명</summary>
public class ExplainProcedure_PulseModule : ConfirmTaskModule
{
    public override string ModuleId => "ExplainProcedure_Pulse";
}
