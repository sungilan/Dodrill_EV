using DG.Tweening;
using System;
using System.Collections.Generic;
using UnityEngine;
using static BatteryJack_BaseModule;

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
//  Task[01] InsulationFaultCheck
//  GDS 부가기능 "절연파괴 검출" 실행 → 파괴 부위 특정
//  GDS_Tablet을 OBD_Zone에 가져다 부가기능 버튼 사용
// ──────────────────────────────────────────────────────────────
public class InsulationFaultCheckModule : GrabZoneTaskModule
{
    public override string ModuleId => "InsulationFaultCheck";
}

// ──────────────────────────────────────────────────────────────
//  Task[01] LOTO_Gloves
//  InsulatedGloves → Player_Hand_Zone 에 가져오면 완료
//  (장갑 착용 = GloveItem.OnEquipped 가 InteractionEvents 발행)
// ──────────────────────────────────────────────────────────────
public class LOTO_GlovesModule : GrabAllItemsModule
{
    public override string ModuleId => "LOTO_Gloves";
}

public class PRA_RemovalModule : GrabAllItemsModule
{
    public override string ModuleId => "PRA_Removal";
}

public class PRA_ReplaceModule : GrabZoneTaskModule
{
    public override string ModuleId => "PRA_Replace";
}

public class LOTO_WarningSignModule : GrabZoneTaskModule
{
    public override string ModuleId => "LOTO_WarningSign";

    protected override void OnModuleSuccess(string itemId)
    {
        // 1. 시스템이 존을 끄기 전에 먼저 실행되거나, 경로를 직접 참조해야 합니다.
        // 만약 FinalModel을 존 외부에 두었다면 아래와 같이 찾습니다.
        GameObject finalModelObj = GameObject.Find("Final_WarningSign_Model");

        if(finalModelObj != null)
        {
            finalModelObj.SetActive(true);
            finalModelObj.transform.DOPunchScale(Vector3.one * 0.15f, 0.4f);
            Debug.Log("<color=lime>표지판 모델 활성화 완료</color>");
        }
    }
}

public class CarLiftUpStepModule : Car_Lift_UpModule
{
    // 클래스 이름이 다르므로 충돌하지 않습니다.
    public override string ModuleId => "Car_Lift_Up";
}

// 1. 배터리 잭 상승 모듈
public class BatteryJack_Lift_UpModule : GrabZoneTaskModule
{
    public override string ModuleId => "BatteryJack_Lift_Up";
}

// 2. 배터리 잭 하강 모듈
public class BatteryJack_LoweringModule : BatteryJack_BaseModule
{
    public override string ModuleId => "BatteryJack_Lowering";

    public override void OnUpdate(float deltaTime)
    {
        if(_isCompleted || _jack == null) return;

        // 목표 높이 '이하'로 내려갔을 때 완료 (보통 바닥 0.1m)
        if(_jack.CurrentHeight <= _config.targetValue)
        {
            _isCompleted = true;
            Debug.Log($"<color=lime>[{ModuleId}] 하강 완료!</color> 최종 높이: {_jack.CurrentHeight:F2}m");
            _onComplete?.Invoke();
        }
    }
}

// ──────────────────────────────────────────────────────────────
//  Task[02] MSD_Removal
//  MSD_Lever 를 MSD_Port_Zone 밖으로 탈거하면 완료
//  InteractablePart(MSD_Lever).OnGrabEnd → Detached 시
//  InteractionEvents.FireZoneActivated("MSD_Port_Zone", "MSD_Lever")
// ──────────────────────────────────────────────────────────────
public class MSD_RemovalModule : ConfirmTaskModule
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

public class HV_FrontVoltageModule : ZoneAndMeasureModule
{
    public override string ModuleId => "HV_Front_Voltage";
    protected override string GetTargetObjName() => "HV_Front_Cable_Term";
}

public class HV_RearVoltageModule : ZoneAndMeasureModule
{
    public override string ModuleId => "HV_Rear_Voltage";
    protected override string GetTargetObjName() => "HV_Rear_Cable_Term";
}

public class InsulationResistanceTestModule : ZoneAndMeasureModule
{
    public override string ModuleId => "Insulation_Resistance_Test";
    protected override string GetTargetObjName() => "Battery_Main_Terminal";
}

// ──────────────────────────────────────────────────────────────
//  Task[05] BatteryPack_Disassemble
//  spawnObjects: ImpactWrench, BatteryJack
//  targetZoneId 없음 → PC에서 requiredItems 중 하나를 집으면 완료
//  (볼트 그룹 실제 분해는 InteractablePart로 처리, 지금은 도구 집기로 완료 판정)
// ──────────────────────────────────────────────────────────────
public class BatteryPack_Disassemble1Module : ConfirmTaskModule
{
    public override string ModuleId => "BatteryPack_Disassemble1";
}

