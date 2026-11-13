using Autohand;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using XRAirpotrSecurity;

// ============================================================
//  CreeperRide.cs
//
//  정비용 크리퍼(구루마). 바퀴 달린 누움판 — 누워서 차 밑으로 들어가 하부를 올려다보며 작업한다.
//  · 평소엔 바닥에 끌리는 장비(FloorDragProp)라 잡고 원하는 자리로 옮긴다
//  · 눕기/일어나기
//      PC     : 크리퍼 2m 안에서 R  (또는 크리퍼를 든 채 F)
//      모바일 : 크리퍼를 든 채 작업 버튼 → 누운 동안 작업 버튼 = 일어나기
//      VR     : 크리퍼 1.2m 안에서 A/X 버튼 (다시 누르면 일어나기)
//  · 누운 동안: 이동하면 크리퍼가 함께 굴러가고(서버가 위치를 맞춰 모두에게 보임), 이동 속도가 느려진다
//      PC·모바일: 카메라가 바닥 높이(머리 쪽)로 내려가 위를 올려다본다. 다른 사람에게는 아바타가 누운 모습으로 보인다
//      VR: 시점을 바닥 가까이로 내리고 시야를 위로 젖힌다(VrLieView, 실제로 서 있어도 차 밑을 올려다볼 수 있음)
// ============================================================
public class CreeperRide : NetworkBehaviour
{
    [Tooltip("누운 동안 이동 속도 상한(m/s)")] public float rideSpeed = 1.3f;
    [Tooltip("누운 동안 이동 속도 = 평소 속도 × 이 값 (바닥 가까이라 평소 속도면 너무 빨라 보였다)")] [Range(0.1f, 1f)] public float rideSpeedFactor = 0.4f;
    [Tooltip("VR: 누웠을 때 눈 높이(m)")] public float vrEyeHeight = 0.45f;
    [Tooltip("VR: 누웠을 때 시야를 위로 젖히는 각도(도). 눈 높이만 내리면 정면만 보여 차 밑이 안 보였다")] [Range(0f, 85f)] public float vrLookUp = 65f;

    private readonly SyncVar<NetworkObject> _rider = new SyncVar<NetworkObject>();
    private SyncGrab _sync;
    private static CreeperRide s_mine;   // 내가 타고 있는 크리퍼

    // 로컬 탑승 상태
    private bool _localRiding;
    private PcMobileAvatarController _pcAvatar;
    private FreeLookController _pcLook;
    private FreeLockControllerMobile _mobileLook;
    private float _savedSpeed;
    private float _savedPitch;
    private const float LyingLookUp = 80f;   // 누웠을 때 처음 올려다보는 각도(도) — 예전엔 앞만 봐서 차 밑이 안 보였다
    private AutoHandPlayer _vrPlayer;
    private float _savedHeightOffset;
    private float _savedVrSpeed;
    private float _nextPose;
    private PcMobileAvatarController _shownLying;   // 이 화면에서 눕혀 둔 아바타
    private bool _prevVrButton;

    public bool Ridden => _rider.Value != null;

    private void Awake() { _sync = GetComponent<SyncGrab>(); }

    private PlatformType Plat => Utils.GetPlatformType();

    // ══════════════════════════════════════════
    //  입력
    // ══════════════════════════════════════════

    private void Update()
    {
        UpdateRiderPose();
        if (Plat == PlatformType.VR) VrHeightGuard();
        if (!IsClientStarted) return;

        bool riderIsMe = _rider.Value != null && _rider.Value.IsOwner;
        if (_localRiding)
        {
            // 서버가 탑승을 반영하기 전(첫 몇 프레임)엔 탑승자가 아직 비어 있다 — 예전엔 여기서 바로 내려 버려
            // 시점은 그대로인데 서버 반영 뒤 아바타만 눕고, 로컬은 '안 탄 상태'라 일어날 수도 없었다
            if (riderIsMe) _riderSeen = true;
            else if (_riderSeen || Time.time - _rideStart > 2f) { EndLocal(); return; }
            if (riderIsMe) FollowMe();
            if (LeavePressed()) { LeaveServerRpc(); _leaveSentAt = Time.time; EndLocal(); }
            return;
        }

        // 서버에는 내가 타 있는데 로컬은 내린 상태(응답이 늦게 온 경우 등) → 다시 탄 상태로 맞춰 일어날 수 있게 한다
        if (riderIsMe && s_mine == null && Time.time - _leaveSentAt > 1.5f)
        {
            BeginLocal();
            _riderSeen = true;
            return;
        }

        if (s_mine != null) return;   // 다른 크리퍼에 타고 있음
        if (Ridden) return;
        if (RidePressed()) TryRide();
    }

