#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// ============================================================
//  CarBodyPanelSplitter.cs  (에디터 전용)
//
//  Ioniq5N 차체 외판 SM_Ioniq_Electric 은 펜더·루프·필러·미러·유리·안쪽 차체가 한 메시다.
//  도장면 큰 조각 하나(1.3만 삼각형)가 펜더부터 쿼터 패널까지 이어져 있어 조각(island) 기준으로는 못 나눈다.
//  → 정비지침서 구성부품 경계(펜더 = 도어 앞 끝·후드 라인·범퍼 경계 등)를 평면으로 정하고, 삼각형을 그 평면으로 잘라 나눈다.
//     · 걸친 삼각형은 평면에서 정확히 잘라(정점 보간) 양쪽에 나눠 준다 → 장착 상태에선 원래 메시와 똑같이 보이고(틈 없음), 떼어 내면 잘린 선이 곧다.
//     · 작은 조각(미러·몰딩 같은 따로 떨어진 메시)은 자르지 않고 조각 가운데가 속한 부품으로 통째로 보낸다.
//     · 외판은 한 겹(뒷면 없음)이라 떼어 들면 안쪽에서 뚫려 보였다 → 안쪽 면(뒤집은 삼각형)을 만든다. 남는 차체도 같은 처리(빈 자리로 들여다봐도 뚫려 보이지 않게).
//  원본은 FBX 메시를 그대로 두고 프리팹의 SM_Ioniq_Electric 노드에 '남은 차체' 메시를 꽂는다(이름을 찾는 코드가 그대로 동작).
//  다시 돌리면 FBX 원본에서 새로 계산한다(만든 노드는 지우고 다시 만든다).
//  메뉴: Dodrill > 차량 메시 분리 > 차체 외판(펜더·미러·유리) 미리보기 / 적용
// ============================================================
public static class CarBodyPanelSplitter
{
    private const string PrefabPath = "Assets/03_3D/Object/O_Prefabs/Dummy/Ioniq5N.prefab";
    private const string MeshDir = "Assets/03_3D/Object/O_Prefabs/Dummy/Ioniq5N_Split";
    private const string SrcName = "SM_Ioniq_Electric";
    private const string BodyMeshName = "SM_Ioniq_Electric_Body";
    public const string PreviewDir = "D:/claude_tmp/body";
    private const int BigIsland = 5000;   // 이보다 큰 조각은 평면으로 자른다, 작은 조각은 통째로

    // ── 반공간: dot(n, p) >= d 이면 안쪽 (p = 차 루트 좌표: 앞 −X, 왼쪽 −Z, 위 +Y) ──
    public struct H
    {
        public Vector3 n; public float d;
        public H(Vector3 n, float d) { this.n = n; this.d = d; }
        public float Dist(Vector3 p) => Vector3.Dot(n, p) - d;
    }
    public static H Xlt(float v) => new H(Vector3.left, -v);
    public static H Xgt(float v) => new H(Vector3.right, v);
    public static H Ylt(float v) => new H(Vector3.down, -v);
    public static H Ygt(float v) => new H(Vector3.up, v);
    public static H Zlt(float v) => new H(Vector3.back, -v);
    public static H Zgt(float v) => new H(Vector3.forward, v);
    /// <summary>점 a, b 를 지나고 위(+Y)로 세운 평면 — (a→b) 의 왼쪽이 안쪽(위에서 볼 때 XZ 평면 기준)</summary>
    public static H Side(float ax, float az, float bx, float bz, bool flip = false)
    {
        var dir = new Vector3(bx - ax, 0f, bz - az).normalized;
        var n = Vector3.Cross(Vector3.up, dir); if (flip) n = -n;
        return new H(n, Vector3.Dot(n, new Vector3(ax, 0f, az)));
    }
    /// <summary>XY 단면(옆에서 본 선) 기준 평면 — (a→b) 선의 왼쪽(옆에서 보기)이 안쪽. Z 와 무관</summary>
    public static H Prof(float ax, float ay, float bx, float by, bool flip = false)
    {
        var dir = new Vector3(bx - ax, by - ay, 0f).normalized;
        var n = Vector3.Cross(Vector3.forward, dir); if (flip) n = -n;
        return new H(n, Vector3.Dot(n, new Vector3(ax, ay, 0f)));
    }

