using System.Collections.Generic;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using XRAirpotrSecurity;

// ============================================================
//  InsulationTape.cs
//
//  절연 테이프. 손에 들고 일반 공구(스패너·드라이버·토크렌치 = ManualTool)에 대고 누르고 있으면(VR 트리거 / PC F / 모바일 작업 버튼)
//  공구 손잡이를 테이프로 감아 절연 공구(1000V)처럼 쓸 수 있게 만든다.
//  · 겨누기: VR 은 테이프 롤을 공구에 15cm 안으로 가져가기(공구를 다른 손에 들고 있어도 된다), PC·모바일은 화면 가운데로 공구를 겨누기(3m 안)
//  · 감는 중 롤이 돌고 '치지직' 소리가 난다 → 감긴 진행도(0~1)만큼 테이프 띠가 공구에 차례로 생긴다. 다 감으면 ManualTool.insulated = true
//  · 동기화: ManualTool 은 건드리지 않고 별도 방송(TapeBroadcast)으로 공구의 NetworkObject 번호별 진행도를 서버가 기억해 모두에게 전한다
//    → 늦게 들어온 사람에게도 감긴 모습·절연 상태가 보인다. (롤의 '쓰는 중' 상태는 EquipmentBase 가 동기화 → 롤 회전·소리가 다른 사람에게도 보인다)
//  · 한 번 감은 테이프는 떼지 않는다(되돌리기 없음). 이미 절연 공구(Insulated 프리팹)는 감을 필요가 없다는 안내만 한다.
// ============================================================
public class InsulationTape : EquipmentBase
{
    protected override string MobileActionName => "테이프 감기";

    [Tooltip("돌아가는 테이프 롤(로컬 Y 축 둘레로 회전). 비우면 돌리지 않는다")] public Transform roll;
    [Tooltip("테이프가 나가는 곳(공구와의 거리 판정 기준). 비우면 이 물체 중심")] public Transform mouth;
    [Tooltip("한 번에 끝까지 감는 데 걸리는 시간(초)")] public float wrapSeconds = 3.5f;
    [Tooltip("VR 에서 롤을 공구에 이만큼(m) 안으로 가져가야 감긴다")] public float vrReach = 0.15f;
    [Tooltip("PC·모바일에서 겨눌 수 있는 최대 거리(m)")] public float aimReach = 3f;

    private AudioSource _loop;
    private ManualTool _target;
    private bool _hasTarget, _wrapping;
    private ManualTool _noticeTool;
    private float _hapticAt;
    private float _nextScan;
    private readonly List<ManualTool> _tools = new List<ManualTool>();

    protected override void Awake()
    {
        base.Awake();
        TapeSync.EnsureDriver();
    }

    private void Start()
    {
        // 풀리는 테이프 소리: 짧게 끊기는 소리를 빠르게 반복(Rattle)
        _loop = EquipFx.LoopSource(gameObject, EquipFx.Rattle, 8f);
    }

    /// <summary>겨눈 공구가 있을 때만 '쓰는 중'으로 보낸다(없는데 누르면 롤만 헛돌지 않게).</summary>
    protected override bool WantUsing(bool input) => input && _hasTarget;

    protected override void Update()
    {
        base.Update();
        bool held = IsLocalHeld;

        _wrapping = false;
        if (held)
        {
            _target = FindTarget();
            _hasTarget = _target != null && !IsFullyWrapped(_target);
            if (_target != null) NoticeTarget(_target);
            if (_target != null && LocalInput && !IsFullyWrapped(_target))
            {
                _wrapping = true;
                float before = TapeSync.Progress(_target);
                float after = TapeSync.LocalAdd(_target, Time.deltaTime / Mathf.Max(0.5f, wrapSeconds));
                if (IsVR && Time.unscaledTime >= _hapticAt) { _hapticAt = Time.unscaledTime + 0.12f; HapticAll(0.03f, 0.25f); }
                if (before < 1f && after >= 1f) OnWrapDone(_target);
            }
            else if (LocalInput && _target == null && Platform != PlatformType.VR)
                EquipFx.Notice("tape_notarget", "감을 공구를 겨누세요", "스패너·드라이버·토크렌치 쪽을 화면 가운데에 두고 F(작업 버튼)를 누르고 있으세요", EquipFx.Info, 2f, 4f);
        }
        else { _hasTarget = false; _target = null; }

        // 롤 회전·소리: 든 사람은 실제로 감는 중일 때, 다른 사람은 동기화된 Using 으로
        bool spin = held ? _wrapping : Using;
        if (roll != null && spin) roll.Rotate(0f, 540f * Time.deltaTime, 0f, Space.Self);
        EquipFx.Drive(_loop, spin ? 0.35f : 0f, 1.6f);
    }

