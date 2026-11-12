using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using XRAirpotrSecurity;

// ============================================================
//  AirCompressor.cs
//
//  에어 공급 장치(컴프레서) + 에어 호스.
//  · 밸브: VR 은 검지로 밸브 손잡이를 툭 치면, PC 는 밸브를 보고 E(또는 클릭), 모바일은 밸브를 터치하면 열고 닫는다
//  · 호스 끝을 에어 임팩렌치의 에어 입구에 대면 연결된다(AirImpactWrench 가 요청). 8m 이상 끌고 가면 빠진다
//  · 밸브가 열렸는데 공구에 연결되지 않으면 호스 끝에서 공기가 그대로 뿜어져 나와 호스가 요동친다 → 위험 경고·진동
//  · 밸브·연결 상태는 서버를 거쳐 모두에게 같다
// ============================================================
public class AirCompressor : NetworkBehaviour
{
    public static readonly List<AirCompressor> All = new List<AirCompressor>();

    [Tooltip("호스가 나오는 곳")] public Transform coupler;
    [Tooltip("밸브 손잡이(콜라이더 포함)")] public Transform valve;
    [Tooltip("호스 끝 모양(커플러 머리)")] public Transform hoseTip;
    public float maxHoseLength = 8f;

    private readonly SyncVar<bool> _valveOpen = new SyncVar<bool>();
    private readonly SyncVar<NetworkObject> _connected = new SyncVar<NetworkObject>();

    public bool ValveOpen => _valveOpen.Value;
    public NetworkObject Connected => _connected.Value;

    private LineRenderer _line;
    private ParticleSystem _blast;
    private AudioSource _hiss;
    private Vector3 _freeEnd;
    private Vector3 _freeVel;
    private float _nextToggle;
    private Quaternion _valveClosedRot;
    private Collider _valveCol;
    private PlatformType? _plat;
    private PlatformType Platform => _plat ??= Utils.GetPlatformType();

    /// <summary>연결 안 된 호스 끝의 현재 위치(세계).</summary>
    public Vector3 FreeEnd => _freeEnd;

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    private void Start()
    {
        if (coupler == null) coupler = transform;
        if (valve != null) { _valveClosedRot = valve.localRotation; _valveCol = valve.GetComponentInChildren<Collider>(); }
        _freeEnd = RestEnd();

        _line = gameObject.AddComponent<LineRenderer>();
        _line.positionCount = 24;
        _line.widthMultiplier = 0.018f;
        _line.numCapVertices = 2;
        _line.useWorldSpace = true;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var lib = EquipFx.Lib;
        if (lib != null && lib.hose != null) _line.sharedMaterial = lib.hose;
        _line.startColor = _line.endColor = new Color(0.85f, 0.15f, 0.1f);

        if (lib != null && lib.dust != null)
        {
            _blast = EquipFx.MakeParticles(transform, "[AirBlast]", new EquipFx.PsSettings
            {
                mat = lib.dust, color = new Color(0.9f, 0.9f, 0.9f, 0.35f), lifetime = 0.5f, speed = 7f,
                size = 0.06f, sizeEnd = 5f, gravity = 0f, rate = 0f, coneAngle = 14f, radius = 0.005f, max = 300, drag = 3f
            });
            _blast.Play();
        }
        _hiss = EquipFx.LoopSource(gameObject, EquipFx.Hiss, 14f);
    }

    private Vector3 RestEnd()
    {
        // 컴프레서 옆 바닥에 걸쳐 둔 자리
        Vector3 side = transform.right * 0.45f + transform.forward * 0.15f;
        Vector3 p = coupler.position + side; p.y = transform.position.y + 0.05f;
        return p;
    }

    // ── 밸브 ─────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void ToggleValveServerRpc() { _valveOpen.Value = !_valveOpen.Value; }

    [ServerRpc(RequireOwnership = false)]
    public void ConnectServerRpc(NetworkObject tool)
    {
        if (tool == null || _connected.Value != null) return;
        foreach (var c in All) if (c != this && c._connected.Value == tool) return;   // 한 공구엔 호스 하나
        _connected.Value = tool;
    }

    [ServerRpc(RequireOwnership = false)]
    public void DisconnectServerRpc() { _connected.Value = null; }

    private void RequestToggle()
    {
        if (Time.unscaledTime < _nextToggle) return;
        _nextToggle = Time.unscaledTime + 0.8f;
        ToggleValveServerRpc();
        if (valve != null) AudioSource.PlayClipAtPoint(EquipFx.Click, valve.position, 0.8f);
    }

