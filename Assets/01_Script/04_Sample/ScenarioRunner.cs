using DoDrill;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing.Scened;
using FishNet.Transporting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;


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

    // [추가] 현재 태스크를 진행 중인 클라이언트 ID
    private int _activePlayerId = -1;

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
            InstanceFinder.ServerManager.RegisterBroadcast<ValueMeasuredSignal>(OnReceiveValueMeasured);
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
            InstanceFinder.ServerManager.UnregisterBroadcast<ValueMeasuredSignal>(OnReceiveValueMeasured);
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
            // [수정] 현재 시나리오가 프리모드라면 종료하지 않고 리턴
            if(_data.scenarioId == "ev_free_mode")
            {
                Debug.Log("[ScenarioRunner] 자유 모드 유지 중...");
                return;
            }

            Debug.Log("[ScenarioRunner] 모든 Task 완료 — 1.5초 후 종료");
            _isRunning = false;
            StartCoroutine(QuitAfterDelay(1.5f));
            return;
        }

        var taskDef = _data.scenario.tasks[index];
        var config = _data.GetModuleConfig(taskDef.moduleId);
        var module = _bootstrapper.Registry.Get(taskDef.moduleId);
        var moduleId = taskDef.moduleId;

        if (module == null)
        {
            Debug.LogWarning($"[ScenarioRunner] 모듈 없음 — Task[{index}] 더미 모듈로 대기 중 (ForceComplete 로 넘길 수 있음)");
            module = new DummyTaskModule(taskDef.moduleId);
        }

        // 서버가 모든 클라이언트에게 미니맵 업데이트 신호를 보냄
        var msg = new UpdateMiniMapBroadcast
        {
            requiredItemIds = config.requiredItems,
            targetZoneId = config.targetZoneId
        };

        _completedClients.Clear();
        _taskLocked = false;
        _activePlayerId = -1;  // [추가] 새 태스크마다 초기화
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
        if(!_isRunning) return;

        // 1. 완료된 태스크의 정보(Title) 가져오기
        var taskDef = _data.scenario.tasks[_currentTaskState.taskIndex];
        var config = _data.GetModuleConfig(taskDef.moduleId);

        string taskTitle = string.Empty;
        if(config != null && config.extra != null)
        {
            foreach(var kv in config.extra)
            {
                if(kv.key == "title")
                {
                    taskTitle = kv.GetLocalized();
                    break;
                }
            }
        }
        if(string.IsNullOrEmpty(taskTitle)) taskTitle = taskDef.moduleId;

        // 2. SessionPlayerServer의 private _players 딕셔너리에서 이름 가져오기 (리플렉션 사용)
        string activePlayerName = "알 수 없는 플레이어";

        // 씬에 있는 SessionPlayerServer 인스턴스를 찾습니다.
        var sessionServer = FindFirstObjectByType<SessionPlayerServer>();
        if(sessionServer != null)
        {
            // 리플렉션을 통해 private 필드인 _players에 접근
            var field = typeof(SessionPlayerServer).GetField("_players",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if(field != null)
            {
                var playersDict = field.GetValue(sessionServer) as Dictionary<int, SessionPlayerEntry>;
                if(playersDict != null && playersDict.TryGetValue(_activePlayerId, out var entry))
                {
                    activePlayerName = entry.displayName;
                }
            }
        }

        // 3. 모든 클라이언트에게 알림 브로드캐스트 전송
        if(InstanceFinder.IsServerStarted)
        {
            InstanceFinder.ServerManager.Broadcast(new TaskNotificationBroadcast
            {
                clientId = _activePlayerId,
                clientName = activePlayerName,
                taskName = taskTitle
            });
        }

        // --- 기존 로직 유지 ---
        BroadcastSound("Task_Success", 1.0f);
        _currentModule?.OnComplete();
        _currentTaskState.status = TaskStatus.Completed;
        _retryCount = 0;

        var currentTaskDef = _data.scenario.tasks[_currentTaskState.taskIndex];
        _spawner?.DespawnCurrentTask(currentTaskDef);
        BroadcastState();

        Debug.Log($"[ScenarioRunner] Task[{_currentTaskState.taskIndex}] 완료 - 진행자: {activePlayerName}");

        int next = _currentTaskState.taskIndex + 1;
        StartTask(next);
    }

    private void FailCurrentTask()
    {
        if (!_isRunning) return;
        BroadcastSound("Task_Fail", 1.0f);
        _currentModule?.OnFail();
        _currentTaskState.status = TaskStatus.Failed;
        _retryCount++;
        _isRunning = false; // 딜레이 중 중복 호출 방지

        // 실패한 Task 오브젝트 디스폰 (재도전 시 StartTask에서 다시 스폰됨)
        var currentTaskDef = _data.scenario.tasks[_currentTaskState.taskIndex];
        _spawner?.DespawnCurrentTask(currentTaskDef);
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

        // 클라이언트가 보낸 clientId는 신뢰하지 않음 — 데디케이티드 서버에서 연결의 ClientId만 사용
        int id = conn.IsValid ? conn.ClientId : signal.clientId;
        _completedClients.Add(id);

        // 첫 인터랙션 → 진행자 등록 (아직 아무도 시작 안 했으면)
        if(_activePlayerId < 0)
            _activePlayerId = id;

        // 완료자 목록을 즉시 동기화 (activePlayerId 갱신용)
        BroadcastState();

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

        // [검증] 현재 태스크에서 요구하는 아이템인지 확인 (선택 사항이지만 권장)
        var config = _data.GetModuleConfig(_data.scenario.tasks[signal.taskIndex].moduleId);
        bool isValidItem = config?.requiredItems == null || config.requiredItems.Count == 0
                           || config.requiredItems.Contains(signal.itemId);

        if(isValidItem)
        {
            // 정답 아이템을 사용했을 때만 진행자 등록 및 UI 불 켜기
            RegisterActivePlayer(conn, signal.clientId);
            InteractionEvents.FireItemUsed(signal.itemId);
        }
    }

    private void OnReceiveItemGrabbed(NetworkConnection conn, ItemGrabbedSignal signal, Channel channel)
    {
        if (!_isRunning) return;
        if (signal.taskIndex != _currentTaskState.taskIndex) return;
        RegisterActivePlayer(conn, signal.clientId);
        InteractionEvents.FireItemGrabbed(signal.itemId);
    }

    private void OnReceiveTaskConfirm(NetworkConnection conn, TaskConfirmSignal signal, Channel channel)
    {
        if (!_isRunning) return;
        if (signal.taskIndex != _currentTaskState.taskIndex) return;

        // [검증] 요청된 moduleId가 현재 태스크와 일치하는지 확인
        var taskDef = _data.scenario.tasks[_currentTaskState.taskIndex];
        if(!string.IsNullOrEmpty(signal.moduleId) && !signal.moduleId.Equals(taskDef.moduleId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // 올바른 확인(Confirm) 신호일 때만 진행자 등록 및 UI 불 켜기
        RegisterActivePlayer(conn, signal.clientId);
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
        // 1. 기본 상태 검증
        if(_taskLocked || !_isRunning) return;
        if(signal.taskIndex != _currentTaskState.taskIndex) return;

        var taskDef = _data.scenario.tasks[_currentTaskState.taskIndex];
        var config = _data.GetModuleConfig(taskDef.moduleId);

        // ── [공통] 타겟 ID 결정 로직 ─────────────────
        // JSON에 targetZoneId가 적혀있으면 그것을 쓰고, 비어있으면 targetObjName을 정답으로 간주
        string officialTargetId = string.IsNullOrEmpty(config?.targetZoneId) ? config?.targetObjName : config?.targetZoneId;

        // ── IStepAwareModule 경로 (다단계 Task) ─────────────────
        if(_currentModule is IStepAwareModule stepModule && stepModule.IsCurrentStepGrabZone)
        {
            // [검증] 단계별 존 ID 확인
            string stepZoneId = stepModule.CurrentStepZoneId;
            if(!string.IsNullOrEmpty(stepZoneId) && stepZoneId != signal.zoneId) return;

            // [검증] 단계별 요구 아이템 확인
            var stepItems = stepModule.CurrentStepRequiredItems;
            bool validStep = stepItems.Count == 0 || stepItems.Contains(signal.itemId);
            if(!validStep) return;

            // [핵심 추가] 검증 통과 후 진행자 등록 및 UI 갱신
            RegisterActivePlayer(conn, signal.clientId);

            _taskLocked = true;

            float snapDuration = stepModule.CurrentStepSnapDuration;
            float snapDelay = stepModule.CurrentStepSnapDelay;
            string guideId = string.IsNullOrEmpty(stepModule.CurrentStepGuideId) ? signal.zoneId : stepModule.CurrentStepGuideId;

            if(InstanceFinder.IsServerStarted)
            {
                InstanceFinder.ServerManager.Broadcast(new PlaySnapAnimBroadcast
                {
                    taskIndex = signal.taskIndex,
                    itemId = signal.itemId,
                    guideId = guideId,
                    snapDuration = snapDuration,
                });
            }

            bool isLastStep = stepModule.AdvanceZoneStep();
            if(isLastStep)
                StartCoroutine(CompleteAfterSnapDelay(snapDuration + snapDelay));
            else
                StartCoroutine(UnlockAfterStepDelay(snapDuration + snapDelay));

            return;
        }

        // ── 기존 단일 스텝 경로 ─────────────────────

        // 1. [검증] 타겟 ID(Zone 또는 ObjName) 일치 여부 확인
        if(!string.IsNullOrEmpty(officialTargetId) && officialTargetId != signal.zoneId)
        {
            // 오답일 경우 로그만 남기고 조용히 리턴 (UI 불 안 켜짐)
            Debug.LogWarning($"[ScenarioRunner] 오답 존 터치 무시: 서버기다림({officialTargetId}) vs 클라보냄({signal.zoneId})");
            return;
        }

        // 2. [검증] 아이템 일치 여부 확인
        var required = config?.requiredItems;
        bool validItem = required == null || required.Count == 0 || required.Contains(signal.itemId);
        if(!validItem)
        {
            Debug.LogWarning($"[ScenarioRunner] 잘못된 아이템 사용 무시: {signal.itemId}");
            return;
        }

        // 3. [핵심 추가] 모든 검증 통과 시점에 진행자 등록 및 UI 갱신
        RegisterActivePlayer(conn, signal.clientId);

        _taskLocked = true;

        // 존 비활성화 처리
        if(!string.IsNullOrEmpty(config?.targetZoneId))
            TaskInteractionZone.Find(config.targetZoneId)?.Deactivate();

        // 스냅 가이드 찾기 및 연출 브로드캐스트
        string guideIdSingle = string.Empty;
        if(taskDef.spawnObjects != null)
        {
            foreach(var bundle in taskDef.spawnObjects)
            {
                if(bundle.prefabId == signal.itemId && !string.IsNullOrEmpty(bundle.guideId))
                {
                    guideIdSingle = bundle.guideId;
                    break;
                }
            }
        }

        float snapDurationSingle = ParseExtraFloat(config, "snapDuration", 0.5f);
        float snapDelaySingle = ParseExtraFloat(config, "snapDelay", 1.5f);

        if(string.IsNullOrEmpty(guideIdSingle))
            guideIdSingle = signal.zoneId;

        if(InstanceFinder.IsServerStarted)
        {
            InstanceFinder.ServerManager.Broadcast(new PlaySnapAnimBroadcast
            {
                taskIndex = signal.taskIndex,
                itemId = signal.itemId,
                guideId = guideIdSingle,
                snapDuration = snapDurationSingle,
            });
        }

        StartCoroutine(CompleteAfterSnapDelay(snapDurationSingle + snapDelaySingle));

        Debug.Log($"<color=lime>[ScenarioRunner] 단계 완료 승인</color>: zone={signal.zoneId}, task={_currentTaskState.taskIndex}");
    }

    /// <summary>
    /// 유효한 상호작용이 확인된 후 호출하여 진행자를 등록하고 상태를 알립니다.
    /// </summary>
    private void RegisterActivePlayer(NetworkConnection conn, int signalClientId)
    {
        if(_activePlayerId < 0)
        {
            int id = conn.IsValid ? conn.ClientId : signalClientId;
            _activePlayerId = id;
            Debug.Log($"<color=cyan>[ScenarioRunner]</color> 정답 확인됨. 진행자 등록: {id}");
            Debug.Log($"<color=magenta>[Step-Trigger]</color> 단계[{_currentTaskState.taskIndex}] 완료 트리거 발생! " +
                  $"진행자 ID: {id}, 원인: {StackTraceUtility.ExtractStackTrace()}");
            // 진행자 정보가 포함된 상태를 즉시 브로드캐스트하여 UI 불을 켭니다.
            BroadcastState();
        }
    }

    private void OnReceiveValueMeasured(NetworkConnection conn, ValueMeasuredSignal signal, Channel channel)
    {
        Debug.Log($"<color=white>[Runner-Check]</color> 서버에 신호 도착함! Task:{signal.taskIndex}");

        // ── [1단계] 서버 상태 확인 ──
        if(!_isRunning || _taskLocked || _currentModule == null)
        {
            Debug.LogWarning($"<color=orange>[Runner-Measure]</color> 신호 무시: 서버 상태 비정상 (Running:{_isRunning}, Locked:{_taskLocked})");
            //return;
        }

        // ── [2단계] 인덱스 일치 확인 ──
        if(signal.taskIndex != _currentTaskState.taskIndex)
        {
            Debug.LogWarning($"<color=yellow>[Runner-Measure]</color> 인덱스 불일치: 신호Index({signal.taskIndex}) vs 서버Index({_currentTaskState.taskIndex})");
            //return;
        }

        // ── [3단계] 모듈 타입 캐스팅 확인 ──
        if(_currentModule is ZoneAndMeasureModule measureModule)
        {
            Debug.Log($"<color=cyan>[Runner-Measure]</color> <b>검증 시작</b>: 단자={signal.terminalId}, 값={signal.value:F2}");

            // 모듈 내부의 판정 로직 실행
            measureModule.HandleValueMeasured(signal.terminalId, signal.value);

            // ── [4단계] 완료 여부 확인 ──
            if(measureModule.IsCompleted)
            {
                Debug.Log($"<color=lime>[Runner-Measure]</color> <b>✅ 판정 성공</b>: 다음 단계로 전이 시작.");

                // [핵심 추가] 정답 확인 시점에 진행자 등록 및 UI 상태 알림
                RegisterActivePlayer(conn, signal.clientId);

                _taskLocked = true;

                // JSON 설정 로드
                var config = _data.GetModuleConfig(_currentTaskState.taskIndex < _data.scenario.tasks.Count ?
                             _data.scenario.tasks[_currentTaskState.taskIndex].moduleId : "");

                float delay = ParseExtraFloat(config, "snapDelay", 1.5f);

                StartCoroutine(CompleteAfterSnapDelay(delay));
            }
            else
            {
                Debug.LogWarning($"<color=white>[Runner-Measure]</color> 모듈 전달 완료했으나 <b>조건 미충족</b> (ID 불일치 혹은 값 범위 초과)");
            }
        }
        else
        {
            Debug.LogError($"<color=red>[Runner-Measure]</color> 타입 불일치 에러: " +
                           $"현재 모듈은 <b>{_currentModule.GetType().Name}</b>이며, ZoneAndMeasureModule이 아닙니다.");
        }
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

    //private void BroadcastState()
    //{
    //    if (!InstanceFinder.IsServerStarted) return;

    //    var broadcast = new TaskStateBroadcast
    //    {
    //        currentTask = _currentTaskState,
    //        totalTaskCount = _data?.scenario.tasks.Count ?? 0,
    //    };

    //    InstanceFinder.ServerManager.Broadcast(broadcast);
    //    SessionPlayerServer.NotifyActivePlayer(_activePlayerId);
    //}

    private void BroadcastState()
    {
        if(!InstanceFinder.IsServerStarted) return;
        _currentTaskState.completedClientIds = _completedClients.ToList();
        _currentTaskState.activePlayerId = _activePlayerId;
        Debug.Log($"[Runner][BroadcastState] completedClients={string.Join(",", _completedClients)} activePlayerId={_activePlayerId}");
        // TaskState와 동일한 진행자 ID로 세션 플레이어 목록 브로드캐스트 (User-State 패널 배경)
        SessionPlayerServer.NotifyActivePlayer(_activePlayerId);
        InstanceFinder.ServerManager.Broadcast(new TaskStateBroadcast { currentTask = _currentTaskState, totalTaskCount = TotalCount });

        // 데디케이티드 서버: 클라이언트로만 나가는 브로드캐스트를 로컬에서도 한 번 반영 (애니·존 동기화 등).
        if(InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted)
            ScenarioStateReceiver.MirrorServerTaskStateForLocalSubscribers(_currentTaskState, TotalCount);
    }

    // ── 디버그 커맨드용 public API ─────────

    public void ForceCompleteCurrentTask() => CompleteCurrentTask();
    public void ForceFailCurrentTask() => FailCurrentTask();

    public void JumpToTask(int index)
    {
        _retryCount = 0;
        _currentModule?.OnFail();
        var currentTaskDef = _data.scenario.tasks[_currentTaskState.taskIndex];
        _spawner?.DespawnCurrentTask(currentTaskDef);
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

    public struct UpdateMiniMapBroadcast : IBroadcast
    {
        public List<string> requiredItemIds;
        public string targetZoneId;
    }

    public struct PlaySoundBroadcast : IBroadcast
    {
        public string soundName;
        public float volume;
    }

    public struct TaskNotificationBroadcast : FishNet.Broadcast.IBroadcast
    {
        public int clientId;      // 행동을 한 플레이어 ID
        public string clientName;  // 플레이어 이름 (UserInfo.UserName 기반)
        public string taskName;    // 완료한 단계 명칭
    }

    public void BroadcastSound(string soundName, float volume = 1.0f)
    {
        if(!InstanceFinder.IsServerStarted) return;

        InstanceFinder.ServerManager.Broadcast(new PlaySoundBroadcast
        {
            soundName = soundName,
            volume = volume
        });
    }
}