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
    private float _builtAt, _impactAt;

    public int Part => _part;

    private void Awake()
    {
        _sync = GetComponent<SyncGrab>();
        _nob = GetComponent<NetworkObject>();
        if (_sync != null)
        {
            _sync.OnReleased += OnReleased;
            // 11/18: 부품은 장착 자세 그대로 스폰돼서, 기본값(멈춘 뒤 스폰 자세로 세우기)이면 바닥에서 차에 달린 모양으로 똑바로 서 버렸다.
            // 라바콘 같은 장비용 기능이라 부품은 끄고 굴러떨어진 자세 그대로 둔다
            _sync.uprightOnRest = false;
        }
    }

    private void OnDestroy()
    {
        if (_part >= 0 && _visual != null && CarPartService.Instance != null) CarPartService.Instance.UnregisterItemVisual(_part, _visual);
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
        _builtAt = Time.time;
        svc.RegisterItemVisual(p, _visual);   // 어셈블리라면 그 안의 세부 부위가 이 겉모습을 따라다닌다

        var box = GetComponent<BoxCollider>();
        if (box != null)
        {
            box.center = b.center;
            box.size = Vector3.Max(b.size, new Vector3(0.05f, 0.05f, 0.05f));
            // 11/18: 상자(메시 범위를 돌린 꼭짓점으로 잡아 실제보다 크다)만으로는 휜 외판·암 같은 부품이 상자 모서리로 서서 바닥에서 떠 보였다
            //  → 메시 모양을 따라가는 볼록(convex) 콜라이더로 바꾸고, 못 만들면(메시 Read/Write 꺼짐) 상자를 그대로 쓴다
            if (AddHullColliders(vis.transform)) box.enabled = false;
        }
        var def = CarPartService.Defs[p];
        var ti = GetComponent<TaskItem>();
        if (ti != null) ti.prefabId = "Part_" + def.id;
        name = "Part_" + def.id;
    }

    // 떼어낸 대형·배터리 부품을 세게 던지거나 높은 데서 떨어뜨리면 부서진다(스폰 직후 바닥에 내려앉는 동안은 제외)
    private void OnCollisionEnter(Collision c)
    {
        if (_part < 0 || _nob == null || !_nob.IsSpawned) return;
        if (!(_nob.IsOwner || _nob.IsServerStarted)) return;
        if (Time.time - _builtAt < 4f || Time.time < _impactAt) return;
        if (_sync != null && _sync.IsGrabbed) return;
        var def = CarPartService.Defs[_part];
        if (!CarPartAccident.IsDamageable(def.id)) return;
        float v = c.relativeVelocity.magnitude;
        if (v < CarPartAccident.BreakSpeed) return;
        _impactAt = Time.time + 2f;
        var svc = CarPartService.Instance;
        if (svc != null) svc.ReportImpact(_part, v);
    }

    private const int MaxHulls = 12;   // 어셈블리(세부 부위 여럿)는 큰 메시부터 이만큼만 — 나머지 작은 조각은 큰 조각 안에 들어간다

    /// <summary>겉모습 메시마다 볼록 MeshCollider 를 붙인다(큰 것부터 최대 MaxHulls). 하나라도 붙였으면 true.</summary>
    private static bool AddHullColliders(Transform vis)
    {
        var list = new System.Collections.Generic.List<(MeshFilter mf, float vol)>();
        foreach (var mf in vis.GetComponentsInChildren<MeshFilter>())
        {
            var m = mf.sharedMesh;
            if (m == null || !m.isReadable || m.vertexCount < 4) continue;
            Vector3 s = Vector3.Scale(m.bounds.size, mf.transform.lossyScale);
            list.Add((mf, Mathf.Abs(s.x * s.y * s.z) + (s.x + s.y + s.z) * 1e-4f));
        }
        if (list.Count == 0) return false;
        list.Sort((a, c) => c.vol.CompareTo(a.vol));
        int made = 0;
        for (int i = 0; i < list.Count && made < MaxHulls; i++)
        {
            var mc = list[i].mf.gameObject.AddComponent<MeshCollider>();
            mc.convex = true;
            mc.sharedMesh = list[i].mf.sharedMesh;
            if (mc.sharedMesh == null) { Destroy(mc); continue; }
            made++;
        }
        return made > 0;
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
        CarPartRules.NoteMount(CarPartService.Defs[_part]);   // 재사용 금지 부품이면 경고
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