    private bool HeldByMe => _sync != null && _sync.IsOwner && _sync.IsGrabbed && !_sync.IsHolstered;

    private bool RidePressed()
    {
        var plat = Plat;
        if (plat == PlatformType.PC)
        {
            if (HeldByMe && Input.GetKeyDown(KeyCode.F)) return true;
            if (Input.GetKeyDown(KeyCode.R) && NearMe(2f)) return true;
            if (NearMe(2f) && !HeldByMe) EquipFx.Notice("creeper_hint", "정비 크리퍼", "R 키를 누르면 누워서 차 밑으로 들어갈 수 있습니다", EquipFx.Info, 2f, 30f);
            return false;
        }
        if (plat == PlatformType.Mobile)
        {
            // 모바일: 크리퍼 2m 안에 가면 '크리퍼에 눕기' 버튼
            MobileBtn(NearMe(2f) && !Ridden, "크리퍼에 눕기");
            return ConsumeMobileClick();
        }
        // VR
        bool b = VrPrimaryPressed();
        bool down = b && !_prevVrButton;
        _prevVrButton = b;
        if (NearMe(1.2f))
        {
            EquipFx.Notice("creeper_hint_vr", "정비 크리퍼", "A(X) 버튼을 누르면 누워서 차 밑으로 들어갑니다", EquipFx.Info, 2f, 30f);
            return down;
        }
        return false;
    }

    private bool LeavePressed()
    {
        var plat = Plat;
        if (plat == PlatformType.PC) return Input.GetKeyDown(KeyCode.R) || (Input.GetKeyDown(KeyCode.F) && !HoldingAnything());
        if (plat == PlatformType.Mobile)
        {
            // 누운 동안 늘 '일어나기' 버튼(물건을 들고 있어도)
            MobileBtn(true, "일어나기");
            return ConsumeMobileClick() && Time.time - _rideStart > 0.5f;
        }
        bool b = VrPrimaryPressed();
        bool down = b && !_prevVrButton;
        _prevVrButton = b;
        return down;
    }

    // ── 모바일 눕기/일어나기 버튼(MobileQuickButtons) — 크리퍼가 여러 개라도 버튼은 하나, 마지막에 켠 크리퍼가 주인 ──
    private static CreeperRide s_btnOwner;
    private bool _mobClick;

    private void MobileBtn(bool show, string text)
    {
        if (show)
        {
            s_btnOwner = this;
            MobileQuickButtons.Set("creeper", true, text, MobileQuickButtons.SlotUpper, () => { if (s_btnOwner != null) s_btnOwner._mobClick = true; });
        }
        else if (s_btnOwner == this)
        {
            MobileQuickButtons.Set("creeper", false);
            s_btnOwner = null;
            _mobClick = false;
        }
    }

    private bool ConsumeMobileClick()
    {
        bool c = _mobClick && s_btnOwner == this;
        _mobClick = false;
        return c;
    }

    private static bool HoldingAnything()
    {
        foreach (var sg in FindObjectsByType<SyncGrab>(FindObjectsSortMode.None))
            if (sg != null && sg.IsOwner && sg.IsGrabbed && !sg.IsHolstered) return true;
        return false;
    }

