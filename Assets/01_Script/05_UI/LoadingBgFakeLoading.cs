using System.Collections;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Loading_BG 등: 표시 후 프로그레스는 4.5~5초 사이 랜덤 시점에 100% 도달, 전체는 <see cref="visibleDurationSeconds"/> 후 비활성화.
/// VR/PC 공통 — <see cref="Time.unscaledTime"/> 사용.
/// <para><c>Panel_LanguageSelection</c> 등 <c>allwaysOnTop</c> 뷰가 Awake/Show에서 <c>SetAsLastSibling</c> 하므로,
/// 표시 구간에는 매 <see cref="LateUpdate"/>로 다시 맨 앞으로 올립니다.</para>
/// </summary>
public class LoadingBgFakeLoading : MonoBehaviour
{
    public static event Action<LoadingBgFakeLoading> OnHidden;

    private static readonly string[] LoadingLabelSteps =
    {
        "로딩중",
        "로딩중.",
        "로딩중..",
        "로딩중..."
    };

    [SerializeField] private Image progressBar;
    [SerializeField] private TMP_Text loadingLabel;

    [Tooltip("켜면 부팅용: 고정 시간 후 스스로 비활성화. 끄면 LayoutClientLoaderEvents / Binder가 끌 때까지 유지(게임 맵 로드).")]
    [SerializeField] private bool dismissAfterFixedTime = true;

    [Tooltip("로딩 패널을 켠 뒤 이 시간(초)이 지나면 GameObject 비활성화 (dismissAfterFixedTime 일 때만)")]
    [SerializeField] private float visibleDurationSeconds = 5f;

    [Tooltip("프로그레스 100%에 도달하는 시각 = 시작 후 [min, max] 초 사이 랜덤")]
    [SerializeField] private float fakeCompleteMin = 4.5f;
    [SerializeField] private float fakeCompleteMax = 5f;

    [Tooltip("맵 로드 대기 모드에서 프로그레스 바가 왔다 갔다 하는 속도")]
    [SerializeField] private float indeterminateProgressSpeed = 0.85f;

    [Header("로딩 문구")]
    [SerializeField] private float labelStepSeconds = 0.45f;

    [Header("텍스트 알파 펄스 (왔다 갔다)")]
    [SerializeField] private float fadePulseSpeed = 1.1f;
    [SerializeField] [Range(0f, 1f)] private float fadeAlphaMin = 0.35f;
    [SerializeField] [Range(0f, 1f)] private float fadeAlphaMax = 1f;

    private bool _keepOnTop;
    private int _labelIndex;
    private float _nextLabelTime;
    private Color _labelBaseColor = Color.white;

    private void OnEnable()
    {
        StopAllCoroutines();
        BringToFront();
        _keepOnTop = true;
        if (loadingLabel == null)
            loadingLabel = GetComponentInChildren<TMP_Text>(true);
        if (loadingLabel != null)
            _labelBaseColor = loadingLabel.color;
        _labelIndex = 0;
        _nextLabelTime = Time.unscaledTime;
        ApplyLoadingLabel();
        StartCoroutine(CoFakeLoading());
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        _keepOnTop = false;
        OnHidden?.Invoke(this);
    }

    /// <summary>훈련 진입 등 — 인스펙터가 부팅용(true)이어도 런타임에서 맵 로드까지 유지하도록 덮어씁니다.</summary>
    public void SetDismissAfterFixedTime(bool value)
    {
        dismissAfterFixedTime = value;
    }

    private void LateUpdate()
    {
        if (!_keepOnTop) return;
        BringToFront();
    }

    private static void BringToFront(Transform t)
    {
        if (t != null)
            t.SetAsLastSibling();
    }

    private void BringToFront() => BringToFront(transform);

    private void ApplyLoadingLabel()
    {
        if (loadingLabel == null) return;
        loadingLabel.text = LoadingLabelSteps[_labelIndex % LoadingLabelSteps.Length];
    }

    private void ApplyFadePulse()
    {
        if (loadingLabel == null) return;

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
            _labelIndex = (_labelIndex + 1) % LoadingLabelSteps.Length;
            ApplyLoadingLabel();
            _nextLabelTime = Time.unscaledTime + Mathf.Max(0.05f, labelStepSeconds);
        }

        ApplyFadePulse();
    }
}
