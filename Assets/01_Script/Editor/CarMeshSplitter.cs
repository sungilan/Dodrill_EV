#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// ============================================================
//  CarMeshSplitter.cs  (에디터 전용)
//
//  차 모델에서 '한 덩어리 메시'로 묶여 있는 부품을 분리해 Ioniq5N 프리팹에 넣는다.
//   · 메시는 서로 이어지지 않은 조각(island)으로 이루어져 있다 — 위치가 같은 정점을 한 점으로 보고 삼각형 연결(Union-Find)로 조각을 찾는다.
//   · 조각의 가운데 좌표로 묶음(예: 바퀴 코너 4곳)을 정해, 묶음마다 새 Mesh 애셋을 만들고 같은 자리의 형제 오브젝트로 단다.
//   · 원본 오브젝트는 지우지 않고 비활성(FBX 원본 보존). 새 오브젝트는 원본과 같은 로컬 좌표·재질.
//   · 한 번 돌린 뒤에는 다시 돌리지 않는다(같은 이름이 있으면 건너뜀).
//  메뉴: Dodrill > 차량 메시 분리 > 브레이크(바퀴별) / 시트(앞 좌·우, 뒤)
// ============================================================
public static class CarMeshSplitter
{
    private const string PrefabPath = "Assets/03_3D/Object/O_Prefabs/Dummy/Ioniq5N.prefab";
    private const string MeshDir = "Assets/03_3D/Object/O_Prefabs/Dummy/Ioniq5N_Split";

