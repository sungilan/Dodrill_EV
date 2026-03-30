using UnityEngine;
using System.Collections;
using DG.Tweening;

// ============================================================
//  VehicleLiftController.cs
//  2주식 차량 리프트.
//
//  TransformBased : leftArm / rightArm 직접 이동
//  AnimatorBased  : Animator 블렌드트리 + platformBone 추적
// ============================================================

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
    private bool _offsetReady = false; // 오프셋 계산 완료 여부

    // ── LiftControllerBase 구현 ───────────────

    protected override float GetMaxHeight() => maxHeight;

    protected override void OnLiftStart()
    {
        if(liftMode == LiftMode.TransformBased)
        {
            if(leftArm != null) _leftArmOrigin = leftArm.localPosition;
            if(rightArm != null) _rightArmOrigin = rightArm.localPosition;
            if(vehicleTransform != null) _vehicleOrigin = vehicleTransform.position;
            _offsetReady = true;
        }
        else // AnimatorBased
        {
            // Animator는 첫 Update 이후에야 뼈대 위치가 확정됨.
            // Start에서 바로 계산하면 platformBone이 초기 T-포즈 위치를 반환해 오프셋이 틀림.
            // → 1프레임 대기 후 계산.
            StartCoroutine(InitOffsetNextFrame());
        }
    }

    /// <summary>Animator 첫 프레임 적용 후 오프셋 계산</summary>
    private IEnumerator InitOffsetNextFrame()
    {
        yield return null; // 1프레임 대기 → Animator Update 완료

        RecalculateOffset();
        _offsetReady = true;
        Debug.Log($"[VehicleLiftController] AnimVehicleOffset 초기화 완료: {_animVehicleOffset}");
    }

    private void RecalculateOffset()
    {
        if(platformBone != null && animVehicleTransform != null)
            _animVehicleOffset = animVehicleTransform.position - platformBone.position;
    }

    protected override void ApplyHeightTransform(float h)
    {
        Vector3 offset = Vector3.up * h;
        if(leftArm != null) leftArm.localPosition = _leftArmOrigin + offset;
        if(rightArm != null) rightArm.localPosition = _rightArmOrigin + offset;
        if(vehicleTransform != null) vehicleTransform.position = _vehicleOrigin + offset;
    }

    // AnimatorBased: Animator 뼈대 이동 후 차량 동기화
    protected override void SyncPayloadLateUpdate()
    {
        if(!_offsetReady) return; // 오프셋 미확정 시 스킵
        if(platformBone == null || animVehicleTransform == null) return;

        animVehicleTransform.position = platformBone.position + _animVehicleOffset;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        DOTween.Kill(transform);
    }

    // ── 공개 유틸 ─────────────────────────────

    /// <summary>외부에서 차량 재연결 (ScenarioRunner 등)</summary>
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