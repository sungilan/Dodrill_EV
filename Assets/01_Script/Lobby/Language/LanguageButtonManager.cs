using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Localization.Settings;
using Sirenix.OdinInspector;

// ============================================================
//  LanguageButtonManager.cs
//  ← → 버튼으로 국기 이미지를 넘기며 언어 선택
//
//  Inspector 연결:
//    FlagImage      — 중앙 국기 Image 컴포넌트
//    LeftButton     — 왼쪽 < 버튼  → OnPrev() 연결
//    RightButton    — 오른쪽 > 버튼 → OnNext() 연결
//    Languages      — 언어별 정보 리스트 (순서대로)
// ============================================================

public class LanguageButtonManager : MonoBehaviour
{
    [System.Serializable]
    public class LanguageEntry
    {
        public string  displayName;   // 디버그/표시용
        public Sprite  flagSprite;    // 국기 이미지
        public int     localeIndex;   // LocalizationSettings 인덱스
    }

    [Header("UI 연결")]
    [SerializeField] private Image  _flagImage;
    [SerializeField] private Button _leftButton;
    [SerializeField] private Button _rightButton;

    [Header("언어 목록")]
    [SerializeField] private List<LanguageEntry> _languages = new();

    private int  _currentIndex   = 0;
    private bool _isInitialized  = false;

    // ─────────────────────────────────────────
    // 초기화
    // ─────────────────────────────────────────

    async void Start()
    {
        await LocalizationSettings.InitializationOperation.Task;
        _isInitialized = true;

        // 현재 설정된 언어로 초기 인덱스 맞추기
        var currentLocaleIndex = LocalizationSettings.AvailableLocales.Locales
            .IndexOf(LocalizationSettings.SelectedLocale);

        int found = _languages.FindIndex(l => l.localeIndex == currentLocaleIndex);
        _currentIndex = found >= 0 ? found : 0;

        _leftButton?.onClick.AddListener(OnPrev);
        _rightButton?.onClick.AddListener(OnNext);

        Refresh();
    }

    // ─────────────────────────────────────────
    // ← → 버튼 (Inspector OnClick으로도 연결 가능)
    // ─────────────────────────────────────────

    public void OnPrev()
    {
        if (_languages.Count == 0) return;
        _currentIndex = (_currentIndex - 1 + _languages.Count) % _languages.Count;
        Refresh();
    }

    public void OnNext()
    {
        if (_languages.Count == 0) return;
        _currentIndex = (_currentIndex + 1) % _languages.Count;
        Refresh();
    }

    // ─────────────────────────────────────────
    // 화면 갱신 + 언어 적용
    // ─────────────────────────────────────────

    private void Refresh()
    {
        if (_languages.Count == 0) return;

        var entry = _languages[_currentIndex];

        // 국기 이미지 교체
        if (_flagImage != null)
            _flagImage.sprite = entry.flagSprite;

        // 언어 변경
        if (_isInitialized)
        {
            var locales = LocalizationSettings.AvailableLocales.Locales;
            if (entry.localeIndex < locales.Count)
            {
                LocalizationSettings.SelectedLocale = locales[entry.localeIndex];
                Debug.Log($"[Language] 변경: {entry.displayName}");
            }
        }
    }

    // ─────────────────────────────────────────
    // Debug
    // ─────────────────────────────────────────

#if UNITY_EDITOR
    [FoldoutGroup("Debug")]
    [Button("◀ Prev"), HorizontalGroup("Debug/Buttons")]
    private void DebugPrev() => OnPrev();

    [Button("Next ▶"), HorizontalGroup("Debug/Buttons")]
    private void DebugNext() => OnNext();
#endif
}
