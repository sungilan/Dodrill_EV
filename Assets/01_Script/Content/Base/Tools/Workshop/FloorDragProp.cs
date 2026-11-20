using Autohand;
using UnityEngine;

// ============================================================
//  FloorDragProp.cs
//
//  바닥에 붙어 끌려 다니는 장비 — 오일 받이(폐유 드레이너), 배터리 리프트(이동식 잭) 등.
//  잡고 손을 움직이면 바닥 높이는 그대로, 수평으로만 따라오고 기울지 않는다(세로축 회전만 허용).
//  · VR: 잡혀 있는 동안 Rigidbody 의 높이·기울기 회전을 잠근다 → AutoHand 가 손 쪽으로 끌어당겨도 바닥을 미끄러지듯 움직임
//  · PC: 들기 위치(화면 앞)를 따라가되 높이는 바닥에 고정
//  · 무거운 느낌: 손을 빨리 휘둘러도 최대 속도 이상으로 따라오지 않는다
//  catchLiquid 를 켜면 위에서 붓는 오일(PourContainer)을 받아 '받은 양'이 늘어난다(오일 드레이너).
// ============================================================
[DefaultExecutionOrder(1000)]   // SyncGrab 이 들기 위치로 옮긴 뒤에 높이를 다시 바닥으로
public class FloorDragProp : MonoBehaviour, IHoldScaleExempt, IFloorCarried
{
    [Tooltip("끌 때 최대 속도(m/s) — 무거운 장비일수록 낮게")] public float maxSpeed = 1.6f;
    [Tooltip("위에서 붓는 액체를 받는다(오일 드레이너)")] public bool catchLiquid;
    [Tooltip("받을 수 있는 최대 양(L)")] public float capacity = 20f;

    private SyncGrab _sync;
    private Rigidbody _rb;
    private float _floorY;
    private bool _floorInit;
    private RigidbodyConstraints _origConstraints;
    private bool _locked;
    private Vector3 _lastPos;
    private float _nextFloorCheck;

    private float _caught;
    private float _caughtShownAt = -10f;
    private GameObject _label;
    private WorldHoverLabel _labelComp;

    /// <summary>받은 액체 양(L) — 이 클라이언트에서 계산.</summary>
    public float Caught => _caught;

    private void Awake()
    {
        _sync = GetComponent<SyncGrab>();
        _rb = GetComponent<Rigidbody>();
        if (_rb != null) _origConstraints = _rb.constraints;
        if (_sync != null)
        {
            // PC·모바일: 예전엔 다른 도구처럼 화면 앞 들기 위치(HoldPoint)로 날아가 '맨 앞에 그리기'로 그려졌는데,
            // 높이는 바닥에 묶여 있어 화면에선 작아지며 바닥 밑에 박힌 것처럼 보였고, 들기 위치의 카메라 기울기가 그대로 남아 놓으면 기울어 있었다.
            // 이제 내 앞 바닥(카메라 앞 일정 거리)을 따라오고, 세로축 회전만 따른다.
            _sync.drawOnTopWhileHeld = false;
            _sync.PcHoldOverride = PcHoldPose;
            // 잡을 때 손 앞 바닥으로 빠르게 붙고(예전 0.5초 → 0.15초), 이후엔 내 화면에서 매 프레임 바로 붙인다(네트워크 보간 지연 없음)
            _sync.flyDuration = 0.15f;
            _sync.snapHoldToTarget = true;
        }
    }

    private void Start()
    {
        EquipFx.LocalBounds(transform, out var lb);
        _bottomOffset = transform.position.y - transform.TransformPoint(lb.center - Vector3.up * lb.extents.y).y;
        _floorY = GroundY(transform.position.y);
        _floorInit = true;
        _lastPos = transform.position;
        if (catchLiquid) LiquidGauge.Attach(gameObject, "받은 오일", capacity, () => _caught, () => LocalHeld);   // 현재 용량 패널(비었음/꽉 참)
    }

    private float _bottomOffset;
    private bool _pcHoldStarted;
    private float _pcDist, _pcYawOffset;

    /// <summary>바로 아래 바닥 높이 + 바닥에서 피벗까지 높이. 못 찾으면 fallback.</summary>
    private float GroundY(float fallback)
    {
        Vector3 from = transform.position + Vector3.up * 0.5f;
        var hits = Physics.RaycastAll(from, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (var h in hits)
        {
            if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
            if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic) continue;   // 굴러다니는 물건 위는 바닥 아님
            if (h.point.y > transform.position.y + 0.3f) continue;
            if (h.point.y > best) best = h.point.y;
        }
        return float.IsNegativeInfinity(best) ? fallback : best + _bottomOffset;
    }

