using UnityEngine;

// ============================================================
//  NursingModuleSet.cs — 간호술기 모듈 묶음을 시나리오 엔진(ScenarioHooks)에 등록 (모듈화 1단계, 2026-11-20)
//
//  예전에는 ScenarioBootstrapper.RegisterModules() 가 간호 모듈 35개를 직접 new 했다(엔진이 콘텐츠를 앎).
//  이제 이 파일이 시작할 때 한 번 등록한다. EV 프로젝트에서 간호 모듈이 필요 없으면 이 파일(과 Modules/*.cs)을 빼면 된다.
// ============================================================
public static class NursingModuleSet
{
    private const int Order = 0;   // EV(100)보다 먼저 — 예전 등록 순서 그대로

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install() => ScenarioHooks.AddModuleProvider("Nursing", Order, RegisterAll);

    public static void RegisterAll(ModuleRegistry Registry)
    {
        // ── 손 위생 ────────────────────────────────────────────────
        Registry.Register(new HandHygieneModule());

        // ── 환자 확인 / 시술 설명 ──────────────────────────────────
        Registry.Register(new PatientIdentificationModule());
        Registry.Register(new PatientIdentification_NonVerbalModule());
        Registry.Register(new ExplainProcedure_BTModule());
        Registry.Register(new ExplainProcedure_PulseModule());

        // ── 활력징후 측정 ──────────────────────────────────────────
        Registry.Register(new MeasureBodyTemperatureModule());
        Registry.Register(new MeasurePulseModule());
        Registry.Register(new MeasureRespirationModule());
        Registry.Register(new MeasureBloodPressureModule());
        Registry.Register(new CheckVitalSignsModule());
        Registry.Register(new RecordVitalSignsModule());

        // ── 혈당·SpO2 ──────────────────────────────────────────────
        Registry.Register(new MeasureBSTModule());
        Registry.Register(new MeasureSpO2Module());
        Registry.Register(new AssessHypoglycemiaModule());

        // ── EKG ────────────────────────────────────────────────────
        Registry.Register(new PrepareEKGModule());
        Registry.Register(new AttachEKGElectrodes_LeadIIModule());
        Registry.Register(new ConfirmEKGReadingModule());

        // ── 구강·설하 투약 ──────────────────────────────────────────
        Registry.Register(new VerifyMedicationOrderModule());
        Registry.Register(new PrepareMedicationModule());
        Registry.Register(new ExplainMedicationModule());
        Registry.Register(new AdministerSublingualModule());
        Registry.Register(new AdministerOralModule());

        // ── 근육주사 ────────────────────────────────────────────────
        Registry.Register(new PrepareMedication_IMModule());
        Registry.Register(new SelectInjectionSiteModule());
        Registry.Register(new DisinfectSiteModule());
        Registry.Register(new AdministerIMModule());
        Registry.Register(new PostInjectionCareModule());

        // ── 다단계 Sequential 모듈 ─────────────────────────────────
        Registry.Register(new AdministerOralSequenceModule());
        Registry.Register(new MeasureBSTSequenceModule());
        Registry.Register(new SubcutaneousInjectionModule());

        // ── 저혈당 처치 ─────────────────────────────────────────────
        Registry.Register(new StopDMMedicationModule());
        Registry.Register(new Start10DW_IVModule());
        Registry.Register(new RecheckBST_30minModule());
        Registry.Register(new AdjustIVRateModule());
        Registry.Register(new CloseObservationModule());
        Registry.Register(new ObservePatientModule());
        Registry.Register(new RecordMedicationModule());
        Registry.Register(new RecordResultsModule());
    }
}
