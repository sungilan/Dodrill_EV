using System.Collections;
using System.Collections.Generic;
using EPOOutline;
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XRAirpotrSecurity;

// ============================================================
//  TireService.cs
//
//  정비소 차량 네 바퀴를 실제로 떼고 다시 다는 자유 실습.
//  · 바퀴마다 휠 너트 5개(Bolt + FreeWorkBolt)를 만든다 → 에어 임팩렌치(17mm)·임팩트 렌치로 풀고 조인다
//    시나리오 단계와 상관없이 언제든 작업할 수 있다(FreeWorkBolt 표시가 붙은 볼트는 '지금 단계' 검사를 건너뛴다)
//  · 너트 모양은 시나리오 볼트(Bolt 프리팹)의 메시·재질을 그대로 쓴다(씬의 Bolt → 스폰 프리팹 → 에디터 경로 순으로 찾음)
//  · 시나리오 볼트와 같은 표시: 아웃라인(가까이 가면 흰색, 작업 중 초록, 체결 끝 초록), 진행 링, '분해/체결 n/5' 숫자 카드
//  · 너트 5개를 다 풀면 차 바퀴가 '떼어낼 수 있는 휠'(TireWheel, 네트워크 장비)로 바뀐다 → 잡아서 빼낸다
//  · 빈 허브에 휠을 가져가면 반투명 고스트로 장착 위치를 보여 주고, 놓으면 '착' 하고 끼워진다(스냅 애니메이션)
//    → 너트가 덜 조인 상태로 나오고, 5개를 다 조이면 끝
//  · 진행도는 서버가 들고(SyncList) 모든 플레이어에게 같은 모습으로 보인다
//  · 바퀴 자리(위치·차축 방향)는 클라이언트·서버가 같은 규칙으로 찾는다(ResolveWheels) — 예전에는 각자 아무 SM_Wheel 이나 집어
//    서버가 다른 쪽(반대편) 바퀴 자리에 휠을 스폰할 수 있었다.
//  씬에 하나 — EquipmentSlot 이 Equip_TireService 프리팹을 Spawn 한다. 차는 런타임에 생기므로 바퀴는 1초마다 찾는다.
// ============================================================

public class TireService : NetworkBehaviour
{
    public static TireService Instance { get; private set; }

    public const int Wheels = 4;
    public const int NutsPerWheel = 5;

    public enum WheelState { Tight = 0, Off = 2, Mounted = 3 }

    [Tooltip("떼어낸 휠로 Spawn 할 장비(TireWheel)")] public NetworkObject wheelPrefab;
    [Tooltip("휠 중심에서 바깥쪽으로 너트가 놓이는 거리(m) — 휠 폭 절반에서 이만큼 안쪽")] public float nutInset = 0.06f;
    [Tooltip("너트를 바깥쪽으로 더 내미는 거리(m) — 림 안쪽에 묻혀 안 보이면 키운다")] public float nutProtrude = 0.01f;
    [Tooltip("시나리오 볼트 메시 배율(휠 너트는 볼트보다 굵다)")] public float nutScale = 1.35f;
    [Tooltip("너트가 놓이는 원 반지름(m) — 승용차 114.3 PCD ≈ 0.057")] public float pcdRadius = 0.057f;
    [Tooltip("휠을 놓았을 때 허브에 장착되는 거리(m) — VR")] public float mountDistVR = 0.3f;
    [Tooltip("휠을 놓았을 때 허브에 장착되는 거리(m) — PC·모바일(들기 위치가 화면 앞이라 넉넉히)")] public float mountDistPC = 1.6f;
    [Tooltip("빈 허브에 고스트가 보이기 시작하는 거리(m) — VR")] public float ghostDistVR = 0.9f;
    [Tooltip("빈 허브에 고스트가 보이기 시작하는 거리(m) — PC·모바일")] public float ghostDistPC = 3.2f;
    [Tooltip("너트 표시(아웃라인·링)가 나타나는 플레이어-바퀴 거리(m)")] public float markerDist = 3.2f;
    [Tooltip("장착 스냅 애니메이션 시간(초)")] public float snapSeconds = 0.3f;

    private readonly SyncList<float> _nut = new SyncList<float>();
    private readonly SyncList<int> _state = new SyncList<int>();

    // ── 로컬(클라이언트) ──
    private class NutRing { public LineRenderer track, arc; }

    private class HubLabel
    {
        public GameObject root;
        public TextMeshProUGUI txt;
        public string text;
    }

    private class Hub
    {
        public Transform wheel;          // 차의 SM_Wheel_0x
        public MeshFilter wheelMf;
        public Renderer wheelMr;
        public Renderer[] wheelRenderers;
        public Vector3 centerLocal, outLocal;   // 바퀴 로컬: 메시 중심, 바깥(차 바깥쪽) 방향
        public float halfW;              // 휠 폭 절반(월드 m)
        public Transform frame;          // 너트·스터드 부모(로컬 -Y = 바깥쪽)
        public Transform nutRoot;        // 너트들 부모(스냅 중에는 숨김)
        public GameObject studs;         // 휠이 빠졌을 때 보이는 허브·스터드
        public Bolt[] nuts = new Bolt[NutsPerWheel];
        public Outlinable[] outlines = new Outlinable[NutsPerWheel];
        public int[] outState = new int[NutsPerWheel];
        public NutRing[] rings = new NutRing[NutsPerWheel];
        public int builtState = -1;
        public HubLabel label;
        public float labelUntil, flashUntil;
        public string flashText;
        public Coroutine snap;
        public GameObject snapObj;
    }

    private readonly Hub[] _hubs = new Hub[Wheels];
    private bool _found;
    private float _nextFind;
    private readonly float[] _sendAt = new float[Wheels * NutsPerWheel];
    private readonly float[] _pending = new float[Wheels * NutsPerWheel];
    private readonly float[] _activeAt = new float[Wheels * NutsPerWheel];
    private static Material s_nutMat, s_hubMat;

    // 시나리오 볼트에서 가져온 모양
    private static Mesh s_boltMesh;
    private static Material[] s_boltMats;
    private static float s_nextTemplateSearch;

    private static readonly Color IdleOutline = new Color(0.92f, 0.96f, 1f, 1f);
    private static readonly Color WorkOutline = new Color(0.25f, 1f, 0.35f, 1f);   // ImpactWrench 의 작업 중 색과 같다

    // ══════════════════════════════════════════
    //  수명
    // ══════════════════════════════════════════

