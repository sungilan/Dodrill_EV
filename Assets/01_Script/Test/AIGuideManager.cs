using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class AIGuideManager : MonoBehaviour
{
    public static AIGuideManager Instance;

    [Header("UI Elements")]
    public TextMeshProUGUI guideTitleText;
    public TextMeshProUGUI guideInstructionText;
    public GameObject guidePanel; // 유저 시선을 따라다니는 패널

    [Header("Guide Settings")]
    public List<GuideStep> steps; // 각 단계별 정보 리스트
    private int currentStepIndex = 0;

    [System.Serializable]
    public class GuideStep
    {
        public string title;
        public string instruction;
        public GameObject targetObject; // 하이라이트할 대상 부품
        public AudioClip voiceOver;     // 가이드 음성
    }

    void Awake() => Instance = this;

    void Start()
    {
        UpdateStep(0); // 첫 번째 단계 시작
    }

    public void StartGuide()
    {
        // 1. 초기 인덱스 설정
        currentStepIndex = 0;

        // 2. 가이드 UI 패널 활성화
        if(guidePanel != null)
        {
            guidePanel.SetActive(true);
        }

        // 3. 첫 번째 단계(index 0) 데이터 업데이트 및 출력
        //if(steps != null && steps.Count > 0)
        //{
        //    UpdateStep(currentStepIndex);
        //    Debug.Log("[가이드] 시나리오가 시작되었습니다. 첫 번째 단계로 이동합니다.");
        //}
        //else
        //{
        //    Debug.LogError("[가이드] 등록된 GuideStep이 없습니다! 리스트를 확인하세요.");
        //}
    }

    public void NextStep()
    {
        currentStepIndex++;
        if(currentStepIndex < steps.Count)
        {
            UpdateStep(currentStepIndex);
        }
        else
        {
            FinishGuide();
        }
    }

    private void UpdateStep(int index)
    {
        //var step = steps[index];
        //guideTitleText.text = step.title;
        //guideInstructionText.text = step.instruction;

        //// TTS 호출: 지시문 텍스트를 음성으로 읽어줌
        //if(TTSManager.Instance != null)
        //{
        //    TTSManager.Instance.Speak(step.instruction);
        //}

        //if(step.targetObject != null) HighlightObject(step.targetObject);
    }

    private void HighlightObject(GameObject obj)
    {
        // 부품에 Outline 효과를 주거나 화살표 아이콘을 띄우는 로직
        Debug.Log($"[가이드] 다음 목표: {obj.name}");
    }

    private void FinishGuide()
    {
        guideTitleText.text = "가이드 종료";
        guideInstructionText.text = "이제 실전 정비를 시작하세요!";
        Invoke("HidePanel", 3f);
    }

    private Coroutine hintCoroutine;

    public void ShowTemporaryHint(float duration)
    {
        // 이미 힌트가 표시 중이라면 기존 코루틴 중단
        if(hintCoroutine != null) StopCoroutine(hintCoroutine);

        hintCoroutine = StartCoroutine(HintRoutine(duration));
    }

    private IEnumerator HintRoutine(float duration)
    {
        // 1. 가이드 패널과 하이라이트 활성화
        guidePanel.SetActive(true);

        // 현재 단계의 대상 오브젝트가 있다면 하이라이트 켜기 (예: Outline 스크립트 활성화)
        var currentTarget = steps[currentStepIndex].targetObject;
        if(currentTarget != null)
        {
            // 대상 오브젝트의 외곽선이나 화살표 등을 활성화하는 로직
            SetHighlight(currentTarget, true);
        }

        // 2. 정해진 시간 동안 대기
        yield return new WaitForSeconds(duration);

        // 3. 다시 숨기기 (평가 모드일 때만)
        if(GameModeManager.Instance.currentMode == GameModeManager.PlayMode.Exam)
        {
            guidePanel.SetActive(false);
            if(currentTarget != null) SetHighlight(currentTarget, false);
        }

        hintCoroutine = null;
    }

    // 하이라이트 제어 보조 함수
    private void SetHighlight(GameObject obj, bool active)
    {
        // 여기에 사용 중인 하이라이트 에셋(예: Outline.cs)의 활성화 코드를 넣으세요.
        // 예: obj.GetComponent<Outline>().enabled = active;
        Debug.Log($"[힌트] {obj.name} 하이라이트: {active}");
    }

    public void JumpToStep(int index)
    {
        if(index < 0 || index >= steps.Count) return;

        currentStepIndex = index;

        // --- 부품 상태 복구 로직 시작 ---
        for(int i = 0; i < steps.Count; i++)
        {
            GameObject target = steps[i].targetObject;
            if(target == null) continue;

            InteractablePart part = target.GetComponent<InteractablePart>();
            if(part == null) continue;

            if(i < index)
            {
                // 현재 도달한 단계보다 이전 단계 부품들은 '완료(제거)' 상태로 강제 세팅
                // (예: 분해 시나리오라면 Removed, 조립 시나리오라면 Assembled)
                part.ForceSetState(InteractablePart.PartState.Detached);
            }
            else if(i == index)
            {
                // 현재 다시 시작해야 하는 단계의 부품은 '초기화'
                part.ResetPart();
            }
        }
        // --- 부품 상태 복구 로직 끝 ---

        UpdateStep(currentStepIndex);
    }
}