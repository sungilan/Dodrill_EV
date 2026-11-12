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
    // 현재 단계가 시작된 서버 시간 — 리프트 버튼을 '이 단계 중에' 누른 사람을 가리는 용도
    private float _taskStartTime;

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

    // 평가: 사람별 점수·감점 기록 (서버가 판정, 규칙은 Resources/Evaluation/evaluation_rules.json)
    private readonly EvaluationService _eval = new EvaluationService();
    public EvaluationService Evaluation => _eval;

    /// <summary>감점이 기록되면 그 사람에게만 알린다(점수판·알림). 문구는 보내지 않고 규칙 ID 만 보낸다.</summary>
    private void HandleEvalRecorded(int clientId, EvalEntry entry, PlayerEval player)
    {
        if (!InstanceFinder.IsServerStarted) return;
        if (!InstanceFinder.ServerManager.Clients.TryGetValue(clientId, out var conn) || conn == null || !conn.IsValid) return;

        InstanceFinder.ServerManager.Broadcast(conn, new EvalPenaltyBroadcast
        {
            ruleId = entry.ruleId,
            points = entry.points,
            score = player.score,
            fatal = player.fatal,
        });

        // 치명 위반: 이 사람만 훈련 종료. 결과를 바로 보내고, 남은 사람들의 진행을 막지 않도록 '모두 해야 하는' 판정에서 뺀다.
        if (player.fatal)
        {
            SendResult(clientId, player, _eval.ElapsedSeconds);
            if (_isRunning && _currentModule is GrabAllItemsModule perPlayer && perPlayer.PerPlayer)
                perPlayer.Recheck(GetRequiredClientIds());
            BroadcastState();
        }
    }

    /// <summary>
    /// 치명 위반으로 탈락한 사람인지. 탈락한 사람은 사고 팝업에서 '취소'를 눌러 화면에 남아 있을 수 있는데,
    /// 그 사람의 조작(존·확인·측정 신호)으로 단계가 진행되면 안 된다.
    /// </summary>
    private bool IsEliminated(NetworkConnection conn)
        => conn != null && _eval.Players.TryGetValue(conn.ClientId, out var p) && p.fatal;

    /// <summary>이미 결과를 보낸 사람(치명 탈락) — 시나리오 끝에 다시 보내지 않는다</summary>
    private readonly HashSet<int> _resultSent = new HashSet<int>();

    private void SendResult(int clientId, PlayerEval player, float elapsed)
    {
        if (!_resultSent.Add(clientId)) return;
        if (!InstanceFinder.ServerManager.Clients.TryGetValue(clientId, out var conn) || conn == null || !conn.IsValid) return;

        InstanceFinder.ServerManager.Broadcast(conn, new EvalResultBroadcast
        {
            score = player.score,
            grade = EvaluationService.GradeOf(player.score, player.fatal),
            fatal = player.fatal,
            lines = EvaluationService.BuildLines(player),
            totalSeconds = Mathf.RoundToInt(elapsed),
        });
    }

    /// <summary>접속 중이고 치명 탈락하지 않은 사람 — '모두 해야 하는' 단계에서 기다려야 할 사람들</summary>
    private List<int> GetRequiredClientIds(int excludeId = -1)
    {
        var list = GetConnectedClientIds(excludeId);
        list.RemoveAll(id => _eval.Players.TryGetValue(id, out var p) && p.fatal);
        return list;
    }

    // ── 안전 사고 (평가) ─────────────────────────────────────

    private const string GlovesModuleId = "LOTO_Gloves";
    private const string MsdModuleId = "MSD_Removal";

    /// <summary>
    /// 시나리오에서 moduleId 단계를 이 사람(clientId)이 마쳤는지. 시나리오에 그 단계가 없으면 '마친 것'으로 본다(잘못된 치명 판정 방지).
    /// 보호구 단계는 사람별로 확인하므로, 지금 그 단계라면 이 사람이 이미 착용했는지(_completedClients)도 본다.
    /// </summary>
    private bool HasPassedModule(string moduleId, int clientId)
    {
        var tasks = _data?.scenario?.tasks;
        if (tasks == null) return true;
        int index = -1;
        for (int i = 0; i < tasks.Count; i++)
            if (tasks[i] != null && string.Equals(tasks[i].moduleId, moduleId, StringComparison.OrdinalIgnoreCase)) { index = i; break; }
        if (index < 0) return true;

        int current = _currentTaskState.taskIndex;
        if (current > index) return true;
        if (current < index) return false;
        return _currentTaskState.status == TaskStatus.Completed || _completedClients.Contains(clientId);
    }

    /// <summary>
    /// 클라이언트에서 안전 사고가 났다는 신호. 클라이언트 판단을 믿지 않고 서버 진행 상태로 다시 판정한다:
    ///   MSD 차단 전 → 치명(SAFE_HV_BEFORE_CUTOFF), 차단 후 보호구 미착용 → 중결점(SAFE_NO_PPE), 둘 다 마쳤으면 기록하지 않음.
    /// </summary>
    private void OnReceiveSafetyAccident(NetworkConnection conn, SafetyAccidentSignal signal, Channel channel)
    {
        if (conn == null || !conn.IsValid || _data == null || _currentTaskState.status != TaskStatus.Running) return;
        int id = conn.ClientId;
        string detail = $"{signal.partName} 접촉";

        if (!HasPassedModule(MsdModuleId, id))
            RecordViolation(conn, "SAFE_HV_BEFORE_CUTOFF", detail + " (MSD 차단 전)");
        else if (!HasPassedModule(GlovesModuleId, id))
            RecordViolation(conn, "SAFE_NO_PPE", detail + " (절연 장갑 미착용)");
        else
            Debug.LogWarning($"[Eval] Client#{id} 사고 신호({signal.partName})를 받았지만 서버 기준으로는 안전 상태 → 기록 안 함(클라이언트 상태가 늦게 갱신됐을 가능성)");
    }

    /// <summary>
    /// 평가 모드에서 힌트 요청: 남은 횟수(EvaluationMode.MaxHints)가 있으면 HINT_USED 로 감점한다.
    /// 감점 알림을 받은 클라이언트가 그 단계 가이드를 보여 준다. 같은 단계에서 다시 누르면 규칙(oncePerTask)상 감점되지 않는다.
    /// </summary>
    private void OnReceiveHintRequest(NetworkConnection conn, EvalHintRequest request, Channel channel)
    {
        if (conn == null || !conn.IsValid || !EvaluationMode.IsEvaluation(_data)) return;
        if (_currentTaskState.status != TaskStatus.Running) return;

        var player = _eval.Get(conn.ClientId);
        int used = 0;
        foreach (var e in player.entries)
            if (e.ruleId == EvaluationMode.HintRuleId) used++;
        if (used >= EvaluationMode.MaxHints)
        {
            Debug.Log($"[Eval] Client#{conn.ClientId} 힌트 요청 거절 — 이미 {used}회 사용");
            return;
        }
        RecordViolation(conn, EvaluationMode.HintRuleId, $"힌트 요청 (클라이언트 단계 {request.taskIndex})");
    }

    /// <summary>
    /// 시나리오가 끝났을 때: 요약 로그, 사람별 CSV 저장(서버), 접속 중인 사람에게 자기 결과 전송.
    /// 중간에 나간 사람도 기록이 있으면 CSV 에는 남긴다.
    /// 훈련 모드는 점수를 매기지 않으므로 로그만 남긴다(치명 탈락 처리는 모드와 관계없이 HandleEvalRecorded 에서).
    /// </summary>
    private void FinishEvaluation()
    {
        Debug.Log(_eval.BuildSummary());
        if (!InstanceFinder.IsServerStarted) return;
        if (!EvaluationMode.IsEvaluation(_data))
        {
            Debug.Log("[Eval] 훈련 모드 — 결과 화면·CSV 없이 종료");
            return;
        }

        float elapsed = _eval.ElapsedSeconds;
        string scenarioId = _data != null ? _data.scenarioId : string.Empty;

        var ids = new HashSet<int>(GetConnectedClientIds());
        foreach (int id in _eval.Players.Keys) ids.Add(id);

        foreach (int id in ids)
        {
            var player = _eval.Get(id);   // 감점이 없던 사람은 시작 점수로 만들어진다

            string name = null;
            SessionPlayerServer.TryGetDisplayName(id, out name);
            if (string.IsNullOrEmpty(name)) name = $"Player {id + 1}";
            EvaluationResultExporter.AppendRow(scenarioId, name, player, elapsed);
            SendResult(id, player, elapsed);   // 치명 탈락으로 이미 받은 사람은 건너뛴다
        }
    }

    /// <summary>
    /// 존 신호의 '실제로 행동한 사람'을 찾는다. 못 찾으면 -1 (그러면 감점하지 않는다).
    /// 존 신호는 연결(conn)로 사람을 알 수 없다 — 누가 하나를 눌러도 모든 클라이언트가 같은 신호를 각자 보내고,
    /// 스폰된 아이템이 존에 겹치기만 해도 모두가 보낸다. 그래서:
    ///   1) 아이템이 들어간 신호: 그 아이템을 지금 들고 있는 사람
    ///   2) 눌러서 작동하는 오브젝트(후드 등): 서버가 요청을 받은 사람(ClickableAnimatorManager)
    ///   3) 둘 다 아니면(스폰된 아이템이 존에 겹친 경우 등) 누구의 잘못도 아니다
    /// </summary>
    private int ResolveActor(ZoneInteractionSignal signal)
    {
        if (!string.IsNullOrEmpty(signal.itemId))
        {
            foreach (var grab in FindObjectsByType<SyncGrab>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (grab == null || !grab.IsGrabbed || grab.Owner == null || !grab.Owner.IsValid) continue;
                var item = grab.GetComponent<TaskItem>();
                if (item != null && item.prefabId == signal.itemId) return grab.Owner.ClientId;
            }
            return -1;
        }

        return ClickableAnimatorManager.TryGetRecentActor(signal.zoneId, 3f, out int actor) ? actor : -1;
    }

    /// <summary>존 신호 위반(순서 위반)을 실제로 행동한 사람에게 기록한다.</summary>
    private void RecordZoneViolation(ZoneInteractionSignal signal, string ruleId, string detail)
    {
        if (_currentTaskState.status != TaskStatus.Running) return;
        int actor = ResolveActor(signal);
        if (actor < 0) return;
        _eval.Record(actor, ruleId, _currentTaskState.taskIndex, detail);
    }

    /// <summary>접속(연결)이 유효한 사람의 위반을 기록한다. 클라이언트가 보낸 ID 는 믿지 않고 연결의 ClientId 만 쓴다.</summary>
    private void RecordViolation(NetworkConnection conn, string ruleId, string detail)
    {
        if (conn == null || !conn.IsValid || _currentTaskState.status != TaskStatus.Running) return;
        _eval.Record(conn.ClientId, ruleId, _currentTaskState.taskIndex, detail);
    }

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
            InstanceFinder.ServerManager.RegisterBroadcast<SafetyAccidentSignal>(OnReceiveSafetyAccident);
            InstanceFinder.ServerManager.RegisterBroadcast<EvalHintRequest>(OnReceiveHintRequest);
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
        // 예전에는 이 경로에서 ValueMeasuredSignal 이 빠져, 서버보다 먼저 활성화되는 구성에서는 측정 단계(8단계)가 진행 불가였다
        InstanceFinder.ServerManager.RegisterBroadcast<ValueMeasuredSignal>(OnReceiveValueMeasured);
            InstanceFinder.ServerManager.RegisterBroadcast<SafetyAccidentSignal>(OnReceiveSafetyAccident);
            InstanceFinder.ServerManager.RegisterBroadcast<EvalHintRequest>(OnReceiveHintRequest);
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
            InstanceFinder.ServerManager.UnregisterBroadcast<SafetyAccidentSignal>(OnReceiveSafetyAccident);
            InstanceFinder.ServerManager.UnregisterBroadcast<EvalHintRequest>(OnReceiveHintRequest);
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
        _eval.Reset();   // 평가 점수·감점 기록 초기화
        _resultSent.Clear();
        _eval.OnRecorded -= HandleEvalRecorded;
        _eval.OnRecorded += HandleEvalRecorded;

        Debug.Log($"[ScenarioRunner] 시나리오 시작 — {data.scenarioName}, Tasks: {data.scenario.tasks.Count}, 모드: {(EvaluationMode.IsEvaluation(data) ? "평가" : "훈련")}");
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
            FinishEvaluation();   // 평가 요약 로그 + CSV 저장 + 사람별 결과 전송
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
        _taskStartTime = Time.time;
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
        // 드릴처럼 여럿이 동시에 쓰는 공구는 지금 참여 중인 인원(탈락자 제외)만큼 놓는다
        _spawner?.SpawnForTask(taskDef, GetRequiredClientIds().Count);

        // GrabUse / GrabAll 계열은 클릭/터치 후 1.5s 딜레이 (Zone은 OnReceiveZoneSignal이 직접 처리)
        bool needsDelay = module is GrabUseTaskModule || module is GrabAllItemsModule;
        System.Action onComplete = needsDelay
            ? () =>
            {
                // 착용·잡기가 끝난 물건은 손에 멈춘 채 1.5초 떠 있지 않게 바로 치운다
                _spawner?.DespawnUsedItemsSoon(null, 0f);
                StartCoroutine(CompleteAfterSnapDelay(1.5f));
            }
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

        // 진행자를 아직 모르면(후드 래치·12V 커버처럼 서버가 클릭 상태를 적용하면서 바로 완료되는 단계)
        // 방금 그 오브젝트를 누른 사람을 진행자로 쓴다. 예전에는 비어 있어서 완료 알림이 'Player' 로 떴다.
        if(_activePlayerId < 0 && !string.IsNullOrEmpty(config?.targetObjName) &&
           ClickableAnimatorManager.TryGetRecentActor(config.targetObjName, 5f, out int clicker))
            _activePlayerId = clicker;

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
        // 이름을 못 찾으면 비워 보낸다 → 클라이언트가 접속자 목록과 같은 규칙('Player N')으로 표시 (예전엔 항상 'Player 1')
        string activePlayerName = string.Empty;

        // 리프트 상승 단계는 높이가 목표에 닿으면 서버가 끝내므로 진행자가 비어 있다 → 이 단계 중에 리프트 UP 버튼을 누른 사람을 진행자로 쓴다.
        // 방전 대기처럼 누르는 사람이 없는 단계는 그대로 비워 둔다(알림에는 단계 이름만 표시).
        if(_activePlayerId < 0 && IsLiftModule(taskDef.moduleId) && TryGetLiftActor(taskDef.moduleId, out int liftActor))
            _activePlayerId = liftActor;
        int notifyId = _activePlayerId;

        // 보호구 착용처럼 '사람마다' 해야 하는 단계는 마지막에 끝낸 한 명의 이름 대신 '모든 작업자'로 알린다(접속자 2명 이상일 때). clientId -2 = 모두.
        if(_currentModule is GrabAllItemsModule everyoneModule && everyoneModule.PerPlayer && GetConnectedClientIds().Count > 1)
            notifyId = -2;

       var sessionServer = FindFirstObjectByType<SessionPlayerServer>();
        if(sessionServer != null)
        {
            // 리플렉션을 통해 private 필드인 _players에 접근
            var field = typeof(SessionPlayerServer).GetField("_players",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if(field != null)
            {
                var playersDict = field.GetValue(sessionServer) as Dictionary<int, SessionPlayerEntry>;
                if(playersDict != null && playersDict.TryGetValue(notifyId, out var entry))
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
                clientId = notifyId,
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

    private static bool IsLiftModule(string moduleId)
        => !string.IsNullOrEmpty(moduleId) && moduleId.IndexOf("Lift_Up", System.StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>리프트 상승 단계(차량 Car_Lift_Up / 배터리 잭 BatteryJack_Lift_Up…)를 시작한 뒤 해당 리프트 UP 버튼을 누른 사람.</summary>
    private bool TryGetLiftActor(string moduleId, out int clientId)
    {
        clientId = -1;
        bool battery = moduleId.IndexOf("Battery", System.StringComparison.OrdinalIgnoreCase) >= 0;
        foreach(var lift in FindObjectsByType<LiftControllerBase>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if(battery != (lift is BatteryLiftController)) continue;
            if(lift.TryGetMoveActor(_taskStartTime, out clientId)) return true;
        }
        clientId = -1;
        return false;
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
        _spawner?.DespawnCurrentTask(currentTaskDef, failed: true);
        _spawner?.ReturnLooseItemsHome();   // 떨어뜨린 도구(이전 단계에서 가져온 드릴 등)도 제자리로
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
        // 클라이언트가 '이 단계 끝났다'고 직접 알리는 신호는 받지 않는다 — 이 신호를 보내는 정식 코드는 없고(SendCompleteSignal 은 호출처 없음),
        // 받아 주면 수정된 클라이언트가 아무 단계나 강제로 넘길 수 있다. 완료는 서버가 신호(존·아이템·확인·측정)로 직접 판정한다.
        // 디버그 모드(에디터·Development Build)에서만 허용.
        if (!DebugBootstrapper.IsDebugMode)
        {
            Debug.LogWarning($"[ScenarioRunner] 클라이언트 완료 신호 무시 (Client#{(conn != null ? conn.ClientId : -1)}, Task[{signal.taskIndex}])");
            return;
        }
        if (IsEliminated(conn)) return;   // 치명 위반으로 탈락한 사람의 조작은 진행에 반영하지 않는다
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
        if (IsEliminated(conn)) return;   // 치명 위반으로 탈락한 사람의 조작은 진행에 반영하지 않는다
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
        if (IsEliminated(conn)) return;   // 치명 위반으로 탈락한 사람의 조작은 진행에 반영하지 않는다
        if (!_isRunning) return;
        if (signal.taskIndex != _currentTaskState.taskIndex) return;

        // 보호구처럼 '사람마다' 해야 하는 단계: 누가 집었는지 서버가 연결(conn)의 ClientId 로 직접 센다.
        // (예전에는 한 명만 집어도 모두가 다음 단계로 넘어갔다)
        if (_currentModule is GrabAllItemsModule perPlayer && perPlayer.PerPlayer)
        {
            HandlePerPlayerGrab(conn, signal, perPlayer);
            return;
        }

        RegisterActivePlayer(conn, signal.clientId);
        InteractionEvents.FireItemGrabbed(signal.itemId);
    }

    private void HandlePerPlayerGrab(NetworkConnection conn, ItemGrabbedSignal signal, GrabAllItemsModule module)
    {
        // 클라이언트가 보낸 clientId 는 믿지 않는다 — 서버가 아는 연결의 ClientId 만 쓴다
        if (conn == null || !conn.IsValid) return;
        int id = conn.ClientId;
        var required = GetRequiredClientIds();   // 치명 탈락한 사람은 기다리지 않는다

        var result = module.NotifyGrabbedBy(id, signal.itemId, required);
        if (result == GrabAllItemsModule.GrabResult.Ignored) return;

        RegisterActivePlayer(conn, signal.clientId);

        if (result == GrabAllItemsModule.GrabResult.PlayerCompleted)
        {
            _completedClients.Add(id);   // 완료한 사람 목록(TaskState.completedClientIds)으로 클라이언트에 알림
            BroadcastState();
        }

        // 아직 끝나지 않은 사람이 있으면: 방금 착용한 사람 손의 아이템을 치우고 같은 자리에 새 아이템을 놓는다.
        // 아이템이 한 개뿐이면 한 명이 들고 있는 동안 다른 사람이 집을 수 없어 다음 단계로 못 간다.
        if (result != GrabAllItemsModule.GrabResult.Counted && !module.IsCompleted)
            ReplaceHeldItem(id, signal.itemId);
    }

    /// <summary>clientId 가 지금 들고 있는 itemId 아이템을 치우고 새 아이템을 스폰한다.</summary>
    private void ReplaceHeldItem(int clientId, string itemId)
    {
        if (_spawner == null || _data == null) return;

        foreach (var grab in FindObjectsByType<SyncGrab>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (grab == null || !grab.IsGrabbed) continue;
            // 같은 착용 신호가 두 번 오는 경우(브리지 + 장비 동기화)에 방금 치운 아이템을 또 치우고 새로 놓지 않게
            if (grab.NetworkObject == null || !grab.NetworkObject.IsSpawned) continue;
            if (grab.Owner == null || !grab.Owner.IsValid || grab.Owner.ClientId != clientId) continue;
            var item = grab.GetComponent<TaskItem>();
            if (item == null || item.prefabId != itemId) continue;

            var taskDef = _data.scenario.tasks[_currentTaskState.taskIndex];
            _spawner.ReplaceTaskItem(taskDef, grab.NetworkObject, itemId);
            Debug.Log($"[ScenarioRunner] Client#{clientId} 이(가) 착용한 {itemId} 을(를) 치우고 다음 사람용으로 새로 배치");
            return;
        }
    }

    /// <summary>지금 접속 중인 모든 클라이언트 ID (excludeId 는 방금 나간 사람)</summary>
    private List<int> GetConnectedClientIds(int excludeId = -1)
    {
        var list = new List<int>();
        if (InstanceFinder.ServerManager == null) return list;
        foreach (var kv in InstanceFinder.ServerManager.Clients)
        {
            var c = kv.Value;
            if (c != null && c.IsValid && c.ClientId != excludeId) list.Add(c.ClientId);
        }
        return list;
    }

    private void OnReceiveTaskConfirm(NetworkConnection conn, TaskConfirmSignal signal, Channel channel)
    {
        if (IsEliminated(conn)) return;   // 치명 위반으로 탈락한 사람의 조작은 진행에 반영하지 않는다
        if (!_isRunning) return;
        if (signal.taskIndex != _currentTaskState.taskIndex) return;

        // [검증] 요청된 moduleId가 현재 태스크와 일치하는지 확인
        var taskDef = _data.scenario.tasks[_currentTaskState.taskIndex];
        if(!string.IsNullOrEmpty(signal.moduleId) && !signal.moduleId.Equals(taskDef.moduleId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // 평가: 조건(방전 대기 시간 등)을 채우기 전에 완료를 시도했으면 감점 (완료 자체는 모듈이 거부한다)
        if (_currentModule is ConfirmTaskModule confirmModule && !confirmModule.CanConfirmNow &&
            !string.IsNullOrEmpty(confirmModule.EarlyConfirmRuleId))
            RecordViolation(conn, confirmModule.EarlyConfirmRuleId, $"{taskDef.moduleId} 조건을 채우기 전에 완료 시도");

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
        if (IsEliminated(conn)) return;   // 치명 위반으로 탈락한 사람의 조작은 진행에 반영하지 않는다
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
            if(!string.IsNullOrEmpty(stepZoneId) && stepZoneId != signal.zoneId)
            {
                RecordZoneViolation(signal, "PROC_ORDER", $"{taskDef.moduleId} 단계 존 불일치: 기대 {stepZoneId} / 접촉 {signal.zoneId}");
                return;
            }

            // [검증] 단계별 요구 아이템 확인
            var stepItems = stepModule.CurrentStepRequiredItems;
            bool validStep = stepItems.Count == 0 || stepItems.Contains(signal.itemId);
            if(!validStep)
            {
                RecordZoneViolation(signal, "PROC_ORDER", $"{taskDef.moduleId} 단계 아이템 불일치: {signal.itemId}");
                return;
            }

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
            {
                // 제자리에 끼워지는 연출(snapDuration)만 보여 주고 바로 치운다 — 다음 단계까지 떠 있지 않게
                _spawner?.DespawnUsedItemsSoon(signal.itemId, snapDuration + 0.1f);
                StartCoroutine(CompleteAfterSnapDelay(snapDuration + snapDelay));
            }
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
            RecordZoneViolation(signal, "PROC_ORDER", $"{taskDef.moduleId} 오답 존: 기대 {officialTargetId} / 접촉 {signal.zoneId}");
            return;
        }

        // 2. [검증] 아이템 일치 여부 확인
        var required = config?.requiredItems;
        bool validItem = required == null || required.Count == 0 || required.Contains(signal.itemId);
        if(!validItem)
        {
            Debug.LogWarning($"[ScenarioRunner] 잘못된 아이템 사용 무시: {signal.itemId}");
            RecordZoneViolation(signal, "PROC_ORDER", $"{taskDef.moduleId} 잘못된 아이템: {signal.itemId}");
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

        // 제자리에 끼워지는 연출(snapDuration)만 보여 주고 바로 치운다 — 다음 단계까지 떠 있지 않게
        _spawner?.DespawnUsedItemsSoon(signal.itemId, snapDurationSingle + 0.1f);
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
        if (IsEliminated(conn)) return;   // 치명 위반으로 탈락한 사람의 조작은 진행에 반영하지 않는다
        Debug.Log($"<color=white>[Runner-Check]</color> 서버에 신호 도착함! Task:{signal.taskIndex}");

        // ── [1단계] 서버 상태 확인 ──
        if(!_isRunning || _taskLocked || _currentModule == null)
        {
            Debug.LogWarning($"<color=orange>[Runner-Measure]</color> 신호 무시: 서버 상태 비정상 (Running:{_isRunning}, Locked:{_taskLocked})");
            return;
        }

        // ── [2단계] 인덱스 일치 확인 ──
        if(signal.taskIndex != _currentTaskState.taskIndex)
        {
            Debug.LogWarning($"<color=yellow>[Runner-Measure]</color> 인덱스 불일치: 신호Index({signal.taskIndex}) vs 서버Index({_currentTaskState.taskIndex})");
            return;
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
        // 치명 탈락한 사람은 '모두 완료' 판정에서 뺀다 (남은 사람들이 그 사람을 기다리느라 멈추지 않게)
        int fatalConnected = 0;
        foreach (var p in _eval.Players.Values)
            if (p.fatal && InstanceFinder.ServerManager != null && InstanceFinder.ServerManager.Clients.ContainsKey(p.clientId)) fatalConnected++;
        int total = Mathf.Max(1, _connectedClientCount - fatalConnected);

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
        {
            _connectedClientCount = Mathf.Max(0, InstanceFinder.ServerManager.Clients.Count);

            // 나간 사람 때문에 '모두 착용' 조건이 이미 충족되는 경우를 놓치지 않게 다시 확인
            if (_isRunning && _currentModule is GrabAllItemsModule perPlayer && perPlayer.PerPlayer)
            {
                _completedClients.Remove(conn.ClientId);
                perPlayer.Recheck(GetRequiredClientIds(conn.ClientId));
            }
        }
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

    /// <summary>디버그: 지정한 사람에게 감점을 기록한다(점수판·결과 화면 확인용). 규칙이 꺼져 있으면 기록되지 않는다.</summary>
    public void DebugRecordViolation(int clientId, string ruleId)
    {
        _eval.Record(clientId, ruleId, _currentTaskState.taskIndex, "디버그 명령");
    }

    /// <summary>디버그: 지금 시나리오가 끝난 것처럼 평가 결과를 보낸다(결과 화면 확인용). 시나리오는 계속 진행된다.</summary>
    public void DebugSendEvaluationResults() => FinishEvaluation();
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