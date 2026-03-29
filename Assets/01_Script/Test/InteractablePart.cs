using System.Collections.Generic;
using UnityEngine;
using Autohand;

// ============================================================
//  InteractablePart.cs  — VR + PC 통합, localPosition 기준
//
//  수정 사항:
//    - XRGrabInteractable 제거 → SyncGrab 방식
//    - initialPosition을 localPosition 기준으로 변경
//      (차량이 이동해도 상대 위치 유지)
//    - GhostManager NullRef 방지 (GetComponentInChildren)
//    - ElectricSafetyManager.bypassSafety 옵션 지원
//    - PC: FreeLookController → OnPCClick() → 집기/내려놓기
// ============================================================
public class InteractablePart : MonoBehaviour
{
    public enum PartState { Assembled, Unlocked, Detached }

    [Header("상태 (읽기 전용)")]
    public PartState currentState = PartState.Assembled;

    [Header("볼트 연결")]
    public List<Bolt> requiredBolts = new List<Bolt>();

    [Header("스냅 설정")]
    public float snapDistance = 0.15f;
    public Color snapReadyColor = Color.green;

    [Header("Ghost")]
    public Material ghostMaterial;

    [Header("테스트")]
    [Tooltip("true: 안전 체크 무시 (MSD/장갑 없어도 집기 가능)")]
    public bool bypassSafety = false;

    // 컴포넌트
    private Grabbable _autoGrabbable;
    private SyncGrab _syncGrab;
    private GhostManager _ghost;

    // 로컬 기준 초기 위치 (차량 이동에 대응)
    private Vector3 _localPos;
    private Quaternion _localRot;
    private Transform _parentCache;

    // PC 집기 상태
    private bool _isPCHeld = false;
    private Transform _holdPoint = null;   // 카메라 앞 기준점 (Tag: "HoldPoint")
    private Rigidbody _rb = null;   // 물리 충돌 제어용

    // ═══════════════════════════════════════════
    //  생명주기
    // ═══════════════════════════════════════════

    private void Awake()
    {
        _autoGrabbable = GetComponent<Grabbable>();
        _syncGrab = GetComponent<SyncGrab>();

        // localPosition 기준으로 초기 위치 저장
        _localPos = transform.localPosition;
        _localRot = transform.localRotation;
        _parentCache = transform.parent;

        // GhostManager 설정 — 자식 MeshFilter도 탐색
        _ghost = gameObject.AddComponent<GhostManager>();
        _ghost.ghostMaterial = ghostMaterial;
        _ghost.CreateGhost();

        // holdPoint: 카메라 앞 기준점 (SyncGrab과 동일한 Tag 사용)
        var holdObj = GameObject.FindWithTag("HoldPoint");
        if (holdObj != null) _holdPoint = holdObj.transform;
        else Debug.LogWarning("[InteractablePart] Tag='HoldPoint' 오브젝트 없음 — PC 집기 시 부품이 안 따라다님");

        _rb = GetComponent<Rigidbody>();

        // VR AutoHand 이벤트
        if (_autoGrabbable != null)
        {
            _autoGrabbable.onGrab.AddListener((h, g) => OnGrabStart());
            _autoGrabbable.onRelease.AddListener((h, g) => OnGrabEnd());
        }
    }

    private void Start()
    {
        SetPartState(PartState.Assembled);
    }

    private void Update()
    {
        // ── PC 집기: holdPoint 실시간 추적 ──────────────────────────
        if (_isPCHeld && _holdPoint != null)
        {
            transform.position = _holdPoint.position;
            transform.rotation = _holdPoint.rotation;
        }

        switch (currentState)
        {
            case PartState.Assembled:
                CheckBoltsForUnlock();
                break;
            case PartState.Detached:
                if (IsBeingHeld()) CheckSnapProximity();
                break;
        }
    }

    // ═══════════════════════════════════════════
    //  PC 인터랙션 (FreeLookController 호출)
    // ═══════════════════════════════════════════

    public void OnPCClick()
    {
        if (currentState == PartState.Assembled)
        {
            Debug.Log($"[Part] {name}: 볼트를 먼저 해체하세요 (상태: Assembled)");
            return;
        }

        if (!_isPCHeld)
        {
            // 집기
            if (!CheckSafety()) return;
            _isPCHeld = true;
            Debug.Log($"[Part] {name}: PC 집기");
            OnGrabStart();
        }
        else
        {
            // 내려놓기
            _isPCHeld = false;
            Debug.Log($"[Part] {name}: PC 내려놓기");
            OnGrabEnd();
        }
    }

    // ═══════════════════════════════════════════
    //  집기 / 내려놓기
    // ═══════════════════════════════════════════

