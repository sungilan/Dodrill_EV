using System.Collections.Generic;
using Autohand;
using UnityEngine;

// ============================================================
//  VrProbeHandling.cs
//
//  VR 전용 — 멀티미터·절연저항계(메거) 프로브를 손으로 꺼내 단자에 대는 측정.
//  예전에는 본체만 들고 단자 근처에 갖다 대면 바로 측정돼(케이블 쪽 큰 트리거) 실습 의미가 없었다.
//
//  사용 흐름 (실제 측정 순서를 따르되 조작은 단순하게)
//    1. 한 손으로 본체를 든다. (내려놓고 프로브만 들어도 된다)
//    2. 빈 손으로 본체를 쥐면 다음에 쓸 프로브가 손에 들린다.
//       근처에 접지 단자가 있으면 검정 집게 → 빨간 프로브 순서. 늘어진 프로브 끝을 직접 쥐어도 된다.
//    3. 프로브 끝이 단자 3.5cm 안에 들어오면 단자 중심에 달라붙고(자석 보정) 링이 차오르며 짧게 진동한다.
//    4. 검정: 0.3초 유지하면 접지에 물리고, 손을 놓아도 그대로 남는다.
//       빨강: 0.5초 유지하면 측정값이 고정(HOLD)되어 단자 옆에 표시되고, 서버로 측정 신호가 간다.
//    5. 손을 놓으면 빨간 프로브는 본체로 돌아간다.
//
//  프로브는 케이블 스킨 메시 끝에 붙어 있어 따로 쥘 메시가 없다. 예전에는 케이블 뼈대를 손 쪽으로 휘었는데
//  기기에서 손에 아무것도 보이지 않았다(2026-11-12 테스트). 이제는 꺼내는 순간 그 색 케이블 메시를 숨기고,
//  손(또는 접지 단자)에 프로브 펜 모양을 붙인 뒤 본체 단자에서 펜 꼬리까지 선 케이블로 잇는다. 놓으면 원래 메시로 돌아간다.
//  측정값은 단자 옆에 띄운다(본체는 다른 손에 들려 있어 LCD 를 보면서 단자를 볼 수 없다).
//
//  · VR 기기에서만 동작한다(PC·모바일은 기존 조준 측정 그대로).
//  · 프로브 위치는 내 화면에서만 움직인다(다른 사람 화면의 케이블은 늘어진 그대로). 측정 결과는 기존처럼 서버로 간다.
//  · MultimeterMaster.Awake 에서 자동으로 붙는다. 씬 배치 불필요.
// ============================================================
[DefaultExecutionOrder(200)]   // 손(AutoHand) 이 움직인 뒤에 케이블을 배치한다
public class VrProbeHandling : MonoBehaviour
{
    // ── 조절값 ──
    private const float TakeFromBodyRadius = 0.3f;    // 빈 손으로 본체 이만큼 안을 쥐면 프로브를 꺼낸다 (0.18 은 실제 기기에서 0.19~0.29m 로 자꾸 빗나가 단계 시간이 초과됐다)
    private const float TakeFromTipRadius = 0.09f;    // 늘어진 프로브 끝을 직접 쥘 때
    private const float SnapRadius = 0.035f;          // 단자 자석 보정 거리
    private const float RedDwell = 0.5f;              // 빨강: 이만큼 대고 있으면 측정
    private const float BlackDwell = 0.3f;            // 검정: 이만큼 대고 있으면 물림
    private const float GroundNeededWithin = 1.5f;    // 측정 단자에서 이 거리 안에 접지 단자가 있을 때만 검정 집게를 요구
    private const float ReturnTime = 0.25f;
    private const float PenReach = 0.05f;             // 주먹(손가락 마디) 앞으로 프로브 끝이 나오는 길이

    private enum State { Docked, Held, Clipped, Returning }

    private class Probe
    {
        public bool red;
        public MultimeterProbe probe;
        public readonly List<Transform> bones = new List<Transform>();
        public Transform root;
        public Vector3[] restPos;
        public Quaternion[] restRot;
        public Vector3[] axis;            // 각 뼈의 '다음 뼈 방향' (로컬)
        public float length;
        public Vector3 restTipLocal;      // 본체 기준 쉬는 위치의 끝
        public Vector3 restDirLocal;

        public State state;
        public Hand hand;
        public bool bySqueeze;            // 트리거로 꺼냈으면 트리거를 놓을 때 돌려놓는다(그립으로 꺼냈으면 그립을 놓을 때)
        public bool posed;
        public SkinnedMeshRenderer cable;  // 본체에 늘어진 케이블+프로브 메시(꺼내면 숨김)
        public GameObject pen;             // 손에 쥔 프로브 펜
        public LineRenderer cord;          // 본체 → 펜 꼬리 케이블

        public MeasurementPoint snap;
        public float snapStart;
        public bool done;
        public MeasurementPoint clip;

        public float returnT;
        public Vector3 returnFrom, returnFromDir;
        public Vector3 lastTip, lastDir;
    }

    private MultimeterMaster _master;
    private Grabbable _bodyGrab;
    private Probe _red, _black;
    private readonly List<Hand> _hands = new List<Hand>();
    private float _nextHandScan;
    private bool _bodyLockedByUs;

