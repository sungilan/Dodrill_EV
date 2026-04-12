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

    private TaskItem _taskItem;

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

        // holdPoint 자동 탐색 (인스펙터 미연결 시)
        if(holdPoint == null)
        {
            var hp = GameObject.FindWithTag("HoldPoint");
            if(hp != null) holdPoint = hp.transform;
            else Debug.LogWarning($"[SyncGrab] {name}: holdPoint 미연결 & Tag='HoldPoint' 오브젝트 없음");
        }

        // SyncVar 콜백 등록
        _isKinematic.OnChange += OnKinematicChanged;
        _isGrabbed.OnChange += OnIsGrabbedChanged;
        _isDespawning.OnChange += OnIsDespawningChanged;

        // 현재 서버 상태 즉시 적용
        ApplyKinematic(_isKinematic.Value);

        _grab = GetComponent<Grabbable>();
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
            _rb.useGravity = !_isGrabbed.Value;
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
        // 이미 Owner → 두 번째 손 포함 즉시 통과
        if(IsOwner) return;

        // 차단 조건
        if(_isTweening || _isGrabbed.Value || _isDespawning.Value)
        {
            hand.ForceReleaseGrab();
            return;
        }

        // 중복 소유권 요청 방지
        if(_pendingHand != null)
        {
            hand.ForceReleaseGrab();
            return;
        }

        _pendingHand = hand;
        hand.ForceReleaseGrab();
        RequestGrabServerRpc(LocalConnection);
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

        if (holdPoint == null)
        {
            // 1. 캐릭터 프리팹 내부나 씬에서 'HoldPoint' 태그를 가진 오브젝트 탐색
            var hp = GameObject.FindWithTag("HoldPoint");
            if (hp != null)
            {
                holdPoint = hp.transform;
                Debug.Log($"[SyncGrab] {name}: 클릭 시점에 holdPoint를 자동 탐색하여 할당했습니다.");
            }
            else
            {
                // 2. 태그로도 못 찾았을 경우 에러 로그를 남기고 중단 (최소한의 방어선)
                Debug.LogError($"[SyncGrab] {name}: 씬 내에 'HoldPoint' 태그를 가진 오브젝트가 없어 동작을 취소합니다.");
                return;
            }
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

    private void Update()
    {
        if(!_isPCHolding || holdPoint == null || !IsOwner) return;

        if(_rb != null) _rb.isKinematic = true;

        // ★ 실시간 추적 시에도 오프셋 적용
        Vector3 finalPos = holdPoint.position + (holdPoint.rotation * holdPositionOffset);
        Quaternion finalRot = holdPoint.rotation * Quaternion.Euler(holdRotationOffset);

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
    /// 외부(InteractablePart.SnapToOriginalPosition)에서 호출.
    /// holdPoint 추적을 즉시 멈추고 소유권을 서버에 반환.
    /// </summary>
    public void StopPCHold()
    {
        if(!_isPCHolding) return;
        _isPCHolding = false;
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
        transform.SetPositionAndRotation(position, rotation);
        _isGrabbed.Value = false;
        NetworkObject.GiveOwnership(null);

        // 서버에서 Kinematic 복구 → SyncVar → 전체 클라 자동 전파
        ServerSetKinematic(true);
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
        float timeout = 1.0f; // 최대 1초 대기
        float timer = 0f;

        // IsOwner가 true가 될 때까지 명확히 대기
        while(!IsOwner && timer < timeout)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        if(!IsOwner || hand == null)
        {
            Debug.LogWarning($"[SyncGrab] 소유권 획득 실패 혹은 손 유실. Release 요청.");
            _pendingHand = null;
            RequestRelease();
            yield break;
        }

        _pendingHand = null;
        hand.TryGrab(_grab);
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