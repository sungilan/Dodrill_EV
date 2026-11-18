using UnityEngine;

// ============================================================
//  HvVoltageCheck.cs
//  자유 실습: 고전압 차단 뒤 '단자 잔류 전압 확인'.
//  매뉴얼 고전압 차단 절차 마지막 단계 — 보호구 → 12V(−) → 서비스 플러그(MSD) → 방전 대기 → 인버터 단자 전압 30 V 이하 확인.
//  서비스 플러그를 뽑은 뒤 인버터(EPCU) 단자를 눌러 전압을 잰다. 방전 중에는 전압이 지수로 내려가며(약 3분에 30 V — 매뉴얼: 서비스 플러그 분리 후 3분 대기, 단자 전압 30 V 이하),
//  30 V 이하가 되기 전에 재면 '아직 위험' 이다. 30 V 이하로 확인하기 전에 고전압 부품 볼트를 돌리면
//  CarPartService 가 '단자 전압 확인 전' 경고를 띄운다(막지는 않음). 시나리오 중에는 시나리오 단계가 확인을 맡으므로 쓰지 않는다.
//  멀티미터 도구 대신 빈손 클릭으로 잰다(프로토타입). 확인 결과는 각자 로컬 — 보호구·MSD 가 해제되면 다시 확인해야 한다.
// ============================================================
public class HvVoltageCheck : MonoBehaviour
{
    private static readonly string[] TerminalParts = { "FrontInverter", "JunctionBlock", "Iccu" };
    private const float PackV = 650f, Tau = 58f, SafeV = 30f;

    /// <summary>이번 차단에서 단자 전압(30 V 이하)을 확인했는가</summary>
    public static bool Verified { get; private set; }

    /// <summary>자유 탈거 연습 보드(FreeLoto)가 단자 전압 확인 결과를 넣는다</summary>
    public static void MarkVerified(bool v) { Verified = v; }

    /// <summary>자유 실습이고 보호구·MSD 는 갖췄는데 단자 전압 확인만 안 했다</summary>
    public static bool NeedsCheck
    {
        get
        {
            var sm = ElectricSafetyManager.Instance;
            return sm != null && !sm.bypassSafetyForTesting && !sm.IsScenarioActive && !Verified;
        }
    }

    private GameObject _go;
    private float _msdAt = -1f, _next;
    private bool _prevMsd;

    private void OnEnable() { Verified = false; }

    private void Update()
    {
        var sm = ElectricSafetyManager.Instance;
        if (sm != null)
        {
            // 서비스 플러그를 막 뽑은 순간부터 방전 시간을 센다. 다시 꽂으면(차단 해제) 확인도 처음부터
            if (sm.isMSDRemoved && !_prevMsd) _msdAt = Time.time;
            if (!sm.isMSDRemoved && _prevMsd) { _msdAt = -1f; Verified = false; }
            _prevMsd = sm.isMSDRemoved;
        }

        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.5f;
        var svc = CarPartService.Instance;
        if (svc == null || !svc.Ready) return;

        int p = -1;
        foreach (var id in TerminalParts)
        {
            int q = CarPartService.IndexOf(id);
            if (q >= 0 && svc.StateOf(q) == (int)CarPartService.PartState.Tight && svc.TryGetPartAnchor(q, out _, out _, out _)) { p = q; break; }
        }
        // 자유 탈거(시나리오 ev_free_disassembly)는 시나리오처럼 멀티미터로 잰다(FreeLoto.OnMeasured) — 빈손 클릭 측정은 끈다
        bool on = p >= 0 && !CarPartService.LocalHoldsBoltTool && !FreeLoto.IsFreeScenario;
        if (!on) { if (_go != null && _go.activeSelf) _go.SetActive(false); return; }

        svc.TryGetPartAnchor(p, out var anchor, out var local, out int layer);
        if (_go == null) Build(anchor);
        if (_go.transform.parent != anchor) _go.transform.SetParent(anchor, false);
        _go.transform.localPosition = local;
        _go.layer = layer;
        if (!_go.activeSelf) _go.SetActive(true);
    }

    private void Build(Transform anchor)
    {
        _go = new GameObject("[HvVoltageCheck]");
        _go.transform.SetParent(anchor, false);
        var sc = _go.AddComponent<SphereCollider>();
        sc.radius = 0.1f / Mathf.Max(0.0001f, anchor.lossyScale.x);
        var proxy = _go.AddComponent<ClickProxy>();
        proxy.displayName = "고전압 DC 단자";
        proxy.vrPokeable = true;
        proxy.hover = touch => Verified ? "단자 전압 확인 완료 (다시 누르면 재측정)" : "눌러서 단자 잔류 전압 측정 (30 V 이하 확인)";
        proxy.onClick = Measure;
    }

    private void Measure()
    {
        var sm = ElectricSafetyManager.Instance;
        if (sm == null) return;
        if (!sm.isGlovesEquipped)
        {
            EquipFx.Notice("hvv_glove", "보호구 필요", "고전압 단자에 접근하기 전에 절연 장갑을 착용하세요", EquipFx.Danger, 4f, 3f);
            return;
        }
        if (!sm.isMSDRemoved)
        {
            EquipFx.Notice("hvv_live", "고전압 활선", $"단자 전압 {PackV:0} V — 서비스 플러그(MSD)를 아직 뽑지 않았습니다. 먼저 12V(−)·서비스 플러그를 차단하세요", EquipFx.Danger, 5f, 3f);
            return;
        }
        float t = _msdAt >= 0f ? Time.time - _msdAt : 9999f;
        float v = PackV * Mathf.Exp(-t / Tau);
        if (v <= SafeV)
        {
            bool first = !Verified;
            Verified = true;
            EquipFx.Notice("hvv_ok", "단자 전압 확인", $"{Mathf.Max(0.1f, v):0.0} V — {SafeV:0} V 이하, 고전압 부품 작업을 시작할 수 있습니다", EquipFx.Info, first ? 5f : 3f, 1f);
        }
        else
        {
            Verified = false;
            float left = Tau * Mathf.Log(v / SafeV);
            EquipFx.Notice("hvv_wait", "방전 중 — 아직 위험", $"단자 전압 {v:0} V ({SafeV:0} V 초과) — 약 {Mathf.CeilToInt(left)}초 더 기다린 뒤 다시 재세요", EquipFx.Danger, 5f, 2f);
        }
    }

    private void OnDestroy()
    {
        if (_go != null) Destroy(_go);
        Verified = false;
    }
}
