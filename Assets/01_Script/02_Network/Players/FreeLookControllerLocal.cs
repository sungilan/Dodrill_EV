using FishNet.Object;
using System.Globalization;
using UnityEngine;

public class FreeLookControllerLocal : MonoBehaviour
{
    public bool enableMovement = true;
    public float moveSpeed = 5f;       // 이동 속도
    public float lookSensitivity = 2f; // 마우스 감도
    public float maxLookX = 80f;       // 위쪽 회전 제한
    public float minLookX = -80f;      // 아래쪽 회전 제한

    // 수직 이동 부드러움
    [Header("Vertical Smooth")]
    public float verticalSmoothTime = 0.12f;
    private float desiredVertical = 0f;            // 외부에서 설정(상승:1, 하강:-1, 정지:0)
    private float currentVerticalVelocity = 0f;    // 보간된 수직 속도 (m/s)

    private float rotX; // 카메라 pitch 값

    void Update()
    {
        Move();

        // 우클릭을 누르고 있을 때만 카메라 회전
        if (Input.GetMouseButton(1))
        {
            CameraLook();
        }

        if (Input.GetKeyDown(KeyCode.Q))
        {
            MoveUp();
        }
        if (Input.GetKeyDown(KeyCode.E))
        {
            MoveDown();
        }

        if (Input.GetKeyUp(KeyCode.Q))
        {
            StopVertical();
        }
        if (Input.GetKeyUp(KeyCode.E))
        {
            StopVertical();
        }
    }

    void Move()
    {
        float x = Input.GetAxis("Horizontal"); // A, D
        float z = Input.GetAxis("Vertical");   // W, S

        Vector3 moveDir = transform.right * x + transform.forward * z;
        Vector3 horizontalVelocity = moveDir * moveSpeed;

        // 수직 목표 속도(상승/하강) 계산, moveSpeed 공유
        float targetVerticalVel = desiredVertical * moveSpeed;
        // 부드럽게 보간
        currentVerticalVelocity = Mathf.Lerp(currentVerticalVelocity, targetVerticalVel, Time.deltaTime / Mathf.Max(0.0001f, verticalSmoothTime));

        // 위치 갱신 (수평 + 수직)
        Vector3 delta = (horizontalVelocity + Vector3.up * currentVerticalVelocity) * Time.deltaTime;
        transform.position += delta;
    }

    void CameraLook()
    {
        float y = Input.GetAxis("Mouse X") * lookSensitivity; // 좌우
        rotX += Input.GetAxis("Mouse Y") * lookSensitivity * -1; // 상하 (반전)

        rotX = Mathf.Clamp(rotX, minLookX, maxLookX);

        // 회전 적용
        transform.eulerAngles = new Vector3(rotX, transform.eulerAngles.y + y, 0f);
    }

    // 위로 상승 (매개변수 없음, 내부 desiredVertical 설정)
    public void MoveUp()
    {
        desiredVertical = 0.5f;
    }

    // 아래로 하강 (매개변수 없음, 내부 desiredVertical 설정)
    public void MoveDown()
    {
        desiredVertical = -0.5f;
    }

    // 상승/하강 중지(원하면 호출)
    public void StopVertical()
    {
        desiredVertical = 0f;
    }
}
