//using UnityEngine;
//using System.Collections;
//using DG.Tweening;

//// ============================================================
////  VehicleLiftController.cs
////  2주식 차량 리프트.
////
////  TransformBased : leftArm / rightArm 직접 이동
////  AnimatorBased  : Animator 블렌드트리 + platformBone 추적
//// ============================================================

//public class VehicleLiftController : LiftControllerBase
//{
//    [Header("── TransformBased 전용 ──")]
//    [Tooltip("올라가는 암(좌)")]
//    public Transform leftArm;
//    [Tooltip("올라가는 암(우)")]
//    public Transform rightArm;
//    [Tooltip("차량 Transform (함께 이동)")]
//    public Transform vehicleTransform;
//    [Tooltip("최대 높이 (m)")]
//    public float maxHeight = 2.0f;

//    [Header("── AnimatorBased 전용 ──")]
//    [Tooltip("상판 위 차량 Transform")]
//    public Transform animVehicleTransform;

//    // 캐시
//    private Vector3 _leftArmOrigin;
//    private Vector3 _rightArmOrigin;
//    private Vector3 _vehicleOrigin;
//    private Vector3 _animVehicleOffset;
//    private bool _offsetReady = false; // 오프셋 계산 완료 여부

//    // ── LiftControllerBase 구현 ───────────────

//    protected override float GetMaxHeight() => maxHeight;

//    protected override void OnLiftStart()
//    {
//        if(liftMode == LiftMode.TransformBased)
//        {
//            if(leftArm != null) _leftArmOrigin = leftArm.localPosition;
//            if(rightArm != null) _rightArmOrigin = rightArm.localPosition;
//            if(vehicleTransform != null) _vehicleOrigin = vehicleTransform.position;
//            _offsetReady = true;
//        }
//        else // AnimatorBased
//        {
//            // Animator는 첫 Update 이후에야 뼈대 위치가 확정됨.
//            // Start에서 바로 계산하면 platformBone이 초기 T-포즈 위치를 반환해 오프셋이 틀림.
//            // → 1프레임 대기 후 계산.
//            StartCoroutine(InitOffsetNextFrame());
//        }
//    }

//    /// <summary>Animator 첫 프레임 적용 후 오프셋 계산</summary>
//    private IEnumerator InitOffsetNextFrame()
//    {
//        yield return null; // 1프레임 대기 → Animator Update 완료

//        RecalculateOffset();
//        _offsetReady = true;
//        Debug.Log($"[VehicleLiftController] AnimVehicleOffset 초기화 완료: {_animVehicleOffset}");
//    }

//    private void RecalculateOffset()
//    {
//        if(platformBone != null && animVehicleTransform != null)
//            _animVehicleOffset = animVehicleTransform.position - platformBone.position;
//    }

//    protected override void ApplyHeightTransform(float h)
//    {
//        Vector3 offset = Vector3.up * h;
//        if(leftArm != null) leftArm.localPosition = _leftArmOrigin + offset;
//        if(rightArm != null) rightArm.localPosition = _rightArmOrigin + offset;
//        if(vehicleTransform != null) vehicleTransform.position = _vehicleOrigin + offset;
//    }

//    // AnimatorBased: Animator 뼈대 이동 후 차량 동기화
//    protected override void SyncPayloadLateUpdate()
//    {
//        if(!_offsetReady) return; // 오프셋 미확정 시 스킵
//        if(platformBone == null || animVehicleTransform == null) return;

//        animVehicleTransform.position = platformBone.position + _animVehicleOffset;
//    }

//    protected override void OnDestroy()
//    {
//        base.OnDestroy();
//        DOTween.Kill(transform);
//    }

//    // ── 공개 유틸 ─────────────────────────────

//    /// <summary>외부에서 차량 재연결 (ScenarioRunner 등)</summary>
//    public void AttachVehicle(Transform vehicle)
//    {
//        if(liftMode == LiftMode.TransformBased)
//        {
//            vehicleTransform = vehicle;
//            _vehicleOrigin = vehicle.position - Vector3.up * _currentHeight;
//        }
//        else
//        {
//            animVehicleTransform = vehicle;
//            RecalculateOffset();
//        }
//    }
//}

using UnityEngine;
using System.Collections;
using DG.Tweening;

public class VehicleLiftController : LiftControllerBase
{
    [Header("── TransformBased 전용 ──")]
    [Tooltip("올라가는 암(좌)")]
    public Transform leftArm;
    [Tooltip("올라가는 암(우)")]
    public Transform rightArm;
    [Tooltip("차량 Transform (함께 이동)")]
    public Transform vehicleTransform;
    [Tooltip("최대 높이 (m)")]
    public float maxHeight = 2.0f;

    [Header("── AnimatorBased 전용 ──")]
    [Tooltip("상판 위 차량 Transform")]
    public Transform animVehicleTransform;

    // 캐시
    private Vector3 _leftArmOrigin;
    private Vector3 _rightArmOrigin;
    private Vector3 _vehicleOrigin;
    private Vector3 _animVehicleOffset;

