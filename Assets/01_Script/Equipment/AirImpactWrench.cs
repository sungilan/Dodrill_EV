using System.Collections.Generic;
using Autohand;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using XRAirpotrSecurity;

// ============================================================
//  AirImpactWrench.cs
//
//  1/2" 에어 임팩렌치(대형). 무거워서 두 손(뒤 권총 손잡이 + 앞 보조 손잡이)으로 잡아야 제대로 들리고 작동한다.
//  · 한 손으로만 잡으면 잡은 높이에서 거의 들리지 않는다(바닥에 끌림) + 안내
//  · 앤빌에 소켓을 끼워야 쓸 수 있다 — 거치대(SocketRack)의 소켓에 앤빌을 갖다 대면 끼워진다
//      10mm : 볼트 머리보다 작아 헛돈다(진행 없음)
//      17mm : 소형 승용차 하부·배터리 체결 볼트 규격 → 정상 작동
//      27mm : 과대 소켓 + 과토크 → 볼트 머리 파손 연출(불꽃·파편·깨지는 소리), 진행 없음
//  · 에어 호스 연결 + 컴프레서 밸브 열림이어야 돈다(AirCompressor)
//  · VR: 에어 입구(손잡이 아래)를 컴프레서 호스 끝에 갖다 대면 연결. PC·모바일: 들고 호스 끝 2m 안으로 가면 자동 연결
//  · PC: Q 로 소켓 바꾸기(거치대 1.5m 안), F 로 작동 — 화면 가운데로 볼트를 겨누면 그 볼트를 돌린다
//  · 휠 너트(FreeWorkBolt): 해머 타격 진동·소리 → '툭' 풀림 → 빠른 헛돌기 / 조일 때는 반대 순서(손맛)
// ============================================================
public class AirImpactWrench : EquipmentBase
{
    protected override string MobileActionName => "볼트 돌리기";

    private static readonly List<AirImpactWrench> s_all = new List<AirImpactWrench>();

    [Tooltip("소켓이 꽂히는 사각 축(앞쪽 = 로컬 +Z)")] public Transform anvil;
    [Tooltip("에어 입구(호스가 붙는 곳, 앞쪽 = 호스가 나가는 방향의 반대)")] public Transform inlet;
    [Tooltip("소켓 모양(1=10mm, 2=17mm, 3=27mm 순서)")] public GameObject[] socketVisuals = new GameObject[3];
    [Tooltip("소켓 길이(m) — 소켓 끝 = 볼트에 닿는 곳")] public float[] socketLengths = { 0.04f, 0.055f, 0.08f };
    [Tooltip("17mm 정상 작동 시 풀림 속도 배율(기존 드릴 대비)")] public float speed = 1.5f;

    private readonly SyncVar<int> _socket = new SyncVar<int>();
    public int Socket => _socket.Value;

    private AudioSource _rattle;
    private float _spin;
    private int _prevHands;
    private float _grabY;
    private float _nextWorkFx;
    private Bolt _lastBroken;
    private float _lastBrokenAt;

    protected override void Awake()
    {
        base.Awake();
        if (!s_all.Contains(this)) s_all.Add(this);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        s_all.Remove(this);
    }

    public static bool AnyHasSocket(int size)
    {
        foreach (var w in s_all) if (w != null && w._socket.Value == size) return true;
        return false;
    }

    private void Start()
    {
        _rattle = EquipFx.LoopSource(gameObject, EquipFx.Rattle, 16f);
        ApplySocketVisual();
    }

    // ── 상태 ─────────────────────────────

    private AirCompressor Air => AirCompressor.ConnectedTo(NetworkObject);
    private bool AirReady { get { var a = Air; return a != null && a.ValveOpen; } }
    // VR: 예전엔 두 손으로 잡아야 들리고(한 손이면 4cm 이상 안 들림) 작동했다 → 들고 소켓·호스를 끼우려면 손이 모자라 떨어뜨리기 일쑤.
    // 기본은 한 손으로 들고 작동. 무거운 공구 느낌이 필요하면 requireTwoHands 를 켠다.
    [UnityEngine.Tooltip("VR: 두 손으로 잡아야 들고 작동(끄면 한 손 사용)")] public bool requireTwoHands = false;
    private bool TwoHands => !IsVR || !requireTwoHands || HandCount >= 2;

    private Vector3 SocketTip
    {
        get
        {
            var a = anvil != null ? anvil : transform;
            int s = _socket.Value;
            float len = s >= 1 && s <= socketLengths.Length ? socketLengths[s - 1] : 0.01f;
            return a.position + a.forward * len;
        }
    }

    protected override bool WantUsing(bool input) => input && TwoHands && AirReady && _socket.Value != 0;

