using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;

// ============================================================
//  VehicleSync.cs
//
//  차량 상호작용(프렁크·냉각수 뚜껑·시동·경적)의 공통 바탕.
//  · IWorldClickable : PC 클릭/모바일 터치로 누를 수 있는 월드 물체(컨트롤러가 이 인터페이스를 보고 OnPCClick 을 부른다)
//  · ILiquidSink     : 붓는 액체(PourContainer)를 받는 물체 — 냉각수 탱크
//  · VehicleStateSync: id → 값(float) 동기화. 서버가 마지막 값을 기억하고 늦게 들어온 사람에게 전부 다시 보낸다(OpenableSync 와 같은 방식)
//  네트워크 메시지(VehicleStateBroadcast)가 새로 생겼으므로 서버·전 클라이언트를 같이 다시 빌드해야 한다.
// ============================================================

/// <summary>눌러서 쓰는 월드 물체(문·덮개·경적 등). 컨트롤러가 호버 이름·안내·클릭을 이 인터페이스로 처리한다.</summary>
/// <summary>
/// 클릭 대상 고르기: 프렁크 덮개·냉각수 뚜껑처럼 후드(ClickableAnimator) 아래에 달린 IWorldClickable 은 후드보다 먼저 눌려야 한다.
/// (예전엔 부모 쪽 후드가 먼저 잡혀 덮개를 닫으려 하면 후드가 닫히고, 뚜껑을 눌러도 후드만 움직였다)
/// </summary>
public static class WorldClickPriority
{
    /// <summary>콜라이더에서 가장 가까운 IWorldClickable 이 ClickableAnimator 보다 안쪽(같거나 자손)이면 그것, 아니면 null.</summary>
    public static IWorldClickable Nearer(Collider col)
    {
        if (col == null) return null;
        var wc = col.GetComponentInParent<IWorldClickable>();
        if (wc == null) return null;
        var ca = col.GetComponentInParent<ClickableAnimator>();
        if (ca == null) return wc;
        var wt = ((Component)wc).transform;
        return wt == ca.transform || wt.IsChildOf(ca.transform) ? wc : null;
    }
}

public interface IWorldClickable
{
    string DisplayName { get; }
    string HoverMessage(bool touch);
    void OnPCClick();
    /// <summary>VR 에서 손가락으로 찔러 누를 수 있는가(뚜껑처럼 돌려야 하는 물체는 false).</summary>
    bool VrPokeable { get; }
}

/// <summary>붓는 액체를 받는 물체. 받아들였으면 true(웅덩이를 만들지 않음).</summary>
public interface ILiquidSink
{
    bool AddLiquid(float liters, string liquidName);
}

/// <summary>동기화되는 상태 하나(문 열림, 시동 등).</summary>
public interface IVehicleState
{
    string StateId { get; }
    void ApplyState(float v);
}

public struct VehicleStateBroadcast : IBroadcast
{
    public string id;      // 비어 있으면: 클라이언트 → 서버 '전체 상태 보내 줘'
    public float v;
}

public static class VehicleStateSync
{
    private static readonly Dictionary<string, IVehicleState> s_all = new Dictionary<string, IVehicleState>();
    private static readonly Dictionary<string, float> s_server = new Dictionary<string, float>();
    private static FishNet.Managing.Server.ServerManager s_sm;
    private static FishNet.Managing.Client.ClientManager s_cm;
    private static float s_lastAsk = -10f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_all.Clear(); s_server.Clear(); s_sm = null; s_cm = null; s_lastAsk = -10f;
    }

    public static void Register(IVehicleState s)
    {
        if (s == null || string.IsNullOrEmpty(s.StateId)) return;
        s_all[s.StateId] = s;
        EnsureRegistered();
        if (s_server.TryGetValue(s.StateId, out float v)) s.ApplyState(v);
        AskAll();
    }

    public static void Unregister(IVehicleState s)
    {
        if (s == null || string.IsNullOrEmpty(s.StateId)) return;
        if (s_all.TryGetValue(s.StateId, out var cur) && cur == s)
        {
            s_all.Remove(s.StateId);
            // 차량이 사라졌으면(다음 시나리오에서 새로 생성) 이전 상태가 새 차량에 되살아나지 않게 서버 기억도 지운다
            if (InstanceFinder.IsServerStarted) s_server.Remove(s.StateId);
        }
    }

    private static void EnsureRegistered()
    {
        var sm = InstanceFinder.ServerManager;
        var cm = InstanceFinder.ClientManager;
        if (sm != null && sm != s_sm)
        {
            sm.RegisterBroadcast<VehicleStateBroadcast>(OnServer);
            s_sm = sm;
        }
        if (cm != null && cm != s_cm)
        {
            cm.RegisterBroadcast<VehicleStateBroadcast>(OnClient);
            s_cm = cm;
        }
    }

    /// <summary>(클라이언트) 서버에 지금까지의 모든 상태를 다시 보내 달라고 한다. 1초에 한 번까지만.</summary>
    public static void AskAll()
    {
        var cm = InstanceFinder.ClientManager;
        if (cm == null || !cm.Started || InstanceFinder.IsServerStarted) return;
        if (Time.unscaledTime - s_lastAsk < 1f) return;
        s_lastAsk = Time.unscaledTime;
        cm.Broadcast(new VehicleStateBroadcast { id = "" });
    }

    public static void Request(string id, float v)
    {
        if (string.IsNullOrEmpty(id)) return;
        EnsureRegistered();
        if (InstanceFinder.IsServerStarted) { ServerSet(id, v); return; }
        if (InstanceFinder.IsClientStarted) { InstanceFinder.ClientManager.Broadcast(new VehicleStateBroadcast { id = id, v = v }); return; }
        if (s_all.TryGetValue(id, out var s)) s.ApplyState(v);   // 오프라인
    }

    private static void ServerSet(string id, float v)
    {
        if (float.IsNaN(v) || float.IsInfinity(v)) return;
        v = Mathf.Clamp(v, 0f, 1000f);
        s_server[id] = v;
        if (s_all.TryGetValue(id, out var s)) s.ApplyState(v);
        InstanceFinder.ServerManager.Broadcast(new VehicleStateBroadcast { id = id, v = v });
    }

    private static void OnServer(NetworkConnection conn, VehicleStateBroadcast b, Channel ch)
    {
        if (string.IsNullOrEmpty(b.id))
        {
            foreach (var kv in s_server)
                InstanceFinder.ServerManager.Broadcast(conn, new VehicleStateBroadcast { id = kv.Key, v = kv.Value });
            return;
        }
        if (b.id.Length > 64) return;
        ServerSet(b.id, b.v);
    }

    private static void OnClient(VehicleStateBroadcast b, Channel ch)
    {
        if (InstanceFinder.IsServerStarted) return;
        if (s_all.TryGetValue(b.id, out var s)) s.ApplyState(b.v);
    }
}

/// <summary>눌렀을 때 동작을 델리게이트로 지정하는 간단한 클릭 대상(경적·냉각수 뚜껑 등).</summary>
public class ClickProxy : MonoBehaviour, IWorldClickable
{
    public string displayName = "";
    public Action onClick;
    public Func<bool, string> hover;
    public bool vrPokeable = true;

    public string DisplayName => displayName;
    public bool VrPokeable => vrPokeable;
    public string HoverMessage(bool touch) => hover != null ? hover(touch) : (touch ? "터치" : "클릭");
    public void OnPCClick() { onClick?.Invoke(); }
}
