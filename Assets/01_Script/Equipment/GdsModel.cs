using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  GdsModel.cs
//
//  GDS 진단기 화면에 보일 '차량 상태'. 실제 차량 통신 대신 지금 시나리오 진행 상황으로 값을 만든다.
//  · 고장 코드: 시나리오 ID(ev_p0aa6_…)에서 코드를 뽑고, 수리 단계(PRA 교체)가 끝나기 전까지 '현재 고장'
//  · 통신: 서비스 플러그 분리 ~ 재장착 사이에는 차량 통신 불가(고전압 차단·12V 분리 상태)
//  · 센서 데이터: 매뉴얼 규정값(팩 480~826V, 셀 2.5~4.2V, 편차 40mV 이하, 절연 500kΩ 이상) 기준으로 자연스럽게 흔들리는 값
//  · 12V 보조 배터리 교환(ev_p0562_…): 새 배터리 장착 전까지 12V 전압 낮음, (−) 단자 분리 ~ 재연결 사이 통신 불가
//  · 냉각수 교환 · 에어빼기(ev_p0c73_…): 에어빼기 전까지 P0C73 현재 고장, 냉각 대기 단계에서 냉각수 온도가 내려감
//  시나리오 없이(자유 실습) 쓰면 P0AA6 절연 고장이 있는 차로 가정한다.
// ============================================================
public static class GdsModel
{
    public struct Dtc { public string code, name, system, symptom, condition; }

    private static readonly Dictionary<string, Dtc> Table = new Dictionary<string, Dtc>
    {
        ["P0AA6"] = new Dtc { code = "P0AA6", name = "고전압 시스템 절연 저항 이상", system = "BMS", symptom = "계기판 경고등 점등, 출력 제한, 충전 불가", condition = "BMU 절연 저항 측정값 500 kΩ 미만" },
        ["P0AA4"] = new Dtc { code = "P0AA4", name = "고전압 배터리 음극 릴레이 고착", system = "BMS", symptom = "시동(READY) 불가", condition = "릴레이 OFF 지령 중 릴레이 후단 전압 검출" },
        ["P0AA1"] = new Dtc { code = "P0AA1", name = "고전압 배터리 양극 릴레이 고착", system = "BMS", symptom = "시동(READY) 불가", condition = "릴레이 OFF 지령 중 릴레이 후단 전압 검출" },
        ["P0A80"] = new Dtc { code = "P0A80", name = "고전압 배터리 셀 열화(교환 필요)", system = "BMS", symptom = "주행 가능 거리 감소", condition = "셀 간 전압 편차 40 mV 초과" },
        ["P0C73"] = new Dtc { code = "P0C73", name = "구동 모터 냉각 시스템 성능 저하", system = "MCU", symptom = "출력 제한", condition = "모터 온도 상승률 과다" },
        ["P0A78"] = new Dtc { code = "P0A78", name = "구동 모터 인버터 성능 이상", system = "MCU", symptom = "주행 불가", condition = "인버터 내부 고장 검출" },
        ["P0D21"] = new Dtc { code = "P0D21", name = "ICCU(통합 충전 제어) 고장", system = "ICCU", symptom = "충전 불가, 12V 충전 불가", condition = "ICCU 내부 고장 검출" },
        ["P0A94"] = new Dtc { code = "P0A94", name = "LDC(DC-DC 컨버터) 성능 이상", system = "ICCU", symptom = "12V 배터리 방전", condition = "LDC 출력 전압 이상" },
        ["P0AEE"] = new Dtc { code = "P0AEE", name = "고전압 배터리 온도 센서 회로 이상", system = "BMS", symptom = "출력 제한", condition = "온도 센서 값 범위 이탈" },
        ["P0562"] = new Dtc { code = "P0562", name = "시스템 전압 낮음 (12V 보조 배터리)", system = "VCU", symptom = "시동(READY) 불가, 계기판 경고등", condition = "12V 전원 전압 11.5 V 미만 지속" },
        ["U1112"] = new Dtc { code = "U1112", name = "CAN 통신 이상", system = "VCU", symptom = "경고등 점등", condition = "제어기 메시지 미수신" },
    };

    public static readonly string[] Systems =
    {
        "VCU (차량 제어)", "BMS (배터리 관리)", "MCU 전륜 (모터 제어)", "MCU 후륜 (모터 제어)", "ICCU (통합 충전 제어)",
        "IEB (통합 전동 브레이크)", "MDPS (전동 조향)", "ACU (에어백)", "BCM (바디 제어)", "FATC (공조)", "VESS (가상 엔진 사운드)",
    };

