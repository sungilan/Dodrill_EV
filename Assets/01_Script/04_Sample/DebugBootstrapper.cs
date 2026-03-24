using FishNet;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;

// ============================================================
//  DebugBootstrapper.cs
//  씬의 빈 GameObject 에 붙여서 사용
//  Inspector 에서 isDebugMode 체크박스로 활성화
// ============================================================

public class DebugBootstrapper : MonoBehaviour
{
    [Header("Debug Mode")]
    [Tooltip("체크 시 디버그 시스템 활성화. 빌드 시에도 동작.")]
    public bool isDebugMode = false;

    public static DebugCommandRegistry Registry { get; private set; }
    public static bool IsDebugMode { get; private set; }

    private void Awake()
    {
        IsDebugMode = isDebugMode;

        if (!IsDebugMode)
        {
            gameObject.SetActive(false);
            return;
        }

        Registry = new DebugCommandRegistry();
        RegisterCommands();

        Debug.Log("[DebugBootstrapper] 디버그 모드 활성화");
    }

    private void OnEnable()
    {
        if (!IsDebugMode) return;

        if (InstanceFinder.ServerManager == null) return;

        if (InstanceFinder.IsServerStarted)
        {
            // 이미 서버가 떠 있으면 바로 등록
            RegisterBroadcastHandler();
        }
        else
        {
            // 서버 시작 대기 — 데디케이티드 서버에서 Awake/OnEnable 이 Start 보다 먼저 호출될 때 대응
            InstanceFinder.ServerManager.OnServerConnectionState += OnServerConnectionState;
        }
    }

    private void OnDisable()
    {
        if (InstanceFinder.ServerManager == null) return;
        InstanceFinder.ServerManager.OnServerConnectionState -= OnServerConnectionState;
        InstanceFinder.ServerManager.UnregisterBroadcast<DebugCommandBroadcast>(OnReceiveCommand);
    }

    private void OnServerConnectionState(ServerConnectionStateArgs args)
    {
        if (args.ConnectionState != LocalConnectionState.Started) return;

        // 서버 시작 완료 → 이제 핸들러 등록
        InstanceFinder.ServerManager.OnServerConnectionState -= OnServerConnectionState;
        RegisterBroadcastHandler();
    }

    private void RegisterBroadcastHandler()
    {
        InstanceFinder.ServerManager.RegisterBroadcast<DebugCommandBroadcast>(OnReceiveCommand);
        Debug.Log("[DebugBootstrapper] DebugCommandBroadcast 핸들러 등록 완료");
    }

    // ── 커맨드 등록 ───────────────────────

    private void RegisterCommands()
    {
        ScenarioDebugCommands.RegisterAll(Registry);
        GuideDebugCommands.RegisterAll(Registry);
        NetworkDebugCommands.RegisterAll(Registry);
    }

    // ── 커맨드 수신 처리 ──────────────────

    private void OnReceiveCommand(NetworkConnection conn, DebugCommandBroadcast broadcast, Channel channel)
    {
        string[] args = string.IsNullOrEmpty(broadcast.argsJson)
            ? System.Array.Empty<string>()
            : Newtonsoft.Json.JsonConvert.DeserializeObject<string[]>(broadcast.argsJson);

        Registry.Execute(broadcast.commandKey, args, broadcast.callerId);
    }
}