using UnityEngine;

// ============================================================
//  AirtightServiceVisual.cs
//  기밀 점검 장비(Game 씬 AirtightService — 배터리 팩 스폰 위치 SP_Battery 둘레)의 겉모습을 시나리오 진행에 맞춘다.
//
//  Battery12VServiceVisual 과 같은 방식 — '단계에 도달했는가 / 끝났는가'로 보이기 · 자세를 정한다.
//  클릭 대상(ClickableAnimator)은 SetActive 로 끄지 않고 렌더러 · 콜라이더만 끈다(등록 상태 유지).
//
//   · 시험기 · 헬륨 실린더: 'BSA 냉각수 배출' 단계부터 '점검 장비 탈거'가 끝날 때까지
//   · 냉각수 포트: 냉각수 라인 점검 구간(배출 ~ 잔압 배출)
//   · 피팅 [IN]/[OUT] · [OUT] 밸브: 피팅 장착 ~ 잔압 배출 (장착이 끝나면 밸브 레버가 잠김 위치로 돈다)
//   · 냉각수 점검 호스: 호스 연결 ~ 잔압 배출,  드레인 호스: 잔압 배출 단계 동안
//   · 막음 커넥터 · 실링 지그 · 어댑터 · 팩 점검 호스: 각 장착 단계 ~ 점검 장비 탈거
//   · 헬륨 밸브 핸들: 헬륨 가압 단계 동안 열림 쪽으로 돌아가 있다
//   · 누설 부위: 탐색 ~ 보수 (탐색이 끝나면 빨간 표시),  리크 디텍터: 탐색 단계 동안 누설 부위 앞으로 옮겨진다
//  기밀 점검 시나리오가 아니면 모두 숨긴다.
// ============================================================
public class AirtightServiceVisual : MonoBehaviour
{
    [Header("장비")]
    public GameObject tester;
    public GameObject helium;
    public Transform heliumValve;
    public Transform detector;

    [Header("냉각수 라인")]
    public GameObject coolantPort;
    public GameObject fittings;
    public Transform outValve;
    public GameObject coolantHose;
    public GameObject drainHose;

    [Header("배터리 팩")]
    public GameObject sealing;
    public GameObject ventJig;
    public GameObject adapter;
    public GameObject packHose;
    public GameObject leakPoint;
    public GameObject leakMarker;

    [Header("자세 (로컬)")]
    public Vector3 valveClosedEuler = new Vector3(0f, 90f, 0f);
    public Vector3 heliumOpenEuler = new Vector3(0f, 120f, 0f);
    public Vector3 detectorSearchPos = new Vector3(-1.2f, 0.12f, 0.15f);
    public Vector3 detectorSearchEuler = new Vector3(0f, 90f, 0f);

    private Quaternion _valveRot, _heliumRot, _detRot;
    private Vector3 _detPos;
    private int _shown = -1;   // 첫 프레임엔 무조건 적용

    private void Awake()
    {
        if (outValve != null) _valveRot = outValve.localRotation;
        if (heliumValve != null) _heliumRot = heliumValve.localRotation;
        if (detector != null) { _detPos = detector.localPosition; _detRot = detector.localRotation; }
    }

    private void Update()
    {
        bool s = GdsModel.IsAirtightScenario;
        bool on = s && Reached("Airtight_Coolant_Drain") && !GdsModel.Done("Airtight_Equipment_Removal");
        bool coolant = s && Reached("Airtight_Coolant_Drain") && !GdsModel.Done("Airtight_Coolant_Air_Release");

        // 상태 비트로 묶어 바뀔 때만 렌더러 · 콜라이더를 갱신
        int bits = 0, i = 0;
        void B(bool v) { if (v) bits |= 1 << i; i++; }
        B(on);                                                                                      // 0 tester / helium / detector
        B(coolant);                                                                                 // 1 port
        B(s && Reached("Airtight_Coolant_Fitting_Install") && !GdsModel.Done("Airtight_Coolant_Air_Release")); // 2 fittings
        B(s && Reached("Airtight_Coolant_Hose_Connect") && !GdsModel.Done("Airtight_Coolant_Air_Release"));    // 3 coolant hose
        B(s && Reached("Airtight_Coolant_Air_Release") && !GdsModel.Done("Airtight_Coolant_Air_Release"));     // 4 drain hose
        B(s && Reached("Airtight_Sealing_Connector_Install") && on);                                // 5 sealing
        B(s && Reached("Airtight_Venting_Jig_Install") && on);                                      // 6 jig
        B(s && Reached("Airtight_Adapter_Install") && on);                                          // 7 adapter
        B(s && Reached("Airtight_Pack_Hose_Connect") && on);                                        // 8 pack hose
        B(s && Reached("Airtight_Leak_Search") && !GdsModel.Done("Airtight_Leak_Repair"));          // 9 leak point
        B(s && GdsModel.Done("Airtight_Leak_Search") && !GdsModel.Done("Airtight_Leak_Repair"));    // 10 leak marker

        if (bits != _shown)
        {
            _shown = bits;
            bool Bit(int n) => (bits & (1 << n)) != 0;
            Show(tester, Bit(0)); Show(helium, Bit(0)); Show(detector != null ? detector.gameObject : null, Bit(0));
            Show(coolantPort, Bit(1));
            Show(fittings, Bit(2)); Show(outValve != null ? outValve.gameObject : null, Bit(2));
            Show(coolantHose, Bit(3));
            Show(drainHose, Bit(4));
            Show(sealing, Bit(5));
            Show(ventJig, Bit(6));
            Show(adapter, Bit(7));
            Show(packHose, Bit(8));
            Show(leakPoint, Bit(9));
            Show(leakMarker, Bit(10));
        }

        float k = 1f - Mathf.Exp(-8f * Time.deltaTime);
        bool valveClosed = s && GdsModel.Done("Airtight_Coolant_Fitting_Install");
        if (outValve != null)
            outValve.localRotation = Quaternion.Slerp(outValve.localRotation, valveClosed ? _valveRot * Quaternion.Euler(valveClosedEuler) : _valveRot, k);

        bool heOpen = s && GdsModel.CurrentModule == "Airtight_Helium_Inject";
        if (heliumValve != null)
            heliumValve.localRotation = Quaternion.Slerp(heliumValve.localRotation, heOpen ? _heliumRot * Quaternion.Euler(heliumOpenEuler) : _heliumRot, k);

        bool searching = s && GdsModel.CurrentModule == "Airtight_Leak_Search";
        if (detector != null)
        {
            detector.localPosition = Vector3.Lerp(detector.localPosition, searching ? detectorSearchPos : _detPos, k);
            detector.localRotation = Quaternion.Slerp(detector.localRotation, searching ? Quaternion.Euler(detectorSearchEuler) : _detRot, k);
        }
    }

    private static void Show(GameObject g, bool on)
    {
        if (g == null) return;
        foreach (var r in g.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
        foreach (var c in g.GetComponentsInChildren<Collider>(true)) c.enabled = on;
    }

    // 그 단계가 지금이거나 이미 지났는가
    private static bool Reached(string moduleId)
    {
        int i = GdsModel.IndexOf(moduleId);
        return i >= 0 && GdsModel.TaskIndex >= i;
    }
}
