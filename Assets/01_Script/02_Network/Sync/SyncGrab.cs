//using Autohand;
//using DG.Tweening;
//using FishNet.Connection;
//using FishNet.Object;
//using FishNet.Object.Synchronizing;
//using FishNet.Transporting;
//using Sirenix.OdinInspector;
//using System.Collections;
//using UnityEngine;

///// <summary>
///// 멀티플레이어 오브젝트 그랩 동기화 — 완전 서버 권위 구조.
/////
///// [설계 원칙]
/////   모든 상태 변경은 서버가 결정하고 클라이언트에 전파.
/////   클라이언트는 요청만 하고 서버 응답을 기다림.
/////   타이밍 문제 원천 차단을 위해 클라이언트 로컬 예측 없음.
/////
///// [상태 전이 — 서버만 변경]
/////   Fixed    : Kinematic=true,  Gravity=false
/////   Grabbing : Kinematic=false, Gravity=false
/////
///// [VR 그랩 흐름]
/////   1. 손이 닿음 → OnBeforeGrab
/////   2. 소유권 요청 → RequestGrabServerRpc
/////   3. 서버: 소유권 부여 + Kinematic 해제 + 전체 전파
/////   4. GrantGrabTargetRpc → 1~2프레임 대기 → TryGrab
/////   5. OnVRGrab (확인용만, 추가 서버 호출 없음)
/////   6. 손 놓음 → ReleaseServerRpc
/////   7. 서버: 위치 확정 + Kinematic 복구 + 전체 전파
/////
///// [핵심]
/////   Kinematic 변경을 RequestGrabServerRpc/ReleaseServerRpc에서만 처리.
/////   클라이언트에서 Kinematic을 직접 변경하지 않음.
/////   → 타이밍 역전, 좀비 상태 원천 차단.
///// </summary>
//public class SyncGrab : NetworkBehaviour
//{
//    // ──────────────────────────────────────────────────────
//    // 인스펙터
//    // ──────────────────────────────────────────────────────

//    [Header("PC/모바일 — 날아갈 위치")]
//    public Transform holdPoint;

//    [Header("DOTween")]
//    public float flyDuration = 0.5f;
//    public Ease flyEase = Ease.OutCubic;
//    public float arcHeight = 0.5f;

//    [Header("PC/모바일 — 잡기 자세 오프셋")]
//    [Tooltip("잡혔을 때 HoldPoint로부터의 위치 오프셋")]
//    public Vector3 holdPositionOffset = Vector3.zero;
//    [Tooltip("잡혔을 때 HoldPoint로부터의 회전 오프셋 (오브젝트 기준)")]
//    public Vector3 holdRotationOffset = Vector3.zero;

//    private TaskItem _taskItem;

//    // ──────────────────────────────────────────────────────
//    // SyncVar — 서버만 쓰기
//    // ──────────────────────────────────────────────────────

//    /// <summary>점유 중 여부. true면 다른 플레이어 차단.</summary>
//    private readonly SyncVar<bool> _isGrabbed = new(
//        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers));

//    /// <summary>현재 물리 상태. 서버가 변경하고 전체 전파.</summary>
//    private readonly SyncVar<bool> _isKinematic = new(
//        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers));

//    /// <summary>Despawn 대기 중.</summary>
//    private readonly SyncVar<bool> _isDespawning = new(
//        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers));

//    public bool IsGrabbed => _isGrabbed.Value;

//    /// <summary>holdPoint 도달 완료(PC) 또는 VR 집기 완료 시 발행</summary>
//    public event System.Action OnGrabbed;
//    /// <summary>내려놓기(PC RequestRelease / VR 손 놓음) 시 발행</summary>
//    public event System.Action OnReleased;

//    // ──────────────────────────────────────────────────────
//    // 컴포넌트
//    // ──────────────────────────────────────────────────────

//    private Rigidbody _rb;
//    private Grabbable _grab;
//    private LocalNetworkTransform _lnt;

//    // ──────────────────────────────────────────────────────
//    // 내부 상태 (클라이언트 로컬 — 요청 관리용)
//    // ──────────────────────────────────────────────────────

