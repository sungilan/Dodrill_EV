using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

[System.Serializable]
public class PenaltyLog
{
    public string reason;   // 감점 사유
    public int score;       // 감점된 점수
}

public class EvaluationReport : MonoBehaviour
{
    public static EvaluationReport Instance;

    [Header("UI References")]
    public TextMeshProUGUI finalScoreText;
    public TextMeshProUGUI gradeText;
    public TextMeshProUGUI logListText;
    public Image scoreFillImage; // 원형 Fill Image

    private List<PenaltyLog> penaltyLogs = new List<PenaltyLog>();
    private int currentScore = 100;

    void Awake() => Instance = this;

    // 감점 발생 시 호출 (ScoreManager나 각 스크립트에서 호출)
    public void AddPenalty(string reason, int penalty)
    {
        currentScore -= penalty;
        currentScore = Mathf.Clamp(currentScore, 0, 100);

        penaltyLogs.Add(new PenaltyLog { reason = reason, score = penalty });
        Debug.Log($"[감점] {reason} : -{penalty}점");
    }

    // 시나리오 종료 시 UI 표시
    public void ShowReport()
    {
        gameObject.SetActive(true);

        // 1. 점수 및 등급 계산
        finalScoreText.text = currentScore.ToString();
        scoreFillImage.fillAmount = currentScore / 100f;
        gradeText.text = CalculateGrade(currentScore);
        gradeText.color = GetGradeColor(gradeText.text);

        // 2. 감점 리스트 출력
        logListText.text = "";
        foreach(var log in penaltyLogs)
        {
            logListText.text += $"<color=red>-{log.score}</color> | {log.reason}\n";
        }
    }

    private string CalculateGrade(int score)
    {
        if(score >= 90) return "S";
        if(score >= 80) return "A";
        if(score >= 70) return "B";
        return "F (재시험)";
    }

    private Color GetGradeColor(string grade)
    {
        if(grade == "S") return Color.cyan;
        if(grade == "F (재시험)") return Color.red;
        return Color.white;
    }

    public void ConfirmAndSave()
    {
        DataPersistenceManager.Instance.SaveResult(currentScore, CalculateGrade(currentScore), penaltyLogs);
        // 메인 메뉴로 이동하거나 종료
    }
}