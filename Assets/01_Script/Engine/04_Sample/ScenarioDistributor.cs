using FishNet;
using UnityEngine;
using Debug = UnityEngine.Debug;

// ============================================================
//  ScenarioDistributor.cs
//  서버 전용 — JSON 로드 + ScenarioRunner 직접 호출 + 클라이언트 브로드캐스트
// ============================================================

public class ScenarioDistributor : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ScenarioRunner _scenarioRunner;

    public string LastScenarioId { get; private set; }

    public void LoadAndBroadcast(string scenarioId)
    {
        if (!InstanceFinder.IsServerStarted)
        {
            Debug.LogWarning("[ScenarioDistributor] 서버가 아닌 곳에서 호출됨");
            return;
        }

        var data = ScenarioLoader.Load(scenarioId);

        if (!ScenarioLoader.Validate(data))
        {
            Debug.LogError("[ScenarioDistributor] 유효하지 않은 ScenarioData — 브로드캐스트 중단");
            return;
        }

        LastScenarioId = scenarioId;

        // 1. 클라이언트에 브로드캐스트
        InstanceFinder.ServerManager.Broadcast(new ScenarioDataBroadcast { data = data });
        Debug.Log($"[ScenarioDistributor] 브로드캐스트 완료 — {data.scenarioName} | 클라이언트: {InstanceFinder.ServerManager.Clients.Count}명");

        // 2. 서버의 ScenarioRunner 직접 호출
        if (_scenarioRunner != null)
            _scenarioRunner.StartScenario(data);
        else
            Debug.LogWarning("[ScenarioDistributor] ScenarioRunner 연결 안 됨");
    }
}