    // ── 시나리오 ──────────────────────────
    // 단계 진행 상태는 ScenarioStateReceiver, 시나리오 데이터는 스냅샷 없이 시작한 세션이면 ScenarioReceiver 에만 있다.
    // 예전엔 CurrentScenario 만 봐서 시나리오가 없는 것으로 판단 → 현재 단계가 빈 값이라 진단을 끝내도 단계 신호를 안 보냈다.
    private static ScenarioReceiver s_scenarioReceiver;
    public static ScenarioData Scenario
    {
        get
        {
            var r = ScenarioStateReceiver.Instance;
            if (r != null && r.CurrentScenario != null) return r.CurrentScenario;
            if (s_scenarioReceiver == null) s_scenarioReceiver = Object.FindFirstObjectByType<ScenarioReceiver>();
            return s_scenarioReceiver != null ? s_scenarioReceiver.ReceivedData : null;
        }
    }
    public static bool HasScenario => Scenario != null && Scenario.scenario != null && Scenario.scenario.tasks != null && Scenario.scenario.tasks.Count > 0;
    public static int TaskIndex => ScenarioStateReceiver.Instance != null ? ScenarioStateReceiver.Instance.CurrentTaskState.taskIndex : -1;
    public static string CurrentModule
    {
        get
        {
            if (!HasScenario) return "";
            int i = TaskIndex;
            var t = Scenario.scenario.tasks;
            return i >= 0 && i < t.Count ? t[i].moduleId : "";
        }
    }

    public static int IndexOf(string moduleId)
    {
        if (!HasScenario) return -1;
        var t = Scenario.scenario.tasks;
        for (int i = 0; i < t.Count; i++) if (t[i].moduleId == moduleId) return i;
        return -1;
    }

    /// <summary>시나리오의 해당 단계가 끝났는가(없는 단계는 false)</summary>
    public static bool Done(string moduleId)
    {
        int i = IndexOf(moduleId);
        if (i < 0) return false;
        int cur = TaskIndex;
        if (cur > i) return true;
        return cur == i && ScenarioStateReceiver.Instance.CurrentTaskState.status == TaskStatus.Completed;
    }

    public static string DtcCode
    {
        get
        {
            if (!HasScenario) return "P0AA6";
            string id = Scenario.scenarioId ?? "";
            var parts = id.Split('_');
            foreach (var p in parts)
                if (p.Length == 5 && (p[0] == 'p' || p[0] == 'u' || p[0] == 'c' || p[0] == 'b' || p[0] == 'P' || p[0] == 'U')) return p.ToUpperInvariant();
            return "";
        }
    }

    public static Dtc CurrentDtc
    {
        get
        {
            string c = DtcCode;
            if (Table.TryGetValue(c, out var d)) return d;
            return new Dtc { code = c, name = "고장 코드 정보", system = "BMS", symptom = "-", condition = "-" };
        }
    }

    /// <summary>고장 원인이 아직 남아 있는가 — 수리(PRA 교체)가 끝나면 false</summary>
    public static bool FaultPresent
    {
        get
        {
            if (string.IsNullOrEmpty(DtcCode)) return false;
            if (!HasScenario) return true;
            if (IsAirtightScenario) return !Done("Airtight_Leak_Repair");
            if (IndexOf("PRA_Replace") >= 0) return !Done("PRA_Replace");
            if (Is12VScenario) return !Done("Battery_12V_Install");
            if (IsCoolantScenario) return !Done("Coolant_Air_Bleed");
            int fin = IndexOf("FinalDiagnosis");
            return fin < 0 || TaskIndex < fin;
        }
    }

    /// <summary>고전압 차단(서비스 플러그 분리·12V 분리) 중이라 차량과 통신할 수 없는가</summary>
    public static bool CommLost => HasScenario &&
        ((Done("MSD_Removal") && !Done("MSD_Reinstall")) ||
         (Done("Battery_12V_Neg_Disconnect") && !Done("Battery_12V_Neg_Connect")));

    // ── 고전압 배터리 팩 · 냉각수 라인 기밀 점검(ev_p0aa6_battery_airtight) ──
    // 고장 원인: 팩 커넥터 결합부 실링 불량으로 수분이 들어가 절연 저항이 떨어짐(P0AA6). 누설 부위를 보수하면 해소.
    // 첫 팩 기밀 점검은 누설로 FAIL, 보수 후 재점검은 PASS.

    /// <summary>기밀 점검 시나리오인가</summary>
    public static bool IsAirtightScenario => IndexOf("Airtight_Pack_Test") >= 0;

