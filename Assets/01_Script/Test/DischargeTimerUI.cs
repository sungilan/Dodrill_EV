using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Components;
using UnityEngine.UI; // ★ Slider 제어를 위해 추가

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

    [Header("Localization Settings")]
    // 인스펙터에서 "잔류 전압 방전 중... {0:F1}s" 형태의 키를 연결하세요.
    [SerializeField] private UnityEngine.Localization.LocalizedString _lsDischarging;
    // 인스펙터에서 "방전 완료. 다음 단계를 진행하십시오." 키를 연결하세요.
    [SerializeField] private UnityEngine.Localization.LocalizedString _lsComplete;

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
        float t = 0f;
        var lse = timerText != null ? timerText.GetComponent<LocalizeStringEvent>() : null;

        while(t < dischargeTime)
        {
            t += Time.deltaTime;
            float remaining = dischargeTime - t;

            // 1. 텍스트 업데이트 (LocalizeStringEvent 방식)
            if(lse != null)
            {
                // 인스펙터에서 지정된 _lsDischarging에 인자(remaining)를 전달하여 갱신
                _lsDischarging.Arguments = new object[] { remaining };
                lse.StringReference = _lsDischarging;
            }

            // 2. 슬라이더 업데이트
            if(progressBar != null)
                progressBar.value = t;

            yield return null;
        }

        isDischarged = true;

        if(progressBar != null) progressBar.value = dischargeTime;

        // 방전 완료 텍스트 설정
        if(lse != null)
        {
            lse.StringReference = _lsComplete;
        }

        yield return new WaitForSeconds(1.0f); // 완료 메시지를 잠시 보여줌
        if(timerPanel != null) timerPanel.SetActive(false);

        InteractionEvents.FireTaskConfirmed("DischargeWait");
        Debug.Log("[DischargeTimer] 방전 완료 → DischargeWait Task 완료");
    }
}