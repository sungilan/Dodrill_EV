using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Debug = UnityEngine.Debug;

// ============================================================
//  DemoTaskUI.cs
//  데모 씬 전용 — 현재 Task 정보 표시 + 상호작용 버튼 제공
//
//  버튼 구성:
//    [완료 확인]  → Confirm 타입 태스크 완료 신호 전송
//    [강제 완료]  → 어떤 타입이든 즉시 완료 (디버그용)
//    [강제 실패]  → 실패 → 재도전 트리거
//    [◀] [▶]     → 태스크 이동 (점프)
// ============================================================

public class DemoTaskUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ScenarioRunner         _runner;
    [SerializeField] private ScenarioStateReceiver  _stateReceiver;

    [Header("UI - Info")]
    [SerializeField] private TMP_Text _taskIndexText;   // "3 / 35"
    [SerializeField] private TMP_Text _moduleIdText;    // "MeasurePulse"
    [SerializeField] private TMP_Text _statusText;      // "진행중 / 완료 / 실패"
    [SerializeField] private TMP_Text _typeHintText;    // "손목동맥에 시계를 가져다 대세요"

    [Header("UI - Buttons")]
    [SerializeField] private Button _confirmButton;     // Confirm 타입용
    [SerializeField] private Button _forceCompleteButton;
    [SerializeField] private Button _forceFailButton;
    [SerializeField] private Button _prevButton;
    [SerializeField] private Button _nextButton;

    // ── 모듈 타입별 힌트 ─────────────────────────────────
    private static readonly System.Collections.Generic.Dictionary<string, string> _hints
        = new()
    {
        { "HandHygiene",                    "손 소독제를 잡고 펌프(쥐기)하세요" },
        { "PatientIdentification",          "완료 버튼을 눌러 환자를 확인하세요" },
        { "PatientIdentification_NonVerbal","완료 버튼을 눌러 손목밴드를 확인하세요" },
        { "ExplainProcedure_BT",            "완료 버튼을 눌러 체온 측정 설명하세요" },
        { "MeasureBodyTemperature",         "체온계를 들고 환자 귀 존에 가져다 대세요" },
        { "ExplainProcedure_Pulse",         "완료 버튼을 눌러 맥박 측정 설명하세요" },
        { "MeasurePulse",                   "초침시계를 들고 손목 존에 가져다 대세요" },
        { "MeasureRespiration",             "초침시계를 들고 가슴 존에 가져다 대세요" },
        { "MeasureBloodPressure",           "혈압계를 들고 상박 존에 가져다 대세요" },
        { "MeasureBST",                     "채혈기 세트를 들고 손가락 존에 가져다 대세요" },
        { "AssessHypoglycemia",             "완료 버튼을 눌러 저혈당 사정하세요" },
        { "MeasureSpO2",                    "산소포화도계를 들고 손가락 존에 가져다 대세요" },
        { "CheckVitalSigns",                "완료 버튼을 눌러 활력징후를 확인하세요" },
        { "RecordVitalSigns",               "완료 버튼을 눌러 활력징후를 기록하세요" },
        { "PrepareEKG",                     "완료 버튼을 눌러 EKG 장비를 준비하세요" },
        { "AttachEKGElectrodes_LeadII",     "전극을 들고 가슴 존에 부착하세요" },
        { "ConfirmEKGReading",              "완료 버튼을 눌러 EKG 파형을 확인하세요" },
        { "VerifyMedicationOrder",          "투약 카드를 집어 처방을 확인하세요" },
        { "PrepareMedication",              "약물 카드와 약을 모두 집어 준비하세요" },
        { "PrepareMedication_IM",           "주사기·약물·증류수를 모두 집어 준비하세요" },
        { "ExplainMedication",              "완료 버튼을 눌러 투약 설명하세요" },
        { "AdministerSublingual",           "니트로글리세린을 들고 입 존에 가져다 대세요" },
        { "AdministerOral",                 "약과 물컵을 들고 입 존에 가져다 대세요" },
        { "SelectInjectionSite",            "빈손으로 주사 부위 존을 터치하세요" },
        { "DisinfectSite",                  "알코올 솜을 들고 주사 부위 존을 소독하세요" },
        { "AdministerIM",                   "주사기를 들고 주사 부위 존에 놓으세요" },
        { "PostInjectionCare",              "완료 버튼을 눌러 주사 후 처리하세요" },
        { "StopDMMedication",               "완료 버튼을 눌러 DM 약물을 중단하세요" },
        { "Start10DW_IV",                   "10% D/W를 들고 IV 포트 존에 연결하세요" },
        { "RecheckBST_30min",               "완료 버튼을 눌러 30분 후 혈당 재측정하세요" },
        { "AdjustIVRate",                   "완료 버튼을 눌러 IV 속도를 조절하세요" },
        { "CloseObservation",               "완료 버튼을 눌러 밀착 관찰을 시작하세요" },
        { "ObservePatient",                 "완료 버튼을 눌러 환자를 관찰하세요" },
        { "RecordMedication",               "완료 버튼을 눌러 투약을 기록하세요" },
        { "RecordResults",                  "완료 버튼을 눌러 측정 결과를 기록하세요" },
    };

    // ── 생명주기 ─────────────────────────────────────────

    private void OnEnable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated += HandleTaskStateUpdated;
        ScenarioStateReceiver.OnSnapshotReceived += HandleSnapshotReceived;
    }

    private void OnDisable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated -= HandleTaskStateUpdated;
        ScenarioStateReceiver.OnSnapshotReceived -= HandleSnapshotReceived;
    }

    private void Start()
    {
        _confirmButton?.onClick.AddListener(OnConfirmClicked);
        _forceCompleteButton?.onClick.AddListener(OnForceCompleteClicked);
        _forceFailButton?.onClick.AddListener(OnForceFailClicked);
        _prevButton?.onClick.AddListener(OnPrevClicked);
        _nextButton?.onClick.AddListener(OnNextClicked);

        RefreshUI();
    }

    // ── 이벤트 핸들러 ────────────────────────────────────

    private void HandleTaskStateUpdated(TaskStateBroadcast broadcast)
    {
        string moduleId = ResolveModuleId(broadcast.currentTask.taskIndex);
        UpdateTaskDisplay(broadcast.currentTask.taskIndex, moduleId, broadcast.totalTaskCount);

        string status = broadcast.currentTask.status switch
        {
            TaskStatus.Completed => "완료",
            TaskStatus.Failed    => "실패",
            TaskStatus.Running   => "진행중",
            _                    => "대기중",
        };
        UpdateStatusDisplay(status);
    }

    private void HandleSnapshotReceived(ScenarioSnapshotBroadcast broadcast)
    {
        string moduleId = broadcast.scenarioData?.scenario.tasks.Count > broadcast.currentTask.taskIndex
            ? broadcast.scenarioData.scenario.tasks[broadcast.currentTask.taskIndex].moduleId
            : "Unknown";

        UpdateTaskDisplay(broadcast.currentTask.taskIndex, moduleId, broadcast.totalTaskCount);
        UpdateStatusDisplay("진행중");
    }

    private void UpdateTaskDisplay(int index, string moduleId, int total)
    {
        if (_taskIndexText != null) _taskIndexText.text = $"{index + 1} / {total}";
        if (_moduleIdText  != null) _moduleIdText.text  = moduleId;
        if (_typeHintText  != null)
            _typeHintText.text = _hints.TryGetValue(moduleId, out var hint) ? hint : moduleId;
    }

    private void UpdateStatusDisplay(string status)
    {
        if (_statusText == null) return;
        _statusText.text  = status;
        _statusText.color = status switch
        {
            "완료" => Color.green,
            "실패" => Color.red,
            _      => Color.white,
        };
    }

    // ── 버튼 콜백 ────────────────────────────────────────

    private void OnConfirmClicked()
    {
        string moduleId = _runner != null ? _runner.CurrentModuleId : string.Empty;
        if (string.IsNullOrEmpty(moduleId)) return;

        Debug.Log($"[DemoTaskUI] Confirm: {moduleId}");
        InteractionEvents.FireTaskConfirmed(moduleId);
    }

    private void OnForceCompleteClicked()
    {
        _runner?.ForceCompleteCurrentTask();
    }

    private void OnForceFailClicked()
    {
        _runner?.ForceFailCurrentTask();
    }

    private void OnPrevClicked()
    {
        if (_runner == null) return;
        int prev = Mathf.Max(0, _runner.CurrentIndex - 1);
        _runner.JumpToTask(prev);
    }

    private void OnNextClicked()
    {
        if (_runner == null) return;
        _runner.JumpToTask(_runner.CurrentIndex + 1);
    }

    // ── 초기 상태 갱신 ───────────────────────────────────

    private void RefreshUI()
    {
        if (_runner == null) return;
        UpdateTaskDisplay(_runner.CurrentIndex, _runner.CurrentModuleId, _runner.TotalCount);
        UpdateStatusDisplay("대기중");
    }

    // ── 유틸 ─────────────────────────────────────────────

    private string ResolveModuleId(int taskIndex)
    {
        var scenario = _stateReceiver?.CurrentScenario;
        if (scenario != null && taskIndex < scenario.scenario.tasks.Count)
            return scenario.scenario.tasks[taskIndex].moduleId;

        // 폴백: ScenarioRunner에서 직접 참조
        if (_runner != null && taskIndex == _runner.CurrentIndex)
            return _runner.CurrentModuleId;

        return "Unknown";
    }
}
