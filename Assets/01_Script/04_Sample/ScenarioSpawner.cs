using System.Collections.Generic;
using FishNet;
using FishNet.Object;
using UnityEngine;
#if UNITY_EDITOR
using FishNet.Managing.Object;
using Sirenix.OdinInspector;
using UnityEditor;
#endif

// ============================================================
//  ScenarioSpawner.cs
//  서버 전용 — Task 시작 시 오브젝트 스폰 + 가이드 활성화
//             Task 종료 시 오브젝트 디스폰 + 가이드 비활성화
//
//  씬 세팅:
//    ScenarioManager
//    ├── ScenarioSpawner       ← _guideRegistry, _prefabRegistry 연결
//    └── ScenarioRunner        ← _spawner 연결
//
//    SpawnPoints (빈 GameObject)
//    ├── SpawnPoint (id: "SpawnPoint_Shelf_A")
//    └── SpawnPoint (id: "SpawnPoint_Tray")
//
//    GuideObjects
//    └── SceneGuideRegistry    ← guides 등록
//
//  동작 흐름:
//    ScenarioRunner.StartTask()
//      → SpawnForTask(taskDef)
//          → 각 SpawnBundle의 spawnPointId로 위치 찾기
//          → prefab 스폰
//          → guideId로 가이드 활성화
//
//    ScenarioRunner.CompleteCurrentTask() / RetryAfterDelay()
//      → DespawnCurrentTask()
//          → 스폰된 오브젝트 전부 디스폰
//          → 활성화된 가이드 전부 비활성화
// ============================================================

public class ScenarioSpawner : MonoBehaviour
{
    [System.Serializable]
    public struct PrefabEntry
    {
        public string prefabId;
        public NetworkObject prefab;
    }

    [Header("스폰 가능한 프리팹 목록 (prefabId → NetworkObject)")]
    [SerializeField] private List<PrefabEntry> _prefabRegistry = new();

    [Header("씬의 가이드 오브젝트 레지스트리")]
    [SerializeField] private SceneGuideRegistry _guideRegistry;

    [Header("spawnPointId 못 찾을 때 폴백 위치")]
    [SerializeField] private Transform _fallbackSpawnRoot;

    // SpawnPoint 위로 띄우는 높이 / 다중 아이템 좌우 간격
    private const float SPAWN_HEIGHT = 0.1f;
    private const float SPAWN_SPREAD = 0.2f;

    // 현재 Task에서 스폰된 NetworkObject 목록
    private readonly List<NetworkObject> _activeObjects = new();

    // map.objects로 스폰된 환경 오브젝트 목록 (차량·리프트·카트 등)
    private readonly List<GameObject> _mapObjects = new();

    // 현재 Task에서 활성화된 guideId 목록
    private readonly List<string> _activeGuides = new();

    // 씬의 모든 SpawnPoint 캐시
    private Dictionary<string, SpawnPoint> _spawnPointCache;

    // ── 에디터 자동 등록 ──────────────────
#if UNITY_EDITOR
    [Button("① 프리팹 자동 등록 (Project 전체 검색)", ButtonSizes.Medium), GUIColor(0.4f, 1f, 0.6f)]
    private void AutoFindPrefabs()
    {
        int added = 0, skipped = 0;
        var registered = new HashSet<string>();
        foreach(var entry in _prefabRegistry)
            if(!string.IsNullOrEmpty(entry.prefabId))
                registered.Add(entry.prefabId);

        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        foreach(var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefabGo = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(prefabGo == null) continue;

            var netObj = prefabGo.GetComponent<NetworkObject>();
            if(netObj == null) continue; // NetworkObject 없는 프리팹은 스킵

            // Item_ / SM_ 접두사 제거 후 prefabId로 사용
            string prefabId = prefabGo.name;
            if(prefabId.StartsWith("Item_", System.StringComparison.OrdinalIgnoreCase))
                prefabId = prefabId.Substring(5);
            else if(prefabId.StartsWith("SM_", System.StringComparison.OrdinalIgnoreCase))
                prefabId = prefabId.Substring(3);

            if(registered.Contains(prefabId)) { skipped++; continue; }

            _prefabRegistry.Add(new PrefabEntry { prefabId = prefabId, prefab = netObj });
            registered.Add(prefabId);
            added++;
            Debug.Log($"[ScenarioSpawner] 등록: {prefabId} ← {path}");
        }

        EditorUtility.SetDirty(this);
        Debug.Log($"[ScenarioSpawner] 프리팹 자동 등록 완료 — 추가:{added}, 이미등록:{skipped}");
    }

