using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

// ============================================================
//  FreeLoto.cs
//
//  자유 탈거 모드(ev_free_disassembly)의 '고전압 차단(LOTO) 연습'과 '신품 부품 교체'.
//
//  · 전에는 자유 탈거 모드에서 보호구·서비스 플러그를 처리할 방법이 없어서(시나리오 단계가 없다) 고전압 경고가 계속 떴고 끌 수도 없었다.
//    이제 연습 보드에서 매뉴얼 순서대로 한다: 절연 장갑(아이템을 잡으면 착용) → 12V(−) 분리 → 서비스 플러그(MSD) 분리 → 방전 대기(약 3분) → 단자 전압 30V 이하 확인.
//    진행 상태는 부위 목록 패널 위쪽에 단계별로 보인다.
//  · 차단 절차 없이 고전압 부위 볼트를 계속 돌리면(경고 뒤 3초) 감전 사고 — 기존 사고 연출(EmergencyEventManager)을 쓴다.
//  · 신품 부품 보관함: 재사용 금지 부품(CarPartRules.SingleUse)을 떼어 냈으면 새 것을 받아 두었다가 끼운다. 받지 않고 끼우면 재사용 경고.
//  · 보드는 각 클라이언트가 자기 화면에만 만든다(네트워크 오브젝트 아님, FreeModeTestBoard 와 같은 방식). 씬 배치 불필요.
// ============================================================
public static class FreeLoto
{
    public const string FreeScenarioId = "ev_free_disassembly";
    public const float PackV = 650f, Tau = 58f, SafeV = 30f;

    /// <summary>자유 연습(교육·평가 시나리오가 아님) — 고전압 차단 연습이 적용되는 상태</summary>
    public static bool IsFreePractice
    {
        get { var sm = ElectricSafetyManager.Instance; return sm != null && !sm.IsScenarioActive; }
    }

    private static bool s_freeScn; private static float s_freeScnAt = -999f;
    /// <summary>지금 시나리오가 자유 탈거(ev_free_disassembly)인가(1초 캐시)</summary>
    public static bool IsFreeScenario
    {
        get
        {
            if (Time.unscaledTime - s_freeScnAt > 1f) { s_freeScnAt = Time.unscaledTime; s_freeScn = FreeLotoBoard.CurrentScenarioId() == FreeScenarioId; }
            return s_freeScn;
        }
    }

    /// <summary>
    /// 차 안 서비스 플러그 레버(ClickableAnimator MSD_Lever)를 눌렀다. 자유 탈거에서는 연습 보드와 같은 절차(장갑 → 12V (−) → 플러그)를 거친다.
    /// 조건이 안 맞아 못 뽑으면 true(레버 동작 막음). 전에는 레버만 움직이고 안전 상태·방전 대기가 그대로였다.
    /// </summary>
    // 자유 탈거의 고전압 측정점(차 프리팹의 MeasurementPoint) — 멀티미터로 잰다
    private static readonly string[] HvTerminals = { "HV_Front_Cable_Term", "HV_Rear_Cable_Term", "HV_Cable_Term", "Battery_Main_Terminal" };
    public static bool IsHvTerminal(string id) { foreach (var t in HvTerminals) if (t == id) return true; return false; }

    /// <summary>
    /// 자유 탈거에서 멀티미터(DC 전압)가 고전압 단자에서 읽는 값 — 서비스 플러그를 뽑기 전에는 팩 전압(활선), 뽑은 뒤에는 방전 곡선.
    /// 시나리오(정상 0 V 고정)와 달리 방전 대기를 지켜야 30V 이하가 된다. 그 밖의 단자·모드는 false(기존 값).
    /// </summary>
    public static bool TryTerminalVoltage(string terminalId, MultimeterMode mode, out float v)
    {
        v = 0f;
        if (!IsFreeScenario || !IsHvTerminal(terminalId) || mode != MultimeterMode.DC_Voltage) return false;
        var sm = ElectricSafetyManager.Instance;
        v = sm != null && sm.isMSDRemoved ? VoltageNow() : PackV;
        return true;
    }

    public static bool BypassIntercept;   // 클릭 지점이 직접 처리한 뒤 레버 애니메이션만 돌릴 때

    public static bool InterceptMsdLever(ClickableAnimator a)
    {
        if (BypassIntercept) return false;
        if (a == null || a.uniqueId != "MSD_Lever" || !IsFreeScenario) return false;
        var sm = ElectricSafetyManager.Instance;
        if (sm == null) return false;
        if (!sm.isMSDRemoved && FreeLotoBoard.LidBlocksMsd()) return true;   // 뚜껑부터 열어야 한다
        bool before = sm.isMSDRemoved;
        FreeLotoBoard.OnMsd();
        return sm.isMSDRemoved == before;
    }

    public static bool Battery12VOff { get; set; }
    public static float MsdAt { get; set; } = -1f;
    public static int ShockCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Battery12VOff = false; MsdAt = -1f; ShockCount = 0; }

    /// <summary>방전 후 지금 단자 전압(추정)</summary>
    public static float VoltageNow()
    {
        float t = MsdAt >= 0f ? Time.time - MsdAt : 9999f;
        return PackV * Mathf.Exp(-t / Tau);
    }

    /// <summary>패널 위쪽에 붙일 '고전압 차단 절차' 줄들. 자유 연습이 아니면 null.</summary>
    public static string StatusRich()
    {
        var sm = ElectricSafetyManager.Instance;
        if (sm == null || !IsFreePractice) return null;
        bool gloves = sm.isGlovesEquipped, v12 = Battery12VOff, msd = sm.isMSDRemoved, ver = HvVoltageCheck.Verified;
        float v = VoltageNow();
        var sb = new StringBuilder();
        sb.Append("<size=76%><b>고전압 차단 절차</b> (연습 보드: 책상 옆)</size>\n");
        sb.Append(Step(1, "절연 장갑 착용 (아이템을 잡으면 착용)", gloves)).Append('\n');
        sb.Append(Step(2, "12V 배터리 (−) 단자 분리", v12)).Append('\n');
        sb.Append(Step(3, "서비스 플러그(MSD) 분리", msd)).Append('\n');
        string dis = !msd ? "서비스 플러그를 뽑은 뒤 약 3분" : v <= SafeV ? "방전 완료" : $"방전 중 — 약 {Mathf.CeilToInt(Tau * Mathf.Log(v / SafeV))}초 남음";
        sb.Append(Step(4, "방전 대기 · " + dis, msd && v <= SafeV)).Append('\n');
        sb.Append(Step(5, "단자 잔류 전압 30V 이하 확인", ver)).Append('\n');
        bool ready = gloves && msd && ver;
        sb.Append(ready ? "<size=76%><color=#7CFF8A>고전압 부위 작업 가능</color></size>"
                        : "<size=76%><color=#FF6B6B>고전압 부위(빨강)는 위 절차를 마친 뒤에만 — 안 하고 만지면 감전 사고</color></size>");
        if (ShockCount > 0) sb.Append($"\n<size=76%><color=#FF5555>감전 사고 {ShockCount}회</color></size>");
        sb.Append('\n');
        return sb.ToString();
    }

    private static string Step(int n, string text, bool done) =>
        done ? $"<size=72%><color=#7CFF8A>{n}. {text} — 완료</color></size>" : $"<size=72%><color=#FFC870>{n}. {text}</color></size>";

    /// <summary>절연 장갑을 착용했다(아이템을 잡았다)</summary>
    public static void WearGloves()
    {
        var sm = ElectricSafetyManager.Instance;
        if (sm == null || !IsFreePractice || sm.isGlovesEquipped) return;
        sm.OnGlovesEquipped();
        EquipFx.Notice("loto_gloves", "절연 장갑 착용", "다음: 12V (−) 단자 분리 → 서비스 플러그(MSD) 분리 (연습 보드)", EquipFx.Info, 3.5f, 2f);
    }

    /// <summary>안전 절차 없이 고전압 부위를 계속 만졌다 — 감전 사고</summary>
    public static void TriggerShock(CarPartService.PartDef d, string reason, Vector3? worldPos = null)
    {
        ShockCount++;
        MentorDialogue.Say("accident_shock", 20f);
        string msg = (d != null ? d.name + ": " : "") + reason;
        var em = EmergencyEventManager.Instance;
        if (em != null) em.TriggerAccident(EmergencyEventManager.AccidentType.ElectricShock, msg, worldPos);
        else EquipFx.Notice("hv_shock", "감전 사고!", msg + " (사고 연출 오브젝트가 씬에 없습니다)", EquipFx.Danger, 5f, 4f);
    }
}

