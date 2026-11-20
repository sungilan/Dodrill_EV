using UnityEngine;

// ============================================================
//  EvUiBinding.cs — EV 콘텐츠가 UI 모듈(UiHooks)에 자기 기능을 등록하는 곳 (모듈화 ⑤, 2026-11-20)
//  씬이 로드되기 전에 한 번 실행된다.
// ============================================================
public static class EvUiBinding
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        UiHooks.DisplayName = HoverNameFormatter.Format;
    }
}
