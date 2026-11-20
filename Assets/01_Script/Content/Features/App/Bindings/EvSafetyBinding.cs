using UnityEngine;

// ============================================================
//  EvSafetyBinding.cs — EV 콘텐츠가 안전 모듈(SafetyHooks)에 자기 기능을 등록하는 곳 (모듈화 ③, 2026-11-20)
//
//  안전 모듈이 EV 클래스(FreeLoto·EvaluationClientUI·DischargeTimerUI·TaskProgressUI·HoverNameFormatter)를
//  직접 부르던 것을 이 파일 한 곳으로 모았다. 씬이 로드되기 전에 한 번 실행된다.
// ============================================================
public static class EvSafetyBinding
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        SafetyHooks.IsPracticeScenario = id => id == FreeLoto.FreeScenarioId;
        SafetyHooks.IsFreePractice = () => FreeLoto.IsFreePractice;
        SafetyHooks.IsResultShowing = () => EvaluationClientUI.IsResultShowing;
        SafetyHooks.LotoLocked = () => DischargeTimerUI.Instance?.StartDischarge();
        SafetyHooks.Toast = DoDrill.Training.TaskProgressUI.RequestToast;
        SafetyHooks.DisplayName = HoverNameFormatter.Format;
    }
}
