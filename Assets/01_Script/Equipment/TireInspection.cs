using UnityEngine;

// ============================================================
//  TireInspection.cs
//  자유 실습: 차에 달린 타이어 윗부분을 눌러 트레드 깊이와 공기압을 점검한다.
//  매뉴얼 「타이어 점검」: 트레드 깊이가 한계(1.6 mm) 이하이면 타이어 교환.
//  공기압 규정값은 매뉴얼에 없어 임시 기준(36 psi, 허용 ±3)을 쓴다 — 실제 값으로 바꾸려면 PsiSpec 만 고치면 된다.
//  공기압이 모자란 타이어는 한 번 더 누르면 규정까지 채운다(공기 주입 · 서버를 거쳐 모두에게 같게).
//  측정값은 휠마다 고정: 앞 오른쪽 트레드 한계 이하, 뒤 왼쪽 공기압 부족.
//  휠이 떼어져 있으면(분해 중) 누를 수 없다. 클릭 대상은 타이어 맨 위(너트 자리와 떨어진 곳).
// ============================================================
public class TireInspection : MonoBehaviour
{
    private static readonly string[] Names = { "앞 왼쪽", "앞 오른쪽", "뒤 왼쪽", "뒤 오른쪽" };
    private static readonly float[] Tread = { 6.1f, 1.4f, 4.8f, 5.3f };
    private static readonly float[] StartPsi = { 35f, 36f, 27f, 34f };
    private static readonly float[] WheelRunout = { 0.10f, 0.12f, 0.42f, 0.08f };   // mm — 뒤 왼쪽 휠이 규정 밖
    private static readonly float[] BalanceG = { 35f, 48f, 52f, 40f };   // g — 모두 규정 안(휠 발란스 점검 문서: 클립식 60 g · 접착식 100 g)
    private const float WheelRunoutLimit = 0.3f;
    private const float TreadLimit = 1.6f, TreadNew = 8f;
    private const float PsiSpec = 36f, PsiTol = 3f;

    private readonly float[] _psi = new float[4];
    private readonly GameObject[] _go = new GameObject[4];
    private readonly bool[] _checked = new bool[4];
    private readonly PsiState[] _states = new PsiState[4];
    private float _next;

    private class PsiState : IVehicleState
    {
        public TireInspection o; public int w;
        public string StateId => "veh/tirepsi" + w;
        public void ApplyState(float v) { o._psi[w] = Mathf.Clamp(v, 0f, 80f); }
    }

    private void Awake()
    {
        for (int i = 0; i < 4; i++)
        {
            _psi[i] = StartPsi[i];
            _states[i] = new PsiState { o = this, w = i };
            VehicleStateSync.Register(_states[i]);
        }
    }

    private void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.4f;
        var ts = TireService.Instance;
        if (ts == null) return;

        for (int i = 0; i < 4; i++)
        {
            bool on = ts.IsWheelTight(i) && ts.TryGetWheel(i, out _, out _);
            if (!on) { if (_go[i] != null && _go[i].activeSelf) _go[i].SetActive(false); continue; }
            ts.TryGetWheel(i, out var wheel, out int layer);

            if (_go[i] == null) Build(i);
            _go[i].layer = layer;
            // 타이어 맨 위: 바퀴 중심에서 월드 위로 (렌더러 크기로 반지름을 잰다)
            float r = 0.34f;
            var rend = wheel.GetComponentInChildren<Renderer>();
            if (rend != null) r = Mathf.Clamp(Mathf.Min(rend.bounds.extents.y, rend.bounds.extents.z) * 0.95f, 0.2f, 0.5f);
            _go[i].transform.position = ts.HubCenter(i) + Vector3.up * r;
            if (!_go[i].activeSelf) _go[i].SetActive(true);
        }
    }

    private void Build(int i)
    {
        var go = new GameObject("[TireInspect_" + i + "]");
        var sc = go.AddComponent<SphereCollider>();
        sc.radius = 0.09f;
        int idx = i;
        var proxy = go.AddComponent<ClickProxy>();
        proxy.displayName = Names[i] + " 타이어";
        proxy.vrPokeable = true;
        proxy.hover = touch => !_checked[idx] ? "눌러서 트레드·공기압 점검"
                             : _psi[idx] < PsiSpec - PsiTol ? "눌러서 공기 주입" : "눌러서 점검 결과 보기";
        proxy.onClick = () => OnClick(idx);
        _go[i] = go;
    }

    private void OnClick(int i)
    {
        bool low = _psi[i] < PsiSpec - PsiTol;
        if (_checked[i] && low)
        {
            // 공기 주입: 규정까지
            VehicleStateSync.Request("veh/tirepsi" + i, PsiSpec);
            AudioSource.PlayClipAtPoint(EquipFx.Click, _go[i].transform.position, 0.6f);
            EquipFx.Notice("tire_fill_" + i, Names[i] + " 타이어", $"공기를 채웠습니다 — {PsiSpec:0} psi", EquipFx.Info, 3f, 1f);
            return;
        }
        _checked[i] = true;
        float t = Tread[i], p = _psi[i];
        bool treadBad = t <= TreadLimit, treadWarn = !treadBad && t <= TreadLimit + 1.6f;
        bool pLow = p < PsiSpec - PsiTol, pHigh = p > PsiSpec + PsiTol;

        string tv = treadBad ? "<color=#FF5252>한계 이하 — 타이어 교환</color>" : treadWarn ? "<color=#FFB000>마모 많음</color>" : "<color=#2ECC71>정상</color>";
        string pv = pLow ? "<color=#FFB000>부족 — 한 번 더 눌러 공기 주입</color>" : pHigh ? "<color=#FFB000>과다</color>" : "<color=#2ECC71>정상</color>";
        // 휠 림 런아웃(종·횡) — 매뉴얼 '휠 점검': 0.3 mm 이하
        bool wBad = WheelRunout[i] > WheelRunoutLimit;
        string wv = wBad ? "<color=#FF5252>규정 초과 — 휠 교환</color>" : "<color=#2ECC71>정상</color>";
        string msg = $"트레드 깊이 {t:0.0} mm (신품 {TreadNew:0} · 한계 {TreadLimit:0.0}) {tv}\n공기압 {p:0} psi (기준 {PsiSpec:0}±{PsiTol:0}) {pv}\n" +
                     $"휠 런아웃 {WheelRunout[i]:0.00} mm (한계 {WheelRunoutLimit:0.0}) {wv}\n" +
                     $"휠 발란스 필요 웨이트 {BalanceG[i]:0} g (클립식 한계 60 g · 접착식 한계 100 g · 최대 불균형 130 g) <color=#2ECC71>정상</color>";
        EquipFx.Notice("tire_inspect_" + i, Names[i] + " 타이어 점검", msg,
            (treadBad || wBad) ? EquipFx.Danger : (treadWarn || pLow || pHigh) ? EquipFx.Warn : EquipFx.Info, 8f, 1f);
    }

    private void OnDestroy()
    {
        foreach (var g in _go) if (g != null) Destroy(g);
        foreach (var s in _states) VehicleStateSync.Unregister(s);
    }
}