    protected override void OnPressDown(Hand h)
    {
        // 누른 순간 왜 안 도는지 알려 준다
        if (!TwoHands) EquipFx.Notice("aiw_hands", "두 손으로 잡으세요", "에어 임팩렌치는 무거워서 앞 손잡이까지 두 손으로 잡아야 작동합니다", EquipFx.Warn, 2.5f, 4f);
        else if (_socket.Value == 0) EquipFx.Notice("aiw_socket", "소켓이 없습니다", IsVR ? "소켓 거치대의 소켓에 앤빌(앞쪽 사각 축)을 꽂으세요" : Platform == PlatformType.Mobile ? "소켓 거치대 근처에서 '소켓 끼우기' 버튼을 누르세요" : "소켓 거치대 근처에서 Q 로 소켓을 끼우세요", EquipFx.Warn, 2.5f, 4f);
        else if (Air == null) EquipFx.Notice("aiw_hose", "에어 호스 미연결", "손잡이 아래 에어 입구에 컴프레서 호스를 연결하세요", EquipFx.Warn, 2.5f, 4f);
        else if (!Air.ValveOpen) EquipFx.Notice("aiw_valve", "공기가 안 나옵니다", "컴프레서의 밸브를 여세요", EquipFx.Warn, 2.5f, 4f);
    }

    // ── 매 프레임 ─────────────────────────────

    protected override void Update()
    {
        base.Update();

        // 모두의 화면: 회전·소리·소켓 모양
        bool run = Using;
        if (run) _spin += Time.deltaTime * 1800f;
        ApplySocketVisual();
        EquipFx.Drive(_rattle, run ? 0.75f : 0f, 1f);

        if (!IsLocalHeld) { HideMobileSocketButton(); _prevHands = HandCount; return; }

        if (run && IsVR && Time.frameCount % 3 == 0) HapticAll(0.04f, 0.55f);

        TrySocketChange();
        TryHoseConnect();
        if (run) Work();
        _prevHands = HandCount;
    }

    private void LateUpdate()
    {
        if (!IsLocalHeld || !IsVR || !requireTwoHands) return;
        int hands = HandCount;
        if (hands == 1 && _prevHands == 0) _grabY = transform.position.y;
        if (hands == 2) _grabY = transform.position.y;
        if (hands != 1) return;

        // 한 손: 무거워서 잡은 높이에서 4cm 이상 들리지 않는다
        float maxY = _grabY + 0.04f;
        if (transform.position.y > maxY)
        {
            var p = transform.position; p.y = maxY;
            transform.position = p;
            if (Rb != null && !Rb.isKinematic && Rb.linearVelocity.y > 0f) { var v = Rb.linearVelocity; v.y = 0f; Rb.linearVelocity = v; }
            EquipFx.Notice("aiw_heavy", "너무 무겁습니다", "앞 보조 손잡이까지 두 손으로 잡아야 들 수 있습니다", EquipFx.Warn, 2.5f, 6f);
        }
    }

    // ── 소켓 ─────────────────────────────

    private void TrySocketChange()
    {
        if (IsVR)
        {
            Vector3 tip = (anvil != null ? anvil : transform).position;
            foreach (var r in SocketRack.All)
            {
                int size = r != null ? r.SizeNear(tip, 0.06f) : 0;
                if (size == 0 || size == _socket.Value || AnyHasSocket(size)) continue;
                SetSocket(size);
                break;
            }
            return;
        }
        bool nearRack = false;
        foreach (var r in SocketRack.All) if (r != null && (r.transform.position - transform.position).sqrMagnitude < 1.5f * 1.5f) nearRack = true;
        if (Platform == PlatformType.Mobile)
        {
            // 모바일: Q 키가 없으므로 거치대 가까이에서 '소켓 바꾸기' 버튼을 띄운다
            _mobileSocketBtn = true;
            string label = _socket.Value == 0 ? "소켓 끼우기" : $"소켓 바꾸기\n<size=70%>지금 {SocketRack.SizeName(_socket.Value)}</size>";
            MobileQuickButtons.Set("aiw_socket", nearRack, label, MobileQuickButtons.SlotLeft, CycleSocket);
            return;
        }
        if (Platform == PlatformType.PC && Input.GetKeyDown(KeyCode.Q))
        {
            if (!nearRack) { EquipFx.Notice("aiw_rack", "소켓 거치대가 멀리 있습니다", "거치대 가까이에서 Q 로 소켓을 바꾸세요", EquipFx.Info, 2f, 3f); return; }
            CycleSocket();
        }
    }

    private bool _mobileSocketBtn;

    /// <summary>다음 크기 소켓으로 바꾼다(다른 렌치가 쓰는 크기는 건너뜀).</summary>
    private void CycleSocket()
    {
        if (Sync == null || !Sync.IsOwner || !Sync.IsGrabbed) return;
        int next = _socket.Value;
        for (int i = 0; i < 3; i++) { next = next % 3 + 1; if (!AnyHasSocket(next) || next == _socket.Value) break; }
        SetSocket(next);
    }

