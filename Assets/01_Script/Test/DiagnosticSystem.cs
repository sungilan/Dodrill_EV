using System.Collections.Generic;
using UnityEngine;
using TMPro;

// 고장 코드 정보를 담는 데이터 구조
[System.Serializable]
public class DTCInfo
{
    public string code;          // 예: "P0A80"
    public string description;   // 예: "Replace Hybrid/EV Battery Pack"
    public string hint;          // 유저에게 줄 힌트 (옵션)
}

public class DiagnosticSystem : MonoBehaviour
{
    public static DiagnosticSystem Instance;

    [Header("UI Elements")]
    public TextMeshProUGUI scannerDisplay;

    [Header("DTC Settings")]
    public List<DTCInfo> dtcPool;        // 인스펙터에서 등록할 고장 코드들
    public List<string> activeDTCs = new List<string>(); // 현재 발견된 코드 (문자열)

    [Header("Target Modules")]
    public List<ModulePart> allModules;  // 배터리 팩 내부의 모든 모듈 리스트

    private bool isScannerConnected = false;
    private bool isFaultGenerated = false;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // 시나리오 시작 시 랜덤 고장 생성
        GenerateRandomFault();
    }

    // 1. 랜덤 고장 생성 로직
    public void GenerateRandomFault()
    {
        activeDTCs.Clear();

        if(dtcPool.Count > 0)
        {
            // 랜덤으로 고장 코드 하나 선택
            DTCInfo selectedDTC = dtcPool[Random.Range(0, dtcPool.Count)];
            activeDTCs.Add($"{selectedDTC.code}: {selectedDTC.description}");

            // 만약 특정 코드(예: P0A80)라면 실제 모듈 하나를 불량으로 만듦
            if(selectedDTC.code == "P0A80" && allModules.Count > 0)
            {
                int randomIdx = Random.Range(0, allModules.Count);
                allModules[randomIdx].isFaulty = true;
                allModules[randomIdx].voltage = 1.2f; // 불량 전압 설정
                Debug.Log($"[시스템] 랜덤 고장 생성 완료: {allModules[randomIdx].gameObject.name} 불량");
            }
        }

        isFaultGenerated = true;
        UpdateDisplay();
    }

    // 2. 스캐너 연결 (OBD-II 단자 접촉 시 호출)
    public void ConnectScanner()
    {
        isScannerConnected = true;
        UpdateDisplay();

        // [사운드] 스캐너 연결 시 '삐빅' 소리 추가 가능
    }

    private void UpdateDisplay()
    {
        if(!isScannerConnected)
        {
            scannerDisplay.text = "Waiting for Connection...";
            return;
        }

        if(activeDTCs.Count > 0)
        {
            scannerDisplay.text = "<color=red>Detected Errors:</color>\n";
            foreach(var dtc in activeDTCs)
            {
                scannerDisplay.text += $"- {dtc}\n";
            }
        }
        else
        {
            scannerDisplay.text = "<color=green>System Status: NORMAL</color>\nNo DTCs Found.";
        }
    }

    // 3. 'Clear DTC' 버튼에 연결
    public void ClearDTCs()
    {
        if(!isScannerConnected) return;

        // 모든 조립이 완벽하고, 불량 모듈이 교체되었을 때만 삭제 가능
        if(DisassemblyManager.Instance.IsEverythingAssembled() && CheckIfFaultFixed())
        {
            activeDTCs.Clear();
            UpdateDisplay();
            Debug.Log("모든 고장 코드가 삭제되었습니다.");
            FinalReadyCheck();
        }
        else
        {
            // 하드웨어 문제가 남아있으면 삭제 불가 피드백
            scannerDisplay.text = "<color=yellow>ERROR: Hardware Fault Persistent.</color>\nCheck Module Voltage or Assembly!";
        }
    }

    // 불량 모듈이 실제로 교체되었는지(전압이 정상인지) 확인
    private bool CheckIfFaultFixed()
    {
        foreach(var module in allModules)
        {
            // 여전히 불량 상태인 모듈이 하나라도 있다면 false
            if(module.isFaulty && module.voltage < 3.0f) return false;
        }
        return true;
    }

    private void FinalReadyCheck()
    {
        // [시각 연출] 계기판 READY 점등 로직 호출 가능
        UIManager.Instance.ShowCompleteUI();
    }
}