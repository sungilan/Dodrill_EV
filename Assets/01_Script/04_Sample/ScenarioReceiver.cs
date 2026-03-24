using FishNet;
using FishNet.Transporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

// ============================================================
//  ScenarioReceiver.cs
//  클라이언트 전용 — ScenarioDataBroadcast 수신 + 이벤트 발행
//
//  책임:
//    1. FishNet Broadcast 핸들러 등록/해제
//    2. 수신한 ScenarioData 를 보관
//    3. OnScenarioReceived 이벤트로 외부에 알림
//
//  연동 예정:
//    - Phase 2: MapLoadingCoordinator 가 OnScenarioReceived 구독 → MapData 전달
//    - Phase 3: ScenarioRunner 가 OnScenarioReceived 구독 → 초기화
//
//  서버 브로드캐스트는 ScenarioDistributor 가 담당
// ============================================================

    public class ScenarioReceiver : MonoBehaviour
    {
        // ── 외부 구독용 이벤트 ────────────────

        /// <summary>
        /// 시나리오 데이터 수신 완료 시 발행
        /// Phase 2 MapLoadingCoordinator, Phase 3 ScenarioRunner 가 구독
        /// </summary>
        public static event System.Action<ScenarioData> OnScenarioReceived;

        // ── 수신 데이터 보관 ──────────────────

        /// <summary>수신한 ScenarioData. 수신 전에는 null</summary>
        public ScenarioData ReceivedData { get; private set; }

        // ── FishNet 핸들러 등록/해제 ──────────

        private void OnEnable()
        {
            InstanceFinder.ClientManager.RegisterBroadcast<ScenarioDataBroadcast>(OnReceive);
        }

        private void OnDisable()
        {
            if (InstanceFinder.ClientManager != null)
                InstanceFinder.ClientManager.UnregisterBroadcast<ScenarioDataBroadcast>(OnReceive);
        }

        // ── 수신 처리 ─────────────────────────

        private void OnReceive(ScenarioDataBroadcast broadcast, Channel channel)
        {
            ReceivedData = broadcast.data;

            UnityEngine.Debug.Log($"[ScenarioReceiver] 수신 완료 — {ReceivedData?.scenarioName} " +
                      $"| Tasks: {ReceivedData?.scenario?.tasks?.Count}");

            OnScenarioReceived?.Invoke(ReceivedData);

            // TODO: Phase 2 — MapLoadingCoordinator.OnScenarioReceived += ... (맵 담당자 연결 지점)
            // TODO: Phase 3 — ScenarioRunner.OnScenarioReceived += ...
        }
    }