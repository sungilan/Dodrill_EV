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

    // ── (11/20 내부 시연) 차를 올리면 차 쪽 가이드 마커도 같이 올라가게 ──
    // GuideMarker 가 '이 자리를 품은 차'를 물으면, 리프트에 올라간 차의 렌더러 경계 안인지 보고 차를 돌려준다.
    private static readonly System.Collections.Generic.List<VehicleLiftController> s_lifts = new System.Collections.Generic.List<VehicleLiftController>();
    private Renderer[] _vehRenderers;
    private Transform _vehRenderersOf;

    private void OnEnable()
    {
        if (!s_lifts.Contains(this)) s_lifts.Add(this);
        GuideMarker.CarrierAt = CarrierAt;
    }

    private void OnDisable() => s_lifts.Remove(this);

    // 차 안쪽에 따로 놓인 가이드 지점(GuideZoneData)·작업 구역(TaskInteractionZone)은 차의 자식이 아니라서 리프트를 올려도 제자리에 남았다
    // (노란 링·화살표가 공중에 남고, 구역 판정도 어긋남). 차가 움직인 만큼 같이 옮긴다 — 모든 기기에서 각자.
    private readonly System.Collections.Generic.List<Transform> _riders = new System.Collections.Generic.List<Transform>();
    private Vector3 _lastVehPos;
    private Transform _lastVeh;
    private float _nextRiderScan;

    private void LateUpdate()
    {
        var v = CurrentVehicle;
        if (v == null) { _lastVeh = null; return; }
        if (v != _lastVeh) { _lastVeh = v; _lastVehPos = v.position; _riders.Clear(); _nextRiderScan = 0f; }

        Vector3 delta = v.position - _lastVehPos;
        _lastVehPos = v.position;
        if (delta.sqrMagnitude > 1e-10f)
        {
            for (int i = _riders.Count - 1; i >= 0; i--)
            {
                var r = _riders[i];
                if (r == null || r.IsChildOf(v)) { _riders.RemoveAt(i); continue; }
                r.position += delta;
            }
        }
        // 차가 내려와 있을 때만 새로 생긴 지점을 찾는다(올라간 상태에서 찾으면 바닥 쪽 물건을 잘못 태울 수 있다)
        else if (CurrentHeight < 0.05f && Time.unscaledTime >= _nextRiderScan)
        {
            _nextRiderScan = Time.unscaledTime + 2f;
            ScanRiders(v);
        }
    }

    private void ScanRiders(Transform v)
    {
        if (_vehRenderersOf != v) { _vehRenderers = v.GetComponentsInChildren<Renderer>(); _vehRenderersOf = v; }
        bool has = false; Bounds b = default;
        foreach (var r in _vehRenderers)
        {
            if (r == null || !r.enabled || r is ParticleSystemRenderer || r is LineRenderer) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        if (!has) return;
        b.Expand(0.3f);
        void Consider(Component c)
        {
            if (c == null) return;
            var t = c.transform;
            if (t.IsChildOf(v) || _riders.Contains(t) || !b.Contains(t.position)) return;
            if (c.GetComponentInParent<Rigidbody>() != null) return;   // 물리로 따로 움직이는 것(들고 다니는 물건)은 제외
            foreach (var r in _riders) if (r != null && t.IsChildOf(r)) return;   // 부모가 이미 같이 움직인다
            _riders.RemoveAll(r => r != null && r.IsChildOf(t));                 // 자식이 먼저 들어 있었다면 두 번 옮기지 않게 뺀다
            _riders.Add(t);
        }
        foreach (var g in FindObjectsByType<GuideZoneData>(FindObjectsSortMode.None)) Consider(g);
        foreach (var z in FindObjectsByType<TaskInteractionZone>(FindObjectsSortMode.None)) Consider(z);
    }

    private Transform CurrentVehicle => liftMode == LiftMode.TransformBased ? vehicleTransform : animVehicleTransform;

    private static Transform CarrierAt(Vector3 pos)
    {
        foreach (var l in s_lifts)
        {
            if (l == null) continue;
            var v = l.CurrentVehicle;
            if (v == null) continue;
            if (l._vehRenderersOf != v) { l._vehRenderers = v.GetComponentsInChildren<Renderer>(); l._vehRenderersOf = v; }
            bool has = false; Bounds b = default;
            foreach (var r in l._vehRenderers)
            {
                if (r == null || !r.enabled || r is ParticleSystemRenderer || r is LineRenderer) continue;
                if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
            }
            if (!has) continue;
            b.Expand(0.4f);   // 차 표면 바로 바깥(문 앞·바퀴 옆) 마커도 포함
            if (b.Contains(pos)) return v;
        }
        return null;
    }

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