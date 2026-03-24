using UnityEngine;

// ============================================================
//  EVDebugCommands.cs
//  기존 DebugCommands.cs 패턴을 따르는 전기차 정비 훈련용 디버그 커맨드.
//
//  등록 방법:
//    DebugBootstrapper.RegisterCommands() 에 아래 한 줄 추가:
//      EVDebugCommands.RegisterAll(Registry);
//
//  사용 가능 커맨드:
//    EV/ForceComplete   — 현재 단계 강제 완료
//    EV/JumpToStep      — 특정 단계로 이동 (args: [stepIndex])
//    EV/GetStatus       — 현재 훈련 상태 출력
//    EV/SetSafeMode     — 안전 상태 강제 설정 (args: [0 or 1])
//    EV/StartTraining   — 훈련 재시작
// ============================================================

public class EVForceCompleteCommand : IDebugCommand
{
    public string Category => "EV";
    public string Name => "ForceComplete";
    public string Description => "현재 정비 단계 강제 완료";

    public void Execute(string[] args, int callerId)
    {
        var mgr = UnityEngine.Object.FindFirstObjectByType<TrainingFlowManager>();
        if(mgr == null)
        {
            Debug.LogWarning("[Debug] TrainingFlowManager 를 씬에서 찾을 수 없음");
            return;
        }
        Debug.Log($"[Debug] Client#{callerId} → EV/ForceComplete");
        mgr.ForceCompleteCurrentTask();
    }
}

public class EVJumpToStepCommand : IDebugCommand
{
    public string Category => "EV";
    public string Name => "JumpToStep";
    public string Description => "특정 정비 단계로 강제 이동 | args: [stepIndex]";

    public void Execute(string[] args, int callerId)
    {
        if(args.Length == 0 || !int.TryParse(args[0], out int index))
        {
            Debug.LogWarning("[Debug] EV/JumpToStep: stepIndex 인자 필요 (예: 2)");
            return;
        }

        var mgr = UnityEngine.Object.FindFirstObjectByType<TrainingFlowManager>();
        if(mgr == null)
        {
            Debug.LogWarning("[Debug] TrainingFlowManager 를 씬에서 찾을 수 없음");
            return;
        }

        Debug.Log($"[Debug] Client#{callerId} → EV/JumpToStep({index})");
        mgr.JumpToTask(index);
    }
}

public class EVGetStatusCommand : IDebugCommand
{
    public string Category => "EV";
    public string Name => "GetStatus";
    public string Description => "현재 훈련 상태 출력 (DTC / 단계 / 활성 여부)";

    public void Execute(string[] args, int callerId)
    {
        var mgr = UnityEngine.Object.FindFirstObjectByType<TrainingFlowManager>();
        if(mgr == null)
        {
            Debug.LogWarning("[Debug] TrainingFlowManager 를 씬에서 찾을 수 없음");
            return;
        }
        Debug.Log($"[Debug] Client#{callerId} → {mgr.GetDebugStatus()}");
    }
}

public class EVSetSafeModeCommand : IDebugCommand
{
    public string Category => "EV";
    public string Name => "SetSafeMode";
    public string Description => "안전 상태 강제 설정 | args: [1=안전 / 0=위험]";

    public void Execute(string[] args, int callerId)
    {
        if(args.Length == 0 || !int.TryParse(args[0], out int val))
        {
            Debug.LogWarning("[Debug] EV/SetSafeMode: 0 또는 1 인자 필요");
            return;
        }

        var safety = UnityEngine.Object.FindFirstObjectByType<ElectricSafetyManager>();
        if(safety == null)
        {
            Debug.LogWarning("[Debug] ElectricSafetyManager 를 씬에서 찾을 수 없음");
            return;
        }

        bool safe = val == 1;
        if(safe)
        {
            safety.isMSDRemoved = true;
            safety.isGlovesEquipped = true;
        }
        else
        {
            //safety.ResetSafetyState();
        }

        Debug.Log($"[Debug] Client#{callerId} → EV/SetSafeMode({(safe ? "안전" : "위험")})");
    }
}

public class EVStartTrainingCommand : IDebugCommand
{
    public string Category => "EV";
    public string Name => "StartTraining";
    public string Description => "훈련 재시작 (랜덤 고장 재선택)";

    public void Execute(string[] args, int callerId)
    {
        var mgr = UnityEngine.Object.FindFirstObjectByType<TrainingFlowManager>();
        if(mgr == null)
        {
            Debug.LogWarning("[Debug] TrainingFlowManager 를 씬에서 찾을 수 없음");
            return;
        }
        Debug.Log($"[Debug] Client#{callerId} → EV/StartTraining");
        mgr.StartTraining();
    }
}

// ── 일괄 등록 헬퍼 ────────────────────────

public static class EVDebugCommands
{
    /// <summary>
    /// DebugBootstrapper.RegisterCommands() 내부에서 호출.
    /// EVDebugCommands.RegisterAll(Registry);
    /// </summary>
    public static void RegisterAll(DebugCommandRegistry registry)
    {
        registry.Register(new EVForceCompleteCommand());
        registry.Register(new EVJumpToStepCommand());
        registry.Register(new EVGetStatusCommand());
        registry.Register(new EVSetSafeModeCommand());
        registry.Register(new EVStartTrainingCommand());
    }
}