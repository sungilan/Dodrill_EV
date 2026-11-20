using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// ============================================================
//  SimplePcInteractor.cs — 최소 PC 조작부 (모듈화, 2026-11-20)
//
//  콘텐츠 조작부(EV 의 FreeLookController) 없이 시나리오 단계를 진행할 수 있는 기본 1인칭 조작.
//    · WASD 이동, Q/E 아래/위(flyVertical 켠 경우), 마우스 오른쪽 버튼을 누른 채 끌어 시점 회전
//    · 왼쪽 클릭
//        - 빈손: 클릭 대상(IWorldClickable: TaskClickTarget 등) 누르기 / 작업 아이템(TaskItem) 집기 / 작업 구역 터치
//        - 들고 있을 때: 작업 구역(TaskInteractionZone)을 누르면 그 자리에 놓기, 그 외 바닥 등에 내려놓기
//    · F: 들고 있는 아이템 사용(UseItem 단계)
//  신호는 InteractionEvents 로 내고, NetworkInteractionBridge 가 서버로 보낸다.
//
//  두 가지 배치
//    ① 카메라에 직접 붙임(예제 Example_Workbench): 카메라가 곧 몸. pitchPivot 비움.
//    ② 몸통 루트에 붙임(범용 플레이어 프리팹 GenericPlayer): 루트는 좌우(yaw)만, pitchPivot(카메라 받침)이 위아래(pitch)를 맡는다.
//  아이템 종류
//    · 씬에 놓인 일반 TaskItem: 이 컴포넌트가 직접 카메라 앞에 든다(1인 시험용).
//    · 네트워크 아이템(SyncGrab): SyncGrab 의 PC 잡기를 쓴다(서버 권한 소유·동기화). 내 HoldPoint(태그 "HoldPoint")로 날아온다.
//      들고 있을 때 클릭 → 구역을 보고 있으면 그 자리에 얹어 놓고, 아니면 그 자리에 놓는다.
// ============================================================
public class SimplePcInteractor : MonoBehaviour, IPcLookController
{
    // ── IPcLookController: PcMobileAvatarController 가 읽어 아바타 걷기 애니메이션·IK·머리 방향을 구동한다 ──
    private Vector3 _planarVelocity;
    private float _crouchInput;
    public Vector3 CurrentPlanarVelocity => _planarVelocity;
    public float CurrentPitch => _pitch;
    public float CrouchInput => _crouchInput;
    public float MoveSpeed => moveSpeed;

    public Camera cam;
    public float moveSpeed = 2.5f;
    public float lookSpeed = 0.15f;
    public float reach = 5f;
    [Tooltip("들고 있을 때 카메라 기준 위치(일반 TaskItem 만. SyncGrab 은 HoldPoint 로 간다)")]
    public Vector3 holdOffset = new Vector3(0.25f, -0.2f, 0.6f);

    [Tooltip("비우면 이 오브젝트(카메라)가 이동·회전한다. 지정하면 이 오브젝트는 좌우(yaw)로만 돌고 이 피벗이 위아래(pitch)를 맡는다.")]
    public Transform pitchPivot;
    [Tooltip("Q/E 로 위아래 이동(자유 비행). 끄면 바닥 높이를 유지한 채 걷는다.")]
    public bool flyVertical = true;

    private TaskItem _held;
    private SyncGrab _heldSync;
    private Transform _heldParent;
    private Collider[] _heldCols;
    private Rigidbody _heldRb;
    private float _yaw, _pitch;

    public TaskItem Held => _held;
    public SyncGrab HeldSync => _heldSync;

    private void Awake()
    {
        if (cam == null) cam = GetComponent<Camera>() ?? GetComponentInChildren<Camera>(true) ?? Camera.main;
        _yaw = transform.eulerAngles.y;
        var pe = (pitchPivot != null ? pitchPivot.localEulerAngles : transform.eulerAngles).x;
        _pitch = pe > 180f ? pe - 360f : pe;
    }

    private void Update()
    {
        if (_heldSync != null && !_heldSync.LocalPcHoldActive) _heldSync = null;   // 서버가 잡힘을 풀었거나 거절됨

        Move();
        if (LeftDown()) Click();
        if (UseDown())
        {
            string id = _held != null ? _held.prefabId : (_heldSync != null ? _heldSync.GetComponent<TaskItem>()?.prefabId : null);
            if (!string.IsNullOrEmpty(id)) InteractionEvents.FireItemUsed(id);
        }
    }

