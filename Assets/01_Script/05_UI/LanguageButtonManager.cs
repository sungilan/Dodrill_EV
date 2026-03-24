using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Localization.Settings;
using System.Collections.Generic;
using Sirenix.OdinInspector;

public class LanguageButtonManager : MonoBehaviour
{
    [System.Serializable]
    public class LanguageButton
    {
        public Button button;
        public Image backgroundImage;
        public Image selectedIcon;
        public int localeIndex;
    }

    [BoxGroup("설정")]
    [SerializeField] private List<LanguageButton> languageButtons;

    [BoxGroup("설정/알파")]
    [SerializeField] private float normalAlpha = 0f;
    [BoxGroup("설정/알파")]
    [SerializeField] private float hoverAlpha = 0.5f;
    [BoxGroup("설정/알파")]
    [SerializeField] private float selectedAlpha = 1f;

    private LanguageButton _selectedButton = null;
    private bool _isInitialized = false;

    // ─────────────────────────────────────────
    // Debug
    // ─────────────────────────────────────────

    [FoldoutGroup("Debug")]
    [HorizontalGroup("Debug/Buttons")]
    [Button("한국어")]
    private void DebugKorean() => OnButtonSelected(languageButtons[0]);

    [HorizontalGroup("Debug/Buttons")]
    [Button("English")]
    private void DebugEnglish() => OnButtonSelected(languageButtons[1]);

    [HorizontalGroup("Debug/Buttons")]
    [Button("Tiếng Việt")]
    private void DebugViet() => OnButtonSelected(languageButtons[2]);

    // ─────────────────────────────────────────
    // 초기화
    // ─────────────────────────────────────────

    async void Start()
    {
        await LocalizationSettings.InitializationOperation.Task;
        _isInitialized = true;

        foreach (var lb in languageButtons)
        {
            var captured = lb;

            if (captured.backgroundImage == null)
                captured.backgroundImage = captured.button.GetComponent<Image>();

            // 아이콘 초기 비활성화
            if (captured.selectedIcon != null)
                captured.selectedIcon.gameObject.SetActive(false);

            captured.button.onClick.AddListener(() => OnButtonSelected(captured));

            var trigger = captured.button.gameObject.GetComponent<EventTrigger>()
                          ?? captured.button.gameObject.AddComponent<EventTrigger>();

            AddEventTrigger(trigger, EventTriggerType.PointerEnter, (_) => OnHoverEnter(captured));
            AddEventTrigger(trigger, EventTriggerType.PointerExit, (_) => OnHoverExit(captured));

            SetAlpha(captured.backgroundImage, normalAlpha);
        }

        // 현재 언어에 맞는 버튼 초기 선택
        var currentIndex = LocalizationSettings.AvailableLocales.Locales
            .IndexOf(LocalizationSettings.SelectedLocale);

        var defaultButton = languageButtons.Find(lb => lb.localeIndex == currentIndex);
        if (defaultButton != null)
        {
            _selectedButton = defaultButton;
            SetAlpha(defaultButton.backgroundImage, selectedAlpha);
            SetIcon(defaultButton, true);
        }
    }

    // ─────────────────────────────────────────
    // 버튼 이벤트
    // ─────────────────────────────────────────

    private void OnButtonSelected(LanguageButton target)
    {
        if (!_isInitialized) return;

        if (_selectedButton != null && _selectedButton != target)
        {
            SetAlpha(_selectedButton.backgroundImage, normalAlpha);
            SetIcon(_selectedButton, false); // 이전 선택 아이콘 끄기
        }

        _selectedButton = target;
        SetAlpha(_selectedButton.backgroundImage, selectedAlpha);
        SetIcon(_selectedButton, true); // 현재 선택 아이콘 켜기

        var locales = LocalizationSettings.AvailableLocales.Locales;
        if (target.localeIndex < locales.Count)
        {
            LocalizationSettings.SelectedLocale = locales[target.localeIndex];
            Debug.Log($"언어 변경: {locales[target.localeIndex].Identifier.Code}");
        }
    }

    private void OnHoverEnter(LanguageButton target)
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        if (target != _selectedButton)
            SetAlpha(target.backgroundImage, hoverAlpha);
#endif
    }

    private void OnHoverExit(LanguageButton target)
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        if (target != _selectedButton)
            SetAlpha(target.backgroundImage, normalAlpha);
#endif
    }

    // ─────────────────────────────────────────
    // 유틸
    // ─────────────────────────────────────────

    private void SetIcon(LanguageButton target, bool active)
    {
        if (target.selectedIcon != null)
            target.selectedIcon.gameObject.SetActive(active);
    }

    private void SetAlpha(Image image, float alpha)
    {
        if (image == null) return;
        Color c = image.color;
        c.a = alpha;
        image.color = c;
    }

    private void AddEventTrigger(EventTrigger trigger, EventTriggerType type,
        System.Action<BaseEventData> action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener((data) => action(data));
        trigger.triggers.Add(entry);
    }
}