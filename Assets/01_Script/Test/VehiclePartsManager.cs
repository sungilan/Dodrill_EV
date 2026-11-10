
using UnityEngine;
using System.Collections.Generic;
using FishNet;
using FishNet.Broadcast;
using FishNet.Transporting;

// 부품 SetActive 동기화 Broadcast
public struct PartActiveBroadcast : IBroadcast
{
    public string partId;
    public bool active;
}

// ============================================================
//  VehiclePartsManager.cs
//  차량 부품(배터리 포함)의 활성/비활성 상태를 관리.
//  Despawn 없이 SetActive로 가시성을 제어하여 재조립 가능.
//
//  배터리 처리:
//    배터리는 차량 씬에 미리 배치 (자식 GO).
//    BatteryJack 접촉 시 → SetActive(false) (사라진 것처럼)
//    재삽입 단계 시 → SetActive(true)
// ============================================================

public class VehiclePartsManager : MonoBehaviour
{
    [System.Serializable]
    public class PartData
    {
        [Tooltip("식별 ID (JSON despawnObjectIds, 코드에서 참조)")]
        public string partId;
        public GameObject partObject;
        public BoltGroupCounter boltGroup;
    }

    [Header("차량 부품 목록")]
    [SerializeField] private List<PartData> vehicleParts = new();

    [Header("배터리 팩 (별도 관리)")]
    [Tooltip("차량 내 배치된 배터리 팩 GO")]
    public GameObject batteryPack;

    private void Start()
    {
        foreach (var part in vehicleParts)
        {
            if (part.boltGroup != null && part.partObject != null)
            {
                part.boltGroup.targetPart = part.partObject;
                Debug.Log($"[VehicleParts] ✓ {part.partId} 등록: {part.partObject.name}");
            }
        }

        RegisterBroadcast();
    }

    private void OnDestroy() => UnregisterBroadcast();

    private void RegisterBroadcast()
    {
        InstanceFinder.ServerManager?.RegisterBroadcast<PartActiveBroadcast>(OnServerPartActive);
        InstanceFinder.ClientManager?.RegisterBroadcast<PartActiveBroadcast>(OnClientPartActive);
    }

    private void UnregisterBroadcast()
    {
        InstanceFinder.ServerManager?.UnregisterBroadcast<PartActiveBroadcast>(OnServerPartActive);
        InstanceFinder.ClientManager?.UnregisterBroadcast<PartActiveBroadcast>(OnClientPartActive);
    }

    // ── 부품 SetActive 제어 ────────────────────────────────

    /// <summary>서버에서 부품 활성/비활성 + 전체 클라에 동기화</summary>
    public void SetPartActive(string partId, bool active)
    {
        if (!InstanceFinder.IsServerStarted)
        {
            // 클라이언트에서 불린 경우(리프트 애니메이션 이벤트는 클라이언트 화면에서 돈다): 내 화면에 바로 적용하고 서버에 알려 모두에게 퍼뜨린다.
            // 예전에는 여기서 그냥 끝나 배터리 리프트로 배터리를 내린 뒤에도 차에 배터리가 그대로 보였다.
            if (InstanceFinder.IsClientStarted)
            {
                ApplyPartActive(partId, active);
                InstanceFinder.ClientManager.Broadcast(new PartActiveBroadcast { partId = partId, active = active });
            }
            return;
        }

        ApplyPartActive(partId, active);
        InstanceFinder.ServerManager.Broadcast(
            new PartActiveBroadcast { partId = partId, active = active });
    }

    /// <summary>배터리 팩 숨기기 (BatteryJack 접촉 시)</summary>
    public void HideBattery()
    {
        if (batteryPack != null)
            SetPartActive("BatteryPack", false);
    }

    /// <summary>배터리 팩 다시 표시 (재삽입 단계)</summary>
    public void ShowBattery()
    {
        if (batteryPack != null)
            SetPartActive("BatteryPack", true);
    }

    // ── BoltGroup 복원 (조립 단계) ─────────────────────────

    /// <summary>
    /// 특정 부품의 볼트 그룹을 조립 모드로 초기화.
    /// ScenarioRunner에서 조립 Task 시작 시 호출.
    /// </summary>
    public void PrepareForAssemble(string partId)
    {
        var part = vehicleParts.Find(p => p.partId == partId);
        if (part == null) return;

        part.boltGroup?.ResetForAssemble();

        // 부품 자체는 볼트 체결 완료 시 활성화됨 (BoltGroupCounter가 처리)
        Debug.Log($"[VehicleParts] {partId} 조립 모드 준비 완료");
    }

    // ── 서버/클라 수신 ─────────────────────────────────────

    private void OnServerPartActive(FishNet.Connection.NetworkConnection conn,
        PartActiveBroadcast msg, Channel ch)
    {
        // 서버도 적용 (호스트 모드)
        ApplyPartActive(msg.partId, msg.active);
        // 다른 클라에 재전파
        InstanceFinder.ServerManager.Broadcast(msg);
    }

    private void OnClientPartActive(PartActiveBroadcast msg, Channel ch)
    {
        if (InstanceFinder.IsServerStarted) return; // 호스트는 서버에서 이미 처리
        ApplyPartActive(msg.partId, msg.active);
    }

    private void ApplyPartActive(string partId, bool active)
    {
        if (partId == "BatteryPack" && batteryPack != null)
        {
            batteryPack.SetActive(active);
            Debug.Log($"[VehicleParts] BatteryPack SetActive({active})");
            return;
        }

        var part = vehicleParts.Find(p => p.partId == partId);
        if (part?.partObject != null)
        {
            part.partObject.SetActive(active);
            Debug.Log($"[VehicleParts] {partId} SetActive({active})");
        }
    }

    /// <summary>
    /// 배터리 팩을 실제로 들고 있는 매니저를 찾는다.
    /// VehiclePartsManager가 차량(Ioniq5N)뿐 아니라 배터리팩 프리팹들에도 붙어 있어서
    /// 아무거나 찾으면 batteryPack이 비어 있는 쪽이 잡혀 배터리가 안 사라지거나 안 돌아온다.
    /// </summary>
    public static VehiclePartsManager FindBatteryOwner()
    {
        var all = FindObjectsByType<VehiclePartsManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        VehiclePartsManager fallback = null;
        foreach (var m in all)
        {
            if (m == null) continue;
            if (m.batteryPack != null) return m;
            if (fallback == null) fallback = m;
        }
        return fallback;
    }

    // ── 조회 ───────────────────────────────────────────────

    public GameObject GetPartByName(string partId)
        => vehicleParts.Find(p => p.partId == partId)?.partObject;

    public GameObject GetPartByBoltGroup(BoltGroupCounter boltGroup)
        => vehicleParts.Find(p => p.boltGroup == boltGroup)?.partObject;
}
