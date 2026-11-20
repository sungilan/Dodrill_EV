using System.Collections.Generic;
using TMPro;
using UnityEngine;

// ============================================================
//  HvTestTerminals.cs
//
//  전·후방 인버터 단자(HV_Front_Cable_Term / HV_Rear_Cable_Term) 측정 지점을 눈에 띄게 만든다. (11/20 테스트 영상 피드백:
//  "전·후방 인버터 단자 잔류 전압 측정할 때 빨간 프로브를 어디에 꽂는지 못 찾겠다")
//
//  원인: 차 프리팹에는 단자마다 측정 지점(MeasurementPoint) 상자 2개가 4.5cm 간격으로 겹쳐 있고(12cm 상자), 배터리 팩 밑판 속(y≈1.82)에 묻혀 있어
//        겉으로는 작은 노란 고리뿐이었다. 빨강·검정 어느 쪽이 어디인지도 알 수 없었다.
//  하는 일(런타임, 프리팹·서버 변경 없음 — 모든 기종 공통):
//   1) 단자마다 2개를 '빨강(+)' / '검정(−)'으로 나누고, 10cm 아래(밑판 바로 밑 공기 중)로 내려 11cm 간격으로 벌린다. 판정 상자는 7cm 로 줄인다.
//   2) 그 자리에 단자 기둥(빨강/검정) + 주황 지지대 + 후광을 세우고, 맨 앞에 그리는 이름표('빨간 프로브 (+)' / '검정 프로브 (−)')와
//      아래로 뻗은 안내 빛줄기를 띄운다. 해당 측정 단계(HV_Front_Voltage / HV_Rear_Voltage)에서만 보인다.
//      평가 모드에서는 이름표·빛줄기·후광을 숨기고 기둥(실제 단자)만 보인다.
//   3) PC 조준 측정(PcProbeAim)이 역할을 따른다 — 검정(−)을 먼저, 빨강(+)은 빨간 표시에.
//  VR·모바일: 프로브 끝이 상자에 닿으면 측정되는 방식 그대로(역할 강제 없음) — 표시와 위치만 바뀐다.
// ============================================================
public class HvTestTerminals : MonoBehaviour
{
    public enum Role { None, Red, Black }

    private static readonly string[] Ids = { "HV_Front_Cable_Term", "HV_Rear_Cable_Term" };
    private const string FrontStep = "HV_Front_Voltage", RearStep = "HV_Rear_Voltage";
    private const float DropDown = 0.02f;     // (11/20) 0.10 → 0.02: 주황색 커넥터(PRA) 높이에 맞춘다. 원래 단자 높이와 거의 같다
    private const float HalfGap = 0.055f;     // 빨강·검정 사이 간격의 절반
    private const float BoxSize = 0.07f;      // 판정 상자 한 변(m)

    private class Term
    {
        public MeasurementPoint mp;
        public string id;
        public Role role;
        public GameObject root;
        public Transform halo, label;
        public GameObject[] guideOnly;   // 평가 모드에서 숨기는 것들(후광·이름표·빛줄기)
        public Vector3 outward;          // 이름표를 놓을 쪽(세계 방향, 수평)
    }

    private static HvTestTerminals s_inst;
    private readonly Dictionary<int, Term> _terms = new Dictionary<int, Term>();
    private float _nextScan;
    private static Material s_unlit;
    private static readonly HashSet<int> s_arranged = new HashSet<int>();   // 이미 옮긴 측정 지점(구성요소가 다시 만들어져도 두 번 내리지 않게)

    /// <summary>지금 측정 안내 표시가 켜져 있는가(PcProbeAim 의 안내 문구가 확인).</summary>
    public static bool GuideActive { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (s_inst != null || Application.isBatchMode) return;
        var go = new GameObject("[HvTestTerminals]");
        DontDestroyOnLoad(go);
        s_inst = go.AddComponent<HvTestTerminals>();
    }

    private void Awake() { if (s_inst == null) s_inst = this; }

    /// <summary>이 측정 지점이 빨강/검정 어느 쪽 단자인가(안내가 만든 단자가 아니면 None).</summary>
    public static Role RoleOf(MeasurementPoint mp)
    {
        if (s_inst == null || mp == null) return Role.None;
        return s_inst._terms.TryGetValue(mp.GetInstanceID(), out var t) ? t.role : Role.None;
    }

