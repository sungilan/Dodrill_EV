//using UnityEngine;
//using FishNet;
//using FishNet.Broadcast;
//using FishNet.Transporting;

//// ============================================================
////  BatteryLiftController.cs
////  배터리팩 전용 잭.
////
////  LiftControllerBase의 공통 기능 + 배터리 전용:
////    - contactHeight: 특정 높이에서 배터리팩 자동 연결
////    - 완전히 내려오면 자동 분리
////    - InteractionEvents 발행 (Task 완료 판정)
////
////  AnimatorBased 권장:
////    시저 구조 기구이므로 Animator 블렌드트리 사용.
////    platformBone에 상판 본 연결 → 배터리팩이 따라 이동.
//// ============================================================

//public class BatteryLiftController : LiftControllerBase
//{
//    [Header("── TransformBased 전용 ──")]
//    [Tooltip("올라가는 플랫폼 Transform")]
//    public Transform liftPlate;
//    [Tooltip("최대 높이 (m)")]
//    public float maxHeight = 0.5f;

//    [Header("── 배터리팩 연동 ──")]
//    [Tooltip("배터리팩 Transform.\nTransformBased: 직접 이동 / AnimatorBased: animBatteryPackTransform 사용")]
//    public Transform batteryPackTransform;
//    [Tooltip("AnimatorBased 전용 — platformBone을 따라 이동할 배터리팩 Transform")]
//    public Transform animBatteryPackTransform;
//    [Tooltip("배터리팩 자동 연결 높이.\nTransformBased: m / AnimatorBased: normalized(0~1)")]
//    public float contactHeight = 0.08f;

//    // 상태
//    [Header("배터리 상태 (읽기 전용)")]
//    [SerializeField] private bool _isBatteryAttached = false;

//    // 캐시
//    private Vector3 _plateOrigin;
//    private Vector3 _batteryOrigin;
//    private Vector3 _animBatteryOffset;

//    // ── LiftControllerBase 구현 ───────────────

//    protected override float GetMaxHeight() => maxHeight;

//    protected override void OnLiftStart()
//    {
//        if (liftMode == LiftMode.TransformBased)
//        {
//            if (liftPlate != null)
//                _plateOrigin = liftPlate.localPosition;
//            if (batteryPackTransform != null)
//                _batteryOrigin = batteryPackTransform.position;
//        }
//        else // AnimatorBased
//        {
//            if (platformBone != null && animBatteryPackTransform != null)
//                _animBatteryOffset = animBatteryPackTransform.position - platformBone.position;
//        }
//    }

//    protected override void ApplyHeightTransform(float h)
//    {
//        if (liftPlate != null)
//            liftPlate.localPosition = _plateOrigin + Vector3.up * h;

//        if (_isBatteryAttached && batteryPackTransform != null)
//            batteryPackTransform.position = _batteryOrigin + Vector3.up * h;
//    }

//    // AnimatorBased: 배터리팩을 상판 본에 동기화
//    protected override void SyncPayloadLateUpdate()
//    {
//        if (!_isBatteryAttached) return;
//        if (platformBone == null || animBatteryPackTransform == null) return;
//        animBatteryPackTransform.position = platformBone.position + _animBatteryOffset;
//    }

//    // 높이 변화마다 접촉 체크
//    protected override void OnHeightChanged(float newHeight, float prevHeight)
//    {
//        if (_isBatteryAttached) return;
//        if (_direction != 1) return;
//        if (newHeight < contactHeight) return;
//        AttachBattery();
//    }

//    // 완전히 내려오면 배터리팩 분리
//    protected override void OnReachedBottom()
//    {
//        if (_isBatteryAttached) DetachBattery();
//    }

//    // ── 배터리팩 연결/분리 ────────────────────

//    private void AttachBattery()
//    {
//        _isBatteryAttached = true;

//        if (liftMode == LiftMode.TransformBased && batteryPackTransform != null)
//            _batteryOrigin = batteryPackTransform.position - Vector3.up * _currentHeight;

//        if (liftMode == LiftMode.AnimatorBased && platformBone != null && animBatteryPackTransform != null)
//            _animBatteryOffset = animBatteryPackTransform.position - platformBone.position;

//        Debug.Log("[BatteryLift] 배터리팩 연결됨");
//        InteractionEvents.FireZoneActivated("BatteryJack_Contact", "BatteryJack");
//    }

//    private void DetachBattery()
//    {
//        _isBatteryAttached = false;
//        Debug.Log("[BatteryLift] 배터리팩 분리됨");
//        InteractionEvents.FireZoneActivated("BatteryPack_BoltGroup", "BatteryJack");
//    }

//    // ── UI 텍스트 재정의 ──────────────────────

//    protected override string GetHeightDisplayText()
//    {
//        string state = _isMoving
//            ? (_direction > 0 ? "▲ 상승 중" : "▼ 하강 중")
//            : (_isBatteryAttached ? "■ 배터리 지지 중" : "■ 대기");

//        string heightStr = liftMode == LiftMode.TransformBased
//            ? $"{_currentHeight * 100:F0}cm"
//            : $"{_currentHeight * 100:F0}%";

//        return $"{state}\n{heightStr}";
//    }

//    // ── 공개 유틸 ─────────────────────────────

//    public bool IsBatteryAttached => _isBatteryAttached;