    // ── 이동·시점 ─────────────────────────────
    private void Move()
    {
        Vector3 dir = Vector3.zero;
        if (KeyHeld('W')) dir += Vector3.forward;
        if (KeyHeld('S')) dir += Vector3.back;
        if (KeyHeld('A')) dir += Vector3.left;
        if (KeyHeld('D')) dir += Vector3.right;
        if (flyVertical)
        {
            if (KeyHeld('Q')) dir += Vector3.down;
            if (KeyHeld('E')) dir += Vector3.up;
        }
        transform.position += transform.TransformDirection(dir.normalized) * moveSpeed * Time.deltaTime;

        // 아바타 구동용 값: 수평 속도(월드), 웅크리기(왼쪽 Ctrl 을 누르면 낮추고, 떼면 다시 일어선다)
        Vector3 planar = transform.TransformDirection(new Vector3(dir.x, 0f, dir.z).normalized) * moveSpeed;
        _planarVelocity = Vector3.Lerp(_planarVelocity, planar, 1f - Mathf.Exp(-12f * Time.deltaTime));
        var avatar = GetComponent<PcMobileAvatarController>();
        _crouchInput = CrouchHeld() ? 1f : (avatar != null && avatar.Crouch01 > 0f ? -1f : 0f);

        if (RightHeld())
        {
            Vector2 d = MouseDelta();
            _yaw += d.x * lookSpeed;
            _pitch = Mathf.Clamp(_pitch - d.y * lookSpeed, -85f, 85f);
            if (pitchPivot != null)
            {
                transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
                pitchPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }
            else transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }
    }

    // ── 클릭 ─────────────────────────────
    private void Click()
    {
        if (cam == null) return;
        Ray ray = cam.ScreenPointToRay(MousePos());
        var hits = Physics.RaycastAll(ray, reach, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        if (_heldSync != null) { ClickHoldingSync(hits); return; }

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
            if (IsOwnBody(h.collider)) continue;

            var click = h.collider.GetComponentInParent<IWorldClickable>();
            if (click != null) { click.OnPCClick(); return; }

            var item = h.collider.GetComponentInParent<TaskItem>();
            if (item != null) { PickUp(item); return; }

            var zone = h.collider.GetComponentInParent<TaskInteractionZone>();
            if (zone != null) { InteractionEvents.FireZoneActivated(zone.zoneId, string.Empty); return; }

            if (!h.collider.isTrigger) return;   // 벽·바닥 등에 막힘
        }
    }

    /// <summary>내 몸통(범용 플레이어 프리팹)의 콜라이더는 클릭 대상이 아니다.</summary>
    private bool IsOwnBody(Collider c) => pitchPivot != null && c.transform.IsChildOf(transform);

    /// <summary>SyncGrab 아이템을 든 채 클릭: 보고 있는 작업 구역에 얹어 놓거나, 없으면 그 자리에 놓는다(서버가 낙하·고정 처리).</summary>
    private void ClickHoldingSync(RaycastHit[] hits)
    {
        var item = _heldSync;
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(item.transform) || IsOwnBody(h.collider)) continue;
            var zone = h.collider.GetComponentInParent<TaskInteractionZone>();
            if (zone != null && h.collider.enabled)
            {
                var b = h.collider.bounds;
                var t = item.transform;
                float halfH = 0f;
                var r = t.GetComponentInChildren<Renderer>();
                if (r != null) halfH = t.position.y - r.bounds.min.y;
                // 구역 위로 위치를 옮긴 뒤 놓는다 — 서버가 그 자리에서 낙하·고정하면서 구역 트리거를 지난다
                t.SetPositionAndRotation(new Vector3(b.center.x, b.min.y + halfH + 0.005f, b.center.z),
                                         Quaternion.Euler(0f, t.eulerAngles.y, 0f));
                break;
            }
            if (!h.collider.isTrigger) break;   // 바닥·벽에 막힘 — 들고 있던 자리에서 놓는다
        }
        item.RequestRelease();
        _heldSync = null;
    }

    private void PickUp(TaskItem item)
    {
        var sync = item.GetComponentInParent<SyncGrab>();
        if (sync != null)
        {
            if (sync.IsGrabbed) return;        // 다른 사람이 들고 있음
            sync.OnPCClick();                   // 서버에 잡기 요청 → 승인되면 HoldPoint 로 날아오고, 도착하면 SyncGrab 이 '집음' 신호를 낸다
            if (sync.LocalPcHoldActive) _heldSync = sync;
            return;
        }

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
    private static bool CrouchHeld() => Keyboard.current != null && Keyboard.current.leftCtrlKey.isPressed;
    private static bool UseDown() => Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
    private static bool LeftDown() => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    private static bool RightHeld() => Mouse.current != null && Mouse.current.rightButton.isPressed;
    private static Vector2 MouseDelta() => Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
    private static Vector2 MousePos() => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
    private static bool KeyHeld(char k) => Input.GetKey((KeyCode)char.ToLower(k));
    private static bool CrouchHeld() => Input.GetKey(KeyCode.LeftControl);
    private static bool UseDown() => Input.GetKeyDown(KeyCode.F);
    private static bool LeftDown() => Input.GetMouseButtonDown(0);
    private static bool RightHeld() => Input.GetMouseButton(1);
    private static Vector2 MouseDelta() => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
    private static Vector2 MousePos() => Input.mousePosition;
#endif
}
