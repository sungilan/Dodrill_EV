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
    private const int BigIsland = 1500;   // 이보다 큰 조각은 평면으로 자른다, 작은 조각은 통째로

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
        public bool smallOnly;     // 큰 조각은 자르지 않고 작은 조각만 가져온다
        public bool twoSided = true;
    }

    // ── 매뉴얼 구성부품 기준 영역 (바디 — 외장) ──
    // 순서가 중요하다: 앞에서 가져간 삼각형은 뒤 영역이 보지 않는다.
    public static List<Region> Regions()
    {
        var L = new List<Region>();
        return L;
    }

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

    public static Mesh LoadSourceMesh(Mesh current)
    {
        // 프리팹에 꽂힌 것이 이미 남은 차체면 FBX 원본에서 다시
        string path = AssetDatabase.GetAssetPath(current);
        if (current.name == SrcName && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return current;
        foreach (var g in AssetDatabase.FindAssets("FBX_ElectricCarV3 t:Model"))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var m = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Mesh>().FirstOrDefault(x => x.name == SrcName);
            if (m != null) return m;
        }
        return null;
    }

    public static Transform FindSrc(GameObject root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == SrcName && t.parent != null && t.parent.name == "FBX_ElectricCar" && t.GetComponent<MeshFilter>() != null) return t;
        return null;
    }

    public static Result Compute(GameObject root, List<Region> regions)
    {
        var res = new Result();
        var src = FindSrc(root);
        if (src == null) { res.report = "SM_Ioniq_Electric 노드를 못 찾음"; return res; }
        var mesh = LoadSourceMesh(src.GetComponent<MeshFilter>().sharedMesh);
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
        var accs = regions.Select(r => new Acc(r.node, S)).ToList();
        var body = new Acc(BodyMeshName, S);
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
            // 작은 조각은 가운데 점으로 통째 배정
            var islandTo = new Dictionary<int, int>();
            foreach (var kv in cnt)
            {
                if (kv.Value >= BigIsland) continue;
                int to = -1; var c = bb[kv.Key].center;
                for (int g = 0; g < regions.Count && to < 0; g++)
                {
                    var rg = regions[g];
                    if ((rg.subMask & (1 << s)) == 0) continue;
                    bool ok = true; foreach (var h in rg.hs) if (h.Dist(c) < 0) { ok = false; break; }
                    if (ok) to = g;
                }
                islandTo[kv.Key] = to;
            }

            var poly = new List<V>(3);
            for (int i = 0; i < tr.Length; i += 3)
            {
                int r = Find(par, wid[tr[i]]);
                poly.Clear(); poly.Add(V0[tr[i]]); poly.Add(V0[tr[i + 1]]); poly.Add(V0[tr[i + 2]]);
                if (islandTo.TryGetValue(r, out int to))
                {
                    (to >= 0 ? accs[to] : body).Poly(poly, s);
                    continue;
                }
                // 큰 조각: 영역마다 평면으로 잘라 안쪽은 그 부품, 바깥은 다음 영역으로
                var pending = new List<List<V>> { new List<V>(poly) };
                for (int g = 0; g < regions.Count && pending.Count > 0; g++)
                {
                    var rg = regions[g];
                    if (rg.smallOnly || (rg.subMask & (1 << s)) == 0) continue;
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
        for (int g = 0; g < regions.Count; g++)
        {
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
    // 프리팹 애셋은 씬 밖이라 activeInHierarchy 가 늘 false — 조상 activeSelf 로 본다
    private static bool ActiveChain(Transform t) { for (; t != null; t = t.parent) if (!t.gameObject.activeSelf) return false; return true; }

    public static string Preview(List<Region> regions, string tag, bool others = true, bool hideBody = false, (string name, Vector3 eye)[] views = null, Vector3? target = null, float fov = 32f, Func<Transform, bool> skip = null)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var res = Compute(prefab, regions);
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
                    Put(mf.sharedMesh, rootInv * mr.transform.localToWorldMatrix, Enumerable.Repeat(grey, mf.sharedMesh.subMeshCount).ToArray());
                }
            }
            if (!hideBody)
            {
                var bodyMat = Mat(new Color(0.85f, 0.85f, 0.82f)); var glass = Mat(new Color(0.35f, 0.75f, 0.95f));
                Put(res.body, srcMtx, Enumerable.Range(0, res.body.subMeshCount).Select(s => s == 1 ? glass : bodyMat).ToArray());
            }
            foreach (var p in res.parts)
            {
                var m = Mat(p.r.dbg);
                Put(p.m, srcMtx, Enumerable.Repeat(m, p.m.subMeshCount).ToArray());
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
    public static string Apply(List<Region> regions)
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var res = Compute(root, regions);
            if (res.source == null) return res.report;
            Directory.CreateDirectory(MeshDir);
            var parent = res.srcNode.parent;
            // 예전에 만든 부품 노드 정리
            foreach (var r in regions) { var old = parent.Find(r.node); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject); }

            Mesh Save(Mesh m)
            {
                string path = $"{MeshDir}/{m.name}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing != null) { EditorUtility.CopySerialized(m, existing); UnityEngine.Object.DestroyImmediate(m); return existing; }
                AssetDatabase.CreateAsset(m, path); return m;
            }
            var bodyMesh = Save(res.body);
            res.srcNode.GetComponent<MeshFilter>().sharedMesh = bodyMesh;
            var mc = res.srcNode.GetComponent<MeshCollider>(); if (mc != null) mc.sharedMesh = bodyMesh;

            int sib = res.srcNode.GetSiblingIndex();
            foreach (var p in res.parts)
            {
                var pm = Save(p.m);
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

    [MenuItem("Dodrill/차량 메시 분리/차체 외판(매뉴얼 구성부품) 미리보기")]
    private static void MenuPreview() => Debug.Log("[CarBodyPanelSplitter] 미리보기 → " + PreviewDir + "\n" + Preview(Regions(), "panels"));

    [MenuItem("Dodrill/차량 메시 분리/차체 외판(매뉴얼 구성부품) 적용")]
    private static void MenuApply() => Debug.Log("[CarBodyPanelSplitter] 적용\n" + Apply(Regions()));
}
#endif