    public class Region
    {
        public string node;        // 새 오브젝트 이름(프리팹)
        public string name;        // 한글(보고·미리보기)
        public Color dbg;
        public H[] hs;             // 모두 만족하면 이 부품
        public int subMask = ~0;   // 어느 서브메시(0 도장 1 유리 2 차체 안쪽 3 시트색 트림)에서 가져오나
        public bool clip;          // true = 평면으로 잘라 안쪽만 / false = 조각(island) 통째(가운데 점 기준, 큰 조각은 isl 이 있을 때만)
        public Func<Bounds, int, bool> isl;   // 조각 조건(차 루트 범위, 삼각형 수)
        public bool twoSided = true;
        public Func<Vector3, Vector3, Vector3, bool> tri;   // 삼각형(차 루트 좌표 세 점) 조건 — 있으면 이것도 만족해야 한다(자르지 않고 통째)
        public bool drop;          // 버린다(깨진 면)
    }

    public static float LongestEdge(Vector3 a, Vector3 b, Vector3 c) => Mathf.Max((a - b).magnitude, Mathf.Max((b - c).magnitude, (c - a).magnitude));

    // ── 매뉴얼 구성부품 기준 영역 (바디 — 외장) ──
    // 순서가 중요하다: 앞에서 가져간 삼각형은 뒤 영역이 보지 않는다.
    // 좌표·경계는 화면 밖 렌더(PreviewDir)로 확인하며 정했다(2026-11-17).
    //  차 루트 기준: 앞 도어 앞 끝 x≈−1.35, 후드 라인 y≈1.14, 펜더 바깥 |z|>0.62, 사이드 실 y<0.28, 뒷유리 x>0.8
    private static bool RearCenter(Bounds b) => b.center.x > 1.25f && b.center.y > 0.95f && Mathf.Abs(b.center.z) < 0.3f && b.size.z > 0.4f;

    public static List<Region> Regions()
    {
        var paint = 1 << 0; var glass = 1 << 1; var inner = 1 << 2;
        return new List<Region>
        {
            // ── 앞 휠 하우스 깨진 면(버림) ──
            // 안쪽 벽(|z|≈0.61) 바깥 3~4cm 에 차 안쪽을 향한 대각선 삼각형이 좌우 3장씩 겹쳐 있다(모델링 오류).
            // 원래는 뒷면이라 안 보였는데 차체를 양면으로 만들면서 휠 하우스 안에 검은 쐐기로 드러났다 → 버린다. 뒤에 안쪽 벽이 있어 뚫리지 않는다
            new Region { node = "_Drop_WheelWell", name = "휠 하우스 깨진 면", dbg = Color.magenta, subMask = inner, drop = true, twoSided = false, hs = new H[0],
                tri = (a, b, c) =>
                {
                    var ce = (a + b + c) / 3f; var cr = Vector3.Cross(b - a, c - a);
                    float az = Mathf.Abs(ce.z);
                    return Mathf.Abs(ce.x + 1.9f) < 0.7f && ce.y > 0.3f && ce.y < 0.9f && az > 0.55f && az < 0.67f
                        && cr.normalized.z * Mathf.Sign(ce.z) < -0.9f && cr.magnitude * 0.5f > 0.012f;
                } },
            // ── 테일게이트 어셈블리(매뉴얼: 글라스·스포일러·가니쉬·리어 와이퍼가 테일게이트에 달려 있다) ──
            // 차 모델은 테일게이트 아래쪽(SM_Ioniq_Back)만 따로고, 위쪽 외판·뒷유리·안쪽 패널이 차체에 붙어 있어 테일게이트를 떼도 차에 남았다
            new Region { node = "SM_RearSpoiler", name = "리어 스포일러", dbg = new Color(1f, 0.55f, 0.1f), subMask = paint, clip = true,
                isl = (b, n) => n > 2000 && n < 4000 && b.center.x > 1.3f, hs = new[] { Ygt(1.47f) } },
            new Region { node = "SM_Tailgate_Upper", name = "테일게이트 상단 외판·리어 와이퍼", dbg = new Color(0.2f, 0.8f, 0.3f), subMask = paint,
                isl = (b, n) => RearCenter(b), hs = new H[0] },
            new Region { node = "SM_Tailgate_Glass", name = "테일게이트 글라스", dbg = new Color(0.2f, 0.4f, 1f), subMask = glass, hs = new[] { Xgt(0.8f) } },
            new Region { node = "SM_Tailgate_Inner", name = "테일게이트 안쪽 패널", dbg = new Color(0.9f, 0.3f, 0.9f), subMask = inner,
                isl = (b, n) => RearCenter(b) && b.size.z > 0.8f, hs = new H[0] },
            // ── 윈드쉴드 글라스(접착식 — 볼트 부위는 아님, 메시만 분리) ──
            new Region { node = "SM_Windshield", name = "윈드쉴드 글라스", dbg = new Color(0.5f, 0.95f, 1f), subMask = glass, hs = new[] { Xlt(0.8f) } },
            // ── 프런트 와이퍼 암·링크(카울 탑 커버 위) ──
            new Region { node = "SM_WiperFront", name = "프런트 와이퍼 암·링크", dbg = new Color(1f, 0.95f, 0.2f), subMask = paint,
                isl = (b, n) => n < 1500 && b.center.x > -1.8f && b.center.x < -1.25f && b.center.y > 0.95f && b.center.y < 1.25f && Mathf.Abs(b.center.z) < 0.75f, hs = new H[0] },
            // ── 프런트 펜더(펜더 가니쉬·안쪽 플랜지 포함): 앞 도어 앞 끝 ~ 범퍼, 후드 라인 아래 ──
            new Region { node = "SM_FenderFL", name = "프런트 펜더(왼쪽)", dbg = new Color(0.9f, 0.2f, 0.2f), subMask = paint, clip = true, hs = new[] { Xlt(-1.34f), Ylt(1.14f), Zlt(-0.62f) } },
            new Region { node = "SM_FenderFR", name = "프런트 펜더(오른쪽)", dbg = new Color(0.6f, 0.1f, 0.1f), subMask = paint, clip = true, hs = new[] { Xlt(-1.34f), Ylt(1.14f), Zgt(0.62f) } },
            // ── 사이드 실 몰딩(도어 아래 실 커버) ──
            new Region { node = "SM_SideSillL", name = "사이드 실 몰딩(왼쪽)", dbg = new Color(0.5f, 0.3f, 0.9f), subMask = paint, clip = true, hs = new[] { Xgt(-1.36f), Xlt(1.25f), Ylt(0.28f), Zlt(-0.75f) } },
            new Region { node = "SM_SideSillR", name = "사이드 실 몰딩(오른쪽)", dbg = new Color(0.3f, 0.2f, 0.6f), subMask = paint, clip = true, hs = new[] { Xgt(-1.36f), Xlt(1.25f), Ylt(0.28f), Zgt(0.75f) } },
        };
    }

