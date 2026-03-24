using UnityEngine;

public class BillboardUI : MonoBehaviour
{
    public Transform playerCamera;
    public float distance = 1.5f; // 유저와의 거리
    public float followSpeed = 5f;

    void Update()
    {
        // 1. 카메라 정면 일정 거리 유지
        Vector3 targetPos = playerCamera.position + (playerCamera.forward * distance);
        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * followSpeed);

        // 2. 유저를 항상 바라보게 함
        transform.LookAt(playerCamera);
        transform.Rotate(0, 180, 0); // UI가 뒤집히지 않게 보정
    }
}