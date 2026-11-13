using System;
using System.Collections;
using System.Collections.Generic;
using Autohand;
using TMPro;
using UnityEngine;

// ============================================================
//  VehicleEngine.cs
//
//  스마트키로 차량 시동 켜기/끄기, 경적.
//  · SmartKeyUse : 스마트키를 들고 PC=F / 모바일=작업 버튼 / VR=트리거를 누르면 가까운 차량의 시동을 켜고 끈다
//  · VehicleEngine : 시동 상태(서버를 거쳐 모두에게 동기화, 늦게 들어온 사람은 소리 없이 현재 상태로)
//      켜짐 → 시동음(전원 켜짐 소리), 전조등 3회 깜박임 후 주간등 유지, 계기판 점등(READY), 낮은 전동 모터 소리
//      꺼짐 → 종료음, 전조등 2회 깜박임 후 소등, 계기판 꺼짐
//  · 경적 : 핸들 중앙(차 안, 운전석)을 클릭 — 모두에게 소리가 난다
//  모델에 계기판 메시가 없어 운전석 앞에 코드로 만든 표시판을 둔다. 위치는 clusterLocalPos / hornLocalPos 로 조정(차량 로컬, 앞 = -X).
// ============================================================

public class SmartKeyUse : MonoBehaviour
{
    private SyncGrab _sync;
    private Grabbable _grab;
    private bool _prevMobile, _mobileShown;
    private XRAirpotrSecurity.PlatformType? _plat;
    private XRAirpotrSecurity.PlatformType Platform => _plat ??= XRAirpotrSecurity.Utils.GetPlatformType();

    private void Awake()
    {
        _sync = GetComponent<SyncGrab>();
        _grab = GetComponent<Grabbable>();
        if (_grab != null) _grab.onSqueeze.AddListener(OnSqueeze);
    }

    private void OnDestroy()
    {
        if (_grab != null) _grab.onSqueeze.RemoveListener(OnSqueeze);
        if (_mobileShown) MobileWorkButton.SetVisible(false);
    }

    private bool LocalHeld => _sync != null && _sync.IsOwner && _sync.IsGrabbed && !_sync.IsHolstered;

    private void OnSqueeze(Hand h, Grabbable g)
    {
        if (LocalHeld && Platform == XRAirpotrSecurity.PlatformType.VR) Press(h);
    }

    private void Update()
    {
        bool held = LocalHeld;
        if (Platform == XRAirpotrSecurity.PlatformType.Mobile && held != _mobileShown) { _mobileShown = held; MobileWorkButton.SetVisible(held, "시동 켜기/끄기", "누르기"); }
        if (!held) { _prevMobile = false; return; }

        if (Platform == XRAirpotrSecurity.PlatformType.PC)
        {
            if (Input.GetKeyDown(KeyCode.F)) Press(null);
            EquipFx.Notice("smartkey_hint", "스마트키", "F 를 누르면 차량 시동을 켜고 끕니다 (차량 가까이에서)", EquipFx.Info, 3f, 40f);
        }
        else if (Platform == XRAirpotrSecurity.PlatformType.Mobile)
        {
            bool now = MobileWorkButton.IsHeld;
            if (now && !_prevMobile) Press(null);
            _prevMobile = now;
            EquipFx.Notice("smartkey_hint", "스마트키", "작업 버튼을 누르면 차량 시동을 켜고 끕니다 (차량 가까이에서)", EquipFx.Info, 3f, 40f);
        }
        else EquipFx.Notice("smartkey_hint", "스마트키", "트리거를 쥐면 차량 시동을 켜고 끕니다 (차량 가까이에서)", EquipFx.Info, 3f, 40f);
    }

    private void Press(Hand h)
    {
        var eng = VehicleEngine.Nearest(transform.position, 6f);
        if (eng == null)
        {
            EquipFx.Notice("smartkey_far", "스마트키", "차량에서 너무 멉니다 — 가까이 가서 누르세요", EquipFx.Warn, 2f, 3f);
            return;
        }
        AudioSource.PlayClipAtPoint(EquipFx.Click, transform.position, 0.7f);
        EquipFx.Haptic(h, 0.08f, 0.7f);
        eng.RequestToggle();
    }
}

public class VehicleEngine : MonoBehaviour, IVehicleState
{
    [Header("차량 로컬 좌표 (앞 = -X, 오른쪽 = +Z, 운전석 = -Z)")]
    public Vector3 clusterLocalPos = new Vector3(-0.884f, 1.085f, -0.358f);
    public Vector2 clusterSize = new Vector2(0.235f, 0.078f);
    public Vector3 hornLocalPos = new Vector3(-0.70f, 1.03f, -0.375f);
    public Vector3[] headlightLocalPos = { new Vector3(-2.34f, 0.79f, -0.6f), new Vector3(-2.34f, 0.79f, 0.6f) };

