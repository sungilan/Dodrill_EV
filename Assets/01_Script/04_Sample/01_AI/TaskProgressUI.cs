using DG.Tweening;
using FishNet;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.UI;
using static ScenarioRunner;

namespace DoDrill.Training
{
    // ============================================================
    //  TaskProgressUI.cs
    //  전 플랫폼 Task 진행 UI (VR HUD / PC / Mobile 공용)
    //
    //  역할:
    //    1. 현재 Task 설명 표시 (task index, moduleId, description)
    //    2. "완료" 버튼 → InteractionEvents.FireTaskConfirmed 발행
    //       → NetworkInteractionBridge가 TaskConfirmSignal로 서버 전달
    //    3. 모든 모듈 타입의 폴백 완료 경로 제공
    //
    //  ConfirmTaskModule:    버튼 클릭 → FireTaskConfirmed(moduleId) → 정상 경로
    //  GrabZone/Use/All:     VR은 물리 인터랙션 우선, 실패 시 버튼 폴백
    //
    //  배치: Canvas(World Space 또는 Screen Space) 하위에 이 컴포넌트 부착
    //        Inspector에서 UI 요소 연결 필요
    // ============================================================

    public class TaskProgressUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject _panel;
        [SerializeField] private TMP_Text   _progressText;    // "1 / 8"
        [SerializeField] private TMP_Text   _moduleText;      // "HandHygiene"
        [SerializeField] private TMP_Text   _descriptionText; // extra.description
        [SerializeField] private TMP_Text   _statusText;      // Running / Failed
        [SerializeField] private Button     _confirmButton;   // 완료 버튼
        [SerializeField] private TMP_Text   _confirmBtnLabel; // 버튼 텍스트
        [SerializeField] private TMP_Text _boltProgressText;

        [Header("Notification UI (New)")]
        [SerializeField] private CanvasGroup _notifCanvasGroup; // 알림 패널의 CanvasGroup
        [SerializeField] private TMP_Text _notifText;          // 알림 텍스트 메쉬
        [SerializeField] private float _displayDuration = 3f;  // 표시 시간

        [Header("Localization Settings")]
        [SerializeField] private UnityEngine.Localization.LocalizedString _lsRunning;
        [SerializeField] private UnityEngine.Localization.LocalizedString _lsFailed;
        [SerializeField] private UnityEngine.Localization.LocalizedString _lsCompleted;
        [SerializeField] private UnityEngine.Localization.LocalizedString _lsRetrying;

        private ScenarioData _scenarioData;
        private int          _currentTaskIndex = -1;
        private int          _totalCount;
        private string       _currentModuleId  = string.Empty;
        private bool         _confirmAllowed;

        // ScenarioData.modules[].config.actionType 값 — UI 확인으로 끝나는 단계
        private const string ConfirmActionType = "ConfirmTask";

        // ── 생명주기 ──────────────────────────────────────────────

        // ── 텍스트 넘침 방지 ──────────────────────────────────────

        /// <summary>글이 길어져도 박스 밖으로 튀어나오지 않도록 자동 크기 조절 + 말줄임.</summary>
        private static void FitText(TMP_Text t, float minRatio)
        {
            if (t == null) return;
            float max = t.fontSize;
            t.enableAutoSizing = true;
            t.fontSizeMax = max;
            t.fontSizeMin = Mathf.Max(8f, max * minRatio);
            t.overflowMode = TextOverflowModes.Ellipsis;
        }

        /// <summary>제목이 없을 때의 대비책: "Front_Cover_Removal" → "Front Cover Removal".</summary>
        private static string PrettifyId(string id)
            => string.IsNullOrEmpty(id) ? string.Empty : id.Replace('_', ' ');

        private void Awake()
        {
            FitText(_descriptionText, 0.6f);
            FitText(_moduleText, 0.7f);
            FitText(_boltProgressText, 0.7f);

            // 시작 시 알림창을 확실히 숨김
            if(_notifCanvasGroup != null)
            {
                _notifCanvasGroup.alpha = 0f;
                _notifCanvasGroup.interactable = false;
                _notifCanvasGroup.blocksRaycasts = false;
            }
        }

