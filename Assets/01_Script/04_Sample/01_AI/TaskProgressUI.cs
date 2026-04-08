using FishNet;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

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

        private ScenarioData _scenarioData;
        private int          _currentTaskIndex = -1;
        private int          _totalCount;
        private string       _currentModuleId  = string.Empty;

        // ── 생명주기 ──────────────────────────────────────────────

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

            if (_confirmButton != null)
                _confirmButton.onClick.AddListener(OnConfirmClicked);
        }

        private void OnDisable()
        {
            ScenarioStateReceiver.OnTaskStateUpdated -= OnTaskStateUpdated;
            ScenarioStateReceiver.OnSnapshotReceived -= OnSnapshotReceived;
            ScenarioReceiver.OnScenarioReceived      -= OnScenarioReceived;
            InstanceFinder.ClientManager?.RegisterBroadcast<BoltProgressBroadcast>(OnBoltProgressReceived);

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
        private void Refresh(TaskState state)
        {
            if(_boltProgressText != null) _boltProgressText.gameObject.SetActive(false);

            _currentTaskIndex = state.taskIndex;

            bool visible = state.status == TaskStatus.Running
                        || state.status == TaskStatus.Failed;
            _panel?.SetActive(visible);

            if (_progressText != null)
                _progressText.text = $"{state.taskIndex + 1} / {_totalCount}";

            if (_statusText != null)
                _statusText.text = state.status switch
                {
                    TaskStatus.Running   => "진행 중",
                    TaskStatus.Failed    => "실패 — 재도전",
                    TaskStatus.Completed => "완료",
                    _                   => string.Empty,
                };

            if (_scenarioData != null && state.taskIndex < _scenarioData.scenario.tasks.Count)
            {
                var taskDef = _scenarioData.scenario.tasks[state.taskIndex];
                _currentModuleId = taskDef.moduleId;
                var config = _scenarioData.GetModuleConfig(taskDef.moduleId);

                if (_moduleText != null)
                    _moduleText.text = taskDef.moduleId;

                if (_descriptionText != null)
                {
                    string desc = config?.GetExtra("description");
                    _descriptionText.text = string.IsNullOrEmpty(desc) ? taskDef.moduleId : desc;
                }
            }

            // 완료 버튼: Running 상태에서만 활성화
            if (_confirmButton != null)
                _confirmButton.interactable = state.status == TaskStatus.Running;

            if (_confirmBtnLabel != null)
                _confirmBtnLabel.text = state.status == TaskStatus.Failed ? "재시도 대기 중..." : "완료";
        }

        // ── 완료 버튼 클릭 ─────────────────────────────────────────

        private void OnConfirmClicked()
        {
            if (_currentTaskIndex < 0) return;
            SendDebugCommand("Scenario/ForceComplete");

#if UNITY_EDITOR
            Debug.Log($"[TaskProgressUI] 완료 버튼: moduleId={_currentModuleId}, task={_currentTaskIndex}");
#endif
        }

        private void SendDebugCommand(string commandKey)
        {
            // Host 모드: Registry 직접 실행
            if (InstanceFinder.IsServerStarted && DebugBootstrapper.Registry != null)
            {
                DebugBootstrapper.Registry.Execute(commandKey, System.Array.Empty<string>(), -99);
                return;
            }

            // 데디케이티드 서버: 클라이언트 → 서버 Broadcast
            if (InstanceFinder.IsClientStarted)
            {
                int clientId = InstanceFinder.ClientManager?.Connection?.ClientId ?? -1;
                InstanceFinder.ClientManager.Broadcast(new DebugCommandBroadcast
                {
                    commandKey = commandKey,
                    argsJson   = "",
                    callerId   = clientId,
                });
            }
        }
    }
}
