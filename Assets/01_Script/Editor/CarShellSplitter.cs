#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// ============================================================
//  CarShellSplitter.cs  (에디터 전용, CarBodyPanelSplitter 의 2단계)
//
//  1단계(CarBodyPanelSplitter)가 펜더·미러·스포일러 같은 외장 부품을 떼고 나면 SM_Ioniq_Electric 에는
//  루프·필러·카울·플로어·센터 콘솔·휠하우스·뒷선반 같은 내장/구조물이 한 덩어리(조각 하나)로 용접돼 남는다.
//  → 모서리 각도(crease) 기준으로 삼각형을 '패치'로 나누고(자르지 않으므로 틈이 없다),
//    패치의 위치·크기·방향으로 정비지침서 구성부품(플로어 콘솔·필러 트림·루프 트림 …)에 배정한다.
//  → 떼어 낸 부품과 남는 자리에 생긴 열린 가장자리는 평평한 뚜껑(cap)으로 막는다(CloseLoops).
//  → 어디에도 배정되지 않은 구조물은 SM_BodyShell(차체 골격) 하나로 이름을 붙인다 — 'SM_Ioniq_Electric' 은 없어진다.
//  메뉴: Dodrill > 차량 메시 분리 > 차체 셸(내장·구조) …
// ============================================================
public static partial class CarBodyPanelSplitter
{
    public const string ShellNode = "SM_BodyShell";

    public class Patch
    {
        public int id, sub, tris, open; public float area; public Bounds b; public Vector3 c, n;
        public List<int> t = new List<int>();
        public string node;
    }

    public class ShellRule
    {
        public string node, name; public Color dbg; public Func<Patch, bool> match;
        public bool twoSided = true; public bool close = false;
        public H[] clipHs; public Bounds clipBox;          // 상자 평면으로 삼각형을 잘라 안쪽을 이 부품으로(패치가 다른 부품과 한 덩어리일 때)
        public Func<Patch, bool> clipFrom;                  // 어떤 패치를 자를 수 있나(없으면 상자에 걸친 모든 패치)
        public Func<Vector3, bool> closeAt;                 // 뚜껑을 덮을 고리의 중심 조건(없으면 close 일 때 모든 고리)
    }

    internal class Seg
    {
        public Acc body; public int S; public int nt;
        public int[] tS, tA, tB, tC; public Vector3[] fn; public float[] ar;
        public int[] wid; public List<Patch> patches; public int[] patchOf;
        public Dictionary<long, List<int>> edges;
    }

