using UnityEngine;
using TMPro;
using System.Collections;
using Autohand;
using FishNet;

// ============================================================
//  GDS_TabletController.cs
//  GDS 진단기 태블릿 — OBD 연결 → DTC 감지 → 시나리오 자동 시작
//
//  전체 흐름:
//    1. 태블릿을 집어서 OBD_Port 콜라이더에 접촉
//    2. connectDelay 후 DiagnosticSystem에서 DTC 목록 읽기
//    3. LCD에 고장코드 표시
//    4. ★ DTC → scenarioId 변환 → ScenarioDistributor.LoadAndBroadcast()
//       → ScenarioRunner 시작 → EVModules 12단계 실행
//    5. 정비 완료 후 재연결 → [소거] 버튼 → FinalDiagnosis 완료
//
//  씬 연결:
//    distributor 필드에 ScenarioDistributor 드래그 연결 필수.
//    OBD_Port 오브젝트에 Tag="OBD_Port" 설정.
// ============================================================

[RequireComponent(typeof(TaskItem))]
public class GDS_TabletController : MonoBehaviour
{
    [Header("LCD 디스플레이")]
    public TextMeshProUGUI lcdText;

    [Header("시나리오 연결 — 인스펙터에서 드래그 연결")]
    [Tooltip("ScenarioDistributor — DTC 감지 후 시나리오 시작에 사용")]
    public ScenarioDistributor distributor;

    [Header("연결 설정")]
    [Tooltip("OBD 포트 감지용 Tag")]
    public string obdPortTag = "OBD_Port";
    [Tooltip("연결 애니메이션 시간 (초)")]
    public float connectDelay = 1.5f;

    [Header("상태 (읽기 전용)")]
    public bool isConnected = false;
    public bool scenarioStarted = false;

    private TaskItem _taskItem;
    private Coroutine _connectRoutine;

    // ═══════════════════════════════════════════
    //  생명주기
    // ═══════════════════════════════════════════

    private void Awake()
    {
        _taskItem = GetComponent<TaskItem>();
    }

    private void Start()
    {
        if (distributor == null)
            Debug.LogWarning("[GDS] ScenarioDistributor가 연결되지 않았습니다. 인스펙터에서 드래그 연결하세요.");
    }

    private void OnDisable()
    {
        if (_connectRoutine != null) StopCoroutine(_connectRoutine);
    }

