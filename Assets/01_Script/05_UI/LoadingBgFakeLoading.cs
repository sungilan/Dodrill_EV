using System.Collections;
using DoDrill;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Loading_BG 등: 표시 후 프로그레스는 4.5~5초 사이 랜덤 시점에 100% 도달,
/// 전체는 <see cref="visibleDurationSeconds"/> 후 비활성화합니다.
/// VR/PC 공통으로 <see cref="Time.unscaledTime"/>를 사용합니다.
///
/// <para><c>Panel_LanguageSelection</c> 또는 <c>alwaysOnTop</c> 계열과 겹칠 때
/// Awake/Show에서 <c>SetAsLastSibling</c>을 하고, 표시 구간 동안은
/// <see cref="LateUpdate"/>로 맨 앞으로 유지합니다.</para>
///
/// <para><see cref="TutorialPanel"/>은 정적 이벤트 없이,
/// 비활성화 시 <see cref="_tutorialPanelOnDismiss"/> 또는 같은 플레이어 루트(<see cref="Transform.root"/>) 아래 패널에 알립니다.</para>
/// </summary>
public class LoadingBgFakeLoading : MonoBehaviour
{
    private static readonly string[] DotSteps = { "", ".", "..", "..." };

    [SerializeField] private Image progressBar;
    [SerializeField] private TMP_Text loadingLabel;

    [Header("Tutorial (이벤트 없음)")]
    //[Tooltip("비우면 같은 플레이어 루트(transform.root) 아래 TutorialPanel을 찾습니다. 멀티에서는 반드시 인스턴스별로 연결하는 것을 권장합니다.")]
    //[SerializeField] private TutorialPanel _tutorialPanelOnDismiss;

    [Tooltip("켜면 고정 시간 후 자동으로 비활성화합니다. 끄면 외부에서 끌 때까지 유지합니다.")]
    [SerializeField] private bool dismissAfterFixedTime = true;

    [Tooltip("로딩 패널 표시 시간(초). dismissAfterFixedTime=true일 때만 사용")]
    [SerializeField] private float visibleDurationSeconds = 5f;

    [Tooltip("프로그레스 100% 도달 시점 = 시작 후 [min, max] 초 사이 랜덤")]
    [SerializeField] private float fakeCompleteMin = 4.5f;
    [SerializeField] private float fakeCompleteMax = 5f;

    [Tooltip("외부 종료 대기 모드에서 프로그레스 바 왕복 속도")]
    [SerializeField] private float indeterminateProgressSpeed = 0.85f;

    [Header("로딩 문구")]
    [SerializeField] private float labelStepSeconds = 0.45f;

    [Header("텍스트 알파 펄스")]
    [SerializeField] private float fadePulseSpeed = 1.1f;
    [SerializeField] [Range(0f, 1f)] private float fadeAlphaMin = 0.35f;
    [SerializeField] [Range(0f, 1f)] private float fadeAlphaMax = 1f;

    private bool _keepOnTop;
    private int _labelIndex;
    private float _nextLabelTime;
    private Color _labelBaseColor = Color.white;
    private string _loadingBaseLabel = "로딩중";

    private void OnEnable()
    {
        LocalizationManager.OnLanguageChanged += OnLocalizationLanguageChanged;
        StopAllCoroutines();
        BringToFront();
        _keepOnTop = true;

        if (loadingLabel == null)
            loadingLabel = GetComponentInChildren<TMP_Text>(true);
        if (loadingLabel != null)
            _labelBaseColor = loadingLabel.color;

        _labelIndex = 0;
        _nextLabelTime = Time.unscaledTime;
        //UpdateLoadingBaseLabel();
        ApplyLoadingLabel();
        StartCoroutine(CoFakeLoading());
    }

    private void OnDisable()
    {
        LocalizationManager.OnLanguageChanged -= OnLocalizationLanguageChanged;
        StopAllCoroutines();
        _keepOnTop = false;

        //var panel = ResolveTutorialPanelForDismiss();
        //panel?.NotifyFakeLoadingDismissed();
    }

//    private TutorialPanel ResolveTutorialPanelForDismiss()
//    {
//        if (_tutorialPanelOnDismiss != null)
//            return _tutorialPanelOnDismiss;

//        var fromRoot = transform.root.GetComponentInChildren<TutorialPanel>(true);
//        if (fromRoot != null)
//            return fromRoot;

//#if UNITY_EDITOR || DEVELOPMENT_BUILD
//        Debug.LogWarning(
//            "[LoadingBgFakeLoading] 이 로딩 UI와 같은 플레이어 계층에서 TutorialPanel을 찾지 못했습니다. Inspector에서 _tutorialPanelOnDismiss를 지정하세요.",
//            this);
//#endif
//        return null;
//    }

