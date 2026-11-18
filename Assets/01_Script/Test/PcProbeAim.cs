using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XRAirpotrSecurity;

// ============================================================
//  PcProbeAim.cs
//
//  PC 전용 — 멀티미터·절연저항계(메거) 프로브를 조준해서 꽂는 측정.
//  예전 PC 조작은 F 를 누르면 계기판 값만 바뀌고 빨간 프로브는 본체에 그대로 붙어 있었다.
//  그래서 단자 위치를 맞추려고 다가가면 본체가 배터리 팩 메시에 묻혀 계기판이 안 보였다.
//
//  사용 흐름
//    1. 계측기를 들면 화면 가운데 조준점(크로스헤어)이 뜬다.
//    2. 검정 프로브를 먼저 꽂는다(실제 측정 순서). (-)·접지 등 단자를 조준하면 '검정 프로브 꽂기 [F]'.
//       근처에 접지 단자가 있는 측정(절연 저항)이면 검정은 접지에만 꽂힌다.
//    3. 측정 단자를 조준하면 '빨간 프로브 꽂기 [F]'. F 를 누르면 프로브가 날아가 꽂히고 측정값이 뜬다(서버 신호는 기존 그대로).
//    4. 꽂힌 프로브는 그 자리에 남고, 케이블은 본체 단자에서 프로브까지 늘어져 본체를 따라다닌다(에어 호스처럼).
//       다른 단자를 조준해 F 를 누르면 빨간 프로브를 옮겨 꽂는다.
//    5. 꽂힌 프로브(또는 그 단자)를 조준하면 '회수 [F]' — 본체로 돌아온다. 둘 다 겨눠지면 빨간 것부터.
//       계측기를 내려놓거나 케이블 길이보다 멀어져도 돌아온다.
//
//  · 프로브·케이블은 내 화면에서만 보인다(VR 프로브와 같다). 측정 결과는 기존처럼 서버로 간다.
//  · 본체를 단자 근처로 가져가기만 해도 측정되던 프로브 트리거는 PC 에서 끈다(F 로만 측정).
//  · MultimeterMaster.Awake 에서 자동으로 붙는다. 씬 배치 불필요.
// ============================================================
[DefaultExecutionOrder(200)]   // 들고 있는 본체가 움직인 뒤에 케이블을 그린다
public class PcProbeAim : MonoBehaviour
{
    /// <summary>지금 내가 들고 있는 계측기의 조준 측정(없으면 null). FreeLookController 의 F 키가 쓴다.</summary>
    public static PcProbeAim Current { get; private set; }

    private const float AimDistance = 6f;
    private const float CableLength = 2.6f;        // 본체 단자에서 이보다 멀어지면 프로브가 빠진다
    private const float GroundNeededWithin = 3f;   // 측정 단자에서 이 거리 안에 접지 단자가 있으면 검정을 먼저 꽂아야 한다
    private const float FlyTime = 0.22f;
    private const float PenTipLen = 0.035f;
    private const float PenBodyLen = 0.10f;
    private const int CordSegments = 24;

    private enum State { Docked, Flying, Plugged, Returning }
    private const float RecallAimRadius = 0.08f;   // 조준선이 꽂힌 프로브에서 이만큼 안을 지나면 '회수'

    private class Probe
    {
        public bool red;
        public MultimeterProbe probe;
        public Transform socket;          // 본체 쪽 케이블 시작점
        public SkinnedMeshRenderer cable; // 본체에 늘어진 원래 케이블 메시(꽂으면 숨김)
        public GameObject pen;
        public LineRenderer cord;
        public State state;
        public MeasurementPoint point;
        public Vector3 tip, dir;          // 꽂힌 위치·방향(월드)
        public Vector3 flyFrom;
        public float flyT;
    }

    private MultimeterMaster _master;
    private Probe _red, _black;
    private bool _held;
    private bool _active;

    // 조준
    private MeasurementPoint _aim;
    private Vector3 _aimPos, _aimDir;

    // 화면 UI
    private static GameObject s_ui;
    private static RectTransform s_cross;
    private static Image[] s_bars;
    private static TextMeshProUGUI s_label;
    private static Material s_cordMat;

    private static readonly Color Idle = new Color(1f, 1f, 1f, 0.75f);
    private static readonly Color Ready = new Color(0.35f, 1f, 0.45f, 1f);
    private static readonly Color Blocked = new Color(1f, 0.75f, 0.2f, 1f);