public class BatteryPack_Cover_OpenModule : ConfirmTaskModule
{
    public override string ModuleId => "BatteryPack_Cover_Open";
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
public class BatteryPack_Extraction_MoveModule : GrabZoneTaskModule
{
    public override string ModuleId => "BatteryPack_Extraction_Move";
}
public class BatteryPack_Reinsertion_MoveModule : GrabZoneTaskModule
{
    public override string ModuleId => "BatteryPack_Reinsertion_Move";
}
public class Gasket_ReplaceModule : GrabZoneTaskModule
{
    public override string ModuleId => "Gasket_Replace";
}

public class BatteryPackTopCover_MoveModule : GrabZoneTaskModule
{
    public override string ModuleId => "BatteryPackTopCover_Move";
}
    // ──────────────────────────────────────────────────────────────
    //  Task[09] BatteryPack_Assemble
    //  BatteryPack_BoltGroup 재조립 (볼트 체결) 완료 시
    //  DisassemblyManager.onAllPartsAssembled → InteractionEvents.FireTaskConfirmed("BatteryPack_Assemble")
    // ──────────────────────────────────────────────────────────────
    public class BatteryPackTopCover_AssembleModule : ConfirmTaskModule
    {
        public override string ModuleId => "BatteryPackTopCover_Assemble";
    }

    public class BatteryPack_Assemble1Module : ConfirmTaskModule
    {
        public override string ModuleId => "BatteryPack_Assemble1";
    }
    public class BatteryPack_Assemble2Module : ConfirmTaskModule
    {
        public override string ModuleId => "BatteryPack_Assemble2";
    }

    public class Connector_Cover_ReInstallModule : ConfirmTaskModule
    {
        public override string ModuleId => "Connector_Cover_Reinstall";
    }

    public class Front_Cover_ReInstallModule : ConfirmTaskModule
    {
        public override string ModuleId => "Front_Cover_ReInstall";
    }

    public class Rear_Cover_ReInstallModule : ConfirmTaskModule
    {
        public override string ModuleId => "Rear_Cover_ReInstall";
    }

// ──────────────────────────────────────────────────────────────
//  Task[10] MSD_Reinstall
//  MSD_Lever 를 MSD_Port_Zone 에 꽂으면 완료
// ──────────────────────────────────────────────────────────────
public class MSD_ReinstallModule : ConfirmTaskModule
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
            // 기존 등록 로직
            registry.Register(new InitialDiagnosisModule());
            registry.Register(new LOTO_GlovesModule());
            registry.Register(new MSD_RemovalModule());
            registry.Register(new DischargeWaitModule());
            registry.Register(new Voltage_VerificationModule());
            registry.Register(new HV_FrontVoltageModule());
            registry.Register(new HV_RearVoltageModule());
            registry.Register(new BatteryPack_Disassemble1Module());
            registry.Register(new BatteryPack_Cover_OpenModule());
            registry.Register(new Insulation_MeasureModule());
            registry.Register(new Contamination_CleanModule());
            registry.Register(new Gasket_ReplaceModule());
            registry.Register(new BatteryPack_Assemble1Module());
            registry.Register(new BatteryPack_Assemble2Module());
            registry.Register(new Connector_Cover_ReInstallModule());
            registry.Register(new MSD_ReinstallModule());
            registry.Register(new FinalDiagnosisModule());

            // ★ [중요] 누락된 v2 시나리오 추가 모듈 9개 등록 ★
            registry.Register(new LOTO_SmartKeyModule());      // 시나리오 2번째 단계
            registry.Register(new LOTO_FaceShieldModule());    // 시나리오 3번째 단계
            registry.Register(new LOTO_ShoesModule());        // 시나리오 4번째 단계
            registry.Register(new Front_Cover_RemovalModule());
            registry.Register(new Rear_Cover_RemovalModule());
            registry.Register(new Battery_Cover_Removal1Module());
            registry.Register(new Battery_Cover_Removal2Module());
            registry.Register(new Connector_Cover_RemovalModule());
            registry.Register(new HV_Front_DisconnectModule());
            registry.Register(new HV_Front_ConnectModule());
            registry.Register(new HV_Rear_DisconnectModule());
            registry.Register(new HV_Rear_ConnectModule());
            registry.Register(new ICCU_DisconnectModule());
            registry.Register(new ICCU_ConnectModule());
            registry.Register(new BMU_DisconnectModule());
            registry.Register(new BMU_ConnectModule());
            registry.Register(new Ground_RemovalModule());
            registry.Register(new Ground_ReinstallModule());
            registry.Register(new Coolant_Hose_InModule());
            registry.Register(new Coolant_Hose_In_ConnectModule());
            registry.Register(new Coolant_Hose_OutModule());
            registry.Register(new Coolant_Hose_Out_ConnectModule());
            registry.Register(new InsulationResistanceTestModule());
            registry.Register(new PRA_RemovalModule());
            registry.Register(new PRA_ReplaceModule());
            registry.Register(new BatteryPackTopCover_MoveModule());
            registry.Register(new BatteryPackTopCover_AssembleModule());
            registry.Register(new LOTO_WarningSignModule());
            registry.Register(new CarLiftUpStepModule());
            registry.Register(new BatteryJack_Lift_UpModule());
            registry.Register(new BatteryJack_LoweringModule());
            registry.Register(new BatteryPack_UnboltModule());
            registry.Register(new Vehicle_Hood_OpenModule());
            registry.Register(new Battery_12V_Cover_RemovalModule());
            registry.Register(new BatteryPack_Extraction_MoveModule());
            registry.Register(new BatteryPack_Reinsertion_MoveModule());
            registry.Register(new Front_Cover_ReInstallModule());
            registry.Register(new Rear_Cover_ReInstallModule());
            registry.Register(new BatteryJack_Lift_Up_FinalModule());
            registry.Register(new BatteryJack_LoweringFinalModule());
            registry.Register(new Coolant_RefillModule());
        
