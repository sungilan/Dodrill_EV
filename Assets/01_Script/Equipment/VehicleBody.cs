using System;
using System.Collections.Generic;
using Autohand;
using UnityEngine;

// ============================================================
//  VehicleBody.cs
//
//  차체 상호작용 — 런타임에 차량(Ioniq5N)을 찾아 붙는다(프리팹은 건드리지 않는다).
//  · VehicleRigBootstrap : 1초마다 새 차량·스마트키를 찾아 VehicleRig / SmartKeyUse 를 붙이고, VR 손가락 찌르기를 처리한다
//  · VehicleRig          : 차량에 붙어 아래를 조립한다
//      - 뒷문 2개 : 자기 Animator 에 컨트롤러가 없어 애니메이션이 안 돌던 문 → 부모 Animator 의 'Open' 클립을 직접 재생(ClipDoor)
//      - 프렁크 덮개(SM_Frunk_lid) : 후드를 연 뒤에 열 수 있는 힌지 덮개(VehicleHinge)
//      - 냉각수 탱크·뚜껑(CoolantReservoir), 시동·계기판·라이트(VehicleEngine), 경적
//      - 문·후드의 열림 상태를 늦게 들어온 사람에게도 보내도록 표시(ClickableAnimator.lateJoinSync)
//  시나리오 진행과는 무관한 자유 상호작용이다.
// ============================================================

/// <summary>힌지로 열리는 덮개(프렁크). Openable 과 같은 회전 계산이지만 세계 좌표의 차량 기준점으로 축을 잡는다.</summary>
public class VehicleHinge : MonoBehaviour, IWorldClickable, IVehicleState
{
    private string _stateId;
    private string _displayName = "덮개";
    private Vector3 _pivotParent, _axisParent;
    private float _angle = 70f, _duration = 0.5f;
    private Func<bool> _canOpen;
    private string _blockedHint = "";
    private bool _open;
    private float _t;
    private Vector3 _p0;
    private Quaternion _r0;
    private bool _ready;

    public string StateId => _stateId;
    public string DisplayName => _displayName;
    public bool VrPokeable => true;
    public bool IsOpen => _open;

    /// <param name="pivotLocal">차량 로컬의 회전축 위의 한 점</param>
    /// <param name="axisLocal">차량 로컬 회전축</param>
    /// <param name="liftSide">열릴 때 덮개 중심이 위로 올라가는 쪽으로 부호를 자동 선택</param>
    public void Init(Transform vehicle, string id, string displayName, Vector3 pivotLocal, Vector3 axisLocal, float openAngle,
                     Func<bool> canOpen, string blockedHint)
    {
        _stateId = id; _displayName = displayName; _canOpen = canOpen; _blockedHint = blockedHint;
        var parent = transform.parent;
        _p0 = transform.localPosition; _r0 = transform.localRotation;
        Vector3 pivotW = vehicle.TransformPoint(pivotLocal);
        Vector3 axisW = vehicle.TransformDirection(axisLocal).normalized;
        // 덮개 중심이 위로 올라가는 방향을 연다
        Vector3 centerW = transform.position;
        var r = GetComponentInChildren<Renderer>();
        if (r != null) centerW = r.bounds.center;
        float up = (pivotW + Quaternion.AngleAxis(openAngle, axisW) * (centerW - pivotW)).y;
        float dn = (pivotW + Quaternion.AngleAxis(-openAngle, axisW) * (centerW - pivotW)).y;
        _angle = up >= dn ? openAngle : -openAngle;
        _pivotParent = parent != null ? parent.InverseTransformPoint(pivotW) : pivotW;
        _axisParent = parent != null ? parent.InverseTransformDirection(axisW).normalized : axisW;
        _ready = true;
        VehicleStateSync.Register(this);
    }

    private void OnDestroy() { VehicleStateSync.Unregister(this); }

    public void ApplyState(float v)
    {
        bool o = v >= 0.5f;
        if (o == _open) return;
        _open = o;
        AudioSource.PlayClipAtPoint(EquipFx.Click, transform.position, 0.6f);
    }

    private bool Blocked => !_open && _canOpen != null && !_canOpen();

    public string HoverMessage(bool touch)
    {
        if (Blocked) return _blockedHint;
        return (touch ? "터치하여 " : "클릭하여 ") + (_open ? "닫기" : "열기");
    }

    public void OnPCClick()
    {
        if (Blocked) { EquipFx.Notice("veh_block_" + _stateId, _displayName, _blockedHint, EquipFx.Warn, 2f, 3f); return; }
        VehicleStateSync.Request(_stateId, _open ? 0f : 1f);
    }

    /// <summary>덮개를 억지로 닫는다(후드가 닫힐 때). 권한이 있는 쪽(서버·오프라인)에서만 부른다.</summary>
    public void RequestClose() { if (_open) VehicleStateSync.Request(_stateId, 0f); }

