using Autohand;
using DG.Tweening;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using Sirenix.OdinInspector;
using System.Collections;
using UnityEngine;

/// <summary>
/// 멀티플레이어 오브젝트 그랩 동기화 — 완전 서버 권위 구조.
///
/// [설계 원칙]
///   모든 상태 변경은 서버가 결정하고 클라이언트에 전파.
///   클라이언트는 요청만 하고 서버 응답을 기다림.
///   타이밍 문제 원천 차단을 위해 클라이언트 로컬 예측 없음.
///
/// [상태 전이 — 서버만 변경]
///   Fixed    : Kinematic=true,  Gravity=false
///   Grabbing : Kinematic=false, Gravity=false
///
/// [VR 그랩 흐름]
///   1. 손이 닿음 → OnBeforeGrab
///   2. 소유권 요청 → RequestGrabServerRpc
///   3. 서버: 소유권 부여 + Kinematic 해제 + 전체 전파
///   4. GrantGrabTargetRpc → 1~2프레임 대기 → TryGrab
///   5. OnVRGrab (확인용만, 추가 서버 호출 없음)
///   6. 손 놓음 → ReleaseServerRpc
///   7. 서버: 위치 확정 + Kinematic 복구 + 전체 전파
///
/// [핵심]
///   Kinematic 변경을 RequestGrabServerRpc/ReleaseServerRpc에서만 처리.
///   클라이언트에서 Kinematic을 직접 변경하지 않음.
///   → 타이밍 역전, 좀비 상태 원천 차단.
/// </summary>
public class SyncGrab : NetworkBehaviour
{
    // ──────────────────────────────────────────────────────
    // 인스펙터
    // ──────────────────────────────────────────────────────

    [Header("PC/모바일 — 날아갈 위치")]
    public Transform holdPoint;

    [Header("DOTween")]
    public float flyDuration = 0.5f;
    public Ease flyEase = Ease.OutCubic;
    public float arcHeight = 0.5f;

    [Header("PC/모바일 — 잡기 자세 오프셋")]
    [Tooltip("잡혔을 때 HoldPoint로부터의 위치 오프셋")]
    public Vector3 holdPositionOffset = Vector3.zero;
    [Tooltip("잡혔을 때 HoldPoint로부터의 회전 오프셋 (오브젝트 기준)")]
    public Vector3 holdRotationOffset = Vector3.zero;

    [Header("PC/모바일 — 차체·벽 관통 방지")]
    [Tooltip("손에 든 물체가 차체·벽에 파고들어 잘려 보이지 않도록, 장애물이 있으면 카메라 쪽으로 끌어당긴다.")]
    public bool avoidGeometryClipping = true;
    [Tooltip("카메라에서 이 거리(m)보다 가깝게는 끌어오지 않는다.")]
    public float minHoldDistance = 0.18f;
    [Tooltip("장애물과 유지할 여유(m).")]
    public float geometryMargin = 0.03f;

    [Tooltip("true: 손에 든 물체를 내 화면에서 HoldPoint에 즉시 붙인다(보간 지연 없음). 서버·다른 클라이언트에는 기존처럼 LNT로 전파.")]
    public bool snapHoldToTarget = true;

    private static readonly RaycastHit[] s_clipBuf = new RaycastHit[16];
    private float _holdRadius = -1f;
    private float _avoidReduction;   // 장애물 때문에 카메라 쪽으로 당겨진 거리(부드럽게 변화)

    private TaskItem _taskItem;

    // ──────────────────────────────────────────────────────
    // 손 모션(PcAvatar)용 — 누가 무엇을 잡고 있는지 / 어디를 잡는지
    // ──────────────────────────────────────────────────────
    private static readonly System.Collections.Generic.List<SyncGrab> s_held = new System.Collections.Generic.List<SyncGrab>();

    [Header("손 모션")]
    [Tooltip("(선택) 아바타 손이 잡을 위치. 비우면 렌더러 경계의 중심을 쓴다.")]
    public Transform gripPoint;
    [Tooltip("PC·모바일 아바타 손목 위치 보정(이 물체 로컬 좌표, 단위 m). Auto Hand 잡기 포즈(GrabbablePose)의 손 위치에서 더 옮기고 싶을 때 쓴다. 기본 0.")]
    public Vector3 handTargetAdjust = Vector3.zero;
    private Renderer[] _gripRenderers;
    private Autohand.GrabbablePose[] _gripPoses;
    private bool _gripPoseSearched;

    /// <summary>
    /// 이 클라이언트가 소유한(=내) 플레이어의 HoldPoint. 모든 클라이언트에는 접속자 수만큼 플레이어 복사본이 있고 각각 HoldPoint 가 있어서
    /// GameObject.FindWithTag("HoldPoint") 는 아무 복사본이나 돌려준다 -> 모바일이 잡은 태블릿이 PC 플레이어 쪽으로 날아가던 원인.
    /// </summary>
    public static Transform FindLocalHoldPoint()
    {
        Transform fallback = null;
        foreach(var go in GameObject.FindGameObjectsWithTag("HoldPoint"))
        {
            if(go == null) continue;
            var nob = go.GetComponentInParent<NetworkObject>();
            if(nob != null && nob.IsOwner) return go.transform;
            if(nob == null && fallback == null) fallback = go.transform;   // 네트워크 오브젝트가 아닌 씬 고정 HoldPoint(테스트 씬 등)
        }
        return fallback;
    }

    /// <summary>holdPoint 가 비었거나 남의 플레이어 것이면 내 HoldPoint 로 교체한다.</summary>
    private void EnsureOwnHoldPoint()
    {
        if(holdPoint != null)
        {
            var nob = holdPoint.GetComponentInParent<NetworkObject>();
            if(nob == null || nob.IsOwner) return;   // 씬 고정 또는 내 것
        }
        var mine = FindLocalHoldPoint();
        if(mine != null) holdPoint = mine;
    }

    /// <summary>해당 클라이언트가 지금 잡고 있는 물체(없으면 null). 소유권이 잡은 사람에게 넘어가므로 Owner 로 판별한다.</summary>
    public static SyncGrab FindHeldBy(int clientId)
    {
        if(clientId < 0) return null;
        for(int i = s_held.Count - 1; i >= 0; i--)
        {
            var g = s_held[i];
            if(g == null) { s_held.RemoveAt(i); continue; }
            if(g.IsGrabbed && g.Owner != null && g.Owner.IsValid && g.Owner.ClientId == clientId) return g;
        }
        return null;
    }