    private void HideMobileSocketButton()
    {
        if (!_mobileSocketBtn) return;
        _mobileSocketBtn = false;
        MobileQuickButtons.Set("aiw_socket", false);
    }

    private void SetSocket(int size)
    {
        SetSocketServerRpc(size);
        AudioSource.PlayClipAtPoint(EquipFx.Click, transform.position, 0.8f);
        HapticAll(0.05f, 0.6f);
        string msg = size == 2 ? "소형 승용차 하부 볼트 규격입니다" : size == 1 ? "볼트 머리보다 작은 소켓입니다" : "대형 차량용 소켓입니다";
        EquipFx.Notice("aiw_set", $"{SocketRack.SizeName(size)} 소켓 장착", msg, EquipFx.Info, 2f, 0.5f);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetSocketServerRpc(int size, FishNet.Connection.NetworkConnection sender = null)
    {
        if (Sync == null || !Sync.IsGrabbed || Owner != sender) return;
        if (size < 0 || size > 3) return;
        _socket.Value = size;
    }

    private void ApplySocketVisual()
    {
        if (socketVisuals == null) return;
        var a = anvil != null ? anvil : transform;
        for (int i = 0; i < socketVisuals.Length; i++)
        {
            var v = socketVisuals[i];
            if (v == null) continue;
            bool on = _socket.Value == i + 1;
            if (v.activeSelf != on)
            {
                v.SetActive(on);
                // 소켓 모양은 눈에 보이기만 하면 된다 — 콜라이더가 켜지면 소켓이 끼워질 때 플레이어 몸을 밀어 몸이 올라갔다
                if (on) foreach (var c in v.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            }
            if (on) v.transform.localRotation = Quaternion.Euler(0f, 0f, _spin);
        }
    }

    // ── 에어 호스 ─────────────────────────────

    private void TryHoseConnect()
    {
        if (Air != null || inlet == null) return;
        foreach (var c in AirCompressor.All)
        {
            if (c == null || c.Connected != null) continue;
            float d = (c.FreeEnd - inlet.position).magnitude;
            if (d < (IsVR ? 0.15f : 2f))
            {
                c.ConnectServerRpc(NetworkObject);
                AudioSource.PlayClipAtPoint(EquipFx.Click, inlet.position, 0.9f);
                HapticAll(0.08f, 0.7f);
                EquipFx.Notice("aiw_hosed", "에어 호스 연결", c.ValveOpen ? "공기가 공급됩니다" : "컴프레서 밸브를 열면 작동합니다", EquipFx.Info, 2f, 2f);
                return;
            }
        }
    }

    // ── 볼트 작업 ─────────────────────────────

    private void Work()
    {
        Vector3 tip = SocketTip;
        var bolt = BoltWork.Nearest(tip, 0.06f);
        if (bolt == null && !IsVR) bolt = AimedBolt();   // PC·모바일: 화면 가운데로 겨눈 볼트
        if (bolt == null || bolt.isloosened) return;
        if (!IsVR) tip = bolt.transform.position;
        int s = _socket.Value;

        if (s == 1)
        {
            if (Time.time >= _nextWorkFx)
            {
                _nextWorkFx = Time.time + 0.45f;
                AudioSource.PlayClipAtPoint(EquipFx.Slip, tip, 0.8f);
            }
            EquipFx.Notice("aiw_small", "소켓이 헛돕니다", "10mm 는 볼트 머리보다 작습니다 — 17mm 소켓으로 바꾸세요", EquipFx.Warn, 2.5f, 4f);
            return;
        }
        if (s == 3)
        {
            if (_lastBroken == bolt && Time.time - _lastBrokenAt < 2.5f) return;
            _lastBroken = bolt; _lastBrokenAt = Time.time;
            BreakFxServerRpc(bolt.transform.position, (tip - bolt.transform.position).normalized);
            HapticAll(0.25f, 1f);
            EquipFx.Notice("aiw_big", "과대 소켓·과토크 — 볼트 머리 파손!", "소형 승용차에는 17mm 소켓을 쓰세요. 큰 소켓은 볼트·부품을 망가뜨립니다", EquipFx.Danger, 3.5f, 3f);
            return;
        }

        if (!BoltWork.IsCurrentStep(bolt))
        {
            EquipFx.Notice("aiw_step", "지금 단계의 볼트가 아닙니다", "안내에 표시된 볼트를 작업하세요", EquipFx.Info, 2f, 5f);
            return;
        }
        if (BoltWork.IsSeized(bolt) && !BoltWork.IsLubed(bolt))
            EquipFx.Notice("seized", "녹슬어 뻑뻑한 볼트", "윤활 방청제를 뿌리면 잘 풀립니다", EquipFx.Warn, 3f, 10f);

        float t = Mathf.Max(0.1f, bolt.timeToComplete);
        float k = 1f;
        var free = bolt.GetComponent<FreeWorkBolt>();
        if (free != null) k = ImpactFeel(bolt, free);
        bolt.InteractWithTool(1f / t * speed * k * BoltWork.SpeedFactor(bolt) * Time.deltaTime, (anvil != null ? anvil : transform).forward);
        if (free != null)
        {
            string act = bolt.isAssembleMode ? "휠 너트 조이는 중" : "휠 너트 푸는 중";
            InteractionProgressBarUI.Instance?.ShowBoltProgress(bolt.Progress, act, "휠 너트", IsVR ? "트리거를 계속 당기세요" : Platform == PlatformType.PC ? "F 키를 계속 누르세요" : "작업 버튼을 계속 누르세요");
        }
    }

    // ── 휠 너트 손맛 ─────────────────────────────
    // 실제 에어 임팩렌치: 꽉 조여진 너트는 '타다다닥' 해머가 때리다가 '툭' 하고 풀린 뒤 빠르게 헛돌며 빠져나온다.
    // 조일 때는 반대로 빠르게 돌아 들어가다 마지막에 '타다다닥' 때리며 조여진다.
    private float _nextHammer;
    private bool _hammerAlt;
    private Bolt _feelBolt;
    private bool _brokeFree;

    private float ImpactFeel(Bolt bolt, FreeWorkBolt free)
    {
        if (_feelBolt != bolt) { _feelBolt = bolt; _brokeFree = bolt.isAssembleMode ? false : bolt.Progress >= free.breakAway; }
        float p = bolt.Progress;
        bool hammer = bolt.isAssembleMode ? p >= 0.75f : p < free.breakAway;

        if (!bolt.isAssembleMode && !_brokeFree && p >= free.breakAway)
        {
            // '툭' — 풀리는 순간 큰 반동
            _brokeFree = true;
            HapticAll(0.14f, 1f);
            AudioSource.PlayClipAtPoint(EquipFx.Crack, bolt.transform.position, 0.35f);
            EquipFx.Notice("lug_free", "툭! 너트가 풀렸습니다", "이제 빠르게 돌아 빠져나옵니다", EquipFx.Info, 1.2f, 20f);
        }

        if (hammer && Time.time >= _nextHammer)
        {
            // 해머 타격: 초당 약 16번, 강약 번갈아
            _nextHammer = Time.time + 0.06f;
            _hammerAlt = !_hammerAlt;
            if (IsVR) HapticAll(0.035f, _hammerAlt ? 1f : 0.6f);
            AudioSource.PlayClipAtPoint(EquipFx.Ratchet, bolt.transform.position, _hammerAlt ? 0.9f : 0.55f);
        }
        if (bolt.isAssembleMode && p >= 0.97f)
            EquipFx.Notice("lug_tight", "체결 완료", "대각선 순서(별 모양)로 조이면 휠이 고르게 붙습니다", EquipFx.Info, 2f, 20f);

        return hammer ? 0.35f : 2.2f;
    }

    /// <summary>PC·모바일: 카메라 앞 2.5m, 화면 가운데에서 가장 가까운 볼트.</summary>
    private Bolt AimedBolt()
    {
        var cam = Camera.main;
        if (cam == null) return null;
        BoltWork.InCone(cam.transform.position, cam.transform.forward, 2.5f, 7f, s_cone);
        Bolt best = null; float bd = -1f;
        foreach (var b in s_cone)
        {
            if (b == null || b.isloosened || b.isBroken) continue;
            float d = Vector3.Dot((b.transform.position - cam.transform.position).normalized, cam.transform.forward);
            if (d > bd) { bd = d; best = b; }
        }
        return best;
    }
    private static readonly List<Bolt> s_cone = new List<Bolt>();

    [ServerRpc(RequireOwnership = false)]
    private void BreakFxServerRpc(Vector3 pos, Vector3 normal) => BreakFxObserversRpc(pos, normal);

    [ObserversRpc]
    private void BreakFxObserversRpc(Vector3 pos, Vector3 normal)
    {
        EquipFx.SparkBurst(pos, normal, 70);
        AudioSource.PlayClipAtPoint(EquipFx.Crack, pos, 1f);
        FlashRed(pos);
    }

    private static void FlashRed(Vector3 pos)
    {
        // 볼트 머리가 순간 달아오른 듯 붉게 번쩍(작은 빛)
        var go = new GameObject("[BreakFlash]");
        go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(1f, 0.25f, 0.05f); l.range = 0.8f; l.intensity = 6f;
        Destroy(go, 0.25f);
    }
}
