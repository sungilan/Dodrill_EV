using UnityEngine;

// ============================================================
//  BrakeInspection.cs
//  자유 실습: 휠을 떼어 드러난 브레이크(캘리퍼·디스크)를 눌러 패드·디스크 마모를 점검한다.
//  매뉴얼 「브레이크 패드·디스크 브레이크 점검」 규정값:
//    패드 두께  앞 규정 11.0 / 뒤 10.0 mm · 정비 한계 2.0 mm (한계 이하면 패드 어셈블리 교환)
//    디스크 두께  앞 규정 34 / 한계 32 mm · 뒤 규정 20 / 한계 18 mm
//  측정값은 휠마다 고정(앞 오른쪽·뒤 오른쪽이 마모 심함, 뒤 오른쪽은 한계 이하) — '4개 다 재 보고 교체할 곳을 찾는' 점검 실습용.
//  버니어 캘리퍼스 도구는 아직 없어 빈손 클릭으로 잰다(프로토타입).
//  브레이크 부위는 CarPartService 의 BrakeFL/FR/RL/RR — 휠이 떼어져 있고 브레이크가 달려 있을 때만 눌린다.
// ============================================================
public class BrakeInspection : MonoBehaviour
{
    private static readonly string[] Ids = { "BrakeFL", "BrakeFR", "BrakeRL", "BrakeRR" };
    private static readonly string[] Names = { "앞 왼쪽", "앞 오른쪽", "뒤 왼쪽", "뒤 오른쪽" };
    private static readonly float[] Pad = { 8.4f, 2.7f, 7.1f, 1.6f };
    private static readonly float[] Disc = { 33.6f, 32.3f, 19.5f, 17.6f };
    private static readonly float[] Runout = { 0.021f, 0.038f, 0.030f, 0.071f };      // mm
    private static readonly float[] Variation = { 0.008f, 0.012f, 0.009f, 0.022f };   // mm
    private const float PadStdF = 11f, PadStdR = 10f, PadLimit = 2f;
    private const float DiscStdF = 34f, DiscLimF = 32f, DiscStdR = 20f, DiscLimR = 18f;

    private readonly GameObject[] _go = new GameObject[4];
    private readonly bool[] _done = new bool[4];
    private float _next;

    private void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.4f;
        var svc = CarPartService.Instance;
        var ts = TireService.Instance;
        if (svc == null || !svc.Ready || ts == null) return;

        for (int i = 0; i < 4; i++)
        {
            int p = CarPartService.IndexOf(Ids[i]);
            bool on = p >= 0 && svc.StateOf(p) == (int)CarPartService.PartState.Tight && ts.IsWheelOff(i)
                      && svc.TryGetPartAnchor(p, out _, out _, out _);
            if (!on) { if (_go[i] != null && _go[i].activeSelf) _go[i].SetActive(false); continue; }

            if (_go[i] == null || _go[i].transform.parent == null) Build(i, svc, p);
            else
            {
                svc.TryGetPartAnchor(p, out var anchor, out var local, out int layer);
                if (_go[i].transform.parent != anchor) { _go[i].transform.SetParent(anchor, false); }
                _go[i].transform.localPosition = local;
                _go[i].layer = layer;
            }
            if (_go[i] != null && !_go[i].activeSelf) _go[i].SetActive(true);
        }
    }

    private void Build(int i, CarPartService svc, int p)
    {
        if (!svc.TryGetPartAnchor(p, out var anchor, out var local, out int layer)) return;
        var go = new GameObject("[BrakeInspect_" + Ids[i] + "]");
        go.layer = layer;
        go.transform.SetParent(anchor, false);
        go.transform.localPosition = local;
        var sc = go.AddComponent<SphereCollider>();
        // 앵커는 스케일이 있을 수 있다 → 월드 기준 약 10cm 가 되도록 보정
        float s = Mathf.Max(0.0001f, anchor.lossyScale.x);
        sc.radius = 0.1f / s;
        int idx = i;
        var proxy = go.AddComponent<ClickProxy>();
        proxy.displayName = Names[i] + " 브레이크";
        proxy.vrPokeable = true;
        proxy.hover = touch => _done[idx] ? "다시 누르면 점검 결과를 다시 봅니다" : "눌러서 패드·디스크 마모를 점검";
        proxy.onClick = () => Measure(idx);
        _go[i] = go;
    }

    private void Measure(int i)
    {
        _done[i] = true;
        bool front = i < 2;
        float pad = Pad[i], disc = Disc[i];
        float padStd = front ? PadStdF : PadStdR;
        float dStd = front ? DiscStdF : DiscStdR, dLim = front ? DiscLimF : DiscLimR;

        bool padBad = pad <= PadLimit, padWarn = !padBad && pad <= PadLimit + 1.2f;
        bool discBad = disc <= dLim;

        string pv = padBad ? "<color=#FF5252>한계 이하 — 패드 어셈블리 교환</color>"
                  : padWarn ? "<color=#FFB000>마모 많음 — 곧 교환</color>" : "<color=#2ECC71>정상</color>";
        string dv = discBad ? "<color=#FF5252>한계 이하 — 디스크 교환</color>" : "<color=#2ECC71>정상</color>";
        // 디스크 런아웃(휠 허브 장착 상태)·두께 편차 — 매뉴얼: 두께 편차 0.015 mm 이하, 런아웃 앞 0.050 / 뒤 0.055 mm 이하
        float runLim = front ? 0.050f : 0.055f, varLim = 0.015f;
        bool runBad = Runout[i] > runLim, varBad = Variation[i] > varLim;
        string rv = runBad ? "<color=#FF5252>규정 초과 — 디스크 교환/연삭</color>" : "<color=#2ECC71>정상</color>";
        string vv = varBad ? "<color=#FF5252>규정 초과 — 디스크 교환</color>" : "<color=#2ECC71>정상</color>";
        string msg = $"패드 두께 {pad:0.0} mm (규정 {padStd:0.0} · 한계 {PadLimit:0.0}) {pv}\n디스크 두께 {disc:0.0} mm (규정 {dStd:0} · 한계 {dLim:0}) {dv}\n" +
                     $"디스크 두께 편차 {Variation[i]:0.000} mm (한계 {varLim:0.000}) {vv}\n디스크 런아웃 {Runout[i]:0.000} mm (한계 {runLim:0.000}) {rv}";
        EquipFx.Notice("brake_inspect_" + i, Names[i] + " 브레이크 점검", msg,
            padBad || discBad || runBad || varBad ? EquipFx.Danger : padWarn ? EquipFx.Warn : EquipFx.Info, 9f, 1f);
    }

    private void OnDestroy()
    {
        foreach (var g in _go) if (g != null) Destroy(g);
    }
}
