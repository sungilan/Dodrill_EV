using System;
using System.Collections.Generic;
using UnityEngine;
using Debug = UnityEngine.Debug;

// ============================================================
//  ScenarioData.cs
//  JSON 역직렬화 + FishNet Broadcast 직렬화 겸용 데이터 구조체
// ============================================================

[Serializable]
public struct StringKVPair
{
    public string key;
    public string ko;
    public string en;
    public string vi;

    public string GetLocalized()
    {
        string lang = LocalizationManager.CurrentLanguage;
        return lang switch
        {
            "en" => string.IsNullOrEmpty(en) ? ko : en,
            "vi" => string.IsNullOrEmpty(vi) ? ko : vi,
            _ => ko,
        };
    }

    [Newtonsoft.Json.JsonProperty("value")]
    public string ValueCompat
    {
        set { if (string.IsNullOrEmpty(ko)) ko = value; }
    }
}

[Serializable]
public struct Vector3Data
{
    public float x, y, z;

    public Vector3 ToVector3() => new Vector3(x, y, z);
    public Quaternion ToQuaternion() => Quaternion.Euler(x, y, z);

    public static Vector3Data From(Vector3 v) => new Vector3Data { x = v.x, y = v.y, z = v.z };
}

[Serializable]
public class ScenarioData
{
    public string scenarioId;
    public LocalizedString scenarioName = new();
    public string version;
    public MapData map = new();
    public ScenarioDef scenario = new();
    public List<ModuleConfigEntry> modules = new();

    public ModuleConfig GetModuleConfig(string moduleId)
    {
        foreach (var entry in modules)
            if (entry.moduleId == moduleId) return entry.config;
        return null;
    }
}

[Serializable]
public class LocalizedString
{
    public string ko;
    public string en;
    public string vi;

    [Newtonsoft.Json.JsonConstructor]
    public LocalizedString() { }

    public LocalizedString(string koText) { ko = koText; }

    public string GetLocalized()
    {
        string lang = LocalizationManager.CurrentLanguage;
        return lang switch
        {
            "en" => string.IsNullOrEmpty(en) ? ko : en,
            "vi" => string.IsNullOrEmpty(vi) ? ko : vi,
            _ => ko,
        };
    }

    public static implicit operator LocalizedString(string s) => new LocalizedString(s);
    public override string ToString() => GetLocalized();
}

[Serializable]
public class MapData
{
    public List<MapObjectEntry> objects = new();
}

[Serializable]
public class MapObjectEntry
{
    public string prefabId;
    public Vector3Data position;
    public Vector3Data rotation;
    public Vector3Data scale;
    public List<StringKVPair> properties = new();
}

[Serializable]
public class ScenarioDef
{
    public List<TaskDef> tasks = new();
}

[Serializable]
public class TaskDef
{
    public string moduleId;
    public float timeout;
    public string completionMode;

    /// <summary>
    /// 이 Task 시작 시 스폰할 오브젝트 번들 목록
    /// 각 번들: 스폰 오브젝트 + 스폰 위치 + 가이드 오브젝트
    /// </summary>
    public List<SpawnBundle> spawnObjects = new();
    public List<string> despawnObjectIds;
}

/// <summary>
/// 스폰 오브젝트 + 가이드 오브젝트 번들
/// JSON 예시:
/// {
///   "prefabId": "HandSanitizer",
///   "spawnPointId": "SpawnPoint_Shelf_A",
///   "guideId": "Guide_HandHygieneZone"
/// }
/// </summary>
[Serializable]
public class SpawnBundle
{
    /// <summary>스폰할 NetworkObject prefab ID</summary>
    public string prefabId;

    /// <summary>씬의 SpawnPoint 컴포넌트 pointId와 매핑</summary>
    public string spawnPointId;

    /// <summary>씬의 SceneGuideRegistry guideId와 매핑. 없으면 빈 문자열</summary>
    public string guideId;

    /// <summary>
    /// 아이템을 가져다 놓을 타겟 존 GO 이름.
    /// GuideSystem이 이 이름으로 씬에서 GO를 찾아 황금색 화살표 + 고스트를 표시.
    /// </summary>
    public string targetGuideId;
    public bool showGuide = true;
    public bool persist = false;
    public bool attachToSpawnPoint = false;
    /// <summary>
    /// 접속 인원수만큼 스폰(여럿이 같은 단계를 동시에 작업하는 공구용). 임팩트 렌치는 이 값이 없어도 자동으로 인원수만큼 놓는다.
    /// </summary>
    public bool perPlayer = false;
}

