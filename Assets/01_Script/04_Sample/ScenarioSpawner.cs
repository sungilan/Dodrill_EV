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
        public string        prefabId;
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
        const ulong prime  = 1099511628211UL;
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
    /// Task 시작 시 ScenarioRunner가 호출
    /// SpawnBundle 목록 기반으로 스폰 + 가이드 활성화
    /// </summary>
    public void SpawnForTask(TaskDef taskDef)
    {
        if (taskDef.spawnObjects == null || taskDef.spawnObjects.Count == 0) return;
        if (!InstanceFinder.IsServerStarted)
        {
            Debug.LogWarning("[ScenarioSpawner] 서버에서만 호출 가능");
            return;
        }

        int count = taskDef.spawnObjects.Count;
        for (int i = 0; i < count; i++)
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
        // 스폰 오브젝트 디스폰
        Debug.Log($"[ScenarioSpawner] DespawnCurrentTask — 대상 {_activeObjects.Count}개, IsServer={InstanceFinder.IsServerStarted}");
        foreach (var obj in _activeObjects)
        {
            if (obj == null)
            {
                Debug.LogWarning("[ScenarioSpawner] null 객체 건너뜀");
                continue;
            }

            Debug.Log($"[ScenarioSpawner] 디스폰: {obj.name}");

            // FishNet Despawn: 서버가 클라이언트에 despawn 메시지 전송 후 오브젝트 파괴
            // SetActive(false) 또는 Destroy()를 먼저 호출하면 FishNet이 패킷을 보내기 전에
            // 오브젝트가 파괴되어 클라이언트가 despawn 알림을 받지 못하는 문제 발생
            if (InstanceFinder.IsServerStarted)
                InstanceFinder.ServerManager.Despawn(obj.gameObject);
            else
                Destroy(obj.gameObject); // 클라이언트 단독 실행 시 폴백
        }
        _activeObjects.Clear();

        // 가이드 비활성화
        foreach (var guideId in _activeGuides)
            _guideRegistry?.Hide(guideId);
        _activeGuides.Clear();
    }

    // ── 내부 처리 ─────────────────────────

    private void SpawnBundle(SpawnBundle bundle, float xOffset = 0f)
    {
        // 1. 스폰 위치 결정
        Vector3    spawnPos = Vector3.zero;
        Quaternion spawnRot = Quaternion.identity;

        if (!string.IsNullOrEmpty(bundle.spawnPointId))
        {
            if (_spawnPointCache.TryGetValue(bundle.spawnPointId, out var sp))
            {
                spawnPos = sp.transform.position;
                spawnRot = sp.transform.rotation;
            }
            else
            {
                Debug.LogWarning($"[ScenarioSpawner] spawnPointId '{bundle.spawnPointId}' 없음 — 폴백 사용");
                if (_fallbackSpawnRoot != null)
                {
                    spawnPos = _fallbackSpawnRoot.position;
                    spawnRot = _fallbackSpawnRoot.rotation;
                }
            }
        }
        else if (_fallbackSpawnRoot != null)
        {
            spawnPos = _fallbackSpawnRoot.position;
            spawnRot = _fallbackSpawnRoot.rotation;
        }

        // Y 위로 띄우기 + 다중 아이템 X 분산
        spawnPos += new Vector3(xOffset, SPAWN_HEIGHT, 0f);

        // 2. prefab 스폰
        if (!string.IsNullOrEmpty(bundle.prefabId))
        {
            var prefab = FindPrefab(bundle.prefabId);
            if (prefab != null)
            {
                var instance = Instantiate(prefab, spawnPos, spawnRot);
                _activeObjects.Add(instance); // Spawn 전에 등록 (Spawn 실패해도 추적 가능)
                if (InstanceFinder.IsServerStarted)
                    InstanceFinder.ServerManager.Spawn(instance.gameObject);
                Debug.Log($"[ScenarioSpawner] 스폰: {bundle.prefabId} @ {bundle.spawnPointId}, 총 추적 {_activeObjects.Count}개");
            }
            else
            {
                Debug.LogWarning($"[ScenarioSpawner] prefabId '{bundle.prefabId}' 등록 안 됨");
            }
        }

        // 3. 가이드 활성화
        if (!string.IsNullOrEmpty(bundle.guideId))
        {
            _guideRegistry?.Show(bundle.guideId);
            _activeGuides.Add(bundle.guideId);
        }
    }

    private NetworkObject FindPrefab(string prefabId)
    {
        foreach (var entry in _prefabRegistry)
            if (entry.prefabId == prefabId) return entry.prefab;
        return null;
    }
}
