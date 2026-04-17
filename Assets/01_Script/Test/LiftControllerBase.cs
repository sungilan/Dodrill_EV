using UnityEngine;
using FishNet;
using FishNet.Broadcast;
using FishNet.Transporting;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using TMPro;
using System.Collections;

// ============================================================
//  LiftControllerBase.cs
//  모든 리프트 컨트롤러의 공통 베이스 클래스.
//
//  공통 기능:
//    - TransformBased / AnimatorBased 모드 분기
//    - 버튼 이벤트 (OnUpButton / OnDownButton / OnStopButton)
//    - FishNet ServerRpc 네트워크 동기화
//    - 감속(slowZone), 오디오, UI 인디케이터
//    - platformBone 기반 탑재 오브젝트 추적 (LateUpdate)
//
//  자식 클래스:
//    VehicleLiftController  — 차량 리프트 (암 2개 이동)
//    BatteryLiftController  — 배터리 잭 (contactHeight 자동 연결)
// ============================================================

public struct LiftStateBroadcast : IBroadcast
{
    public string id;
    public float height;
    public bool isMoving;
    public int direction;
}

public abstract class LiftControllerBase : NetworkBehaviour, ILiftController
{
    // ── 리프트 모드 ────────────────────────────

    public enum LiftMode { TransformBased, AnimatorBased }

    [Header("리프트 모드")]
    [Tooltip("TransformBased: Transform 직접 이동(LNT 타겟 지정) / AnimatorBased: Animator 블렌드트리 제어")]
    public LiftMode liftMode = LiftMode.TransformBased;

    // ── 공통 ───────────────────────────────────

    [Header("식별")]
    public string uniqueId = "Lift_01";

    [Header("리프트 설정")]
    [Tooltip("속도. TransformBased: m/s, AnimatorBased: normalized/s")]
    public float liftSpeed = 0.4f;
    [Tooltip("상하단 감속 구간. TransformBased: m, AnimatorBased: normalized")]
    public float slowZone = 0.15f;

    [Header("UI (선택)")]
    public TextMeshProUGUI heightDisplay;
    public GameObject upIndicator;
    public GameObject downIndicator;
    public AudioSource liftAudio;

    // ── AnimatorBased 공통 ────────────────────

    [Header("── AnimatorBased 공통 ──")]
    [Tooltip("Animator 컴포넌트 (없으면 자신의 Animator 사용)")]
    public Animator liftAnimator;
    [Tooltip("블렌드트리 float 파라미터 이름")]
    public string liftParamName = "LiftHeight";
    [Tooltip("탑재 오브젝트가 따라갈 상판 본(bone)")]
    public Transform platformBone;

    // ── 보호 상태 (자식 접근 가능) ─────────────

    private readonly SyncVar<float> _syncCurrentHeight = new SyncVar<float>();
    private readonly SyncVar<int> _syncDirection = new SyncVar<int>();

    protected float _currentHeight => _syncCurrentHeight.Value;
    protected int _direction => _syncDirection.Value;
    protected bool _isMoving => _direction != 0;

    // ── 내부 캐시 ─────────────────────────────

    private int _liftParamHash;
    protected bool _isInitialized = false;

    // 모드별 최대값
    protected float MaxValue => liftMode == LiftMode.TransformBased ? GetMaxHeight() : 1f;

    // ═══════════════════════════════════════════
    //  추상/가상 메서드 — 자식에서 구현
    // ═══════════════════════════════════════════

    protected abstract float GetMaxHeight();
    protected abstract void ApplyHeightTransform(float h);
    protected abstract void SyncPayloadLateUpdate();

    protected virtual void OnLiftStart() { }
    protected virtual void OnHeightChanged(float newHeight, float prevHeight) { }
    protected virtual void OnReachedBottom() { }
    protected virtual void OnReachedTop() { }

    protected virtual string GetHeightDisplayText()
    {
        return liftMode == LiftMode.TransformBased
            ? $"{_currentHeight:F2} m"
            : $"{_currentHeight * 100f:F0} %";
    }