//    private bool _isTweening;
//    private bool _isPCHolding;
//    private bool _wantCancel;
//    private System.Action _onGranted;
//    private Hand _pendingHand;

//    // ──────────────────────────────────────────────────────
//    // 코루틴
//    // ──────────────────────────────────────────────────────

//    private Coroutine _validateCoroutine;
//    private Coroutine _grabCoroutine;

//    // ══════════════════════════════════════════════════════
//    // 생명주기
//    // ══════════════════════════════════════════════════════

//    public override void OnStartServer()
//    {
//        base.OnStartServer();
//        _rb = GetComponent<Rigidbody>();
//        _isKinematic.Value = true; // 초기값 명시적 설정
//        ApplyKinematic(true);
//        _validateCoroutine = StartCoroutine(ValidateOwnerRoutine());
//    }

//    public override void OnStopServer()
//    {
//        base.OnStopServer();
//        StopCoroutineIfRunning(ref _validateCoroutine);
//    }

//    public override void OnStartClient()
//    {
//        base.OnStartClient();
//        _rb = GetComponent<Rigidbody>();
//        _lnt = GetComponent<LocalNetworkTransform>();

//        _taskItem = GetComponent<TaskItem>();
//        if(_taskItem == null)
//        {
//            Debug.LogWarning($"[SyncGrab] {name}: TaskItem 컴포넌트 없음");
//        }

//        // holdPoint 자동 탐색 (인스펙터 미연결 시)
//        if(holdPoint == null)
//        {
//            var hp = GameObject.FindWithTag("HoldPoint");
//            if(hp != null) holdPoint = hp.transform;
//            else Debug.LogWarning($"[SyncGrab] {name}: holdPoint 미연결 & Tag='HoldPoint' 오브젝트 없음");
//        }

//        // SyncVar 콜백 등록
//        _isKinematic.OnChange += OnKinematicChanged;
//        _isGrabbed.OnChange += OnIsGrabbedChanged;
//        _isDespawning.OnChange += OnIsDespawningChanged;

//        // 현재 서버 상태 즉시 적용
//        ApplyKinematic(_isKinematic.Value);

//        _grab = GetComponentInChildren<Grabbable>();
//        if(_grab != null)
//        {
//            _grab.OnBeforeGrabEvent += OnBeforeGrab;
//            _grab.onGrab.AddListener(OnVRGrab);
//            _grab.onRelease.AddListener(OnVRRelease);
//        }
//        else
//        {
//            Debug.LogError($"[SyncGrab] {name} Grabbable 없음!");
//        }

//        NetworkObjectFinder.Instance?.Register(gameObject.name, gameObject);
//    }

//    public override void OnStopClient()
//    {
//        base.OnStopClient();

//        _isKinematic.OnChange -= OnKinematicChanged;
//        _isGrabbed.OnChange -= OnIsGrabbedChanged;
//        _isDespawning.OnChange -= OnIsDespawningChanged;

//        DOTween.Kill(transform);
//        _isPCHolding = false;
//        _isTweening = false;
//        _onGranted = null;
//        _pendingHand = null;
//        StopCoroutineIfRunning(ref _grabCoroutine);

//        if(_grab != null)
//        {
//            _grab.OnBeforeGrabEvent -= OnBeforeGrab;
//            _grab.onGrab.RemoveListener(OnVRGrab);
//            _grab.onRelease.RemoveListener(OnVRRelease);
//        }
//    }

//    public override void OnOwnershipServer(NetworkConnection prevOwner)
//    {
//        base.OnOwnershipServer(prevOwner);

//        bool ownerGone = NetworkObject.Owner == null || !NetworkObject.Owner.IsValid;
//        if(!_isGrabbed.Value || !ownerGone) return;

//        ServerForceReset();
//    }

//    // ══════════════════════════════════════════════════════
//    // Kinematic 적용
//    // ══════════════════════════════════════════════════════

//    /// <summary>Rigidbody에 Kinematic 상태 적용. 서버/클라 모두 사용.</summary>
//    private void ApplyKinematic(bool isKinematic)
//    {
//        if(_rb == null) return;

