using System.Collections.Generic;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;
using XRAirpotrSecurity;

// ============================================================
//  PaintGun.cs
//
//  도색 스프레이건. 누르고 있는 동안(VR 트리거 / PC F / 모바일 작업 버튼) 페인트 안개를 뿜고,
//  '분사하는 위치'의 차체 패널(문·후드·범퍼·차체 등)만 뿌린 만큼 서서히 그 색으로 덮인다(패널 전체가 아님).
//  도색 결과는 모두에게 같게 보이고 늦게 들어온 사람에게도 보인다(PaintSync).
//  · 방식: 패널마다 작은 3D 마스크(렌더러 로컬 좌표, 약 5cm 칸)에 분사 지점을 누적 → 전용 셰이더(Dodrill/PaintLit, URP Lit 복사본)가 마스크만큼 색을 입힌다
//  · 색 고르기: 도색 색상판(PaintSwatch)을 클릭/터치하거나 VR 에서 건 노즐을 색 칸에 대기. PC 는 들고 마우스 휠로 다음 색(Q 는 높이 올리기)
//  · 너무 가까이(15cm 안) 뿌리면 흘러내림 경고, 너무 멀면(1.2m 밖) 거의 안 묻는다 — 적정 거리 15~30cm
// ============================================================
public class PaintGun : EquipmentBase
{
    protected override string MobileActionName => "도색";

    public static readonly Color[] Palette =
    {
        new Color(0.85f, 0.08f, 0.08f), new Color(0.05f, 0.25f, 0.75f), new Color(0.95f, 0.95f, 0.95f),
        new Color(0.08f, 0.08f, 0.09f), new Color(0.98f, 0.75f, 0.05f), new Color(0.1f, 0.55f, 0.25f),
        new Color(0.6f, 0.62f, 0.66f),
    };
    public static readonly string[] PaletteNames = { "레드", "블루", "화이트", "블랙", "옐로", "그린", "실버" };

    /// <summary>이 화면에서 마지막으로 고른 색(색상판 클릭) — 건을 들면 이 색으로 맞춘다.</summary>
    public static int LocalPick = 0;

    [Tooltip("노즐(앞쪽 = 로컬 +Z)")] public Transform nozzle;
    [Tooltip("컵 안 페인트 색을 보여 줄 렌더러")] public Renderer cupPaint;

    private readonly SyncVar<int> _color = new SyncVar<int>();
    private ParticleSystem _mist;
    private AudioSource _hiss;
    private float _sendAt;
    private float _dabAcc;   // 마지막 점 찍기 이후 실제로 패널에 뿌린 시간
    private bool _wasHeld;
    private MaterialPropertyBlock _mpb;

    public Color CurrentColor => Palette[Mathf.Clamp(_color.Value, 0, Palette.Length - 1)];

    private void Start()
    {
        var lib = EquipFx.Lib;
        if (lib != null && lib.mist != null)
        {
            _mist = EquipFx.MakeParticles(transform, "[PaintMist]", new EquipFx.PsSettings
            {
                // 분사가 잘 안 보이던 문제: 입자를 키우고(0.03→0.055) 더 오래·더 많이 뿜는다
                mat = lib.mist, color = CurrentColor, lifetime = 0.7f, speed = 3.4f, size = 0.055f, sizeEnd = 6f,
                gravity = 0.02f, rate = 0f, coneAngle = 12f, radius = 0.004f, max = 700, drag = 2.0f
            });
            _mist.Play();
        }
        _hiss = EquipFx.LoopSource(gameObject, EquipFx.Hiss, 8f);
        PaintSync.EnsureDriver();   // 서버·늦게 들어온 클라이언트도 방송을 받도록
    }

    protected override void Update()
    {
        base.Update();
        bool held = IsLocalHeld;
        if (held && !_wasHeld && _color.Value != LocalPick) SetColorServerRpc(LocalPick);
        _wasHeld = held;

        if (held)
        {
            if (Platform == PlatformType.PC && EquipFx.PcWheelStep())
                Pick((_color.Value + (Input.mouseScrollDelta.y > 0f ? 1 : Palette.Length - 1)) % Palette.Length);
            if (IsVR) foreach (var sw in PaintSwatch.All) if (sw != null && Nozzle != null && (sw.transform.position - Nozzle.position).sqrMagnitude < 0.12f * 0.12f && sw.index != _color.Value) Pick(sw.index);
        }

        var n = Nozzle;
        Color c = CurrentColor;
        if (cupPaint != null)
        {
            _mpb ??= new MaterialPropertyBlock();
            _mpb.SetColor("_BaseColor", c); _mpb.SetColor("_Color", c);
            cupPaint.SetPropertyBlock(_mpb);
        }
        if (_mist != null)
        {
            var main = _mist.main; main.startColor = new Color(c.r, c.g, c.b, 0.8f);
            _mist.transform.SetPositionAndRotation(n.position, Quaternion.LookRotation(SprayDir));
            var em = _mist.emission; em.rateOverTime = Using ? 280f : 0f;
        }
        EquipFx.Drive(_hiss, Using ? 0.4f : 0f, 1.3f);

        if (Using && held) Paint(n.position, SprayDir);
    }

    private Transform Nozzle => nozzle != null ? nozzle : transform;

