//using UnityEngine;
//using UnityEngine.UI;
//using UnityEngine.EventSystems;
//using UnityEngine.Localization.Settings;
//using System.Collections.Generic;
//using Sirenix.OdinInspector;

//public class LanguageButtonManager : MonoBehaviour
//{
//    [System.Serializable]
//    public class LanguageButton
//    {
//        public Button button;
//        public Image backgroundImage;
//        public Image selectedIcon;
//        public int localeIndex;
//    }

//    [BoxGroup("설정")]
//    [SerializeField] private List<LanguageButton> languageButtons;

//    [BoxGroup("설정/알파")]
//    [SerializeField] private float normalAlpha = 0f;
//    [BoxGroup("설정/알파")]
//    [SerializeField] private float hoverAlpha = 0.5f;
//    [BoxGroup("설정/알파")]
//    [SerializeField] private float selectedAlpha = 1f;

//    private LanguageButton _selectedButton = null;
//    private bool _isInitialized = false;

//    // ─────────────────────────────────────────
//    // Debug
//    // ─────────────────────────────────────────

//    [FoldoutGroup("Debug")]
//    [HorizontalGroup("Debug/Buttons")]
//    [Button("한국어")]
//    private void DebugKorean() => OnButtonSelected(languageButtons[0]);

//    [HorizontalGroup("Debug/Buttons")]
//    [Button("English")]
//    private void DebugEnglish() => OnButtonSelected(languageButtons[1]);

//    [HorizontalGroup("Debug/Buttons")]
//    [Button("Tiếng Việt")]
//    private void DebugViet() => OnButtonSelected(languageButtons[2]);

//    // ─────────────────────────────────────────
//    // 초기화
//    // ─────────────────────────────────────────

//    async void Start()
//    {
//        await LocalizationSettings.InitializationOperation.Task;
//        _isInitialized = true;

//        foreach (var lb in languageButtons)
//        {
//            var captured = lb;

//            if (captured.backgroundImage == null)
//                captured.backgroundImage = captured.button.GetComponent<Image>();

//            // 아이콘 초기 비활성화
//            if (captured.selectedIcon != null)
//                captured.selectedIcon.gameObject.SetActive(false);

//            captured.button.onClick.AddListener(() => OnButtonSelected(captured));

//            var trigger = captured.button.gameObject.GetComponent<EventTrigger>()
//                          ?? captured.button.gameObject.AddComponent<EventTrigger>();

//            AddEventTrigger(trigger, EventTriggerType.PointerEnter, (_) => OnHoverEnter(captured));
//            AddEventTrigger(trigger, EventTriggerType.PointerExit, (_) => OnHoverExit(captured));

//            SetAlpha(captured.backgroundImage, normalAlpha);
//        }

//        // 현재 언어에 맞는 버튼 초기 선택
//        var currentIndex = LocalizationSettings.AvailableLocales.Locales
//            .IndexOf(LocalizationSettings.SelectedLocale);

//        var defaultButton = languageButtons.Find(lb => lb.localeIndex == currentIndex);
//        if (defaultButton != null)
//        {
//            _selectedButton = defaultButton;
//            SetAlpha(defaultButton.backgroundImage, selectedAlpha);
//            SetIcon(defaultButton, true);
//        }
//    }

//    // ─────────────────────────────────────────
//    // 버튼 이벤트
//    // ─────────────────────────────────────────

//    private void OnButtonSelected(LanguageButton target)
//    {
//        if (!_isInitialized) return;

//        if (_selectedButton != null && _selectedButton != target)
//        {
//            SetAlpha(_selectedButton.backgroundImage, normalAlpha);
//            SetIcon(_selectedButton, false); // 이전 선택 아이콘 끄기
//        }

//        _selectedButton = target;
//        SetAlpha(_selectedButton.backgroundImage, selectedAlpha);
//        SetIcon(_selectedButton, true); // 현재 선택 아이콘 켜기

//        var locales = LocalizationSettings.AvailableLocales.Locales;
//        if (target.localeIndex < locales.Count)
//        {
//            LocalizationSettings.SelectedLocale = locales[target.localeIndex];
//            Debug.Log($"언어 변경: {locales[target.localeIndex].Identifier.Code}");
//        }
//    }

//    private void OnHoverEnter(LanguageButton target)
//    {
//#if UNITY_EDITOR || UNITY_STANDALONE
//        if (target != _selectedButton)
//            SetAlpha(target.backgroundImage, hoverAlpha);
//#endif
//    }