//        if(isKinematic)
//        {
//            // Kinematic으로 만들기 전 속도를 먼저 0으로 (에러 방지)
//            _rb.linearVelocity = Vector3.zero;
//            _rb.angularVelocity = Vector3.zero;
//            _rb.isKinematic = true;
//            _rb.useGravity = false;
//        }
//        else
//        {
//            _rb.isKinematic = false;
//            _rb.useGravity = true;
//        }
//    }

//    /// <summary>
//    /// [서버] Kinematic 상태 변경 + SyncVar 갱신.
//    /// SyncVar가 변경되면 OnKinematicChanged를 통해 전체 클라에 자동 전파.
//    /// </summary>
//    private void ServerSetKinematic(bool isKinematic)
//    {
//        _isKinematic.Value = isKinematic;
//        ApplyKinematic(isKinematic); // 서버 자신에게도 즉시 적용
//    }

//    // ══════════════════════════════════════════════════════
//    // VR AutoHand 콜백
//    // ══════════════════════════════════════════════════════

//    private void OnBeforeGrab(Hand hand, Grabbable g)
//    {
//        // 이미 Owner → 두 번째 손 포함 즉시 통과
//        if(IsOwner) return;

//        // 차단 조건
//        if(_isTweening || _isGrabbed.Value || _isDespawning.Value)
//        {
//            hand.ForceReleaseGrab();
//            return;
//        }

//        // 중복 소유권 요청 방지
//        if(_pendingHand != null)
//        {
//            hand.ForceReleaseGrab();
//            return;
//        }

//        _pendingHand = hand;
//        hand.ForceReleaseGrab();
//        RequestGrabServerRpc(LocalConnection);
//    }

//    /// <summary>
//    /// 그랩 완료 확인용. 실제 상태 변경은 RequestGrabServerRpc에서 이미 처리됨.
//    /// </summary>
//    private void OnVRGrab(Hand hand, Grabbable g)
//    {
//        _pendingHand = null;
//        StopCoroutineIfRunning(ref _grabCoroutine);

//        if(!IsOwner) return;

//        GuideSystem.Instance?.OnItemGrabbed(GetComponent<TaskItem>()?.prefabId ?? "");

//        if(_isDespawning.Value)
//        {
//            hand.ForceReleaseGrab();
//            OnGrabbed?.Invoke();
//            InteractionEvents.FireItemGrabbed(GetPrefabId());
//            return;
//        }

//        DOTween.Kill(transform);
//        // 상태는 이미 서버가 Grabbing으로 설정했으므로 추가 호출 없음
//    }
//    private void OnVRRelease(Hand hand, Grabbable g)
//    {
//        if(!IsOwner) return;

//        // 다른 손이 아직 잡고 있으면 무시
//        if(_grab != null && _grab.GetHeldBy().Count > 0) return;

//        OnReleased?.Invoke();   // ★ 내려놓기 → InteractablePart에 알림
//        ReleaseServerRpc(transform.position, transform.rotation);
//    }

//    // ══════════════════════════════════════════════════════
//    // PC / 모바일
//    // ══════════════════════════════════════════════════════

//    public void OnPCClick()
//    {
//        if(_isTweening) return;
//        if(_isDespawning.Value) return;

//        if(_isPCHolding)
//        {
//            RequestRelease();
//            return;
//        }

//        if(_isGrabbed.Value) return;

//        GuideSystem.Instance?.OnItemGrabbed(GetComponent<TaskItem>()?.prefabId ?? "");

//        if (holdPoint == null)
//        {
//            // 1. 캐릭터 프리팹 내부나 씬에서 'HoldPoint' 태그를 가진 오브젝트 탐색
//            var hp = GameObject.FindWithTag("HoldPoint");
//            if (hp != null)
//            {
//                holdPoint = hp.transform;
//                Debug.Log($"[SyncGrab] {name}: 클릭 시점에 holdPoint를 자동 탐색하여 할당했습니다.");
//            }
//            else
//            {
//                // 2. 태그로도 못 찾았을 경우 에러 로그를 남기고 중단 (최소한의 방어선)
//                Debug.LogError($"[SyncGrab] {name}: 씬 내에 'HoldPoint' 태그를 가진 오브젝트가 없어 동작을 취소합니다.");
//                return;
//            }
//        }