    /// <summary>손이 향할 월드 위치.</summary>
    public Vector3 GripWorldPoint
    {
        get
        {
            if(gripPoint != null) return gripPoint.position;
            if(_gripRenderers == null) _gripRenderers = GetComponentsInChildren<Renderer>(false);
            bool any = false; Bounds b = default;
            foreach(var r in _gripRenderers)
            {
                if(r == null || !r.enabled || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                if(!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any ? b.center : transform.position;
        }
    }

    /// <summary>
    /// PC·모바일 아바타의 '손목'이 가야 할 월드 위치.
    /// 도구에 Auto Hand 잡기 포즈(GrabbablePose)가 저장돼 있으면(예: 드릴 손잡이) 그 포즈의 손 위치를 쓴다 — 물체 전체의 중심(GripWorldPoint)으로 가면
    /// 손이 손잡이가 아니라 몸통 쪽에 놓여 손잡이를 못 쥐었다. 포즈가 없는 물체(태블릿 등)는 예전처럼 GripWorldPoint.
    /// 포즈가 한쪽 손만 저장돼 있으면 물체 로컬 z 를 뒤집어 반대 손으로 쓴다(손잡이가 좌우 대칭이라는 가정).
    /// </summary>
    public Vector3 HandTargetPoint(bool rightHand)
    {
        // 같은 손 포즈가 있으면 그 손목 위치 그대로
        if(TryGetGripPose(rightHand, out var same, out var poseTf))
            return poseTf.TransformPoint(same.handOffset) + transform.TransformVector(handTargetAdjust);

        // 반대 손 포즈만 있으면 좌우 대칭(포즈 기준 로컬 z 반전)으로 손목 위치만 쓴다
        if(TryGetGripPose(!rightHand, out var other, out var otherTf))
        {
            Vector3 local = other.handOffset;
            local.z = -local.z;
            return otherTf.TransformPoint(local) + transform.TransformVector(handTargetAdjust);
        }
        return GripWorldPoint;
    }

    /// <summary>
    /// 이 도구에 저장된 Auto Hand 잡기 포즈(GrabbablePose) 중 해당 손 것을 찾는다.
    /// pose 의 handOffset / localQuaternionOffset / 손가락 회전은 모두 poseTransform(=GrabbablePose 가 붙은 오브젝트) 기준이다.
    /// PC·모바일 아바타 손(PcHandGrip)이 VR 과 같은 손 모양으로 쥐는 데 쓴다.
    /// </summary>
    public bool TryGetGripPose(bool rightHand, out Autohand.HandPoseData pose, out Transform poseTransform)
    {
        pose = default;
        poseTransform = null;
        if(!_gripPoseSearched)
        {
            _gripPoseSearched = true;
            _gripPoses = GetComponentsInChildren<Autohand.GrabbablePose>(true);
        }
        if(_gripPoses == null) return false;

        foreach(var gp in _gripPoses)
        {
            if(gp == null || !gp.poseEnabled) continue;
            bool set = rightHand ? gp.rightPoseSet : gp.leftPoseSet;
            if(!set) continue;
            var data = rightHand ? gp.rightPose : gp.leftPose;
            if(data.fingerPoses == null || data.fingerPoses.Length < 5) continue;
            pose = data;
            poseTransform = gp.transform;
            return true;
        }
        return false;
    }

    private void TrackHeld(bool held)
    {
        if(held) { if(!s_held.Contains(this)) s_held.Add(this); }
        else s_held.Remove(this);
    }

    // ──────────────────────────────────────────────────────
    // SyncVar — 서버만 쓰기
    // ──────────────────────────────────────────────────────

    /// <summary>점유 중 여부. true면 다른 플레이어 차단.</summary>
    private readonly SyncVar<bool> _isGrabbed = new(
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers));

    /// <summary>현재 물리 상태. 서버가 변경하고 전체 전파.</summary>
    private readonly SyncVar<bool> _isKinematic = new(
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers));

    /// <summary>Despawn 대기 중.</summary>
    private readonly SyncVar<bool> _isDespawning = new(
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers));

    public bool IsGrabbed => _isGrabbed.Value;

    /// <summary>holdPoint 도달 완료(PC) 또는 VR 집기 완료 시 발행</summary>
    public event System.Action OnGrabbed;
    /// <summary>내려놓기(PC RequestRelease / VR 손 놓음) 시 발행</summary>
    public event System.Action OnReleased;

    // ──────────────────────────────────────────────────────
    // 컴포넌트
    // ──────────────────────────────────────────────────────

    private Rigidbody _rb;
    private Grabbable _grab;
    private LocalNetworkTransform _lnt;

    // ──────────────────────────────────────────────────────
    // 내부 상태 (클라이언트 로컬 — 요청 관리용)
    // ──────────────────────────────────────────────────────

    private bool _isTweening;
    private bool _isPCHolding;
    private bool _wantCancel;
    private System.Action _onGranted;
    private Hand _pendingHand;

    // ──────────────────────────────────────────────────────
    // 코루틴
    // ──────────────────────────────────────────────────────

    private Coroutine _validateCoroutine;
    private Coroutine _grabCoroutine;

    // ══════════════════════════════════════════════════════
    // 생명주기
    // ══════════════════════════════════════════════════════

    public override void OnStartServer()
    {
        base.OnStartServer();
        _restEuler = transform.eulerAngles;   // 스폰 때 기울기 = 놓은 뒤 세울 기준
        _rb = GetComponent<Rigidbody>();
        _isKinematic.Value = true; // 초기값 명시적 설정
        ApplyKinematic(true);
        _validateCoroutine = StartCoroutine(ValidateOwnerRoutine());
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        StopServerSettle();
        StopCoroutineIfRunning(ref _validateCoroutine);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        _rb = GetComponent<Rigidbody>();
        _lnt = GetComponent<LocalNetworkTransform>();

        _taskItem = GetComponent<TaskItem>();
        if(_taskItem == null)
        {
            Debug.LogWarning($"[SyncGrab] {name}: TaskItem 컴포넌트 없음");
        }

        // holdPoint 는 잡는 순간 내 플레이어 것으로 다시 정한다(EnsureOwnHoldPoint). 시작 시점엔 내 플레이어가 아직 없을 수 있다.
        if(holdPoint == null) EnsureOwnHoldPoint();

        // SyncVar 콜백 등록
        _isKinematic.OnChange += OnKinematicChanged;
        _isGrabbed.OnChange += OnIsGrabbedChanged;
        _isDespawning.OnChange += OnIsDespawningChanged;
        TrackHeld(_isGrabbed.Value);   // 늦게 들어왔을 때 이미 잡힌 물체도 등록

        // 현재 서버 상태 즉시 적용
        ApplyKinematic(_isKinematic.Value);

        _grab = GetComponentInChildren<Grabbable>();
        if(_grab != null)
        {
            _grab.OnBeforeGrabEvent += OnBeforeGrab;
            _grab.onGrab.AddListener(OnVRGrab);
            _grab.onRelease.AddListener(OnVRRelease);
            EnsureDistanceGrab();
        }
        else
        {
            Debug.LogError($"[SyncGrab] {name} Grabbable 없음!");
        }

        NetworkObjectFinder.Instance?.Register(gameObject.name, gameObject);
    }

    public override void OnStopClient()
    {
        base.OnStopClient();

        _isKinematic.OnChange -= OnKinematicChanged;
        _isGrabbed.OnChange -= OnIsGrabbedChanged;
        _isDespawning.OnChange -= OnIsDespawningChanged;
        TrackHeld(false);

        DOTween.Kill(transform);
        _isPCHolding = false;
        _isTweening = false;
        _onGranted = null;
        _pendingHand = null;
        StopCoroutineIfRunning(ref _grabCoroutine);

        if(_grab != null)
        {
            _grab.OnBeforeGrabEvent -= OnBeforeGrab;
            _grab.onGrab.RemoveListener(OnVRGrab);
            _grab.onRelease.RemoveListener(OnVRRelease);
        }
    }

    public override void OnOwnershipServer(NetworkConnection prevOwner)
    {
        base.OnOwnershipServer(prevOwner);

        bool ownerGone = NetworkObject.Owner == null || !NetworkObject.Owner.IsValid;
        if(!_isGrabbed.Value || !ownerGone) return;

        ServerForceReset();
    }

