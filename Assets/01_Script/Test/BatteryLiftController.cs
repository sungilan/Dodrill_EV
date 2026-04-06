//using UnityEngine;

//public class BatteryLiftController : LiftControllerBase
//{
//    [Header("── TransformBased 전용 ──")]
//    public Transform liftPlate;
//    public float maxHeight = 0.5f;

//    [Header("── 배터리팩 연동 ──")]
//    public Transform batteryPackTransform;
//    public Transform animBatteryPackTransform;
//    public float contactHeight = 0.08f;

//    [Header("배터리 상태 (읽기 전용)")]
//    [SerializeField] private bool _isBatteryAttached = false;

//    private Vector3 _plateOrigin;
//    private Vector3 _batteryOrigin;
//    private Vector3 _animBatteryOffset;

//    protected override float GetMaxHeight() => maxHeight;

//    protected override void OnLiftStart()
//    {
//        if(liftMode == LiftMode.TransformBased)
//        {
//            // 배터리 리프트도 Animator가 뼈대를 잡고 있을 수 있으므로 꺼줌
//            if(liftAnimator != null) liftAnimator.enabled = false;
//            else
//            {
//                var anim = GetComponent<Animator>();
//                if(anim != null) anim.enabled = false;
//            }

//            if(liftPlate != null) _plateOrigin = liftPlate.localPosition;
//            if(batteryPackTransform != null) _batteryOrigin = batteryPackTransform.position;
//        }
//        else
//        {
//            if(platformBone != null && animBatteryPackTransform != null)
//                _animBatteryOffset = animBatteryPackTransform.position - platformBone.position;
//        }
//    }

//    protected override void ApplyHeightTransform(float h)
//    {
//        // ★ 실제 직속 부모 기준으로 월드 좌표 변환
//        if(liftPlate != null)
//        {
//            Vector3 targetLocalPos = _plateOrigin + Vector3.up * h;
//            Vector3 targetWorldPos = liftPlate.parent != null ? liftPlate.parent.TransformPoint(targetLocalPos) : targetLocalPos;
//            UpdateTargetPositionViaLNT(liftPlate, targetWorldPos);
//        }

//        if(_isBatteryAttached && batteryPackTransform != null)
//        {
//            UpdateTargetPositionViaLNT(batteryPackTransform, _batteryOrigin + Vector3.up * h);
//        }
//    }

//    protected override void SyncPayloadLateUpdate()
//    {
//        if(!_isBatteryAttached || !_isInitialized) return;
//        if(platformBone == null || animBatteryPackTransform == null) return;

//        Vector3 targetWorldPos = platformBone.position + _animBatteryOffset;
//        UpdateTargetPositionViaLNT(animBatteryPackTransform, targetWorldPos);
//    }

//    protected override void OnHeightChanged(float newHeight, float prevHeight)
//    {
//        if(NetworkObject == null || !IsServerInitialized) return;

//        if(_isBatteryAttached) return;
//        if(_direction != 1) return;
//        if(newHeight < contactHeight) return;

//        AttachBattery();
//    }

//    protected override void OnReachedBottom()
//    {
//        if(NetworkObject == null || !IsServerInitialized) return;
//        if(_isBatteryAttached) DetachBattery();
//    }

//    private void AttachBattery()
//    {
//        _isBatteryAttached = true;

//        if(liftMode == LiftMode.TransformBased && batteryPackTransform != null)
//            _batteryOrigin = batteryPackTransform.position - Vector3.up * _currentHeight;

//        if(liftMode == LiftMode.AnimatorBased && platformBone != null && animBatteryPackTransform != null)
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

//    public bool IsBatteryAttached => _isBatteryAttached;

//    public void SetBatteryPack(Transform pack)
//    {
//        if(liftMode == LiftMode.TransformBased)
//        {
//            batteryPackTransform = pack;
//            _batteryOrigin = pack.position;
//        }
//        else
//        {
//            animBatteryPackTransform = pack;
//            if(platformBone != null)
//                _animBatteryOffset = pack.position - platformBone.position;
//        }
//    }
//}

using UnityEngine;
using FishNet.Object;

/// <summary>
/// 배터리 리프트(배터리 잭) 컨트롤러.
/// 차량 하부 정위치(Zone)에 있고 특정 높이에 도달했을 때 배터리 팩을 논리적으로 연결함.
/// </summary>
public class BatteryLiftController : LiftControllerBase
{
    [Header("── TransformBased 전용 ──")]
    public Transform liftPlate;
    public float maxHeight = 0.5f;

    [Header("── 배터리팩 연동 ──")]
    public Transform batteryPackTransform;
    public Transform animBatteryPackTransform;

    [Tooltip("부착을 시도할 최소 높이 (Animator모드면 0~1 사이 정규화된 값)")]
    public float contactHeight = 0.08f;

    [Header("배터리 상태 (읽기 전용)")]
    [SerializeField] private bool _isBatteryAttached = false;

