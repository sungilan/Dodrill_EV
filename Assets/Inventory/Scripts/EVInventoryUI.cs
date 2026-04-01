using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

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
    public GameObject panel;              // 인벤토리 패널 루트
    public Transform slotContainer;      // 슬롯이 추가될 부모 (GridLayout 권장)
    public GameObject slotPrefab;         // EVInventorySlot 프리팹
    public Sprite defaultPartIcon;

    [Header("툴팁")]
    public GameObject tooltipPanel;
    public TMPro.TMP_Text tooltipText;

    [Header("단축키")]
    public KeyCode toggleKey = KeyCode.I;

    // partId → 슬롯 매핑 (같은 partId가 여러 개면 count 표시)
    private readonly Dictionary<string, EVInventorySlot> _slots = new();

    // ── 생명주기 ───────────────────────────────────────────

    private void Awake()
    {
        if(Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        panel?.SetActive(false);
        tooltipPanel?.SetActive(false);
    }

    private void Update()
    {
        if(Input.GetKeyDown(toggleKey))
            TogglePanel();
    }

    // ── 외부 API ───────────────────────────────────────────

    /// <summary>부품 탈거 시 호출 — 슬롯 추가 또는 카운트 증가</summary>
    public void AddPart(InteractablePart part, PartDataSO data = null)
    {
        if(part == null && data == null) return;

        string partId = data?.partId ?? part?.name ?? "unknown";

        if(_slots.TryGetValue(partId, out var existingSlot))
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
        if(panel == null) return;

        // 1. 현재 상태의 반대값을 변수에 저장 (현재 꺼져있으면 true, 켜져있으면 false)
        bool nextState = !panel.activeSelf;

        // 2. 패널 활성화 상태 변경
        panel.SetActive(nextState);

        var adapter = GetComponent<InventoryCanvasAdapter>();
        if(adapter != null)
        {
            adapter.OnPanelOpened(nextState);
        }
    }

    public void ShowPanel() => panel?.SetActive(true);
    public void HidePanel() => panel?.SetActive(false);
    public void ClearAll()
    {
        foreach(var s in _slots.Values) Destroy(s.gameObject);
        _slots.Clear();
    }

    // ── 슬롯 생성 ─────────────────────────────────────────

    private EVInventorySlot CreateSlot(string partId)
    {
        if(slotPrefab == null || slotContainer == null)
        {
            Debug.LogError("[EVInventoryUI] slotPrefab 또는 slotContainer 미연결");
            return null;
        }

        var go = Instantiate(slotPrefab, slotContainer);
        go.SetActive(true);   // 패널 비활성 상태여도 슬롯 자체는 활성 보장
        var slot = go.GetComponent<EVInventorySlot>();
        _slots[partId] = slot;

        // 클릭 이벤트
        var btn = go.GetComponent<Button>();
        if(btn != null)
        {
            string pid = partId;
            btn.onClick.AddListener(() =>
            {
                slot.RetrievePart();
                // 비어진 슬롯 제거
                if(!slot.HasItem)
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
            ShowSharedTooltip(slot);
        });

        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ =>
        {
            tooltipPanel?.SetActive(false);
        });

        trigger.triggers.Add(enter);
        trigger.triggers.Add(exit);
    }

    private void ShowSharedTooltip(EVInventorySlot slot)
    {
        if(tooltipPanel == null || slot.StoredData == null) return;
        tooltipPanel.SetActive(true);

        if(tooltipText != null)
        {
            string partId = slot.StoredData.partId;

            // 1. String Table에서 번역된 텍스트를 코드로 직접 당겨옵니다.
            string locName = LocalizationSettings.StringDatabase.GetLocalizedString("UI_Text", partId);
            string locDesc = LocalizationSettings.StringDatabase.GetLocalizedString("UI_Text", partId + "_desc");

            // 2. 만약 실수로 번역 데이터를 안 만들었다면? -> PartDataSO에 적힌 원래 텍스트를 띄움 (안전장치)
            if(string.IsNullOrEmpty(locName)) locName = slot.StoredData.displayName;
            if(string.IsNullOrEmpty(locDesc)) locDesc = slot.StoredData.description;

            // 3. 굵은 글씨의 이름과 일반 글씨의 설명을 합쳐서 툴팁에 띄웁니다.
            tooltipText.text = $"<b>{locName}</b>\n{locDesc}";
        }
    }
}