    [Button("② 프리팹 목록 초기화", ButtonSizes.Medium), GUIColor(1f, 0.5f, 0.4f)]
    private void ClearPrefabRegistry()
    {
        _prefabRegistry.Clear();
        EditorUtility.SetDirty(this);
        Debug.Log("[ScenarioSpawner] 프리팹 목록 초기화 완료");
    }

    [Button("③ GuideRegistry 자동 연결", ButtonSizes.Medium), GUIColor(0.4f, 0.8f, 1f)]
    private void AutoFindGuideRegistry()
    {
        if(_guideRegistry == null)
            _guideRegistry = FindAnyObjectByType<SceneGuideRegistry>();
        EditorUtility.SetDirty(this);
        Debug.Log($"[ScenarioSpawner] GuideRegistry: {(_guideRegistry != null ? "연결 완료" : "씬에 없음")}");
    }

    [Button("④ FishNet SpawnableObjects 등록", ButtonSizes.Medium), GUIColor(1f, 0.85f, 0.3f)]
    private void RegisterToFishNetSpawnableObjects()
    {
        // NetworkManager가 실제로 사용하는 DefaultPrefabObjects 탐색
        string assetPath = null;
        var networkManager = FindAnyObjectByType<FishNet.Managing.NetworkManager>();
        if(networkManager != null && networkManager.SpawnablePrefabs != null)
            assetPath = AssetDatabase.GetAssetPath(networkManager.SpawnablePrefabs);

        // 폴백: 후보 경로 순서대로 시도
        if(string.IsNullOrEmpty(assetPath))
        {
            string[] candidates = { "Assets/Settings/DefaultPrefabObjects.asset", "Assets/DefaultPrefabObjects.asset" };
            foreach(var c in candidates)
                if(System.IO.File.Exists(System.IO.Path.GetFullPath(c))) { assetPath = c; break; }
        }

        if(string.IsNullOrEmpty(assetPath))
        {
            EditorUtility.DisplayDialog("에셋 없음",
                "DefaultPrefabObjects 에셋을 찾을 수 없습니다.\n" +
                "씬에 NetworkManager가 있는지 확인하거나\n" +
                "Tools > Fish-Networking > Utility > Refresh Default Prefabs 를 먼저 실행하세요.",
                "확인");
            return;
        }

        var defaultPrefabs = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(assetPath);
        if(defaultPrefabs == null)
        {
            Debug.LogError("[ScenarioSpawner] DefaultPrefabObjects 로드 실패: " + assetPath);
            return;
        }

        // 이미 등록된 NetworkObject 집합
        var existingSet = new HashSet<NetworkObject>(defaultPrefabs.Prefabs);

        int added = 0, skipped = 0;
        foreach(var entry in _prefabRegistry)
        {
            if(entry.prefab == null) { skipped++; continue; }
            if(existingSet.Contains(entry.prefab)) { skipped++; continue; }

            // FNV-1 64비트 해시 설정 (FishNet DefaultPrefabObjects.SetAssetPathHashes 와 동일)
            string prefabPath = AssetDatabase.GetAssetPath(entry.prefab.gameObject);
            string pathAndName = $"{prefabPath}{entry.prefab.gameObject.name}";
            ulong hash = ComputeFNV1_64(pathAndName);
            entry.prefab.SetAssetPathHash(hash);
            EditorUtility.SetDirty(entry.prefab);

            defaultPrefabs.AddObject(entry.prefab, checkForDuplicates: true, initializeAdded: false);
            existingSet.Add(entry.prefab);
            added++;

            Debug.Log($"[ScenarioSpawner] FishNet 등록: {entry.prefabId} ← {prefabPath}");
        }

        EditorUtility.SetDirty(defaultPrefabs);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog(
            "FishNet SpawnableObjects 등록 완료",
            $"추가: {added}개  /  이미 등록: {skipped}개\n\n에셋 경로: {assetPath}",
            "확인");
    }