    /// <summary>
    /// 로비 진입 중 인스턴스가 이미 활성 상태일 때도 맵 로드까지 유지하도록 외부에서 제어합니다.
    /// </summary>
    public void SetDismissAfterFixedTime(bool value)
    {
        dismissAfterFixedTime = value;
    }

    private void LateUpdate()
    {
        if (!_keepOnTop)
            return;

        BringToFront();
    }

    private static void BringToFront(Transform t)
    {
        if (t != null)
            t.SetAsLastSibling();
    }

    private void BringToFront() => BringToFront(transform);

    private void OnLocalizationLanguageChanged(string _)
    {
        //UpdateLoadingBaseLabel();
        ApplyLoadingLabel();
    }

    //private void UpdateLoadingBaseLabel()
    //{
    //    _loadingBaseLabel = LocalizationManager.GetOrDefault(LocaleKeys.LoadingInProgress, "로딩중");
    //}

    private void ApplyLoadingLabel()
    {
        if (loadingLabel == null)
            return;

        string dots = DotSteps[_labelIndex % DotSteps.Length];
        loadingLabel.text = $"{_loadingBaseLabel}{dots}";
    }

    private void ApplyFadePulse()
    {
        if (loadingLabel == null)
            return;

        float t = Mathf.PingPong(Time.unscaledTime * fadePulseSpeed, 1f);
        float a = Mathf.Lerp(fadeAlphaMin, fadeAlphaMax, t);
        var c = _labelBaseColor;
        c.a = a;
        loadingLabel.color = c;
    }

    private IEnumerator CoFakeLoading()
    {
        yield return null;
        BringToFront();

        if (dismissAfterFixedTime)
            yield return CoTimedDismiss();
        else
            yield return CoWaitUntilExternalDismiss();
    }

    private IEnumerator CoTimedDismiss()
    {
        float cap = Mathf.Max(0.01f, visibleDurationSeconds);
        float minT = Mathf.Clamp(fakeCompleteMin, 0.1f, cap);
        float maxT = Mathf.Clamp(fakeCompleteMax, minT, cap);
        float completeAt = UnityEngine.Random.Range(minT, maxT);

        if (progressBar != null)
            progressBar.fillAmount = 0f;

        float start = Time.unscaledTime;
        _nextLabelTime = start;

        while (Time.unscaledTime - start < visibleDurationSeconds)
        {
            float elapsed = Time.unscaledTime - start;
            if (progressBar != null)
                progressBar.fillAmount = Mathf.Clamp01(elapsed / completeAt);

            TickLabelAndFade();
            yield return null;
        }

        if (progressBar != null)
            progressBar.fillAmount = 1f;

        if (loadingLabel != null)
        {
            var c = _labelBaseColor;
            c.a = fadeAlphaMax;
            loadingLabel.color = c;
        }

        _keepOnTop = false;
        gameObject.SetActive(false);
    }

    private IEnumerator CoWaitUntilExternalDismiss()
    {
        if (progressBar != null)
            progressBar.fillAmount = 0.35f;

        float start = Time.unscaledTime;
        _nextLabelTime = start;

        while (gameObject.activeInHierarchy)
        {
            if (progressBar != null)
            {
                float pulse = 0.55f + 0.35f * Mathf.Sin(Time.unscaledTime * indeterminateProgressSpeed);
                progressBar.fillAmount = pulse;
            }

            TickLabelAndFade();
            yield return null;
        }

        _keepOnTop = false;
    }

    private void TickLabelAndFade()
    {
        if (loadingLabel != null && Time.unscaledTime >= _nextLabelTime)
        {
            _labelIndex = (_labelIndex + 1) % DotSteps.Length;
            ApplyLoadingLabel();
            _nextLabelTime = Time.unscaledTime + Mathf.Max(0.05f, labelStepSeconds);
        }

        ApplyFadePulse();
    }
}