    private static long EK(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    // ── 패치 만들기 ──
    internal static Seg Segment(Acc body, float creaseDeg, int minTris)
    {
        var sg = new Seg { body = body, S = body.sub.Length };
        sg.nt = body.sub.Sum(l => l.Count / 3);
        sg.tS = new int[sg.nt]; sg.tA = new int[sg.nt]; sg.tB = new int[sg.nt]; sg.tC = new int[sg.nt]; sg.fn = new Vector3[sg.nt]; sg.ar = new float[sg.nt];
        int k = 0;
        for (int s = 0; s < sg.S; s++)
            for (int i = 0; i < body.sub[s].Count; i += 3)
            { sg.tS[k] = s; sg.tA[k] = body.sub[s][i]; sg.tB[k] = body.sub[s][i + 1]; sg.tC[k] = body.sub[s][i + 2]; k++; }

        var weld = new Dictionary<Vector3Int, int>(); sg.wid = new int[body.v.Count];
        for (int i = 0; i < body.v.Count; i++)
        {
            var key = Vector3Int.RoundToInt(body.v[i].w * 2000f);
            if (!weld.TryGetValue(key, out int w)) { w = weld.Count; weld[key] = w; }
            sg.wid[i] = w;
        }
        sg.edges = new Dictionary<long, List<int>>();
        for (int t = 0; t < sg.nt; t++)
        {
            var a = body.v[sg.tA[t]].w; var b = body.v[sg.tB[t]].w; var c = body.v[sg.tC[t]].w;
            var cr = Vector3.Cross(b - a, c - a); sg.ar[t] = cr.magnitude * 0.5f; sg.fn[t] = sg.ar[t] > 1e-12f ? cr.normalized : Vector3.up;
            int[] ids = { sg.wid[sg.tA[t]], sg.wid[sg.tB[t]], sg.wid[sg.tC[t]] };
            for (int e = 0; e < 3; e++)
            {
                int p = ids[e], q = ids[(e + 1) % 3]; if (p == q) continue;
                long key = EK(p, q);
                if (!sg.edges.TryGetValue(key, out var l)) sg.edges[key] = l = new List<int>(2);
                l.Add(t);
            }
        }
        var par = Enumerable.Range(0, sg.nt).ToArray();
        float cosT = Mathf.Cos(creaseDeg * Mathf.Deg2Rad);
        foreach (var kv in sg.edges)
        {
            if (kv.Value.Count != 2) continue;
            int a = kv.Value[0], b = kv.Value[1];
            if (sg.tS[a] != sg.tS[b]) continue;
            if (Vector3.Dot(sg.fn[a], sg.fn[b]) >= cosT) par[Find(par, a)] = Find(par, b);
        }
        // 작은 패치는 가장 많이 맞닿은 이웃에 흡수(두 번)
        for (int pass = 0; pass < 2; pass++)
        {
            var cnt = new Dictionary<int, int>(); foreach (var r in Enumerable.Range(0, sg.nt)) { int rt = Find(par, r); cnt[rt] = cnt.TryGetValue(rt, out int c0) ? c0 + 1 : 1; }
            var best = new Dictionary<int, Dictionary<int, int>>();
            foreach (var kv in sg.edges)
            {
                if (kv.Value.Count < 2) continue;
                int ra = Find(par, kv.Value[0]), rb = Find(par, kv.Value[1]);
                if (ra == rb) continue;
                if (cnt[ra] < minTris) { if (!best.TryGetValue(ra, out var d)) best[ra] = d = new Dictionary<int, int>(); d[rb] = d.TryGetValue(rb, out int n0) ? n0 + 1 : 1; }
                if (cnt[rb] < minTris) { if (!best.TryGetValue(rb, out var d)) best[rb] = d = new Dictionary<int, int>(); d[ra] = d.TryGetValue(ra, out int n0) ? n0 + 1 : 1; }
            }
            foreach (var kv in best)
            {
                int target = kv.Value.OrderByDescending(x => cnt[x.Key]).ThenByDescending(x => x.Value).First().Key;
                par[Find(par, kv.Key)] = Find(par, target);
            }
        }
        // 패치 통계
        sg.patchOf = new int[sg.nt]; sg.patches = new List<Patch>();
        var map = new Dictionary<int, Patch>();
        for (int t = 0; t < sg.nt; t++)
        {
            int r = Find(par, t);
            if (!map.TryGetValue(r, out var p)) { map[r] = p = new Patch { sub = sg.tS[t], id = map.Count }; sg.patches.Add(p); p.b = new Bounds(body.v[sg.tA[t]].w, Vector3.zero); }
            p.t.Add(t); p.tris++; p.area += sg.ar[t]; p.n += sg.fn[t] * sg.ar[t];
            p.c += (body.v[sg.tA[t]].w + body.v[sg.tB[t]].w + body.v[sg.tC[t]].w) / 3f * sg.ar[t];
            p.b.Encapsulate(body.v[sg.tA[t]].w); p.b.Encapsulate(body.v[sg.tB[t]].w); p.b.Encapsulate(body.v[sg.tC[t]].w);
            sg.patchOf[t] = p.id;
        }
        foreach (var p in sg.patches) { p.c = p.area > 1e-9f ? p.c / p.area : p.b.center; p.n = p.n.sqrMagnitude > 1e-12f ? p.n.normalized : Vector3.up; }
        foreach (var kv in sg.edges) if (kv.Value.Count == 1) sg.patches[sg.patchOf[kv.Value[0]]].open++;
        sg.patches = sg.patches.OrderByDescending(p => p.area).ToList();
        var re = new Dictionary<int, int>(); for (int i = 0; i < sg.patches.Count; i++) { re[sg.patches[i].id] = i; sg.patches[i].id = i; }
        for (int t = 0; t < sg.nt; t++) sg.patchOf[t] = re[sg.patchOf[t]];
        return sg;
    }

    internal static void Assign(Seg sg, List<ShellRule> rules)
    {
        foreach (var p in sg.patches)
        {
            p.node = null;
            foreach (var r in rules) if (r.match(p)) { p.node = r.node; break; }
        }
    }

    internal static Dictionary<string, Acc> Extract(Seg sg, List<ShellRule> rules)
    {
        var d = new Dictionary<string, Acc>();
        Acc Get(string key) { if (!d.TryGetValue(key, out var a)) d[key] = a = new Acc(key, sg.S); return a; }
        var poly = new List<V>(3); var inside = new List<V>(); var outside = new List<V>();
        var clipRules = rules.Where(r => r.clipHs != null).ToList();
        foreach (var p in sg.patches)
        {
            string key = p.node ?? ShellNode;
            var cr = clipRules.Where(r => r.node != key && p.b.Intersects(r.clipBox) && (r.clipFrom == null || r.clipFrom(p))).ToList();
            foreach (int t in p.t)
            {
                poly.Clear(); poly.Add(sg.body.v[sg.tA[t]]); poly.Add(sg.body.v[sg.tB[t]]); poly.Add(sg.body.v[sg.tC[t]]);
                if (cr.Count == 0) { Get(key).Poly(poly, sg.tS[t]); continue; }
                var pending = new List<List<V>> { new List<V>(poly) };
                foreach (var r in cr)
                {
                    var next = new List<List<V>>();
                    foreach (var pc in pending)
                    {
                        var cur = pc; bool any = true;
                        foreach (var h in r.clipHs)
                        {
                            Clip(cur, h, inside, outside);
                            if (outside.Count >= 3) next.Add(new List<V>(outside));
                            if (inside.Count < 3) { any = false; break; }
                            cur = new List<V>(inside);
                        }
                        if (any) Get(r.node).Poly(cur, sg.tS[t]);
                    }
                    pending = next;
                }
                foreach (var pc in pending) Get(key).Poly(pc, sg.tS[t]);
            }
        }
        return d;
    }

    // ── 구멍 막기 ──
    // 열린 가장자리(한 삼각형에만 속한 모서리)를 고리로 이어, 평평한 고리는 귀 자르기로, 휜 고리는 중심 부채꼴로 막는다.
    // 뚜껑의 앞면은 고리를 거꾸로 돈 방향 = 이웃 삼각형과 같은 쪽(바깥)을 본다. 이후 양면화되므로 안쪽에서도 보인다.
    internal static int CloseLoops(Acc a, Matrix4x4 rootToLocal, StringBuilder log = null, Func<Vector3, bool> where = null, float maxPlanar = 0.07f, float maxFan = 0.30f, int minLoop = 4, float minPerim = 0.10f, List<(List<Vector3> pts, Vector3 n)> capsOut = null)
    {
        var weld = new Dictionary<Vector3Int, int>(); var rep = new List<int>();
        int W(int vi)
        {
            var key = Vector3Int.RoundToInt(a.v[vi].w * 2000f);
            if (!weld.TryGetValue(key, out int w)) { w = weld.Count; weld[key] = w; rep.Add(vi); }
            return w;
        }
        var dir = new Dictionary<(int, int), int>();
        for (int s = 0; s < a.sub.Length; s++)
            for (int i = 0; i < a.sub[s].Count; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    int p = W(a.sub[s][i + e]), q = W(a.sub[s][i + (e + 1) % 3]); if (p == q) continue;
                    dir[(p, q)] = s;
                }
        var outg = new Dictionary<int, List<int>>();
        foreach (var kv in dir)
            if (!dir.ContainsKey((kv.Key.Item2, kv.Key.Item1)))
            { if (!outg.TryGetValue(kv.Key.Item1, out var l)) outg[kv.Key.Item1] = l = new List<int>(); l.Add(kv.Key.Item2); }
        var used = new HashSet<(int, int)>();
        int caps = 0, skipped = 0;
        foreach (var kv in dir.ToList())
        {
            var e0 = kv.Key;
            if (dir.ContainsKey((e0.Item2, e0.Item1)) || used.Contains(e0)) continue;
            var loop = new List<int> { e0.Item1 }; var subCount = new Dictionary<int, int>();
            int cur = e0.Item2; used.Add(e0); subCount[kv.Value] = 1; bool closed = false;
            for (int guard = 0; guard < 20000; guard++)
            {
                if (cur == e0.Item1) { closed = true; break; }
                loop.Add(cur);
                if (!outg.TryGetValue(cur, out var nx)) break;
                int pick = -1; foreach (int q in nx) if (!used.Contains((cur, q))) { pick = q; break; }
                if (pick < 0) break;
                used.Add((cur, pick)); int sb = dir[(cur, pick)]; subCount[sb] = subCount.TryGetValue(sb, out int c1) ? c1 + 1 : 1;
                cur = pick;
            }
            if (!closed || loop.Count < minLoop) { skipped++; continue; }
            // 뚜껑 방향 = 거꾸로 돈 고리
            loop.Reverse();
            var P = loop.Select(w => a.v[rep[w]].w).ToList();
            Vector3 N = Vector3.zero, C = Vector3.zero; float per = 0f;
            for (int i = 0; i < P.Count; i++) { var p0 = P[i]; var p1 = P[(i + 1) % P.Count]; N += Vector3.Cross(p0, p1); C += p0; per += (p1 - p0).magnitude; }
            C /= P.Count; if (per < minPerim || N.sqrMagnitude < 1e-12f || (where != null && !where(C))) { skipped++; continue; }
            N = N.normalized;
            float rad = P.Max(p => (p - C).magnitude); float dev = P.Max(p => Mathf.Abs(Vector3.Dot(N, p - C)));
            float planar = dev / Mathf.Max(rad, 1e-4f);
            if (planar > maxFan) { skipped++; continue; }
            int sub = subCount.OrderByDescending(x => x.Value).First().Key;
            var nLocal = rootToLocal.MultiplyVector(N).normalized;
            int Add(int w)
            {
                var v = a.v[rep[w]]; v.n = nLocal; v.orig = -1; a.v.Add(v); return a.v.Count - 1;
            }
            var ids = loop.Select(Add).ToList();
            bool fan = planar > maxPlanar || P.Count > 260;
            if (fan)
            {
                var cv = a.v[ids[0]]; cv.w = C; cv.p = rootToLocal.MultiplyPoint3x4(C); a.v.Add(cv); int ci = a.v.Count - 1;
                for (int i = 0; i < ids.Count; i++)
                {
                    int i0 = ids[i], i1 = ids[(i + 1) % ids.Count];
                    AddCapTri(a, sub, ci, i0, i1, N);
                }
            }
            else
            {
                var u = Mathf.Abs(N.y) < 0.9f ? Vector3.Cross(N, Vector3.up).normalized : Vector3.Cross(N, Vector3.right).normalized; var v2 = Vector3.Cross(N, u);
                var pts = P.Select(p => new Vector2(Vector3.Dot(p - C, u), Vector3.Dot(p - C, v2))).ToList();
                float sa = 0f; for (int i = 0; i < pts.Count; i++) { var p0 = pts[i]; var p1 = pts[(i + 1) % pts.Count]; sa += p0.x * p1.y - p1.x * p0.y; }
                var order = Enumerable.Range(0, pts.Count).ToList(); if (sa < 0) order.Reverse();
                foreach (var tri in EarClip(pts, order)) AddCapTri(a, sub, ids[tri.Item1], ids[tri.Item2], ids[tri.Item3], N);
            }
            caps++; capsOut?.Add((new List<Vector3>(P), N));
            log?.AppendLine($"  cap {(fan ? "fan" : "flat")} 고리 {loop.Count}점 둘레 {per:F2}m 평탄도 {planar:F3} @({C.x:F2},{C.y:F2},{C.z:F2})");
        }
        if (log != null && skipped > 0) log.AppendLine($"  (막지 않은 고리 {skipped}개: 너무 작거나 많이 휨)");
        return caps;
    }

    /// <summary>콘솔 바닥 뚜껑(아래를 보는 평평한 고리)을 바닥에도 한 장 깐다 — 콘솔을 떼면 바닥에 뚫린 구멍이 보이던 자리(모델에 바닥이 없었다)</summary>
    internal static int AddFloorPlugs(Acc shell, List<(List<Vector3> pts, Vector3 n)> caps, Matrix4x4 rootToLocal, int sub, float drop = 0.004f)
    {
        int n = 0;
        foreach (var (pts, nrm) in caps)
        {
            if (nrm.y > -0.8f || pts.Count < 3) continue;
            var P = pts.Select(p => new Vector3(p.x, p.y - drop, p.z)).ToList();
            var C = P.Aggregate(Vector3.zero, (a, b) => a + b) / P.Count;
            var u = Vector3.right; var v2 = Vector3.forward;   // 수평 평면 — 위에서 본 (x,z)
            var pts2 = P.Select(p => new Vector2(p.x - C.x, p.z - C.z)).ToList();
            float sa = 0f; for (int i = 0; i < pts2.Count; i++) { var a = pts2[i]; var b = pts2[(i + 1) % pts2.Count]; sa += a.x * b.y - b.x * a.y; }
            var order = Enumerable.Range(0, pts2.Count).ToList(); if (sa < 0) order.Reverse();
            var ids = new List<int>();
            foreach (var p in P)
            {
                PickFloorUV(shell, p, out var uvv, out var uv2v, out var tg);
                var vv = new V { w = p, p = rootToLocal.MultiplyPoint3x4(p), n = rootToLocal.MultiplyVector(Vector3.up).normalized, t = tg, uv = uvv, uv2 = uv2v, orig = -1 };
                shell.v.Add(vv); ids.Add(shell.v.Count - 1);
            }
            foreach (var tri in EarClip(pts2, order)) AddCapTri(shell, sub, ids[tri.Item1], ids[tri.Item2], ids[tri.Item3], Vector3.up);
            n++;
        }
        return n;
    }

    /// <summary>콘솔의 바닥판(아래를 보는 수평 삼각형)을 바닥에 위를 보게 복사한다 — 모델에 콘솔 밑 바닥이 없어서, 콘솔을 떼면 차 밑이 뚫려 보였다</summary>
    internal static int CopyFloorFrom(Acc console, Acc shell, Matrix4x4 rootToLocal, int sub, float maxY = 0.47f, float drop = 0.004f)
    {
        int n = 0; var poly = new List<V>(3);
        for (int s = 0; s < console.sub.Length; s++)
            for (int i = 0; i < console.sub[s].Count; i += 3)
            {
                var a = console.v[console.sub[s][i]]; var b = console.v[console.sub[s][i + 1]]; var c = console.v[console.sub[s][i + 2]];
                if (a.w.y > maxY || b.w.y > maxY || c.w.y > maxY) continue;
                var cr = Vector3.Cross(b.w - a.w, c.w - a.w); if (cr.sqrMagnitude < 1e-12f) continue;
                if (Mathf.Abs(cr.normalized.y) < 0.9f) continue;      // 수평 면만(콘솔 바닥판)
                V Mk(V v) { var w = v.w - new Vector3(0, drop, 0); return new V { w = w, p = rootToLocal.MultiplyPoint3x4(w), n = rootToLocal.MultiplyVector(Vector3.up).normalized, t = v.t, uv = v.uv, uv2 = v.uv2, orig = -1 }; }
                poly.Clear(); poly.Add(Mk(a)); if (cr.y > 0f) { poly.Add(Mk(b)); poly.Add(Mk(c)); } else { poly.Add(Mk(c)); poly.Add(Mk(b)); }   // 위를 보게
                shell.Poly(poly, sub); n++;
            }
        return n;
    }

    /// <summary>콘솔 밑이 뚫려 있다 — 바닥 가까운 정점들의 (x,z) 볼록 껍질로 바닥판을 만들어 콘솔 밑에 붙이고, 같은 판을 바닥에도 깐다(콘솔을 떼도 차 밑이 안 보이게)</summary>
    internal static string AddBottomPlate(Acc console, Acc shell, Matrix4x4 rootToLocal, int sub, float nearY = 0.50f, float drop = 0.004f)
    {
        var pts = new List<Vector2>(); float minY = float.MaxValue;
        foreach (var l in console.sub) foreach (int i in l) { var w = console.v[i].w; if (w.y < nearY) { pts.Add(new Vector2(w.x, w.z)); minY = Mathf.Min(minY, w.y); } }
        if (pts.Count < 3) return "바닥판 없음(정점 부족)";
        pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        float Cr(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        var hull = new List<Vector2>();
        foreach (var p in pts) { while (hull.Count >= 2 && Cr(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
        int lower = hull.Count + 1;
        for (int i = pts.Count - 2; i >= 0; i--) { var p = pts[i]; while (hull.Count >= lower && Cr(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(p); }
        hull.RemoveAt(hull.Count - 1);
        if (hull.Count < 3) return "바닥판 없음(껍질 퇴화)";
        var c2 = hull.Aggregate(Vector2.zero, (a, b) => a + b) / hull.Count;
        V Mk(Vector2 q, float y, bool up) { var w = new Vector3(q.x, y, q.y); PickFloorUV(shell, w, out var uvv, out var uv2v, out var tg); return new V { w = w, p = rootToLocal.MultiplyPoint3x4(w), n = rootToLocal.MultiplyVector(up ? Vector3.up : Vector3.down).normalized, t = tg, uv = uvv, uv2 = uv2v, orig = -1 }; }
        var poly = new List<V>(3); int n = 0;
        for (int i = 0; i < hull.Count; i++)
        {
            var a = hull[i]; var b = hull[(i + 1) % hull.Count];
            // 콘솔 밑(아래를 봄)
            poly.Clear(); poly.Add(Mk(c2, minY, false)); poly.Add(Mk(a, minY, false)); poly.Add(Mk(b, minY, false));
            var cr = Vector3.Cross(poly[1].w - poly[0].w, poly[2].w - poly[0].w);
            if (cr.y > 0f) { var t = poly[1]; poly[1] = poly[2]; poly[2] = t; }
            console.Poly(poly, sub);
            // 바닥(위를 봄, 4mm 아래)
            poly.Clear(); poly.Add(Mk(c2, minY - drop, true)); poly.Add(Mk(a, minY - drop, true)); poly.Add(Mk(b, minY - drop, true));
            cr = Vector3.Cross(poly[1].w - poly[0].w, poly[2].w - poly[0].w);
            if (cr.y < 0f) { var t = poly[1]; poly[1] = poly[2]; poly[2] = t; }
            shell.Poly(poly, sub); n++;
        }
        return $"바닥판 {n}장 (y={minY:F3}, 껍질 {hull.Count}점)";
    }

    /// <summary>바닥판의 UV — 새로 만든 면의 UV 가 (0.5,0.5) 이면 재질(알파 클립) 때문에 구멍으로 보였다. 가까운 바닥 정점의 UV 를 빌린다</summary>
    private static void PickFloorUV(Acc shell, Vector3 w, out Vector2 uv, out Vector2 uv2, out Vector4 tan)
    {
        float best = float.MaxValue; uv = Vector2.zero; uv2 = Vector2.zero; tan = new Vector4(1, 0, 0, 1);
        for (int i = 0; i < shell.v.Count; i++)
        {
            var v = shell.v[i]; if (v.orig < 0 || v.w.y < 0.36f || v.w.y > 0.47f || Mathf.Abs(v.w.z) < 0.28f || Mathf.Abs(v.w.z) > 0.62f || v.w.x < -0.9f || v.w.x > 0.7f) continue;   // 중앙 터널의 알파 구멍 무늬를 피해, 바닥 양옆의 꽉 찬 자리에서 UV 를 빌린다
            float d = (v.w.x - w.x) * (v.w.x - w.x) + (v.w.z - w.z) * (v.w.z - w.z);
            if (d < best) { best = d; uv = v.uv; uv2 = v.uv2; tan = v.t; }
        }
    }

    private static void AddCapTri(Acc a, int sub, int i0, int i1, int i2, Vector3 nOut)
    {
        var p0 = a.v[i0].w; var p1 = a.v[i1].w; var p2 = a.v[i2].w;
        var cr = Vector3.Cross(p1 - p0, p2 - p0);
        if (cr.sqrMagnitude < 1e-14f) return;
        if (Vector3.Dot(cr, nOut) < 0f) { int t = i1; i1 = i2; i2 = t; }
        a.sub[sub].Add(i0); a.sub[sub].Add(i1); a.sub[sub].Add(i2); a.tris++;
    }

    private static List<(int, int, int)> EarClip(List<Vector2> pts, List<int> order)
    {
        var res = new List<(int, int, int)>(); var idx = new List<int>(order);
        int guard = 0;
        while (idx.Count > 3 && guard++ < 100000)
        {
            bool cut = false;
            for (int i = 0; i < idx.Count; i++)
            {
                int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                Vector2 A = pts[ia], B = pts[ib], Cc = pts[ic];
                float cz = (B.x - A.x) * (Cc.y - B.y) - (B.y - A.y) * (Cc.x - B.x);
                if (cz <= 1e-10f) continue;
                bool ok = true;
                for (int j = 0; j < idx.Count && ok; j++)
                {
                    int ij = idx[j]; if (ij == ia || ij == ib || ij == ic) continue;
                    var Pp = pts[ij];
                    float d1 = (Pp.x - B.x) * (A.y - B.y) - (A.x - B.x) * (Pp.y - B.y);
                    float d2 = (Pp.x - Cc.x) * (B.y - Cc.y) - (B.x - Cc.x) * (Pp.y - Cc.y);
                    float d3 = (Pp.x - A.x) * (Cc.y - A.y) - (Cc.x - A.x) * (Pp.y - A.y);
                    bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
                    if (!(neg && pos)) ok = false;
                }
                if (!ok) continue;
                res.Add((ia, ib, ic)); idx.RemoveAt(i); cut = true; break;
            }
            if (!cut) { for (int k = 1; k + 1 < idx.Count; k++) res.Add((idx[0], idx[k], idx[k + 1])); idx.Clear(); break; }
        }
        if (idx.Count == 3) res.Add((idx[0], idx[1], idx[2]));
        return res;
    }

    // ═════════════════════════ 패치 분석(덤프) ═════════════════════════
    public static string DumpPatches(string file, float creaseDeg = 35f, int minTris = 10)
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var res = Compute(root, Regions(), SrcName);
            if (res.bodyAcc == null) return res.report;
            var sg = Segment(res.bodyAcc, creaseDeg, minTris);
            var sb = new StringBuilder();
            sb.AppendLine($"남은 차체 {sg.nt}삼각형 → 패치 {sg.patches.Count}개 (crease {creaseDeg}°, 최소 {minTris})");
            foreach (var p in sg.patches)
                sb.AppendLine($"#{p.id} s{p.sub} tris={p.tris} open={p.open} area={p.area:F3} c=({p.c.x:F2},{p.c.y:F2},{p.c.z:F2}) min=({p.b.min.x:F2},{p.b.min.y:F2},{p.b.min.z:F2}) max=({p.b.max.x:F2},{p.b.max.y:F2},{p.b.max.z:F2}) n=({p.n.x:F2},{p.n.y:F2},{p.n.z:F2})");
            File.WriteAllText(file, sb.ToString());
            return $"패치 {sg.patches.Count}개 → {file}";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // ═════════════════════════ 정비지침서 구성부품 규칙 ═════════════════════════
    // 좌표: 앞 −X, 왼쪽 −Z, 위 +Y (차 루트). 패치 범위가 상자 안에 들어오면(여유 tol) 그 부품으로 배정한다. 순서가 중요: 먼저 맞는 규칙이 가져간다.
    private static bool Inside(Patch p, float x0, float y0, float z0, float x1, float y1, float z1, float tol = 0.02f)
        => p.b.min.x >= x0 - tol && p.b.min.y >= y0 - tol && p.b.min.z >= z0 - tol && p.b.max.x <= x1 + tol && p.b.max.y <= y1 + tol && p.b.max.z <= z1 + tol;
    private static bool Cen(Patch p, float x0, float y0, float z0, float x1, float y1, float z1)
        => p.c.x >= x0 && p.c.x <= x1 && p.c.y >= y0 && p.c.y <= y1 && p.c.z >= z0 && p.c.z <= z1;

    // 플로어 콘솔이 앉는 자리(센터 터널 위). 패치가 이 안에 들어오거나, 걸친 패치는 이 상자로 잘라 안쪽을 콘솔로 가져온다
    private static readonly Vector3 ConMin = new Vector3(-1.13f, 0.44f, -0.205f), ConMax = new Vector3(-0.015f, 0.93f, 0.205f);
    private static H[] BoxHs(Vector3 mn, Vector3 mx) => new[] { Xgt(mn.x), Xlt(mx.x), Ygt(mn.y), Ylt(mx.y), Zgt(mn.z), Zlt(mx.z) };
    public static Func<Vector3, bool> ShellCloseAt = c => c.x > -1.25f && c.x < 0.2f && Mathf.Abs(c.z) < 0.32f && c.y > 0.30f && c.y < 1.0f;

    public static List<ShellRule> ShellRules()
    {
        Color Col(float r, float g, float b) => new Color(r, g, b);
        var l = new List<ShellRule>
        {
            // 플로어 콘솔 어셈블리(어퍼 커버·리어 커버·컵 홀더·암레스트)
            new ShellRule { node = "SM_FloorConsole", name = "플로어 콘솔 어셈블리", dbg = Col(0.95f, 0.55f, 0.15f), close = true,
                match = p => Inside(p, ConMin.x, ConMin.y, ConMin.z, ConMax.x, ConMax.y, ConMax.z),
                clipHs = BoxHs(ConMin, ConMax), clipBox = new Bounds((ConMin + ConMax) / 2f, ConMax - ConMin), clipFrom = p => (p.node == null || Array.IndexOf(StructNodes, p.node) >= 0) && p.sub >= 2 },
            // 플로어 카펫 어셈블리
            new ShellRule { node = "SM_FloorCarpet", name = "플로어 카펫 어셈블리", dbg = Col(0.25f, 0.75f, 0.30f),
                match = p => p.n.y > 0.85f && p.area > 0.02f && Cen(p, -1.5f, 0.36f, -0.72f, 1.1f, 0.62f, 0.72f) },
            // 비전 루프(글라스 + 롤러 블라인드)
            new ShellRule { node = "SM_VisionRoof", name = "비전 루프 어셈블리(글라스·롤러 블라인드)", dbg = Col(0.25f, 0.45f, 0.95f),
                match = p => p.area > 0.30f && Cen(p, -0.9f, 1.50f, -0.7f, 1.2f, 1.70f, 0.7f) && Mathf.Abs(p.n.y) > 0.9f },
            // 프런트 필라 트림(A 필라 안쪽)
            new ShellRule { node = "SM_PillarTrimFrontL", name = "프런트 필라 트림(왼쪽)", dbg = Col(0.80f, 0.30f, 0.90f),
                match = p => Cen(p, -1.50f, 0.40f, -0.99f, -1.10f, 1.35f, -0.70f) && p.b.size.z < 0.5f },
            new ShellRule { node = "SM_PillarTrimFrontR", name = "프런트 필라 트림(오른쪽)", dbg = Col(0.60f, 0.20f, 0.70f),
                match = p => Cen(p, -1.50f, 0.40f, 0.70f, -1.10f, 1.35f, 0.99f) && p.b.size.z < 0.5f },
            // 도어 스커프 트림(문턱 위 얇은 판 — 앞·뒤 도어를 이어서 한 장)
            new ShellRule { node = "SM_DoorScuffL", name = "도어 스커프 트림(왼쪽)", dbg = Col(0.95f, 0.80f, 0.15f),
                match = p => p.b.size.x > 1.5f && p.area < 0.4f && Cen(p, -0.9f, 0.26f, -0.95f, 0.5f, 0.38f, -0.78f) },
            new ShellRule { node = "SM_DoorScuffR", name = "도어 스커프 트림(오른쪽)", dbg = Col(0.80f, 0.65f, 0.10f),
                match = p => p.b.size.x > 1.5f && p.area < 0.4f && Cen(p, -0.9f, 0.26f, 0.78f, 0.5f, 0.38f, 0.95f) },
            // 인사이드 미러(룸미러): 윈드쉴드 위쪽 가운데
            new ShellRule { node = "SM_InsideMirror", name = "인사이드 미러 어셈블리", dbg = Col(0.95f, 0.45f, 0.70f), close = true,
                match = p => Inside(p, -1.00f, 1.30f, -0.36f, -0.55f, 1.56f, 0.36f) },
            // 카울 탑 커버(윈드쉴드 아래 띠)
            new ShellRule { node = "SM_CowlTopCover", name = "카울 탑 커버", dbg = Col(0.15f, 0.85f, 0.85f),
                match = p => Cen(p, -1.80f, 0.85f, -0.70f, -1.30f, 1.20f, 0.70f) && p.b.size.z > 0.5f },
        };
        l.AddRange(StructRules());
        return l;
    }


    // ── 차체 구조 패널(매뉴얼에 구성부품이 없는 '차체 자체'를 위치별 이름 있는 판으로) ──
    public static readonly string[] StructNodes =
    {
        "SM_RoofPanel", "SM_BodySideL", "SM_BodySideR", "SM_FloorPan", "SM_Firewall", "SM_FrontApron",
        "SM_WheelHouseFL", "SM_WheelHouseFR", "SM_WheelHouseRL", "SM_WheelHouseRR", "SM_RearBody",
    };

    /// <summary>모든 패치를 하나의 구조 패널로 보내는 전함수(남는 것이 없다). 앞 −X, 왼쪽 −Z, 위 +Y</summary>
    public static string StructName(Patch p)
    {
        var c = p.c; float az = Mathf.Abs(c.z);
        if (c.y > 1.40f && c.x > -1.05f && c.x < 1.45f) return "SM_RoofPanel";
        if (c.x < -1.25f) return (az > 0.5f && c.y < 1.15f) ? (c.z < 0f ? "SM_WheelHouseFL" : "SM_WheelHouseFR") : "SM_FrontApron";
        if (c.x > 0.75f) return (az > 0.45f && c.y < 1.0f) ? (c.z < 0f ? "SM_WheelHouseRL" : "SM_WheelHouseRR") : "SM_RearBody";
        if (c.x < -1.0f) return "SM_Firewall";
        if (az > 0.6f) return c.z < 0f ? "SM_BodySideL" : "SM_BodySideR";
        if (c.y > 1.15f) return "SM_RoofPanel";
        return "SM_FloorPan";
    }

    public static List<ShellRule> StructRules()
    {
        var cols = new[] { new Color(0.35f, 0.55f, 1f), new Color(0.95f, 0.8f, 0.2f), new Color(0.8f, 0.6f, 0.1f), new Color(0.3f, 0.85f, 0.4f), new Color(0.9f, 0.3f, 0.3f), new Color(0.2f, 0.85f, 0.85f),
                           new Color(0.9f, 0.5f, 0.1f), new Color(0.7f, 0.35f, 0.1f), new Color(0.8f, 0.3f, 0.8f), new Color(0.55f, 0.2f, 0.6f), new Color(0.6f, 0.9f, 0.3f) };
        var names = new[] { "루프 패널", "바디 사이드(왼쪽: A·B·C 필라·루프 레일·사이드 실)", "바디 사이드(오른쪽)", "플로어 팬(바닥·터널)", "방화벽(대시 패널)", "프런트 에이프런(프렁크 둘레 구조)",
                            "휠 하우스(앞 왼쪽)", "휠 하우스(앞 오른쪽)", "휠 하우스(뒤 왼쪽)", "휠 하우스(뒤 오른쪽)", "리어 바디(화물 바닥·뒷선반)" };
        var l = new List<ShellRule>();
        for (int i = 0; i < StructNodes.Length; i++)
        {
            string n = StructNodes[i];
            l.Add(new ShellRule { node = n, name = names[i], dbg = cols[i], match = p => StructName(p) == n });
        }
        return l;
    }

    // ═════════════════════════ 한 겹 메시의 구멍 막기(의자·12V 배터리 등) ═════════════════════════
    /// <summary>구멍을 메워 줄 부품. 정말 열려 있어야 하는 곳(그릴·창문 틀 같은 개구부)은 막으면 안 되므로 이름으로 한정한다</summary>
    public static readonly string[] CloseNodes = { "SM_Seat_FL", "SM_Seat_FR", "SM_Seat_Rear", "SM_12v", "Battery_12V_Cover" };

    internal static Acc AccFromMesh(Mesh orig)
    {
        var vs = orig.vertices; var ns = orig.normals; var ts = orig.tangents; var uv = orig.uv; var uv2 = orig.uv2;
        int S = orig.subMeshCount; var a = new Acc(orig.name, S); var poly = new List<V>(3);
        for (int s = 0; s < S; s++)
        {
            var tr = orig.GetTriangles(s);
            for (int i = 0; i < tr.Length; i += 3)
            {
                poly.Clear();
                for (int k = 0; k < 3; k++)
                {
                    int o = tr[i + k];
                    poly.Add(new V { p = vs[o], w = vs[o], n = ns.Length > o ? ns[o] : Vector3.up, t = ts.Length > o ? ts[o] : new Vector4(1, 0, 0, 1),
                        uv = uv.Length > o ? uv[o] : Vector2.zero, uv2 = uv2.Length > o ? uv2[o] : Vector2.zero, orig = o });
                }
                a.Poly(poly, s);
            }
        }
        return a;
    }

    /// <summary>구멍 막기 전/후 아래쪽에서 본 모습(왼쪽 = 원래, 오른쪽 = 막은 뒤). 막은 뚜껑은 원래 재질로 보인다</summary>
    public static string PreviewClose(string[] names, string tag)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Directory.CreateDirectory(PreviewDir);
        var sb = new StringBuilder();
        foreach (var name in names)
        {
            var t = FindSrc(prefab, name); if (t == null) { sb.AppendLine(name + ": 없음"); continue; }
            var mr0 = t.GetComponent<MeshRenderer>(); var orig = LoadSourceMesh(t.GetComponent<MeshFilter>().sharedMesh, name);
            var before = AccFromMesh(orig); var after = AccFromMesh(orig);
            var cnt0 = after.sub.Select(l => l.Count).ToArray();
            var log = new StringBuilder(); int caps = CloseLoops(after, Matrix4x4.identity, log);
            var capOnly = new Acc("caps", after.sub.Length); capOnly.v.AddRange(after.v);
            for (int s2 = 0; s2 < after.sub.Length; s2++) capOnly.sub[s2].AddRange(after.sub[s2].Skip(cnt0[s2]));
            sb.AppendLine($"{name}: 원래 {orig.triangles.Length / 3} → 뚜껑 {caps}개, {after.tris}삼각형");
            sb.Append(log);
            var scene = EditorSceneManager.NewPreviewScene(); var made = new List<UnityEngine.Object>();
            try
            {
                var mA = Build(before, false, -1); var mB = Build(before, false, -1); var mC = Build(capOnly, false, -1); made.Add(mA); made.Add(mB); made.Add(mC);
                var b = mA.bounds; var mtx = prefab.transform.worldToLocalMatrix * t.localToWorldMatrix;
                float sc = mtx.lossyScale.x; float sz = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) * sc;
                var wc = mtx.MultiplyPoint3x4(b.center);
                var flat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.72f, 0.74f, 0.78f) }; made.Add(flat);
                var mag = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(1f, 0.1f, 0.6f) }; made.Add(mag);
                var go = new[] { new GameObject("a"), new GameObject("b"), new GameObject("c") };
                var meshes = new[] { mA, mB, mC };
                for (int i = 0; i < 3; i++)
                {
                    int slot = i == 0 ? 0 : 1;
                    SceneManager.MoveGameObjectToScene(go[i], scene);
                    go[i].transform.rotation = mtx.rotation; go[i].transform.localScale = mtx.lossyScale;
                    go[i].transform.position = (Vector3)mtx.GetColumn(3) - wc + new Vector3(slot * sz * 1.25f, 0, 0);
                    go[i].AddComponent<MeshFilter>().sharedMesh = meshes[i]; go[i].AddComponent<MeshRenderer>().sharedMaterials = Enumerable.Repeat(i == 2 ? mag : flat, meshes[i].subMeshCount).ToArray();
                }
                var lg = new GameObject("l"); SceneManager.MoveGameObjectToScene(lg, scene); var li = lg.AddComponent<Light>(); li.type = LightType.Directional; li.intensity = 1f; lg.transform.rotation = Quaternion.Euler(-50f, 30f, 0f);
                var lg2 = new GameObject("l2"); SceneManager.MoveGameObjectToScene(lg2, scene); var l2 = lg2.AddComponent<Light>(); l2.type = LightType.Directional; l2.intensity = 0.6f; lg2.transform.rotation = Quaternion.Euler(50f, -150f, 0f);
                var cg = new GameObject("c"); SceneManager.MoveGameObjectToScene(cg, scene);
                var cam = cg.AddComponent<Camera>(); cam.scene = scene; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.16f, 0.17f, 0.19f); cam.fieldOfView = 30f; cam.nearClipPlane = 0.02f;
                var rt = new RenderTexture(1400, 700, 24) { antiAliasing = 4 }; made.Add(rt); cam.targetTexture = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); made.Add(tex);
                var mid = new Vector3(sz * 0.625f, 0, 0);
                var viewsC = new (string, Vector3)[] { ("under", new Vector3(0.2f, -1f, 0.25f)), ("over", new Vector3(0.2f, 1f, 0.25f)), ("side", new Vector3(0.1f, 0.2f, 1f)) };
                foreach (var (vn, dir) in viewsC)
                {
                    cg.transform.position = mid + dir.normalized * sz * 3.2f; cg.transform.LookAt(mid, Mathf.Abs(dir.normalized.y) > 0.9f ? Vector3.forward : Vector3.up);
                    cam.Render(); var prev = RenderTexture.active; RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = prev;
                    File.WriteAllBytes($"{PreviewDir}/{tag}_{name}_{vn}.png", tex.EncodeToPNG());
                }
                cam.targetTexture = null;
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); foreach (var o in made) if (o != null) UnityEngine.Object.DestroyImmediate(o); }
        }
        return sb.ToString();
    }

    // ═════════════════════════ 적용 ═════════════════════════
    public static string ApplyShell(List<ShellRule> rules, float crease = 25f, int minTris = 6)
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        var rep = new StringBuilder();
        try
        {
            var regions = Regions();
            var res = Compute(root, regions, SrcName);
            if (res.bodyAcc == null) return res.report;
            var sg = Segment(res.bodyAcc, crease, minTris); Assign(sg, rules);
            var parts = Extract(sg, rules);
            var toLocal = res.srcNode.worldToLocalMatrix * root.transform.localToWorldMatrix;
            int glassIdx = -1; for (int s = 0; s < res.mats.Length; s++) if (res.mats[s] != null && res.mats[s].name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0) glassIdx = s;
            Directory.CreateDirectory(MeshDir);
            var parent = res.srcNode.parent; var srcMr = res.srcNode.GetComponent<MeshRenderer>();

            var names = new HashSet<string>(regions.Select(r => r.node)); foreach (var r in rules) names.Add(r.node);
            foreach (var n in names) { var old = parent.Find(n); if (old != null && old != res.srcNode) UnityEngine.Object.DestroyImmediate(old.gameObject); }

            int sib = res.srcNode.GetSiblingIndex();
            void MakeNode(string node, Mesh mesh)
            {
                var go = new GameObject(node); go.layer = res.srcNode.gameObject.layer;
                go.transform.SetParent(parent, false);
                go.transform.localPosition = res.srcNode.localPosition; go.transform.localRotation = res.srcNode.localRotation; go.transform.localScale = res.srcNode.localScale;
                go.transform.SetSiblingIndex(++sib);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = res.mats;
                mr.shadowCastingMode = srcMr.shadowCastingMode; mr.receiveShadows = srcMr.receiveShadows;
                GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(res.srcNode.gameObject));
            }
            // 1단계 외장 부품(펜더·스포일러·테일게이트 …)
            foreach (var p in res.parts)
            {
                if (p.r.drop) { UnityEngine.Object.DestroyImmediate(p.m); continue; }
                MakeNode(p.r.node, SaveMesh(p.m));
                rep.AppendLine($"{p.r.node} = {p.tris}삼각형");
            }
            // 2단계 내장·구조 부품
            //  ① 구멍 막기·콘솔 바닥판 → ② 노드 만들기. 구조 패널 중 가장 큰 것이 원본 노드(옛 SM_Ioniq_Electric)를 이어 받는다
            string floorKey = parts.ContainsKey("SM_FloorPan") ? "SM_FloorPan" : ShellNode;
            if (!parts.ContainsKey(floorKey)) parts[floorKey] = new Acc(floorKey, sg.S);
            var floorAcc = parts[floorKey];
            var floorCaps = new List<(List<Vector3> pts, Vector3 n)>(); var capCount = new Dictionary<string, int>();
            foreach (var kv in parts.ToList())
            {
                var rule = rules.FirstOrDefault(r => r.node == kv.Key);
                var capList = new List<(List<Vector3> pts, Vector3 n)>();
                capCount[kv.Key] = (rule != null && rule.close) ? CloseLoops(kv.Value, toLocal, null, rule.closeAt, capsOut: capList) : 0;
                if (kv.Key == "SM_FloorConsole") { floorCaps.AddRange(capList); rep.AppendLine("  콘솔 " + AddBottomPlate(kv.Value, floorAcc, toLocal, 2)); }
            }
            int floorCapN = CloseLoops(floorAcc, toLocal, null, ShellCloseAt);
            int plugs = AddFloorPlugs(floorAcc, floorCaps, toLocal, 2);
            string carrier = StructNodes.Where(n => parts.ContainsKey(n)).OrderByDescending(n => parts[n].tris).FirstOrDefault() ?? (parts.ContainsKey(ShellNode) ? ShellNode : null);
            foreach (var kv in parts)
            {
                var rule = rules.FirstOrDefault(r => r.node == kv.Key);
                var m = Build(kv.Value, rule == null || rule.twoSided, glassIdx); m.name = kv.Key;
                if (kv.Key == carrier)
                {
                    res.srcNode.GetComponent<MeshFilter>().sharedMesh = SaveMesh(m); res.srcNode.name = kv.Key;
                    var colMesh = Build(res.bodyAcc, false, glassIdx); colMesh.name = "SM_BodyStructure_Collider";
                    var mc = res.srcNode.GetComponent<MeshCollider>();
                    if (mc != null) mc.sharedMesh = SaveMesh(colMesh); else UnityEngine.Object.DestroyImmediate(colMesh);
                }
                else MakeNode(kv.Key, SaveMesh(m));
                int cn = capCount.TryGetValue(kv.Key, out var cc0) ? cc0 : 0; if (kv.Key == floorKey) cn += floorCapN;
                rep.AppendLine($"{kv.Key}({(rule != null ? rule.name : "")}) = {kv.Value.tris}삼각형, 뚜껑 {cn}개" + (kv.Key == floorKey ? $", 콘솔 자리 바닥판 {plugs}장" : "") + (kv.Key == carrier ? " ← 원본 노드 계승" : ""));
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            rep.AppendLine(res.report);
            return rep.ToString();
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // ═════════════════════════ 미리보기(패치 색칠) ═════════════════════════
    private static readonly Color[] Pal =
    {
        new Color(0.90f,0.25f,0.25f), new Color(0.25f,0.75f,0.30f), new Color(0.25f,0.45f,0.95f), new Color(0.95f,0.80f,0.15f), new Color(0.80f,0.30f,0.90f),
        new Color(0.15f,0.85f,0.85f), new Color(0.95f,0.55f,0.15f), new Color(0.60f,0.95f,0.30f), new Color(0.95f,0.45f,0.70f), new Color(0.55f,0.40f,0.25f),
    };

    /// <param name="byPatch">true = 패치마다 다른 색(상위 N개) / false = 규칙(부품)별 색</param>
    public static string PreviewShell(List<ShellRule> rules, string tag, (string name, Vector3 eye)[] views, Vector3 target, float fov, bool byPatch = false, int topN = 40,
        Func<Patch, bool> show = null, Func<Region, bool> showStage1 = null, bool withOthers = false, float creaseDeg = 35f, int minTris = 10, bool close = false, bool real = false)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var res = Compute(prefab, Regions(), SrcName);
        if (res.bodyAcc == null) return res.report;
        var sg = Segment(res.bodyAcc, creaseDeg, minTris);
        Assign(sg, rules);
        Directory.CreateDirectory(PreviewDir);
        var scene = EditorSceneManager.NewPreviewScene();
        var made = new List<UnityEngine.Object>();
        var sbRep = new StringBuilder();
        try
        {
            Material Mat(Color c) { var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.color = c; m.SetFloat("_Smoothness", 0.25f); made.Add(m); return m; }
            void Put(Mesh mesh, Matrix4x4 mtx, Material[] mats)
            {
                var go = new GameObject("p"); SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.SetPositionAndRotation(mtx.GetColumn(3), mtx.rotation); go.transform.localScale = mtx.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            }
            var rootInv = prefab.transform.worldToLocalMatrix; var srcMtx = rootInv * res.srcNode.localToWorldMatrix;
            var toLocal = res.srcNode.worldToLocalMatrix * prefab.transform.localToWorldMatrix;
            if (withOthers)
            {
                var grey = Mat(new Color(0.32f, 0.33f, 0.35f));
                foreach (var mr in prefab.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr.transform == res.srcNode || !mr.enabled || !ActiveChain(mr.transform) || UnderCables(mr.transform)) continue;
                    var mf = mr.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                    Put(mf.sharedMesh, rootInv * mr.transform.localToWorldMatrix, Enumerable.Repeat(grey, mf.sharedMesh.subMeshCount).ToArray());
                }
            }
            var glassIdx = -1; for (int s = 0; s < res.mats.Length; s++) if (res.mats[s] != null && res.mats[s].name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0) glassIdx = s;
            // 1단계 부품(펜더 등)
            foreach (var p in res.parts)
            {
                if (showStage1 != null && !showStage1(p.r)) continue;
                Put(p.m, srcMtx, real ? res.mats : Enumerable.Repeat(Mat(p.r.dbg), p.m.subMeshCount).ToArray());
            }
            if (byPatch)
            {
                for (int i = 0; i < sg.patches.Count; i++)
                {
                    var p = sg.patches[i]; if (show != null && !show(p)) continue;
                    var acc = new Acc("pp", sg.S); var poly = new List<V>(3);
                    foreach (int t in p.t) { poly.Clear(); poly.Add(sg.body.v[sg.tA[t]]); poly.Add(sg.body.v[sg.tB[t]]); poly.Add(sg.body.v[sg.tC[t]]); acc.Poly(poly, sg.tS[t]); }
                    var m = Build(acc, true, glassIdx); made.Add(m);
                    var col = i < topN ? Pal[i % Pal.Length] * (1f - 0.12f * (i / Pal.Length)) : new Color(0.4f, 0.4f, 0.42f);
                    Put(m, srcMtx, Enumerable.Repeat(Mat(col), m.subMeshCount).ToArray());
                    if (i < topN) sbRep.AppendLine($"#{p.id} {PalName(i)} tris={p.tris} c=({p.c.x:F2},{p.c.y:F2},{p.c.z:F2}) sz=({p.b.size.x:F2},{p.b.size.y:F2},{p.b.size.z:F2}) n=({p.n.x:F2},{p.n.y:F2},{p.n.z:F2})");
                }
            }
            else
            {
                var parts = Extract(sg, rules);
                foreach (var kv in parts)
                {
                    var rule = rules.FirstOrDefault(r => r.node == kv.Key);
                    if (show != null && rule == null && kv.Key != ShellNode) continue;
                    if (close && (rule == null ? ShellCloseAt != null : rule.close)) CloseLoops(kv.Value, toLocal, null, rule != null ? rule.closeAt : ShellCloseAt);
                    var m = Build(kv.Value, true, glassIdx); made.Add(m);
                    var col = rule != null ? rule.dbg : new Color(0.55f, 0.56f, 0.58f);
                    Put(m, srcMtx, real ? res.mats : Enumerable.Repeat(Mat(col), m.subMeshCount).ToArray());
                    sbRep.AppendLine($"{kv.Key} = {kv.Value.tris}삼각형");
                }
            }
            var lg = new GameObject("light"); SceneManager.MoveGameObjectToScene(lg, scene);
            var li = lg.AddComponent<Light>(); li.type = LightType.Directional; li.intensity = 0.9f; lg.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var lg2 = new GameObject("light2"); SceneManager.MoveGameObjectToScene(lg2, scene);
            var li2 = lg2.AddComponent<Light>(); li2.type = LightType.Directional; li2.intensity = 0.4f; lg2.transform.rotation = Quaternion.Euler(20f, 150f, 0f);
            var cg = new GameObject("cam"); SceneManager.MoveGameObjectToScene(cg, scene);
            var cam = cg.AddComponent<Camera>(); cam.scene = scene; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
            cam.fieldOfView = fov; cam.nearClipPlane = 0.02f; cam.farClipPlane = 100f;
            var rt = new RenderTexture(1400, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; made.Add(rt);
            cam.targetTexture = rt; var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); made.Add(tex);
            foreach (var v in views)
            {
                cg.transform.position = v.eye; cg.transform.LookAt(target, Mathf.Abs(v.eye.y - target.y) > 6f ? Vector3.left : Vector3.up);
                cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = prev;
                File.WriteAllBytes($"{PreviewDir}/{tag}_{v.name}.png", tex.EncodeToPNG());
            }
            cam.targetTexture = null;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var o in made) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            foreach (var p in res.parts) UnityEngine.Object.DestroyImmediate(p.m);
            if (res.body != null) UnityEngine.Object.DestroyImmediate(res.body);
        }
        return sbRep.ToString();
    }

    private static readonly string[] PalNames = { "빨강", "초록", "파랑", "노랑", "보라", "청록", "주황", "연두", "분홍", "갈색" };
    private static string PalName(int i) => PalNames[i % PalNames.Length] + (i >= PalNames.Length ? "(어둡게" + (i / PalNames.Length) + ")" : "");
}
#endif
