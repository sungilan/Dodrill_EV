using System.Collections.Generic;
using System.Text;
using UnityEngine;

// ============================================================
//  ManualInspection.cs
//  자유 실습: 매뉴얼 '점검' 문서의 측정 항목을 부위를 눌러 확인한다(규정값은 매뉴얼 PDF 에서 읽은 숫자).
//  이미 있던 것: 브레이크 패드·디스크 두께(BrakeInspection) · 타이어 트레드·공기압(TireInspection) · 서스펜션 결함 찾기(SuspensionInspection)
//                 · 배터리 SOC/SOH/전압 · 절연·융착·셀 전압(GDS) · 기밀 점검(GDS) · 고전압 단자 전압(HvVoltageCheck)
//  여기서 추가: 휠 속도 센서 4 · 스티어링(유격·조향력) · 이베퍼레이터 온도 센서 · A/C 컴프레서 CAN 종단 · BMU G-CAN · PRA 절연 저항
//  측정값은 부위마다 고정(휠 속도 센서 뒤 오른쪽만 규정 밖) — 점검 실습용. 도구 없이 빈손 클릭(프로토타입).
//  고전압 부위(BMU·PRA)는 보호구·서비스 플러그 차단·단자 전압 확인 전에는 측정하지 못한다.
//  각 항목의 출처 문서는 Check.src 에 적어 둔다.
// ============================================================
public class ManualInspection : MonoBehaviour
{
    private struct Line
    {
        public string label, spec, value; public bool ok;
        public Line(string label, string spec, string value, bool ok = true) { this.label = label; this.spec = spec; this.value = value; this.ok = ok; }
    }

    private class Check
    {
        public string id, name, src;
        public bool hv;
        public Line[] lines;
        public string failAdvice = "교환";
    }

    private static readonly Check[] Checks =
    {
        new Check { id = "FrontWheelSpeedL", name = "앞 휠 속도 센서(왼쪽)", src = "브레이크 시스템 — ESC 프런트 휠 속도 센서 점검",
            lines = new[] { new Line("출력 전류 Low", "7 ± 1.05 mA", "7.2 mA"), new Line("출력 전류 High", "14 ± 2.1 mA", "13.8 mA"), new Line("주파수(Active)", "0.03 ~ 3000 Hz", "시험 회전 시 120 Hz") } },
        new Check { id = "FrontWheelSpeedR", name = "앞 휠 속도 센서(오른쪽)", src = "브레이크 시스템 — ESC 프런트 휠 속도 센서 점검",
            lines = new[] { new Line("출력 전류 Low", "7 ± 1.05 mA", "6.6 mA"), new Line("출력 전류 High", "14 ± 2.1 mA", "14.3 mA"), new Line("주파수(Active)", "0.03 ~ 3000 Hz", "시험 회전 시 118 Hz") } },
        new Check { id = "RearWheelSpeedL", name = "뒤 휠 속도 센서(왼쪽)", src = "브레이크 시스템 — ESC 리어 휠 속도 센서 점검",
            lines = new[] { new Line("출력 전류 Low", "7 ± 1.05 mA", "7.4 mA"), new Line("출력 전류 High", "14 ± 2.1 mA", "13.1 mA"), new Line("주파수", "0.03 ~ 3000 Hz", "시험 회전 시 121 Hz") } },
        new Check { id = "RearWheelSpeedR", name = "뒤 휠 속도 센서(오른쪽)", src = "브레이크 시스템 — ESC 리어 휠 속도 센서 점검", failAdvice = "센서 교환",
            lines = new[] { new Line("출력 전류 Low", "7 ± 1.05 mA", "4.1 mA", false), new Line("출력 전류 High", "14 ± 2.1 mA", "8.0 mA", false), new Line("주파수", "0.03 ~ 3000 Hz", "신호 없음", false) } },
        new Check { id = "SteeringColumn", name = "스티어링 휠 · 컬럼", src = "스티어링 시스템 — 점검(MDPS ON 상태)", failAdvice = "조정·수리",
            lines = new[] { new Line("스티어링 휠 자유 유격", "0 ~ 30 mm", "14 mm"), new Line("조향력(MDPS ON)", "3.0 kgf 이하", "2.4 kgf") } },
        new Check { id = "Hvac", name = "이베퍼레이터 온도 센서", src = "히터 및 에어컨 장치 — 이베퍼레이터 온도 센서 점검",
            lines = new[] { new Line("센서 저항 (4 ℃)", "25.32 kΩ ± 1%", "25.4 kΩ") } },
        new Check { id = "AcCompressor", name = "A/C 컴프레서 통신선", src = "히터 및 에어컨 장치 — 전동식 에어컨 컴프레서 점검",
            lines = new[] { new Line("CAN High–Low 종단 저항", "120 Ω", "121 Ω") } },
        new Check { id = "Bmu", name = "BMU G-CAN", src = "배터리 제어 시스템 — 배터리 매니지먼트 유닛(BMU) 점검", hv = true,
            lines = new[] { new Line("G-CAN Low 전압", "1.5 ~ 3.5 V", "2.4 V"), new Line("G-CAN High 전압", "1.5 ~ 3.5 V", "2.6 V"), new Line("G-CAN 종단 저항", "120 Ω", "119 Ω") } },
        new Check { id = "Pra", name = "PRA 단자 절연 저항", src = "배터리 제어 시스템 — 절연 저항 점검 (1000 V, 1분 인가)", hv = true,
            lines = new[] { new Line("PRA(+) – 차체 절연 저항", "2 MΩ 이상 (20 ℃)", "3.0 MΩ"), new Line("PRA(−) – 차체 절연 저항", "2 MΩ 이상 (20 ℃)", "3.1 MΩ") } },
    };