    // 내가 잡아서 소유권을 받거나 잃으면 물리 상태를 다시 맞춘다(VR 손 잡기는 소유자만 물리 사용)
    public override void OnOwnershipClient(NetworkConnection prevOwner)
    {
        base.OnOwnershipClient(prevOwner);
        ApplyKinematic(_isKinematic.Value);
    }

    // ══════════════════════════════════════════════════════
    // Kinematic 적용
    // ══════════════════════════════════════════════════════

    /// <summary>Rigidbody에 Kinematic 상태 적용. 서버/클라 모두 사용.</summary>
    private void ApplyKinematic(bool isKinematic)
    {
        if(_rb == null) return;

        // 누가 잡고 있는 동안, 잡은 사람(소유자)이 아닌 쪽(서버·다른 클라이언트)에서는 물리를 끈다.
        // 예전: 잡기 RPC 에서 _isGrabbed=true(중력 끔) 뒤에 ServerSetKinematic(false)가 중력을 다시 켜서
        //       서버·관찰자 화면에서 잡힌 물체가 자유낙하(수 초에 수백 m) → 잡은 사람이 가만히 있으면
        //       남의 화면에서 팔이 아래로 펴져 '기본 자세'처럼 보였다(움직일 때만 위치가 되돌아와 잡은 자세).
        if(!isKinematic && _isGrabbed.Value && !IsOwner) isKinematic = true;

        if(isKinematic)
        {
            // Kinematic으로 만들기 전 속도를 먼저 0으로 (에러 방지)
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
            _rb.useGravity = false;
        }
        else
        {
            _rb.isKinematic = false;
            _rb.useGravity = true;
        }
    }

    /// <summary>
    /// [서버] Kinematic 상태 변경 + SyncVar 갱신.
    /// SyncVar가 변경되면 OnKinematicChanged를 통해 전체 클라에 자동 전파.
    /// </summary>
    private void ServerSetKinematic(bool isKinematic)
    {
        StopServerSettle();
        _isKinematic.Value = isKinematic;
        ApplyKinematic(isKinematic); // 서버 자신에게도 즉시 적용
    }

    // ── 놓은 물체 낙하(서버) ──
    // 예전: 놓으면 그 자리에서 바로 Kinematic 이 켜져 공중에서 놓은 도구가 허공에 멈춰 떠 있었다('무중력').
    // 지금: 서버에서만 잠깐 중력을 켜 바닥·작업대에 떨어져 멈출 때까지 시뮬레이션하고(위치는 LocalNetworkTransform 이 모두에게 전파),
    //       멈추면(또는 6초 후) 다시 Kinematic 으로 고정한다. 클라이언트는 SyncVar(_isKinematic=true) 그대로라 물리가 서로 싸우지 않는다.
    private Coroutine _settleCo;
    private bool _serverSettling;

    [Server]
    private void BeginServerSettle()
    {
        if(_rb == null) return;
        StopServerSettle();
        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.WakeUp();
        _serverSettling = true;
        _settleCo = StartCoroutine(ServerSettleRoutine());
    }

    private void StopServerSettle()
    {
        if(_settleCo != null) { StopCoroutine(_settleCo); _settleCo = null; }
        _serverSettling = false;
    }

    /// <summary>
    /// [서버] 물체를 '처음 놓인 상태'로 되돌린다 — 리스폰(persist 오브젝트 재사용)·재배치 때 호출.
    /// 낙하 중이던 속도·중력, 잡힘/디스폰 표시, 소유권을 모두 정리하고 고정(Kinematic)한다.
    /// (예전: 놓은 드릴이 끝없이 떨어지다 단계 실패로 스폰 위치에 다시 놓였는데, 낙하 속도가 남아 서랍 안으로 파고들어 돌아간 채 멈추고 잡히지 않았다)
    /// </summary>
    [Server]
    public void ServerResetToRest()
    {
        if(_rb == null) _rb = GetComponent<Rigidbody>();
        StopServerSettle();
        _isGrabbed.Value = false;
        _isDespawning.Value = false;
        if(NetworkObject != null && NetworkObject.Owner != null && NetworkObject.Owner.IsValid) NetworkObject.GiveOwnership(null);
        _isKinematic.Value = true;
        ApplyKinematic(true);   // 속도 0 + Kinematic
    }

    // 서버에 바닥 콜라이더가 없거나 못 맞춰 물체가 바닥을 뚫고 끝없이 떨어지는 것을 막는 '가상 바닥'(작업장 바닥 y=0).
    [Tooltip("놓은 물체가 이 높이(월드 y)보다 아래로 내려가면 이 높이 위에 얹고 멈춘다(서버 낙하 보호).")]
    public float serverFloorY = 0f;

    private float LowestPointY()
    {
        float min = transform.position.y;
        bool any = false;
        foreach(var c in GetComponentsInChildren<Collider>())
        {
            if(c == null || !c.enabled || c.isTrigger) continue;
            float y = c.bounds.min.y;
            if(!any || y < min) { min = y; any = true; }
        }
        return min;
    }