/// <summary>자유 탈거 모드 전용 연습 보드 두 개 — 고전압 차단(LOTO) 보드와 신품 부품 보관함.</summary>
public class FreeLotoBoard : MonoBehaviour
{
    private const string AnchorName = "SP_Desk";
    private GameObject _loto, _parts;
    private TMP_Text _status;
    private readonly List<Row> _rows = new List<Row>();
    private float _nextUi;
    private bool _warned;

    private class Row { public TMP_Text label; public GameObject button; public int part = -1; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (Application.isBatchMode || FindAnyObjectByType<FreeLotoBoard>() != null) return;
        var go = new GameObject("[FreeLotoBoard]");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        go.AddComponent<FreeLotoBoard>();
    }

    private void OnEnable() { InteractionEvents.OnItemGrabbed += OnGrabbed; InteractionEvents.OnValueMeasured += OnMeasured; }
    private void OnDisable() { InteractionEvents.OnItemGrabbed -= OnGrabbed; InteractionEvents.OnValueMeasured -= OnMeasured; }

    // 멀티미터로 고전압 단자를 쟀다 — 30V 이하면 '단자 전압 확인' 완료(책상 옆 보드 버튼과 같은 결과)
    private static void OnMeasured(string terminalId, float value)
    {
        if (!FreeLoto.IsFreeScenario || !FreeLoto.IsHvTerminal(terminalId)) return;
        var sm = ElectricSafetyManager.Instance; if (sm == null) return;
        var mm = MultimeterMaster.Instance;
        if (mm != null && mm.currentMode != MultimeterMode.DC_Voltage)
        {
            EquipFx.Notice("hvv_mode", "멀티미터 모드", "다이얼을 DC 전압(V)에 맞춰야 단자 전압을 잴 수 있습니다", EquipFx.Warn, 3.5f, 3f);
            return;
        }
        if (!sm.isGlovesEquipped) { EquipFx.Notice("hvv_glove", "보호구 필요", "고전압 단자에 프로브를 대기 전에 절연 장갑을 착용하세요", EquipFx.Danger, 4f, 3f); return; }
        if (!sm.isMSDRemoved)
        {
            HvVoltageCheck.MarkVerified(false);
            EquipFx.Notice("hvv_live", "고전압 활선", $"단자 전압 {value:0} V — 서비스 플러그(MSD)를 아직 뽑지 않았습니다. 12V (−)·서비스 플러그를 먼저 차단하세요", EquipFx.Danger, 5f, 3f);
            return;
        }
        if (value <= FreeLoto.SafeV)
        {
            HvVoltageCheck.MarkVerified(true);
            MentorDialogue.Say("loto_ok", 120f);
            EquipFx.Notice("hvv_ok", "단자 전압 확인", $"{Mathf.Max(0.1f, value):0.0} V — {FreeLoto.SafeV:0} V 이하, 고전압 부위 작업을 시작할 수 있습니다", EquipFx.Info, 5f, 1f);
        }
        else
        {
            HvVoltageCheck.MarkVerified(false);
            MentorDialogue.Say("loto_wait", 60f);
            EquipFx.Notice("hvv_wait", "방전 중 — 아직 위험", $"단자 전압 {value:0} V ({FreeLoto.SafeV:0} V 초과) — 약 {Mathf.CeilToInt(FreeLoto.Tau * Mathf.Log(value / FreeLoto.SafeV))}초 더 기다린 뒤 다시 재세요", EquipFx.Danger, 5f, 2f);
        }
    }

    private static void OnGrabbed(string prefabId)
    {
        if (prefabId == "InsulatedGloves")
        {
            FreeLoto.WearGloves();
            var b = FindFirstObjectByType<FreeLotoBoard>();
            if (b != null) b.StartCoroutine(ConsumeHeldGloves());   // 시나리오처럼 착용하면 손에서 사라진다
        }
        MentorDialogue.Say("grab_" + prefabId, 300f);   // 처음 집은 도구마다 선임이 한마디
    }

    private static System.Collections.IEnumerator ConsumeHeldGloves()
    {
        yield return new WaitForSeconds(0.4f);   // 잡힘·소유권이 정해진 뒤
        var svc = CarPartService.Instance;
        if (svc == null) yield break;
        foreach (var ti in TaskItem.Registry.Values)
        {
            if (ti == null || ti.prefabId != "InsulatedGloves") continue;
            var sg = ti.GetComponent<SyncGrab>();
            if (sg != null && sg.IsGrabbed && sg.IsOwner) { svc.ConsumeItem(sg.NetworkObject); yield break; }
        }
    }

    private System.Collections.IEnumerator Start()
    {
        var wait = new WaitForSecondsRealtime(1f);
        while (true)
        {
            bool free = CurrentScenarioId() == FreeLoto.FreeScenarioId;
            var fsvc = CarPartService.Instance;
            if (fsvc != null) fsvc.ReportFreeMode(free);   // 서버는 시나리오를 모른다 — 자유 모드일 때만 부품 사고(낙하·누출)를 낸다
            UpdateMsdAccess(free);
            UpdateHotspots(free);
            if (free && _loto == null) Build();
            else if (!free && _loto != null) { Destroy(_loto); Destroy(_parts); _loto = _parts = null; _rows.Clear(); }
            yield return wait;
        }
    }

    internal static string CurrentScenarioId()
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

    // 서비스 플러그 레버는 2×3 cm 라 누르기 어렵다 — 자유 탈거 동안 클릭 상자만 키운다(시나리오로 바뀌면 되돌림).
    // 덮개(Battery_12V_Cover) 콜라이더는 건드리지 않는다: 시나리오처럼 '뚜껑을 눌러 열고 → 레버를 누르는' 순서로 한다.
    private BoxCollider _leverBox;
    private Vector3 _leverBoxSize;

    private void UpdateMsdAccess(bool free)
    {
        var lever = ClickableAnimatorManager.Get("MSD_Lever");
        if (!free)
        {
            if (_leverBox != null) _leverBox.size = _leverBoxSize;
            _leverBox = null;
            return;
        }
        if (lever == null) return;
        var bc = lever.GetComponent<BoxCollider>();
        if (bc == null || bc == _leverBox) return;
        _leverBox = bc; _leverBoxSize = bc.size;
        bc.size = new Vector3(Mathf.Max(bc.size.x, 0.07f), Mathf.Max(bc.size.y, 0.07f), Mathf.Max(bc.size.z, 0.07f));
    }

    /// <summary>서비스 플러그 레버에 닿으려면 12V 배터리 덮개 뚜껑을 먼저 열어야 한다(시나리오와 같은 순서). 막히면 true + 안내</summary>
    internal static bool LidBlocksMsd()
    {
        var cover = ClickableAnimatorManager.Get("Battery_12V_Cover");
        if (cover == null || cover.IsOpen) return false;
        var svc = CarPartService.Instance;
        int q = CarPartService.IndexOf("Battery12VCover");
        if (svc != null && q >= 0 && svc.Ready && svc.StateOf(q) == (int)CarPartService.PartState.Off) return false;   // 덮개를 아예 떼어냈다
        EquipFx.Notice("loto_open_lid", "먼저 12V 배터리 커버 뚜껑", "후드를 열고 12V 배터리 커버 뚜껑을 눌러 연 뒤, 아래의 서비스 플러그(MSD) 레버를 누르세요", EquipFx.Warn, 4.5f, 3f);
        return true;
    }

    // ── 차 안 클릭 지점(핫스팟) ──
    // 서비스 플러그 레버는 2×3 cm 에 스트럿 탑·12V 덮개 모형에 파묻혀 보이지도 눌리지도 않았다. 자유 탈거에서는 그 위쪽 열린 자리에 눈에 띄는 표시를 띄워
    // 누르게 한다. 12V (−) 단자(검은 클램프)도 직접 눌러 뗄 수 있다(책상 옆 보드와 같은 동작).
    private GameObject _msdSpot, _negSpot;
    private TMP_Text _msdLabel, _negLabel;
    private Material _spotMat;

