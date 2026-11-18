using System.Collections.Generic;
using UnityEngine;

// ============================================================
//  CarPartLube.cs
//
//  자유 실습(CarPartService)에 정비 매뉴얼의 '도포(구리스)' 규칙을 얹는다.
//   · 매뉴얼에서 부품을 다시 끼우기 전에 그리스를 바르라고 한 부위를 표(Of)로 둔다 — 부위 id → 재료·규정·도포 지점(볼트 번호).
//   · 부품을 제자리에 끼우면(장착 상태) 그 부위에 노란 도포 점이 뜬다. 구리스 스프레이(SprayTool.Kind.Grease)로 점을 향해 뿌리면
//     점마다 도포량이 오르고 색이 바뀐다: 노랑(안 발림) → 연노랑(바르는 중) → 초록(적정, 1.0~1.35) → 빨강(과다, 1.35 초과).
//   · 도포량은 서버(CarPartService._lube)가 모두에게 같게 기억한다. 부품을 떼거나 끼우는 순간 0 으로 돌아간다.
//   · 도포하지 않거나 모자란 채 볼트를 다 조이면 경고하고 횟수를 센다(막지는 않는다 — 자유 실습에는 점수가 없다). 과다도 센다.
//  근거(정비지침서): 브레이크 패드 그리스 Daikalub 528D(앞 0.1~0.3g, 뒤 최소 0.1g), IDA 스플라인 CASMOLY CSG-101M 0.5~1.0g.
//  도포 지점은 모델에 접촉면이 따로 없어서 각 부위의 볼트 자리를 쓴다(근사). 재료(실란트·냉동유 O-링 등)를 늘리려면 Of 에 줄을 더하면 된다.
// ============================================================
public static class CarPartLube
{
    public class Info
    {
        public string what;      // 재료
        public string spec;      // 양·자리 안내
        public int[] bolts;      // 도포 지점으로 쓸 볼트 번호(null = 모두)
    }

    public const float Ok = 1.0f, Max = 1.35f, Cap = 1.8f;   // 적정 범위·최대

    private static Info Brake(bool front) => new Info
    {
        what = "브레이크 그리스 Daikalub 528D",
        spec = front ? "패드 접촉부 0.1~0.3g" : "패드 리테이너 접촉부 최소 0.1g",
    };

    private static readonly Dictionary<string, Info> Of = new Dictionary<string, Info>
    {
        { "BrakeFL", Brake(true) }, { "BrakeFR", Brake(true) },
        { "BrakeRL", Brake(false) }, { "BrakeRR", Brake(false) },
        { "FrontDriveShaftL", new Info { what = "스플라인 그리스 CASMOLY CSG-101M", spec = "감속기 쪽 스플라인 0.5~1.0g" } },
        { "FrontDriveShaftR", new Info { what = "스플라인 그리스 CASMOLY CSG-101M", spec = "감속기 쪽 스플라인 0.5~1.0g" } },
    };

    public static Info InfoOf(string partId) => partId != null && Of.TryGetValue(partId, out var i) ? i : null;

    /// <summary>도포 지점 수(볼트 번호 목록 길이, 없으면 0)</summary>
    public static int SpotCount(CarPartService.PartDef d)
    {
        var i = InfoOf(d?.id);
        if (i == null || d.bolts == null) return 0;
        return i.bolts != null ? i.bolts.Length : Mathf.Min(d.bolts.Length, 3);
    }

    /// <summary>k 번째 도포 지점이 쓰는 볼트 번호</summary>
    public static int BoltOf(CarPartService.PartDef d, int k)
    {
        var i = InfoOf(d?.id);
        return i != null && i.bolts != null ? i.bolts[k] : k;
    }

    // ── 안내·집계(내가 끼운 것만) ──
    public static int MissCount { get; private set; }
    public static int ExcessCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { MissCount = 0; ExcessCount = 0; }

    /// <summary>부품이 제자리에 얹혔다 — 도포하라고 알린다.</summary>
    public static void NoteMounted(CarPartService.PartDef d)
    {
        var i = InfoOf(d?.id);
        if (i == null) return;
        EquipFx.Notice("lube_need_" + d.id, "도포 필요 — " + d.name,
            $"{i.what} ({i.spec}) — 노란 점에 구리스 스프레이를 뿌린 뒤 볼트를 조이세요", EquipFx.Warn, 4.5f, 6f);
    }

    /// <summary>볼트를 다 조였다 — 도포 결과를 알린다. ok = 적정인 지점, excess = 과다인 지점.</summary>
    public static void NoteTightened(CarPartService.PartDef d, int ok, int total, int excess)
    {
        var i = InfoOf(d?.id);
        if (i == null || total <= 0) return;
        int under = total - ok - excess;
        if (under > 0)
        {
            MissCount++;
            EquipFx.Notice("lube_miss_" + d.id, "도포 누락", $"{d.name}: {i.what} 를 바르지 않았거나 모자랍니다({ok}/{total}) — 매뉴얼은 장착 전 도포를 요구합니다", EquipFx.Danger, 4.5f, 6f);
        }
        else if (excess > 0)
        {
            ExcessCount++;
            EquipFx.Notice("lube_excess_" + d.id, "도포 과다", $"{d.name}: {i.what} 를 너무 많이 발랐습니다 — 규정량({i.spec})만 바르세요", EquipFx.Warn, 4f, 6f);
        }
        else
            EquipFx.Notice("lube_ok_" + d.id, "도포 적정", $"{d.name}: {i.what} 도포가 적정합니다", EquipFx.Info, 2.5f, 6f);
    }
}