    // 아웃사이드 미러 — 앞 도어 메시에 붙어 있다. 받침(스톡)은 도어 벨트 라인과 겹쳐 자르면 도어가 같이 잘려서 도어에 남기고 하우징만 뗀다
    public static List<Region> MirrorRegions(bool left) => new List<Region>
    {
        new Region { node = left ? "SM_MirrorL" : "SM_MirrorR", name = left ? "아웃사이드 미러(왼쪽)" : "아웃사이드 미러(오른쪽)", dbg = new Color(1f, 0.5f, 0.1f), clip = true,
            hs = new[] { Xlt(-0.70f), Xgt(-1.10f), Ygt(1.06f), Ylt(1.26f), left ? Zlt(-0.86f) : Zgt(0.86f) } },
    };

    /// <summary>원본 노드 → 영역. 적용 메뉴가 이 순서대로 돌린다</summary>
    public static List<(string src, List<Region> regions)> Plan() => new List<(string, List<Region>)>
    {
        (SrcName, Regions()),
        ("SM_Ioniq_DoorFrontLeft", MirrorRegions(true)),
        ("SM_Ioniq_DoorFrontRight", MirrorRegions(false)),
    };

    /// <summary>떼어 내는 부품 중 한 겹 메시 — 안쪽 면을 덧붙인다(외판 분리로 새로 생긴 부품은 분리할 때 이미 양면)</summary>
    public static readonly string[] DoubleSideNodes =
    {
        "SM_Ioniq_Bumper", "Hood_Latch_Point", "SM_Ioniq_Front", "SM_Ioniq_Frunk", "SM_Frunk.001", "SM_Frunk_lid",
        "SM_Ioniq_DoorRearLeft", "SM_Ioniq_DoorRearRight",   // 앞 도어는 미러를 자르면서 이미 양면
        "SM_Ioniq_Back", "SM_Ioniq_Back.001", "SM_Ioniq_Back.002", "SM_Ioniq_Back.004", "SM_Ioniq_Back.005", "SM_Ioniq_Back.006",
        "SM_Bottom_01", "SM_Bottom_02", "Battery_12V_Cover", "SM_Ioniq_Cockpit", "SM_Seat_FL", "SM_Seat_FR", "SM_Seat_Rear",
    };