    private GameObject _hvSpot;

    private void UpdateHotspots(bool free)
    {
        if (!free)
        {
            if (_msdSpot != null) Destroy(_msdSpot);
            if (_negSpot != null) Destroy(_negSpot);
            if (_hvSpot != null) Destroy(_hvSpot);
            _msdSpot = _negSpot = _hvSpot = null;
            return;
        }
        if (_hvSpot == null)
        {
            // 멀티미터로 재는 인버터 HV 단자(시나리오 HV_Front_Voltage 와 같은 측정점) 위에 청록 점 — 누르는 곳이 아니라 프로브를 꽂는 곳
            foreach (var mp in FindObjectsByType<MeasurementPoint>(FindObjectsSortMode.None))
            {
                if (mp == null || mp.terminalId != "HV_Front_Cable_Term" || !mp.isActiveAndEnabled) continue;
                _hvSpot = MakeMarker(mp.transform, mp.transform.position + Vector3.up * 0.12f, "FreeSpot_HV", "인버터 HV 단자 (멀티미터)", new Color(0.2f, 0.9f, 1f));
                break;
            }
        }
        var sm = ElectricSafetyManager.Instance;
        var lever = ClickableAnimatorManager.Get("MSD_Lever");
        if (lever != null)
        {
            if (_msdSpot == null)
            {
                // 레버 위 10 cm 에 한 번만 만든다. 차(레버의 부모)에 붙어 있어 리프트로 올라가도 따라가고, 이후 위치를 다시 계산하지 않는다
                var lc = lever.GetComponent<Collider>();
                Vector3 p = (lc != null ? lc.bounds.center : lever.transform.position) + Vector3.up * 0.10f;
                _msdSpot = MakeSpot(lever.transform.parent, p, "FreeSpot_MSD", "서비스 플러그(MSD)", out _msdLabel,
                    () => sm != null && sm.isMSDRemoved ? "서비스 플러그(MSD) — 눌러서 다시 꽂기"
                          : "서비스 플러그(MSD) — 12V 커버 뚜껑을 연 뒤, 장갑 → 12V (−) 분리 다음에 눌러서 분리",
                    () => OnMsdSpot(lever));
            }
            if (_msdLabel != null) _msdLabel.text = sm != null && sm.isMSDRemoved ? "서비스 플러그(MSD) — 분리됨" : "서비스 플러그(MSD)";
            Tint(_msdSpot, sm != null && sm.isMSDRemoved ? new Color(0.3f, 0.9f, 0.4f) : new Color(1f, 0.55f, 0.15f));
        }
        var vis = FindFirstObjectByType<Battery12VServiceVisual>();
        if (vis != null && vis.negTerminal != null)
        {
            if (_negSpot == null)
            {
                Vector3 p = vis.negTerminal.position + Vector3.up * 0.08f;
                _negSpot = MakeSpot(vis.negTerminal.parent != null ? vis.negTerminal.parent : vis.negTerminal, p, "FreeSpot_12V", "12V 배터리 (−)", out _negLabel,
                    () => FreeLoto.Battery12VOff ? "12V (−) 단자 — 눌러서 다시 연결" : "12V (−) 단자 — 눌러서 분리 (절연 장갑 먼저)", On12V);
            }
            if (_negLabel != null) _negLabel.text = FreeLoto.Battery12VOff ? "12V (−) — 분리됨" : "12V 배터리 (−)";
            Tint(_negSpot, FreeLoto.Battery12VOff ? new Color(0.3f, 0.9f, 0.4f) : new Color(1f, 0.85f, 0.2f));
        }
    }

