using UnityEngine;
using System.Collections;
using DG.Tweening;
using FishNet.Object;

public class VehicleLiftController : LiftControllerBase
{
    [Header("── TransformBased 전용 ──")]
    public Transform leftArm;
    public Transform rightArm;
    public Transform vehicleTransform;
    public float maxHeight = 2.0f;

    [Header("── AnimatorBased 전용 ──")]
    public Transform animVehicleTransform;

    private Vector3 _leftArmOrigin;
    private Vector3 _rightArmOrigin;
    private Vector3 _vehicleOrigin;
    private Vector3 _animVehicleOffset;

    protected override float GetMaxHeight() => maxHeight;

    protected override void OnLiftStart()
    {
        Debug.Log($"<color=cyan>[VehicleLift]</color> OnLiftStart 호출됨 (Mode: {liftMode})");

        if (liftMode == LiftMode.TransformBased)
        {
            if (liftAnimator != null) liftAnimator.enabled = false;

            if (leftArm != null)
            {
                _leftArmOrigin = leftArm.localPosition;
                Debug.Log($"<color=white>[VehicleLift]</color> LeftArm 원본 위치 캐시: {_leftArmOrigin}");
            }
            if (rightArm != null)
            {
                _rightArmOrigin = rightArm.localPosition;
                Debug.Log($"<color=white>[VehicleLift]</color> RightArm 원본 위치 캐시: {_rightArmOrigin}");
            }
            if (vehicleTransform != null)
            {
                _vehicleOrigin = vehicleTransform.position;
                Debug.Log($"<color=white>[VehicleLift]</color> 수동 할당된 차량 위치 캐시: {_vehicleOrigin}");
            }
        }
        else
        {
            StartCoroutine(InitOffsetNextFrame());
        }

        // 차량 자동 탐색 시작
        StartCoroutine(AutoSearchAndAttachVehicle());
    }

    private IEnumerator AutoSearchAndAttachVehicle()
    {
        var wait = new WaitForSeconds(0.5f);
        Debug.Log("<color=yellow>[VehicleLift]</color> 차량 본체 자동 탐색 코루틴 시작...");

        while (true)
        {
            // 이미 차량이 연결되어 있다면 탐색을 즉시 종료
            bool isAttached = (liftMode == LiftMode.TransformBased && vehicleTransform != null) ||
                              (liftMode == LiftMode.AnimatorBased && animVehicleTransform != null);

            if (isAttached)
            {
                Debug.Log("<color=lime>[VehicleLift]</color> 차량 연결 감지됨. 탐색 코루틴을 종료합니다.");
                yield break;
            }

            var allNetObjs = FindObjectsByType<NetworkObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            foreach (var netObj in allNetObjs)
            {
                string objName = netObj.gameObject.name;

                // ★ [개선된 필터링]
                // 1. 이름에 'Ioniq5N'이 포함되어야 함
                // 2. 반드시 최상위 부모(root)여야 함 (자식 부품들은 부모가 있으므로 걸러짐)
                // 3. 'Clone' 이름 패턴 확인 (생성된 객체 위주)
                bool isTargetName = objName.Contains("Ioniq5N") || objName.Contains("Vehicle_Root");
                bool isRoot = netObj.transform.parent == null;

                if (isTargetName && isRoot)
                {
                    Debug.Log($"<color=lime>[VehicleLift]</color> 최상위 차량 본체 발견: <b>{objName}</b>. 연결을 시도합니다.");
                    AttachVehicle(netObj.transform);
                    yield break;
                }
            }

            yield return wait;
        }
    }

    private IEnumerator InitOffsetNextFrame()
    {
        yield return null;
        RecalculateOffset();
        Debug.Log($"<color=white>[VehicleLift]</color> Animator 기반 오프셋 초기화 완료: {_animVehicleOffset}");
    }

    private void RecalculateOffset()
    {
        if (platformBone != null && animVehicleTransform != null)
            _animVehicleOffset = animVehicleTransform.position - platformBone.position;
    }

    protected override void ApplyHeightTransform(float h)
    {
        // 이 로그가 찍히면 서버에서 높이 값이 계산되어 내려오고 있는 것입니다.
        // Debug.Log($"<color=white>[VehicleLift]</color> ApplyHeightTransform 실행 중 (h: {h:F3})");

        Vector3 localOffset = Vector3.up * h;

        if (leftArm != null)
        {
            Vector3 targetLocalPos = _leftArmOrigin + localOffset;
            Vector3 targetWorldPos = leftArm.parent != null ? leftArm.parent.TransformPoint(targetLocalPos) : targetLocalPos;
            UpdateTargetPositionViaLNT(leftArm, targetWorldPos);
        }

        if (rightArm != null)
        {
            Vector3 targetLocalPos = _rightArmOrigin + localOffset;
            Vector3 targetWorldPos = rightArm.parent != null ? rightArm.parent.TransformPoint(targetLocalPos) : targetLocalPos;
            UpdateTargetPositionViaLNT(rightArm, targetWorldPos);
        }

        if (vehicleTransform != null)
        {
            Vector3 targetWorldPos = _vehicleOrigin + Vector3.up * h;
            UpdateTargetPositionViaLNT(vehicleTransform, targetWorldPos);
        }
    }

    protected override void SyncPayloadLateUpdate()
    {
        if (!_isInitialized || platformBone == null || animVehicleTransform == null) return;

        Vector3 targetWorldPos = platformBone.position + _animVehicleOffset;
        UpdateTargetPositionViaLNT(animVehicleTransform, targetWorldPos);
    }

    public void AttachVehicle(Transform vehicle)
    {
        if (vehicle == null) return;

        if (liftMode == LiftMode.TransformBased)
        {
            vehicleTransform = vehicle;
            // 현재 리프트 높이가 0이 아닐 경우를 대비해 원본 위치 역산
            _vehicleOrigin = vehicle.position - Vector3.up * CurrentHeight;
            Debug.Log($"<color=lime>[VehicleLift]</color> Transform 기반 차량 연결 완료: {vehicle.name} (Origin: {_vehicleOrigin})");
        }
        else
        {
            animVehicleTransform = vehicle;
            RecalculateOffset();
            Debug.Log($"<color=lime>[VehicleLift]</color> Animator 기반 차량 연결 완료: {vehicle.name}");
        }
    }
}