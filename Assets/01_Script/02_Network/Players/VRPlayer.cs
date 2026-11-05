using FishNet.Object;
using FishNet.Transporting;
using RootMotion.FinalIK;
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public enum FingerName { Thumb, Index, Middle, Ring, Pinky }

[Serializable]
public enum FingerBone { Metacarpal, Proximal, Intermediate, Distal, Tip }

[Serializable]
public class FingerData
{
    public FingerName fingerName;
    [Header("Apply Transforms (Avatar)")]
    public List<Transform> applyBones = new List<Transform>(5);
    [Header("Tracking Transforms (Input)")]
    public List<Transform> trackingBones = new List<Transform>(5);
}

[Serializable]
public class HandData
{
    [Header("Apply Transform")]
    public Transform applyWrist;
    [Header("Tracking Transform")]
    public Transform trackingWrist;
    public List<FingerData> fingers = new List<FingerData>();
}

[Serializable]
public class VRTrackingSetup
{
    [Header("Apply Head")]
    public Transform applyHead;
    [Header("Tracking Head")]
    public Transform trackingHead;
    public HandData leftHand = new HandData();
    public HandData rightHand = new HandData();
}

public class VRPlayer : BasePlayer
{ 
    [Header("VR Tracking Setup")]
    public VRTrackingSetup trackingSetup;
    [Header("Local Tracking Object (Owner Only)")]
    public GameObject[] localTrackingInfos;
    public GameObject applyAvatar;
    public Transform anchor;

    [Header("Sync Interval")]
    [Range(0.01f, 0.1f)]
    public float syncInterval = 0.05f;
    private float syncTimer = 0f;

    [Header("Smoothing")]
    [Range(0.01f, 10f)]
    public float positionLerp = 0.2f;
    [Range(0.01f, 10f)]
    public float rotationLerp = 0.2f;

    private Vector3 targetHeadPos;
    private Quaternion targetHeadRot;
    private Vector3 targetLeftWristPos;
    private Quaternion targetLeftWristRot;
    private Vector3 targetRightWristPos;
    private Quaternion targetRightWristRot;
    private Quaternion[] targetLeftFingerRots;
    private Quaternion[] targetRightFingerRots;

    // 손가락은 관절별 로컬 회전만 받는다 (A-2). 위치는 아바타 뼈 길이가 결정하므로 건드리지 않는다.
    [Header("손가락 보간")]
    [SerializeField] private float fingerFollowSpeed = 18f;
    private Quaternion[] _leftFingerPose;
    private Quaternion[] _rightFingerPose;

    // 머리·손목 보간 계수(positionLerp/rotationLerp)는 프레임당 고정값이라 30fps 모바일과 72~90Hz Quest 의 수렴 속도가 2~3배 달랐다(A-5).
    // 인스펙터 값을 '60fps 기준 프레임당 계수'로 보고 실제 dt 에 맞게 환산한다. (손가락은 fingerFollowSpeed 로 이미 시간 기반)
    private static float Blend(float perFrame60)
    {
        return Mathf.Clamp01(1f - Mathf.Pow(1f - Mathf.Clamp01(perFrame60), Time.deltaTime * 60f));
    }

    public override void OnStartClient()
    {
        anchor = GameObject.FindGameObjectWithTag("Anchor")?.transform;

        // 다른 사람 화면에서 이 아바타가 든 물체가 손에 붙어 보이게 한다
        if (GetComponent<HeldObjectHandAttach>() == null) gameObject.AddComponent<HeldObjectHandAttach>();

        if (IsOwner)
        {
            Destroy(applyAvatar);
            foreach (var go in localTrackingInfos) go.SetActive(true);
        }
        else
        {
            foreach (var go in localTrackingInfos)
            {
                applyAvatar.SetActive(true);
                Destroy(go);
            }

            // 접속 직후 추적 값이 오기 전에는 아바타가 스폰 지점에서 몸이 늘어나고 뚫린 채 보였다 → 첫 값을 받아 자세를 맞춘 뒤에 보이게 한다
            if (applyAvatar != null)
            {
                _avatarRenderers = applyAvatar.GetComponentsInChildren<Renderer>(true);
                SetAvatarVisible(false);
            }
        }
    }