    private static readonly System.Collections.Generic.List<UnityEngine.XR.InputDevice> s_devs = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
    private static bool VrPrimaryPressed()
    {
        s_devs.Clear();
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(UnityEngine.XR.InputDeviceCharacteristics.Controller, s_devs);
        foreach (var d in s_devs)
            if (d.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool v) && v) return true;
        return false;
    }

    private bool NearMe(float r)
    {
        var cam = Camera.main;
        if (cam == null) return false;
        Vector3 d = cam.transform.position - transform.position; d.y = 0f;
        return d.sqrMagnitude < r * r;
    }

    // ══════════════════════════════════════════
    //  눕기 / 일어나기 (로컬)
    // ══════════════════════════════════════════

    private float _rideStart;
    private bool _riderSeen;            // 서버가 나를 탑승자로 반영한 것을 한 번이라도 봤는지
    private float _leaveSentAt = -10f;

    private void TryRide()
    {
        var me = FindMyPlayer();
        if (me == null) return;
        if (HeldByMe) _sync.RequestRelease();
        RideServerRpc(me);
        BeginLocal();
    }

    private NetworkObject FindMyPlayer()
    {
        var conn = ClientManager != null ? ClientManager.Connection : null;
        if (conn == null) return null;
        foreach (var nob in conn.Objects)
        {
            if (nob == null) continue;
            if (nob.GetComponent<PcMobileAvatarController>() != null || nob.GetComponentInChildren<AutoHandPlayer>(true) != null || nob.GetComponent<BasePlayer>() != null) return nob;
        }
        return conn.FirstObject;
    }

    private void BeginLocal()
    {
        _localRiding = true;
        s_mine = this;
        _rideStart = Time.time;
        _riderSeen = false;
        var me = FindMyPlayer();
        if (Plat == PlatformType.VR)
        {
            _vrPlayer = FindVrPlayer();
            if (_vrPlayer != null)
            {
                _savedHeightOffset = _vrPlayer.heightOffset;
                if (s_vrBaseKnown && _vrPlayer == s_guardPlayer) _savedHeightOffset = s_vrBaseOffset - (_vrPlayer.crouching ? _vrPlayer.crouchHeight : 0f);   // 이미 틀어진 값을 '원래 값'으로 저장하지 않게
                float eye = _vrPlayer.headCamera != null ? _vrPlayer.headCamera.transform.position.y - transform.position.y : 1.6f;
                _vrPlayer.heightOffset = _savedHeightOffset + (vrEyeHeight - eye);
                _savedVrSpeed = _vrPlayer.maxMoveSpeed;
                _vrPlayer.maxMoveSpeed = Mathf.Min(rideSpeed, _savedVrSpeed * rideSpeedFactor);
                VrLieView.Begin(_vrPlayer, vrLookUp);
            }
            EquipFx.Notice("creeper_on", "크리퍼에 누웠습니다", "스틱으로 이동해 차 밑으로 들어가세요. A(X) 버튼 = 일어나기", EquipFx.Info, 3f, 0f);
        }
        else
        {
            _pcLook = me != null ? me.GetComponentInChildren<FreeLookController>() : null;
            _mobileLook = me != null ? me.GetComponentInChildren<FreeLockControllerMobile>() : null;
            if (_pcLook != null) { _savedSpeed = _pcLook.moveSpeed; _pcLook.moveSpeed = Mathf.Min(rideSpeed, _savedSpeed * rideSpeedFactor); _savedPitch = _pcLook.CurrentPitch; _pcLook.SetPitch(-LyingLookUp); _pcLook.LookLocked = true; }
            if (_mobileLook != null) { _savedSpeed = _mobileLook.moveSpeed; _mobileLook.moveSpeed = Mathf.Min(rideSpeed, _savedSpeed * rideSpeedFactor); _savedPitch = _mobileLook.CurrentPitch; _mobileLook.SetPitch(-LyingLookUp); _mobileLook.LookLocked = true; }
            EquipFx.Notice("creeper_on", "크리퍼에 누웠습니다", Plat == PlatformType.PC ? "이동 키로 차 밑으로 들어가세요. 누워 있는 동안 시선은 위로 고정됩니다. R = 일어나기" : "조이스틱으로 차 밑으로 들어가세요. '일어나기' 버튼으로 내려옵니다", EquipFx.Info, 3.5f, 0f);
        }
        WorldSfx.TorqueClick(transform.position);
    }

    private void EndLocal()
    {
        if (!_localRiding) return;
        _localRiding = false;
        if (s_mine == this) s_mine = null;
        if (_vrPlayer != null) { _vrPlayer.heightOffset = _savedHeightOffset; _vrPlayer.maxMoveSpeed = _savedVrSpeed; _vrPlayer = null; }
        if (Plat == PlatformType.VR) VrLieView.End();
        if (_pcLook != null) { _pcLook.moveSpeed = _savedSpeed; _pcLook.LookLocked = false; _pcLook.SetPitch(0f); _pcLook = null; }
        if (_mobileLook != null) { _mobileLook.moveSpeed = _savedSpeed; _mobileLook.LookLocked = false; _mobileLook.SetPitch(0f); _mobileLook = null; }
        // 서버 응답(탑승자 해제)을 기다리지 않고 내 아바타를 바로 세우고 높이 조절을 풀어 준다
        if (Plat != PlatformType.VR)
        {
            var me = FindMyPlayer();
            var av = me != null ? me.GetComponentInChildren<PcMobileAvatarController>() : null;
            if (av != null) av.ResetCrouch();   // SetLying(false) 는 다음 UpdateRiderPose 가 처리
        }
        if (Plat == PlatformType.Mobile) MobileBtn(false, null);
    }

    // ── VR 높이 안전장치 ──────────────────────────────────────
    // 누울 때 VR 눈높이(AutoHandPlayer.heightOffset)를 낮추고 시야를 젖힌다(VrLieView). 일어날 때 되돌리지 못한 경우
    // (크리퍼가 사라짐·연결 끊김·탑승 상태가 꼬임 등) 낮은 높이가 그대로 남으면 내 시점과 다른 사람 화면의 내 아바타 키가 계속 틀어진다.
    // → 처음 본 값(누운 적 없을 때)을 기준으로 기억해 두고, 어느 크리퍼에도 타고 있지 않은데 기준과 다르면 1초 안에 되돌린다.
    //   AutoHand 자체 웅크리기(crouching)로 낮춘 만큼은 정상으로 본다.
    private static AutoHandPlayer s_guardPlayer;
    private static float s_vrBaseOffset;
    private static bool s_vrBaseKnown;
    private static float s_nextGuard;

    private static void VrHeightGuard()
    {
        if (Time.unscaledTime < s_nextGuard) return;
        s_nextGuard = Time.unscaledTime + 1f;
        if (s_guardPlayer == null || !s_guardPlayer.isActiveAndEnabled) { s_guardPlayer = FindVrPlayer(); s_vrBaseKnown = false; }
        var p = s_guardPlayer;
        if (p == null) return;
        float crouch = p.crouching ? p.crouchHeight : 0f;
        if (s_mine != null) return;                       // 지금 누워 있음 — 낮춘 값이 정상
        if (!s_vrBaseKnown) { s_vrBaseOffset = p.heightOffset + crouch; s_vrBaseKnown = true; return; }
        float want = s_vrBaseOffset - crouch;
        if (Mathf.Abs(p.heightOffset - want) > 0.01f)
        {
            Debug.Log($"[Creeper] VR 높이 안전장치: 누워 있지 않은데 눈높이 보정이 {p.heightOffset:0.00} → 기준 {want:0.00} 으로 되돌림");
            p.heightOffset = want;
        }
        VrLieView.End();   // 젖힌 시야가 남아 있으면 세운다(없으면 아무 일 없음)
    }

    private static AutoHandPlayer FindVrPlayer()
    {
        foreach (var p in FindObjectsByType<AutoHandPlayer>(FindObjectsSortMode.None))
            if (p != null && p.isActiveAndEnabled && p.headCamera != null && p.headCamera.isActiveAndEnabled) return p;
        return null;
    }

    /// <summary>내가 누운 위치로 크리퍼를 옮겨 달라고 서버에 보낸다(초당 15번).</summary>
    private void FollowMe()
    {
        if (Time.unscaledTime < _nextPose) return;
        _nextPose = Time.unscaledTime + 1f / 15f;
        Vector3 pos; float yaw;
        var me = _rider.Value;
        if (Plat == PlatformType.VR)
        {
            var cam = Camera.main;
            if (cam == null) return;
            pos = cam.transform.position;
            Vector3 f = cam.transform.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = cam.transform.up;
            yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            pos -= Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 0.55f;   // 머리가 크리퍼 앞쪽에 오게
        }
        else
        {
            if (me == null) return;
            pos = me.transform.position;
            yaw = me.transform.eulerAngles.y;
        }
        pos.y = transform.position.y;
        PoseServerRpc(pos, yaw);
    }

    // ══════════════════════════════════════════
    //  서버
    // ══════════════════════════════════════════

    [ServerRpc(RequireOwnership = false)]
    private void RideServerRpc(NetworkObject player, NetworkConnection sender = null)
    {
        if (player == null || player.Owner != sender) return;
        if (_rider.Value != null && _rider.Value.IsSpawned) return;
        if (_sync != null && _sync.IsGrabbed && Owner != sender) return;   // 남이 끌고 있는 중
        _rider.Value = player;
    }

    [ServerRpc(RequireOwnership = false)]
    private void LeaveServerRpc(NetworkConnection sender = null)
    {
        if (_rider.Value != null && _rider.Value.Owner == sender) _rider.Value = null;
    }

    [ServerRpc(RequireOwnership = false)]
    private void PoseServerRpc(Vector3 pos, float yaw, NetworkConnection sender = null)
    {
        if (_rider.Value == null || _rider.Value.Owner != sender) return;
        if ((pos - transform.position).sqrMagnitude > 4f) return;   // 순간이동 방지
        transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
    }

    // ══════════════════════════════════════════
    //  모든 화면: 누운 아바타
    // ══════════════════════════════════════════

    private void UpdateRiderPose()
    {
        if (IsServerStarted && _rider.Value != null && !_rider.Value.IsSpawned) _rider.Value = null;

        UpdateVrLying();
        var want = _rider.Value != null ? _rider.Value.GetComponentInChildren<PcMobileAvatarController>() : null;
        if (want != null && want.IsOwner && !_localRiding) want = null;   // 내가 일어났으면 서버 반영을 기다리지 않고 바로 선다
        if (want == _shownLying) return;
        if (_shownLying != null) _shownLying.SetLying(false);
        _shownLying = want;
        if (_shownLying != null) _shownLying.SetLying(true);
    }

    // VR 아바타: 탄 사람의 VRIK(원격 아바타 / 본인 시점 몸)에 VrLyingPose 를 붙여 눕힌다
    private NetworkObject _vrShownFor;
    private readonly System.Collections.Generic.List<VrLyingPose> _vrPoses = new System.Collections.Generic.List<VrLyingPose>();

    private void UpdateVrLying()
    {
        var rider = _rider.Value != null && _rider.Value.IsSpawned ? _rider.Value : null;
        if (rider == _vrShownFor) return;
        ClearVrLying();
        _vrShownFor = rider;
        if (rider == null || rider.GetComponentInChildren<PcMobileAvatarController>() != null) return;   // PC·모바일 아바타는 SetLying 이 처리

        var hidden = new System.Collections.Generic.HashSet<RootMotion.FinalIK.VRIK>();
        foreach (var ah in rider.GetComponentsInChildren<AutoHandVRIK>(true)) if (ah.invisibleIK != null) hidden.Add(ah.invisibleIK);
        foreach (var ik in rider.GetComponentsInChildren<RootMotion.FinalIK.VRIK>(true))
        {
            if (ik == null || hidden.Contains(ik)) continue;   // AutoHand 의 숨은 IK 복사본(손 물리용)은 그대로
            var p = ik.GetComponent<VrLyingPose>();
            if (p == null) { p = ik.gameObject.AddComponent<VrLyingPose>(); p.enabled = false; }
            p.creeper = transform;
            p.enabled = true;
            _vrPoses.Add(p);
        }
    }

    private void ClearVrLying()
    {
        foreach (var p in _vrPoses) if (p != null) p.enabled = false;
        _vrPoses.Clear();
        _vrShownFor = null;
    }

    private void OnDisable()
    {
        MobileBtn(false, null);
        ClearVrLying();
        if (_shownLying != null) { _shownLying.SetLying(false); _shownLying = null; }
        EndLocal();
    }
}
