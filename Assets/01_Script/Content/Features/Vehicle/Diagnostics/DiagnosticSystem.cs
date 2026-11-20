using System.Collections.Generic;
using UnityEngine;
using TMPro;

// ============================================================
//  DiagnosticSystem.cs
//  랜덤 고장 생성 + DTC 판독/소거 + 시나리오 매핑 제공
//
//  흐름:
//    1. Start() → GenerateRandomFault() → dtcPool에서 랜덤 DTC 선택
//    2. GDS_TabletController가 OBD 연결 시 GetPrimaryDTCCode() 호출
//    3. GDS_TabletController가 DTCToScenarioId() 로 시나리오 ID 변환
//    4. ScenarioDistributor.LoadAndBroadcast(scenarioId) 호출
//
//  씬 배치:
//    _Managers 하위 빈 GameObject에 부착.
//    dtcPool에 지원하는 DTC 목록 등록.
// ============================================================

[System.Serializable]
public class DTCInfo
{
    public string code;         // 예: "P0AA6"
    public string description;  // 예: "고전압 배터리 절연 저항 불량"
    public string scenarioId;   // 매핑할 JSON 시나리오 ID (예: "ev_p0aa6_battery_insulation")
    [TextArea(1, 2)]
    public string hint;         // 정비사 참고 힌트 (선택)
}

public class DiagnosticSystem : MonoBehaviour
{
    public static DiagnosticSystem Instance { get; private set; }

    [Header("UI")]
    [Tooltip("GDS 태블릿 LCD 디스플레이 (없으면 생략)")]
    public TextMeshProUGUI scannerDisplay;

    [Header("DTC 풀 — 인스펙터에서 등록")]
    [Tooltip("랜덤 선택할 고장코드 목록")]
    public List<DTCInfo> dtcPool = new();

    [Header("상태 (런타임)")]
    public List<string> activeDTCs = new();     // "P0AA6: 설명" 형태
    public DTCInfo activeDTCInfo;          // 선택된 DTCInfo 원본

    [Header("배터리 모듈 (P0A80 등 특정 코드 전용)")]
    [Tooltip("특정 DTC가 물리 모듈 고장을 유발하는 경우 연결")]
    public List<ModulePart> allModules = new();

    private bool _isScannerConnected = false;