    /// <summary>PC·모바일 들기 목표: 카메라 앞 바닥, 잡을 때의 거리·방향 유지.</summary>
    private Pose PcHoldPose(Vector3 holdPos, Quaternion holdRot)
    {
        var cam = Camera.main;
        if (cam == null) return new Pose(new Vector3(holdPos.x, _floorY, holdPos.z), Quaternion.Euler(0f, holdRot.eulerAngles.y, 0f));
        Vector3 c = cam.transform.position;
        Vector3 f = cam.transform.forward; f.y = 0f;
        if (f.sqrMagnitude < 1e-4f) f = cam.transform.up; f.y = 0f;
        f.Normalize();
        float camYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        if (!_pcHoldStarted)
        {
            _pcHoldStarted = true;
            Vector3 flat = transform.position - c; flat.y = 0f;
            _pcDist = Mathf.Clamp(flat.magnitude, 0.9f, 1.5f);
            _pcYawOffset = Mathf.DeltaAngle(camYaw, transform.eulerAngles.y);
        }
        Vector3 p = c + f * _pcDist;
        p.y = _floorY;
        return new Pose(p, Quaternion.Euler(0f, camYaw + _pcYawOffset, 0f));
    }

    private bool LocalHeld => _sync != null && _sync.IsOwner && _sync.IsGrabbed;

    private void FixedUpdate()
    {
        if (_rb == null) return;
        bool held = LocalHeld;
        if (held != _locked)
        {
            _locked = held;
            _rb.constraints = held
                ? (_origConstraints | RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ)
                : _origConstraints;
        }
        if (!held) _vrHeld = false;

        // VR: 잡은 손(컨트롤러)을 직접 따라가는 키네마틱 끌기(11/16) — 아래 설명
        if (VrKinematicDrag(held)) return;

        if (!held || _rb.isKinematic) return;

        // 높이·기울기 보정
        Vector3 p = _rb.position;
        if (Mathf.Abs(p.y - _floorY) > 0.001f) { p.y = _floorY; _rb.position = p; }
        Vector3 fwd = _rb.rotation * Vector3.forward; fwd.y = 0f;
        Quaternion upright = fwd.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fwd, Vector3.up) : _rb.rotation;
        if (Quaternion.Angle(_rb.rotation, upright) > 0.5f) _rb.MoveRotation(upright);

