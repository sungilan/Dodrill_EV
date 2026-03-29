using UnityEngine;
using FishNet;
using FishNet.Broadcast;
using FishNet.Transporting;
using TMPro;

// ============================================================
//  LiftControllerBase.cs
//  모든 리프트 컨트롤러의 공통 베이스 클래스.
//
//  공통 기능:
//    - TransformBased / AnimatorBased 모드 분기
//    - 버튼 이벤트 (OnUpButton / OnDownButton / OnStopButton)
//    - FishNet Broadcast 네트워크 동기화
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
    public float  height;    // TransformBased: 0~maxHeight, AnimatorBased: 0~1
    public bool   isMoving;
    public int    direction; // 1=올리기, -1=내리기, 0=정지
}

public abstract class LiftControllerBase : MonoBehaviour, ILiftController
{
    // ── 리프트 모드 ────────────────────────────

    public enum LiftMode { TransformBased, AnimatorBased }

    [Header("리프트 모드")]
    [Tooltip("TransformBased: Transform 직접 이동 / AnimatorBased: Animator 블렌드트리 제어")]
    public LiftMode liftMode = LiftMode.TransformBased;

    // ── 공통 ───────────────────────────────────

    [Header("식별")]
    public string uniqueId = "Lift_01";

    [Header("리프트 설정")]
    [Tooltip("속도. TransformBased: m/s, AnimatorBased: normalized/s")]
    public float liftSpeed = 0.4f;
    [Tooltip("상하단 감속 구간. TransformBased: m, AnimatorBased: normalized")]
    public float slowZone  = 0.15f;

    [Header("UI (선택)")]
    public TextMeshProUGUI heightDisplay;
    public GameObject      upIndicator;
    public GameObject      downIndicator;
    public AudioSource     liftAudio;

    // ── AnimatorBased 공통 ────────────────────

    [Header("── AnimatorBased 공통 ──")]
    [Tooltip("Animator 컴포넌트 (없으면 자신의 Animator 사용)")]
    public Animator liftAnimator;
    [Tooltip("블렌드트리 float 파라미터 이름")]
    public string liftParamName = "LiftHeight";
    [Tooltip("탑재 오브젝트가 따라갈 상판 본(bone)")]
    public Transform platformBone;

    // ── 보호 상태 (자식 접근 가능) ─────────────

    protected float _currentHeight = 0f;
    protected int   _direction     = 0;
    protected bool  _isMoving      = false;

    // ── 내부 캐시 ─────────────────────────────

    private int _liftParamHash;

    // 모드별 최대값
    protected float MaxValue => liftMode == LiftMode.TransformBased ? GetMaxHeight() : 1f;

    // ═══════════════════════════════════════════
    //  추상/가상 메서드 — 자식에서 구현
    // ═══════════════════════════════════════════

    /// <summary>TransformBased 모드의 최대 높이 (m)</summary>
    protected abstract float GetMaxHeight();

    /// <summary>TransformBased 높이 적용</summary>
    protected abstract void ApplyHeightTransform(float h);

    /// <summary>자식 클래스 초기화 (Start에서 호출)</summary>
    protected virtual void OnLiftStart() { }

    /// <summary>매 Update마다 높이 변화 후 호출 (배터리 접촉 체크 등)</summary>
    protected virtual void OnHeightChanged(float newHeight, float prevHeight) { }

    /// <summary>완전히 내려왔을 때 호출</summary>
    protected virtual void OnReachedBottom() { }

    /// <summary>완전히 올라갔을 때 호출</summary>
    protected virtual void OnReachedTop() { }

    /// <summary>LateUpdate에서 탑재 오브젝트 위치 동기화 (AnimatorBased)</summary>
    protected virtual void SyncPayloadLateUpdate() { }

    /// <summary>UI 텍스트 내용 반환 (자식이 재정의 가능)</summary>
    protected virtual string GetHeightDisplayText()
    {
        return liftMode == LiftMode.TransformBased
            ? $"{_currentHeight:F2} m"
            : $"{_currentHeight * 100f:F0} %";
    }

    // ═══════════════════════════════════════════
    //  생명주기
    // ═══════════════════════════════════════════

    protected virtual void Start()
    {
        if (liftMode == LiftMode.AnimatorBased)
        {
            if (liftAnimator == null)
                liftAnimator = GetComponent<Animator>();

            if (liftAnimator != null)
                _liftParamHash = Animator.StringToHash(liftParamName);
            else
                Debug.LogWarning($"[{GetType().Name}] Animator를 찾을 수 없습니다. ({gameObject.name})");
        }

        OnLiftStart();
        RegisterBroadcast();
        UpdateUI();
    }

    protected virtual void OnDestroy() => UnregisterBroadcast();

    private void LateUpdate()
    {
        if (liftMode != LiftMode.AnimatorBased) return;
        SyncPayloadLateUpdate();
    }

