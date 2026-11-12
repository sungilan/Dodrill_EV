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
public class FloorDragProp : MonoBehaviour
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
    }

    private void Start()
    {
        _floorY = transform.position.y;
        _floorInit = true;
        _lastPos = transform.position;
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
        if (!held || _rb.isKinematic) return;

        // 높이·기울기 보정
        Vector3 p = _rb.position;
        if (Mathf.Abs(p.y - _floorY) > 0.001f) { p.y = _floorY; _rb.position = p; }
        Vector3 fwd = _rb.rotation * Vector3.forward; fwd.y = 0f;
        Quaternion upright = fwd.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fwd, Vector3.up) : _rb.rotation;
        if (Quaternion.Angle(_rb.rotation, upright) > 0.5f) _rb.MoveRotation(upright);

        // 무게감: 수평 속도 제한
        Vector3 v = _rb.linearVelocity; v.y = 0f;
        if (v.magnitude > maxSpeed) v = v.normalized * maxSpeed;
        _rb.linearVelocity = v;
        _rb.angularVelocity = new Vector3(0f, Mathf.Clamp(_rb.angularVelocity.y, -2f, 2f), 0f);
    }

    private void LateUpdate()
    {
        if (!_floorInit) return;

        if (LocalHeld)
        {
            // PC 들기·키네마틱으로 끌려온 경우에도 바닥 높이·수직 자세 유지, 속도 제한
            Vector3 p = transform.position;
            Vector3 target = new Vector3(p.x, _floorY, p.z);
            Vector3 flat = target - new Vector3(_lastPos.x, _floorY, _lastPos.z);
            float maxStep = maxSpeed * Time.deltaTime;
            if (flat.magnitude > maxStep) target = new Vector3(_lastPos.x, _floorY, _lastPos.z) + flat.normalized * maxStep;
            Vector3 fwd = transform.forward; fwd.y = 0f;
            Quaternion rot = fwd.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fwd, Vector3.up) : transform.rotation;
            transform.SetPositionAndRotation(target, rot);
        }
        else if (_sync == null || !_sync.IsGrabbed)
        {
            // 놓여 있을 때의 높이를 바닥으로 기억(서버가 떨어뜨려 바닥에 닿은 높이)
            if (_rb == null || _rb.isKinematic) _floorY = transform.position.y;
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
