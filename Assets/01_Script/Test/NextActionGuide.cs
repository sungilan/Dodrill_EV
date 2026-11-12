using System.Collections.Generic;
using Autohand;
using FishNet;
using UnityEngine;
using XRAirpotrSecurity;

// ============================================================
//  NextActionGuide.cs
//
//  '지금 해야 할 행동 하나'만 짚어 주는 안내. GuideSystem 위에 얹혀 동작한다(GuideSystem.Awake 에서 자동 추가).
//
//  기존 가이드는 단계가 시작되면 도구 마커·목표 마커·아웃라인을 한꺼번에 띄워서,
//  도구를 아직 안 들었는데 나침반 화살표는 목표 지점을 가리키는 식으로 무엇부터 할지 헷갈렸다.
//
//  · 필요한 도구를 아직 안 들었으면 → 그 도구(가장 가까운 것)만 가리키고, 목표 지점 마커는 잠시 숨긴다.
//  · 도구를 들었으면 → 목표. 볼트 작업이면 남은 볼트 중 가장 가까운 것에 링 + "볼트 n개 남음".
//    목표 마커가 없는 지점(커넥터·레버 등)은 링과 함께 할 일을 짧게 적은 라벨을 띄운다.
//  · 나침반(TaskDirectionCompass)도 이 대상을 따라간다.
//  · 볼트 하나·도구 집기 완료 시 짧은 '틱', 단계 완료 시 차임.
//  · 같은 상태로 오래(25초) 멈춰 있으면 단계 안내 패널을 한 번 다시 띄운다.
//  평가 모드(힌트 미사용)·Hard 난이도에서는 안내를 띄우지 않는다(완료 차임만).
// ============================================================
[DefaultExecutionOrder(100)]
public class NextActionGuide : MonoBehaviour
{
    // 남은 시간이 얼마 없을 때(TaskTimeWarning) 링을 크게·빠르게 깜빡이고, 꺼져 있던 링·라벨도 잠시 켠다
    private static float s_emphasizeUntil;
    public static void Emphasize(float seconds) => s_emphasizeUntil = Mathf.Max(s_emphasizeUntil, Time.unscaledTime + seconds);
    private static bool Emphasized => Time.unscaledTime < s_emphasizeUntil;

    private const float EvalInterval = 0.2f;
    private const float StuckTime = 25f;
    private static readonly Color ItemColor = new Color(0.2f, 0.9f, 1f);
    private static readonly Color TargetColor = new Color(1f, 0.85f, 0.1f);

    /// <summary>지금 가리키는 대상(나침반이 쓴다). 없으면 null.</summary>
    /// <summary>지금 안내 중인 볼트 묶음(없으면 null) — 엑스레이 모드가 남은 볼트를 비춘다.</summary>
    public BoltGroupCounter FocusGroup => _group;

    public Transform FocusTransform => _focus != null && _focus.gameObject.activeInHierarchy ? _focus : null;

    private Transform _focus;
    private Renderer[] _focusRenderers;
    private bool _itemStep;
    private bool _showLabel, _showRing;
    private string _title, _action;
    private float _ringRadius;
    private Color _color;

    private float _nextEval;
    private int _taskIdx = -1;
    private TaskStatus _taskStatus;
    private BoltGroupCounter _group;
    private int _groupRemoved = -1;
    private bool _wasItemStep;
    private string _stuckKey;
    private float _stuckSince;
    private bool _stuckFired;
    private float _labelPulseUntil;

    private GameObject _labelGo;
    private WorldHoverLabel _label;
    private string _labelText;
    private LineRenderer _ring;
    private AudioSource _audio;
    private AudioClip _tick, _chime;
    private readonly Dictionary<string, string> _names = new Dictionary<string, string>();
    private static GameObject s_labelPrefab;

    private void OnEnable() => ScenarioStateReceiver.OnTaskStateUpdated += OnTaskState;