    private IEnumerator ServerSettleRoutine()
    {
        float start = Time.time, rest = 0f;
        yield return new WaitForFixedUpdate();
        while(_rb != null && !_isGrabbed.Value && Time.time - start < 6f)
        {
            // 가상 바닥 보호: 바닥을 뚫고 내려가면 바닥 위로 올려놓고 멈춘다
            float low = LowestPointY();
            if(low < serverFloorY)
            {
                transform.position += Vector3.up * (serverFloorY - low);
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                Debug.LogWarning($"[SyncGrab] {name}: 바닥 아래로 떨어져 y={serverFloorY:F2} 위로 되돌림(서버 바닥 콜라이더 확인 필요)");
                break;
            }
            bool still = _rb.linearVelocity.sqrMagnitude < 0.0025f && _rb.angularVelocity.sqrMagnitude < 0.01f;
            rest = still ? rest + Time.deltaTime : 0f;
            if(rest >= 0.5f) break;
            yield return null;
        }
        if(_rb != null && !_isGrabbed.Value) ApplyKinematic(true);   // 멈춘 자리에서 고정

        // 멈춘 뒤 처음 놓여 있던 기울기(보통 똑바로)로 세운다 — 라바콘 등 내려놓은 물건이 비스듬히 놓여 있던 문제
        if(uprightOnRest && _rb != null && !_isGrabbed.Value)
        {
            Quaternion want = Quaternion.Euler(_restEuler.x, transform.eulerAngles.y, _restEuler.z);
            if(Quaternion.Angle(transform.rotation, want) > 3f)
            {
                float lowBefore = LowestPointY();
                for(float t = 0f; t < 0.25f && !_isGrabbed.Value; t += Time.deltaTime)
                {
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, want, 540f * Time.deltaTime);
                    yield return null;
                }
                if(!_isGrabbed.Value)
                {
                    transform.rotation = want;
                    transform.position += Vector3.up * (lowBefore - LowestPointY());   // 세우면서 바닥을 파고들거나 뜨지 않게
                }
            }
        }
        if(_rb != null && !_isGrabbed.Value) LiftAboveGround();
        _settleCo = null;
        _serverSettling = false;
    }

    [Tooltip("내려놓아 멈춘 뒤 보이는 모델 아래끝을 바로 아래 바닥 면보다 이만큼 위에 둔다(m) — 콜라이더가 모델보다 작아 바닥에 묻혀 보이던 문제")]
    public float groundClearance = 0.01f;

    /// <summary>보이는 모델(렌더러) 아래끝이 바닥 면 아래로 들어가 있으면 그만큼 올린다(최대 15cm, 서버).</summary>
    private void LiftAboveGround()
    {
        bool any = false; Bounds rb = default;
        foreach(var r in GetComponentsInChildren<Renderer>())
        {
            if(r == null || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if(!any) { rb = r.bounds; any = true; } else rb.Encapsulate(r.bounds);
        }
        if(!any) return;
        float low = Mathf.Min(rb.min.y, LowestPointY());
        Vector3 from = new Vector3(rb.center.x, rb.max.y + 0.05f, rb.center.z);
        float ground = float.NegativeInfinity;
        foreach(var h in Physics.RaycastAll(from, Vector3.down, rb.size.y + 1f, ~0, QueryTriggerInteraction.Ignore))
        {
            if(h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
            if(h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic) continue;
            if(h.point.y > low + 0.15f) continue;   // 위에 걸친 다른 물체는 바닥이 아님
            if(h.point.y > ground) ground = h.point.y;
        }
        if(float.IsNegativeInfinity(ground)) ground = serverFloorY;
        float lift = ground + groundClearance - low;
        if(lift > 0.0005f && lift < 0.15f) transform.position += Vector3.up * lift;
    }

    [Tooltip("놓아서 멈춘 뒤 처음 스폰됐을 때의 기울기(보통 똑바로)로 세운다. 일부러 눕혀 두는 물건은 끈다.")]
    public bool uprightOnRest = true;
    private Vector3 _restEuler;

    // ══════════════════════════════════════════════════════
    // VR AutoHand 콜백
    // ══════════════════════════════════════════════════════

    private void OnBeforeGrab(Hand hand, Grabbable g)
    {
        if(IsOwner) return;

        // 잡기 차단 조건
        if(_isTweening || _isGrabbed.Value || _isDespawning.Value || _pendingHand != null)
        {
            ForceSafeRelease(hand);
            return;
        }

        _pendingHand = hand;

        if(_rb != null)
        {
            _rb.isKinematic = false;
            _rb.useGravity = false; // 잡는 동안 아래로 흐르지 않게
        }

        // [핵심] 에러 방지를 위해 Kinematic을 잠시 풀고 해제 호출
        ForceSafeRelease(hand);

        // 서버에 소유권 요청
        RequestGrabServerRpc(LocalConnection);
    }

    private void ForceSafeRelease(Hand hand)
    {
        if(hand == null) return;

        // Kinematic 상태에서 Velocity를 수정하려 하면 유니티 에러가 발생하므로 잠시 해제
        bool originalKinematic = _rb.isKinematic;
        _rb.isKinematic = false;

        hand.ForceReleaseGrab();

        _rb.isKinematic = originalKinematic;
    }


    /// <summary>
    /// 그랩 완료 확인용. 실제 상태 변경은 RequestGrabServerRpc에서 이미 처리됨.
    /// </summary>
    private void OnVRGrab(Hand hand, Grabbable g)
    {
        _pendingHand = null;
        StopCoroutineIfRunning(ref _grabCoroutine);

        if(!IsOwner) return;

        GuideSystem.Instance?.OnItemGrabbed(GetComponent<TaskItem>()?.prefabId ?? "");

        if(_isDespawning.Value)
        {
            hand.ForceReleaseGrab();
            OnGrabbed?.Invoke();
            InteractionEvents.FireItemGrabbed(GetPrefabId());
            return;
        }

        DOTween.Kill(transform);
        // 상태는 이미 서버가 Grabbing으로 설정했으므로 추가 호출 없음
    }
    /// <summary>VR 도구 벨트(VrToolBelt): 허리 슬롯 근처에서 놓으면 true 를 돌려주고 직접 보관한다 — 이때는 서버에 놓기를 보내지 않는다(소유·잡힘 유지).</summary>
    /// <summary>VR 에서 손을 놓을 때 먼저 물어보는 처리기들(도구 벨트·장착 스냅). 하나라도 true 면 기본 놓기(서버 낙하)를 하지 않는다.</summary>
    public static readonly System.Collections.Generic.List<System.Func<SyncGrab, Hand, bool>> VrReleaseInterceptors = new System.Collections.Generic.List<System.Func<SyncGrab, Hand, bool>>();
    /// <summary>VR 도구 벨트에 꽂혀 있음(잡힘 상태는 유지되지만 손에는 없음).</summary>
    [System.NonSerialized] public bool IsHolstered;

    private void OnVRRelease(Hand hand, Grabbable g)
    {
        if(!IsOwner)
        {
            // 들고 있는 사이 소유권이 넘어갔으면(단계 전환 때 서버가 되돌림 등) 손을 떼는 순간 서버 상태로 바로 맞춘다
            // — 그대로 두면 잡는 동안의 '중력 꺼짐' 상태로 손의 속도를 받아 날아간다
            if(_rb != null && (_grab == null || _grab.GetHeldBy().Count == 0))
            {
                if(!_rb.isKinematic) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
                ApplyKinematic(true);
            }
            return;
        }

        // 다른 손이 아직 잡고 있으면 무시
        if(_grab != null && _grab.GetHeldBy().Count > 0) return;

        for(int i = 0; i < VrReleaseInterceptors.Count; i++)
            if(VrReleaseInterceptors[i] != null && VrReleaseInterceptors[i](this, hand)) return;

        OnReleased?.Invoke();   // ★ 내려놓기 → InteractablePart에 알림
        ReleaseServerRpc(transform.position, transform.rotation);
    }

    // ══════════════════════════════════════════════════════
    // PC / 모바일
    // ══════════════════════════════════════════════════════

    public void OnPCClick()
    {
        if(_isTweening) return;
        if(_isDespawning.Value) return;

        if(_isPCHolding)
        {
            RequestRelease();
            return;
        }

        if(_isGrabbed.Value) return;

        GuideSystem.Instance?.OnItemGrabbed(GetComponent<TaskItem>()?.prefabId ?? "");

        // 클릭 시점에 항상 '내' 플레이어의 HoldPoint 로 맞춘다 (FindWithTag 는 다른 플레이어 복사본을 돌려줄 수 있음)
        EnsureOwnHoldPoint();
        if(holdPoint == null)
        {
            Debug.LogError($"[SyncGrab] {name}: 내 플레이어의 'HoldPoint' 를 찾지 못해 동작을 취소합니다.");
            return;
        }

        RequestGrab(() => PCFlyTo(holdPoint.position, holdPoint.rotation));
    }


    /// <summary>
    /// PC·모바일 들기 목표를 장비가 직접 정하고 싶을 때(바닥에 끌리는 오일 받이 등) 설정한다.
    /// (HoldPoint 기준 목표 위치, 회전) → 실제 목표. '맨 앞에 그리기'가 필요 없으면 drawOnTopWhileHeld 도 함께 끈다.
    /// </summary>
    public System.Func<Vector3, Quaternion, Pose> PcHoldOverride;

    private void PCFlyTo(Vector3 targetPos, Quaternion targetRot)
    {
        _isTweening = true;
        DOTween.Kill(transform);

        if(_rb != null) _rb.isKinematic = true;

        // ★ 오프셋이 적용된 최종 목표 위치와 회전 계산
        Vector3 finalPos = targetPos + (targetRot * holdPositionOffset);
        // HoldPoint의 회전에 오브젝트 고유의 오프셋 회전을 결합
        Quaternion finalRot = targetRot * Quaternion.Euler(holdRotationOffset);
        bool overridden = PcHoldOverride != null;
        if(overridden) { var pose = PcHoldOverride(finalPos, finalRot); finalPos = pose.position; finalRot = pose.rotation; }

        Vector3 mid = (transform.position + finalPos) * 0.5f + Vector3.up * (overridden ? 0f : arcHeight);
        Vector3[] path = { mid, finalPos };

        Sequence seq = DOTween.Sequence();
        seq.Join(transform.DOPath(path, flyDuration, PathType.CatmullRom).SetEase(flyEase).SetOptions(false));
        seq.Join(transform.DORotateQuaternion(finalRot, flyDuration).SetEase(flyEase));
        seq.SetLink(gameObject);
        seq.OnComplete(() =>
        {
            _isTweening = false;
            _isPCHolding = true;
            OnGrabbed?.Invoke();
            InteractionEvents.FireItemGrabbed(GetPrefabId());
        });
    }

    private static float s_nextLaserCheck;

    private void LateUpdate()
    {
        // VR 플레이어가 물건보다 늦게 생성돼도 레이저 레이어가 맞춰지게 가끔 다시 확인한다
        if(Time.unscaledTime >= s_nextLaserCheck)
        {
            s_nextLaserCheck = Time.unscaledTime + 3f;
            EnsureLaserHitsLayer(-1);
        }
        UpdateDrawOnTop();
        FollowHoldPointNow();
        ReconcilePhysics();
    }

    // ── 물리 상태 보정 ──
    // VR 에서 같은 도구를 짧은 시간에 여러 번 잡았다 놓으면, 서버 SyncVar(_isKinematic)가 중간에 바뀌었다 돌아온 변화가
    // 한 틱에 합쳐져 OnChange 가 안 불리고, 내 쪽 Rigidbody 는 잡는 동안 만든 상태(키네마틱 아님·중력 꺼짐)로 남아
    // 놓은 도구가 중력 없이 공중을 떠다녔다. 손에 없는 물체는 0.5초마다 서버 상태에 다시 맞춘다.
    private float _nextPhysReconcile;
    private void ReconcilePhysics()
    {
        if(_rb == null || Time.time < _nextPhysReconcile) return;
        _nextPhysReconcile = Time.time + 0.5f;
        if(_serverSettling) return;   // 서버가 낙하 시뮬레이션 중

        CheckOrphanGrab();

        if(_isGrabbed.Value || _isTweening || _isPCHolding || _pendingHand != null) return;
        if(_grab != null && _grab.GetHeldBy().Count > 0) return;   // 지금 VR 손에 있음

        bool want = _isKinematic.Value;
        if(_rb.isKinematic != want) { ApplyKinematic(want); return; }
        if(!want && !_rb.useGravity) _rb.useGravity = true;         // 중력이 꺼진 채 떠 있는 경우
    }

    // ── 손에 없는데 '잡힘'으로 남은 물체 정리(VR) ──
    // 잡기 요청이 승인돼 소유권을 받았는데 AutoHand 잡기가 실패하면(손이 이미 다른 걸 잡음·손이 멀어짐 등)
    // 물체가 '잡힘 + 키네마틱 아님 + 중력 꺼짐' 상태로 남는다. 이때 손이 스치면 그 속도 그대로 무중력으로 날아가
    // 다시 돌아오지 않았다(드릴 분실). 소유·잡힘인데 1초 넘게 어느 손에도 없으면 보통의 놓기와 똑같이 처리한다.
    private float _orphanSince = -1f;
    private void CheckOrphanGrab()
    {
        bool orphan = IsOwner && _isGrabbed.Value && !IsHolstered && !_isPCHolding && !_isTweening
                      && _pendingHand == null && _onGranted == null && _grab != null && _grabCoroutine == null
                      && VrProbeHandling.IsVRDevice && _grab.GetHeldBy().Count == 0;
        if(!orphan) { _orphanSince = -1f; return; }
        if(_orphanSince < 0f) { _orphanSince = Time.time; return; }
        if(Time.time - _orphanSince < 1f) return;

        _orphanSince = -1f;
        Debug.LogWarning($"[SyncGrab] {name}: 손에 없는데 잡힘 상태로 남아 놓기 처리(무중력 표류 방지)");
        if(_rb != null && !_rb.isKinematic) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
        RequestRelease();
    }

    // ── PC·모바일 1인칭: 내가 든 도구와 내 장갑을 화면 맨 앞에 그린다(HeldItemOnTopFeature) ──
    // 차체 밑·벽에 도구를 대면 도구가 차체를 뚫고 잘려 보이던 문제. 위치·충돌은 그대로 두고 그리기 순서만 바꾼다.
    [Tooltip("PC·모바일에서 들고 있는 동안 내 화면에서는 이 물체와 내 장갑을 다른 물체보다 앞에 그린다(차체·벽 관통 방지).")]
    public bool drawOnTopWhileHeld = true;
    private bool _drawOnTop;
    private readonly System.Collections.Generic.List<Renderer> _onTopGloves = new System.Collections.Generic.List<Renderer>();

    private void OnDisable()
    {
        if(_drawOnTop) SetDrawOnTop(false);
    }

    private void UpdateDrawOnTop()
    {
        bool want = drawOnTopWhileHeld && _isPCHolding && IsOwner && holdPoint != null;
        if(!want && drawOnTopWhileHeld && IsOwner && IsVRMeterHeld()) want = true;
        if(want == _drawOnTop) return;
        SetDrawOnTop(want);
    }

    // VR: 멀티미터·메거는 단자를 재려고 배터리 팩 안쪽까지 손을 넣는데, 팩 본체에는 충돌체가 없어(안쪽 단자에 손이 닿아야 하므로)
    // 본체가 팩 메시에 묻혀 LCD 가 안 보였다. 계측기만 VR 에서도 맨 앞에 그린다. (다른 도구는 VR 에서 기존과 같다)
    private int _isMeter = -1;
    private bool IsVRMeterHeld()
    {
        if(_isPCHolding || _grab == null || !_grab.IsHeld()) return false;
        if(_isMeter < 0) _isMeter = GetComponentInChildren<MultimeterMaster>(true) != null ? 1 : 0;
        return _isMeter == 1;
    }

    private void SetDrawOnTop(bool on)
    {
        _drawOnTop = on;
        Debug.Log($"[HeldOnTop] {name} 맨 앞에 그리기 {(on ? "ON" : "OFF")}");
        HeldItemOnTop.Set(gameObject, on);

        if(on)
        {
            // 잡은 사람(내 플레이어)의 장갑 — 도구를 쥔 손이 도구 뒤로 잘려 보이지 않게 같이 앞에 그린다
            _onTopGloves.Clear();
            Transform root = holdPoint != null ? holdPoint.root : null;
            if(_isPCHolding && root != null)
            {
                foreach(var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if(smr.name.Contains("Glove")) _onTopGloves.Add(smr);
            }
            else if(_grab != null)
            {
                // VR: 계측기를 쥔 손 모델도 같이 앞에 그린다
                foreach(var h in _grab.GetHeldBy())
                    if(h != null)
                        foreach(var smr in h.GetComponentsInChildren<SkinnedMeshRenderer>(true)) _onTopGloves.Add(smr);
            }
        }
        foreach(var r in _onTopGloves) HeldItemOnTop.Set(r, on);
        if(!on) _onTopGloves.Clear();
    }

    private int _followFrame = -1;

    /// <summary>
    /// PC 홀드 중이면 이번 프레임의 HoldPoint 로 물체를 옮긴다(프레임당 1회).
    /// 아바타 손 IK(PcAvatarIKDriver, 실행 순서 -100)가 이 위치를 목표로 쓰므로, IK 가 먼저 불러서 '이번 프레임' 위치를 받게 한다.
    /// 예전에는 IK 가 지난 프레임 위치를 쫓아 손(과 손에 붙여 그리는 도구)이 카메라보다 한 프레임 늦어 1인칭에서 떨려 보였다.
    /// </summary>
    public void FollowHoldPointNow()
    {
        if(!_isPCHolding || holdPoint == null || !IsOwner) return;
        if(_followFrame == Time.frameCount) return;
        _followFrame = Time.frameCount;

        if(_rb != null) _rb.isKinematic = true;

        // ★ 실시간 추적 시에도 오프셋 적용
        Vector3 finalPos = holdPoint.position + (holdPoint.rotation * holdPositionOffset);
        Quaternion finalRot = holdPoint.rotation * Quaternion.Euler(holdRotationOffset);

        // 차체 아래 등 좁은 곳에서 물체가 메시를 뚫고 잘려 보이는 문제 방지
        finalPos = AvoidGeometry(finalPos);
        if(PcHoldOverride != null)
        {
            var pose = PcHoldOverride(finalPos, finalRot);
            finalPos = pose.position; finalRot = pose.rotation;
        }

        // 내 화면에서는 즉시 붙이고(지연 없음), 같은 목표를 LNT에 넘겨 서버·타 클라이언트로 전파한다.
        if(snapHoldToTarget)
            transform.SetPositionAndRotation(finalPos, finalRot);

        if(_lnt != null)
        {
            _lnt.SetTargetPosition(finalPos, finalRot);
        }
        else
        {
            transform.position = Vector3.Lerp(transform.position, finalPos, Time.deltaTime * 20f);
            transform.rotation = Quaternion.Slerp(transform.rotation, finalRot, Time.deltaTime * 20f);
        }
    }

    /// <summary>
    /// 카메라 → 목표 위치 사이에 장애물(차체·바닥·벽)이 있으면 목표를 카메라 쪽으로 끌어온다.
    /// 물체 크기만큼(_holdRadius)을 여유로 두고, minHoldDistance 보다 가깝게는 오지 않는다.
    /// </summary>
    private Vector3 AvoidGeometry(Vector3 desired)
    {
        if(!avoidGeometryClipping) return desired;
        // 맨 앞에 그리는 중이면 차체·배터리에 겹쳐도 보이므로 당기지 않는다.
        // 당기면 물체가 카메라 바로 앞(화면 밖)으로 와서 오히려 안 보였고(메거를 배터리에 댈 때), 프로브·드릴 끝이 단자·볼트에 닿지 못했다.
        if(_drawOnTop) { _avoidReduction = 0f; return desired; }

        var cam = Camera.main;
        if(cam == null) return desired;

        Vector3 origin = cam.transform.position;
        Vector3 to = desired - origin;
        float dist = to.magnitude;
        if(dist < 0.0001f) return desired;

        Vector3 dir = to / dist;
        if(_holdRadius < 0f) _holdRadius = ComputeHoldRadius();

        // 이번 프레임에 필요한 '당겨올 거리'. 장애물이 없으면 0.
        float targetReduction = 0f;

        int n = Physics.RaycastNonAlloc(origin, dir, s_clipBuf, dist + _holdRadius,
                                        ~0, QueryTriggerInteraction.Ignore);
        if(n > 0)
        {
            Transform playerRoot = holdPoint != null ? holdPoint.root : null;
            float nearest = float.MaxValue;

            for(int i = 0; i < n; i++)
            {
                var col = s_clipBuf[i].collider;
                if(col == null) continue;
                if(col.transform.IsChildOf(transform)) continue;                          // 자기 자신
                if(playerRoot != null && col.transform.IsChildOf(playerRoot)) continue;   // 내 플레이어

                if(s_clipBuf[i].distance < nearest) nearest = s_clipBuf[i].distance;
            }

            if(nearest != float.MaxValue)
            {
                float allowed = Mathf.Max(minHoldDistance, nearest - _holdRadius - geometryMargin);
                targetReduction = Mathf.Max(0f, dist - allowed);
            }
        }

        // 급격한 튐을 막기 위해 부드럽게 변화시킨다 (당겨질 때는 빠르게, 풀릴 때는 완만하게)
        float speed = targetReduction > _avoidReduction ? 30f : 12f;
        _avoidReduction = Mathf.Lerp(_avoidReduction, targetReduction, 1f - Mathf.Exp(-speed * Time.deltaTime));
        if(_avoidReduction < 0.001f) _avoidReduction = 0f;

        return _avoidReduction <= 0f ? desired : origin + dir * Mathf.Max(minHoldDistance, dist - _avoidReduction);
    }

    /// <summary>렌더러 바운드 기준 물체 반경(대략). 너무 크거나 작은 값은 제한.</summary>
    private float ComputeHoldRadius()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        if(renderers.Length == 0) return 0.1f;

        Bounds b = renderers[0].bounds;
        for(int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

        return Mathf.Clamp(b.extents.magnitude * 0.8f, 0.05f, 0.6f);
    }

    /// <summary>
    /// 외부(InteractablePart.SnapToOriginalPosition)에서 호출.
    /// holdPoint 추적을 즉시 멈추고 소유권을 서버에 반환.
    /// </summary>
    public void StopPCHold()
    {
        if(!_isPCHolding) return;
        _isPCHolding = false;
        _holdRadius = -1f;
        _avoidReduction = 0f;
        DOTween.Kill(transform);
        _lnt?.ClearTargetPosition();
    }

    // ══════════════════════════════════════════════════════
    // 공통 API
    // ══════════════════════════════════════════════════════

    public void RequestGrab(System.Action onGranted)
    {
        _wantCancel = false;
        _onGranted = onGranted;

        if(IsOwner)
        {
            _onGranted?.Invoke();
            _onGranted = null;
            return;
        }

        RequestGrabServerRpc(LocalConnection);
    }

    public void CancelGrabRequest()
    {
        _wantCancel = true;
        _onGranted = null;
        _pendingHand = null;
        StopCoroutineIfRunning(ref _grabCoroutine);
    }

    public void RequestRelease()
    {
        if(!IsOwner) return;

        _isPCHolding = false;
        _pendingHand = null;
        DOTween.Kill(transform);
        StopCoroutineIfRunning(ref _grabCoroutine);

        OnReleased?.Invoke();
        ReleaseServerRpc(transform.position, transform.rotation);
    }

    [Server]
    public void ForceDetach()
    {
        _isDespawning.Value = true;
        ServerForceReset();
        ForceDetachObserversRpc();
    }

    [ObserversRpc]
    private void ForceDetachObserversRpc()
    {
        DOTween.Kill(transform);
        _isPCHolding = false;
        _isTweening = false;
        _pendingHand = null;
        StopCoroutineIfRunning(ref _grabCoroutine);
        _grab?.ForceHandsRelease();
        OnReleased?.Invoke();   // 강제 해제도 알림
    }

    [Server]
    public void TriggerDisappear()
    {
        StartCoroutine(DisappearRoutine());
    }

    private IEnumerator DisappearRoutine()
    {
        _isDespawning.Value = true;
        ServerForceReset();

        Animator anim = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
        if(anim != null)
        {
            anim.SetTrigger("Disappear");
            yield return null;
            AnimatorClipInfo[] clips = anim.GetCurrentAnimatorClipInfo(0);
            float len = clips.Length > 0 ? clips[0].clip.length : 2f;
            yield return new WaitForSeconds(len);
        }
        else
        {
            yield return new WaitForSeconds(2f);
        }

        if(NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn();
    }

    /// <summary>[서버] 모든 예외 상황의 공통 복구 루틴.</summary>
    [Server]
    private void ServerForceReset()
    {
        _isGrabbed.Value = false;
        NetworkObject.GiveOwnership(null);
        ServerSetKinematic(true);
    }

    // ══════════════════════════════════════════════════════
    // ServerRpc
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// 소유권 요청.
    /// 허가 시: 소유권 부여 + Kinematic 해제 + GrantGrabTargetRpc.
    /// 거부 시: DenyGrabTargetRpc.
    /// Kinematic은 여기서만 해제 — 타이밍 역전 원천 차단.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestGrabServerRpc(NetworkConnection sender = null)
    {
        if(_isGrabbed.Value || _isDespawning.Value)
        {
            DenyGrabTargetRpc(sender);
            return;
        }

        _isGrabbed.Value = true;
        NetworkObject.GiveOwnership(sender);

        // 서버에서 Kinematic 해제 → SyncVar → 전체 클라 자동 전파
        ServerSetKinematic(false);

        GrantGrabTargetRpc(sender);
    }

    /// <summary>
    /// 점유 해제.
    /// RequireOwnership=true — Owner만 호출 가능.
    /// 위치 확정 + Kinematic 복구 + 소유권 반납.
    /// </summary>
    /// <summary>
    /// [VR 장착 스냅] 놓은 자리에 떨어뜨리지 않고 정해진 자세로 고정한 채 놓는다(소유권은 서버로).
    /// VrPlacementGhost 가 부품을 장착 위치로 끌어당긴 뒤 호출한다.
    /// </summary>
    public void ReleaseAndPlace(Vector3 position, Quaternion rotation)
    {
        if(!IsOwner) return;
        IsHolstered = false;
        DOTween.Kill(transform);
        OnReleased?.Invoke();
        PlaceServerRpc(position, rotation);
    }

    [ServerRpc(RequireOwnership = true)]
    private void PlaceServerRpc(Vector3 position, Quaternion rotation)
    {
        _isGrabbed.Value = false;
        StopServerSettle();
        transform.SetPositionAndRotation(position, rotation);
        _isKinematic.Value = true;
        ApplyKinematic(true);
        NetworkObject.GiveOwnership(null);
    }

    [ServerRpc(RequireOwnership = true)]
    private void ReleaseServerRpc(Vector3 position, Quaternion rotation)
    {
        _isGrabbed.Value = false;
        transform.SetPositionAndRotation(position, rotation);

        // 클라이언트에는 Kinematic(고정)으로 알리고, 서버에서만 중력으로 떨어뜨린 뒤 멈추면 고정한다(BeginServerSettle)
        StopServerSettle();
        _isKinematic.Value = true;
        NetworkObject.GiveOwnership(null);
        BeginServerSettle();
    }

    // ══════════════════════════════════════════════════════
    // TargetRpc
    // ══════════════════════════════════════════════════════

    [TargetRpc]
    private void GrantGrabTargetRpc(NetworkConnection target)
    {
        if(_wantCancel)
        {
            _wantCancel = false;
            _onGranted = null;
            _pendingHand = null;
            RequestRelease();
            return;
        }

        // PC/모바일
        if(_onGranted != null)
        {
            _onGranted.Invoke();
            _onGranted = null;
            return;
        }

        // VR: 저장된 손으로 TryGrab
        if(_pendingHand != null)
        {
            // 소유권이 확인될 때까지 대기하는 코루틴을 더 확실하게 실행
            StopCoroutineIfRunning(ref _grabCoroutine);
            _grabCoroutine = StartCoroutine(TryGrabAfterOwnership(_pendingHand));
        }
        else
        {
            // 손이 없으면 소유권 즉시 반납
            RequestRelease();
        }
    }

    /// <summary>
    /// 소유권 전파 완료 후 TryGrab.
    ///
    /// [Kinematic 처리]
    ///   RequestGrabServerRpc에서 이미 Kinematic=false로 설정 + SyncVar 전파.
    ///   여기서는 IsOwner 확정 대기만 하고 TryGrab 호출.
    ///
    /// [실패 처리]
    ///   hand가 null이거나 IsOwner가 2프레임 후에도 false면 소유권 반납.
    ///   TryGrab 자체의 성공/실패는 OnVRGrab/OnVRRelease 흐름에서 처리.
    /// </summary>
    private IEnumerator TryGrabAfterOwnership(Hand hand)
    {
        float timeout = 1.0f; // 네트워크 상황 고려하여 조금 넉넉히 대기
        float timer = 0f;

        // FishNet의 소유권 갱신을 확실히 기다림
        while(!IsOwner && timer < timeout)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        if(IsOwner && hand != null && _grab != null)
        {
            // 잡기 직전 물리 상태 리셋 (에러 방지 및 AutoHand 제어권 확보)
            _rb.isKinematic = false;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;

            // 한 프레임 더 쉬어서 물리 엔진이 Kinematic 해제를 인지하게 함
            yield return new WaitForFixedUpdate();

            hand.TryGrab(_grab);
            _pendingHand = null;
            _grabCoroutine = null;

            Debug.Log($"<color=lime>[SyncGrab]</color> {name} 소유권 획득 후 그랩 성공");
        }
        else
        {
            Debug.LogWarning($"<color=red>[SyncGrab]</color> {name} 그랩 실패 (Owner: {IsOwner}, Hand: {hand != null})");
            _grabCoroutine = null;
            RequestRelease();
        }
    }

    [TargetRpc]
    private void DenyGrabTargetRpc(NetworkConnection target)
    {
        _onGranted = null;
        _wantCancel = false;
        _pendingHand = null;
        StopCoroutineIfRunning(ref _grabCoroutine);
    }

    // ══════════════════════════════════════════════════════
    // SyncVar 콜백
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// 서버가 Kinematic 상태를 변경하면 모든 클라에 자동 적용.
    /// 이것이 유일한 Kinematic 변경 경로.
    /// </summary>
    private void OnKinematicChanged(bool prev, bool next, bool asServer)
    {
        ApplyKinematic(next);
    }

    private void OnIsGrabbedChanged(bool prev, bool next, bool asServer)
    {
        TrackHeld(next);
        // 누군가(다른 클라이언트 포함)가 잡았으면 이 화면의 '여기 있음' 가이드 마커도 치운다.
        // 예전에는 잡은 클라이언트에서만 치워서, 다른 사람 화면에는 마커가 잡기 전 자리에 계속 남았다.
        if(next) GuideSystem.Instance?.OnItemGrabbed(GetComponent<TaskItem>()?.prefabId ?? "");
        // 잡힘 상태가 바뀌면 물리 상태를 다시 맞춘다(소유자가 아닌 쪽은 잡힌 동안 kinematic, 놓으면 원래 값)
        ApplyKinematic(_isKinematic.Value);
        if(!_isKinematic.Value && _rb != null && !_rb.isKinematic)
        {
            _rb.useGravity = !next;
        }
    }

    private void OnIsDespawningChanged(bool prev, bool next, bool asServer)
    {
        if(!next) return;
        DOTween.Kill(transform);
        _isPCHolding = false;
        _isTweening = false;
        _pendingHand = null;
        StopCoroutineIfRunning(ref _grabCoroutine);
    }

    // ══════════════════════════════════════════════════════
    // 서버 유틸리티
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// 0.5초마다 좀비 소유권 감지.
    /// Owner가 끊겼는데 _isGrabbed=true면 ServerForceReset 호출.
    /// TryGrab 실패로 인한 Kinematic 방치도 여기서 복구.
    /// </summary>
    private IEnumerator ValidateOwnerRoutine()
    {
        var wait = new WaitForSeconds(0.5f);
        while(true)
        {
            yield return wait;
            if(!_isGrabbed.Value) continue;

            bool ownerGone = NetworkObject.Owner == null || !NetworkObject.Owner.IsValid;
            if(!ownerGone) continue;

            ServerForceReset();
        }
    }

    // ══════════════════════════════════════════════════════
    // 유틸리티
    // ══════════════════════════════════════════════════════

    private void StopCoroutineIfRunning(ref Coroutine coroutine)
    {
        if(coroutine == null) return;
        StopCoroutine(coroutine);
        coroutine = null;
    }

    /// <summary>TaskItem에서 prefabId 얻기</summary>
    // ── VR 거리 잡기 ─────────────────────────────────────────
    // VR 플레이어 오른손에는 AutoHand 거리 잡기(HandDistanceGrabber: 그립=레이저, 트리거=당겨 잡기)가 이미 있지만
    // 도구에 DistanceGrabbable 이 없어서 레이저가 아무것도 잡지 못했다. 바닥에 떨어뜨린 드릴을 다시 집을 방법이 없던 원인 중 하나.
    // 잡을 수 있는 물건마다 DistanceGrabbable 을 붙이고, 레이저가 닿으면 물건 위에 이름표를 띄운다.
    // 당겨 잡을 때도 손이 잡는 순간 OnBeforeGrab → 서버 소유권 요청 → 소유권 받은 뒤 잡기로 기존과 같은 경로를 탄다.
    private void EnsureDistanceGrab()
    {
        if(_grab == null || !IsHandheldItem()) return;
        EnsureLaserHitsLayer(_grab.gameObject.layer);
        var dg = _grab.GetComponent<DistanceGrabbable>();
        if(dg == null)
        {
            dg = _grab.gameObject.AddComponent<DistanceGrabbable>();
            dg.instantPull = true;
            dg.ignoreHighlights = true;
        }
        if(dg.StartTargeting == null) dg.StartTargeting = new UnityHandGrabEvent();
        if(dg.StopTargeting == null) dg.StopTargeting = new UnityHandGrabEvent();
        if(dg.StartSelecting == null) dg.StartSelecting = new UnityHandGrabEvent();
        if(dg.OnPull == null) dg.OnPull = new UnityHandGrabEvent();
        string label = GetPrefabId();
        // 안내 마커(빨간 아웃라인 + 이름 라벨)가 이미 붙은 도구는 이름표를 또 띄우지 않는다 — 라벨이 겹쳐 보였다
        dg.StartTargeting.AddListener((h, g) => { if(!_isGrabbed.Value && !(GuideSystem.Instance != null && GuideSystem.Instance.HasSpawnMarker(label))) ItemNameTag.Show(transform, label); });
        dg.StopTargeting.AddListener((h, g) => ItemNameTag.Hide(transform));
        dg.StartSelecting.AddListener((h, g) => ItemNameTag.Hide(transform));
        // 당기는 순간: AutoHand 의 Hand.Grab 은 Kinematic(내려놓아 고정된) 물체를 잡지 않으므로,
        // 여기서 먼저 서버에 소유권을 요청하고 물리를 풀어 둔다. 소유권을 받으면 TryGrabAfterOwnership 이 손으로 바로 잡는다.
        dg.OnPull.AddListener((h, g) =>
        {
            ItemNameTag.Hide(transform);
            if(_isGrabbed.Value || _isDespawning.Value) return;
            if(IsOwner) { if(_rb != null) { _rb.isKinematic = false; _rb.useGravity = false; } return; }
            OnBeforeGrab(h, g);
        });
        _grab.onGrab.AddListener((h, g) => ItemNameTag.Hide(transform));
    }

    /// <summary>
    /// 손 레이저(HandDistanceGrabber)가 이 물건의 레이어를 맞힐 수 있게 한다.
    /// 레이저는 레이어를 비워 두면 'Grabbable' 레이어만 검사하는데, 도구 프리팹(멀티미터·렌치·보호구 등)은 모두 'Interactable' 레이어라
    /// 레이저가 아무것도 맞히지 못해 거리 잡기가 전혀 되지 않았다.
    /// </summary>
    private static void EnsureLaserHitsLayer(int layer)
    {
        int add = LayerMask.GetMask(Hand.grabbableLayerNameDefault, "Interactable");
        if(layer >= 0 && layer < 32) add |= 1 << layer;
        foreach(var g in FindObjectsByType<HandDistanceGrabber>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if(g == null) continue;
            int cur = g.layers.value;
            if((cur & add) != add) g.layers = cur | add;
        }
    }

    [Tooltip("VR 손 레이저로 당겨 잡을 수 있는 최대 크기(m). 카트·리프트·배터리 커버처럼 큰 물체는 당겨 오지 않는다.")]
    public float distanceGrabMaxSize = 0.7f;

    /// <summary>손에 들고 다니는 도구인지(차량 부품·카트·리프트 제외).</summary>
    private bool IsHandheldItem()
    {
        if(GetComponent<TaskItem>() == null) return false;
        if(GetComponentInParent<VehiclePartsManager>() != null) return false;
        for(var t = transform.parent; t != null; t = t.parent)
            if(t.name.Contains("Ioniq")) return false;
        bool any = false; Bounds b = default;
        foreach(var r in GetComponentsInChildren<Renderer>(true))
        {
            if(r is ParticleSystemRenderer || r is LineRenderer || r.GetComponentInParent<Canvas>() != null) continue;
            if(r.GetComponentInParent<SyncGrab>() != this) continue;   // 프로브·케이블처럼 따로 잡는 자식은 크기에서 뺀다
            if(!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return any && Mathf.Max(b.size.x, b.size.y, b.size.z) <= distanceGrabMaxSize;
    }

    private string GetPrefabId()
    {
        if(_taskItem != null && !string.IsNullOrEmpty(_taskItem.prefabId))
        {
            return _taskItem.prefabId;
        }

        // Fallback: GameObject 이름에서 추출
        string name = gameObject.name.Replace("(Clone)", "").Trim();
        if(name.Contains("_"))
            return name.Split('_')[0];

        return name;
    }
}