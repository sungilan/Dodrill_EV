using DG.Tweening;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;
using XRAirpotrSecurity; // PlatformType 참조를 위해 추가

// ============================================================
//  EVInventoryUI.cs
//  자유탈거 모드 인벤토리 UI 총괄 (VR 플랫폼일 경우에만 연출 생략)
// ============================================================
public class EVInventoryUI : MonoBehaviour
{
    public static EVInventoryUI Instance { get; private set; }

    [Header("UI References")]
    public GameObject panel;               // 인벤토리 패널 루트
    public Transform slotContainer;       // 슬롯이 추가될 부모
    public GameObject slotPrefab;          // EVInventorySlot 프리팹
    public Sprite defaultPartIcon;

    [Header("Animation Settings")]
    public float animationDuration = 0.3f;
    private CanvasGroup canvasGroup;
    private Sequence openCloseSequence;

    [Header("Tooltip")]
    public GameObject tooltipPanel;
    public TMPro.TMP_Text tooltipText;

    [Header("Inventory Count Badge")]
    public TMPro.TMP_Text totalCountText;
    public CanvasGroup countBadgeGroup;
    public float fadeDuration = 0.5f;
    private Tween countFadeTween;

    [Header("Hotkeys")]
    public KeyCode toggleKey = KeyCode.I;

    private readonly Dictionary<string, EVInventorySlot> _slots = new();

    private void Awake()
    {
        if(Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        canvasGroup = panel.GetComponent<CanvasGroup>() ?? panel.AddComponent<CanvasGroup>();

        // 초기 상태 설정
        panel.SetActive(false);
        canvasGroup.alpha = 0;
        panel.transform.localScale = Vector3.one * 0.8f;

        tooltipPanel?.SetActive(false);

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

    public void AddPart(InteractablePart part, PartDataSO data = null)
    {
        if(part == null && data == null) return;

        string partId = data?.partId ?? part?.name ?? "unknown";

        if(_slots.TryGetValue(partId, out var existingSlot))
        {
            existingSlot.StorePart(part, data);
        }
        else
        {
            var slot = CreateSlot(partId);
            slot.StorePart(part, data);
        }

        UpdateTotalSlotCountUI();
    }

    /// <summary>플랫폼에 따라 연출 여부를 결정하여 패널 토글</summary>
    public void TogglePanel()
    {
        if(panel == null) return;

        bool isOpening = !panel.activeSelf;

        // VR 플랫폼 여부 확인
        bool isVR = UserInfo.PlatformType == PlatformType.VR;

        if(isVR)
        {
            // VR일 때는 연출 없이 즉시 처리 (슬라이딩 패널 충돌 방지)
            panel.SetActive(isOpening);
            canvasGroup.alpha = isOpening ? 1f : 0f;
            panel.transform.localScale = Vector3.one;
        }
        else
        {
            // PC/Mobile 등 일반 플랫폼일 때는 기존 연출 실행
            PlayDefaultAnimation(isOpening);
        }

        var adapter = GetComponent<InventoryCanvasAdapter>();
        adapter?.OnPanelOpened(isOpening);
    }

    /// <summary>일반 플랫폼용 DOTween 애니메이션</summary>
    private void PlayDefaultAnimation(bool isOpening)
    {
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
    }

    // ── 슬롯 및 UI 갱신 로직 (동일) ──────────────────────────

    private EVInventorySlot CreateSlot(string partId)
    {
        if(slotPrefab == null || slotContainer == null) return null;

        var go = Instantiate(slotPrefab, slotContainer);
        go.SetActive(true);
        var slot = go.GetComponent<EVInventorySlot>();
        _slots[partId] = slot;

        var btn = go.GetComponent<Button>();
        if(btn != null)
        {
            string pid = partId;
            btn.onClick.AddListener(() =>
            {
                slot.RetrievePart();
                if(!slot.HasItem)
                {
                    _slots.Remove(pid);
                    Destroy(go);
                    UpdateTotalSlotCountUI();
                }
                tooltipPanel?.SetActive(false);
            });
        }

        AddHoverEvents(go, slot);
        return slot;
    }

    public void UpdateTotalSlotCountUI()
    {
        if(totalCountText == null || countBadgeGroup == null) return;

        int currentSlotCount = _slots.Count;
        totalCountText.text = $"{currentSlotCount}";

        countFadeTween?.Kill();

        if(currentSlotCount > 0)
        {
            if(!countBadgeGroup.gameObject.activeSelf) countBadgeGroup.gameObject.SetActive(true);
            countFadeTween = countBadgeGroup.DOFade(1f, fadeDuration).SetUpdate(true);
        }
        else
        {
            countFadeTween = countBadgeGroup.DOFade(0f, fadeDuration)
                .SetUpdate(true)
                .OnComplete(() => countBadgeGroup.gameObject.SetActive(false));
        }
    }

    // ── 툴팁 및 이벤트 (동일) ───────────────────────────────────

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