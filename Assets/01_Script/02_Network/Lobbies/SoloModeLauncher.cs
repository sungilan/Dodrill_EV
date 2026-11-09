using System;
using System.Collections;
using System.Reflection;
using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// '혼자 하기' 모드: 방을 만들지 않고, 인터넷·마스터 서버 없이 이 기기에서 바로 훈련을 시작한다.
///
/// 방식: 이 앱 안에서 FishNet 호스트(서버 + 클라이언트)를 시작한다. 훈련 진행(ScenarioRunner 등)은 원래 Game 씬 안에 있어서
///       호스트가 Game 씬을 불러오면(Client 씬의 DefaultScene 이 서버 시작 때 자동으로 불러온다) 평소 서버가 하던 일을 그대로 한다.
///       시나리오는 Game 씬의 ScenarioAutoLoader 가 폴백 ID 로 자동 시작한다.
///
/// 손대지 않는 것: 마스터 서버 로그인·방 목록·로비 (혼자 하기에서는 쓰지 않고 건너뛴다).
/// 막아야 하는 것: 클라이언트 앱에도 들어 있는 MST 방 서버/클라이언트 부품(RoomNetworkManager 등)이 FishNet 이벤트에 붙어 있어,
///               호스트를 시작하면 '방 접근 토큰 없음' 오류를 내며 이벤트 처리를 끊는다 → 시작 전에 떼어 낸다.
///
/// 아직 실기기에서 확인하지 않았다 — 각 단계마다 [Solo] 로그를 남기므로 실패하면 로그의 마지막 단계를 보면 원인을 알 수 있다.
/// </summary>
public static class SoloModeLauncher
{
    /// <summary>지금 혼자 하기 모드로 실행 중인지</summary>
    public static bool IsSolo { get; internal set; }

    /// <summary>혼자 하기로 시작하는 시나리오 (Resources/Scenarios/ 파일명)</summary>
    public static string ScenarioId = "ev_p0aa6_battery_insulation";

    private static SoloModeRunner s_runner;

    /// <summary>혼자 하기 시작. 실패하면 onFailed(사유)가 불린다(이때 네트워크는 원래대로 되돌린다).</summary>
    public static void Begin(Action<string> onFailed = null)
    {
        if (s_runner != null) return;   // 이미 시작 중

        var go = new GameObject("[SoloModeRunner]");
        Object.DontDestroyOnLoad(go);
        s_runner = go.AddComponent<SoloModeRunner>();
        s_runner.Begin(onFailed);
    }

    internal static void RunnerFinished()
    {
        if (s_runner != null) Object.Destroy(s_runner.gameObject);
        s_runner = null;
    }
}

internal class SoloModeRunner : MonoBehaviour
{
    private const string LocalAddress = "127.0.0.1";
    private const float StartTimeout = 10f;

    private NetworkManager _nm;
    private Action<string> _onFailed;
    private bool _watching;

    public void Begin(Action<string> onFailed)
    {
        _onFailed = onFailed;
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        _nm = InstanceFinder.NetworkManager;
        if (_nm == null) { Fail("NetworkManager 를 찾지 못했습니다."); yield break; }
        if (_nm.IsServerStarted || _nm.IsClientStarted) { Fail("이미 네트워크가 시작되어 있습니다."); yield break; }

        Debug.Log("[Solo] 1/5 MST 방 서버·클라이언트 부품 정리");
        RemoveRoomComponents();
        // Destroy 는 프레임 끝에 일어난다 — OnDestroy 에서 이벤트 구독이 풀리도록 한 프레임 기다린다
        yield return null;
        yield return null;

        Debug.Log("[Solo] 2/5 로컬 주소 설정");
        var transport = _nm.TransportManager.Transport;
        if (transport == null) { Fail("전송 계층(Transport)이 없습니다."); yield break; }
        transport.SetClientAddress(LocalAddress);
        transport.SetMaximumClients(8);

        Debug.Log("[Solo] 3/5 호스트 서버 시작");
        SoloModeLauncher.IsSolo = true;
        _nm.ClientManager.OnClientConnectionState += OnClientConnectionState;
        _watching = true;

        _nm.ServerManager.StartConnection();
        float t = 0f;
        while (!_nm.IsServerStarted && t < StartTimeout) { t += Time.unscaledDeltaTime; yield return null; }
        if (!_nm.IsServerStarted) { Fail("서버를 시작하지 못했습니다(포트가 이미 사용 중일 수 있습니다)."); yield break; }

        Debug.Log("[Solo] 4/5 호스트 클라이언트 접속");
        _nm.ClientManager.StartConnection();
        t = 0f;
        while (!_nm.IsClientStarted && t < StartTimeout) { t += Time.unscaledDeltaTime; yield return null; }
        if (!_nm.IsClientStarted) { Fail("클라이언트 접속에 실패했습니다."); yield break; }

        // Game 씬은 서버가 시작될 때 DefaultScene 이 자동으로 불러온다 — 시나리오는 ScenarioAutoLoader 가 시작한다.
        Debug.Log("[Solo] 5/5 완료 — Game 씬 로드와 시나리오 시작은 DefaultScene / ScenarioAutoLoader 가 진행");
        SoloModeLauncher.RunnerFinished();   // 감시(OnClientConnectionState)는 이 컴포넌트가 아니라 정적 핸들러로 이어 간다
        SoloModeWatcher.Attach(_nm);
    }

