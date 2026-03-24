using DoDrill;
using UnityEngine;
using Debug = UnityEngine.Debug;

// ============================================================
//  DebugCommands.cs
//  IDebugCommand 구현체 모음
//
//  포함:
//    - ScenarioDebugCommands : Task 강제완료 / 강제실패 / 특정 Task 이동 / 상태 조회
//    - GuideDebugCommands    : 가이드 표시 / 전체 숨김
//    - NetworkDebugCommands  : Broadcast 재전송 / 수신 상태 확인
//
//  추가 콘텐츠: IDebugCommand 구현 후 DebugBootstrapper.RegisterCommands() 에 등록
// ============================================================

    public class ScenarioForceCompleteCommand : IDebugCommand
    {
        public string Category    => "Scenario";
        public string Name        => "ForceComplete";
        public string Description => "현재 Task 강제 완료";

        public void Execute(string[] args, int callerId)
        {
            var runner = UnityEngine.Object.FindFirstObjectByType<ScenarioRunner>();
            if (runner == null)
            {
                UnityEngine.Debug.LogWarning("[Debug] ScenarioRunner 를 씬에서 찾을 수 없음");
                return;
            }

            UnityEngine.Debug.Log($"[Debug] Client#{callerId} → ForceComplete");
            runner.ForceCompleteCurrentTask();
        }
    }

    public class ScenarioForceFailCommand : IDebugCommand
    {
        public string Category    => "Scenario";
        public string Name        => "ForceFail";
        public string Description => "현재 Task 강제 실패";

        public void Execute(string[] args, int callerId)
        {
            var runner = UnityEngine.Object.FindFirstObjectByType<ScenarioRunner>();
            if (runner == null)
            {
                UnityEngine.Debug.LogWarning("[Debug] ScenarioRunner 를 씬에서 찾을 수 없음");
                return;
            }

            UnityEngine.Debug.Log($"[Debug] Client#{callerId} → ForceFail");
            runner.ForceFailCurrentTask();
        }
    }

    public class ScenarioJumpToTaskCommand : IDebugCommand
    {
        public string Category    => "Scenario";
        public string Name        => "JumpToTask";
        public string Description => "특정 Task로 강제 이동 | args: [taskIndex]";

        public void Execute(string[] args, int callerId)
        {
            if (args.Length == 0 || !int.TryParse(args[0], out int index))
            {
                UnityEngine.Debug.LogWarning("[Debug] JumpToTask: taskIndex 인자 필요 (예: 2)");
                return;
            }

            var runner = UnityEngine.Object.FindFirstObjectByType<ScenarioRunner>();
            if (runner == null)
            {
                UnityEngine.Debug.LogWarning("[Debug] ScenarioRunner 를 씬에서 찾을 수 없음");
                return;
            }

            UnityEngine.Debug.Log($"[Debug] Client#{callerId} → JumpToTask({index})");
            runner.JumpToTask(index);
        }
    }

    public class ScenarioGetStatusCommand : IDebugCommand
    {
        public string Category    => "Scenario";
        public string Name        => "GetStatus";
        public string Description => "현재 Task 상태 출력 (index / moduleId / elapsed)";

        public void Execute(string[] args, int callerId)
        {
            var runner = UnityEngine.Object.FindFirstObjectByType<ScenarioRunner>();
            if (runner == null)
            {
                UnityEngine.Debug.LogWarning("[Debug] ScenarioRunner 를 씬에서 찾을 수 없음");
                return;
            }

            UnityEngine.Debug.Log($"[Debug] Client#{callerId} → {runner.GetDebugStatus()}");
        }
    }

    public class ScenarioDebugCommands
    {
        public static void RegisterAll(DebugCommandRegistry registry)
        {
            registry.Register(new ScenarioForceCompleteCommand());
            registry.Register(new ScenarioForceFailCommand());
            registry.Register(new ScenarioJumpToTaskCommand());
            registry.Register(new ScenarioGetStatusCommand());
        }
    }

    // ══════════════════════════════════════════
    //  가이드 디버그 커맨드
    // ══════════════════════════════════════════

    public class GuideShowCommand : IDebugCommand
    {
        public string Category    => "Guide";
        public string Name        => "Show";
        public string Description => "특정 Zone 가이드 표시 | args: [zoneId]";

        public void Execute(string[] args, int callerId)
        {
            if (args.Length == 0 || string.IsNullOrEmpty(args[0]))
            {
                UnityEngine.Debug.LogWarning("[Debug] GuideShow: zoneId 인자 필요 (예: PatientEar)");
                return;
            }

            var registry = UnityEngine.Object.FindFirstObjectByType<SceneGuideRegistry>();
            if (registry == null)
            {
                UnityEngine.Debug.LogWarning("[Debug] SceneGuideRegistry 를 씬에서 찾을 수 없음");
                return;
            }

            registry.HideAll();
            registry.Show(args[0]);
            UnityEngine.Debug.Log($"[Debug] Client#{callerId} → GuideShow({args[0]})");
        }
    }

    public class GuideHideAllCommand : IDebugCommand
    {
        public string Category    => "Guide";
        public string Name        => "HideAll";
        public string Description => "모든 Zone 가이드 숨기기";

        public void Execute(string[] args, int callerId)
        {
            var registry = UnityEngine.Object.FindFirstObjectByType<SceneGuideRegistry>();
            if (registry == null)
            {
                UnityEngine.Debug.LogWarning("[Debug] SceneGuideRegistry 를 씬에서 찾을 수 없음");
                return;
            }

            registry.HideAll();
            UnityEngine.Debug.Log($"[Debug] Client#{callerId} → GuideHideAll");
        }
    }

    public class GuideDebugCommands
    {
        public static void RegisterAll(DebugCommandRegistry registry)
        {
            registry.Register(new GuideShowCommand());
            registry.Register(new GuideHideAllCommand());
        }
    }

    // ══════════════════════════════════════════
    //  네트워크 디버그 커맨드 (기존 DebugCommands.cs 에서 이관)
    // ══════════════════════════════════════════

    public class NetworkResendScenarioCommand : IDebugCommand
    {
        public string Category    => "Network";
        public string Name        => "ResendScenario";
        public string Description => "ScenarioData 재브로드캐스트 (수신 확인용)";

        public void Execute(string[] args, int callerId)
        {
            var distributor = UnityEngine.Object.FindFirstObjectByType<ScenarioDistributor>();
            if (distributor == null)
            {
                UnityEngine.Debug.LogWarning("[Debug] ScenarioDistributor 를 씬에서 찾을 수 없음");
                return;
            }

            if (string.IsNullOrEmpty(distributor.LastScenarioId))
            {
                UnityEngine.Debug.LogWarning("[Debug] ResendScenario: 아직 로드된 시나리오 없음");
                return;
            }

            UnityEngine.Debug.Log($"[Debug] Client#{callerId} → ResendScenario ({distributor.LastScenarioId})");
            distributor.LoadAndBroadcast(distributor.LastScenarioId);
        }
    }

    public class NetworkCheckReceiverCommand : IDebugCommand
    {
        public string Category    => "Network";
        public string Name        => "CheckReceiver";
        public string Description => "ScenarioReceiver 수신 상태 확인";

        public void Execute(string[] args, int callerId)
        {
            var receiver = UnityEngine.Object.FindFirstObjectByType<ScenarioReceiver>();
            if (receiver == null)
            {
                UnityEngine.Debug.LogWarning("[Debug] ScenarioReceiver 를 씬에서 찾을 수 없음");
                return;
            }

            var data = receiver.ReceivedData;
            if (data == null)
            {
                UnityEngine.Debug.Log($"[Debug] Client#{callerId} → CheckReceiver: 수신된 데이터 없음");
                return;
            }

            UnityEngine.Debug.Log($"[Debug] Client#{callerId} → CheckReceiver: " +
                                  $"{data.scenarioName} | Tasks: {data.scenario?.tasks?.Count}");
        }
    }

    public class NetworkDebugCommands
    {
        public static void RegisterAll(DebugCommandRegistry registry)
        {
            registry.Register(new NetworkResendScenarioCommand());
            registry.Register(new NetworkCheckReceiverCommand());
        }
    }