using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

public struct BuildHelloBroadcast : IBroadcast
{
    public string protocol;
    public string version;
}

public struct BuildVerdictBroadcast : IBroadcast
{
    public bool compatible;
    public string serverProtocol;
    public string serverVersion;
}

/// <summary>
/// 빌드 호환성 확인 (N-8).
/// 서버와 클라이언트는 같은 네트워크 계약(RPC·SyncVar·브로드캐스트·네트워크 프리팹)으로 빌드되어야 한다.
/// 어긋나면 FishNet 은 알리지 않고 RPC 를 흘려 버려 '접속은 되는데 캐릭터가 안 생기는' 상태가 된다.
/// → 접속 직후 계약 해시(NetworkProtocolHash 가 빌드 때 기록)를 비교해 다르면 이유를 보여 주고 끊는다.
/// 브로드캐스트는 RPC 와 달리 계약이 달라도 도달하고, 핸들러가 없는 옛 서버는 건너뛰므로 접속을 깨지 않는다.
/// 씬 구성과 무관하게 동작하도록 시작 시 스스로 생성된다.
/// </summary>
public class BuildCompat : MonoBehaviour
{
    private const string ProtocolResource = "network_protocol";
    private const string NoticeResource = "BuildCompatNotice";
    private const float ReplyTimeoutSeconds = 10f;
    private const float ManagerScanInterval = 1f;

    private NetworkManager _manager;
    private BuildCompatNotice _notice;
    private string _protocol;
    private float _nextManagerScan;
    private float _replyDeadline = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        var go = new GameObject(nameof(BuildCompat));
        DontDestroyOnLoad(go);
        go.AddComponent<BuildCompat>();
    }

    private void Awake()
    {
        var asset = Resources.Load<TextAsset>(ProtocolResource);
        _protocol = asset != null ? asset.text.Trim() : string.Empty;
        Debug.Log($"[BuildCompat] v{Application.version} 네트워크 계약 {Display(_protocol)}");

        if(!Application.isBatchMode)
        {
            var prefab = Resources.Load<BuildCompatNotice>(NoticeResource);
            if(prefab != null)
            {
                _notice = Instantiate(prefab, transform);
                _notice.SetBuildLabel($"v{Application.version} · {Display(_protocol)}");
            }
        }
    }

    private void Update()
    {
        if(_manager == null && Time.unscaledTime >= _nextManagerScan)
        {
            _nextManagerScan = Time.unscaledTime + ManagerScanInterval;
            Hook();
        }

        if(_replyDeadline >= 0f && Time.unscaledTime >= _replyDeadline)
        {
            _replyDeadline = -1f;
            // 옛 서버일 가능성이 크지만 단정할 수 없으므로 끊지 않고 알리기만 한다
            Debug.LogWarning("[BuildCompat] 서버가 빌드 확인에 응답하지 않습니다. 서버가 옛 빌드일 수 있습니다.");
            ShowNotice("서버가 빌드 확인에 응답하지 않습니다.\n서버가 이전 빌드일 수 있습니다. 캐릭터가 나타나지 않으면 관리자에게 문의해 주세요.");
        }
    }

    private void OnDestroy()
    {
        if(_manager == null) return;

        _manager.ServerManager.UnregisterBroadcast<BuildHelloBroadcast>(OnClientHello);
        _manager.ClientManager.UnregisterBroadcast<BuildVerdictBroadcast>(OnServerVerdict);
        _manager.ClientManager.OnAuthenticated -= OnAuthenticated;
        _manager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
    }

    private void Hook()
    {
        var managers = NetworkManager.Instances;
        if(managers.Count == 0 || managers[0] == null || !managers[0].Initialized) return;

        _manager = managers[0];
        _manager.ServerManager.RegisterBroadcast<BuildHelloBroadcast>(OnClientHello, requireAuthentication: false);
        _manager.ClientManager.RegisterBroadcast<BuildVerdictBroadcast>(OnServerVerdict);
        _manager.ClientManager.OnAuthenticated += OnAuthenticated;
        _manager.ClientManager.OnClientConnectionState += OnClientConnectionState;

        // 찾기 전에 이미 인증이 끝났다면 이벤트를 놓친 것이므로 바로 보낸다
        if(_manager.ClientManager.Started && _manager.ClientManager.Connection.IsAuthenticated)
            OnAuthenticated();
    }

    // ── 클라이언트 ─────────────────────────────────────────────

    private void OnAuthenticated()
    {
        _replyDeadline = Time.unscaledTime + ReplyTimeoutSeconds;
        _manager.ClientManager.Broadcast(new BuildHelloBroadcast { protocol = _protocol, version = Application.version });
    }

    private void OnClientConnectionState(ClientConnectionStateArgs args)
    {
        if(args.ConnectionState == LocalConnectionState.Stopped)
            _replyDeadline = -1f;
    }

    private void OnServerVerdict(BuildVerdictBroadcast verdict, Channel channel)
    {
        _replyDeadline = -1f;
        if(verdict.compatible)
        {
            Debug.Log($"[BuildCompat] 서버 빌드 일치 (v{verdict.serverVersion}, {Display(verdict.serverProtocol)})");
            return;
        }

        string mine = $"v{Application.version} ({Display(_protocol)})";
        string server = $"v{verdict.serverVersion} ({Display(verdict.serverProtocol)})";
        Debug.LogError($"[BuildCompat] 서버와 빌드가 다릅니다. 클라이언트 {mine} / 서버 {server}");
        ShowNotice($"서버와 앱의 빌드가 달라 접속할 수 없습니다.\n앱을 최신 빌드로 업데이트하거나 서버를 다시 배포해 주세요.\n\n앱 {mine}\n서버 {server}");
    }

    private void ShowNotice(string message)
    {
        if(_notice != null) _notice.Show(message);
    }

    // ── 서버 ───────────────────────────────────────────────────

    private void OnClientHello(NetworkConnection conn, BuildHelloBroadcast hello, Channel channel)
    {
        // 해시가 없는 쪽(한 번도 빌드 전처리를 거치지 않은 에디터 등)은 비교할 수 없으므로 통과
        bool compatible = string.IsNullOrEmpty(_protocol) || string.IsNullOrEmpty(hello.protocol) || hello.protocol == _protocol;

        _manager.ServerManager.Broadcast(conn, new BuildVerdictBroadcast
        {
            compatible = compatible,
            serverProtocol = _protocol,
            serverVersion = Application.version,
        }, requireAuthenticated: false);

        if(compatible) return;

        Debug.LogWarning($"[BuildCompat] 클라이언트 {conn.ClientId} 빌드 불일치로 연결을 끊습니다. 클라이언트 v{hello.version} ({Display(hello.protocol)}) / 서버 v{Application.version} ({Display(_protocol)})");
        conn.Disconnect(false);     // false: 위 응답을 보낸 뒤에 끊는다
    }

    private static string Display(string protocol) => string.IsNullOrEmpty(protocol) ? "해시 없음" : protocol;
}