    // 존 방식 판정을 위한 내부 상태
    private bool _isInPosition = false;

    private Vector3 _plateOrigin;
    private Vector3 _batteryOrigin;
    private Vector3 _animBatteryOffset;

    protected override float GetMaxHeight() => maxHeight;

    protected override void OnLiftStart()
    {
        if(liftMode == LiftMode.TransformBased)
        {
            // 애니메이터 간섭 차단
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

        Debug.Log($"[BatteryLift] 시작 - 모드: {liftMode}, 목표 접촉 높이: {contactHeight}");
    }

    protected override void ApplyHeightTransform(float h)
    {
        // 리프트 판넬 이동 (LNT 동기화)
        if(liftPlate != null)
        {
            Vector3 targetLocalPos = _plateOrigin + Vector3.up * h;
            Vector3 targetWorldPos = liftPlate.parent != null ? liftPlate.parent.TransformPoint(targetLocalPos) : targetLocalPos;
            UpdateTargetPositionViaLNT(liftPlate, targetWorldPos);
        }

        // 배터리가 부착된 상태라면 배터리도 함께 위로 이동
        if(_isBatteryAttached && batteryPackTransform != null)
        {
            UpdateTargetPositionViaLNT(batteryPackTransform, _batteryOrigin + Vector3.up * h);
        }
    }

    protected override void SyncPayloadLateUpdate()
    {
        // AnimatorBased 모드에서 상판 본(Bone)의 위치에 배터리를 강제 정렬
        if(!_isBatteryAttached || !_isInitialized) return;
        if(platformBone == null || animBatteryPackTransform == null) return;

        Vector3 targetWorldPos = platformBone.position + _animBatteryOffset;
        UpdateTargetPositionViaLNT(animBatteryPackTransform, targetWorldPos);
    }

    protected override void OnHeightChanged(float newHeight, float prevHeight)
    {
        // 판정은 서버 권위로만 수행
        //if(NetworkObject == null || !IsServerInitialized) return;

        // 이미 붙어있거나 내려가는 중이면 체크 안 함
        if(_isBatteryAttached) return;
        if(_direction != 1) return;

        // 디버깅 로그: 높이 변화 실시간 출력 (image_9c28ba.jpg 기준 Animator 모드 값 확인용)
        if(Mathf.Abs(newHeight - prevHeight) > 0.001f)
        {
            Debug.Log($"[BatteryLift] 상승 중... 현재 높이: {newHeight:F3} (목표: {contactHeight}), 존 진입: {_isInPosition}");
        }

        // 1. 높이 조건 체크
        if(newHeight < contactHeight) return;

        // 2. 존 방식 체크 (차량 아래 정위치인지 확인)
        if(!_isInPosition)
        {
            // 높이는 도달했으나 위치가 틀렸을 때 경고 (매 프레임 방지 위해 가끔 출력)
            if(Time.frameCount % 60 == 0)
                Debug.LogWarning("[BatteryLift] 접촉 높이 도달! 하지만 차량 정위치(Zone)가 아닙니다.");
            return;
        }

        // 모든 조건 충족 시 부착
        AttachBattery();
    }

    protected override void OnReachedBottom()
    {
        if(NetworkObject == null || !IsServerInitialized) return;

        Debug.Log("[BatteryLift] 하단 도달 - 배터리 분리 시도");
        if(_isBatteryAttached) DetachBattery();
    }

    private void AttachBattery()
    {
        _isBatteryAttached = true;

        if(liftMode == LiftMode.TransformBased && batteryPackTransform != null)
            _batteryOrigin = batteryPackTransform.position - Vector3.up * _currentHeight;

        if(liftMode == LiftMode.AnimatorBased && platformBone != null && animBatteryPackTransform != null)
            _animBatteryOffset = animBatteryPackTransform.position - platformBone.position;

        Debug.Log("[BatteryLift] ★ 배터리팩 부착 성공 ★");

        // 시나리오 시스템에 상태 보고
        InteractionEvents.FireZoneActivated("BatteryJack_Contact", "BatteryJack");
    }

    private void DetachBattery()
    {
        _isBatteryAttached = false;
        Debug.Log("[BatteryLift] ★ 배터리팩 분리(바닥 하강) 완료 ★");

        // 다음 단계를 위한 이벤트 발행
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

    // ── 존(Zone) 판정 로직 ───────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        // 씬의 배터리 안착 구역(Trigger) 식별
        if(other.name.Contains("Battery_Mount") || other.CompareTag("BatteryZone"))
        {
            _isInPosition = true;
            Debug.Log("[BatteryLift] 차량 하부 정위치 진입 완료");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if(other.name.Contains("Battery_Mount") || other.CompareTag("BatteryZone"))
        {
            _isInPosition = false;
            Debug.Log("[BatteryLift] 차량 하부 정위치 이탈");
        }
    }
}