    private void Update()
    {
        if (!_ready) return;
        float target = _open ? 1f : 0f;
        if (Mathf.Approximately(_t, target)) return;
        _t = Mathf.MoveTowards(_t, target, Time.deltaTime / Mathf.Max(0.05f, _duration));
        float e = Mathf.SmoothStep(0f, 1f, _t);
        Quaternion q = Quaternion.AngleAxis(_angle * e, _axisParent);
        transform.localRotation = q * _r0;
        transform.localPosition = _pivotParent + q * (_p0 - _pivotParent);
    }
}

/// <summary>
/// 자기 Animator 에 컨트롤러가 없어 열고 닫기 애니메이션이 안 돌던 문(뒷문).
/// 부모(Door*_Parent)의 Animator 가 가진 'Open' 클립을 상태 기계 없이 직접 재생한다(열림 여부는 ClickableAnimator.IsOpen 을 따른다).
/// </summary>
public class ClipDoor : MonoBehaviour
{
    private ClickableAnimator _ca;
    private GameObject _target;
    private AnimationClip _clip;
    private Vector3 _closedPos;
    private Quaternion _closedRot;
    private float _t;

    public static bool NeedsClipDriver(ClickableAnimator ca)
    {
        var own = ca.GetComponent<Animator>();
        bool ownOk = own != null && own.enabled && own.runtimeAnimatorController != null;
        if (ownOk) return false;
        var pa = ca.transform.parent != null ? ca.transform.parent.GetComponent<Animator>() : null;
        return pa != null && pa.runtimeAnimatorController != null;
    }

    public void Init(ClickableAnimator ca)
    {
        _ca = ca;
        var pa = ca.transform.parent.GetComponent<Animator>();
        _target = pa.gameObject;
        foreach (var c in pa.runtimeAnimatorController.animationClips)
            if (c != null && c.name == "Open" && c.length > 0.01f) { _clip = c; break; }
        _closedPos = _target.transform.localPosition;
        _closedRot = _target.transform.localRotation;
        enabled = _clip != null;
        // 상태 기계가 클립을 덮어쓰지 않도록 끈다(트리거는 무시된다). 클립을 못 찾으면 원래대로 둔다.
        if (_clip != null) pa.enabled = false;
    }

    private void LateUpdate()
    {
        if (_ca == null || _clip == null || _target == null) return;
        float goal = _ca.IsOpen ? 1f : 0f;
        if (Mathf.Approximately(_t, goal)) return;
        _t = Mathf.MoveTowards(_t, goal, Time.deltaTime / _clip.length);
        if (_t <= 0f) { _target.transform.localPosition = _closedPos; _target.transform.localRotation = _closedRot; return; }
        _clip.SampleAnimation(_target, _t * _clip.length);
    }
}

/// <summary>차량 하나에 붙어 상호작용 부품을 조립한다.</summary>
public class VehicleRig : MonoBehaviour
{
    [Header("프렁크 덮개 (차량 로컬: 앞 = -X, 오른쪽 = +Z)")]
    public string frunkLidName = "SM_Frunk_lid";
    public float frunkOpenAngle = 75f;

    private VehicleHinge _frunk;
    private ClickableAnimator _hood;
    private float _nextHoodCheck;

