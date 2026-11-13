using System.Collections.Generic;
using Autohand;
using UnityEngine;

// ============================================================
//  FocusMode.cs  (2026-11-13)
//
//  집중 모드 — 볼트 묶음을 렌치로 작업하거나 커넥터·단자를 다룰 때, 내 눈과 작업 대상 사이를 가리는
//  차체·팩 커버 등을 하얀 반투명으로 바꿔 손과 대상이 잘 보이게 한다(엑스레이의 다음 단계).
//  · 자동: ① 렌치가 볼트를 물었을 때(ImpactWrench.LocalTargetBolt) → 그 볼트 묶음
//          ② 손(PC·모바일은 카메라)이 지금 단계 대상(안내 표시 대상) 가까이 왔을 때 → 그 대상
//    작업이 끝나도 1.5초는 유지(깜빡임 방지).
//  · VR: 손바닥 메뉴 '집중 모드' / PC: K 키 로 자동 ↔ 끔 전환(기기에 저장).
//  · 내 화면에서만 재질을 바꾼다(다른 사람·서버 무관). 위치·충돌은 그대로.
//  · Quest 성능: 반투명으로 바꾸는 렌더러 수를 MaxFaded 개로 제한하고, 바꾼 렌더러는 그림자를 끈다.
//  재질: 고스트 가이드 재질(빌드에 들어 있는 URP Lit 반투명)을 복사해 흰색으로 쓴다(XRayMode 와 같은 이유).
//  씬 배치 불필요 — 게임 시작 시 자동 생성.
// ============================================================
public class FocusMode : MonoBehaviour
{
    private const string PrefOff = "focusmode_off";
    private const int MaxFaded = 60;
    private const float HandNear = 0.45f;       // VR: 손이 대상에서 이만큼 안
    private const float CamNear = 1.3f;         // PC·모바일: 카메라가 대상에서 이만큼 안
    private const float CandidateRadius = 2.2f; // 대상 주변 이 반경 안 렌더러만 후보
    private const float LingerSeconds = 1.5f;
    private const float MaxRendererSize = 9f;   // 바닥·벽처럼 큰 것은 제외

    public static bool Enabled
    {
        get => PlayerPrefs.GetInt(PrefOff, 0) == 0;
        set { PlayerPrefs.SetInt(PrefOff, value ? 0 : 1); PlayerPrefs.Save(); if (!value && s_inst != null) s_inst.RestoreAll(); }
    }
    /// <summary>지금 무언가를 반투명으로 바꾸고 있는가.</summary>
    public static bool Engaged => s_inst != null && s_inst._target != null;

    public static void Toggle()
    {
        Enabled = !Enabled;
        WarningPopup.Show("focusmode", Enabled ? "집중 모드 자동" : "집중 모드 끔",
            Enabled ? "작업 대상 가까이 가면 가리는 차체가 반투명해집니다" : "", new Color(0.25f, 0.28f, 0.32f, 0.92f), 1.6f);
    }

    private class Faded
    {
        public Material[] orig;
        public UnityEngine.Rendering.ShadowCastingMode shadow;
        public float lastHit;
    }

