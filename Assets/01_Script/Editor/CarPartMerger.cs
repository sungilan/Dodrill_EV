#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// ============================================================
//  CarPartMerger.cs  (에디터 전용)
//  Ioniq5N 프리팹의 애매한 조각 노드를 정비 부품 단위로 합친다.
//   · SM_Ioniq_Back + .002 + SM_Tailgate_Upper/Glass/Inner  → SM_Tailgate      (테일게이트 어셈블리)
//   · SM_Ioniq_Back.001 + .003 + .004 + .005                → SM_RearBumper    (리어 범퍼)
//   · SM_Ioniq_Back.006 (뒤 모서리 패널 좌우 한 덩어리)     → SM_RearQuarterL / R (쿼터 패널, 차체)
//   · 다른 차량 FBX(FBX_ElectricCar_Out)의 SM_Ioniq_ChargingPort → SM_ChargingPort (충전 도어)
//  합친 노드는 '기준 노드'(Back / Back.001) 의 변환을 그대로 써서, 그 노드 로컬로 잰 CarPartService 볼트 좌표가 유지된다.
//  순서: CarBodyPanelSplitter.ApplyAll() → CarPartMerger.MergeAll().  다시 돌릴 땐 프리팹을 병합 전 상태로 되돌린 뒤 같은 순서로.
//  메뉴: Dodrill > 차량 메시 분리 > 조각 병합 + 충전 도어
// ============================================================
public static class CarPartMerger
{
    private const string PrefabPath = "Assets/03_3D/Object/O_Prefabs/Dummy/Ioniq5N.prefab";
    private const string MeshDir = "Assets/03_3D/Object/O_Prefabs/Dummy/Ioniq5N_Split";
    private const string OutFbx = "Assets/03_3D/Object/O_FBX/FBX_ElectricCar_Out.fbx";

    private static bool ActiveChain(Transform t) { for (; t != null; t = t.parent) if (!t.gameObject.activeSelf) return false; return true; }
    private static bool UnderCables(Transform t) { for (t = t.parent; t != null; t = t.parent) if (t.name == "Cables") return true; return false; }

