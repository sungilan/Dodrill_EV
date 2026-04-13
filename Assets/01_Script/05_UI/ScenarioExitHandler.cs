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
            if (gameObject.activeInHierarchy && enabled)
                StartCoroutine(ReturnToTitleCoroutine());
            else
                ScenarioExitCoroutineHost.Run(ReturnToTitleCoroutine());
        }

        /// <summary>비활성 오브젝트에 붙은 핸들러까지 포함해 씬에서 하나 찾습니다.</summary>
        public static ScenarioExitHandler FindAnyInScene()
        {
            var found = Object.FindObjectsByType<ScenarioExitHandler>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            return found != null && found.Length > 0 ? found[0] : null;
        }

        /// <summary>인스펙터에 핸들러가 없을 때 타이틀 복귀만 수행합니다(앱 종료 대신).</summary>
        public static void ReturnToTitleWithoutHandler(string titleSceneName = null)
        {
            string title = string.IsNullOrEmpty(titleSceneName) ? "Title" : titleSceneName;
            ScenarioExitCoroutineHost.Run(ReturnToTitleCore(title, 0.5f));
        }

        private System.Collections.IEnumerator ReturnToTitleCoroutine()
        {
            yield return ReturnToTitleCore(_titleSceneName, _exitDelay);
        }

        private static System.Collections.IEnumerator ReturnToTitleCore(string titleSceneName, float exitDelay)
        {
            Debug.Log("[ScenarioExitHandler] 타이틀 복귀 시작");

            var roomClient = Object.FindFirstObjectByType<MasterServerToolkit.Bridges.FishNetworking.RoomClientManager>();
            if (roomClient != null)
            {
                roomClient.SetOfflineScene(titleSceneName);
                Debug.Log($"[ScenarioExitHandler] OfflineScene → {titleSceneName}");
            }

            LobbyPanel.ResetStaticState();

            if (InstanceFinder.IsClientStarted)
            {
                InstanceFinder.ClientManager.StopConnection();
                yield return new WaitForSeconds(exitDelay);
            }
            else
            {
                SceneManager.LoadScene(titleSceneName);
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

    /// <summary>
    /// <see cref="ScenarioExitHandler"/>가 비활성 오브젝트(예: ScenarioEndSystem)에 붙어 있을 때
    /// <see cref="MonoBehaviour.StartCoroutine(System.Collections.IEnumerator)"/>가 실패하지 않도록
    /// 항상 활성인 숨김 호스트에서 코루틴을 실행합니다.
    /// </summary>
    internal static class ScenarioExitCoroutineHost
    {
        private static Host _host;

        public static void Run(System.Collections.IEnumerator routine)
        {
            if (_host == null)
            {
                var go = new GameObject("[DoDrill] ScenarioExitCoroutineHost");
                Object.DontDestroyOnLoad(go);
                _host = go.AddComponent<Host>();
            }

            _host.StartCoroutine(routine);
        }

        private sealed class Host : MonoBehaviour { }
    }
}