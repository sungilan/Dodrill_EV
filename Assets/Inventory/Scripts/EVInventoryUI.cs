using DG.Tweening;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

// ============================================================
//  EVInventoryUI.cs
//  자유탈거 모드 인벤토리 UI 총괄 (카운트 배지 페이드 연출 포함)
// ============================================================
public class EVInventoryUI : MonoBehaviour
{
    public static EVInventoryUI Instance { get; private set; }

    [Header("UI References")]
    public GameObject panel;               // 인벤토리 패널 루트
    public Transform slotContainer;       // 슬롯이 추가될 부모 (GridLayout 권장)
    public GameObject slotPrefab;          // EVInventorySlot 프리팹
    public Sprite defaultPartIcon;

    [Header("Animation Settings")]
    public float animationDuration = 0.3f; // 패널 열기/닫기 속도
    private CanvasGroup canvasGroup;       // 패널 투명도 조절용
    private Sequence openCloseSequence;    // 트윈 중복 실행 방지용

    [Header("Tooltip")]
    public GameObject tooltipPanel;
    public TMPro.TMP_Text tooltipText;

    [Header("Inventory Count Badge (New)")]
    public TMPro.TMP_Text totalCountText;  // 숫자 텍스트 (예: "5")
    public CanvasGroup countBadgeGroup;    // 카운트 UI 전체를 감싸는 부모 (이미지 포함)
    public float fadeDuration = 0.5f;      // 나타나고 사라지는 페이드 시간
    private Tween countFadeTween;          // 카운트 페이드 전용 트윈

    [Header("Hotkeys")]
    public KeyCode toggleKey = KeyCode.I;

    // partId → 슬롯 매핑
    private readonly Dictionary<string, EVInventorySlot> _slots = new();

    // ── 생명주기 ───────────────────────────────────────────

    private void Awake()
    {
        if(Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // 메인 패널 CanvasGroup 설정
        canvasGroup = panel.GetComponent<CanvasGroup>() ?? panel.AddComponent<CanvasGroup>();

        // 초기 상태 설정 (꺼져있음)
        panel.SetActive(false);
        canvasGroup.alpha = 0;
        panel.transform.localScale = Vector3.one * 0.8f;

        tooltipPanel?.SetActive(false);

        // 카운트 배지 초기화 (0개이므로 투명하게 시작)
        if(countBadgeGroup != null)
        {
            countBadgeGroup.alpha = 0f;
            countBadgeGroup.gameObject.SetActive(false);
        }
    }

    private void Start()
    {
        UpdateTotalSlotCountUI();
    }

    private void Update()
    {
        if(Input.GetKeyDown(toggleKey))
            TogglePanel();
    }

    // ── 외부 API ───────────────────────────────────────────

    /// <summary>부품 탈거 시 호출 — 슬롯 추가 또는 기존 슬롯에 저장</summary>
    public void AddPart(InteractablePart part, PartDataSO data = null)
    {
        if(part == null && data == null) return;

        string partId = data?.partId ?? part?.name ?? "unknown";

        if(_slots.TryGetValue(partId, out var existingSlot))
        {
            // 이미 있는 부품이면 해당 슬롯의 개수만 증가
            existingSlot.StorePart(part, data);
        }
        else
        {
            // 새로운 부품이면 슬롯 생성
            var slot = CreateSlot(partId);
            slot.StorePart(part, data);
        }

        UpdateTotalSlotCountUI();
    }

    /// <summary>I키 또는 버튼으로 패널 토글</summary>
    public void TogglePanel()
    {
        if(panel == null) return;

        bool isOpening = !panel.activeSelf;

        openCloseSequence?.Kill();
        openCloseSequence = DOTween.Sequence().SetUpdate(true);

        if(isOpening)
        {
            panel.SetActive(true);
            openCloseSequence.Append(canvasGroup.DOFade(1, animationDuration))
                             .Join(panel.transform.DOScale(1f, animationDuration).SetEase(Ease.OutBack));
        }
        else
        {
            openCloseSequence.Append(canvasGroup.DOFade(0, animationDuration))
                             .Join(panel.transform.DOScale(0.8f, animationDuration).SetEase(Ease.InBack))
                             .OnComplete(() => panel.SetActive(false));
        }

        var adapter = GetComponent<InventoryCanvasAdapter>();
        adapter?.OnPanelOpened(isOpening);
    }

    // ── 슬롯 생성 및 카운트 갱신 ────────────────────────────────

    private EVInventorySlot CreateSlot(string partId)
    {
        if(slotPrefab == null || slotContainer == null) return null;

        var go = Instantiate(slotPrefab, slotContainer);
        go.SetActive(true);
        var slot = go.GetComponent<EVInventorySlot>();
        _slots[partId] = slot;

        // 클릭 이벤트: 아이템 꺼내기
        var btn = go.GetComponent<Button>();
        if(btn != null)
        {
            string pid = partId;
            btn.onClick.AddListener(() =>
            {
                slot.RetrievePart();
                // 슬롯이 완전히 비었으면 제거
                if(!slot.HasItem)
                {
                    _slots.Remove(pid);
                    Destroy(go);
                    // 삭제 직후 UI 상태 갱신
                    UpdateTotalSlotCountUI();
                }
                tooltipPanel?.SetActive(false);
            });
        }

        AddHoverEvents(go, slot);
        return slot;
    }

    /// <summary>총 슬롯 개수에 따른 UI 배지 애니메이션 처리</summary>
    public void UpdateTotalSlotCountUI()
    {
        if(totalCountText == null || countBadgeGroup == null) return;

        int currentSlotCount = _slots.Count;
        totalCountText.text = $"{currentSlotCount}";

        // 진행 중인 페이드 트윈 중단
        countFadeTween?.Kill();

        if(currentSlotCount > 0)
        {
            // 아이템이 생기면: 스르륵 나타나기
            if(!countBadgeGroup.gameObject.activeSelf) countBadgeGroup.gameObject.SetActive(true);
            countFadeTween = countBadgeGroup.DOFade(1f, fadeDuration).SetUpdate(true);
        }
        else
        {
            // 아이템이 하나도 없으면: 스르륵 사라지기
            countFadeTween = countBadgeGroup.DOFade(0f, fadeDuration)
                .SetUpdate(true)
                .OnComplete(() => countBadgeGroup.gameObject.SetActive(false));
        }
    }

    // ── 툴팁 및 이벤트 ───────────────────────────────────────

    private void AddHoverEvents(GameObject go, EVInventorySlot slot)
    {
        var trigger = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();

        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => ShowSharedTooltip(slot));

        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => tooltipPanel?.SetActive(false));

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
            string locName = LocalizationSettings.StringDatabase.GetLocalizedString("UI_Text", partId);
            string locDesc = LocalizationSettings.StringDatabase.GetLocalizedString("UI_Text", partId + "_desc");

            if(string.IsNullOrEmpty(locName)) locName = slot.StoredData.displayName;
            if(string.IsNullOrEmpty(locDesc)) locDesc = slot.StoredData.description;

            tooltipText.text = $"<b>{locName}</b>\n{locDesc}";
        }
    }
}