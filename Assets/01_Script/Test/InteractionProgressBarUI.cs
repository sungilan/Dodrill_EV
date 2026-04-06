using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InteractionProgressBarUI : MonoBehaviour
{
    public static InteractionProgressBarUI Instance;

    [Header("UI 참조")]
    public GameObject rootPanel;      // 최상위 패널
    public Slider progressBar;        // 슬라이더
    public TextMeshProUGUI taskText;  // "볼트 해제 중..." 등
    public TextMeshProUGUI targetNameText; // "대상: 배터리팩 볼트 #1"

    private void Awake()
    {
        Instance = this;
        if(rootPanel != null) rootPanel.SetActive(false);
    }

    /// <summary>
    /// 진행도 업데이트 및 표시
    /// </summary>
    /// <param name="value">0~1</param>
    /// <param name="taskName">작업 종류</param>
    /// <param name="objName">작업 대상 이름</param>
    public void ShowProgress(float value, string taskName, string objName)
    {
        if(rootPanel == null) return;

        // 진행 중일 때만 표시
        if(value > 0.01f && value < 0.99f)
        {
            if(!rootPanel.activeSelf) rootPanel.SetActive(true);

            progressBar.value = value;
            if(taskText != null) taskText.text = taskName;
            if(targetNameText != null) targetNameText.text = $"대상: {objName}";
        }
        else
        {
            // 완료되거나 중단되면 숨김
            if(rootPanel.activeSelf) rootPanel.SetActive(false);
        }
    }
}