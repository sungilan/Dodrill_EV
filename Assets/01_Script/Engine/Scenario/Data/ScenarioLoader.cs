using UnityEngine;
using Debug = UnityEngine.Debug;
using Newtonsoft.Json;

// ============================================================
//  ScenarioLoader.cs
//  서버/클라이언트 공용 — Resources 폴더에서 JSON 로드
//
//  [폴더 구조]
//    Assets/Resources/Scenarios/
//      1_vital_signs_complex.json
//      2_spo2_ekg_monitor.json
//      ...
//
//  StreamingAssets 불필요 — 플랫폼 무관하게 동작
// ============================================================

public static class ScenarioLoader
{
    private const string ResourcePath = "Scenarios";

    /// <summary>
    /// scenarioId 로 Resources/Scenarios/ 에서 JSON 을 찾아 ScenarioData 로 역직렬화
    /// </summary>
    public static ScenarioData Load(string scenarioId)
    {
        string path = $"{ResourcePath}/{scenarioId}";
        var asset = Resources.Load<TextAsset>(path);

        if (asset == null)
        {
            Debug.LogError($"[ScenarioLoader] 파일 없음: Resources/{path}");
            return null;
        }

        try
        {
            var data = JsonConvert.DeserializeObject<ScenarioData>(asset.text);

            if (data == null)
            {
                Debug.LogError($"[ScenarioLoader] 역직렬화 실패 (null 반환): {path}");
                return null;
            }

            Debug.Log($"[ScenarioLoader] 로드 완료 — {data.scenarioName} (v{data.version}), Tasks: {data.scenario?.tasks?.Count}");
            return data;
        }
        catch (JsonException e)
        {
            Debug.LogError($"[ScenarioLoader] JSON 파싱 오류: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 로드된 ScenarioData 유효성 검사
    /// </summary>
    public static bool Validate(ScenarioData data)
    {
        if (data == null) return Fail("data is null");
        if (string.IsNullOrEmpty(data.scenarioId)) return Fail("scenarioId 없음");
        if (data.scenario == null) return Fail("scenario 없음");
        if (data.scenario.tasks == null || data.scenario.tasks.Count == 0)
            return Fail("tasks 없음");

        foreach (var task in data.scenario.tasks)
        {
            if (string.IsNullOrEmpty(task.moduleId)) return Fail("moduleId 없는 task 존재");
            if (string.IsNullOrEmpty(task.completionMode)) return Fail($"{task.moduleId}: completionMode 없음");
        }

        Debug.Log($"[ScenarioLoader] Validate 통과 — {data.scenarioId}");
        return true;
    }

    private static bool Fail(string reason)
    {
        Debug.LogError($"[ScenarioLoader] 유효성 검사 실패: {reason}");
        return false;
    }
}