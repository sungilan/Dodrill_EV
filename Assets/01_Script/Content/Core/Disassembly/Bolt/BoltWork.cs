using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  BoltWork.cs
//
//  볼트 찾기·지금 단계 볼트인가 확인·윤활 상태와 풀림 속도 (EquipFx.cs 에서 분리, 11/20)
// ============================================================

/// <summary>볼트 관련 공통 처리 — 지금 단계 확인, 윤활 상태, 풀림 속도.</summary>
public static class BoltWork
{
    private static readonly List<Bolt> s_bolts = new List<Bolt>();
    private static float s_nextScan;

    /// <summary>점 p 에서 radius 안의 가장 가까운(아직 다 안 된) 볼트.</summary>
    public static Bolt Nearest(Vector3 p, float radius)
    {
        if (Time.unscaledTime >= s_nextScan)
        {
            s_nextScan = Time.unscaledTime + 1f;
            s_bolts.Clear();
            s_bolts.AddRange(Object.FindObjectsByType<Bolt>(FindObjectsSortMode.None));
        }
        Bolt best = null; float bd = radius * radius;
        foreach (var b in s_bolts)
        {
            if (b == null || !b.isActiveAndEnabled || b.isBroken) continue;
            float d = (b.transform.position - p).sqrMagnitude;
            if (d < bd) { bd = d; best = b; }
        }
        return best;
    }

    /// <summary>볼트들 중 점 p 에서 dir 방향 원뿔(angle) 안, range 안에 있는 것.</summary>
    public static void InCone(Vector3 p, Vector3 dir, float range, float angle, List<Bolt> outList)
    {
        outList.Clear();
        Nearest(p, 0f);   // 목록 갱신
        float cos = Mathf.Cos(angle * Mathf.Deg2Rad);
        foreach (var b in s_bolts)
        {
            if (b == null || !b.isActiveAndEnabled) continue;
            Vector3 v = b.transform.position - p;
            float d = v.magnitude;
            if (d > range || d < 1e-4f) continue;
            if (Vector3.Dot(v / d, dir) >= cos) outList.Add(b);
        }
    }

    /// <summary>이 볼트가 지금 단계(시나리오)의 작업 대상인가. ImpactWrench 와 같은 판정.</summary>
    public static bool IsCurrentStep(Bolt bolt)
    {
        if (bolt == null) return false;
        if (bolt.GetComponent<FreeWorkBolt>() != null) return true;   // 타이어 휠 너트 등 자유 실습
        var group = bolt.GetComponentInParent<BoltGroupCounter>();
        if (group == null) return false;
        var recv = ScenarioStateReceiver.Instance;
        var data = recv != null ? recv.CurrentScenario : null;
        if (data == null || data.scenario == null || data.scenario.tasks == null) return false;
        int i = recv.CurrentTaskState.taskIndex;
        if (i < 0 || i >= data.scenario.tasks.Count) return false;
        string cur = data.scenario.tasks[i].moduleId ?? "";
        return group.taskId != null && group.taskId.Trim().Equals(cur.Trim(), System.StringComparison.OrdinalIgnoreCase);
    }

    // ── 윤활 ─────────────────────────────

    private const float LubeSeconds = 240f;
    private static readonly Dictionary<Bolt, float> s_lubedUntil = new Dictionary<Bolt, float>();

    /// <summary>윤활 방청제를 뿌렸다. 처음이면 true.</summary>
    public static bool Lubricate(Bolt b)
    {
        if (b == null) return false;
        bool first = !IsLubed(b);
        s_lubedUntil[b] = Time.time + LubeSeconds;
        return first;
    }

    public static bool IsLubed(Bolt b) => b != null && s_lubedUntil.TryGetValue(b, out var u) && Time.time < u;

    /// <summary>
    /// 녹슬어 뻑뻑한 볼트 — 볼트 묶음마다 일부(형제 순서로 정해 모든 플레이어가 같은 볼트). 분해할 때만.
    /// 윤활제를 뿌리면 오히려 잘 풀린다.
    /// </summary>
    // (11/20) 자유 탈거(연습) 모드에서만 — 시나리오에서는 녹슨 볼트를 만들지 않는다. 시나리오에는 방청제 도구가 없는데 볼트 4개 중 1개가
    // 0.35배 속도로 풀려 'F 를 눌러도 안 풀린다'(0%에 머문다)로 보였고, 갈색 안내만 뜨고 해결할 방법이 없었다.
    public static bool IsSeized(Bolt b) => b != null && !b.isAssembleMode && FreeLoto.IsFreeScenario && b.transform.GetSiblingIndex() % 4 == 1;

    /// <summary>풀림 속도 배율: 윤활 1.6배, 녹슨 볼트 0.35배, 보통 1배.</summary>
    public static float SpeedFactor(Bolt b)
    {
        if (IsLubed(b)) return 1.6f;
        return IsSeized(b) ? 0.35f : 1f;
    }
}
