//using UnityEngine;
//using FishNet;
//using FishNet.Broadcast;
//using FishNet.Transporting;
//using TMPro;

//// ============================================================
////  LiftControllerBase.cs
////  모든 리프트 컨트롤러의 공통 베이스 클래스.
////
////  공통 기능:
////    - TransformBased / AnimatorBased 모드 분기
////    - 버튼 이벤트 (OnUpButton / OnDownButton / OnStopButton)
////    - FishNet Broadcast 네트워크 동기화
////    - 감속(slowZone), 오디오, UI 인디케이터
////    - platformBone 기반 탑재 오브젝트 추적 (LateUpdate)
////
////  자식 클래스:
////    VehicleLiftController  — 차량 리프트 (암 2개 이동)
////    BatteryLiftController  — 배터리 잭 (contactHeight 자동 연결)
//// ============================================================

//public struct LiftStateBroadcast : IBroadcast
//{
//    public string id;
//    public float  height;    // TransformBased: 0~maxHeight, AnimatorBased: 0~1
//    public bool   isMoving;
//    public int    direction; // 1=올리기, -1=내리기, 0=정지
//}

//public abstract class LiftControllerBase : MonoBehaviour, ILiftController
//{
//    // ── 리프트 모드 ────────────────────────────

//    public enum LiftMode { TransformBased, AnimatorBased }

//    [Header("리프트 모드")]
//    [Tooltip("TransformBased: Transform 직접 이동 / AnimatorBased: Animator 블렌드트리 제어")]
//    public LiftMode liftMode = LiftMode.TransformBased;

//    // ── 공통 ───────────────────────────────────

//    [Header("식별")]
//    public string uniqueId = "Lift_01";

//    [Header("리프트 설정")]
//    [Tooltip("속도. TransformBased: m/s, AnimatorBased: normalized/s")]
//    public float liftSpeed = 0.4f;
//    [Tooltip("상하단 감속 구간. TransformBased: m, AnimatorBased: normalized")]
//    public float slowZone  = 0.15f;

//    [Header("UI (선택)")]
//    public TextMeshProUGUI heightDisplay;
//    public GameObject      upIndicator;
//    public GameObject      downIndicator;
//    public AudioSource     liftAudio;

//    // ── AnimatorBased 공통 ────────────────────

//    [Header("── AnimatorBased 공통 ──")]
//    [Tooltip("Animator 컴포넌트 (없으면 자신의 Animator 사용)")]
//    public Animator liftAnimator;
//    [Tooltip("블렌드트리 float 파라미터 이름")]
//    public string liftParamName = "LiftHeight";
//    [Tooltip("탑재 오브젝트가 따라갈 상판 본(bone)")]
//    public Transform platformBone;

//    // ── 보호 상태 (자식 접근 가능) ─────────────

//    protected float _currentHeight = 0f;
//    protected int   _direction     = 0;
//    protected bool  _isMoving      = false;

//    // ── 내부 캐시 ─────────────────────────────

//    private int _liftParamHash;

//    // 모드별 최대값
//    protected float MaxValue => liftMode == LiftMode.TransformBased ? GetMaxHeight() : 1f;

//    // ═══════════════════════════════════════════
//    //  추상/가상 메서드 — 자식에서 구현
//    // ═══════════════════════════════════════════

//    /// <summary>TransformBased 모드의 최대 높이 (m)</summary>
//    protected abstract float GetMaxHeight();

//    /// <summary>TransformBased 높이 적용</summary>
//    protected abstract void ApplyHeightTransform(float h);

//    /// <summary>자식 클래스 초기화 (Start에서 호출)</summary>
//    protected virtual void OnLiftStart() { }

//    /// <summary>매 Update마다 높이 변화 후 호출 (배터리 접촉 체크 등)</summary>
//    protected virtual void OnHeightChanged(float newHeight, float prevHeight) { }

