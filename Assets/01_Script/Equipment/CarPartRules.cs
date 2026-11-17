using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  CarPartRules.cs
//  자유 실습(CarPartService 분해·조립)에 정비 매뉴얼의 '규칙'을 얹는다 — 지금은 두 가지.
//
//  1) 냉각 회로 공기 (CoolantAir)
//     냉각수가 지나가는 부품(3웨이 밸브·EWP·PTC·분배 파이프·오일 쿨러·수냉 콘덴서 등)을 떼면 회로에 공기가 들어간다.
//     다시 조립해도 GDS [부가 기능 → 냉각수 에어빼기]를 하기 전에는 '에어빼기 필요' 상태가 남는다.
//     값은 VehicleStateSync 로 서버를 거쳐 모두에게 같게(늦게 들어온 사람도): 0 정상 / 1 회로 열림 / 2 조립됐으나 에어빼기 필요
//     (냉각수 교환 '시나리오'는 기존 Coolant_Air_Bleed 단계가 그대로 담당한다 — 이 값은 시나리오에서는 건드리지 않는다)
//
//  2) 재사용 금지 부품 (SingleUse)
//     매뉴얼의 '재사용 금지' 표시(분할 핀 · 록 너트 · 캘리퍼 볼트 · 유니버설 조인트 볼트 등)가 붙은 부위를 다시 끼우면
//     새 부품으로 교환하라는 경고를 띄우고 횟수를 센다(부위 목록 패널에 표시). 자유 실습에는 점수가 없어 감점은 하지 않는다.
// ============================================================
public static class CarPartRules
{
    // ── 재사용 금지 ──
    private static readonly Dictionary<string, string> SingleUse = new Dictionary<string, string>
    {
        { "FrontSuspension", "유니버설 조인트 볼트 · 볼 조인트 분할 핀" },
        { "TieRodEndL", "분할 핀" }, { "TieRodEndR", "분할 핀" },
        { "FrontKnuckleL", "분할 핀" }, { "FrontKnuckleR", "분할 핀" },
        { "TensionArmL", "분할 핀" }, { "TensionArmR", "분할 핀" },
        { "BrakeFL", "프런트 캘리퍼 볼트" }, { "BrakeFR", "프런트 캘리퍼 볼트" },
        { "BrakeRL", "리어 캘리퍼 캐리어 볼트" }, { "BrakeRR", "리어 캘리퍼 캐리어 볼트" },
        { "StrutInsulatorL", "록 너트" }, { "StrutInsulatorR", "록 너트" },
        { "ShockInsulatorL", "록 너트" }, { "ShockInsulatorR", "록 너트" },
        { "SteeringRack", "유니버설 조인트 볼트" },
        { "UniversalJoint", "조인트 볼트" },
        { "AcInverter", "커버 스크류 · 가스켓 · 절연 시트" },
        { "Bmu", "BMU 볼트·너트 · 서비스 커버" },
    };

    /// <summary>이 부위에 재사용 금지 소모품이 있으면 그 이름, 없으면 null.</summary>
    public static string SingleUseOf(string partId) => partId != null && SingleUse.TryGetValue(partId, out var s) ? s : null;

    /// <summary>이번 접속에서 재사용 금지 부품을 그대로 다시 끼운 횟수(내가 끼운 것만)</summary>
    public static int ReuseCount { get; private set; }

    /// <summary>부품을 끼우려는 사람(로컬)이 부른다. 재사용 금지 부위면 경고하고 센다.</summary>
    public static void NoteMount(CarPartService.PartDef d)
    {
        string what = SingleUseOf(d?.id);
        if (what == null) return;
        ReuseCount++;
        EquipFx.Notice("reuse_" + d.id, "재사용 금지 부품", $"{d.name}: {what} 은(는) 재사용 금지입니다 — 매뉴얼대로라면 새 것으로 교환해야 합니다", EquipFx.Warn, 4f, 6f);
    }

    // ── 특수공구 · 크레인 필요 ──
    // 매뉴얼 '특수공구' 표 — 해당 부위 볼트를 돌릴 때 맞는 공구(SpecialTool)나 크레인 자키(ShopCrane)가 3~4 m 안에 없으면 경고(막지는 않음).
    private static readonly Dictionary<string, string[]> ToolsOf = new Dictionary<string, string[]>
    {
        { "TieRodEndL", new[] { "puller" } }, { "TieRodEndR", new[] { "puller" } },
        { "TensionArmL", new[] { "puller" } }, { "TensionArmR", new[] { "puller" } },
        { "FrontKnuckleL", new[] { "puller" } }, { "FrontKnuckleR", new[] { "puller" } },
        { "StrutInsulatorL", new[] { "strutnut" } }, { "StrutInsulatorR", new[] { "strutnut" } },
        { "ShockInsulatorL", new[] { "shocknut" } }, { "ShockInsulatorR", new[] { "shocknut" } },
        { "FrontSuspension", new[] { "beam", "crane" } },
        { "FrontMotor", new[] { "crane" } }, { "RearMotor", new[] { "crane" } },
        { "RearSuspension", new[] { "crane" } },
        { "BatteryPack", new[] { "hanger", "crane" } },
    };

