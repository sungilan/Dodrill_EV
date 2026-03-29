using UnityEngine;
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

    // ── LiftControllerBase 구현 ───────────────

    protected override float GetMaxHeight() => maxHeight;

    protected override void OnLiftStart()
    {
        if (liftMode == LiftMode.TransformBased)
        {
            if (leftArm != null) _leftArmOrigin = leftArm.localPosition;
            if (rightArm != null) _rightArmOrigin = rightArm.localPosition;
            if (vehicleTransform != null) _vehicleOrigin = vehicleTransform.position;
        }
        else // AnimatorBased
        {
            if (platformBone != null && animVehicleTransform != null)
                _animVehicleOffset = animVehicleTransform.position - platformBone.position;
        }
    }

    protected override void ApplyHeightTransform(float h)
    {
        Vector3 offset = Vector3.up * h;
        if (leftArm != null) leftArm.localPosition = _leftArmOrigin + offset;
        if (rightArm != null) rightArm.localPosition = _rightArmOrigin + offset;
        if (vehicleTransform != null) vehicleTransform.position = _vehicleOrigin + offset;
    }

    // AnimatorBased: Animator가 뼈대 이동 후 차량 동기화
    protected override void SyncPayloadLateUpdate()
    {
        if (platformBone == null || animVehicleTransform == null) return;
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
        if (liftMode == LiftMode.TransformBased)
        {
            vehicleTransform = vehicle;
            _vehicleOrigin = vehicle.position - Vector3.up * _currentHeight;
        }
        else
        {
            animVehicleTransform = vehicle;
            if (platformBone != null)
                _animVehicleOffset = vehicle.position - platformBone.position;
        }
    }
}