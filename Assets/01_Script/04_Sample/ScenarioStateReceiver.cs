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
    public static event System.Action<TaskStateBroadcast> OnTaskStateUpdated;
    public static event System.Action<ScenarioSnapshotBroadcast> OnSnapshotReceived;

    // ── 현재 상태 보관 ────────────────────
    public TaskState CurrentTaskState { get; private set; }
    public ScenarioData CurrentScenario { get; private set; }
    public int TotalTaskCount { get; private set; }

    // ── FishNet 등록 ──────────────────────

    private void OnEnable()
    {
        if(InstanceFinder.ClientManager == null) return;
        InstanceFinder.ClientManager.RegisterBroadcast<TaskStateBroadcast>(OnReceiveTaskState);
        InstanceFinder.ClientManager.RegisterBroadcast<ScenarioSnapshotBroadcast>(OnReceiveSnapshot);
    }

    private void OnDisable()
    {
        if(InstanceFinder.ClientManager == null) return;
        InstanceFinder.ClientManager.UnregisterBroadcast<TaskStateBroadcast>(OnReceiveTaskState);
        InstanceFinder.ClientManager.UnregisterBroadcast<ScenarioSnapshotBroadcast>(OnReceiveSnapshot);
    }

    // ── 수신 처리 ─────────────────────────

    private void OnReceiveTaskState(TaskStateBroadcast broadcast, Channel channel)
    {
        CurrentTaskState = broadcast.currentTask;
        TotalTaskCount = broadcast.totalTaskCount;

#if UNITY_EDITOR
        Debug.Log($"[ScenarioStateReceiver] Task[{broadcast.currentTask.taskIndex}] 상태: {broadcast.currentTask.status}");
#endif
        OnTaskStateUpdated?.Invoke(broadcast);
    }

    private void OnReceiveSnapshot(ScenarioSnapshotBroadcast broadcast, Channel channel)
    {
        CurrentScenario = broadcast.scenarioData;
        CurrentTaskState = broadcast.currentTask;
        TotalTaskCount = broadcast.totalTaskCount;

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
            clientId = clientId,
        });

        Debug.Log($"[ScenarioStateReceiver] 완료 신호 전송 — Task[{taskIndex}], Client#{clientId}");
    }
}

//using FishNet;
//using FishNet.Transporting;
//using UnityEngine;
//using Debug = UnityEngine.Debug;

//public class ScenarioStateReceiver : MonoBehaviour
//{
//    public static ScenarioStateReceiver Instance { get; private set; }

//    // ★ Channel 파라미터 제거
//    public static event System.Action<TaskStateBroadcast> OnTaskStateUpdated;
//    public static event System.Action<ScenarioSnapshotBroadcast> OnSnapshotReceived;

//    public TaskState CurrentTaskState { get; private set; }
//    public ScenarioData CurrentScenario { get; private set; }
//    public int TotalTaskCount { get; private set; }

//    private void Awake()
//    {
//        Debug.Log("[ScenarioStateReceiver] Awake");
//        if(Instance != null && Instance != this)
//        {
//            Debug.LogWarning("[ScenarioStateReceiver] 이미 Instance가 있음 — 파괴");
//            Destroy(gameObject);
//            return;
//        }
//        Instance = this;
//    }

//    private void OnEnable()
//    {
//        Debug.Log("[ScenarioStateReceiver] OnEnable — 브로드캐스트 등록");
//        if(InstanceFinder.ClientManager == null)
//        {
//            Debug.LogError("[ScenarioStateReceiver] ClientManager가 없음!");
//            return;
//        }
//        InstanceFinder.ClientManager.RegisterBroadcast<TaskStateBroadcast>(OnReceiveTaskState);
//        InstanceFinder.ClientManager.RegisterBroadcast<ScenarioSnapshotBroadcast>(OnReceiveSnapshot);
//    }

//    private void OnDisable()
//    {
//        Debug.Log("[ScenarioStateReceiver] OnDisable — 브로드캐스트 등록 해제");
//        if(InstanceFinder.ClientManager == null) return;
//        InstanceFinder.ClientManager.UnregisterBroadcast<TaskStateBroadcast>(OnReceiveTaskState);
//        InstanceFinder.ClientManager.UnregisterBroadcast<ScenarioSnapshotBroadcast>(OnReceiveSnapshot);
//    }

//    // ★ Channel 파라미터는 유지 (FishNet이 요구)하지만, 이벤트 발화할 때는 포함 X
//    private void OnReceiveTaskState(TaskStateBroadcast broadcast, Channel channel)
//    {
//        CurrentTaskState = broadcast.currentTask;
//        TotalTaskCount = broadcast.totalTaskCount;

//        Debug.Log($"[ScenarioStateReceiver] ★ TaskState 수신 ★");
//        Debug.Log($"[ScenarioStateReceiver]   taskIndex: {broadcast.currentTask.taskIndex}");
//        Debug.Log($"[ScenarioStateReceiver]   status: {broadcast.currentTask.status}");
//        Debug.Log($"[ScenarioStateReceiver]   totalTaskCount: {broadcast.totalTaskCount}");

//        // ★ Channel 파라미터 없이 발화
//        OnTaskStateUpdated?.Invoke(broadcast);
//    }

//    private void OnReceiveSnapshot(ScenarioSnapshotBroadcast broadcast, Channel channel)
//    {
//        CurrentScenario = broadcast.scenarioData;
//        CurrentTaskState = broadcast.currentTask;
//        TotalTaskCount = broadcast.totalTaskCount;

//        Debug.Log($"[ScenarioStateReceiver] ★ Snapshot 수신 ★");
//        Debug.Log($"[ScenarioStateReceiver]   scenarioData: {(broadcast.scenarioData != null ? "있음" : "NULL")}");
//        Debug.Log($"[ScenarioStateReceiver]   taskIndex: {broadcast.currentTask.taskIndex}");
//        Debug.Log($"[ScenarioStateReceiver]   totalTaskCount: {broadcast.totalTaskCount}");

//        // ★ Channel 파라미터 없이 발화
//        OnSnapshotReceived?.Invoke(broadcast);
//    }

//    public void SendCompleteSignal(int taskIndex)
//    {
//        int clientId = InstanceFinder.ClientManager?.Connection?.ClientId ?? -1;

//        InstanceFinder.ClientManager.Broadcast(new TaskCompleteSignal
//        {
//            taskIndex = taskIndex,
//            clientId = clientId,
//        });

//        Debug.Log($"[ScenarioStateReceiver] 완료 신호 전송 — Task[{taskIndex}], Client#{clientId}");
//    }
//}