    private void Start()
    {
        _master = GetComponent<MultimeterMaster>();
        _active = _master != null && !VrProbeHandling.IsVRDevice && Utils.GetPlatformType() == PlatformType.PC;
        if (!_active) { enabled = false; return; }

        _red = BuildProbe(_master.redProbe, true);
        _black = BuildProbe(_master.blackProbe, false);

        // F 로만 측정한다 — 본체·케이블을 단자 근처로 가져가기만 해도 측정되던 트리거를 내 화면에서 끈다(잡기용 콜라이더는 그대로)
        foreach (var mp in GetComponentsInChildren<MultimeterProbe>(true)) mp.ignoreTriggers = true;
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
    }

    private void OnDisable()
    {
        if (Current == this) { Current = null; ShowCross(false); }
        ReturnProbe(_red); ReturnProbe(_black);
    }

    private void OnDestroy()
    {
        DestroyPen(_red); DestroyPen(_black);
    }

    // ── 프로브 ─────────────────────────────

    private Probe BuildProbe(MultimeterProbe probe, bool red)
    {
        if (probe == null) return null;
        string color = red ? "Red" : "Black";
        Transform socket = null;
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            // Multimeter_Bone_Red_Root / Megohmmeter_Bone_Cable_Red (VrProbeHandling 과 같은 규칙)
            if ((t.name.EndsWith($"_{color}_Root") || t.name.EndsWith($"Cable_{color}")) && t.GetComponent<Renderer>() == null) { socket = t; break; }
        }
        return new Probe { red = red, probe = probe, socket = socket != null ? socket : transform, cable = probe.GetComponent<SkinnedMeshRenderer>() };
    }

    private void ReturnProbe(Probe p)
    {
        if (p == null) return;
        bool wasOut = p.state != State.Docked;
        if (wasOut && p.probe != null && !p.red) p.probe.currentTerminalId = "";
        p.state = State.Docked;
        // 프로브가 단자에서 빠졌으니 회로가 끊긴 것 — 마지막 값(HOLD)을 지우고 '----' 로 돌린다.
        // (예전에는 회수해도 계기판에 마지막 측정값이 그대로 남아 아직 재고 있는 것처럼 보였다)
        if (wasOut && _master != null && _master.isActiveAndEnabled) _master.EvaluateConnection();
        p.point = null;
        if (p.pen != null) p.pen.SetActive(false);
        if (p.cord != null) p.cord.enabled = false;
        if (p.cable != null) p.cable.enabled = true;
    }

    // ── 매 프레임 ───────────────────────────

    private void Update()
    {
        _held = HeldByMe();
        if (_held) Current = this;
        else if (Current == this) { Current = null; ShowCross(false); }

        if (!_held)
        {
            // 내려놓으면 프로브는 본체로 돌아온다
            if (_red != null && _red.state != State.Docked) ReturnProbe(_red);
            if (_black != null && _black.state != State.Docked) ReturnProbe(_black);
            return;
        }

        UpdateAim();
        UpdateCrossUI();
    }

    private void LateUpdate()
    {
        DrawProbe(_black);
        DrawProbe(_red);
    }

    private void DrawProbe(Probe p)
    {
        if (p == null || p.state == State.Docked) return;

        if (p.state == State.Returning)
        {
            p.flyT += Time.deltaTime / FlyTime;
            float r = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p.flyT));
            if (p.flyT >= 1f) { ReturnProbe(p); return; }
            Vector3 at = Vector3.Lerp(p.flyFrom, p.socket.position, r) + Vector3.up * (0.1f * Mathf.Sin(r * Mathf.PI));
            ShowPen(p, at, Vector3.Slerp(p.dir, Vector3.down, r));
            return;
        }

        // 꽂혀 있던 단자가 사라졌거나(단계마다 배터리 팩이 바뀐다) 케이블보다 멀어지면 빠진다
        if (p.state == State.Plugged && (p.point == null || !p.point.isActiveAndEnabled))
        { ReturnProbe(p); return; }
        if (Vector3.Distance(p.socket.position, p.tip) > CableLength)
        {
            EquipFx.Notice("probe_pulled", p.red ? "빨간 프로브가 빠졌습니다" : "검정 프로브가 빠졌습니다", "케이블 길이보다 멀어졌습니다. 다시 꽂으세요.", EquipFx.Warn, 2.5f, 3f);
            ReturnProbe(p);
            return;
        }

        Vector3 tip = p.tip;
        if (p.state == State.Flying)
        {
            p.flyT += Time.deltaTime / FlyTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p.flyT));
            Vector3 lift = Vector3.up * (0.12f * Mathf.Sin(k * Mathf.PI));
            tip = Vector3.Lerp(p.flyFrom, p.tip, k) + lift;
            if (p.flyT >= 1f) { tip = p.tip; OnArrived(p); }
        }
        ShowPen(p, tip, p.dir);
    }

    private void OnArrived(Probe p)
    {
        p.state = State.Plugged;
        AudioSource.PlayClipAtPoint(EquipFx.Click, p.tip, 0.7f);
        if (!p.red)
        {
            p.probe.currentTerminalId = p.point.terminalId;
            Debug.Log($"[PcProbe] 검정 프로브 → {p.point.terminalId}");
            return;
        }

        // 빨간 프로브 측정: 값 계산·표시·서버 신호는 기존 MultimeterMaster 그대로.
        // 한 번 재고 접촉 상태는 원래대로 돌린다(화면 값은 남는다 = HOLD). 그래야 다음 단계에서 같은 값이 또 나가지 않는다.
        string before = p.probe.currentTerminalId;
        p.probe.currentTerminalId = p.point.terminalId;
        _master.EvaluateConnection();
        p.probe.currentTerminalId = before;
        Debug.Log($"[PcProbe] 빨간 프로브 → {p.point.terminalId} = {(_master.lcdDisplay != null ? _master.lcdDisplay.text : "?")}");
    }

    // ── 조준 ───────────────────────────────

    private void UpdateAim()
    {
        _aim = null;
        var cam = Camera.main;
        if (cam == null) return;
        Ray ray = cam.ScreenPointToRay(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
        var hits = Physics.RaycastAll(ray, AimDistance, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        Transform heldRoot = _heldGrab != null ? _heldGrab.transform : null;
        foreach (var h in hits)
        {
            var t = h.collider.transform;
            if (t.IsChildOf(transform) || (heldRoot != null && t.IsChildOf(heldRoot))) continue;   // 들고 있는 계측기 자신
            if (t.IsChildOf(cam.transform)) continue;
            var mp = h.collider.GetComponent<MeasurementPoint>() ?? h.collider.GetComponentInParent<MeasurementPoint>();
            if (mp != null && mp.isActiveAndEnabled && !string.IsNullOrEmpty(mp.terminalId))
            {
                _aim = mp;
                _aimPos = SurfacePoint(ray, h, heldRoot, cam.transform);
                _aimDir = ray.direction;
                return;
            }
            if (!h.collider.isTrigger) return;   // 단자보다 앞에 벽·차체가 있으면 꽂을 수 없다
        }
    }

    /// <summary>
    /// 프로브 끝을 놓을 자리. 단자 판정용 트리거 상자는 실제 단자 메시보다 커서, 상자 겉면(레이가 처음 닿는 곳)에 꽂으면
    /// 옆에서 볼 때 공중에 떠 있었다. → 같은 조준선으로 트리거를 빼고 실제 표면을 찾아, 그 점이 단자 상자 안이면 거기에 꽂는다.
    /// 상자 안에 표면이 없으면(콜라이더 없는 단자 메시) 조준선 위에서 상자 중심에 가장 가까운 점까지 넣는다.
    /// </summary>
    private Vector3 SurfacePoint(Ray ray, RaycastHit triggerHit, Transform heldRoot, Transform cam)
    {
        var box = triggerHit.collider.bounds;
        var grow = box; grow.Expand(0.02f);
        var hits = Physics.RaycastAll(ray, AimDistance, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in hits)
        {
            var t = h.collider.transform;
            if (t.IsChildOf(transform) || (heldRoot != null && t.IsChildOf(heldRoot)) || t.IsChildOf(cam)) continue;
            if (grow.Contains(h.point)) return h.point;
            break;   // 첫 실제 표면이 단자 상자 밖이면 쓰지 않는다
        }
        float along = Vector3.Dot(box.center - ray.origin, ray.direction);
        return ray.origin + ray.direction * Mathf.Max(triggerHit.distance, along);
    }

    /// <summary>조준선이 지나가는 꽂힌 프로브(회수 대상). 빨간 것이 우선.</summary>
    private Probe AimedPluggedProbe()
    {
        var cam = Camera.main;
        if (cam == null) return null;
        Ray ray = cam.ScreenPointToRay(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
        foreach (var p in new[] { _red, _black })
        {
            if (p == null || p.state != State.Plugged) continue;
            if (_aim != null && p.point == _aim) return p;
            // 펜 끝 ~ 손잡이 끝 사이 몇 점과 조준선 사이 거리
            Vector3 a = p.tip, b = p.tip - p.dir * (PenTipLen + PenBodyLen);
            for (int i = 0; i <= 4; i++)
            {
                Vector3 q = Vector3.Lerp(a, b, i / 4f);
                float along = Vector3.Dot(q - ray.origin, ray.direction);
                if (along <= 0f || along > AimDistance) continue;
                if (Vector3.Distance(ray.origin + ray.direction * along, q) < RecallAimRadius) return p;
            }
        }
        return null;
    }

    /// <summary>지금 조준한 것에 무엇을 할 수 있는지. ok=false 면 이유를 돌려준다(빈 문자열 = 프로브가 움직이는 중).</summary>
    private string Describe(out bool ok, out Probe which, out bool recall)
    {
        ok = false; which = null; recall = false;

        // 꽂힌 프로브(또는 그 단자)를 보고 있으면 회수
        var plugged = AimedPluggedProbe();
        if (plugged != null)
        {
            ok = true; which = plugged; recall = true;
            return plugged.red ? "빨간 프로브 회수  [F]" : "검정 프로브 회수  [F]";
        }

        bool blackIn = _black == null || BlackPlugged;
        if (_aim == null)
        {
            if (!blackIn) return NeedsBlackAnywhere() ? "검정 프로브: 접지 단자를 조준하세요" : "검정 프로브: (-)·접지 단자를 조준하세요";
            return "측정할 단자를 조준하세요";
        }

        bool ground = IsGround(_aim);

        // 1) 검정 먼저 — 근처에 접지 단자가 있는 측정(절연 저항)은 접지에만, 아니면 (-) 단자 등 아무 단자에
        if (!blackIn)
        {
            if (_black.state == State.Flying || _black.state == State.Returning) return "";
            if (!ground && NeedsBlack(_aimPos)) return "검정 프로브를 먼저 접지에 꽂으세요";
            if (_red != null && _red.state != State.Docked && _red.point == _aim) return "빨간 프로브가 꽂힌 단자입니다";
            ok = true; which = _black;
            return "검정 프로브 꽂기  [F]";
        }

        // 2) 빨강
        if (_red == null) return "측정 단자";
        if (_black != null && _black.point == _aim) return "검정 프로브가 꽂힌 단자입니다";
        if (ground) return "접지 단자 — 빨간 프로브는 측정 단자에 꽂으세요";
        if (MeterOff()) return "다이얼을 돌려 측정 모드를 먼저 고르세요";
        if (_red.state == State.Flying || _red.state == State.Returning) return "";
        ok = true; which = _red;
        return _red.state == State.Plugged ? "빨간 프로브 옮겨 꽂기  [F]" : "빨간 프로브 꽂기  [F]";
    }

    /// <summary>F 키. 처리했으면 true (FreeLookController 는 이때 예전 측정 경로를 타지 않는다).</summary>
    public bool TryUse()
    {
        if (!_active || !_held) return false;
        string msg = Describe(out bool ok, out var p, out bool recall);
        if (!ok)
        {
            if (!string.IsNullOrEmpty(msg)) EquipFx.Notice("probe_aim", "프로브 꽂기", msg, EquipFx.Info, 2f, 0.5f);
            return true;
        }
        if (recall) Recall(p);
        else Plug(p, _aim, _aimPos, _aimDir);
        return true;
    }

    private void Recall(Probe p)
    {
        if (!p.red && p.probe != null) p.probe.currentTerminalId = "";
        p.flyFrom = p.tip;
        p.flyT = 0f;
        p.state = State.Returning;
        p.point = null;
        AudioSource.PlayClipAtPoint(EquipFx.Click, p.tip, 0.5f);
        Debug.Log($"[PcProbe] {(p.red ? "빨간" : "검정")} 프로브 회수");
    }

    private void Plug(Probe p, MeasurementPoint mp, Vector3 at, Vector3 dir)
    {
        // 날아가기 시작점: 이미 꽂혀 있으면 그 자리, 아니면 본체 케이블 끝
        p.flyFrom = p.state == State.Docked ? p.socket.position : p.tip;
        p.point = mp;
        p.tip = at;
        p.dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.down;
        p.flyT = 0f;
        p.state = State.Flying;
        if (!p.red) p.probe.currentTerminalId = "";
    }

    // ── 접지 ───────────────────────────────

    private static bool IsGround(MeasurementPoint mp) => mp.terminalId.Contains("Ground");

    private float _groundCacheTime = -10f;
    private readonly List<MeasurementPoint> _grounds = new List<MeasurementPoint>();

    private void RefreshGrounds()
    {
        if (Time.unscaledTime - _groundCacheTime < 1f) return;
        _groundCacheTime = Time.unscaledTime;
        _grounds.Clear();
        foreach (var mp in FindObjectsByType<MeasurementPoint>(FindObjectsSortMode.None))
            if (mp.isActiveAndEnabled && !string.IsNullOrEmpty(mp.terminalId) && IsGround(mp)) _grounds.Add(mp);
    }

    private bool BlackPlugged => _black != null && _black.state == State.Plugged && _black.point != null;

    /// <summary>이 측정 위치 가까이에 접지 단자가 있으면 검정을 먼저 꽂아야 한다(없으면 차체 접지로 본다 — 기존 autoGroundBlackProbe).</summary>
    private bool NeedsBlack(Vector3 measureAt)
    {
        if (_black == null || BlackPlugged) return false;
        RefreshGrounds();
        foreach (var g in _grounds)
            if (g != null && Vector3.Distance(measureAt, g.transform.position) < GroundNeededWithin) return true;
        return false;
    }

    private bool NeedsBlackAnywhere()
    {
        if (_black == null || BlackPlugged) return false;
        RefreshGrounds();
        return _grounds.Count > 0;
    }

    private bool MeterOff() => !_master.bypassModeCheck && _master.currentMode == MultimeterMode.OFF;

    // ── 들고 있는지 ─────────────────────────

    private SyncGrab _heldGrab;

    private bool HeldByMe()
    {
        _heldGrab = null;
        var cm = FishNet.InstanceFinder.ClientManager;
        if (cm == null || !cm.Started || cm.Connection == null || !cm.Connection.IsValid) return false;
        var held = SyncGrab.FindHeldBy(cm.Connection.ClientId);
        if (held == null || held.IsHolstered) return false;
        Transform t = held.transform;
        bool mine = t == transform || t.IsChildOf(transform) || transform.IsChildOf(t)
                 || (_master.redProbe != null && _master.redProbe.GetComponentInParent<SyncGrab>() == held)
                 || (_master.blackProbe != null && _master.blackProbe.GetComponentInParent<SyncGrab>() == held);
        if (mine) _heldGrab = held;
        return mine;
    }

    // ── 펜·케이블 ───────────────────────────

    private void ShowPen(Probe p, Vector3 tip, Vector3 dir)
    {
        if (p.pen == null) BuildPen(p);
        if (p.cable != null && p.cable.enabled) p.cable.enabled = false;
        if (!p.pen.activeSelf) p.pen.SetActive(true);
        p.pen.transform.SetPositionAndRotation(tip, Quaternion.FromToRotation(Vector3.up, -dir));   // 펜 끝(아래)이 단자를 향한다

        // 본체 단자 → 펜 꼬리: 남는 길이만큼 아래로 처진다
        Vector3 a = p.socket.position;
        Vector3 b = tip - dir * (PenTipLen + PenBodyLen);
        float d = Vector3.Distance(a, b);
        float sag = Mathf.Clamp((CableLength - d) * 0.35f, 0.03f, 0.5f);
        Vector3 c1 = a + Vector3.down * sag;
        Vector3 c2 = b - dir * Mathf.Min(0.12f, d * 0.3f) + Vector3.down * sag * 0.6f;
        if (!p.cord.enabled) p.cord.enabled = true;
        for (int i = 0; i < CordSegments; i++) p.cord.SetPosition(i, Bezier(a, c1, c2, b, (float)i / (CordSegments - 1)));
    }

    private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
    {
        float u = 1f - t;
        return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
    }

    private void DestroyPen(Probe p)
    {
        if (p == null) return;
        if (p.pen != null) Destroy(p.pen);
        if (p.cord != null) Destroy(p.cord.gameObject);
        p.pen = null; p.cord = null;
    }

    /// <summary>빨강/검정 손잡이 + 금속 탐침. 충돌체는 없다(조준 레이를 막지 않게).</summary>
    private void BuildPen(Probe p)
    {
        Color body = p.red ? new Color(0.85f, 0.08f, 0.08f) : new Color(0.08f, 0.08f, 0.08f);
        var root = new GameObject(p.red ? "[PcProbePen_Red]" : "[PcProbePen_Black]");
        root.layer = 1;   // TransparentFX — 런타임 표시물 레이어(VR 카메라도 그린다)
        // 피벗 = 탐침 끝. 위(+Y)로 탐침 → 손잡이
        MakePart(root.transform, new Vector3(0f, PenTipLen * 0.5f, 0f), new Vector3(0.0025f, PenTipLen * 0.5f, 0.0025f), new Color(0.75f, 0.75f, 0.78f), p);
        MakePart(root.transform, new Vector3(0f, PenTipLen + PenBodyLen * 0.5f, 0f), new Vector3(0.012f, PenBodyLen * 0.5f, 0.012f), body, p);
        MakePart(root.transform, new Vector3(0f, PenTipLen + 0.004f, 0f), new Vector3(0.018f, 0.003f, 0.018f), body, p);
        p.pen = root;

        var cordGo = new GameObject(p.red ? "[PcProbeCord_Red]" : "[PcProbeCord_Black]");
        cordGo.layer = 1;
        var lr = cordGo.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = CordSegments;
        lr.widthMultiplier = 0.006f;
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

    // ── 조준점 UI ───────────────────────────

    private void UpdateCrossUI()
    {
        EnsureUI();
        ShowCross(true);
        string msg = Describe(out bool ok, out _, out _);
        Color c = ok ? Ready : (_aim != null ? Blocked : Idle);
        foreach (var b in s_bars) b.color = c;
        s_label.text = msg;
        s_label.color = c;
        s_cross.localScale = Vector3.one * (ok ? 1.25f : 1f);
    }

    private static void ShowCross(bool on)
    {
        if (s_ui != null && s_ui.activeSelf != on) s_ui.SetActive(on);
    }

    private void EnsureUI()
    {
        if (s_ui != null) return;
        s_ui = new GameObject("[PcProbeCrosshair]");
        DontDestroyOnLoad(s_ui);
        var canvas = s_ui.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 290;
        var scaler = s_ui.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        var cgo = new GameObject("Cross", typeof(RectTransform));
        cgo.transform.SetParent(s_ui.transform, false);
        s_cross = (RectTransform)cgo.transform;
        s_cross.anchorMin = s_cross.anchorMax = new Vector2(0.5f, 0.5f);
        s_cross.sizeDelta = new Vector2(48f, 48f);

        // 가운데를 비운 + 모양 네 막대와 가운데 점 (슈팅 게임 조준점)
        const float gap = 7f, len = 14f, w = 3f;
        s_bars = new[]
        {
            Bar(new Vector2(0f, gap + len * 0.5f), new Vector2(w, len)),
            Bar(new Vector2(0f, -gap - len * 0.5f), new Vector2(w, len)),
            Bar(new Vector2(gap + len * 0.5f, 0f), new Vector2(len, w)),
            Bar(new Vector2(-gap - len * 0.5f, 0f), new Vector2(len, w)),
            Bar(Vector2.zero, new Vector2(w, w)),
        };

        var lgo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        lgo.transform.SetParent(s_ui.transform, false);
        s_label = lgo.GetComponent<TextMeshProUGUI>();
        var lrt = s_label.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
        lrt.anchoredPosition = new Vector2(0f, -58f);
        lrt.sizeDelta = new Vector2(900f, 60f);
        var font = FindKoreanFont();
        if (font != null) s_label.font = font;
        s_label.fontSize = 30f;
        s_label.fontStyle = FontStyles.Bold;
        s_label.alignment = TextAlignmentOptions.Center;
        s_label.enableWordWrapping = false;
        s_label.raycastTarget = false;
        s_label.outlineWidth = 0.18f;
        s_label.outlineColor = new Color32(0, 0, 0, 200);
    }

    private static Image Bar(Vector2 pos, Vector2 size)
    {
        var go = new GameObject("Bar", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(s_cross, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        var ol = go.AddComponent<Outline>();   // 밝은 배경에서도 보이게
        ol.effectColor = new Color(0f, 0f, 0f, 0.6f);
        ol.effectDistance = new Vector2(1f, -1f);
        return img;
    }

    /// <summary>한글이 들어 있는 폰트를 화면 UI 에서 빌려 온다(기본 TMP 폰트에는 한글이 없다).</summary>
    private static TMP_FontAsset FindKoreanFont()
    {
        foreach (var t in FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (t.font != null && t.font.HasCharacter('검', true)) return t.font;
        return null;
    }
}
