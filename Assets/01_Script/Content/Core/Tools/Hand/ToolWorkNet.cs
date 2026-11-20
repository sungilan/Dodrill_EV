using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

// ============================================================
//  ToolWorkNet.cs
//
//  (11/20 내부 시연) 다른 사람이 쓰는 드릴(ImpactWrench) 소리가 안 들렸다.
//  ImpactWrench 는 '내가 든 렌치'만 작동 상태를 계산하고, 남이 든 렌치는 항상 멈춘 것으로 처리했기 때문이다.
//  작동을 시작·멈출 때 한 번씩 서버로 알리고, 서버는 보낸 사람을 뺀 전원에게 전한다.
//  받은 쪽은 그 렌치의 소리·회전 애니메이션만 켠다(볼트 판정은 기존처럼 든 사람 쪽에서만).
// ============================================================
public struct ToolWorkBroadcast : IBroadcast
{
    public int objectId;
    public bool working;
}

public static class ToolWorkNet
{
    private static FishNet.Managing.Server.ServerManager s_sm;
    private static FishNet.Managing.Client.ClientManager s_cm;

    public static void Init()
    {
        var sm = InstanceFinder.ServerManager;
        var cm = InstanceFinder.ClientManager;
        if (sm != null && sm != s_sm)
        {
            if (s_sm != null) s_sm.UnregisterBroadcast<ToolWorkBroadcast>(OnServer);
            sm.RegisterBroadcast<ToolWorkBroadcast>(OnServer);
            s_sm = sm;
        }
        if (cm != null && cm != s_cm)
        {
            if (s_cm != null) s_cm.UnregisterBroadcast<ToolWorkBroadcast>(OnClient);
            cm.RegisterBroadcast<ToolWorkBroadcast>(OnClient);
            s_cm = cm;
        }
    }

    public static void Report(Component tool, bool working)
    {
        if (tool == null || !InstanceFinder.IsClientStarted) return;
        var nob = tool.GetComponentInParent<NetworkObject>();
        if (nob == null || !nob.IsSpawned) return;
        Init();
        InstanceFinder.ClientManager.Broadcast(new ToolWorkBroadcast { objectId = nob.ObjectId, working = working });
    }

    private static void OnServer(NetworkConnection conn, ToolWorkBroadcast msg, Channel channel)
    {
        InstanceFinder.ServerManager.BroadcastExcept(conn, msg, true);
    }

    private static void OnClient(ToolWorkBroadcast msg, Channel channel)
    {
        var cm = InstanceFinder.ClientManager;
        if (cm == null || !cm.Objects.Spawned.TryGetValue(msg.objectId, out var nob) || nob == null) return;
        var w = nob.GetComponentInChildren<ImpactWrench>(true);
        if (w != null) w.SetRemoteWorking(msg.working);
    }
}