    /// <summary>배터리 팩 기밀 점검 압력 변화(mbar) — 누설 부위를 보수하기 전에는 판정 기준을 넘는다</summary>
    public static float PackLeakMbar => IsAirtightScenario && !Done("Airtight_Leak_Repair") ? 4.8f + N(11.3f, 0.3f) : 0f;

    /// <summary>12V 보조 배터리 교환 시나리오인가</summary>
    public static bool Is12VScenario => IndexOf("Battery_12V_Install") >= 0;

    /// <summary>12V 보조 배터리 전압 — 교환 시나리오에서 새 배터리를 달기 전에는 방전 상태(약 10.9 V)</summary>
    public static float AuxVoltage => Is12VScenario && !Done("Battery_12V_Install") ? 10.9f + N(9.1f, 0.03f) : 12.6f + N(9.1f, 0.03f);

    // ── 냉각수 교환 · 에어빼기(ev_p0c73_…) ──────────────
    // 고장 원인: 냉각 회로에 공기가 차 순환이 안 됨 → 모터 온도 상승(P0C73). 에어빼기가 끝나면 해소.
    // 냉각수 온도: 처음엔 뜨겁고(약 84°C), '냉각 대기' 단계 동안 식어서 40°C 아래로 내려간다.

    /// <summary>냉각수 교환 · 에어빼기 시나리오인가</summary>
    public static bool IsCoolantScenario => IndexOf("Coolant_Air_Bleed") >= 0;

    /// <summary>GDS 부가 기능 '냉각수 에어빼기' 실행 중 (태블릿이 켜고 끈다)</summary>
    public static bool AirBleedRunning;

    public const float CoolantSafeTemp = 40f;
    private static float s_coolStart = -1f;

    public static float CoolantTemp
    {
        get
        {
            if (!IsCoolantScenario) return 27f + N(10.2f, 0.4f, 0.1f);
            if (Done("Coolant_Cool_Down")) { s_coolStart = -1f; return 31f + N(10.2f, 0.4f, 0.1f); }
            if (CurrentModule != "Coolant_Cool_Down") return 84f + N(10.2f, 0.8f, 0.2f);

            // 이 단계에 들어온 시점부터 대기 시간 동안 84 → 36°C
            if (s_coolStart < 0f) s_coolStart = Time.time;
            float wait = DischargeTimerUI.GetWaitSeconds(Scenario?.GetModuleConfig("Coolant_Cool_Down"), 60f);
            float k = Mathf.Clamp01((Time.time - s_coolStart) / Mathf.Max(1f, wait));
            return Mathf.Lerp(84f, 36f, 1f - (1f - k) * (1f - k)) + N(10.2f, 0.4f, 0.2f);
        }
    }

    /// <summary>구동 모터 온도 — 냉각 회로에 공기가 있는 동안은 높다</summary>
    public static float MotorTemp => IsCoolantScenario && FaultPresent
        ? Mathf.Max(CoolantTemp + 30f, 70f) + N(11.4f, 1.2f, 0.3f)
        : CoolantTemp + 6f + N(11.4f, 0.6f, 0.3f);

    public static List<Item> CoolantData()
    {
        float ct = CoolantTemp, mt = MotorTemp;
        bool bleed = AirBleedRunning;
        bool air = IsCoolantScenario && FaultPresent;
        string pump = bleed ? $"{2800f + N(12.6f, 60f, 1.5f):0}" : "0";
        return new List<Item>
        {
            new Item { name = "냉각수 온도 (전장 회로)", value = $"{ct:0}", unit = "°C", spec = $"캡 개방 {CoolantSafeTemp:0} 이하", bad = ct > CoolantSafeTemp },
            new Item { name = "냉각수 온도 (배터리 입구)", value = $"{ct - 4f:0}", unit = "°C", spec = "" },
            new Item { name = "구동 모터 온도", value = $"{mt:0}", unit = "°C", spec = "", bad = air },
            new Item { name = "인버터 온도", value = $"{mt - 12f:0}", unit = "°C", spec = "" },
            new Item { name = "전동 워터펌프 (전장) 회전수", value = pump, unit = "rpm", spec = "" },
            new Item { name = "전동 워터펌프 (배터리) 회전수", value = pump, unit = "rpm", spec = "" },
            new Item { name = "냉각수 밸브 모드", value = bleed ? "통합 순환 (에어빼기)" : "분리 순환", unit = "", spec = "" },
            new Item { name = "모터 출력 제한", value = air ? "작동" : "해제", unit = "", spec = "해제", bad = air },
            new Item { name = "보조 배터리(12V) 전압", value = $"{AuxVoltage:0.00}", unit = "V", spec = "11.5 ~ 15.0", bad = AuxVoltage < 11.5f },
            new Item { name = "고장 코드 개수", value = FaultPresent ? "1" : "0", unit = "", spec = "0", bad = FaultPresent },
        };
    }