    // 표시물
    private LineRenderer _ring;
    private Material _ringMat;
    private GameObject _guide;
    private WorldHoverLabel _guideLabel;
    private string _guideText;
    private GameObject _valueLabel;
    private float _valueLabelUntil;
    private MeasurementPoint _valueAt;
    private string _valueText;

    private static GameObject s_labelPrefab;

    public static bool IsVRDevice
    {
        get
        {
#if ENABLE_VR || UNITY_XR_MANAGEMENT
            if (UnityEngine.XR.XRSettings.isDeviceActive) return true;
            var d = UnityEngine.XR.XRSettings.loadedDeviceName;
            if (!string.IsNullOrEmpty(d) && d != "None") return true;
#endif
            return false;
        }
    }

    private void Start()
    {
        _master = GetComponent<MultimeterMaster>();
        _bodyGrab = GetComponent<Grabbable>();
        if (!IsVRDevice || _master == null) { enabled = false; return; }

        _red = BuildProbe(_master.redProbe, true);
        _black = BuildProbe(_master.blackProbe, false);
        if (_red == null) { Debug.LogWarning($"[VrProbe] {name}: 빨간 프로브 케이블 뼈대를 찾지 못해 VR 프로브 조작을 끕니다."); enabled = false; return; }

        // 메거 프리팹은 본체 Grabbable·SyncGrab 이 꺼져 있고 빨간 케이블만 잡게 되어 있다(0.6.6 부터, PC 흐름용).
        // VR 에서는 아래에서 케이블 잡기를 끄므로 그대로 두면 잡을 곳이 하나도 없어 '메거가 안 잡힘'(2026-11-12 테스트) → 본체를 잡게 켠다.
        if (_bodyGrab != null && !_bodyGrab.enabled)
        {
            _bodyGrab.enabled = true;
            var sg = GetComponent<SyncGrab>();
            if (sg != null && !sg.enabled) sg.enabled = true;
            Debug.Log($"[VrProbe] {name}: 본체 잡기 켬 (VR)");
        }

        // 예전 방식(케이블 오브젝트의 큰 트리거로 본체만 갖다 대도 측정)을 내 화면에서 끈다. 케이블 오브젝트를 손이 잡지도 않게 한다.
        foreach (var mp in GetComponentsInChildren<MultimeterProbe>(true))
        {
            foreach (var c in mp.GetComponents<Collider>()) c.enabled = false;
            var g = mp.GetComponent<Grabbable>();
            if (g != null) g.isGrabbable = false;
        }
        // 케이블을 본체 밖으로 길게 끌어내면 원래 경계 상자를 벗어나 화면에서 잘릴 수 있다
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
        Debug.Log($"[VrProbe] {name}: VR 프로브 조작 준비 (빨강 뼈 {_red.bones.Count}개, 검정 {(_black != null ? _black.bones.Count : 0)}개)");
    }

    private void OnDisable()
    {
        foreach (var h in _hands) Unhook(h);
        _hands.Clear();
        if (_bodyLockedByUs && _bodyGrab != null) _bodyGrab.isGrabbable = true;
        _bodyLockedByUs = false;
        RestoreRest(_red); RestoreRest(_black);
        DestroyPen(_red); DestroyPen(_black);
        if (_red != null) _red.state = State.Docked;
        if (_black != null) { _black.state = State.Docked; _black.clip = null; }
        if (_ring != null) Destroy(_ring.gameObject);
        if (_guide != null) Destroy(_guide);
        if (_valueLabel != null) Destroy(_valueLabel);
    }

    // ── 케이블 뼈대 ─────────────────────────────

    private Probe BuildProbe(MultimeterProbe probe, bool red)
    {
        if (probe == null) return null;
        string color = red ? "Red" : "Black";
        Transform root = null;
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            // Multimeter_Bone_Red_Root / Megohmmeter_Bone_Cable_Red
            if (t.name.EndsWith($"_{color}_Root") || t.name.EndsWith($"Cable_{color}")) { if (t.GetComponent<Renderer>() == null) { root = t; break; } }
        }
        if (root == null) return null;

        var p = new Probe { red = red, probe = probe, root = root, cable = probe.GetComponent<SkinnedMeshRenderer>() };
        var cur = root;
        while (true)
        {
            Transform next = null;
            foreach (Transform c in cur) if (c.name.Contains(color)) { next = c; break; }
            if (next == null) break;
            p.bones.Add(next);
            cur = next;
        }
        if (p.bones.Count < 3) return null;

