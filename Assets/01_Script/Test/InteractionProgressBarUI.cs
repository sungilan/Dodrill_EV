using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

public class InteractionProgressBarUI : MonoBehaviour
{
    public static InteractionProgressBarUI Instance;

    [Header("볼트 가이드용 패널")]
    public CanvasGroup boltPanelGroup;
    public Slider boltProgressBar;
    public TextMeshProUGUI boltTaskText;
    public TextMeshProUGUI boltTargetText;
    public TextMeshProUGUI boltInstructionText;

    [Header("방전 타이머용 패널")]
    public CanvasGroup timerPanelGroup;
    public TextMeshProUGUI timerText;

    private void Awake()
    {
        Instance = this;
        // 문구가 길어져도 패널 밖으로 튀어나오지 않도록 자동 크기 조절
        FitText(boltTaskText, 0.6f);
        FitText(boltTargetText, 0.6f);
        FitText(boltInstructionText, 0.6f);
        FitText(timerText, 0.6f);
        // 초기화: 둘 다 숨김
        if(boltPanelGroup != null) boltPanelGroup.alpha = 0;
        //if(timerPanelGroup != null) timerPanelGroup.alpha = 0;
    }
    // ShowBoltProgress는 사용 중인 도구가 매 프레임 호출한다. 호출이 끊기면(도구 비활성화·볼트 제거·단계 전환 등)
    // 호출자의 정리 코드와 무관하게 패널이 스스로 사라지도록 하는 안전장치.
    private const float HideAfterSilence = 0.4f;
    private float _lastShowTime;
    private bool _boltVisible;

    private void Update()
    {
        if(_boltVisible && Time.unscaledTime - _lastShowTime > HideAfterSilence)
            HideBoltProgress();
    }

    private static void FitText(TextMeshProUGUI tmp, float minRatio)
    {
        if(tmp == null) return;
        float max = tmp.fontSize;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMax = max;
        tmp.fontSizeMin = Mathf.Max(8f, max * minRatio);
        tmp.overflowMode = TextOverflowModes.Ellipsis;
    }


    // --- 볼트 가이드 제어 ---
    public void ShowBoltProgress(float value, string taskName, string objName, string instruction = "")
    {
        if(boltPanelGroup == null) return;

        _lastShowTime = Time.unscaledTime;
        _boltVisible = true;

        // 현재 보이지 않는 상태라면 페이드 인 시작
        if(boltPanelGroup.alpha < 0.1f)
        {
            boltPanelGroup.DOKill();
            boltPanelGroup.DOFade(1f, 0.2f).SetEase(Ease.OutQuad);
        }

        boltProgressBar.value = value;
        if(boltTaskText != null) boltTaskText.text = taskName;
        if(boltTargetText != null) boltTargetText.text = $"대상: {objName}";
        if(boltInstructionText != null) boltInstructionText.text = instruction;
    }

    public void HideBoltProgress()
    {
        _boltVisible = false;
        if(boltPanelGroup == null || boltPanelGroup.alpha <= 0) return;

        boltPanelGroup.DOKill();
        boltPanelGroup.DOFade(0f, 0.2f).SetEase(Ease.InQuad);
    }

    // --- 방전 타이머 제어 ---
    public void ShowTimer(string timeStr)
    {
        if(timerPanelGroup == null) return;
        if(timerPanelGroup.alpha < 0.9f && !DOTween.IsTweening(timerPanelGroup))
        {
            timerPanelGroup.DOKill();
            timerPanelGroup.DOFade(1f, 0.3f);
        }
        if(timerText != null) timerText.text = timeStr;
    }

    public void HideTimer()
    {
        if(timerPanelGroup == null) return;
        timerPanelGroup.DOKill();
        timerPanelGroup.DOFade(0f, 0.3f);
    }
}