//        RequestGrab(() => PCFlyTo(holdPoint.position, holdPoint.rotation));
//    }

//    //private void PCFlyTo(Vector3 targetPos, Quaternion targetRot)
//    //{
//    //    _isTweening = true;
//    //    DOTween.Kill(transform);

//    //    if(_rb != null) _rb.isKinematic = true;

//    //    Vector3 mid = (transform.position + targetPos) * 0.5f + Vector3.up * arcHeight;
//    //    Vector3[] path = { mid, targetPos };

//    //    Sequence seq = DOTween.Sequence();
//    //    seq.Join(transform.DOPath(path, flyDuration, PathType.CatmullRom).SetEase(flyEase).SetOptions(false));
//    //    seq.Join(transform.DORotateQuaternion(targetRot, flyDuration).SetEase(flyEase));
//    //    seq.SetLink(gameObject);
//    //    seq.OnComplete(() =>
//    //    {
//    //        _isTweening = false;
//    //        _isPCHolding = true;
//    //        OnGrabbed?.Invoke();   // ★ holdPoint 도달 확정 → InteractablePart에 알림
//    //        //ReleaseServerRpc(transform.position, transform.rotation);
//    //    });
//    //}

//    private void PCFlyTo(Vector3 targetPos, Quaternion targetRot)
//    {
//        _isTweening = true;
//        DOTween.Kill(transform);

//        if(_rb != null) _rb.isKinematic = true;

//        // ★ 오프셋이 적용된 최종 목표 위치와 회전 계산
//        Vector3 finalPos = targetPos + (targetRot * holdPositionOffset);
//        // HoldPoint의 회전에 오브젝트 고유의 오프셋 회전을 결합
//        Quaternion finalRot = targetRot * Quaternion.Euler(holdRotationOffset);

//        Vector3 mid = (transform.position + finalPos) * 0.5f + Vector3.up * arcHeight;
//        Vector3[] path = { mid, finalPos };

//        Sequence seq = DOTween.Sequence();
//        seq.Join(transform.DOPath(path, flyDuration, PathType.CatmullRom).SetEase(flyEase).SetOptions(false));
//        seq.Join(transform.DORotateQuaternion(finalRot, flyDuration).SetEase(flyEase));
//        seq.SetLink(gameObject);
//        seq.OnComplete(() =>
//        {
//            _isTweening = false;
//            _isPCHolding = true;
//            OnGrabbed?.Invoke();
//            InteractionEvents.FireItemGrabbed(GetPrefabId());
//        });
//    }

//    //private void Update()
//    //{
//    //    // PC 홀드 중: LNT에 목표 위치 주입 → LNT가 Lerp 이동 + 서버 전파
//    //    // transform을 직접 건드리지 않음 — 다른 클라이언트 동기화는 LNT가 담당
//    //    if(!_isPCHolding || holdPoint == null || !IsOwner) return;

//    //    if(_rb != null) _rb.isKinematic = true;

//    //    if(_lnt != null)
//    //    {
//    //        _lnt.SetTargetPosition(holdPoint.position, holdPoint.rotation);
//    //    }
//    //    else
//    //    {
//    //        // LNT 없으면 폴백 (동기화 없음)
//    //        transform.position = Vector3.Lerp(
//    //            transform.position, holdPoint.position, Time.deltaTime * 20f);
//    //        transform.rotation = Quaternion.Slerp(
//    //            transform.rotation, holdPoint.rotation, Time.deltaTime * 20f);
//    //    }
//    //}

//    private void Update()
//    {
//        if(!_isPCHolding || holdPoint == null || !IsOwner) return;

//        if(_rb != null) _rb.isKinematic = true;