[Serializable]
public class ModuleConfigEntry
{
    public string moduleId;
    public ModuleConfig config = new();
}

/// <summary>
/// 선임 정비사 대사 한 줄 — 시나리오 JSON 의 modules[].config.dialogueLines.
/// 간호술기 프로젝트의 대사 시스템과 같은 모양(id·speaker·ko/en/vi)이라 두드릴 서버(/dialogues)·에디터와 그대로 주고받는다.
/// trigger 가 비어 있으면 그 단계(moduleId)에 들어갈 때 순서대로 재생하고, 값이 있으면 같은 이름의 사건(MentorDialogue.Say)이 날 때 재생한다.
/// </summary>
[Serializable]
public class DialogueLineJson
{
    /// <summary>시나리오 안에서 고유한 순번 문자열("1","2",…) — 서버 dialogue_id 와 이 값으로 짝을 맞춘다</summary>
    [Newtonsoft.Json.JsonProperty("id", NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public string id = "";

    /// <summary>Mentor(선임 정비사) 또는 Trainee(나). 대소문자 무시</summary>
    public string speaker = "Mentor";

    /// <summary>비어 있지 않으면 해당 사건이 날 때만 재생(예: intro, hv_warn, accident_shock)</summary>
    public string trigger = "";

    /// <summary>선임 정비사 표정: neutral / praise / serious / warn / alarm / grave / sigh. 비면 trigger 에 맞는 기본 표정(MentorDialogue.DefaultEmotionFor)</summary>
    public string emotion = "";

    public string ko = "";
    public string en = "";
    public string vi = "";

    /// <summary>화면에 머무는 시간(초). 0 이면 글자 수로 자동</summary>
    public float displayDuration;

    /// <summary>
    /// 음성 파일 경로(간호술기와 같은 규칙): <c>res:폴더/이름</c> 또는 접두사 없음 = Resources(확장자 제외), <c>sa:상대경로.wav</c> = StreamingAssets.
    /// voiceResourcesPath 는 한국어 기본값이고, 언어별 경로(Ko/En/Vi)가 있으면 그것을 먼저 쓴다. 비면 음성 없음.
    /// </summary>
    public string voiceResourcesPath = "";
    public string voiceResourcesPathKo = "";
    public string voiceResourcesPathEn = "";
    public string voiceResourcesPathVi = "";
    public float volume = 1f;

    /// <summary>true 면 음성이 끝날 때까지 대사를 유지한다(기본)</summary>
    public bool waitForVoiceEnd = true;

    public string GetLocalizedVoicePath()
    {
        string lang = (LocalizationManager.CurrentLanguage ?? "ko").ToLowerInvariant();
        string ko = First(voiceResourcesPathKo, voiceResourcesPath), en = First(voiceResourcesPathEn), vi = First(voiceResourcesPathVi);
        if (lang.StartsWith("en")) return First(en, ko, vi);
        if (lang.StartsWith("vi")) return First(vi, ko, en);
        return First(ko, en, vi);
    }

    private static string First(params string[] v)
    {
        foreach (var s in v) if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
        return string.Empty;
    }

    public string GetLocalizedText()
    {
        string lang = (LocalizationManager.CurrentLanguage ?? "ko").ToLowerInvariant();
        if (lang.StartsWith("en")) return string.IsNullOrEmpty(en) ? ko : en;
        if (lang.StartsWith("vi")) return string.IsNullOrEmpty(vi) ? ko : vi;
        return ko;
    }
}

[Serializable]
public class ModuleConfig
{
    public List<string> requiredItems = new();

    /// <summary>선임 정비사 대사(MentorDialogue). FishNet 으로는 전달되지 않아 클라이언트가 로컬 JSON 에서 다시 읽는다.</summary>
    public List<DialogueLineJson> dialogueLines = new();
    public string targetZoneId;
    public List<StringKVPair> extra = new();
    public string actionType;
    public string targetObjName;

    public float targetValue = 0f;
    public float toleranceRatio = 0.05f;

    [Tooltip("0보다 크면 측정값이 이 값 이하일 때 합격 (예: 인버터 잔류 전압 30V 이하). targetValue 판정보다 우선한다.")]
    public float maxValue = 0f;

    public string GetExtra(string key)
    {
        foreach (var pair in extra)
            if (pair.key == key) return pair.GetLocalized();
        return string.Empty;
    }

    public string GetExtraRaw(string key)
    {
        foreach (var pair in extra)
            if (pair.key == key) return pair.ko;
        return string.Empty;
    }
}
