using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InteractionProgressBarUI : MonoBehaviour
{
    public static InteractionProgressBarUI Instance;

    [Header("UI 참조")]
    public GameObject rootPanel;
    public Slider progressBar;
    public TextMeshProUGUI taskText;
    public TextMeshProUGUI targetNameText;

    private void Awake()
    {
        Instance = this;
        if (rootPanel != null) rootPanel.SetActive(false);
    }

    public void ShowProgress(float value, string taskName, string objName)
    {
        if (rootPanel == null) return;

        // 1. 패널이 꺼져있다면 켬
        if (!rootPanel.activeSelf) rootPanel.SetActive(true);

        // 2. 값 업데이트 (범위 제한 없이 1.0까지 표시)
        progressBar.value = value;
        if (taskText != null) taskText.text = taskName;
        if (targetNameText != null) targetNameText.text = $"대상: {objName}";

        // 3. 만약 100% 완료되었다면 약간의 연출 후 꺼지게 하고 싶다면 여기서 제어 가능
        // 현재는 렌치를 떼는 시점에 끄는 것이 가장 정확하므로 아래 Hide함수를 렌치에서 호출합니다.
    }

    public void HideProgress()
    {
        if (rootPanel != null && rootPanel.activeSelf)
            rootPanel.SetActive(false);
    }
}