        Debug.Log($"[EVModules] P0AA6 시나리오 전체 모듈 등록 완료 (v2 포함)");
        }
    }

// ──────────────────────────────────────────────────────────────
//  v2 시나리오 추가 모듈 (9개)
//  모두 ConfirmTaskModule 상속 — 타겟 GO 클릭 or TaskCompleteSignal로 완료
// ──────────────────────────────────────────────────────────────

    public class Coolant_RefillModule : ConfirmTaskModule
    {
        public override string ModuleId => "Coolant_Refill";
    }

    public class BatteryJack_Lift_Up_FinalModule : GrabZoneTaskModule
{
        public override string ModuleId => "BatteryJack_Lift_Up_Final";
    }
    public class BatteryJack_LoweringFinalModule : ConfirmTaskModule
    {
        public override string ModuleId => "BatteryJack_Lowering_Final";
    }
    public class Front_Cover_RemovalModule : ConfirmTaskModule
    {
        public override string ModuleId => "Front_Cover_Removal";
    }
    public class Rear_Cover_RemovalModule : ConfirmTaskModule
    {
        public override string ModuleId => "Rear_Cover_Removal";
    }
    public class Battery_Cover_Removal1Module : ConfirmTaskModule
    {
        public override string ModuleId => "Battery_Cover_Removal1";
    }
    public class Battery_Cover_Removal2Module : ConfirmTaskModule
    {
        public override string ModuleId => "Battery_Cover_Removal2";
    }

    public class Connector_Cover_RemovalModule : ConfirmTaskModule
    {
        public override string ModuleId => "Connector_Cover_Removal";
    }

    public class HV_Front_DisconnectModule : ConfirmTaskModule
    {
        public override string ModuleId => "HV_Front_Disconnect";
    }
    public class HV_Front_ConnectModule : ConfirmTaskModule
    {
        public override string ModuleId => "HV_Front_Connect";
    }

    public class HV_Rear_DisconnectModule : ConfirmTaskModule
    {
        public override string ModuleId => "HV_Rear_Disconnect";
    }
    public class HV_Rear_ConnectModule : ConfirmTaskModule
    {
        public override string ModuleId => "HV_Rear_Connect";
    }

    public class ICCU_DisconnectModule : ConfirmTaskModule
    {
        public override string ModuleId => "ICCU_Disconnect";
    }
    public class ICCU_ConnectModule : ConfirmTaskModule
    {
        public override string ModuleId => "ICCU_Connect";
    }

    public class BMU_DisconnectModule : ConfirmTaskModule
    {
        public override string ModuleId => "BMU_Disconnect";
    }
    public class BMU_ConnectModule : ConfirmTaskModule
    {
        public override string ModuleId => "BMU_Connect";
    }

    public class Ground_RemovalModule : ConfirmTaskModule
    {
        public override string ModuleId => "Ground_Removal";
    }
    public class Ground_ReinstallModule : ConfirmTaskModule
    {
        public override string ModuleId => "Ground_Reinstall";
    }

    public class Coolant_Hose_InModule : ConfirmTaskModule
    {
        public override string ModuleId => "Coolant_Hose_In";
    }
    public class Coolant_Hose_In_ConnectModule : ConfirmTaskModule
    {
        public override string ModuleId => "Coolant_Hose_In_Connect";
    }

    public class Coolant_Hose_OutModule : ConfirmTaskModule
    {
        public override string ModuleId => "Coolant_Hose_Out";
    }
    public class Coolant_Hose_Out_ConnectModule : ConfirmTaskModule
    {
        public override string ModuleId => "Coolant_Hose_Out_Connect";
    }

    public class BatteryPack_UnboltModule : ConfirmTaskModule
    {
        public override string ModuleId => "BatteryPack_Unbolt";
    }
    public class Vehicle_Hood_OpenModule : ConfirmTaskModule
    {
        public override string ModuleId => "Vehicle_Hood_Open";
    }

    public class Battery_12V_Cover_RemovalModule : ConfirmTaskModule
    {
        public override string ModuleId => "Battery_12V_Cover_Removal";
    }

    public class LOTO_SmartKeyModule : GrabZoneTaskModule
    {
        public override string ModuleId => "LOTO_SmartKey";
    }

    public class LOTO_FaceShieldModule : GrabAllItemsModule
    {
        public override string ModuleId => "LOTO_FaceShield";
    }

    public class LOTO_ShoesModule : GrabAllItemsModule
    {
        public override string ModuleId => "LOTO_Shoes";
    }