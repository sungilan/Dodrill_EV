using System.Collections;
using System.Collections.Generic;
using FishNet;
using Newtonsoft.Json;
using UnityEngine;

// ============================================================
//  ExampleScenarioStarter.cs — 엔진만으로 시나리오를 돌리는 최소 예제 (모듈화 적용 가이드, 2026-11-20)
//
//  하는 일 (새 프로젝트에 엔진을 붙일 때의 순서 그대로):
//   1) 콘텐츠 모듈 등록 — ScenarioHooks.AddModuleProvider (실제 프로젝트는 Ev*Binding 처럼 RuntimeInitializeOnLoadMethod 로 한다.
//      여기서는 예제 씬에서만 등록되도록 Awake 에서, ScenarioBootstrapper 보다 먼저 실행되게 실행 순서를 앞당겼다)
//   2) FishNet 호스트(서버+클라이언트) 시작 — 엔진의 단계 진행은 서버에서 돈다
//   3) 시나리오 JSON 을 읽어 ScenarioRunner.StartScenario
//  화면 왼쪽 위에 진행 로그를 보여 준다.
//  ※ 예제 JSON 은 Resources 밖에 둔다(Resources/Scenarios 에 두면 EV 로비 시나리오 목록에 섞인다).
// ============================================================
[DefaultExecutionOrder(-1000)]
public class ExampleScenarioStarter : MonoBehaviour
{
    [SerializeField] private ScenarioRunner runner;
    [SerializeField] private TextAsset scenarioJson;
    [SerializeField] private bool autoStartHost = true;

    private readonly List<string> _lines = new List<string>();

    private void Awake()
    {
        ScenarioHooks.AddModuleProvider("Example", 0, registry =>
        {
            registry.Register(new ExampleWaitModule());
            registry.Register(new ExamplePressKeyModule());
        });
        Application.logMessageReceived += OnLog;
    }

    private void OnDestroy() => Application.logMessageReceived -= OnLog;

    private IEnumerator Start()
    {
        if (runner == null) runner = FindFirstObjectByType<ScenarioRunner>();
        if (runner == null || scenarioJson == null) { Debug.LogError("[Example] ScenarioRunner 또는 시나리오 JSON 이 비어 있습니다"); yield break; }

        if (autoStartHost && !InstanceFinder.IsServerStarted)
        {
            InstanceFinder.ServerManager.StartConnection();
            InstanceFinder.ClientManager.StartConnection();
        }
        while (!InstanceFinder.IsServerStarted) yield return null;
        yield return null;

        var data = JsonConvert.DeserializeObject<ScenarioData>(scenarioJson.text);
        if (data == null || !ScenarioLoader.Validate(data)) { Debug.LogError("[Example] 시나리오 JSON 을 읽지 못했습니다"); yield break; }
        runner.StartScenario(data);
    }

    private void OnLog(string msg, string stack, LogType type)
    {
        if (!(msg.StartsWith("[Example]") || msg.StartsWith("[ScenarioRunner]") || msg.StartsWith("[ModuleRegistry]") || type == LogType.Error)) return;
        _lines.Add(msg.Length > 120 ? msg.Substring(0, 120) : msg);
        if (_lines.Count > 14) _lines.RemoveAt(0);
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 900, 400));
        GUILayout.Label("<b>DoDrill 모듈 최소 예제</b> — 엔진(시나리오) + 예제 단계 2개", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 16 });
        foreach (var l in _lines) GUILayout.Label(l);
        GUILayout.EndArea();
    }
}
