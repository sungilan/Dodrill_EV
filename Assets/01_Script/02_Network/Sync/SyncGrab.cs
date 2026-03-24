using Autohand;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;
using System.Collections;
using DG.Tweening;

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

    // ──────────────────────────────────────────────────────
    // 컴포넌트
    // ──────────────────────────────────────────────────────

    private Rigidbody _rb;
    private Grabbable _grab;

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

        // SyncVar 콜백 등록
        _isKinematic.OnChange += OnKinematicChanged;
        _isGrabbed.OnChange += OnIsGrabbedChanged;
        _isDespawning.OnChange += OnIsDespawningChanged;

        // 현재 서버 상태 즉시 적용
        ApplyKinematic(_isKinematic.Value);

        _grab = GetComponent<Grabbable>();
        if (_grab != null)
        {
            _grab.OnBeforeGrabEvent += OnBeforeGrab;
            _grab.onGrab.AddListener(OnVRGrab);
            _grab.onRelease.AddListener(OnVRRelease);
        }
        else
        {
            Debug.LogError($"[SyncGrab] {name} Grabbable 없음!");
        }
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

        if (_grab != null)
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
        if (!_isGrabbed.Value || !ownerGone) return;

        ServerForceReset();
    }

    // ══════════════════════════════════════════════════════
    // Kinematic 적용
    // ══════════════════════════════════════════════════════

    /// <summary>Rigidbody에 Kinematic 상태 적용. 서버/클라 모두 사용.</summary>
    private void ApplyKinematic(bool isKinematic)
    {
        if (_rb == null) return;
        _rb.isKinematic = isKinematic;
        _rb.useGravity = false;
        if (isKinematic)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
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
        if (IsOwner) return;

        // 차단 조건
        if (_isTweening || _isGrabbed.Value || _isDespawning.Value)
        {
            hand.ForceReleaseGrab();
            return;
        }

        // 중복 소유권 요청 방지
        if (_pendingHand != null)
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

        if (!IsOwner) return;

        if (_isDespawning.Value)
        {
            hand.ForceReleaseGrab();
            return;
        }

        DOTween.Kill(transform);
        // 상태는 이미 서버가 Grabbing으로 설정했으므로 추가 호출 없음
    }

    private void OnVRRelease(Hand hand, Grabbable g)
    {
        if (!IsOwner) return;

        // 다른 손이 아직 잡고 있으면 무시
        if (_grab != null && _grab.GetHeldBy().Count > 0) return;

        ReleaseServerRpc(transform.position, transform.rotation);
    }

    // ══════════════════════════════════════════════════════
    // PC / 모바일
    // ══════════════════════════════════════════════════════

    public void OnPCClick()
    {
        if (_isTweening) return;
        if (_isDespawning.Value) return;

        if (_isPCHolding)
        {
            RequestRelease();
            return;
        }

        if (_isGrabbed.Value) return;

        if (holdPoint == null)
        {
            Debug.LogWarning($"[SyncGrab] {name}: holdPoint 없음");
            return;
        }

        RequestGrab(() => PCFlyTo(holdPoint.position, holdPoint.rotation));
    }

    private void PCFlyTo(Vector3 targetPos, Quaternion targetRot)
    {
        _isTweening = true;
        DOTween.Kill(transform);

        Vector3 mid = (transform.position + targetPos) * 0.5f + Vector3.up * arcHeight;
        Vector3[] path = { mid, targetPos };

        Sequence seq = DOTween.Sequence();
        seq.Join(transform.DOPath(path, flyDuration, PathType.CatmullRom).SetEase(flyEase).SetOptions(false));
        seq.Join(transform.DORotateQuaternion(targetRot, flyDuration).SetEase(flyEase));
        seq.SetLink(gameObject);
        seq.OnComplete(() =>
        {
            _isTweening = false;
            _isPCHolding = true;
            ReleaseServerRpc(transform.position, transform.rotation);
        });
    }

    // ══════════════════════════════════════════════════════
    // 공통 API
    // ══════════════════════════════════════════════════════

    public void RequestGrab(System.Action onGranted)
    {
        _wantCancel = false;
        _onGranted = onGranted;

        if (IsOwner)
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
        if (!IsOwner) return;

        _isPCHolding = false;
        _pendingHand = null;
        DOTween.Kill(transform);
        StopCoroutineIfRunning(ref _grabCoroutine);
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
        if (anim != null)
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

        if (NetworkObject != null && NetworkObject.IsSpawned)
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
        if (_isGrabbed.Value || _isDespawning.Value)
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
        if (_wantCancel)
        {
            _wantCancel = false;
            _onGranted = null;
            _pendingHand = null;
            RequestRelease();
            return;
        }

        // PC/모바일
        if (_onGranted != null)
        {
            _onGranted.Invoke();
            _onGranted = null;
            return;
        }

        // VR: 저장된 손으로 TryGrab
        if (_pendingHand != null)
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
        if (hand == null)
        {
            RequestRelease();
            yield break;
        }

        // 소유권 전파 완료 대기 (최대 2프레임)
        yield return null;
        if (!IsOwner) yield return null;

        if (!IsOwner)
        {
            _pendingHand = null;
            RequestRelease();
            yield break;
        }

        if (hand == null)
        {
            _pendingHand = null;
            RequestRelease();
            yield break;
        }

        _pendingHand = null;
        hand.TryGrab(_grab);
        // TryGrab 성공 → OnVRGrab 호출 → 정상 흐름
        // TryGrab 실패 → OnVRGrab 미호출 → ValidateOwnerRoutine이 0.5초 내 복구
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

    private void OnIsGrabbedChanged(bool prev, bool next, bool asServer) { }

    private void OnIsDespawningChanged(bool prev, bool next, bool asServer)
    {
        if (!next) return;
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
        while (true)
        {
            yield return wait;
            if (!_isGrabbed.Value) continue;

            bool ownerGone = NetworkObject.Owner == null || !NetworkObject.Owner.IsValid;
            if (!ownerGone) continue;

            ServerForceReset();
        }
    }

    // ══════════════════════════════════════════════════════
    // 유틸리티
    // ══════════════════════════════════════════════════════

    private void StopCoroutineIfRunning(ref Coroutine coroutine)
    {
        if (coroutine == null) return;
        StopCoroutine(coroutine);
        coroutine = null;
    }
}