    // ═══════════════════════════════════════════
    //  생명주기
    // ═══════════════════════════════════════════

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        Debug.Log($"[LiftBase] OnStartNetwork 호출 - ID: {uniqueId} | IsServer: {IsServerInitialized} | IsClient: {IsClientInitialized}");

        if(IsServerInitialized)
        {
            Debug.Log($"[LiftBase] ★ 서버 초기화 시작 - ID: {uniqueId}");

            if(liftMode == LiftMode.AnimatorBased)
            {
                if(liftAnimator == null)
                    liftAnimator = GetComponent<Animator>();
                if(liftAnimator != null)
                    _liftParamHash = Animator.StringToHash(liftParamName);
                else
                    Debug.LogWarning($"[{GetType().Name}] Animator를 찾을 수 없습니다. ({gameObject.name})");
            }

            OnLiftStart();
            _isInitialized = true;
            UpdateUI();

            Debug.Log($"[LiftBase] ★ 서버 초기화 완료 - ID: {uniqueId}");
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        Debug.Log($"[LiftBase] OnStartClient 호출 - ID: {uniqueId}");

        _syncCurrentHeight.OnChange += OnSyncHeightChanged;
        _syncDirection.OnChange += OnSyncDirectionChanged;

        _isInitialized = true;

        // 클라이언트도 vehicleTransform 탐색 등 초기화 필요
        if(!IsServerInitialized)
        {
            if(liftMode == LiftMode.AnimatorBased)
            {
                if(liftAnimator == null) liftAnimator = GetComponent<Animator>();
                if(liftAnimator != null) _liftParamHash = Animator.StringToHash(liftParamName);
            }
            OnLiftStart();
        }

        UpdateUI();
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        Debug.Log($"[LiftBase] OnStopServer 호출 - ID: {uniqueId}");
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        Debug.Log($"[LiftBase] OnStopClient 호출 - ID: {uniqueId}");
        StopAllCoroutines();
        _syncCurrentHeight.OnChange -= OnSyncHeightChanged;
        _syncDirection.OnChange -= OnSyncDirectionChanged;
    }

