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
