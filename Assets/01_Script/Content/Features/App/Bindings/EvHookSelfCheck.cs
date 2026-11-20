using UnityEngine;

// ============================================================
//  EvHookSelfCheck.cs — 앱이 시작한 직후, EV 가 훅을 다 등록했는지 점검한다 (11/20)
//
//  Ev*Binding(씬 로드 전)이 실행돼야 아래층(엔진·아바타·인터랙션)이 EV 기능을 쓸 수 있다.
//  어셈블리가 빌드에서 빠졌거나 바인딩이 안 불리면 오류 없이 '기능이 사라지므로' 여기서 한 번 로그로 확인한다.
//  결과는 로그의 [HookCheck:EV] 줄 — 정상이면 "훅 N개 모두 등록됨", 아니면 빠진 훅 이름이 나온다.
// ============================================================
public static class EvHookSelfCheck
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Check()
    {
        HookRegistry.Verify("EV",
            new[] { typeof(ScenarioHooks), typeof(AvatarHooks), typeof(GrabHooks), typeof(InteractionEvents), typeof(VrUi) },
            new[]
            {
                new HookRegistry.Critical("시나리오 모듈 공급자(EV)", () => ScenarioHooks.ProviderCount >= 1),
            });
    }
}
