using System.Collections.Generic;
using System.Text;

/// <summary>
/// 월드 호버 라벨 등에 띄우는 오브젝트 이름 정리.
/// 시나리오·프리팹의 내부 ID(Hood_Latch_Point, BatteryPack_Open_RemovePRA …)를 그대로 보여주지 않고
/// 한국어 표시 이름으로 바꾼다. 사전에 없는 ID 는 "Hood Latch Point" 처럼 읽기 좋게만 다듬는다.
/// </summary>
public static class HoverNameFormatter
{
    private static readonly Dictionary<string, string> Ko = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
    {
        // 아이템
        ["Ioniq5N"] = "아이오닉 5 N",
        ["GDS_Tablet"] = "GDS 진단기",
        ["SmartKey"] = "스마트키",
        ["FaceShield"] = "안면보호구",
        ["InsulatedShoes"] = "절연화",
        ["InsulatedGloves"] = "절연장갑",
        ["ImpactWrench"] = "임팩트 렌치",
        ["Multimeter"] = "멀티미터",
        ["Megger"] = "절연저항 측정기",
        ["BatteryJackA"] = "배터리 잭",
        ["BatteryJackFinalA"] = "배터리 잭",
        ["BatteryPack_Disassemble"] = "배터리 팩",
        ["BatteryPack_Open"] = "배터리 팩",
        ["BatteryPack_Open_RemovePRA"] = "배터리 팩",
        ["BatteryPack_Assemble"] = "배터리 팩",
        ["New_PRA_Unit"] = "새 PRA 유닛",
        ["Old_PRA_Unit"] = "기존 PRA 유닛",
        // 정비 장비
        ["Equip_OilDrainer"] = "오일 받이",
        ["Equip_BatteryLift"] = "이동식 배터리 리프트",
        ["Equip_OilBottle"] = "엔진 오일",
        ["Equip_Canister"] = "냉각수 통",
        ["Equip_Spray_Lube"] = "윤활 방청제 스프레이",
        ["Equip_Spray_Cleaner"] = "접점 세정제",
        ["Equip_Extinguisher"] = "소화기",
        ["Equip_WorkLight"] = "작업등",
        ["Equip_AirImpact"] = "에어 임팩렌치",
        ["Equip_Compressor"] = "에어 컴프레서",
        ["Equip_Spanner"] = "스패너",
        ["Equip_Screwdriver"] = "드라이버",
        ["Equip_Wheel"] = "휠(타이어+림)",
        // 차체 부품 탈거·장착(CarPartService)
        ["Equip_CarPart"] = "떼어낸 부품",
        ["Part_FrontBumper"] = "앞 범퍼",
        ["Part_Headlamp"] = "헤드램프",
        ["Part_Hood"] = "후드",
        ["Part_Frunk"] = "프렁크(앞 트렁크)",
        ["Part_FrontMotor"] = "전륜 모터·감속기",
        ["Part_FrontBeam"] = "앞 범퍼 빔(백빔)",
        ["Part_Cooling"] = "냉각 모듈(콘덴서·라디에이터·팬)",
        ["Part_AcCompressor"] = "전동식 A/C 컴프레서",
        ["Part_DoorFL"] = "앞 도어(왼쪽)",
        ["Part_DoorFR"] = "앞 도어(오른쪽)",
        ["Part_DoorRL"] = "뒤 도어(왼쪽)",
        ["Part_DoorRR"] = "뒤 도어(오른쪽)",
        ["Part_Tailgate"] = "테일게이트",
        ["Part_RearBumper"] = "뒤 범퍼",
        ["Part_FrontUnderCover"] = "프런트 언더 커버",
        ["Part_RearUnderCover"] = "리어 언더 커버",
        ["Part_FrontSuspension"] = "전륜 서브 프레임·서스펜션",
        ["Part_RearMotor"] = "후륜 모터·감속기(IDA)",
        ["Part_RearSuspension"] = "리어 크로스 멤버(서브 프레임)",
        ["Part_RearShockL"] = "리어 쇽 업소버(왼쪽)", ["Part_RearShockR"] = "리어 쇽 업소버(오른쪽)",
        ["Part_RearAxleL"] = "리어 액슬 어셈블리(왼쪽)", ["Part_RearAxleR"] = "리어 액슬 어셈블리(오른쪽)",
        ["Part_RearLowerArmL"] = "리어 로어 암(왼쪽)", ["Part_RearLowerArmR"] = "리어 로어 암(오른쪽)",
        ["Part_RearTrailingArmL"] = "리어 트레일링 암(왼쪽)", ["Part_RearTrailingArmR"] = "리어 트레일링 암(오른쪽)",
        ["SM_RearCrossmember"] = "리어 크로스 멤버", ["SM_RearShock_L"] = "리어 쇽 업소버(왼쪽)", ["SM_RearShock_R"] = "리어 쇽 업소버(오른쪽)",
        ["SM_RearAxle_L"] = "리어 액슬(왼쪽)", ["SM_RearAxle_R"] = "리어 액슬(오른쪽)", ["SM_RearLowerArm_L"] = "리어 로어 암(왼쪽)", ["SM_RearLowerArm_R"] = "리어 로어 암(오른쪽)",
        ["SM_RearTrailingArm_L"] = "리어 트레일링 암(왼쪽)", ["SM_RearTrailingArm_R"] = "리어 트레일링 암(오른쪽)",
        ["Part_Cockpit"] = "크래시 패드(대시보드)",
        ["Part_Hvac"] = "HVAC·블로워 유닛",
        ["Part_Battery12VCover"] = "12V 배터리 커버",
        ["Part_Battery12V"] = "12V 보조 배터리",
        ["Part_BrakeFL"] = "브레이크(앞 왼쪽 캘리퍼·디스크)",
        ["Part_BrakeFR"] = "브레이크(앞 오른쪽 캘리퍼·디스크)",
        ["Part_BrakeRL"] = "브레이크(뒤 왼쪽 캘리퍼·디스크)",
        ["Part_BrakeRR"] = "브레이크(뒤 오른쪽 캘리퍼·디스크)",
        ["Part_SeatFL"] = "앞 좌석(왼쪽·운전석)",
        ["Part_SeatFR"] = "앞 좌석(오른쪽·동승석)",
        ["Part_SeatRear"] = "뒷좌석(시트 쿠션·등받이)",
        ["SM_FrontLowerArm_L"] = "프런트 로어 암(왼쪽)", ["SM_FrontLowerArm_R"] = "프런트 로어 암(오른쪽)", ["SM_FrontStabilizer"] = "프런트 스태빌라이저 바", ["SM_FrontSubframe"] = "프런트 서브 프레임",
        ["SM_Brake_FL"] = "브레이크(앞 왼쪽)", ["SM_Brake_FR"] = "브레이크(앞 오른쪽)", ["SM_Brake_RL"] = "브레이크(뒤 왼쪽)", ["SM_Brake_RR"] = "브레이크(뒤 오른쪽)",
        ["SM_BrakeLines"] = "브레이크 라인",
        ["SM_Seat_FL"] = "앞 좌석(왼쪽)", ["SM_Seat_FR"] = "앞 좌석(오른쪽)", ["SM_Seat_Rear"] = "뒷좌석",
        ["Part_FrontInverter"] = "전륜 인버터(EPCU)",
        ["Part_Iccu"] = "통합 충전기·컨버터(ICCU)",
        ["Part_JunctionBlock"] = "고전압 정션 블록(프런트)", ["Part_RearJunctionBlock"] = "고전압 정션 블록(리어)",
        ["Part_SteeringRack"] = "스티어링 기어박스",
        ["Part_FrontStabilizer"] = "프런트 스태빌라이저 바",
        ["Part_FrontLowerArmL"] = "프런트 로어 암(왼쪽)", ["Part_FrontLowerArmR"] = "프런트 로어 암(오른쪽)",
        ["Part_FrontStrutL"] = "프런트 스트럿(왼쪽)",
        ["Part_TieRodEndL"] = "타이로드 엔드(왼쪽)", ["Part_TieRodEndR"] = "타이로드 엔드(오른쪽)", ["Part_SteeringPowerPack"] = "MDPS 파워팩",
        ["Part_StrutInsulatorL"] = "스트럿 인슐레이터(왼쪽)", ["Part_StrutInsulatorR"] = "스트럿 인슐레이터(오른쪽)",
        ["Part_CoolingFan"] = "쿨링 팬 어셈블리", ["Part_Condenser"] = "에어컨 콘덴서", ["Part_AcInverter"] = "컴프레서 인버터", ["Part_Vcu"] = "차량 제어 유닛(VCU)",
        ["Part_Qra"] = "급속 충전 릴레이(QRA)", ["Part_BatteryPackCover"] = "배터리 팩 하부 커버", ["Part_BatteryPack"] = "배터리 팩 어셈블리(BSA)", ["Part_BatteryTopCase"] = "배터리 팩 상부 케이스", ["Part_Bmu"] = "배터리 매니지먼트 유닛(BMU)", ["Part_MainFuse"] = "메인 퓨즈", ["Part_Pra"] = "파워 릴레이 어셈블리(PRA)",
        ["Part_ShockInsulatorL"] = "쇽 인슐레이터(왼쪽)", ["Part_ShockInsulatorR"] = "쇽 인슐레이터(오른쪽)",
        ["Part_FrontStrutR"] = "프런트 스트럿(오른쪽)",
        ["Part_RearStabilizer"] = "리어 스태빌라이저 바",
        ["Equip_Tire"] = "예비 타이어",
        ["Equip_Ladder"] = "사다리",
        ["Equip_StepLadder"] = "A형 사다리",
        ["Equip_Cone"] = "라바콘",
        ["Equip_Basket"] = "부품 바스켓",
        ["Equip_TapeMeasure"] = "줄자",
        ["Equip_Cutter"] = "커터칼",
        ["Equip_OilJug"] = "엔진 오일 말통(20L)",
        ["Equip_TorqueWrench"] = "토크렌치",
        ["Equip_Spanner_Insulated"] = "절연 스패너(1000V)",
        ["Equip_Screwdriver_Insulated"] = "절연 드라이버(1000V)",
        ["Equip_Grinder"] = "핸드 그라인더",
        ["Equip_PaintGun"] = "도색 스프레이건",
        ["Equip_InsulationTape"] = "절연 테이프",
        ["Equip_Creeper"] = "정비 크리퍼(누움판)",
        ["Equip_TorqueWrench_Insulated"] = "절연 토크렌치(1000V)",
        ["Equip_WheelChock"] = "타이어 고임목",

        // 차량·부품
        ["Hood_Latch_Point"] = "후드 래치",
        ["Battery_12V_Cover"] = "12V 배터리 커버",
        ["12V_Terminal_Neg"] = "12V (−) 단자",
        ["12V_Terminal_Pos"] = "12V (+) 단자",
        ["12V_Battery_Sensor"] = "배터리 센서 커넥터",
        ["12V_Battery_Clamp"] = "배터리 고정 클램프",
        ["Old_12V_Battery"] = "기존 12V 배터리",
        ["New_12V_Battery"] = "신품 12V 배터리",
        ["12V_Install_Zone"] = "12V 배터리 장착 위치",
        ["12V_Removal_Zone"] = "정비 카트(구품 배터리)",
        ["OBD_Port"] = "OBD-II 단자",
        ["OBD_Zone"] = "OBD-II 단자",
        ["Battery_Main_Terminal"] = "배터리 메인 단자",
        ["Battery_Main_Terminal_Zone"] = "배터리 메인 단자",
        ["Battery_Main_Bolts"] = "배터리 메인 볼트",
        ["Battery_Unbolt_Zone"] = "배터리 볼트 해체 위치",
        ["MSD_Lever"] = "서비스 플러그(MSD) 레버",
        ["Discharge_Timer_UI"] = "방전 타이머",
        ["Conn_Front"] = "앞 커넥터",
        ["Conn_Rear"] = "뒤 커넥터",
        ["Conn_ICCU"] = "ICCU 커넥터",
        ["Conn_BMU"] = "BMU 커넥터",
        ["Conn_Ground"] = "접지 커넥터",
        ["HV_Front_Cable_Term"] = "고전압 앞 케이블 단자",
        ["HV_Rear_Cable_Term"] = "고전압 뒤 케이블 단자",
        ["Inverter_Check_Point"] = "인버터 점검 지점",
        ["Coolant_Hose_Inlet"] = "냉각수 호스(입구)",
        ["Coolant_Hose_Outlet"] = "냉각수 호스(출구)",
        ["Coolant_Reservoir_Fill"] = "냉각수 보조탱크",
        ["PRA_BPA_Plus_Terminal"] = "PRA BPA (+) 단자",
        ["PRA_BPA_Minus_Terminal"] = "PRA BPA (−) 단자",
        ["PRA_Inverter_Plus_Terminal"] = "PRA 인버터 (+) 단자",
        ["PRA_Inverter_Minus_Terminal"] = "PRA 인버터 (−) 단자",
        ["PRA_Install_Zone"] = "PRA 설치 위치",

        // 볼트
        ["Front_Cover_Bolts"] = "앞 커버 볼트",
        ["Rear_Cover_Bolts"] = "뒤 커버 볼트",
        ["Front_Cover_Assemble_Bolts"] = "앞 커버 조립 볼트",
        ["Rear_Cover_Assemble_Bolts"] = "뒤 커버 조립 볼트",
        ["Battery_Cover_Bolts"] = "배터리 커버 볼트",
        ["Battery_TopCover_Bolts"] = "배터리 상부 커버 볼트",
        ["Connector_Cover_Bolts"] = "커넥터 커버 볼트",
        ["BatteryPack_Front_Cover_Bolts"] = "배터리 팩 앞 커버 볼트",
        ["BatteryPack_Disassemble1_Bolts"] = "배터리 팩 볼트 (1)",
        ["BatteryPack_Disassemble2_Bolts"] = "배터리 팩 볼트 (2)",
        ["BatteryPack_Assemble1_Bolts"] = "배터리 팩 조립 볼트 (1)",
        ["BatteryPack_Assemble2_Bolts"] = "배터리 팩 조립 볼트 (2)",
        ["BatteryPack_Bolts1"] = "배터리 팩 볼트",

        // 버튼·구역
        ["Lift_Button_Up"] = "리프트 올림 버튼",
        ["Lift_Button_Down"] = "리프트 내림 버튼",
        ["BatteryJack_Button_Up"] = "배터리 잭 올림 버튼",
        ["BatteryJack_Button_Down"] = "배터리 잭 내림 버튼",
        ["BatteryJack_Detached"] = "배터리 잭 분리 위치",
        ["Car_Lift_Up"] = "차량 리프트",
        ["Guide_Car_Lift_Move_Zone"] = "리프트 이동 구역",
        ["Guide_LockBox"] = "잠금박스",
        ["LockBox_Zone"] = "잠금박스",
        ["Guide_WarningSign"] = "경고 표지",
        ["WarningSign_Zone"] = "경고 표지",
        ["Guide_Ground_Point"] = "접지 지점",
        ["Ground_Point_Zone"] = "접지 지점",
        ["BatteryContactZone"] = "배터리 접점",
        ["BatteryPackTopCover_Zone"] = "배터리 팩 상부 커버",
        ["Battery_Extraction_Zone"] = "배터리 탈거 위치",
        ["BatteryPack_Reinsertion_Zone"] = "배터리 팩 재장착 위치",

        // 시나리오 볼트가 달린 차 모델 부품(볼트 진행 패널 제목에 쓴다)
        ["SM_FrontConnector"] = "고전압 커넥터 커버(앞)",
        ["SM_RearConnector"] = "고전압 커넥터 커버(뒤)",
        ["SM_RearConnector_02"] = "고전압 커넥터 커버(뒤)",
        ["SM_Bottom_01"] = "프런트 언더 커버",
        ["SM_Bottom_02"] = "리어 언더 커버",
        ["SM_BatteryCover"] = "배터리 팩 하부 커버",
        ["SM_Cover"] = "배터리 팩 상부 케이스",
        ["SM_BatteryPack"] = "배터리 팩",
        ["SM_Bmu"] = "배터리 매니지먼트 유닛(BMU)",
        ["SM_MainFuse"] = "메인 퓨즈",
        ["SM_Pra"] = "파워 릴레이 어셈블리(PRA)",
        ["SM_BatteryParts1_Rest"] = "고전압 버스바·케이블",
        ["SM_BatteryParts2"] = "전압·온도 센싱 와이어링",
        ["SM_Module"] = "배터리 모듈",
        ["SM_BMU"] = "배터리 매니지먼트 유닛(BMU)",
        ["SM_Gearbox"] = "감속기",
        ["SM_ElectricEngine"] = "전륜 모터",

        // 냉각수 교환
        ["Coolant_Reservoir"] = "모터 냉각수 리저버 탱크",
        ["Coolant_Reservoir_Cap"] = "모터 리저버 탱크 캡",
        ["SM_CoolantTank_Motor"] = "모터 냉각수 리저버 탱크",
        ["SM_CoolantCap_Motor"] = "모터 리저버 탱크 캡",
        ["SM_CoolantTank_Battery"] = "고전압 배터리 냉각수 리저버 탱크",
        ["SM_CoolantCap_Battery"] = "배터리 리저버 탱크 캡",
        ["Part_CoolantTankMotor"] = "모터 냉각수 리저버 탱크",
        ["Part_CoolantTankBattery"] = "고전압 배터리 냉각수 리저버 탱크",
        ["Coolant_Drain_Plug"] = "냉각수 드레인 플러그",
        ["Coolant_Drain_Zone"] = "배출 받침 위치",
        ["Guide_Coolant_Drain_Zone"] = "배출 받침 위치",
        ["Coolant_Fill_Zone"] = "냉각수 주입구",
        ["Guide_Coolant_Fill_Zone"] = "냉각수 주입구",
        ["Drain_Pan"] = "배출 받침",
        ["Coolant_Bottle"] = "냉각수(부동액+물 혼합액)",

        // 기밀 점검
        ["Airtight_Tester"] = "기밀 시험기(ULT-M100)",
        ["Airtight_Coolant_Port"] = "배터리 팩 냉각수 입·출구",
        ["Airtight_Fitting_In"] = "냉각수 피팅 [IN]",
        ["Airtight_Fitting_Out"] = "냉각수 피팅 [OUT]",
        ["Airtight_Fitting_Out_Valve"] = "피팅 [OUT] 밸브",
        ["Airtight_Coolant_Hose"] = "냉각수 라인 점검 호스",
        ["Airtight_Drain_Hose"] = "잔압 배출 드레인 호스",
        ["Airtight_Sealing_Connector"] = "막음 커넥터",
        ["Airtight_Venting_Jig"] = "벤팅 밸브 실링 지그",
        ["Airtight_Air_Adapter"] = "에어 주입 어댑터 · 압력 센서 모듈",
        ["Airtight_Pack_Hose"] = "배터리 팩 점검 호스",
        ["Airtight_Helium_Valve"] = "헬륨 밸브",
        ["Airtight_Leak_Detector"] = "헬륨 리크 디텍터",
        ["Airtight_Leak_Point"] = "누설 부위(커넥터 결합부)",
    };