    /// <summary>필요한 특수공구가 근처에 없으면 경고를 띄우고 true. 시나리오 중에는 시나리오가 공구를 다루므로 쓰지 않는다.</summary>
    public static bool NeedsToolWarning(CarPartService.PartDef d, Vector3 workPos)
    {
        if (d == null || !ToolsOf.TryGetValue(d.id, out var need)) return false;
        var sm = ElectricSafetyManager.Instance;
        if (sm != null && sm.IsScenarioActive) return false;
        var miss = new List<string>();
        // 'crane' 은 크레인 자키, 나머지는 SpecialTool. 목록 중 하나만 있으면 충분(빔 또는 크레인)
        foreach (var id in need)
        {
            bool have = id == "crane" ? NearCrane(workPos, 4f) : SpecialTool.NearTool(id, workPos, 3f);
            if (have) return false;
            miss.Add(SpecialTool.NameOf(id));
        }
        EquipFx.Notice("tool_need_" + d.id, "특수공구 필요 — " + d.name, string.Join(" 또는 ", miss) + " 이(가) 작업 위치 가까이에 있어야 합니다", EquipFx.Warn, 4f, 12f);
        return true;
    }

    private static bool NearCrane(Vector3 pos, float maxDist)
    {
        foreach (var c in ShopCrane.All) if (c != null && (c.transform.position - pos).sqrMagnitude <= maxDist * maxDist) return true;
        return false;
    }

    // ── 냉각 회로 공기 ──
    private static readonly HashSet<string> Coolant = new HashSet<string>
    {
        "ThreeWayValveMotor", "ThreeWayValveBattery", "PtcHeatPump", "CoolantManifold",
        "EwpMotor", "EwpBattery1", "EwpBattery2", "MotorOilCoolerFront", "MotorOilCoolerRear", "WaterCondenser", "Cooling",
    };

    public static bool IsCoolantCircuit(string partId) => partId != null && Coolant.Contains(partId);

    public const string CoolantAirId = "veh/coolantair";

    /// <summary>0 정상 / 1 냉각 회로가 열려 있음(부품이 빠져 있음) / 2 조립됐으나 에어빼기 필요</summary>
    public static int CoolantAirValue { get; private set; }
    public static bool NeedsBleed => CoolantAirValue > 0;

    private class AirState : IVehicleState
    {
        public string StateId => CoolantAirId;
        public void ApplyState(float v)
        {
            int nv = Mathf.Clamp(Mathf.RoundToInt(v), 0, 2);
            int old = CoolantAirValue;
            CoolantAirValue = nv;
            if (nv == old) return;
            if (nv == 1)
                EquipFx.Notice("coolant_air_open", "냉각 회로 개방", "냉각수 라인을 열었습니다 — 다시 조립한 뒤 GDS [부가 기능 → 냉각수 에어빼기]를 해야 합니다", EquipFx.Warn, 4f, 3f);
            else if (nv == 2)
                EquipFx.Notice("coolant_air_need", "에어빼기 필요", "냉각 회로를 다시 닫았습니다 — 리저버를 MAX 까지 채우고 GDS 에서 에어빼기를 실행하세요", EquipFx.Warn, 4.5f, 3f);
            else
                EquipFx.Notice("coolant_air_done", "에어빼기 완료", "냉각 회로 공기가 빠졌습니다", EquipFx.Info, 3f, 3f);
        }
    }

    // ── 교환 후 학습·조정 필요 ──
    // 부품을 갈고 나면 진단기로 학습·영점 조정을 해야 한다(매뉴얼 '교환 후 캘리브레이션'). 안 하면 목록·GDS 에 계속 '필요'로 남는다.
    private static readonly Dictionary<string, string> LearnOf = new Dictionary<string, string>
    {
        { "EpbActuatorL", "epb" }, { "EpbActuatorR", "epb" }, { "BrakeRL", "epb" }, { "BrakeRR", "epb" },
        { "Ieb", "ieb" },
        { "SteeringRack", "mdps" }, { "SteeringColumn", "mdps" },
    };
    private static readonly Dictionary<string, string> LearnName = new Dictionary<string, string>
    {
        { "epb", "EPB 브레이크 패드 교체모드" }, { "ieb", "IEB 영점 설정" }, { "mdps", "MDPS 튜닝 사양 설정" },
    };
    private static readonly Dictionary<string, int> s_learn = new Dictionary<string, int>();
    private static readonly List<LearnState> s_learnStates = new List<LearnState>();