    // 클릭 상자 없이 표시만(점 + 이름표 + 숨쉬기)
    private GameObject MakeMarker(Transform parent, Vector3 worldPos, string name, string label, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, true);
        go.transform.position = worldPos;
        var vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var vc = vis.GetComponent<Collider>(); if (vc != null) Destroy(vc);
        vis.name = "Dot"; vis.transform.SetParent(go.transform, true);
        vis.transform.position = worldPos; vis.transform.localScale = Vector3.one * 0.07f;
        if (_spotMat == null)
        {
            var sd = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (sd != null) _spotMat = new Material(sd);
        }
        var mr = vis.GetComponent<MeshRenderer>();
        if (_spotMat != null) mr.sharedMaterial = new Material(_spotMat);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Tint(go, color);
        var lb = Label(go.transform, label, new Vector3(0f, 0.09f, 0f), 0.05f, Color.white, FindKoreanFont());
        lb.rectTransform.sizeDelta = new Vector2(0.7f, 0.08f);
        go.AddComponent<FreeSpotPulse>().label = lb.transform;
        return go;
    }

    private void OnMsdSpot(ClickableAnimator lever)
    {
        var sm = ElectricSafetyManager.Instance; if (sm == null) return;
        if (!sm.isMSDRemoved && LidBlocksMsd()) return;   // 뚜껑부터
        bool before = sm.isMSDRemoved;
        OnMsd();
        if (sm.isMSDRemoved != before && lever != null)   // 절차가 맞아 실제로 바뀌었으면 레버도 같이 움직인다
        {
            FreeLoto.BypassIntercept = true;
            try { lever.Toggle(); } finally { FreeLoto.BypassIntercept = false; }
        }
    }

    private GameObject MakeSpot(Transform parent, Vector3 worldPos, string name, string label, out TMP_Text labelOut, System.Func<string> hover, System.Action click)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, true);
        go.transform.position = worldPos;
        go.layer = 15;
        var vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var vc = vis.GetComponent<Collider>(); if (vc != null) Destroy(vc);
        vis.name = "Dot"; vis.transform.SetParent(go.transform, false); vis.transform.localScale = Vector3.one * 0.07f;
        if (_spotMat == null)
        {
            var sd = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (sd != null) _spotMat = new Material(sd);
        }
        var mr = vis.GetComponent<MeshRenderer>();
        if (_spotMat != null) mr.sharedMaterial = new Material(_spotMat);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var col = go.AddComponent<SphereCollider>(); col.radius = 0.07f;
        var proxy = go.AddComponent<ClickProxy>();
        proxy.displayName = label; proxy.vrPokeable = true;
        proxy.hover = touch => hover();
        proxy.onClick = click;
        var lb = Label(go.transform, label, new Vector3(0f, 0.09f, 0f), 0.05f, Color.white, FindKoreanFont());
        lb.rectTransform.sizeDelta = new Vector2(0.6f, 0.08f);
        labelOut = lb;
        go.AddComponent<FreeSpotPulse>().label = lb.transform;
        return go;
    }

    private static void Tint(GameObject spot, Color c)
    {
        if (spot == null) return;
        var r = spot.GetComponentInChildren<MeshRenderer>();
        if (r == null || r.sharedMaterial == null) return;
        if (r.sharedMaterial.HasProperty("_BaseColor")) r.sharedMaterial.SetColor("_BaseColor", c);
        if (r.sharedMaterial.HasProperty("_Color")) r.sharedMaterial.SetColor("_Color", c);
    }

    // ── 보드 만들기 ──
    private void Build()
    {
        SpawnPoint anchor = null;
        foreach (var sp in FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (sp.pointId == AnchorName) { anchor = sp; break; }
        if (anchor == null) { if (!_warned) { _warned = true; Debug.LogWarning("[FreeLotoBoard] SP_Desk 를 못 찾음 — 기다립니다"); } return; }

        Vector3 a = anchor.transform.position;
        Vector3 toCenter = new Vector3(-a.x, 0f, -a.z);
        toCenter = toCenter.sqrMagnitude > 0.01f ? toCenter.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, toCenter);   // 계측 연습 보드(FreeModeTestBoard) 옆으로 나란히
        Vector3 basePos = new Vector3(a.x, 0f, a.z) + toCenter * 1.1f;
        Quaternion rot = Quaternion.LookRotation(-toCenter, Vector3.up);

        var font = FindKoreanFont();
        var mat = FindLitMaterial();

        // ── 고전압 차단 보드 ──
        _loto = new GameObject("[FreeLoto_Board]");
        _loto.transform.SetPositionAndRotation(basePos + side * 1.45f, rot);
        var t = _loto.transform;
        Cube(t, "Leg", new Vector3(0f, 0.45f, 0.06f), new Vector3(0.06f, 0.9f, 0.06f), new Color(0.25f, 0.25f, 0.27f), mat, true);
        Cube(t, "Foot", new Vector3(0f, 0.015f, 0.06f), new Vector3(0.5f, 0.03f, 0.4f), new Color(0.25f, 0.25f, 0.27f), mat, true);
        Cube(t, "Panel", new Vector3(0f, 1.15f, 0.03f), new Vector3(1.1f, 0.62f, 0.03f), new Color(0.10f, 0.11f, 0.14f), mat, true);
        Label(t, "고전압 차단(LOTO) 연습", new Vector3(0f, 1.40f, -0.01f), 0.05f, new Color(1f, 0.55f, 0.5f), font);
        // 제목(y 1.40) 아래에서 시작해 버튼(y 0.93) 위에서 끝나게 — 글 위쪽이 제목과 겹치던 것을 고쳤다(11/19)
        var st = Label(t, "", new Vector3(0f, 1.115f, -0.01f), 0.03f, Color.white, font);
        st.rectTransform.sizeDelta = new Vector2(1.0f, 0.4f);
        st.alignment = TextAlignmentOptions.Top;
        _status = st;

        Button(t, new Vector3(-0.36f, 0.93f, -0.02f), "12V (−) 단자", Color.yellow, mat, font,
               () => FreeLoto.Battery12VOff ? "누르면 12V (−) 단자를 다시 연결" : "누르면 12V 배터리 (−) 단자 분리", On12V);
        Button(t, new Vector3(0f, 0.93f, -0.02f), "서비스 플러그(MSD)", new Color(1f, 0.45f, 0.2f), mat, font,
               () => { var sm = ElectricSafetyManager.Instance; return sm != null && sm.isMSDRemoved ? "누르면 서비스 플러그를 다시 꽂기" : "누르면 서비스 플러그(MSD) 분리"; }, OnMsd);
        Button(t, new Vector3(0.36f, 0.93f, -0.02f), "단자 전압 측정", new Color(0.4f, 0.8f, 1f), mat, font,
               () => "누르면 인버터 DC 단자 잔류 전압을 잰다(30V 이하여야 작업 가능)", OnMeasure);

        // ── 신품 부품 보관함 ──
        _parts = new GameObject("[FreeNewParts_Board]");
        _parts.transform.SetPositionAndRotation(basePos + side * 2.75f, rot);
        var p = _parts.transform;
        Cube(p, "Leg", new Vector3(0f, 0.45f, 0.06f), new Vector3(0.06f, 0.9f, 0.06f), new Color(0.25f, 0.25f, 0.27f), mat, true);
        Cube(p, "Foot", new Vector3(0f, 0.015f, 0.06f), new Vector3(0.5f, 0.03f, 0.4f), new Color(0.25f, 0.25f, 0.27f), mat, true);
        Cube(p, "Panel", new Vector3(0f, 1.15f, 0.03f), new Vector3(1.25f, 0.62f, 0.03f), new Color(0.10f, 0.12f, 0.10f), mat, true);
        Label(p, "신품 부품 보관함 (재사용 금지 부품)", new Vector3(0f, 1.40f, -0.01f), 0.045f, new Color(0.6f, 1f, 0.7f), font);
        for (int i = 0; i < 6; i++)
        {
            var row = new Row();
            float y = 1.30f - i * 0.085f;
            row.label = Label(p, "", new Vector3(-0.20f, y, -0.01f), 0.026f, Color.white, font);
            row.label.alignment = TextAlignmentOptions.Left;
            row.label.rectTransform.sizeDelta = new Vector2(0.78f, 0.07f);
            int idx = i;
            row.button = Button(p, new Vector3(0.50f, y, -0.02f), "신품 받기", new Color(0.4f, 1f, 0.5f), mat, font,
                                () => RowHover(idx), () => OnNewPart(idx), new Vector3(0.20f, 0.06f, 0.03f));
            _rows.Add(row);
        }
        Debug.Log("[FreeLotoBoard] 고전압 차단 보드·신품 부품 보관함 생성");
    }

    // ── 동작 ──
    internal static void On12V()
    {
        var sm = ElectricSafetyManager.Instance; if (sm == null) return;
        if (!sm.isGlovesEquipped) { EquipFx.Notice("loto_need_gloves", "보호구 먼저", "절연 장갑을 먼저 착용하세요 (아이템을 잡으면 착용됩니다)", EquipFx.Danger, 3.5f, 3f); return; }
        FreeLoto.Battery12VOff = !FreeLoto.Battery12VOff;
        if (FreeLoto.Battery12VOff) MentorDialogue.Say("loto_12v", 120f);
        if (!FreeLoto.Battery12VOff && sm.isMSDRemoved) EquipFx.Notice("loto_12v_back", "12V (−) 연결", "서비스 플러그가 뽑힌 채입니다 — 매뉴얼은 12V 를 먼저 분리한 뒤 플러그를 뽑습니다", EquipFx.Warn, 3.5f, 3f);
        else EquipFx.Notice("loto_12v", FreeLoto.Battery12VOff ? "12V (−) 단자 분리" : "12V (−) 단자 연결", FreeLoto.Battery12VOff ? "다음: 서비스 플러그(MSD) 분리" : "고전압 차단이 해제됩니다", EquipFx.Info, 3f, 2f);
    }

    internal static void OnMsd()
    {
        var sm = ElectricSafetyManager.Instance; if (sm == null) return;
        if (!sm.isGlovesEquipped) { EquipFx.Notice("loto_need_gloves", "보호구 먼저", "절연 장갑을 먼저 착용하세요", EquipFx.Danger, 3.5f, 3f); return; }
        if (sm.isMSDRemoved)
        {
            sm.isMSDRemoved = false; FreeLoto.MsdAt = -1f; HvVoltageCheck.MarkVerified(false);
            EquipFx.Notice("loto_msd_in", "서비스 플러그 장착", "고전압이 다시 살아납니다 — 차단 절차를 처음부터 해야 합니다", EquipFx.Warn, 3.5f, 2f);
            return;
        }
        if (!FreeLoto.Battery12VOff) { EquipFx.Notice("loto_need_12v", "순서 — 12V (−) 먼저", "먼저 엔진룸 12V 배터리의 (−) 단자(노란 점 '12V 배터리 (−)')를 눌러 떼세요 — 책상 옆 보드 버튼도 됩니다. 매뉴얼 순서: 보호구 → 12V (−) 분리 → 서비스 플러그 분리", EquipFx.Danger, 4f, 3f); return; }
        sm.OnMSDRemoved(); FreeLoto.MsdAt = Time.time; HvVoltageCheck.MarkVerified(false);
        MentorDialogue.Say("loto_msd", 120f);
        EquipFx.Notice("loto_msd_out", "서비스 플러그(MSD) 분리", "방전을 약 3분 기다린 뒤 멀티미터(카트 위)로 인버터 HV 단자(청록 점)를 재세요 — 다이얼을 DC 전압(V)에 맞추고, 검정 프로브(접지 GND 또는 옆 단자)를 먼저, 빨간 프로브를 청록 점에 꽂습니다(F). 30V 이하여야 작업할 수 있습니다", EquipFx.Info, 4f, 2f);
    }

    private static void OnMeasure()
    {
        var sm = ElectricSafetyManager.Instance; if (sm == null) return;
        if (!sm.isGlovesEquipped) { EquipFx.Notice("hvv_glove", "보호구 필요", "고전압 단자에 접근하기 전에 절연 장갑을 착용하세요", EquipFx.Danger, 4f, 3f); return; }
        if (!sm.isMSDRemoved) { EquipFx.Notice("hvv_live", "고전압 활선", $"단자 전압 {FreeLoto.PackV:0} V — 서비스 플러그(MSD)를 아직 뽑지 않았습니다", EquipFx.Danger, 5f, 3f); return; }
        float v = FreeLoto.VoltageNow();
        if (v <= FreeLoto.SafeV)
        {
            HvVoltageCheck.MarkVerified(true);
            MentorDialogue.Say("loto_ok", 120f);
            EquipFx.Notice("hvv_ok", "단자 전압 확인", $"{Mathf.Max(0.1f, v):0.0} V — {FreeLoto.SafeV:0} V 이하, 고전압 부위 작업을 시작할 수 있습니다", EquipFx.Info, 5f, 1f);
        }
        else
        {
            HvVoltageCheck.MarkVerified(false);
            MentorDialogue.Say("loto_wait", 60f);
            EquipFx.Notice("hvv_wait", "방전 중 — 아직 위험", $"단자 전압 {v:0} V ({FreeLoto.SafeV:0} V 초과) — 약 {Mathf.CeilToInt(FreeLoto.Tau * Mathf.Log(v / FreeLoto.SafeV))}초 더 기다리세요", EquipFx.Danger, 5f, 2f);
        }
    }

    private string RowHover(int i)
    {
        if (i >= _rows.Count || _rows[i].part < 0) return "떼어낸 재사용 금지 부품이 여기에 나옵니다";
        string id = CarPartService.Defs[_rows[i].part].id;
        if (CarPartRules.IsDamaged(id)) return "누르면 부서진 부품 대신 새 부품을 받습니다 (제자리에 나타납니다)";
        return CarPartRules.NewPartReady(id) ? "신품은 이미 받았습니다 — 부품을 제자리에 끼우세요" : "누르면 신품(" + CarPartRules.SingleUseOf(id) + ")을 받습니다";
    }

    private void OnNewPart(int i)
    {
        if (i >= _rows.Count || _rows[i].part < 0) return;
        var d = CarPartService.Defs[_rows[i].part];
        if (CarPartRules.IsDamaged(d.id))   // 사고로 부서진 부품 — 새 부품이 차 옆에 나타난다
        {
            var svc2 = CarPartService.Instance;
            if (svc2 != null) svc2.ReplaceDamaged(_rows[i].part);
            EquipFx.Notice("newpart_" + d.id, "새 부품 준비: " + d.name, "새 부품을 받았습니다 — 차 옆에서 들어 제자리에 끼우세요", EquipFx.Info, 3.5f, 1f);
            return;
        }
        if (CarPartRules.NewPartReady(d.id)) return;
        CarPartRules.SetNewPart(d.id, true);
        EquipFx.Notice("newpart_" + d.id, "신품 준비: " + d.name, CarPartRules.SingleUseOf(d.id) + " 신품을 받았습니다 — 떼어낸 부품을 제자리에 끼우세요", EquipFx.Info, 3.5f, 1f);
    }

    private void Update()
    {
        if (_loto == null || Time.unscaledTime < _nextUi) return;
        _nextUi = Time.unscaledTime + 0.3f;
        if (_status != null)
        {
            string s = FreeLoto.StatusRich();
            _status.text = s ?? "";
        }
        var svc = CarPartService.Instance;
        // 떼어낸(Off) 재사용 금지 부위를 앞에서부터 6개
        int r = 0;
        if (svc != null && svc.Ready)
            for (int p = 0; p < CarPartService.Defs.Length && r < _rows.Count; p++)
            {
                string id0 = CarPartService.Defs[p].id;
                bool broken = CarPartRules.IsDamaged(id0);
                if (svc.StateOf(p) != (int)CarPartService.PartState.Off || (!broken && CarPartRules.SingleUseOf(id0) == null)) continue;
                var row = _rows[r++];
                row.part = p;
                string id = id0;
                bool ready = !broken && CarPartRules.NewPartReady(id);
                row.label.text = broken
                    ? $"{CarPartService.Defs[p].name} — <color=#FF6B6B>파손(새 부품 필요)</color>"
                    : $"{CarPartService.Defs[p].name} — {CarPartRules.SingleUseOf(id)} {(ready ? "<color=#7CFF8A>(신품 준비됨)</color>" : "")}";
                row.label.gameObject.SetActive(true); row.button.SetActive(!ready);
            }
        for (; r < _rows.Count; r++)
        {
            _rows[r].part = -1;
            _rows[r].label.text = r == 0 ? "<color=#9FB4C8>떼어낸 재사용 금지 부품이 없습니다</color>" : "";
            _rows[r].button.SetActive(false);
        }
    }

    // ── 도우미(FreeModeTestBoard 와 같은 방식) ──
    private static GameObject Cube(Transform parent, string name, Vector3 lp, Vector3 size, Color color, Material baseMat, bool collider)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = lp; go.transform.localScale = size;
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

    private static TMP_Text Label(Transform parent, string text, Vector3 lp, float size, Color color, TMP_FontAsset font)
    {
        var go = new GameObject("Label", typeof(TextMeshPro));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = lp;
        var tmp = go.GetComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        tmp.text = text; tmp.fontSize = size * 10f; tmp.color = color; tmp.richText = true;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(0.9f, 0.06f);
        return tmp;
    }

    private static GameObject Button(Transform parent, Vector3 lp, string text, Color color, Material mat, TMP_FontAsset font,
                                     System.Func<string> hover, System.Action onClick, Vector3? size = null)
    {
        var sz = size ?? new Vector3(0.30f, 0.10f, 0.03f);
        var go = Cube(parent, "Button_" + text, lp, sz, color, mat, true);
        go.layer = 15;   // 클릭 대상 레이어(차량 상호작용과 같다)
        var proxy = go.AddComponent<ClickProxy>();
        proxy.displayName = text; proxy.vrPokeable = true;
        proxy.hover = touch => hover();
        proxy.onClick = onClick;
        var lb = Label(go.transform, text, new Vector3(0f, 0f, -0.6f), 0.02f / Mathf.Max(0.05f, sz.y) * 0.5f, Color.black, font);
        lb.transform.localScale = new Vector3(1f / sz.x, 1f / sz.y, 1f / sz.z);   // 큐브 스케일을 상쇄해 글자가 일그러지지 않게
        lb.rectTransform.sizeDelta = new Vector2(sz.x * 0.95f, sz.y);
        lb.fontSize = 0.026f * 10f * 0.9f; lb.fontStyle = FontStyles.Bold;
        return go;
    }

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