    public void OnGrabStart()
    {
        if (!CheckSafety()) return;

        // Rigidbody가 있으면 kinematic으로 전환 (물리가 위치를 덮어쓰지 않게)
        if (_rb != null)
        {
            _rb.isKinematic = true;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        // 고스트 표시 (원위치 반투명 가이드)
        if (_ghost != null) _ghost.SetGhostActive(true);
    }

    public void OnGrabEnd()
    {
        _isPCHeld = false;

        float distance = Vector3.Distance(transform.position, GetWorldAssemblyPos());

        if (distance < snapDistance)
        {
            SnapToOriginalPosition();
        }
        else
        {
            SetPartState(PartState.Detached);

            // 탈거 완료: Rigidbody 물리 복원 (중력 적용)
            if (_rb != null) _rb.isKinematic = false;
        }
    }

    // ═══════════════════════════════════════════
    //  볼트 해제 체크
    // ═══════════════════════════════════════════

    private void CheckBoltsForUnlock()
    {
        // ★ 볼트 해제 체크는 안전 조건과 무관
        //    안전 체크는 집기(OnGrabStart/OnPCClick)에서만 수행
        if (requiredBolts == null || requiredBolts.Count == 0)
        {
            SetPartState(PartState.Unlocked);
            return;
        }

        foreach (var bolt in requiredBolts)
        {
            if (bolt != null && !bolt.isloosened) return;
        }
        SetPartState(PartState.Unlocked);
        Debug.Log($"[Part] {name}: 볼트 해체 완료 → Unlocked (bypassSafety={bypassSafety})");
    }

    // ═══════════════════════════════════════════
    //  스냅 근접 체크 (Detached 상태에서 원위치 근처일 때 초록 표시)
    // ═══════════════════════════════════════════

    private void CheckSnapProximity()
    {
        if (_ghost == null) return;
        float distance = Vector3.Distance(transform.position, GetWorldAssemblyPos());
        if (distance < snapDistance) _ghost.UpdateGhostColor(snapReadyColor);
        else _ghost.ResetGhostColor();
    }

    // ═══════════════════════════════════════════
    //  스냅 (조립 완료)
    // ═══════════════════════════════════════════

    private void SnapToOriginalPosition()
    {
        // Rigidbody kinematic으로 고정 (조립 위치에서 물리 흔들림 방지)
        if (_rb != null)
        {
            _rb.isKinematic = true;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        // 부모가 바뀌지 않았다면 localPosition 기준으로 복원
        if (_parentCache != null && transform.parent == _parentCache)
        {
            transform.localPosition = _localPos;
            transform.localRotation = _localRot;
        }
        else
        {
            transform.position = GetWorldAssemblyPos();
        }

        SetPartState(PartState.Assembled);

        // 볼트 리셋
        foreach (var bolt in requiredBolts)
        {
            if (bolt == null) continue;
            bolt.gameObject.SetActive(true);
            bolt.ResetBolt();
        }

        Debug.Log($"[Part] {name}: 조립 완료 → Assembled");
        InteractionEvents.FireZoneActivated(name + "_Assembled", name);
    }

    // ═══════════════════════════════════════════
    //  상태 설정
    // ═══════════════════════════════════════════

    private void SetPartState(PartState newState)
    {
        currentState = newState;

        bool canGrab = (newState != PartState.Assembled);

        if (_autoGrabbable != null) _autoGrabbable.enabled = canGrab;
        if (_syncGrab != null) _syncGrab.enabled = canGrab;
        if (_ghost != null) _ghost.SetGhostActive(newState == PartState.Detached);
    }

    // ═══════════════════════════════════════════
    //  안전 체크
    // ═══════════════════════════════════════════

    private bool CheckSafety()
    {
        if (bypassSafety) return true;
        if (ElectricSafetyManager.Instance == null) return true;
        if (ElectricSafetyManager.Instance.IsSafeToWork()) return true;

        if (SafetyUIHandler.Instance != null)
            SafetyUIHandler.Instance.TriggerWarning("위험! MSD 탈거 및 절연 장갑 착용 후 작업하세요!");

        Debug.LogWarning($"[Part] {name}: 안전 조건 미충족 — 집기 차단");
        return false;
    }

    // 볼트 해제 체크용 (경고 없이 조용히 false)
    private bool CheckSafetyPassive()
    {
        if (bypassSafety) return true;
        return ElectricSafetyManager.Instance == null || ElectricSafetyManager.Instance.IsSafeToWork();
    }

    // ═══════════════════════════════════════════
    //  유틸
    // ═══════════════════════════════════════════

    private bool IsBeingHeld()
    {
        bool autoHeld = (_autoGrabbable != null && _autoGrabbable.IsHeld());
        return autoHeld || _isPCHeld;
    }

    private Vector3 GetWorldAssemblyPos()
    {
        // 부모가 살아있으면 localPosition → world 변환
        if (_parentCache != null)
            return _parentCache.TransformPoint(_localPos);
        return transform.position;
    }

    // ── 외부 API ─────────────────────────────

    public void ForceDetach()
    {
        transform.position = GetWorldAssemblyPos() + Vector3.up * 0.5f;
        if (requiredBolts != null)
            foreach (var b in requiredBolts) if (b != null) b.progress = 1f;
        SetPartState(PartState.Detached);
    }

    public void ForceAssemble() => SnapToOriginalPosition();
    public virtual void ResetPart() => SnapToOriginalPosition();

    public bool IsAllBoltsTightened()
    {
        if (requiredBolts == null || requiredBolts.Count == 0) return true;
        foreach (var b in requiredBolts) if (b != null && !b.isTightened) return false;
        return true;
    }
}