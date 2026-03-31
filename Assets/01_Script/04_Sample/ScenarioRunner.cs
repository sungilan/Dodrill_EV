using System.Collections;
using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;
using Debug = UnityEngine.Debug;
using FishNet.Managing.Scened;
using DoDrill;

// ============================================================
//  ScenarioRunner.cs
//  서버 전용 — Task 순서 관리 / 상태 전이 / 완료 판정
//
//  책임:
//    1. ScenarioReceiver.OnScenarioReceived 구독 → 초기화
//    2. Task 순서 순회 (Pending → Running → Completed/Failed)
//    3. completionMode 판정 (AnyPlayer / AllPlayers / Majority)
//    4. TaskStateBroadcast 로 전체 클라이언트 동기화
//    5. 신규 접속자에게 ScenarioSnapshotBroadcast 1:1 전송
//
//  클라이언트 수신은 ScenarioStateReceiver 가 담당
//
//  Fail 처리:
//    - Fail 시 같은 Task 를 재시작 (재도전)
//    - maxRetries 초과 시에만 완전 중단
//    - 재도전 횟수는 TaskState.retryCount 로 클라이언트에 동기화
// ============================================================

public class ScenarioRunner : MonoBehaviour
{
    // ── 외부 주입 ─────────────────────────
    [Header("References")]
    [Tooltip("씬의 ModuleRegistry를 들고 있는 ScenarioBootstrapper")]
    [SerializeField] private ScenarioBootstrapper _bootstrapper;

    [Tooltip("Task 시작/종료 시 오브젝트 스폰/디스폰 담당")]
    [SerializeField] private ScenarioSpawner _spawner;

    [Header("Retry Settings")]
    [Tooltip("Task 당 최대 재도전 횟수. 0 = 무제한 재도전")]
    [SerializeField] private int _maxRetries = 0;

    [Tooltip("Fail 후 재시작 전 대기 시간 (초) — UI에서 실패 상태를 보여주는 시간")]
    [SerializeField] private float _failRetryDelay = 1.5f;

    // ── 내부 상태 ─────────────────────────
    private ScenarioData _data;
    private TaskState _currentTaskState;
    private ITaskModule _currentModule;
    private bool _isRunning;

    // 재도전 추적
    private int _retryCount;

    // completionMode 판정용
    private HashSet<int> _completedClients = new();
    private int _connectedClientCount;

    // 존 인터랙션 레이스 컨디션 방지 (서버 전용 락)
    private bool _taskLocked;

    // ── 생명주기 ──────────────────────────

    private void OnEnable()
    {
        if (InstanceFinder.ServerManager != null && InstanceFinder.IsServerStarted)
        {
            InstanceFinder.NetworkManager.SceneManager.OnClientLoadedStartScenes += OnClientLoadedStartScenes;
            InstanceFinder.ServerManager.OnRemoteConnectionState += OnClientDisconnected;
            InstanceFinder.ServerManager.RegisterBroadcast<TaskCompleteSignal>(OnReceiveCompleteSignal);
            InstanceFinder.ServerManager.RegisterBroadcast<ZoneInteractionSignal>(OnReceiveZoneSignal);
            InstanceFinder.ServerManager.RegisterBroadcast<ItemUsedSignal>(OnReceiveItemUsed);
            InstanceFinder.ServerManager.RegisterBroadcast<ItemGrabbedSignal>(OnReceiveItemGrabbed);
            InstanceFinder.ServerManager.RegisterBroadcast<TaskConfirmSignal>(OnReceiveTaskConfirm);
            InteractionEvents.OnStepAdvanced += OnStepAdvanced;
        }
        else if (InstanceFinder.ServerManager != null)
        {
            InstanceFinder.ServerManager.OnServerConnectionState += OnServerStarted;
        }
    }

