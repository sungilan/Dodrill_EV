using UnityEngine;
using TMPro;
using System.Collections;

// ============================================================
//  DischargeTimerUI.cs
//  Task[3] DischargeWait Running 감지 → 패널 자동 활성화 → 10초 카운트다운
//
//  ScenarioStateReceiver.OnTaskStateUpdated 이벤트를 클라이언트에서 구독.
//  서버에서 직접 UI를 조작할 수 없으므로 이 방식 사용.
//
//  씬 배치:
//    Discharge_Timer_UI (활성화 상태 유지 — Awake 호출 필요)
//      └── Panel (기본 비활성) ← timerPanel 필드에 연결
//            └── DescriptionText (TMP) ← timerText 필드에 연결
// ============================================================
public class DischargeTimerUI : MonoBehaviour
{
    public static DischargeTimerUI Instance;

    [Header("UI 참조")]
    public TextMeshProUGUI timerText;
    public GameObject timerPanel;

    [Header("설정")]
    public float dischargeTime = 10f;

    [Header("테스트")]
    [Tooltip("LOTOSystem 잠금 없이도 타이머 시작")]
    public bool bypassLOTO = true;

    [Tooltip("DischargeWait 가 몇 번째 Task인지 (0-based). JSON 순서와 일치시킬 것)")]
    public int dischargeTaskIndex = 3;

    public bool isDischarged = false;

    private void Awake()
    {
        Instance = this;
        if(timerPanel != null) timerPanel.SetActive(false);
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
        // Task[3] DischargeWait 가 Running 이 되면 자동 시작
        if(broadcast.currentTask.taskIndex == dischargeTaskIndex &&
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

        if(!locked)
        {
            Debug.LogWarning("[DischargeTimer] LOTO 잠금 안 됨 — bypassLOTO 체크 또는 LOTOSystem 설정 확인");
            return;
        }

        if(isDischarged) return;

        if(timerPanel != null) timerPanel.SetActive(true);
        StartCoroutine(DischargeRoutine());
    }

    private IEnumerator DischargeRoutine()
    {
        float t = dischargeTime;
        while(t > 0)
        {
            if(timerText != null)
                timerText.text = $"잔류 전압 방전 중... {t:F1}s";
            yield return new WaitForSeconds(0.1f);
            t -= 0.1f;
        }

        isDischarged = true;

        if(timerText != null)
            timerText.text = "방전 완료. 다음 단계를 진행하십시오.";

        // ConfirmTaskModule(DischargeWaitModule)에 완료 신호
        InteractionEvents.FireTaskConfirmed("DischargeWait");
        Debug.Log("[DischargeTimer] 방전 완료 → DischargeWait Task 완료");
    }
}