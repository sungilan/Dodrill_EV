using FishNet.Object;
using PinePie.SimpleJoystick;
using UnityEngine;

public class FreeLockControllerMobile : NetworkBehaviour
{
    public float moveSpeed = 5f;
    public float lookSensitivity = 5f;
    public float maxLookX = 80f;
    public float minLookX = -80f;

    [Header("Smooth")]
    public float moveSmoothTime = 0.12f;
    public float verticalSmoothTime = 0.12f;
    private Vector3 currentVelocity = Vector3.zero;
    private Vector3 desiredMove = Vector3.zero;
    private float desiredVertical = 0f;
    private float currentVerticalVelocity = 0f;

    private float rotX;
    private float rotY;

    [Header("Joystick/Touch")]
    public JoystickController moveJoyStick;

    private int lookTouchId = -1;
    private Vector2 lastLookTouchPos;

    [Header("State")]
    public bool isInteracting = false;
    private bool isMoving = false;
    private bool isLooking = false;

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (IsOwner == false)
            Destroy(this);
    }

    void Update()
    {
        Move();
        CameraLookTouch();

        // 이동 중이거나 회전 중이면 true
        isInteracting = isMoving || isLooking;
    }

    void Move()
    {
        Vector2 moveInput = Vector2.zero;
        if (moveJoyStick != null)
            moveInput = moveJoyStick.InputDirection;

        // 입력이 거의 없으면 이동 아님
        isMoving = moveInput.sqrMagnitude > 0.01f || Mathf.Abs(desiredVertical) > 0.01f;

        Vector3 moveDir = transform.right * moveInput.x + transform.forward * moveInput.y;
        desiredMove = moveDir * moveSpeed;

        currentVelocity = Vector3.Lerp(currentVelocity, desiredMove, Time.deltaTime / Mathf.Max(0.0001f, moveSmoothTime));

        float targetVerticalVel = desiredVertical * moveSpeed;
        currentVerticalVelocity = Mathf.Lerp(currentVerticalVelocity, targetVerticalVel, Time.deltaTime / Mathf.Max(0.0001f, verticalSmoothTime));

        Vector3 delta = (currentVelocity + Vector3.up * currentVerticalVelocity) * Time.deltaTime;
        transform.position += delta;
    }

    public void MoveUp() => desiredVertical = 0.5f;
    public void MoveDown() => desiredVertical = -0.5f;
    public void StopMove() => desiredVertical = 0f;

    void CameraLookTouch()
    {
        isLooking = false; // 기본값 초기화

        if (Input.touchCount == 0)
        {
            lookTouchId = -1;
            return;
        }

        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);

            if (lookTouchId == -1 && t.phase == TouchPhase.Began && t.position.x > Screen.width * 0.5f)
            {
                lookTouchId = t.fingerId;
                lastLookTouchPos = t.position;
            }

            if (t.fingerId == lookTouchId)
            {
                if (t.phase == TouchPhase.Moved)
                {
                    Vector2 delta = t.position - lastLookTouchPos;
                    rotY += delta.x * lookSensitivity * 0.02f;
                    rotX += delta.y * lookSensitivity * -0.02f;
                    rotX = Mathf.Clamp(rotX, minLookX, maxLookX);
                    transform.rotation = Quaternion.Euler(rotX, rotY, 0f);
                    lastLookTouchPos = t.position;

                    // 터치로 회전 중
                    isLooking = true;
                }

                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                {
                    lookTouchId = -1;
                }
            }
        }
    }
}
