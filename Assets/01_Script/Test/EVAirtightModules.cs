using System;
using System.Collections.Generic;

// ============================================================
//  EVAirtightModules.cs
//  ev_p0aa6_battery_airtight.json(고전압 배터리 팩 · 냉각수 라인 기밀 점검) 전용 단계
//  근거: 정비 매뉴얼 「배터리 제어 시스템 - 기밀 점검」
//
//  흐름 (배터리 팩(BSA)을 차에서 내린 상태에서 진행)
//   1) 냉각수 라인 기밀 — 상부 케이스 장착 전
//      BSA 냉각수 완전 배출 → 피팅 [IN]/[OUT] 장착 · [OUT] 밸브 잠금
//      → GDS 로 기밀 시험기(ULT-M100) 연결 · 영점 조정 → 호스를 시험기 H(HIGH PRESSURE AIR OUTPUT)에 연결
//      → GDS 냉각수 라인 기밀 점검(약 20초, PASS) → 드레인 호스로 잔압 배출 후 피팅 탈거
//   2) 상부 케이스 장착 (기존 BatteryPackTopCover_Assemble)
//   3) 배터리 팩 기밀 — 막음 커넥터(E004 · E007 · E008 · E010) · 벤팅 밸브 실링 지그 장착
//      → 에어 주입 어댑터 · 압력 센서 모듈을 압력 조절기에 5초 이상 눌러 부착
//      → GDS 영점 조정 → 어댑터 호스를 시험기 L AIR OUTPUT · SENSOR INPUT 에 연결
//      → GDS 배터리 팩 기밀 점검 → FAIL
//   4) 헬륨 누설 탐지 — 헬륨 밸브에 연결해 500 mbar 로 약 30초 가압 → 리크 디텍터로 누설 부위 탐색
//      → 누설 부위 보수 → 재점검 PASS → 점검 장비 탈거
//
//  GDS 단계(Tester_Setup / Coolant_Line_Test / Pack_Zero_Adjust / Pack_Test / Pack_Retest)는
//  GdsTablet 부가 기능 '고전압 배터리 팩 및 냉각수라인 기밀 점검'이 끝나면 완료 신호를 보낸다.
//
//  순서 위반(PROC_ORDER)
//   · 냉각수 라인 점검 중 [OUT] 밸브를 엶 — 공기가 새서 압력이 안 잡힌다
//   · 잔압을 빼기 전에 피팅을 탈거 — 피팅이 튀어 다칠 수 있다
//   · 막음 커넥터 · 실링 지그 없이 시험기 호스 연결
//   · 헬륨 가압 중 밸브를 먼저 잠금(30초 전)
// ============================================================

public static class AirtightIds
{
    // 기밀 점검 장비 · 부품 오브젝트 ID (ClickableAnimator uniqueId / 구역 ID) — 3D 파츠 제작 시 이 이름을 쓴다
    public const string Tester = "Airtight_Tester";                  // 기밀 시험기 ULT-M100
    public const string FittingIn = "Airtight_Fitting_In";           // 냉각수 피팅 [IN]
    public const string FittingOut = "Airtight_Fitting_Out";         // 냉각수 피팅 [OUT]
    public const string OutValve = "Airtight_Fitting_Out_Valve";     // 피팅 [OUT] 밸브
    public const string CoolantHose = "Airtight_Coolant_Hose";       // 피팅 [IN] ↔ 시험기 H AIR OUTPUT 호스
    public const string DrainHose = "Airtight_Drain_Hose";           // 잔압 배출용 드레인 호스
    public const string SealingConnector = "Airtight_Sealing_Connector"; // 막음 커넥터 E004/E007/E008/E010
    public const string VentingJig = "Airtight_Venting_Jig";         // 벤팅 밸브 실링 지그
    public const string AirAdapter = "Airtight_Air_Adapter";         // 에어 주입 어댑터 + 압력 센서 모듈
    public const string PackHose = "Airtight_Pack_Hose";             // 어댑터 ↔ 시험기 L AIR OUTPUT / SENSOR INPUT
    public const string HeliumValve = "Airtight_Helium_Valve";       // 헬륨 가압 밸브
    public const string LeakDetector = "Airtight_Leak_Detector";     // 헬륨 리크 디텍터
    public const string LeakPoint = "Airtight_Leak_Point";           // 누설 부위(커넥터 결합부)
}

// ── 1) 냉각수 라인 기밀 ─────────────────────────────────────

