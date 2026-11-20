using FishNet;
using DoDrill;
using FishNet.Transporting;
using UnityEngine;

// ============================================================
//  SimpleTaskHud.cs — 프리팹 없이 쓰는 최소 단계 표시 (모듈화, 2026-11-20)
//
//  UI 프리팹(TaskProgressUI 등)을 만들기 전 시험용. 씬 아무 곳에 붙이면 화면 왼쪽 위에
//  시나리오 이름, 현재 단계(번호·제목), 안내 문구, 조작 도움말, 종료 문구를 보여 준다.
//  단계 제목은 JSON extra "title", 안내는 extra "hint".
// ============================================================
public class SimpleTaskHud : MonoBehaviour
{
    [TextArea] public string controlsHelp = "WASD 이동 · Q/E 아래/위 · 오른쪽 버튼 끌기: 시점 · 왼쪽 클릭: 집기/놓기/누르기 · F: 사용";
    public int fontSize = 18;

    private bool _ended;
    private bool _registered;
    private GUIStyle _title, _body;

    private void Update()
    {
        if (!_registered && InstanceFinder.ClientManager != null && InstanceFinder.IsClientStarted)
        {
            InstanceFinder.ClientManager.RegisterBroadcast<ScenarioEndBroadcast>(OnEnd);
            _registered = true;
        }
    }

    private void OnDestroy()
    {
        if (_registered && InstanceFinder.ClientManager != null)
            InstanceFinder.ClientManager.UnregisterBroadcast<ScenarioEndBroadcast>(OnEnd);
    }

    private void OnEnd(ScenarioEndBroadcast msg, Channel channel) => _ended = true;

    private void OnGUI()
    {
        if (_title == null)
        {
            _title = new GUIStyle(GUI.skin.label) { fontSize = fontSize + 4, fontStyle = FontStyle.Bold, richText = true };
            _body = new GUIStyle(GUI.skin.label) { fontSize = fontSize, richText = true, wordWrap = true };
        }

        var r = ScenarioStateReceiver.Instance;
        GUILayout.BeginArea(new Rect(12, 12, Mathf.Min(760, Screen.width - 24), 260), GUI.skin.box);

        var data = r != null ? r.CurrentScenario : null;
        GUILayout.Label(data != null ? data.scenarioName?.GetLocalized() ?? data.scenarioId : "시나리오 대기 중…", _title);

        if (_ended)
            GUILayout.Label("<color=lime>모든 단계 완료</color>", _title);
        else if (data != null)
        {
            int idx = r.GetCurrentTaskIndex();
            int total = data.scenario.tasks.Count;
            var cfg = r.GetCurrentTaskConfig();
            string title = cfg?.GetExtra("title");
            if (string.IsNullOrEmpty(title)) title = r.GetCurrentTask()?.moduleId ?? "";
            GUILayout.Label($"단계 {Mathf.Clamp(idx + 1, 1, total)} / {total} : <b>{title}</b>", _body);
            string hint = cfg?.GetExtra("hint");
            if (!string.IsNullOrEmpty(hint)) GUILayout.Label($"<color=#ffd27f>{hint}</color>", _body);
        }

        GUILayout.Space(6);
        GUILayout.Label($"<size={fontSize - 4}><color=#bbbbbb>{controlsHelp}</color></size>", _body);
        GUILayout.EndArea();
    }
}