        private void Start()
        {
            if (_scenarioData != null) return;

            var sr = FindFirstObjectByType<ScenarioStateReceiver>();
            if (sr?.CurrentScenario != null)
            {
                _scenarioData = sr.CurrentScenario;
                _totalCount   = sr.TotalTaskCount;
                Refresh(sr.CurrentTaskState);
                return;
            }

            var receiver = FindFirstObjectByType<ScenarioReceiver>();
            if (receiver?.ReceivedData != null)
                _scenarioData = receiver.ReceivedData;
        }

        private void OnEnable()
        {
            ScenarioStateReceiver.OnTaskStateUpdated += OnTaskStateUpdated;
            ScenarioStateReceiver.OnSnapshotReceived += OnSnapshotReceived;
            ScenarioReceiver.OnScenarioReceived      += OnScenarioReceived;
            InstanceFinder.ClientManager?.RegisterBroadcast<BoltProgressBroadcast>(OnBoltProgressReceived);
            InstanceFinder.ClientManager?.RegisterBroadcast<TaskNotificationBroadcast>(OnTaskNotificationReceived);

            if (_confirmButton != null)
                _confirmButton.onClick.AddListener(OnConfirmClicked);
        }

        private void OnDisable()
        {
            ScenarioStateReceiver.OnTaskStateUpdated -= OnTaskStateUpdated;
            ScenarioStateReceiver.OnSnapshotReceived -= OnSnapshotReceived;
            ScenarioReceiver.OnScenarioReceived      -= OnScenarioReceived;
            InstanceFinder.ClientManager?.UnregisterBroadcast<BoltProgressBroadcast>(OnBoltProgressReceived);
            InstanceFinder.ClientManager?.UnregisterBroadcast<TaskNotificationBroadcast>(OnTaskNotificationReceived);

            if (_confirmButton != null)
                _confirmButton.onClick.RemoveListener(OnConfirmClicked);
        }

        // ── 수신 핸들러 ───────────────────────────────────────────

        private void OnScenarioReceived(ScenarioData data)
        {
            if (data == null) return;
            _scenarioData = data;

            var sr = FindFirstObjectByType<ScenarioStateReceiver>();
            if (sr != null && sr.CurrentTaskState.taskIndex >= 0)
            {
                _totalCount = sr.TotalTaskCount;
                Refresh(sr.CurrentTaskState);
            }
        }

        private void OnTaskStateUpdated(TaskStateBroadcast broadcast)
        {
            _totalCount = broadcast.totalTaskCount;

            if (_scenarioData == null)
            {
                var sr = FindFirstObjectByType<ScenarioStateReceiver>();
                if (sr?.CurrentScenario != null)
                    _scenarioData = sr.CurrentScenario;
                else
                {
                    var receiver = FindFirstObjectByType<ScenarioReceiver>();
                    if (receiver?.ReceivedData != null)
                        _scenarioData = receiver.ReceivedData;
                }
            }

            Refresh(broadcast.currentTask);
        }

        private void OnSnapshotReceived(ScenarioSnapshotBroadcast broadcast)
        {
            _scenarioData = broadcast.scenarioData;
            _totalCount   = broadcast.totalTaskCount;
            Refresh(broadcast.currentTask);
        }

        private void OnBoltProgressReceived(BoltProgressBroadcast msg, FishNet.Transporting.Channel channel)
        {
            if(_boltProgressText == null) return;

            int remaining = msg.total - msg.removed;

            if(remaining > 0)
            {
                _boltProgressText.gameObject.SetActive(true);
                _boltProgressText.text = $"남은 볼트: <color=yellow>{remaining}</color> / {msg.total}";
            }
            else
            {
                _boltProgressText.gameObject.SetActive(false);
            }
        }

        private void OnTaskNotificationReceived(TaskNotificationBroadcast msg, FishNet.Transporting.Channel channel)
        {
            if(_notifCanvasGroup == null || _notifText == null) return;

            // ── 1. 이름 결정 로직 (ScenarioRunner가 보낸 이름을 우선 사용) ──
            string activePlayerName = msg.clientName;

            // 서버가 이름을 찾지 못해 빈 값이 오면 접속자 목록(UserStateRow)과 같은 규칙의 번호로 대체
            if(string.IsNullOrEmpty(activePlayerName))
                activePlayerName = msg.clientId >= 0 ? $"Player {msg.clientId + 1}" : "Player";

            // ── 2. 텍스트 구성 및 색상 적용 ──
            // 요청하신 형광 초록 계열 색상 (#00f800) 적용
            string pColor = "#00f800";
            _notifText.text = $"<color={pColor}><b>{activePlayerName}</b></color>(이)가\n<b>{msg.taskName}</b>를 완료하였습니다.";

            // ── 3. DOTween 연출 (나타났다 사라지기) ──
            _notifCanvasGroup.DOKill(); // 진행 중인 연출 즉시 중단

            Sequence seq = DOTween.Sequence();

            // UI 초기화
            _notifCanvasGroup.alpha = 0f;
            _notifCanvasGroup.gameObject.SetActive(true);

            // 연출 시작
            seq.Append(_notifCanvasGroup.DOFade(1f, 0.5f).SetEase(Ease.OutCubic)); // 부드럽게 나타남
            seq.AppendInterval(_displayDuration);                                 // 설정된 시간만큼 대기
            seq.Append(_notifCanvasGroup.DOFade(0f, 0.8f).SetEase(Ease.InSine));   // 천천히 사라짐
            seq.OnComplete(() => {
                _notifCanvasGroup.gameObject.SetActive(false);
            });
        }

