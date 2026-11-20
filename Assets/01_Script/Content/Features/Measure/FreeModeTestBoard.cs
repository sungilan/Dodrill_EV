using System.Collections;
using TMPro;
using UnityEngine;

// ============================================================
//  FreeModeTestBoard.cs
//
//  자유 탈거 모드(ev_free_disassembly) 전용 — 멀티미터·절연저항계(메거) 연습용 단자 보드.
//  계측기는 시나리오의 해당 단계가 돼야만 나오고 측정할 단자도 그 단계의 부품에 붙어 있어, 측정 조작만 바로 시험해 볼 수 없었다.
//  자유 탈거 모드에서는 계측기 두 개가 처음부터 나오고(시나리오 JSON spawnObjects), 이 보드가 책상(SP_Desk) 앞에 선다.
//
//  단자 (값은 빨간 프로브 쪽 단자로 정해진다 — MeasurementPoint.GetValue)
//    · Test_Ground       접지. 근처에 접지가 있으므로 검정 프로브는 여기에 먼저 꽂는다
//    · Test_12V_Plus     12.6 V
//    · Test_HV_Plus      653 V / 절연 500 MΩ(합격)
//    · Test_HV_Minus     잔류 0.3 V / 절연 500 MΩ
//    · Test_Insul_Bad    절연 불량 0.1 MΩ (메거 불합격 확인용)
//
//  · 각 클라이언트가 자기 화면에만 만든다(네트워크 오브젝트 아님). 측정 신호는 서버로 가지만 자유 탈거 단계는 측정을 판정하지 않는다.
//  · 씬 배치 불필요 — 게임 시작 때 스스로 붙어 시나리오를 지켜본다.
// ============================================================
public class FreeModeTestBoard : MonoBehaviour
{
    private const string FreeScenarioId = "ev_free_disassembly";
    private const string AnchorName = "SP_Desk";