    private static bool IsFullyWrapped(ManualTool t) => t == null || t.insulated || TapeSync.Progress(t) >= 1f;

    private void OnWrapDone(ManualTool t)
    {
        AudioSource.PlayClipAtPoint(EquipFx.Slip, t.transform.position, 0.6f);   // 테이프를 '쭉' 끊는 소리
        if (IsVR) HapticAll(0.1f, 0.7f);
        EquipFx.Notice("tape_done", "절연 처리 완료", "이 공구를 고전압 부품 작업에 쓸 수 있습니다. (실제 현장에서는 정격 1000V 절연 공구를 쓰는 것이 원칙이고, 테이프는 임시 보강입니다)", EquipFx.Info, 4f, 0f);
    }

    private void NoticeTarget(ManualTool t)
    {
        if (t == _noticeTool) return;
        _noticeTool = t;
        string nm = ToolName(t);
        if (t.insulated && TapeSync.Progress(t) < 1f)
            EquipFx.Notice("tape_already", $"{nm}: 이미 절연 공구입니다", "빨강/노랑 손잡이의 1000V 절연 공구는 테이프를 감을 필요가 없습니다", EquipFx.Info, 2.5f, 2f);
        else if (TapeSync.Progress(t) >= 1f)
            EquipFx.Notice("tape_already", $"{nm}: 이미 테이프로 감았습니다", "고전압 부품 작업에 쓸 수 있습니다", EquipFx.Info, 2f, 2f);
        else
            EquipFx.Notice("tape_aim", $"{nm}", Platform == PlatformType.VR ? "트리거를 누르고 있으면 테이프로 감깁니다" : "F(작업 버튼)를 누르고 있으면 테이프로 감깁니다", EquipFx.Info, 2f, 2f);
    }

    private static string ToolName(ManualTool t)
    {
        string n = t.name.Replace("(Clone)", "").Trim();
        return HoverNameFormatter.Format(n);
    }

    // ── 겨눈 공구 찾기 ──────────────────────────────────────────────────────────

    private ManualTool FindTarget()
    {
        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + 1f;   // 새로 Spawn 된 공구를 찾기 위해 1초마다 갱신
            _tools.Clear();
            _tools.AddRange(FindObjectsByType<ManualTool>(FindObjectsSortMode.None));
        }
        var m = mouth != null ? mouth : transform;
        bool vr = IsVR;
        Ray ray = default;
        if (!vr && Camera.main != null) ray = new Ray(Camera.main.transform.position, Camera.main.transform.forward);

        ManualTool best = null; float bestD = float.MaxValue;
        foreach (var t in _tools)
        {
            if (t == null || !t.gameObject.activeInHierarchy || t.transform.IsChildOf(transform)) continue;
            if (!TapeSync.TryBounds(t, out Bounds b)) continue;
            float d;
            if (vr)
            {
                d = Vector3.Distance(m.position, b.ClosestPoint(m.position));
                if (d > vrReach) continue;
            }
            else
            {
                if (Camera.main == null) continue;
                b.Expand(0.05f);   // 얇은 공구(2cm)도 화면 가운데로 맞추기 쉽게 여유를 둔다
                if (!b.IntersectRay(ray, out d) || d > aimReach) continue;
            }
            if (d < bestD) { bestD = d; best = t; }
        }
        return best;
    }
}

// ============================================================
//  TapeSync — 절연 테이프 감김 상태(공구 NetworkObject 번호 → 진행도 0~1)
// ============================================================
public struct TapeBroadcast : IBroadcast
{
    public int id;          // 공구의 NetworkObject.ObjectId. -1 이면 전체 상태 요청
    public float progress;  // 0~1
}