    /// <summary>부위 목록 패널에 붙일 한 줄(점검을 시작하지 않았으면 null)</summary>
    public static string Summary { get; private set; }

    private readonly bool[] _seen = new bool[Checks.Length];
    private readonly GameObject[] _go = new GameObject[Checks.Length];
    private float _next;

    private void OnEnable() { Summary = null; }

    private void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.4f;
        var svc = CarPartService.Instance;
        if (svc == null || !svc.Ready) return;
        bool tool = CarPartService.LocalHoldsBoltTool;

        for (int i = 0; i < Checks.Length; i++)
        {
            int p = CarPartService.IndexOf(Checks[i].id);
            bool on = !tool && p >= 0 && svc.StateOf(p) == (int)CarPartService.PartState.Tight
                      && svc.TryGetPartAnchor(p, out _, out _, out _);
            if (!on) { if (_go[i] != null && _go[i].activeSelf) _go[i].SetActive(false); continue; }

            svc.TryGetPartAnchor(p, out var anchor, out var local, out int layer);
            if (_go[i] == null) Build(i, anchor);
            if (_go[i].transform.parent != anchor) _go[i].transform.SetParent(anchor, false);
            _go[i].transform.localPosition = local;
            _go[i].layer = layer;
            if (!_go[i].activeSelf) _go[i].SetActive(true);
        }
    }

    private void Build(int i, Transform anchor)
    {
        var go = new GameObject("[ManualInspect_" + Checks[i].id + "]");
        go.transform.SetParent(anchor, false);
        var sc = go.AddComponent<SphereCollider>();
        sc.radius = 0.07f / Mathf.Max(0.0001f, anchor.lossyScale.x);
        int idx = i;
        var proxy = go.AddComponent<ClickProxy>();
        proxy.displayName = Checks[i].name;
        proxy.vrPokeable = true;
        proxy.hover = touch => _seen[idx] ? "다시 누르면 측정 결과를 봅니다" : "눌러서 매뉴얼 점검 측정";
        proxy.onClick = () => Measure(idx);
        _go[i] = go;
    }

    private void Measure(int i)
    {
        var c = Checks[i];
        if (c.hv)
        {
            var sm = ElectricSafetyManager.Instance;
            bool safe = sm == null || sm.IsSafeToWork();
            if (!safe || HvVoltageCheck.NeedsCheck)
            {
                EquipFx.Notice("minsp_hv", "고전압 차단 필요 — " + c.name, "보호구 → 12V(−) → 서비스 플러그 분리 → 3분 방전 → 인버터 단자 전압 30V 이하 확인 뒤에 측정하세요", EquipFx.Danger, 5f, 4f);
                return;
            }
        }
        bool first = !_seen[i];
        _seen[i] = true;
        var sb = new StringBuilder();
        bool allOk = true;
        foreach (var l in c.lines)
        {
            allOk &= l.ok;
            sb.Append(l.label).Append(": ").Append(l.ok ? "<color=#2ECC71>" : "<color=#FF5252>").Append(l.value).Append("</color>")
              .Append(" (규정 ").Append(l.spec).Append(")\n");
        }
        sb.Append(allOk ? "<color=#2ECC71><b>판정: 정상</b></color>" : "<color=#FF5252><b>판정: 규정 밖 — " + c.failAdvice + "</b></color>");
        sb.Append("\n<size=70%><color=#9FB4C8>").Append(c.src).Append("</color></size>");
        EquipFx.Notice("minsp_" + c.id, c.name + " 점검", sb.ToString(), allOk ? EquipFx.Info : EquipFx.Danger, 8f, 1f);
        if (first) UpdateSummary();
    }

    private void UpdateSummary()
    {
        int seen = 0, bad = 0;
        for (int i = 0; i < Checks.Length; i++)
        {
            if (!_seen[i]) continue;
            seen++;
            foreach (var l in Checks[i].lines) if (!l.ok) { bad++; break; }
        }
        Summary = $"매뉴얼 측정 점검 {seen}/{Checks.Length} · 규정 밖 {bad}";
    }

    private void OnDestroy()
    {
        foreach (var g in _go) if (g != null) Destroy(g);
        Summary = null;
    }
}