    private void Awake()
    {
        Instance = this;
        for (int i = 0; i < _pending.Length; i++) { _pending[i] = -1f; _activeAt[i] = -100f; }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        for (int i = 0; i < Wheels; i++)
        {
            var h = _hubs[i];
            if (h == null) continue;
            StopSnap(h);
            DestroyHubVisuals(h);
            SetWheelVisible(h, true);
        }
        if (_ghost != null) Destroy(_ghost);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        if (_nut.Count == 0) for (int i = 0; i < Wheels * NutsPerWheel; i++) _nut.Add(0f);
        if (_state.Count == 0) for (int i = 0; i < Wheels; i++) _state.Add((int)WheelState.Tight);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        _nut.OnChange += OnNutChanged;
        _state.OnChange += OnStateChanged;
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        _nut.OnChange -= OnNutChanged;
        _state.OnChange -= OnStateChanged;
    }

    private void OnNutChanged(SyncListOperation op, int index, float oldItem, float newItem, bool asServer)
    {
        if (asServer || !_found) return;
        if (op != SyncListOperation.Set && op != SyncListOperation.Add) return;
        ApplyNut(index, newItem);
    }

    private void OnStateChanged(SyncListOperation op, int index, int oldItem, int newItem, bool asServer)
    {
        if (asServer || !_found) return;
        if (index < 0 || index >= Wheels) return;
        Rebuild(index);
    }

    // ══════════════════════════════════════════
    //  매 프레임(클라이언트)
    // ══════════════════════════════════════════

    private void Update()
    {
        if (!IsClientStarted) return;
        if (!_found)
        {
            if (Time.unscaledTime < _nextFind) return;
            _nextFind = Time.unscaledTime + 1f;
            if (!FindCar()) return;
            _found = true;
            for (int i = 0; i < Wheels; i++) Rebuild(i);
            return;
        }

        // 차가 사라졌으면(씬 전환·재생성) 다시 찾는다
        for (int i = 0; i < Wheels; i++)
            if (_hubs[i] == null || _hubs[i].wheel == null) { ResetLocal(); return; }

        // 풀린 너트는 떨어뜨린다, 내 진행도는 서버로
        for (int w = 0; w < Wheels; w++)
        {
            var h = _hubs[w];
            for (int n = 0; n < NutsPerWheel; n++)
            {
                var b = h.nuts[n];
                if (b == null) continue;
                if (!b.isAssembleMode && b.isloosened && b.gameObject.activeSelf) DropNut(b);
                int idx = w * NutsPerWheel + n;
                if (_pending[idx] >= 0f && (Time.unscaledTime >= _sendAt[idx] || _pending[idx] >= 1f))
                {
                    NutProgressServerRpc(idx, _pending[idx]);
                    _pending[idx] = -1f;
                    _sendAt[idx] = Time.unscaledTime + 0.1f;
                }
            }
        }
    }

    private void LateUpdate()
    {
        if (!_found) { HideGhost(); return; }
        PlaceFrames();
        UpdateNutVisuals();
        UpdateGhost();
    }

    private void PlaceFrames()
    {
        for (int i = 0; i < Wheels; i++)
        {
            var h = _hubs[i];
            if (h == null || h.wheel == null || h.frame == null) continue;
            Vector3 c = h.wheel.TransformPoint(h.centerLocal);
            Vector3 o = h.wheel.TransformDirection(h.outLocal).normalized;
            Vector3 side = Vector3.Cross(o, Vector3.up); if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(o, Vector3.forward);
            h.frame.SetPositionAndRotation(c + o * (h.halfW - nutInset), Quaternion.LookRotation(side.normalized, -o));
        }
    }

    private void ResetLocal()
    {
        _found = false;
        HideGhost();
        for (int i = 0; i < Wheels; i++)
        {
            var h = _hubs[i];
            if (h != null)
            {
                StopSnap(h);
                DestroyHubVisuals(h);
                if (h.frame != null) Destroy(h.frame.gameObject);
            }
            _hubs[i] = null;
        }
    }

    private static void DestroyHubVisuals(Hub h)
    {
        for (int n = 0; n < NutsPerWheel; n++)
        {
            var r = h.rings[n];
            if (r != null)
            {
                if (r.track != null) Destroy(r.track.gameObject);
                if (r.arc != null) Destroy(r.arc.gameObject);
            }
            h.rings[n] = null;
        }
        if (h.label != null && h.label.root != null) Destroy(h.label.root);
        h.label = null;
    }

    // ══════════════════════════════════════════
    //  차 바퀴 찾기 — 클라이언트·서버 공용
    // ══════════════════════════════════════════

    private struct WheelInfo
    {
        public Transform t;
        public Vector3 centerLocal, outLocal;   // 바퀴 로컬
        public float halfW;                     // 월드 m
        public Vector3 centerWorld, outWorld;
    }

    private static string PathOf(Transform t)
    {
        var sb = new System.Text.StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }

