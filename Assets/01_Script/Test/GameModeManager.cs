using UnityEngine;

public class GameModeManager : MonoBehaviour
{
    public enum PlayMode { Training, Exam }
    public PlayMode currentMode = PlayMode.Training;

    [Header("Evaluation Settings")]
    public int totalScore = 100;
    public int hintPenalty = 5; // 힌트 사용 시 감점
    public int hintCount = 3; // 최대 3번만 가능

    public static GameModeManager Instance;
    void Awake() => Instance = this;

    // 평가 모드 설정 시 가이드 UI 숨김
    public void SetMode(PlayMode mode)
    {
        currentMode = mode;
        if(currentMode == PlayMode.Exam)
        {
            AIGuideManager.Instance.guidePanel.SetActive(false); // 가이드 숨김
            // 모든 하이라이트 제거 로직 호출
        }
    }

    public void UseHint()
    {
        if(currentMode == PlayMode.Exam && hintCount > 0)
        {
            hintCount--;
            totalScore -= hintPenalty;
            AIGuideManager.Instance.ShowTemporaryHint(5f);
            // UI에 남은 힌트 개수 표시 로직 추가 가능
        }
    }
}