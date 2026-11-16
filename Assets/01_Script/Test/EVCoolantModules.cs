using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  EVCoolantModules.cs
//  ev_p0c73_coolant_replace.json(냉각수 교환 · 에어빼기) 전용 단계
//
//  흐름: 초기 진단(P0C73) → 장갑 → 후드 → 수위 확인 → 냉각 대기 → 리저버 캡 개방
//        → 리프트 상승 → 배출 받침 → 드레인 플러그 풀기 → (배출 대기) 플러그 잠그기 → 리프트 하강
//        → 냉각수 주입 → GDS 에어빼기 → 수위 보충 → 캡 닫기 → 후드 닫기 → 최종 진단
//
//  완료 방법은 ConfirmTaskModule 과 같다(ClickableAnimator uniqueId = JSON targetObjName).
//  예외: Coolant_Air_Bleed 는 GDS 태블릿 부가 기능 '냉각수 에어빼기'가 끝나면
//        태블릿이 InteractionEvents.FireTaskConfirmed("Coolant_Air_Bleed") 를 보낸다.
//
//  감점:
//   · 냉각 대기 전에 리저버 캡을 만지거나 대기 시간 전에 완료 → SAFE_HOT_COOLANT (뜨거운 냉각수 분출 · 화상)
//   · 배출이 끝나기 전에 드레인 플러그를 잠그려 함 → PROC_ORDER
//   · 에어빼기 중 캡을 닫음 → PROC_ORDER
// ============================================================

public static class CoolantIds
{
    public const string Reservoir = "Coolant_Reservoir";
    public const string Cap = "Coolant_Reservoir_Cap";
    public const string DrainPlug = "Coolant_Drain_Plug";
    public const string HotRule = "SAFE_HOT_COOLANT";
}

/// <summary>
/// 정해진 시간이 지나야 완료되는 단계. 시간은 서버가 직접 잰다(extra.waitSeconds).
/// 시간 전에 목표 오브젝트를 조작하거나 완료 버튼을 누르면 EarlyConfirmRuleId 로 감점.
/// </summary>
public abstract class TimedGuardedModule : OrderGuardedModule
{
    protected abstract float DefaultWaitSeconds { get; }
    protected abstract string EarlyWhy { get; }

    private float _earliest;
    private bool _listening;

    public override void OnStart(ModuleConfig config, Action onComplete, Action onFail)
    {
        base.OnStart(config, onComplete, onFail);
        _earliest = Time.time + DischargeTimerUI.GetWaitSeconds(config, DefaultWaitSeconds);
        InteractionEvents.OnZoneActivated += CheckEarly;
        _listening = true;
    }

    protected override bool CanConfirm() => Time.time >= _earliest;

    public override void OnComplete() { Stop(); base.OnComplete(); }
    public override void OnFail() { Stop(); base.OnFail(); }

    private void Stop()
    {
        if (!_listening) return;
        _listening = false;
        InteractionEvents.OnZoneActivated -= CheckEarly;
    }

    // 버튼 경로의 조기 완료는 ScenarioRunner 가 EarlyConfirmRuleId 로 감점한다. 여기서는 오브젝트 조작 경로만 본다.
    private void CheckEarly(string id, string itemId)
    {
        if (string.IsNullOrEmpty(_targetObjName) || id != _targetObjName || CanConfirm()) return;
        var runner = UnityEngine.Object.FindFirstObjectByType<ScenarioRunner>();
        if (runner != null && !string.IsNullOrEmpty(EarlyConfirmRuleId))
            runner.RecordObjectViolation(id, EarlyConfirmRuleId, $"{ModuleId}: {EarlyWhy}");
        Debug.LogWarning($"[{ModuleId}] 대기 시간 전 조작 — {EarlyWhy}");
    }
}

// ── 점검 · 준비 ──────────────────────────────────────────────

/// <summary>리저버 탱크 수위 확인(캡은 열지 않는다)</summary>
public class Coolant_Level_CheckModule : OrderGuardedModule
{
    public override string ModuleId => "Coolant_Level_Check";
    protected override string ViolationRuleId => CoolantIds.HotRule;
    protected override IEnumerable<(string, string)> Forbidden => new[]
    {
        (CoolantIds.Cap, "냉각수가 뜨거운 상태에서 리저버 캡 개방"),
    };
}

