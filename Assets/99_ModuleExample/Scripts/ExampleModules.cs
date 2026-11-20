using System;
using UnityEngine;

// ============================================================
//  ExampleModules.cs — 최소 예제용 단계 모듈 2개 (모듈화 적용 가이드, 2026-11-20)
//
//  새 프로젝트에서 단계(Task)를 만들 때의 최소 모양: ITaskModule 을 구현하고 ModuleId 를 JSON moduleId 와 맞춘다.
//  모듈은 서버에서만 돈다. 완료 조건이 되면 onComplete 를 부른다(ScenarioRunner 가 다음 단계로 넘긴다).
//  설정값은 시나리오 JSON 의 modules[].config.extra (key/ko) 로 받는다.
// ============================================================

/// <summary>정해진 시간이 지나면 완료 — extra "seconds"</summary>
public class ExampleWaitModule : ITaskModule
{
    public string ModuleId => "Example_Wait";

    private float _left;
    private Action _onComplete;

    public void OnStart(ModuleConfig config, Action onComplete, Action onFail)
    {
        _onComplete = onComplete;
        _left = ExampleConfig.Float(config, "seconds", 3f);
        Debug.Log($"[Example] 대기 단계 시작 — {_left:F0}초 뒤 자동 완료");
    }

    public void OnUpdate(float deltaTime)
    {
        if (_onComplete == null) return;
        _left -= deltaTime;
        if (_left > 0f) return;
        var done = _onComplete; _onComplete = null;
        done();
    }

    public void OnComplete() => Debug.Log("[Example] 대기 단계 완료");
    public void OnFail() { _onComplete = null; }
}

/// <summary>키를 누르면 완료 — extra "key" (KeyCode 이름, 기본 Space). 호스트(서버+클라이언트 한 몸)에서 확인하는 예제</summary>
public class ExamplePressKeyModule : ITaskModule
{
    public string ModuleId => "Example_PressKey";

    private KeyCode _key;
    private Action _onComplete;

    public void OnStart(ModuleConfig config, Action onComplete, Action onFail)
    {
        _onComplete = onComplete;
        string name = ExampleConfig.Str(config, "key", "Space");
        if (!Enum.TryParse(name, true, out _key)) _key = KeyCode.Space;
        Debug.Log($"[Example] 키 입력 단계 시작 — {_key} 키를 누르세요");
    }

    public void OnUpdate(float deltaTime)
    {
        if (_onComplete == null || !Input.GetKeyDown(_key)) return;
        var done = _onComplete; _onComplete = null;
        done();
    }

    public void OnComplete() => Debug.Log("[Example] 키 입력 단계 완료");
    public void OnFail() { _onComplete = null; }
}

/// <summary>config.extra 값 읽기 도우미</summary>
public static class ExampleConfig
{
    public static string Str(ModuleConfig config, string key, string fallback)
    {
        if (config?.extra == null) return fallback;
        foreach (var kv in config.extra)
            if (kv.key == key && !string.IsNullOrEmpty(kv.ko)) return kv.ko;
        return fallback;
    }

    public static float Float(ModuleConfig config, string key, float fallback)
        => float.TryParse(Str(config, key, null), System.Globalization.NumberStyles.Float,
                          System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;
}
