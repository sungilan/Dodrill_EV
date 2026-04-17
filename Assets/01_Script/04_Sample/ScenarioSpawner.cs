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

    // --- [추가] persist=true 로 유지되고 있는 오브젝트 목록 ---
    private readonly List<NetworkObject> _persistedObjects = new();

    // map.objects로 스폰된 환경 오브젝트 목록 (차량·리프트·카트 등)
    private readonly List<GameObject> _mapObjects = new();

    // 현재 Task에서 활성화된 guideId 목록
    private readonly List<string> _activeGuides = new();

    // 씬의 모든 SpawnPoint 캐시
    private Dictionary<string, SpawnPoint> _spawnPointCache;

    // prefabId로 Persisted 오브젝트를 찾는 헬퍼
    private NetworkObject GetPersistedObject(string prefabId)
    {
        return _persistedObjects.Find(x => x.name == $"{prefabId}(Persist)");
    }

    // ── 에디터 자동 등록 ──────────────────
#if UNITY_EDITOR
    [Button("① 프리팹 자동 등록 (Project 전체 검색)", ButtonSizes.Medium), GUIColor(0.4f, 1f, 0.6f)]
    private void AutoFindPrefabs()
    {
        int added = 0, skipped = 0;
        var registered = new HashSet<string>();
        foreach (var entry in _prefabRegistry)
            if (!string.IsNullOrEmpty(entry.prefabId))
                registered.Add(entry.prefabId);

        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefabGo = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefabGo == null) continue;

            var netObj = prefabGo.GetComponent<NetworkObject>();
            if (netObj == null) continue; // NetworkObject 없는 프리팹은 스킵

            // Item_ / SM_ 접두사 제거 후 prefabId로 사용
            string prefabId = prefabGo.name;
            if (prefabId.StartsWith("Item_", System.StringComparison.OrdinalIgnoreCase))
                prefabId = prefabId.Substring(5);
            else if (prefabId.StartsWith("SM_", System.StringComparison.OrdinalIgnoreCase))
                prefabId = prefabId.Substring(3);

            if (registered.Contains(prefabId)) { skipped++; continue; }

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
        if (_guideRegistry == null)
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
        if (networkManager != null && networkManager.SpawnablePrefabs != null)
            assetPath = AssetDatabase.GetAssetPath(networkManager.SpawnablePrefabs);

        // 폴백: 후보 경로 순서대로 시도
        if (string.IsNullOrEmpty(assetPath))
        {
            string[] candidates = { "Assets/Settings/DefaultPrefabObjects.asset", "Assets/DefaultPrefabObjects.asset" };
            foreach (var c in candidates)
                if (System.IO.File.Exists(System.IO.Path.GetFullPath(c))) { assetPath = c; break; }
        }

        if (string.IsNullOrEmpty(assetPath))
        {
            EditorUtility.DisplayDialog("에셋 없음",
                "DefaultPrefabObjects 에셋을 찾을 수 없습니다.\n" +
                "씬에 NetworkManager가 있는지 확인하거나\n" +
                "Tools > Fish-Networking > Utility > Refresh Default Prefabs 를 먼저 실행하세요.",
                "확인");
            return;
        }

        var defaultPrefabs = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(assetPath);
        if (defaultPrefabs == null)
        {
            Debug.LogError("[ScenarioSpawner] DefaultPrefabObjects 로드 실패: " + assetPath);
            return;
        }

        // 이미 등록된 NetworkObject 집합
        var existingSet = new HashSet<NetworkObject>(defaultPrefabs.Prefabs);

        int added = 0, skipped = 0;
        foreach (var entry in _prefabRegistry)
        {
            if (entry.prefab == null) { skipped++; continue; }
            if (existingSet.Contains(entry.prefab)) { skipped++; continue; }

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
            foreach (char c in text)
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
        foreach (var sp in FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (string.IsNullOrEmpty(sp.pointId))
            {
                Debug.LogWarning($"[ScenarioSpawner] SpawnPoint '{sp.name}' pointId 비어있음 — 스킵");
                continue;
            }
            if (_spawnPointCache.ContainsKey(sp.pointId))
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
        if (mapData == null || mapData.objects == null || mapData.objects.Count == 0)
        {
            Debug.Log("[ScenarioSpawner] map.objects 없음 — 스킵");
            return;
        }

        if (!InstanceFinder.IsServerStarted)
        {
            Debug.LogWarning("[ScenarioSpawner] SpawnMapObjects는 서버에서만 호출 가능");
            return;
        }

        // 기존 맵 오브젝트가 있으면 먼저 제거
        DespawnMapObjects();

        foreach (var entry in mapData.objects)
        {
            if (string.IsNullOrEmpty(entry.prefabId)) continue;

            var prefab = FindPrefab(entry.prefabId);
            if (prefab == null)
            {
                Debug.LogWarning($"[ScenarioSpawner] map prefabId '{entry.prefabId}' 등록 안 됨 — 스킵");
                continue;
            }

            // JSON position/rotation/scale → Unity Transform
            // Vector3Data는 struct이므로 null 체크 없이 직접 변환
            // JSON 파싱 실패 시 x=0,y=0,z=0 기본값이 자동 적용됨
            Vector3 pos = entry.position.ToVector3();
            Quaternion rot = Quaternion.Euler(entry.rotation.ToVector3());
            Vector3 scale = entry.scale.ToVector3();

            // scale이 (0,0,0)이면 JSON에 scale 미지정 → 기본값 (1,1,1) 적용
            if (scale == Vector3.zero) scale = Vector3.one;

            var go = Instantiate(prefab.gameObject, pos, rot);
            go.transform.localScale = scale;
            go.name = entry.prefabId; // 씬 계층에서 식별하기 쉽게

            // 프로퍼티 적용 (VIN, 배터리 상태 등)
            ApplyProperties(go, entry.properties);

            // FishNet 스폰
            InstanceFinder.ServerManager.Spawn(go);

            // 차량 부품(SyncGrab 포함) 자동 개별 스폰
            SpawnChildNetworkObjects(go);

            // VehicleLiftController 자동 차량 연결
            TryLinkVehicleToLift(go);

            _mapObjects.Add(go);
            Debug.Log($"[ScenarioSpawner] 맵 스폰: {entry.prefabId} @ {pos}");
        }

        Debug.Log($"[ScenarioSpawner] SpawnMapObjects 완료 — {_mapObjects.Count}개");
    }

    /// <summary>맵 오브젝트 전체 디스폰 (씬 리셋 또는 시나리오 재시작 시)</summary>
    public void DespawnMapObjects()
    {
        foreach (var go in _mapObjects)
        {
            if (go == null) continue;
            if (InstanceFinder.IsServerStarted)
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
        foreach (var nob in root.GetComponentsInChildren<FishNet.Object.NetworkObject>(true))
        {
            // 루트 자신은 이미 스폰됨
            if (nob.gameObject == root) continue;
            // SyncGrab이 있는 부품만 개별 스폰
            if (nob.GetComponent<SyncGrab>() == null) continue;
            if (nob.IsSpawned) continue;

            InstanceFinder.ServerManager.Spawn(nob.gameObject);
            Debug.Log($"[ScenarioSpawner] 부품 스폰: {nob.gameObject.name}");
        }
    }

    /// <summary>
    /// 씬의 VehicleLiftController에 스폰된 차량을 자동 연결.
    /// prefabId에 "Ioniq" 또는 "Vehicle"이 포함된 경우에만 시도.
    /// </summary>
    private void TryLinkVehicleToLift(GameObject go)
    {
        string id = go.name;
        bool isVehicle = id.Contains("Ioniq") || id.Contains("Vehicle") || id.Contains("Car");
        if (!isVehicle) return;

        var lift = FindFirstObjectByType<VehicleLiftController>();
        if (lift != null)
        {
            lift.AttachVehicle(go.transform);
            Debug.Log($"[ScenarioSpawner] VehicleLiftController ← {go.name} 자동 연결");
        }
    }

    /// <summary>MapObjectEntry.properties를 GO에 적용</summary>
    private void ApplyProperties(GameObject go, List<StringKVPair> properties)
    {
        if (properties == null || properties.Count == 0) return;

        // IPropertyReceiver를 구현한 컴포넌트에게 위임
        var receiver = go.GetComponentInChildren<IMapPropertyReceiver>();
        if (receiver != null)
        {
            foreach (var kv in properties)
                receiver.OnMapProperty(kv.key, kv.ko, kv.en);
            return;
        }

        // 폴백: TMP_Text에 vin 값 설정
        foreach (var kv in properties)
        {
            if (kv.key == "vin")
            {
                var tmp = go.GetComponentInChildren<TMPro.TextMeshPro>();
                if (tmp != null) tmp.text = kv.ko;
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
        if(!InstanceFinder.IsServerStarted) return;

        int count = taskDef.spawnObjects.Count;
        for(int i = 0; i < count; i++)
        {
            // 아이템 좌우 분산 계산
            float xOffset = (i - (count - 1) * 0.5f) * SPAWN_SPREAD;
            SpawnBundle(taskDef.spawnObjects[i], xOffset);
        }
    }

    /// <summary>
    /// Task 종료 시 호출. 
    /// 1. despawnObjectIds에 명시된 persist 오브젝트 삭제
    /// 2. 일반 오브젝트 삭제 및 새로 생긴 persist 오브젝트 이관
    /// </summary>
    public void DespawnCurrentTask(TaskDef taskDef)
    {
        // 1. 이번 단계에서 명시적으로 "지워라"고 한 놈들 처리
        if(taskDef.despawnObjectIds != null)
        {
            foreach(string id in taskDef.despawnObjectIds)
            {
                NetworkObject target = GetPersistedObject(id);
                if(target != null)
                {
                    _persistedObjects.Remove(target);
                    _activeObjects.Remove(target);
                    if(InstanceFinder.IsServerStarted) InstanceFinder.ServerManager.Despawn(target.gameObject);
                    Debug.Log($"[Spawner] 조건부 유지 종료 - 삭제: {id}");
                }
            }
        }

        // 2. 나머지 오브젝트 처리 (역순 순회 필수)
        for(int i = _activeObjects.Count - 1; i >= 0; i--)
        {
            var obj = _activeObjects[i];
            if(obj == null) continue;

            if(!obj.name.Contains("(Persist)"))
            {
                if(InstanceFinder.IsServerStarted) InstanceFinder.ServerManager.Despawn(obj.gameObject);
                else Destroy(obj.gameObject);
            }
            else
            {
                // (Persist)가 있으면 유지 목록으로 이동
                if(!_persistedObjects.Contains(obj)) _persistedObjects.Add(obj);
            }
        }
        _activeObjects.Clear();

        // 3. 가이드 비활성화
        foreach(var guideId in _activeGuides) _guideRegistry?.Hide(guideId);
        _activeGuides.Clear();
    }

    // ── 내부 처리 ─────────────────────────

    //private void SpawnBundle(SpawnBundle bundle, float xOffset = 0f)
    //{
    //    // 1. 위치 결정
    //    Vector3 spawnPos = _fallbackSpawnRoot != null ? _fallbackSpawnRoot.position : Vector3.zero;
    //    Quaternion spawnRot = _fallbackSpawnRoot != null ? _fallbackSpawnRoot.rotation : Quaternion.identity;

    //    Transform targetParent = null;

    //    if(!string.IsNullOrEmpty(bundle.spawnPointId) && _spawnPointCache.TryGetValue(bundle.spawnPointId, out var sp))
    //    {
    //        spawnPos = sp.transform.position;
    //        spawnRot = sp.transform.rotation;

    //        if(bundle.attachToSpawnPoint)
    //        {
    //            targetParent = sp.transform;
    //        }
    //    }
    //    spawnPos += new Vector3(xOffset, SPAWN_HEIGHT, 0f);

    //    // 2. 스폰 또는 재사용
    //    if(!string.IsNullOrEmpty(bundle.prefabId))
    //    {
    //        NetworkObject existingPersist = GetPersistedObject(bundle.prefabId);

    //        if(existingPersist != null)
    //        {
    //            Debug.Log($"[Spawner] 기존 persist 오브젝트 재사용: {bundle.prefabId}");
    //            _persistedObjects.Remove(existingPersist);
    //            _activeObjects.Add(existingPersist);

    //            //existingPersist.transform.position = spawnPos;
    //            //existingPersist.transform.rotation = spawnRot;
    //        }
    //        else
    //        {
    //            var prefab = FindPrefab(bundle.prefabId);
    //            if(prefab != null)
    //            {
    //                // Instantiate 시점에 부모 지정 여부 결정
    //                var instance = Instantiate(prefab, spawnPos, spawnRot, targetParent);
    //                instance.name = bundle.persist ? $"{bundle.prefabId}(Persist)" : bundle.prefabId;

    //                _activeObjects.Add(instance);
    //                if(InstanceFinder.IsServerStarted)
    //                    InstanceFinder.ServerManager.Spawn(instance.gameObject);
    //            }
    //            else
    //            {
    //                Debug.LogWarning($"[Spawner] 프리팹을 찾을 수 없음: {bundle.prefabId}");
    //            }
    //        }
    //    }

    //    // 3. 가이드 활성화
    //    if(!string.IsNullOrEmpty(bundle.guideId))
    //    {
    //        _guideRegistry?.Show(bundle.guideId);
    //        _activeGuides.Add(bundle.guideId);
    //    }
    //}
    private void SpawnBundle(SpawnBundle bundle, float xOffset = 0f)
    {
        // 1. 위치 결정
        Vector3 spawnPos = _fallbackSpawnRoot != null ? _fallbackSpawnRoot.position : Vector3.zero;
        Quaternion spawnRot = _fallbackSpawnRoot != null ? _fallbackSpawnRoot.rotation : Quaternion.identity;

        Transform targetParent = null;

        if(!string.IsNullOrEmpty(bundle.spawnPointId) && _spawnPointCache.TryGetValue(bundle.spawnPointId, out var sp))
        {
            spawnPos = sp.transform.position;
            spawnRot = sp.transform.rotation;

            if(bundle.attachToSpawnPoint)
            {
                targetParent = sp.transform;
            }
        }
        spawnPos += new Vector3(xOffset, SPAWN_HEIGHT, 0f);

        // 2. 스폰 또는 재사용
        if(!string.IsNullOrEmpty(bundle.prefabId))
        {
            NetworkObject existingPersist = GetPersistedObject(bundle.prefabId);
            NetworkObject instanceToSpawn = null;

            if(existingPersist != null)
            {
                Debug.Log($"[Spawner] 기존 persist 오브젝트 재사용: {bundle.prefabId}");
                _persistedObjects.Remove(existingPersist);
                instanceToSpawn = existingPersist;
            }
            else
            {
                var prefab = FindPrefab(bundle.prefabId);
                if(prefab != null)
                {
                    // ✅ 수정: Instantiate 시점에는 부모를 지정하지 않음 (targetParent 제외)
                    var instanceGo = Instantiate(prefab.gameObject, spawnPos, spawnRot);
                    instanceToSpawn = instanceGo.GetComponent<NetworkObject>();
                    instanceToSpawn.name = bundle.persist ? $"{bundle.prefabId}(Persist)" : bundle.prefabId;
                }
                else
                {
                    Debug.LogWarning($"[Spawner] 프리팹을 찾을 수 없음: {bundle.prefabId}");
                }
            }

            // 실제 네트워크 스폰 및 부모 설정 실행
            if(instanceToSpawn != null)
            {
                _activeObjects.Add(instanceToSpawn);

                if(InstanceFinder.IsServerStarted)
                {
                    // ✅ 1단계: 서버 매니저를 통해 네트워크 스폰 (월드 루트 상태로 스폰됨)
                    InstanceFinder.ServerManager.Spawn(instanceToSpawn.gameObject);

                    // ✅ 2단계: 스폰 완료 후 FishNet API로 부모 설정 (동기화 보장)
                    if(bundle.attachToSpawnPoint && targetParent != null)
                    {
                        // 부모가 NetworkObject를 가지고 있는지 확인 (FishNet 권장 사항)
                        var parentNob = targetParent.GetComponent<NetworkObject>() ?? targetParent.GetComponentInParent<NetworkObject>();

                        if(parentNob != null)
                        {
                            instanceToSpawn.SetParent(parentNob);
                        }
                        else
                        {
                            // 부모가 NetworkObject가 없는 단순 Transform일 경우의 폴백
                            instanceToSpawn.transform.SetParent(targetParent);
                        }
                    }
                }
            }
        }

        // 3. 가이드 활성화
        if(!string.IsNullOrEmpty(bundle.guideId))
        {
            _guideRegistry?.Show(bundle.guideId);
            _activeGuides.Add(bundle.guideId);
        }
    }

    public void RegisterExternalPersistObject(string prefabId, NetworkObject netObj)
    {
        if(netObj == null) return;

        // JSON에서 찾는 이름 규칙에 맞게 이름 변경
        netObj.gameObject.name = $"{prefabId}(Persist)";

        if(!_persistedObjects.Contains(netObj))
        {
            _persistedObjects.Add(netObj);
            Debug.Log($"[Spawner] 외부 오브젝트 등록 완료: {netObj.name}");
        }
    }

    /// <summary>시나리오 완전 종료 시 호출하여 남겨둔 persist 오브젝트들을 모두 정리</summary>
    public void DespawnAllPersisted()
    {
        foreach(var obj in _persistedObjects)
        {
            if(obj != null && InstanceFinder.IsServerStarted)
                InstanceFinder.ServerManager.Despawn(obj.gameObject);
        }
        _persistedObjects.Clear();
        Debug.Log("[ScenarioSpawner] 모든 Persisted 오브젝트 정리 완료");
    }

    private NetworkObject FindPrefab(string prefabId)
    {
        var entry = _prefabRegistry.Find(e => e.prefabId == prefabId);
        return entry.prefab;
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