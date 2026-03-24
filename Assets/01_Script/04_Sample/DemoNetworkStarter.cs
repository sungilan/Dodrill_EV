using System.Collections;
using FishNet;
using FishNet.Transporting;
using MasterServerToolkit.MasterServer;
using UnityEngine;
using Debug = UnityEngine.Debug;

// ============================================================
//  DemoNetworkStarter.cs
//  데모 씬 전용 — FishNet 호스트(서버+클라이언트)로 자동 시작 후
//  ScenarioDistributor를 통해 시나리오를 로드합니다.
//
//  우선순위:
//    MST RoomAccess 토큰이 있으면 → RoomClientManager가 연결을 담당
//    토큰이 없으면 → 직접 호스트 시작 (순수 데모 모드)
//
//  사용 조건:
//    씬에 FishNet NetworkManager가 존재해야 합니다.
//    없으면 콘솔 에러를 출력하고 종료합니다.
// ============================================================

public class DemoNetworkStarter : MonoBehaviour
{
    [Header("Scenario")]
    [Tooltip("Resources/Scenarios/ 파일명 (확장자 제외)")]
    public string scenarioId = "0_test_all_features";

    [Header("References")]
    [SerializeField] private ScenarioDistributor _distributor;

    [Header("Settings")]
    [Tooltip("서버 시작 후 시나리오 로드 대기 시간 (초)")]
    [SerializeField] private float _startDelay = 0.5f;

    // ── 생명주기 ─────────────────────────────────────────────

    private IEnumerator Start()
    {
        if (InstanceFinder.NetworkManager == null)
        {
            Debug.LogError("[DemoNetworkStarter] NetworkManager가 씬에 없습니다. " +
                           "FishNet NetworkManager를 추가하거나 StandaloneScenarioRunner를 사용하세요.");
            yield break;
        }

        if (_distributor == null)
        {
            Debug.LogError("[DemoNetworkStarter] ScenarioDistributor 레퍼런스가 없습니다.");
            yield break;
        }

        // MST 룸 토큰이 있으면 RoomClientManager가 연결을 처리하므로 호스트 시작 생략
        bool hasMstRoomAccess = Mst.Client.Rooms.ReceivedAccess != null &&
                                !string.IsNullOrEmpty(Mst.Client.Rooms.ReceivedAccess.Token);

        // MST 룸 모드: RoomClientManager가 연결과 시나리오 배포를 모두 담당
        if (hasMstRoomAccess)
        {
            Debug.Log("[DemoNetworkStarter] MST 룸 접근 감지 — RoomClientManager에 위임, 종료");
            yield break;
        }

        // 순수 데모 모드: MST 없이 직접 호스트 시작
        if (!InstanceFinder.IsServerStarted)
        {
            Debug.Log("[DemoNetworkStarter] 호스트(서버+클라이언트) 시작 중...");
            InstanceFinder.ServerManager.StartConnection();
            InstanceFinder.ClientManager.StartConnection();
        }

        // 서버가 완전히 시작될 때까지 대기
        float waited = 0f;
        while (!InstanceFinder.IsServerStarted && waited < 5f)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        if (!InstanceFinder.IsServerStarted)
        {
            Debug.LogError("[DemoNetworkStarter] 서버 시작 실패 (5초 타임아웃). " +
                           "NetworkManager 설정을 확인하세요.");
            yield break;
        }

        yield return new WaitForSeconds(_startDelay);

        Debug.Log($"[DemoNetworkStarter] 시나리오 로드: {scenarioId}");
        _distributor.LoadAndBroadcast(scenarioId);
    }
}
