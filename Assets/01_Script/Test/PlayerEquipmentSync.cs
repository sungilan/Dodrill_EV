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

    // 서버가 들고 있는 이 플레이어의 착용 목록.
    // BufferLast 는 마지막 RPC 하나만 남기므로, 매번 "지금까지 착용한 전체 목록"을 보내야
    // 늦게 들어온 사람도 헬멧·절연화 등을 모두 본다.
    private readonly HashSet<string> _serverEquipped = new();

    [ServerRpc]
    private void EquipServerRpc(string prefabId)
    {
        if(FindEntry(prefabId) == null) return;
        if(!_serverEquipped.Add(prefabId)) return;
        var ids = new string[_serverEquipped.Count];
        _serverEquipped.CopyTo(ids);
        EquipObserversRpc(ids);
    }

    [ObserversRpc(BufferLast = true)]
    private void EquipObserversRpc(string[] prefabIds)
    {
        if(prefabIds == null) return;
        foreach(var id in prefabIds) ApplyEquip(id);
        ApplyHairRule();
    }

    // ── 헬멧 착용 시 헤어 교체 ─────────────────────────────
    private const string HairName = "SM_Mechanic_Hair";
    private const string HelmetHairName = "SM_Mechanic_Hair_Helmet";

    private void Awake()
    {
        // 프리팹에 헬멧 헤어가 켜진 채로 저장돼 있어도 시작할 때는 기본 헤어만 보이게 한다
        ApplyHairRule();
    }

    /// <summary>헬멧(안면보호구)을 썼으면 기본 헤어를 끄고 헬멧 전용 헤어를 켠다. 안 썼으면 반대.</summary>
    private void ApplyHairRule()
    {
        if(equipments == null || equipments.Count == 0) return;   // 비어 있는 예전 컴포넌트는 건드리지 않음

        bool helmetOn = false;
        foreach(var id in _equipped)
        {
            if(IsHelmetEntry(FindEntry(id))) { helmetOn = true; break; }
        }

        int hairLayer = -1;
        var helmetHairs = new List<GameObject>();
        var nob = GetComponentInParent<NetworkObject>(true);
        var root = nob != null ? nob.transform : transform;
        foreach(var t in root.GetComponentsInChildren<Transform>(true))
        {
            if(t.name == HairName)
            {
                hairLayer = t.gameObject.layer;
                t.gameObject.SetActive(!helmetOn);
            }
            else if(t.name == HelmetHairName)
            {
                helmetHairs.Add(t.gameObject);
            }
        }

        foreach(var go in helmetHairs)
        {
            go.SetActive(helmetOn);
            // 본인 화면에서는 기본 헤어와 같은 방식으로 보이게(가려지게) 레이어를 맞춘다
            if(hairLayer >= 0) SetLayerRecursively(go, hairLayer);
        }
    }

    private static bool IsHelmetEntry(EquipmentEntry entry)
    {
        if(entry == null) return false;
        if(ContainsHelmetWord(entry.prefabId)) return true;
        if(entry.objectsToActivate != null)
            foreach(var obj in entry.objectsToActivate)
                if(obj != null && (obj.name == HelmetHairName || ContainsHelmetWord(obj.name))) return true;
        return false;
    }

    private static bool ContainsHelmetWord(string s)
    {
        if(string.IsNullOrEmpty(s)) return false;
        return s.IndexOf("Helmet", System.StringComparison.OrdinalIgnoreCase) >= 0
            || s.IndexOf("FaceShield", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

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