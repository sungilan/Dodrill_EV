using UnityEngine;
using FishNet;
using FishNet.Object;
using System.Collections.Generic;

public class PlayerEquipmentSync : NetworkBehaviour
{
    [System.Serializable]
    public class EquipmentEntry
    {
        public string prefabId;

        [Header("활성화/비활성화 설정")]
        [Tooltip("착용 시 켜질 오브젝트들 (예: 절연화, 헬멧, 안면보호구)")]
        public GameObject[] objectsToActivate;

        [Tooltip("착용 시 꺼질 원래 오브젝트들 (예: 기본 신발, 기본 헤어)")]
        public GameObject[] objectsToDeactivate;

        [Header("머테리얼 설정 (장갑 등)")]
        public SkinnedMeshRenderer targetRenderer;
        public Material equipMaterial;
    }

    [Header("보호구 목록")]
    public List<EquipmentEntry> equipments = new();

    private Dictionary<string, Material[]> _originalMaterials = new();
    private HashSet<string> _equipped = new();

    public override void OnStartClient()
    {
        base.OnStartClient();
        if(!IsOwner) return;

        Debug.Log("<color=cyan>[Equipment-Internal]</color> Owner 이벤트 구독 완료");
        InteractionEvents.OnItemGrabbed += HandleItemGrabbed;
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        if(!IsOwner) return;
        InteractionEvents.OnItemGrabbed -= HandleItemGrabbed;
    }

    private void HandleItemGrabbed(string prefabId)
    {
        if(!IsOwner) return;

        Debug.Log($"<color=yellow>[Equipment-Step 1]</color> 로컬 그랩 감지됨: {prefabId}");

        if(InstanceFinder.ClientManager != null)
        {
            var taskIndex = ScenarioStateReceiver.Instance.CurrentTaskState.taskIndex;
            InstanceFinder.ClientManager.Broadcast(new ItemGrabbedSignal
            {
                clientId = InstanceFinder.ClientManager.Connection.ClientId,
                taskIndex = taskIndex,
                itemId = prefabId
            });
        }

        var entry = FindEntry(prefabId);
        if(entry == null || _equipped.Contains(prefabId)) return;

        Debug.Log($"<color=yellow>[Equipment-Step 3]</color> 서버에 RPC 요청: {prefabId}");
        EquipServerRpc(prefabId);
    }

    [ServerRpc]
    private void EquipServerRpc(string prefabId) => EquipObserversRpc(prefabId);

    [ObserversRpc(BufferLast = true)]
    private void EquipObserversRpc(string prefabId) => ApplyEquip(prefabId);

    private void ApplyEquip(string prefabId)
    {
        var entry = FindEntry(prefabId);
        if(entry == null || _equipped.Contains(prefabId)) return;

        _equipped.Add(prefabId);

        // 1. 오브젝트 활성화 처리 (여러 개 가능)
        if(entry.objectsToActivate != null)
        {
            foreach(var obj in entry.objectsToActivate)
            {
                if(obj == null) continue;
                obj.SetActive(true);

                // 본인 시야 가림 방지 (레이어 변경)
                if(IsOwner) SetLayerRecursively(obj, 2);
            }
        }

        // 2. 오브젝트 비활성화 처리 (교체되는 파츠들)
        if(entry.objectsToDeactivate != null)
        {
            foreach(var obj in entry.objectsToDeactivate)
            {
                if(obj == null) continue;
                obj.SetActive(false);
            }
        }

        // 3. 머테리얼 교체 (장갑 등)
        if(entry.targetRenderer != null && entry.equipMaterial != null)
        {
            if(!_originalMaterials.ContainsKey(prefabId))
                _originalMaterials[prefabId] = entry.targetRenderer.materials;

            var mats = new Material[entry.targetRenderer.materials.Length];
            for(int i = 0; i < mats.Length; i++) mats[i] = entry.equipMaterial;
            entry.targetRenderer.materials = mats;
        }

        Debug.Log($"<color=lime>[Equipment-Final]</color> {prefabId} 착용 및 파츠 교체 완료");
    }

    private void SetLayerRecursively(GameObject obj, int newLayer)
    {
        obj.layer = newLayer;
        foreach(Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, newLayer);
        }
    }

    private EquipmentEntry FindEntry(string prefabId)
    {
        return equipments.Find(e => string.Equals(e.prefabId, prefabId, System.StringComparison.OrdinalIgnoreCase));
    }
}