    /// <summary>내부 ID → 표시 이름. 한국어 설정이면 사전을 쓰고, 아니면(또는 사전에 없으면) 읽기 좋게만 다듬는다.</summary>
    public static string Format(string rawId)
    {
        if (string.IsNullOrEmpty(rawId)) return string.Empty;

        // Unity 가 붙인 "(Clone)", "(1)" 등 정리
        string id = rawId.Replace("(Clone)", "").Trim();
        int paren = id.IndexOf(" (");
        if (paren > 0) id = id.Substring(0, paren);

        bool ko = LocalizationManager.CurrentLanguage == "ko" || string.IsNullOrEmpty(LocalizationManager.CurrentLanguage);
        if (ko && Ko.TryGetValue(id, out var name)) return name;
        return Prettify(id);
    }

    /// <summary>"Hood_Latch_Point" → "Hood Latch Point", "BatteryPackCover" → "Battery Pack Cover" (Guide_ 접두어·_Zone 접미어는 제거)</summary>
    public static string Prettify(string id)
    {
        if (string.IsNullOrEmpty(id)) return string.Empty;
        if (id.StartsWith("Guide_")) id = id.Substring(6);
        if (id.EndsWith("_Zone")) id = id.Substring(0, id.Length - 5);

        var sb = new StringBuilder(id.Length + 8);
        for (int i = 0; i < id.Length; i++)
        {
            char c = id[i];
            if (c == '_' || c == '-') { if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' '); continue; }
            // 소문자→대문자 경계에 공백 (CamelCase)
            if (i > 0 && char.IsUpper(c) && char.IsLower(id[i - 1]) && sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}