/// <summary>BSA 안에 남은 냉각수를 다 빼낸다 — 냉각수가 남아 있으면 압력 변화가 왜곡된다</summary>
public class Airtight_Coolant_DrainModule : TimedGuardedModule
{
    public override string ModuleId => "Airtight_Coolant_Drain";
    protected override float DefaultWaitSeconds => 20f;
    protected override string EarlyWhy => "BSA 냉각수가 다 빠지기 전에 배출 완료 처리";
    public override string EarlyConfirmRuleId => "PROC_ORDER";
}

/// <summary>냉각수 입구에 피팅 [IN], 출구에 피팅 [OUT] 장착 후 [OUT] 밸브 잠금</summary>
public class Airtight_Coolant_Fitting_InstallModule : OrderGuardedModule
{
    public override string ModuleId => "Airtight_Coolant_Fitting_Install";
    protected override IEnumerable<(string, string)> Forbidden => new[]
    {
        (AirtightIds.CoolantHose, "피팅 장착 · [OUT] 밸브 잠금 전에 시험기 호스 연결"),
    };
}

/// <summary>GDS 로 배터리 코드 입력 · 시험기 연결 · 냉각수 라인 영점 조정 (GDS 완료 신호)</summary>
public class Airtight_Tester_SetupModule : OrderGuardedModule
{
    public override string ModuleId => "Airtight_Tester_Setup";
    protected override IEnumerable<(string, string)> Forbidden => new[]
    {
        (AirtightIds.CoolantHose, "영점 조정 전에 호스를 연결 — 영점은 모든 연결을 분리한 상태에서 잡는다"),
    };
}

/// <summary>피팅 [IN] 호스를 시험기 H(HIGH PRESSURE AIR OUTPUT)에 연결</summary>
public class Airtight_Coolant_Hose_ConnectModule : OrderGuardedModule
{
    public override string ModuleId => "Airtight_Coolant_Hose_Connect";
}

/// <summary>GDS 냉각수 라인 기밀 점검 실행 → PASS (GDS 완료 신호)</summary>
public class Airtight_Coolant_Line_TestModule : OrderGuardedModule
{
    public override string ModuleId => "Airtight_Coolant_Line_Test";
    protected override IEnumerable<(string, string)> Forbidden => new[]
    {
        (AirtightIds.OutValve, "점검 중 피팅 [OUT] 밸브를 엶 — 공기가 새서 압력이 유지되지 않는다"),
        (AirtightIds.CoolantHose, "점검 중 시험기 호스 분리"),
    };
}

/// <summary>드레인 호스로 잔류 공기를 뺀 뒤 호스 · 피팅 탈거</summary>
public class Airtight_Coolant_Air_ReleaseModule : OrderGuardedModule
{
    public override string ModuleId => "Airtight_Coolant_Air_Release";
    protected override IEnumerable<(string, string)> Forbidden => new[]
    {
        (AirtightIds.FittingIn, "잔압을 빼기 전에 피팅 [IN] 탈거 — 피팅이 튀어나갈 수 있다"),
        (AirtightIds.FittingOut, "잔압을 빼기 전에 피팅 [OUT] 탈거 — 피팅이 튀어나갈 수 있다"),
    };
}

// ── 3) 배터리 팩 기밀 ───────────────────────────────────────

/// <summary>차종에 맞는 막음 커넥터 E004 · E007 · E008 · E010 장착</summary>
public class Airtight_Sealing_Connector_InstallModule : OrderGuardedModule
{
    public override string ModuleId => "Airtight_Sealing_Connector_Install";
    protected override IEnumerable<(string, string)> Forbidden => new[]
    {
        (AirtightIds.PackHose, "막음 커넥터 장착 전에 시험기 호스 연결"),
    };
}

/// <summary>벤팅 밸브(상부 · 측면)에 실링 지그 장착</summary>
public class Airtight_Venting_Jig_InstallModule : OrderGuardedModule
{
    public override string ModuleId => "Airtight_Venting_Jig_Install";
    protected override IEnumerable<(string, string)> Forbidden => new[]
    {
        (AirtightIds.PackHose, "벤팅 밸브 실링 전에 시험기 호스 연결"),
    };
}

/// <summary>에어 주입 어댑터 · 압력 센서 모듈을 압력 조절기에 5초 이상 눌러 부착 — 덜 붙으면 압력이 샌다</summary>
public class Airtight_Adapter_InstallModule : TimedGuardedModule
{
    public override string ModuleId => "Airtight_Adapter_Install";
    protected override float DefaultWaitSeconds => 5f;
    protected override string EarlyWhy => "어댑터를 5초 이상 눌러 붙이지 않음 — 압력이 샌다";
    public override string EarlyConfirmRuleId => "PROC_ORDER";
}

