using System.Collections;
using FishNet.Object;
using UnityEngine;
using XRAirpotrSecurity;

/// <summary>
/// 차에서 떼어낸 부품(범퍼·도어·후드 …). CarPartService 가 볼트를 다 풀면 그 자리에 Spawn 한다.
/// 어느 부위인지는 서비스가 들고 있는 ObjectId 표로 알아내고, 겉모습은 차 모델의 메시를 그대로 복사해 만든다.
/// 잡아서 옮기고, 원래 자리 가까이에서 놓으면 '착' 끼워진다(그 뒤 볼트를 조여 장착 마무리).
/// </summary>
[RequireComponent(typeof(SyncGrab))]
public class CarPartItem : MonoBehaviour
{
    private SyncGrab _sync;
    private NetworkObject _nob;
    private bool _wasLocalHeld;
    private int _part = -1;
    private Transform _visual;

    public int Part => _part;

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
        var svc = CarPartService.Instance;
        if (_part < 0) { TryBuild(svc); return; }

        bool held = _sync != null && _sync.IsOwner && _sync.IsGrabbed && !_sync.IsHolstered;
        _wasLocalHeld = held;
        if (!held || svc == null) return;
        if (svc.StateOf(_part) != (int)CarPartService.PartState.Off) return;

        // 원래 자리에 고스트 — 멀면 연한 파랑, 놓으면 장착될 거리면 초록
        if (!svc.IsFreeSlotNear(_part, transform.position, svc.GhostDistance)) return;
        var def = CarPartService.Defs[_part];
        string blk = svc.MountBlockersOf(_part);
        if (!string.IsNullOrEmpty(blk))
        {
            EquipFx.Notice("carpart_order", "장착 순서", $"{def.name}보다 {blk}을(를) 먼저 장착해야 합니다", EquipFx.Info, 2f, 4f);
            return;
        }
        bool inRange = svc.IsFreeSlotNear(_part, transform.position, svc.MountDistance);
        svc.ShowMountGhost(_part, inRange);
        if (inRange)
        {
            bool vr = Utils.GetPlatformType() == PlatformType.VR;
            EquipFx.Notice("carpart_mount", $"{def.name} 자리에 맞춰졌습니다", vr ? "손을 놓으면 끼워집니다" : "클릭해서 내려놓으면 끼워집니다", EquipFx.Info, 1.5f, 6f);
        }
    }

    private void TryBuild(CarPartService svc)
    {
        if (svc == null || _nob == null || !_nob.IsSpawned) return;
        int p = svc.PartOfItem(_nob.ObjectId);
        if (p < 0) return;

        // 프리팹에 들어 있던 겉모습(자리표)은 지우고 차 메시를 복사해 넣는다
        var old = transform.Find("Visual");
        if (old != null) Destroy(old.gameObject);
        var vis = new GameObject("PartVisual");
        vis.layer = gameObject.layer;
        vis.transform.SetParent(transform, false);
        if (!svc.BuildCopy(p, vis.transform, null, out var b)) { Destroy(vis); return; }
        _visual = vis.transform;
        _part = p;

        var box = GetComponent<BoxCollider>();
        if (box != null)
        {
            box.center = b.center;
            box.size = Vector3.Max(b.size, new Vector3(0.05f, 0.05f, 0.05f));
        }
        var def = CarPartService.Defs[p];
        var ti = GetComponent<TaskItem>();
        if (ti != null) ti.prefabId = "Part_" + def.id;
        name = "Part_" + def.id;
    }

    private void OnReleased()
    {
        if (!_wasLocalHeld) return;   // 남이 놓은 것·강제 해제
        _wasLocalHeld = false;
        var svc = CarPartService.Instance;
        if (svc == null || _part < 0) return;
        if (!svc.IsFreeSlotNear(_part, transform.position, svc.MountDistance)) return;
        if (!svc.CanMount(_part)) return;
        svc.NoteMountPose(_part, transform.position, transform.rotation);
        // 놓기(ReleaseServerRpc)가 먼저 도착하도록 한 박자 뒤에 보낸다
        svc.StartCoroutine(MountLater(svc, _part, _nob));
        AudioSource.PlayClipAtPoint(EquipFx.Click, transform.position, 0.9f);
    }

    private static IEnumerator MountLater(CarPartService svc, int p, NetworkObject nob)
    {
        yield return new WaitForSeconds(0.15f);
        if (svc != null && nob != null && nob.IsSpawned) svc.MountServerRpc(p, nob);
    }
}
