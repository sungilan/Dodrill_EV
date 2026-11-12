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
        CacheEntryPaths();       // 동기화 모델이 파괴되기(VRPlayer.OnStartClient) 전에 본인용 대응 경로를 적어 둔다
        MirrorInitialStates();
    }

    // 본인용 모델(VR)은 프리팹에서 안면보호구·절연화·보안경이 켜진 채로 저장돼 있어, 착용 전부터 겹쳐 보일 수 있다
    // → 시작할 때 동기화 모델의 켜짐/꺼짐 상태를 본인용 모델에 그대로 맞춘다.
    private void MirrorInitialStates()
    {
        if(equipments == null) return;
        foreach(var e in equipments)
        {
            if(e == null) continue;
            if(e.objectsToActivate != null)
                foreach(var o in e.objectsToActivate) MirrorActive(o);
            if(e.objectsToDeactivate != null)
                foreach(var o in e.objectsToDeactivate) MirrorActive(o);
        }
    }

    private void MirrorActive(GameObject syncObj)
    {
        if(syncObj == null) return;
        var twin = FindLocalTwin(syncObj.transform);
        if(twin != null && twin.gameObject.activeSelf != syncObj.activeSelf)
            twin.gameObject.SetActive(syncObj.activeSelf);
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

    // ── VR 본인 시점 아바타 ─────────────────────────────────
    // VR 플레이어 프리팹에는 정비사 모델이 두 벌 있다:
    //   · SyncAvatar/Mechanic                         — 다른 사람에게 보이는(네트워크 동기화) 모델. 보호구 목록(equipments)이 가리키는 쪽.
    //   · FinalIK/TrackingDatas/TrackerOffsets/Avatars/Mechanic — 본인 눈에 보이는 VRIK 모델.
    // 예전에는 앞의 것만 바꿔서 절연 장갑·절연화 등을 착용해도 본인 화면에는 기존 장갑·신발이 그대로였다 → 같은 경로의 짝을 찾아 똑같이 바꾼다.
    private Transform _syncMechanic;     // equipments 가 가리키는 모델의 루트('Mechanic')
    private Transform _localMechanic;    // 본인용 모델의 루트
    private bool _mirrorResolved;

    private void ResolveMirror()
    {
        if(_mirrorResolved) return;
        _mirrorResolved = true;

        var nob = GetComponentInParent<NetworkObject>(true);
        var root = nob != null ? nob.transform : transform;
        foreach(var t in root.GetComponentsInChildren<Transform>(true))
        {
            if(t.name != "Mechanic" || t.parent == null) continue;
            if(t.parent.name == "SyncAvatar") _syncMechanic = t;
            else if(t.parent.name == "Avatars") _localMechanic = t;
        }
    }

    /// <summary>동기화 모델의 오브젝트에 대응하는 본인용 모델 오브젝트를 돌려준다(없으면 null).</summary>
    private Transform FindLocalTwin(Transform syncObj)
    {
        ResolveMirror();
        if(_syncMechanic == null || _localMechanic == null || syncObj == null) return null;
        if(!syncObj.IsChildOf(_syncMechanic) || syncObj == _syncMechanic) return null;

        // 'Mechanic' 아래의 상대 경로로 찾는다
        var parts = new List<string>();
        for(var t = syncObj; t != null && t != _syncMechanic; t = t.parent) parts.Insert(0, t.name);
        return _localMechanic.Find(string.Join("/", parts));
    }

    // 본인(VR 소유자)에게는 VRPlayer.OnStartClient 가 동기화 모델(SyncAvatar)을 Destroy 한다 → equipments 가 가리키는 오브젝트가 이미 없다.
    // 그래서 '동기화 모델의 오브젝트에 대응하는 본인용 오브젝트'를 그 순간 찾을 수 없다 → Awake(파괴 전)에 상대 경로를 미리 적어 둔다.
    private class EntryPaths
    {
        public readonly List<string> activate = new List<string>();
        public readonly List<string> deactivate = new List<string>();
        public string renderer;
    }
    private readonly Dictionary<string, EntryPaths> _entryPaths = new Dictionary<string, EntryPaths>(System.StringComparer.OrdinalIgnoreCase);

    private string RelPathUnderSync(Transform t)
    {
        if(t == null || _syncMechanic == null || t == _syncMechanic || !t.IsChildOf(_syncMechanic)) return null;
        var parts = new List<string>();
        for(var x = t; x != null && x != _syncMechanic; x = x.parent) parts.Insert(0, x.name);
        return string.Join("/", parts);
    }

    private void CacheEntryPaths()
    {
        ResolveMirror();
        _entryPaths.Clear();
        if(equipments == null || _syncMechanic == null || _localMechanic == null) return;
        foreach(var e in equipments)
        {
            if(e == null || string.IsNullOrEmpty(e.prefabId)) continue;
            var p = new EntryPaths();
            if(e.objectsToActivate != null)
                foreach(var o in e.objectsToActivate) { var r = o != null ? RelPathUnderSync(o.transform) : null; if(r != null) p.activate.Add(r); }
            if(e.objectsToDeactivate != null)
                foreach(var o in e.objectsToDeactivate) { var r = o != null ? RelPathUnderSync(o.transform) : null; if(r != null) p.deactivate.Add(r); }
            if(e.targetRenderer != null) p.renderer = RelPathUnderSync(e.targetRenderer.transform);
            _entryPaths[e.prefabId] = p;
        }
    }

    private Transform LocalByPath(string path)
    {
        if(string.IsNullOrEmpty(path) || _localMechanic == null) return null;   // 소유자가 아니면 본인용 모델은 이미 없다
        return _localMechanic.Find(path);
    }

    private void ApplyEquip(string prefabId)
    {
        var entry = FindEntry(prefabId);
        if(entry == null || _equipped.Contains(prefabId)) return;

        _equipped.Add(prefabId);

        // 본인이 착용했을 때 안내 문구(작업 완료 알림 팝업 재사용)
        if(IsOwner)
            DoDrill.Training.TaskProgressUI.RequestToast($"<color=#00f800><b>{HoverNameFormatter.Format(prefabId)}</b></color>을(를)\n착용했습니다.");

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

            SwapMaterial(entry.targetRenderer, entry.equipMaterial);
        }

        // 4. 본인용 모델(VR 소유자) — 동기화 모델이 이미 파괴돼 있어도 Awake 때 적어 둔 경로로 같은 변경을 적용
        int localApplied = 0;
        if(_entryPaths.TryGetValue(prefabId, out var paths) && _localMechanic != null)
        {
            foreach(var p in paths.activate) { var tw = LocalByPath(p); if(tw != null) { tw.gameObject.SetActive(true); localApplied++; } }
            foreach(var p in paths.deactivate) { var tw = LocalByPath(p); if(tw != null) { tw.gameObject.SetActive(false); localApplied++; } }
            if(entry.equipMaterial != null && !string.IsNullOrEmpty(paths.renderer))
            {
                var tw = LocalByPath(paths.renderer);
                var rend = tw != null ? tw.GetComponent<Renderer>() : null;
                if(rend != null) { SwapMaterial(rend, entry.equipMaterial); localApplied++; }
            }
        }

        Debug.Log($"<color=lime>[Equipment-Final]</color> {prefabId} 착용 및 파츠 교체 완료" +
                  (_localMechanic != null ? $" (본인용 모델 {localApplied}곳 적용)" : ""));
    }

    private static void SwapMaterial(Renderer r, Material m)
    {
        var mats = new Material[r.materials.Length];
        for(int i = 0; i < mats.Length; i++) mats[i] = m;
        r.materials = mats;
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