    private const string EngineId = "veh/engine";
    private const string HornId = "veh/horn";

    public static readonly List<VehicleEngine> All = new List<VehicleEngine>();
    public bool IsOn { get; private set; }

    private Transform _root;
    private AudioSource _audio, _hum;
    private AudioClip _startClip, _stopClip, _humClip, _hornClip;
    private GameObject _cluster;
    private Material _lightMat; private Color _lightBase;
    private Light[] _lights = new Light[0];
    private float _lightLevel, _lightTarget;      // 0 소등 ~ 1 최대
    private float _regAt;
    private Coroutine _blink;
    private int _hornCounter;

    private class HornState : IVehicleState { public VehicleEngine o; public string StateId => HornId; public void ApplyState(float v) { o.OnHornState(v); } }
    private HornState _hState;

    // 이 컴포넌트 자체가 시동 상태(IVehicleState)이고, 경적은 내부 객체로 따로 등록한다
    public string StateId => EngineId;
    public void ApplyState(float v) { OnEngineState(v); }

    public static VehicleEngine Nearest(Vector3 p, float maxDist)
    {
        VehicleEngine best = null; float bd = maxDist * maxDist;
        foreach (var e in All)
        {
            if (e == null) continue;
            // 차량 경계(루트 박스는 지붕 부분만이라 쓸 수 없다) 대신 차량 중심까지 거리
            float d = (e._root.TransformPoint(new Vector3(0f, 0.8f, 0f)) - p).sqrMagnitude;
            if (d < bd) { bd = d; best = e; }
        }
        return best;
    }

    public void Build(Transform vehicle, int layer)
    {
        _root = vehicle;
        _regAt = Time.unscaledTime;

        _audio = vehicle.gameObject.AddComponent<AudioSource>();
        _audio.spatialBlend = 1f; _audio.minDistance = 2f; _audio.maxDistance = 30f; _audio.playOnAwake = false; _audio.dopplerLevel = 0f;
        _startClip = SynthStart(); _stopClip = SynthStop(); _hornClip = SynthHorn(); _humClip = SynthHum();
        var humGo = new GameObject("EngineHum"); humGo.transform.SetParent(vehicle, false);
        humGo.transform.localPosition = new Vector3(-1.5f, 0.6f, 0f);
        _hum = humGo.AddComponent<AudioSource>();
        _hum.clip = _humClip; _hum.loop = true; _hum.spatialBlend = 1f; _hum.minDistance = 1.5f; _hum.maxDistance = 10f; _hum.volume = 0f; _hum.playOnAwake = false;

        // 전조등: 모델의 라이트 스트립(SM_Ioniq_Bumper_Lights)의 이미션 + 실제 스포트라이트 두 개
        foreach (var r in vehicle.GetComponentsInChildren<Renderer>(false))
        {
            if (r.name != "SM_Ioniq_Bumper_Lights" || !r.gameObject.activeInHierarchy) continue;
            _lightMat = r.material;     // 이 차량만 쓰도록 인스턴스화
            _lightMat.EnableKeyword("_EMISSION");
            _lightBase = _lightMat.HasProperty("_EmissionColor") ? _lightMat.GetColor("_EmissionColor") : Color.black;
            break;
        }
        var lights = new List<Light>();
        foreach (var lp in headlightLocalPos)
        {
            var go = new GameObject("Headlight"); go.transform.SetParent(vehicle, false);
            go.transform.localPosition = lp;
            go.transform.localRotation = Quaternion.LookRotation(Vector3.left, Vector3.up);   // 차량 앞(-X)
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot; l.range = 8f; l.spotAngle = 75f; l.intensity = 4f; l.color = new Color(1f, 0.78f, 0.38f);   // 노란 계열(할로겐 느낌)
            l.shadows = LightShadows.None; l.enabled = false;
            lights.Add(l);
        }
        _lights = lights.ToArray();

        BuildCluster(vehicle);
        BuildHorn(vehicle, layer);

        All.Add(this);
        VehicleStateSync.Register(this);                // 시동
        _hState = new HornState { o = this };
        VehicleStateSync.Register(_hState);             // 경적
    }

    private void OnDestroy()
    {
        All.Remove(this);
        VehicleStateSync.Unregister(this);
        VehicleStateSync.Unregister(_hState);
        if (_lightMat != null) Destroy(_lightMat);
    }

    // ── 입력 → 서버 ─────────────────────────────

    public void RequestToggle() { VehicleStateSync.Request(EngineId, IsOn ? 0f : 1f); }

