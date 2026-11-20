using System.Collections;
using FishNet;
using Newtonsoft.Json;
using UnityEngine;

// ============================================================
//  ScenarioAutoStarter.cs — 씬을 열면 바로 시나리오를 시작하는 범용 시작기 (모듈화, 2026-11-20)
//
//  로비·마스터 서버 없이 시나리오 하나를 바로 돌릴 때 쓴다(새 콘텐츠 시험, 예제, 단독 실행).
//   1) FishNet 호스트(서버+클라이언트)를 시작한다(이미 시작돼 있으면 그대로)
//   2) 시나리오 JSON 을 읽는다 — scenarioJson(TextAsset)이 있으면 그것, 없으면 Resources/Scenarios/<scenarioId>
//   3) ScenarioRunner.StartScenario
//  코드 작성 없이 씬에 붙이고 JSON 만 연결하면 된다.
// ============================================================
public class ScenarioAutoStarter : MonoBehaviour
{
    [SerializeField] private ScenarioRunner runner;
    [Tooltip("시나리오 JSON. 비우면 scenarioId 로 Resources/Scenarios 에서 읽는다")]
    [SerializeField] private TextAsset scenarioJson;
    [SerializeField] private string scenarioId;
    [Tooltip("서버가 아직 없으면 이 기기에서 호스트(서버+클라이언트)를 시작")]
    [SerializeField] private bool autoStartHost = true;
    [Tooltip("호스트 시작 뒤 시나리오 시작까지 기다리는 시간(초)")]
    [SerializeField] private float startDelay = 0.5f;

    private IEnumerator Start()
    {
        if (runner == null) runner = FindFirstObjectByType<ScenarioRunner>();
        if (runner == null) { Debug.LogError("[ScenarioAutoStarter] 씬에 ScenarioRunner 가 없습니다"); yield break; }

        if (autoStartHost && !InstanceFinder.IsServerStarted)
        {
            InstanceFinder.ServerManager.StartConnection();
            InstanceFinder.ClientManager.StartConnection();
        }
        while (!InstanceFinder.IsServerStarted) yield return null;
        if (autoStartHost)
            while (!InstanceFinder.IsClientStarted) yield return null;
        if (startDelay > 0f) yield return new WaitForSeconds(startDelay);

        ScenarioData data = null;
        if (scenarioJson != null)
        {
            data = JsonConvert.DeserializeObject<ScenarioData>(scenarioJson.text);
            if (data != null && !ScenarioLoader.Validate(data)) data = null;
        }
        else if (!string.IsNullOrEmpty(scenarioId))
            data = ScenarioLoader.Load(scenarioId);

        if (data == null) { Debug.LogError("[ScenarioAutoStarter] 시나리오를 읽지 못했습니다 — scenarioJson 또는 scenarioId 를 확인하세요"); yield break; }

        // 클라이언트(화면·조작 쪽)에 시나리오 전체를 먼저 보낸다 — 로비의 ScenarioDistributor 와 같은 순서
        InstanceFinder.ServerManager.Broadcast(new ScenarioDataBroadcast { data = data });
        Debug.Log($"[ScenarioAutoStarter] 시나리오 시작: {data.scenarioId} (단계 {data.scenario.tasks.Count}개)");
        runner.StartScenario(data);
    }
}
