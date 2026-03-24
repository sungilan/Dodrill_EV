using FishNet;
using FishNet.Broadcast;
using FishNet.Transporting;
using MasterServerToolkit.MasterServer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DoDrill
{
    // ============================================================
    //  ScenarioEndBroadcast — 서버 → 클라이언트 종료 신호
    // ============================================================
    public struct ScenarioEndBroadcast : IBroadcast { }

    // ============================================================
    //  ScenarioExitHandler
    //  클라이언트 전용 — 시나리오 종료 시 Room 나가고 타이틀로 복귀
    //
    //  씬에 빈 GameObject 붙이고 _titleSceneName 설정
    // ============================================================
    public class ScenarioExitHandler : MonoBehaviour
    {
        [Header("타이틀 씬 이름")]
        [SerializeField] private string _titleSceneName = "Title";

        [Header("종료 전 딜레이 (초)")]
        [SerializeField] private float _exitDelay = 0.5f;

        private void OnEnable()
        {
            if (InstanceFinder.ClientManager != null)
                InstanceFinder.ClientManager.RegisterBroadcast<ScenarioEndBroadcast>(OnReceiveEnd);
        }

        private void OnDisable()
        {
            if (InstanceFinder.ClientManager != null)
                InstanceFinder.ClientManager.UnregisterBroadcast<ScenarioEndBroadcast>(OnReceiveEnd);
        }

        private void OnReceiveEnd(ScenarioEndBroadcast broadcast, Channel channel)
        {
            Debug.Log("[ScenarioExitHandler] 시나리오 종료 수신 → 종료 팝업 표시");
            Mst.Events.Invoke(ScenarioEventKeys.ShowEndPopup);
        }

        public void ReturnToTitle()
        {
            StartCoroutine(ReturnToTitleCoroutine());
        }

        private System.Collections.IEnumerator ReturnToTitleCoroutine()
        {
            Debug.Log("[ScenarioExitHandler] 타이틀 복귀 시작");

            // 1. RoomClientManager 오프라인 씬을 타이틀로 변경
            // FishNet 연결 해제 시 자동으로 타이틀 씬으로 이동
            var roomClient = Object.FindFirstObjectByType<MasterServerToolkit.Bridges.FishNetworking.RoomClientManager>();
            if (roomClient != null)
            {
                roomClient.SetOfflineScene(_titleSceneName);
                Debug.Log($"[ScenarioExitHandler] OfflineScene → {_titleSceneName}");
            }

            // 2. 로비 정적 상태 초기화 (타이틀 씬에서 로비 UI 자동 표시 방지)
            // static 메서드라 씬에 오브젝트 없어도 호출 가능
            LobbyPanel.ResetStaticState();

            // 3. FishNet 연결 해제 (RoomClientManager가 씬 전환 처리)
            if (InstanceFinder.IsClientStarted)
            {
                InstanceFinder.ClientManager.StopConnection();
                yield return new WaitForSeconds(_exitDelay);
            }
            else
            {
                // FishNet 연결 없으면 직접 씬 전환
                SceneManager.LoadScene(_titleSceneName);
            }
        }
    }

    // ──────────────────────────────────────────
    //  시나리오 전용 이벤트 키
    // ──────────────────────────────────────────
    public static class ScenarioEventKeys
    {
        // 시나리오 완료 시 자동으로 뜨는 팝업 (Popup_EndGame)
        public const string ShowEndPopup = "showScenarioEndPopup";
        public const string HideEndPopup = "hideScenarioEndPopup";

        // 게임 도중 홈 버튼으로 나갈 때 팝업 (Popup_ScenarioEndPanel)
        public const string ShowExitPopup = "showScenarioExitPopup";
        public const string HideExitPopup = "hideScenarioExitPopup";
    }
}