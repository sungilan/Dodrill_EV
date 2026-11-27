using FishNet;
using FishNet.Broadcast;
using FishNet.Transporting;
using MasterServerToolkit.MasterServer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DoDrill
{
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
            // 평가 결과 화면이 떠 있으면 그것을 닫은 뒤에 종료 팝업을 띄운다 (겹치지 않게)
            EvaluationClientUI.RunWhenResultClosed(Popup_EndGame.RequestShow);
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
    // ============================================================
    //  ScenarioExitAll — (11/20 내부 시연) 시나리오를 다 마치고 PC 가 종료를 눌렀는데 PC 만 메인으로 가고
    //  다른 플레이어는 계속 남아 있었다. 종료 팝업의 '확인'은 서버에 알리고, 서버가 방 전원에게 복귀 신호를 보낸다.
    //  (게임 도중 홈 버튼으로 혼자 나가는 것은 그대로 개인 종료)
    // ============================================================
    public static class ScenarioExitAll
    {
        private static bool s_exiting;
        private static FishNet.Managing.Server.ServerManager s_sm;
        private static FishNet.Managing.Client.ClientManager s_cm;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("[DoDrill] ScenarioExitAll");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            go.AddComponent<Poller>();
        }

        // 네트워크 매니저가 늦게 생기거나 바뀌어도 한 번씩 등록한다
        private sealed class Poller : MonoBehaviour
        {
            private float _next;
            private void Update()
            {
                if (Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + 1f;
                var sm = InstanceFinder.ServerManager;
                var cm = InstanceFinder.ClientManager;
                ToolWorkNet.Init();    // 드릴 작동 소리 중계
                MeasureAnnounceNet.Init();
                ProbePoseNet.Init();   // 프로브 위치 중계도 서버가 계측기보다 먼저 떠 있을 때를 대비해 여기서 함께 등록
                if (sm != null && sm != s_sm)
                {
                    if (s_sm != null) s_sm.UnregisterBroadcast<ScenarioExitAllRequest>(OnServerRequest);
                    sm.RegisterBroadcast<ScenarioExitAllRequest>(OnServerRequest);
                    s_sm = sm;
                }
                if (cm != null && cm != s_cm)
                {
                    if (s_cm != null) s_cm.UnregisterBroadcast<ScenarioExitAllBroadcast>(OnClientExit);
                    cm.RegisterBroadcast<ScenarioExitAllBroadcast>(OnClientExit);
                    s_cm = cm;
                }
                // 접속이 끊긴 뒤(메인 화면)에는 다음 판을 위해 플래그를 푼다
                if (s_exiting && !InstanceFinder.IsClientStarted && !InstanceFinder.IsServerStarted) s_exiting = false;
            }
        }

        private static void OnServerRequest(FishNet.Connection.NetworkConnection conn, ScenarioExitAllRequest msg, Channel channel)
        {
            Debug.Log($"[ScenarioExitAll] 클라이언트 {conn?.ClientId} 종료 요청 → 전원 복귀 신호");
            InstanceFinder.ServerManager.Broadcast(new ScenarioExitAllBroadcast());
        }

        private static void OnClientExit(ScenarioExitAllBroadcast msg, Channel channel)
        {
            if (s_exiting) return;
            Debug.Log("[ScenarioExitAll] 전원 종료 신호 수신 → 메인 화면으로");
            Go();
        }

        /// <summary>종료 팝업 확인: 방 전원 종료를 요청하고, 신호가 나간 뒤 나도 나간다.</summary>
        public static void RequestAll()
        {
            if (s_exiting) return;
            if (InstanceFinder.IsClientStarted && InstanceFinder.ClientManager != null)
            {
                InstanceFinder.ClientManager.Broadcast(new ScenarioExitAllRequest());
                s_exiting = true;
                ScenarioExitCoroutineHost.Run(GoAfter(0.4f));   // 요청이 서버에 닿을 시간을 두고 내 접속을 끊는다
                return;
            }
            Go();
        }

        private static System.Collections.IEnumerator GoAfter(float sec)
        {
            yield return new WaitForSecondsRealtime(sec);
            s_exiting = false;
            Go();
        }

        private static void Go()
        {
            s_exiting = true;
            Mst.Events.Invoke(ScenarioEventKeys.HideEndPopup);
            Mst.Events.Invoke(ScenarioEventKeys.HideExitPopup);
            var h = ScenarioExitHandler.FindAnyInScene();
            if (h != null) h.ReturnToTitle();
            else ScenarioExitHandler.ReturnToTitleWithoutHandler();
        }
    }

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