//    /// <summary>완전히 내려왔을 때 호출</summary>
//    protected virtual void OnReachedBottom() { }

//    /// <summary>완전히 올라갔을 때 호출</summary>
//    protected virtual void OnReachedTop() { }

//    /// <summary>LateUpdate에서 탑재 오브젝트 위치 동기화 (AnimatorBased)</summary>
//    protected virtual void SyncPayloadLateUpdate() { }

//    /// <summary>UI 텍스트 내용 반환 (자식이 재정의 가능)</summary>
//    protected virtual string GetHeightDisplayText()
//    {
//        return liftMode == LiftMode.TransformBased
//            ? $"{_currentHeight:F2} m"
//            : $"{_currentHeight * 100f:F0} %";
//    }

//    // ═══════════════════════════════════════════
//    //  생명주기
//    // ═══════════════════════════════════════════

//    protected virtual void Start()
//    {
//        if (liftMode == LiftMode.AnimatorBased)
//        {
//            if (liftAnimator == null)
//                liftAnimator = GetComponent<Animator>();

//            if (liftAnimator != null)
//                _liftParamHash = Animator.StringToHash(liftParamName);
//            else
//                Debug.LogWarning($"[{GetType().Name}] Animator를 찾을 수 없습니다. ({gameObject.name})");
//        }

//        OnLiftStart();
//        RegisterBroadcast();
//        UpdateUI();
//    }

//    protected virtual void OnDestroy() => UnregisterBroadcast();

//    private void LateUpdate()
//    {
//        if (liftMode != LiftMode.AnimatorBased) return;
//        SyncPayloadLateUpdate();
//    }

//    private void Update()
//    {
//        if (!_isMoving) return;
//        if (!InstanceFinder.IsServerStarted) return;

//        float prevHeight = _currentHeight;
//        float speed      = CalculateSpeed();
//        float newHeight  = Mathf.Clamp(_currentHeight + _direction * speed * Time.deltaTime, 0f, MaxValue);

//        bool reachedEdge = newHeight <= 0f || newHeight >= MaxValue;
//        if (reachedEdge)
//        {
//            bool wasAtBottom = newHeight <= 0f;
//            newHeight  = Mathf.Clamp(newHeight, 0f, MaxValue);
//            _direction = 0;
//            _isMoving  = false;
//            if (liftAudio != null) liftAudio.Stop();

//            if (wasAtBottom) OnReachedBottom();
//            else             OnReachedTop();
//        }

//        _currentHeight = newHeight;
//        ApplyHeight(_currentHeight);
//        OnHeightChanged(_currentHeight, prevHeight);
//        BroadcastState();
//        UpdateUI();
//    }

//    // ═══════════════════════════════════════════
//    //  ILiftController 버튼 이벤트
//    // ═══════════════════════════════════════════

//    public virtual void OnUpButton()
//    {
//        if (_currentHeight >= MaxValue) return;
//        RequestMove(1);
//    }

//    public virtual void OnDownButton()
//    {
//        if (_currentHeight <= 0f) return;
//        RequestMove(-1);
//    }

//    public virtual void OnStopButton() => RequestMove(0);

//    // ═══════════════════════════════════════════
//    //  높이 적용
//    // ═══════════════════════════════════════════

//    protected void ApplyHeight(float h)
//    {
//        if (liftMode == LiftMode.TransformBased)
//            ApplyHeightTransform(h);
//        else
//            ApplyHeightAnimator(h);
//    }

//    private void ApplyHeightAnimator(float normalizedValue)
//    {
//        if (liftAnimator == null) return;
//        liftAnimator.SetFloat(_liftParamHash, normalizedValue);
//    }

//    // ═══════════════════════════════════════════
//    //  네트워크
//    // ═══════════════════════════════════════════

