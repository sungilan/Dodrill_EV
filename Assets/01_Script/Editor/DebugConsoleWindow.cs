#if UNITY_EDITOR
using System.Collections.Generic;
using FishNet;
using Newtonsoft.Json;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

// ============================================================
//  DebugConsoleWindow.cs
//  Odin Editor Window — 인게임 디버그 콘솔
//
//  메뉴: DoDrill > Debug Console  (단축키 Shift+Alt+D)
//  ShowIf 대신 OnInspectorGUI 오버라이드로 탭 전환 처리
//  (GameKit.Dependencies ShowIfAttribute 충돌 우회)
// ============================================================

public class DebugConsoleWindow : OdinEditorWindow
{
    [MenuItem("DoDrill/Debug Console #&d")]
    private static void OpenWindow()
    {
        var window = GetWindow<DebugConsoleWindow>();
        window.titleContent = new GUIContent("DoDrill Debug");
        window.minSize = new Vector2(500, 600);
        window.Show();
    }

    // ── 탭 ────────────────────────────────
    [EnumToggleButtons, HideLabel, PropertyOrder(-10)]
    public Tab CurrentTab = Tab.Tasks;
    public enum Tab { Tasks, Commands, Log }

    // ── Tasks 탭 데이터 ───────────────────
    [HideInInspector] public string TaskStatusText = "시나리오 수신 대기 중...";
    [HideInInspector] public string CurrentModuleText = "";
    [HideInInspector] public string PageLabel = "1 / 1";
    [HideInInspector] public List<TaskRow> TaskRows = new();

    // ── Commands 탭 데이터 ────────────────
    [HideInInspector] public string ArgsInput = "";
    [HideInInspector] public List<CommandRow> CommandRows = new();

    // ── Log 탭 데이터 ─────────────────────
    [HideInInspector] public List<LogRow> LogRows = new();

    // ── 내부 상태 ─────────────────────────
    private int _currentPage = 0;
    private Vector2 _taskScrollPos;
    private int _totalPages = 1;
    private const int PAGE_SIZE = 10;

    private ScenarioData _cachedData;
    private int _currentIndex;
    private TaskStatus _currentStatus;
    private string _currentModuleId = "";

    // ── 윈도우 생명주기 ───────────────────

    protected override void OnEnable()
    {
        base.OnEnable();
        EditorApplication.update += OnEditorUpdate;
        ScenarioStateReceiver.OnTaskStateUpdated += OnTaskStateUpdated;
        ScenarioStateReceiver.OnSnapshotReceived += OnSnapshotReceived;
        DebugLogger.OnLogAdded += OnLogAdded;

        var receiver = Object.FindFirstObjectByType<ScenarioReceiver>();
        if (receiver?.ReceivedData != null)
        {
            _cachedData = receiver.ReceivedData;
            var sr = Object.FindFirstObjectByType<ScenarioStateReceiver>();
            if (sr != null)
            {
                _currentIndex = sr.CurrentTaskState.taskIndex;
                _currentStatus = sr.CurrentTaskState.status;
            }
            RefreshAll();
        }
        RebuildCommandRows();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        EditorApplication.update -= OnEditorUpdate;
        ScenarioStateReceiver.OnTaskStateUpdated -= OnTaskStateUpdated;
        ScenarioStateReceiver.OnSnapshotReceived -= OnSnapshotReceived;
        DebugLogger.OnLogAdded -= OnLogAdded;
    }

    private float _pollTimer = 0f;
    private const float POLL_INTERVAL = 0.5f;

    private void OnEditorUpdate()
    {
        if (!Application.isPlaying) return;

        // 데이터 없으면 주기적으로 재시도
        _pollTimer += 0.02f; // EditorApplication.update ~ 50fps
        if (_pollTimer >= POLL_INTERVAL)
        {
            _pollTimer = 0f;
            TryLoadFromScene();
        }

        Repaint();
    }

