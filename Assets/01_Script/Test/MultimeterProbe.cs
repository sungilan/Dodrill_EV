using UnityEngine;

public class MultimeterProbe : MonoBehaviour
{
    [Header("극성")]
    [Tooltip("true = 빨간(+), false = 검정(-)")]
    public bool isRedProbe = true;

    [Header("연결된 본체")]
    public MultimeterMaster master;

    [Tooltip("현재 접촉 중인 단자 ID (비어있으면 미접촉)")]
    public string currentTerminalId = "";

    private void OnTriggerEnter(Collider other)
    {
        // MeasurementPoint 컴포넌트가 있는 단자만 인식
        var point = other.GetComponent<MeasurementPoint>();
        if (point == null) point = other.GetComponentInParent<MeasurementPoint>();
        if (point == null) return;

        currentTerminalId = point.terminalId;

        // 피드백
        Debug.Log($"[Probe:{(isRedProbe ? "RED" : "BLK")}] 접촉: {currentTerminalId}");

        // 본체에 재계산 요청
        if (master != null) master.EvaluateConnection();
    }

    private void OnTriggerExit(Collider other)
    {
        var point = other.GetComponent<MeasurementPoint>();
        if (point == null) point = other.GetComponentInParent<MeasurementPoint>();
        if (point == null) return;

        if (currentTerminalId == point.terminalId)
        {
            currentTerminalId = "";
            if (master != null) master.EvaluateConnection();
        }
    }
}

// ============================================================
//  MultimeterDial.cs
//  다이얼 회전각 → MultimeterMode 변환
// ============================================================