/// <summary>냉각수가 식을 때까지 대기 — GDS 서비스 데이터의 냉각수 온도로 확인</summary>
public class Coolant_Cool_DownModule : TimedGuardedModule
{
    public override string ModuleId => "Coolant_Cool_Down";
    protected override float DefaultWaitSeconds => 60f;
    protected override string EarlyWhy => "냉각수가 식기 전에 완료";
    public override string EarlyConfirmRuleId => CoolantIds.HotRule;
    protected override string ViolationRuleId => CoolantIds.HotRule;
    protected override IEnumerable<(string, string)> Forbidden => new[]
    {
        (CoolantIds.Cap, "냉각수가 식기 전에 리저버 캡 개방"),
    };
}

public class Coolant_Cap_OpenModule : OrderGuardedModule
{
    public override string ModuleId => "Coolant_Cap_Open";
}

// ── 배출 ─────────────────────────────────────────────────────

public class Car_Lift_DownModule : ConfirmTaskModule
{
    public override string ModuleId => "Car_Lift_Down";
}

/// <summary>배출 받침(드레인 팬)을 드레인 플러그 아래에 놓는다</summary>
public class Coolant_Drain_Pan_PlaceModule : GrabZoneTaskModule
{
    public override string ModuleId => "Coolant_Drain_Pan_Place";
}

public class Coolant_Drain_Plug_OpenModule : OrderGuardedModule
{
    public override string ModuleId => "Coolant_Drain_Plug_Open";
}

/// <summary>냉각수가 다 빠질 때까지 기다렸다가 드레인 플러그를 잠근다</summary>
public class Coolant_Drain_Plug_CloseModule : TimedGuardedModule
{
    public override string ModuleId => "Coolant_Drain_Plug_Close";
    protected override float DefaultWaitSeconds => 30f;
    protected override string EarlyWhy => "냉각수 배출이 끝나기 전에 드레인 플러그 체결";
    public override string EarlyConfirmRuleId => "PROC_ORDER";
}

// ── 주입 · 에어빼기 ──────────────────────────────────────────

/// <summary>전기차 전용(저전도) 냉각수 통을 리저버 주입구에 가져가면 완료</summary>
public class Coolant_FillModule : GrabZoneTaskModule
{
    public override string ModuleId => "Coolant_Fill";
}

/// <summary>GDS 부가 기능 '냉각수 에어빼기' 완료 신호로 끝난다</summary>
public class Coolant_Air_BleedModule : OrderGuardedModule
{
    public override string ModuleId => "Coolant_Air_Bleed";
    protected override IEnumerable<(string, string)> Forbidden => new[]
    {
        (CoolantIds.Cap, "에어빼기 중 리저버 캡을 닫음 — 회로 안의 공기가 빠지지 않는다"),
    };
}

/// <summary>에어빼기로 내려간 수위를 MAX 선까지 보충</summary>
public class Coolant_Top_UpModule : OrderGuardedModule
{
    public override string ModuleId => "Coolant_Top_Up";
    protected override IEnumerable<(string, string)> Forbidden => new[]
    {
        (CoolantIds.Cap, "수위 보충 전에 리저버 캡을 닫음"),
    };
}

public class Coolant_Cap_CloseModule : OrderGuardedModule
{
    public override string ModuleId => "Coolant_Cap_Close";
}

public static class EVCoolantModules
{
    public static void RegisterAll(ModuleRegistry registry)
    {
        registry.Register(new Coolant_Level_CheckModule());
        registry.Register(new Coolant_Cool_DownModule());
        registry.Register(new Coolant_Cap_OpenModule());
        registry.Register(new Car_Lift_DownModule());
        registry.Register(new Coolant_Drain_Pan_PlaceModule());
        registry.Register(new Coolant_Drain_Plug_OpenModule());
        registry.Register(new Coolant_Drain_Plug_CloseModule());
        registry.Register(new Coolant_FillModule());
        registry.Register(new Coolant_Air_BleedModule());
        registry.Register(new Coolant_Top_UpModule());
        registry.Register(new Coolant_Cap_CloseModule());
    }
}