    // FNV-1 64비트 (FishNet Hashing.GetStableHashU64 와 동일한 알고리즘)
    private static ulong ComputeFNV1_64(string text)
    {
        const ulong prime = 1099511628211UL;
        const ulong offset = 14695981039346656037UL;
        unchecked
        {
            ulong hash = offset;
            foreach(char c in text)
            {
                hash = hash * prime;
                hash = hash ^ (ulong)c;
            }
            return hash;
        }
    }
#endif

    // ── 생명주기 ──────────────────────────

    private void Awake()
    {
        CacheSpawnPoints();
    }

    private void CacheSpawnPoints()
    {
        _spawnPointCache = new Dictionary<string, SpawnPoint>();
        foreach(var sp in FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if(string.IsNullOrEmpty(sp.pointId))
            {
                Debug.LogWarning($"[ScenarioSpawner] SpawnPoint '{sp.name}' pointId 비어있음 — 스킵");
                continue;
            }
            if(_spawnPointCache.ContainsKey(sp.pointId))
            {
                Debug.LogWarning($"[ScenarioSpawner] 중복 pointId: '{sp.pointId}' — 첫 번째만 사용");
                continue;
            }
            _spawnPointCache[sp.pointId] = sp;
        }
        Debug.Log($"[ScenarioSpawner] SpawnPoint {_spawnPointCache.Count}개 캐시 완료");
    }

    // ── 외부 API ──────────────────────────

    /// <summary>
    /// 시나리오 시작 시 ScenarioRunner가 호출.
    /// ScenarioData.map.objects 배열을 읽어 차량·리프트·카트 등 환경 오브젝트를 스폰.
    /// VehicleSpawner 없이도 JSON만으로 차량 배치 가능.
    ///
    /// MapObjectEntry.properties 활용 예:
    ///   { "key": "vin",           "ko": "KMHC251XXXXXXXX" }  → 차량 VIN 텍스트 설정
    ///   { "key": "batteryStatus", "ko": "SOC 68%" }          → 배터리 상태 표시
    /// </summary>
    public void SpawnMapObjects(MapData mapData)
    {
        if(mapData == null || mapData.objects == null || mapData.objects.Count == 0)
        {
            Debug.Log("[ScenarioSpawner] map.objects 없음 — 스킵");
            return;
        }

        if(!InstanceFinder.IsServerStarted)
        {
            Debug.LogWarning("[ScenarioSpawner] SpawnMapObjects는 서버에서만 호출 가능");
            return;
        }

        // 기존 맵 오브젝트가 있으면 먼저 제거
        DespawnMapObjects();

        foreach(var entry in mapData.objects)
        {
            if(string.IsNullOrEmpty(entry.prefabId)) continue;

            var prefab = FindPrefab(entry.prefabId);
            if(prefab == null)
            {
                Debug.LogWarning($"[ScenarioSpawner] map prefabId '{entry.prefabId}' 등록 안 됨 — 스킵");
                continue;
            }

            Vector3 pos = entry.position.ToVector3();
            Quaternion rot = Quaternion.Euler(entry.rotation.ToVector3());
            Vector3 scale = entry.scale.ToVector3();

            if(scale == Vector3.zero) scale = Vector3.one;

            var go = Instantiate(prefab.gameObject, pos, rot);
            go.transform.localScale = scale;
            go.name = entry.prefabId; // JSON에 적힌 "Ioniq5Nfree"가 이름이 됨

            ApplyProperties(go, entry.properties);
            InstanceFinder.ServerManager.Spawn(go);
            SpawnChildNetworkObjects(go);

            _mapObjects.Add(go);
            Debug.Log($"[ScenarioSpawner] 맵 스폰: {entry.prefabId} @ {pos}");

            // ★ 차량이 스폰되자마자 즉시 리프트 연결을 시도합니다.
            TryLinkVehicleToLift(go);
        }

        Debug.Log($"[ScenarioSpawner] SpawnMapObjects 완료 — {_mapObjects.Count}개");
    }