    // ═══════════════════════════════════════════
    //  생명주기
    // ═══════════════════════════════════════════

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        // 씬 시작 시 랜덤 고장 사전 생성
        // (플레이어가 태블릿 연결 전까지 숨겨짐)
        GenerateRandomFault();
    }

    // ═══════════════════════════════════════════
    //  고장 생성
    // ═══════════════════════════════════════════

    /// <summary>
    /// dtcPool에서 랜덤 DTC 선택 → activeDTCs 저장.
    /// VehicleSpawner.SpawnAndInitialize() 완료 후 호출하거나
    /// Start()에서 자동 호출됨.
    /// </summary>
    public void GenerateRandomFault()
    {
        activeDTCs.Clear();
        activeDTCInfo = null;

        if (dtcPool == null || dtcPool.Count == 0)
        {
            Debug.LogWarning("[DiagnosticSystem] dtcPool이 비어있습니다 — 인스펙터에서 DTC 등록 필요");
            return;
        }

        // 랜덤 선택
        var selected = dtcPool[Random.Range(0, dtcPool.Count)];
        activeDTCInfo = selected;
        activeDTCs.Add($"{selected.code}: {selected.description}");

        // 특정 코드가 물리 모듈 고장을 유발하는 경우
        ApplyModuleFault(selected);

        Debug.Log($"[DiagnosticSystem] 랜덤 고장 생성 완료: {selected.code} — {selected.description}");
        UpdateDisplay();
    }

    private void ApplyModuleFault(DTCInfo dtc)
    {
        // 배터리 셀 불량 계열 코드만 물리 모듈에 적용
        bool isCellFault = dtc.code is "P0A80" or "P0AA4" or "P0AA1";
        if (!isCellFault || allModules.Count == 0) return;

        int idx = Random.Range(0, allModules.Count);
        allModules[idx].isFaulty = true;
        allModules[idx].voltage = 1.2f; // 불량 셀 전압
        Debug.Log($"[DiagnosticSystem] 물리 모듈 고장 적용: {allModules[idx].name}");
    }

    // ═══════════════════════════════════════════
    //  스캐너 연결 / 소거
    // ═══════════════════════════════════════════

    /// <summary>GDS 태블릿 OBD 연결 시 호출</summary>
    public void ConnectScanner()
    {
        _isScannerConnected = true;
        UpdateDisplay();
    }

    /// <summary>
    /// [소거] 버튼 — 정비 완료 조건 검사 후 DTC 소거.
    /// FinalDiagnosis Task에서 사용.
    /// </summary>
    public bool TryClearDTCs()
    {
        if (!_isScannerConnected) return false;

        bool assemblyOk = DisassemblyManager.Instance == null ||
                          DisassemblyManager.Instance.IsEverythingAssembled();
        bool faultFixed = CheckIfFaultFixed();

        if (assemblyOk && faultFixed)
        {
            activeDTCs.Clear();
            activeDTCInfo = null;
            UpdateDisplay();
            Debug.Log("[DiagnosticSystem] DTC 소거 완료 — 정비 성공");
            return true;
        }

        // 소거 실패 메시지
        if (scannerDisplay != null)
            scannerDisplay.text = "<color=yellow>ERROR: Hardware Fault Persistent.\nCheck Assembly & Module Voltage!</color>";

        Debug.LogWarning("[DiagnosticSystem] DTC 소거 실패 — 정비 미완료");
        return false;
    }

    // ═══════════════════════════════════════════
    //  공개 조회 API
    // ═══════════════════════════════════════════

    /// <summary>
    /// 현재 활성 DTC의 코드만 반환 ("P0AA6: 설명" → "P0AA6")
    /// GDS_TabletController에서 시나리오 매핑에 사용.
    /// </summary>
    public string GetPrimaryDTCCode()
    {
        if (activeDTCInfo != null) return activeDTCInfo.code;
        if (activeDTCs.Count == 0) return string.Empty;
        return activeDTCs[0].Split(':')[0].Trim();
    }

    /// <summary>
    /// 현재 활성 DTC에 매핑된 시나리오 ID 반환.
    /// DTCInfo.scenarioId 필드가 우선, 없으면 코드 기반 자동 매핑.
    /// </summary>
    public string GetScenarioId()
    {
        // DTCInfo에 직접 설정된 scenarioId 우선
        if (activeDTCInfo != null && !string.IsNullOrEmpty(activeDTCInfo.scenarioId))
            return activeDTCInfo.scenarioId;

        // 코드 기반 자동 매핑 (폴백)
        return GetPrimaryDTCCode() switch
        {
            "P0AA6" => "ev_p0aa6_battery_insulation",
            "P0AA4" => "ev_p0aa4_battery_insulation",
            "P0AA1" => "ev_p0aa1_battery_voltage",
            "P0C73" => "ev_p0c73_motor_fault",
            "P0A78" => "ev_p0a78_motor_inverter",
            "P0D21" => "ev_p0d21_iccu_fault",
            "P0534" => "ev_p0534_ac_refrigerant",
            "P1727" => "ev_p1727_transmission",
            "P0A94" => "ev_p0a94_dcdc_converter",
            "P0AEE" => "ev_p0aee_battery_temp",
            "U1112" => "ev_u1112_can_comm",
            _ => string.Empty,
        };
    }

    public bool HasActiveDTC => activeDTCs.Count > 0;

    // ═══════════════════════════════════════════
    //  내부 유틸
    // ═══════════════════════════════════════════

    private bool CheckIfFaultFixed()
    {
        foreach (var module in allModules)
            if (module != null && module.isFaulty && module.voltage < 3.0f)
                return false;
        return true;
    }

    private void UpdateDisplay()
    {
        if (scannerDisplay == null) return;

        if (!_isScannerConnected)
        {
            scannerDisplay.text = "Waiting for Connection...";
            return;
        }

        if (activeDTCs.Count > 0)
        {
            string text = "<color=#FF4444>고장코드 발견:</color>\n";
            foreach (var dtc in activeDTCs)
                text += $"  {dtc}\n";
            scannerDisplay.text = text;
        }
        else
        {
            scannerDisplay.text = "<color=#00FF88>System Status: NORMAL\nNo DTCs Found.</color>";
        }
    }
}