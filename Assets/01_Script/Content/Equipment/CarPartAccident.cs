using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  CarPartAccident.cs
//  자유 탈거 모드의 '부품 사고' 규칙 — 기획서 사고 시스템 중 부품과 관련된 것.
//
//   ① 대형 부품 낙하: 배터리 팩·모터·서스펜션 서브프레임은 크레인·배터리 리프트로 받치지 않은 채 마지막 볼트를 풀면
//      그대로 떨어진다 → 메시가 여러 조각으로 부서지고(AccidentFx.Shatter), 부품 파손(새 부품 필요), 배터리면 열폭주 화재.
//   ② 충격: 떼어낸 배터리 부품·대형 부품을 들고 있다 세게 던지거나 높은 데서 떨어뜨려도 같다(CarPartItem 이 속도를 잰다).
//   ③ 냉각수 누출: 냉각 회로 부품을 떼면 바닥에 냉각수가 쏟아진다 → 닦지 않고 두면 감점, 오래 두면 배터리 열폭주.
//  (이 사고들은 자유 실습에서만 — 교육·평가 시나리오는 시나리오가 사고 판정을 한다.)
// ============================================================
public static class CarPartAccident
{
    // 받침(크레인·리프트) 없이 떼면 떨어지는 대형 부품
    private static readonly HashSet<string> Heavy = new HashSet<string>
    {
        "BatteryPack", "FrontMotor", "RearMotor", "FrontSuspension", "RearSuspension",
    };
    // 부서지면 열폭주(불)가 나는 고전압 배터리 부품
    private static readonly HashSet<string> Battery = new HashSet<string>
    {
        "BatteryPack", "BatteryTopCase", "Bmu", "MainFuse", "Pra",
    };

    public static IEnumerable<string> DamageableIds
    {
        get { foreach (var h in Heavy) yield return h; foreach (var b in Battery) if (!Heavy.Contains(b)) yield return b; }
    }

    public static bool IsHeavy(string id) => id != null && Heavy.Contains(id);
    public static bool IsBatteryPart(string id) => id != null && Battery.Contains(id);
    public static bool IsDamageable(string id) => IsHeavy(id) || IsBatteryPart(id);

    /// <summary>충격(던짐·높은 데서 낙하)으로 부서지는 속도(m/s). 손 높이(약 1.2m)에서 떨어뜨리면 4.8 m/s.</summary>
    public const float BreakSpeed = 4.2f;

    /// <summary>pos 근처에 크레인 자키나 배터리 리프트가 있어 대형 부품을 받치고 있는가</summary>
    public static bool Supported(Vector3 pos)
    {
        foreach (var c in ShopCrane.All)
            if (c != null && (c.transform.position - pos).sqrMagnitude <= 4f * 4f) return true;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            string n = t.name;
            if (!(n.StartsWith("Equip_BatteryLift") || n.StartsWith("Equip_ShopCrane"))) continue;
            Vector3 d = t.position - pos; d.y = 0f;
            if (d.sqrMagnitude <= 3.5f * 3.5f) return true;
        }
        return false;
    }

    // ── 클라이언트: 부서지는 연출 ──
    /// <summary>모든 기기에서 — 부품 메시를 조각내 흩뿌리고, 배터리면 열폭주 화재 사고를 낸다.</summary>
    public static void PlayShatter(CarPartService svc, int p, Vector3 pos, Quaternion rot, bool fire, string why)
    {
        if (svc == null || p < 0 || p >= CarPartService.Defs.Length) return;
        var def = CarPartService.Defs[p];
        var root = new GameObject("[ShatterRoot]");
        root.transform.SetPositionAndRotation(pos, rot);
        if (svc.BuildCopy(p, root.transform, null, out _))
        {
            AccidentFx.PlayCrash(pos);
            AccidentFx.Shatter(root.transform, pos, fire ? 4.5f : 3.2f, fire ? 36 : 26);
        }
        else Object.Destroy(root);

        CarPartRules.NoteDropDamage();
        MentorDialogue.Say(fire ? "accident_fire" : "accident_drop", 20f);
        EquipFx.Notice("drop_" + def.id, $"부품 파손 — {def.name}", why + " (감점 -10 · 신품 부품 보관함에서 새 부품을 받아야 합니다)", EquipFx.Danger, 6f, 2f);

        if (fire)
        {
            var em = EmergencyEventManager.Instance;
            string msg = $"{def.name}: {why}";
            if (em != null) em.TriggerAccident(EmergencyEventManager.AccidentType.ThermalRunaway, msg, pos + Vector3.up * 0.15f);
            else AccidentFx.Fire(pos + Vector3.up * 0.15f);   // 사고 매니저가 없으면 불만이라도
        }
    }

    // ── 냉각수 누출 ──
    private static readonly Dictionary<int, GameObject> s_pools = new Dictionary<int, GameObject>();

    /// <summary>모든 기기에서 — 바닥에 냉각수가 쏟아진다. 보는 사람이 가까우면 화면 가장자리에 얼룩이 번진다.</summary>
    public static void PlaySpill(CarPartService svc, int spillId, Vector3 pos, bool battery, System.Action onCleaned)
    {
        Color liquid = battery ? new Color(0.25f, 0.65f, 1f, 0.78f) : new Color(0.3f, 0.85f, 0.95f, 0.78f);
        var go = AccidentFx.Pool(pos, battery ? 0.85f : 0.7f, liquid, "냉각수 웅덩이", 3, () => { s_pools.Remove(spillId); onCleaned?.Invoke(); });
        s_pools[spillId] = go;
        var cam = Camera.main;
        if (cam != null && (cam.transform.position - pos).sqrMagnitude < 3.5f * 3.5f) AccidentFx.ScreenStain(new Color(0.35f, 0.75f, 1f), 9f);
        MentorDialogue.Say("spill", 90f);
        EquipFx.Notice("spill", "냉각수 누출", "바닥에 냉각수가 쏟아졌습니다 — 미끄러짐·오염 위험. 웅덩이를 눌러 닦아내세요 (방치하면 감점)", EquipFx.Warn, 5f, 4f);
    }

    /// <summary>다른 사람이 닦았을 때 — 웅덩이를 치운다</summary>
    public static void RemoveSpill(int spillId)
    {
        if (s_pools.TryGetValue(spillId, out var go)) { if (go != null) Object.Destroy(go); s_pools.Remove(spillId); }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { s_pools.Clear(); }
}
