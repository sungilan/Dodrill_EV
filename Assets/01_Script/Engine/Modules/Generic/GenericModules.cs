using System;
using System.Globalization;
using FishNet;
using UnityEngine;
using Debug = UnityEngine.Debug;

// ============================================================
//  GenericModules.cs — JSON 만으로 쓰는 범용 단계 모듈 (모듈화, 2026-11-20)
//
//  시나리오 JSON 의 modules[].config.type 에 아래 종류를 적으면 C# 코드 없이 단계가 돈다.
//  엔진(ScenarioBootstrapper)이 항상 등록하므로 콘텐츠 바인딩이 없는 새 프로젝트에서도 쓸 수 있다.
//  같은 moduleId 로 C# 모듈이 등록돼 있으면 그쪽이 우선한다(EV 기존 모듈은 그대로).
//
//  type        완료 조건                                    쓰는 설정
//  ----------  -------------------------------------------  ------------------------------------------
//  Confirm     대상 오브젝트 클릭 또는 확인 버튼             targetObjName (비우면 확인 버튼만)
//  GrabToZone  아이템을 들고 목표 구역에 닿기(놓기)          targetZoneId, requiredItems
//  UseItem     아이템을 들고 사용(트리거)                    requiredItems
//  GrabAll     필요한 아이템을 모두 집기                     requiredItems, extra "perPlayer"=true(사람마다)
//  Measure     측정 신호의 값이 기준 안                      targetObjName(단자), targetValue·toleranceRatio 또는 maxValue,
//                                                            targetZoneId(선택, 안내 구역)
//  Wait        정해진 시간이 지나면                          extra "seconds"
// ============================================================
public static class GenericModules
{
    public const string Confirm    = "Confirm";
    public const string GrabToZone = "GrabToZone";
    public const string UseItem    = "UseItem";
    public const string GrabAll    = "GrabAll";
    public const string Measure    = "Measure";
    public const string Wait       = "Wait";

    public static void RegisterTypes(ModuleRegistry registry)
    {
        registry.RegisterType(Confirm,    (id, cfg) => new GenericConfirmModule(id));
        registry.RegisterType(GrabToZone, (id, cfg) => new GenericGrabToZoneModule(id));
        registry.RegisterType(UseItem,    (id, cfg) => new GenericUseItemModule(id));
        registry.RegisterType(GrabAll,    (id, cfg) => new GenericGrabAllModule(id, ExtraBool(cfg, "perPlayer")));
        registry.RegisterType(Measure,    (id, cfg) => new GenericMeasureModule(id));
        registry.RegisterType(Wait,       (id, cfg) => new GenericWaitModule(id));
    }

    // ── extra 읽기 도우미 ─────────────────────────────
    public static string ExtraStr(ModuleConfig cfg, string key, string def = "")
    {
        if (cfg == null) return def;
        string v = cfg.GetExtraRaw(key);
        return string.IsNullOrEmpty(v) ? def : v;
    }

    public static float ExtraFloat(ModuleConfig cfg, string key, float def)
    {
        string v = ExtraStr(cfg, key);
        return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : def;
    }