    public static string Vin
    {
        get
        {
            var s = Scenario;
            if (s != null && s.map != null && s.map.objects != null)
                foreach (var o in s.map.objects)
                    if (o != null && o.properties != null)
                        foreach (var p in o.properties) if (p.key == "vin" && !string.IsNullOrEmpty(p.ko)) return p.ko;
            return "KMHC251CXRU000417";
        }
    }

    public static float Soc
    {
        get
        {
            var s = Scenario;
            if (s != null && s.map != null && s.map.objects != null)
                foreach (var o in s.map.objects)
                    if (o != null && o.properties != null)
                        foreach (var p in o.properties)
                            if (p.key == "batteryStatus" && !string.IsNullOrEmpty(p.ko))
                            {
                                string digits = "";
                                foreach (char ch in p.ko) if (char.IsDigit(ch) || ch == '.') digits += ch;
                                if (float.TryParse(digits, out float v)) return v;
                            }
            return 68f;
        }
    }

    // ── 센서 데이터 ──────────────────────
    public struct Item
    {
        public string name, value, unit, spec;
        public bool bad;
    }

    private static float N(float seed, float amp, float speed = 0.7f) => (Mathf.PerlinNoise(seed, Time.time * speed) - 0.5f) * 2f * amp;

    public const int Cells = 192;

    public static float PackVoltage => 566f + Soc * 1.92f + N(1.3f, 0.6f);
    public static float CellAvg => PackVoltage / Cells;
    public static float CellMax => CellAvg + 0.008f + Mathf.Abs(N(2.1f, 0.002f));
    public static float CellMin => CellAvg - 0.006f - Mathf.Abs(N(3.7f, 0.002f));
    public static float InsulationK => FaultPresent ? 188f + N(4.4f, 22f, 0.35f) : 3000f;

    public static List<Item> BmsData()
    {
        float pv = PackVoltage, mx = CellMax, mn = CellMin, ins = InsulationK;
        float dev = (mx - mn) * 1000f;
        var l = new List<Item>
        {
            new Item { name = "배터리 충전 상태(SOC)", value = $"{Soc:0.0}", unit = "%", spec = "" },
            new Item { name = "배터리 팩 전압", value = $"{pv:0.0}", unit = "V", spec = "480 ~ 826", bad = pv < 480f || pv > 826f },
            new Item { name = "배터리 팩 전류", value = $"{N(5.1f, 0.3f):0.0}", unit = "A", spec = "" },
            new Item { name = "최대 셀 전압", value = $"{mx:0.000}", unit = "V", spec = "2.5 ~ 4.2", bad = mx > 4.2f },
            new Item { name = "최소 셀 전압", value = $"{mn:0.000}", unit = "V", spec = "2.5 ~ 4.2", bad = mn < 2.5f },
            new Item { name = "셀 간 전압 편차", value = $"{dev:0}", unit = "mV", spec = "40 이하", bad = dev > 40f },
            new Item { name = "절연 저항", value = $"{ins:0}", unit = "kΩ", spec = "500 이상", bad = ins < 500f },
            new Item { name = "배터리 최고 온도", value = $"{25f + N(6.2f, 0.4f, 0.1f):0}", unit = "°C", spec = "" },
            new Item { name = "배터리 최저 온도", value = $"{23f + N(7.9f, 0.4f, 0.1f):0}", unit = "°C", spec = "" },
            new Item { name = "배터리 냉각수 입구 온도", value = $"{21f + N(8.3f, 0.3f, 0.1f):0}", unit = "°C", spec = "" },
            new Item { name = "메인 릴레이(+) / (−)", value = "OFF / OFF", unit = "", spec = "" },
            new Item { name = "프리차지 릴레이", value = "OFF", unit = "", spec = "" },
            new Item { name = "릴레이 융착 상태", value = "Relay Welding not detection", unit = "", spec = "not detection" },
            new Item { name = "배터리 건강 상태(SOH)", value = "97.8", unit = "%", spec = "" },
            new Item { name = "보조 배터리(12V) 전압", value = $"{AuxVoltage:0.00}", unit = "V", spec = "11.5 ~ 15.0", bad = AuxVoltage < 11.5f },
            new Item { name = "고장 코드 개수", value = FaultPresent ? "1" : "0", unit = "", spec = "0", bad = FaultPresent },
        };
        return l;
    }
}