    private void Update()
    {
        // 서버: 높이 계산 권한자
        // Owner(버튼 누른 클라이언트): SyncGrab 방식으로 직접 제어
        bool canUpdate = IsServerInitialized || IsOwner;
        if(!canUpdate) return;

        if(!_isMoving) return;

        float prevHeight = _currentHeight;
        float speed = CalculateSpeed();

        float movement = _direction * speed * Time.deltaTime;
        float newHeight = Mathf.Clamp(_currentHeight + movement, 0f, MaxValue);

        bool reachedEdge = newHeight <= 0f || newHeight >= MaxValue;

        if(reachedEdge)
        {
            bool wasAtBottom = newHeight <= 0f;
            newHeight = Mathf.Clamp(newHeight, 0f, MaxValue);

            // SyncVar는 서버만 직접 쓸 수 있음
            if(IsServerInitialized)
                _syncDirection.Value = 0;
            else
                StopMoveServerRpc();

            if(liftAudio != null) liftAudio.Stop();

            if(wasAtBottom) OnReachedBottom();
            else OnReachedTop();
        }

        // 높이 저장: 서버는 직접, Owner 클라이언트는 서버에 전달
        if(IsServerInitialized)
            _syncCurrentHeight.Value = newHeight;
        else
            SyncHeightToServerRpc(newHeight);

        // 로컬 시각 즉시 반영 (SyncVar 콜백 대기 없이)
        ApplyHeight(newHeight);
        OnHeightChanged(newHeight, prevHeight);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestMoveServerRpc(int dir)
    {
        if(!IsServerInitialized) return;
        _syncDirection.Value = dir;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestOwnershipAndMoveServerRpc(int dir, FishNet.Connection.NetworkConnection sender = null)
    {
        if(!IsServerInitialized) return;
        _syncDirection.Value = dir;
        NetworkObject.GiveOwnership(sender);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReleaseOwnershipServerRpc()
    {
        if(!IsServerInitialized) return;
        _syncDirection.Value = 0;
        NetworkObject.GiveOwnership(null);
    }

    [ServerRpc(RequireOwnership = false)]
    private void StopMoveServerRpc()
    {
        if(!IsServerInitialized) return;
        _syncDirection.Value = 0;
        NetworkObject.GiveOwnership(null);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SyncHeightToServerRpc(float height)
    {
        if(!IsServerInitialized) return;
        _syncCurrentHeight.Value = height;
    }

    // ═══════════════════════════════════════════
    //  ILiftController 버튼 이벤트
    // ═══════════════════════════════════════════

    public virtual void OnUpButton()
    {
        if(_currentHeight >= MaxValue) return;
        // Ownership을 가져오지 말고 명령만 내림
        RequestMoveServerRpc(1);
    }

    public virtual void OnDownButton()
    {
        if(_currentHeight <= 0f) return;
        RequestMoveServerRpc(-1);
    }

    public virtual void OnStopButton()
    {
        ReleaseOwnershipServerRpc();
    }

    // ═══════════════════════════════════════════
    //  높이 적용 (SyncVar 콜백)
    // ═══════════════════════════════════════════

    private void OnSyncHeightChanged(float prev, float next, bool asServer)
    {
        if(!_isInitialized) return;
        // Owner는 Update에서 ApplyHeight를 직접 호출하므로 중복 방지
        if(IsOwner && !IsServerInitialized) return;
        ApplyHeight(next);
    }

    private void OnSyncDirectionChanged(int prev, int next, bool asServer)
    {
        if(next != 0 && liftAudio != null && !liftAudio.isPlaying)
            liftAudio.Play();
        if(next == 0 && liftAudio != null)
            liftAudio.Stop();
        UpdateUI();
    }

    protected void ApplyHeight(float h)
    {
        if(liftMode == LiftMode.TransformBased)
            ApplyHeightTransform(h);
        else
            ApplyHeightAnimator(h);
    }

    private void ApplyHeightAnimator(float normalizedValue)
    {
        if(liftAnimator == null) return;
        liftAnimator.SetFloat(_liftParamHash, normalizedValue);
    }

    // ═══════════════════════════════════════════
    //  LNT 안전 이동 유틸리티
    // ═══════════════════════════════════════════

    /// <summary>
    /// 대상 Transform이 LocalNetworkTransform을 가지고 있으면 이를 통해 위치를 업데이트합니다.
    /// 없으면 즉시 transform.position을 변경합니다.
    /// </summary>
    protected void UpdateTargetPositionViaLNT(Transform target, Vector3 newPos)
    {
        if(target == null) return;
        // 서버 또는 리프트 Owner(버튼 누른 클라이언트)만 수정
        // LNT가 붙은 오브젝트라면 LNT의 서버 authority나 Owner 경로로 다른 클라에 전파됨
        if(IsServerInitialized || IsOwner)
            target.position = newPos;
    }


    /// <summary>
    /// 로컬 좌표로 이동 (리프트 자신의 암 등)
    /// </summary>
    protected void UpdateTargetLocalPosition(Transform target, Vector3 newLocalPos)
    {
        if(target == null) return;
        target.localPosition = newLocalPos;
    }

    // ═══════════════════════════════════════════
    //  감속 / UI
    // ═══════════════════════════════════════════

    private float CalculateSpeed()
    {
        float minDist = Mathf.Min(_currentHeight, MaxValue - _currentHeight);
        if(minDist < slowZone)
            return liftSpeed * Mathf.Lerp(0.25f, 1f, minDist / slowZone);
        return liftSpeed;
    }

    protected void UpdateUI()
    {
        if(heightDisplay != null)
            heightDisplay.text = GetHeightDisplayText();
        if(upIndicator != null)
            upIndicator.SetActive(_direction > 0);
        if(downIndicator != null)
            downIndicator.SetActive(_direction < 0);
    }

    // ═══════════════════════════════════════════
    //  공개 프로퍼티
    // ═══════════════════════════════════════════

    public float CurrentHeight => _currentHeight;
    public bool IsRaised => _currentHeight > 0.05f;
    public float NormalizedHeight => liftMode == LiftMode.AnimatorBased
        ? _currentHeight
        : _currentHeight / Mathf.Max(GetMaxHeight(), 0.001f);
}