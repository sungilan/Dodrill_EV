using Autohand;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using XRAirpotrSecurity;

// ============================================================
//  EquipmentBase.cs
//
//  손에 들고 '눌러서 쓰는' 정비 장비의 공통 바탕(스프레이·소화기·작업등·에어 임팩렌치·수공구).
//  · 잡기·놓기·위치 동기화는 같은 오브젝트의 SyncGrab 이 맡는다(다른 장비와 같은 흐름)
//  · 누르기 입력: VR 은 잡은 손의 트리거(Grabbable.onSqueeze), PC 는 F 키, 모바일은 작업 버튼
//  · '쓰는 중' 상태(Using)와 쓰는 방향(UseDirLocal)은 서버를 거쳐 모두에게 전파된다 → 다른 사람 화면에도 분사·회전이 보인다
//  · 손에서 놓으면 서버가 Using 을 끈다
// ============================================================
[RequireComponent(typeof(SyncGrab))]
public abstract class EquipmentBase : NetworkBehaviour
{
    protected SyncGrab Sync;
    protected Grabbable Grab;
    protected Rigidbody Rb;

    private readonly SyncVar<bool> _using = new SyncVar<bool>();
    private readonly SyncVar<Vector3> _useDirLocal = new SyncVar<Vector3>(Vector3.forward);

    /// <summary>지금 쓰는 중(모두에게 같은 값).</summary>
    public bool Using => _using.Value;
    /// <summary>쓰는 방향(물체 로컬). 스프레이 분사 방향 등.</summary>
    public Vector3 UseDirLocal => _useDirLocal.Value;

    /// <summary>내 입력이 눌려 있는가(이 클라이언트에서 들고 있을 때만).</summary>
    protected bool LocalInput { get; private set; }

    private bool _vrSqueeze;
    private Hand _squeezeHand;
    private float _nextSend;
    private PlatformType? _plat;
    protected PlatformType Platform => _plat ??= Utils.GetPlatformType();
    protected bool IsVR => Platform == PlatformType.VR;

    protected virtual void Awake()
    {
        Sync = GetComponent<SyncGrab>();
        Grab = GetComponent<Grabbable>();
        Rb = GetComponent<Rigidbody>();
        if (Grab != null)
        {
            Grab.onSqueeze.AddListener(OnSqueeze);
            Grab.onUnsqueeze.AddListener(OnUnsqueeze);
            Grab.onRelease.AddListener(OnHandRelease);
        }
    }

    protected virtual void OnDestroy()
    {
        if (Grab != null)
        {
            Grab.onSqueeze.RemoveListener(OnSqueeze);
            Grab.onUnsqueeze.RemoveListener(OnUnsqueeze);
            Grab.onRelease.RemoveListener(OnHandRelease);
        }
        if (_mobileShown) MobileWorkButton.SetVisible(false);
    }

    private void OnSqueeze(Hand h, Grabbable g) { _vrSqueeze = true; _squeezeHand = h; if (IsLocalHeld) OnPressDown(h); }
    private void OnUnsqueeze(Hand h, Grabbable g) { if (_squeezeHand == null || _squeezeHand == h) { _vrSqueeze = false; _squeezeHand = null; } }
    private void OnHandRelease(Hand h, Grabbable g) { if (g == null || g.GetHeldBy().Count == 0 || h == _squeezeHand) { _vrSqueeze = false; _squeezeHand = null; } }

    /// <summary>이 클라이언트가 손(또는 PC 들기)으로 들고 있다.</summary>
    public bool IsLocalHeld => Sync != null && Sync.IsOwner && Sync.IsGrabbed && !Sync.IsHolstered;

    /// <summary>지금 잡고 있는 VR 손 수.</summary>
    public int HandCount => Grab != null ? Grab.GetHeldBy().Count : 0;

    /// <summary>트리거를 누른 손(진동용). PC 면 null.</summary>
    protected Hand PressHand => _squeezeHand;

    /// <summary>누르는 순간(토글형 장비용). PC·모바일은 h == null.</summary>
    protected virtual void OnPressDown(Hand h) { }

    /// <summary>입력이 눌려 있을 때 실제로 '쓰는 중'으로 볼지(두 손 필수 등). 기본: 눌려 있으면 사용.</summary>
    protected virtual bool WantUsing(bool input) => input;

    /// <summary>누르기 시작할 때 보낼 쓰는 방향(물체 로컬). 기본: 앞쪽.</summary>
    protected virtual Vector3 ComputeUseDirLocal() => Vector3.forward;

    private bool _mobileShown;

    protected virtual void Update()
    {
        bool held = IsLocalHeld;
        bool input = false;
        if (held)
        {
            if (Platform == PlatformType.PC) input = Input.GetKey(KeyCode.F);
            else if (Platform == PlatformType.Mobile) input = MobileWorkButton.IsHeld;
            else input = _vrSqueeze;
        }
        else { _vrSqueeze = false; _squeezeHand = null; }

        if (Platform == PlatformType.Mobile && held != _mobileShown) { _mobileShown = held; MobileWorkButton.SetVisible(held); }

        bool prev = LocalInput;
        LocalInput = input;
        if (input && !prev && !IsVR) OnPressDown(null);

        if (held)
        {
            bool want = WantUsing(input);
            if (want != _using.Value && Time.unscaledTime >= _nextSend)
            {
                _nextSend = Time.unscaledTime + 0.08f;
                SetUsingServerRpc(want, want ? ComputeUseDirLocal() : _useDirLocal.Value);
            }
        }

        // 서버: 아무도 안 들고 있으면 끈다
        if (IsServerStarted && _using.Value && (Sync == null || !Sync.IsGrabbed || Sync.IsHolstered)) _using.Value = false;
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetUsingServerRpc(bool on, Vector3 dirLocal, NetworkConnection sender = null)
    {
        // 들고 있는 사람만
        if (on && (Sync == null || !Sync.IsGrabbed || Owner != sender)) return;
        _using.Value = on;
        if (dirLocal.sqrMagnitude > 1e-6f) _useDirLocal.Value = dirLocal.normalized;
    }

    /// <summary>잡은 손의 '검지가 가리키는' 방향(세계). 없으면 카메라 앞.</summary>
    protected Vector3 PointingDir()
    {
        var h = _squeezeHand;
        if (h == null && Grab != null && Grab.GetHeldBy().Count > 0) h = Grab.GetHeldBy()[0];
        if (h != null) return h.transform.forward;
        var cam = Camera.main;
        return cam != null ? cam.transform.forward : transform.forward;
    }

    /// <summary>세계 방향을 이 물체의 가장 가까운 로컬 주축으로 맞춘다(모델마다 축이 달라 분사구가 엉뚱한 쪽을 보지 않게).</summary>
    protected Vector3 SnapToLocalAxis(Vector3 worldDir, Vector3 excludeLocal)
    {
        Vector3 l = transform.InverseTransformDirection(worldDir);
        Vector3 best = Vector3.forward; float bd = -2f;
        foreach (var ax in new[] { Vector3.up, Vector3.down, Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
        {
            if (excludeLocal != Vector3.zero && Mathf.Abs(Vector3.Dot(ax, excludeLocal)) > 0.9f) continue;
            float d = Vector3.Dot(ax, l.normalized);
            if (d > bd) { bd = d; best = ax; }
        }
        return best;
    }

    /// <summary>잡은 모든 손에 진동.</summary>
    protected void HapticAll(float duration, float amp)
    {
        if (Grab == null) return;
        foreach (var h in Grab.GetHeldBy()) EquipFx.Haptic(h, duration, amp);
    }
}