    private void PollValveInput()
    {
        if (valve == null) return;
        if (Platform == PlatformType.VR)
        {
            foreach (var h in LocalHands.All)
            {
                var tip = LocalHands.IndexTip(h);
                if (tip == null || h.holdingObj != null) continue;
                if ((tip.position - valve.position).sqrMagnitude < 0.07f * 0.07f)
                {
                    if (Time.unscaledTime >= _nextToggle) EquipFx.Haptic(h, 0.05f, 0.6f);
                    RequestToggle();
                }
            }
            return;
        }
        var cam = Camera.main;
        if (cam == null || _valveCol == null) return;
        bool press = Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0);
        if (!press) return;
        Ray ray = Platform == PlatformType.Mobile && Input.touchCount > 0
            ? cam.ScreenPointToRay(Input.GetTouch(0).position)
            : (Input.GetKeyDown(KeyCode.E) ? new Ray(cam.transform.position, cam.transform.forward) : cam.ScreenPointToRay(Input.mousePosition));
        if (_valveCol.Raycast(ray, out _, 2.5f)) RequestToggle();
    }

    // ── 매 프레임 ─────────────────────────────

    private void Update()
    {
        PollValveInput();

        // 밸브 손잡이 모양(열림 = 90° 돌아감)
        if (valve != null)
            valve.localRotation = Quaternion.Slerp(valve.localRotation,
                _valveClosedRot * Quaternion.Euler(0f, 0f, _valveOpen.Value ? 90f : 0f), Time.deltaTime * 12f);

        Transform inlet = ConnectedInlet();
        bool free = inlet == null;
        bool blasting = free && _valveOpen.Value;

        // 서버: 너무 멀리 끌고 가면 빠진다
        if (IsServerStarted && !free && (inlet.position - coupler.position).magnitude > maxHoseLength)
        {
            _connected.Value = null;
        }

        Vector3 end;
        Vector3 endDir;
        if (!free)
        {
            end = inlet.position;
            endDir = -inlet.forward;
            _freeEnd = end;
            _freeVel = Vector3.zero;
        }
        else
        {
            // 풀린 호스 끝: 쉬고 있는 자리로 돌아가거나, 공기가 뿜어지면 요동친다
            Vector3 rest = RestEnd();
            Vector3 target = rest;
            if (blasting)
            {
                float t = Time.time;
                target += new Vector3(Mathf.PerlinNoise(t * 3f, 0.1f) - 0.5f, Mathf.PerlinNoise(0.3f, t * 4f) * 0.8f, Mathf.PerlinNoise(t * 3.5f, 0.7f) - 0.5f) * 0.9f;
            }
            Vector3 acc = (target - _freeEnd) * (blasting ? 60f : 25f) - _freeVel * (blasting ? 4f : 8f);
            _freeVel += acc * Time.deltaTime;
            _freeEnd += _freeVel * Time.deltaTime;
            float floor = transform.position.y + 0.02f;
            if (_freeEnd.y < floor) { _freeEnd.y = floor; if (_freeVel.y < 0f) _freeVel.y *= -0.3f; }
            end = _freeEnd;
            endDir = _freeVel.sqrMagnitude > 0.01f ? -_freeVel.normalized : (end - coupler.position).normalized;
        }

        DrawHose(coupler.position, end);
        if (hoseTip != null)
        {
            hoseTip.gameObject.SetActive(free);
            if (free) hoseTip.SetPositionAndRotation(end, Quaternion.LookRotation(endDir.sqrMagnitude > 1e-4f ? endDir : Vector3.forward));
        }

        // 공기 분출
        if (_blast != null)
        {
            Vector3 outDir = -endDir;
            _blast.transform.SetPositionAndRotation(end, Quaternion.LookRotation(outDir.sqrMagnitude > 1e-4f ? outDir : Vector3.up));
            var em = _blast.emission; em.rateOverTime = blasting ? 220f : 0f;
        }
        EquipFx.Drive(_hiss, blasting ? 0.9f : (_valveOpen.Value ? 0.05f : 0f), blasting ? 1.3f : 0.7f);

        if (blasting) WarnNearby(end);
    }

    private void WarnNearby(Vector3 end)
    {
        var cam = Camera.main;
        if (cam != null && (cam.transform.position - end).sqrMagnitude < 4f * 4f)
            EquipFx.Notice("air_blast", "에어 분출! 부상 위험", "호스가 공구에 연결되지 않았습니다 — 밸브를 잠그거나 공구에 연결하세요", EquipFx.Danger, 3f, 6f);
        foreach (var h in LocalHands.All)
            if (h != null && (h.transform.position - end).sqrMagnitude < 0.5f * 0.5f && Time.frameCount % 5 == 0)
                EquipFx.Haptic(h, 0.06f, 0.8f);
    }

    private Transform ConnectedInlet()
    {
        var nob = _connected.Value;
        if (nob == null) return null;
        var w = nob.GetComponent<AirImpactWrench>();
        return w != null && w.inlet != null ? w.inlet : nob.transform;
    }

    private void DrawHose(Vector3 a, Vector3 b)
    {
        if (_line == null) return;
        float dist = Vector3.Distance(a, b);
        float slack = Mathf.Max(0f, maxHoseLength * 0.35f - dist * 0.3f);
        float sag = Mathf.Clamp(0.15f + slack * 0.25f, 0.1f, 0.9f);
        float floor = transform.position.y + 0.012f;
        Vector3 mid = (a + b) * 0.5f + Vector3.down * sag;
        int n = _line.positionCount;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / (n - 1);
            Vector3 p = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * mid + t * t * b;
            if (p.y < floor) p.y = floor;
            _line.SetPosition(i, p);
        }
    }

    /// <summary>이 공구에 연결된 컴프레서(없으면 null).</summary>
    public static AirCompressor ConnectedTo(NetworkObject tool)
    {
        if (tool == null) return null;
        foreach (var c in All) if (c != null && c._connected.Value == tool) return c;
        return null;
    }
}
