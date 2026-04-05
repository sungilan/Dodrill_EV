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
}

[Serializable]
public class ModuleConfigEntry
{
    public string moduleId;
    public ModuleConfig config = new();
}

[Serializable]
public class ModuleConfig
{
    public List<string> requiredItems = new();
    public string targetZoneId;
    public List<StringKVPair> extra = new();
    public string actionType;
    public string targetObjName;

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