//    private void RequestMove(int dir)
//    {
//        if (InstanceFinder.IsServerStarted)
//            ServerSetDirection(dir);
//        else if (InstanceFinder.IsClientStarted)
//            InstanceFinder.ClientManager.Broadcast(
//                new LiftStateBroadcast { id = uniqueId, direction = dir,
//                                         height = _currentHeight, isMoving = dir != 0 });
//    }

//    protected void ServerSetDirection(int dir)
//    {
//        _direction = dir;
//        _isMoving  = dir != 0;

//        if (_isMoving  && liftAudio != null && !liftAudio.isPlaying) liftAudio.Play();
//        if (!_isMoving && liftAudio != null) liftAudio.Stop();

//        BroadcastState();
//    }

//    private void RegisterBroadcast()
//    {
//        InstanceFinder.ServerManager?.RegisterBroadcast<LiftStateBroadcast>(OnServerReceive);
//        InstanceFinder.ClientManager?.RegisterBroadcast<LiftStateBroadcast>(OnClientReceive);
//    }

//    private void UnregisterBroadcast()
//    {
//        InstanceFinder.ServerManager?.UnregisterBroadcast<LiftStateBroadcast>(OnServerReceive);
//        InstanceFinder.ClientManager?.UnregisterBroadcast<LiftStateBroadcast>(OnClientReceive);
//    }

//    private void OnServerReceive(FishNet.Connection.NetworkConnection conn,
//                                  LiftStateBroadcast msg, Channel ch)
//    {
//        if (msg.id != uniqueId) return;
//        ServerSetDirection(msg.direction);
//    }

//    protected virtual void OnClientReceive(LiftStateBroadcast msg, Channel ch)
//    {
//        if (msg.id != uniqueId) return;
//        if (InstanceFinder.IsServerStarted) return;

//        _currentHeight = msg.height;
//        _direction     = msg.direction;
//        _isMoving      = msg.isMoving;
//        ApplyHeight(_currentHeight);
//        UpdateUI();
//    }

//    protected void BroadcastState()
//    {
//        InstanceFinder.ServerManager?.Broadcast(
//            new LiftStateBroadcast { id = uniqueId, height = _currentHeight,
//                                     isMoving = _isMoving, direction = _direction });
//    }

//    // ═══════════════════════════════════════════
//    //  감속 / UI
//    // ═══════════════════════════════════════════

//    private float CalculateSpeed()
//    {
//        float minDist = Mathf.Min(_currentHeight, MaxValue - _currentHeight);
//        if (minDist < slowZone)
//            return liftSpeed * Mathf.Lerp(0.25f, 1f, minDist / slowZone);
//        return liftSpeed;
//    }

//    protected void UpdateUI()
//    {
//        if (heightDisplay != null) heightDisplay.text = GetHeightDisplayText();
//        if (upIndicator   != null) upIndicator.SetActive(_direction > 0);
//        if (downIndicator != null) downIndicator.SetActive(_direction < 0);
//    }

//    // ═══════════════════════════════════════════
//    //  공개 프로퍼티
//    // ═══════════════════════════════════════════