//    /// <summary>배터리팩 외부 연결 (스폰 후 호출)</summary>
//    public void SetBatteryPack(Transform pack)
//    {
//        if (liftMode == LiftMode.TransformBased)
//        {
//            batteryPackTransform = pack;
//            _batteryOrigin = pack.position;
//        }
//        else
//        {
//            animBatteryPackTransform = pack;
//            if (platformBone != null)
//                _animBatteryOffset = pack.position - platformBone.position;
//        }
//    }

//    public void DebugRaiseImmediate()
//    {
//        _currentHeight = MaxValue;
//        _isBatteryAttached = true;
//        _isMoving = false;
//        ApplyHeight(_currentHeight);
//        BroadcastState();
//    }

//    public void DebugLowerImmediate()
//    {
//        _currentHeight = 0f;
//        _isBatteryAttached = false;
//        _isMoving = false;
//        ApplyHeight(_currentHeight);
//        BroadcastState();
//    }
//}

using UnityEngine;

public class BatteryLiftController : LiftControllerBase
{
    [Header("── TransformBased 전용 ──")]
    public Transform liftPlate;
    public float maxHeight = 0.5f;

    [Header("── 배터리팩 연동 ──")]
    public Transform batteryPackTransform;
    public Transform animBatteryPackTransform;
    public float contactHeight = 0.08f;

    [Header("배터리 상태 (읽기 전용)")]
    [SerializeField] private bool _isBatteryAttached = false;

    private Vector3 _plateOrigin;
    private Vector3 _batteryOrigin;
    private Vector3 _animBatteryOffset;

    protected override float GetMaxHeight() => maxHeight;

    protected override void OnLiftStart()
    {
        if(liftMode == LiftMode.TransformBased)
        {
            // 배터리 리프트도 Animator가 뼈대를 잡고 있을 수 있으므로 꺼줌
            if(liftAnimator != null) liftAnimator.enabled = false;
            else
            {
                var anim = GetComponent<Animator>();
                if(anim != null) anim.enabled = false;
            }

            if(liftPlate != null) _plateOrigin = liftPlate.localPosition;
            if(batteryPackTransform != null) _batteryOrigin = batteryPackTransform.position;
        }
        else
        {
            if(platformBone != null && animBatteryPackTransform != null)
                _animBatteryOffset = animBatteryPackTransform.position - platformBone.position;
        }
    }

    protected override void ApplyHeightTransform(float h)
    {
        // ★ 실제 직속 부모 기준으로 월드 좌표 변환
        if(liftPlate != null)
        {
            Vector3 targetLocalPos = _plateOrigin + Vector3.up * h;
            Vector3 targetWorldPos = liftPlate.parent != null ? liftPlate.parent.TransformPoint(targetLocalPos) : targetLocalPos;
            UpdateTargetPositionViaLNT(liftPlate, targetWorldPos);
        }

        if(_isBatteryAttached && batteryPackTransform != null)
        {
            UpdateTargetPositionViaLNT(batteryPackTransform, _batteryOrigin + Vector3.up * h);
        }
    }

    protected override void SyncPayloadLateUpdate()
    {
        if(!_isBatteryAttached || !_isInitialized) return;
        if(platformBone == null || animBatteryPackTransform == null) return;

        Vector3 targetWorldPos = platformBone.position + _animBatteryOffset;
        UpdateTargetPositionViaLNT(animBatteryPackTransform, targetWorldPos);
    }

    protected override void OnHeightChanged(float newHeight, float prevHeight)
    {
        if(NetworkObject == null || !IsServerInitialized) return;

        if(_isBatteryAttached) return;
        if(_direction != 1) return;
        if(newHeight < contactHeight) return;

        AttachBattery();
    }

    protected override void OnReachedBottom()
    {
        if(NetworkObject == null || !IsServerInitialized) return;
        if(_isBatteryAttached) DetachBattery();
    }

    private void AttachBattery()
    {
        _isBatteryAttached = true;

        if(liftMode == LiftMode.TransformBased && batteryPackTransform != null)
            _batteryOrigin = batteryPackTransform.position - Vector3.up * _currentHeight;

        if(liftMode == LiftMode.AnimatorBased && platformBone != null && animBatteryPackTransform != null)
            _animBatteryOffset = animBatteryPackTransform.position - platformBone.position;

        Debug.Log("[BatteryLift] 배터리팩 연결됨");
        InteractionEvents.FireZoneActivated("BatteryJack_Contact", "BatteryJack");
    }

    private void DetachBattery()
    {
        _isBatteryAttached = false;
        Debug.Log("[BatteryLift] 배터리팩 분리됨");
        InteractionEvents.FireZoneActivated("BatteryPack_BoltGroup", "BatteryJack");
    }

    protected override string GetHeightDisplayText()
    {
        string state = _isMoving
            ? (_direction > 0 ? "▲ 상승 중" : "▼ 하강 중")
            : (_isBatteryAttached ? "■ 배터리 지지 중" : "■ 대기");

        string heightStr = liftMode == LiftMode.TransformBased
            ? $"{_currentHeight * 100:F0}cm"
            : $"{_currentHeight * 100:F0}%";

        return $"{state}\n{heightStr}";
    }

    public bool IsBatteryAttached => _isBatteryAttached;

    public void SetBatteryPack(Transform pack)
    {
        if(liftMode == LiftMode.TransformBased)
        {
            batteryPackTransform = pack;
            _batteryOrigin = pack.position;
        }
        else
        {
            animBatteryPackTransform = pack;
            if(platformBone != null)
                _animBatteryOffset = pack.position - platformBone.position;
        }
    }
}