    private void OnDisable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated -= OnTaskState;
        SetFocus(null);
        HideVisuals();
    }

    private void OnDestroy()
    {
        if (_labelGo != null) Destroy(_labelGo);
        if (_ring != null) Destroy(_ring.gameObject);
    }

    private void OnTaskState(TaskStateBroadcast b)
    {
        var t = b.currentTask;
        bool newlyDone = t.status == TaskStatus.Completed && !(t.taskIndex == _taskIdx && _taskStatus == TaskStatus.Completed);
        if (newlyDone) Play(_chime != null ? _chime : (_chime = MakeChime()), 0.5f);
        if (t.taskIndex != _taskIdx) { _group = null; _groupRemoved = -1; _wasItemStep = false; _stuckFired = false; }
        _taskIdx = t.taskIndex;
        _taskStatus = t.status;
        _nextEval = 0f;
    }

    private void Update()
    {
        var gs = GuideSystem.Instance;
        if (gs == null || !gs.IsGuiding)
        {
            SetFocus(null);
            HideVisuals();
            return;
        }

        if (Time.unscaledTime >= _nextEval)
        {
            _nextEval = Time.unscaledTime + EvalInterval;
            Evaluate(gs);
        }

        if (FocusTransform == null) { HideVisuals(); return; }
        DrawVisuals();
    }

    // ── 판단 ─────────────────────────────────────

    private void Evaluate(GuideSystem gs)
    {
        var cfg = gs.CurrentConfig;
        int myId = LocalClientId();
        Vector3 head = HeadPos();

        Transform focus = null;
        bool itemStep = false, label = false, ring = false;
        string title = null, action = null;
        float radius = 0.05f;
        BoltGroupCounter group = null;

        // 1) 필요한 도구를 들고 있는가. 하나라도 내 손에 있으면 도구 단계는 끝난 것으로 본다
        //    (배터리 잭처럼 손에 드는 물건이 아닌 것이 같이 적혀 있는 단계가 있다).
        if (cfg?.requiredItems != null && cfg.requiredItems.Count > 0 && !HoldsAnyRequired(cfg.requiredItems, myId))
        {
            foreach (var id in cfg.requiredItems)
            {
                var nearest = NearestFreeItem(id, head);
                if (nearest == null) continue;
                focus = nearest.transform;
                itemStep = true;
                // 도구 안내는 스폰 때와 같은 빨간 아웃라인 + 이름 라벨 하나로 통일한다(자체 라벨·링은 띄우지 않음).
                // 앞 단계에서 쓰고 내려놓은 도구처럼 마커가 없으면 GuideSystem 이 같은 마커를 붙인다.
                gs.EnsureItemGuide(nearest.gameObject, id);
                label = ring = false;
                break;
            }
        }

        // 2) 도구를 들었거나 필요 없으면 목표
        if (focus == null)
        {
            GameObject tgt = null;
            var targets = gs.ClickTargets;
            if (targets != null)
                foreach (var t in targets)
                    if (t != null && t.activeInHierarchy) { tgt = t; break; }

            if (tgt != null)
            {
                group = tgt.GetComponent<BoltGroupCounter>();
                if (group == null) group = tgt.GetComponentInChildren<BoltGroupCounter>();
            }

            if (group != null)
            {
                var bolt = NearestOpenBolt(group, ToolPos(myId, head));
                if (bolt != null)
                {
                    focus = bolt.transform;
                    int left = Mathf.Max(0, group.TotalCount - group.RemovedCount);
                    title = TaskTitle(cfg);
                    action = group.assembleMode ? $"볼트 {left}개 남음 · 렌치로 조이세요" : $"볼트 {left}개 남음 · 렌치로 푸세요";
                    label = ring = true;
                    radius = Radius(bolt.gameObject, 0.018f, 0.045f);
                }
            }
            else
            {
                var marker = gs.TargetMarker;
                Transform mt = marker != null ? marker.FollowTarget : null;
                if (mt != null)
                {
                    focus = mt;   // 목표 마커(화살표+설명)가 이미 있으니 라벨은 생략
                }
                else if (tgt != null)
                {
                    focus = tgt.transform;
                    title = TaskTitle(cfg);
                    action = ActionVerb(cfg);
                    label = ring = true;
                    radius = Radius(tgt, 0.03f, 0.2f);
                }
            }

            // 대상이 지금 내 손에 든 물건이면(보호구 착용 등) 가리킬 곳이 없다
            if (focus != null && IsInMyHand(focus, myId)) { focus = null; label = ring = false; }
        }

        // 효과음: 볼트 하나 끝 / 도구 집음
        if (group != null && group == _group && group.RemovedCount > _groupRemoved && _groupRemoved >= 0) PlayTick();
        if (_wasItemStep && !itemStep && focus != null) PlayTick();
        _group = group;
        _groupRemoved = group != null ? group.RemovedCount : -1;
        _wasItemStep = itemStep;

        // 도구 단계 동안은 목표 마커를 숨겨 '지금 할 일' 하나만 보이게
        // 측정값 라벨이 떠 있으면 그 단자 위 목표 마커가 값을 가리므로 잠시 숨긴다
        gs.SetTargetMarkerVisible(!itemStep && !VrProbeHandling.ValueLabelShowing);

        SetFocus(focus);
        _itemStep = itemStep;
        _showLabel = label && focus != null;
        _showRing = ring && focus != null;
        _title = title;
        _action = action;
        _ringRadius = radius;
        _color = itemStep ? ItemColor : TargetColor;

        // 막힘 감지: 같은 대상·같은 진행도로 오래 있으면 단계 안내 패널을 한 번 다시 띄운다
        string key = $"{_taskIdx}|{(focus != null ? focus.GetInstanceID() : 0)}|{_groupRemoved}|{itemStep}";
        if (key != _stuckKey) { _stuckKey = key; _stuckSince = Time.unscaledTime; }
        else if (!_stuckFired && Time.unscaledTime - _stuckSince > StuckTime)
        {
            _stuckFired = true;
            gs.ReopenGuideUI();
            _labelPulseUntil = Time.unscaledTime + 3f;
        }
    }

    private void SetFocus(Transform t)
    {
        if (t == _focus) return;
        _focus = t;
        _focusRenderers = t != null ? t.GetComponentsInChildren<Renderer>(false) : null;
    }

    private static bool HoldsAnyRequired(List<string> ids, int myId)
    {
        if (myId < 0) return false;
        foreach (var ti in TaskItem.Registry.Values)
        {
            if (ti == null || !ids.Contains(ti.prefabId)) continue;
            var sg = ti.GetComponent<SyncGrab>();
            if (sg != null && sg.IsGrabbed && sg.Owner != null && sg.Owner.IsValid && sg.Owner.ClientId == myId) return true;
        }
        return false;
    }

    /// <summary>아무도 들고 있지 않은 그 도구 중 가장 가까운 것. 손으로 드는 물건(SyncGrab)이 아니면 제외.</summary>
    private static TaskItem NearestFreeItem(string id, Vector3 from)
    {
        if (string.IsNullOrEmpty(id)) return null;
        TaskItem best = null;
        float bestD = float.MaxValue;
        foreach (var ti in TaskItem.Registry.Values)
        {
            if (ti == null || ti.prefabId != id || !ti.isActiveAndEnabled) continue;
            var sg = ti.GetComponent<SyncGrab>();
            if (sg == null || sg.IsGrabbed) continue;
            var gr = ti.GetComponent<Grabbable>();
            if (gr != null && !gr.isGrabbable) continue;
            float d = (ti.transform.position - from).sqrMagnitude;
            if (d < bestD) { bestD = d; best = ti; }
        }
        return best;
    }

    private static Bolt NearestOpenBolt(BoltGroupCounter group, Vector3 from)
    {
        Bolt best = null;
        float bestD = float.MaxValue;
        foreach (var b in group.GetComponentsInChildren<Bolt>(false))
        {
            if (b == null || b.isBroken || b.IsMarkedDone || b.isloosened) continue;
            float d = (b.transform.position - from).sqrMagnitude;
            if (d < bestD) { bestD = d; best = b; }
        }
        return best;
    }

    private static bool IsInMyHand(Transform t, int myId)
    {
        var held = SyncGrab.FindHeldBy(myId);
        return held != null && (t == held.transform || t.IsChildOf(held.transform));
    }

    private static Vector3 ToolPos(int myId, Vector3 fallback)
    {
        var held = SyncGrab.FindHeldBy(myId);
        return held != null ? held.transform.position : fallback;
    }

    private static int LocalClientId()
    {
        var cm = InstanceFinder.ClientManager;
        return cm != null && cm.Connection != null && cm.Connection.IsValid ? cm.Connection.ClientId : -1;
    }

    private static Vector3 HeadPos()
    {
        var cam = Camera.main;
        return cam != null ? cam.transform.position : Vector3.zero;
    }

    private string DisplayName(GuideSystem gs, string id)
    {
        if (_names.TryGetValue(id, out var n)) return n;
        try { n = gs.GetItemDisplayName(id); } catch { n = null; }
        if (string.IsNullOrWhiteSpace(n) || n.Contains("No translation")) n = HoverNameFormatter.Format(id);
        _names[id] = n;
        return n;
    }

    private static string TaskTitle(ModuleConfig cfg)
    {
        string t = cfg?.GetExtra("title");
        return string.IsNullOrEmpty(t) ? "다음 작업" : t;
    }

    private static string ActionVerb(ModuleConfig cfg)
    {
        // 옮길 물건이 없는 Swap(방전 대기처럼 타이머만 기다리는 단계)에 '가져다 놓으세요'가 뜨던 문제
        if (cfg != null && cfg.actionType == "Swap" && (cfg.requiredItems == null || cfg.requiredItems.Count == 0))
        {
            string t = cfg.GetExtraRaw("title") ?? "";
            return t.Contains("대기") ? "타이머가 끝날 때까지 기다리세요" : "표시된 곳을 확인하세요";
        }
        switch (cfg?.actionType)
        {
            case "MeasureVoltage":
            case "MeasureResistance": return "프로브를 이 단자에 대세요";
            case "ScanDTC": return "진단기를 이곳에 연결하세요";
            case "Swap": return "이곳에 가져다 놓으세요";
            case "ZoneAndUse": return "이곳으로 옮기세요";
        }
        switch (GameScenePlatformManager.CurrentPlatform)
        {
            case PlatformType.VR: return "손으로 조작하세요";
            case PlatformType.Mobile: return "눌러서 작업하세요";
            default: return "클릭해서 작업하세요";
        }
    }

    private static float Radius(GameObject go, float min, float max)
    {
        var rs = go.GetComponentsInChildren<Renderer>(false);
        bool any = false; Bounds b = default;
        foreach (var r in rs)
        {
            if (r == null || !r.enabled || r is LineRenderer || r is ParticleSystemRenderer || r is TrailRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (!any) return min;
        return Mathf.Clamp(b.extents.magnitude * 0.9f, min, max);
    }

    // ── 표시 ─────────────────────────────────────

    private Vector3 FocusCenter(out float top)
    {
        bool any = false; Bounds b = default;
        if (_focusRenderers != null)
            foreach (var r in _focusRenderers)
            {
                if (r == null || !r.enabled || r is LineRenderer || r is ParticleSystemRenderer || r is TrailRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
        if (!any) { top = _focus.position.y; return _focus.position; }
        top = b.max.y;
        return b.center;
    }

    private void DrawVisuals()
    {
        Vector3 c = FocusCenter(out float top);

        // 렌치가 이미 이 볼트를 겨누고 있으면 렌치의 초록 진행 링만 보이게 한다
        var working = ImpactWrench.LocalTargetBolt;
        bool emph = Emphasized;
        bool ringOn = (_showRing || emph) && !(working != null && working.transform == _focus);
        if (ringOn)
        {
            ShowRing(c);   // 깊이 무시(ZTest Always)로 그려 차체에 가려도 보인다
        }
        else if (_ring != null && _ring.gameObject.activeSelf) _ring.gameObject.SetActive(false);

        if (_showLabel || (emph && !string.IsNullOrEmpty(_title)))
        {
            Vector3 lp = FocusCenter(out top);
            lp = new Vector3(lp.x, Mathf.Max(top, lp.y + _ringRadius) + 0.05f, lp.z);
            ShowLabel(lp);
        }
        else if (_labelGo != null && _labelGo.activeSelf) _labelGo.SetActive(false);
    }

    private void HideVisuals()
    {
        if (_ring != null && _ring.gameObject.activeSelf) _ring.gameObject.SetActive(false);
        if (_labelGo != null && _labelGo.activeSelf) _labelGo.SetActive(false);
    }

    private void ShowRing(Vector3 center, float scale = 1f)
    {
        if (_ring == null)
        {
            var go = new GameObject("[NextActionRing]");
            go.layer = 1;
            _ring = go.AddComponent<LineRenderer>();
            _ring.useWorldSpace = true;
            _ring.loop = true;
            _ring.positionCount = 48;
            _ring.numCapVertices = 0;
            _ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ring.receiveShadows = false;
            var sh = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            mat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            mat.renderQueue = 4000;
            _ring.material = mat;
        }
        if (!_ring.gameObject.activeSelf) _ring.gameObject.SetActive(true);

        bool emph = Emphasized;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (emph ? 10f : 5f));
        float r = _ringRadius * scale * (emph ? 1.6f : 1f) * (1f + (emph ? 0.3f : 0.18f) * pulse);
        var col = _color; col.a = Mathf.Lerp(0.55f, 1f, pulse);
        _ring.startColor = _ring.endColor = col;
        _ring.widthMultiplier = Mathf.Clamp(_ringRadius * 0.12f, 0.003f, 0.01f) * scale;

        var cam = Camera.main;
        Vector3 normal = cam != null ? (cam.transform.position - center).normalized : Vector3.up;
        Vector3 u = Vector3.Cross(normal, Vector3.up);
        if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(normal, Vector3.right);
        u.Normalize();
        Vector3 v = Vector3.Cross(normal, u);
        int n = _ring.positionCount;
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.PI * 2f * i / n;
            _ring.SetPosition(i, center + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * r);
        }
    }

    private void ShowLabel(Vector3 pos)
    {
        if (_labelGo == null)
        {
            if (s_labelPrefab == null) s_labelPrefab = Resources.Load<GameObject>("ItemNameTagLabel");
            if (s_labelPrefab == null) return;
            _labelGo = Instantiate(s_labelPrefab);
            _labelGo.name = "[NextActionLabel]";
            foreach (var t in _labelGo.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 1;
            _label = _labelGo.GetComponent<WorldHoverLabel>();
            _labelText = null;
            _labelGo.transform.position = pos;
        }
        if (!_labelGo.activeSelf) { _labelGo.SetActive(true); _labelGo.transform.position = pos; }

        string text = $"{_title}|{_action}";
        if (text != _labelText && _label != null)
        {
            _labelText = text;
            _label.SetText(_title, _action);
        }

        _labelGo.transform.position = Vector3.Lerp(_labelGo.transform.position, pos, Time.unscaledDeltaTime * 12f);

        // 막혀서 다시 알릴 때 잠깐 들썩이게
        if (Time.unscaledTime < _labelPulseUntil)
            _labelGo.transform.position += Vector3.up * (0.015f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 8f)));
    }

    // ── 소리 ─────────────────────────────────────

    private float _nextTick;
    private void PlayTick()
    {
        if (Time.unscaledTime < _nextTick) return;   // 상태가 깜빡여도 틱이 연달아 나지 않게
        _nextTick = Time.unscaledTime + 0.8f;
        Play(_tick != null ? _tick : (_tick = MakeTick()), 0.35f);
    }

    private void Play(AudioClip clip, float volume)
    {
        if (clip == null) return;
        if (_audio == null)
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
        }
        _audio.PlayOneShot(clip, volume);
    }

    private const int Rate = 44100;

    private static AudioClip MakeTick()
    {
        int len = (int)(Rate * 0.09f);
        var d = new float[len];
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / Rate;
            d[i] = Mathf.Sin(2f * Mathf.PI * 1320f * t) * Mathf.Exp(-t * 45f) * 0.8f;
        }
        var c = AudioClip.Create("NextActionTick", len, 1, Rate, false);
        c.SetData(d, 0);
        return c;
    }

    private static AudioClip MakeChime()
    {
        int len = (int)(Rate * 0.55f);
        var d = new float[len];
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / Rate;
            float a = Mathf.Sin(2f * Mathf.PI * 880f * t) * Mathf.Exp(-t * 9f);
            float t2 = t - 0.12f;
            float b = t2 > 0f ? Mathf.Sin(2f * Mathf.PI * 1318.5f * t2) * Mathf.Exp(-t2 * 7f) : 0f;
            float atk = Mathf.Clamp01(t / 0.005f);
            d[i] = (a * 0.5f + b * 0.55f) * atk;
        }
        var c = AudioClip.Create("NextActionChime", len, 1, Rate, false);
        c.SetData(d, 0);
        return c;
    }
}