//        // ★ 실시간 추적 시에도 오프셋 적용
//        Vector3 finalPos = holdPoint.position + (holdPoint.rotation * holdPositionOffset);
//        Quaternion finalRot = holdPoint.rotation * Quaternion.Euler(holdRotationOffset);

//        if(_lnt != null)
//        {
//            _lnt.SetTargetPosition(finalPos, finalRot);
//        }
//        else
//        {
//            transform.position = Vector3.Lerp(transform.position, finalPos, Time.deltaTime * 20f);
//            transform.rotation = Quaternion.Slerp(transform.rotation, finalRot, Time.deltaTime * 20f);
//        }
//    }

//    /// <summary>
//    /// 외부(InteractablePart.SnapToOriginalPosition)에서 호출.
//    /// holdPoint 추적을 즉시 멈추고 소유권을 서버에 반환.
//    /// </summary>
//    public void StopPCHold()
//    {
//        if(!_isPCHolding) return;
//        _isPCHolding = false;
//        DOTween.Kill(transform);
//        _lnt?.ClearTargetPosition();
//    }

//    // ══════════════════════════════════════════════════════
//    // 공통 API
//    // ══════════════════════════════════════════════════════

//    public void RequestGrab(System.Action onGranted)
//    {
//        _wantCancel = false;
//        _onGranted = onGranted;

//        if(IsOwner)
//        {
//            _onGranted?.Invoke();
//            _onGranted = null;
//            return;
//        }

//        RequestGrabServerRpc(LocalConnection);
//    }

//    public void CancelGrabRequest()
//    {
//        _wantCancel = true;
//        _onGranted = null;
//        _pendingHand = null;
//        StopCoroutineIfRunning(ref _grabCoroutine);
//    }

//    public void RequestRelease()
//    {
//        if(!IsOwner) return;

//        _isPCHolding = false;
//        _pendingHand = null;
//        DOTween.Kill(transform);
//        StopCoroutineIfRunning(ref _grabCoroutine);

//        OnReleased?.Invoke();
//        ReleaseServerRpc(transform.position, transform.rotation);
//    }

//    [Server]
//    public void ForceDetach()
//    {
//        _isDespawning.Value = true;
//        ServerForceReset();
//        ForceDetachObserversRpc();
//    }

//    [ObserversRpc]
//    private void ForceDetachObserversRpc()
//    {
//        DOTween.Kill(transform);
//        _isPCHolding = false;
//        _isTweening = false;
//        _pendingHand = null;
//        StopCoroutineIfRunning(ref _grabCoroutine);
//        _grab?.ForceHandsRelease();
//        OnReleased?.Invoke();   // 강제 해제도 알림
//    }

//    [Server]
//    public void TriggerDisappear()
//    {
//        StartCoroutine(DisappearRoutine());
//    }

//    private IEnumerator DisappearRoutine()
//    {
//        _isDespawning.Value = true;
//        ServerForceReset();

//        Animator anim = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
//        if(anim != null)
//        {
//            anim.SetTrigger("Disappear");
//            yield return null;
//            AnimatorClipInfo[] clips = anim.GetCurrentAnimatorClipInfo(0);
//            float len = clips.Length > 0 ? clips[0].clip.length : 2f;
//            yield return new WaitForSeconds(len);
//        }
//        else
//        {
//            yield return new WaitForSeconds(2f);
//        }

//        if(NetworkObject != null && NetworkObject.IsSpawned)
//            NetworkObject.Despawn();
//    }

//    /// <summary>[서버] 모든 예외 상황의 공통 복구 루틴.</summary>
//    [Server]
//    private void ServerForceReset()
//    {
//        _isGrabbed.Value = false;
//        NetworkObject.GiveOwnership(null);
//        ServerSetKinematic(true);
//    }

//    // ══════════════════════════════════════════════════════
//    // ServerRpc
//    // ══════════════════════════════════════════════════════