public static class TapeSync
{
    private static readonly Dictionary<int, float> s_state = new Dictionary<int, float>();       // 내가 아는 진행도(서버는 정본)
    private static readonly Dictionary<int, float> s_sentAt = new Dictionary<int, float>();
    private static readonly Dictionary<int, TapeWrapVisual> s_vis = new Dictionary<int, TapeWrapVisual>();
    private static bool s_reg, s_cliReg, s_asked;
    private static TapeSyncDriver s_driver;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_state.Clear(); s_sentAt.Clear(); s_vis.Clear();
        s_reg = s_cliReg = s_asked = false; s_driver = null;
    }

    public static void EnsureDriver()
    {
        if (s_driver != null) return;
        var go = new GameObject("[TapeSync]");
        go.hideFlags = HideFlags.HideInHierarchy;
        Object.DontDestroyOnLoad(go);
        s_driver = go.AddComponent<TapeSyncDriver>();
    }

    private static int IdOf(ManualTool t) => t != null && t.NetworkObject != null && t.IsSpawned ? t.ObjectId : -1;

    public static float Progress(ManualTool t)
    {
        int id = IdOf(t);
        return id >= 0 && s_state.TryGetValue(id, out var p) ? p : 0f;
    }

    /// <summary>공구 렌더러 전체를 감싸는 세계 경계(테이프 띠는 제외하고 처음 계산한 값을 쓰지 않아도 크게 달라지지 않는다).</summary>
    public static bool TryBounds(ManualTool t, out Bounds b)
    {
        b = default; bool any = false;
        foreach (var r in t.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return any;
    }

    /// <summary>들고 감는 사람 화면에서 진행도를 올린다(즉시 보이고, 0.15초마다 서버로). 올린 뒤의 값을 돌려준다.</summary>
    public static float LocalAdd(ManualTool t, float amount)
    {
        int id = IdOf(t);
        if (id < 0) return 0f;
        float p = Mathf.Clamp01((s_state.TryGetValue(id, out var cur) ? cur : 0f) + amount);
        s_state[id] = p;
        EnsureVisual(id, t, p);
        s_sentAt.TryGetValue(id, out var last);
        if (Time.unscaledTime >= last || p >= 1f)
        {
            s_sentAt[id] = Time.unscaledTime + 0.15f;
            var b = new TapeBroadcast { id = id, progress = p };
            if (InstanceFinder.IsServerStarted) ServerStore(b);
            else if (InstanceFinder.IsClientStarted) InstanceFinder.ClientManager.Broadcast(b);
        }
        return p;
    }

    private static void EnsureVisual(int id, ManualTool t, float p)
    {
        if (t == null) return;
        if (!s_vis.TryGetValue(id, out var v) || v == null)
        {
            v = t.GetComponent<TapeWrapVisual>();
            if (v == null) v = t.gameObject.AddComponent<TapeWrapVisual>();
            v.tool = t;
            s_vis[id] = v;
        }
        v.SetTarget(p);
    }

    // ── 방송 ────────────────────────────────────────────────────────────────────

    public static void Register()
    {
        var sm = InstanceFinder.ServerManager; var cm = InstanceFinder.ClientManager;
        if (!s_reg && sm != null) { sm.RegisterBroadcast<TapeBroadcast>(OnServer); s_reg = true; }
        if (!s_cliReg && cm != null) { cm.RegisterBroadcast<TapeBroadcast>(OnClient); s_cliReg = true; }
        if (!s_asked && cm != null && cm.Started && !InstanceFinder.IsServerStarted) { s_asked = true; cm.Broadcast(new TapeBroadcast { id = -1 }); }
    }

    private static Dictionary<int, NetworkObject> Spawned()
    {
        var nm = InstanceFinder.NetworkManager;
        if (nm == null) return null;
        if (nm.IsServerStarted) return nm.ServerManager.Objects.Spawned;
        if (nm.IsClientStarted) return nm.ClientManager.Objects.Spawned;
        return null;
    }

    private static void ServerStore(TapeBroadcast b)
    {
        float cur = s_state.TryGetValue(b.id, out var c) ? c : 0f;
        b.progress = Mathf.Max(cur, Mathf.Clamp01(b.progress));   // 되돌리기 없음
        s_state[b.id] = b.progress;
        ApplyLocal(b.id, b.progress);
        InstanceFinder.ServerManager.Broadcast(b);
    }

    private static void OnServer(NetworkConnection conn, TapeBroadcast b, Channel ch)
    {
        if (b.id < 0)
        {
            foreach (var kv in s_state) InstanceFinder.ServerManager.Broadcast(conn, new TapeBroadcast { id = kv.Key, progress = kv.Value });
            return;
        }
        // 실제로 있는 공구 번호만 받는다(엉뚱한 번호로 상태를 쌓지 못하게)
        var sp = Spawned();
        if (sp == null || !sp.TryGetValue(b.id, out var nob) || nob == null || nob.GetComponent<ManualTool>() == null) return;
        ServerStore(b);
    }

    private static void OnClient(TapeBroadcast b, Channel ch)
    {
        if (InstanceFinder.IsServerStarted) return;   // 호스트는 ServerStore 에서 이미 적용
        float cur = s_state.TryGetValue(b.id, out var c) ? c : 0f;
        s_state[b.id] = Mathf.Max(cur, Mathf.Clamp01(b.progress));
        ApplyLocal(b.id, s_state[b.id]);
    }

    /// <summary>이 번호의 공구가 이미 있으면 바로 입힌다(없으면 드라이버가 나중에 다시 시도).</summary>
    private static void ApplyLocal(int id, float p)
    {
        var sp = Spawned();
        if (sp == null || !sp.TryGetValue(id, out var nob) || nob == null) return;
        var t = nob.GetComponent<ManualTool>();
        if (t != null) EnsureVisual(id, t, p);
    }

    /// <summary>드라이버가 0.5초마다 호출 — 아직 공구를 못 찾은 항목을 입히고, 서버는 사라진 공구의 기록을 지운다.</summary>
    public static void Tick()
    {
        Register();
        if (s_state.Count == 0) return;
        var sp = Spawned();
        if (sp == null) return;
        List<int> gone = null;
        foreach (var kv in s_state)
        {
            if (s_vis.TryGetValue(kv.Key, out var v) && v != null) continue;
            if (sp.TryGetValue(kv.Key, out var nob) && nob != null)
            {
                var t = nob.GetComponent<ManualTool>();
                if (t != null) EnsureVisual(kv.Key, t, kv.Value);
            }
            else if (InstanceFinder.IsServerStarted) (gone ??= new List<int>()).Add(kv.Key);
        }
        if (gone != null) foreach (var id in gone) { s_state.Remove(id); s_vis.Remove(id); s_sentAt.Remove(id); }
    }
}