    private void OnServerStarted(ServerConnectionStateArgs args)
    {
        if (args.ConnectionState != LocalConnectionState.Started) return;
        InstanceFinder.ServerManager.OnServerConnectionState -= OnServerStarted;
        InstanceFinder.NetworkManager.SceneManager.OnClientLoadedStartScenes += OnClientLoadedStartScenes;
        InstanceFinder.ServerManager.OnRemoteConnectionState += OnClientDisconnected;
        InstanceFinder.ServerManager.RegisterBroadcast<TaskCompleteSignal>(OnReceiveCompleteSignal);
        InstanceFinder.ServerManager.RegisterBroadcast<ZoneInteractionSignal>(OnReceiveZoneSignal);
        InstanceFinder.ServerManager.RegisterBroadcast<ItemUsedSignal>(OnReceiveItemUsed);
        InstanceFinder.ServerManager.RegisterBroadcast<ItemGrabbedSignal>(OnReceiveItemGrabbed);
        InstanceFinder.ServerManager.RegisterBroadcast<TaskConfirmSignal>(OnReceiveTaskConfirm);
        InteractionEvents.OnStepAdvanced += OnStepAdvanced;
    }

    private void OnDisable()
    {
        if (InstanceFinder.ServerManager != null)
        {
            InstanceFinder.ServerManager.OnServerConnectionState -= OnServerStarted;
            InstanceFinder.NetworkManager.SceneManager.OnClientLoadedStartScenes -= OnClientLoadedStartScenes;
            InstanceFinder.ServerManager.OnRemoteConnectionState -= OnClientDisconnected;
            InstanceFinder.ServerManager.UnregisterBroadcast<TaskCompleteSignal>(OnReceiveCompleteSignal);
            InstanceFinder.ServerManager.UnregisterBroadcast<ZoneInteractionSignal>(OnReceiveZoneSignal);
            InstanceFinder.ServerManager.UnregisterBroadcast<ItemUsedSignal>(OnReceiveItemUsed);
            InstanceFinder.ServerManager.UnregisterBroadcast<ItemGrabbedSignal>(OnReceiveItemGrabbed);
            InstanceFinder.ServerManager.UnregisterBroadcast<TaskConfirmSignal>(OnReceiveTaskConfirm);
        }
        InteractionEvents.OnStepAdvanced -= OnStepAdvanced;
    }

    private void Update()
    {
        if (!_isRunning || _currentModule == null) return;

        _currentTaskState.elapsedTime += Time.deltaTime;
        _currentModule.OnUpdate(Time.deltaTime);

        // 타임아웃 체크 (_taskLocked 중에는 스냅 연출 진행 중이므로 스킵)
        if (!_taskLocked &&
            _currentTaskState.timeoutSeconds > 0f &&
            _currentTaskState.elapsedTime >= _currentTaskState.timeoutSeconds)
        {
            Debug.LogWarning($"[ScenarioRunner] Task[{_currentTaskState.taskIndex}] 타임아웃 → Fail");
            FailCurrentTask();
        }
    }

    // ── 시나리오 시작 (ScenarioDistributor 가 직접 호출) ──────

    public void StartScenario(ScenarioData data)
    {
        _data = data;
        _isRunning = false;
        _retryCount = 0;
        _connectedClientCount = InstanceFinder.ServerManager?.Clients?.Count ?? 0;

        Debug.Log($"[ScenarioRunner] 시나리오 시작 — {data.scenarioName}, Tasks: {data.scenario.tasks.Count}");
        _spawner?.SpawnMapObjects(data.map);
        StartTask(0);
    }

    // ── Task 전이 ─────────────────────────