    private static Transform FindActive(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(false))
            if (t.name == name && t.gameObject.activeInHierarchy) return t;
        return null;
    }

    private void Start()
    {
        var root = transform;
        int clickLayer = 15;

        // 1) 문·후드: 늦게 들어온 사람에게도 열림 상태를 보내고, 애니메이션이 안 도는 문은 클립으로 직접 재생
        foreach (var ca in root.GetComponentsInChildren<ClickableAnimator>(false))
        {
            if (!ca.gameObject.activeInHierarchy) continue;
            string id = ca.uniqueId ?? "";
            // 뒷문은 프리팹에서 ClickableAnimator 가 꺼져 있어 Start(=등록)가 안 돌았다 → 눌러도 서버·다른 화면이 문을 못 찾아 열리지 않았다
            if (id.StartsWith("Door") && !ca.enabled) ca.enabled = true;
            if (id.StartsWith("Door") ||id.StartsWith("Hood") || id == "Battery_12V_Cover" || id == "12VBattery")
            {
                ca.lateJoinSync = true;
                clickLayer = ca.gameObject.layer;
                if (id == "Hood_Latch_Point") _hood = ca;
            }
            if (id.StartsWith("Door") && ClipDoor.NeedsClipDriver(ca) && ca.GetComponent<ClipDoor>() == null)
                ca.gameObject.AddComponent<ClipDoor>().Init(ca);
        }

        // 2) 프렁크 덮개
        var lid = FindActive(root, frunkLidName);
        if (lid != null && lid.GetComponent<VehicleHinge>() == null)
        {
            var rend = lid.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                // 차량 로컬에서 덮개의 뒤쪽 가장자리(+X) 중앙을 축으로, 좌우(Z)축 둘레로 앞쪽이 들리게 연다
                Bounds lb = LocalBounds(root, rend.bounds);
                Vector3 pivot = new Vector3(lb.max.x, lb.center.y, lb.center.z);
                EnsureCollider(lid, rend, clickLayer);
                _frunk = lid.gameObject.AddComponent<VehicleHinge>();
                _frunk.Init(root, "veh/frunk", "프렁크 덮개", pivot, Vector3.forward, frunkOpenAngle,
                    () => _hood == null || _hood.IsOpen, "후드를 먼저 여세요");
            }
        }

        // 3) 냉각수 탱크·뚜껑
        var res = root.gameObject.AddComponent<CoolantReservoir>();
        res.Build(root, clickLayer);

        // 4) 시동·계기판·라이트·경적
        var eng = root.gameObject.AddComponent<VehicleEngine>();
        eng.Build(root, clickLayer);

        ClickableAnimatorManager.RequestLateJoinState();
    }

    private void Update()
    {
        // 후드가 닫히면 프렁크 덮개도 닫는다(서버 또는 오프라인만 요청 → 중복 요청 방지)
        if (_frunk == null || _hood == null || Time.time < _nextHoodCheck) return;
        _nextHoodCheck = Time.time + 0.3f;
        if (_frunk.IsOpen && !_hood.IsOpen && (FishNet.InstanceFinder.IsServerStarted || !FishNet.InstanceFinder.IsClientStarted))
            _frunk.RequestClose();
    }

    public static Bounds LocalBounds(Transform root, Bounds world)
    {
        Vector3 c = world.center, e = world.extents;
        bool first = true; Bounds b = default;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            Vector3 l = root.InverseTransformPoint(corner);
            if (first) { b = new Bounds(l, Vector3.zero); first = false; } else b.Encapsulate(l);
        }
        return b;
    }

    /// <summary>덮개처럼 얇은 메시에 눌릴 콜라이더(없으면)를 붙인다. 레이어는 다른 문과 같게.</summary>
    public static void EnsureCollider(Transform t, Renderer rend, int layer)
    {
        t.gameObject.layer = layer;
        if (t.GetComponent<Collider>() != null) return;
        var mf = t.GetComponent<MeshFilter>();
        var box = t.gameObject.AddComponent<BoxCollider>();
        if (mf != null && mf.sharedMesh != null)
        {
            Bounds mb = mf.sharedMesh.bounds;
            Vector3 s = t.lossyScale;
            Vector3 size = mb.size;
            // 얇은 축은 최소 4cm(세계 길이)로 부풀려 레이가 놓치지 않게
            for (int i = 0; i < 3; i++)
            {
                float world = size[i] * Mathf.Abs(s[i]);
                if (world < 0.04f) size[i] = 0.04f / Mathf.Max(1e-4f, Mathf.Abs(s[i]));
            }
            box.center = mb.center; box.size = size;
        }
    }
}

public class VehicleRigBootstrap : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (FindFirstObjectByType<VehicleRigBootstrap>() != null) return;
        var go = new GameObject("[VehicleRigBootstrap]");
        DontDestroyOnLoad(go);
        go.AddComponent<VehicleRigBootstrap>();
    }

    private float _next;
    private bool _isVr;
    private readonly Dictionary<Collider, float> _pokeCooldown = new Dictionary<Collider, float>();

    private void Update()
    {
        if (Time.unscaledTime >= _next)
        {
            _next = Time.unscaledTime + 1f;
            // 플랫폼은 캐시하지 않고 1초마다 다시 본다(씬 로드 직후엔 XR 초기화 전이라 PC 로 잘못 보일 수 있다)
            _isVr = XRAirpotrSecurity.Utils.GetPlatformType() == XRAirpotrSecurity.PlatformType.VR;
            Scan();
        }
        if (_isVr) PokeUpdate();
    }

    private void Scan()
    {
        foreach (var pm in FindObjectsByType<VehiclePartsManager>(FindObjectsSortMode.None))
        {
            if (pm == null || pm.GetComponent<VehicleRig>() != null) continue;
            if (pm.transform.Find("Guide_Coolant_Reservoir") == null) continue;   // 차량 본체만(배터리 팩 등 제외)
            pm.gameObject.AddComponent<VehicleRig>();
        }
        foreach (var sg in FindObjectsByType<SyncGrab>(FindObjectsSortMode.None))
        {
            if (sg == null || sg.GetComponent<SmartKeyUse>() != null) continue;
            var ti = sg.GetComponent<TaskItem>();
            if (ti != null && ti.prefabId == "SmartKey") sg.gameObject.AddComponent<SmartKeyUse>();
        }
    }

    // ── VR: 손가락으로 찔러 누르기 ─────────────────────────────
    private void PokeUpdate()
    {
        foreach (var hand in LocalHands.All)
        {
            if (hand == null || hand.holdingObj != null) continue;
            Transform tip = LocalHands.IndexTip(hand);
            if (tip == null) continue;
            foreach (var col in Physics.OverlapSphere(tip.position, 0.015f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (col == null) continue;
                var clk = col.GetComponentInParent<IWorldClickable>();
                if (clk == null || !clk.VrPokeable) continue;
                if (_pokeCooldown.TryGetValue(col, out float until) && Time.unscaledTime < until) continue;
                _pokeCooldown[col] = Time.unscaledTime + 0.9f;
                clk.OnPCClick();
                EquipFx.Haptic(hand, 0.05f, 0.5f);
            }
        }
    }
}