        int n = p.bones.Count;
        p.restPos = new Vector3[n]; p.restRot = new Quaternion[n]; p.axis = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            p.restPos[i] = p.bones[i].localPosition;
            p.restRot[i] = p.bones[i].localRotation;
            Vector3 a = i + 1 < n ? p.bones[i + 1].localPosition : (i > 0 ? p.axis[i - 1] : Vector3.up);
            p.axis[i] = a.sqrMagnitude > 1e-8f ? a.normalized : Vector3.up;
            // 케이블 길이(월드): 단자(root) → 각 뼈 사이 거리의 합
            p.length += Vector3.Distance(i == 0 ? root.position : p.bones[i - 1].position, p.bones[i].position);
        }
        p.length = Mathf.Max(p.length, 0.3f);
        var last = p.bones[n - 1];
        p.restTipLocal = transform.InverseTransformPoint(last.position);
        p.restDirLocal = transform.InverseTransformDirection(last.rotation * p.axis[n - 1]);
        return p;
    }

    private void RestoreRest(Probe p)
    {
        if (p == null || !p.posed) return;
        for (int i = 0; i < p.bones.Count; i++)
        {
            p.bones[i].localPosition = p.restPos[i];
            p.bones[i].localRotation = p.restRot[i];
        }
        p.posed = false;
    }

    /// <summary>본체 단자(root) → 끝(tip) 을 3차 곡선으로 잇고, 뼈를 곡선 위에 차례로 놓는다.</summary>
    private void Pose(Probe p, Vector3 tip, Vector3 dir)
    {
        Vector3 a = p.root.position;
        Vector3 toTip = tip - a;
        float maxLen = p.length * 1.1f;
        if (toTip.magnitude > maxLen) tip = a + toTip.normalized * maxLen;   // 케이블 길이 이상은 늘어나지 않게
        float d = Vector3.Distance(a, tip);
        float sag = Mathf.Max(0f, p.length - d) * 0.45f;
        Vector3 exitDir = p.root.rotation * (p.bones[0].localPosition.sqrMagnitude > 1e-8f ? p.bones[0].localPosition.normalized : Vector3.down);
        Vector3 c1 = a + exitDir * Mathf.Min(0.08f, d * 0.3f) + Vector3.down * sag;
        Vector3 c2 = tip - dir * Mathf.Min(0.15f, d * 0.4f) + Vector3.down * sag * 0.6f;

        int n = p.bones.Count;
        var pts = new Vector3[n + 1];
        for (int i = 0; i <= n; i++) pts[i] = Bezier(a, c1, c2, tip, (float)i / n);

        for (int i = 0; i < n; i++)
        {
            var b = p.bones[i];
            b.position = pts[i];
            Vector3 want = i + 1 < n ? pts[i + 1] - pts[i] : dir;
            if (i == n - 1) b.position = tip - dir * 0.001f;
            if (want.sqrMagnitude < 1e-8f) continue;
            Vector3 cur = b.rotation * p.axis[i];
            b.rotation = Quaternion.FromToRotation(cur, want.normalized) * b.rotation;
        }
        p.posed = true;
        p.lastTip = tip; p.lastDir = dir;
    }

    private static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float u = 1f - t;
        return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
    }

    private Vector3 RestTip(Probe p) => transform.TransformPoint(p.restTipLocal);
    private Vector3 CurrentTip(Probe p) => p.state == State.Docked ? RestTip(p) : p.lastTip;

    // ── 손 ─────────────────────────────

    private void ScanHands()
    {
        if (Time.unscaledTime < _nextHandScan) return;
        _nextHandScan = Time.unscaledTime + 2f;
        foreach (var h in FindObjectsByType<Hand>(FindObjectsSortMode.None))
        {
            if (h == null || _hands.Contains(h)) continue;
            var nob = h.GetComponentInParent<FishNet.Object.NetworkObject>();
            if (nob != null && nob.IsSpawned && !nob.IsOwner) continue;   // 다른 사람 손은 제외
            _hands.Add(h);
            h.OnTriggerGrab += OnHandGrabPressed;
            h.OnTriggerRelease += OnHandReleasePressed;
            h.OnGrabbed += OnHandGrabbed;
            h.OnSqueezed += OnHandSqueezed;
            h.OnUnsqueezed += OnHandUnsqueezed;
            Debug.Log($"[VrProbe] {name}: 손 연결 {h.name} (왼손={h.left})");
        }
        _hands.RemoveAll(h => h == null);
    }

    private void Unhook(Hand h)
    {
        if (h == null) return;
        h.OnTriggerGrab -= OnHandGrabPressed;
        h.OnTriggerRelease -= OnHandReleasePressed;
        h.OnGrabbed -= OnHandGrabbed;
        h.OnSqueezed -= OnHandSqueezed;
        h.OnUnsqueezed -= OnHandUnsqueezed;
    }

    private bool IsHolding(Hand h) => (_red != null && _red.state == State.Held && _red.hand == h) || (_black != null && _black.state == State.Held && _black.hand == h);

    private bool BodyHeldByOtherHand(Hand h)
    {
        if (_bodyGrab == null || !_bodyGrab.IsHeld()) return false;
        foreach (var o in _bodyGrab.GetHeldBy()) if (o != null && o != h) return true;
        return false;
    }

    // ── 누르면 쥐고, 다시 누르면 놓는다(토글) ──
    // 예전에는 버튼을 누르고 있는 동안만 쥐어서, 눌렀다 떼면 프로브가 곧바로 돌아가 '안 잡힌다' 로 보였다(VR 테스트 로그: 1초 간격으로 꺼냄만 반복).
    // 컨트롤러 연결 스크립트가 같은 버튼 이벤트를 두 번 보내므로 짧은 시간 안의 중복 입력은 무시한다.
    private readonly Dictionary<Hand, float> _lastToggle = new Dictionary<Hand, float>();

    private bool Debounced(Hand hand)
    {
        if (_lastToggle.TryGetValue(hand, out float t) && Time.unscaledTime - t < 0.25f) return true;
        _lastToggle[hand] = Time.unscaledTime;
        return false;
    }

    private void OnHandGrabPressed(Hand hand, Grabbable _) => Toggle(hand, false);

    // 빈손 트리거로도 꺼내고 놓을 수 있다
    private void OnHandSqueezed(Hand hand, Grabbable held)
    {
        if (held == null) Toggle(hand, true);
    }

    private void OnHandUnsqueezed(Hand hand, Grabbable _) { }

    private void Toggle(Hand hand, bool bySqueeze)
    {
        if (!isActiveAndEnabled || hand == null) return;
        if (IsHolding(hand))
        {
            if (Debounced(hand)) return;
            foreach (var p in new[] { _red, _black })
                if (p != null && p.state == State.Held && p.hand == hand) { ReleaseProbe(p); Debug.Log($"[VrProbe] {(p.red ? "빨간 프로브" : "검정 집게")} 내려놓음"); }
            return;
        }
        if (_lastToggle.TryGetValue(hand, out float t0) && Time.unscaledTime - t0 < 0.25f) return;
        if (TryTake(hand, bySqueeze)) _lastToggle[hand] = Time.unscaledTime;
    }

    private float _nextMissLog;

    private bool TryTake(Hand hand, bool bySqueeze)
    {
        if (!isActiveAndEnabled || hand == null || IsHolding(hand) || hand.GetHeld() != null) return false;
        Vector3 palm = hand.palmTransform != null ? hand.palmTransform.position : hand.transform.position;

        Probe pick = null;
        float best = TakeFromTipRadius;
        foreach (var p in new[] { _red, _black })
        {
            if (p == null || p.state == State.Held) continue;
            float d = Vector3.Distance(palm, CurrentTip(p));
            if (d < best) { best = d; pick = p; }
        }
        if (pick == null && BodyHeldByOtherHand(hand) && Vector3.Distance(palm, BodyCenter()) < TakeFromBodyRadius)
            pick = NextProbeToTake();
        if (pick == null)
        {
            // 본체 근처에서 눌렀는데 못 꺼낸 경우만 원인을 남긴다(테스트 확인용)
            float dBody = Vector3.Distance(palm, BodyCenter());
            if (dBody < 0.5f && Time.unscaledTime > _nextMissLog)
            {
                _nextMissLog = Time.unscaledTime + 1f;
                Debug.Log($"[VrProbe] 꺼내기 실패: 손={hand.name} 본체거리={dBody:0.00}m(기준 {TakeFromBodyRadius}) 다른손이본체쥠={BodyHeldByOtherHand(hand)} " +
                          $"빨강끝거리={(_red != null ? Vector3.Distance(palm, CurrentTip(_red)) : -1f):0.00}m 트리거={bySqueeze}");
            }
            return false;
        }

        if (pick.state == State.Clipped && !pick.red) Unclip(pick);
        pick.state = State.Held;
        pick.hand = hand;
        pick.bySqueeze = bySqueeze;
        Debug.Log($"[VrProbe] {(pick.red ? "빨간 프로브" : "검정 집게")} 꺼냄 (손={hand.name}, {(bySqueeze ? "트리거" : "그립")})");
        pick.snap = null; pick.done = false;
        if (!pick.posed) { pick.lastTip = RestTip(pick); pick.lastDir = transform.TransformDirection(pick.restDirLocal); }
        Haptic(hand, 0.06f, 0.35f);
        return true;
    }

    private void OnHandGrabbed(Hand hand, Grabbable grab)
    {
        // 프로브를 쥔 손이 같은 순간 근처 물건까지 잡지 않게 한다
        if (IsHolding(hand)) hand.ForceReleaseGrab();
    }

    // 버튼을 떼도 프로브는 손에 남는다(다시 누르면 놓는다)
    private void OnHandReleasePressed(Hand hand, Grabbable _) { }

    private void ReleaseProbe(Probe p)
    {
        p.hand = null;
        if (!p.red && p.clip != null) { p.state = State.Clipped; return; }   // 접지에 물린 검정 집게는 그대로 둔다
        StartReturn(p);
    }

    private Probe NextProbeToTake()
    {
        bool blackFirst = _black != null && _black.clip == null && _black.state != State.Held && FindGround(BodyCenter(), 2f) != null;
        if (blackFirst) return _black;
        if (_red != null && _red.state != State.Held) return _red;
        return null;
    }

    private void StartReturn(Probe p)
    {
        // 펜을 거두고 본체에 늘어진 원래 케이블 메시를 다시 보인다
        HidePen(p);
        RestoreRest(p);
        p.state = State.Docked;
        p.snap = null;
    }

    private void Unclip(Probe p)
    {
        p.clip = null;
        if (p.probe != null) p.probe.currentTerminalId = "";
    }

    // ── 매 프레임 ─────────────────────────────

    private void LateUpdate()
    {
        ScanHands();
        LockBodySecondHand();

        bool anyActive = false;
        foreach (var p in new[] { _red, _black })
        {
            if (p == null) continue;
            switch (p.state)
            {
                case State.Held:
                    if (p.hand == null || !p.hand.isActiveAndEnabled) { StartReturn(p); break; }
                    UpdateHeld(p);
                    anyActive = true;
                    break;
                case State.Clipped:
                    if (p.clip == null || !p.clip.isActiveAndEnabled || Vector3.Distance(p.root.position, ClipCenter(p.clip)) > p.length * 1.2f)
                    {
                        Unclip(p); StartReturn(p); break;   // 본체를 들고 너무 멀어지면 집게가 빠진다
                    }
                    ShowPen(p, ClipCenter(p.clip), Vector3.down);
                    break;
                case State.Returning:
                    p.returnT += Time.deltaTime / ReturnTime;
                    if (p.returnT >= 1f) { RestoreRest(p); p.state = State.Docked; break; }
                    float t = Mathf.SmoothStep(0f, 1f, p.returnT);
                    Pose(p, Vector3.Lerp(p.returnFrom, RestTip(p), t),
                         Vector3.Slerp(p.returnFromDir, transform.TransformDirection(p.restDirLocal), t));
                    break;
            }
        }

        if (!anyActive) HideRing();
        UpdateValueLabel();
        UpdateGuide();
    }

    private void UpdateHeld(Probe p)
    {
        var hand = p.hand;
        Vector3 palm = hand.palmTransform != null ? hand.palmTransform.position : hand.transform.position;
        Vector3 knuckle = palm;
        if (hand.fingers != null && hand.fingers.Length > 1 && hand.fingers[1] != null && hand.fingers[1].FingerJoints != null && hand.fingers[1].FingerJoints.Length > 0)
            knuckle = hand.fingers[1].FingerJoints[0].position;
        Vector3 dir = knuckle - palm;
        dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : hand.transform.forward;
        Vector3 tip = knuckle + dir * PenReach;

        // 자석 보정: 단자 근처면 끝을 단자 중심에 붙인다
        var mp = FindPoint(tip, SnapRadius, wantGround: !p.red);
        if (!p.red && p.clip != null && mp != p.clip) Unclip(p);   // 쥔 채로 접지에서 떼면 집게도 빠진다
        if (mp != p.snap)
        {
            p.snap = mp;
            p.snapStart = Time.time;
            p.done = false;
            if (mp != null) { Haptic(hand, 0.04f, 0.25f); Debug.Log($"[VrProbe] {(p.red ? "빨간 프로브" : "검정 집게")} 단자 접촉: {mp.terminalId}"); }
        }

        if (mp != null)
        {
            Vector3 c = ClipCenter(mp);
            tip = Vector3.Lerp(tip, c, 0.85f);
            float dwell = p.red ? RedDwell : BlackDwell;
            float prog = Mathf.Clamp01((Time.time - p.snapStart) / dwell);
            if (!p.done && prog >= 1f)
            {
                p.done = true;
                OnContactComplete(p, mp);
            }
            ShowRing(c, p.done ? 1f : prog, p.done ? new Color(0.3f, 1f, 0.45f) : new Color(1f, 0.85f, 0.2f));
        }
        else HideRing();

        ShowPen(p, tip, dir);
        GripFingers(hand);
    }

    private void OnContactComplete(Probe p, MeasurementPoint mp)
    {
        if (!p.red)
        {
            // 검정 집게: 접지에 물린다. 손을 놓아도 남는다.
            p.clip = mp;
            p.probe.currentTerminalId = mp.terminalId;
            Haptic(p.hand, 0.1f, 0.6f);
            Debug.Log($"[VrProbe] 검정 집게 → {mp.terminalId}");
            // 물리면 손에서 저절로 놓아져 집게가 접지에 남는다 — 빈 손으로 바로 빨간 프로브를 꺼낼 수 있게
            p.hand = null;
            p.state = State.Clipped;
            return;
        }

        if (MeterOff())
        {
            FlashGuide("다이얼을 돌려 측정 모드를 먼저 고르세요");
            Debug.Log("[VrProbe] 측정 보류: 계측기 OFF");
            return;
        }
        if (NeedsBlack(ClipCenter(mp)))
        {
            FlashGuide("검정 집게를 먼저 접지에 물리세요");
            Debug.Log($"[VrProbe] 측정 보류: 접지 집게 필요 ({mp.terminalId})");
            return;
        }

        // 빨간 프로브 측정: 값 계산·표시·서버 신호는 기존 MultimeterMaster 그대로.
        // 측정은 한 번으로 끝내고(currentTerminalId 원복) 화면 값은 남겨 둔다 → HOLD
        var probe = p.probe;
        string before = probe.currentTerminalId;
        probe.currentTerminalId = mp.terminalId;
        _master.EvaluateConnection();
        probe.currentTerminalId = before;

        Haptic(p.hand, 0.12f, 0.8f);
        _valueAt = mp;
        _valueText = _master.lcdDisplay != null ? _master.lcdDisplay.text : null;
        _valueLabelUntil = Time.time + 6f;
        Debug.Log($"[VrProbe] 빨간 프로브 측정 → {mp.terminalId} = {(_master.lcdDisplay != null ? _master.lcdDisplay.text : "?")}");
    }

    // ── 단자 찾기 ─────────────────────────────

    private static readonly Collider[] s_hits = new Collider[32];

    private static MeasurementPoint FindPoint(Vector3 pos, float radius, bool wantGround)
    {
        int n = Physics.OverlapSphereNonAlloc(pos, radius, s_hits, ~0, QueryTriggerInteraction.Collide);
        MeasurementPoint best = null;
        float bestD = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var col = s_hits[i];
            if (col == null) continue;
            var mp = col.GetComponent<MeasurementPoint>() ?? col.GetComponentInParent<MeasurementPoint>();
            if (mp == null || !mp.isActiveAndEnabled || string.IsNullOrEmpty(mp.terminalId)) continue;
            if (IsGround(mp) != wantGround) continue;
            float d = col.bounds.SqrDistance(pos);
            if (d < bestD) { bestD = d; best = mp; }
        }
        return best;
    }

    private static bool IsGround(MeasurementPoint mp) => mp.terminalId.Contains("Ground");

    private static Vector3 ClipCenter(MeasurementPoint mp)
    {
        var col = mp.GetComponent<Collider>() ?? mp.GetComponentInChildren<Collider>();
        return col != null ? col.bounds.center : mp.transform.position;
    }

    private float _groundCacheTime = -10f;
    private readonly List<MeasurementPoint> _grounds = new List<MeasurementPoint>();

    private MeasurementPoint FindGround(Vector3 near, float within)
    {
        if (Time.unscaledTime - _groundCacheTime > 1f)
        {
            _groundCacheTime = Time.unscaledTime;
            _grounds.Clear();
            foreach (var mp in FindObjectsByType<MeasurementPoint>(FindObjectsSortMode.None))
                if (mp.isActiveAndEnabled && !string.IsNullOrEmpty(mp.terminalId) && IsGround(mp)) _grounds.Add(mp);
        }
        MeasurementPoint best = null;
        float bestD = within;
        foreach (var g in _grounds)
        {
            if (g == null) continue;
            float d = Vector3.Distance(near, ClipCenter(g));
            if (d < bestD) { bestD = d; best = g; }
        }
        return best;
    }

    /// <summary>이 측정 위치 가까이에 접지 단자가 있으면 검정 집게를 먼저 물려야 한다(없으면 차체 접지로 본다).</summary>
    private bool NeedsBlack(Vector3 measureAt)
    {
        if (_black == null || _black.clip != null) return false;
        return FindGround(measureAt, GroundNeededWithin) != null;
    }

    private bool MeterOff() => !_master.bypassModeCheck && _master.currentMode == MultimeterMode.OFF;

    private Vector3 BodyCenter()
    {
        var col = GetComponent<Collider>();
        return col != null ? col.bounds.center : transform.position;
    }

    // ── 본체 두 번째 손 잡기 막기 ─────────────────
    // 본체를 든 상태에서 빈 손으로 본체를 쥐면 '프로브 꺼내기' 가 되어야 하는데, AutoHand 가 본체를 양손으로 잡아 버리면 안 된다.
    private void LockBodySecondHand()
    {
        if (_bodyGrab == null) return;
        bool heldByMe = false;
        if (_bodyGrab.IsHeld())
            foreach (var h in _bodyGrab.GetHeldBy()) if (_hands.Contains(h)) { heldByMe = true; break; }

        if (heldByMe && !_bodyLockedByUs && _bodyGrab.isGrabbable)
        {
            _bodyGrab.isGrabbable = false;   // 이미 쥔 손은 그대로 유지된다(enabled 는 건드리지 않음)
            _bodyLockedByUs = true;
        }
        else if (!heldByMe && _bodyLockedByUs)
        {
            _bodyGrab.isGrabbable = true;
            _bodyLockedByUs = false;
        }
    }

    // ── 표시: 링 · 값 · 안내 ─────────────────────

    private void ShowRing(Vector3 center, float progress, Color color)
    {
        if (_ring == null)
        {
            var go = new GameObject("[VrProbeRing]");
            go.layer = 1;
            _ring = go.AddComponent<LineRenderer>();
            _ring.useWorldSpace = true;
            _ring.loop = false;
            _ring.widthMultiplier = 0.004f;
            _ring.numCapVertices = 2;
            _ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ring.receiveShadows = false;
            var sh = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
            _ringMat = new Material(sh);
            _ringMat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            _ringMat.renderQueue = 4000;
            _ring.material = _ringMat;
        }
        _ring.gameObject.SetActive(true);
        _ring.startColor = _ring.endColor = color;

        var cam = Camera.main;
        Vector3 normal = cam != null ? (cam.transform.position - center).normalized : Vector3.up;
        Vector3 u = Vector3.Cross(normal, Vector3.up);
        if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(normal, Vector3.right);
        u.Normalize();
        Vector3 v = Vector3.Cross(normal, u);
        const int seg = 40;
        int count = Mathf.Max(2, Mathf.CeilToInt(seg * Mathf.Clamp01(progress)) + 1);
        _ring.positionCount = count;
        const float r = 0.025f;
        for (int i = 0; i < count; i++)
        {
            float ang = Mathf.PI * 2f * Mathf.Clamp01(progress) * i / (count - 1) + Mathf.PI * 0.5f;
            _ring.SetPosition(i, center + (u * Mathf.Cos(ang) + v * Mathf.Sin(ang)) * r);
        }
    }

    private void HideRing()
    {
        if (_ring != null && _ring.gameObject.activeSelf) _ring.gameObject.SetActive(false);
    }

    private GameObject MakeLabel(string goName)
    {
        if (s_labelPrefab == null) s_labelPrefab = Resources.Load<GameObject>("ItemNameTagLabel");
        if (s_labelPrefab == null) return null;
        var go = Instantiate(s_labelPrefab);
        go.name = goName;
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 1;
        return go;
    }

    private string _valueShown;
    private static float s_valueShowingUntil;
    /// <summary>측정값 라벨이 떠 있는 동안 true — 같은 단자 위 목표 마커(노란 안내)가 값을 가리지 않게 NextActionGuide 가 마커를 숨긴다.</summary>
    public static bool ValueLabelShowing => Time.time < s_valueShowingUntil;

    private void UpdateValueLabel()
    {
        bool stillOn = _valueAt != null && _red != null && _red.state == State.Held && _red.snap == _valueAt;
        if (stillOn) _valueLabelUntil = Mathf.Max(_valueLabelUntil, Time.time + 2f);
        if (_valueAt == null || Time.time > _valueLabelUntil)
        {
            if (_valueLabel != null) _valueLabel.SetActive(false);
            return;
        }
        if (_valueLabel == null) _valueLabel = MakeLabel("[VrProbeValue]");
        if (_valueLabel == null) return;
        _valueLabel.SetActive(true);
        // 값이 떠 있는 동안 단자 위 노란 목표 마커('빨간 프로브를 대주세요')를 숨긴다 — 그 안내가 값을 덮었다
        s_valueShowingUntil = Time.time + 0.4f;
        var gsys = GuideSystem.Instance;
        if (gsys != null) gsys.SetTargetMarkerVisible(false);
        // 단자 바로 위, 보는 사람 쪽으로 당겨 띄운다(마커·안내보다 앞)
        Vector3 at = ClipCenter(_valueAt) + Vector3.up * 0.06f;
        var cam = Camera.main;
        if (cam != null) at += (cam.transform.position - at).normalized * 0.1f;
        _valueLabel.transform.position = at;
        var l = _valueLabel.GetComponent<WorldHoverLabel>();
        string v = _valueText;
        if (string.IsNullOrWhiteSpace(v) && _master.lcdDisplay != null) v = _master.lcdDisplay.text;
        string shown = string.IsNullOrWhiteSpace(v) ? "----" : v;
        if (l != null && shown != _valueShown) { _valueShown = shown; l.SetText(shown, "측정값 HOLD"); }
    }

    private float _flashUntil;
    private string _flashText;

    private void FlashGuide(string text)
    {
        _flashText = text;
        _flashUntil = Time.time + 2.5f;
    }

    private void UpdateGuide()
    {
        string text = null;
        bool bodyMine = false;
        if (_bodyGrab != null && _bodyGrab.IsHeld())
            foreach (var h in _bodyGrab.GetHeldBy()) if (_hands.Contains(h)) { bodyMine = true; break; }
        bool redHeld = _red != null && _red.state == State.Held;
        bool blackHeld = _black != null && _black.state == State.Held;

        if (Time.time < _flashUntil) text = _flashText;
        else if (bodyMine || redHeld || blackHeld)
        {
            if (MeterOff()) text = "다이얼을 돌려 측정 모드를 고르세요";
            else if (blackHeld) text = "검정 집게를 접지 단자에 대고 잠시 기다리세요";
            else if (redHeld)
                text = _red.snap != null && NeedsBlack(ClipCenter(_red.snap)) ? "검정 집게를 먼저 접지에 물리세요" : "빨간 프로브를 단자에 대고 잠시 기다리세요 (다시 누르면 내려놓기)";
            else if (_black != null && _black.clip == null && FindGround(BodyCenter(), 2f) != null) text = "다른 손을 계측기에 대고 그립·트리거 → 검정 집게";
            else text = "다른 손을 계측기에 대고 그립·트리거 → 빨간 프로브";
        }

        // 측정값이 떠 있는 동안은 안내를 숨긴다 — 같은 단자 위에 겹쳐 값이 잘 안 보였다(값을 읽는 순간에는 안내가 필요 없다)
        if (_valueLabel != null && _valueLabel.activeSelf && Time.time >= _flashUntil) text = null;

        if (text == null)
        {
            if (_guide != null && _guide.activeSelf) _guide.SetActive(false);
            return;
        }
        if (_guide == null)
        {
            _guide = MakeLabel("[VrProbeGuide]");
            if (_guide == null) return;
            _guideLabel = _guide.GetComponent<WorldHoverLabel>();
        }
        if (!_guide.activeSelf) _guide.SetActive(true);
        if (text != _guideText && _guideLabel != null)
        {
            _guideText = text;
            _guideLabel.SetText(_master.name.Contains("Megger") ? "절연저항계" : "멀티미터", text);
        }
        // 프로브를 들고 있으면 사용자는 단자 쪽을 보고 있으므로 안내도 프로브 끝 근처에 띄운다
        Vector3 want;
        var heldP = redHeld ? _red : (blackHeld ? _black : null);
        if (heldP != null)
        {
            want = heldP.lastTip + Vector3.up * 0.1f;
        }
        else
        {
            var col = GetComponent<Collider>();
            Vector3 top = col != null ? new Vector3(col.bounds.center.x, col.bounds.max.y, col.bounds.center.z) : transform.position;
            want = top + Vector3.up * 0.12f;
            // 계측기 바로 위에 띄우면 LCD(측정값 화면)를 가리므로 보는 사람 기준 옆으로 비켜 띄운다
            var cam = Camera.main;
            if (cam != null) want += Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized * 0.14f;
        }
        _guide.transform.position = Vector3.Lerp(_guide.transform.position, want, _guide.transform.position == Vector3.zero ? 1f : Time.deltaTime * 12f);
    }

    // ── 손에 쥔 프로브 펜 ─────────────────────

    private const float PenTipLen = 0.025f;
    private const float PenBodyLen = 0.10f;
    private const int CordSegments = 24;
    private static Material s_cordMat;

    private void ShowPen(Probe p, Vector3 tip, Vector3 dir)
    {
        if (p.pen == null) BuildPen(p);
        if (p.cable != null && p.cable.enabled) p.cable.enabled = false;
        if (!p.pen.activeSelf) p.pen.SetActive(true);
        if (dir.sqrMagnitude < 1e-6f) dir = Vector3.down;
        p.pen.transform.SetPositionAndRotation(tip, Quaternion.FromToRotation(Vector3.up, dir));
        p.lastTip = tip; p.lastDir = dir;

        // 본체 단자 → 펜 꼬리: 남는 길이만큼 아래로 처지게
        Vector3 a = p.root.position;
        Vector3 b = tip - dir * (PenTipLen + PenBodyLen);
        float d = Vector3.Distance(a, b);
        float sag = Mathf.Max(0.03f, (p.length - d) * 0.45f);
        Vector3 c1 = a + Vector3.down * sag;
        Vector3 c2 = b - dir * Mathf.Min(0.1f, d * 0.3f) + Vector3.down * sag * 0.6f;
        if (!p.cord.enabled) p.cord.enabled = true;
        for (int i = 0; i < CordSegments; i++) p.cord.SetPosition(i, Bezier(a, c1, c2, b, (float)i / (CordSegments - 1)));
    }

    private void HidePen(Probe p)
    {
        if (p == null) return;
        if (p.pen != null && p.pen.activeSelf) p.pen.SetActive(false);
        if (p.cord != null) p.cord.enabled = false;
        if (p.cable != null) p.cable.enabled = true;
    }

    private void DestroyPen(Probe p)
    {
        if (p == null) return;
        HidePen(p);
        if (p.pen != null) Destroy(p.pen);
        if (p.cord != null) Destroy(p.cord.gameObject);
        p.pen = null; p.cord = null;
    }

    /// <summary>빨강/검정 손잡이 + 금속 탐침. 충돌체는 없다(손·레이저를 막지 않게).</summary>
    private void BuildPen(Probe p)
    {
        Color body = p.red ? new Color(0.85f, 0.08f, 0.08f) : new Color(0.08f, 0.08f, 0.08f);
        var root = new GameObject(p.red ? "[VrProbePen_Red]" : "[VrProbePen_Black]");
        root.layer = 1;
        MakePart(root.transform, new Vector3(0f, -PenTipLen * 0.5f, 0f), new Vector3(0.0025f, PenTipLen * 0.5f, 0.0025f), new Color(0.75f, 0.75f, 0.78f), p);   // 금속 탐침
        MakePart(root.transform, new Vector3(0f, -PenTipLen - PenBodyLen * 0.5f, 0f), new Vector3(0.012f, PenBodyLen * 0.5f, 0.012f), body, p);              // 손잡이
        MakePart(root.transform, new Vector3(0f, -PenTipLen - 0.004f, 0f), new Vector3(0.018f, 0.003f, 0.018f), body, p);                                    // 손가락 걸이
        p.pen = root;

        var cordGo = new GameObject(p.red ? "[VrProbeCord_Red]" : "[VrProbeCord_Black]");
        cordGo.layer = 1;
        var lr = cordGo.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = CordSegments;
        lr.widthMultiplier = 0.005f;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        if (s_cordMat == null) s_cordMat = new Material(Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default"));
        lr.sharedMaterial = s_cordMat;
        lr.startColor = lr.endColor = p.red ? new Color(0.7f, 0.06f, 0.06f) : new Color(0.06f, 0.06f, 0.06f);
        lr.enabled = false;
        p.cord = lr;
    }

    private static void MakePart(Transform parent, Vector3 localPos, Vector3 localScale, Color color, Probe p)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        var col = go.GetComponent<Collider>();
        if (col != null) { col.enabled = false; Destroy(col); }
        go.layer = 1;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        var r = go.GetComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // 케이블 메시의 재질(빌드에 포함된 URP 셰이더)을 복사해 색만 바꾼다
        Material baseMat = p.cable != null ? p.cable.sharedMaterial : null;
        var m = baseMat != null ? new Material(baseMat) : new Material(r.sharedMaterial);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        r.sharedMaterial = m;
    }

    /// <summary>프로브를 쥔 손가락을 오므린다(손이 펜을 쥔 모양).</summary>
    private static void GripFingers(Hand hand)
    {
        if (hand == null || hand.fingers == null) return;
        foreach (var f in hand.fingers) if (f != null) f.SetFingerBend(0.7f);
    }

#if UNITY_EDITOR
    /// <summary>에디터 확인용: 실행하지 않고 케이블을 지정한 위치로 휘어 본다. 반환값은 빨간 케이블 뼈 개수·길이.</summary>
    public string EditorPreviewPose(Vector3 redTip, Vector3 redDir, Vector3 blackTip, bool restoreAfter)
    {
        _master = GetComponent<MultimeterMaster>();
        var red = BuildProbe(_master.redProbe, true);
        var black = BuildProbe(_master.blackProbe, false);
        if (red == null) return "red chain not found";
        Pose(red, redTip, redDir.normalized);
        if (black != null) Pose(black, blackTip, Vector3.down);
        string info = $"red bones={red.bones.Count} len={red.length:F2} tip(rest,local)={red.restTipLocal:F3} black={(black != null ? black.bones.Count + " bones" : "none")}";
        if (restoreAfter) { RestoreRest(red); RestoreRest(black); }
        return info;
    }
#endif

    private static void Haptic(Hand h, float duration, float amp)
    {
        if (h == null) return;
        try { h.PlayHapticVibration(duration, amp); } catch { }
    }
}