        private void Refresh(TaskState state)
        {
            if(_boltProgressText != null) _boltProgressText.gameObject.SetActive(false);

            _currentTaskIndex = state.taskIndex;

            bool visible = state.status == TaskStatus.Running
                        || state.status == TaskStatus.Failed;
            _panel?.SetActive(visible);

            if (_progressText != null)
                _progressText.text = $"{state.taskIndex + 1} / {_totalCount}";

            if(_statusText != null)
            {
                var lse = _statusText.GetComponent<LocalizeStringEvent>();
                if(lse != null)
                {
                    lse.StringReference = state.status switch
                    {
                        TaskStatus.Running => _lsRunning,
                        TaskStatus.Failed => _lsFailed,
                        TaskStatus.Completed => _lsCompleted,
                        _ => null
                    };
                }
                //_statusText.gameObject.SetActive(true);
            }

            _confirmAllowed = false;

            if (_scenarioData != null && state.taskIndex < _scenarioData.scenario.tasks.Count)
            {
                var taskDef = _scenarioData.scenario.tasks[state.taskIndex];
                _currentModuleId = taskDef.moduleId;
                var config = _scenarioData.GetModuleConfig(taskDef.moduleId);

                // '완료' 버튼은 UI 확인으로 끝나는 단계(actionType = ConfirmTask)에서만 허용.
                // 물건 집기·존 도착·측정 단계는 실제 상호작용으로만 완료되어야 한다.
                _confirmAllowed = config != null
                    && string.Equals(config.actionType, ConfirmActionType, System.StringComparison.OrdinalIgnoreCase);

                // 내부 ID(Front_Cover_Removal 등)를 그대로 보여주지 않고 시나리오의 단계 제목(extra.title)을 쓴다.
                if (_moduleText != null)
                {
                    string title = config?.GetExtra("title");
                    _moduleText.text = string.IsNullOrEmpty(title) ? PrettifyId(taskDef.moduleId) : title;
                }

                if (_descriptionText != null)
                {
                    string desc = config?.GetExtra("description");
                    _descriptionText.text = string.IsNullOrEmpty(desc) ? PrettifyId(taskDef.moduleId) : desc;
                }
            }

            // 완료 버튼: Running 상태이면서 UI 확인 단계일 때만 활성화
            if (_confirmButton != null)
                _confirmButton.interactable = state.status == TaskStatus.Running && _confirmAllowed;

            if (_confirmBtnLabel != null)
                _confirmBtnLabel.text = state.status == TaskStatus.Failed ? "재시도 대기 중..." : "완료";
        }

        // ── 완료 버튼 클릭 ─────────────────────────────────────────

        private void OnConfirmClicked()
        {
            if (_currentTaskIndex < 0 || !_confirmAllowed || string.IsNullOrEmpty(_currentModuleId)) return;

            // 정식 확인 경로: InteractionEvents → NetworkInteractionBridge → TaskConfirmSignal → 서버.
            // 서버(ScenarioRunner.OnReceiveTaskConfirm)가 현재 단계·moduleId 일치를 검증한 뒤 처리한다.
            // 디버그 명령을 쓰지 않으므로 DebugBootstrapper.isDebugMode 와 무관하게 동작한다.
            InteractionEvents.FireTaskConfirmed(_currentModuleId);

#if UNITY_EDITOR
            Debug.Log($"[TaskProgressUI] 완료 버튼: moduleId={_currentModuleId}, task={_currentTaskIndex}");
#endif
        }
    }
}