    private void RequestHorn()
    {
        _hornCounter = (_hornCounter + 1) % 1000;
        VehicleStateSync.Request(HornId, _hornCounter + 1f);   // 값이 바뀌는 것 자체가 신호
    }

    // ── 수신(모든 화면) ─────────────────────────────

    private void OnEngineState(float v)
    {
        bool on = v >= 0.5f;
        bool silent = Time.unscaledTime - _regAt < 1.2f;      // 늦게 들어온 사람은 소리·깜박임 없이 현재 상태로
        if (on == IsOn && !silent) return;
        IsOn = on;
        if (_cluster != null) _cluster.SetActive(on);
        _hum.volume = on ? 0.12f : 0f;
        if (on) { if (!_hum.isPlaying) _hum.Play(); } else _hum.Stop();
        if (silent)
        {
            _lightTarget = on ? 0.35f : 0f; _lightLevel = _lightTarget; ApplyLights();
            return;
        }
        _audio.PlayOneShot(on ? _startClip : _stopClip, 1f);
        if (_blink != null) StopCoroutine(_blink);
        _blink = StartCoroutine(Blink(on ? 3 : 2, on ? 0.35f : 0f));
    }

    private void OnHornState(float v)
    {
        if (Time.unscaledTime - _regAt < 1.2f) return;      // 접속 직후 재생되는 옛 경적은 무시
        if (_audio != null) _audio.PlayOneShot(_hornClip, 1f);
        if (_blink == null) _blink = StartCoroutine(Blink(1, IsOn ? 0.35f : 0f));
    }

    private IEnumerator Blink(int times, float finalLevel)
    {
        for (int i = 0; i < times; i++)
        {
            _lightTarget = 1f; yield return new WaitForSeconds(0.22f);
            _lightTarget = 0f; yield return new WaitForSeconds(0.2f);
        }
        _lightTarget = finalLevel;
        _blink = null;
    }

    private void Update()
    {
        if (!Mathf.Approximately(_lightLevel, _lightTarget))
        {
            _lightLevel = Mathf.MoveTowards(_lightLevel, _lightTarget, Time.deltaTime * 12f);
            ApplyLights();
        }
    }

    private void ApplyLights()
    {
        if (_lightMat != null && _lightMat.HasProperty("_EmissionColor"))
            _lightMat.SetColor("_EmissionColor", _lightBase + new Color(1f, 0.72f, 0.28f) * (_lightLevel * 3f));
        foreach (var l in _lights) { if (l == null) continue; l.enabled = _lightLevel > 0.02f; l.intensity = 4f * _lightLevel; }
    }

    // ── 계기판 · 경적 부품 ─────────────────────────────