    private void Update()
    {
        if (!_isMoving) return;
        if (!InstanceFinder.IsServerStarted) return;

        float prevHeight = _currentHeight;
        float speed      = CalculateSpeed();
        float newHeight  = Mathf.Clamp(_currentHeight + _direction * speed * Time.deltaTime, 0f, MaxValue);

        bool reachedEdge = newHeight <= 0f || newHeight >= MaxValue;
        if (reachedEdge)
        {
            bool wasAtBottom = newHeight <= 0f;
            newHeight  = Mathf.Clamp(newHeight, 0f, MaxValue);
            _direction = 0;
            _isMoving  = false;
            if (liftAudio != null) liftAudio.Stop();

            if (wasAtBottom) OnReachedBottom();
            else             OnReachedTop();
        }

        _currentHeight = newHeight;
        ApplyHeight(_currentHeight);
        OnHeightChanged(_currentHeight, prevHeight);
        BroadcastState();
        UpdateUI();
    }

    // ═══════════════════════════════════════════
    //  ILiftController 버튼 이벤트
    // ═══════════════════════════════════════════

    public virtual void OnUpButton()
    {
        if (_currentHeight >= MaxValue) return;
        RequestMove(1);
    }

    public virtual void OnDownButton()
    {
        if (_currentHeight <= 0f) return;
        RequestMove(-1);
    }

    public virtual void OnStopButton() => RequestMove(0);

    // ═══════════════════════════════════════════
    //  높이 적용
    // ═══════════════════════════════════════════

    protected void ApplyHeight(float h)
    {
        if (liftMode == LiftMode.TransformBased)
            ApplyHeightTransform(h);
        else
            ApplyHeightAnimator(h);
    }

    private void ApplyHeightAnimator(float normalizedValue)
    {
        if (liftAnimator == null) return;
        liftAnimator.SetFloat(_liftParamHash, normalizedValue);
    }

    // ═══════════════════════════════════════════
    //  네트워크
    // ═══════════════════════════════════════════

    private void RequestMove(int dir)
    {
        if (InstanceFinder.IsServerStarted)
            ServerSetDirection(dir);
        else if (InstanceFinder.IsClientStarted)
            InstanceFinder.ClientManager.Broadcast(
                new LiftStateBroadcast { id = uniqueId, direction = dir,
                                         height = _currentHeight, isMoving = dir != 0 });
    }

    protected void ServerSetDirection(int dir)
    {
        _direction = dir;
        _isMoving  = dir != 0;

        if (_isMoving  && liftAudio != null && !liftAudio.isPlaying) liftAudio.Play();
        if (!_isMoving && liftAudio != null) liftAudio.Stop();

        BroadcastState();
    }

    private void RegisterBroadcast()
    {
        InstanceFinder.ServerManager?.RegisterBroadcast<LiftStateBroadcast>(OnServerReceive);
        InstanceFinder.ClientManager?.RegisterBroadcast<LiftStateBroadcast>(OnClientReceive);
    }

    private void UnregisterBroadcast()
    {
        InstanceFinder.ServerManager?.UnregisterBroadcast<LiftStateBroadcast>(OnServerReceive);
        InstanceFinder.ClientManager?.UnregisterBroadcast<LiftStateBroadcast>(OnClientReceive);
    }

    private void OnServerReceive(FishNet.Connection.NetworkConnection conn,
                                  LiftStateBroadcast msg, Channel ch)
    {
        if (msg.id != uniqueId) return;
        ServerSetDirection(msg.direction);
    }

    protected virtual void OnClientReceive(LiftStateBroadcast msg, Channel ch)
    {
        if (msg.id != uniqueId) return;
        if (InstanceFinder.IsServerStarted) return;

        _currentHeight = msg.height;
        _direction     = msg.direction;
        _isMoving      = msg.isMoving;
        ApplyHeight(_currentHeight);
        UpdateUI();
    }

    protected void BroadcastState()
    {
        InstanceFinder.ServerManager?.Broadcast(
            new LiftStateBroadcast { id = uniqueId, height = _currentHeight,
                                     isMoving = _isMoving, direction = _direction });
    }

    // ═══════════════════════════════════════════
    //  감속 / UI
    // ═══════════════════════════════════════════

    private float CalculateSpeed()
    {
        float minDist = Mathf.Min(_currentHeight, MaxValue - _currentHeight);
        if (minDist < slowZone)
            return liftSpeed * Mathf.Lerp(0.25f, 1f, minDist / slowZone);
        return liftSpeed;
    }

    protected void UpdateUI()
    {
        if (heightDisplay != null) heightDisplay.text = GetHeightDisplayText();
        if (upIndicator   != null) upIndicator.SetActive(_direction > 0);
        if (downIndicator != null) downIndicator.SetActive(_direction < 0);
    }

    // ═══════════════════════════════════════════
    //  공개 프로퍼티
    // ═══════════════════════════════════════════

    public float CurrentHeight    => _currentHeight;
    public bool  IsRaised         => _currentHeight > 0.05f;
    public float NormalizedHeight => liftMode == LiftMode.AnimatorBased
        ? _currentHeight
        : _currentHeight / Mathf.Max(GetMaxHeight(), 0.001f);
}
