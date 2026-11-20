using UnityEngine;

// ============================================================
//  LOTOSystem.cs  (Lock-Out Tag-Out)
//
//  DischargeTimerUI.StartDischarge() 가 LOTOSystem.Instance.isLOCKED
//  를 체크하므로 이 컴포넌트가 씬에 있어야 함.
//
//  씬 배치: _Managers 하위 빈 GameObject 에 부착
//
//  연결 흐름:
//    InteractablePart(MSD_Lever) 탈거
//      → ElectricSafetyManager.OnMSDRemoved()
//      → LOTOSystem.OnMSDRemoved()  ← 소켓 활성화
//    유저가 Lock 소켓에 자물쇠 스냅
//      → LOTOSystem.OnLockSnapped() → isLOCKED = true
//      → DischargeTimerUI.StartDischarge() 호출 가능 상태
// ============================================================

public class LOTOSystem : MonoBehaviour
{
    public static LOTOSystem Instance { get; private set; }

    [Header("상태")]
    [Tooltip("자물쇠가 체결되었는지 여부 — DischargeTimerUI 가 참조")]
    public bool isLOCKED = false;

    [Header("참조 (선택)")]
    [Tooltip("MSD 탈거 후 활성화되는 자물쇠 소켓 오브젝트 (없으면 무시)")]
    public GameObject lockSocket;

    [Tooltip("체결 완료 시 표시할 태그 오브젝트 (없으면 무시)")]
    public GameObject lockTag;

    [Header("테스트 편의")]
    [Tooltip("true 로 설정하면 자물쇠 절차 없이 바로 잠금 상태로 간주")]
    public bool bypassLOTO = false;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        // 소켓은 MSD 탈거 전까지 비활성
        if (lockSocket != null) lockSocket.SetActive(false);
        if (lockTag    != null) lockTag.SetActive(false);
    }

    // ── 공개 API ─────────────────────────────

    /// <summary>
    /// MSD 탈거 완료 시 호출 — 자물쇠 소켓 활성화.
    /// ElectricSafetyManager.OnMSDRemoved() 뒤에 호출.
    /// </summary>
    public void OnMSDRemoved()
    {
        if (bypassLOTO)
        {
            isLOCKED = true;
            Debug.Log("[LOTO] bypassLOTO=true → 자동 잠금");
            return;
        }

        if (lockSocket != null) lockSocket.SetActive(true);
        Debug.Log("[LOTO] MSD 탈거 완료 — 자물쇠 소켓 활성화");
    }

    /// <summary>
    /// 자물쇠 소켓에 스냅됐을 때 호출 (TaskInteractionZone or SyncGrab 소켓 이벤트).
    /// </summary>
    public void OnLockSnapped()
    {
        isLOCKED = true;
        if (lockTag != null) lockTag.SetActive(true);
        Debug.Log("[LOTO] 자물쇠 체결 완료 — 방전 대기 시작 가능");

        // 자동으로 방전 타이머 시작
        DischargeTimerUI.Instance?.StartDischarge();
    }

    /// <summary>
    /// 상태 초기화 — CheckpointManager.ResetAllInteractables 에서 호출
    /// </summary>
    public void ResetLOTO()
    {
        isLOCKED = false;
        if (lockSocket != null) lockSocket.SetActive(false);
        if (lockTag    != null) lockTag.SetActive(false);
        Debug.Log("[LOTO] 초기화");
    }
}
