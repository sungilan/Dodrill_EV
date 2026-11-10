using UnityEngine;

// ============================================================
//  ElectricSafetyManager.cs
//  고전압 안전 상태 싱글톤.
//
//  테스트 시 빠른 우회:
//    Inspector 에서 isMSDRemoved / isGlovesEquipped 를 true 로 설정하거나
//    Debug 커맨드: EV/SetSafeMode 1
//
//  InteractablePart.CheckBoltsForUnlock() 에서 참조.
//  ElectricSafetyManager.Instance.IsSafeToWork() == false 이면
//  부품을 잡을 수 없도록 막는다.
// ============================================================

public class ElectricSafetyManager : MonoBehaviour
{
    public static ElectricSafetyManager Instance { get; private set; }

    [Header("안전 상태")]
    [Tooltip("MSD(서비스 플러그) 차단 여부")]
    public bool isMSDRemoved = false;

    [Tooltip("절연 장갑 착용 여부")]
    public bool isGlovesEquipped = false;

    [Header("테스트 편의")]
    [Tooltip("true 로 설정하면 안전 검사를 완전히 건너뜀 (테스트 전용)")]
    public bool bypassSafetyForTesting = false;

    /// <summary>서버에서 시나리오 진행 상태를 받은 적이 있다(= 자유 모드가 아니라 교육·평가 중)</summary>
    public bool IsScenarioActive { get; private set; }

    private void Awake()
    {
        if(Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── 시나리오 진행에서 안전 상태 만들기 ─────────────────────
    // 예전에는 이 값을 켜는 곳이 옛 VR 스크립트(GloveItem·MSDPart)뿐이어서, PC·모바일에서는 보호구를 착용하고 MSD 를 차단해도
    // 계속 '안전하지 않음'으로 남아 부품을 잡는 순간 사고 연출이 나올 수 있었다.
    // 이제 서버가 진행시킨 단계에서 상태를 읽는다: 보호구 단계는 접속자 전원이 착용해야 넘어가므로(사람별 확인),
    // 그 단계를 지났다면 이 사람도 착용한 것이고, MSD 차단 단계를 마쳤다면 차단된 것이다.
    // (훈련 중간에 들어온 사람은 착용할 기회가 없었으므로 같은 기준으로 착용한 것으로 본다.)
    private const string GlovesModuleId = "LOTO_Gloves";
    private const string MsdModuleId = "MSD_Removal";

    private void OnEnable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated += HandleTaskState;
        ScenarioStateReceiver.OnSnapshotReceived += HandleSnapshot;
    }

    private void OnDisable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated -= HandleTaskState;
        ScenarioStateReceiver.OnSnapshotReceived -= HandleSnapshot;
    }

    private void HandleTaskState(TaskStateBroadcast broadcast)
    {
        SyncFromProgress(broadcast.currentTask, ScenarioStateReceiver.Instance != null ? ScenarioStateReceiver.Instance.CurrentScenario : null);
    }

    private void HandleSnapshot(ScenarioSnapshotBroadcast snapshot)
    {
        SyncFromProgress(snapshot.currentTask, snapshot.scenarioData);
    }

    private void SyncFromProgress(TaskState task, ScenarioData data)
    {
        var tasks = data?.scenario?.tasks;
        if(tasks == null || tasks.Count == 0) return;

        // 시나리오(교육·평가)가 돌고 있다 → 부품이 프리모드여도 사고 체크를 한다 (InteractablePart.CheckSafetyAndTriggerAccident)
        IsScenarioActive = true;

        // 처음부터 다시 시작하면 안전 상태도 처음으로 (디버그로 켜 둔 값이 남지 않게)
        if(task.taskIndex <= 0)
        {
            if(isMSDRemoved || isGlovesEquipped) ResetSafetyState();
            return;
        }

        int glovesIndex = IndexOfModule(tasks, GlovesModuleId);
        if(!isGlovesEquipped && glovesIndex >= 0 && task.taskIndex > glovesIndex)
            OnGlovesEquipped();

        int msdIndex = IndexOfModule(tasks, MsdModuleId);
        bool msdDone = msdIndex >= 0 && (task.taskIndex > msdIndex ||
                       (task.taskIndex == msdIndex && task.status == TaskStatus.Completed));
        if(!isMSDRemoved && msdDone)
            OnMSDRemoved();
    }

    private static int IndexOfModule(System.Collections.Generic.List<TaskDef> tasks, string moduleId)
    {
        for(int i = 0; i < tasks.Count; i++)
            if(tasks[i] != null && string.Equals(tasks[i].moduleId, moduleId, System.StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <summary>
    /// 현재 작업이 안전한지 반환.
    /// InteractablePart 와 TrainingFlowManager 에서 호출.
    /// </summary>
    public bool IsSafeToWork()
    {
        if(bypassSafetyForTesting) return true;
        return isMSDRemoved && isGlovesEquipped;
    }

    /// <summary>안전 상태를 초기값(위험)으로 리셋</summary>
    public void ResetSafetyState()
    {
        isMSDRemoved = false;
        isGlovesEquipped = false;
    }

    /// <summary>MSD 차단 완료 — MSDPart 탈거 시 호출</summary>
    public void OnMSDRemoved()
    {
        isMSDRemoved = true;
        Debug.Log("[Safety] MSD 차단 완료");
    }

    /// <summary>절연 장갑 착용 완료 — GloveItem 착용 시 호출</summary>
    public void OnGlovesEquipped()
    {
        isGlovesEquipped = true;
        Debug.Log("[Safety] 절연 장갑 착용 완료");
    }
}