public class TapeSyncDriver : MonoBehaviour
{
    private float _next;
    private void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.5f;
        TapeSync.Tick();
    }
}

// ============================================================
//  TapeWrapVisual — 공구에 붙는 테이프 띠(진행도만큼 한쪽 끝부터 차례로 생긴다)
// ============================================================
public class TapeWrapVisual : MonoBehaviour
{
    [System.NonSerialized] public ManualTool tool;

    private const int Bands = 8;
    private float _target, _shown = -1f;
    private Transform[] _bands;
    private Vector3[] _fullScale;
    private bool _built;
    private static Material s_mat;
    private static Mesh s_cyl;

    public void SetTarget(float p)
    {
        _target = Mathf.Max(_target, p);
        if (tool == null) tool = GetComponent<ManualTool>();
        if (_target >= 1f && tool != null) tool.insulated = true;   // 기능은 즉시 — 띠 연출은 따라온다
        if (_shown < 0f && _target > 0f) _shown = _target;   // 처음 입히는 값으로 바로 맞춘다(늦게 들어온 사람은 완성된 모습이 보인다)
    }

    private void Update() => Step(Time.deltaTime);

    public void Step(float dt)
    {
        if (_shown < 0f) return;
        if (!_built) Build();
        if (_bands == null) return;
        _shown = Mathf.MoveTowards(_shown, _target, dt * 1.2f);   // 갑자기 늘어도 부드럽게 따라간다
        for (int i = 0; i < Bands; i++)
        {
            float f = Mathf.Clamp01(_shown * Bands - i);   // 이 띠가 얼마나 감겼나
            var b = _bands[i];
            if (f <= 0.001f) { if (b.gameObject.activeSelf) b.gameObject.SetActive(false); continue; }
            if (!b.gameObject.activeSelf) b.gameObject.SetActive(true);
            b.localScale = new Vector3(_fullScale[i].x, _fullScale[i].y * f, _fullScale[i].z);
        }
    }

