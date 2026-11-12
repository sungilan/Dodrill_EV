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
//  케이블은 본체 뼈대(Bone)에 붙은 스킨 메시라, 케이블 오브젝트를 옮겨도 모양이 바뀌지 않는다.
//  그래서 케이블 뼈대를 '본체 단자 → 프로브 끝' 곡선을 따라 직접 배치한다.
//
//  · VR 기기에서만 동작한다(PC·모바일은 기존 조준 측정 그대로).
//  · 프로브 위치는 내 화면에서만 움직인다(다른 사람 화면의 케이블은 늘어진 그대로). 측정 결과는 기존처럼 서버로 간다.
//  · MultimeterMaster.Awake 에서 자동으로 붙는다. 씬 배치 불필요.
// ============================================================
[DefaultExecutionOrder(200)]   // 손(AutoHand) 이 움직인 뒤에 케이블을 배치한다
public class VrProbeHandling : MonoBehaviour
{
    // ── 조절값 ──
    private const float TakeFromBodyRadius = 0.18f;   // 빈 손으로 본체 이만큼 안을 쥐면 프로브를 꺼낸다
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
        public bool posed;

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

        var p = new Probe { red = red, probe = probe, root = root };
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
        }
        _hands.RemoveAll(h => h == null);
    }

    private void Unhook(Hand h)
    {
        if (h == null) return;
        h.OnTriggerGrab -= OnHandGrabPressed;
        h.OnTriggerRelease -= OnHandReleasePressed;
        h.OnGrabbed -= OnHandGrabbed;
    }

    private bool IsHolding(Hand h) => (_red != null && _red.state == State.Held && _red.hand == h) || (_black != null && _black.state == State.Held && _black.hand == h);

    private bool BodyHeldByOtherHand(Hand h)
    {
        if (_bodyGrab == null || !_bodyGrab.IsHeld()) return false;
        foreach (var o in _bodyGrab.GetHeldBy()) if (o != null && o != h) return true;
        return false;
    }

    private void OnHandGrabPressed(Hand hand, Grabbable _)
    {
        if (!isActiveAndEnabled || hand == null || IsHolding(hand) || hand.GetHeld() != null) return;
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
        if (pick == null) return;

        if (pick.state == State.Clipped && !pick.red) Unclip(pick);
        pick.state = State.Held;
        pick.hand = hand;
        pick.snap = null; pick.done = false;
        if (!pick.posed) { pick.lastTip = RestTip(pick); pick.lastDir = transform.TransformDirection(pick.restDirLocal); }
        Haptic(hand, 0.06f, 0.35f);
    }

    private void OnHandGrabbed(Hand hand, Grabbable grab)
    {
        // 프로브를 쥔 손이 같은 순간 근처 물건까지 잡지 않게 한다
        if (IsHolding(hand)) hand.ForceReleaseGrab();
    }

    private void OnHandReleasePressed(Hand hand, Grabbable _)
    {
        foreach (var p in new[] { _red, _black })
        {
            if (p == null || p.state != State.Held || p.hand != hand) continue;
            p.hand = null;
            if (!p.red && p.clip != null) { p.state = State.Clipped; continue; }   // 접지에 물린 검정 집게는 그대로 둔다
            StartReturn(p);
        }
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
        p.state = State.Returning;
        p.returnT = 0f;
        p.returnFrom = p.lastTip;
        p.returnFromDir = p.lastDir;
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
                    Pose(p, ClipCenter(p.clip), Vector3.down);
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
            if (mp != null) Haptic(hand, 0.04f, 0.25f);
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

        Pose(p, tip, dir);
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
            return;
        }

        if (MeterOff())
        {
            FlashGuide("다이얼을 돌려 측정 모드를 먼저 고르세요");
            return;
        }
        if (NeedsBlack(ClipCenter(mp)))
        {
            FlashGuide("검정 집게를 먼저 접지에 물리세요");
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
        _valueLabelUntil = Time.time + 4f;
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
            go.layer = 2;
            _ring = go.AddComponent<LineRenderer>();
            _ring.useWorldSpace = true;
            _ring.loop = false;
            _ring.widthMultiplier = 0.004f;
            _ring.numCapVertices = 2;
            _ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ring.receiveShadows = false;
            var sh = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
            _ringMat = new Material(sh);
            if (_ringMat.HasProperty("unity_GUIZTestMode")) _ringMat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
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
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;
        return go;
    }

    private void UpdateValueLabel()
    {
        if (_valueAt == null || Time.time > _valueLabelUntil || _master.lcdDisplay == null)
        {
            if (_valueLabel != null) _valueLabel.SetActive(false);
            return;
        }
        if (_valueLabel == null) _valueLabel = MakeLabel("[VrProbeValue]");
        if (_valueLabel == null) return;
        _valueLabel.SetActive(true);
        _valueLabel.transform.position = ClipCenter(_valueAt) + Vector3.up * 0.07f;
        var l = _valueLabel.GetComponent<WorldHoverLabel>();
        string v = _master.lcdDisplay.text;
        if (l != null) l.SetText(string.IsNullOrWhiteSpace(v) ? "----" : v, "HOLD");
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
                text = _red.snap != null && NeedsBlack(ClipCenter(_red.snap)) ? "검정 집게를 먼저 접지에 물리세요" : "빨간 프로브를 단자에 대고 잠시 기다리세요";
            else if (_black != null && _black.clip == null && FindGround(BodyCenter(), 2f) != null) text = "빈 손으로 계측기를 쥐면 검정 집게를 꺼냅니다";
            else text = "빈 손으로 계측기를 쥐면 빨간 프로브를 꺼냅니다";
        }

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
        var col = GetComponent<Collider>();
        Vector3 top = col != null ? new Vector3(col.bounds.center.x, col.bounds.max.y, col.bounds.center.z) : transform.position;
        _guide.transform.position = Vector3.Lerp(_guide.transform.position, top + Vector3.up * 0.12f, _guide.transform.position == Vector3.zero ? 1f : Time.deltaTime * 12f);
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
