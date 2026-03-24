using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using Debug = UnityEngine.Debug;

// ============================================================
//  LocalizationManager.cs
//  유니티 Localization 패키지 기반 UI 공통 텍스트 관리
//
//  역할 분담:
//    ① UI 공통 텍스트 (완료/실패/진행중 등)
//       → 유니티 Localization String Table "UI" 테이블에서 관리
//       → LocalizationManager.Get(LocaleKeys.CommonComplete)
//
//    ② 시나리오별 고유 텍스트 (description, stepN, dialogue 등)
//       → StringKVPair.GetLocalized() 가 CurrentLanguage 를 참조
//       → JSON 안에 ko/en/vi 직접 내장
//
//  유니티 패키지 설치:
//    Window → Package Manager → Unity Registry → Localization
//
//  String Table 생성:
//    Edit → Project Settings → Localization → String Tables → "UI" 테이블 생성
//    테이블에 LocaleKeys 상수와 동일한 키로 각 언어 텍스트 입력
//
//  언어 추가:
//    Edit → Project Settings → Localization → Locale Generator
//    추가 후 String Table 에 해당 언어 컬럼 추가
// ============================================================

    public class LocalizationManager : MonoBehaviour
    {
        public static LocalizationManager Instance { get; private set; }

        // ── 현재 언어 (StringKVPair.GetLocalized 가 참조) ──
        public static string CurrentLanguage { get; private set; } = "ko";

        /// <summary>언어 변경 시 UI 갱신 이벤트</summary>
        public static event System.Action<string> OnLanguageChanged;

        [Header("Settings")]
        [Tooltip("UI 텍스트가 들어있는 String Table 이름")]
        [SerializeField] private string _tableName = "UI";

        private StringTable _stringTable;

        // ── 생명주기 ──────────────────────────

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        private async void Start()
        {
            // Localization 초기화 대기
            await LocalizationSettings.InitializationOperation.Task;

            // 현재 언어 동기화
            CurrentLanguage = LocalizationSettings.SelectedLocale?.Identifier.Code ?? "ko";

            // 언어 변경 이벤트 구독
            LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;

            // String Table 로드
            await LoadTable();

            Debug.Log($"[LocalizationManager] 초기화 완료 — 언어: {CurrentLanguage}");
        }

        private void OnDestroy()
        {
            LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
        }

        // ── 테이블 로드 ───────────────────────

        private async System.Threading.Tasks.Task LoadTable()
        {
            var op = LocalizationSettings.StringDatabase.GetTableAsync(_tableName);
            await op.Task;

            if (op.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
            {
                _stringTable = op.Result;
                Debug.Log($"[LocalizationManager] String Table 로드 완료 — {_tableName}");
            }
            else
            {
                Debug.LogError($"[LocalizationManager] String Table 로드 실패 — {_tableName}");
            }
        }

        // ── 언어 변경 콜백 ────────────────────

        private async void OnLocaleChanged(Locale locale)
        {
            CurrentLanguage = locale.Identifier.Code;
            await LoadTable();

            Debug.Log($"[LocalizationManager] 언어 변경 → {CurrentLanguage}");
            OnLanguageChanged?.Invoke(CurrentLanguage);
        }

        // ── UI 공통 텍스트 조회 ───────────────

        /// <summary>
        /// String Table 키로 현재 언어 텍스트 반환
        /// 없으면 [key] 반환
        /// </summary>
        public static string Get(string key)
        {
            if (Instance == null)
            {
                Debug.LogWarning("[LocalizationManager] Instance 없음");
                return $"[{key}]";
            }

            if (Instance._stringTable == null)
            {
                Debug.LogWarning($"[LocalizationManager] String Table 미로드 — key: {key}");
                return $"[{key}]";
            }

            var entry = Instance._stringTable[key];
            if (entry == null)
            {
                Debug.LogWarning($"[LocalizationManager] 키 없음: {key}");
                return $"[{key}]";
            }

            return entry.LocalizedValue;
        }

        // ── 런타임 언어 변경 ──────────────────

        /// <summary>
        /// 런타임 언어 변경
        /// 유니티 Localization 패키지가 변경을 처리하고
        /// OnLocaleChanged 콜백으로 CurrentLanguage 자동 갱신
        /// StringKVPair.GetLocalized() 도 자동으로 새 언어 반영
        /// </summary>
        public void ChangeLanguage(string langCode)
        {
            var locale = LocalizationSettings.AvailableLocales.GetLocale(langCode);
            if (locale == null)
            {
                Debug.LogWarning($"[LocalizationManager] 지원하지 않는 언어: {langCode}");
                return;
            }

            LocalizationSettings.SelectedLocale = locale;
        }
    }

    // ──────────────────────────────────────────
    //  UI 공통 키 상수 — 오타 방지
    //  유니티 String Table 의 키와 동일해야 함
    //  시나리오별 텍스트는 StringKVPair.GetLocalized() 사용
    // ──────────────────────────────────────────
    public static class LocaleKeys
    {
        // 공통 상태
        public const string CommonComplete = "common.complete";
        public const string CommonFailed   = "common.failed";
        public const string CommonPending  = "common.pending";
        public const string CommonRunning  = "common.running";

        // Task UI
        public const string UiTaskProgress = "ui.task.progress"; // "{0} / {1}"
        public const string UiTaskTitle    = "ui.task.title";
        public const string UiTaskDesc     = "ui.task.desc";
        public const string UiTaskTimeout  = "ui.task.timeout";  // "남은 시간: {0}s"
    }