    /// <summary>공구의 가장 긴 축 가운데 구간에 띠를 만든다(ManualTool 과 같은 방식으로 긴 축을 고른다).</summary>
    private void Build()
    {
        _built = true;
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;   // 서버(렌더 없음)는 만들지 않는다
        if (!MeshLocalBounds(transform, out var lb)) return;
        Material mat = GetMaterial();
        Mesh mesh = GetCylinder();
        if (mat == null || mesh == null) return;

        Vector3 e = lb.extents;
        Vector3 axis = e.x >= e.y && e.x >= e.z ? Vector3.right : (e.y >= e.z ? Vector3.up : Vector3.forward);
        float half = Vector3.Dot(e, axis);
        // 축에 수직인 두 방향 중 얇은 쪽을 손잡이 굵기로 본다
        Vector3 pe = e - axis * half;
        float thin = Mathf.Min(pe.x > 0f ? pe.x : float.MaxValue, pe.y > 0f ? pe.y : float.MaxValue, pe.z > 0f ? pe.z : float.MaxValue);
        if (thin == float.MaxValue) thin = 0.01f;
        float radius = thin * 1.2f + 0.002f;
        float span = half * 2f * 0.5f;                  // 가운데 절반
        float step = span / Bands;
        Vector3 start = lb.center - axis * (span * 0.5f);

        _bands = new Transform[Bands];
        _fullScale = new Vector3[Bands];
        var rot = Quaternion.FromToRotation(Vector3.up, axis);
        for (int i = 0; i < Bands; i++)
        {
            var go = new GameObject("[TapeBand]");
            go.layer = gameObject.layer;
            var t = go.transform;
            t.SetParent(transform, false);
            t.localPosition = start + axis * (step * (i + 0.5f));
            t.localRotation = rot;
            // 기본 원기둥은 지름 1·높이 2 → 지름 2r, 높이 step*1.15(살짝 겹치게)
            var sc = new Vector3(radius * 2f, step * 1.15f * 0.5f, radius * 2f);
            _fullScale[i] = sc;
            t.localScale = sc;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            _bands[i] = t;
        }
    }

    /// <summary>메시 경계(MeshFilter)의 모서리를 루트 로컬로 옮긴 경계. 렌더러의 세계 AABB 를 되돌리는 방식은 공구가 기울어 있으면 부풀어서, 자세와 상관없이 같은 값이 나오게 한다.</summary>
    private static bool MeshLocalBounds(Transform root, out Bounds b)
    {
        b = default; bool any = false;
        var inv = root.worldToLocalMatrix;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null || mf.name == "[TapeBand]") continue;
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr == null || !mr.enabled) continue;
            var mb = mf.sharedMesh.bounds;
            var m = inv * mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = mb.center + new Vector3((i & 1) == 0 ? -mb.extents.x : mb.extents.x, (i & 2) == 0 ? -mb.extents.y : mb.extents.y, (i & 4) == 0 ? -mb.extents.z : mb.extents.z);
                var p = m.MultiplyPoint3x4(c);
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
            }
        }
        return any;
    }

    private Material GetMaterial()
    {
        if (s_mat != null) return s_mat;
        // 공구 자신의 재질(URP Lit)을 복제해 검은 비닐 테이프 색만 바꾼다 → Shader.Find 없이 빌드에서도 안전
        Material src = null;
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is LineRenderer) continue;
            if (r.sharedMaterial != null) { src = r.sharedMaterial; break; }
        }
        if (src == null) return null;
        s_mat = new Material(src) { name = "TapeBlack(runtime)" };
        var black = new Color(0.04f, 0.04f, 0.045f, 1f);
        if (s_mat.HasProperty("_BaseColor")) s_mat.SetColor("_BaseColor", black);
        if (s_mat.HasProperty("_Color")) s_mat.SetColor("_Color", black);
        if (s_mat.HasProperty("_BaseMap")) s_mat.SetTexture("_BaseMap", null);
        if (s_mat.HasProperty("_MainTex")) s_mat.SetTexture("_MainTex", null);
        if (s_mat.HasProperty("_BumpMap")) s_mat.SetTexture("_BumpMap", null);
        if (s_mat.HasProperty("_Metallic")) s_mat.SetFloat("_Metallic", 0f);
        if (s_mat.HasProperty("_Smoothness")) s_mat.SetFloat("_Smoothness", 0.45f);
        s_mat.DisableKeyword("_EMISSION");
        return s_mat;
    }

    private static Mesh GetCylinder()
    {
        if (s_cyl != null) return s_cyl;
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);   // 빌드에도 들어 있는 기본 원기둥 메시를 빌려 온다
        s_cyl = tmp.GetComponent<MeshFilter>().sharedMesh;
        if (Application.isPlaying) Object.Destroy(tmp); else Object.DestroyImmediate(tmp);
        return s_cyl;
    }
}