/// <summary>
/// 감전 위험 게이지 — 고전압 차단 절차 없이 고전압(빨강) 부위 볼트를 돌리는 동안 화면 가운데 아래에 빨간 막대가 차오른다.
/// 가득 차면 감전 사고(CarPartService → FreeLoto.TriggerShock). 손을 떼면 서서히 내려간다. PC·모바일 화면용(VR 제외).
/// </summary>
public class HvShockGauge : MonoBehaviour
{
    private GameObject _canvas;
    private UnityEngine.UI.Image _fill, _back;
    private TMP_Text _label, _sub;
    private float _nextCheck;

    /// <summary>감전 위험 게이지가 지금 화면에 떠 있는가 — 볼트 분해 게이지가 같은 자리에 겹치지 않게 아래로 비켜 선다.</summary>
    public static bool Visible { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isBatchMode || FindAnyObjectByType<HvShockGauge>() != null) return;
        var go = new GameObject("[HvShockGauge]");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        go.AddComponent<HvShockGauge>();
    }

    private void Update()
    {
        var svc = CarPartService.Instance;
        float r = svc != null && svc.Ready ? svc.HvTouchRatio : 0f;
        if (EmergencyEventManager.Instance != null && EmergencyEventManager.Instance.PopupUp) r = 0f;   // 훈련 종료 창과 겹치지 않게
        if (_canvas == null)
        {
            if (r <= 0.01f || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 1f;
            Build();
        }
        bool show = r > 0.01f;
        Visible = show;
        if (_canvas.activeSelf != show) _canvas.SetActive(show);
        if (!show) return;

        // 차오를수록 진하고 빠르게 깜빡인다
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (4f + 14f * r));
        _fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(r), 1f);
        _fill.color = Color.Lerp(new Color(1f, 0.55f, 0.1f), new Color(1f, 0.1f, 0.1f), r) * new Color(1f, 1f, 1f, 0.75f + 0.25f * pulse * r);
        _back.color = new Color(0.12f, 0.02f, 0.02f, 0.85f);
        _label.text = svc != null && svc.HvTouchHolding ? "<b>감전 위험이 쌓여 있습니다 — 차단 절차를 마쳐야 내려갑니다</b>"
                    : r >= 0.66f ? "<b>감전 직전! 지금 손을 떼세요</b>" : "<b>감전 위험 — 고전압 차단 안 됨</b>";
        _sub.text = svc != null ? svc.HvTouchWhy : "";
    }

    private void Build()
    {
        _canvas = VrUi.NewHudCanvas("[HvShockGauge_Canvas]", 70).gameObject;   // PC·모바일 = 화면 오버레이, VR = 시선 앞 월드 캔버스(같은 자리·모양)

        var back = NewRect("Back", _canvas.transform);
        back.anchorMin = back.anchorMax = new Vector2(0.5f, 0.5f); back.pivot = new Vector2(0.5f, 0.5f);
        back.anchoredPosition = new Vector2(0f, -170f); back.sizeDelta = new Vector2(620f, 34f);
        _back = back.gameObject.AddComponent<UnityEngine.UI.Image>(); _back.raycastTarget = false;

        var fill = NewRect("Fill", back);
        fill.anchorMin = new Vector2(0f, 0f); fill.anchorMax = new Vector2(0f, 1f); fill.pivot = new Vector2(0f, 0.5f);
        fill.offsetMin = new Vector2(3f, 3f); fill.offsetMax = new Vector2(-3f, -3f);
        _fill = fill.gameObject.AddComponent<UnityEngine.UI.Image>(); _fill.raycastTarget = false;
        // 앵커 비율로 채우므로 오프셋은 안쪽 여백만
        fill.anchorMax = new Vector2(0f, 1f);

        _label = NewText(back, "Label", 26f, new Vector2(0f, 38f));
        _sub = NewText(back, "Sub", 19f, new Vector2(0f, -34f));
        _sub.color = new Color(1f, 0.75f, 0.7f);
        VrUi.Finish(_canvas);   // VR: 맨 앞에 그리기
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    private static TMP_Text NewText(Transform parent, string name, float size, Vector2 pos)
    {
        var rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(900f, 36f);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        var kf = MobileWorkButton.KoreanFont();
        if (kf != null) t.font = kf;
        t.fontSize = size; t.alignment = TextAlignmentOptions.Center; t.color = new Color(1f, 0.25f, 0.2f); t.raycastTarget = false;
        t.richText = true; t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }
}

