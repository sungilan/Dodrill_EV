using UnityEngine;
using FishNet;
using FishNet.Transporting;

// ============================================================
//  EVBootstrapper.cs
//  전기차 정비 훈련 씬의 초기화 진입점.
//
//  기존 ScenarioBootstrapper 와의 대응:
//    ScenarioBootstrapper.RegisterModules()  ↔  RegisterEVSystems()
//    ScenarioBootstrapper.Registry           ↔  TrainingFlowManager.scenarioDatabase
//
//  씬 배치:
//    빈 GameObject 에 부착 (이름: _EVBootstrapper).
//    TrainingFlowManager, ElectricSafetyManager 등이 같은 씬에 있어야 함.
//
//  데모 씬 vs 실제 씬:
//    isDemoMode = true  → 서버 자동 시작 (DemoNetworkStarter 와 유사)
//    isDemoMode = false → MST 또는 외부 세션 관리자가 서버를 시작함
// ============================================================

public class EVBootstrapper : MonoBehaviour
{
    [Header("모드")]
    [Tooltip("true: 씬 시작 시 호스트(서버+클라이언트) 자동 시작")]
    public bool isDemoMode = true;

    [Header("참조")]
    [SerializeField] private TrainingFlowManager _trainingManager;
    [SerializeField] private ElectricSafetyManager _safetyManager;

    [Header("설정")]
    [SerializeField] private float _startDelay = 0.5f;

    private void Awake()
    {
        // 인스펙터 미연결 시 씬에서 자동 탐색
        if(_trainingManager == null)
            _trainingManager = FindFirstObjectByType<TrainingFlowManager>();

        if(_safetyManager == null)
            _safetyManager = FindFirstObjectByType<ElectricSafetyManager>();
    }

    private void Start()
    {
        if(!isDemoMode) return;

        if(InstanceFinder.NetworkManager == null)
        {
            Debug.LogError("[EVBootstrapper] NetworkManager 가 씬에 없습니다.");
            return;
        }

        if(!InstanceFinder.IsServerStarted)
        {
            Debug.Log("[EVBootstrapper] 호스트(서버+클라이언트) 시작 중...");
            InstanceFinder.ServerManager.StartConnection();
            InstanceFinder.ClientManager.StartConnection();

            // 서버 시작 완료 대기 후 초기화
            InstanceFinder.ServerManager.OnServerConnectionState += OnServerStarted;
        }
        else
        {
            // 이미 서버가 떠 있는 경우 (씬 재로드 등)
            Invoke(nameof(InitializeSystems), _startDelay);
        }
    }

    private void OnServerStarted(ServerConnectionStateArgs args)
    {
        if(args.ConnectionState != LocalConnectionState.Started) return;

        InstanceFinder.ServerManager.OnServerConnectionState -= OnServerStarted;
        Invoke(nameof(InitializeSystems), _startDelay);
    }

    private void InitializeSystems()
    {
        Debug.Log("[EVBootstrapper] 전기차 훈련 시스템 초기화");

        // 안전 시스템 초기화 — 기본값: 안전하지 않음(장갑 미착용)
        //_safetyManager?.ResetSafetyState();

        // 훈련 시작 — TrainingFlowManager 가 랜덤 고장 선택
        // (isDemoMode 일 때만, 아닐 때는 외부에서 호출)
        if(isDemoMode && _trainingManager != null && InstanceFinder.IsServerStarted)
        {
            _trainingManager.StartTraining();
        }
    }

    private void OnDestroy()
    {
        if(InstanceFinder.ServerManager != null)
            InstanceFinder.ServerManager.OnServerConnectionState -= OnServerStarted;
    }
}