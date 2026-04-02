using UnityEngine;
using UnityEngine.UI; // ★ Slider 제어를 위해 추가
using TMPro;
using System.Collections;

public class DischargeTimerUI : MonoBehaviour
{
    public static DischargeTimerUI Instance;

    [Header("UI 참조")]
    public TextMeshProUGUI timerText;
    public GameObject timerPanel;
    public Slider progressBar; // ★ 프로그레스 바 Slider 참조 추가

    [Header("설정")]
    public float dischargeTime = 10f;

    [Header("테스트")]
    public bool bypassLOTO = true;
    public int dischargeTaskIndex = 3;

    public bool isDischarged = false;

    private void Awake()
    {
        Instance = this;
        if (timerPanel != null) timerPanel.SetActive(false);

        // 슬라이더 초기 설정
        if (progressBar != null)
        {
            progressBar.minValue = 0;
            progressBar.maxValue = dischargeTime;
            progressBar.value = 0;
        }
    }

    private void OnEnable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated += OnTaskStateUpdated;
    }

    private void OnDisable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated -= OnTaskStateUpdated;
    }

    private void OnTaskStateUpdated(TaskStateBroadcast broadcast)
    {
        if (broadcast.currentTask.taskIndex == dischargeTaskIndex &&
            broadcast.currentTask.status == TaskStatus.Running &&
            !isDischarged)
        {
            StartDischarge();
        }
    }

    public void StartDischarge()
    {
        bool locked = bypassLOTO
                   || LOTOSystem.Instance == null
                   || LOTOSystem.Instance.isLOCKED;

        if (!locked)
        {
            Debug.LogWarning("[DischargeTimer] LOTO 잠금 안 됨");
            return;
        }

        if (isDischarged) return;

        if (timerPanel != null) timerPanel.SetActive(true);
        StartCoroutine(DischargeRoutine());
    }

    private IEnumerator DischargeRoutine()
    {
        float t = 0f; // 0에서 시작해서 dischargeTime까지 증가

        while (t < dischargeTime)
        {
            t += Time.deltaTime;

            // 1. 텍스트 업데이트 (남은 시간 표시)
            if (timerText != null)
                timerText.text = $"잔류 전압 방전 중... {(dischargeTime - t):F1}s";

            // 2. 슬라이더 업데이트 (진행도 표시)
            if (progressBar != null)
                progressBar.value = t;

            yield return null; // 매 프레임 부드럽게 업데이트
        }

        isDischarged = true;

        if (progressBar != null) progressBar.value = dischargeTime;

        if (timerText != null)
            timerText.text = "방전 완료. 다음 단계를 진행하십시오.";
        if (timerPanel != null) timerPanel.SetActive(false);

        InteractionEvents.FireTaskConfirmed("DischargeWait");
        Debug.Log("[DischargeTimer] 방전 완료 → DischargeWait Task 완료");
    }
}