    private Renderer[] _avatarRenderers;
    private bool _gotData;
    private int _showCountdown = -1;

    private void SetAvatarVisible(bool visible)
    {
        if (_avatarRenderers == null) return;
        foreach (var r in _avatarRenderers) if (r != null) r.enabled = visible;
    }

    private void Start()
    {
        anchor = GameObject.FindGameObjectWithTag("Anchor")?.transform;
    }

    private void Update()
    {
        if (IsServerInitialized == true)
            return;
        if (!IsOwner)
        {
            // Anchor 가 아직 없으면(씬 로드 순서) 재탐색만 하고 건너뜀 — 예전에는 여기서 NullReferenceException
            if (anchor == null)
            {
                anchor = GameObject.FindGameObjectWithTag("Anchor")?.transform;
                if (anchor == null) return;
            }

            if (!_gotData) return;

            if (_snapPending)
            {
                _snapPending = false;
                SnapToTargets();
            }

            if (trackingSetup.applyHead != null)
            {
                Vector3 worldHeadPos = anchor.position + anchor.rotation * targetHeadPos;
                Quaternion worldHeadRot = anchor.rotation * targetHeadRot;

                trackingSetup.applyHead.position = Vector3.Lerp(trackingSetup.applyHead.position, worldHeadPos, Blend(positionLerp));
                trackingSetup.applyHead.rotation = Quaternion.Slerp(trackingSetup.applyHead.rotation, worldHeadRot, Blend(rotationLerp));

                FollowAvatarRoot(trackingSetup.applyHead.position, trackingSetup.applyHead.rotation);
            }

            ApplyWristSmooth(trackingSetup.leftHand, targetLeftWristPos, targetLeftWristRot);
            ApplyWristSmooth(trackingSetup.rightHand, targetRightWristPos, targetRightWristRot);

            // 스냅 후 두 프레임 지나(IK 가 한 번 풀린 뒤) 아바타를 보이게 한다
            if (_showCountdown > 0 && --_showCountdown == 0) SetAvatarVisible(true);
            DebugRemote();
            return;
        }

        DebugOwner();
        syncTimer += Time.deltaTime;
        if (syncTimer >= syncInterval)
        {
            syncTimer = 0f;
            SendTrackingData();
        }
    }

    // Animator 가 Update 뒤에 손가락을 덮어쓸 수 있으므로 LateUpdate 에서 적용한다.
    // 보간 상태를 뼈가 아닌 별도 배열에 두어야 Animator 가 매 프레임 되돌려도 목표에 수렴한다.
    private void LateUpdate()
    {
        if (IsServerInitialized == true || IsOwner) return;

        ApplyFingers(trackingSetup.leftHand, targetLeftFingerRots, ref _leftFingerPose);
        ApplyFingers(trackingSetup.rightHand, targetRightFingerRots, ref _rightFingerPose);
    }

    // ── 원격 아바타 몸통 추종 (A-1) ─────────────────────────────
    // VR_Player 는 루트를 동기화하지 않고 머리·손 목표만 보낸다. 아바타 루트가 스폰 지점에 고정된 채
    // 목표만 멀어지면 VRIK 이 척추·팔을 억지로 늘리거나 꺾어 기괴한 자세가 된다.
    // → 아바타 루트(VRIK.references.root)를 머리의 수평 위치·좌우 방향으로 따라가게 하면
    //   VRIK 의 보행(locomotion) 이 발을 옮기며 자연스럽게 서 있는다.
    [Header("원격 아바타 몸통 추종")]
    [SerializeField] private bool followAvatarRoot = true;
    [SerializeField] private float rootFollowSpeed = 10f;
    [SerializeField] private float rootTurnSpeed = 6f;
    private Transform _avatarRoot;
    private float _avatarFloorY;

    private bool _snapPending;