    private static Vector3 AbsScale(Vector3 s) => new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));

    /// <summary>가장 얇은 로컬 축(= 차축)과 그 반폭(월드 m).</summary>
    private static Vector3 ThinAxis(Mesh mesh, Vector3 lossy, out float half)
    {
        Vector3 e = Vector3.Scale(mesh.bounds.extents, AbsScale(lossy));
        if (e.x <= e.y && e.x <= e.z) { half = e.x; return Vector3.right; }
        if (e.y <= e.z) { half = e.y; return Vector3.up; }
        half = e.z; return Vector3.forward;
    }

    /// <summary>
    /// 차의 SM_Wheel_01~04 를 정한다. 차 모델이 여러 벌(LOD·변형) 겹쳐 있으므로 '네 바퀴가 다 있는 같은 부모' 묶음 중 경로가 가장 앞선 것을 쓴다.
    /// (예전에는 아무거나 처음 걸린 것을 써서 클라이언트·서버가 서로 다른 바퀴를 가리킬 수 있었다.)
    /// </summary>
    private static bool ResolveWheels(WheelInfo[] res)
    {
        var groups = new Dictionary<Transform, Transform[]>();
        var all = new List<KeyValuePair<string, Transform>>[Wheels];
        for (int i = 0; i < Wheels; i++) all[i] = new List<KeyValuePair<string, Transform>>();

        foreach (var mf in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
        {
            if (mf == null || mf.sharedMesh == null || !mf.gameObject.activeInHierarchy) continue;
            string n = mf.name;
            if (!n.StartsWith("SM_Wheel_0") || n.Length < 11) continue;
            int k = n[10] - '1';
            if (k < 0 || k >= Wheels) continue;
            // 월드 크기로 판정 — 메시가 작고 스케일 100 으로 키운 변형도 진짜 바퀴다
            if (Vector3.Scale(mf.sharedMesh.bounds.size, AbsScale(mf.transform.lossyScale)).magnitude < 0.5f) continue;

            var key = mf.transform.parent != null ? mf.transform.parent : mf.transform;
            if (!groups.TryGetValue(key, out var arr)) { arr = new Transform[Wheels]; groups[key] = arr; }
            if (arr[k] == null) arr[k] = mf.transform;
            all[k].Add(new KeyValuePair<string, Transform>(PathOf(mf.transform), mf.transform));
        }

        Transform[] pick = null; string pickKey = null;
        foreach (var kv in groups)
        {
            bool full = true;
            for (int i = 0; i < Wheels; i++) if (kv.Value[i] == null) { full = false; break; }
            if (!full) continue;
            string key = PathOf(kv.Key);
            if (pick == null || string.CompareOrdinal(key, pickKey) < 0) { pick = kv.Value; pickKey = key; }
        }
        if (pick == null)
        {
            // 묶음이 없으면 번호별로 경로가 가장 앞선 것
            pick = new Transform[Wheels];
            for (int i = 0; i < Wheels; i++)
            {
                string best = null;
                foreach (var kv in all[i]) if (best == null || string.CompareOrdinal(kv.Key, best) < 0) { best = kv.Key; pick[i] = kv.Value; }
                if (pick[i] == null) return false;
            }
        }

        var centers = new Vector3[Wheels];
        Vector3 carCenter = Vector3.zero;
        for (int i = 0; i < Wheels; i++)
        {
            var mb = pick[i].GetComponent<MeshFilter>().sharedMesh.bounds;
            centers[i] = pick[i].TransformPoint(mb.center);
            carCenter += centers[i];
        }
        carCenter /= Wheels;

        for (int i = 0; i < Wheels; i++)
        {
            var w = pick[i];
            var mesh = w.GetComponent<MeshFilter>().sharedMesh;
            Vector3 axis = ThinAxis(mesh, w.lossyScale, out float half);
            Vector3 axisW = w.TransformDirection(axis).normalized;
            if (Vector3.Dot(centers[i] - carCenter, axisW) < 0f) { axis = -axis; axisW = -axisW; }   // 차 바깥쪽이 앞
            res[i] = new WheelInfo { t = w, centerLocal = mesh.bounds.center, outLocal = axis, halfW = half, centerWorld = centers[i], outWorld = axisW };
        }
        return true;
    }

    private bool FindCar()
    {
        var info = new WheelInfo[Wheels];
        if (!ResolveWheels(info)) return false;

        for (int i = 0; i < Wheels; i++)
        {
            var w = info[i].t;
            var h = new Hub
            {
                wheel = w,
                wheelMf = w.GetComponent<MeshFilter>(),
                wheelMr = w.GetComponent<Renderer>(),
                wheelRenderers = w.GetComponentsInChildren<Renderer>(true),
                centerLocal = info[i].centerLocal,
                outLocal = info[i].outLocal,
                halfW = info[i].halfW,
            };
            var fr = new GameObject($"[TireHub {i + 1}]");
            fr.transform.SetParent(transform, false);
            h.frame = fr.transform;
            var nr = new GameObject("Nuts");
            nr.transform.SetParent(h.frame, false);
            h.nutRoot = nr.transform;
            h.studs = BuildStuds(h.frame);
            _hubs[i] = h;
        }
        PlaceFrames();
        return true;
    }

    // ══════════════════════════════════════════
    //  너트·허브 만들기
    // ══════════════════════════════════════════

    private static Material NutMat
    {
        get
        {
            if (s_nutMat != null) return s_nutMat;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            s_nutMat = new Material(sh) { name = "LugNut" };
            s_nutMat.color = new Color(0.62f, 0.63f, 0.66f);
            if (s_nutMat.HasProperty("_Metallic")) s_nutMat.SetFloat("_Metallic", 0.9f);
            if (s_nutMat.HasProperty("_Smoothness")) s_nutMat.SetFloat("_Smoothness", 0.6f);
            return s_nutMat;
        }
    }

    private static Material HubMat
    {
        get
        {
            if (s_hubMat != null) return s_hubMat;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            s_hubMat = new Material(sh) { name = "WheelHub" };
            s_hubMat.color = new Color(0.25f, 0.22f, 0.2f);
            if (s_hubMat.HasProperty("_Metallic")) s_hubMat.SetFloat("_Metallic", 0.7f);
            if (s_hubMat.HasProperty("_Smoothness")) s_hubMat.SetFloat("_Smoothness", 0.3f);
            return s_hubMat;
        }
    }

    private static GameObject Cyl(Transform parent, string name, Vector3 lp, float dia, float len, Material m)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        g.name = name;
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = lp;
        g.transform.localScale = new Vector3(dia, len * 0.5f, dia);
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    private Vector3 NutLocal(int n)
    {
        float a = n * Mathf.PI * 2f / NutsPerWheel;
        return new Vector3(Mathf.Cos(a) * pcdRadius, 0f, Mathf.Sin(a) * pcdRadius);
    }

    /// <summary>휠을 뗐을 때 보이는 허브(브레이크 디스크·허브 플랜지·스터드 볼트).</summary>
    private GameObject BuildStuds(Transform frame)
    {
        var root = new GameObject("Studs");
        root.transform.SetParent(frame, false);
        Cyl(root.transform, "Disc", new Vector3(0f, 0.045f, 0f), 0.30f, 0.025f, HubMat);
        Cyl(root.transform, "Flange", new Vector3(0f, 0.02f, 0f), 0.15f, 0.02f, HubMat);
        for (int n = 0; n < NutsPerWheel; n++)
            Cyl(root.transform, "Stud" + n, NutLocal(n) + new Vector3(0f, -0.005f, 0f), 0.012f, 0.04f, NutMat);
        root.SetActive(false);
        return root;
    }

    // ── 시나리오 볼트 모양 찾기 ──

    private static bool TakeTemplate(MeshFilter mf)
    {
        if (mf == null || mf.sharedMesh == null) return false;
        var mr = mf.GetComponent<MeshRenderer>();
        if (mr == null || mr.sharedMaterial == null) return false;
        var e = mf.sharedMesh.bounds.extents;
        if (e.magnitude > 0.1f) return false;                       // 볼트 크기만
        if (!(e.z > e.x && e.z > e.y)) return false;                // 긴 축이 Z, 머리는 +Z 인 시나리오 볼트 모양
        s_boltMesh = mf.sharedMesh;
        s_boltMats = mr.sharedMaterials;
        return true;
    }

    private static bool EnsureBoltTemplate()
    {
        if (s_boltMesh != null) return true;
        if (Time.unscaledTime < s_nextTemplateSearch) return false;
        s_nextTemplateSearch = Time.unscaledTime + 3f;

        // 1) 씬에 있는 시나리오 볼트
        foreach (var b in FindObjectsByType<Bolt>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (b == null || b.GetComponent<FreeWorkBolt>() != null) continue;
            if (TakeTemplate(b.GetComponent<MeshFilter>()) || TakeTemplate(b.GetComponentInChildren<MeshFilter>(true))) return true;
        }

        // 2) 스폰 가능한 프리팹(시나리오가 아직 볼트를 안 만들었어도 프리팹 목록에는 있다)
        try
        {
            var nm = InstanceFinder.NetworkManager;
            var list = nm != null ? nm.SpawnablePrefabs : null;
            if (list != null)
            {
                int cnt = list.GetObjectCount();
                for (int i = 0; i < cnt; i++)
                {
                    var nob = list.GetObject(false, i);
                    if (nob == null) continue;
                    var bolt = nob.GetComponentInChildren<Bolt>(true);
                    if (bolt == null || bolt.GetComponent<FreeWorkBolt>() != null) continue;
                    if (TakeTemplate(bolt.GetComponent<MeshFilter>()) || TakeTemplate(bolt.GetComponentInChildren<MeshFilter>(true))) return true;
                }
            }
        }
        catch (System.Exception) { }

#if UNITY_EDITOR
        // 3) 에디터: 프로젝트의 볼트 프리팹
        var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/03_3D/Object/O_Prefabs/Dummy/Front_Cover_Bolts.prefab");
        if (asset != null)
        {
            var bolt = asset.GetComponentInChildren<Bolt>(true);
            if (bolt != null && (TakeTemplate(bolt.GetComponent<MeshFilter>()) || TakeTemplate(bolt.GetComponentInChildren<MeshFilter>(true)))) return true;
        }
#endif
        return false;
    }

    private Bolt MakeNut(Hub h, int w, int n)
    {
        var g = new GameObject($"LugNut {w + 1}-{n + 1}");
        g.transform.SetParent(h.nutRoot, false);
        g.transform.localPosition = NutLocal(n) + new Vector3(0f, -nutProtrude, 0f);
        g.transform.localRotation = Quaternion.identity;
        g.layer = 18;
        try { g.tag = "Bolt"; } catch { }
        var col = g.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.center = new Vector3(0f, -0.02f, 0f);
        col.radius = 0.028f;

        if (EnsureBoltTemplate())
        {
            // 시나리오 볼트와 같은 메시·재질. 메시는 +Z 가 머리 → 바깥쪽(-Y)으로 세운다
            var vis = new GameObject("Mesh");
            vis.layer = 18;
            vis.transform.SetParent(g.transform, false);
            vis.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            vis.transform.localScale = Vector3.one * nutScale;
            vis.AddComponent<MeshFilter>().sharedMesh = s_boltMesh;
            var mr = vis.AddComponent<MeshRenderer>();
            mr.sharedMaterials = s_boltMats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        else
        {
            // 볼트 프리팹을 못 찾았을 때의 임시 모양(육각 너트 느낌의 원기둥)
            var vis = Cyl(g.transform, "Hex", new Vector3(0f, -0.008f, 0f), 0.026f, 0.026f, NutMat);
            vis.layer = 18;
            var cap = Cyl(g.transform, "Cap", new Vector3(0f, -0.022f, 0f), 0.02f, 0.005f, NutMat);
            cap.layer = 18;
        }

        var b = g.AddComponent<Bolt>();   // Awake 에서 지금 위치를 원점으로 기억
        b.timeToComplete = 1.1f;
        b.ejectLocalY = 0.03f;
        g.AddComponent<FreeWorkBolt>();
        int idx = w * NutsPerWheel + n;
        b.OnProgressChanged += _ => { _pending[idx] = b.Progress; _activeAt[idx] = Time.time; };
        return b;
    }

    /// <summary>바퀴 상태에 맞춰 너트·허브·차 바퀴 모습을 다시 만든다.</summary>
    private void Rebuild(int w)
    {
        var h = _hubs[w];
        if (h == null || w >= _state.Count) return;
        int st = _state[w];
        if (h.builtState == st) return;
        int prev = h.builtState;
        h.builtState = st;

        StopSnap(h);
        // 조립(체결)을 막 끝냈으면 조인 너트는 바닥에 떨구지 않고 제자리에서 서서히 사라지고, 조립된 휠의 너트가 나타난다
        bool justTightened = prev == (int)WheelState.Mounted && st == (int)WheelState.Tight;
        // 이전 너트 표시 정리
        for (int n = 0; n < NutsPerWheel; n++)
        {
            if (h.nuts[n] != null)
            {
                if (justTightened && h.nuts[n].gameObject.activeInHierarchy) FadeOutNut(h.nuts[n]);
                else Destroy(h.nuts[n].gameObject);
            }
            h.nuts[n] = null;
            h.outlines[n] = null;
            h.outState[n] = -1;
            var r = h.rings[n];
            if (r != null)
            {
                if (r.track != null) Destroy(r.track.gameObject);
                if (r.arc != null) Destroy(r.arc.gameObject);
                h.rings[n] = null;
            }
            _pending[w * NutsPerWheel + n] = -1f;
        }
        if (prev >= 0 && st != (int)WheelState.Mounted)
            FlashLabel(h, st == (int)WheelState.Off ? "<b>분해 완료</b>\n<size=62%><color=#8FD3FF>휠을 잡아서 빼내세요</color></size>"
                                                    : "<b>체결 완료</b>\n<size=62%><color=#8FD3FF>휠이 고정되었습니다</color></size>");

        bool off = st == (int)WheelState.Off;
        bool snapping = prev == (int)WheelState.Off && st == (int)WheelState.Mounted && h.wheelMf != null && h.wheelMf.sharedMesh != null;
        if (h.studs != null) h.studs.SetActive(off || snapping);
        if (off) { SetWheelVisible(h, false); return; }

        for (int n = 0; n < NutsPerWheel; n++)
        {
            var b = MakeNut(h, w, n);
            if (st == (int)WheelState.Mounted) b.ShowAssembleStart();
            h.nuts[n] = b;
            int idx = w * NutsPerWheel + n;
            // 막 조립을 끝낸 경우 서버는 진행도를 0 으로 되돌리지만, 상태 변경이 먼저 도착하면 여기엔 아직 이전 값(1)이 남아 있다.
            // 그 값을 새 너트에 넣으면 '다 풀림'으로 보고 바닥에 떨어뜨리고, 이후 풀기 표시도 안 나왔다 → 이때는 적용하지 않는다
            if (!justTightened && idx < _nut.Count && _nut[idx] > 0f) ApplyNut(idx, _nut[idx]);
            if (justTightened) StartCoroutine(ScaleIn(b.transform, 0.25f, 0.35f));
        }

        if (snapping)
        {
            SetWheelVisible(h, false);
            h.nutRoot.gameObject.SetActive(false);
            h.snap = StartCoroutine(SnapRoutine(w, h));
        }
        else SetWheelVisible(h, true);
    }

    private void ApplyNut(int idx, float p)
    {
        int w = idx / NutsPerWheel, n = idx % NutsPerWheel;
        if (w < 0 || w >= Wheels || _hubs[w] == null) return;
        var b = _hubs[w].nuts[n];
        if (b == null || p <= b.Progress) return;
        b.ApplyRemoteProgress(p);
        _activeAt[idx] = Time.time;
    }

    /// <summary>조인 너트(조립 모드)를 제자리에서 줄어들며 사라지게 한다 — 기능은 바로 끈다.</summary>
    private void FadeOutNut(Bolt b)
    {
        var go = b.gameObject;
        b.enabled = false;
        foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
        var o = go.GetComponent<Outlinable>(); if (o != null) o.enabled = false;
        go.transform.SetParent(null, true);
        go.name = "[TightenedNut]";
        StartCoroutine(ShrinkAndDestroy(go.transform, 0.35f));
    }

    private static IEnumerator ShrinkAndDestroy(Transform t, float dur)
    {
        Vector3 s0 = t.localScale;
        for (float e = 0f; e < dur && t != null; e += Time.deltaTime)
        {
            t.localScale = s0 * (1f - Mathf.SmoothStep(0f, 1f, e / dur));
            yield return null;
        }
        if (t != null) Destroy(t.gameObject);
    }

    private static IEnumerator ScaleIn(Transform t, float delay, float dur)
    {
        if (t == null) yield break;
        Vector3 s1 = t.localScale;
        t.localScale = Vector3.zero;
        yield return new WaitForSeconds(delay);
        for (float e = 0f; e < dur && t != null; e += Time.deltaTime)
        {
            t.localScale = s1 * Mathf.SmoothStep(0f, 1f, e / dur);
            yield return null;
        }
        if (t != null) t.localScale = s1;
    }

    private static void SetWheelVisible(Hub h, bool on)
    {
        if (h.wheelRenderers == null) return;
        foreach (var r in h.wheelRenderers) if (r != null) r.enabled = on;
    }

    /// <summary>다 풀린 너트: 원래 것은 숨기고 물리 복사본을 바닥에 떨어뜨린다(이 화면에서만, 8초 뒤 사라짐).</summary>
    private static void DropNut(Bolt b)
    {
        var copy = new GameObject("[LooseNut]");
        var mf = b.GetComponentInChildren<MeshFilter>();
        float len = 0.03f, rad = 0.012f;
        if (mf != null && mf.sharedMesh != null)
        {
            // 볼트 메시를 그대로 복사해 떨어뜨린다
            copy.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
            copy.transform.localScale = mf.transform.lossyScale;
            copy.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var cmr = copy.AddComponent<MeshRenderer>();
            cmr.sharedMaterials = mf.GetComponent<MeshRenderer>().sharedMaterials;
            var cap = copy.AddComponent<CapsuleCollider>();
            cap.direction = 2;   // 메시의 긴 축 = Z
            cap.center = mf.sharedMesh.bounds.center;
            cap.radius = Mathf.Max(mf.sharedMesh.bounds.extents.x, mf.sharedMesh.bounds.extents.y) * 0.9f;
            cap.height = mf.sharedMesh.bounds.size.z;
        }
        else
        {
            copy.transform.SetPositionAndRotation(b.transform.position, b.transform.rotation);
            Cyl(copy.transform, "Hex", new Vector3(0f, -0.008f, 0f), 0.026f, 0.026f, NutMat);
            var c = copy.AddComponent<CapsuleCollider>();
            c.radius = rad; c.height = len; c.center = new Vector3(0f, -0.008f, 0f);
        }
        var rb = copy.AddComponent<Rigidbody>();
        rb.mass = 0.05f;
        rb.linearVelocity = -b.transform.up * 0.6f + Random.insideUnitSphere * 0.15f;
        rb.angularVelocity = Random.insideUnitSphere * 8f;
        AudioSource.PlayClipAtPoint(EquipFx.Click, b.transform.position, 0.6f);
        Destroy(copy, 8f);
        b.gameObject.SetActive(false);
    }

    // ══════════════════════════════════════════
    //  너트 표시 — 아웃라인·진행 링·'n/5' 카드 (시나리오 볼트와 같은 모양)
    // ══════════════════════════════════════════

    private static bool IsVRPlatform => Utils.GetPlatformType() == PlatformType.VR;

    public Vector3 HubCenter(int w)
    {
        var h = _hubs[w];
        return h != null && h.wheel != null ? h.wheel.TransformPoint(h.centerLocal) : Vector3.zero;
    }

    private void UpdateNutVisuals()
    {
        var cam = Camera.main;
        bool hasCam = cam != null;
        Vector3 cp = hasCam ? cam.transform.position : Vector3.zero;
        float near = IsVRPlatform ? Mathf.Min(markerDist, 2.2f) : markerDist;

        for (int w = 0; w < Wheels; w++)
        {
            var h = _hubs[w];
            if (h == null || h.wheel == null) continue;
            int st = w < _state.Count ? _state[w] : (int)WheelState.Tight;
            bool nearHub = hasCam && (HubCenter(w) - cp).sqrMagnitude < near * near;
            bool anyActive = false;
            int done = 0;

            for (int n = 0; n < NutsPerWheel; n++)
            {
                var b = h.nuts[n];
                if (b == null) continue;
                int idx = w * NutsPerWheel + n;
                if (b.isloosened) done++;
                bool alive = b.gameObject.activeInHierarchy;
                bool open = alive && !b.isloosened;
                bool active = open && b.Progress > 0f && Time.time - _activeAt[idx] < 0.5f;
                if (active) anyActive = true;

                DrawNutRing(h, n, b, open && nearHub, active, cam);
                SetNutOutline(h, n, b, open && nearHub ? (active ? 2 : 1) : 0);
            }

            // 타이어 가까이 가면 지금 할 일(분해/장착/조립)을 카드로 늘 보여 준다
            bool guide = hasCam && (HubCenter(w) - cp).sqrMagnitude < 2.2f * 2.2f;
            if (guide)
            {
                h.labelUntil = Mathf.Max(h.labelUntil, Time.time + 0.3f);
                EquipFx.Notice("tire_guide", "타이어 교체",
                    "1. 임팩트 렌치(17mm)로 휠 너트 5개 풀기  2. 휠 잡아 빼기  3. 휠을 허브에 다시 끼우기  4. 너트 5개 조여 조립",
                    EquipFx.Info, 4f, 60f);
            }
            UpdateLabel(h, w, st, done, anyActive, cam);
        }
    }

    private static Outlinable EnsureOutline(Bolt b)
    {
        var o = b.GetComponent<Outlinable>();
        if (o == null)
        {
            o = b.gameObject.AddComponent<Outlinable>();
            o.AddAllChildRenderersToRenderingList();
        }
        o.RenderStyle = RenderStyle.Single;
        return o;
    }

    /// <summary>0 = 끔, 1 = 흰색(가까이 있을 때 '이 너트를 푸세요'), 2 = 초록(지금 작업 중).</summary>
    private void SetNutOutline(Hub h, int n, Bolt b, int state)
    {
        if (b.IsMarkedDone) return;   // 체결 끝난 너트의 초록 표시는 Bolt 가 관리
        if (h.outState[n] == state) return;
        h.outState[n] = state;
        if (state == 0)
        {
            if (h.outlines[n] != null) h.outlines[n].enabled = false;
            return;
        }
        var o = h.outlines[n] != null ? h.outlines[n] : (h.outlines[n] = EnsureOutline(b));
        o.enabled = true;
        var p = o.OutlineParameters;
        p.Color = state == 2 ? WorkOutline : IdleOutline;
        p.Enabled = true;
    }

    // ── 진행 링 ──

    private static LineRenderer MakeRing(string goName, Transform parent)
    {
        var go = new GameObject(goName);
        go.layer = 1;   // TransparentFX — VR·PC 카메라 모두 그림
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        var sh = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
        var m = new Material(sh);
        m.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);   // 림·차체에 가려도 보이게
        m.renderQueue = 4000;
        lr.material = m;
        go.SetActive(false);
        return lr;
    }

    private static void SetArc(LineRenderer lr, Vector3 c, Vector3 u, Vector3 v, float r, float frac, Color col)
    {
        if (!lr.gameObject.activeSelf) lr.gameObject.SetActive(true);
        lr.startColor = lr.endColor = col;
        int count = Mathf.Max(2, Mathf.CeilToInt(40 * frac) + 1);
        lr.positionCount = count;
        for (int i = 0; i < count; i++)
        {
            float a = Mathf.PI * 0.5f - Mathf.PI * 2f * frac * i / (count - 1);   // 12시 방향에서 시계 방향으로 찬다
            lr.SetPosition(i, c + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * r);
        }
    }

    private void DrawNutRing(Hub h, int n, Bolt b, bool showTrack, bool active, Camera cam)
    {
        var ring = h.rings[n];
        if (!showTrack || cam == null)
        {
            if (ring != null)
            {
                if (ring.track != null && ring.track.gameObject.activeSelf) ring.track.gameObject.SetActive(false);
                if (ring.arc != null && ring.arc.gameObject.activeSelf) ring.arc.gameObject.SetActive(false);
            }
            return;
        }
        if (ring == null)
        {
            ring = h.rings[n] = new NutRing { track = MakeRing("[NutTrack]", h.frame), arc = MakeRing("[NutArc]", h.frame) };
        }

        Vector3 c = b.transform.position + h.frame.up * -0.02f;   // 너트 머리 쪽(바깥)
        Vector3 nrm = (cam.transform.position - c).normalized;
        Vector3 u = Vector3.Cross(nrm, Vector3.up);
        if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(nrm, Vector3.right);
        u.Normalize();
        Vector3 v = Vector3.Cross(nrm, u);
        float radius = 0.027f;
        float width = 0.0035f;

        SetArc(ring.track, c, u, v, radius, 1f, active ? new Color(1f, 1f, 1f, 0.35f) : new Color(1f, 1f, 1f, 0.22f));
        ring.track.widthMultiplier = width;
        if (active)
        {
            SetArc(ring.arc, c, u, v, radius, Mathf.Max(Mathf.Clamp01(b.Progress), 0.001f), WorkOutline);
            ring.arc.widthMultiplier = width * 1.4f;
        }
        else if (ring.arc.gameObject.activeSelf) ring.arc.gameObject.SetActive(false);
    }

    // ── '분해/체결 n/5' 카드 ──

    private HubLabel MakeLabel(Hub h, int w)
    {
        var lb = new HubLabel();
        var root = new GameObject($"[NutCount {w + 1}]");
        root.layer = 1;
        root.transform.SetParent(h.frame, false);
        var cg = new GameObject("Canvas");
        cg.layer = 1;
        cg.transform.SetParent(root.transform, false);
        var canvas = cg.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var crt = (RectTransform)cg.transform;
        crt.sizeDelta = new Vector2(210f, 74f);
        cg.transform.localScale = Vector3.one * 0.0016f;

        var bgGo = new GameObject("BG");
        bgGo.layer = 1;
        bgGo.transform.SetParent(cg.transform, false);
        var bgRt = bgGo.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one; bgRt.offsetMin = Vector2.zero; bgRt.offsetMax = Vector2.zero;
        var img = bgGo.AddComponent<Image>();
        WorldHoverLabel.StyleCard(img);

        var tGo = new GameObject("Text");
        tGo.layer = 1;
        tGo.transform.SetParent(cg.transform, false);
        var trt = tGo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = new Vector2(8f, 4f); trt.offsetMax = new Vector2(-8f, -4f);
        var t = tGo.AddComponent<TextMeshProUGUI>();
        var kf = MobileWorkButton.KoreanFont();   // 기본 TMP 글꼴엔 한글이 없어 안내 글자가 ㅁ 으로 나왔다
        if (kf != null) t.font = kf; else if (TMP_Settings.defaultFontAsset != null) t.font = TMP_Settings.defaultFontAsset;
        t.richText = true;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.enableAutoSizing = false;
        t.fontSize = 30f;
        t.color = Color.white;
        t.raycastTarget = false;
        WorldHoverLabel.DrawOnTop(root);   // 차체·림에 가려지지 않게

        lb.root = root;
        lb.txt = t;
        root.SetActive(false);
        return lb;
    }

    private void FlashLabel(Hub h, string text)
    {
        h.flashText = text;
        h.flashUntil = Time.time + 1.8f;
        h.labelUntil = Mathf.Max(h.labelUntil, h.flashUntil);
    }

    private void UpdateLabel(Hub h, int w, int st, int done, bool anyActive, Camera cam)
    {
        if (anyActive) h.labelUntil = Time.time + 2.5f;
        bool show = cam != null && Time.time < h.labelUntil;
        if (!show)
        {
            if (h.label != null && h.label.root.activeSelf) h.label.root.SetActive(false);
            return;
        }
        if (h.label == null) h.label = MakeLabel(h, w);
        var lb = h.label;
        if (!lb.root.activeSelf) lb.root.SetActive(true);

        string text;
        if (Time.time < h.flashUntil && !string.IsNullOrEmpty(h.flashText)) text = h.flashText;
        else
        {
            bool assemble = st == (int)WheelState.Mounted;
            if (st == (int)WheelState.Off)
                text = "<b>분해됨</b>\n<size=62%><color=#8FD3FF>휠을 이 허브에 끼우면 조립 단계로</color></size>";
            else if (assemble)
                text = $"<b>조립(체결) {done}/{NutsPerWheel}</b>\n<size=62%><color=#8FD3FF>휠 너트를 조이세요</color></size>";
            else
                text = $"<b>분해 {done}/{NutsPerWheel}</b>\n<size=62%><color=#8FD3FF>휠 너트를 푸세요</color></size>";
        }
        if (lb.text != text) { lb.text = text; lb.txt.text = text; }

        // 바퀴 바깥쪽 위에 띄우고 카메라를 본다(가까울수록 작게)
        Vector3 outward = -h.frame.up;
        var tr = lb.root.transform;
        tr.position = h.frame.position + outward * 0.16f + Vector3.up * 0.24f;
        tr.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
        float d = Vector3.Distance(cam.transform.position, tr.position);
        tr.localScale = Vector3.one * Mathf.Clamp(d / 1.8f, 0.4f, 1.1f);
    }

    // ══════════════════════════════════════════
    //  장착 고스트 · 스냅 애니메이션 (클라이언트)
    // ══════════════════════════════════════════

    public float MountDistance => IsVRPlatform ? mountDistVR : mountDistPC;
    public float GhostDistance => IsVRPlatform ? ghostDistVR : ghostDistPC;

    private GameObject _ghost;
    private MeshFilter _ghostMf;
    private Material _ghostMat;
    private Outlinable _ghostOutline;
    private int _ghostHub = -1;
    private int _ghostFrame = -10;
    private bool _ghostInRange;

    private static Material MakeGhostMaterial()
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        var m = new Material(sh) { name = "WheelGhost" };
        if (m.HasProperty("_Surface"))
        {
            m.SetFloat("_Surface", 1f);   // Transparent
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
        m.renderQueue = 3000;
        return m;
    }

    /// <summary>TireWheel 이 휠을 들고 있는 동안 매 프레임 부른다. hub = 가장 가까운 빈 허브, inRange = 놓으면 장착되는 거리인가.</summary>
    public void ShowMountGhost(int hub, bool inRange)
    {
        _ghostHub = hub;
        _ghostInRange = inRange;
        _ghostFrame = Time.frameCount;
    }

    private void HideGhost()
    {
        if (_ghost != null && _ghost.activeSelf) _ghost.SetActive(false);
    }

    private void UpdateGhost()
    {
        bool want = Time.frameCount - _ghostFrame <= 1 && _ghostHub >= 0 && _ghostHub < Wheels;
        var h = want ? _hubs[_ghostHub] : null;
        if (h == null || h.wheel == null || h.wheelMf == null || h.wheelMf.sharedMesh == null || (_ghostHub < _state.Count && _state[_ghostHub] != (int)WheelState.Off))
        {
            HideGhost();
            return;
        }

        if (_ghost == null)
        {
            _ghost = new GameObject("[WheelGhost]");
            _ghostMf = _ghost.AddComponent<MeshFilter>();
            var mr = _ghost.AddComponent<MeshRenderer>();
            _ghostMat = MakeGhostMaterial();
            mr.sharedMaterial = _ghostMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            try
            {
                _ghostOutline = _ghost.AddComponent<Outlinable>();
                _ghostOutline.AddAllChildRenderersToRenderingList();
                _ghostOutline.RenderStyle = RenderStyle.Single;
            }
            catch (System.Exception) { _ghostOutline = null; }
        }
        if (_ghostMf.sharedMesh != h.wheelMf.sharedMesh) _ghostMf.sharedMesh = h.wheelMf.sharedMesh;
        if (!_ghost.activeSelf) _ghost.SetActive(true);

        // 차 바퀴가 있던 바로 그 자리·방향·크기 — 여기에 끼워진다
        _ghost.transform.SetPositionAndRotation(h.wheel.position, h.wheel.rotation);
        _ghost.transform.localScale = h.wheel.lossyScale;

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 5f);
        Color baseCol = _ghostInRange ? new Color(0.25f, 1f, 0.45f) : new Color(0.45f, 0.8f, 1f);
        baseCol.a = (_ghostInRange ? 0.34f : 0.2f) + 0.1f * pulse;
        _ghostMat.color = baseCol;
        if (_ghostOutline != null)
        {
            var p = _ghostOutline.OutlineParameters;
            p.Color = _ghostInRange ? new Color(0.3f, 1f, 0.5f, 1f) : new Color(0.55f, 0.85f, 1f, 1f);
            p.Enabled = true;
        }
    }

    // 놓은 휠의 위치 — 스냅 애니메이션의 시작점(놓은 사람 화면에서만 알고, 다른 화면은 기본 거리에서 시작)
    private static int s_snapHub = -1;
    private static Vector3 s_snapFrom;
    private static float s_snapTime;

    public void NoteMountPose(int hub, Vector3 pos)
    {
        s_snapHub = hub; s_snapFrom = pos; s_snapTime = Time.time;
    }

    private void StopSnap(Hub h)
    {
        if (h.snap != null) { StopCoroutine(h.snap); h.snap = null; }
        if (h.snapObj != null) { Destroy(h.snapObj); h.snapObj = null; }
        if (h.nutRoot != null && !h.nutRoot.gameObject.activeSelf) h.nutRoot.gameObject.SetActive(true);
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.0f, c3 = c1 + 1f;
        float x = t - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }

    /// <summary>휠이 허브로 '착' 끼워진다: 바깥에서 날아와 살짝 눌렀다 자리 잡고, 그제서야 너트가 나타난다.</summary>
    private IEnumerator SnapRoutine(int w, Hub h)
    {
        Vector3 endPos = h.wheel.position;
        Quaternion endRot = h.wheel.rotation;
        Vector3 outW = h.wheel.TransformDirection(h.outLocal).normalized;

        Vector3 startPos = endPos + outW * 0.45f;
        if (s_snapHub == w && Time.time - s_snapTime < 4f && (s_snapFrom - endPos).magnitude < 1.4f) startPos = s_snapFrom;
        Vector3 perp = Vector3.Cross(outW, Vector3.up); if (perp.sqrMagnitude < 1e-4f) perp = Vector3.right;
        Quaternion startRot = Quaternion.AngleAxis(16f, perp.normalized) * Quaternion.AngleAxis(35f, outW) * endRot;

        var go = new GameObject("[WheelSnap]");
        go.layer = h.wheel.gameObject.layer;
        go.AddComponent<MeshFilter>().sharedMesh = h.wheelMf.sharedMesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = h.wheelMr != null ? h.wheelMr.sharedMaterials : new Material[0];
        go.transform.SetPositionAndRotation(startPos, startRot);
        go.transform.localScale = h.wheel.lossyScale;
        h.snapObj = go;

        float dur = Mathf.Max(0.1f, snapSeconds);
        for (float e = 0f; e < dur; e += Time.deltaTime)
        {
            if (h.wheel == null) yield break;
            float t = Mathf.Clamp01(e / dur);
            endPos = h.wheel.position; endRot = h.wheel.rotation;
            go.transform.position = Vector3.LerpUnclamped(startPos, endPos, EaseOutBack(t));
            go.transform.rotation = Quaternion.Slerp(startRot, endRot, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }

        Destroy(go);
        h.snapObj = null;
        SetWheelVisible(h, true);
        if (h.studs != null) h.studs.SetActive(false);
        if (h.nutRoot != null) h.nutRoot.gameObject.SetActive(true);
        h.snap = null;
        AudioSource.PlayClipAtPoint(EquipFx.Click, endPos, 1f);
        AudioSource.PlayClipAtPoint(EquipFx.Crack, endPos, 0.3f);
    }

    // ══════════════════════════════════════════
    //  서버
    // ══════════════════════════════════════════

    [ServerRpc(RequireOwnership = false)]
    private void NutProgressServerRpc(int idx, float p)
    {
        if (idx < 0 || idx >= _nut.Count) return;
        int w = idx / NutsPerWheel;
        if (_state[w] == (int)WheelState.Off) return;
        p = Mathf.Clamp01(p);
        if (p <= _nut[idx]) return;
        _nut[idx] = p;

        for (int n = 0; n < NutsPerWheel; n++) if (_nut[w * NutsPerWheel + n] < 1f) return;

        // 바퀴 하나의 너트 5개가 끝났다
        for (int n = 0; n < NutsPerWheel; n++) _nut[w * NutsPerWheel + n] = 0f;
        if (_state[w] == (int)WheelState.Mounted) { _state[w] = (int)WheelState.Tight; return; }
        _state[w] = (int)WheelState.Off;
        SpawnWheel(w);
    }

    [Server]
    private void SpawnWheel(int w)
    {
        if (wheelPrefab == null) return;
        if (!ServerHubPose(w, out var pos, out var rot)) return;
        var nob = Instantiate(wheelPrefab, pos, rot);
        if (gameObject.scene.IsValid() && nob.gameObject.scene != gameObject.scene)
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(nob.gameObject, gameObject.scene);
        ServerManager.Spawn(nob);
    }

    /// <summary>
    /// 서버에서 휠을 놓을 자리. 클라이언트와 같은 규칙(ResolveWheels)으로 바퀴를 찾고, 휠 프리팹의 차축을 그 바퀴의 바깥 방향에 맞춘다.
    /// (예전에는 SM_Wheel 하나를 이름으로만 골라 그 회전을 그대로 써서, 모델 변형에 따라 휠이 엉뚱한 쪽·방향으로 스폰될 수 있었다.)
    /// </summary>
    private bool ServerHubPose(int w, out Vector3 pos, out Quaternion rot)
    {
        pos = default; rot = Quaternion.identity;
        Vector3 c, o;
        var info = new WheelInfo[Wheels];
        if (ResolveWheels(info)) { c = info[w].centerWorld; o = info[w].outWorld; }
        else if (_hubs[w] != null && _hubs[w].wheel != null)
        {
            c = _hubs[w].wheel.TransformPoint(_hubs[w].centerLocal);
            o = _hubs[w].wheel.TransformDirection(_hubs[w].outLocal).normalized;
        }
        else return false;

        // 휠 프리팹의 차축(가장 얇은 축)과 메시 중심 — 루트 기준
        Vector3 pa = Vector3.right, pc = Vector3.zero;
        if (wheelPrefab != null)
        {
            var pmf = wheelPrefab.GetComponentInChildren<MeshFilter>(true);
            if (pmf != null && pmf.sharedMesh != null)
            {
                Vector3 axis = ThinAxis(pmf.sharedMesh, pmf.transform.lossyScale, out _);
                var root = wheelPrefab.transform;
                pa = root.InverseTransformDirection(pmf.transform.TransformDirection(axis)).normalized;
                pc = root.InverseTransformPoint(pmf.transform.TransformPoint(pmf.sharedMesh.bounds.center));
            }
        }
        rot = Quaternion.FromToRotation(pa, o);
        pos = c - rot * pc;
        return true;
    }

    /// <summary>휠을 놓은 클라이언트가 부른다. 빈 허브면 휠을 없애고 장착 상태로.</summary>
    [ServerRpc(RequireOwnership = false)]
    public void MountServerRpc(int w, NetworkObject wheelObj, NetworkConnection sender = null)
    {
        if (w < 0 || w >= Wheels || wheelObj == null || !wheelObj.IsSpawned) return;
        if (_state[w] != (int)WheelState.Off) return;
        var sg = wheelObj.GetComponent<SyncGrab>();
        if (sg != null && sg.IsGrabbed && wheelObj.Owner != sender) return;   // 남이 들고 있는 휠은 안 된다
        ServerManager.Despawn(wheelObj);
        for (int n = 0; n < NutsPerWheel; n++) _nut[w * NutsPerWheel + n] = 0f;
        _state[w] = (int)WheelState.Mounted;
    }

    // ══════════════════════════════════════════
    //  휠 장착 판정(클라이언트)
    // ══════════════════════════════════════════

    /// <summary>pos 근처의 빈 허브(바퀴 중심 기준). 없으면 -1.</summary>
    public int FreeHubNear(Vector3 pos, float maxDist)
    {
        if (!_found) return -1;
        int best = -1; float bd = maxDist * maxDist;
        for (int i = 0; i < Wheels; i++)
        {
            if (i >= _state.Count || _state[i] != (int)WheelState.Off) continue;
            var h = _hubs[i];
            if (h == null || h.wheel == null) continue;
            float d = (HubCenter(i) - pos).sqrMagnitude;
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    public bool AnyHubFree
    {
        get { for (int i = 0; i < _state.Count; i++) if (_state[i] == (int)WheelState.Off) return true; return false; }
    }
}
