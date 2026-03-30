using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================
//  FreeModeInventoryUI.cs
//  인벤토리 UI 렌더링
//
//  씬 구조:
//    InventoryPanel (이 컴포넌트 부착)
//      ├─ Header / CloseButton
//      ├─ ScrollView
//      │    └─ Content (itemContainer)
//      └─ TooltipPanel
//           └─ TooltipText (TMP)
//
//  itemSlotPrefab 구조:
//    InventorySlot (Button)
//      ├─ Icon (Image)
//      ├─ NameText (TMP)
//      ├─ CountText (TMP)   — "x2" 등
//      └─ HoverArea (EventTrigger 자동 추가됨)
// ============================================================
public class FreeModeInventoryUI : MonoBehaviour
{
    [Header("UI 참조")]
    public Transform itemContainer;      // ScrollView > Content
    public GameObject itemSlotPrefab;    // 슬롯 프리팹
    public Button closeButton;
    public Sprite defaultIcon;

    [Header("툴팁")]
    public GameObject tooltipPanel;
    public TMP_Text tooltipText;

    [Header("연결")]
    public FreeModeSpawner spawner;

    // 현재 표시 중인 슬롯 캐시 (partId → slot GO)
    private Dictionary<string, GameObject> _slots = new();

    // ── 생명주기 ──────────────────────────────────────────

    private void OnEnable()
    {
        var inv = FreeModeInventory.Instance;
        if (inv == null) return;

        inv.OnItemAdded     += HandleItemAdded;
        inv.OnItemRemoved   += HandleItemRemoved;
        inv.OnInventoryCleared += HandleCleared;

        RefreshAll();
    }

    private void OnDisable()
    {
        var inv = FreeModeInventory.Instance;
        if (inv == null) return;

        inv.OnItemAdded     -= HandleItemAdded;
        inv.OnItemRemoved   -= HandleItemRemoved;
        inv.OnInventoryCleared -= HandleCleared;

        HideTooltip();
    }

    private void Start()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(() => FreeModeManager.Instance?.ToggleInventory());

        HideTooltip();
    }

    // ── 이벤트 핸들러 ────────────────────────────────────

    private void HandleItemAdded(string partId, FreeModeInventory.InventoryEntry entry)
    {
        if (_slots.TryGetValue(partId, out var slot))
        {
            // 카운트만 갱신
            var countText = slot.transform.Find("CountText")?.GetComponent<TMP_Text>();
            if (countText != null) countText.text = entry.count > 1 ? $"x{entry.count}" : "";
        }
        else
        {
            CreateSlot(partId, entry);
        }
    }

    private void HandleItemRemoved(string partId)
    {
        if (!_slots.TryGetValue(partId, out var slot)) return;
        Destroy(slot);
        _slots.Remove(partId);
    }

    private void HandleCleared()
    {
        foreach (var s in _slots.Values) Destroy(s);
        _slots.Clear();
    }

    // ── 슬롯 생성 ─────────────────────────────────────────

    private void CreateSlot(string partId, FreeModeInventory.InventoryEntry entry)
    {
        if (itemSlotPrefab == null || itemContainer == null) return;

        var slot = Instantiate(itemSlotPrefab, itemContainer);
        _slots[partId] = slot;

        // 아이콘
        var icon = slot.transform.Find("Icon")?.GetComponent<Image>();
        if (icon != null)
            icon.sprite = entry.data.icon != null ? entry.data.icon : defaultIcon;

        // 이름
        var nameText = slot.transform.Find("NameText")?.GetComponent<TMP_Text>();
        if (nameText != null) nameText.text = entry.data.displayName;

        // 카운트
        var countText = slot.transform.Find("CountText")?.GetComponent<TMP_Text>();
        if (countText != null) countText.text = entry.count > 1 ? $"x{entry.count}" : "";

        // 클릭 → 스폰
        var btn = slot.GetComponent<Button>();
        if (btn != null)
        {
            string pid = partId; // 클로저 캡처
            btn.onClick.AddListener(() =>
            {
                spawner?.SpawnPart(pid);
            });
        }

        // 호버 → 툴팁
        AddTooltipHover(slot, entry.data.description);
    }

    // ── 툴팁 ──────────────────────────────────────────────

    private void AddTooltipHover(GameObject slot, string desc)
    {
        var trigger = slot.AddComponent<UnityEngine.EventSystems.EventTrigger>();

        var enter = new UnityEngine.EventSystems.EventTrigger.Entry
            { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => ShowTooltip(desc));

        var exit = new UnityEngine.EventSystems.EventTrigger.Entry
            { eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => HideTooltip());

        trigger.triggers.Add(enter);
        trigger.triggers.Add(exit);
    }

    private void ShowTooltip(string desc)
    {
        if (tooltipPanel == null) return;
        tooltipPanel.SetActive(true);
        if (tooltipText != null) tooltipText.text = desc;
    }

    private void HideTooltip()
    {
        if (tooltipPanel != null) tooltipPanel.SetActive(false);
    }

    // ── 전체 갱신 (패널 열릴 때) ─────────────────────────

    private void RefreshAll()
    {
        foreach (var s in _slots.Values) Destroy(s);
        _slots.Clear();

        var inv = FreeModeInventory.Instance;
        if (inv == null) return;

        foreach (var entry in inv.GetAll())
            CreateSlot(entry.data.partId, entry);
    }
}
