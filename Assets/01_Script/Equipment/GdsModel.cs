using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  GdsModel.cs
//
//  GDS 진단기 화면에 보일 '차량 상태'. 실제 차량 통신 대신 지금 시나리오 진행 상황으로 값을 만든다.
//  · 고장 코드: 시나리오 ID(ev_p0aa6_…)에서 코드를 뽑고, 수리 단계(PRA 교체)가 끝나기 전까지 '현재 고장'
//  · 통신: 서비스 플러그 분리 ~ 재장착 사이에는 차량 통신 불가(고전압 차단·12V 분리 상태)
//  · 센서 데이터: 매뉴얼 규정값(팩 480~826V, 셀 2.5~4.2V, 편차 40mV 이하, 절연 500kΩ 이상) 기준으로 자연스럽게 흔들리는 값
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
        ["U1112"] = new Dtc { code = "U1112", name = "CAN 통신 이상", system = "VCU", symptom = "경고등 점등", condition = "제어기 메시지 미수신" },
    };

    public static readonly string[] Systems =
    {
        "VCU (차량 제어)", "BMS (배터리 관리)", "MCU 전륜 (모터 제어)", "MCU 후륜 (모터 제어)", "ICCU (통합 충전 제어)",
        "IEB (통합 전동 브레이크)", "MDPS (전동 조향)", "ACU (에어백)", "BCM (바디 제어)", "FATC (공조)", "VESS (가상 엔진 사운드)",
    };

    // ── 시나리오 ──────────────────────────
    public static ScenarioData Scenario => ScenarioStateReceiver.Instance != null ? ScenarioStateReceiver.Instance.CurrentScenario : null;
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
            if (IndexOf("PRA_Replace") >= 0) return !Done("PRA_Replace");
            int fin = IndexOf("FinalDiagnosis");
            return fin < 0 || TaskIndex < fin;
        }
    }

    /// <summary>고전압 차단(서비스 플러그 분리·12V 분리) 중이라 차량과 통신할 수 없는가</summary>
    public static bool CommLost => HasScenario && Done("MSD_Removal") && !Done("MSD_Reinstall");

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
            new Item { name = "보조 배터리(12V) 전압", value = $"{12.6f + N(9.1f, 0.03f):0.00}", unit = "V", spec = "11.5 ~ 15.0" },
            new Item { name = "고장 코드 개수", value = FaultPresent ? "1" : "0", unit = "", spec = "0", bad = FaultPresent },
        };
        return l;
    }
}