    protected override float GetMaxHeight() => maxHeight;

    protected override void OnLiftStart()
    {
        if(liftMode == LiftMode.TransformBased)
        {
            if(liftAnimator != null) liftAnimator.enabled = false;
            else
            {
                var anim = GetComponent<Animator>();
                if(anim != null) anim.enabled = false;
            }

            if(leftArm != null) _leftArmOrigin = leftArm.localPosition;
            if(rightArm != null) _rightArmOrigin = rightArm.localPosition;
            if(vehicleTransform != null) _vehicleOrigin = vehicleTransform.position;
        }
        else
        {
            StartCoroutine(InitOffsetNextFrame());
        }

        // ★ [핵심] 리프트가 스스로 차량을 찾도록 탐색 코루틴을 시작합니다.
        StartCoroutine(AutoSearchAndAttachVehicle());
    }

    /// <summary>
    /// 0.5초마다 씬을 뒤져서 차량(Ioniq, Car, Vehicle)을 찾아 스스로 연결하는 무적 코루틴
    /// </summary>
    private IEnumerator AutoSearchAndAttachVehicle()
    {
        var wait = new WaitForSeconds(0.5f);

        while(true)
        {
            // 이미 차량이 연결되어 있다면 탐색을 즉시 종료합니다.
            bool isAttached = (liftMode == LiftMode.TransformBased && vehicleTransform != null) ||
                              (liftMode == LiftMode.AnimatorBased && animVehicleTransform != null);

            if(isAttached) yield break;

            // 씬에 있는 모든 NetworkObject를 뒤집니다. (일반 GameObject 전체 검색보다 성능에 좋음)
            var allNetObjs = FindObjectsByType<FishNet.Object.NetworkObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            foreach(var netObj in allNetObjs)
            {
                // ★ [필터링 1] 이름 조건 확인
                string id = netObj.gameObject.name.ToLower();
                if(!id.Contains("ioniq") && !id.Contains("vehicle")) continue;

                // ★ [필터링 2] 부품(SyncGrab)이 붙은 자식 오브젝트는 제외합니다.
                // 차량 본체 프리팹 루트에는 보통 SyncGrab이 없고 자식 부품들에만 있습니다.
                if(netObj.GetComponent<SyncGrab>() != null) continue;

                // 모든 필터를 통과했다면 진짜 차량 본체입니다!
                AttachVehicle(netObj.transform);
                Debug.Log($"🎉 [VehicleLiftController] 차량 본체({netObj.gameObject.name})를 찾아 자동 연결 완료!");
                yield break;
            }

            // 못 찾았으면 0.5초 뒤에 다시 찾습니다.
            yield return wait;
        }
    }

    private IEnumerator InitOffsetNextFrame()
    {
        yield return null;
        RecalculateOffset();
        Debug.Log($"[VehicleLiftController] AnimVehicleOffset 초기화 완료: {_animVehicleOffset}");
    }

    private void RecalculateOffset()
    {
        if(platformBone != null && animVehicleTransform != null)
            _animVehicleOffset = animVehicleTransform.position - platformBone.position;
    }

    protected override void ApplyHeightTransform(float h)
    {
        Vector3 localOffset = Vector3.up * h;

        // ★ [핵심 2] 루트(transform)가 아닌, 실제 뼈대의 직속 부모(parent) 기준으로 월드 좌표 변환
        if(leftArm != null)
        {
            Vector3 targetLocalPos = _leftArmOrigin + localOffset;
            Vector3 targetWorldPos = leftArm.parent != null ? leftArm.parent.TransformPoint(targetLocalPos) : targetLocalPos;
            UpdateTargetPositionViaLNT(leftArm, targetWorldPos);
        }

        if(rightArm != null)
        {
            Vector3 targetLocalPos = _rightArmOrigin + localOffset;
            Vector3 targetWorldPos = rightArm.parent != null ? rightArm.parent.TransformPoint(targetLocalPos) : targetLocalPos;
            UpdateTargetPositionViaLNT(rightArm, targetWorldPos);
        }

        if(vehicleTransform != null)
        {
            // 차량은 월드 좌표 원본에 높이만 더해서 LNT로 주입
            Vector3 targetWorldPos = _vehicleOrigin + Vector3.up * h;
            UpdateTargetPositionViaLNT(vehicleTransform, targetWorldPos);
        }
    }

    protected override void SyncPayloadLateUpdate()
    {
        if(!_isInitialized || platformBone == null || animVehicleTransform == null) return;

        Vector3 targetWorldPos = platformBone.position + _animVehicleOffset;
        UpdateTargetPositionViaLNT(animVehicleTransform, targetWorldPos);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        DOTween.Kill(transform);
    }

    public void AttachVehicle(Transform vehicle)
    {
        if(liftMode == LiftMode.TransformBased)
        {
            vehicleTransform = vehicle;
            _vehicleOrigin = vehicle.position - Vector3.up * _currentHeight;
        }
        else
        {
            animVehicleTransform = vehicle;
            RecalculateOffset();
        }
    }
}