//    /// <summary>
//    /// 소유권 요청.
//    /// 허가 시: 소유권 부여 + Kinematic 해제 + GrantGrabTargetRpc.
//    /// 거부 시: DenyGrabTargetRpc.
//    /// Kinematic은 여기서만 해제 — 타이밍 역전 원천 차단.
//    /// </summary>
//    [ServerRpc(RequireOwnership = false)]
//    private void RequestGrabServerRpc(NetworkConnection sender = null)
//    {
//        if(_isGrabbed.Value || _isDespawning.Value)
//        {
//            DenyGrabTargetRpc(sender);
//            return;
//        }

//        _isGrabbed.Value = true;
//        NetworkObject.GiveOwnership(sender);

//        // 서버에서 Kinematic 해제 → SyncVar → 전체 클라 자동 전파
//        ServerSetKinematic(false);

//        GrantGrabTargetRpc(sender);
//    }

//    /// <summary>
//    /// 점유 해제.
//    /// RequireOwnership=true — Owner만 호출 가능.
//    /// 위치 확정 + Kinematic 복구 + 소유권 반납.
//    /// </summary>
//    [ServerRpc(RequireOwnership = true)]
//    private void ReleaseServerRpc(Vector3 position, Quaternion rotation)
//    {
//        _isGrabbed.Value = false;
//        transform.SetPositionAndRotation(position, rotation);

//        // 즉시 Kinematic을 켜지 말고, 한 프레임 뒤에 켜서 
//        // 클라이언트들이 위치를 잡을 시간을 줌 (선택 사항)
//        ServerSetKinematic(true);
//        NetworkObject.GiveOwnership(null);
//    }

//    // ══════════════════════════════════════════════════════
//    // TargetRpc
//    // ══════════════════════════════════════════════════════

//    [TargetRpc]
//    private void GrantGrabTargetRpc(NetworkConnection target)
//    {
//        if(_wantCancel)
//        {
//            _wantCancel = false;
//            _onGranted = null;
//            _pendingHand = null;
//            RequestRelease();
//            return;
//        }

//        // PC/모바일
//        if(_onGranted != null)
//        {
//            _onGranted.Invoke();
//            _onGranted = null;
//            return;
//        }

//        // VR: 저장된 손으로 TryGrab
//        if(_pendingHand != null)
//        {
//            // 소유권이 확인될 때까지 대기하는 코루틴을 더 확실하게 실행
//            StopCoroutineIfRunning(ref _grabCoroutine);
//            _grabCoroutine = StartCoroutine(TryGrabAfterOwnership(_pendingHand));
//        }
//        else
//        {
//            // 손이 없으면 소유권 즉시 반납
//            RequestRelease();
//        }
//    }

//    /// <summary>
//    /// 소유권 전파 완료 후 TryGrab.
//    ///
//    /// [Kinematic 처리]
//    ///   RequestGrabServerRpc에서 이미 Kinematic=false로 설정 + SyncVar 전파.
//    ///   여기서는 IsOwner 확정 대기만 하고 TryGrab 호출.
//    ///
//    /// [실패 처리]
//    ///   hand가 null이거나 IsOwner가 2프레임 후에도 false면 소유권 반납.
//    ///   TryGrab 자체의 성공/실패는 OnVRGrab/OnVRRelease 흐름에서 처리.
//    /// </summary>
//    private IEnumerator TryGrabAfterOwnership(Hand hand)
//    {
//        float timeout = 0.5f;
//        float timer = 0f;

//        while(!IsOwner && timer < timeout)
//        {
//            timer += Time.deltaTime;
//            yield return null;
//        }

//        if(IsOwner && hand != null && _grab != null)
//        {
//            // 1. 가장 권장되는 방식
//            hand.TryGrab(_grab);

//            // 2. 만약 위 함수가 안 된다면 (구버전 또는 특정 커스텀)
//            // hand.Grab(_grab);

//            _pendingHand = null;
//        }
//        else
//        {
//            RequestRelease();
//        }
//    }

//    [TargetRpc]
//    private void DenyGrabTargetRpc(NetworkConnection target)
//    {
//        _onGranted = null;
//        _wantCancel = false;
//        _pendingHand = null;
//        StopCoroutineIfRunning(ref _grabCoroutine);
//    }

//    // ══════════════════════════════════════════════════════
//    // SyncVar 콜백
//    // ══════════════════════════════════════════════════════

