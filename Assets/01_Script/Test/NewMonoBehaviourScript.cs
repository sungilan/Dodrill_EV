using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class CheckpointManager : MonoBehaviour
{
    public static CheckpointManager Instance;

    [System.Serializable]
    public class CheckpointData
    {
        public int stepIndex;
        // 필요 시 플레이어 위치나 부품의 상태(조립 여부)를 저장하는 구조체 추가 가능
    }

    private int lastSavedStepIndex = 0;

    void Awake() => Instance = this;

    // AIGuideManager에서 다음 단계로 넘어갈 때 호출
    public void SaveCheckpoint(int stepIndex)
    {
        lastSavedStepIndex = stepIndex;
        Debug.Log($"체크포인트 저장: {stepIndex}단계");
    }

    // "현재 단계부터 다시 시작" 버튼 클릭 시 호출
    public void RestartFromCheckpoint()
    {
        // 1. 현재 씬의 모든 가변 데이터 리셋 (볼트, 부품 상태 등)
        ResetAllInteractables();

        // 2. 가이드 매니저를 저장된 인덱스로 복구
        AIGuideManager.Instance.JumpToStep(lastSavedStepIndex);

        // 3. UI 닫기
        // EmergencyEventManager.Instance.deathCanvas.SetActive(false);
    }

    private void ResetAllInteractables()
    {
        // 씬 내의 모든 Bolt와 InteractablePart를 찾아 Reset 함수 실행
        Bolt[] bolts = FindObjectsOfType<Bolt>();
        foreach(var b in bolts) b.ResetBolt();

        InteractablePart[] parts = FindObjectsOfType<InteractablePart>();
        // 각 부품의 상태를 초기화하는 로직 추가
    }
}