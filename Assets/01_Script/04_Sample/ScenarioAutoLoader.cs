using FishNet;
using FishNet.Transporting;
using MasterServerToolkit.MasterServer;
using UnityEngine;

public class ScenarioAutoLoader : MonoBehaviour
{
    [Header("폴백 시나리오 ID (MST Args 없을 때 사용)")]
    public string autoLoadScenarioId = "ev_p0aa6_battery_insulation";

    [Header("참조")]
    public ScenarioDistributor distributor;

    private void OnEnable()
    {
        if (InstanceFinder.ServerManager == null) return;

        if (InstanceFinder.IsServerStarted)
        {
            StartCoroutine(LoadAfterDelay());
            return;
        }

        InstanceFinder.ServerManager.OnServerConnectionState += OnServerConnectionState;
    }

    private void OnDisable()
    {
        if (InstanceFinder.ServerManager != null)
            InstanceFinder.ServerManager.OnServerConnectionState -= OnServerConnectionState;
    }

    private void OnServerConnectionState(ServerConnectionStateArgs args)
    {
        if (args.ConnectionState != LocalConnectionState.Started) return;
        InstanceFinder.ServerManager.OnServerConnectionState -= OnServerConnectionState;
        StartCoroutine(LoadAfterDelay());
    }

    private System.Collections.IEnumerator LoadAfterDelay()
    {
        yield return new WaitForSeconds(2f);

        // MST RoomOptions에서 scenarioId 꺼내기
        string scenarioId = autoLoadScenarioId;

        if (Mst.Server.Rooms != null)
        {
            var roomOptions = Mst.Args.AsString("scenarioId", string.Empty);
            if (!string.IsNullOrEmpty(roomOptions))
            {
                scenarioId = roomOptions;
                Debug.Log($"[AutoLoader] MST scenarioId 수신: {scenarioId}");
            }
            else
            {
                Debug.LogWarning($"[AutoLoader] scenarioId 없음 → 폴백: {scenarioId}");
            }
        }

        Debug.Log($"[AutoLoader] 로드: {scenarioId}");
        distributor.LoadAndBroadcast(scenarioId);
    }
}