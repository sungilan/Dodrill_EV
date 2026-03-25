using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  EVModules.cs
//  ev_p0aa6_battery_insulation.json 의 12개 Task 모듈 전부 포함
//
//  ScenarioBootstrapper.RegisterModules() 에 추가할 것:
//
//    // ── EV 정비 모듈 ──────────────────────────────────────────
//    EVModules.RegisterAll(Registry);
//
//  또는 개별 등록:
//    Registry.Register(new InitialDiagnosisModule());
//    ... (12개)
// ============================================================


// ──────────────────────────────────────────────────────────────
//  Task[00] InitialDiagnosis
//  GDS_Tablet → OBD_Zone 에 가져오면 완료 (ScanDTC)
// ──────────────────────────────────────────────────────────────
public class InitialDiagnosisModule : GrabZoneTaskModule
{
    public override string ModuleId => "InitialDiagnosis";
}

// ──────────────────────────────────────────────────────────────
//  Task[01] LOTO_Gloves
//  InsulatedGloves → Player_Hand_Zone 에 가져오면 완료
//  (장갑 착용 = GloveItem.OnEquipped 가 InteractionEvents 발행)
// ──────────────────────────────────────────────────────────────
public class LOTO_GlovesModule : GrabZoneTaskModule
{
    public override string ModuleId => "LOTO_Gloves";
}

// ──────────────────────────────────────────────────────────────
//  Task[02] MSD_Removal
//  MSD_Lever 를 MSD_Port_Zone 밖으로 탈거하면 완료
//  InteractablePart(MSD_Lever).OnGrabEnd → Detached 시
//  InteractionEvents.FireZoneActivated("MSD_Port_Zone", "MSD_Lever")
// ──────────────────────────────────────────────────────────────
public class MSD_RemovalModule : GrabZoneTaskModule
{
    public override string ModuleId => "MSD_Removal";
}

// ──────────────────────────────────────────────────────────────
//  Task[03] DischargeWait
//  Task 시작 시 DischargeTimerUI 자동 실행
//  타이머 완료 → InteractionEvents.FireTaskConfirmed("DischargeWait") → 완료
//
//  [중요] ScenarioRunner는 서버에서 실행됨.
//  UI 활성화는 클라이언트에서 해야 하므로
//  ScenarioStateReceiver.OnTaskStateUpdated 이벤트를 DischargeTimerUI가 직접 구독.
// ──────────────────────────────────────────────────────────────
public class DischargeWaitModule : ConfirmTaskModule
{
    public override string ModuleId => "DischargeWait";

    public override void OnStart(ModuleConfig config, Action onComplete, Action onFail)
    {
        base.OnStart(config, onComplete, onFail);
        // UI 활성화는 DischargeTimerUI가 ScenarioStateReceiver 이벤트로 처리
        // (서버에서 UI 직접 조작 불가 — 클라이언트 이벤트로 위임)
        UnityEngine.Debug.Log("[DischargeWait] 모듈 시작 — 클라이언트 타이머 대기 중");
    }
}

// ──────────────────────────────────────────────────────────────
//  Task[04] Voltage_Verification
//  TwoPoleTester → Inverter_Check_Point 존 진입
//  → HV_Cable_Term 단자에 멀티테스터 프로브 접촉 → 완료
// ──────────────────────────────────────────────────────────────
public class Voltage_VerificationModule : ZoneAndMeasureModule
{
    public override string ModuleId => "Voltage_Verification";
    protected override string GetTargetObjName() => "HV_Cable_Term";
}

// ──────────────────────────────────────────────────────────────
//  Task[05] BatteryPack_Disassemble
//  spawnObjects: ImpactWrench, BatteryJack
//  targetZoneId 없음 → PC에서 requiredItems 중 하나를 집으면 완료
//  (볼트 그룹 실제 분해는 InteractablePart로 처리, 지금은 도구 집기로 완료 판정)
// ──────────────────────────────────────────────────────────────
public class BatteryPack_DisassembleModule : ITaskModule
{
    public string ModuleId => "BatteryPack_Disassemble";

    private Action _onComplete;
    private Action _onFail;
    private bool   _completed;

    // JSON requiredItems: ["ImpactWrench", "BatteryJack"]
    private static readonly System.Collections.Generic.HashSet<string> _requiredItems
        = new() { "ImpactWrench", "BatteryJack" };

    public void OnStart(ModuleConfig config, Action onComplete, Action onFail)
    {
        _onComplete = onComplete;
        _onFail     = onFail;
        _completed  = false;

        InteractionEvents.OnItemGrabbed += HandleGrab;
        // BatteryPack_BoltGroup이 실제 InteractablePart로 탈거될 경우 대비
        InteractionEvents.OnZoneActivated += HandleZone;
    }