    // ═══════════════════════════════════════════
    //  OBD 포트 접촉 감지
    // ═══════════════════════════════════════════

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(obdPortTag)) return;
        if (isConnected) return;

        if (_connectRoutine != null) StopCoroutine(_connectRoutine);
        _connectRoutine = StartCoroutine(ConnectRoutine());
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(obdPortTag)) return;
        Disconnect();
    }

    // ═══════════════════════════════════════════
    //  연결 로직 — 핵심 흐름
    // ═══════════════════════════════════════════

    private IEnumerator ConnectRoutine()
    {
        // 1. 연결 애니메이션
        SetLCD("차량 통신 연결 중...");
        yield return new WaitForSeconds(connectDelay);

        // 2. DiagnosticSystem에 스캐너 연결 알림
        DiagnosticSystem.Instance?.ConnectScanner();
        isConnected = true;

        // 3. DTC 목록 LCD 표시
        ShowDTCList();

        // 4. ★ DTC → 시나리오 자동 시작 (최초 연결 시 1회만)
        if (!scenarioStarted)
            TryStartScenarioFromDTC();

        // 5. 기존 ScenarioRunner 이벤트 발행 (InitialDiagnosis Task 완료용)
        InteractionEvents.FireZoneActivated("OBD_Zone", _taskItem?.prefabId ?? "GDS_Tablet");

        Debug.Log("[GDS] OBD 연결 완료");
    }

    // ═══════════════════════════════════════════
    //  DTC → 시나리오 시작 (핵심 연결 고리)
    // ═══════════════════════════════════════════

    private void TryStartScenarioFromDTC()
    {
        var diag = DiagnosticSystem.Instance;
        if (diag == null || !diag.HasActiveDTC)
        {
            Debug.Log("[GDS] 활성 DTC 없음 — 시나리오 시작 스킵");
            return;
        }

        // 서버에서만 시나리오 시작 (클라이언트는 브로드캐스트 수신)
        if (!InstanceFinder.IsServerStarted)
        {
            Debug.Log("[GDS] 클라이언트 — 서버에서 시나리오 시작 대기 중");
            return;
        }

        string dtcCode = diag.GetPrimaryDTCCode();
        string scenarioId = diag.GetScenarioId();

        if (string.IsNullOrEmpty(scenarioId))
        {
            Debug.LogWarning($"[GDS] DTC '{dtcCode}'에 매핑된 시나리오 없음\n" +
                             "DiagnosticSystem.dtcPool의 scenarioId 필드를 설정하거나\n" +
                             "DiagnosticSystem.GetScenarioId() 스위치에 추가하세요.");
            SetLCD($"<color=#FF4444>고장코드: {dtcCode}</color>\n<color=#FFAA00>시나리오 데이터 없음</color>");
            return;
        }

        if (distributor == null)
        {
            Debug.LogError("[GDS] ScenarioDistributor 미연결 — 인스펙터에서 드래그 연결 필요");
            return;
        }

        scenarioStarted = true;

        Debug.Log($"[GDS] DTC {dtcCode} → 시나리오 시작: {scenarioId}");
        SetLCD($"<color=#FF4444>감지: {dtcCode}</color>\n시나리오 로드 중...");

        // ScenarioDistributor → JSON 로드 → ScenarioRunner.StartScenario()
        distributor.LoadAndBroadcast(scenarioId);

        SetLCD($"<color=#FF4444>감지: {dtcCode}</color>\n정비 시나리오 시작됨");
    }

    // ═══════════════════════════════════════════
    //  버튼 이벤트 (인스펙터 OnClick 연결)
    // ═══════════════════════════════════════════

    /// <summary>[스캔] 버튼 — DTC 재판독</summary>
    public void OnScanButtonPressed()
    {
        if (!isConnected)
        {
            SetLCD("OBD 포트에 먼저 연결하세요.");
            return;
        }

        DiagnosticSystem.Instance?.ConnectScanner();
        ShowDTCList();
        Debug.Log("[GDS] 수동 스캔 실행");
    }

    /// <summary>
    /// [소거] 버튼 — 정비 완료 조건 검사 후 DTC 소거.
    /// FinalDiagnosis Task에서 사용.
    /// </summary>
    public void OnClearButtonPressed()
    {
        if (!isConnected)
        {
            SetLCD("OBD 포트에 먼저 연결하세요.");
            return;
        }

        var diag = DiagnosticSystem.Instance;
        if (diag == null) return;

        bool cleared = diag.TryClearDTCs();
        ShowDTCList();

        if (cleared)
        {
            // FinalDiagnosis Task 완료 신호
            InteractionEvents.FireZoneActivated("OBD_Zone", _taskItem?.prefabId ?? "GDS_Tablet");
            SetLCD("<color=#00FF88>System NORMAL\nDTC 소거 완료</color>");
            Debug.Log("[GDS] DTC 소거 완료 → FinalDiagnosis 완료");
        }
        else
        {
            Debug.LogWarning("[GDS] DTC 소거 실패 — 정비 미완료 상태");
        }
    }

    // ═══════════════════════════════════════════
    //  내부 유틸
    // ═══════════════════════════════════════════

    private void Disconnect()
    {
        isConnected = false;
        SetLCD("대기 중...");
        Debug.Log("[GDS] OBD 연결 해제");
    }

    private void ShowDTCList()
    {
        var diag = DiagnosticSystem.Instance;
        if (diag == null) { SetLCD("진단 시스템 없음"); return; }

        var dtcs = diag.activeDTCs;
        if (dtcs.Count == 0)
        {
            SetLCD("<color=#00FF88>고장코드 없음\nSystem: NORMAL</color>");
        }
        else
        {
            string text = "<color=#FF4444>고장코드 발견:</color>\n";
            foreach (var d in dtcs) text += $"  {d}\n";
            SetLCD(text);
        }
    }

    private void SetLCD(string text)
    {
        if (lcdText != null) lcdText.text = text;
    }

    // ═══════════════════════════════════════════
    //  공개 유틸
    // ═══════════════════════════════════════════

    /// <summary>시나리오 재시작 시 상태 초기화</summary>
    public void ResetTablet()
    {
        isConnected = false;
        scenarioStarted = false;
        SetLCD("대기 중...");
    }
}