using UnityEngine;
using TMPro;
using System.Collections;

// ============================================================
//  SafetyUIHandler.cs
//  안전 수칙 위반 시 경고 UI + 화면 효과.
//
//  씬 배치:
//    빈 GameObject 에 부착 (이름: _SafetyUIHandler).
//    warningPanel: World Space Canvas 또는 Screen Space Overlay.
//    비네트는 URP Volume 없어도 동작하도록 간단한 이미지 기반으로 구현.
//
//  테스트:
//    SafetyUIHandler.Instance.TriggerWarning("테스트 경고");
// ============================================================

public class SafetyUIHandler : MonoBehaviour
{
    public static SafetyUIHandler Instance { get; private set; }

    [Header("UI Elements")]
    [Tooltip("경고 패널 루트 GameObject")]
    public GameObject warningPanel;

    [Tooltip("경고 메시지 텍스트 (TMP)")]
    public TextMeshProUGUI warningText;

    [Tooltip("화면 가장자리 빨간 비네트 이미지 (선택)")]
    public UnityEngine.UI.Image vignetteImage;

    [Header("설정")]
    [Tooltip("경고 표시 지속 시간 (초)")]
    public float displayDuration = 3f;

    [Tooltip("비네트 최대 알파값")]
    [Range(0f, 1f)]
    public float vignetteMaxAlpha = 0.5f;

    private Coroutine _warningCoroutine;

    private void Awake()
    {
        if(Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        if(warningPanel != null) warningPanel.SetActive(false);
        if(vignetteImage != null) SetVignetteAlpha(0f);
    }

    // ── 공개 API ─────────────────────────────

    /// <summary>
    /// 경고 메시지 표시.
    /// InteractablePart.ShowSafetyWarning() 에서 호출.
    /// </summary>
    public void TriggerWarning(string message)
    {
        if(_warningCoroutine != null) StopCoroutine(_warningCoroutine);
        _warningCoroutine = StartCoroutine(ShowWarningRoutine(message));
    }

    // ── 내부 로직 ────────────────────────────

    private IEnumerator ShowWarningRoutine(string message)
    {
        // 1. 메시지 표시
        if(warningText != null) warningText.text = message;
        if(warningPanel != null) warningPanel.SetActive(true);

        // 2. 비네트 페이드 인
        yield return StartCoroutine(FadeVignette(0f, vignetteMaxAlpha, 0.2f));

        // 3. 대기
        yield return new WaitForSeconds(displayDuration);

        // 4. 비네트 페이드 아웃
        yield return StartCoroutine(FadeVignette(vignetteMaxAlpha, 0f, 0.4f));

        // 5. 패널 숨김
        if(warningPanel != null) warningPanel.SetActive(false);

        _warningCoroutine = null;
    }

    private IEnumerator FadeVignette(float from, float to, float duration)
    {
        if(vignetteImage == null) yield break;

        float elapsed = 0f;
        while(elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetVignetteAlpha(Mathf.Lerp(from, to, elapsed / duration));
            yield return null;
        }
        SetVignetteAlpha(to);
    }

    private void SetVignetteAlpha(float alpha)
    {
        if(vignetteImage == null) return;
        var c = vignetteImage.color;
        c.a = alpha;
        vignetteImage.color = c;
    }
}