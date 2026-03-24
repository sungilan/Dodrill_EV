using TMPro;
using UnityEngine;
using Debug = UnityEngine.Debug;
using UnityEngine.UI;

// ============================================================
//  ProgressHUD.cs
//  클라이언트 전용 — 현재 Task 진행 상태 오버레이 UI
//
//  표시 항목:
//    - 현재 Task 번호 / 전체 (예: 2 / 5)
//    - Task 이름 (LocalizationManager 에서 가져옴)
//    - 진행 상태 (Running / Completed / Failed)
//    - 경과 시간 / 타임아웃
//
//  ScenarioStateReceiver 이벤트 구독
// ============================================================

    public class ProgressHUD : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TextMeshProUGUI _taskIndexText;   // "2 / 5"
        [SerializeField] private TextMeshProUGUI _taskTitleText;   // Task 이름
        [SerializeField] private TextMeshProUGUI _statusText;      // Running / 완료 / 실패
        [SerializeField] private TextMeshProUGUI _timerText;       // 경과 / 타임아웃
        [SerializeField] private Image           _progressBar;     // 타임아웃 진행 바 (없으면 무시)
        [SerializeField] private GameObject      _hudRoot;         // 전체 패널

        private TaskState _current;
        private int       _totalCount;
        private bool      _isTracking;

        // ── 생명주기 ──────────────────────────

        private void OnEnable()
        {
            ScenarioStateReceiver.OnTaskStateUpdated += OnTaskStateUpdated;
            ScenarioStateReceiver.OnSnapshotReceived += OnSnapshotReceived;
        }

        private void OnDisable()
        {
            ScenarioStateReceiver.OnTaskStateUpdated -= OnTaskStateUpdated;
            ScenarioStateReceiver.OnSnapshotReceived -= OnSnapshotReceived;
        }

        private void Update()
        {
            if (!_isTracking || _current.status != TaskStatus.Running) return;

            // 타이머는 클라이언트에서 로컬로 증가 (서버 동기화 보조용)
            _current.elapsedTime += Time.deltaTime;
            RefreshTimer();
        }

        // ── 이벤트 수신 ───────────────────────

        private void OnTaskStateUpdated(TaskStateBroadcast broadcast)
        {
            _current    = broadcast.currentTask;
            _totalCount = broadcast.totalTaskCount;
            _isTracking = true;
            RefreshAll();
        }

        private void OnSnapshotReceived(ScenarioSnapshotBroadcast broadcast)
        {
            _current    = broadcast.currentTask;
            _totalCount = broadcast.totalTaskCount;
            _isTracking = true;
            RefreshAll();
        }

        // ── UI 갱신 ───────────────────────────

        private void RefreshAll()
        {
            if (_hudRoot != null)
                _hudRoot.SetActive(true);

            // "2 / 5"
            if (_taskIndexText != null)
                _taskIndexText.text = $"{_current.taskIndex + 1} / {_totalCount}";

            // Task 이름 — LocalizationManager 에서 가져옴
            // 키 컨벤션: "task.{moduleId}.title"
            // ScenarioData 에서 moduleId 를 알아야 하므로 ScenarioStateReceiver 참조 필요
            // 여기서는 taskIndex 기반으로 단순 표시 (추후 연동)
            if (_taskTitleText != null)
                _taskTitleText.text = LocalizationManager.Get(LocaleKeys.UiTaskTitle);

            RefreshStatus();
            RefreshTimer();
        }

        private void RefreshStatus()
        {
            if (_statusText == null) return;

            _statusText.text = _current.status switch
            {
                TaskStatus.Running   => LocalizationManager.Get(LocaleKeys.CommonRunning),
                TaskStatus.Completed => LocalizationManager.Get(LocaleKeys.CommonComplete),
                TaskStatus.Failed    => LocalizationManager.Get(LocaleKeys.CommonFailed),
                TaskStatus.Pending   => LocalizationManager.Get(LocaleKeys.CommonPending),
                _                   => "",
            };
        }

        private void RefreshTimer()
        {
            if (_timerText == null) return;

            if (_current.timeoutSeconds > 0f)
            {
                float remaining = Mathf.Max(0f, _current.timeoutSeconds - _current.elapsedTime);
                _timerText.text = $"{remaining:F1}s";

                if (_progressBar != null)
                    _progressBar.fillAmount = 1f - (remaining / _current.timeoutSeconds);
            }
            else
            {
                _timerText.text = $"{_current.elapsedTime:F1}s";

                if (_progressBar != null)
                    _progressBar.fillAmount = 0f;
            }
        }
    }