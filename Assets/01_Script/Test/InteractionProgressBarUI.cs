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
        // 초기화: 둘 다 숨김
        if(boltPanelGroup != null) boltPanelGroup.alpha = 0;
        //if(timerPanelGroup != null) timerPanelGroup.alpha = 0;
    }

    // --- 볼트 가이드 제어 ---
    public void ShowBoltProgress(float value, string taskName, string objName, string instruction = "")
    {
        if(boltPanelGroup == null) return;

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