    private void StartTask(int index)
    {
        if (_data == null || index >= _data.scenario.tasks.Count)
        {
            Debug.Log("[ScenarioRunner] 모든 Task 완료 — 1.5초 후 종료");
            _isRunning = false;
            StartCoroutine(QuitAfterDelay(1.5f));
            return;
        }

        var taskDef = _data.scenario.tasks[index];
        var config = _data.GetModuleConfig(taskDef.moduleId);
        var module = _bootstrapper.Registry.Get(taskDef.moduleId);

        if (module == null)
        {
            Debug.LogWarning($"[ScenarioRunner] 모듈 없음 — Task[{index}] 더미 모듈로 대기 중 (ForceComplete 로 넘길 수 있음)");
            module = new DummyTaskModule(taskDef.moduleId);
        }

        _completedClients.Clear();
        _taskLocked = false;
        _currentModule = module;

        _currentTaskState = new TaskState
        {
            taskIndex = index,
            status = TaskStatus.Running,
            elapsedTime = 0f,
            timeoutSeconds = taskDef.timeout,
            completedClientIds = new List<int>(),
            retryCount = _retryCount,
            currentStepIndex = 0,
            totalSteps = (module is IStepAwareModule sm) ? sm.TotalSteps : 0,
        };

        _isRunning = true;

        // Task 오브젝트 스폰 (서버 전용, FishNet이 클라이언트에 자동 복제)
        _spawner?.SpawnForTask(taskDef);

        // GrabUse / GrabAll 계열은 클릭/터치 후 1.5s 딜레이 (Zone은 OnReceiveZoneSignal이 직접 처리)
        bool needsDelay = module is GrabUseTaskModule || module is GrabAllItemsModule;
        System.Action onComplete = needsDelay
            ? () => StartCoroutine(CompleteAfterSnapDelay(1.5f))
            : (System.Action)CompleteCurrentTask;

        module.OnStart(config,
            onComplete: onComplete,
            onFail: FailCurrentTask);

        BroadcastState();
        Debug.Log($"[ScenarioRunner] Task[{index}] 시작 — {taskDef.moduleId}" +
                  (_retryCount > 0 ? $" (재도전 #{_retryCount})" : ""));
    }

    private void CompleteCurrentTask()
    {
        if (!_isRunning) return;

        _currentModule?.OnComplete();
        _currentTaskState.status = TaskStatus.Completed;
        _retryCount = 0;
        // 현재 Task 오브젝트 디스폰 (FishNet이 클라이언트에 자동 제거)
        _spawner?.DespawnCurrentTask();
        BroadcastState();

        Debug.Log($"[ScenarioRunner] Task[{_currentTaskState.taskIndex}] 완료");

        int next = _currentTaskState.taskIndex + 1;
        StartTask(next);
    }

    private void FailCurrentTask()
    {
        if (!_isRunning) return;

        _currentModule?.OnFail();
        _currentTaskState.status = TaskStatus.Failed;
        _retryCount++;
        _isRunning = false; // 딜레이 중 중복 호출 방지

        // 실패한 Task 오브젝트 디스폰 (재도전 시 StartTask에서 다시 스폰됨)
        _spawner?.DespawnCurrentTask();
        BroadcastState(); // 클라이언트에 Fail 상태 먼저 전달

        // maxRetries 초과 체크 (0 = 무제한)
        if (_maxRetries > 0 && _retryCount > _maxRetries)
        {
            Debug.LogError($"[ScenarioRunner] Task[{_currentTaskState.taskIndex}] " +
                           $"최대 재도전 횟수({_maxRetries}) 초과 → 시나리오 중단");
            return;
        }

        int taskIndex = _currentTaskState.taskIndex;
        Debug.LogWarning($"[ScenarioRunner] Task[{taskIndex}] 실패 → " +
                         $"재도전 #{_retryCount}" +
                         (_maxRetries > 0 ? $" / {_maxRetries}" : " (무제한)"));

        // Fail UI를 잠깐 보여준 후 재시작
        StartCoroutine(RetryAfterDelay(taskIndex, _failRetryDelay));
    }

