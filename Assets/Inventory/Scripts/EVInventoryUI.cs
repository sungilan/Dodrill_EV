using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// ============================================================
//  EVInventoryUI.cs
//  자유탈거 모드 인벤토리 UI 총괄
//
//  원본: InventoryManager (MikeNspired XRIStarterKit)
//  수정: XR Controller → PC 마우스 / VR AutoHand
//        InputActionReference → 기존 FreeLookController I키 연동
//
//  씬 구조:
//    EVInventoryUI  ← 이 스크립트
//      ├─ Panel (Canvas/RectTransform)
//      │   └─ SlotContainer (GridLayoutGroup)
//      └─ TooltipPanel (공유 툴팁)
//           └─ TooltipText (TMP)
//
//  사용 흐름:
//    1. FreeModeBootstrapper.Activate() → EVInventoryUI 등록
//    2. 부품 탈거 → FreeLookController.DropToInventory() → AddPart()
//    3. I키 / UI 버튼 → TogglePanel()
//    4. 슬롯 클릭 → EVInventorySlot.RetrievePart()
// ============================================================
public class EVInventoryUI : MonoBehaviour
{
    public static EVInventoryUI Instance { get; private set; }

    [Header("UI")]
    public GameObject   panel;              // 인벤토리 패널 루트
    public Transform    slotContainer;      // 슬롯이 추가될 부모 (GridLayout 권장)
    public GameObject   slotPrefab;         // EVInventorySlot 프리팹
    public Sprite       defaultPartIcon;

    [Header("툴팁")]
    public GameObject   tooltipPanel;
    public TMPro.TMP_Text tooltipText;

    [Header("단축키")]
    public KeyCode toggleKey = KeyCode.I;

    // partId → 슬롯 매핑 (같은 partId가 여러 개면 count 표시)
    private readonly Dictionary<string, EVInventorySlot> _slots = new();

    // ── 생명주기 ───────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        panel?.SetActive(false);
        tooltipPanel?.SetActive(false);
    }

    private void Update()
    {
        //if (FreeModeManager.IsActive && Input.GetKeyDown(toggleKey))
            TogglePanel();
    }

    // ── 외부 API ───────────────────────────────────────────

    /// <summary>부품 탈거 시 호출 — 슬롯 추가 또는 카운트 증가</summary>
    public void AddPart(InteractablePart part, PartDataSO data = null)
    {
        if (part == null && data == null) return;

        string partId = data?.partId ?? part?.name ?? "unknown";

        if (_slots.TryGetValue(partId, out var existingSlot))
        {
            // 같은 부품 중복 추가 — 같은 슬롯에 덮어쓰기 (현재는 단순 교체)
            // 필요 시 카운트 표시로 확장 가능
            existingSlot.StorePart(part, data);
        }
        else
        {
            var slot = CreateSlot(partId);
            slot.StorePart(part, data);
        }
    }

    /// <summary>I키 또는 버튼으로 패널 토글</summary>
    public void TogglePanel()
    {
        if (panel == null) return;
        panel.SetActive(!panel.activeSelf);
    }

    public void ShowPanel()  => panel?.SetActive(true);
    public void HidePanel()  => panel?.SetActive(false);
    public void ClearAll()
    {
        foreach (var s in _slots.Values) Destroy(s.gameObject);
        _slots.Clear();
    }

    // ── 슬롯 생성 ─────────────────────────────────────────

    private EVInventorySlot CreateSlot(string partId)
    {
        if (slotPrefab == null || slotContainer == null)
        {
            Debug.LogError("[EVInventoryUI] slotPrefab 또는 slotContainer 미연결");
            return null;
        }

        var go   = Instantiate(slotPrefab, slotContainer);
        var slot = go.GetComponent<EVInventorySlot>();
        _slots[partId] = slot;

        // 클릭 이벤트
        var btn = go.GetComponent<Button>();
        if (btn != null)
        {
            string pid = partId;
            btn.onClick.AddListener(() =>
            {
                slot.RetrievePart();
                // 비어진 슬롯 제거
                if (!slot.HasItem)
                {
                    _slots.Remove(pid);
                    Destroy(go);
                }
                // 공유 툴팁 숨기기
                tooltipPanel?.SetActive(false);
            });
        }

        // 호버 이벤트 (EventTrigger 사용)
        AddHoverEvents(go, slot);

        return slot;
    }

    private void AddHoverEvents(GameObject go, EVInventorySlot slot)
    {
        var trigger = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();

        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ =>
        {
            slot.OnHoverEnter();
            ShowSharedTooltip(slot);
        });

        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ =>
        {
            slot.OnHoverExit();
            tooltipPanel?.SetActive(false);
        });

        trigger.triggers.Add(enter);
        trigger.triggers.Add(exit);
    }

    private void ShowSharedTooltip(EVInventorySlot slot)
    {
        if (tooltipPanel == null || slot.StoredData == null) return;
        tooltipPanel.SetActive(true);
        if (tooltipText != null)
            tooltipText.text = $"<b>{slot.StoredData.displayName}</b>\n{slot.StoredData.description}";
    }
}