    // 처음 받은 값으로 머리·손·아바타 루트를 바로 맞춘다(보간으로 날아오지 않게)
    private void SnapToTargets()
    {
        if (anchor == null) return;
        if (trackingSetup.applyHead != null)
        {
            trackingSetup.applyHead.position = anchor.position + anchor.rotation * targetHeadPos;
            trackingSetup.applyHead.rotation = anchor.rotation * targetHeadRot;
            FollowAvatarRoot(trackingSetup.applyHead.position, trackingSetup.applyHead.rotation, true);
        }
        if (trackingSetup.leftHand.applyWrist != null)
        {
            trackingSetup.leftHand.applyWrist.position = anchor.position + anchor.rotation * targetLeftWristPos;
            trackingSetup.leftHand.applyWrist.rotation = anchor.rotation * targetLeftWristRot;
        }
        if (trackingSetup.rightHand.applyWrist != null)
        {
            trackingSetup.rightHand.applyWrist.position = anchor.position + anchor.rotation * targetRightWristPos;
            trackingSetup.rightHand.applyWrist.rotation = anchor.rotation * targetRightWristRot;
        }
    }

    // ── 체형 보정 ──
    // 원격 아바타는 접속자의 실제 키·팔 길이와 상관없는 고정 체형이라, 손 목표(실제 손 위치)에 팔이 닿지 않거나 남아
    // 팔이 구부정하게 접히고 잡은 물건이 손에서 떨어져 허공에 떠 보였다. VRIKCalibrator 와 같은 방식으로
    // 아바타 눈높이가 접속자의 (관측된 최대) 눈높이와 맞도록 루트 크기를 맞춘다.
    [Header("원격 아바타 체형 보정")]
    [SerializeField] private bool matchAvatarHeight = true;
    [SerializeField] private Vector2 avatarScaleRange = new Vector2(0.75f, 1.35f);
    private Vector3 _avatarBaseScale = Vector3.one;
    private float _avatarEyeAtBase;
    private float _maxHeadHeight;
    private Transform _avatarHeadBone;

    private void MatchAvatarHeight()
    {
        if (!matchAvatarHeight || _avatarRoot == null || _avatarHeadBone == null) return;
        // 앉거나 웅크려 있을 때를 키로 오인하지 않도록 지금까지 본 가장 높은 머리 높이를 쓴다(아바타 부모가 바닥 기준이라 가정)
        _maxHeadHeight = Mathf.Max(_maxHeadHeight, targetHeadPos.y);
        if (_maxHeadHeight < 1.0f || _avatarEyeAtBase < 0.5f) return;
        float factor = Mathf.Clamp(_maxHeadHeight / _avatarEyeAtBase, avatarScaleRange.x, avatarScaleRange.y);
        float cur = _avatarRoot.localScale.y / Mathf.Max(0.0001f, _avatarBaseScale.y);
        if (Mathf.Abs(factor - cur) < 0.03f) return;
        float next = Mathf.Lerp(cur, factor, 1f - Mathf.Exp(-3f * Time.deltaTime));
        _avatarRoot.localScale = _avatarBaseScale * next;
    }

    // ── 몸통 추종 개선 (2026-11-05) ──
    // 예전: 아바타 루트를 매 프레임 머리의 수평 위치로 옮겨 허리를 숙여도 몸이 머리 밑으로 따라와 '쪼그림'이 됐고,
    //       루트가 계속 움직여 발 걸음이 끊임없이 나왔다.
    // 지금: ① 머리가 루트에서 일정 범위(앞 0.35 / 뒤 0.15 / 옆 0.2 m) 안이면 루트를 그대로 두어 척추가 굽게 하고, 범위를 넘을 때만 따라간다
    //       ② 몸 방향도 머리 방향과 35° 이상 벌어질 때만 돌린다 ③ 바닥 높이를 아바타 발 밑 레이캐스트로 갱신
    //       ④ VRIK minHeadHeight(0.8→0.3)·stepThreshold(0.4→0.5) 조정
    [Header("원격 아바타 몸통 추종 — 범위/바닥")]
    [SerializeField] private float leanForward = 0.35f;
    [SerializeField] private float leanBackward = 0.15f;
    [SerializeField] private float leanSide = 0.2f;
    [SerializeField] private float yawDeadZoneDeg = 35f;
    [SerializeField] private bool probeFloor = true;
    [SerializeField] private LayerMask floorMask = ~0;
    [SerializeField] private float floorProbeUp = 0.4f;
    [SerializeField] private float floorProbeDown = 1.2f;
    [SerializeField] private float floorMaxDeviation = 0.6f;
    [SerializeField] private float minHeadHeightOverride = 0.3f;
    [SerializeField] private float stepThresholdOverride = 0.5f;