        // VR: AutoHand 손 조인트에만 맡기면 바닥 고정(높이 잠금)과 손 당김이 서로 싸워 장비가 떨리고 튀었다(사다리 미끄러지며 날아감).
        // 잡은 손의 잡은 지점이 실제 컨트롤러 위치로 가도록 수평 속도를 직접 준다.
        Vector3 v = _rb.linearVelocity; v.y = 0f;
        if (VrDriveVelocity(out var drive)) v = drive;
        float cap = Mathf.Max(maxSpeed, 3f);   // VR: 걷는 속도로 끌어도 손보다 늦지 않게(손을 휘두르는 것만 막는다)
        if (v.magnitude > cap) v = v.normalized * cap;
        _rb.linearVelocity = v;
        _rb.angularVelocity = new Vector3(0f, Mathf.Clamp(_rb.angularVelocity.y, -2f, 2f), 0f);
    }

    private bool _vrHeld;

    // ── VR 키네마틱 끌기 ─────────────────────────────
    // 11/16 VR 테스트: 리프트·사다리를 잡으면 손(팔)이 늘어나고, 잘 안 잡히고, 잡거나 놓을 때 날아갔다.
    // 동적 Rigidbody 에 속도를 주는 방식은 AutoHand 손 조인트·플레이어 몸 충돌·놓을 때 던지기 속도와 계속 싸웠다.
    // 이제 VR 로 잡고 있는 동안은 키네마틱으로 바꾸고, 잡은 순간의 '컨트롤러 → 장비' 수평 오프셋을 유지하며 직접 옮긴다.
    //  · 키네마틱이라 손·몸에 밀려 튀지 않고, 놓을 때 던지기 속도도 받지 않는다(AutoHand 는 키네마틱이면 속도를 안 줌)
    //  · 손목을 돌리면 장비가 손잡이를 축으로 천천히 돈다(세로축만, 부드럽게)
    //  · 놓으면 키네마틱 그대로 그 자리에 선다(서버 낙하 처리는 SyncGrab 이 맡음)
    private bool _kinDrag;
    private Hand _kinHand;
    private Vector3 _kinOffset;      // 컨트롤러 기준(수평 회전만) 장비 위치
    private float _kinYawOffset;     // 장비 yaw - 컨트롤러 yaw
    private float _kinYaw;

    private static float FlatYaw(Quaternion q)
    {
        Vector3 f = q * Vector3.forward; f.y = 0f;
        if (f.sqrMagnitude < 1e-4f) { f = q * Vector3.up; f.y = 0f; }
        return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }

    private Hand HoldingHand()
    {
        foreach (var h in LocalHands.All)
        {
            if (h == null || h.holdingObj == null || h.moveTo == null) continue;
            if (h.holdingObj.transform == transform || h.holdingObj.transform.IsChildOf(transform)) return h;
        }
        return null;
    }

    private bool VrKinematicDrag(bool held)
    {
        Hand h = held ? HoldingHand() : null;
        if (h == null)
        {
            if (_kinDrag)
            {
                _kinDrag = false;
                _kinHand = null;
                // 놓았거나 손에서 빠짐 → 그 자리에 멈춰 선다
                if (!_rb.isKinematic) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
                _rb.isKinematic = true;
            }
            return false;
        }

        _vrHeld = true;
        Transform c = h.moveTo;
        float cYaw = FlatYaw(c.rotation);
        if (!_kinDrag || _kinHand != h)
        {
            _kinDrag = true;
            _kinHand = h;
            if (!_rb.isKinematic) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
            _rb.isKinematic = true;
            Vector3 d = _rb.position - c.position; d.y = 0f;
            _kinOffset = Quaternion.Euler(0f, -cYaw, 0f) * d;
            _kinYaw = FlatYaw(_rb.rotation);
            _kinYawOffset = Mathf.DeltaAngle(cYaw, _kinYaw);
        }
        if (!_rb.isKinematic) _rb.isKinematic = true;   // SyncGrab 이 잡힘 처리 중 다시 풀어도 유지

        // 세로축 회전: 손목 돌림을 부드럽게(지터가 긴 장비 끝에서 커지지 않게)
        float wantYaw = cYaw + _kinYawOffset;
        _kinYaw = Mathf.LerpAngle(_kinYaw, wantYaw, 1f - Mathf.Exp(-6f * Time.fixedDeltaTime));
        float yawDelta = Mathf.DeltaAngle(cYaw + _kinYawOffset, _kinYaw);   // 회전이 늦게 따라오는 만큼 위치도 맞춘다
        Vector3 target = c.position + Quaternion.Euler(0f, cYaw + yawDelta, 0f) * _kinOffset;
        target.y = _floorY;

        // 무거운 느낌: 한 스텝 이동 거리 제한(걷는 속도보다는 빠르게)
        Vector3 cur = _rb.position;
        Vector3 step = target - cur; step.y = 0f;
        float maxStep = Mathf.Max(maxSpeed, 3f) * Time.fixedDeltaTime;
        if (step.magnitude > maxStep) step = step.normalized * maxStep;
        _rb.MovePosition(new Vector3(cur.x + step.x, _floorY, cur.z + step.z));
        _rb.MoveRotation(Quaternion.Euler(0f, _kinYaw, 0f) * Quaternion.Inverse(Quaternion.Euler(0f, FlatYaw(_rb.rotation), 0f)) * _rb.rotation);
        return true;
    }

    /// <summary>VR 로 잡고 있으면 잡은 지점→컨트롤러 수평 차이를 메우는 속도.</summary>
    private bool VrDriveVelocity(out Vector3 v)
    {
        v = Vector3.zero;
        int n = 0;
        foreach (var h in LocalHands.All)
        {
            if (h == null || h.holdingObj == null || h.moveTo == null) continue;
            if (h.holdingObj.transform != transform && !h.holdingObj.transform.IsChildOf(transform)) continue;
            Vector3 grip = h.handGrabPoint != null ? h.handGrabPoint.position : h.transform.position;
            Vector3 d = h.moveTo.position - grip; d.y = 0f;
            v += d; n++;
        }
        _vrHeld = n > 0;
        if (n == 0) return false;
        v = v / n / Mathf.Max(0.005f, Time.fixedDeltaTime) * 0.5f;   // 반 프레임씩 따라잡아 튀지 않게
        return true;
    }

    [Tooltip("VR: 잡은 손과 실제 컨트롤러가 이만큼 벌어지면 놓는다(m) — 손이 장비에 붙은 채 팔만 늘어나 보이지 않게")]
    public float vrReleaseDistance = 0.3f;

    private void Update()
    {
        if (!LocalHeld) return;
        // 바닥 높이·속도 제한 때문에 장비가 손을 못 따라오면 물리 손이 손잡이에 붙은 채 남고, 아바타 팔이 그 손까지 늘어났다.
        // 실제 손(컨트롤러)이 손잡이에서 일정 거리 이상 멀어지면 놓아 준다.
        foreach (var h in LocalHands.All)
        {
            if (h == null || h.holdingObj == null || h.moveTo == null) continue;
            if (h.holdingObj.transform != transform && !h.holdingObj.transform.IsChildOf(transform)) continue;
            Vector3 grip = h.handGrabPoint != null ? h.handGrabPoint.position : h.transform.position;
            // 장비는 수평으로 손을 따라오므로 벌어지는 건 주로 높이(허리를 펴거나 손을 들 때) — 높이는 조금 더 봐준다
            Vector3 gap = grip - h.moveTo.position;
            float horiz = new Vector2(gap.x, gap.z).magnitude;
            if (horiz > vrReleaseDistance || Mathf.Abs(gap.y) > vrReleaseDistance * 1.6f)
            {
                h.ForceReleaseGrab();
                EquipFx.Haptic(h, 0.08f, 0.5f);
            }
        }
    }

    private void LateUpdate()
    {
        if (!_floorInit) return;

        if (LocalHeld && !_vrHeld)   // VR 은 물리(FixedUpdate)로만 움직인다 — 여기서 transform 을 옮기면 손 조인트와 싸워 떨렸다
        {
            // PC 들기·키네마틱으로 끌려온 경우에도 바닥 높이·수직 자세 유지.
            // 예전엔 여기서 속도를 1.6 m/s 로 묶어 걸어가면 장비가 손(들기 위치)보다 늦게 따라왔다 → 이제 내 이동 속도 그대로 따라온다
            Vector3 p = transform.position;
            Vector3 target = new Vector3(p.x, _floorY, p.z);
            Vector3 fwd = transform.forward; fwd.y = 0f;
            Quaternion rot = fwd.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fwd, Vector3.up) : transform.rotation;
            transform.SetPositionAndRotation(target, rot);
        }
        else if (_sync == null || !_sync.IsGrabbed)
        {
            _pcHoldStarted = false;
            // 놓여 멈춰 있을 때 바로 아래 바닥 높이를 다시 잰다(1초마다) — 예전엔 '지금 높이'를 바닥으로 기억해,
            // 들어 올리는 도중 값이 저장되면 다음에 잡을 때 그 높이(또는 바닥 밑)에 붙었다
            bool resting = _rb == null || _rb.isKinematic || _rb.linearVelocity.sqrMagnitude < 0.0025f;
            if (resting && Time.time >= _nextFloorCheck)
            {
                _nextFloorCheck = Time.time + 1f;
                _floorY = GroundY(_floorY);
            }
        }
        _lastPos = transform.position;

        UpdateLabel();
    }

    // ── 액체 받기 ─────────────────────────────

    /// <summary>PourContainer 가 부른다. 받았으면 true.</summary>
    public bool AddLiquid(float liters)
    {
        if (!catchLiquid) return false;
        _caught = Mathf.Min(capacity, _caught + liters);
        _caughtShownAt = Time.time;
        return true;
    }

    private void UpdateLabel()
    {
        if (!catchLiquid) return;
        if (GetComponent<LiquidGauge>() != null) return;   // 용량 패널(LiquidGauge)이 대신 보여 준다
        bool show = Time.time - _caughtShownAt < 3f;
        if (!show) { if (_label != null && _label.activeSelf) _label.SetActive(false); return; }
        if (_label == null)
        {
            var prefab = Resources.Load<GameObject>("ItemNameTagLabel");
            if (prefab == null) return;
            _label = Instantiate(prefab);
            _label.name = "[DrainerLabel]";
            foreach (var t in _label.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 1;
            _labelComp = _label.GetComponent<WorldHoverLabel>();
        }
        if (!_label.activeSelf) _label.SetActive(true);
        EquipFx.LocalBounds(transform, out var lb);
        _label.transform.position = transform.TransformPoint(lb.center) + Vector3.up * (lb.extents.y + 0.18f);
        if (_labelComp != null) _labelComp.SetText($"받은 오일 {_caught:0.0} L", _caught >= capacity ? "가득 참 — 비우세요" : "오일 받는 중");
    }

    private void OnDestroy()
    {
        if (_label != null) Destroy(_label);
    }
}