    /// <summary>이 GDS 기능(epb/ieb/mdps)이 교환 후 필요한 상태인가</summary>
    public static bool LearnNeeded(string proc) => s_learn.TryGetValue(proc, out var v) && v > 0;

    /// <summary>학습이 필요한 기능 이름들(없으면 null)</summary>
    public static string PendingLearnNames()
    {
        string s = null;
        foreach (var kv in LearnName) if (LearnNeeded(kv.Key)) s = s == null ? kv.Value : s + ", " + kv.Value;
        return s;
    }

    private class LearnState : IVehicleState
    {
        public string proc;
        public string StateId => "veh/learn/" + proc;
        public void ApplyState(float v)
        {
            int nv = v > 0.5f ? 1 : 0;
            s_learn.TryGetValue(proc, out int old);
            s_learn[proc] = nv;
            if (nv == old) return;
            string n = LearnName.TryGetValue(proc, out var nm) ? nm : proc;
            if (nv == 1) EquipFx.Notice("learn_" + proc, n + " 필요", "부품을 교환했습니다 — GDS [부가 기능 → " + n + "]를 실행해야 합니다", EquipFx.Warn, 4.5f, 3f);
            else EquipFx.Notice("learn_ok_" + proc, n + " 완료", "학습값이 저장되었습니다", EquipFx.Info, 3f, 3f);
        }
    }

    /// <summary>GDS 에서 학습·조정을 마쳤다(실행한 사람 한 명이 보낸다)</summary>
    public static void OnLearnDone(string proc)
    {
        if (LearnNeeded(proc)) VehicleStateSync.Request("veh/learn/" + proc, 0f);
    }

    private static AirState s_air;
    private static bool s_registered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_air = null; s_registered = false; CoolantAirValue = 0; ReuseCount = 0;
        s_learn.Clear(); s_learnStates.Clear();
    }

    public static void EnsureRegistered()
    {
        if (s_registered && s_air != null) return;
        s_air = new AirState();
        s_registered = true;
        VehicleStateSync.Register(s_air);
        s_learnStates.Clear();
        foreach (var proc in LearnName.Keys)
        {
            var st = new LearnState { proc = proc };
            s_learnStates.Add(st);
            VehicleStateSync.Register(st);
        }
    }

    /// <summary>[서버] 교환 후 학습이 필요한 부위가 다시 체결됐다</summary>
    private static void ServerLearnCheck(string partId)
    {
        if (partId == null || !LearnOf.TryGetValue(partId, out var proc)) return;
        EnsureRegistered();
        if (!LearnNeeded(proc)) VehicleStateSync.Request("veh/learn/" + proc, 1f);
    }

    /// <summary>[서버] 냉각 회로 부품이 떨어졌다 → 회로 열림</summary>
    public static void ServerPartRemoved(string partId)
    {
        if (!IsCoolantCircuit(partId)) return;
        EnsureRegistered();
        if (CoolantAirValue != 1) VehicleStateSync.Request(CoolantAirId, 1f);
    }

    /// <summary>[서버] 냉각 회로 부품이 다시 체결됐다. 빠진 냉각 부품이 하나도 없으면 '에어빼기 필요'로.</summary>
    public static void ServerPartTightened(string partId, System.Func<string, bool> isStillOff)
    {
        ServerLearnCheck(partId);
        if (!IsCoolantCircuit(partId) || CoolantAirValue != 1) return;
        foreach (var id in Coolant) if (isStillOff(id)) return;
        VehicleStateSync.Request(CoolantAirId, 2f);
    }

    /// <summary>[서버] 전부 원래대로 되돌릴 때</summary>
    public static void ServerClear()
    {
        if (CoolantAirValue != 0) VehicleStateSync.Request(CoolantAirId, 0f);
        foreach (var proc in LearnName.Keys) if (LearnNeeded(proc)) VehicleStateSync.Request("veh/learn/" + proc, 0f);
    }

    /// <summary>GDS 에어빼기가 끝났다(자유 실습). 공기가 있었다면 해소하고, 빠진 공기만큼 리저버 수위가 내려간다.</summary>
    public static void OnBleedDone()
    {
        if (CoolantAirValue == 0) return;
        if (CoolantAirValue == 1)
        {
            // 부품이 빠진 채로 펌프를 돌려도 공기는 안 빠진다
            EquipFx.Notice("coolant_air_open2", "에어빼기 불가", "냉각 회로에 빠진 부품이 있습니다 — 먼저 모두 조립하세요", EquipFx.Warn, 3.5f, 3f);
            return;
        }
        VehicleStateSync.Request(CoolantAirId, 0f);
        VehicleStateSync.Request("veh/coolantlevel", Mathf.Max(0.05f, CoolantReservoir.MotorLevel - 0.2f));
    }
}
