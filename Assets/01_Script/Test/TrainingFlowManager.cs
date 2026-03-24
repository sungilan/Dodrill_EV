using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Connection;

// ============================================================
//  TrainingFlowManager.cs
//  전기차 정비 훈련의 서버 권위(Server Authority) 흐름 관리자.
//
//  기존 ScenarioRunner 와의 대응:
//    ScenarioRunner.StartScenario(data)  ↔  StartTraining()
//    ScenarioRunner.ForceCompleteCurrentTask()  ↔  ServerCompleteStep()
//    ScenarioRunner.JumpToTask(index)  ↔  ServerJumpToStep(index)
//    TaskStateBroadcast  ↔  TrainingStateBroadcast
//
//  씬 배치:
//    빈 NetworkObject 에 이 컴포넌트 부착.
//    scenarioDatabase 에 DTCData 에셋 드래그.
//
//  동작 흐름:
//    1. 서버 시작 → GenerateRandomFault() 로 DTCData 랜덤 선택
//    2. SyncVar(_activeDTCIndex, _currentStepIndex) 전 클라이언트 전파
//    3. 각 도구(멀티테스터기, 렌치 등)가 ProcessStepAction() 호출
//    4. 서버 검증 통과 → 다음 단계, 모두 완료 → CompleteTraining()
// ============================================================

// ──────────────────────────────────────────────────────────────
//  훈련 상태 브로드캐스트 (기존 TaskStateBroadcast 와 동일 역할)
// ──────────────────────────────────────────────────────────────
public struct TrainingStateBroadcast : FishNet.Broadcast.IBroadcast
{
    public int stepIndex;
    public int totalSteps;
    public bool isActive;
    public string dtcCode;
    public string stepDescription;
}

public struct TrainingCompleteBroadcast : FishNet.Broadcast.IBroadcast
{
    public bool success;
    public int errorCount;
    public float timeTaken;
}

public class TrainingFlowManager : NetworkBehaviour
{
    public static TrainingFlowManager Instance { get; private set; }

    // ── 인스펙터 ──────────────────────────────

    [Header("시나리오 데이터베이스")]
    [Tooltip("랜덤 선택할 DTCData 에셋 리스트")]
    public List<DTCData> scenarioDatabase = new();

    [Header("디버그")]
    [SerializeField] private bool _autoStartOnServer = true;
    [SerializeField] private float _startDelay = 1f;

    // ── SyncVar (서버→모든 클라이언트 자동 전파) ──