/// <summary>
/// 방전 대기 게이지 — 감전 위험 게이지·볼트 진행 막대와 같은 자리(화면 가운데 아래 막대)에 뜬다. 모든 시나리오·자유 탈거 공통(11/20).
///  · 자유 탈거: 서비스 플러그를 뽑으면 '잔류 전압 방전 중' 막대가 차오르고, 30V 이하가 되면 '방전 완료 — 단자 전압을 측정하세요'로 바뀌었다 사라진다.
///  · 시나리오: DischargeTimerUI(DischargeWait 단계)가 SetExternal 로 값을 넘겨 같은 막대를 쓴다(예전 오른쪽 위 패널은 안 띄운다).
/// PC·모바일 화면 오버레이, VR 은 시선 앞 월드 캔버스(VrUi).
/// </summary>
public class FreeDischargeHud : MonoBehaviour
{
    private GameObject _canvas;
    private RectTransform _back;
    private UnityEngine.UI.Image _fill;
    private TMP_Text _label, _sub;
    private float _doneAt = -1f;
    private bool _dismissed;

    /// <summary>방전 게이지가 지금 화면에 떠 있는가 — 볼트 분해 게이지가 같은 자리에 겹치지 않게 위로 비켜 선다.</summary>
    public static bool Visible { get; private set; }

    // 시나리오가 넘겨 주는 값 — 마지막 호출로부터 holdSeconds 동안만 유효(끊기면 저절로 사라진다)
    private static float s_extUntil = -1f, s_extRatio;
    private static string s_extLabel = "", s_extSub = "";
    private static bool s_extDone;

    /// <summary>시나리오 방전 대기(DischargeTimerUI)가 매 프레임 부른다. done=true 면 초록 '완료' 모양.</summary>
    public static void SetExternal(float ratio, string label, string sub, bool done, float holdSeconds = 0.6f)
    {
        s_extRatio = Mathf.Clamp01(ratio); s_extLabel = label ?? ""; s_extSub = sub ?? ""; s_extDone = done;
        s_extUntil = Time.unscaledTime + holdSeconds;
    }

    public static void ClearExternal() { s_extUntil = -1f; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isBatchMode || FindAnyObjectByType<FreeDischargeHud>() != null) return;
        var go = new GameObject("[FreeDischargeHud]");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        go.AddComponent<FreeDischargeHud>();
    }

    private void Hide()
    {
        Visible = false;
        if (_canvas != null && _canvas.activeSelf) _canvas.SetActive(false);
    }

    private void Update()
    {
        bool popup = EmergencyEventManager.Instance != null && EmergencyEventManager.Instance.PopupUp;
        float ratio; string label, sub; bool done;

        if (Time.unscaledTime < s_extUntil && !popup)
        {
            ratio = s_extRatio; label = s_extLabel; sub = s_extSub; done = s_extDone;
            _dismissed = false; _doneAt = -1f;
        }
        else
        {
            var sm = ElectricSafetyManager.Instance;
            bool active = sm != null && FreeLoto.IsFreeScenario && sm.isMSDRemoved && FreeLoto.MsdAt >= 0f && !HvVoltageCheck.Verified && !popup;
            if (!active) { _dismissed = false; _doneAt = -1f; Hide(); return; }
            if (_dismissed) { Hide(); return; }

            float v = FreeLoto.VoltageNow();
            float total = FreeLoto.Tau * Mathf.Log(FreeLoto.PackV / FreeLoto.SafeV);
            float el = Time.time - FreeLoto.MsdAt;
            done = v <= FreeLoto.SafeV;
            if (done)
            {
                if (_doneAt < 0f) _doneAt = Time.unscaledTime;
                if (Time.unscaledTime - _doneAt > 8f) { _dismissed = true; Hide(); return; }
            }
            ratio = done ? 1f : Mathf.Clamp01(el / Mathf.Max(1f, total));
            int remain = Mathf.CeilToInt(Mathf.Max(0f, total - el));
            label = done ? "<b>방전 완료 — 단자 전압을 측정하세요</b>" : $"<b>잔류 전압 방전 중... {remain / 60}:{remain % 60:00}</b>";
            sub = done ? "멀티미터(카트 위)로 인버터 HV 단자(청록 점)를 재서 30V 이하를 확인하세요"
                       : $"현재 약 {v:0} V → {FreeLoto.SafeV:0} V 이하까지 기다리세요 (방전 전에 만지면 감전)";
        }

        if (_canvas == null) Build();
        if (!_canvas.activeSelf) _canvas.SetActive(true);
        Visible = true;
        // 감전 위험 게이지가 같이 뜨면 그 위로 올라간다(볼트 게이지와 같은 규칙)
        _back.anchoredPosition = new Vector2(0f, HvShockGauge.Visible ? -92f : -170f);
        _fill.rectTransform.anchorMax = new Vector2(ratio, 1f);
        _fill.color = done ? new Color(0.3f, 0.85f, 0.4f, 0.95f) : new Color(1f, 0.72f, 0.2f, 0.95f);
        _label.text = label;
        _label.color = done ? new Color(0.6f, 1f, 0.65f) : new Color(1f, 0.85f, 0.45f);
        _sub.text = sub;
    }

    private void Build()
    {
        _canvas = VrUi.NewHudCanvas("[FreeDischargeHud_Canvas]", 68).gameObject;   // PC·모바일 = 화면 오버레이, VR = 시선 앞 월드 캔버스(같은 자리·모양)

        // 감전 위험 게이지(HvShockGauge)·볼트 막대(InteractionProgressBarUI)와 같은 치수: 막대 620×34 @ 가운데 (0,-170), 제목 +38, 설명 -34
        _back = NewRect("Back", _canvas.transform);
        _back.anchorMin = _back.anchorMax = new Vector2(0.5f, 0.5f); _back.pivot = new Vector2(0.5f, 0.5f);
        _back.anchoredPosition = new Vector2(0f, -170f); _back.sizeDelta = new Vector2(620f, 34f);
        var bi = _back.gameObject.AddComponent<UnityEngine.UI.Image>(); bi.color = new Color(0.07f, 0.09f, 0.12f, 0.88f); bi.raycastTarget = false;

        var fill = NewRect("Fill", _back);
        fill.anchorMin = new Vector2(0f, 0f); fill.anchorMax = new Vector2(0f, 1f); fill.pivot = new Vector2(0f, 0.5f);
        fill.offsetMin = new Vector2(3f, 3f); fill.offsetMax = new Vector2(-3f, -3f);
        _fill = fill.gameObject.AddComponent<UnityEngine.UI.Image>(); _fill.raycastTarget = false;

        _label = NewText(_back, "Label", 26f, new Vector2(0f, 38f));
        _sub = NewText(_back, "Sub", 19f, new Vector2(0f, -34f)); _sub.color = new Color(0.8f, 0.86f, 0.95f);
        VrUi.Finish(_canvas);   // VR: 맨 앞에 그리기
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    private static TMP_Text NewText(Transform parent, string name, float size, Vector2 pos)
    {
        var rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(900f, 36f);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        var kf = MobileWorkButton.KoreanFont();
        if (kf != null) t.font = kf;
        t.fontSize = size; t.alignment = TextAlignmentOptions.Center; t.color = new Color(1f, 0.85f, 0.45f); t.raycastTarget = false;
        t.richText = true; t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }
}

