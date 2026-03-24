using UnityEngine;
using Debug = UnityEngine.Debug;

// ============================================================
//  ScenarioBootstrapper.cs
//  씬의 빈 GameObject 에 붙여서 사용
//  모든 ITaskModule 구현체를 여기서 등록
//
//  새 모듈 추가 시 RegisterModules() 에 한 줄만 추가하면 됨
// ============================================================

    public class ScenarioBootstrapper : MonoBehaviour
    {
        public ModuleRegistry Registry { get; private set; }

        private void Awake()
        {
            Registry = new ModuleRegistry();
            RegisterModules();
        }

        // ──────────────────────────────────────
        //  모듈 등록 목록
        //  새 콘텐츠 추가 시 여기에 한 줄만 추가
        // ──────────────────────────────────────
        private void RegisterModules()
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

            Debug.Log($"[ScenarioBootstrapper] 총 {Registry.Count}개 모듈 등록 완료");
        }
    }