using UnityEngine;
using System.Collections;
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
        Debug.Log($"<color=cyan>[VehicleLift]</color> ✓ OnLiftStart 호출됨! (Mode: {liftMode})");

        if(liftMode == LiftMode.TransformBased)
        {
            if(liftAnimator != null)
                liftAnimator.enabled = false;

            if(leftArm != null)
            {
                _leftArmOrigin = leftArm.localPosition;
                Debug.Log($"<color=lime>✓ LeftArm 캐시: {_leftArmOrigin}</color>");
            }
            else
                Debug.LogError("<color=red>✗ LeftArm이 null!</color>");

            if(rightArm != null)
            {
                _rightArmOrigin = rightArm.localPosition;
                Debug.Log($"<color=lime>✓ RightArm 캐시: {_rightArmOrigin}</color>");
            }
            else
                Debug.LogError("<color=red>✗ RightArm이 null!</color>");

            if(vehicleTransform != null)
            {
                _vehicleOrigin = vehicleTransform.position;
                Debug.Log($"<color=lime>✓ 차량 위치 캐시: {_vehicleOrigin}</color>");
            }
            else
                Debug.LogWarning("<color=yellow>⚠ 차량이 아직 할당되지 않음</color>");
        }
        else
        {
            Debug.Log("<color=cyan>[VehicleLift]</color> AnimatorBased 모드 초기화");
            StartCoroutine(InitOffsetNextFrame());
        }

        // 차량 자동 탐색 시작
        StartCoroutine(AutoSearchAndAttachVehicle());
    }

    private IEnumerator AutoSearchAndAttachVehicle()
    {
        var wait = new WaitForSeconds(0.5f);
        Debug.Log("<color=yellow>[VehicleLift]</color> 차량 본체 자동 탐색 코루틴 시작...");

        int attemptCount = 0;
        while(true)
        {
            attemptCount++;

            // 이미 차량이 연결되어 있다면 탐색을 즉시 종료
            bool isAttached = (liftMode == LiftMode.TransformBased && vehicleTransform != null) ||
                              (liftMode == LiftMode.AnimatorBased && animVehicleTransform != null);

            if(isAttached)
            {
                Debug.Log("<color=lime>[VehicleLift]</color> 차량 연결 감지됨. 탐색 코루틴을 종료합니다.");
                yield break;
            }

            var allNetObjs = FindObjectsByType<NetworkObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Debug.Log($"<color=yellow>[VehicleLift]</color> 차량 탐색 시도 #{attemptCount}: 찾은 NetworkObject 개수 = {allNetObjs.Length}");

            foreach(var netObj in allNetObjs)
            {
                string objName = netObj.gameObject.name;
                bool isTargetName = objName.Contains("Ioniq5N") || objName.Contains("Vehicle_Root");
                bool isRoot = netObj.transform.parent == null;

                if(isTargetName && isRoot)
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
        if(platformBone != null && animVehicleTransform != null)
            _animVehicleOffset = animVehicleTransform.position - platformBone.position;
    }

    protected override void ApplyHeightTransform(float h)
    {
        if(h == 0f) return;

        // (높이가 바뀔 때마다 찍던 이동 로그 — 프레임마다 6줄씩 나와 제거. 문제는 아래 Vehicle 미할당 경고로 확인)
        Vector3 localOffset = Vector3.up * h;

        if(leftArm != null)
        {
            Vector3 targetLocalPos = _leftArmOrigin + localOffset;
            Vector3 targetWorldPos = leftArm.parent != null
                ? leftArm.parent.TransformPoint(targetLocalPos)
                : targetLocalPos;

            UpdateTargetPositionViaLNT(leftArm, targetWorldPos);

            // 이동 후 실제 위치 확인
        }

        if(rightArm != null)
        {
            Vector3 targetLocalPos = _rightArmOrigin + localOffset;
            Vector3 targetWorldPos = rightArm.parent != null
                ? rightArm.parent.TransformPoint(targetLocalPos)
                : targetLocalPos;

            UpdateTargetPositionViaLNT(rightArm, targetWorldPos);

        }

        if(vehicleTransform != null)
        {
            Vector3 targetWorldPos = _vehicleOrigin + Vector3.up * h;
            UpdateTargetPositionViaLNT(vehicleTransform, targetWorldPos);

        }
        else
        {
            Debug.LogWarning($"<color=red>✗ Vehicle이 할당되지 않아 이동할 수 없습니다!</color>");
        }
    }

    protected override void SyncPayloadLateUpdate()
    {
        if(!_isInitialized || platformBone == null || animVehicleTransform == null) return;

        Vector3 targetWorldPos = platformBone.position + _animVehicleOffset;
        UpdateTargetPositionViaLNT(animVehicleTransform, targetWorldPos);
    }

    public void AttachVehicle(Transform vehicle)
    {
        var rb = vehicle.GetComponent<Rigidbody>();
        if(rb != null)
        {
            rb.isKinematic = true; // 리프트 이동 중 물리 계산 중단
            rb.useGravity = false;
        }

        if(vehicle == null)
        {
            Debug.LogError("[VehicleLift] AttachVehicle: 전달된 vehicle이 null입니다!");
            return;
        }

        if(liftMode == LiftMode.TransformBased)
        {
            vehicleTransform = vehicle;
            _vehicleOrigin = vehicle.position - Vector3.up * CurrentHeight;
            Debug.Log($"<color=lime>[VehicleLift]</color> ✓ Transform 기반 차량 연결 완료: {vehicle.name} (Origin: {_vehicleOrigin})");
        }
        else
        {
            animVehicleTransform = vehicle;
            RecalculateOffset();
            Debug.Log($"<color=lime>[VehicleLift]</color> ✓ Animator 기반 차량 연결 완료: {vehicle.name}");
        }
    }
}