    private void BuildCluster(Transform vehicle)
    {
        Material baseMat = null;
        foreach (var r in vehicle.GetComponentsInChildren<Renderer>(false))
            if (r.name == "SM_Ioniq_Cockpit" && r.gameObject.activeInHierarchy) { baseMat = r.sharedMaterial; break; }

        _cluster = new GameObject("Cluster");
        _cluster.transform.SetParent(vehicle, false);
        _cluster.transform.localPosition = clusterLocalPos;
        // 운전자(뒤쪽 +X)를 향하게, 위쪽으로 살짝 기울인다
        _cluster.transform.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.up);

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Panel";
        var qc = quad.GetComponent<Collider>(); if (qc != null) Destroy(qc);
        quad.transform.SetParent(_cluster.transform, false);
        quad.transform.localScale = new Vector3(clusterSize.x, clusterSize.y, 1f);
        // 쿼드는 -Z 를 앞으로 본다 → 뒤집어 +Z(운전자 쪽)를 향하게
        quad.transform.localRotation = Quaternion.Euler(0, 180f, 0);
        var qr = quad.GetComponent<MeshRenderer>();
        if (baseMat != null)
        {
            var m = new Material(baseMat);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.02f, 0.03f, 0.05f, 1f));
            m.EnableKeyword("_EMISSION");
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", new Color(0.02f, 0.06f, 0.09f));
            qr.sharedMaterial = m;
        }
        qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        try
        {
            var tgo = new GameObject("Text");
            tgo.transform.SetParent(_cluster.transform, false);
            tgo.transform.localPosition = new Vector3(0, 0, 0.002f);
            tgo.transform.localRotation = Quaternion.Euler(0, 180f, 0);   // 패널과 같이 운전자 쪽을 보게
            var t = tgo.AddComponent<TextMeshPro>();
            t.rectTransform.sizeDelta = clusterSize;
            t.enableAutoSizing = true; t.fontSizeMin = 0.1f; t.fontSizeMax = 2f;
            t.alignment = TextAlignmentOptions.Center;
            t.richText = true;
            t.color = new Color(0.4f, 1f, 0.7f);
            t.text = "<size=140%><b>READY</b></size>\n0 km/h   <color=#7fd4ff>SOC 68%</color>   D";
            t.textWrappingMode = TextWrappingModes.NoWrap;
        }
        catch (Exception e) { Debug.LogWarning("[VehicleEngine] 계기판 글자 생성 실패: " + e.Message); }
        _cluster.SetActive(false);
    }

    private void BuildHorn(Transform vehicle, int layer)
    {
        var go = new GameObject("Horn_Hotspot");
        go.transform.SetParent(vehicle, false);
        go.transform.localPosition = hornLocalPos;
        go.layer = layer;
        var bc = go.AddComponent<BoxCollider>();
        bc.size = new Vector3(0.12f, 0.16f, 0.16f);
        var p = go.AddComponent<ClickProxy>();
        p.displayName = "핸들 (경적)";
        p.hover = touch => (touch ? "터치하여 " : "클릭하여 ") + "경적 울리기";
        p.onClick = RequestHorn;
    }

    // ── 합성 소리 ─────────────────────────────

    private const int Rate = 44100;

    private static AudioClip Synth(string name, float seconds, Func<float, float> f, bool loop = false)
    {
        int len = (int)(Rate * seconds);
        var d = new float[len];
        for (int i = 0; i < len; i++) d[i] = Mathf.Clamp(f((float)i / Rate), -1f, 1f);
        var c = AudioClip.Create(name, len, 1, Rate, false);
        c.SetData(d, 0);
        return c;
    }

    private static float Note(float t, float t0, float hz, float decay)
    {
        if (t < t0) return 0f;
        float x = t - t0;
        return (Mathf.Sin(2f * Mathf.PI * hz * x) + 0.35f * Mathf.Sin(2f * Mathf.PI * hz * 2f * x)) * Mathf.Exp(-x * decay) * 0.22f * Mathf.Min(1f, x * 200f);
    }

    /// <summary>전원 켜짐: 릴레이 딸깍 → 상승 전동음 → 3음 차임.</summary>
    private static AudioClip SynthStart()
    {
        return Synth("VehStart", 1.6f, t =>
        {
            float s = 0f;
            s += t < 0.03f ? (UnityEngine.Random.value * 2f - 1f) * Mathf.Exp(-t * 120f) * 0.6f : 0f;
            if (t > 0.05f && t < 0.6f)
            {
                float u = (t - 0.05f) / 0.55f;
                float hz = 120f + 780f * u * u;
                s += Mathf.Sin(2f * Mathf.PI * hz * t) * 0.18f * Mathf.Sin(u * Mathf.PI);
            }
            s += Note(t, 0.62f, 659f, 5f) + Note(t, 0.82f, 830f, 5f) + Note(t, 1.02f, 988f, 4f);
            return s;
        });
    }

    /// <summary>전원 꺼짐: 하강 전동음 → 딸깍.</summary>
    private static AudioClip SynthStop()
    {
        return Synth("VehStop", 1.0f, t =>
        {
            float s = 0f;
            if (t < 0.6f)
            {
                float u = t / 0.6f;
                float hz = 760f - 640f * u;
                s += Mathf.Sin(2f * Mathf.PI * hz * t) * 0.18f * (1f - u);
            }
            s += Note(t, 0.0f, 784f, 9f) * 0.6f + Note(t, 0.12f, 523f, 7f) * 0.6f;
            s += (t > 0.62f && t < 0.65f) ? (UnityEngine.Random.value * 2f - 1f) * 0.5f : 0f;
            return s;
        });
    }

    /// <summary>경적: 두 음(400/500Hz) 약간 거친 소리.</summary>
    private static AudioClip SynthHorn()
    {
        return Synth("VehHorn", 0.55f, t =>
        {
            float env = Mathf.Min(1f, t * 60f) * Mathf.Min(1f, (0.55f - t) * 30f);
            float a = Mathf.Sin(2f * Mathf.PI * 400f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 800f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * 1200f * t);
            float b = Mathf.Sin(2f * Mathf.PI * 500f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 1000f * t);
            return (a + b) * 0.2f * env;
        });
    }

    /// <summary>전동 모터 대기음(반복): 정수 주기라 이음매 없음.</summary>
    private static AudioClip SynthHum()
    {
        return Synth("VehHum", 1f, t => Mathf.Sin(2f * Mathf.PI * 95f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 190f * t) * 0.25f + Mathf.Sin(2f * Mathf.PI * 1140f * t) * 0.05f, true);
    }
}