    // ═════════════════════════ 계산 ═════════════════════════
    private struct V
    {
        public Vector3 p, w, n; public Vector4 t; public Vector2 uv, uv2; public int orig;
        public static V Lerp(V a, V b, float k) => new V
        {
            p = Vector3.Lerp(a.p, b.p, k), w = Vector3.Lerp(a.w, b.w, k), n = Vector3.Lerp(a.n, b.n, k).normalized,
            t = Vector4.Lerp(a.t, b.t, k), uv = Vector2.Lerp(a.uv, b.uv, k), uv2 = Vector2.Lerp(a.uv2, b.uv2, k), orig = -1
        };
    }

    private class Acc
    {
        public string name; public readonly List<V> v = new List<V>(); public List<int>[] sub; public Dictionary<int, int> map = new Dictionary<int, int>();
        public int tris;
        public Acc(string n, int s) { name = n; sub = new List<int>[s]; for (int i = 0; i < s; i++) sub[i] = new List<int>(); }
        private int Idx(V x)
        {
            if (x.orig >= 0 && map.TryGetValue(x.orig, out int i)) return i;
            i = v.Count; v.Add(x); if (x.orig >= 0) map[x.orig] = i; return i;
        }
        public void Poly(List<V> poly, int s)
        {
            if (poly.Count < 3) return;
            int a = Idx(poly[0]);
            for (int k = 1; k + 1 < poly.Count; k++)
            {
                int b = Idx(poly[k]), c = Idx(poly[k + 1]);
                // 잘리고 남은 바늘 같은 삼각형은 버린다
                if (Vector3.Cross(v[b].w - v[a].w, v[c].w - v[a].w).sqrMagnitude < 1e-14f) continue;
                sub[s].Add(a); sub[s].Add(b); sub[s].Add(c); tris++;
            }
        }
    }

    private static void Clip(List<V> poly, H h, List<V> inside, List<V> outside)
    {
        inside.Clear(); outside.Clear();
        int n = poly.Count;
        for (int i = 0; i < n; i++)
        {
            V a = poly[i], b = poly[(i + 1) % n];
            float da = h.Dist(a.w), db = h.Dist(b.w);
            if (da >= 0) inside.Add(a); else outside.Add(a);
            if ((da >= 0) != (db >= 0))
            {
                var m = V.Lerp(a, b, da / (da - db));
                inside.Add(m); outside.Add(m);
            }
        }
    }

