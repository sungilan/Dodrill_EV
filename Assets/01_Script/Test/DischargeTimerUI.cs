using UnityEngine;
using TMPro;
using System.Collections;

public class DischargeTimerUI : MonoBehaviour
{
    public static DischargeTimerUI Instance;

    public TextMeshProUGUI timerText;
    public GameObject timerPanel;
    public float dischargeTime = 10f; // 실제로는 5분이지만 테스트용 10초

    public bool isDischarged = false;

    void Awake() => Instance = this;

    public void StartDischarge()
    {
        if(!LOTOSystem.Instance.isLOCKED) return; // 자물쇠 안 걸었으면 시작 불가

        timerPanel.SetActive(true);
        StartCoroutine(DischargeRoutine());
    }

    private IEnumerator DischargeRoutine()
    {
        float currentTime = dischargeTime;
        while(currentTime > 0)
        {
            timerText.text = $"잔류 전압 방전 중... {currentTime:F1}s";
            yield return new WaitForSeconds(0.1f);
            currentTime -= 0.1f;
        }

        isDischarged = true;
        timerText.text = "방전 완료. 전압 측정을 시작하십시오.";
        // [사운드] 완료 알림음
    }
}