    private void TryLoadFromScene()
    {
        var receiver = Object.FindFirstObjectByType<ScenarioReceiver>();
        if (receiver?.ReceivedData == null) return;

        bool dataChanged = _cachedData != receiver.ReceivedData;
        _cachedData = receiver.ReceivedData;

        var sr = Object.FindFirstObjectByType<ScenarioStateReceiver>();
        if (sr != null)
        {
            int prevIndex = _currentIndex;
            _currentIndex = sr.CurrentTaskState.taskIndex;
            _currentStatus = sr.CurrentTaskState.status;

            // Task 인덱스가 바뀌었으면 페이지 자동 이동
            if (prevIndex != _currentIndex)
            {
                int targetPage = _currentIndex / PAGE_SIZE;
                if (targetPage != _currentPage)
                {
                    _currentPage = targetPage;
                    dataChanged = true; // 페이지 바뀌면 전체 재렌더
                }
            }
        }

        // ScenarioRunner에서 현재 moduleId 직접 취득
        var runner = Object.FindFirstObjectByType<ScenarioRunner>();
        if (runner != null && !string.IsNullOrEmpty(runner.CurrentModuleId))
            _currentModuleId = runner.CurrentModuleId;

        if (dataChanged || TaskRows.Count == 0)
            RefreshAll();
        else
            RefreshTaskRows(); // 색상/상태만 갱신
    }

    // ── Odin Inspector 오버라이드 ─────────
    // ShowIf 대신 탭에 따라 직접 GUI 그리기

    protected override void DrawEditors()
    {
        // EnumToggleButtons (탭 선택) 는 항상 표시
        base.DrawEditors();
    }

    protected override void OnImGUI()
    {
        // 탭 버튼 (EnumToggleButtons)
        EditorGUILayout.Space(4);
        CurrentTab = (Tab)GUILayout.Toolbar((int)CurrentTab,
            new[] { "Tasks", "Commands", "Log" },
            GUILayout.Height(28));
        EditorGUILayout.Space(4);

        switch (CurrentTab)
        {
            case Tab.Tasks: DrawTasksTab(); break;
            case Tab.Commands: DrawCommandsTab(); break;
            case Tab.Log: DrawLogTab(); break;
        }
    }

    // ══════════════════════════════════════
    //  Tasks 탭 GUI
    // ══════════════════════════════════════

    private void DrawTasksTab()
    {
        // ── 상단 고정 영역 ──────────────────

        // 상태 헤더
        var headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
        EditorGUILayout.LabelField(TaskStatusText, headerStyle);

        if (!string.IsNullOrEmpty(CurrentModuleText))
        {
            var moduleStyle = new GUIStyle(EditorStyles.label)
                { fontSize = 11, normal = { textColor = new Color(0.7f, 0.9f, 1f) } };
            EditorGUILayout.LabelField($"▶  {CurrentModuleText}", moduleStyle);
        }
        EditorGUILayout.Space(4);

        // ForceComplete / ForceFail
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
            if (GUILayout.Button("ForceComplete", GUILayout.Height(28)))
                SendCommand("Scenario/ForceComplete");

            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("ForceFail", GUILayout.Height(28)))
                SendCommand("Scenario/ForceFail");