    private Vector3 SprayDir => IsLocalHeld && !IsVR && Camera.main != null ? Camera.main.transform.forward : Nozzle.forward;

    public void Pick(int i)
    {
        LocalPick = i;
        SetColorServerRpc(i);
        AudioSource.PlayClipAtPoint(EquipFx.Click, transform.position, 0.6f);
        EquipFx.Notice("paint_color", $"도색 색상: {PaletteNames[i]}", Platform == PlatformType.PC ? "마우스 휠로 다른 색" : "색상판에서 다른 색을 고를 수 있습니다", EquipFx.Info, 1.5f, 0f);
    }

    [FishNet.Object.ServerRpc(RequireOwnership = false)]
    private void SetColorServerRpc(int i, NetworkConnection sender = null)
    {
        if (Sync == null || !Sync.IsGrabbed || Owner != sender) return;
        _color.Value = Mathf.Clamp(i, 0, Palette.Length - 1);
    }


    // ── 칠하기(들고 있는 사람만 계산 → PaintSync 로 공유) ─────────────────────────────

    private void Paint(Vector3 origin, Vector3 dir)
    {
        // PC·모바일은 카메라 기준으로 겨누되, 거리는 건에서 잰다
        Vector3 from = !IsVR && Camera.main != null ? Camera.main.transform.position : origin;
        PaintSync.Target best = null; float bestD = float.MaxValue; Vector3 bestPoint = default;
        // 1) 실제 차체 면: 물리 레이가 맞은 콜라이더에 속한 패널(맞은 점이 그 패널 경계 안)
        bool surface = PaintSync.TryPhysicsHit(from, dir, 4f, out best, out bestPoint, out bestD);
        // 2) 콜라이더가 없는 패널: 경계 상자로 찾고, 면 위치를 모르므로 들어간 점부터 안쪽으로 25cm 까지 칠한다
        //    (패널 메시로 정확한 면을 찾을 수 있으면(ExactSurfaces) 이 어림 방식은 쓰지 않는다 — 허공·안쪽이 칠해진다)
        if (!surface && !PaintSync.ExactSurfaces)
        {
            foreach (var t in PaintSync.Targets())
            {
                if (t.renderer == null || !t.renderer.enabled || !t.renderer.gameObject.activeInHierarchy) continue;
                if (!t.TryRay(from, dir, out float d, out Vector3 hp)) continue;
                if (d < 0.02f) continue;   // 경계 상자 안에 서 있음 — 그 자리를 칠하던 문제(차 옆에 서면 엉뚱한 곳이 칠해짐)
                if (d < bestD) { bestD = d; best = t; bestPoint = hp; }
            }
        }
        if (best == null) return;
        float dist = Vector3.Distance(origin, bestPoint);
        if (!IsVR) { if (bestD > 3f) return; dist = 0.25f; }   // PC·모바일: 화면 가운데 3m 안이면 적정 거리로 본다
        if (dist > 1.2f) return;
        if (dist < 0.12f) EquipFx.Notice("paint_close", "너무 가깝습니다", "15~30cm 거리에서 뿌려야 흘러내리지 않습니다", EquipFx.Warn, 2f, 6f);

        // 0.1초마다 '분사 지점'을 한 번 찍는다 — 한 곳에 1.7초쯤 뿌리면 그 자리가 다 덮인다(예전 2.5초는 느리다는 의견). 멀수록 덜 묻는다.
        _dabAcc += Time.deltaTime;
        if (_dabAcc >= 0.1f)
        {
            float k = Mathf.Lerp(1f, 0.25f, Mathf.InverseLerp(0.3f, 1.2f, dist));
            int amt = Mathf.Clamp(Mathf.RoundToInt(_dabAcc * k * 600f), 1, 255);
            _dabAcc = 0f;
            // 같은 렌더러의 칸(재질 번호)마다 찍는다 — 어느 칸에 속한 면인지는 셰이더가 마스크로 가른다
            foreach (var t in PaintSync.Targets())
            {
                if (t.renderer != best.renderer) continue;
                if (surface) PaintSync.LocalDab(t, bestPoint, _color.Value, amt);
                else for (float s = 0f; s <= 0.25f; s += 0.04f) PaintSync.LocalDab(t, bestPoint + dir.normalized * s, _color.Value, amt);
            }
        }
        if (IsVR && Time.frameCount % 5 == 0) HapticAll(0.02f, 0.15f);
    }
}

/// <summary>
/// 도색 분사 지점 묶음. 패널(렌더러+재질 칸)마다 3D 마스크 칸(nx·ny·nz) 안의 번호(cells)에 색(colors)과 양(amts)을 더한다.
/// 모든 기기가 같은 순서·같은 정수 계산으로 적용하므로 결과가 같다.
/// </summary>
public struct PaintBroadcast : IBroadcast
{
    public string key;     // 비면: 전체 상태 요청
    public int dims;       // 마스크 칸 수: nx | (ny << 8) | (nz << 16)
    public int from;       // 보낸 클라이언트(서버가 채움). 자기가 보낸 것은 다시 적용하지 않는다. 늦게 들어온 사람에게 되돌려 줄 땐 -1
    public int[] cells;
    public byte[] colors;
    public byte[] amts;    // 1~255 (1/1023 단위)
}