    /// <summary>MST 방 서버/클라이언트 부품을 떼어 낸다. (훈련 방 접속 때만 쓰는 것들 — 혼자 하기에서는 필요 없다)</summary>
    private void RemoveRoomComponents()
    {
        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (mb == null) continue;
            string n = mb.GetType().Name;

            if (n == "RoomNetworkManager")
            {
                // 이 컴포넌트는 Start 에서 서버 이벤트에 구독하고 OnDestroy 에서 풀지 않는다 → 직접 푼다
                UnsubscribeRoomNetworkManager(mb);
                Destroy(mb);
                Debug.Log("[Solo]  - RoomNetworkManager 제거");
            }
            else if (n == "RoomClientManager" || n == "ClientToMasterConnector")
            {
                Destroy(mb);   // RoomClientManager 는 OnDestroy 에서 스스로 구독을 푼다
                Debug.Log($"[Solo]  - {n} 제거");
            }
        }
    }

    private void UnsubscribeRoomNetworkManager(MonoBehaviour rnm)
    {
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = rnm.GetType();

            var onServer = type.GetMethod("ServerManager_OnServerConnectionState", flags);
            if (onServer != null)
                _nm.ServerManager.OnServerConnectionState -=
                    (Action<ServerConnectionStateArgs>)Delegate.CreateDelegate(typeof(Action<ServerConnectionStateArgs>), rnm, onServer);

            var onRemote = type.GetMethod("ServerManager_OnRemoteConnectionState", flags);
            if (onRemote != null)
                _nm.ServerManager.OnRemoteConnectionState -=
                    (Action<NetworkConnection, RemoteConnectionStateArgs>)Delegate.CreateDelegate(typeof(Action<NetworkConnection, RemoteConnectionStateArgs>), rnm, onRemote);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Solo] RoomNetworkManager 구독 해제 실패: {e.Message}");
        }
    }

    private void OnClientConnectionState(ClientConnectionStateArgs args)
    {
        // 시작 도중 클라이언트가 끊기면 실패로 처리
        if (args.ConnectionState == LocalConnectionState.Stopped && _watching)
            Debug.LogWarning("[Solo] 시작 도중 클라이언트 연결이 끊겼습니다.");
    }

    private void Fail(string reason)
    {
        Debug.LogError($"[Solo] 시작 실패: {reason}");

        if (_nm != null)
        {
            if (_watching) _nm.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            if (_nm.IsClientStarted) _nm.ClientManager.StopConnection();
            if (_nm.IsServerStarted) _nm.ServerManager.StopConnection(true);
        }

        SoloModeLauncher.IsSolo = false;
        _watching = false;
        var cb = _onFailed;
        SoloModeLauncher.RunnerFinished();
        cb?.Invoke(reason);
    }

    private void OnDestroy()
    {
        if (_watching && _nm != null) _nm.ClientManager.OnClientConnectionState -= OnClientConnectionState;
    }
}

/// <summary>
/// 혼자 하기 도중에 계속 살아 있는 감시: 훈련을 끝내고 클라이언트가 나가면 같이 켜 둔 서버도 끄고(→ DefaultScene 이 로비 씬으로 복귀),
/// 혼자 하기 상태를 해제한다.
/// </summary>
internal static class SoloModeWatcher
{
    private static NetworkManager s_nm;

    public static void Attach(NetworkManager nm)
    {
        Detach();
        s_nm = nm;
        s_nm.ClientManager.OnClientConnectionState += OnClient;
    }

    private static void Detach()
    {
        if (s_nm != null) s_nm.ClientManager.OnClientConnectionState -= OnClient;
        s_nm = null;
    }

    private static void OnClient(ClientConnectionStateArgs args)
    {
        if (args.ConnectionState != LocalConnectionState.Stopped) return;

        var nm = s_nm;
        Detach();
        SoloModeLauncher.IsSolo = false;

        // 호스트 클라이언트가 나가면 서버도 끈다 → 서버가 꺼지면 DefaultScene 이 로비(Client) 씬으로 되돌린다
        if (nm != null && nm.IsServerStarted)
        {
            Debug.Log("[Solo] 클라이언트 종료 → 호스트 서버도 종료");
            nm.ServerManager.StopConnection(true);
        }
    }
}