    private static int Find(int[] p, int x) { while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; } return x; }

    public class Result
    {
        public Mesh source; public Transform srcNode; public Material[] mats;
        public List<(Region r, Mesh m, int tris)> parts = new List<(Region, Mesh, int)>();
        public Mesh body; public int bodyTris;
        public string report;
    }

    /// <summary>
    /// 이 도구가 만든 메시(…_Body, …_2S)가 꽂혀 있으면 원본으로 되돌아가 다시 계산한다 — FBX 원본(FBX_ElectricCarV3) 또는 분리 폴더의 원본 애셋.
    /// </summary>
    public static Mesh LoadSourceMesh(Mesh current, string srcName)
    {
        string path = AssetDatabase.GetAssetPath(current);
        if (current.name == srcName) return current;
        foreach (var g in AssetDatabase.FindAssets("FBX_ElectricCarV3 t:Model"))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var m = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Mesh>().FirstOrDefault(x => x.name == srcName);
            if (m != null) return m;
        }
        var a = AssetDatabase.LoadAssetAtPath<Mesh>($"{MeshDir}/{srcName}.asset");
        return a != null ? a : current;
    }

    private static bool UnderCables(Transform t) { for (t = t.parent; t != null; t = t.parent) if (t.name == "Cables") return true; return false; }
    private static bool ActiveChain(Transform t) { for (; t != null; t = t.parent) if (!t.gameObject.activeSelf) return false; return true; }

    /// <summary>켜진 변형의 노드(FBX_ElectricCar 바로 아래 우선, 없으면 켜진 아무 곳 — 도어는 DoorFrontLeft_Parent 아래)</summary>
    public static Transform FindSrc(GameObject root, string srcName)
    {
        Transform any = null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != srcName || t.GetComponent<MeshFilter>() == null || !ActiveChain(t) || UnderCables(t)) continue;
            if (t.parent != null && t.parent.name == "FBX_ElectricCar") return t;
            if (any == null) any = t;
        }
        return any;
    }

    public static Result Compute(GameObject root, List<Region> regions, string srcName = SrcName)
    {
        var res = new Result();
        var src = FindSrc(root, srcName);
        if (src == null) { res.report = srcName + " 노드를 못 찾음"; return res; }
        var mesh = LoadSourceMesh(src.GetComponent<MeshFilter>().sharedMesh, srcName);
        if (mesh == null || !mesh.isReadable) { res.report = "원본 메시를 못 읽음"; return res; }
        res.source = mesh; res.srcNode = src; res.mats = src.GetComponent<MeshRenderer>().sharedMaterials;

        var vs = mesh.vertices; var ns = mesh.normals; var ts = mesh.tangents; var uv = mesh.uv; var uv2 = mesh.uv2;
        var toRoot = root.transform.worldToLocalMatrix * src.localToWorldMatrix;
        var V0 = new V[vs.Length];
        for (int i = 0; i < vs.Length; i++)
            V0[i] = new V
            {
                p = vs[i], w = toRoot.MultiplyPoint3x4(vs[i]), n = ns.Length > i ? ns[i] : Vector3.up, t = ts.Length > i ? ts[i] : new Vector4(1, 0, 0, 1),
                uv = uv.Length > i ? uv[i] : Vector2.zero, uv2 = uv2.Length > i ? uv2[i] : Vector2.zero, orig = i
            };

        // 같은 위치 정점 묶기 → 서브메시별 조각
        var weld = new Dictionary<Vector3Int, int>(); var wid = new int[vs.Length];
        for (int i = 0; i < vs.Length; i++)
        {
            var k = Vector3Int.RoundToInt(vs[i] * 2000f);
            if (!weld.TryGetValue(k, out int w)) { w = weld.Count; weld[k] = w; }
            wid[i] = w;
        }

        int S = mesh.subMeshCount;
        // 같은 node 이름의 영역은 한 메시로 모은다(예: 미러 머리 + 받침)
        var byNode = new Dictionary<string, Acc>();
        var accs = regions.Select(r => byNode.TryGetValue(r.node, out var a) ? a : (byNode[r.node] = new Acc(r.node, S))).ToList();
        var body = new Acc(srcName == SrcName ? BodyMeshName : srcName + "_Body", S);
        var inside = new List<V>(); var outside = new List<V>();

        for (int s = 0; s < S; s++)
        {
            var tr = mesh.GetTriangles(s);
            var par = new int[weld.Count]; for (int i = 0; i < par.Length; i++) par[i] = i;
            for (int i = 0; i < tr.Length; i += 3)
            {
                int a = Find(par, wid[tr[i]]);
                par[Find(par, wid[tr[i + 1]])] = a; par[Find(par, wid[tr[i + 2]])] = Find(par, a);
            }
            var cnt = new Dictionary<int, int>(); var bb = new Dictionary<int, Bounds>();
            for (int i = 0; i < tr.Length; i += 3)
            {
                int r = Find(par, wid[tr[i]]);
                if (!bb.TryGetValue(r, out var b)) { b = new Bounds(V0[tr[i]].w, Vector3.zero); cnt[r] = 0; }
                b.Encapsulate(V0[tr[i]].w); b.Encapsulate(V0[tr[i + 1]].w); b.Encapsulate(V0[tr[i + 2]].w);
                bb[r] = b; cnt[r]++;
            }
            // 영역을 순서대로 본다. 앞 영역이 가져간 조각(잘린 조각)은 다음 영역이 보지 않는다.
            //  · tri 조건 영역: 삼각형 통째(가운데 점이 hs 안)
            //  · clip 영역: 조각 조건(isl)이 맞는 조각의 삼각형을 hs 평면으로 잘라 안쪽만
            //  · 그 밖: 조각 통째(조각 가운데 점이 hs 안)
            bool In(H[] hs, Vector3 p) { foreach (var h in hs) if (h.Dist(p) < 0) return false; return true; }
            var poly = new List<V>(3);
            for (int i = 0; i < tr.Length; i += 3)
            {
                int r = Find(par, wid[tr[i]]);
                var ib = bb[r]; int ic = cnt[r];
                poly.Clear(); poly.Add(V0[tr[i]]); poly.Add(V0[tr[i + 1]]); poly.Add(V0[tr[i + 2]]);
                var pending = new List<List<V>> { new List<V>(poly) };
                for (int g = 0; g < regions.Count && pending.Count > 0; g++)
                {
                    var rg = regions[g];
                    if ((rg.subMask & (1 << s)) == 0) continue;
                    if (rg.isl != null && !rg.isl(ib, ic)) continue;
                    if (rg.tri != null)
                    {
                        var keep = new List<List<V>>();
                        foreach (var pc in pending)
                        {
                            bool ok = rg.tri(poly[0].w, poly[1].w, poly[2].w) && In(rg.hs, (pc[0].w + pc[1].w + pc[2].w) / 3f);
                            if (ok) accs[g].Poly(pc, s); else keep.Add(pc);
                        }
                        pending = keep; continue;
                    }
                    if (!rg.clip || (ic < BigIsland && rg.isl == null))   // 자르는 영역도 작은 조각(가니쉬 등)은 통째로
                    {
                        if (ic < BigIsland || rg.isl != null)
                        {
                            if (In(rg.hs, ib.center)) { foreach (var pc in pending) accs[g].Poly(pc, s); pending.Clear(); }
                        }
                        continue;
                    }
                    var next = new List<List<V>>();
                    foreach (var pc in pending)
                    {
                        var cur = pc; bool any = true;
                        foreach (var h in rg.hs)
                        {
                            Clip(cur, h, inside, outside);
                            if (outside.Count >= 3) next.Add(new List<V>(outside));
                            if (inside.Count < 3) { any = false; break; }
                            cur = new List<V>(inside);
                        }
                        if (any) accs[g].Poly(cur, s);
                    }
                    pending = next;
                }
                foreach (var pc in pending) body.Poly(pc, s);
            }
        }

        var sb = new System.Text.StringBuilder();
        int glassSub = -1;
        for (int s = 0; s < res.mats.Length; s++) if (res.mats[s] != null && res.mats[s].name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0) glassSub = s;
        var done = new HashSet<Acc>();
        for (int g = 0; g < regions.Count; g++)
        {
            if (!done.Add(accs[g])) continue;
            if (accs[g].tris == 0) { sb.AppendLine($"{regions[g].node}({regions[g].name}) = 0"); continue; }
            var m = Build(accs[g], regions[g].twoSided, glassSub);
            res.parts.Add((regions[g], m, accs[g].tris));
            sb.AppendLine($"{regions[g].node}({regions[g].name}) = {accs[g].tris}삼각형 [{string.Join(",", Enumerable.Range(0, S).Select(s => accs[g].sub[s].Count / 3))}] 범위 {m.bounds.size}");
        }
        res.body = Build(body, true, glassSub); res.bodyTris = body.tris;
        sb.AppendLine($"남은 차체 = {body.tris}삼각형 (원본 {mesh.triangles.Length / 3})");
        res.report = sb.ToString();
        return res;
    }

    // 뒤집은 삼각형(안쪽 면)을 같은 서브메시에 덧붙인다 — 외판 안쪽도 도장색(실제 패널 안쪽도 도장돼 있다). 유리는 원래 두 겹이라 제외
    private static Mesh Build(Acc a, bool twoSided, int glassSub)
    {
        int S = a.sub.Length; int baseCount = a.v.Count;
        var verts = new List<V>(a.v);
        var subs = new List<int>[S];
        for (int s = 0; s < S; s++)
        {
            subs[s] = new List<int>(a.sub[s]);
            if (!twoSided || s == glassSub) continue;
            var back = new Dictionary<int, int>();
            var src = a.sub[s];
            for (int i = 0; i < src.Count; i += 3)
            {
                for (int k = 2; k >= 0; k--)
                {
                    int o = src[i + k];
                    if (!back.TryGetValue(o, out int bi))
                    {
                        var x = a.v[o]; x.n = -x.n; x.t = new Vector4(x.t.x, x.t.y, x.t.z, -x.t.w);
                        // 앞면과 겹쳐 깜빡이지 않게 법선 반대쪽으로 0.4mm
                        x.p -= a.v[o].n * 0.0004f;
                        bi = verts.Count; verts.Add(x); back[o] = bi;
                    }
                    subs[s].Add(bi);
                }
            }
        }
        var m = new Mesh { name = a.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(verts.Select(x => x.p).ToList());
        m.SetNormals(verts.Select(x => x.n).ToList());
        m.SetTangents(verts.Select(x => x.t).ToList());
        m.SetUVs(0, verts.Select(x => x.uv).ToList());
        m.SetUVs(1, verts.Select(x => x.uv2).ToList());
        m.subMeshCount = S;
        for (int s = 0; s < S; s++) m.SetTriangles(subs[s], s);
        m.RecalculateBounds();
        return m;
    }

    // ═════════════════════════ 미리보기(화면 밖 렌더) ═════════════════════════
    public static readonly (string name, Vector3 eye)[] Views =
    {
        ("front_left", new Vector3(-5.2f, 2.4f, -3.6f)),
        ("rear_right", new Vector3(4.6f, 2.4f, 3.8f)),
        ("left", new Vector3(-0.3f, 1.1f, -7.0f)),
        ("right", new Vector3(-0.3f, 1.1f, 7.0f)),
        ("top", new Vector3(-0.3f, 8.0f, 0.01f)),
        ("front", new Vector3(-7.5f, 1.3f, 0f)),
    };

    /// <summary>영역별 색으로 칠해 여러 방향에서 PNG 로 저장. hideBody = 남은 차체 숨김(떼어 낸 부품만), others = 다른 차 부품도 회색으로</summary>
    public static string Preview(List<Region> regions, string tag, bool others = true, bool hideBody = false, (string name, Vector3 eye)[] views = null, Vector3? target = null, float fov = 32f, Func<Transform, bool> skip = null, string srcName = SrcName,
        bool real = false, Func<Region, bool> showPart = null, Dictionary<string, Matrix4x4> move = null)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var res = Compute(prefab, regions, srcName);
        if (res.source == null) return res.report;
        Directory.CreateDirectory(PreviewDir);
        var scene = EditorSceneManager.NewPreviewScene();
        var made = new List<UnityEngine.Object>();
        try
        {
            Material Mat(Color c)
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.color = c; m.SetFloat("_Smoothness", 0.25f); made.Add(m); return m;
            }
            void Put(Mesh mesh, Matrix4x4 mtx, Material[] mats)
            {
                var go = new GameObject("p"); SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.SetPositionAndRotation(mtx.GetColumn(3), mtx.rotation); go.transform.localScale = mtx.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            }
            var rootInv = prefab.transform.worldToLocalMatrix;
            var srcMtx = rootInv * res.srcNode.localToWorldMatrix;
            if (others)
            {
                var grey = Mat(new Color(0.32f, 0.33f, 0.35f));
                foreach (var mr in prefab.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr.transform == res.srcNode || !mr.enabled || !ActiveChain(mr.transform) || (skip != null && skip(mr.transform))) continue;
                    var mf = mr.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                    Put(mf.sharedMesh, rootInv * mr.transform.localToWorldMatrix, real ? mr.sharedMaterials : Enumerable.Repeat(grey, mf.sharedMesh.subMeshCount).ToArray());
                }
            }
            if (!hideBody)
            {
                var bodyMat = Mat(new Color(0.85f, 0.85f, 0.82f)); var glass = Mat(new Color(0.35f, 0.75f, 0.95f));
                Put(res.body, srcMtx, real ? res.mats : Enumerable.Range(0, res.body.subMeshCount).Select(s => s == 1 ? glass : bodyMat).ToArray());
            }
            foreach (var p in res.parts)
            {
                if (showPart != null && !showPart(p.r)) continue;
                var m = Mat(p.r.dbg);
                // move: 떼어 낸 부품을 옆으로 옮겨 보기(차 루트 기준 이동·회전)
                var mtx = move != null && move.TryGetValue(p.r.node, out var mv) ? mv * srcMtx : srcMtx;
                Put(p.m, mtx, real ? res.mats : Enumerable.Repeat(m, p.m.subMeshCount).ToArray());
            }
            var lg = new GameObject("light"); SceneManager.MoveGameObjectToScene(lg, scene);
            var li = lg.AddComponent<Light>(); li.type = LightType.Directional; li.intensity = 0.9f; lg.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var lg2 = new GameObject("light2"); SceneManager.MoveGameObjectToScene(lg2, scene);
            var li2 = lg2.AddComponent<Light>(); li2.type = LightType.Directional; li2.intensity = 0.35f; lg2.transform.rotation = Quaternion.Euler(20f, 150f, 0f);

            var cg = new GameObject("cam"); SceneManager.MoveGameObjectToScene(cg, scene);
            var cam = cg.AddComponent<Camera>(); cam.scene = scene; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
            cam.fieldOfView = fov; cam.nearClipPlane = 0.02f; cam.farClipPlane = 100f;
            var rt = new RenderTexture(1400, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; made.Add(rt);
            cam.targetTexture = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); made.Add(tex);
            var tgt = target ?? new Vector3(-0.3f, 0.85f, 0f);
            foreach (var v in views ?? Views)
            {
                cg.transform.position = v.eye; cg.transform.LookAt(tgt, Mathf.Abs(v.eye.y - tgt.y) > 6f ? Vector3.left : Vector3.up);
                cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                RenderTexture.active = prev;
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
        return res.report;
    }

    // ═════════════════════════ 적용 ═════════════════════════
    public static string Apply(List<Region> regions, string srcName = SrcName)
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var res = Compute(root, regions, srcName);
            if (res.source == null) return res.report;
            Directory.CreateDirectory(MeshDir);
            var parent = res.srcNode.parent;
            // 예전에 만든 부품 노드 정리
            foreach (var r in regions) { var old = parent.Find(r.node); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject); }

            var bodyMesh = SaveMesh(res.body);
            res.srcNode.GetComponent<MeshFilter>().sharedMesh = bodyMesh;
            var mc = res.srcNode.GetComponent<MeshCollider>(); if (mc != null) mc.sharedMesh = bodyMesh;

            int sib = res.srcNode.GetSiblingIndex();
            foreach (var p in res.parts)
            {
                if (p.r.drop) { UnityEngine.Object.DestroyImmediate(p.m); continue; }   // 깨진 면은 버린다
                var pm = SaveMesh(p.m);
                var go = new GameObject(p.r.node);
                go.layer = res.srcNode.gameObject.layer;
                go.transform.SetParent(parent, false);
                go.transform.localPosition = res.srcNode.localPosition; go.transform.localRotation = res.srcNode.localRotation; go.transform.localScale = res.srcNode.localScale;
                go.transform.SetSiblingIndex(++sib);
                go.AddComponent<MeshFilter>().sharedMesh = pm;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = res.mats;
                var srcMr = res.srcNode.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = srcMr.shadowCastingMode; mr.receiveShadows = srcMr.receiveShadows;
                GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(res.srcNode.gameObject));
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            return res.report;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static Mesh SaveMesh(Mesh m)
    {
        Directory.CreateDirectory(MeshDir);
        string path = $"{MeshDir}/{m.name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) { EditorUtility.CopySerialized(m, existing); UnityEngine.Object.DestroyImmediate(m); return existing; }
        AssetDatabase.CreateAsset(m, path); return m;
    }

    // ═════════════════════════ 양면화(떼어 내는 부품) ═════════════════════════
    // 모델이 한 겹(바깥면만)이라 부품을 떼어 들고 안쪽에서 보면 뚫려 보였다 → 뒤집은 면을 덧붙인 '<이름>_2S' 메시로 바꾼다.
    // 장착 상태 겉모습은 그대로(바깥면은 같은 정점·같은 재질). 콜라이더는 건드리지 않는다. 다시 돌리면 원본에서 새로 만든다.
    public static string DoubleSide(IEnumerable<string> names, bool save = true)
    {
        var sb = new System.Text.StringBuilder();
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            foreach (var name in names)
            {
                var t = FindSrc(root, name);
                if (t == null) { sb.AppendLine($"{name}: 노드 없음"); continue; }
                var mf = t.GetComponent<MeshFilter>(); var mr = t.GetComponent<MeshRenderer>();
                var orig = LoadSourceMesh(mf.sharedMesh, name);
                if (orig == null || !orig.isReadable) { sb.AppendLine($"{name}: 원본 메시를 읽을 수 없음"); continue; }
                var vs = orig.vertices; var ns = orig.normals; var ts = orig.tangents; var uv = orig.uv; var uv2 = orig.uv2;
                int S = orig.subMeshCount;
                var a = new Acc(name + "_2S", S);
                var poly = new List<V>(3);
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
                int glassSub = -1;
                var mats = mr != null ? mr.sharedMaterials : new Material[0];
                for (int s = 0; s < mats.Length; s++) if (mats[s] != null && mats[s].name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0) glassSub = s;
                var m = Build(a, true, glassSub);
                if (!save) { sb.AppendLine($"{name}: {orig.triangles.Length / 3} → {m.triangles.Length / 3}삼각형 (미저장)"); UnityEngine.Object.DestroyImmediate(m); continue; }
                var saved = SaveMesh(m);
                mf.sharedMesh = saved;
                sb.AppendLine($"{name}: {orig.triangles.Length / 3} → {saved.triangles.Length / 3}삼각형");
            }
            if (save) { PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); AssetDatabase.SaveAssets(); }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return sb.ToString();
    }

    [MenuItem("Dodrill/차량 메시 분리/차체 외판(매뉴얼 구성부품) 미리보기")]
    private static void MenuPreview()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (src, regions) in Plan()) sb.AppendLine(Preview(regions, "plan_" + src, true, false, null, null, 32f, null, src));
        Debug.Log("[CarBodyPanelSplitter] 미리보기 → " + PreviewDir + System.Environment.NewLine + sb);
    }

    [MenuItem("Dodrill/차량 메시 분리/차체 외판(매뉴얼 구성부품) 적용 + 부품 양면화")]
    private static void MenuApply() => Debug.Log("[CarBodyPanelSplitter] 적용" + System.Environment.NewLine + ApplyAll());

    public static string ApplyAll()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (src, regions) in Plan()) sb.AppendLine(Apply(regions, src));
        sb.AppendLine(DoubleSide(DoubleSideNodes));
        return sb.ToString();
    }
}
#endif
