using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

/// <summary>
/// 끊긴 접속 정리 시간.
/// Tugboat 기본 타임아웃이 1800초(30분)라서, 정상 종료 패킷 없이 사라진 클라이언트
/// (VR 전원 꺼짐, 모바일 앱 강제 종료·튕김, 네트워크 단절)는 서버가 30분 동안 접속 중으로 보고
/// 그 사람의 아바타도 30분간 남아 있었다. 서버·클라 양쪽 타임아웃을 짧게 줄인다.
/// 씬 구성과 무관하게 동작하도록 시작 시 스스로 생성된다.
/// </summary>
public class NetTimeoutGuard : MonoBehaviour
{
    // 이 시간(초) 동안 상대에게서 아무 패킷(전송 계층 핑 포함)도 없으면 끊긴 것으로 본다.
    // 핑은 전송 계층 자체 스레드에서 오가므로 씬 로딩 중 멈칫하는 정도로는 끊기지 않는다.
    private const float TimeoutSeconds = 20f;
    private const float ManagerScanInterval = 1f;

    private NetworkManager _manager;
    private float _nextManagerScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        var go = new GameObject(nameof(NetTimeoutGuard));
        DontDestroyOnLoad(go);
        go.AddComponent<NetTimeoutGuard>();
    }

    private void Update()
    {
        if(_manager != null || Time.unscaledTime < _nextManagerScan) return;
        _nextManagerScan = Time.unscaledTime + ManagerScanInterval;

        var nm = FindFirstObjectByType<NetworkManager>();
        if(nm == null) return;
        _manager = nm;
        _manager.ServerManager.OnServerConnectionState += OnServerState;
        _manager.ClientManager.OnClientConnectionState += OnClientState;
        // 이미 시작된 뒤에 찾았을 수도 있으니 지금도 한 번 적용(시작 전이면 시작 시 이 값이 쓰인다)
        Apply(true);
        Apply(false);
    }

    private void OnDestroy()
    {
        if(_manager == null) return;
        _manager.ServerManager.OnServerConnectionState -= OnServerState;
        _manager.ClientManager.OnClientConnectionState -= OnClientState;
    }

    private void OnServerState(ServerConnectionStateArgs args)
    {
        if(args.ConnectionState == LocalConnectionState.Started) Apply(true);
    }

    private void OnClientState(ClientConnectionStateArgs args)
    {
        if(args.ConnectionState == LocalConnectionState.Started) Apply(false);
    }

    private void Apply(bool asServer)
    {
        if(_manager == null) return;
        var transport = _manager.TransportManager != null ? _manager.TransportManager.Transport : null;
        if(transport == null) return;
        transport.SetTimeout(TimeoutSeconds, asServer);
        if(asServer && _manager.ServerManager.Started)
            Debug.Log($"[NetTimeoutGuard] 서버 접속 타임아웃 {TimeoutSeconds}초 적용");
    }
}