    private void HandleGrab(string itemId)
    {
        if (_completed) return;

        // config의 requiredItems 중 하나라도 집으면 완료
        if (!_requiredItems.Contains(itemId)) return;
        Complete();
    }

    private void HandleZone(string zoneId, string itemId)
    {
        if (_completed) return;
        // BatteryPack_BoltGroup이 존으로 등록된 경우 대비
        if (zoneId != "BatteryPack_BoltGroup" && itemId != "BatteryPack_BoltGroup") return;
        Complete();
    }

    private void Complete()
    {
        _completed = true;
        _onComplete?.Invoke();
    }

    public void OnUpdate(float deltaTime) { }
    public void OnComplete() { Cleanup(); }
    public void OnFail()    { Cleanup(); }

    private void Cleanup()
    {
        InteractionEvents.OnItemGrabbed   -= HandleGrab;
        InteractionEvents.OnZoneActivated -= HandleZone;
    }
}

// ──────────────────────────────────────────────────────────────
//  Task[06] Insulation_Measure
//  MultimeterKit → (targetZone 없음, 직접 Battery_Case 측정)
//  → Battery_Case 단자 접촉 → 완료
// ──────────────────────────────────────────────────────────────
public class Insulation_MeasureModule : ZoneAndMeasureModule
{
    public override string ModuleId => "Insulation_Measure";
    protected override string GetTargetObjName() => "Battery_Case";
}

// ──────────────────────────────────────────────────────────────
//  Task[07] Contamination_Clean
//  AirGun 들고 Busbar_A1에 도포 100% → 완료
//  GreaseTool.OnDosingComplete → FireZoneActivated("Busbar_A1", "AirGun")
// ──────────────────────────────────────────────────────────────
public class Contamination_CleanModule : ZoneAndFillModule
{
    public override string ModuleId => "Contamination_Clean";
    protected override string GetTargetObjName() => "Busbar_A1";
}

// ──────────────────────────────────────────────────────────────
//  Task[08] Gasket_Replace
//  SealGasket_New → Battery_Top_Zone 에 가져다 놓으면 완료
// ──────────────────────────────────────────────────────────────
public class Gasket_ReplaceModule : GrabZoneTaskModule
{
    public override string ModuleId => "Gasket_Replace";
}

// ──────────────────────────────────────────────────────────────
//  Task[09] BatteryPack_Assemble
//  BatteryPack_BoltGroup 재조립 (볼트 체결) 완료 시
//  DisassemblyManager.onAllPartsAssembled → InteractionEvents.FireTaskConfirmed("BatteryPack_Assemble")
// ──────────────────────────────────────────────────────────────
public class BatteryPack_AssembleModule : ConfirmTaskModule
{
    public override string ModuleId => "BatteryPack_Assemble";
}

// ──────────────────────────────────────────────────────────────
//  Task[10] MSD_Reinstall
//  MSD_Lever 를 MSD_Port_Zone 에 꽂으면 완료
// ──────────────────────────────────────────────────────────────
public class MSD_ReinstallModule : GrabZoneTaskModule
{
    public override string ModuleId => "MSD_Reinstall";
}

// ──────────────────────────────────────────────────────────────
//  Task[11] FinalDiagnosis
//  GDS_Tablet → OBD_Zone 재연결 + DTC 없음 확인 시 완료
// ──────────────────────────────────────────────────────────────
public class FinalDiagnosisModule : GrabZoneTaskModule
{
    public override string ModuleId => "FinalDiagnosis";
}

// ──────────────────────────────────────────────────────────────
//  일괄 등록 헬퍼
// ──────────────────────────────────────────────────────────────
public static class EVModules
{
    /// <summary>
    /// ScenarioBootstrapper.RegisterModules() 안에서 호출:
    ///   EVModules.RegisterAll(Registry);
    /// </summary>
    public static void RegisterAll(ModuleRegistry registry)
    {
        registry.Register(new InitialDiagnosisModule());
        registry.Register(new LOTO_GlovesModule());
        registry.Register(new MSD_RemovalModule());
        registry.Register(new DischargeWaitModule());
        registry.Register(new Voltage_VerificationModule());
        registry.Register(new BatteryPack_DisassembleModule());
        registry.Register(new Insulation_MeasureModule());
        registry.Register(new Contamination_CleanModule());
        registry.Register(new Gasket_ReplaceModule());
        registry.Register(new BatteryPack_AssembleModule());
        registry.Register(new MSD_ReinstallModule());
        registry.Register(new FinalDiagnosisModule());

        Debug.Log("[EVModules] P0AA6 시나리오 12개 모듈 등록 완료");
    }
}