/// <summary>자유 탈거 클릭 지점 — 숨 쉬듯 커졌다 작아지고, 라벨은 항상 카메라를 본다</summary>
public class FreeSpotPulse : MonoBehaviour
{
    public Transform label;
    private Transform _dot;
    private float _base = -1f;
    private void LateUpdate()
    {
        float k = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 4f);
        if (_dot == null) _dot = transform.Find("Dot");
        if (_dot != null)
        {
            if (_base < 0f) _base = _dot.localScale.x;   // 부모 배율이 달라도 처음 크기를 기준으로 숨 쉰다
            _dot.localScale = Vector3.one * _base * k;
        }
        var cam = Camera.main;
        if (label != null && cam != null) label.rotation = cam.transform.rotation;
    }
}

/// <summary>
/// 고전압 차단 상태 막대 — 자유 탈거에서 화면 위 가운데에 항상 떠 있다. 절연 장갑 → 12V (−) → 서비스 플러그 → 방전 대기 → 전압 측정
/// 다섯 단계를 아이콘 원으로 잇고(완료 초록 · 지금 할 일 주황 테두리 깜빡임 · 남은 단계 흐리게), 맨 끝 방패가 모두 끝나면 켜진다.
/// 글자는 지금 할 단계의 이름(방전 대기는 남은 초) 하나와 아래 한 줄 안내만 둔다. PC·모바일은 화면 위쪽 가운데, VR 은 왼손 손목(손등을 얼굴 쪽으로).
/// 아이콘: Resources/HvSteps (Flaticon — 출처 표기 필요, 같은 폴더 CREDITS.txt)
/// </summary>
public class HvStatusHud : MonoBehaviour
{
    private const int N = 5;
    private static readonly string[] Names = { "절연 장갑", "12V (−) 분리", "서비스 플러그 분리", "방전 대기", "단자 전압 측정" };
    private static readonly string[] Icons = { "hand-glove", "car-battery", "plug", "hourglass", "multimeter" };

    private const float Circle = 54f, IconSize = 38f, Gap = 26f, BackW = 640f;
    // (11/19) 오른쪽 위 메뉴 버튼을 20% 줄여 화면 정가운데에 둔다. 알림(ScreenToast)이 바로 아래에 같은 가운데로 쌓인다
    private const float CenterX = 0.5f;
    private static readonly Color Done = new Color(0.20f, 0.62f, 0.30f, 1f);
    private static readonly Color Now = new Color(1f, 0.6f, 0.1f, 1f);
    private static readonly Color Idle = new Color(0.30f, 0.33f, 0.38f, 1f);
    private static readonly Color DoneDisc = new Color(0.10f, 0.30f, 0.15f, 0.95f);
    private static readonly Color NowDisc = new Color(0.32f, 0.20f, 0.06f, 0.95f);
    private static readonly Color IdleDisc = new Color(0.13f, 0.15f, 0.18f, 0.95f);