/// <summary>GDS 막음 커넥터 결합 확인 · 압력 센서 모듈 영점 조정 (GDS 완료 신호)</summary>
public class Airtight_Pack_Zero_AdjustModule : OrderGuardedModule
{
    public override string ModuleId => "Airtight_Pack_Zero_Adjust";
}

/// <summary>어댑터 호스를 시험기 L AIR OUTPUT · SENSOR INPUT 에 연결하고 압력 조절기 확인</summary>
public class Airtight_Pack_Hose_ConnectModule : OrderGuardedModule
{
    public override string ModuleId => "Airtight_Pack_Hose_Connect";
}

/// <summary>GDS 배터리 팩 기밀 점검 실행 → 누설로 FAIL (GDS 완료 신호)</summary>
public class Airtight_Pack_TestModule : OrderGuardedModule
{
    public override string ModuleId => "Airtight_Pack_Test";
    protected override IEnumerable<(string, string)> Forbidden => new[]
    {
        (AirtightIds.PackHose, "점검 중 시험기 호스 분리"),
        (AirtightIds.AirAdapter, "점검 중 에어 주입 어댑터 분리"),
    };
}

// ── 4) 헬륨 누설 탐지 · 보수 · 재점검 ──────────────────────

/// <summary>어댑터 호스를 헬륨 밸브에 옮겨 500 mbar 로 약 30초 가압 후 밸브 잠금</summary>
public class Airtight_Helium_InjectModule : TimedGuardedModule
{
    public override string ModuleId => "Airtight_Helium_Inject";
    protected override float DefaultWaitSeconds => 30f;
    protected override string EarlyWhy => "헬륨을 30초 채우기 전에 밸브 잠금 — 누설 부위에서 헬륨이 충분히 나오지 않는다";
    public override string EarlyConfirmRuleId => "PROC_ORDER";
}

/// <summary>리크 디텍터로 하부 케이스 용접부 · 커넥터 결합부 · 상부 케이스 가스켓을 훑어 누설 부위를 찾는다</summary>
public class Airtight_Leak_SearchModule : OrderGuardedModule
{
    public override string ModuleId => "Airtight_Leak_Search";
}

/// <summary>찾은 누설 부위(커넥터 결합부 실링) 보수</summary>
public class Airtight_Leak_RepairModule : OrderGuardedModule
{
    public override string ModuleId => "Airtight_Leak_Repair";
}

/// <summary>보수 후 GDS 배터리 팩 기밀 재점검 → PASS (GDS 완료 신호)</summary>
public class Airtight_Pack_RetestModule : OrderGuardedModule
{
    public override string ModuleId => "Airtight_Pack_Retest";
    protected override IEnumerable<(string, string)> Forbidden => new[]
    {
        (AirtightIds.PackHose, "점검 중 시험기 호스 분리"),
        (AirtightIds.AirAdapter, "점검 중 에어 주입 어댑터 분리"),
    };
}

/// <summary>잔압 배출 → 어댑터 · 센서 모듈 · 실링 지그 · 막음 커넥터 탈거</summary>
public class Airtight_Equipment_RemovalModule : OrderGuardedModule
{
    public override string ModuleId => "Airtight_Equipment_Removal";
}

public static class EVAirtightModules
{
    public static void RegisterAll(ModuleRegistry registry)
    {
        registry.Register(new Airtight_Coolant_DrainModule());
        registry.Register(new Airtight_Coolant_Fitting_InstallModule());
        registry.Register(new Airtight_Tester_SetupModule());
        registry.Register(new Airtight_Coolant_Hose_ConnectModule());
        registry.Register(new Airtight_Coolant_Line_TestModule());
        registry.Register(new Airtight_Coolant_Air_ReleaseModule());
        registry.Register(new Airtight_Sealing_Connector_InstallModule());
        registry.Register(new Airtight_Venting_Jig_InstallModule());
        registry.Register(new Airtight_Adapter_InstallModule());
        registry.Register(new Airtight_Pack_Zero_AdjustModule());
        registry.Register(new Airtight_Pack_Hose_ConnectModule());
        registry.Register(new Airtight_Pack_TestModule());
        registry.Register(new Airtight_Helium_InjectModule());
        registry.Register(new Airtight_Leak_SearchModule());
        registry.Register(new Airtight_Leak_RepairModule());
        registry.Register(new Airtight_Pack_RetestModule());
        registry.Register(new Airtight_Equipment_RemovalModule());
    }
}