    private void Update()
    {
        if (Time.unscaledTime >= _nextScan) { _nextScan = Time.unscaledTime + 1f; Scan(); }
        if (_terms.Count == 0) { GuideActive = false; return; }

        string module = CurrentModule();
        bool eval = EvaluationMode.ClientIsEvaluation;
        var cam = Camera.main;
        bool any = false;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f);
        foreach (var t in _terms.Values)
        {
            if (t.root == null) continue;
            bool show = ShouldShow(t.id, module);
            if (t.root.activeSelf != show) t.root.SetActive(show);
            if (!show) continue;
            if (t.guideOnly != null)
                foreach (var g in t.guideOnly) if (g != null && g.activeSelf == eval) g.SetActive(!eval);
            if (eval) continue;
            any = true;
            if (t.halo != null) t.halo.localScale = Vector3.one * (0.125f + 0.025f * pulse);
            if (t.label != null && cam != null)
            {
                Vector3 look = t.label.position - cam.transform.position;
                if (look.sqrMagnitude > 1e-6f) t.label.rotation = Quaternion.LookRotation(look, Vector3.up);
            }
        }
        GuideActive = any;
    }

    /// <summary>점검용 — true 면 측정 단계가 아니어도 단자 안내를 항상 보인다(에디터에서 모양 확인).</summary>
    public static bool DebugShowAll;

    private static bool ShouldShow(string id, string module)
    {
        if (DebugShowAll) return true;
        if (id == Ids[0] && module == FrontStep) return true;
        if (id == Ids[1] && module == RearStep) return true;
        // 자유 탈거: 서비스 플러그를 뽑은 뒤 단자 전압을 아직 확인하지 않았다면 앞쪽 단자를 보여 준다
        if (id == Ids[0] && FreeLoto.IsFreeScenario)
        {
            var sm = ElectricSafetyManager.Instance;
            return sm != null && sm.isMSDRemoved && !HvVoltageCheck.Verified;
        }
        return false;
    }

    /// <summary>지금 진행 중인 단계의 moduleId (시나리오 정보가 없거나 진행 중이 아니면 null).</summary>
    private static string CurrentModule()
    {
        var r = ScenarioStateReceiver.Instance;
        if (r == null) return null;
        var sc = r.CurrentScenario;
        if (sc == null || sc.scenario == null || sc.scenario.tasks == null) return null;
        var st = r.CurrentTaskState;
        if (st.status != TaskStatus.Running) return null;
        int i = st.taskIndex;
        if (i < 0 || i >= sc.scenario.tasks.Count || sc.scenario.tasks[i] == null) return null;
        return sc.scenario.tasks[i].moduleId;
    }

    // ── 측정 지점 찾기·재배치 ─────────────────────────────

    private void Scan()
    {
        var dead = new List<int>();
        foreach (var kv in _terms) if (kv.Value.mp == null) dead.Add(kv.Key);
        foreach (var k in dead) { if (_terms[k].root != null) Destroy(_terms[k].root); _terms.Remove(k); }

        var groups = new Dictionary<string, List<MeasurementPoint>>();
        foreach (var mp in FindObjectsByType<MeasurementPoint>(FindObjectsSortMode.None))
        {
            if (mp == null || !mp.isActiveAndEnabled || System.Array.IndexOf(Ids, mp.terminalId) < 0) continue;
            if (!groups.TryGetValue(mp.terminalId, out var l)) groups[mp.terminalId] = l = new List<MeasurementPoint>();
            l.Add(mp);
        }
        foreach (var kv in groups)
        {
            var l = kv.Value;
            if (l.Count != 2) continue;   // 한 쌍이 아니면 건드리지 않는다
            if (_terms.ContainsKey(l[0].GetInstanceID()) && _terms.ContainsKey(l[1].GetInstanceID())) continue;
            if (s_arranged.Contains(l[0].GetInstanceID()) || s_arranged.Contains(l[1].GetInstanceID())) continue;
            Arrange(l[0], l[1]);
        }
    }

    private void Arrange(MeasurementPoint a, MeasurementPoint b)
    {
        // 부모(차) 기준 로컬 z 가 큰 쪽 = 빨강(+). 고정 규칙이라 모든 클라이언트가 같은 쪽을 빨강으로 본다
        float za = a.transform.localPosition.z, zb = b.transform.localPosition.z;
        bool aRed = !Mathf.Approximately(za, zb) ? za > zb : a.GetInstanceID() > b.GetInstanceID();
        var red = aRed ? a : b; var black = aRed ? b : a;

        Vector3 center = (a.transform.position + b.transform.position) * 0.5f;
        Vector3 axis = red.transform.position - black.transform.position; axis.y = 0f;
        if (axis.sqrMagnitude < 1e-6f) { var par = red.transform.parent; axis = par != null ? par.forward : Vector3.forward; axis.y = 0f; }
        axis.Normalize();

        Place(red, center + axis * HalfGap + Vector3.down * DropDown, Role.Red, axis);
        Place(black, center - axis * HalfGap + Vector3.down * DropDown, Role.Black, -axis);
    }

    private void Place(MeasurementPoint mp, Vector3 worldPos, Role role, Vector3 outward)
    {
        mp.transform.position = worldPos;
        s_arranged.Add(mp.GetInstanceID());
        var bc = mp.GetComponent<BoxCollider>();
        if (bc != null) { bc.size = Vector3.one * BoxSize; bc.center = Vector3.zero; }
        var term = new Term { mp = mp, id = mp.terminalId, role = role, outward = outward };
        BuildVisual(term);
        _terms[mp.GetInstanceID()] = term;
    }

    // ── 보이는 부분 ─────────────────────────────

    private static Material Unlit(Color c, bool onTop, int queue = 4000)
    {
        var sh = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
        var m = new Material(sh) { color = c };
        if (onTop) { m.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always); m.renderQueue = queue; }
        else m.renderQueue = 3000;
        return m;
    }

    private static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material m)
    {
        var go = GameObject.CreatePrimitive(type);
        var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);   // 조준 레이·프로브가 이 모형에 막히지 않게
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return go;
    }

    private void BuildVisual(Term t)
    {
        bool red = t.role == Role.Red;
        Color cap = red ? new Color(1f, 0.12f, 0.12f, 1f) : new Color(0.12f, 0.12f, 0.15f, 1f);
        Color glow = red ? new Color(1f, 0.25f, 0.2f, 1f) : new Color(0.75f, 0.85f, 1f, 1f);

        t.root = new GameObject("[HvTestTerminal_" + (red ? "Red" : "Black") + "]");
        t.root.transform.SetParent(t.mp.transform, false);
        t.root.transform.localPosition = Vector3.zero;
        t.root.transform.localRotation = Quaternion.identity;

        // 실제 단자: 기둥(빨강/검정 머리) + 주황 지지대(밑판까지)
        Prim(PrimitiveType.Cylinder, "Post", t.root.transform, Vector3.zero, new Vector3(0.036f, 0.022f, 0.036f), Unlit(cap, true, 3996));   // (11/20) 맨 앞에 — 배터리 메쉬에 가려지지 않게
        Prim(PrimitiveType.Cylinder, "Ring", t.root.transform, new Vector3(0f, 0.018f, 0f), new Vector3(0.046f, 0.004f, 0.046f), Unlit(glow, true, 3997));
        Prim(PrimitiveType.Cylinder, "Stem", t.root.transform, new Vector3(0f, 0.062f, 0f), new Vector3(0.012f, 0.040f, 0.012f), Unlit(new Color(1f, 0.45f, 0.05f, 1f), true, 3995));

        // 안내(평가 모드에서는 숨김): 후광 · 맨 앞 이름표
        var guide = new List<GameObject>();
        var halo = Prim(PrimitiveType.Sphere, "Halo", t.root.transform, Vector3.zero, Vector3.one * 0.125f, Unlit(new Color(glow.r, glow.g, glow.b, 0.30f), true, 3990));
        t.halo = halo.transform; guide.Add(halo);

        // (11/20) 아래로 뻗은 빨강·파랑 빛줄기(Beam)는 뺐다 — 후광·이름표로 충분하다는 의견

        var labelRoot = new GameObject("Label");
        labelRoot.transform.SetParent(t.root.transform, false);
        Vector3 off = t.root.transform.InverseTransformDirection(t.outward) * 0.24f + Vector3.down * 0.13f;
        labelRoot.transform.localPosition = off;
        t.label = labelRoot.transform; guide.Add(labelRoot);

        var back = Prim(PrimitiveType.Quad, "Back", labelRoot.transform, new Vector3(0f, 0f, 0.003f), new Vector3(0.40f, 0.12f, 1f), Unlit(new Color(0f, 0f, 0f, 0.72f), true));
        var font = MobileWorkButton.KoreanFont();
        var tgo = new GameObject("Text");
        tgo.transform.SetParent(labelRoot.transform, false);
        var tmp = tgo.AddComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        tmp.fontSize = 0.34f;   // 3D TMP 는 글자 크기 1 ≈ 월드 0.1m → 줄 높이 약 3.4cm
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.richText = true;
        tmp.rectTransform.sizeDelta = new Vector2(0.40f, 0.12f);
        tmp.text = red ? "<b>빨간 프로브 (+)</b>\n<size=72%>여기에 꽂기</size>" : "<b>검정 프로브 (−)</b>\n<size=72%>먼저 여기에 꽂기</size>";
        tmp.color = red ? new Color(1f, 0.55f, 0.5f) : Color.white;
        var om = font != null ? VrUi.TmpOverlayMat(font) : null;
        if (om != null) tmp.fontSharedMaterial = om;
        var mr = tgo.GetComponent<MeshRenderer>(); if (mr != null) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        t.guideOnly = guide.ToArray();
        t.root.SetActive(false);   // 측정 단계가 될 때까지 숨김(Update 가 켠다)
    }
}