//    public float CurrentHeight    => _currentHeight;
//    public bool  IsRaised         => _currentHeight > 0.05f;
//    public float NormalizedHeight => liftMode == LiftMode.AnimatorBased
//        ? _currentHeight
//        : _currentHeight / Mathf.Max(GetMaxHeight(), 0.001f);
//}
using UnityEngine;
using FishNet;
using FishNet.Broadcast;
using FishNet.Transporting;
using FishNet.Object; // 추가: LocalNetworkTransform 사용을 위해 필요
using FishNet.Object.Synchronizing; // 추가
using TMPro;
using System.Collections;

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
    public float height;    // TransformBased: 0~maxHeight, AnimatorBased: 0~1
    public bool isMoving;
    public int direction; // 1=올리기, -1=내리기, 0=정지
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

    // [SyncVar]로 높이와 방향을 서버 권위로 관리
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

    // 이제 Transform 변경은 LNT를 통해서만 수행해야 하므로, 
    // 직접 Transform을 넘기는 대신 자식에서 LNT를 제어하도록 역할을 위임합니다.
    protected abstract void ApplyHeightTransform(float h);

    // Animator 모드에서 매 프레임 물체를 이동시킬 때 호출됨
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

    protected virtual void OnDestroy()
    {
    }
    public override void OnStartClient()
    {
        base.OnStartClient();

        if(liftMode == LiftMode.AnimatorBased)
        {
            if(liftAnimator == null) liftAnimator = GetComponent<Animator>();
            if(liftAnimator != null) _liftParamHash = Animator.StringToHash(liftParamName);
            else Debug.LogWarning($"[{GetType().Name}] Animator를 찾을 수 없습니다. ({gameObject.name})");
        }

        // 콜백 등록
        _syncCurrentHeight.OnChange += OnSyncHeightChanged;
        _syncDirection.OnChange += OnSyncDirectionChanged;

        OnLiftStart();

        // 초기화 완료 플래그 (LateUpdate 등에서 사용)
        StartCoroutine(SetInitializedNextFrame());

        UpdateUI();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        _syncCurrentHeight.OnChange -= OnSyncHeightChanged;
        _syncDirection.OnChange -= OnSyncDirectionChanged;
    }

    private IEnumerator SetInitializedNextFrame()
    {
        yield return null;
        _isInitialized = true;
        // 초기 상태 반영
        ApplyHeight(_currentHeight);
    }

    private void LateUpdate()
    {
        // 서버/클라이언트 모두 Animator 뼈대 위치를 읽어 LNT 타겟을 갱신해야 함.
        // 다만 LNT SetTargetPosition은 매 프레임 호출해도 부드럽게 이동함.
        if(!_isInitialized || liftMode != LiftMode.AnimatorBased) return;
        SyncPayloadLateUpdate();
    }

    //private void Update()
    //{
    //    // 업데이트 로직은 서버에서만 수행하여 동기화
    //    if(!IsServerInitialized || !_isMoving) return;

    //    float prevHeight = _currentHeight;
    //    float speed = CalculateSpeed();
    //    float newHeight = Mathf.Clamp(_currentHeight + _direction * speed * Time.deltaTime, 0f, MaxValue);

    //    bool reachedEdge = newHeight <= 0f || newHeight >= MaxValue;
    //    if(reachedEdge)
    //    {
    //        bool wasAtBottom = newHeight <= 0f;
    //        newHeight = Mathf.Clamp(newHeight, 0f, MaxValue);

    //        // 정지
    //        _syncDirection.Value = 0;

    //        if(liftAudio != null) liftAudio.Stop();

    //        if(wasAtBottom) OnReachedBottom();
    //        else OnReachedTop();
    //    }

    //    _syncCurrentHeight.Value = newHeight;
    //    OnHeightChanged(newHeight, prevHeight);
    //}

    private void Update()
    {
        // 1. 서버 초기화 및 이동 상태 체크 로그
        //if(!IsServerInitialized) return;

        // 리프트가 멈춰있는데 로그가 안 찍힌다면 _syncDirection이 0인 상태입니다.
        if(!_isMoving) return;
        float prevHeight = _currentHeight;
        float speed = CalculateSpeed();

        // 현재 방향에 따른 계산값 로그
        float movement = _direction * speed * Time.deltaTime;
        float newHeight = Mathf.Clamp(_currentHeight + movement, 0f, MaxValue);

        // [로그 추가] 실시간 높이 변화 모니터링 (너무 많이 찍히지 않게 0.1초 간격 권장하나, 일단 전체 출력)
        Debug.Log($"[LiftBase] 이동 중 - ID: {uniqueId} | 방향: {_direction} | 이전높이: {prevHeight:F3} -> 새높이: {newHeight:F3} (Max: {MaxValue})");

        bool reachedEdge = newHeight <= 0f || newHeight >= MaxValue;

        if(reachedEdge)
        {
            bool wasAtBottom = newHeight <= 0f;
            newHeight = Mathf.Clamp(newHeight, 0f, MaxValue);

            // [로그 추가] 경계 도달 판정
            Debug.Log($"[LiftBase] 경계 도달 - {(wasAtBottom ? "최하단(Bottom)" : "최상단(Top)")} 정지 실행");

            // 정지
            _syncDirection.Value = 0;

            if(liftAudio != null) liftAudio.Stop();

            if(wasAtBottom) OnReachedBottom();
            else OnReachedTop();
        }

        // 서버 변수 갱신
        _syncCurrentHeight.Value = newHeight;

        // 자식 클래스(BatteryLiftController)의 판정 로직 호출
        OnHeightChanged(newHeight, prevHeight);
    }

    // ═══════════════════════════════════════════
    //  ILiftController 버튼 이벤트 (클라이언트 -> 서버 요청)
    // ═══════════════════════════════════════════

    public virtual void OnUpButton()
    {
        if(_currentHeight >= MaxValue) return;
        RequestMoveServerRpc(1);
    }

    public virtual void OnDownButton()
    {
        if(_currentHeight <= 0f) return;
        RequestMoveServerRpc(-1);
    }

    public virtual void OnStopButton() => RequestMoveServerRpc(0);

    [ServerRpc(RequireOwnership = false)]
    private void RequestMoveServerRpc(int dir)
    {
        Debug.Log($"[LiftBase] 서버 RPC 수신 - 요청 방향: {dir}");
        _syncDirection.Value = dir;
    }

    // ═══════════════════════════════════════════
    //  높이 적용 (SyncVar 콜백)
    // ═══════════════════════════════════════════

    private void OnSyncHeightChanged(float prev, float next, bool asServer)
    {
        if(!_isInitialized) return;
        ApplyHeight(next);
    }

    private void OnSyncDirectionChanged(int prev, int next, bool asServer)
    {
        if(next != 0 && liftAudio != null && !liftAudio.isPlaying) liftAudio.Play();
        if(next == 0 && liftAudio != null) liftAudio.Stop();
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
    //  LNT 안전 이동 유틸리티 (자식 클래스 제공)
    // ═══════════════════════════════════════════

    /// <summary>
    /// 대상 Transform이 LocalNetworkTransform을 가지고 있으면 이를 통해 위치를 업데이트합니다.
    /// 없으면 즉시 transform.position을 변경합니다.
    /// </summary>
    protected void UpdateTargetPositionViaLNT(Transform target, Vector3 newPos)
    {
        if(target == null) return;

        var lnt = target.GetComponent<LocalNetworkTransform>();
        if(lnt != null)
        {
            // LNT에게 목표 위치 지정. 회전은 변하지 않는다고 가정.
            lnt.SetTargetPosition(newPos, target.rotation);
        }
        else
        {
            target.position = newPos;
        }
    }

    // 로컬 좌표로 이동해야 하는 경우 (예: 리프트 자신의 암)
    protected void UpdateTargetLocalPosition(Transform target, Vector3 newLocalPos)
    {
        if(target == null) return;
        target.localPosition = newLocalPos; // 리프트 자체의 부품은 보통 LNT를 쓰지 않거나 부모를 따라감
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
        if(heightDisplay != null) heightDisplay.text = GetHeightDisplayText();
        if(upIndicator != null) upIndicator.SetActive(_direction > 0);
        if(downIndicator != null) downIndicator.SetActive(_direction < 0);
    }

    public float CurrentHeight => _currentHeight;
    public bool IsRaised => _currentHeight > 0.05f;
    public float NormalizedHeight => liftMode == LiftMode.AnimatorBased
        ? _currentHeight
        : _currentHeight / Mathf.Max(GetMaxHeight(), 0.001f);
}