using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  FreeModeInventory.cs
//  자유탈거 모드 인벤토리 — 싱글톤
//  탈거된 부품 데이터를 보관하고 UI에 이벤트로 알림
// ============================================================
public class FreeModeInventory : MonoBehaviour
{
    public static FreeModeInventory Instance { get; private set; }

    // 인벤토리 항목
    public class InventoryEntry
    {
        public PartDataSO data;
        public int count;
    }

    private readonly Dictionary<string, InventoryEntry> _items = new();

    // UI가 구독할 이벤트
    public System.Action<string, InventoryEntry> OnItemAdded;
    public System.Action<string> OnItemRemoved;
    public System.Action OnInventoryCleared;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── 부품 추가 (탈거 시 호출) ───────────────────────────

    public void AddPart(PartDataSO data)
    {
        if (data == null) return;

        if (_items.TryGetValue(data.partId, out var entry))
        {
            entry.count++;
        }
        else
        {
            entry = new InventoryEntry { data = data, count = 1 };
            _items[data.partId] = entry;
        }

        Debug.Log($"[Inventory] 추가: {data.displayName} (x{entry.count})");
        OnItemAdded?.Invoke(data.partId, entry);
    }

    // ── 부품 제거 (스폰 시 호출) ───────────────────────────

    /// <returns>스폰할 PartDataSO, 없으면 null</returns>
    public PartDataSO RemovePart(string partId)
    {
        if (!_items.TryGetValue(partId, out var entry)) return null;

        entry.count--;
        PartDataSO data = entry.data;

        if (entry.count <= 0)
        {
            _items.Remove(partId);
            OnItemRemoved?.Invoke(partId);
        }
        else
        {
            OnItemAdded?.Invoke(partId, entry); // count 갱신용 재발행
        }

        Debug.Log($"[Inventory] 제거: {data.displayName} (남은: {entry.count})");
        return data;
    }

    public bool HasPart(string partId) => _items.ContainsKey(partId);

    public IEnumerable<InventoryEntry> GetAll() => _items.Values;

    public void Clear()
    {
        _items.Clear();
        OnInventoryCleared?.Invoke();
    }
}