    public static bool ExtraBool(ModuleConfig cfg, string key, bool def = false)
    {
        string v = ExtraStr(cfg, key);
        if (string.IsNullOrEmpty(v)) return def;
        return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>클라이언트 없는 전용 서버인가(구역 안내 표시는 화면이 있는 쪽만)</summary>
    internal static bool IsServerOnly => InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted;
}

/// <summary>대상 클릭·확인 버튼으로 완료 (type "Confirm")</summary>
public sealed class GenericConfirmModule : ConfirmTaskModule
{
    private readonly string _id;
    public GenericConfirmModule(string moduleId) { _id = moduleId; }
    public override string ModuleId => _id;
}

/// <summary>아이템을 목표 구역에 가져다 대면 완료 (type "GrabToZone")</summary>
public sealed class GenericGrabToZoneModule : GrabZoneTaskModule
{
    private readonly string _id;
    public GenericGrabToZoneModule(string moduleId) { _id = moduleId; }
    public override string ModuleId => _id;
}

/// <summary>아이템 사용으로 완료 (type "UseItem")</summary>
public sealed class GenericUseItemModule : GrabUseTaskModule
{
    private readonly string _id;
    public GenericUseItemModule(string moduleId) { _id = moduleId; }
    public override string ModuleId => _id;
}

/// <summary>필요 아이템을 모두 집으면 완료 (type "GrabAll", extra "perPlayer" 로 사람마다)</summary>
public sealed class GenericGrabAllModule : GrabAllItemsModule
{
    private readonly string _id;
    private readonly bool _perPlayer;
    public GenericGrabAllModule(string moduleId, bool perPlayer) { _id = moduleId; _perPlayer = perPlayer; }
    public override string ModuleId => _id;
    public override bool PerPlayer => _perPlayer;
}

/// <summary>
/// 측정값으로 완료 (type "Measure"). 판정만 하고 완료 처리(딜레이 extra "snapDelay")는 ScenarioRunner 가 한다.
///  · maxValue &gt; 0 → 측정값 ≤ maxValue
///  · 그 외        → |측정값 − targetValue| ≤ 허용오차 (targetValue × toleranceRatio, extra "tolerance" 가 있으면 그 값)
/// 단자는 targetObjName(비우면 아무 단자나).
/// </summary>
public sealed class GenericMeasureModule : IMeasureTaskModule
{
    private readonly string _id;
    private string _terminal, _zoneId;
    private float _target, _tolerance, _max;
    private bool _completed;

    public GenericMeasureModule(string moduleId) { _id = moduleId; }
    public string ModuleId => _id;
    public bool IsCompleted => _completed;

    public void OnStart(ModuleConfig config, Action onComplete, Action onFail)
    {
        _terminal  = config?.targetObjName ?? string.Empty;
        _zoneId    = config?.targetZoneId ?? string.Empty;
        _target    = config?.targetValue ?? 0f;
        _max       = config?.maxValue ?? 0f;
        _tolerance = GenericModules.ExtraFloat(config, "tolerance",
                        Mathf.Abs(_target) * (config?.toleranceRatio ?? 0.05f));
        _completed = false;

        if (!string.IsNullOrEmpty(_zoneId) && !GenericModules.IsServerOnly)
            TaskInteractionZone.Find(_zoneId)?.Activate();

        Debug.Log($"[{_id}] 측정 시작 — 단자:{(string.IsNullOrEmpty(_terminal) ? "(아무거나)" : _terminal)} / " +
                  (_max > 0f ? $"≤ {_max}" : $"{_target} ± {_tolerance}"));
    }

    public void HandleValueMeasured(string terminalId, float value)
    {
        if (_completed) return;
        if (!string.IsNullOrEmpty(_terminal) && !string.Equals(terminalId, _terminal, StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log($"[{_id}] 단자 불일치 — {terminalId} (기대 {_terminal})");
            return;
        }

        bool ok = _max > 0f ? value <= _max : Mathf.Abs(value - _target) <= _tolerance;
        Debug.Log($"[{_id}] 측정 {value:F2} → {(ok ? "합격" : "불합격")}");
        if (ok) _completed = true;
    }

    public void OnUpdate(float deltaTime) { }
    public void OnComplete() => Cleanup();
    public void OnFail() => Cleanup();

    private void Cleanup()
    {
        if (!string.IsNullOrEmpty(_zoneId) && !GenericModules.IsServerOnly)
            TaskInteractionZone.Find(_zoneId)?.Deactivate();
    }
}

/// <summary>정해진 시간 뒤 자동 완료 (type "Wait", extra "seconds", 기본 3초)</summary>
public sealed class GenericWaitModule : ITaskModule
{
    private readonly string _id;
    private float _left;
    private Action _onComplete;
    private bool _done;

    public GenericWaitModule(string moduleId) { _id = moduleId; }
    public string ModuleId => _id;

    public void OnStart(ModuleConfig config, Action onComplete, Action onFail)
    {
        _left = Mathf.Max(0f, GenericModules.ExtraFloat(config, "seconds", 3f));
        _onComplete = onComplete;
        _done = false;
    }

    public void OnUpdate(float deltaTime)
    {
        if (_done) return;
        _left -= deltaTime;
        if (_left > 0f) return;
        _done = true;
        _onComplete?.Invoke();
    }

    public void OnComplete() { }
    public void OnFail() { }
}