    private static FocusMode s_inst;
    private Material _white;
    private Transform _target;
    private float _lastEngaged;
    private readonly List<Renderer> _targetRenderers = new List<Renderer>();
    private readonly List<Renderer> _candidates = new List<Renderer>();
    private readonly Dictionary<Renderer, Faded> _faded = new Dictionary<Renderer, Faded>();
    private readonly List<Renderer> _tmp = new List<Renderer>();
    private readonly List<Vector3> _pts = new List<Vector3>();
    private float _nextRay;
    private bool _lastXray;
    private NextActionGuide _guide;
    private float _nextGuideFind;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (s_inst != null || FindAnyObjectByType<FocusMode>() != null) return;
        var go = new GameObject("[FocusMode]");
        DontDestroyOnLoad(go);
        s_inst = go.AddComponent<FocusMode>();
    }

    private void Awake() => s_inst = this;

    private void OnDestroy()
    {
        RestoreAll();
        if (_white != null) Destroy(_white);
        if (s_inst == this) s_inst = null;
    }

    private void Update()
    {
        if (!VrProbeHandling.IsVRDevice)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.kKey.wasPressedThisFrame) Toggle();
        }
        bool xray = XRayMode.Active;
        if (xray != _lastXray) { _lastXray = xray; RestoreAll(); }   // 엑스레이를 켜고 끌 때 후보(차체 전체 ↔ 주변)를 다시 모은다
        if (!Enabled && !xray) { if (_target != null) RestoreAll(); return; }

        var cam = Camera.main;
        if (cam == null) return;

        Transform want = FindWorkTarget(cam.transform);
        if (want != null) _lastEngaged = Time.unscaledTime;
        else if (_target != null && Time.unscaledTime - _lastEngaged < LingerSeconds) want = _target;   // 잠깐 떨어져도 유지

        if (want != _target)
        {
            RestoreAll();
            _target = want;
            if (_target != null) CollectCandidates();
        }
        if (_target == null) return;
        if (!_target.gameObject.activeInHierarchy) { RestoreAll(); return; }

        if (Time.unscaledTime < _nextRay) return;
        _nextRay = Time.unscaledTime + 0.15f;
        UpdateOccluders(cam.transform.position);
    }

    // ── 작업 대상 찾기 ─────────────────────────────

    private Transform FindWorkTarget(Transform cam)
    {
        // 엑스레이가 켜져 있으면 거리와 상관없이 지금 단계 대상 앞을 비운다(엑스레이를 켜도 차가 그대로라 변화가 안 보였다)
        if (XRayMode.Active)
        {
            var xf = CurrentFocus();
            if (xf != null) return xf;
        }
        if (!Enabled) return null;
        // ① 렌치가 볼트를 물었다
        var bolt = ImpactWrench.LocalTargetBolt;
        if (bolt != null && bolt.isActiveAndEnabled)
        {
            var g = bolt.GetComponentInParent<BoltGroupCounter>();
            return g != null ? g.transform : bolt.transform;
        }

        // ② 지금 단계 대상 가까이
        Transform focus = CurrentFocus();
        if (focus == null) return null;
        Vector3 c = Center(focus);
        if (VrProbeHandling.IsVRDevice)
        {
            foreach (var h in LocalHands.All)
            {
                if (h == null || !h.isActiveAndEnabled) continue;
                Vector3 p = h.palmTransform != null ? h.palmTransform.position : h.transform.position;
                if ((p - ClosestOnTarget(focus, p, c)).sqrMagnitude < HandNear * HandNear) return focus;
            }
            return null;
        }
        return (cam.position - ClosestOnTarget(focus, cam.position, c)).sqrMagnitude < CamNear * CamNear ? focus : null;
    }

    private Transform CurrentFocus()
    {
        if (_guide == null && Time.unscaledTime >= _nextGuideFind) { _nextGuideFind = Time.unscaledTime + 2f; _guide = FindAnyObjectByType<NextActionGuide>(); }
        if (_guide != null)
        {
            if (_guide.FocusGroup != null && _guide.FocusGroup.gameObject.activeInHierarchy) return _guide.FocusGroup.transform;
            if (_guide.FocusTransform != null) return _guide.FocusTransform;
        }
        var gs = GuideSystem.Instance;
        if (gs != null && gs.XRayTarget != null && gs.XRayTarget.activeInHierarchy) return gs.XRayTarget.transform;
        return null;
    }

    private Vector3 ClosestOnTarget(Transform t, Vector3 p, Vector3 fallback)
    {
        // 볼트 묶음처럼 넓게 퍼진 대상은 가장 가까운 렌더러 기준
        _tmp.Clear();
        t.GetComponentsInChildren(false, _tmp);
        float best = float.MaxValue; Vector3 bp = fallback;
        foreach (var r in _tmp)
        {
            if (r == null || !r.enabled || r is LineRenderer || r is ParticleSystemRenderer) continue;
            Vector3 q = r.bounds.ClosestPoint(p);
            float d = (q - p).sqrMagnitude;
            if (d < best) { best = d; bp = q; }
        }
        return bp;
    }

    private static Vector3 Center(Transform t)
    {
        bool any = false; Bounds b = default;
        foreach (var r in t.GetComponentsInChildren<Renderer>())
        {
            if (r == null || !r.enabled || r is LineRenderer || r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return any ? b.center : t.position;
    }

    // ── 가리는 것 찾기 ─────────────────────────────

    private void CollectCandidates()
    {
        _candidates.Clear();
        _targetRenderers.Clear();
        _target.GetComponentsInChildren(false, _targetRenderers);
        var targetSet = new HashSet<Renderer>(_targetRenderers);
        Vector3 c = Center(_target);

        // 엑스레이: 거리·크기와 상관없이 차체 전체(바퀴·전면부 포함)를 후보로 — 예전엔 반경 2.2m·크기 9m 제한과 스킨드 메시 제외로 바퀴·전면부가 빠졌다
        bool xray = XRayMode.Active;
        float radius = xray ? float.MaxValue * 0.25f : CandidateRadius;
        float maxSize = xray ? 1000f : MaxRendererSize;
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || targetSet.Contains(r)) continue;
            int layer = r.gameObject.layer;
            if (layer == 1 || layer == 2 || layer == 5) continue;                       // 안내·UI
            // '차량 외 다른 오브젝트도 투명해지던' 문제: 선반·타이어 더미·공구함 같은 주변 물건은 건드리지 않고 차량(과 배터리 팩) 부품만 대상으로 한다
            if (r.GetComponentInParent<VehiclePartsManager>(true) == null && r.GetComponentInParent<TireWheel>() == null) continue;
            var b = r.bounds;
            if (b.size.magnitude > maxSize) continue;
            if (!xray && (b.ClosestPoint(c) - c).sqrMagnitude > radius * radius) continue;
            if (r.GetComponentInParent<Hand>() != null) continue;
            if (r.GetComponentInParent<SyncGrab>() != null && r.GetComponentInParent<TireWheel>() == null) continue;   // 도구·들 수 있는 물건(휠은 제외 — 차 바퀴)
            if (r.GetComponentInParent<Bolt>() != null) continue;                       // 볼트는 그대로 보이게
            var sm = r.sharedMaterial;
            if (sm == null || sm.renderQueue > 2500) continue;                          // 이미 반투명
            _candidates.Add(r);
        }
        if (_white == null) MakeMaterial();
    }

    private void UpdateOccluders(Vector3 eye)
    {
        if (_white == null) return;
        // 대상 렌더러 중심 + 묶음 경계 모서리 쪽으로 몇 줄 쏴서 사이에 있는 렌더러를 찾는다
        _pts.Clear();
        foreach (var r in _targetRenderers)
            if (r != null && r.enabled && r.gameObject.activeInHierarchy && !(r is LineRenderer) && !(r is ParticleSystemRenderer) && r.gameObject.layer != 1 && r.gameObject.layer != 5)
                _pts.Add(r.bounds.center);
        if (_pts.Count == 0)
        {
            // 메시 없는 대상(클릭 구역) → 콜라이더 중심, 그것도 없으면 위치
            foreach (var c in _target.GetComponentsInChildren<Collider>()) if (c != null && c.enabled) _pts.Add(c.bounds.center);
            if (_pts.Count == 0) _pts.Add(_target.position);
        }

        float now = Time.unscaledTime;
        int rays = 0;
        int step = Mathf.Max(1, _pts.Count / 8);   // 볼트가 많으면 일부만
        for (int i = 0; i < _pts.Count; i += step)
        {
            Vector3 p = _pts[i];
            Vector3 d = p - eye;
            float len = d.magnitude;
            if (len < 0.05f) continue;
            var ray = new Ray(eye, d / len);
            rays++;
            foreach (var r in _candidates)
            {
                if (r == null || !r.enabled) continue;
                if (!r.bounds.IntersectRay(ray, out float hit) || hit > len - 0.04f) continue;
                if (r.bounds.Contains(eye)) continue;   // 내가 그 안에 있으면(차 밑 등) 바꾸지 않는다 — 시야 전체가 하얘짐
                if (_faded.TryGetValue(r, out var f)) { f.lastHit = now; ClearBlocks(r); continue; }
                if (_faded.Count >= (XRayMode.Active ? MaxFaded * 3 : MaxFaded)) continue;
                Fade(r, now);
            }
        }

        // 0.5초 동안 안 가리면 원래대로
        List<Renderer> drop = null;
        foreach (var kv in _faded)
            if (kv.Key == null || now - kv.Value.lastHit > 0.5f) (drop ??= new List<Renderer>()).Add(kv.Key);
        if (drop != null) foreach (var r in drop) Restore(r);
    }

    private void Fade(Renderer r, float now)
    {
        var orig = r.sharedMaterials;
        var f = new Faded { orig = orig, shadow = r.shadowCastingMode, lastHit = now };
        var mats = new Material[orig.Length];
        for (int i = 0; i < mats.Length; i++) mats[i] = _white;
        r.sharedMaterials = mats;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _faded[r] = f;
        ClearBlocks(r);
    }

    // 도색(PaintSync)은 재질 칸마다 MaterialPropertyBlock 으로 색(알파 1)을 덮어쓴다 → 반투명 재질로 바꿔도 도색 색이 불투명하게 남아
    // '엑스레이에서 도색된 색으로 보이던' 문제. 반투명인 동안은 블록을 비워 둔다(복원할 때 도색을 다시 입힌다).
    private static void ClearBlocks(Renderer r)
    {
        if (r == null) return;
        int n = r.sharedMaterials.Length;
        for (int i = 0; i < n; i++) r.SetPropertyBlock(null, i);
    }

    /// <summary>지금 반투명으로 바꿔 둔 렌더러인가(도색이 색을 덮어쓰지 않게 PaintSync 가 확인).</summary>
    public static bool IsFaded(Renderer r) => s_inst != null && r != null && s_inst._faded.ContainsKey(r);

    private void Restore(Renderer r)
    {
        if (r != null && _faded.TryGetValue(r, out var f))
        {
            r.sharedMaterials = f.orig;
            r.shadowCastingMode = f.shadow;
        }
        _faded.Remove(r);
        PaintSync.Reshow(r);
    }

    private void RestoreAll()
    {
        foreach (var kv in _faded)
            if (kv.Key != null) { kv.Key.sharedMaterials = kv.Value.orig; kv.Key.shadowCastingMode = kv.Value.shadow; }
        var toReshow = new List<Renderer>(_faded.Keys);
        _faded.Clear();
        foreach (var r in toReshow) PaintSync.Reshow(r);
        _candidates.Clear();
        _targetRenderers.Clear();
        _target = null;
    }

    private void MakeMaterial()
    {
        var src = GuideSystem.Instance != null ? GuideSystem.Instance.ghostMaterial : null;
        if (src == null) return;
        _white = new Material(src) { name = "FocusWhite" };
        if (_white.HasProperty("_BaseMap")) _white.SetTexture("_BaseMap", null);
        if (_white.HasProperty("_MainTex")) _white.SetTexture("_MainTex", null);
        var c = new Color(0.95f, 0.97f, 1f, 0.16f);
        if (_white.HasProperty("_BaseColor")) _white.SetColor("_BaseColor", c);
        if (_white.HasProperty("_Color")) _white.SetColor("_Color", c);
        if (_white.HasProperty("_EmissionColor")) _white.SetColor("_EmissionColor", Color.black);
    }
}
