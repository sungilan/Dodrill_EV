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

    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private float fixedY;

    void Start()
    {
        if (cameraTransform != null)
            Init(cameraTransform);
        else
            StartCoroutine(WaitForCamera());
    }

    // PlayerUISetup 등 외부에서 카메라 주입 시 호출
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

        float angle = Vector3.Angle(
            cameraTransform.forward,
            transform.position - cameraTransform.position
        );

        if (angle > angleThreshold)
            SetTargetTransform();

        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * followSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotateSpeed);
    }

    void SetTargetTransform()
    {
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