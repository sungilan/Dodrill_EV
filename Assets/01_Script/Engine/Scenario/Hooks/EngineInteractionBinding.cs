using UnityEngine;

// ============================================================
//  EngineInteractionBinding.cs — 시나리오 엔진이 인터랙션 코어(InteractionEvents)에 "현재 단계 기준 구역 유효성" 판정을 등록 (모듈화 2단계, 2026-11-20)
//  TaskInteractionZone 이 엔진(ScenarioStateReceiver)을 직접 부르던 것을 이 파일로 옮겼다. 씬 로드 전에 한 번 실행된다.
// ============================================================
public static class EngineInteractionBinding
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        InteractionEvents.ZoneValidator = IsCurrentTaskValid;
    }

    private static bool IsCurrentTaskValid(string zoneId, string itemId)
    {
        var receiver = Object.FindAnyObjectByType<ScenarioStateReceiver>();
        if(receiver == null) return true;

        var config = receiver.GetCurrentTaskConfig();
        if(config == null) return true;

        if(!string.IsNullOrEmpty(config.targetZoneId) && config.targetZoneId != zoneId) return false;

        if(config.requiredItems != null && config.requiredItems.Count > 0)
            return config.requiredItems.Contains(itemId);

        return true;
    }
}