    private GameObject _canvas;
    private readonly UnityEngine.UI.Image[] _ring = new UnityEngine.UI.Image[N + 1];
    private readonly UnityEngine.UI.Image[] _disc = new UnityEngine.UI.Image[N + 1];
    private readonly UnityEngine.UI.Image[] _icon = new UnityEngine.UI.Image[N + 1];
    private readonly UnityEngine.UI.Image[] _link = new UnityEngine.UI.Image[N];
    private readonly RectTransform[] _slot = new RectTransform[N + 1];
    private TMP_Text _label, _next;
    private static Sprite s_disc, s_ring;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Application.isBatchMode || FindAnyObjectByType<HvStatusHud>() != null) return;
        var go = new GameObject("[HvStatusHud]");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        go.AddComponent<HvStatusHud>();
    }

    // 다른 UI(VR 손바닥 메뉴)가 읽는 현재 상태. VR 은 별도 손목 패널 없이 손바닥 메뉴 윗부분에 같은 아이콘을 그린다(11/19 — 패널 하나로 통합).
    public static bool SnapActive;
    public static readonly bool[] SnapDone = new bool[N];
    public static int SnapCur;
    public static bool SnapAll;
    public static string SnapLabel = "", SnapNext = "", SnapNextShort = "";
    public static string StepIcon(int i) => i < N ? Icons[i] : "insurance";   // N 번째는 완료 방패

    private bool _vrKnown;

    private void Update()
    {
        var sm = ElectricSafetyManager.Instance;
        bool active = sm != null && FreeLoto.IsFreeScenario
                      && !(EmergencyEventManager.Instance != null && EmergencyEventManager.Instance.PopupUp);
        SnapActive = active;
        if (!active) { if (_canvas != null && _canvas.activeSelf) _canvas.SetActive(false); return; }
        if (!_vrKnown) { _vr = XRAirpotrSecurity.Utils.GetPlatformType() == XRAirpotrSecurity.PlatformType.VR; _vrKnown = true; }

        float v = FreeLoto.VoltageNow();
        bool msd = sm.isMSDRemoved;
        bool dis = msd && v <= FreeLoto.SafeV;
        bool[] done = { sm.isGlovesEquipped, FreeLoto.Battery12VOff, msd, dis, HvVoltageCheck.Verified };
        int cur = -1;
        for (int i = 0; i < N; i++) if (!done[i]) { cur = i; break; }
        bool all = cur < 0;
        for (int i = 0; i < N; i++) SnapDone[i] = done[i];
        SnapCur = cur; SnapAll = all;

        // 지금 할 단계 이름(방전 대기는 남은 초)과 안내 한 줄
        string labelStr;
        if (all) labelStr = "<b>차단 완료</b>";
        else
        {
            string name = cur == 3 && msd
                ? $"방전 대기 {Mathf.CeilToInt(FreeLoto.Tau * Mathf.Log(Mathf.Max(v, 31f) / FreeLoto.SafeV))}초"
                : Names[cur];
            labelStr = $"<b>{cur + 1}. {name}</b>";
        }
        string next;
        switch (cur)
        {
            case 0: next = "절연 장갑을 서랍에서 집으세요 — 집으면 바로 착용됩니다"; break;
            case 1: next = "엔진룸 12V 배터리 (−) 단자의 노란 점을 누르세요 (책상 옆 보드 버튼도 됩니다)"; break;
            case 2: next = "후드를 열고 12V 배터리 커버 뚜껑을 눌러 연 뒤, 서비스 플러그(MSD) 레버(주황 점)를 누르세요"; break;
            case 3: next = "방전 대기 중 — 전압이 30V 이하로 내려갈 때까지 고전압 부위를 만지지 마세요"; break;
            case 4: next = "멀티미터(카트 위)를 들고 DC 전압(V)에 맞춘 뒤, 검정 프로브(접지 GND 또는 옆 단자)를 먼저, 빨간 프로브는 인버터 HV 단자(청록 점)에 꽂아 30V 이하를 확인하세요 (F)"; break;
            default: next = "고전압 차단 완료 — 고전압 부위(빨강) 작업을 할 수 있습니다"; break;
        }
        SnapLabel = labelStr; SnapNext = next;
        // VR 메뉴용 짧은 안내(글자를 크게 쓰려고 줄였다)
        switch (cur)
        {
            case 0: SnapNextShort = "서랍에서 절연 장갑을 집으세요"; break;
            case 1: SnapNextShort = "12V 배터리 (−) 단자의 노란 점을 누르세요"; break;
            case 2: SnapNextShort = "12V 커버 뚜껑을 열고 서비스 플러그(주황 점)를 누르세요"; break;
            case 3: SnapNextShort = "30V 이하가 될 때까지 고전압 부위를 만지지 마세요"; break;
            case 4: SnapNextShort = "멀티미터로 인버터 단자(청록 점) 30V 이하를 확인하세요"; break;
            default: SnapNextShort = "고전압 부위(빨강) 작업을 할 수 있습니다"; break;
        }
        if (_vr) return;   // VR: 화면에 따로 그리지 않고 손바닥 메뉴 패널 윗부분에 그린다

        if (_canvas == null) Build();
        if (!_canvas.activeSelf) _canvas.SetActive(true);

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
        for (int i = 0; i < N; i++)
        {
            if (done[i]) Paint(i, Done, DoneDisc, Color.white, 1f);
            else if (i == cur) Paint(i, Color.Lerp(Now, Color.white, pulse * 0.35f), NowDisc, Color.white, 1f + 0.06f * pulse);
            else Paint(i, Idle, IdleDisc, new Color(1f, 1f, 1f, 0.35f), 1f);
            _link[i].color = done[i] ? Done : Idle;   // 이 단계 → 다음 원(마지막은 방패)으로 가는 선
        }
        // 맨 끝 방패: 다섯 단계를 다 마쳐야 켜진다
        if (all) Paint(N, Done, DoneDisc, Color.white, 1f + 0.04f * pulse);
        else Paint(N, Idle, IdleDisc, new Color(1f, 1f, 1f, 0.25f), 1f);

        // 지금 할 단계 이름을 그 원 아래에
        int at = all ? N : cur;
        _label.text = labelStr;
        _label.color = all ? new Color(0.6f, 1f, 0.65f) : new Color(1f, 0.82f, 0.45f);
        _label.rectTransform.anchoredPosition = new Vector2(_slot[at].anchoredPosition.x, LabelY);

        _next.text = next;
        _next.color = all ? new Color(0.6f, 1f, 0.65f) : new Color(1f, 0.85f, 0.5f);
    }

    private bool _vr;

    private const float Top = 36f;                       // 위 여백 — 맨 위에 '고전압 상태' 제목
    private const float CircleY = -Top - Circle * 0.5f;   // 원 중심 높이(Back 위쪽 가운데 기준)
    private const float LabelY = -Top - Circle - 4f;      // 단계 이름
    private const float NextY = LabelY - 26f;             // 안내 한 줄

    private void Paint(int i, Color ring, Color disc, Color icon, float scale)
    {
        _ring[i].color = ring;
        _disc[i].color = disc;
        _icon[i].color = icon;
        _slot[i].localScale = new Vector3(scale, scale, 1f);
    }

    private void Build()
    {
        if (s_disc == null) s_disc = MakeCircle(false);
        if (s_ring == null) s_ring = MakeCircle(true);

        _canvas = new GameObject("[HvStatusHud_Canvas]");
        var cv = _canvas.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 66;
        var sc = _canvas.AddComponent<UnityEngine.UI.CanvasScaler>();
        sc.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920f, 1080f); sc.matchWidthOrHeight = 0.5f;

        var back = NewRect("Back", _canvas.transform);
        back.anchorMin = back.anchorMax = new Vector2(CenterX, 1f); back.pivot = new Vector2(0.5f, 1f);
        back.anchoredPosition = new Vector2(0f, -12f); back.sizeDelta = new Vector2(BackW, -NextY + 40f);
        // 검은 배경 상자는 두지 않는다(화면을 덜 가리게) — 아이콘 원·글자 외곽선만으로 읽히게 한다
        var title = NewText(back, "Title", 17f, new Vector2(0f, -12f), 240f, TextAlignmentOptions.Center);
        title.text = "<b>고전압 상태</b>"; title.color = new Color(0.85f, 0.88f, 0.92f, 0.95f);

        // 원 6개(5단계 + 완료 방패)를 가운데 정렬하고 사이를 선으로 잇는다. 선을 먼저 만들어 원 뒤에 깔리게 한다
        float step = Circle + Gap;
        float x0 = -step * N * 0.5f;
        for (int i = 0; i < N; i++)
        {
            var l = NewRect("Link" + i, back);
            l.anchorMin = l.anchorMax = new Vector2(0.5f, 1f); l.pivot = new Vector2(0f, 0.5f);
            l.anchoredPosition = new Vector2(x0 + i * step + Circle * 0.5f, CircleY);
            l.sizeDelta = new Vector2(Gap, 4f);
            _link[i] = l.gameObject.AddComponent<UnityEngine.UI.Image>(); _link[i].raycastTarget = false;
        }
        for (int i = 0; i <= N; i++)
        {
            var s = NewRect(i < N ? "Step" + i : "Done", back);
            s.anchorMin = s.anchorMax = new Vector2(0.5f, 1f); s.pivot = new Vector2(0.5f, 0.5f);
            s.anchoredPosition = new Vector2(x0 + i * step, CircleY); s.sizeDelta = new Vector2(Circle, Circle);
            _slot[i] = s;
            _disc[i] = Img(s, "Disc", s_disc, Circle - 4f);
            _ring[i] = Img(s, "Ring", s_ring, Circle);
            string icon = i < N ? Icons[i] : "insurance";
            _icon[i] = Img(s, "Icon", Resources.Load<Sprite>("HvSteps/" + icon), i < N ? IconSize : IconSize - 4f);
            _icon[i].preserveAspect = true;
            if (_icon[i].sprite == null) Debug.LogWarning("[HvStatusHud] 아이콘 없음: Resources/HvSteps/" + icon);
        }
        _label = NewText(back, "Label", 17f, new Vector2(0f, LabelY), 220f, TextAlignmentOptions.Center);
        _next = NewText(back, "Next", 16f, new Vector2(0f, NextY), BackW - 24f, TextAlignmentOptions.Center);
        _next.textWrappingMode = TextWrappingModes.Normal; _next.rectTransform.sizeDelta = new Vector2(BackW - 24f, 40f);
        _next.enableAutoSizing = true; _next.fontSizeMin = 12f; _next.fontSizeMax = 16f;
        if (_vr) foreach (var t in _canvas.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 1;   // TransparentFX — VR 카메라가 그리는 레이어
    }

    private static UnityEngine.UI.Image Img(RectTransform parent, string name, Sprite sp, float size)
    {
        var r = NewRect(name, parent);
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = Vector2.zero; r.sizeDelta = new Vector2(size, size);
        var im = r.gameObject.AddComponent<UnityEngine.UI.Image>();
        im.sprite = sp; im.raycastTarget = false;
        return im;
    }

    /// <summary>원판·고리 스프라이트를 코드로 만든다(빌트인 Knob 은 빌드에 안 들어갈 수 있어서). 가장자리는 1px 부드럽게.</summary>
    internal static Sprite MakeCircle(bool ring)
    {
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[S * S];
        float c = (S - 1) * 0.5f, outer = S * 0.5f - 1f, inner = outer - 7f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float a = Mathf.Clamp01(outer - d + 0.5f);
                if (ring) a *= Mathf.Clamp01(d - inner + 0.5f);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<RectTransform>();
    }

    private static TMP_Text NewText(Transform parent, string name, float size, Vector2 pos, float width, TextAlignmentOptions align)
    {
        var rt = NewRect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(width, 28f);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        var kf = MobileWorkButton.KoreanFont();
        if (kf != null) t.font = kf;
        t.fontSize = size; t.alignment = align; t.color = Color.white; t.raycastTarget = false;
        t.richText = true; t.textWrappingMode = TextWrappingModes.NoWrap;
        // 배경 상자가 없어 밝은 벽·차체 위에서도 읽히게 검은 외곽선
        t.outlineWidth = 0.22f; t.outlineColor = new Color32(0, 0, 0, 220);
        return t;
    }
}