    /// <summary>
    /// 스폰된 오브젝트가 차량이면, 씬에 있는 리프트에 즉시 연결합니다.
    /// </summary>
    private void TryLinkVehicleToLift(GameObject go)
    {
        string id = go.name.ToLower();
        bool isVehicle = id.Contains("ioniq") || id.Contains("vehicle") || id.Contains("car");

        // 차량이 아닌 일반 부품/장비면 조용히 넘어갑니다.
        if(!isVehicle) return;

        Debug.Log($"🔍 [ScenarioSpawner] 스폰된 차량 감지됨: {go.name} - 리프트 연결 시도 중...");

        // 씬에 배치된 리프트 찾기 (꺼져있는 오브젝트도 무조건 찾음)
        var lift = FindFirstObjectByType<VehicleLiftController>(FindObjectsInactive.Include);

        if(lift != null)
        {
            lift.AttachVehicle(go.transform);
            Debug.Log($"🎉 [ScenarioSpawner] VehicleLiftController ← {go.name} 즉시 자동 연결 완료!");
        }
        else
        {
            Debug.LogError($"❌ [ScenarioSpawner] 차량({go.name})이 스폰되었으나, 씬에서 'VehicleLiftController' 컴포넌트를 가진 오브젝트를 아예 찾을 수 없습니다!");
        }
    }

    /// <summary>맵 오브젝트 전체 디스폰 (씬 리셋 또는 시나리오 재시작 시)</summary>
    public void DespawnMapObjects()
    {
        foreach(var go in _mapObjects)
        {
            if(go == null) continue;
            if(InstanceFinder.IsServerStarted)
                InstanceFinder.ServerManager.Despawn(go);
            else
                Destroy(go);
        }
        _mapObjects.Clear();
    }

    // ── 내부 헬퍼 ─────────────────────────────────────────────

    /// <summary>
    /// SyncGrab이 붙은 자식 NetworkObject 개별 스폰.
    /// 루트 Spawn() 만으로는 자식 NetworkObject가 자동 스폰되지 않음.
    /// </summary>
    private void SpawnChildNetworkObjects(GameObject root)
    {
        foreach(var nob in root.GetComponentsInChildren<FishNet.Object.NetworkObject>(true))
        {
            // 루트 자신은 이미 스폰됨
            if(nob.gameObject == root) continue;
            // SyncGrab이 있는 부품만 개별 스폰
            if(nob.GetComponent<SyncGrab>() == null) continue;
            if(nob.IsSpawned) continue;

            InstanceFinder.ServerManager.Spawn(nob.gameObject);
            Debug.Log($"[ScenarioSpawner] 부품 스폰: {nob.gameObject.name}");
        }
    }

    /// <summary>MapObjectEntry.properties를 GO에 적용</summary>
    private void ApplyProperties(GameObject go, List<StringKVPair> properties)
    {
        if(properties == null || properties.Count == 0) return;

        // IPropertyReceiver를 구현한 컴포넌트에게 위임
        var receiver = go.GetComponentInChildren<IMapPropertyReceiver>();
        if(receiver != null)
        {
            foreach(var kv in properties)
                receiver.OnMapProperty(kv.key, kv.ko, kv.en);
            return;
        }

        // 폴백: TMP_Text에 vin 값 설정
        foreach(var kv in properties)
        {
            if(kv.key == "vin")
            {
                var tmp = go.GetComponentInChildren<TMPro.TextMeshPro>();
                if(tmp != null) tmp.text = kv.ko;
            }
        }
    }

    /// <summary>
    /// <summary>
    /// Task 시작 시 ScenarioRunner가 호출
    /// SpawnBundle 목록 기반으로 스폰 + 가이드 활성화
    /// </summary>
    public void SpawnForTask(TaskDef taskDef)
    {
        if(taskDef.spawnObjects == null || taskDef.spawnObjects.Count == 0) return;
        if(!InstanceFinder.IsServerStarted)
        {
            Debug.LogWarning("[ScenarioSpawner] 서버에서만 호출 가능");
            return;
        }

        int count = taskDef.spawnObjects.Count;
        for(int i = 0; i < count; i++)
        {
            // 아이템이 여러 개면 중앙 기준으로 좌우 분산
            // ex) 2개: -0.2 / +0.2 / 3개: -0.4 / 0 / +0.4
            float xOffset = (i - (count - 1) * 0.5f) * SPAWN_SPREAD;
            SpawnBundle(taskDef.spawnObjects[i], xOffset);
        }
    }

