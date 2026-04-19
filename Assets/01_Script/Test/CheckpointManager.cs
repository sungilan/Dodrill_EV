using UnityEngine;

// ============================================================
//  CheckpointManager.cs
//  정비 단계 체크포인트 저장 및 재시작.
//
//  기존 파일이 빈 MonoBehaviour 였으므로 완성본으로 교체.
//
//  사용 흐름:
//    1. AIGuideManager.NextStep() 호출 시 SaveCheckpoint(stepIndex) 자동 호출
//    2. 사고 발생(EmergencyEventManager) 후 재시도 버튼 → RestartFromCheckpoint()
//    3. AIGuideManager.JumpToStep(lastSavedStepIndex) 로 복귀
// ============================================================

public class CheckpointManager : MonoBehaviour
{
    public static CheckpointManager Instance { get; private set; }

    private int _lastSavedStepIndex = 0;

    private void Awake()
    {
        if(Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── 공개 API ─────────────────────────────

    /// <summary>
    /// 현재 단계를 체크포인트로 저장.
    /// AIGuideManager.NextStep() 이 호출될 때마다 자동으로 실행.
    /// </summary>
    public void SaveCheckpoint(int stepIndex)
    {
        _lastSavedStepIndex = stepIndex;
        Debug.Log($"[Checkpoint] Step {stepIndex} 저장됨");
    }

    /// <summary>
    /// 마지막 체크포인트부터 재시작.
    /// 재시도 버튼의 onClick 에 연결.
    /// </summary>
    public void RestartFromCheckpoint()
    {
        Debug.Log($"[Checkpoint] Step {_lastSavedStepIndex} 에서 재시작");

        // 1. 씬의 모든 부품/볼트 상태 초기화
        ResetAllInteractables();

        // 2. 가이드 매니저를 저장된 단계로 복구
        var guide = FindFirstObjectByType<AIGuideManager>();
        if(guide != null)
            guide.JumpToStep(_lastSavedStepIndex);

        // 3. 안전 상태도 초기화 (장갑·MSD 다시 착용해야 함)
        ElectricSafetyManager.Instance?.ResetSafetyState();
    }

    public int LastSavedStepIndex => _lastSavedStepIndex;

    // ── 내부 유틸 ────────────────────────────

    private void ResetAllInteractables()
    {
        // 씬의 모든 볼트 초기화
        //foreach(var bolt in FindObjectsByType<Bolt>(FindObjectsSortMode.None))
            //bolt.ResetBolt();

        // 씬의 모든 부품 초기화
        foreach(var part in FindObjectsByType<InteractablePart>(FindObjectsSortMode.None))
            part.ResetPart();

        Debug.Log("[Checkpoint] 씬 오브젝트 초기화 완료");
    }
}