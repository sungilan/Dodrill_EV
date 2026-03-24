using FishNet;
using FishNet.Transporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

// ============================================================
//  ScenarioStateReceiver.cs
//  클라이언트 전용 — TaskStateBroadcast / ScenarioSnapshotBroadcast 수신
//
//  책임:
//    1. 진행 중 상태 업데이트 (TaskStateBroadcast)
//    2. 신규 접속 시 전체 스냅샷 수신 (ScenarioSnapshotBroadcast)
//    3. UI 이벤트 발행 (ProgressHUD, TaskListPanel 이 구독)
// ============================================================

    public class ScenarioStateReceiver : MonoBehaviour
    {
        // ── UI 구독용 이벤트 ──────────────────
        public static event System.Action<TaskStateBroadcast>        OnTaskStateUpdated;
        public static event System.Action<ScenarioSnapshotBroadcast> OnSnapshotReceived;

        // ── 현재 상태 보관 ────────────────────
        public TaskState    CurrentTaskState  { get; private set; }
        public ScenarioData CurrentScenario   { get; private set; }
        public int          TotalTaskCount    { get; private set; }

        // ── FishNet 등록 ──────────────────────

        private void OnEnable()
        {
            if (InstanceFinder.ClientManager == null) return;
            InstanceFinder.ClientManager.RegisterBroadcast<TaskStateBroadcast>(OnReceiveTaskState);
            InstanceFinder.ClientManager.RegisterBroadcast<ScenarioSnapshotBroadcast>(OnReceiveSnapshot);
        }

        private void OnDisable()
        {
            if (InstanceFinder.ClientManager == null) return;
            InstanceFinder.ClientManager.UnregisterBroadcast<TaskStateBroadcast>(OnReceiveTaskState);
            InstanceFinder.ClientManager.UnregisterBroadcast<ScenarioSnapshotBroadcast>(OnReceiveSnapshot);
        }

        // ── 수신 처리 ─────────────────────────

        private void OnReceiveTaskState(TaskStateBroadcast broadcast, Channel channel)
        {
            CurrentTaskState = broadcast.currentTask;
            TotalTaskCount   = broadcast.totalTaskCount;

#if UNITY_EDITOR
            Debug.Log($"[ScenarioStateReceiver] Task[{broadcast.currentTask.taskIndex}] 상태: {broadcast.currentTask.status}");
#endif
            OnTaskStateUpdated?.Invoke(broadcast);
        }

        private void OnReceiveSnapshot(ScenarioSnapshotBroadcast broadcast, Channel channel)
        {
            CurrentScenario  = broadcast.scenarioData;
            CurrentTaskState = broadcast.currentTask;
            TotalTaskCount   = broadcast.totalTaskCount;

#if UNITY_EDITOR
            Debug.Log($"[ScenarioStateReceiver] 스냅샷 수신 — Task[{broadcast.currentTask.taskIndex}] / {broadcast.totalTaskCount}");
#endif
            OnSnapshotReceived?.Invoke(broadcast);
        }

        // ── 완료 신호 전송 ────────────────────

        /// <summary>
        /// 클라이언트가 Task 완료 조건을 충족했을 때 호출
        /// 서버로 TaskCompleteSignal 전송
        /// </summary>
        public void SendCompleteSignal(int taskIndex)
        {
            int clientId = InstanceFinder.ClientManager?.Connection?.ClientId ?? -1;

            InstanceFinder.ClientManager.Broadcast(new TaskCompleteSignal
            {
                taskIndex = taskIndex,
                clientId  = clientId,
            });

            Debug.Log($"[ScenarioStateReceiver] 완료 신호 전송 — Task[{taskIndex}], Client#{clientId}");
        }
    }