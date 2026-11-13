using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ============================================================
//  MeasurementLog.cs  (2026-11-13)
//
//  측정 기록지 — 멀티미터·절연저항계로 잰 값이 자동으로 쌓이고, 기준값과 비교해 합격/불합격을 표시한다.
//  실제 정비 절차의 '측정값 기록'과 같은 역할이며 평가 자료로도 쓸 수 있다.
//  · 기록: MultimeterMaster 가 측정 신호(InteractionEvents.OnValueMeasured)를 보낼 때마다 단자별 최신값으로 갱신.
//          다시 재면 덮어쓰고, 잰 횟수를 센다.
//  · 보기: VR — 손바닥 메뉴 '측정 기록' / PC — J 키 / 모바일 — 화면 오른쪽 위 '측정 기록' 버튼
//  · 기준값(아래 Spec 표)은 정비 매뉴얼 값으로 맞춰 쓴다.
//  · 이 기록은 잰 사람 화면에만 쌓인다(서버 판정은 기존 시나리오 그대로).
//  씬 배치 불필요 — 게임 시작 시 자동 생성.
// ============================================================
public class MeasurementLog : MonoBehaviour
{
    public enum Kind { Voltage, Insulation }

    /// <summary>절연저항 합격 기준(MΩ 이상). 정비 매뉴얼 값으로 맞출 것.</summary>
    public static float InsulationMinMOhm = 2f;
    /// <summary>잔류 전압 합격 기준(V 이하). 시나리오 maxValue 와 같은 30V.</summary>
    public static float VoltageMaxV = 30f;

    private class Spec
    {
        public string label;
        public Kind kind;
        public int order;
        public Spec(string l, Kind k, int o) { label = l; kind = k; order = o; }
    }

    // 기록지에 미리 칸을 만들어 둘 단자(아직 안 쟀으면 '-' 로 보인다)
    private static readonly Dictionary<string, Spec> s_specs = new Dictionary<string, Spec>
    {
        ["HV_Cable_Term"] = new Spec("고전압 케이블 잔류 전압", Kind.Voltage, 0),
        ["HV_Front_Cable_Term"] = new Spec("앞 고전압 케이블 잔류 전압", Kind.Voltage, 1),
        ["HV_Rear_Cable_Term"] = new Spec("뒤 고전압 케이블 잔류 전압", Kind.Voltage, 2),
        ["PRA_BPA_Plus_Terminal"] = new Spec("BPA (+) 절연저항", Kind.Insulation, 10),
        ["PRA_BPA_Minus_Terminal"] = new Spec("BPA (−) 절연저항", Kind.Insulation, 11),
        ["PRA_Inverter_Plus_Terminal"] = new Spec("인버터 (+) 절연저항", Kind.Insulation, 12),
        ["PRA_Inverter_Minus_Terminal"] = new Spec("인버터 (−) 절연저항", Kind.Insulation, 13),
        ["Battery_Main_Terminal"] = new Spec("배터리 메인 단자 절연저항", Kind.Insulation, 14),
        ["Battery_Case"] = new Spec("배터리 케이스 절연저항", Kind.Insulation, 15),
    };

    // 화면에 기본으로 보일 칸(절연저항 4점 + 잔류 전압 앞/뒤)
    private static readonly string[] s_defaultRows =
    {
        "HV_Front_Cable_Term", "HV_Rear_Cable_Term",
        "PRA_BPA_Plus_Terminal", "PRA_BPA_Minus_Terminal", "PRA_Inverter_Plus_Terminal", "PRA_Inverter_Minus_Terminal",
    };

    public class Entry
    {
        public string terminal;
        public string label;
        public Kind kind;
        public int order;
        public float value;
        public bool measured;
        public bool pass;
        public int count;
        public float time;
    }

    private static MeasurementLog s_inst;
    private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
    private readonly List<Entry> _sorted = new List<Entry>();
    private bool _dirty = true;

    public static bool Visible { get; private set; }
    public static int MeasuredCount => s_inst == null ? 0 : s_inst.CountMeasured();
    /// <summary>새 값이 기록될 때(단자, 값, 합격 여부).</summary>
    public static event System.Action<string, float, bool> OnRecorded;