/// <summary>
/// 차체 패널 도색 상태.
/// · 패널 = 렌더러 이름#재질번호(Target). 렌더러 로컬 좌표를 약 5cm 칸으로 나눈 3D 마스크(최대 64³, 14만 칸 이하)를 갖고,
///   분사 지점(렌더러 로컬 경계와 시선이 만나는 점)을 반경 2칸 부드럽게 누적한다. 같은 색은 쌓이고, 다른 색은 먼저 지워진 뒤 바뀐다.
/// · 마스크는 RGBA32 3D 텍스처(색×덮임, 덮임)로 올려 전용 셰이더(Dodrill/PaintLit)가 읽는다 → 그 자리만 칠해지고, 문이 열려도 따라간다.
/// · 서버는 분사 지점 기록(로그)만 갖고(마스크를 만들지 않음) 모두에게 전한다. 늦게 들어온 사람은 전체 기록을 받아 순서대로 다시 적용한다.
/// · 집중·엑스레이 모드(FocusMode)가 반투명으로 바꾼 동안(IsFaded)은 입히지 않고, 복원할 때 Reshow 가 다시 입힌다.
/// </summary>
public static class PaintSync
{
    public class Target
    {
        public Renderer renderer;
        public int mat;
        public string key;
        public MeshFilter mf;

        // 3D 마스크
        public int dims, nx, ny, nz;
        public Vector3 gmin, vs;          // 마스크 최솟점·칸 크기(렌더러 로컬)
        public ushort[] cov;              // 덮임 0~1023
        public byte[] pal;                // 팔레트 번호
        public Color32[] pix;             // 올릴 픽셀(미리 곱한 색, 알파=덮임)
        public Texture3D tex;
        public bool dirty;
        public float uploadAt;

        public bool HasMask => tex != null;

        /// <summary>시선(from, dir)이 이 패널 렌더러의 로컬 경계 상자와 처음 만나는 점. 메시를 읽지 못해도 되도록 경계 상자만 쓴다.</summary>
        public bool TryRay(Vector3 from, Vector3 dir, out float dist, out Vector3 hitWorld)
        {
            dist = 0f; hitWorld = default;
            if (mf == null || mf.sharedMesh == null) return false;
            if (!renderer.bounds.IntersectRay(new Ray(from, dir))) return false;   // 세계 경계로 먼저 걸러 낸다
            var tr = renderer.transform;
            Bounds lb = mf.sharedMesh.bounds;
            Vector3 o = tr.InverseTransformPoint(from);
            Vector3 dl = tr.InverseTransformVector(dir);
            if (dl.sqrMagnitude < 1e-12f) return false;
            Vector3 hitLocal;
            if (lb.Contains(o)) hitLocal = o;
            else
            {
                if (!lb.IntersectRay(new Ray(o, dl.normalized), out float dd)) return false;
                hitLocal = o + dl.normalized * dd;
            }
            hitWorld = tr.TransformPoint(hitLocal);
            dist = Vector3.Distance(from, hitWorld);
            return true;
        }
    }

