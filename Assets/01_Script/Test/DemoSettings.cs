// ============================================================
//  DemoSettings.cs — 내부 시연용 스위치 모음 (2026-11-20 16시 시연)
//
//  시연에서 보이면 곤란한 것들을 한곳에서 켜고 끈다. 값만 바꾸면 된다(true = 시연용으로 숨김/제한).
//  · HideGeneratedPartsInScenario  : 시나리오(자유 탈거가 아님)에서 런타임에 만든 부품 모형(인버터·정션 블록·MSD 위 상자 등)을 숨긴다
//  · HideSpecialToolsInFreeMode    : 자유 탈거에서 테스트용 특수 공구(Equip_Tool_*)를 스폰하지 않는다
//  · ScenarioListDemoOnly          : 로비 시나리오 목록·'혼자 하기' 시나리오 전환에 DemoScenarioIds 의 시나리오만 보인다
//  서버·클라이언트가 같은 코드를 쓰므로 값을 바꾸면 서버(RoomServer)와 클라이언트를 함께 재빌드해야 한다(특수 공구 스폰은 서버가 하므로).
// ============================================================
public static class DemoSettings
{
    /// <summary>시나리오에서 런타임 생성 부품 모형을 숨긴다(자유 탈거에서는 항상 보임)</summary>
    public static bool HideGeneratedPartsInScenario = true;

    /// <summary>자유 탈거 모드의 테스트용 특수 공구(prefabId 가 Equip_Tool_ 로 시작)를 스폰하지 않는다</summary>
    public static bool HideSpecialToolsInFreeMode = true;

    /// <summary>시나리오 목록에 DemoScenarioIds 만 보이게 한다</summary>
    public static bool ScenarioListDemoOnly = true;

    /// <summary>시연에 남길 시나리오 — 자유 탈거, 기존 고전압 배터리 시나리오</summary>
    public static readonly string[] DemoScenarioIds = { "ev_free_disassembly", "ev_p0aa6_battery_insulation" };

    /// <summary>이 시나리오를 목록에 보여도 되는가</summary>
    public static bool ScenarioAllowed(string scenarioId)
    {
        if (!ScenarioListDemoOnly) return true;
        if (string.IsNullOrEmpty(scenarioId)) return false;
        foreach (var id in DemoScenarioIds) if (id == scenarioId) return true;
        return false;
    }

    /// <summary>이 프리팹을 스폰하지 않을 것인가(자유 탈거 특수 공구 숨김)</summary>
    public static bool SkipSpawn(string prefabId, string scenarioId)
    {
        return HideSpecialToolsInFreeMode && scenarioId == "ev_free_disassembly"
               && !string.IsNullOrEmpty(prefabId) && prefabId.StartsWith("Equip_Tool_");
    }
}