//    /// <summary>
//    /// 서버가 Kinematic 상태를 변경하면 모든 클라에 자동 적용.
//    /// 이것이 유일한 Kinematic 변경 경로.
//    /// </summary>
//    private void OnKinematicChanged(bool prev, bool next, bool asServer)
//    {
//        ApplyKinematic(next);
//    }

//    private void OnIsGrabbedChanged(bool prev, bool next, bool asServer) 
//    {
//        // 잡힘 상태가 변할 때, 만약 Kinematic이 풀려있다면 중력 상태를 즉시 동기화합니다.
//        if(!_isKinematic.Value && _rb != null)
//        {
//            _rb.useGravity = !next;
//        }
//    }

//    private void OnIsDespawningChanged(bool prev, bool next, bool asServer)
//    {
//        if(!next) return;
//        DOTween.Kill(transform);
//        _isPCHolding = false;
//        _isTweening = false;
//        _pendingHand = null;
//        StopCoroutineIfRunning(ref _grabCoroutine);
//    }

//    // ══════════════════════════════════════════════════════
//    // 서버 유틸리티
//    // ══════════════════════════════════════════════════════

//    /// <summary>
//    /// 0.5초마다 좀비 소유권 감지.
//    /// Owner가 끊겼는데 _isGrabbed=true면 ServerForceReset 호출.
//    /// TryGrab 실패로 인한 Kinematic 방치도 여기서 복구.
//    /// </summary>
//    private IEnumerator ValidateOwnerRoutine()
//    {
//        var wait = new WaitForSeconds(0.5f);
//        while(true)
//        {
//            yield return wait;
//            if(!_isGrabbed.Value) continue;

//            bool ownerGone = NetworkObject.Owner == null || !NetworkObject.Owner.IsValid;
//            if(!ownerGone) continue;

//            ServerForceReset();
//        }
//    }

//    // ══════════════════════════════════════════════════════
//    // 유틸리티
//    // ══════════════════════════════════════════════════════

//    private void StopCoroutineIfRunning(ref Coroutine coroutine)
//    {
//        if(coroutine == null) return;
//        StopCoroutine(coroutine);
//        coroutine = null;
//    }

//    /// <summary>TaskItem에서 prefabId 얻기</summary>
//    private string GetPrefabId()
//    {
//        if(_taskItem != null && !string.IsNullOrEmpty(_taskItem.prefabId))
//        {
//            return _taskItem.prefabId;
//        }

//        // Fallback: GameObject 이름에서 추출
//        string name = gameObject.name.Replace("(Clone)", "").Trim();
//        if(name.Contains("_"))
//            return name.Split('_')[0];