    private static Transform FindNode(GameObject root, string name)
    {
        Transform any = null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != name || t.GetComponent<MeshFilter>() == null || !ActiveChain(t) || UnderCables(t)) continue;
            if (t.parent != null && t.parent.name == "FBX_ElectricCar") return t;
            if (any == null) any = t;
        }
        return any;
    }

    private static Mesh SaveMesh(Mesh m)
    {
        Directory.CreateDirectory(MeshDir);
        string path = $"{MeshDir}/{m.name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) { EditorUtility.CopySerialized(m, existing); UnityEngine.Object.DestroyImmediate(m); return existing; }
        AssetDatabase.CreateAsset(m, path); return m;
    }

    /// <summary>여러 노드의 메시를 기준 노드 로컬 좌표로 모아 한 메시(재질별 서브메시)로 만들고, 기준 노드 자리에 새 노드를 만든다.</summary>
    public static string MergeNodes(GameObject root, string[] srcNames, string newName, string frameName)
    {
        var nodes = new List<Transform>(); var miss = new List<string>();
        foreach (var n in srcNames) { var t = FindNode(root, n); if (t == null) miss.Add(n); else nodes.Add(t); }
        if (miss.Count > 0) return $"{newName}: 노드 없음 → {string.Join(", ", miss)} (이미 합쳤거나 프리팹 상태가 다름)";
        var frame = nodes[Array.IndexOf(srcNames, frameName)];
        var frameMr = frame.GetComponent<MeshRenderer>();

        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tans = new List<Vector4>(); var uv0 = new List<Vector2>(); var uv1 = new List<Vector2>();
        var mats = new List<Material>(); var tris = new List<List<int>>();
        foreach (var node in nodes)
        {
            var mesh = node.GetComponent<MeshFilter>().sharedMesh; var mr = node.GetComponent<MeshRenderer>();
            var M = frame.worldToLocalMatrix * node.localToWorldMatrix; bool flip = M.determinant < 0f;
            var vs = mesh.vertices; var ns = mesh.normals; var ts = mesh.tangents; var u0 = mesh.uv; var u1 = mesh.uv2;
            int bv = verts.Count;
            for (int i = 0; i < vs.Length; i++)
            {
                verts.Add(M.MultiplyPoint3x4(vs[i]));
                norms.Add(ns.Length > i ? M.MultiplyVector(ns[i]).normalized : Vector3.up);
                if (ts.Length > i) { var d = M.MultiplyVector(new Vector3(ts[i].x, ts[i].y, ts[i].z)).normalized; tans.Add(new Vector4(d.x, d.y, d.z, flip ? -ts[i].w : ts[i].w)); } else tans.Add(new Vector4(1, 0, 0, 1));
                uv0.Add(u0.Length > i ? u0[i] : Vector2.zero); uv1.Add(u1.Length > i ? u1[i] : Vector2.zero);
            }
            var sm = mr.sharedMaterials;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var mat = s < sm.Length ? sm[s] : null;
                int mi = mats.IndexOf(mat); if (mi < 0) { mats.Add(mat); tris.Add(new List<int>()); mi = mats.Count - 1; }
                var tr = mesh.GetTriangles(s);
                for (int i = 0; i < tr.Length; i += 3)
                {
                    if (flip) { tris[mi].Add(tr[i] + bv); tris[mi].Add(tr[i + 2] + bv); tris[mi].Add(tr[i + 1] + bv); }
                    else { tris[mi].Add(tr[i] + bv); tris[mi].Add(tr[i + 1] + bv); tris[mi].Add(tr[i + 2] + bv); }
                }
            }
        }
        var m = new Mesh { name = newName, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(verts); m.SetNormals(norms); m.SetTangents(tans); m.SetUVs(0, uv0); m.SetUVs(1, uv1);
        m.subMeshCount = mats.Count; for (int s = 0; s < mats.Count; s++) m.SetTriangles(tris[s], s);
        m.RecalculateBounds();
        var saved = SaveMesh(m);

        var go = new GameObject(newName) { layer = frame.gameObject.layer };
        go.transform.SetParent(frame.parent, false);
        go.transform.localPosition = frame.localPosition; go.transform.localRotation = frame.localRotation; go.transform.localScale = frame.localScale;
        go.transform.SetSiblingIndex(frame.GetSiblingIndex());
        go.AddComponent<MeshFilter>().sharedMesh = saved;
        var nmr = go.AddComponent<MeshRenderer>(); nmr.sharedMaterials = mats.ToArray();
        nmr.shadowCastingMode = frameMr.shadowCastingMode; nmr.receiveShadows = frameMr.receiveShadows;
        GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(frame.gameObject));
        var fc = frame.GetComponent<MeshCollider>(); if (fc != null) go.AddComponent<MeshCollider>().sharedMesh = saved;
        foreach (var n in nodes) UnityEngine.Object.DestroyImmediate(n.gameObject);
        return $"{newName} = {srcNames.Length}개 합침 → {saved.triangles.Length / 3}삼각형, 재질 {mats.Count}종 ({string.Join(",", mats.Select(x => x != null ? x.name : "null"))})";
    }

    /// <summary>한 메시 안의 떨어진 조각(용접 정점 기준)을 중심 z 부호로 좌/우로 나눠 두 노드로 만든다</summary>
    public static string SplitLeftRight(GameObject root, string srcName, string nameL, string nameR)
    {
        var src = FindNode(root, srcName); if (src == null) return $"{srcName}: 노드 없음";
        var mesh = src.GetComponent<MeshFilter>().sharedMesh; var mr = src.GetComponent<MeshRenderer>();
        var toRoot = root.transform.worldToLocalMatrix * src.localToWorldMatrix;
        var vs = mesh.vertices; var ns = mesh.normals; var ts = mesh.tangents; var u0 = mesh.uv; var u1 = mesh.uv2;
        var weld = new Dictionary<Vector3Int, int>(); var wid = new int[vs.Length];
        for (int i = 0; i < vs.Length; i++) { var k = Vector3Int.RoundToInt(vs[i] * 2000f); if (!weld.TryGetValue(k, out int w)) { w = weld.Count; weld[k] = w; } wid[i] = w; }
        var par = Enumerable.Range(0, weld.Count).ToArray();
        int Find(int x) { while (par[x] != x) { par[x] = par[par[x]]; x = par[x]; } return x; }
        for (int s = 0; s < mesh.subMeshCount; s++) { var tr = mesh.GetTriangles(s); for (int i = 0; i < tr.Length; i += 3) { int a = Find(wid[tr[i]]); par[Find(wid[tr[i + 1]])] = a; par[Find(wid[tr[i + 2]])] = Find(a); } }
        // 조각 중심 z 부호
        var sum = new Dictionary<int, (Vector3 c, int n)>();
        for (int i = 0; i < vs.Length; i++) { int r = Find(wid[i]); var w3 = toRoot.MultiplyPoint3x4(vs[i]); sum[r] = sum.TryGetValue(r, out var q) ? (q.c + w3, q.n + 1) : (w3, 1); }
        bool Left(int vi) { var q = sum[Find(wid[vi])]; return q.c.z / q.n < 0f; }
        var parts = new[] { nameL, nameR }; var rep = new StringBuilder();
        for (int side = 0; side < 2; side++)
        {
            var vlist = new List<int>(); var map = new Dictionary<int, int>(); var sub = new List<int>[mesh.subMeshCount]; for (int s = 0; s < sub.Length; s++) sub[s] = new List<int>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var tr = mesh.GetTriangles(s);
                for (int i = 0; i < tr.Length; i += 3)
                {
                    bool left = Left(tr[i]); if ((side == 0) != left) continue;
                    for (int k = 0; k < 3; k++) { int o = tr[i + k]; if (!map.TryGetValue(o, out int ni)) { ni = vlist.Count; vlist.Add(o); map[o] = ni; } sub[s].Add(ni); }
                }
            }
            var m = new Mesh { name = parts[side], indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(vlist.Select(i => vs[i]).ToList()); m.SetNormals(vlist.Select(i => ns.Length > i ? ns[i] : Vector3.up).ToList());
            m.SetTangents(vlist.Select(i => ts.Length > i ? ts[i] : new Vector4(1, 0, 0, 1)).ToList());
            m.SetUVs(0, vlist.Select(i => u0.Length > i ? u0[i] : Vector2.zero).ToList()); m.SetUVs(1, vlist.Select(i => u1.Length > i ? u1[i] : Vector2.zero).ToList());
            m.subMeshCount = mesh.subMeshCount; for (int s = 0; s < sub.Length; s++) m.SetTriangles(sub[s], s); m.RecalculateBounds();
            var saved = SaveMesh(m);
            var go = new GameObject(parts[side]) { layer = src.gameObject.layer };
            go.transform.SetParent(src.parent, false); go.transform.localPosition = src.localPosition; go.transform.localRotation = src.localRotation; go.transform.localScale = src.localScale;
            go.transform.SetSiblingIndex(src.GetSiblingIndex());
            go.AddComponent<MeshFilter>().sharedMesh = saved;
            var nmr = go.AddComponent<MeshRenderer>(); nmr.sharedMaterials = mr.sharedMaterials; nmr.shadowCastingMode = mr.shadowCastingMode; nmr.receiveShadows = mr.receiveShadows;
            GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(src.gameObject));
            rep.AppendLine($"{parts[side]} = {saved.triangles.Length / 3}삼각형");
        }
        UnityEngine.Object.DestroyImmediate(src.gameObject);
        return rep.ToString();
    }

    /// <summary>다른 차량 FBX(FBX_ElectricCar_Out)의 충전구 메시를 프리팹 좌표로 옮겨 붙인다. Out FBX → 프리팹: p = (−z − 0.27, y + 0.16, x), 회전 Y 270°(Cockpit·Bumper·Door 로 대조)</summary>
    public static string AddChargingPort(GameObject root)
    {
        var old = FindNode(root, "SM_ChargingPort"); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(OutFbx); if (fbx == null) return "충전구: Out FBX 없음";
        var cp = fbx.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "SM_Ioniq_ChargingPort"); if (cp == null) return "충전구: 메시 없음";
        var mesh = cp.GetComponent<MeshFilter>().sharedMesh;
        var fender = FindNode(root, "SM_FenderFL");
        Material paint = fender != null ? fender.GetComponent<MeshRenderer>().sharedMaterials[0] : cp.GetComponent<MeshRenderer>().sharedMaterial;
        var parent = fender != null ? fender.parent : root.transform;
        var o = cp.position; // FBX 루트 기준(월드) 아닌 로컬 — Out FBX 의 부모가 단위 변환이라 localPosition 사용
        var lp = cp.localPosition;
        var go = new GameObject("SM_ChargingPort") { layer = fender != null ? fender.gameObject.layer : 0 };
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(-lp.z - 0.27f, lp.y + 0.16f, lp.x);
        go.transform.localRotation = Quaternion.Euler(0f, 270f, 0f);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = paint;
        if (fender != null) { var fm = fender.GetComponent<MeshRenderer>(); mr.shadowCastingMode = fm.shadowCastingMode; mr.receiveShadows = fm.receiveShadows; GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(fender.gameObject)); }
        return $"SM_ChargingPort = {mesh.triangles.Length / 3}삼각형, 위치 {go.transform.localPosition}, 재질 {(paint != null ? paint.name : "null")} (FBX_ElectricCar_Out 메시 참조)";
    }

    public static string MergeAll()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath); var sb = new StringBuilder();
        try
        {
            sb.AppendLine(MergeNodes(root, new[] { "SM_Ioniq_Back", "SM_Ioniq_Back.002", "SM_Tailgate_Upper", "SM_Tailgate_Glass", "SM_Tailgate_Inner" }, "SM_Tailgate", "SM_Ioniq_Back"));
            sb.AppendLine(MergeNodes(root, new[] { "SM_Ioniq_Back.001", "SM_Ioniq_Back.003", "SM_Ioniq_Back.004", "SM_Ioniq_Back.005" }, "SM_RearBumper", "SM_Ioniq_Back.001"));
            sb.AppendLine(SplitLeftRight(root, "SM_Ioniq_Back.006", "SM_RearQuarterL", "SM_RearQuarterR"));
            sb.AppendLine(AddChargingPort(root));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); AssetDatabase.SaveAssets();
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return sb.ToString();
    }

    [MenuItem("Dodrill/차량 메시 분리/조각 병합 + 충전 도어")]
    private static void Menu() => Debug.Log("[CarPartMerger]\n" + MergeAll());
}
#endif
