using DoDrill.Training;
using FishNet;
using FishNet.Transporting;
using UnityEngine;
using static ScenarioRunner;
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
    public static ScenarioStateReceiver Instance { get; private set; }
    private void Awake() { Instance = this; }

    // ── UI 구독용 이벤트 ──────────────────
    public static event System.Action<TaskStateBroadcast> OnTaskStateUpdated;
    public static event System.Action<ScenarioSnapshotBroadcast> OnSnapshotReceived;

    // ── 현재 상태 보관 ────────────────────
    public TaskState CurrentTaskState { get; private set; }
    // 스냅샷(ScenarioSnapshotBroadcast) 없이 시작한 세션은 시나리오가 ScenarioReceiver 에만 들어온다.
    // 예전엔 여기만 보는 코드(진단기, 평가 모드, 방전 타이머, 고전압 경고, 과제 물체 배치 등)가 시나리오가 없다고 판단했다.
    private ScenarioData _snapshotScenario;
    private ScenarioReceiver _scenarioReceiver;
    public ScenarioData CurrentScenario
    {
        get
        {
            if(_snapshotScenario != null) return _snapshotScenario;
            if(_scenarioReceiver == null) _scenarioReceiver = FindFirstObjectByType<ScenarioReceiver>();
            return _scenarioReceiver != null ? _scenarioReceiver.ReceivedData : null;
        }
        private set { _snapshotScenario = value; }
    }
    public int TotalTaskCount { get; private set; }

    // ── FishNet 등록 ──────────────────────

    private void OnEnable()
    {
        if(InstanceFinder.ClientManager == null) return;
        InstanceFinder.ClientManager.RegisterBroadcast<TaskStateBroadcast>(OnReceiveTaskState);
        InstanceFinder.ClientManager.RegisterBroadcast<ScenarioSnapshotBroadcast>(OnReceiveSnapshot);
        InstanceFinder.ClientManager.RegisterBroadcast<PlaySoundBroadcast>(OnReceiveSound);
    }
    private void ApplyBroadcast(TaskStateBroadcast broadcast)
    {
        CurrentTaskState = broadcast.currentTask;
        TotalTaskCount = broadcast.totalTaskCount;

        RefreshTaskItemFilter(broadcast.currentTask.taskIndex);

        NetworkedSessionUserList.InvokeActivePlayerChanged(broadcast.currentTask.activePlayerId, "");
    }
    private void RefreshTaskItemFilter(int taskIndex)
    {
        if(CurrentScenario == null) return;
        if(taskIndex < 0 || taskIndex >= CurrentScenario.scenario.tasks.Count) return;

        var taskDef = CurrentScenario.scenario.tasks[taskIndex];
        var config = CurrentScenario.GetModuleConfig(taskDef.moduleId);

        if(config == null || config.requiredItems == null || config.requiredItems.Count == 0)
        {
            //TaskItemFilter.Clear();
            return;
        }

        var state = CurrentTaskState;
        if(state.status == TaskStatus.Running
            && state.totalSteps > 0
            && config.requiredItems.Count == state.totalSteps
            && state.currentStepIndex >= 0
            && state.currentStepIndex < config.requiredItems.Count)
        {
            //TaskItemFilter.SetCurrentTaskItems(new[] { config.requiredItems[state.currentStepIndex] });
            return;
        }

        //TaskItemFilter.SetCurrentTaskItems(config.requiredItems);
    }
    private void OnDisable()
    {
        if(InstanceFinder.ClientManager == null) return;
        InstanceFinder.ClientManager.UnregisterBroadcast<TaskStateBroadcast>(OnReceiveTaskState);
        InstanceFinder.ClientManager.UnregisterBroadcast<ScenarioSnapshotBroadcast>(OnReceiveSnapshot);
    }

    // ── 수신 처리 ─────────────────────────

    private string _lastReceiverLogKey;

    private void OnReceiveTaskState(TaskStateBroadcast broadcast, Channel channel)
    {
        CurrentTaskState = broadcast.currentTask;
        TotalTaskCount = broadcast.totalTaskCount;
        // 같은 상태가 연달아 오는 경우(진행자·완료자 갱신 등)에는 로그를 반복하지 않는다
        string logKey = $"{broadcast.currentTask.moduleId}|{broadcast.currentTask.taskIndex}|{broadcast.currentTask.status}";
        if(logKey != _lastReceiverLogKey)
        {
            _lastReceiverLogKey = logKey;
            Debug.Log($"[Receiver] 모듈 전환: {broadcast.currentTask.moduleId} (Index: {broadcast.currentTask.taskIndex}, {broadcast.currentTask.status})");
        }

#if UNITY_EDITOR
        Debug.Log($"[ScenarioStateReceiver] Task[{broadcast.currentTask.taskIndex}] 상태: {broadcast.currentTask.status}");
#endif
        SaveContentProgress(broadcast.currentTask, broadcast.totalTaskCount);
        OnTaskStateUpdated?.Invoke(broadcast);
    }

    // 콘텐츠 목록 카드의 진행률(D-17): 예전에는 ScenarioDataLoader 의 초기값(0) 말고는 저장하는 곳이 없어 항상 0% 였다.
    // 새 단계가 시작되면(Running) 그 앞의 단계는 모두 끝난 것이므로 completed = taskIndex, 마지막 단계가 끝나면 전부 완료로 기록한다.
    // 기록은 기기 로컬(progress.json)이고 각 플레이어가 자기 기기에 남긴다.
    private void SaveContentProgress(TaskState state, int total)
    {
        var mgr = ScenarioProgressManager.Instance;
        string id = CurrentScenario != null ? CurrentScenario.scenarioId : null;
        if(mgr == null || string.IsNullOrEmpty(id) || total <= 0) return;

        int completed;
        if(state.status == TaskStatus.Running) completed = state.taskIndex;
        else if(state.status == TaskStatus.Completed) completed = state.taskIndex + 1;
        else return;

        completed = Mathf.Clamp(completed, 0, total);
        // 이미 더 많이 진행한 기록이 있으면(재도전 시작 등) 덮어써서 줄이지 않는다
        var prev = mgr.GetProgress(id);
        if(prev != null && prev.totalTasks == total && prev.completedTasks > completed) return;
        mgr.SaveProgress(id, completed, total);
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

    private void OnReceiveSound(PlaySoundBroadcast msg, Channel channel)
    {
        // ClickableAnimator에서 사용한 것과 동일한 사운드 매니저 호출
        if(!string.IsNullOrEmpty(msg.soundName))
        {
            Managers.Sound.Play(msg.soundName, Define.Sound.Effect, 1.0f, msg.volume);
        }
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

    public static void MirrorServerTaskStateForLocalSubscribers(in TaskState currentTask, int totalTaskCount)
    {
        var broadcast = new TaskStateBroadcast { currentTask = currentTask, totalTaskCount = totalTaskCount };

        foreach(var r in Object.FindObjectsByType<ScenarioStateReceiver>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if(r != null)
                r.ApplyBroadcast(broadcast);
        }

        OnTaskStateUpdated?.Invoke(broadcast);
    }

    // ✅ TaskInteractionZone에서 사용할 메서드들 추가
    /// <summary>
    /// 현재 진행 중인 Task의 TaskDef를 반환합니다.
    /// </summary>
    public TaskDef GetCurrentTask()
    {
        if(CurrentScenario == null || CurrentTaskState.taskIndex < 0 ||
           CurrentTaskState.taskIndex >= CurrentScenario.scenario.tasks.Count)
            return null;

        return CurrentScenario.scenario.tasks[CurrentTaskState.taskIndex];
    }

    /// <summary>
    /// 현재 Task의 모듈 설정(Config)을 반환합니다.
    /// </summary>
    public ModuleConfig GetCurrentTaskConfig()
    {
        var task = GetCurrentTask();
        if(task == null) return null;

        return CurrentScenario.GetModuleConfig(task.moduleId);
    }

    /// <summary>
    /// 현재 Task의 인덱스를 반환합니다.
    /// </summary>
    public int GetCurrentTaskIndex() => CurrentTaskState.taskIndex;

}

