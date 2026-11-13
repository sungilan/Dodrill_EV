using System.Collections;
using MasterServerToolkit.MasterServer;
using UnityEngine;
using UnityEngine.SceneManagement;

// ============================================================
//  MasterReconnectGuard.cs
//
//  타이틀(Client 씬)에 있는 동안 마스터 서버 연결이 끊겨 있으면 다시 붙인다.
//
//  · ClientToMasterConnector 는 DontDestroyOnLoad 싱글톤이라 처음 한 번(Start)만 접속을 시도한다.
//    훈련 중이나 홈 버튼으로 나오는 사이 마스터 연결이 끊기면, Client 씬을 다시 불러도
//    새 커넥터는 중복이라 바로 지워지고 기존 커넥터는 재접속을 하지 않아 '연결 중...' 에서 멈췄다.
//  · 끊긴 상태에서 남아 있던 로그인 정보(AccountInfo)는 서버에선 이미 무효라 먼저 지운다.
//    재접속되면 AuthBehaviour 가 로그인 화면을 다시 띄운다.
// ============================================================
public static class MasterReconnectGuard
{
    private const string TitleScene = "Client";
    private static Runner _runner;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (Application.isBatchMode) return;   // 서버(헤드리스) 빌드는 제외
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        if (SceneManager.GetActiveScene().name == TitleScene) Begin();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single && scene.name == TitleScene) Begin();
    }

    private static void Begin()
    {
        if (_runner == null)
        {
            var go = new GameObject("[DoDrill] MasterReconnectGuard");
            Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<Runner>();
        }
        _runner.StopAllCoroutines();
        _runner.StartCoroutine(Watch());
    }

    private static IEnumerator Watch()
    {
        // 씬의 커넥터가 Start 에서 스스로 붙을 시간을 준다
        yield return new WaitForSecondsRealtime(1.5f);

        var wait = new WaitForSecondsRealtime(2f);
        while (SceneManager.GetActiveScene().name == TitleScene)
        {
            var conn = Mst.Client.Connection;
            if (conn != null && !conn.IsConnected && !conn.IsConnecting)
            {
                var connector = Object.FindFirstObjectByType<ClientToMasterConnector>();
                if (connector != null)   // 혼자 하기 모드에선 커넥터를 일부러 지우므로 그때는 건드리지 않는다
                {
                    if (Mst.Client.Auth.IsSignedIn)
                    {
                        Debug.Log("[MasterReconnect] 끊긴 연결의 로그인 정보 정리");
                        Mst.Client.Auth.SignOut();
                    }
                    Debug.Log("[MasterReconnect] 마스터 서버 연결이 끊겨 있음 → 재접속 시도");
                    connector.StartConnection();
                }
            }
            yield return wait;
        }
    }

    private sealed class Runner : MonoBehaviour { }
}
