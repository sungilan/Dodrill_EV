using System.Collections;
using UnityEngine;

public class VRUIFollower : MonoBehaviour
{
    [Header("따라올 카메라")]
    public Transform cameraTransform;

    [Header("거리 설정")]
    public float distance = 1.0f;
    public float heightOffset = 0f;

    [Header("수직 고정")]
    [Tooltip("체크 시 Y축 고정 (좌우만 따라옴)")]
    public bool lockVertical = true;

    [Header("부드러움 설정")]
    public float followSpeed = 2f;
    public float rotateSpeed = 2f;

    [Header("각도 임계값")]
    public float angleThreshold = 30f;

    [Header("거리 임계값")]
    [Tooltip("카메라~UI 수평 오프셋을 시선 전방에 투영한 거리가 설정 distance와 이 값(m) 이상 어긋나면 위치 갱신")]
    public float distanceThreshold = 0.12f;

    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private float fixedY;

    private bool _manualRepositioning;
    private bool _useLocalOffset;

    // 수동 배치 후: 시선 수평 기준 앞뒤 거리 + 높이(상하)
    private float _savedDistance;
    private float _savedHeight;

    void Start()
    {
        if (cameraTransform != null)
            Init(cameraTransform);
        else
            StartCoroutine(WaitForCamera());
    }

    public void SetCamera(Transform cam)
    {
        StopAllCoroutines();
        Init(cam);
    }

    private IEnumerator WaitForCamera()
    {
        while (Camera.main == null)
            yield return null;

        Init(Camera.main.transform);
    }

    private void Init(Transform cam)
    {
        cameraTransform = cam;
        fixedY = cam.position.y + heightOffset;
        SetTargetTransform();
        transform.position = targetPosition;
        transform.rotation = targetRotation;
    }

    void Update()
    {
        if (cameraTransform == null) return;
        if (_manualRepositioning) return;

        if (_useLocalOffset)
        {
            Vector3 flatFwd = cameraTransform.forward;
            flatFwd.y = 0f;
            if (flatFwd.sqrMagnitude < 1e-6f) flatFwd = Vector3.forward;
            flatFwd.Normalize();

            targetPosition = new Vector3(
                cameraTransform.position.x + flatFwd.x * _savedDistance,
                _savedHeight,
                cameraTransform.position.z + flatFwd.z * _savedDistance
            );

            targetRotation = Quaternion.LookRotation(flatFwd, Vector3.up);
        }
        else
        {
            Vector3 forward = cameraTransform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 toUi = transform.position - cameraTransform.position;
            toUi.y = 0f;

            float angle = Vector3.Angle(cameraTransform.forward, transform.position - cameraTransform.position);
            float projDist = toUi.sqrMagnitude > 1e-8f ? Vector3.Dot(toUi, forward) : 0f;

            if (angle > angleThreshold || Mathf.Abs(projDist - distance) > distanceThreshold)
                SetTargetTransform();
        }

        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * followSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotateSpeed);
    }

    // ── Meta 스타일 수동 배치 API ─────────────────────

    public void BeginManualReposition()
    {
        _manualRepositioning = true;
    }

    public void EndManualReposition()
    {
        if (!_manualRepositioning) return;
        _manualRepositioning = false;

        if (cameraTransform != null)
        {
            Vector3 flatFwd = cameraTransform.forward;
            flatFwd.y = 0f;
            if (flatFwd.sqrMagnitude < 1e-6f) flatFwd = Vector3.forward;
            flatFwd.Normalize();

            Vector3 toUi = transform.position - cameraTransform.position;
            Vector3 toFlat = new Vector3(toUi.x, 0f, toUi.z);
            _savedDistance = Mathf.Max(0.3f, Vector3.Dot(toFlat, flatFwd));
            _savedHeight = transform.position.y;

            _useLocalOffset = true;
        }
    }

    /// <summary>수동 배치 모드를 해제하고 기본 정면 Follow로 복귀</summary>
    public void ResetToDefaultFollow()
    {
        _useLocalOffset = false;
        _manualRepositioning = false;
    }

    // ── 기본 정면 Follow 목표 계산 ────────────────────

    void SetTargetTransform()
    {
        if (cameraTransform == null) return;

        Vector3 forward = cameraTransform.forward;
        forward.y = 0f;
        forward.Normalize();

        float originY = lockVertical ? fixedY : cameraTransform.position.y + heightOffset;

        targetPosition = new Vector3(
            cameraTransform.position.x,
            originY,
            cameraTransform.position.z
        ) + forward * distance;

        targetRotation = Quaternion.LookRotation(forward);
    }
}