//        return name;
//    }
//}
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
    private Renderer[] _gripRenderers;

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
        _rb = GetComponent<Rigidbody>();
        _isKinematic.Value = true; // 초기값 명시적 설정
        ApplyKinematic(true);
        _validateCoroutine = StartCoroutine(ValidateOwnerRoutine());
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
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

    // ══════════════════════════════════════════════════════
    // Kinematic 적용
    // ══════════════════════════════════════════════════════

    /// <summary>Rigidbody에 Kinematic 상태 적용. 서버/클라 모두 사용.</summary>
    private void ApplyKinematic(bool isKinematic)
    {
        if(_rb == null) return;

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
        _isKinematic.Value = isKinematic;
        ApplyKinematic(isKinematic); // 서버 자신에게도 즉시 적용
    }

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
    private void OnVRRelease(Hand hand, Grabbable g)
    {
        if(!IsOwner) return;

        // 다른 손이 아직 잡고 있으면 무시
        if(_grab != null && _grab.GetHeldBy().Count > 0) return;

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

    //private void PCFlyTo(Vector3 targetPos, Quaternion targetRot)
    //{
    //    _isTweening = true;
    //    DOTween.Kill(transform);

    //    if(_rb != null) _rb.isKinematic = true;

    //    Vector3 mid = (transform.position + targetPos) * 0.5f + Vector3.up * arcHeight;
    //    Vector3[] path = { mid, targetPos };

    //    Sequence seq = DOTween.Sequence();
    //    seq.Join(transform.DOPath(path, flyDuration, PathType.CatmullRom).SetEase(flyEase).SetOptions(false));
    //    seq.Join(transform.DORotateQuaternion(targetRot, flyDuration).SetEase(flyEase));
    //    seq.SetLink(gameObject);
    //    seq.OnComplete(() =>
    //    {
    //        _isTweening = false;
    //        _isPCHolding = true;
    //        OnGrabbed?.Invoke();   // ★ holdPoint 도달 확정 → InteractablePart에 알림
    //        //ReleaseServerRpc(transform.position, transform.rotation);
    //    });
    //}

    private void PCFlyTo(Vector3 targetPos, Quaternion targetRot)
    {
        _isTweening = true;
        DOTween.Kill(transform);

        if(_rb != null) _rb.isKinematic = true;

        // ★ 오프셋이 적용된 최종 목표 위치와 회전 계산
        Vector3 finalPos = targetPos + (targetRot * holdPositionOffset);
        // HoldPoint의 회전에 오브젝트 고유의 오프셋 회전을 결합
        Quaternion finalRot = targetRot * Quaternion.Euler(holdRotationOffset);

        Vector3 mid = (transform.position + finalPos) * 0.5f + Vector3.up * arcHeight;
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

    //private void Update()
    //{
    //    // PC 홀드 중: LNT에 목표 위치 주입 → LNT가 Lerp 이동 + 서버 전파
    //    // transform을 직접 건드리지 않음 — 다른 클라이언트 동기화는 LNT가 담당
    //    if(!_isPCHolding || holdPoint == null || !IsOwner) return;

    //    if(_rb != null) _rb.isKinematic = true;

    //    if(_lnt != null)
    //    {
    //        _lnt.SetTargetPosition(holdPoint.position, holdPoint.rotation);
    //    }
    //    else
    //    {
    //        // LNT 없으면 폴백 (동기화 없음)
    //        transform.position = Vector3.Lerp(
    //            transform.position, holdPoint.position, Time.deltaTime * 20f);
    //        transform.rotation = Quaternion.Slerp(
    //            transform.rotation, holdPoint.rotation, Time.deltaTime * 20f);
    //    }
    //}

    // LateUpdate: 카메라 회전·이동(FreeLookController.Update)이 모두 끝난 뒤의 HoldPoint 위치를 사용해
    // '한 프레임 늦게 따라오는' 현상을 없앤다.
    private void LateUpdate()
    {
        if(!_isPCHolding || holdPoint == null || !IsOwner) return;

        if(_rb != null) _rb.isKinematic = true;

        // ★ 실시간 추적 시에도 오프셋 적용
        Vector3 finalPos = holdPoint.position + (holdPoint.rotation * holdPositionOffset);
        Quaternion finalRot = holdPoint.rotation * Quaternion.Euler(holdRotationOffset);

        // 차체 아래 등 좁은 곳에서 물체가 메시를 뚫고 잘려 보이는 문제 방지
        finalPos = AvoidGeometry(finalPos);

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
    [ServerRpc(RequireOwnership = true)]
    private void ReleaseServerRpc(Vector3 position, Quaternion rotation)
    {
        _isGrabbed.Value = false;
        transform.SetPositionAndRotation(position, rotation);

        // 즉시 Kinematic을 켜지 말고, 한 프레임 뒤에 켜서 
        // 클라이언트들이 위치를 잡을 시간을 줌 (선택 사항)
        ServerSetKinematic(true);
        NetworkObject.GiveOwnership(null);
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

            Debug.Log($"<color=lime>[SyncGrab]</color> {name} 소유권 획득 후 그랩 성공");
        }
        else
        {
            Debug.LogWarning($"<color=red>[SyncGrab]</color> {name} 그랩 실패 (Owner: {IsOwner}, Hand: {hand != null})");
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
        // 잡힘 상태가 변할 때, 만약 Kinematic이 풀려있다면 중력 상태를 즉시 동기화합니다.
        if(!_isKinematic.Value && _rb != null)
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