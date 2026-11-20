using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// ============================================================
//  SimplePcInteractor.cs — 최소 PC 조작부 (모듈화, 2026-11-20)
//
//  콘텐츠 조작부(EV 의 FreeLookController) 없이 시나리오 단계를 진행할 수 있는 기본 1인칭 조작.
//  카메라에 붙인다.
//    · WASD 이동, Q/E 아래/위, 마우스 오른쪽 버튼을 누른 채 끌어 시점 회전
//    · 왼쪽 클릭
//        - 빈손: 클릭 대상(IWorldClickable: TaskClickTarget 등) 누르기 / 작업 아이템(TaskItem) 집기 / 작업 구역 터치
//        - 들고 있을 때: 작업 구역(TaskInteractionZone)을 누르면 그 자리에 놓기, 그 외 바닥 등에 내려놓기
//    · F: 들고 있는 아이템 사용(UseItem 단계)
//  신호는 InteractionEvents 로 내고, NetworkInteractionBridge 가 서버로 보낸다.
//  ※ 네트워크로 동기화되는 아이템(SyncGrab)은 SyncGrab 의 PC 잡기를 쓴다. 여기서 직접 드는 것은 씬에 놓인 일반 아이템.
// ============================================================
public class SimplePcInteractor : MonoBehaviour
{
    public Camera cam;
    public float moveSpeed = 2.5f;
    public float lookSpeed = 0.15f;
    public float reach = 5f;
    [Tooltip("들고 있을 때 카메라 기준 위치")]
    public Vector3 holdOffset = new Vector3(0.25f, -0.2f, 0.6f);

    private TaskItem _held;
    private Transform _heldParent;
    private Collider[] _heldCols;
    private Rigidbody _heldRb;
    private float _yaw, _pitch;

    public TaskItem Held => _held;

    private void Awake()
    {
        if (cam == null) cam = GetComponent<Camera>() ?? Camera.main;
        var e = transform.eulerAngles;
        _yaw = e.y; _pitch = e.x > 180f ? e.x - 360f : e.x;
    }

    private void Update()
    {
        Move();
        if (LeftDown()) Click();
        if (UseDown() && _held != null) InteractionEvents.FireItemUsed(_held.prefabId);
    }

    // ── 이동·시점 ─────────────────────────────
    private void Move()
    {
        Vector3 dir = Vector3.zero;
        if (KeyHeld('W')) dir += Vector3.forward;
        if (KeyHeld('S')) dir += Vector3.back;
        if (KeyHeld('A')) dir += Vector3.left;
        if (KeyHeld('D')) dir += Vector3.right;
        if (KeyHeld('Q')) dir += Vector3.down;
        if (KeyHeld('E')) dir += Vector3.up;
        transform.position += transform.TransformDirection(dir.normalized) * moveSpeed * Time.deltaTime;

        if (RightHeld())
        {
            Vector2 d = MouseDelta();
            _yaw += d.x * lookSpeed;
            _pitch = Mathf.Clamp(_pitch - d.y * lookSpeed, -85f, 85f);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }
    }

    // ── 클릭 ─────────────────────────────
    private void Click()
    {
        if (cam == null) return;
        Ray ray = cam.ScreenPointToRay(MousePos());
        var hits = Physics.RaycastAll(ray, reach, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        if (_held != null)
        {
            foreach (var h in hits)
            {
                var zone = h.collider.GetComponentInParent<TaskInteractionZone>();
                if (zone != null && h.collider.enabled) { PlaceAt(zone); return; }
            }
            foreach (var h in hits)
            {
                if (h.collider.isTrigger) continue;
                Drop(h.point);
                return;
            }
            return;
        }

        foreach (var h in hits)
        {
            var click = h.collider.GetComponentInParent<IWorldClickable>();
            if (click != null) { click.OnPCClick(); return; }

            var item = h.collider.GetComponentInParent<TaskItem>();
            if (item != null) { PickUp(item); return; }

            var zone = h.collider.GetComponentInParent<TaskInteractionZone>();
            if (zone != null) { InteractionEvents.FireZoneActivated(zone.zoneId, string.Empty); return; }

            if (!h.collider.isTrigger) return;   // 벽·바닥 등에 막힘
        }
    }

    private void PickUp(TaskItem item)
    {
        var sync = item.GetComponentInParent<SyncGrab>();
        if (sync != null) { sync.OnPCClick(); InteractionEvents.FireItemGrabbed(item.prefabId); return; }

        _held = item;
        _heldParent = item.transform.parent;
        _heldCols = item.GetComponentsInChildren<Collider>();
        foreach (var c in _heldCols) c.enabled = false;
        _heldRb = item.GetComponent<Rigidbody>();
        if (_heldRb != null) _heldRb.isKinematic = true;

        item.transform.SetParent(cam.transform, true);
        item.transform.localPosition = holdOffset;
        InteractionEvents.FireItemGrabbed(item.prefabId);
    }

    private void PlaceAt(TaskInteractionZone zone)
    {
        var b = zone.GetComponent<Collider>().bounds;
        var item = _held;
        Release(new Vector3(b.center.x, b.min.y, b.center.z));
        InteractionEvents.FireZoneActivated(zone.zoneId, item.prefabId);
    }

    private void Drop(Vector3 point) => Release(point);

    private void Release(Vector3 groundPoint)
    {
        var t = _held.transform;
        t.SetParent(_heldParent, true);
        t.rotation = Quaternion.Euler(0f, t.eulerAngles.y, 0f);
        // 바닥면에 아이템 밑면이 닿게
        float halfH = 0f;
        var r = t.GetComponentInChildren<Renderer>();
        if (r != null) halfH = t.position.y - r.bounds.min.y;
        t.position = groundPoint + Vector3.up * (halfH + 0.005f);

        foreach (var c in _heldCols) if (c != null) c.enabled = true;
        _held = null; _heldCols = null; _heldRb = null;
    }

    // ── 입력 (Input System / 기존 Input 둘 다) ─────────────────────────────
#if ENABLE_INPUT_SYSTEM
    private static bool KeyHeld(char k)
    {
        var kb = Keyboard.current; if (kb == null) return false;
        switch (k)
        {
            case 'W': return kb.wKey.isPressed; case 'S': return kb.sKey.isPressed;
            case 'A': return kb.aKey.isPressed; case 'D': return kb.dKey.isPressed;
            case 'Q': return kb.qKey.isPressed; case 'E': return kb.eKey.isPressed;
        }
        return false;
    }
    private static bool UseDown() => Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
    private static bool LeftDown() => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    private static bool RightHeld() => Mouse.current != null && Mouse.current.rightButton.isPressed;
    private static Vector2 MouseDelta() => Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
    private static Vector2 MousePos() => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
    private static bool KeyHeld(char k) => Input.GetKey((KeyCode)char.ToLower(k));
    private static bool UseDown() => Input.GetKeyDown(KeyCode.F);
    private static bool LeftDown() => Input.GetMouseButtonDown(0);
    private static bool RightHeld() => Input.GetMouseButton(1);
    private static Vector2 MouseDelta() => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
    private static Vector2 MousePos() => Input.mousePosition;
#endif
}