    private IEnumerator RetryAfterDelay(int taskIndex, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_currentTaskState.taskIndex != taskIndex)
        {
            Debug.Log($"[ScenarioRunner] RetryAfterDelay 무시 — 현재 Task[{_currentTaskState.taskIndex}], 대기 중 Task[{taskIndex}]");
            yield break;
        }
        Debug.Log($"[ScenarioRunner] Task[{taskIndex}] 재시작");
        StartTask(taskIndex);
    }

    // ── completionMode 판정 ───────────────

    private void OnReceiveCompleteSignal(NetworkConnection conn, TaskCompleteSignal signal, Channel channel)
    {
        // Zone 스냅 애니메이션 진행 중이면 무시 (중복 완료 방지)
        if (_taskLocked || !_isRunning) return;
        if (signal.taskIndex != _currentTaskState.taskIndex) return;

        _completedClients.Add(signal.clientId);
        _currentTaskState.completedClientIds.Add(signal.clientId);

        var taskDef = _data.scenario.tasks[_currentTaskState.taskIndex];
        if (EvaluateCompletion(taskDef.completionMode))
            CompleteCurrentTask();
    }

    // ── InteractionEvents 서버 측 중계 ─────────────────────────
    // 클라이언트(VR/PC/Mobile) → 신호 → 서버에서 InteractionEvents 재발행
    // → 기존 모듈(GrabUse, GrabAll, Confirm)이 서버에서 정상 처리

    private void OnReceiveItemUsed(NetworkConnection conn, ItemUsedSignal signal, Channel channel)
    {
        if (!_isRunning) return;
        if (signal.taskIndex != _currentTaskState.taskIndex) return;
        InteractionEvents.FireItemUsed(signal.itemId);
    }

    private void OnReceiveItemGrabbed(NetworkConnection conn, ItemGrabbedSignal signal, Channel channel)
    {
        if (!_isRunning) return;
        if (signal.taskIndex != _currentTaskState.taskIndex) return;
        InteractionEvents.FireItemGrabbed(signal.itemId);
    }

    private void OnReceiveTaskConfirm(NetworkConnection conn, TaskConfirmSignal signal, Channel channel)
    {
        if (!_isRunning) return;
        if (signal.taskIndex != _currentTaskState.taskIndex) return;
        InteractionEvents.FireTaskConfirmed(signal.moduleId);
    }

    // ── 스텝 진행 알림 (SequentialStepModule → ScenarioRunner) ─

    private void OnStepAdvanced(int stepIndex, int totalSteps)
    {
        if (!_isRunning) return;
        _currentTaskState.currentStepIndex = stepIndex;
        _currentTaskState.totalSteps       = totalSteps;
        BroadcastState();
    }

    // ── 존 인터랙션 신호 처리 (서버 권위, 레이스 컨디션 방지) ──

    private void OnReceiveZoneSignal(NetworkConnection conn, ZoneInteractionSignal signal, Channel channel)
    {
        if (_taskLocked || !_isRunning) return;
        if (signal.taskIndex != _currentTaskState.taskIndex) return;

        var taskDef = _data.scenario.tasks[_currentTaskState.taskIndex];

        // ── IStepAwareModule 경로 (다단계 Task) ─────────────────
        if (_currentModule is IStepAwareModule stepModule && stepModule.IsCurrentStepGrabZone)
        {
            string stepZoneId = stepModule.CurrentStepZoneId;
            if (!string.IsNullOrEmpty(stepZoneId) && stepZoneId != signal.zoneId) return;

            var stepItems = stepModule.CurrentStepRequiredItems;
            bool validStep = stepItems.Count == 0 || stepItems.Contains(signal.itemId);
            if (!validStep) return;

            _taskLocked = true;

            float  snapDuration = stepModule.CurrentStepSnapDuration;
            float  snapDelay    = stepModule.CurrentStepSnapDelay;
            string guideId      = stepModule.CurrentStepGuideId;
            if (string.IsNullOrEmpty(guideId)) guideId = signal.zoneId;

            if (!string.IsNullOrEmpty(guideId) && InstanceFinder.IsServerStarted)
            {
                InstanceFinder.ServerManager.Broadcast(new PlaySnapAnimBroadcast
                {
                    taskIndex    = signal.taskIndex,
                    itemId       = signal.itemId,
                    guideId      = guideId,
                    snapDuration = snapDuration,
                });
            }

            // 스텝 진행 (AdvanceZoneStep 내부에서 FireStepAdvanced → OnStepAdvanced → BroadcastState)
            bool isLastStep = stepModule.AdvanceZoneStep();

            Debug.Log($"[ScenarioRunner] Step ZoneSignal: zone={signal.zoneId}, item={signal.itemId}, " +
                      $"마지막={isLastStep}");

            if (isLastStep)
                StartCoroutine(CompleteAfterSnapDelay(snapDuration + snapDelay));
            else
                StartCoroutine(UnlockAfterStepDelay(snapDuration + snapDelay));

            return;
        }

        // ── 기존 단일 스텝 경로 ──────────────────────────────────
        var config = _data.GetModuleConfig(taskDef.moduleId);

        if (!string.IsNullOrEmpty(config?.targetZoneId) && config.targetZoneId != signal.zoneId)
            return;

        var required = config?.requiredItems;
        bool validItem = required == null || required.Count == 0 || required.Contains(signal.itemId);
        if (!validItem) return;

        _taskLocked = true;

        if (!string.IsNullOrEmpty(config?.targetZoneId))
            TaskInteractionZone.Find(config.targetZoneId)?.Deactivate();

        string guideIdSingle = string.Empty;
        if (taskDef.spawnObjects != null)
        {
            foreach (var bundle in taskDef.spawnObjects)
            {
                if (bundle.prefabId == signal.itemId && !string.IsNullOrEmpty(bundle.guideId))
                {
                    guideIdSingle = bundle.guideId;
                    break;
                }
            }
        }

        float snapDurationSingle = ParseExtraFloat(config, "snapDuration", 0.5f);
        float snapDelaySingle    = ParseExtraFloat(config, "snapDelay",    1.5f);

        if (string.IsNullOrEmpty(guideIdSingle))
            guideIdSingle = signal.zoneId;

        if (!string.IsNullOrEmpty(guideIdSingle) && InstanceFinder.IsServerStarted)
        {
            InstanceFinder.ServerManager.Broadcast(new PlaySnapAnimBroadcast
            {
                taskIndex    = signal.taskIndex,
                itemId       = signal.itemId,
                guideId      = guideIdSingle,
                snapDuration = snapDurationSingle,
            });
        }

        StartCoroutine(CompleteAfterSnapDelay(snapDurationSingle + snapDelaySingle));

        Debug.Log($"[ScenarioRunner] ZoneSignal: zone={signal.zoneId}, item={signal.itemId}, " +
                  $"guide={guideIdSingle}, snapDur={snapDurationSingle}s, delay={snapDelaySingle}s");
    }

    private IEnumerator UnlockAfterStepDelay(float delay)
    {
        int taskIndexAtStart = _currentTaskState.taskIndex;
        yield return new WaitForSeconds(delay);
        if (_currentTaskState.taskIndex != taskIndexAtStart) yield break;
        _taskLocked = false;
    }

    private IEnumerator CompleteAfterSnapDelay(float delay)
    {
        int taskIndexAtStart = _currentTaskState.taskIndex;
        yield return new WaitForSeconds(delay);
        _taskLocked = false;

        // 대기 중 이미 다른 경로로 완료된 경우 중복 실행 방지
        if (_currentTaskState.taskIndex != taskIndexAtStart) yield break;
        CompleteCurrentTask();
    }

    private static float ParseExtraFloat(ModuleConfig config, string key, float defaultValue)
    {
        if (config == null) return defaultValue;
        string raw = config.GetExtraRaw(key);
        return float.TryParse(raw, System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out float val)
            ? val : defaultValue;
    }

    private bool EvaluateCompletion(string mode)
    {
        int completed = _completedClients.Count;
        int total = Mathf.Max(1, _connectedClientCount);

        return mode switch
        {
            "AnyPlayer" => completed >= 1,
            "AllPlayers" => completed >= total,
            "Majority" => completed > total / 2,
            _ => false,
        };
    }

    // ── 신규 접속자 스냅샷 ────────────────

    private void OnClientLoadedStartScenes(NetworkConnection conn, bool asServer)
    {
        if (!asServer) return;

        _connectedClientCount = InstanceFinder.ServerManager.Clients.Count;

        if (!_isRunning || _data == null) return;

        var snapshot = new ScenarioSnapshotBroadcast
        {
            scenarioData = _data,
            currentTask = _currentTaskState,
            totalTaskCount = _data.scenario.tasks.Count,
        };

        InstanceFinder.ServerManager.Broadcast(conn, snapshot);
        Debug.Log($"[ScenarioRunner] 스냅샷 전송 → Client#{conn.ClientId}");
    }

    private void OnClientDisconnected(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        if (args.ConnectionState == RemoteConnectionState.Stopped)
            _connectedClientCount = Mathf.Max(0, InstanceFinder.ServerManager.Clients.Count);
    }

    // ── 시나리오 종료 ─────────────────────────

    private System.Collections.IEnumerator QuitAfterDelay(float delay)
    {
        yield return new UnityEngine.WaitForSeconds(delay);
        Debug.Log("[ScenarioRunner] 시나리오 종료 — 종료 팝업 표시");

        // 클라이언트에 종료 팝업 표시 브로드캐스트
        if (InstanceFinder.IsServerStarted)
            InstanceFinder.ServerManager.Broadcast(new ScenarioEndBroadcast());
    }

    // ── 더미 모듈 ─────────────────────────

    private class DummyTaskModule : ITaskModule
    {
        public string ModuleId { get; }

        public DummyTaskModule(string moduleId) { ModuleId = moduleId; }

        public void OnStart(ModuleConfig config, System.Action onComplete, System.Action onFail) { }
        public void OnUpdate(float deltaTime) { }
        public void OnComplete() { }
        public void OnFail() { }
    }

    // ── Broadcast ─────────────────────────

    private void BroadcastState()
    {
        if (!InstanceFinder.IsServerStarted) return;

        var broadcast = new TaskStateBroadcast
        {
            currentTask = _currentTaskState,
            totalTaskCount = _data?.scenario.tasks.Count ?? 0,
        };

        InstanceFinder.ServerManager.Broadcast(broadcast);
    }

    // ── 디버그 커맨드용 public API ─────────

    public void ForceCompleteCurrentTask() => CompleteCurrentTask();
    public void ForceFailCurrentTask() => FailCurrentTask();

    public void JumpToTask(int index)
    {
        _retryCount = 0;
        _currentModule?.OnFail();
        _spawner?.DespawnCurrentTask();
        StartTask(index);
    }

    public TaskState GetCurrentSnapshot() => _currentTaskState;

    public int    CurrentIndex    => _currentTaskState.taskIndex;
    public int    TotalCount      => _data?.scenario.tasks.Count ?? 0;
    public string CurrentModuleId => (TotalCount > 0 && CurrentIndex < TotalCount)
        ? _data.scenario.tasks[CurrentIndex].moduleId
        : string.Empty;

    // ── 디버그 상태 조회 ──────────────────

    public string GetDebugStatus()
    {
        if (_data == null) return "[ScenarioRunner] 시나리오 없음";

        var taskDef = _data.scenario.tasks[_currentTaskState.taskIndex];
        return $"[ScenarioRunner] Task[{_currentTaskState.taskIndex}] " +
               $"| 상태: {_currentTaskState.status} " +
               $"| 재도전: {_retryCount}{(_maxRetries > 0 ? $"/{_maxRetries}" : "")} " +
               $"| 모듈: {taskDef.moduleId} " +
               $"| 경과: {_currentTaskState.elapsedTime:F1}s";
    }
}