    private static int Find(int[] p, int x) { while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; } return x; }

    private struct Island { public int root; public List<int>[] tris; public Bounds rootBounds; public int triCount; }

    /// <summary>group(조각의 차 루트 기준 가운데 좌표, 크기) → 묶음 번호(0..names.Length-1) 또는 -1(원본에 남김)</summary>
    public delegate int GroupFunc(Vector3 center, Vector3 size, int tris);

    public static string SplitMesh(GameObject root, string sourceName, string[] newNames, GroupFunc group, bool alsoRemainder, string remainderName)
    {
        // 쓰는 변형(켜져 있고 FBX_ElectricCar 바로 아래)의 원본
        Transform src = null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == sourceName && t.gameObject.activeInHierarchy && t.parent != null && t.parent.name == "FBX_ElectricCar") { src = t; break; }
        if (src == null) return $"{sourceName}: 켜진 원본을 못 찾음(이미 분리했거나 구조가 다름)";
        return SplitFrom(root, src, sourceName, newNames, group, alsoRemainder, remainderName);
    }

    /// <summary>원본 노드를 직접 넘겨 분리한다(FBX_ElectricCar 바로 아래가 아닌 노드 — 예: 배터리 팩 안쪽).</summary>
    public static string SplitFrom(GameObject root, Transform src, string sourceName, string[] newNames, GroupFunc group, bool alsoRemainder, string remainderName)
    {
        var mf = src.GetComponent<MeshFilter>(); var mr = src.GetComponent<MeshRenderer>();
        if (mf == null || mr == null || mf.sharedMesh == null) return $"{sourceName}: MeshFilter/Renderer 없음";
        var mesh = mf.sharedMesh;
        if (!mesh.isReadable) return $"{sourceName}: 메시를 읽을 수 없음(Read/Write 꺼짐)";

        var vs = mesh.vertices;
        var map = new Dictionary<long, int>(); var idx = new int[vs.Length];
        for (int i = 0; i < vs.Length; i++)
        {
            long k = ((long)Mathf.RoundToInt(vs[i].x * 2000f) * 73856093L) ^ ((long)Mathf.RoundToInt(vs[i].y * 2000f) * 19349663L) ^ ((long)Mathf.RoundToInt(vs[i].z * 2000f) * 83492791L);
            if (!map.TryGetValue(k, out int v)) { v = map.Count; map[k] = v; }
            idx[i] = v;
        }
        var par = new int[map.Count]; for (int i = 0; i < par.Length; i++) par[i] = i;
        int sub = mesh.subMeshCount;
        var triSub = new int[sub][];
        for (int s = 0; s < sub; s++)
        {
            triSub[s] = mesh.GetTriangles(s);
            var tr = triSub[s];
            for (int i = 0; i < tr.Length; i += 3)
            {
                int a = Find(par, idx[tr[i]]), b = Find(par, idx[tr[i + 1]]), c = Find(par, idx[tr[i + 2]]);
                par[b] = a; par[Find(par, c)] = Find(par, a);
            }
        }

        // 조각별 범위(차 루트 좌표)
        var bounds = new Dictionary<int, Bounds>(); var counts = new Dictionary<int, int>();
        for (int s = 0; s < sub; s++)
        {
            var tr = triSub[s];
            for (int i = 0; i < tr.Length; i += 3)
            {
                int r = Find(par, idx[tr[i]]);
                // 세 꼭짓점 모두 — 첫 꼭짓점만 보면 팬 모양 삼각형으로 만든 긴 케이블의 범위가 짧게 나온다
                for (int k = 0; k < 3; k++)
                {
                    Vector3 w = root.transform.InverseTransformPoint(src.TransformPoint(vs[tr[i + k]]));
                    if (!bounds.ContainsKey(r)) { bounds[r] = new Bounds(w, Vector3.zero); counts[r] = 0; } else { var bb = bounds[r]; bb.Encapsulate(w); bounds[r] = bb; }
                }
                counts[r]++;
            }
        }
        var rootGroup = new Dictionary<int, int>();
        foreach (var kv in bounds) rootGroup[kv.Key] = group(kv.Value.center, kv.Value.size, counts[kv.Key]);

        int groups = newNames.Length + (alsoRemainder ? 1 : 0);
        Directory.CreateDirectory(MeshDir);
        var report = new System.Text.StringBuilder($"{sourceName}: 조각 {bounds.Count}개 → ");
        for (int g = 0; g < groups; g++)
        {
            bool isRem = g == newNames.Length;
            string nm = isRem ? remainderName : newNames[g];
            // 이 묶음의 삼각형만 모아 새 메시(정점 다시 번호)
            var remap = new Dictionary<int, int>();
            var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nt = new List<Vector4>(); var nuv = new List<Vector2>(); var nuv2 = new List<Vector2>(); var nc = new List<Color>();
            var normals = mesh.normals; var tangents = mesh.tangents; var uv = mesh.uv; var uv2 = mesh.uv2; var cols = mesh.colors;
            var subTris = new List<int>[sub]; int total = 0;
            for (int s = 0; s < sub; s++)
            {
                subTris[s] = new List<int>();
                var tr = triSub[s];
                for (int i = 0; i < tr.Length; i += 3)
                {
                    int r = Find(par, idx[tr[i]]);
                    int gi = rootGroup[r];
                    bool inG = isRem ? gi < 0 : gi == g;
                    if (!inG) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        int o = tr[i + k];
                        if (!remap.TryGetValue(o, out int n))
                        {
                            n = nv.Count; remap[o] = n; nv.Add(vs[o]);
                            if (normals.Length > o) nn.Add(normals[o]);
                            if (tangents.Length > o) nt.Add(tangents[o]);
                            if (uv.Length > o) nuv.Add(uv[o]);
                            if (uv2.Length > o) nuv2.Add(uv2[o]);
                            if (cols.Length > o) nc.Add(cols[o]);
                        }
                        subTris[s].Add(n);
                    }
                    total++;
                }
            }
            if (total == 0) { report.Append($"{nm}=0(건너뜀) "); continue; }
            var m = new Mesh { name = nm, indexFormat = nv.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            m.SetVertices(nv);
            if (nn.Count == nv.Count) m.SetNormals(nn);
            if (nt.Count == nv.Count) m.SetTangents(nt);
            if (nuv.Count == nv.Count) m.SetUVs(0, nuv);
            if (nuv2.Count == nv.Count) m.SetUVs(1, nuv2);
            if (nc.Count == nv.Count) m.SetColors(nc);
            m.subMeshCount = sub;
            for (int s = 0; s < sub; s++) m.SetTriangles(subTris[s], s);
            if (nn.Count != nv.Count) m.RecalculateNormals();
            m.RecalculateBounds();
            string assetPath = $"{MeshDir}/{nm}.asset";
            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(m, assetPath);

            // 같은 부모 아래 같은 로컬 좌표로 형제 오브젝트
            var go = new GameObject(nm);
            go.layer = src.gameObject.layer;
            go.transform.SetParent(src.parent, false);
            go.transform.localPosition = src.localPosition; go.transform.localRotation = src.localRotation; go.transform.localScale = src.localScale;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var nr = go.AddComponent<MeshRenderer>();
            nr.sharedMaterials = mr.sharedMaterials;
            nr.shadowCastingMode = mr.shadowCastingMode; nr.receiveShadows = mr.receiveShadows;
            go.isStatic = src.gameObject.isStatic;
            report.Append($"{nm}={total}tri ");
        }
        src.gameObject.SetActive(false);   // 원본(FBX)은 지우지 않고 끈다
        return report.ToString();
    }

    private static string Run(Func<GameObject, string> work)
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        string r;
        try
        {
            r = work(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Debug.Log("[CarMeshSplitter] " + r);
        return r;
    }

    // 바퀴 코너 중심(차 루트 좌표, 앞 = −X, 왼쪽 = −Z): FL, FR, RL, RR
    private static readonly Vector2[] Corners = { new Vector2(-1.85f, -0.85f), new Vector2(-1.85f, 0.85f), new Vector2(1.15f, -0.85f), new Vector2(1.15f, 0.85f) };

    [MenuItem("Dodrill/차량 메시 분리/브레이크(바퀴별)")]
    public static string SplitBrake()
    {
        return Run(root => SplitMesh(root, "SM_Breaksystem",
            new[] { "SM_Brake_FL", "SM_Brake_FR", "SM_Brake_RL", "SM_Brake_RR" },
            (c, s, t) =>
            {
                // 바퀴 코너 안(앞뒤 0.45m·좌우 0.35m)에 가운데가 있는 조각 = 그 바퀴의 캘리퍼·디스크·방진판. 길게 늘어진 라인(길이 > 1m)은 제외.
                if (Mathf.Max(s.x, s.z) > 1.0f || Mathf.Abs(c.y - 0.46f) > 0.22f) return -1;   // 높이도 휠 중심 근처(0.24~0.68m)만
                for (int i = 0; i < 4; i++)
                    if (Mathf.Abs(c.x - Corners[i].x) < 0.45f && Mathf.Abs(c.z - Corners[i].y) < 0.35f) return i;
                return -1;
            }, true, "SM_BrakeLines"));
    }

    // ── 뒤 서스펜션: 매뉴얼 구성부품으로 조각 분류 ──
    // 반환: 0 쇽L 1 쇽R 2 액슬L 3 액슬R 4 로어암L 5 로어암R 6 트레일링암L 7 트레일링암R, -1 = 크로스 멤버(프레임: 가로 막대 + S자 측면 레일)
    // 좌표는 차 루트 기준(앞 −X, 왼쪽 −Z). 색으로 렌더해 눈으로 확인해 다듬은 값.
    // 매뉴얼의 어퍼 암 프런트·리어·어시스트 암은 이 모델에서 큰 조각으로 구분되지 않는다(가는 링크가 다른 조각에 합쳐짐) → 따로 나누지 않는다.
    public static readonly string[] RearNames = {
        "SM_RearShock_L", "SM_RearShock_R", "SM_RearAxle_L", "SM_RearAxle_R", "SM_RearLowerArm_L", "SM_RearLowerArm_R",
        "SM_RearTrailingArm_L", "SM_RearTrailingArm_R" };

    public static int RearGroup(Vector3 c, Vector3 s, int tris)
    {
        float az = Mathf.Abs(c.z); int side = c.z < 0f ? 0 : 1;
        if (az < 0.12f) return -1;                                                   // 가운데 = 크로스 멤버
        if (az >= 0.66f) return 2 + side;                                            // 바깥쪽 = 액슬(너클·허브·스터드)
        if (c.x > 1.15f && c.x < 1.40f && c.y > 0.55f && c.y < 0.85f && az > 0.50f) return 0 + side;   // 쇽 업소버(스프링·댐퍼·탑마운트)
        if (c.x < 1.12f && az > 0.50f) return 6 + side;                              // 트레일링 암(앞으로 뻗은 암·앞 부싱)
        if (c.y < 0.43f && az > 0.14f) return 4 + side;                              // 로어 암
        return -1;                                                                   // 나머지(S자 측면 레일·브래킷) = 크로스 멤버
    }

    [MenuItem("Dodrill/차량 메시 분리/뒤 서스펜션(매뉴얼 부품별)")]
    public static string SplitRearSuspension()
    {
        return Run(root => SplitMesh(root, "SM_RearSuspension", RearNames, RearGroup, true, "SM_RearCrossmember"));
    }

    // ── 앞 서스펜션: 로어 암(좌·우) + 스태빌라이저 바(바·부싱 브래킷·엔드 링크), 나머지 = 서브 프레임 ──
    // 반환: 0 로어 암 L, 1 로어 암 R, 2 스태빌라이저, -1 서브 프레임(프레임 판·너클·허브·타이로드 등)
    // 조각 값은 SM_FrontSuspension 179개 조각을 색·번호로 렌더해 눈으로 확인한 것(차 루트 기준, 앞 −X, 왼쪽 −Z).
    // 모델의 로어 암은 한 장의 판(텐션 암·레터럴 암이 한 덩어리)이라 매뉴얼의 두 부품으로 나눌 수 없다 → 로어 암 한 부위로 다룬다.
    public static readonly string[] FrontNames = { "SM_FrontLowerArm_L", "SM_FrontLowerArm_R", "SM_FrontStabilizer" };

    public static int FrontGroup(Vector3 c, Vector3 s, int tris)
    {
        float az = Mathf.Abs(c.z); int side = c.z < 0f ? 0 : 1;
        // 로어 암 판(262) + 볼 조인트 스터드(208)
        if (tris >= 255 && tris <= 270 && az > 0.45f && az < 0.65f && c.y < 0.45f) return side;
        if (tris >= 200 && tris <= 215 && az > 0.68f && az < 0.76f && c.y < 0.46f) return side;
        // 스태빌라이저: 바(384) · 부싱 브래킷(330, 238) · 엔드 링크 위(288)·로드(238)·아래 볼(128)
        if (tris >= 380 && tris <= 390 && az < 0.1f && c.x > -1.62f && c.x < -1.52f) return 2;
        if (tris >= 325 && tris <= 335 && az > 0.25f && az < 0.35f && c.x > -1.50f && c.x < -1.42f) return 2;
        if (tris >= 234 && tris <= 242 && az > 0.28f && az < 0.34f && c.x > -1.50f && c.x < -1.42f) return 2;
        if (tris >= 284 && tris <= 292 && az > 0.45f && az < 0.53f && c.x > -1.70f && c.x < -1.62f) return 2;
        if (tris >= 234 && tris <= 242 && az > 0.45f && az < 0.53f && c.x > -1.70f && c.x < -1.62f) return 2;
        if (tris >= 124 && tris <= 132 && az > 0.45f && az < 0.53f && c.x > -1.70f && c.x < -1.63f) return 2;
        return -1;
    }

    [MenuItem("Dodrill/차량 메시 분리/앞 서스펜션(로어 암·스태빌라이저)")]
    public static string SplitFrontSuspension()
    {
        return Run(root => SplitMesh(root, "SM_FrontSuspension", FrontNames, FrontGroup, true, "SM_FrontSubframe"));
    }

    // ── 배터리 팩 안쪽: SM_BatteryParts1(고전압 케이블·커넥터·릴레이 박스) 조각 중 BMU·메인 퓨즈·PRA 자리의 것을 떼어 낸다 ──
    // 차 루트 기준(앞 −X, 왼쪽 −Z). 모델에 이름표가 없어서 위쪽에서 렌더해 눈으로 확인한 자리다:
    //  BMU = 팩 앞 오른쪽 핀 커넥터 박스(x −1.30~−1.05, z 0.05~0.40), 메인 퓨즈 = 팩 뒤 오른쪽 막대 박스(x 0.86~1.06, z 0.05~0.40),
    //  PRA = 팩 뒤 왼쪽 릴레이 블록과 스터드(x 0.84~1.08, z −0.62~0.05)
    public static readonly string[] BatteryNames = { "SM_Bmu", "SM_MainFuse", "SM_Pra" };

    public static int BatteryGroup(Vector3 c, Vector3 s, int tris)
    {
        if (c.y > 0.42f || c.y < 0.22f) return -1;
        if (Mathf.Max(s.x, s.z) > 0.35f) return -1;   // 길게 뻗은 버스바·케이블은 제외 (PRA 블록에 이어져 있어도 따로 둔다)
        if (c.x > -1.30f && c.x < -1.05f && c.z > 0.05f && c.z < 0.40f) return 0;
        if (c.x > 0.86f && c.x < 1.06f && c.z > 0.05f && c.z < 0.40f) return 1;
        if (c.x > 0.84f && c.x < 1.08f && c.z > -0.62f && c.z <= 0.05f) return 2;
        return -1;
    }

    [MenuItem("Dodrill/차량 메시 분리/배터리 팩(BMU·메인 퓨즈·PRA)")]
    public static string SplitBatteryPack()
    {
        return Run(root =>
        {
            Transform pos = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == "BatteryPackPos") { pos = t; break; }
            if (pos == null) return "BatteryPackPos 를 못 찾음";
            Transform src = null;
            foreach (var t in pos.GetComponentsInChildren<Transform>(true)) if (t.name == "SM_BatteryParts1" && t.gameObject.activeSelf) { src = t; break; }
            if (src == null) return "SM_BatteryParts1: 켜진 원본을 못 찾음(이미 분리했거나 구조가 다름)";
            string r = SplitFrom(root, src, "SM_BatteryParts1", BatteryNames, BatteryGroup, true, "SM_BatteryParts1_Rest");
            // 쓰지 않던 하부 커버·상부 케이스 노드를 켠다 — 자유 실습의 부위로 쓴다(둘 다 팩 노드 아래)
            foreach (var t in pos.GetComponentsInChildren<Transform>(true))
                if (t.name == "SM_BatteryCover" || t.name == "SM_Cover") { t.gameObject.SetActive(true); r += $" | {t.name} 켬"; }
            return r;
        });
    }

    // ── 프렁크 메시(SM_Frunk.001)에 한 덩어리로 들어 있는 냉각수 리저버 탱크 2개와 뚜껑 ──
    // 매뉴얼: 리저버 탱크가 모터용·고전압 배터리용 두 개다. 모델에는 회색 둥근 상자 두 개(각 0.238×0.245×0.137m)와 검은 뚜껑 두 개가 오른쪽 위(+Z)에 나란히 있다.
    // (렌더로 확인: 앞쪽 탱크 x −1.976, 뒤쪽 탱크 x −1.732, 뚜껑은 두 탱크가 맞닿는 쪽 위. 같은 자리의 검은 큰 통(파란 캡)은 워셔액 탱크다.)
    // 앞쪽 = 모터 냉각수(냉각수 교환 시나리오 P0C73 대상), 뒤쪽 = 고전압 배터리 냉각수(저전도) — 어느 쪽이 무엇인지는 매뉴얼 그림 없이 정한 것.
    public static readonly string[] CoolantNames = { "SM_CoolantTank_Motor", "SM_CoolantCap_Motor", "SM_CoolantTank_Battery", "SM_CoolantCap_Battery" };

    public static int CoolantGroup(Vector3 c, Vector3 s, int tris)
    {
        if (c.z < 0.35f || c.z > 0.51f) return -1;
        if (s.y > 0.20f && s.y < 0.30f && c.y > 0.65f && c.y < 0.76f)                       // 탱크 몸체(높이 0.245)
        {
            if (c.x > -2.10f && c.x < -1.86f) return 0;
            if (c.x > -1.86f && c.x < -1.62f) return 2;
        }
        if (s.y < 0.03f && s.x < 0.06f && c.y > 0.82f && c.y < 0.85f)                        // 뚜껑(지름 0.048, 두께 0.017)
        {
            if (c.x > -1.96f && c.x < -1.88f) return 1;
            if (c.x > -1.82f && c.x < -1.74f) return 3;
        }
        return -1;
    }

    [MenuItem("Dodrill/차량 메시 분리/냉각수 리저버 탱크(모터·배터리)")]
    public static string SplitCoolantTanks()
    {
        return Run(root =>
        {
            Transform src = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "SM_Frunk.001" && t.gameObject.activeSelf && t.parent != null && t.parent.name == "FBX_ElectricCar") { src = t; break; }
            if (src == null) return "SM_Frunk.001: 켜진 원본을 못 찾음(이미 분리했거나 구조가 다름)";
            string r = SplitFrom(root, src, "SM_Frunk.001", CoolantNames, CoolantGroup, true, "SM_Frunk.001");
            src.name = "SM_Frunk.001_FBX";   // 원본(꺼짐)과 나머지 조각이 같은 이름이면 헷갈린다 — 부위 정의(CarPartService)는 이름이 SM_Frunk.001 인 켜진 쪽을 쓴다
            return r;
        });
    }

    [MenuItem("Dodrill/차량 메시 분리/시트(앞 좌·우, 뒤)")]
    public static string SplitSeats()
    {
        return Run(root => SplitMesh(root, "SM_Seats",
            new[] { "SM_Seat_FL", "SM_Seat_FR", "SM_Seat_Rear" },
            (c, s, t) =>
            {
                // 가운데가 뒤쪽(x > 0.25)이면 뒷좌석, 아니면 z 부호로 앞 좌(−z)/우(+z)
                if (c.x > 0.25f) return 2;
                return c.z < 0f ? 0 : 1;
            }, false, null));
    }
}
#endif
