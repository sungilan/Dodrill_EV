using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  FreeModeSpawner.cs
//  인벤토리에서 부품 버튼 클릭 시 월드에 스폰
//
//  씬 세팅:
//    - _Managers 하위에 배치
//    - partPrefabs 리스트에 partId ↔ 프리팹 연결
//    - spawnAnchor: 부품이 스폰될 위치 Transform (카메라 앞 적당한 곳)
// ============================================================
public class FreeModeSpawner : MonoBehaviour
{
    public static FreeModeSpawner Instance { get; private set; }

    [System.Serializable]
    public class PrefabEntry
    {
        public string partId;
        public GameObject prefab;
    }

    [Header("프리팹 목록")]
    public List<PrefabEntry> partPrefabs = new();

    [Header("스폰 위치")]
    public Transform spawnAnchor;   // 없으면 플레이어 앞 1.5m에 자동 배치

    private Camera _cam;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        _cam = Camera.main;
    }

    // ── 인벤토리 UI에서 호출 ──────────────────────────────

    public void SpawnPart(string partId)
    {
        // 1. 인벤토리에서 제거
        var data = FreeModeInventory.Instance?.RemovePart(partId);
        if (data == null)
        {
            Debug.LogWarning($"[FreeModeSpawner] 인벤토리에 {partId} 없음");
            return;
        }

        // 2. 프리팹 찾기
        var entry = partPrefabs.Find(e => e.partId == partId);
        if (entry == null || entry.prefab == null)
        {
            Debug.LogWarning($"[FreeModeSpawner] 프리팹 없음: {partId}");
            return;
        }

        // 3. 스폰 위치 결정
        Vector3 pos = GetSpawnPosition();

        // 4. 스폰
        var go = Instantiate(entry.prefab, pos, Quaternion.identity);
        go.SetActive(true);

        // InteractablePart 상태를 Detached로 (바로 집을 수 있게)
        if (go.TryGetComponent<InteractablePart>(out var part))
            part.ForceDetach();

        Debug.Log($"[FreeModeSpawner] 스폰: {data.displayName} at {pos}");
    }

    private Vector3 GetSpawnPosition()
    {
        if (spawnAnchor != null) return spawnAnchor.position;

        if (_cam != null)
            return _cam.transform.position + _cam.transform.forward * 1.5f
                   + Vector3.down * 0.3f;

        return Vector3.zero;
    }
}