    /// <summary>물리 레이로 실제 면을 찾는다 — 맞은 콜라이더와 같은(또는 부모·자식) 오브젝트의 패널 중 맞은 점을 경계에 품은 것.</summary>
    public static bool TryPhysicsHit(Vector3 from, Vector3 dir, float maxDist, out Target target, out Vector3 point, out float dist)
    {
        target = null; point = default; dist = float.MaxValue;
        Scan();
        if (TryProxyHit(from, dir, maxDist, out target, out point, out dist)) return true;
        if (ExactSurfaces) return false;
        var hits = Physics.RaycastAll(from, dir, maxDist, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            if (h.collider == null) continue;
            var ct = h.collider.transform;
            if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic) continue;   // 손에 든 물건 등
            Target found = null;
            foreach (var t in s_targets.Values)
            {
                if (t.renderer == null || !t.renderer.enabled || !t.renderer.gameObject.activeInHierarchy) continue;
                var rt = t.renderer.transform;
                // 콜라이더가 패널 자신이거나 패널 아래에 있을 때만. 차 전체를 감싼 루트 상자(Ioniq5N)에 맞은 점은
                // 실제 면이 아니라 허공이라(후드 위 지붕 높이 등) 그 자리에 칠하면 아무 데도 묻지 않았다
                if (rt != ct && !ct.IsChildOf(rt)) continue;
                var b = t.renderer.bounds; b.Expand(0.1f);
                if (!b.Contains(h.point)) continue;
                found = t; break;
            }
            if (found == null)
            {
                // 차체 콜라이더가 아니라 다른 물건(공구·사람)에 먼저 막혔으면 칠하지 않는다
                var sg = ct.GetComponentInParent<SyncGrab>();
                if (sg != null && !sg.IsGrabbed) return false;   // 들고 있는 도구(분무기 자신 등)는 건너뛴다
                continue;
            }
            target = found; point = h.point; dist = h.distance;
            return true;
        }
        return false;
    }

    // ── 정확한 면 찾기 ─────────────────────────────────────────────────────────
    // 차에는 패널별 콜라이더가 거의 없고 차 전체를 감싼 상자 하나뿐이라, 레이로는 실제 면을 알 수 없었다.
    // 칠할 패널과 같은 차의 다른 부품(유리·볼트 등, 가림용) 메시로 MeshCollider 복제본을 만들어
    // 게임과 분리된 물리 공간에 두고, 분사할 때만 원본 자세로 맞춘 뒤 레이를 쏜다(게임 물리·충돌에는 영향 없음).
    // 메시 Read/Write 가 꺼져 있으면 복제본을 만들 수 없으므로 예전 방식(콜라이더 + 경계 상자)으로 돌아간다.
    private class Proxy { public Transform src; public Renderer renderer; public Transform tr; public Collider col; public Target target; }
    private static UnityEngine.SceneManagement.Scene s_proxyScene;
    private static PhysicsScene s_proxyPhys;
    private static readonly Dictionary<Renderer, Proxy> s_proxies = new Dictionary<Renderer, Proxy>();
    private static readonly Dictionary<Collider, Proxy> s_proxyByCol = new Dictionary<Collider, Proxy>();
    private static float s_nextProxyScan;

    /// <summary>칠할 패널 전부를 메시로 정확히 판정할 수 있는가 — 그렇다면 경계 상자 어림 칠하기를 하지 않는다.</summary>
    public static bool ExactSurfaces { get; private set; }

    private static bool EnsureProxyScene()
    {
        if (s_proxyScene.IsValid() && s_proxyScene.isLoaded) return true;
        s_proxies.Clear(); s_proxyByCol.Clear();
        try
        {
            s_proxyScene = UnityEngine.SceneManagement.SceneManager.CreateScene("[PaintProxies]",
                new UnityEngine.SceneManagement.CreateSceneParameters(UnityEngine.SceneManagement.LocalPhysicsMode.Physics3D));
            s_proxyPhys = s_proxyScene.GetPhysicsScene();
            return true;
        }
        catch (System.Exception e) { Debug.LogWarning("[PaintSync] 판정용 물리 공간 생성 실패: " + e.Message); return false; }
    }

    private static void UpdateProxies()
    {
        if (Headless || Time.unscaledTime < s_nextProxyScan) return;
        s_nextProxyScan = Time.unscaledTime + 2f;
        if (!EnsureProxyScene()) { ExactSurfaces = false; return; }

        // 사라진 원본 정리
        List<Renderer> dead = null;
        foreach (var kv in s_proxies)
            if (kv.Key == null || kv.Value.src == null) (dead ??= new List<Renderer>()).Add(kv.Key);
        if (dead != null)
            foreach (var r in dead)
            {
                var px = s_proxies[r];
                if (px.col != null) s_proxyByCol.Remove(px.col);
                if (px.tr != null) Object.Destroy(px.tr.gameObject);
                s_proxies.Remove(r);
            }

        bool all = true, any = false;
        var roots = new HashSet<Transform>();
        foreach (var t in s_targets.Values)
        {
            if (t.renderer == null || !t.renderer.gameObject.activeInHierarchy) continue;
            roots.Add(t.renderer.transform.root);
            var px = MakeProxy(t.renderer, t);
            if (px == null) all = false; else { any = true; if (px.target == null) px.target = t; }
        }
        // 가림용: 같은 차의 다른 부품(유리·실내·볼트 등)
        foreach (var root in roots)
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false))
                if (!s_proxies.ContainsKey(r)) MakeProxy(r, null);
        ExactSurfaces = any && all;
    }

    private static Proxy MakeProxy(Renderer r, Target t)
    {
        if (s_proxies.TryGetValue(r, out var have)) return have;
        var mf = r.GetComponent<MeshFilter>();
        var mesh = mf != null ? mf.sharedMesh : null;
        if (mesh == null || !mesh.isReadable) return null;
        var go = new GameObject("Proxy " + r.name);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, s_proxyScene);
        var mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        var px = new Proxy { src = r.transform, renderer = r, tr = go.transform, col = mc, target = t };
        s_proxies[r] = px;
        s_proxyByCol[mc] = px;
        return px;
    }

    private static bool TryProxyHit(Vector3 from, Vector3 dir, float maxDist, out Target target, out Vector3 point, out float dist)
    {
        target = null; point = default; dist = float.MaxValue;
        UpdateProxies();
        if (s_proxies.Count == 0 || !s_proxyScene.IsValid()) return false;
        foreach (var px in s_proxies.Values)
        {
            if (px.src == null || px.tr == null) continue;
            bool on = px.renderer != null && px.renderer.enabled && px.src.gameObject.activeInHierarchy;
            if (px.col.enabled != on) px.col.enabled = on;
            if (!on) continue;
            px.tr.SetPositionAndRotation(px.src.position, px.src.rotation);
            px.tr.localScale = px.src.lossyScale;
        }
        Physics.SyncTransforms();
        if (!s_proxyPhys.Raycast(from, dir, out var hit, maxDist, ~0, QueryTriggerInteraction.Ignore)) return false;
        if (!s_proxyByCol.TryGetValue(hit.collider, out var p) || p.target == null) return false;   // 가림용 부품에 먼저 맞음
        if (p.target.renderer == null || !p.target.renderer.enabled) return false;
        target = p.target; point = hit.point; dist = hit.distance;
        return true;
    }

    // 칠할 수 있는 재질(차체 외판)
    private static readonly string[] PaintableMats = { "M_Door", "M_Electric", "M_Back", "M_Bumper", "M_Hood", "M_Front", "M_Frunk" };

    private const float VoxelWorld = 0.05f;     // 칸 한 변(m) 목표
    private const int MaxEdge = 64, MaxVoxels = 140000;
    private const int DabRadius = 2;            // 칸 단위 반경
    private static readonly int[] Falloff = { 256, 205, 150, 100, 60, 28, 10 };   // 거리² 0~6 → 가중(256=100%)
    private const int MaxBatch = 120;           // 한 번에 보내는 분사 지점 수
    private const int MaxLogPerKey = 40000;

    private static readonly Dictionary<string, Target> s_targets = new Dictionary<string, Target>();
    private static readonly Dictionary<string, List<PaintBroadcast>> s_pending = new Dictionary<string, List<PaintBroadcast>>();   // 차가 아직 없을 때 온 것
    private static readonly HashSet<Target> s_dirty = new HashSet<Target>();
    private static readonly Dictionary<Material, Material> s_clone = new Dictionary<Material, Material>();
    private static float s_nextScan;
    private static bool s_reg, s_cliReg, s_asked, s_shaderTried;
    private static MaterialPropertyBlock s_mpb;
    private static Shader s_shader;
    private static Color32[] s_pal32;
    private static PaintSyncDriver s_driver;

    private static readonly int IdMask = Shader.PropertyToID("_PaintMask");
    private static readonly int IdMin = Shader.PropertyToID("_PaintBMin");
    private static readonly int IdInv = Shader.PropertyToID("_PaintBInvSize");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_targets.Clear(); s_pending.Clear(); s_dirty.Clear(); s_clone.Clear(); s_log.Clear(); s_out.Clear();
        s_reg = s_cliReg = s_asked = s_shaderTried = false; s_driver = null; s_shader = null; s_nextScan = 0f;
        s_proxies.Clear(); s_proxyByCol.Clear(); s_proxyScene = default; s_nextProxyScan = 0f; ExactSurfaces = false;
    }

    public static void EnsureDriver()
    {
        if (s_driver != null) return;
        var go = new GameObject("[PaintSync]");
        go.hideFlags = HideFlags.HideInHierarchy;
        Object.DontDestroyOnLoad(go);
        s_driver = go.AddComponent<PaintSyncDriver>();
    }

    private static bool Headless => SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

    public static IEnumerable<Target> Targets()
    {
        Scan();
        return s_targets.Values;
    }

    // ── 패널 찾기 ──────────────────────────────────────────────────────────────

    private static void Scan()
    {
        Register();
        if (Time.unscaledTime < s_nextScan) return;
        s_nextScan = Time.unscaledTime + 2f;
        if (Headless) return;   // 렌더링 없는 서버는 마스크를 만들지 않는다(기록만)
        // 차가 다시 생겼을 수 있으니 렌더러를 새로 찾는다
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (r == null || !r.gameObject.activeInHierarchy) continue;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                bool ok = false;
                foreach (var p in PaintableMats) if (m.name.StartsWith(p)) { ok = true; break; }
                if (!ok) continue;
                string key = r.name + "#" + i;
                if (s_targets.TryGetValue(key, out var old))
                {
                    if (old.renderer == r) continue;
                    if (old.renderer != null) continue;   // 이름이 같은 다른 렌더러 — 먼저 잡은 쪽을 쓴다(마스크가 섞이지 않게)
                }
                var t = new Target { renderer = r, mat = i, key = key, mf = r.GetComponent<MeshFilter>() };
                s_targets[key] = t;
                if (s_pending.TryGetValue(key, out var list))
                {
                    s_pending.Remove(key);
                    foreach (var b in list) ApplyBatch(t, b);
                }
            }
        }
    }

    // ── 마스크 ────────────────────────────────────────────────────────────────

    private static int PackDims(int x, int y, int z) => x | (y << 8) | (z << 16);

    /// <summary>이 패널에 맞는 마스크 칸 수(경계 상자 ÷ 5cm + 가장자리 여유 2칸).</summary>
    private static int DimsFor(Target t)
    {
        Vector3 s = t.mf.sharedMesh.bounds.size;
        Vector3 ls = t.renderer.transform.lossyScale;
        float wx = Mathf.Max(0.01f, s.x * Mathf.Abs(ls.x)), wy = Mathf.Max(0.01f, s.y * Mathf.Abs(ls.y)), wz = Mathf.Max(0.01f, s.z * Mathf.Abs(ls.z));
        int nx = Mathf.Clamp(Mathf.CeilToInt(wx / VoxelWorld) + 2, 4, MaxEdge);
        int ny = Mathf.Clamp(Mathf.CeilToInt(wy / VoxelWorld) + 2, 4, MaxEdge);
        int nz = Mathf.Clamp(Mathf.CeilToInt(wz / VoxelWorld) + 2, 4, MaxEdge);
        while (nx * ny * nz > MaxVoxels)
        {
            if (nx >= ny && nx >= nz) nx = Mathf.Max(4, nx - 4);
            else if (ny >= nz) ny = Mathf.Max(4, ny - 4);
            else nz = Mathf.Max(4, nz - 4);
        }
        return PackDims(nx, ny, nz);
    }

    private static bool DimsValid(int dims, out int nx, out int ny, out int nz)
    {
        nx = dims & 255; ny = (dims >> 8) & 255; nz = (dims >> 16) & 255;
        return nx >= 4 && ny >= 4 && nz >= 4 && nx <= MaxEdge && ny <= MaxEdge && nz <= MaxEdge && nx * ny * nz <= MaxVoxels;
    }

    private static bool Build(Target t, int dims)
    {
        if (!DimsValid(dims, out int nx, out int ny, out int nz) || t.mf == null || t.mf.sharedMesh == null) return false;
        Bounds lb = t.mf.sharedMesh.bounds;
        Vector3 size = Vector3.Max(lb.size, new Vector3(0.01f, 0.01f, 0.01f));
        t.dims = dims; t.nx = nx; t.ny = ny; t.nz = nz;
        t.vs = new Vector3(size.x / (nx - 2), size.y / (ny - 2), size.z / (nz - 2));   // 가장자리 한 칸씩은 비워 둔다
        t.gmin = lb.min - t.vs;
        int n = nx * ny * nz;
        t.cov = new ushort[n]; t.pal = new byte[n]; t.pix = new Color32[n];
        t.tex = new Texture3D(nx, ny, nz, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "PaintMask(" + t.key + ")" };
        return true;
    }

    private static Color32 Pal(int i)
    {
        if (s_pal32 == null)
        {
            s_pal32 = new Color32[PaintGun.Palette.Length];
            for (int k = 0; k < s_pal32.Length; k++) s_pal32[k] = PaintGun.Palette[k];
        }
        return s_pal32[Mathf.Clamp(i, 0, s_pal32.Length - 1)];
    }

    /// <summary>분사 지점 하나를 마스크에 더한다(정수 계산 — 모든 기기에서 같은 결과).</summary>
    private static void ApplyDab(Target t, int cell, int color, int amt)
    {
        int ix = cell % t.nx, iy = (cell / t.nx) % t.ny, iz = cell / (t.nx * t.ny);
        Color32 pc = Pal(color);
        bool any = false;
        for (int dz = -DabRadius; dz <= DabRadius; dz++)
        {
            int z = iz + dz; if (z < 1 || z > t.nz - 2) continue;
            for (int dy = -DabRadius; dy <= DabRadius; dy++)
            {
                int y = iy + dy; if (y < 1 || y > t.ny - 2) continue;
                for (int dx = -DabRadius; dx <= DabRadius; dx++)
                {
                    int x = ix + dx; if (x < 1 || x > t.nx - 2) continue;
                    int d2 = dx * dx + dy * dy + dz * dz;
                    if (d2 >= Falloff.Length) continue;
                    int add = (amt * Falloff[d2]) >> 8;
                    if (add <= 0) continue;
                    int idx = x + t.nx * (y + t.ny * z);
                    int c = t.cov[idx];
                    if (c == 0 || t.pal[idx] == color) { t.pal[idx] = (byte)color; c = Mathf.Min(1023, c + add); }
                    else { c -= add; if (c <= 0) { t.pal[idx] = (byte)color; c = -c; } }   // 다른 색은 먼저 옅어진 뒤 새 색으로
                    t.cov[idx] = (ushort)c;
                    int a = c >> 2;                                       // 0~255
                    Color32 q = Pal(t.pal[idx]);
                    t.pix[idx] = new Color32((byte)(q.r * a / 255), (byte)(q.g * a / 255), (byte)(q.b * a / 255), (byte)a);
                    any = true;
                }
            }
        }
        if (any) { t.dirty = true; s_dirty.Add(t); }
    }

    private static int CellOf(Target t, Vector3 local)
    {
        int ix = Mathf.Clamp(Mathf.FloorToInt((local.x - t.gmin.x) / t.vs.x), 1, t.nx - 2);
        int iy = Mathf.Clamp(Mathf.FloorToInt((local.y - t.gmin.y) / t.vs.y), 1, t.ny - 2);
        int iz = Mathf.Clamp(Mathf.FloorToInt((local.z - t.gmin.z) / t.vs.z), 1, t.nz - 2);
        return ix + t.nx * (iy + t.ny * iz);
    }

    // ── 보이기(집중·엑스레이 모드와 같이 쓰기) ─────────────────────────────────────

    /// <summary>집중 모드가 렌더러를 반투명으로 되돌려 놓은 뒤 도색을 다시 입힌다.</summary>
    public static void Reshow(Renderer r)
    {
        if (r == null) return;
        foreach (var t in s_targets.Values) if (t.renderer == r && t.HasMask) { t.dirty = true; Show(t); }
    }

    private static Shader PaintShader
    {
        get
        {
            if (!s_shaderTried)
            {
                s_shaderTried = true;
                s_shader = Resources.Load<Shader>("Equipment/DodrillPaintLit");
                if (s_shader == null || !s_shader.isSupported) Debug.LogWarning("[PaintSync] 도색 셰이더(Resources/Equipment/DodrillPaintLit)를 쓸 수 없어 도색이 보이지 않습니다.");
            }
            return s_shader != null && s_shader.isSupported ? s_shader : null;
        }
    }

    /// <summary>재질 칸을 도색 셰이더 복사본으로 바꾼다(재질 이름은 그대로 — 다른 코드가 이름으로 부품을 알아본다).</summary>
    private static bool EnsurePaintMaterial(Target t)
    {
        var sh = PaintShader;
        if (sh == null || t.renderer == null) return false;
        var mats = t.renderer.sharedMaterials;
        if (t.mat >= mats.Length) return false;
        var cur = mats[t.mat];
        if (cur == null) return false;
        if (cur.shader == sh) return true;
        if (!cur.name.StartsWith("M_")) return false;   // 집중 모드의 투명 재질 등 — 건드리지 않는다
        if (!s_clone.TryGetValue(cur, out var clone) || clone == null)
        {
            clone = new Material(cur) { name = cur.name };
            clone.shader = sh;
            s_clone[cur] = clone;
        }
        mats[t.mat] = clone;
        t.renderer.sharedMaterials = mats;
        return true;
    }

    private static void Show(Target t)
    {
        if (t.renderer == null || !t.HasMask) return;
        if (FocusMode.IsFaded(t.renderer)) return;   // 반투명(집중·엑스레이) 중에는 입히지 않는다 — 복원할 때 Reshow
        if (!EnsurePaintMaterial(t)) return;
        if (t.dirty)
        {
            t.dirty = false;
            t.tex.SetPixelData(t.pix, 0);
            t.tex.Apply(false, false);
            t.uploadAt = Time.unscaledTime;
        }
        s_mpb ??= new MaterialPropertyBlock();
        t.renderer.GetPropertyBlock(s_mpb, t.mat);
        s_mpb.SetTexture(IdMask, t.tex);
        s_mpb.SetVector(IdMin, new Vector4(t.gmin.x, t.gmin.y, t.gmin.z, 1f));
        s_mpb.SetVector(IdInv, new Vector4(1f / (t.vs.x * t.nx), 1f / (t.vs.y * t.ny), 1f / (t.vs.z * t.nz), 0f));
        t.renderer.SetPropertyBlock(s_mpb, t.mat);
    }

    // ── 분사 지점 보내기 ───────────────────────────────────────────────────────

    private class Out { public int dims; public readonly List<int> cells = new List<int>(); public readonly List<byte> cols = new List<byte>(), amts = new List<byte>(); }
    private static readonly Dictionary<string, Out> s_out = new Dictionary<string, Out>();
    private static float s_flushAt;

    /// <summary>들고 뿌리는 사람 화면에서 칠한다(즉시 보이고, 0.1초마다 모아서 서버로).</summary>
    public static void LocalDab(Target t, Vector3 worldPoint, int color, int amt)
    {
        if (t == null || t.renderer == null || t.mf == null || t.mf.sharedMesh == null) return;
        if (!t.HasMask && !Build(t, DimsFor(t))) return;
        Vector3 local = t.renderer.transform.InverseTransformPoint(worldPoint);
        // 분사가 한 점에 박히지 않도록 칸 하나 안쪽에서 살짝 흩뜨린다
        Vector3 j = Random.insideUnitSphere * 0.8f;
        local += new Vector3(j.x * t.vs.x, j.y * t.vs.y, j.z * t.vs.z);
        int cell = CellOf(t, local);
        color = Mathf.Clamp(color, 0, PaintGun.Palette.Length - 1);
        amt = Mathf.Clamp(amt, 1, 255);
        ApplyDab(t, cell, color, amt);
        Show(t);
        if (!s_out.TryGetValue(t.key, out var o)) s_out[t.key] = o = new Out { dims = t.dims };
        o.cells.Add(cell); o.cols.Add((byte)color); o.amts.Add((byte)amt);
    }

    private static void Flush()
    {
        if (s_out.Count == 0) return;
        foreach (var kv in s_out)
        {
            var o = kv.Value;
            for (int i = 0; i < o.cells.Count; i += MaxBatch)
            {
                int n = Mathf.Min(MaxBatch, o.cells.Count - i);
                var b = new PaintBroadcast { key = kv.Key, dims = o.dims, from = -1, cells = new int[n], colors = new byte[n], amts = new byte[n] };
                o.cells.CopyTo(i, b.cells, 0, n); o.cols.CopyTo(i, b.colors, 0, n); o.amts.CopyTo(i, b.amts, 0, n);
                if (InstanceFinder.IsServerStarted) ServerStore(b, null, false);
                else if (InstanceFinder.IsClientStarted) InstanceFinder.ClientManager.Broadcast(b);
            }
        }
        s_out.Clear();
    }

    /// <summary>드라이버가 매 프레임 호출 — 방송 등록, 패널 찾기, 모아 둔 분사 지점 전송, 바뀐 마스크 올리기.</summary>
    public static void Tick()
    {
        Scan();
        if (Time.unscaledTime >= s_flushAt) { s_flushAt = Time.unscaledTime + 0.1f; Flush(); }
        if (s_dirty.Count > 0)
        {
            List<Target> done = null;
            foreach (var t in s_dirty)
            {
                if (t == null || t.renderer == null) { (done ??= new List<Target>()).Add(t); continue; }
                if (Time.unscaledTime - t.uploadAt < 0.1f) continue;   // 올리기는 초당 10번까지
                Show(t);   // 반투명 중이면 못 입힌다 — 복원할 때 Reshow 가 다시 입힌다
                (done ??= new List<Target>()).Add(t);
            }
            if (done != null) foreach (var t in done) s_dirty.Remove(t);
        }
    }

    // ── 방송 ───────────────────────────────────────────────────────────────────

    private class Log
    {
        public int dims;
        public readonly List<int> cells = new List<int>();
        public readonly List<byte> cols = new List<byte>(), amts = new List<byte>();
    }
    private static readonly Dictionary<string, Log> s_log = new Dictionary<string, Log>();   // 서버: 늦게 들어온 사람에게 줄 기록

    private static void Register()
    {
        var sm = InstanceFinder.ServerManager; var cm = InstanceFinder.ClientManager;
        if (!s_reg && sm != null) { sm.RegisterBroadcast<PaintBroadcast>(OnServer); s_reg = true; }
        if (!s_cliReg && cm != null) { cm.RegisterBroadcast<PaintBroadcast>(OnClient); s_cliReg = true; }
        if (!s_asked && cm != null && cm.Started && !InstanceFinder.IsServerStarted) { s_asked = true; cm.Broadcast(new PaintBroadcast { key = "" }); }
    }

    private static bool Valid(PaintBroadcast b)
    {
        if (string.IsNullOrEmpty(b.key) || b.key.Length > 160) return false;
        if (!DimsValid(b.dims, out int nx, out int ny, out int nz)) return false;
        if (b.cells == null || b.colors == null || b.amts == null) return false;
        int n = b.cells.Length;
        if (n == 0 || n > 256 || b.colors.Length != n || b.amts.Length != n) return false;
        int total = nx * ny * nz;
        for (int i = 0; i < n; i++)
            if (b.cells[i] < 0 || b.cells[i] >= total || b.colors[i] >= PaintGun.Palette.Length || b.amts[i] == 0) return false;
        return true;
    }

    private static void ServerStore(PaintBroadcast b, NetworkConnection sender, bool applyLocal)
    {
        if (!s_log.TryGetValue(b.key, out var log)) s_log[b.key] = log = new Log { dims = b.dims };
        else if (log.dims != b.dims) { Debug.LogWarning($"[PaintSync] {b.key}: 마스크 크기가 달라 무시했습니다"); return; }
        if (log.cells.Count < MaxLogPerKey) { log.cells.AddRange(b.cells); log.cols.AddRange(b.colors); log.amts.AddRange(b.amts); }
        b.from = sender != null ? sender.ClientId : -1;
        if (applyLocal && InstanceFinder.IsClientStarted) ApplyOrQueue(b);   // 호스트 화면
        if (sender != null) InstanceFinder.ServerManager.BroadcastExcept(sender, b);
        else InstanceFinder.ServerManager.Broadcast(b);
    }

    private static void OnServer(NetworkConnection conn, PaintBroadcast b, Channel ch)
    {
        if (string.IsNullOrEmpty(b.key))
        {
            // 늦게 들어온 사람: 지금까지의 기록을 순서대로 돌려준다
            foreach (var kv in s_log)
            {
                var log = kv.Value;
                for (int i = 0; i < log.cells.Count; i += MaxBatch)
                {
                    int n = Mathf.Min(MaxBatch, log.cells.Count - i);
                    var r = new PaintBroadcast { key = kv.Key, dims = log.dims, from = -1, cells = new int[n], colors = new byte[n], amts = new byte[n] };
                    log.cells.CopyTo(i, r.cells, 0, n); log.cols.CopyTo(i, r.colors, 0, n); log.amts.CopyTo(i, r.amts, 0, n);
                    InstanceFinder.ServerManager.Broadcast(conn, r);
                }
            }
            return;
        }
        if (!Valid(b)) return;
        ServerStore(b, conn, true);
    }

    private static void OnClient(PaintBroadcast b, Channel ch)
    {
        if (InstanceFinder.IsServerStarted) return;   // 호스트는 ServerStore 에서 이미 처리
        var cm = InstanceFinder.ClientManager;
        if (b.from >= 0 && cm != null && cm.Connection != null && cm.Connection.ClientId == b.from) return;   // 내가 보낸 것
        if (!Valid(b)) return;
        ApplyOrQueue(b);
    }

    private static void ApplyOrQueue(PaintBroadcast b)
    {
        if (Headless) return;
        Scan();
        if (s_targets.TryGetValue(b.key, out var t) && t.renderer != null) { ApplyBatch(t, b); return; }
        // 차가 아직 없다 — 생기면 Scan 이 적용
        if (!s_pending.TryGetValue(b.key, out var list)) s_pending[b.key] = list = new List<PaintBroadcast>();
        if (list.Count < 600) list.Add(b);
    }

    private static void ApplyBatch(Target t, PaintBroadcast b)
    {
        if (!t.HasMask && !Build(t, b.dims)) return;
        if (t.dims != b.dims) { Debug.LogWarning($"[PaintSync] {t.key}: 마스크 크기가 달라 무시했습니다"); return; }
        int total = t.nx * t.ny * t.nz;
        for (int i = 0; i < b.cells.Length; i++)
            if (b.cells[i] >= 0 && b.cells[i] < total) ApplyDab(t, b.cells[i], b.colors[i], b.amts[i]);
        // 화면 반영(텍스처 올리기)은 Tick 이 초당 10번까지 모아서 한다
    }
}

public class PaintSyncDriver : MonoBehaviour
{
    private void Update() => PaintSync.Tick();
}