    /// <summary>현재 선택된 시나리오 인덱스 (-1 = 미선택)</summary>
    private readonly SyncVar<int> _activeDTCIndex = new(
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers));

    /// <summary>현재 진행 중인 정비 단계</summary>
    private readonly SyncVar<int> _currentStepIndex = new(
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers));

    /// <summary>훈련 활성 여부</summary>
    private readonly SyncVar<bool> _isActive = new(
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers));

    // ── 서버 로컬 상태 ────────────────────────

    private DTCData _currentScenario;
    private float _startTime;
    private int _errorCount;

    // ── 이벤트 (UI/가이드 구독용) ─────────────

    /// <summary>단계가 바뀔 때 발행 (stepIndex, description)</summary>
    public static event System.Action<int, string> OnStepChanged;

    /// <summary>훈련 완료 시 발행</summary>
    public static event System.Action<TrainingCompleteBroadcast> OnTrainingComplete;

    /// <summary>오답/실수 발생 시 발행</summary>
    public static event System.Action<string> OnMistake;

    // ══════════════════════════════════════════
    //  생명주기
    // ══════════════════════════════════════════

    private void Awake()
    {
        if(Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        _activeDTCIndex.Value = -1;
        _currentStepIndex.Value = 0;
        _isActive.Value = false;

        _currentStepIndex.OnChange += OnStepIndexChanged_Server;

        if(_autoStartOnServer)
            StartCoroutine(DelayedStart());
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // SyncVar 변경 콜백 — 클라이언트 UI 갱신
        _currentStepIndex.OnChange += OnStepIndexChanged_Client;
        _isActive.OnChange += OnIsActiveChanged_Client;

        // 클라이언트 브로드캐스트 수신 등록
        InstanceFinder.ClientManager.RegisterBroadcast<TrainingStateBroadcast>(OnReceiveState);
        InstanceFinder.ClientManager.RegisterBroadcast<TrainingCompleteBroadcast>(OnReceiveComplete);
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        _currentStepIndex.OnChange -= OnStepIndexChanged_Client;
        _isActive.OnChange -= OnIsActiveChanged_Client;

        if(InstanceFinder.ClientManager != null)
        {
            InstanceFinder.ClientManager.UnregisterBroadcast<TrainingStateBroadcast>(OnReceiveState);
            InstanceFinder.ClientManager.UnregisterBroadcast<TrainingCompleteBroadcast>(OnReceiveComplete);
        }
    }

    private IEnumerator DelayedStart()
    {
        yield return new WaitForSeconds(_startDelay);
        StartTraining();
    }

    // ══════════════════════════════════════════
    //  공개 API (서버에서 호출)
    // ══════════════════════════════════════════

    /// <summary>
    /// [서버] 랜덤 고장 선택 후 훈련 시작.
    /// StartupManager 또는 DemoNetworkStarter 에서 호출.
    /// </summary>
    [Server]
    public void StartTraining()
    {
        if(scenarioDatabase == null || scenarioDatabase.Count == 0)
        {
            Debug.LogError("[TrainingFlow] scenarioDatabase 가 비어있습니다.");
            return;
        }

        int rand = Random.Range(0, scenarioDatabase.Count);
        _currentScenario = scenarioDatabase[rand];

        _activeDTCIndex.Value = rand;
        _currentStepIndex.Value = 0;
        _isActive.Value = true;
        _startTime = Time.time;
        _errorCount = 0;

        Debug.Log($"[TrainingFlow] 훈련 시작 — DTC: {_currentScenario.dtcCode} / {_currentScenario.faultName}");

        BroadcastCurrentState();
    }

    /// <summary>
    /// [서버] 도구가 단계 조건을 충족했을 때 호출.
    /// Multimeter, GreaseTool, InteractablePart 등에서 진입점.
    /// </summary>
    [Server]
    public void ProcessStepAction(RepairActionType action, string targetName, float measuredValue = 0f)
    {
        if(!_isActive.Value || _currentScenario == null) return;

        var step = _currentScenario.GetStep(_currentStepIndex.Value);
        if(step == null) return;

        // 1. 안전 검사
        if(step.requiresSafetyCheck && ElectricSafetyManager.Instance != null)
        {
            if(!ElectricSafetyManager.Instance.IsSafeToWork())
            {
                _errorCount++;
                NotifyMistakeObserversRpc("고전압 안전 수칙 미준수 — MSD 차단 및 절연 장갑 착용 필요");
                return;
            }
        }

        // 2. 대상 오브젝트 이름 매칭
        if(step.targetObjName != targetName)
        {
            _errorCount++;
            NotifyMistakeObserversRpc($"잘못된 부품 조작 — 현재 단계 대상: {step.targetObjName}");
            return;
        }

        // 3. 행동 타입 매칭
        if(step.action != action)
        {
            _errorCount++;
            NotifyMistakeObserversRpc($"잘못된 도구 사용 — 필요 행동: {step.action}");
            return;
        }

        // 4. 수치 검증 (측정 단계)
        if((action == RepairActionType.MeasureVoltage ||
             action == RepairActionType.MeasureResistance) &&
            !step.IsValueValid(measuredValue))
        {
            _errorCount++;
            NotifyMistakeObserversRpc(
                $"측정값 불일치 — 측정값: {measuredValue:F1} / 목표: {step.targetValue:F1} ±{step.toleranceRatio * 100:F0}%");
            return;
        }

        // ✅ 모든 조건 통과 → 다음 단계
        AdvanceStep();
    }

    /// <summary>[서버] 현재 단계를 강제 완료 (디버그 커맨드용)</summary>
    [Server]
    public void ServerCompleteStep()
    {
        if(!_isActive.Value) return;
        AdvanceStep();
    }

    /// <summary>[서버] 특정 단계로 강제 이동 (디버그 커맨드용)</summary>
    [Server]
    public void ServerJumpToStep(int index)
    {
        if(_currentScenario == null) return;
        index = Mathf.Clamp(index, 0, _currentScenario.TotalSteps - 1);
        _currentStepIndex.Value = index;
        BroadcastCurrentState();
    }

    // ══════════════════════════════════════════
    //  내부 로직
    // ══════════════════════════════════════════

    [Server]
    private void AdvanceStep()
    {
        int next = _currentStepIndex.Value + 1;

        if(next >= _currentScenario.TotalSteps)
        {
            CompleteTraining();
            return;
        }

        _currentStepIndex.Value = next;
        BroadcastCurrentState();

        Debug.Log($"[TrainingFlow] Step {next}/{_currentScenario.TotalSteps - 1} — {_currentScenario.GetStep(next)?.stepDescription}");
    }

    [Server]
    private void CompleteTraining()
    {
        _isActive.Value = false;

        float elapsed = Time.time - _startTime;

        var result = new TrainingCompleteBroadcast
        {
            success = true,
            errorCount = _errorCount,
            timeTaken = elapsed,
        };

        InstanceFinder.ServerManager.Broadcast(result);
        Debug.Log($"[TrainingFlow] 훈련 완료 — 시간: {elapsed:F1}s / 오류: {_errorCount}회");
    }

    [Server]
    private void BroadcastCurrentState()
    {
        if(_currentScenario == null) return;

        var step = _currentScenario.GetStep(_currentStepIndex.Value);

        InstanceFinder.ServerManager.Broadcast(new TrainingStateBroadcast
        {
            stepIndex = _currentStepIndex.Value,
            totalSteps = _currentScenario.TotalSteps,
            isActive = _isActive.Value,
            dtcCode = _currentScenario.dtcCode,
            stepDescription = step?.stepDescription ?? "",
        });
    }

    // ══════════════════════════════════════════
    //  SyncVar 콜백
    // ══════════════════════════════════════════

    private void OnStepIndexChanged_Server(int prev, int next, bool asServer)
    {
        if(!asServer) return;
        // 서버 측 추가 처리 필요 시 작성
    }

    private void OnStepIndexChanged_Client(int prev, int next, bool asServer)
    {
        if(asServer) return;
        var step = GetCurrentStepLocal();
        OnStepChanged?.Invoke(next, step?.stepDescription ?? "");
    }

    private void OnIsActiveChanged_Client(bool prev, bool next, bool asServer)
    {
        if(asServer || next) return;
        // 비활성화 시 클라이언트 UI 정리
    }

    // ══════════════════════════════════════════
    //  브로드캐스트 수신 (클라이언트)
    // ══════════════════════════════════════════

    private void OnReceiveState(TrainingStateBroadcast broadcast, FishNet.Transporting.Channel channel)
    {
        OnStepChanged?.Invoke(broadcast.stepIndex, broadcast.stepDescription);
    }

    private void OnReceiveComplete(TrainingCompleteBroadcast broadcast, FishNet.Transporting.Channel channel)
    {
        OnTrainingComplete?.Invoke(broadcast);
    }

    // ══════════════════════════════════════════
    //  ObserversRpc
    // ══════════════════════════════════════════

    [ObserversRpc]
    private void NotifyMistakeObserversRpc(string reason)
    {
        OnMistake?.Invoke(reason);
        Debug.LogWarning($"[TrainingFlow] 실수: {reason}");
    }

    // ══════════════════════════════════════════
    //  유틸리티
    // ══════════════════════════════════════════

    public DTCData CurrentScenario => _currentScenario;
    public int CurrentStepIndex => _currentStepIndex.Value;
    public bool IsActive => _isActive.Value;

    /// <summary>클라이언트에서 현재 단계 정보 조회 (읽기 전용)</summary>
    public RepairStep GetCurrentStepLocal()
    {
        // 클라이언트는 scenarioDatabase 에서 같은 인덱스를 읽음
        if(_activeDTCIndex.Value < 0 ||
            _activeDTCIndex.Value >= scenarioDatabase.Count) return null;

        var scenario = scenarioDatabase[_activeDTCIndex.Value];
        return scenario.GetStep(_currentStepIndex.Value);
    }

    /// <summary>기존 ScenarioRunner.ForceCompleteCurrentTask() 호환 래퍼</summary>
    public void ForceCompleteCurrentTask()
    {
        if(IsServerStarted) ServerCompleteStep();
    }

    /// <summary>기존 ScenarioRunner.JumpToTask(index) 호환 래퍼</summary>
    public void JumpToTask(int index)
    {
        if(IsServerStarted) ServerJumpToStep(index);
    }

    /// <summary>기존 ScenarioRunner.GetDebugStatus() 호환 래퍼</summary>
    public string GetDebugStatus()
    {
        if(_currentScenario == null) return "미초기화";
        return $"DTC:{_currentScenario.dtcCode} | Step:{_currentStepIndex.Value}/{_currentScenario.TotalSteps} | Active:{_isActive.Value}";
    }
}