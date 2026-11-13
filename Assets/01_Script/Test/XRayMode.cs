using System.Collections.Generic;
using Autohand;
using UnityEngine;

// ============================================================
//  XRayMode.cs
//
//  엑스레이 모드 — 지금 단계에서 다뤄야 할 부품(남은 볼트·커넥터·단자·장착 위치·찾아야 할 도구)을
//  차체·팩 커버 너머로 하늘색으로 빛나게 비춰 보여 준다. 차 밑 볼트처럼 어디 있는지 안 보이는 대상을 찾을 때 쓴다.
//  · VR: 손바닥 메뉴 '엑스레이' 버튼 / PC: X 키
//  · 평가 모드에서는 쓸 수 없다(위치를 알려 주는 힌트와 같으므로)
//  · 단계가 바뀌면 비추는 대상도 따라 바뀐다. 다 푼(사라진)·다 조인 볼트는 빠진다.
//  그리기: 대상 렌더러에 렌더링 레이어 'XRayTarget' 표시를 붙이면 HeldItemOnTopFeature 의 엑스레이 패스가
//          깊이를 무시하고 반투명 하늘색으로 한 번 더 그린다(물체 위치·충돌·다른 사람 화면은 그대로).
//  · 메시가 없는 대상(클릭 구역)은 그 자리에 하늘색 구를 띄워 비춘다. 켜져 있는 동안 내 눈과 대상 사이 차체는 하얀 반투명(FocusMode).
//  씬 배치 불필요 — 게임 시작 시 자동 생성된다.
// ============================================================
public class XRayMode : MonoBehaviour
{
    /// <summary>ProjectSettings(Tags and Layers → Rendering Layers) 2번 'XRayTarget'.</summary>
    public const uint Bit = 1u << 2;

    private const float RefreshInterval = 0.25f;
    private static readonly Color Tint = new Color(0.25f, 0.85f, 1f, 1f);

    public static bool Active { get; private set; }
    /// <summary>엑스레이 패스가 덮어 그릴 재질(하늘색 반투명). 렌더 기능이 읽는다.</summary>
    public static Material OverrideMaterial { get; private set; }
    /// <summary>지금 비추는 대상이 하나라도 있는가 — 없으면 렌더 패스를 넣지 않는다.</summary>
    public static bool HasTargets => Active && s_inst != null && s_inst._marked.Count > 0;

