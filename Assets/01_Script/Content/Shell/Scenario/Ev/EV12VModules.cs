using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  EV12VModules.cs
//  ev_p0562_12v_battery_replace.json(12V 보조 배터리 교환) 전용 단계
//
//  완료 방법은 ConfirmTaskModule 과 같다.
//   · 차량의 ClickableAnimator(uniqueId = JSON targetObjName)를 조작하거나
//   · BoltGroupCounter(taskId = 이 모듈 ID)의 볼트를 전부 풀거나/조이면 완료
//     → 체결 단계의 볼트 그룹 taskId 에 "12V" 가 들어가므로 TorqueSpec 이 12V 규정 토크를 적용한다
//       (클램프 0.9~1.4 / 단자 0.8~1.0 / 센서 2.7~3.3 kgf·m)
//
//  순서 위반: 단계마다 '지금 만지면 안 되는 대상'을 정해 두고, 그걸 조작하면 PROC_ORDER 감점
//   · 분리: (−) → (+)   (+)를 먼저 풀면 공구가 차체에 닿아 단락될 수 있다
//   · 장착: (+) → (−)
// ============================================================

/// <summary>
/// 단계마다 '지금 만지면 안 되는 대상'을 정해 두고, 그 대상을 조작하면 평가 규칙(기본 PROC_ORDER)으로 감점하는 확인 단계.
/// 12V 배터리 교환·냉각수 교환 시나리오가 함께 쓴다.
/// </summary>
public abstract class OrderGuardedModule : ConfirmTaskModule
{
    /// <summary>이 단계에서 먼저 만지면 순서 위반인 대상과 그 이유</summary>
    protected virtual IEnumerable<(string id, string why)> Forbidden => Array.Empty<(string, string)>();

    /// <summary>위반 시 기록할 평가 규칙 ID (evaluation_rules.json)</summary>
    protected virtual string ViolationRuleId => "PROC_ORDER";

    private bool _guarding;

    public override void OnStart(ModuleConfig config, Action onComplete, Action onFail)
    {
        base.OnStart(config, onComplete, onFail);
        InteractionEvents.OnZoneActivated += CheckOrder;
        _guarding = true;
    }

    public override void OnComplete() { StopGuard(); base.OnComplete(); }
    public override void OnFail() { StopGuard(); base.OnFail(); }

    private void StopGuard()
    {
        if (!_guarding) return;
        _guarding = false;
        InteractionEvents.OnZoneActivated -= CheckOrder;
    }

    private void CheckOrder(string id, string itemId)
    {
        if (string.IsNullOrEmpty(id) || id == _targetObjName) return;
        foreach (var (fid, why) in Forbidden)
        {
            if (id != fid) continue;
            var runner = UnityEngine.Object.FindFirstObjectByType<ScenarioRunner>();
            if (runner != null) runner.RecordObjectViolation(id, ViolationRuleId, $"{ModuleId}: {why}");
            Debug.LogWarning($"[{ModuleId}] 순서 위반 — {why}");
            return;
        }
    }
}

public abstract class Battery12VStepModule : OrderGuardedModule
{
    // 차량 쪽 12V 부품 오브젝트 ID (ClickableAnimator uniqueId / 볼트 그룹 이름)
    public const string NegTerminal = "12V_Terminal_Neg";
    public const string PosTerminal = "12V_Terminal_Pos";
    public const string Sensor = "12V_Battery_Sensor";
    public const string Clamp = "12V_Battery_Clamp";
}

// ── 분리 ─────────────────────────────────────────────────────

public class Battery_12V_Neg_DisconnectModule : Battery12VStepModule
{
    public override string ModuleId => "Battery_12V_Neg_Disconnect";
    protected override IEnumerable<(string, string)> Forbidden => new[]
    {
        (PosTerminal, "(−) 단자보다 (+) 단자를 먼저 분리"),
        (Clamp, "단자 분리 전에 클램프 탈거"),
    };
}

public class Battery_12V_Pos_DisconnectModule : Battery12VStepModule
{
    public override string ModuleId => "Battery_12V_Pos_Disconnect";
    protected override IEnumerable<(string, string)> Forbidden => new[]
    {
        (Clamp, "(+) 단자 분리 전에 클램프 탈거"),
    };
}

public class Battery_12V_Sensor_DisconnectModule : Battery12VStepModule
{
    public override string ModuleId => "Battery_12V_Sensor_Disconnect";
}

public class Battery_12V_Clamp_RemovalModule : Battery12VStepModule
{
    public override string ModuleId => "Battery_12V_Clamp_Removal";
}

// ── 교체 ─────────────────────────────────────────────────────

/// <summary>기존 배터리를 들어 내려놓는다(아이템을 잡아 존에 가져가면 완료)</summary>
public class Battery_12V_RemovalModule : GrabZoneTaskModule
{
    public override string ModuleId => "Battery_12V_Removal";
}

/// <summary>새 배터리를 트레이에 올린다</summary>
public class Battery_12V_InstallModule : GrabZoneTaskModule
{
    public override string ModuleId => "Battery_12V_Install";
}

// ── 장착 ─────────────────────────────────────────────────────

public class Battery_12V_Clamp_InstallModule : Battery12VStepModule
{
    public override string ModuleId => "Battery_12V_Clamp_Install";
    protected override IEnumerable<(string, string)> Forbidden => new[]
    {
        (PosTerminal, "배터리 고정(클램프) 전에 단자 연결"),
        (NegTerminal, "배터리 고정(클램프) 전에 단자 연결"),
    };
}

public class Battery_12V_Pos_ConnectModule : Battery12VStepModule
{
    public override string ModuleId => "Battery_12V_Pos_Connect";
    protected override IEnumerable<(string, string)> Forbidden => new[]
    {
        (NegTerminal, "(+) 단자보다 (−) 단자를 먼저 연결"),
    };
}

public class Battery_12V_Neg_ConnectModule : Battery12VStepModule
{
    public override string ModuleId => "Battery_12V_Neg_Connect";
}

public class Battery_12V_Sensor_ConnectModule : Battery12VStepModule
{
    public override string ModuleId => "Battery_12V_Sensor_Connect";
}

public class Battery_12V_Cover_InstallModule : ConfirmTaskModule
{
    public override string ModuleId => "Battery_12V_Cover_Install";
}

public class Vehicle_Hood_CloseModule : ConfirmTaskModule
{
    public override string ModuleId => "Vehicle_Hood_Close";
}

public static class EV12VModules
{
    public static void RegisterAll(ModuleRegistry registry)
    {
        registry.Register(new Battery_12V_Neg_DisconnectModule());
        registry.Register(new Battery_12V_Pos_DisconnectModule());
        registry.Register(new Battery_12V_Sensor_DisconnectModule());
        registry.Register(new Battery_12V_Clamp_RemovalModule());
        registry.Register(new Battery_12V_RemovalModule());
        registry.Register(new Battery_12V_InstallModule());
        registry.Register(new Battery_12V_Clamp_InstallModule());
        registry.Register(new Battery_12V_Pos_ConnectModule());
        registry.Register(new Battery_12V_Neg_ConnectModule());
        registry.Register(new Battery_12V_Sensor_ConnectModule());
        registry.Register(new Battery_12V_Cover_InstallModule());
        registry.Register(new Vehicle_Hood_CloseModule());
    }
}