    /// <summary>
    /// Task 종료(완료/Fail 재시작) 시 ScenarioRunner가 호출
    /// 현재 Task의 스폰 오브젝트 디스폰 + 가이드 비활성화
    /// </summary>
    public void DespawnCurrentTask()
    {
        foreach(var obj in _activeObjects)
        {
            if(obj == null) continue;

            // 1. 강제 릴리즈 (Hand UI 제거 핵심)
            // 자식 오브젝트까지 싹 뒤져서 SyncGrab을 찾습니다.
            var syncGrabs = obj.GetComponentsInChildren<SyncGrab>(true);
            foreach(var sg in syncGrabs)
            {
                sg.StopPCHold(); // PC 레이저/핸들 UI 즉시 파괴
                sg.RequestRelease();
            }

            var grabbables = obj.GetComponentsInChildren<Autohand.Grabbable>(true);
            foreach(var gr in grabbables)
            {
                gr.ForceHandsRelease(); // VR 손 해제
            }

            // 2. 네트워크 디스폰
            if(InstanceFinder.IsServerStarted)
                InstanceFinder.ServerManager.Despawn(obj.gameObject);
            else
                Destroy(obj.gameObject);
        }
        _activeObjects.Clear();

        // 가이드 정리
        foreach(var guideId in _activeGuides)
            _guideRegistry?.Hide(guideId);
        _activeGuides.Clear();
    }

    // ── 내부 처리 ─────────────────────────

    private void SpawnBundle(SpawnBundle bundle, float xOffset = 0f)
    {
        // 1. 스폰 위치 결정
        Vector3 spawnPos = Vector3.zero;
        Quaternion spawnRot = Quaternion.identity;

        if(!string.IsNullOrEmpty(bundle.spawnPointId))
        {
            if(_spawnPointCache.TryGetValue(bundle.spawnPointId, out var sp))
            {
                spawnPos = sp.transform.position;
                spawnRot = sp.transform.rotation;
            }
            else
            {
                Debug.LogWarning($"[ScenarioSpawner] spawnPointId '{bundle.spawnPointId}' 없음 — 폴백 사용");
                if(_fallbackSpawnRoot != null)
                {
                    spawnPos = _fallbackSpawnRoot.position;
                    spawnRot = _fallbackSpawnRoot.rotation;
                }
            }
        }
        else if(_fallbackSpawnRoot != null)
        {
            spawnPos = _fallbackSpawnRoot.position;
            spawnRot = _fallbackSpawnRoot.rotation;
        }

        // Y 위로 띄우기 + 다중 아이템 X 분산
        spawnPos += new Vector3(xOffset, SPAWN_HEIGHT, 0f);

        // 2. prefab 스폰
        if(!string.IsNullOrEmpty(bundle.prefabId))
        {
            var prefab = FindPrefab(bundle.prefabId);
            if(prefab != null)
            {
                var instance = Instantiate(prefab, spawnPos, spawnRot);
                _activeObjects.Add(instance); // Spawn 전에 등록 (Spawn 실패해도 추적 가능)
                if(InstanceFinder.IsServerStarted)
                    InstanceFinder.ServerManager.Spawn(instance.gameObject);
                Debug.Log($"[ScenarioSpawner] 스폰: {bundle.prefabId} @ {bundle.spawnPointId}, 총 추적 {_activeObjects.Count}개");
            }
            else
            {
                Debug.LogWarning($"[ScenarioSpawner] prefabId '{bundle.prefabId}' 등록 안 됨");
            }
        }

        // 3. 가이드 활성화
        if(!string.IsNullOrEmpty(bundle.guideId))
        {
            _guideRegistry?.Show(bundle.guideId);
            _activeGuides.Add(bundle.guideId);
        }
    }

    private NetworkObject FindPrefab(string prefabId)
    {
        foreach(var entry in _prefabRegistry)
            if(entry.prefabId == prefabId) return entry.prefab;
        return null;
    }
}

// ============================================================
//  IMapPropertyReceiver
//  MapObjectEntry.properties를 수신하고 싶은 컴포넌트가 구현
//
//  예시 — 차량 VIN 표시 컴포넌트:
//    public class VehicleDisplay : MonoBehaviour, IMapPropertyReceiver
//    {
//        public void OnMapProperty(string key, string ko, string en)
//        {
//            if (key == "vin") vinText.text = ko;
//            if (key == "batteryStatus") socText.text = ko;
//        }
//    }
// ============================================================
public interface IMapPropertyReceiver
{
    void OnMapProperty(string key, string ko, string en);
}