    private static XRayMode s_inst;
    private readonly HashSet<Renderer> _marked = new HashSet<Renderer>();
    private readonly HashSet<Renderer> _want = new HashSet<Renderer>();
    private readonly List<Renderer> _tmp = new List<Renderer>();
    private float _nextRefresh;
    private NextActionGuide _guide;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (s_inst != null || FindAnyObjectByType<XRayMode>() != null) return;
        var go = new GameObject("[XRayMode]");
        DontDestroyOnLoad(go);
        s_inst = go.AddComponent<XRayMode>();
    }

    private void Awake() => s_inst = this;

    /// <summary>평가 모드면 켤 수 없다.</summary>
    public static bool Available => !EvaluationMode.ClientIsEvaluation;

    public static void Toggle() => SetActive(!Active);

    public static void SetActive(bool on)
    {
        if (on && !Available)
        {
            WarningPopup.Show("xray", "엑스레이", "평가 모드에서는 쓸 수 없습니다", new Color(0.25f, 0.25f, 0.28f, 0.92f), 1.6f);
            return;
        }
        if (Active == on) return;
        Active = on;
        if (s_inst != null) { s_inst._nextRefresh = 0f; if (!on) s_inst.ClearAll(); }
        WarningPopup.Show("xray", on ? "엑스레이 켜짐" : "엑스레이 꺼짐", on ? "지금 다룰 부품이 하늘색으로 비칩니다" : "", new Color(0.05f, 0.3f, 0.42f, 0.92f), 1.4f);
        Debug.Log($"[XRay] {(on ? "ON" : "OFF")}");
    }

    private void Update()
    {
        // PC: X 키
        if (!VrProbeHandling.IsVRDevice)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.xKey.wasPressedThisFrame && !TypingInUI()) Toggle();
        }

        if (Active && !Available) { Active = false; ClearAll(); }
        if (!Active) return;

        // 반투명 하늘색이 숨 쉬듯
        EnsureMaterial();
        if (OverrideMaterial != null)
        {
            float a = 0.28f + 0.14f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f));
            var c = new Color(Tint.r, Tint.g, Tint.b, a);
            if (OverrideMaterial.HasProperty("_BaseColor")) OverrideMaterial.SetColor("_BaseColor", c);
            if (OverrideMaterial.HasProperty("_Color")) OverrideMaterial.SetColor("_Color", c);
            if (OverrideMaterial.HasProperty("_EmissionColor")) OverrideMaterial.SetColor("_EmissionColor", Tint * 0.9f);
        }

        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + RefreshInterval;
        Refresh();
    }

    // ── 비출 대상 ─────────────────────────────

    private void Refresh()
    {
        _want.Clear();
        _zoneFor = null;
        var gs = GuideSystem.Instance;
        if (gs != null && gs.XRayTarget != null) AddTarget(gs.XRayTarget.transform);

        if (_guide == null) _guide = FindAnyObjectByType<NextActionGuide>();
        if (_guide != null)
        {
            if (_guide.FocusGroup != null) AddTarget(_guide.FocusGroup.transform);
            var f = _guide.FocusTransform;
            if (f != null) AddTarget(f);
        }

        if (_zoneFor == null && _zoneMarker != null && _zoneMarker.activeSelf) _zoneMarker.SetActive(false);
        if (_want.Count == 0 && !_warnedEmpty)
        {
            _warnedEmpty = true;
            WarningPopup.Show("xray_empty", "엑스레이", "지금 단계에는 비춰 볼 부품이 없습니다", new Color(0.05f, 0.3f, 0.42f, 0.92f), 1.6f);
        }
        if (_want.Count > 0) _warnedEmpty = false;
        foreach (var r in _marked) if (r != null && !_want.Contains(r)) Mark(r, false);
        _marked.RemoveWhere(r => r == null || !_want.Contains(r));
        foreach (var r in _want) if (_marked.Add(r)) Mark(r, true);
    }

    // 메시가 없는 대상(클릭 구역·트리거만 있는 '후드 래치' 같은 점)은 비출 렌더러가 없어 엑스레이를 켜도 아무 변화가 없었다.
    // 그 구역 콜라이더 자리에 빛나는 구를 띄워 같은 엑스레이 패스로 비춘다.
    private GameObject _zoneMarker;
    private Transform _zoneFor;

    private void AddTarget(Transform root)
    {
        if (root == null || !root.gameObject.activeInHierarchy) return;
        int before = _want.Count;
        AddTargetInner(root);
        if (_want.Count == before && !HasSolidRenderer(root)) MarkZone(root);   // 다 푼 볼트 묶음처럼 '이미 끝나서' 빈 경우는 제외
    }

    private static bool HasSolidRenderer(Transform root)
    {
        if (root.GetComponentInChildren<Bolt>(true) != null) return true;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (r == null || !r.enabled || r is LineRenderer || r is TrailRenderer || r is ParticleSystemRenderer) continue;
            int l = r.gameObject.layer;
            if (l == 1 || l == 2 || l == 5) continue;
            return true;
        }
        return false;
    }

    private void MarkZone(Transform root)
    {
        bool any = false; Bounds b = default;
        foreach (var c in root.GetComponentsInChildren<Collider>())
        {
            if (c == null || !c.enabled) continue;
            if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
        }
        if (!any) b = new Bounds(root.position, Vector3.one * 0.08f);
        if (_zoneMarker == null)
        {
            _zoneMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _zoneMarker.name = "[XRayZoneMarker]";
            Destroy(_zoneMarker.GetComponent<Collider>());
            DontDestroyOnLoad(_zoneMarker);
            var mr = _zoneMarker.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
        EnsureMaterial();
        var r = _zoneMarker.GetComponent<MeshRenderer>();
        if (OverrideMaterial != null) r.sharedMaterial = OverrideMaterial;   // 평소 화면에서도 같은 하늘색 구
        float size = Mathf.Clamp(Mathf.Max(b.size.x, b.size.y, b.size.z), 0.06f, 0.3f);
        _zoneMarker.transform.position = b.center;
        _zoneMarker.transform.localScale = Vector3.one * size;
        if (!_zoneMarker.activeSelf) _zoneMarker.SetActive(true);
        _zoneFor = root;
        _want.Add(r);
    }

    private void AddTargetInner(Transform root)
    {

        // 볼트 묶음이면 남은 볼트만
        var bolts = root.GetComponentsInChildren<Bolt>();
        if (bolts.Length > 0)
        {
            foreach (var b in bolts)
            {
                if (b == null || !b.gameObject.activeInHierarchy || b.IsMarkedDone || (!b.isAssembleMode && b.isloosened)) continue;
                AddRenderers(b.transform);
            }
            return;
        }
        AddRenderers(root);
    }

    private void AddRenderers(Transform t)
    {
        _tmp.Clear();
        t.GetComponentsInChildren(false, _tmp);
        foreach (var r in _tmp)
        {
            if (r == null || !r.enabled) continue;
            if (r is LineRenderer || r is TrailRenderer || r is ParticleSystemRenderer) continue;
            int layer = r.gameObject.layer;
            if (layer == 1 || layer == 2 || layer == 5) continue;               // 안내·UI
            if (r.GetComponentInParent<Hand>() != null) continue;                // 손
            var sg = r.GetComponentInParent<SyncGrab>();
            if (sg != null && (sg.IsGrabbed || sg.IsHolstered)) continue;        // 누가 들고 있는 물건
            _want.Add(r);
        }
    }

    private static void Mark(Renderer r, bool on)
    {
        if (r == null) return;
        r.renderingLayerMask = on ? (r.renderingLayerMask | Bit) : (r.renderingLayerMask & ~Bit);
    }

    private bool _warnedEmpty;

    private void ClearAll()
    {
        if (_zoneMarker != null && _zoneMarker.activeSelf) _zoneMarker.SetActive(false);
        _warnedEmpty = false;
        foreach (var r in _marked) Mark(r, false);
        _marked.Clear();
    }

    private void OnDestroy()
    {
        ClearAll();
        if (_zoneMarker != null) Destroy(_zoneMarker);
        if (s_inst == this) s_inst = null;
    }

    // ── 재질 ─────────────────────────────

    private static void EnsureMaterial()
    {
        if (OverrideMaterial != null) return;
        // 고스트 가이드 재질(빌드에 들어 있는 URP Lit 반투명)을 복사해 쓴다 — 런타임에 만든 셰이더 변형이 빌드에서 빠지는 문제를 피한다
        var src = GuideSystem.Instance != null ? GuideSystem.Instance.ghostMaterial : null;
        if (src == null) return;
        var m = new Material(src) { name = "XRayOverride" };
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
        OverrideMaterial = m;
    }

    private static bool TypingInUI()
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        return sel != null && (sel.GetComponent<TMPro.TMP_InputField>() != null || sel.GetComponent<UnityEngine.UI.InputField>() != null);
    }
}
