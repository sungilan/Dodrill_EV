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

        // 계측기를 든 사람의 화면에서만 측정한다.
        // 예전에는 다른 플레이어가 든 계측기가(위치 동기화로) 단자 근처를 지나가기만 해도 내 화면의 프로브 트리거가 닿아
        // 측정 신호를 서버로 보냈다. 그래서 VR 에서 프로브를 꺼내 대기 전에 같은 PC 에 켜 둔 PC 클라이언트가 먼저 단계를 끝내 버렸다.
        if (!HeldByLocalPlayer()) return;

        currentTerminalId = point.terminalId;

        // 피드백
        Debug.Log($"[Probe:{(isRedProbe ? "RED" : "BLK")}] 접촉: {currentTerminalId}");

        // 본체에 재계산 요청
        if (master != null) master.EvaluateConnection();
    }

    /// <summary>이 계측기(본체 또는 이 프로브)를 내가 들고 있는가. 네트워크에 붙지 않은 상태(오프라인 테스트)면 그대로 허용.</summary>
    private bool HeldByLocalPlayer()
    {
        var cm = FishNet.InstanceFinder.ClientManager;
        if (cm == null || !cm.Started || cm.Connection == null || !cm.Connection.IsValid) return true;
        int me = cm.Connection.ClientId;
        return IsMine(GetComponentInParent<SyncGrab>(), me)
            || (master != null && IsMine(master.GetComponentInParent<SyncGrab>(), me));
    }

    private static bool IsMine(SyncGrab g, int me) =>
        g != null && g.IsGrabbed && g.Owner != null && g.Owner.IsValid && g.Owner.ClientId == me;

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
