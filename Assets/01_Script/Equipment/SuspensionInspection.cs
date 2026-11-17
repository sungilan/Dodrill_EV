using UnityEngine;

// ============================================================
//  SuspensionInspection.cs
//  자유 실습: 서스펜션 12개 부위(스트럿·쇽·로어 암·스태빌라이저·텐션 암)를 눌러 결함을 찾는 '점검' 실습.
//  매뉴얼 서스펜션 점검 항목 — 쇽 업소버 오일 누유, 부싱 균열, 스태빌라이저 링크 유격 등을 눈으로·손으로 확인한다.
//  결함은 3곳에 고정(앞 오른쪽 스트럿 누유 · 뒤 왼쪽 로어 암 부싱 균열 · 프런트 스태빌라이저 링크 유격)이고
//  나머지는 이상 없음. 12곳을 모두 점검하면 완료 알림. 진행은 부위 목록 패널에 표시(Summary).
//  공구(볼트 도구)를 든 동안은 클릭 대상을 끈다 — 볼트 조작과 겹치지 않게. 점검 결과는 각자 로컬이다.
// ============================================================
public class SuspensionInspection : MonoBehaviour
{
    private struct Item
    {
        public string id, name, defect;   // defect == null → 이상 없음
        public Item(string id, string name, string defect = null) { this.id = id; this.name = name; this.defect = defect; }
    }

    private static readonly Item[] Items =
    {
        new Item("FrontStrutL", "프런트 스트럿(왼쪽)"),
        new Item("FrontStrutR", "프런트 스트럿(오른쪽)", "쇽 업소버 오일 누유 — 로드 주위가 젖어 있습니다 (스트럿 교환)"),
        new Item("RearShockL", "리어 쇽 업소버(왼쪽)"),
        new Item("RearShockR", "리어 쇽 업소버(오른쪽)"),
        new Item("FrontLowerArmL", "프런트 로어 암(왼쪽)"),
        new Item("FrontLowerArmR", "프런트 로어 암(오른쪽)"),
        new Item("RearLowerArmL", "리어 로어 암(왼쪽)", "부싱 고무 균열 — 균열이 깊습니다 (로어 암 교환)"),
        new Item("RearLowerArmR", "리어 로어 암(오른쪽)"),
        new Item("FrontStabilizer", "프런트 스태빌라이저 바", "스태빌라이저 링크 볼 조인트 유격 — 흔들면 덜컥거립니다 (링크 교환)"),
        new Item("RearStabilizer", "리어 스태빌라이저 바"),
        new Item("TensionArmL", "프런트 텐션 암(왼쪽)"),
        new Item("TensionArmR", "프런트 텐션 암(오른쪽)"),
    };

    /// <summary>부위 목록 패널에 붙일 한 줄(점검을 시작하지 않았으면 null)</summary>
    public static string Summary { get; private set; }

    private readonly bool[] _seen = new bool[Items.Length];
    private readonly GameObject[] _go = new GameObject[Items.Length];
    private float _next;

    private static int DefectTotal() { int n = 0; foreach (var it in Items) if (it.defect != null) n++; return n; }

    private void OnEnable() { Summary = null; }

    private void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.4f;
        var svc = CarPartService.Instance;
        if (svc == null || !svc.Ready) return;
        bool tool = CarPartService.LocalHoldsBoltTool;

        for (int i = 0; i < Items.Length; i++)
        {
            int p = CarPartService.IndexOf(Items[i].id);
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
        var go = new GameObject("[SuspInspect_" + Items[i].id + "]");
        go.transform.SetParent(anchor, false);
        var sc = go.AddComponent<SphereCollider>();
        sc.radius = 0.08f / Mathf.Max(0.0001f, anchor.lossyScale.x);
        int idx = i;
        var proxy = go.AddComponent<ClickProxy>();
        proxy.displayName = Items[i].name;
        proxy.vrPokeable = true;
        proxy.hover = touch => _seen[idx] ? "다시 누르면 점검 결과를 봅니다" : "눌러서 결함 점검 (누유·균열·유격)";
        proxy.onClick = () => Inspect(idx);
        _go[i] = go;
    }

    private void Inspect(int i)
    {
        bool first = !_seen[i];
        _seen[i] = true;
        var it = Items[i];
        if (it.defect != null)
            EquipFx.Notice("susp_" + i, it.name + " 점검", "<color=#FF5252><b>결함 발견</b></color>\n" + it.defect, EquipFx.Danger, 6f, 1f);
        else
            EquipFx.Notice("susp_" + i, it.name + " 점검", "<color=#2ECC71>이상 없음</color> — 누유·균열·유격이 보이지 않습니다", EquipFx.Info, 3.5f, 1f);
        if (first) UpdateSummary();
    }

    private void UpdateSummary()
    {
        int seen = 0, found = 0;
        for (int i = 0; i < Items.Length; i++)
        {
            if (!_seen[i]) continue;
            seen++;
            if (Items[i].defect != null) found++;
        }
        int total = DefectTotal();
        Summary = $"서스펜션 점검 {seen}/{Items.Length} · 결함 발견 {found}/{total}";
        if (seen == Items.Length)
            EquipFx.Notice("susp_done", "서스펜션 점검 완료", $"{Items.Length}곳을 모두 점검했습니다 — 결함 {found}곳 발견, 해당 부품을 교환하세요", EquipFx.Info, 6f, 1f);
    }

    private void OnDestroy()
    {
        foreach (var g in _go) if (g != null) Destroy(g);
        Summary = null;
    }
}
