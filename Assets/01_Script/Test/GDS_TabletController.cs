using UnityEngine;
using TMPro;
using System.Collections;
using Autohand;

// ============================================================
//  GDS_TabletController.cs
//  GDS 진단기 태블릿 — OBD-II 포트 스냅 & DTC 판독/소거.
//
//  씬 배치:
//    GDS_Tablet 프리팹 루트에 부착.
//    SyncGrab + Grabbable 도 함께 부착 (도구이므로 스폰됨).
//
//  동작 흐름:
//    1. 유저가 태블릿을 잡고 OBD_Zone/OBD_Port 에 가져감
//    2. OnTriggerEnter → ConnectToVehicle()
//    3. 애니메이션 후 DiagnosticSystem 에서 DTC 목록 읽어와 LCD 표시
//    4. [스캔] 버튼 → Task ScanDTC 완료 신호 전송
//    5. [소거] 버튼 → 정비 완료 조건 검사 후 코드 제거 → FinalDiagnosis 완료
//
//  기존 시스템 연동:
//    TaskItem + InteractionEvents.FireZoneActivated() 병행 발행.
// ============================================================

[RequireComponent(typeof(TaskItem))]
public class GDS_TabletController : MonoBehaviour
{
    [Header("LCD 디스플레이")]
    public TextMeshProUGUI lcdText;

    [Header("연결 설정")]
    [Tooltip("OBD 포트 감지용 Tag (OBD_Port 오브젝트에 설정)")]
    public string obdPortTag = "OBD_Port";

    [Tooltip("연결 애니메이션 시간 (초)")]
    public float connectDelay = 1.5f;

    [Header("상태")]
    public bool isConnected = false;

    private TaskItem _taskItem;
    private Coroutine _connectRoutine;

    // 현재 씬 시나리오 moduleId (마지막으로 시작된 Task 추적용)
    private string _lastModuleId = "";

    private void Awake()
    {
        _taskItem = GetComponent<TaskItem>();
    }

    private void OnEnable()
    {
        // ScenarioRunner 가 Task 변경 알림 수신
        TrainingFlowManager.OnStepChanged += OnStepChanged;
    }

    private void OnDisable()
    {
        TrainingFlowManager.OnStepChanged -= OnStepChanged;
        if (_connectRoutine != null) StopCoroutine(_connectRoutine);
    }

    // ── OBD 포트 접촉 감지 ───────────────────

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

    // ── 연결 로직 ────────────────────────────

    private IEnumerator ConnectRoutine()
    {
        SetLCD("차량 통신 연결 중...");
        yield return new WaitForSeconds(connectDelay);

        isConnected = true;
        ShowDTCList();

        // TrainingFlowManager 에 ScanDTC 완료 보고
        string target = "OBD_Port";
        TrainingFlowManager.Instance?.ProcessStepAction(RepairActionType.ScanDTC, target, 0f);

        // 기존 ScenarioRunner 호환 이벤트
        InteractionEvents.FireZoneActivated("OBD_Zone", _taskItem?.prefabId ?? "GDS_Tablet");

        Debug.Log("[GDS] OBD 연결 완료 — DTC 판독");
    }

    private void Disconnect()
    {
        isConnected = false;
        SetLCD("대기 중...");
        Debug.Log("[GDS] OBD 연결 해제");
    }

    // ── DTC 표시 ─────────────────────────────

    private void ShowDTCList()
    {
        if (DiagnosticSystem.Instance == null)
        {
            SetLCD("진단 시스템 없음");
            return;
        }

        var dtcs = DiagnosticSystem.Instance.activeDTCs;
        if (dtcs == null || dtcs.Count == 0)
        {
            SetLCD("<color=#00FF88>고장코드 없음\nSystem: NORMAL</color>");
        }
        else
        {
            string text = "<color=#FF4444>고장코드 발견:</color>\n";
            foreach (var d in dtcs)
                text += $"  {d}\n";
            SetLCD(text);
        }
    }

    // ── 버튼 이벤트 (인스펙터 연결) ──────────

    /// <summary>[스캔] 버튼 OnClick 에 연결</summary>
    public void OnScanButtonPressed()
    {
        if (!isConnected)
        {
            SetLCD("OBD 포트에 먼저 연결하세요.");
            return;
        }

        DiagnosticSystem.Instance?.ConnectScanner();
        ShowDTCList();
    }

    /// <summary>[소거] 버튼 OnClick 에 연결 — FinalDiagnosis Task 용</summary>
    public void OnClearButtonPressed()
    {
        if (!isConnected) return;

        // 정비 완료 여부 검사 후 소거
        DiagnosticSystem.Instance?.ClearDTCs();
        ShowDTCList();

        // TrainingFlowManager 에 최종 ScanDTC 완료 보고
        if (DiagnosticSystem.Instance != null &&
            DiagnosticSystem.Instance.activeDTCs.Count == 0)
        {
            TrainingFlowManager.Instance?.ProcessStepAction(
                RepairActionType.ScanDTC, "OBD_Port", 0f);
        }
    }

    // ── 스텝 변경 콜백 ───────────────────────

    private void OnStepChanged(int stepIndex, string description)
    {
        _lastModuleId = description;
    }

    // ── 유틸 ─────────────────────────────────

    private void SetLCD(string text)
    {
        if (lcdText != null) lcdText.text = text;
    }
}
