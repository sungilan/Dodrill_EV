using UnityEngine;

// ============================================================
//  BoltHint.cs
//
//  볼트 윤곽선 색 규칙(초록·노랑·갈색)과 안내 (EquipFx.cs 에서 분리, 11/20)
// ============================================================

/// <summary>
/// 볼트 윤곽선 색 규칙(CMS 2021 참고) — 도구를 볼트에 댔을 때 '왜 안 풀리는지'를 색으로 바로 알린다.
///  · 초록 : 지금 단계의 볼트 — 바로 작업 가능
///  · 노랑 : 다른 단계의 볼트 — 먼저(또는 나중에) 할 작업이 있다(잠깐 깜빡이고 지금 단계 이름을 알려 준다)
///  · 갈색 : 녹슬어 뻑뻑한 볼트 — 윤활 방청제를 뿌리면 초록으로 바뀌고 잘 풀린다
/// 평가 모드에서는 노랑·갈색 힌트를 끈다(작업 중 초록 표시만 — CMS Expert 모드처럼 순서·상태는 스스로 판단).
/// </summary>
public static class BoltHint
{
    public static readonly Color Workable = new Color(0.25f, 1f, 0.35f, 1f);
    public static readonly Color Blocked = new Color(1f, 0.82f, 0.1f, 1f);
    public static readonly Color Seized = new Color(0.78f, 0.42f, 0.12f, 1f);

    public static bool HintsAllowed => !EvaluationMode.ClientIsEvaluation;

    public static bool NeedsLube(Bolt b) => BoltWork.IsSeized(b) && !BoltWork.IsLubed(b);

    /// <summary>지금 단계 볼트를 작업할 때 윤곽선 색(녹슨 볼트면 갈색).</summary>
    public static Color WorkColor(Bolt b) => HintsAllowed && NeedsLube(b) ? Seized : Workable;

    /// <summary>지금 단계 제목(시나리오 extra.title). 없으면 null.</summary>
    public static string CurrentStepTitle()
    {
        var recv = ScenarioStateReceiver.Instance;
        var data = recv != null ? recv.CurrentScenario : null;
        if (data == null || data.scenario == null || data.scenario.tasks == null) return null;
        int i = recv.CurrentTaskState.taskIndex;
        if (i < 0 || i >= data.scenario.tasks.Count) return null;
        string id = data.scenario.tasks[i].moduleId;
        string title = data.GetModuleConfig(id)?.GetExtra("title");
        return string.IsNullOrEmpty(title) ? HoverNameFormatter.Prettify(id) : title;
    }

    /// <summary>다른 단계의 볼트에 도구를 댔다 — 노랑으로 잠깐 깜빡이고 지금 할 단계를 알려 준다.</summary>
    public static void ShowBlocked(Bolt b)
    {
        if (b == null || !HintsAllowed || b.IsMarkedDone || b.isloosened) return;
        var flash = b.GetComponent<BoltHintFlash>() ?? b.gameObject.AddComponent<BoltHintFlash>();
        flash.Flash(Blocked, 1.6f);
        string step = CurrentStepTitle();
        EquipFx.Notice("bolt_blocked", "아직 작업할 볼트가 아닙니다",
            string.IsNullOrEmpty(step) ? "안내 표시가 있는 볼트부터 작업하세요" : $"지금 단계: {step} — 노란 볼트는 다른 단계에서 작업합니다",
            EquipFx.Warn, 2.5f, 6f);
    }

    /// <summary>녹슨 볼트를 처음 겨눴을 때 안내.</summary>
    public static void NoticeSeized()
    {
        if (!HintsAllowed) return;
        EquipFx.Notice("seized", "녹슬어 뻑뻑한 볼트 (갈색)", "윤활 방청제를 뿌리면 초록으로 바뀌고 잘 풀립니다", EquipFx.Warn, 3f, 10f);
    }
}