            GUI.backgroundColor = Color.white;
        }

        EditorGUILayout.Space(4);

        // 페이지 바 (항상 상단에 표시)
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = _currentPage > 0;
            if (GUILayout.Button("◀", GUILayout.Width(36), GUILayout.Height(24)))
            { _currentPage--; RefreshTaskRows(); }

            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.Label(PageLabel, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            GUI.enabled = _currentPage < _totalPages - 1;
            if (GUILayout.Button("▶", GUILayout.Width(36), GUILayout.Height(24)))
            { _currentPage++; RefreshTaskRows(); }

            GUI.enabled = true;
        }

        EditorGUILayout.Space(4);

        // Task 행 헤더
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("#", GUILayout.Width(30));
            GUILayout.Label("", GUILayout.Width(24));
            GUILayout.Label("설명", GUILayout.ExpandWidth(true));
            GUILayout.Label("moduleId", GUILayout.Width(150));
            GUILayout.Label("", GUILayout.Width(54));
        }

        // ── Task 행 목록 (스크롤 가능) ───────
        _taskScrollPos = EditorGUILayout.BeginScrollView(_taskScrollPos);

        foreach (var row in TaskRows)
        {
            Color rowColor = row.BgColor;
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = rowColor;

            using (new EditorGUILayout.HorizontalScope(GUI.skin.box, GUILayout.Height(24)))
            {
                GUI.backgroundColor = prevBg;
                GUILayout.Label(row.Num.ToString("D2"), GUILayout.Width(30));
                GUILayout.Label(row.Status, GUILayout.Width(24));
                GUILayout.Label(row.Desc, GUILayout.ExpandWidth(true));
                GUILayout.Label(row.ModuleId, GUILayout.Width(150));

                GUI.backgroundColor = new Color(0.8f, 0.8f, 1f);
                if (GUILayout.Button("Jump", GUILayout.Width(50), GUILayout.Height(20)))
                    SendJump(row.TaskIndex);
                GUI.backgroundColor = prevBg;
            }
        }

        EditorGUILayout.EndScrollView();
    }

    // ══════════════════════════════════════
    //  Commands 탭 GUI
    // ══════════════════════════════════════

    private void DrawCommandsTab()
    {
        EditorGUILayout.LabelField("Args (공백 구분)", EditorStyles.boldLabel);
        ArgsInput = EditorGUILayout.TextField(ArgsInput);
        EditorGUILayout.Space(6);

        // 커맨드 행 헤더
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("Category", GUILayout.Width(80));
            GUILayout.Label("Name", GUILayout.Width(130));
            GUILayout.Label("Description", GUILayout.ExpandWidth(true));
            GUILayout.Label("", GUILayout.Width(54));
        }

        foreach (var row in CommandRows)
        {
            using (new EditorGUILayout.HorizontalScope(GUI.skin.box, GUILayout.Height(24)))
            {
                GUILayout.Label(row.Category, GUILayout.Width(80));
                GUILayout.Label(row.Name, GUILayout.Width(130));
                GUILayout.Label(row.Desc, GUILayout.ExpandWidth(true));

                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.8f, 1f, 0.8f);
                if (GUILayout.Button("실행", GUILayout.Width(50), GUILayout.Height(20)))
                    SendCommandWithArgs(row.Key);
                GUI.backgroundColor = prevBg;
            }
        }
    }

    // ══════════════════════════════════════
    //  Log 탭 GUI
    // ══════════════════════════════════════

    private void DrawLogTab()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("새로고침", GUILayout.Height(24))) RebuildLogRows();
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.7f, 0.4f);
            if (GUILayout.Button("지우기", GUILayout.Height(24)))
            {
                DebugLogger.Instance?.Clear();
                LogRows.Clear();
            }
            GUI.backgroundColor = prevBg;
        }

        EditorGUILayout.Space(4);

        foreach (var row in LogRows)
        {
            EditorGUILayout.LabelField(row.Entry, EditorStyles.wordWrappedMiniLabel);
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));
        }
    }

    // ── 이벤트 수신 ───────────────────────

    private void OnTaskStateUpdated(TaskStateBroadcast broadcast)
    {
        int prevIndex = _currentIndex;
        _currentIndex = broadcast.currentTask.taskIndex;
        _currentStatus = broadcast.currentTask.status;

        // Task 인덱스 바뀌면 즉시 페이지 이동
        if (prevIndex != _currentIndex)
        {
            int targetPage = _currentIndex / PAGE_SIZE;
            if (targetPage != _currentPage)
                _currentPage = targetPage;
        }

        // moduleId 갱신: ScenarioRunner에서 직접 취득
        var runner = Object.FindFirstObjectByType<ScenarioRunner>();
        if (runner != null) _currentModuleId = runner.CurrentModuleId;
        else if (_cachedData?.scenario?.tasks?.Count > _currentIndex)
            _currentModuleId = _cachedData.scenario.tasks[_currentIndex].moduleId;

        UpdateStatusText(broadcast.totalTaskCount);
        RefreshTaskRows();
        Repaint();
    }

    private void OnSnapshotReceived(ScenarioSnapshotBroadcast broadcast)
    {
        _cachedData = broadcast.scenarioData;
        _currentIndex = broadcast.currentTask.taskIndex;
        _currentStatus = broadcast.currentTask.status;
        RefreshAll();
        Repaint();
    }

    private void OnLogAdded(DebugLogEntry entry)
    {
        LogRows.Insert(0, new LogRow { Entry = entry.ToString() });
        if (LogRows.Count > 200) LogRows.RemoveAt(LogRows.Count - 1);
        Repaint();
    }

    // ── 데이터 갱신 ───────────────────────

    private void RefreshAll()
    {
        if (_cachedData?.scenario?.tasks == null) return;
        int total = _cachedData.scenario.tasks.Count;
        _totalPages = Mathf.CeilToInt((float)total / PAGE_SIZE);
        _currentPage = Mathf.Clamp(_currentIndex / PAGE_SIZE, 0, _totalPages - 1);
        UpdateStatusText(total);
        RefreshTaskRows();
        RebuildCommandRows();
    }

    private void UpdateStatusText(int total)
    {
        TaskStatusText = $"Task  {_currentIndex + 1} / {total}    [{_currentStatus}]";
        CurrentModuleText = _currentModuleId;
    }

    private void RefreshTaskRows()
    {
        TaskRows.Clear();
        if (_cachedData?.scenario?.tasks == null) return;

        int start = _currentPage * PAGE_SIZE;
        int end = Mathf.Min(start + PAGE_SIZE, _cachedData.scenario.tasks.Count);

        for (int i = start; i < end; i++)
        {
            var taskDef = _cachedData.scenario.tasks[i];
            var config = _cachedData.GetModuleConfig(taskDef.moduleId);
            string desc = config?.GetExtra("description");
            if (string.IsNullOrEmpty(desc)) desc = taskDef.moduleId;

            string statusIcon =
                i < _currentIndex ? "✅" :
                i == _currentIndex ? (_currentStatus == TaskStatus.Failed ? "❌" :
                                      _currentStatus == TaskStatus.Completed ? "🔵" : "▶") :
                                     "○";

            Color bgColor =
                i < _currentIndex ? new Color(0.2f, 0.5f, 0.2f, 0.5f) :
                i == _currentIndex ? (_currentStatus == TaskStatus.Failed
                                      ? new Color(0.6f, 0.2f, 0.2f, 0.6f)
                                      : new Color(0.2f, 0.5f, 0.9f, 0.6f)) :
                                     new Color(0.3f, 0.3f, 0.3f, 0.3f);

            TaskRows.Add(new TaskRow
            {
                Num = i + 1,
                Status = statusIcon,
                Desc = desc,
                ModuleId = taskDef.moduleId,
                TaskIndex = i,
                BgColor = bgColor,
            });
        }

        PageLabel = $"{_currentPage + 1} / {_totalPages}";
    }

    private void RebuildCommandRows()
    {
        CommandRows.Clear();
        if (DebugBootstrapper.Registry == null) return;

        foreach (var (key, command) in DebugBootstrapper.Registry.Commands)
        {
            CommandRows.Add(new CommandRow
            {
                Category = command.Category,
                Name = command.Name,
                Desc = command.Description,
                Key = key,
            });
        }
    }

    private void RebuildLogRows()
    {
        LogRows.Clear();
        if (DebugLogger.Instance == null) return;
        foreach (var entry in DebugLogger.Instance.Logs)
            LogRows.Insert(0, new LogRow { Entry = entry.ToString() });
    }

    // ── FishNet 커맨드 전송 ───────────────

    internal void SendCommand(string commandKey, string argsJson = "")
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[DebugConsole] 플레이 모드에서만 전송 가능합니다.");
            return;
        }

        string[] args = string.IsNullOrEmpty(argsJson)
            ? System.Array.Empty<string>()
            : Newtonsoft.Json.JsonConvert.DeserializeObject<string[]>(argsJson);

        // Host 모드 (서버+클라이언트 동시) — Registry 직접 호출
        if (InstanceFinder.IsServerStarted && DebugBootstrapper.Registry != null)
        {
            DebugBootstrapper.Registry.Execute(commandKey, args, -99);
            Debug.Log($"[DebugConsole] Registry 직접 실행 → {commandKey}  args: {argsJson}");
            return;
        }

        // 데디케이티드 서버 구성 — 클라이언트로 Broadcast
        if (InstanceFinder.IsClientStarted)
        {
            int callerId = InstanceFinder.ClientManager?.Connection?.ClientId ?? -1;
            InstanceFinder.ClientManager.Broadcast(new DebugCommandBroadcast
            {
                commandKey = commandKey,
                argsJson = argsJson,
                callerId = callerId,
            });
            Debug.Log($"[DebugConsole] Broadcast → {commandKey}  args: {argsJson}");
            return;
        }

        Debug.LogWarning("[DebugConsole] 서버도 클라이언트도 연결 안 됨");
    }

    private void SendJump(int taskIndex)
    {
        string argsJson = JsonConvert.SerializeObject(new[] { taskIndex.ToString() });
        SendCommand("Scenario/JumpToTask", argsJson);
    }

    private void SendCommandWithArgs(string key)
    {
        string argsJson = "";
        if (!string.IsNullOrEmpty(ArgsInput))
            argsJson = JsonConvert.SerializeObject(ArgsInput.Trim().Split(' '));
        SendCommand(key, argsJson);
    }

    // ── 행 데이터 클래스 ─────────────────

    public class TaskRow
    {
        public int Num;
        public string Status;
        public string Desc;
        public string ModuleId;
        public int TaskIndex;
        public Color BgColor;
    }

    public class CommandRow
    {
        public string Category;
        public string Name;
        public string Desc;
        public string Key;
    }

    public class LogRow
    {
        public string Entry;
    }
}
#endif