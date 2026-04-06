using System.Collections.Generic;
using UnityEngine;
using Autohand;
using static EmergencyEventManager;

// ============================================================
//  InteractablePart.cs  — VR + PC 통합
//
//  isFreeMode 옵션:
//    true  → 볼트/안전 조건 무시, 즉시 집기 가능
//            VR: Grabbable.canDistanceGrab = true (AutoHand DistanceGrab 활성)
//            PC: Assembled 상태에서도 OnPCClick() 허용
//    false → 기존 시나리오 흐름 (볼트 해제 → Unlocked → 집기)
//
//  FreeModeManager.IsActive 가 true일 때
//  ScenarioRunner가 ev_freemode.json을 로드하면서
//  모든 InteractablePart에 SetFreeMode(true)를 호출
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

    [Header("옵션")]
    [Tooltip("true: 안전/볼트 체크 무시 (디버그·개발용)")]
    public bool bypassSafety = false;

    [Tooltip("true: 프리모드 — 볼트·안전 무시, VR DistanceGrab 활성")]
    public bool isFreeMode = false;

    public bool _isInsideSnapZone = false;

    [Header("사운드 설정")]
    [Tooltip("부품이 탈거될 때 (손에 잡힐 때) 재생할 사운드")]
    public string detachSound = "Part_Detach";

    [Tooltip("부품이 원위치에 조립될 때 재생할 사운드")]
    public string assembleSound = "Part_Assemble";

    [Tooltip("사운드 볼륨 (0~1)")]
    [Range(0f, 1f)]
    public float soundVolume = 1.0f;

    // 컴포넌트
    private Grabbable _autoGrabbable;
    private SyncGrab _syncGrab;
    private GhostManager _ghost;

    // 위치 캐시
    private Vector3 _localPos;
    private Quaternion _localRot;
    private Transform _parentCache;
    private Vector3 _initialScale;

    // PC 집기
    private bool _isPCHeld = false;
    private Transform _holdPoint = null;
    private Rigidbody _rb = null;

    // ── 생명주기 ───────────────────────────────────────────

    private void Awake()
    {
        _autoGrabbable = GetComponent<Grabbable>();
        _syncGrab = GetComponent<SyncGrab>();

        _localPos = transform.localPosition;
        _localRot = transform.localRotation;
        _initialScale = transform.localScale;
        _parentCache = transform.parent;

        _ghost = gameObject.AddComponent<GhostManager>();
        _ghost.ghostMaterial = ghostMaterial;
        _ghost.CreateGhost();

        var holdObj = GameObject.FindWithTag("HoldPoint");
        if(holdObj != null) _holdPoint = holdObj.transform;
        else Debug.LogWarning("[InteractablePart] Tag='HoldPoint' 없음");

        _rb = GetComponent<Rigidbody>();

        if(_autoGrabbable != null)
        {
            _autoGrabbable.onGrab.AddListener((h, g) => OnGrabStart());
            _autoGrabbable.onRelease.AddListener((h, g) => OnGrabEnd());
        }

        // SyncGrab 이벤트 구독
        // OnGrabbed  : PCFlyTo 완료(holdPoint 도달) 또는 VR 집기 완료 시
        // OnReleased : RequestRelease(내려놓기) 또는 VR 손 놓음 시
        // → SyncVar 콜백(스폰 시 오발동) 사용 안 함
        if(_syncGrab != null)
        {
            _syncGrab.OnGrabbed += OnSyncGrabbed;
            _syncGrab.OnReleased += OnSyncReleased;
        }
    }

    private void OnDestroy()
    {
        if(_syncGrab != null)
        {
            _syncGrab.OnGrabbed -= OnSyncGrabbed;
            _syncGrab.OnReleased -= OnSyncReleased;
        }
    }

    // SyncGrab이 holdPoint에 도달 완료했을 때
    private void OnSyncGrabbed()
    {
        if(currentState == PartState.Assembled || currentState == PartState.Unlocked)
        {
            SetPartState(PartState.Detached);
            _ghost?.SetGhostActive(true);
            Debug.Log($"[InteractablePart] {name}: 집힘 → Detached, 고스트 ON");
        }
    }

    // SyncGrab이 내려놓아졌을 때
    private void OnSyncReleased()
    {
        // 유저가 물건을 딱 놓았을 때, 트리거(존) 안에 있다면 즉시 조립!
        if(_isInsideSnapZone)
        {
            SnapToOriginalPosition();
            _isInsideSnapZone = false;
            Debug.Log($"[InteractablePart] {name}: 존 내부에서 놓임 → 조립 완료");
        }
        else
        {
            // 존 밖에서 놨다면 평소처럼 바닥에 떨어지고 고스트 유지
            _ghost?.SetGhostActive(true);
            Debug.Log($"[InteractablePart] {name}: 존 외부에서 놓임 → 고스트 유지, 재집기 대기");
        }
    }

    private void Start()
    {
        SetPartState(PartState.Assembled);
        ApplyFreeModeToGrabbable();
    }

    private void Update()
    {
        switch(currentState)
        {
            case PartState.Assembled:
                // 프리모드: Assembled에서도 집기 가능 — 볼트 체크 스킵
                if(!isFreeMode) CheckBoltsForUnlock();
                break;
            case PartState.Detached:
                // 집고 있든 내려놨든 항상 스냅 근접 체크
                // 고스트는 Detached 진입 시 켜지고 Assembled 시 꺼짐
                CheckSnapProximity();
                break;
        }
    }

    // ── 프리모드 설정 ──────────────────────────────────────

    /// <summary>
    /// ScenarioRunner / FreeModeBootstrapper에서 씬 시작 시 호출.
    /// 씬의 모든 InteractablePart에 일괄 적용.
    /// </summary>
    public void SetFreeMode(bool enabled)
    {
        isFreeMode = enabled;
        ApplyFreeModeToGrabbable();

        // 프리모드: Assembled → 즉시 Unlocked (볼트 없어도 집기 가능)
        if(enabled && currentState == PartState.Assembled)
            SetPartState(PartState.Unlocked);
    }

    private void ApplyFreeModeToGrabbable()
    {
        if(_autoGrabbable == null) return;

        // AutoHand DistanceGrab: 프리모드일 때만 활성
        // (Grabbable에 canDistanceGrab 프로퍼티가 있는 경우)
        if(_autoGrabbable is Grabbable g)
        {
            // AutoHand 버전에 따라 필드명 다를 수 있음
            // canDistanceGrab 또는 distanceGrabbable
            var field = typeof(Grabbable).GetField("canDistanceGrab",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if(field != null)
                field.SetValue(g, isFreeMode);
            else
            {
                // 필드가 없으면 DistanceGrabber 컴포넌트 활성/비활성
                var distGrab = GetComponent<Autohand.DistanceGrabbable>();
                if(distGrab != null) distGrab.enabled = isFreeMode;
            }
        }
    }

    // ── PC 인터랙션 ────────────────────────────────────────

    public void OnPCClick()
    {
        if (!isFreeMode && currentState == PartState.Assembled)
        {
            Debug.Log($"[Part] {name}: 볼트를 먼저 해체하세요");
            return;
        }

        if(!CheckSafetyAndTriggerAccident()) return;

        // _isPCHeld 토글 로직은 더 이상 사용하지 않습니다.
        // SyncGrab이 점유(Grab) 이벤트를 발생시키면 OnGrabStart()가 알아서 불립니다.
        Debug.Log($"[Part] {name}: PC 집기 신호 통과 (실제 잡기는 SyncGrab이 수행)");
    }

    // ── 집기 / 내려놓기 ────────────────────────────────────

    public void OnGrabStart()
    {
        if(!CheckSafetyAndTriggerAccident()) return;

        if(_rb != null)
        {
            //_rb.isKinematic = true;
            //_rb.linearVelocity = Vector3.zero;
            //_rb.angularVelocity = Vector3.zero;
        }

        if(_ghost != null) _ghost.SetGhostActive(true);
    }

    public void OnGrabEnd()
    {
        _isPCHeld = false;

        // SyncGrab이 있으면 OnSyncGrabStateChanged(false)가 이미 처리
        if(_syncGrab != null) return;

        // SyncGrab 없는 경우(VR AutoHand 직접 집기 등) 기존 처리
        float dist = Vector3.Distance(transform.position, GetWorldAssemblyPos());
        if(dist < snapDistance)
            SnapToOriginalPosition();
        else
        {
            SetPartState(PartState.Detached);
            //if(_rb != null) _rb.isKinematic = false;
            _ghost?.SetGhostActive(true);
        }
    }

    // ── 볼트 해제 체크 ─────────────────────────────────────

    private void CheckBoltsForUnlock()
    {
        if(requiredBolts == null || requiredBolts.Count == 0)
        {
            SetPartState(PartState.Unlocked);
            return;
        }
        foreach(var bolt in requiredBolts)
            if(bolt != null && !bolt.isloosened) return;

        SetPartState(PartState.Unlocked);
        Debug.Log($"[Part] {name}: 볼트 해체 완료 → Unlocked");
    }

    // ── 스냅 근접 체크 (수정) ────────────────────────────────

    private void CheckSnapProximity()
    {
        // Update문에서는 이제 '강제 조립'은 하지 않고, 존 안에 들어왔을 때 색상(초록색)만 바꿔줍니다.
        // 실제 조립은 물건을 손에서 놓는 OnSyncReleased에서 완벽하게 처리됩니다.
        if(_isInsideSnapZone)
        {
            _ghost?.UpdateGhostColor(snapReadyColor);
        }
        else
        {
            _ghost?.ResetGhostColor();
        }
    }

    // ── 스냅 (조립 완료) ───────────────────────────────────

    private void SnapToOriginalPosition()
    {
        // ① SyncGrab의 '추적'만 멈춤 
        // (RequestRelease는 밖에서 이미 호출했으므로 중복 호출하지 않고 삭제!)
        if(_syncGrab != null)
        {
            _syncGrab.StopPCHold();
        }

        // ② Rigidbody 고정
        if(_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
        }

        // ③ 원위치 복원 및 스케일 롤백
        if(_parentCache != null && transform.parent == _parentCache)
        {
            transform.localPosition = _localPos;
            transform.localRotation = _localRot;
        }
        else
        {
            transform.position = GetWorldAssemblyPos();
        }

        // 조립 시 원래 스케일로 원상 복구
        transform.localScale = _initialScale;

        // ★ 추가: 조립 사운드 재생
        if (!string.IsNullOrEmpty(assembleSound))
            Managers.Sound.Play(assembleSound, Define.Sound.Effect, 1.0f, soundVolume);

        // ④ 상태 변경 (고스트 OFF 포함)
        SetPartState(PartState.Assembled);

        foreach (var bolt in requiredBolts)
        {
            if(bolt == null) continue;
            bolt.gameObject.SetActive(true);
            bolt.ResetBolt();
        }

        Debug.Log($"[Part] {name}: 조립 완료 → Assembled");
        InteractionEvents.FireZoneActivated(name + "_Assembled", name);
    }

    // ── 상태 설정 ──────────────────────────────────────────

    private void SetPartState(PartState newState)
    {
        PartState oldState = currentState;
        currentState = newState;

        bool canGrab = isFreeMode || (newState != PartState.Assembled);
        if (_autoGrabbable != null) _autoGrabbable.enabled = canGrab;

        if (newState == PartState.Assembled)
        {
            _ghost?.SetGhostActive(false);
            if (_rb != null) _rb.isKinematic = true;
        }
        else if (newState == PartState.Detached)
        {
            _ghost?.SetGhostActive(true);

            // ★ 추가: Assembled 상태에서 Detached로 변할 때만 탈거 사운드 재생
            if (oldState == PartState.Assembled || oldState == PartState.Unlocked)
            {
                if (!string.IsNullOrEmpty(detachSound))
                    Managers.Sound.Play(detachSound, Define.Sound.Effect, 1.0f, soundVolume);
            }
        }
        else // Unlocked
        {
            _ghost?.SetGhostActive(false);
        }
    }

    // ── 안전 체크 ──────────────────────────────────────────

    public bool CheckSafetyAndTriggerAccident()
    {
        // 1. 패스 조건 (프리모드나 디버그 모드)
        if (isFreeMode || bypassSafety) return true;
        if (ElectricSafetyManager.Instance == null) return true;

        // 2. 완벽하게 안전한 상태인지 확인
        if (ElectricSafetyManager.Instance.IsSafeToWork()) return true;

        // 3. [사고 판단 로직] 안전하지 않다면 어떤 사고인지 결정
        string reason = "";
        EmergencyEventManager.AccidentType accidentType;

        // 상황 A: MSD가 아직 꽂혀있는 상태에서 만짐 -> 감전
        if (!ElectricSafetyManager.Instance.isMSDRemoved)
        {
            accidentType = EmergencyEventManager.AccidentType.ElectricShock;
            reason = "고전압 서비스 플러그(MSD)가 차단되지 않았습니다.";
        }
        // 상황 B: MSD는 뽑았는데 장갑을 안 낌 -> 잔류 전압 감전 혹은 화상
        else if (!ElectricSafetyManager.Instance.isGlovesEquipped)
        {
            accidentType = EmergencyEventManager.AccidentType.ElectricShock;
            reason = "절연 장갑을 착용하지 않았습니다.";
        }
        // 상황 C: 기타 위험 상황 (예: 배터리 내부 쇼트 유발 등) -> 열폭주
        else
        {
            accidentType = EmergencyEventManager.AccidentType.ThermalRunaway;
            reason = "부적절한 도구 사용으로 배터리 셀이 손상되었습니다.";
        }

        // 사고 발생!
        EmergencyEventManager.Instance.TriggerAccident(accidentType, reason);
        return false;
    }

    // ── 유틸 ───────────────────────────────────────────────

    private bool IsBeingHeld()
    {
        bool autoHeld = (_autoGrabbable != null && _autoGrabbable.IsHeld());
        return autoHeld || _isPCHeld;
    }

    private Vector3 GetWorldAssemblyPos()
    {
        if(_parentCache != null)
            return _parentCache.TransformPoint(_localPos);
        return transform.position;
    }

    // ── 외부 API ───────────────────────────────────────────

    public void ForceDetach()
    {
        transform.position = GetWorldAssemblyPos() + Vector3.up * 0.5f;
        if(requiredBolts != null)
            //foreach(var b in requiredBolts) if(b != null) b.Progress = 1f;
        SetPartState(PartState.Detached);
    }

    public void ForceAssemble()
    {
        if(_syncGrab != null && _syncGrab.IsGrabbed)
        {
            _syncGrab.ForceDetach(); // 억지로 조립할 땐 손에서 강제로 떼어냄
        }
        SnapToOriginalPosition();
    }
    public virtual void ResetPart() => SnapToOriginalPosition();

    public bool IsAllBoltsTightened()
    {
        if(requiredBolts == null || requiredBolts.Count == 0) return true;
        foreach(var b in requiredBolts) if(b != null && !b.isTightened) return false;
        return true;
    }

    public void SetInsideSnapZone(bool isInside)
    {
        _isInsideSnapZone = isInside;
    }
}