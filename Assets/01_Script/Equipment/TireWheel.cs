using System.Collections;
using FishNet.Object;
using UnityEngine;
using XRAirpotrSecurity;

/// <summary>
/// 떼어낸 휠·예비 휠. 잡아서 옮기고, 빈 허브 가까이에서 놓으면 장착된다.
/// 들고 허브에 다가가면 TireService 가 그 자리에 반투명 고스트를 보여 주고(초록 = 지금 놓으면 장착),
/// 놓으면 휠이 허브로 '착' 끼워지는 스냅 애니메이션이 나온다.
/// </summary>
[RequireComponent(typeof(SyncGrab))]
public class TireWheel : MonoBehaviour
{
    private SyncGrab _sync;
    private NetworkObject _nob;
    private bool _wasLocalHeld;

    private void Awake()
    {
        _sync = GetComponent<SyncGrab>();
        _nob = GetComponent<NetworkObject>();
        if (_sync != null) _sync.OnReleased += OnReleased;
    }

    private void OnDestroy()
    {
        if (_sync != null) _sync.OnReleased -= OnReleased;
    }

    private void Update()
    {
        bool held = _sync != null && _sync.IsOwner && _sync.IsGrabbed && !_sync.IsHolstered;
        _wasLocalHeld = held;
        if (!held) return;
        var ts = TireService.Instance;
        if (ts == null || !ts.AnyHubFree) return;

        // 가장 가까운 빈 허브에 고스트 — 멀면 연한 파랑, 놓으면 장착될 거리면 초록
        int hub = ts.FreeHubNear(transform.position, ts.GhostDistance);
        if (hub < 0) return;
        bool inRange = (ts.HubCenter(hub) - transform.position).sqrMagnitude <= ts.MountDistance * ts.MountDistance;
        ts.ShowMountGhost(hub, inRange);
        if (inRange)
        {
            bool vr = Utils.GetPlatformType() == PlatformType.VR;
            EquipFx.Notice("tire_mount", "허브에 맞춰졌습니다", vr ? "손을 놓으면 휠이 장착됩니다" : "클릭해서 내려놓으면 휠이 장착됩니다", EquipFx.Info, 1.5f, 6f);
        }
    }

    private void OnReleased()
    {
        if (!_wasLocalHeld) return;   // 남이 놓은 것·강제 해제
        _wasLocalHeld = false;
        var ts = TireService.Instance;
        if (ts == null) return;
        int hub = ts.FreeHubNear(transform.position, ts.MountDistance);
        if (hub < 0) return;
        ts.NoteMountPose(hub, transform.position);   // 스냅 애니메이션이 놓은 자리에서 시작하도록
        // 놓기(ReleaseServerRpc)가 먼저 도착하도록 한 박자 뒤에 보낸다
        ts.StartCoroutine(MountLater(ts, hub, _nob));
        AudioSource.PlayClipAtPoint(EquipFx.Click, transform.position, 0.9f);
    }

    private static IEnumerator MountLater(TireService ts, int hub, NetworkObject nob)
    {
        yield return new WaitForSeconds(0.15f);
        if (ts != null && nob != null && nob.IsSpawned) ts.MountServerRpc(hub, nob);
    }
}