//    private void OnHoverExit(LanguageButton target)
//    {
//#if UNITY_EDITOR || UNITY_STANDALONE
//        if (target != _selectedButton)
//            SetAlpha(target.backgroundImage, normalAlpha);
//#endif
//    }

//    // ─────────────────────────────────────────
//    // 유틸
//    // ─────────────────────────────────────────

//    private void SetIcon(LanguageButton target, bool active)
//    {
//        if (target.selectedIcon != null)
//            target.selectedIcon.gameObject.SetActive(active);
//    }

//    private void SetAlpha(Image image, float alpha)
//    {
//        if (image == null) return;
//        Color c = image.color;
//        c.a = alpha;
//        image.color = c;
//    }

//    private void AddEventTrigger(EventTrigger trigger, EventTriggerType type,
//        System.Action<BaseEventData> action)
//    {
//        var entry = new EventTrigger.Entry { eventID = type };
//        entry.callback.AddListener((data) => action(data));
//        trigger.triggers.Add(entry);
//    }
//}

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization.Settings;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;

public class LanguageSelector : MonoBehaviour
{
    [System.Serializable]
    public class LocaleData
    {
        public string languageName; // 표시용 이름 (선택 사항)
        public Sprite flagSprite;   // 국기 이미지
        public int localeIndex;     // Localization Settings의 인덱스
    }

    [BoxGroup("UI 참조")]
    [SerializeField] private Image displayFlagImage;    // 현재 선택된 국기 표시용 Image
    [SerializeField] private Button btnLeft;            // 왼쪽 화살표 버튼
    [SerializeField] private Button btnRight;           // 오른쪽 화살표 버튼
    [SerializeField] private Button btnConfirm;         // 하단 '확인' 버튼

    [BoxGroup("데이터 설정")]
    [SerializeField] private List<LocaleData> locales;

    [ReadOnly]
    [SerializeField] private int _currentIndex = 0;
    private bool _isInitialized = false;

    async void Start()
    {
        // 1. 로컬라이제이션 초기화 대기
        await LocalizationSettings.InitializationOperation.Task;
        _isInitialized = true;

        // 2. 현재 설정된 언어의 인덱스 찾기
        var currentLocale = LocalizationSettings.SelectedLocale;
        _currentIndex = locales.FindIndex(l => l.localeIndex == LocalizationSettings.AvailableLocales.Locales.IndexOf(currentLocale));

        if(_currentIndex == -1) _currentIndex = 0;

        // 3. 버튼 리스너 등록
        btnLeft.onClick.AddListener(() => ChangeIndex(-1));
        btnRight.onClick.AddListener(() => ChangeIndex(1));
        btnConfirm.onClick.AddListener(OnConfirm);

        // 4. 초기 UI 업데이트
        UpdateUI();
    }

    private void ChangeIndex(int direction)
    {
        if(!_isInitialized) return;

        // 인덱스 순환 (처음에서 왼쪽 누르면 마지막으로, 마지막에서 오른쪽 누르면 처음으로)
        _currentIndex += direction;
        if(_currentIndex < 0) _currentIndex = locales.Count - 1;
        else if(_currentIndex >= locales.Count) _currentIndex = 0;

        UpdateUI();

        // [선택 사항] 화살표를 누를 때마다 바로 언어를 바꾸고 싶다면 아래 주석 해제
        // SetLocalization(_currentIndex);
    }

    private void UpdateUI()
    {
        if(locales.Count == 0) return;

        // 국기 이미지 교체
        if(displayFlagImage != null && locales[_currentIndex].flagSprite != null)
        {
            displayFlagImage.sprite = locales[_currentIndex].flagSprite;
        }

        Debug.Log($"현재 미리보기 언어: {locales[_currentIndex].languageName}");
    }

    private void OnConfirm()
    {
        if(!_isInitialized) return;

        // 확인 버튼을 눌렀을 때 실제 언어 변경 적용
        SetLocalization(_currentIndex);

        Debug.Log("언어 설정이 적용되었습니다.");
        // 여기에 설정창 닫기 등 추가 로직 작성
    }

    private void SetLocalization(int index)
    {
        var availableLocales = LocalizationSettings.AvailableLocales.Locales;
        int targetLocaleIndex = locales[index].localeIndex;

        if(targetLocaleIndex < availableLocales.Count)
        {
            LocalizationSettings.SelectedLocale = availableLocales[targetLocaleIndex];
            Debug.Log($"실제 언어 변경 완료: {availableLocales[targetLocaleIndex].Identifier.Code}");
        }
    }

    #region Debug (Odin)
    [Button("다음 언어로")]
    private void TestNext() => ChangeIndex(1);
    #endregion
}