    private static FreeModeTestBoard s_inst;
    private GameObject _board;
    private bool _warned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (Application.isBatchMode || s_inst != null) return;   // 전용 서버(헤드리스)에는 만들지 않는다
        var go = new GameObject("[FreeModeTestBoard]");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        s_inst = go.AddComponent<FreeModeTestBoard>();
    }

    private IEnumerator Start()
    {
        var wait = new WaitForSecondsRealtime(1f);
        while (true)
        {
            bool free = CurrentScenarioId() == FreeScenarioId;
            if (free && _board == null) Build();
            else if (!free && _board != null) { Destroy(_board); _board = null; }
            yield return wait;
        }
    }

    private static string CurrentScenarioId()
    {
        var r = ScenarioStateReceiver.Instance;
        var sc = r != null ? r.CurrentScenario : null;
        if (sc == null)
        {
            var rec = FindFirstObjectByType<ScenarioReceiver>();
            if (rec != null) sc = rec.ReceivedData;
        }
        return sc != null ? sc.scenarioId : null;
    }

    // ── 보드 만들기 ─────────────────────────

    private void Build()
    {
        // SP_Desk 오브젝트는 씬에서 꺼져 있어 GameObject.Find 로는 못 찾는다(첫 버전에서 보드가 안 생긴 원인) → 꺼진 것까지 SpawnPoint 로 찾는다
        SpawnPoint anchor = null;
        foreach (var sp in FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (sp.pointId == AnchorName) { anchor = sp; break; }
        if (anchor == null)
        {
            if (!_warned) { _warned = true; Debug.LogWarning($"[FreeModeTestBoard] {AnchorName} 스폰 지점을 찾지 못함 — Game 씬 로드를 기다립니다"); }
            return;
        }

        // 책상에서 방 가운데(차) 쪽으로 1.1 m 나와 바닥에 세우고, 차 쪽을 바라보게 한다
        Vector3 a = anchor.transform.position;
        Vector3 toCenter = new Vector3(-a.x, 0f, -a.z);
        toCenter = toCenter.sqrMagnitude > 0.01f ? toCenter.normalized : Vector3.forward;
        Vector3 pos = new Vector3(a.x, 0f, a.z) + toCenter * 1.1f;

        _board = new GameObject("[FreeMode_MeterTestBoard]");
        _board.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-toCenter, Vector3.up));   // 앞면(+Z 반대쪽)이 차를 본다
        var t = _board.transform;

        var font = FindKoreanFont();
        Material baseMat = FindLitMaterial();

        // 받침 다리 + 판 (판 앞면이 -Z — 보는 사람 쪽)
        Part(t, "Leg", new Vector3(0f, 0.45f, 0.06f), new Vector3(0.06f, 0.9f, 0.06f), new Color(0.25f, 0.25f, 0.27f), baseMat, true);
        Part(t, "Foot", new Vector3(0f, 0.015f, 0.06f), new Vector3(0.5f, 0.03f, 0.4f), new Color(0.25f, 0.25f, 0.27f), baseMat, true);
        Part(t, "Panel", new Vector3(0f, 1.05f, 0.03f), new Vector3(1.0f, 0.42f, 0.03f), new Color(0.12f, 0.13f, 0.15f), baseMat, true);
        Label(t, "측정 연습 단자  (자유 탈거 모드)", new Vector3(0f, 1.22f, 0.0f), 0.045f, Color.white, font);

        // 단자: 판 앞면에 박힌 금속 단자(실제 콜라이더) + 판정용 트리거
        float y = 1.03f;
        Terminal(t, "Test_Ground", "접지", -0.40f, y, 0f, 0f, 0f, new Color(0.15f, 0.55f, 0.2f), baseMat, font);
        Terminal(t, "Test_12V_Plus", "12V (+)", -0.20f, y, 12.6f, 0.5f, 0f, new Color(0.85f, 0.6f, 0.1f), baseMat, font);
        Terminal(t, "Test_HV_Plus", "HV (+)", 0.00f, y, 653f, 500f, 0f, new Color(0.9f, 0.35f, 0.05f), baseMat, font);
        Terminal(t, "Test_HV_Minus", "HV (−)", 0.20f, y, 0.3f, 500f, 0f, new Color(0.2f, 0.35f, 0.85f), baseMat, font);
        Terminal(t, "Test_Insul_Bad", "절연 불량", 0.40f, y, 653f, 0.1f, 0f, new Color(0.75f, 0.1f, 0.1f), baseMat, font);

        Debug.Log($"[FreeModeTestBoard] 연습용 단자 보드 생성 at {pos}");
    }

    private void Terminal(Transform parent, string id, string label, float x, float y, float volt, float res, float faultVolt,
                          Color ring, Material baseMat, TMP_FontAsset font)
    {
        // GameObject.Find(terminalId) 로 값을 찾으므로 오브젝트 이름 = terminalId
        var go = new GameObject(id);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(x, y, -0.005f);

        var mp = go.AddComponent<MeasurementPoint>();
        mp.terminalId = id;
        mp.displayName = $"연습 단자 {label}";   // 측정 기록 알림에 'Test_12V_Plus' 대신 보일 이름
        mp.voltageValue = volt;
        mp.resistanceValue = res;
        mp.faultyVoltageValue = faultVolt;
        mp.isFaulty = false;

        // 판정 트리거(조준용, 단자보다 조금 크다)
        var trig = go.AddComponent<BoxCollider>();
        trig.isTrigger = true;
        trig.size = new Vector3(0.06f, 0.06f, 0.05f);

        // 색 고리 + 금속 단자(콜라이더가 있어 프로브 끝이 이 표면에 꽂힌다)
        Part(go.transform, "Ring", new Vector3(0f, 0f, 0.016f), new Vector3(0.05f, 0.05f, 0.01f), ring, baseMat, false);
        var stud = Part(go.transform, "Stud", new Vector3(0f, 0f, -0.004f), new Vector3(0.022f, 0.022f, 0.022f), new Color(0.78f, 0.78f, 0.8f), baseMat, true);
        stud.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);

        Label(parent, label, new Vector3(x, y - 0.075f, -0.01f), 0.03f, Color.white, font);
        string val = res >= 1000000f ? $"{volt:0.#} V" : $"{volt:0.#} V · {res:0.#} MΩ";
        Label(parent, val, new Vector3(x, y - 0.11f, -0.01f), 0.022f, new Color(1f, 1f, 1f, 0.6f), font);
    }

    private static GameObject Part(Transform parent, string name, Vector3 localPos, Vector3 size, Color color, Material baseMat, bool collider)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        if (!collider) { var c = go.GetComponent<Collider>(); if (c != null) Destroy(c); }
        var r = go.GetComponent<MeshRenderer>();
        var m = baseMat != null ? new Material(baseMat) : new Material(r.sharedMaterial);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        r.sharedMaterial = m;
        return go;
    }

    private static void Label(Transform parent, string text, Vector3 localPos, float size, Color color, TMP_FontAsset font)
    {
        var go = new GameObject("Label", typeof(TextMeshPro));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        // TMP 글자는 -Z 쪽에서 읽힌다 = 판 앞면(-Z) 쪽 사람이 그대로 읽는다(돌리지 않는다)
        var tmp = go.GetComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size * 10f;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.rectTransform.sizeDelta = new Vector2(0.4f, 0.06f);
    }

    /// <summary>빌드에 포함된 URP Lit 계열 재질을 하나 빌려 온다(새 Shader.Find 는 빌드에서 빠져 있을 수 있다).</summary>
    private static Material FindLitMaterial()
    {
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            var m = r.sharedMaterial;
            if (m != null && m.shader != null && m.shader.name.Contains("Lit") && m.HasProperty("_BaseColor")) return m;
        }
        return null;
    }

    private static TMP_FontAsset FindKoreanFont()
    {
        foreach (var t in FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (t.font != null && t.font.HasCharacter('접', true)) return t.font;
        return null;
    }
}