    // VR
    private GameObject _vrRoot;
    private TextMeshPro _vrText;
    private Material _vrBgMat;
    private Vector3 _vrPos;
    private Quaternion _vrRot;
    private bool _vrPlaced;
    private bool _vrFollow;
    // PC·모바일
    private GameObject _canvasGo;
    private GameObject _panel;
    private TextMeshProUGUI _uiText;
    private GameObject _mobileBtn;
    private TMP_FontAsset _font;
    private XRAirpotrSecurity.PlatformType? _plat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (s_inst != null || FindAnyObjectByType<MeasurementLog>() != null) return;
        var go = new GameObject("[MeasurementLog]");
        DontDestroyOnLoad(go);
        s_inst = go.AddComponent<MeasurementLog>();
    }

    private void Awake()
    {
        s_inst = this;
        foreach (var id in s_defaultRows) GetOrAdd(id);
    }

    private void OnEnable() { InteractionEvents.OnValueMeasured += HandleMeasured; }
    private void OnDisable() { InteractionEvents.OnValueMeasured -= HandleMeasured; }

    private void OnDestroy()
    {
        if (s_inst == this) s_inst = null;
        if (_vrRoot != null) Destroy(_vrRoot);
        if (_canvasGo != null) Destroy(_canvasGo);
        if (_vrBgMat != null) Destroy(_vrBgMat);
    }

    public static void Toggle() => SetVisible(!Visible);

    public static void SetVisible(bool on)
    {
        Visible = on;
        if (s_inst != null) { s_inst._vrPlaced = false; s_inst._dirty = true; }
    }

    /// <summary>모든 기록(평가 결과 등에 붙일 때).</summary>
    public static string BuildPlainText()
    {
        if (s_inst == null) return "";
        var sb = new StringBuilder();
        foreach (var e in s_inst.Sorted())
            if (e.measured) sb.AppendLine($"{e.label}: {FormatValue(e)} — {(e.pass ? "합격" : "불합격")} (기준 {Criterion(e.kind)}, {e.count}회)");
        return sb.ToString();
    }

    // ── 기록 ─────────────────────────────

    private void HandleMeasured(string terminal, float value)
    {
        if (string.IsNullOrEmpty(terminal)) return;
        var e = GetOrAdd(terminal);
        if (!s_specs.ContainsKey(terminal)) e.kind = GuessKind(terminal);
        e.value = value;
        e.measured = true;
        e.count++;
        e.time = Time.time;
        e.pass = e.kind == Kind.Voltage ? value <= VoltageMaxV : value >= InsulationMinMOhm;
        _dirty = true;
        Debug.Log($"[MeasureLog] {e.label} = {FormatValue(e)} → {(e.pass ? "합격" : "불합격")}");
        WarningPopup.Show("measure_log", $"기록됨 · {e.label}", $"{FormatValue(e)}  {(e.pass ? "합격" : "불합격")} (기준 {Criterion(e.kind)})",
            e.pass ? new Color(0.08f, 0.35f, 0.18f, 0.92f) : new Color(0.45f, 0.1f, 0.08f, 0.92f), 2.2f, 1);
        OnRecorded?.Invoke(terminal, value, e.pass);
    }

    private Entry GetOrAdd(string terminal)
    {
        if (_entries.TryGetValue(terminal, out var e)) return e;
        e = new Entry { terminal = terminal };
        if (s_specs.TryGetValue(terminal, out var sp)) { e.label = sp.label; e.kind = sp.kind; e.order = sp.order; }
        else { e.label = terminal; e.kind = Kind.Voltage; e.order = 100 + _entries.Count; }
        _entries[terminal] = e;
        _dirty = true;
        return e;
    }

    /// <summary>표에 없는 단자: 그 단자를 잰 측정기의 다이얼 모드로 전압/저항을 정한다.</summary>
    private static Kind GuessKind(string terminal)
    {
        foreach (var m in FindObjectsByType<MultimeterMaster>(FindObjectsSortMode.None))
        {
            if (m == null || m.redProbe == null || m.redProbe.currentTerminalId != terminal) continue;
            return m.currentMode == MultimeterMode.Resistance || m.currentMode == MultimeterMode.Continuity ? Kind.Insulation : Kind.Voltage;
        }
        var mi = MultimeterMaster.Instance;
        if (mi != null && (mi.currentMode == MultimeterMode.Resistance || mi.currentMode == MultimeterMode.Continuity)) return Kind.Insulation;
        return Kind.Voltage;
    }

    private int CountMeasured() { int n = 0; foreach (var e in _entries.Values) if (e.measured) n++; return n; }

    private List<Entry> Sorted()
    {
        _sorted.Clear();
        _sorted.AddRange(_entries.Values);
        _sorted.Sort((a, b) => a.order.CompareTo(b.order));
        return _sorted;
    }

    private static string FormatValue(Entry e)
    {
        if (!e.measured) return "-";
        if (e.kind == Kind.Voltage) return $"{e.value:0.0} V";
        if (e.value >= 9999999f) return "O.L (∞)";
        return e.value >= 100f ? $"{e.value:0} MΩ" : $"{e.value:0.0#} MΩ";
    }

    private static string Criterion(Kind k) => k == Kind.Voltage ? $"{VoltageMaxV:0}V 이하" : $"{InsulationMinMOhm:0.#}MΩ 이상";

    private string BuildRich()
    {
        var sb = new StringBuilder();
        sb.Append("<b>측정 기록지</b>\n<size=70%><color=#9fb3c8>기준값과 비교해 자동 판정 · 다시 재면 갱신</color></size>\n");
        Kind? section = null;
        foreach (var e in Sorted())
        {
            if (section != e.kind)
            {
                section = e.kind;
                sb.Append(e.kind == Kind.Voltage
                    ? $"\n<color=#ffd27a>잔류 전압</color> <size=70%>(기준 {Criterion(Kind.Voltage)})</size>\n"
                    : $"\n<color=#7ad7ff>절연저항</color> <size=70%>(기준 {Criterion(Kind.Insulation)})</size>\n");
            }
            string verdict = !e.measured ? "<color=#808080>미측정</color>"
                : e.pass ? "<color=#5be37d>합격</color>" : "<color=#ff5a4a>불합격</color>";
            sb.Append($"{e.label}  <b>{FormatValue(e)}</b>  {verdict}");
            if (e.count > 1) sb.Append($" <size=70%><color=#9fb3c8>({e.count}회)</color></size>");
            sb.Append('\n');
        }
        return sb.ToString();
    }

    // ── 표시 ─────────────────────────────

    private void Update()
    {
        bool vr = VrProbeHandling.IsVRDevice;
        if (!vr) PollPcMobileInput();

        if (vr) UpdateVr();
        else UpdateScreen();
    }

    private void PollPcMobileInput()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.jKey.wasPressedThisFrame && !TypingInUI()) Toggle();
    }

    private TMP_FontAsset Font()
    {
        if (_font != null) return _font;
        var tagPrefab = Resources.Load<GameObject>("ItemNameTagLabel");
        if (tagPrefab != null)
        {
            var t = tagPrefab.GetComponentInChildren<TMP_Text>(true);
            if (t != null) _font = t.font;
        }
        return _font;
    }

    private void UpdateVr()
    {
        if (!Visible)
        {
            if (_vrRoot != null && _vrRoot.activeSelf) _vrRoot.SetActive(false);
            return;
        }
        var cam = Camera.main;
        if (cam == null) return;
        if (_vrRoot == null) BuildVr();
        if (!_vrRoot.activeSelf) _vrRoot.SetActive(true);
        if (_dirty) { _vrText.text = BuildRich(); _dirty = false; }

        // 시선 왼쪽 아래 0.6m — 고개를 크게 돌렸을 때만 천천히 따라온다(작업 중 시야를 가리지 않게)
        Vector3 f = cam.transform.forward; f.y = 0f;
        if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
        f.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, f);
        Vector3 want = cam.transform.position + f * 0.6f - right * 0.28f + Vector3.down * 0.18f;
        Quaternion wantRot = Quaternion.LookRotation(want - cam.transform.position, Vector3.up);
        if (!_vrPlaced) { _vrPos = want; _vrRot = wantRot; _vrPlaced = true; _vrFollow = false; }
        float off = Vector3.Angle(_vrRot * Vector3.forward, wantRot * Vector3.forward);
        float dist = (want - _vrPos).magnitude;
        if (off > 35f || dist > 0.35f) _vrFollow = true;          // 크게 벗어나면 따라가기 시작
        else if (off < 4f && dist < 0.03f) _vrFollow = false;      // 거의 제자리면 멈춤
        if (_vrFollow)
        {
            float k = 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime);
            _vrPos = Vector3.Lerp(_vrPos, want, k);
            _vrRot = Quaternion.Slerp(_vrRot, wantRot, k);
        }
        _vrRoot.transform.SetPositionAndRotation(_vrPos, _vrRot);
    }

    private void BuildVr()
    {
        _vrRoot = new GameObject("[MeasurementLog_VR]");
        _vrRoot.layer = 1;   // VR 카메라가 그리는 레이어
        DontDestroyOnLoad(_vrRoot);

        var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var col = bg.GetComponent<Collider>();
        if (col != null) { col.enabled = false; Destroy(col); }
        bg.layer = 1;
        bg.transform.SetParent(_vrRoot.transform, false);
        bg.transform.localPosition = new Vector3(0f, 0f, 0.002f);
        bg.transform.localScale = new Vector3(0.36f, 0.3f, 1f);
        var mr = bg.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        _vrBgMat = new Material(Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default"));
        _vrBgMat.renderQueue = 3990;
        _vrBgMat.color = new Color(0.06f, 0.08f, 0.11f, 0.9f);
        mr.sharedMaterial = _vrBgMat;

        var tgo = new GameObject("Text");
        tgo.layer = 1;
        tgo.transform.SetParent(_vrRoot.transform, false);
        _vrText = tgo.AddComponent<TextMeshPro>();
        var font = Font();
        if (font != null) _vrText.font = font;
        _vrText.rectTransform.sizeDelta = new Vector2(0.33f, 0.28f);
        _vrText.fontSize = 0.13f;
        _vrText.enableAutoSizing = true;
        _vrText.fontSizeMin = 0.05f;
        _vrText.fontSizeMax = 0.14f;
        _vrText.alignment = TextAlignmentOptions.TopLeft;
        _vrText.color = Color.white;
        _vrText.richText = true;
        if (_vrText.fontMaterial != null) _vrText.fontMaterial.renderQueue = 4000;
        var tr = tgo.GetComponent<MeshRenderer>();
        if (tr != null) tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private void UpdateScreen()
    {
        if (_canvasGo == null) BuildScreen();
        if (_canvasGo == null) return;

        _plat ??= XRAirpotrSecurity.Utils.GetPlatformType();
        // 로비(Client 씬)에서는 버튼을 숨긴다 — 훈련(Game 씬)에서만 쓴다
        bool inGame = UnityEngine.SceneManagement.SceneManager.GetSceneByName("Game").isLoaded;
        if (!inGame && Visible) { Visible = false; _dirty = true; }
        bool mobile = _plat == XRAirpotrSecurity.PlatformType.Mobile && inGame;
        if (_mobileBtn != null && _mobileBtn.activeSelf != mobile) _mobileBtn.SetActive(mobile);
        if (_panel.activeSelf != Visible) _panel.SetActive(Visible);
        if (Visible && _dirty) { _uiText.text = BuildRich() + (mobile ? "" : "\n<size=70%><color=#9fb3c8>J 키로 닫기</color></size>"); _dirty = false; }
    }

    private void BuildScreen()
    {
        _canvasGo = new GameObject("[MeasurementLog_UI]");
        DontDestroyOnLoad(_canvasGo);
        var canvas = _canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;
        var scaler = _canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _canvasGo.AddComponent<GraphicRaycaster>();

        // 기록지 패널(오른쪽 위)
        _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        _panel.transform.SetParent(_canvasGo.transform, false);
        var prt = (RectTransform)_panel.transform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(1f, 1f);
        prt.anchoredPosition = new Vector2(-24f, -240f);   // 버튼 아래
        prt.sizeDelta = new Vector2(620f, 520f);
        var img = _panel.GetComponent<Image>();
        img.color = new Color(0.06f, 0.08f, 0.11f, 0.9f);
        img.raycastTarget = false;

        var tgo = new GameObject("Text", typeof(RectTransform));
        tgo.transform.SetParent(_panel.transform, false);
        var trt = (RectTransform)tgo.transform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(22f, 18f); trt.offsetMax = new Vector2(-22f, -18f);
        _uiText = tgo.AddComponent<TextMeshProUGUI>();
        var font = Font();
        if (font != null) _uiText.font = font;
        _uiText.fontSize = 28f;
        _uiText.enableAutoSizing = true;
        _uiText.fontSizeMin = 16f;
        _uiText.fontSizeMax = 30f;
        _uiText.alignment = TextAlignmentOptions.TopLeft;
        _uiText.richText = true;
        _uiText.raycastTarget = false;
        _panel.SetActive(false);

        // 모바일 버튼
        _mobileBtn = new GameObject("MobileButton", typeof(RectTransform), typeof(Image), typeof(Button));
        _mobileBtn.transform.SetParent(_canvasGo.transform, false);
        var brt = (RectTransform)_mobileBtn.transform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(1f, 1f);
        brt.anchoredPosition = new Vector2(-24f, -160f);   // 오른쪽 위 메뉴 버튼들 아래로
        brt.sizeDelta = new Vector2(200f, 66f);
        _mobileBtn.GetComponent<Image>().color = new Color(0.1f, 0.2f, 0.32f, 0.92f);
        _mobileBtn.GetComponent<Button>().onClick.AddListener(Toggle);
        var lgo = new GameObject("Label", typeof(RectTransform));
        lgo.transform.SetParent(_mobileBtn.transform, false);
        var lrt = (RectTransform)lgo.transform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        var lt = lgo.AddComponent<TextMeshProUGUI>();
        if (font != null) lt.font = font;
        lt.text = "측정 기록";
        lt.fontSize = 28f;
        lt.alignment = TextAlignmentOptions.Center;
        lt.raycastTarget = false;
        _mobileBtn.SetActive(false);

        // 클릭 이벤트용 EventSystem 이 없으면(드묾) 버튼만 안 눌린다 — 씬 UI 가 이미 가지고 있다
    }

    private static bool TypingInUI()
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        return sel != null && (sel.GetComponent<TMP_InputField>() != null || sel.GetComponent<InputField>() != null);
    }
}