    private VRIK _vrik;
    private float _floorBaseY;
    private float _nextFloorProbe;
    private string _floorHitName = "-";
    private static readonly RaycastHit[] s_floorHits = new RaycastHit[12];

    private void ProbeFloor()
    {
        if (!probeFloor || _avatarRoot == null || Time.unscaledTime < _nextFloorProbe) return;
        _nextFloorProbe = Time.unscaledTime + 0.1f;

        Vector3 o = new Vector3(_avatarRoot.position.x, _avatarFloorY + floorProbeUp, _avatarRoot.position.z);
        int n = Physics.RaycastNonAlloc(o, Vector3.down, s_floorHits, floorProbeUp + floorProbeDown, floorMask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue; float y = 0f; bool found = false;
        for (int i = 0; i < n; i++)
        {
            var c = s_floorHits[i].collider;
            if (c == null || c.transform.IsChildOf(transform)) continue;
            // 다른 플레이어·잡을 수 있는 물건(NetworkObject)은 바닥이 아니다
            if (c.GetComponentInParent<NetworkObject>() != null) continue;
            if (s_floorHits[i].distance < best) { best = s_floorHits[i].distance; y = s_floorHits[i].point.y; found = true; _floorHitName = c.name; }
        }
        if (!found) { _floorHitName = "none"; return; }
        y = Mathf.Clamp(y, _floorBaseY - floorMaxDeviation, _floorBaseY + floorMaxDeviation);
        _avatarFloorY = Mathf.Lerp(_avatarFloorY, y, 0.5f);
    }

    private void FollowAvatarRoot(Vector3 headPos, Quaternion headRot, bool snap = false)
    {
        if (!followAvatarRoot || applyAvatar == null) return;

        if (_avatarRoot == null)
        {
            var vrik = applyAvatar.GetComponentInChildren<VRIK>(true);
            if (vrik == null) { followAvatarRoot = false; return; }
            _vrik = vrik;
            _avatarRoot = vrik.references.root != null ? vrik.references.root : vrik.transform;
            _avatarFloorY = _avatarRoot.position.y;     // 처음 위치의 높이 = 바닥(이후 레이캐스트로 보정)
            _floorBaseY = _avatarFloorY;
            _avatarBaseScale = _avatarRoot.localScale;
            _avatarHeadBone = vrik.references.head;
            if (_avatarHeadBone != null)
                _avatarEyeAtBase = (_avatarHeadBone.position.y - _avatarRoot.position.y) + 0.08f * Mathf.Max(0.01f, _avatarRoot.lossyScale.y);

            vrik.solver.spine.minHeadHeight = minHeadHeightOverride;
            vrik.solver.locomotion.stepThreshold = stepThresholdOverride;
            CacheArmLengths();
        }

        MatchAvatarHeight();
        ProbeFloor();

        float dt = Time.deltaTime;
        float sc = Mathf.Max(0.01f, _avatarRoot.lossyScale.y);

        Vector3 headFwd = Vector3.ProjectOnPlane(headRot * Vector3.forward, Vector3.up);
        bool headFwdOk = headFwd.sqrMagnitude > 0.05f;   // 위/아래를 볼 때는 수평 성분이 작아 방향이 튄다

        // ── 방향: 머리와 yawDeadZoneDeg 이상 벌어질 때만 돌린다 ──
        Vector3 rootFwd = Vector3.ProjectOnPlane(_avatarRoot.forward, Vector3.up).normalized;
        if (headFwdOk)
        {
            headFwd.Normalize();
            Quaternion want;
            if (snap) want = Quaternion.LookRotation(headFwd, Vector3.up);
            else
            {
                float delta = Vector3.SignedAngle(rootFwd, headFwd, Vector3.up);
                float over = Mathf.Abs(delta) - yawDeadZoneDeg;
                if (over <= 0f) want = _avatarRoot.rotation;
                else want = Quaternion.AngleAxis(Mathf.Sign(delta) * over, Vector3.up) * _avatarRoot.rotation;
            }
            _avatarRoot.rotation = snap ? want : Quaternion.Slerp(_avatarRoot.rotation, want, 1f - Mathf.Exp(-rootTurnSpeed * dt));
            rootFwd = Vector3.ProjectOnPlane(_avatarRoot.forward, Vector3.up).normalized;
        }

        // ── 위치: 머리가 범위를 벗어난 만큼만 따라간다 ──
        Vector3 rp = _avatarRoot.position;
        Vector3 target;
        if (snap)
        {
            target = new Vector3(headPos.x, _avatarFloorY, headPos.z);
        }
        else
        {
            Vector3 right = Vector3.Cross(Vector3.up, rootFwd);
            Vector3 d = new Vector3(headPos.x - rp.x, 0f, headPos.z - rp.z);
            float lf = Vector3.Dot(d, rootFwd);
            float lr = Vector3.Dot(d, right);
            float cf = Mathf.Clamp(lf, -leanBackward * sc, leanForward * sc);
            float cr = Mathf.Clamp(lr, -leanSide * sc, leanSide * sc);
            Vector3 xz = new Vector3(headPos.x, 0f, headPos.z) - rootFwd * cf - right * cr;
            target = new Vector3(xz.x, _avatarFloorY, xz.z);
        }

        if (snap) { _avatarRoot.position = target; }
        else
        {
            float k = 1f - Mathf.Exp(-rootFollowSpeed * dt);
            Vector3 np = Vector3.Lerp(rp, new Vector3(target.x, rp.y, target.z), k);
            np.y = Mathf.Lerp(rp.y, target.y, 1f - Mathf.Exp(-12f * dt));
            _avatarRoot.position = np;
        }
    }

    // ── 디버그 로그 (2026-11-05) ────────────────────────────────────────
    // 헤드셋에서 자세 문제의 실제 원인(머리 높이·몸통 거리·팔이 닿는지·갱신 지연)을 확정하기 위한 로그. 2초마다 한 줄.
    // 보는 법: Quest = adb logcat -s Unity | PC = Player.log,  접두어 [VRDebug]
    [Header("디버그")]
    [SerializeField] private bool debugLog = true;
    [SerializeField] private float debugInterval = 2f;
    private float _nextDebug;
    private float _lastRxTime;
    private int _rxCount;
    private float _rxWindowStart;
    private float _rxHz;
    private float _armLenL, _armLenR;

    private void CacheArmLengths()
    {
        if (_vrik == null) return;
        var r = _vrik.references;
        _armLenL = ChainLen(r.leftUpperArm, r.leftForearm, r.leftHand);
        _armLenR = ChainLen(r.rightUpperArm, r.rightForearm, r.rightHand);
    }

    private static float ChainLen(Transform a, Transform b, Transform c)
    {
        if (a == null || b == null || c == null) return 0f;
        return Vector3.Distance(a.position, b.position) + Vector3.Distance(b.position, c.position);
    }

    private void CountRx()
    {
        _lastRxTime = Time.unscaledTime;
        _rxCount++;
        if (Time.unscaledTime - _rxWindowStart >= 2f)
        {
            _rxHz = _rxCount / Mathf.Max(0.01f, Time.unscaledTime - _rxWindowStart);
            _rxCount = 0; _rxWindowStart = Time.unscaledTime;
        }
    }

    private void DebugRemote()
    {
        if (!debugLog || Time.unscaledTime < _nextDebug) return;
        _nextDebug = Time.unscaledTime + Mathf.Max(0.5f, debugInterval);
        if (_avatarRoot == null || trackingSetup.applyHead == null) return;

        Vector3 hp = trackingSetup.applyHead.position;
        Vector3 rp = _avatarRoot.position;
        Vector3 fwd = Vector3.ProjectOnPlane(_avatarRoot.forward, Vector3.up).normalized;
        Vector3 d = new Vector3(hp.x - rp.x, 0f, hp.z - rp.z);
        float scale = _avatarRoot.localScale.y / Mathf.Max(0.0001f, _avatarBaseScale.y);
        float yawDiff = Vector3.SignedAngle(fwd, Vector3.ProjectOnPlane(trackingSetup.applyHead.forward, Vector3.up), Vector3.up);

        string armL = ArmInfo(_vrik != null ? _vrik.references.leftUpperArm : null, trackingSetup.leftHand.applyWrist, _armLenL);
        string armR = ArmInfo(_vrik != null ? _vrik.references.rightUpperArm : null, trackingSetup.rightHand.applyWrist, _armLenR);

        var held = SyncGrab.FindHeldBy(OwnerId);
        string heldInfo = "-";
        if (held != null)
        {
            Vector3 g = held.GripWorldPoint;
            float dl = trackingSetup.leftHand.applyWrist != null ? Vector3.Distance(g, trackingSetup.leftHand.applyWrist.position) : -1f;
            float dr = trackingSetup.rightHand.applyWrist != null ? Vector3.Distance(g, trackingSetup.rightHand.applyWrist.position) : -1f;
            heldInfo = $"{held.name} gripToWristL={dl:F2} R={dr:F2}";
        }

        Debug.Log($"[VRDebug][remote owner={OwnerId}] headH={hp.y - _avatarFloorY:F2}m (maxSeen={_maxHeadHeight:F2}) scale={scale:F2} " +
                  $"floorY={_avatarFloorY:F2}(hit={_floorHitName}) headToRoot fwd={Vector3.Dot(d, fwd):F2} side={Vector3.Dot(d, Vector3.Cross(Vector3.up, fwd)):F2} yawDiff={yawDiff:F0} " +
                  $"arm L[{armL}] R[{armR}] held[{heldInfo}] rx={_rxHz:F0}Hz age={(Time.unscaledTime - _lastRxTime) * 1000f:F0}ms");
    }

    private static string ArmInfo(Transform shoulder, Transform target, float len)
    {
        if (shoulder == null || target == null) return "n/a";
        float dist = Vector3.Distance(shoulder.position, target.position);
        return $"reach {dist:F2}/{len:F2}{(len > 0.01f && dist > len * 1.02f ? " OVER" : "")}";
    }

    private void DebugOwner()
    {
        if (!debugLog || Time.unscaledTime < _nextDebug) return;
        _nextDebug = Time.unscaledTime + Mathf.Max(0.5f, debugInterval);
        Transform anchorT = anchor != null ? anchor : transform;
        if (trackingSetup.trackingHead == null) return;
        Vector3 h = trackingSetup.trackingHead.position - anchorT.position;
        Vector3 l = trackingSetup.leftHand.trackingWrist != null ? trackingSetup.leftHand.trackingWrist.position - anchorT.position : Vector3.zero;
        Vector3 r = trackingSetup.rightHand.trackingWrist != null ? trackingSetup.rightHand.trackingWrist.position - anchorT.position : Vector3.zero;
        // 머리 높이가 실제 키(바닥 기준)인지, 시작 자세 기준(Device)인지 가르기 위한 정보
        string originMode = "n/a";
        try
        {
            var subs = new System.Collections.Generic.List<UnityEngine.XR.XRInputSubsystem>();
            UnityEngine.SubsystemManager.GetSubsystems(subs);
            if (subs.Count > 0) originMode = $"{subs[0].GetTrackingOriginMode()}/supported={subs[0].GetSupportedTrackingOriginModes()}";
        }
        catch { }
        Debug.Log($"[VRDebug][owner] trackingOrigin={originMode} headY={h.y:F2}m (anchor-relative)wristL=({l.x:F2},{l.y:F2},{l.z:F2}) wristR=({r.x:F2},{r.y:F2},{r.z:F2}) " +
                  $"R-wrist={(trackingSetup.rightHand.trackingWrist != null ? trackingSetup.rightHand.trackingWrist.name : "null")} anchor={(anchor != null ? "ok" : "MISSING")} send={1f / Mathf.Max(0.001f, syncInterval):F0}Hz");
    }

    // ── 추적 데이터 전송 (2026-11-05 개선) ──
    // 예전: 머리·양 손목·손가락 48개 회전(약 0.8KB)을 40Hz 로 '신뢰 전송' 한 덩어리로 보냄 → 약 30KB/s, 패킷이 하나 늦으면
    //       뒤 데이터가 줄줄이 막혀(head-of-line blocking) 와이파이 Quest 에서 아바타가 끊기고 뒤늦게 몰려 움직였다.
    // 지금: ① 머리·손목(약 70B)은 비신뢰 채널로 40Hz — 유실돼도 다음 값이 곧 오므로 무해 ② 손가락은 신뢰 채널로 12.5Hz,
    //       바뀐 것이 없으면 0.5초마다 한 번만(약 85% 감소)
    [Header("손가락 전송")]
    [SerializeField] private float fingerSendInterval = 0.08f;
    [SerializeField] private float fingerKeepAlive = 0.5f;
    [SerializeField] private float fingerChangeDeg = 0.7f;
    private float _fingerTimer;
    private float _fingerLastSent;
    private Quaternion[] _lastSentFingersL, _lastSentFingersR;

    public void SendTrackingData()
    {
        if (!IsOwner) return;
        Transform anchorT = anchor != null ? anchor : transform;
        Quaternion inv = Quaternion.Inverse(anchorT.rotation);

        Vector3 headPos = trackingSetup.trackingHead ? inv * (trackingSetup.trackingHead.position - anchorT.position) : Vector3.zero;
        Quaternion headRot = trackingSetup.trackingHead ? inv * trackingSetup.trackingHead.rotation : Quaternion.identity;
        Vector3 leftWristPos = trackingSetup.leftHand.trackingWrist ? inv * (trackingSetup.leftHand.trackingWrist.position - anchorT.position) : Vector3.zero;
        Quaternion leftWristRot = trackingSetup.leftHand.trackingWrist ? inv * trackingSetup.leftHand.trackingWrist.rotation : Quaternion.identity;
        Vector3 rightWristPos = trackingSetup.rightHand.trackingWrist ? inv * (trackingSetup.rightHand.trackingWrist.position - anchorT.position) : Vector3.zero;
        Quaternion rightWristRot = trackingSetup.rightHand.trackingWrist ? inv * trackingSetup.rightHand.trackingWrist.rotation : Quaternion.identity;

        SendPoseToServer(headPos, headRot, leftWristPos, leftWristRot, rightWristPos, rightWristRot, Channel.Unreliable);

        _fingerTimer += syncInterval;
        if (_fingerTimer >= fingerSendInterval)
        {
            _fingerTimer = 0f;
            Quaternion[] l = GetFingerLocalRotations(trackingSetup.leftHand);
            Quaternion[] r = GetFingerLocalRotations(trackingSetup.rightHand);
            bool changed = FingersChanged(l, _lastSentFingersL) || FingersChanged(r, _lastSentFingersR);
            if (changed || Time.unscaledTime - _fingerLastSent >= fingerKeepAlive)
            {
                _fingerLastSent = Time.unscaledTime;
                _lastSentFingersL = l; _lastSentFingersR = r;
                SendFingersToServer(l, r);
            }
        }
    }

    private bool FingersChanged(Quaternion[] now, Quaternion[] last)
    {
        if (last == null || now == null || last.Length != now.Length) return true;
        for (int i = 0; i < now.Length; i++)
            if (Quaternion.Angle(now[i], last[i]) > fingerChangeDeg) return true;
        return false;
    }

    private Quaternion[] GetFingerLocalRotations(HandData hand)
    {
        List<Quaternion> rots = new List<Quaternion>();
        foreach (var finger in hand.fingers)
            for (int i = 0; i < finger.trackingBones.Count; i++)
                rots.Add(finger.trackingBones[i] != null ? finger.trackingBones[i].localRotation : Quaternion.identity);
        return rots.ToArray();
    }

    // 머리·손목: 비신뢰(유실돼도 다음 값이 곧 옴)
    // (이 FishNet 버전은 채널을 특성이 아니라 마지막 매개변수 Channel 로 지정한다)
    [ServerRpc(RequireOwnership = true)]
    private void SendPoseToServer(Vector3 headPos, Quaternion headRot,
                                  Vector3 leftWristPos, Quaternion leftWristRot,
                                  Vector3 rightWristPos, Quaternion rightWristRot,
                                  Channel channel = Channel.Reliable)
    {
        PosToClients(headPos, headRot, leftWristPos, leftWristRot, rightWristPos, rightWristRot, Channel.Unreliable);
    }

    [ObserversRpc(ExcludeOwner = true)]
    private void PosToClients(Vector3 headPos, Quaternion headRot,
                              Vector3 leftWristPos, Quaternion leftWristRot,
                              Vector3 rightWristPos, Quaternion rightWristRot,
                              Channel channel = Channel.Reliable)
    {
        if (anchor == null)
        {
            anchor = GameObject.FindGameObjectWithTag("Anchor")?.transform;
            return;
        }
        if (!_gotData) { _gotData = true; _snapPending = true; _showCountdown = 3; _rxWindowStart = Time.unscaledTime; }
        CountRx();
        targetHeadPos = headPos;
        targetHeadRot = headRot;
        targetLeftWristPos = leftWristPos;
        targetLeftWristRot = leftWristRot;
        targetRightWristPos = rightWristPos;
        targetRightWristRot = rightWristRot;
    }

    // 손가락: 신뢰(느리게, 바뀔 때만)
    [ServerRpc(RequireOwnership = true)]
    private void SendFingersToServer(Quaternion[] leftFingerRots, Quaternion[] rightFingerRots)
    {
        FingersToClients(leftFingerRots, rightFingerRots);
    }

    [ObserversRpc(ExcludeOwner = true, BufferLast = true)]
    private void FingersToClients(Quaternion[] leftFingerRots, Quaternion[] rightFingerRots)
    {
        targetLeftFingerRots = leftFingerRots;
        targetRightFingerRots = rightFingerRots;
    }

    private void ApplyWristSmooth(HandData hand, Vector3 wristPos, Quaternion wristRot)
    {
        if (anchor == null)
        {
            anchor = GameObject.FindGameObjectWithTag("Anchor")?.transform;
            return;
        }

        if (hand.applyWrist != null)
        {
            Vector3 targetWristPos = anchor.position + anchor.rotation * wristPos;
            Quaternion targetWristRot = anchor.rotation * wristRot;

            hand.applyWrist.position = Vector3.Lerp(hand.applyWrist.position, targetWristPos, Blend(positionLerp));
            hand.applyWrist.rotation = Quaternion.Slerp(hand.applyWrist.rotation, targetWristRot, Blend(rotationLerp));
        }
    }

    private void ApplyFingers(HandData hand, Quaternion[] targets, ref Quaternion[] pose)
    {
        if (targets == null) return;
        if (pose == null || pose.Length != targets.Length) pose = (Quaternion[])targets.Clone();

        float t = 1f - Mathf.Exp(-fingerFollowSpeed * Time.deltaTime);
        int rotIdx = 0;
        for (int f = 0; f < hand.fingers.Count; f++)
        {
            var finger = hand.fingers[f];
            for (int i = 0; i < finger.applyBones.Count; i++)
            {
                if (finger.applyBones[i] != null && rotIdx < targets.Length)
                {
                    pose[rotIdx] = Quaternion.Slerp(pose[rotIdx], targets[rotIdx], t);
                    finger.applyBones[i].localRotation = pose[rotIdx];
                }
                rotIdx++;
            }
        }
    }
}
