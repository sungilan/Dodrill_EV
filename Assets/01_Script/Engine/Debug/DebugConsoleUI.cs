using System.Collections.Generic;
using FishNet;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================
//  DebugConsoleUI.cs
//  빌드용 인게임 디버그 오버레이
//
//  Unity 씬 구조:
//    DebugConsoleUI
//    ├── Panel
//    │   ├── BTN_Toggle          (_toggleButton)
//    │   └── Root                (_root)
//    │       ├── TAB_Bar         (HorizontalLayoutGroup)
//    │       │   ├── BTN_TabCommands  (_tabCommandsBtn)
//    │       │   └── BTN_TabTasks     (_tabTasksBtn)
//    │       ├── PNL_Commands    (_commandsPanel)
//    │       │   ├── HLD_Btn     (_buttonContainer)
//    │       │   ├── TXT_Log     (_logText)
//    │       │   └── InputField  (_argsInput)
//    │       └── PNL_Tasks       (_tasksPanel)
//    │           ├── TXT_Status  (_taskStatusHeader)
//    │           ├── Content     (_taskListContent, VerticalLayoutGroup)
//    │           └── PNL_PageBar (HorizontalLayoutGroup)
//    │               ├── BTN_Prev   (_pagePrevBtn)  "◀"
//    │               ├── TXT_Page   (_pageLabel)    "1 / 5"
//    │               └── BTN_Next   (_pageNextBtn)  "▶"
//
//  TaskDebugItem 프리팹:
//    TaskDebugItem (HorizontalLayoutGroup + Image)
//    ├── TXT_Index  (TMP)    ~40px 고정
//    ├── TXT_Desc   (TMP)    Flexible
//    └── BTN_Jump   (Button) ~50px 고정
//
//  행 색상:
//    완료(이전) — 초록
//    Running   — 파랑
//    Completed — 파랑
//    Failed    — 빨강 → 재도전 시 Running으로 돌아오면 파랑 복귀
//    미진행    — 어두운 회색
// ============================================================

public class DebugConsoleUI : MonoBehaviour
{
    [Header("공통")]
    [SerializeField] private GameObject _root;
    [SerializeField] private Button _toggleButton;

    [Header("탭 버튼")]
    [SerializeField] private Button _tabCommandsBtn;
    [SerializeField] private Button _tabTasksBtn;

    [Header("패널")]
    [SerializeField] private GameObject _commandsPanel;
    [SerializeField] private GameObject _tasksPanel;

    [Header("Commands 패널")]
    [SerializeField] private Transform _buttonContainer;
    [SerializeField] private TMP_InputField _argsInput;
    [SerializeField] private TextMeshProUGUI _logText;
    [SerializeField] private GameObject _commandButtonPrefab;

    [Header("Tasks 패널")]
    [SerializeField] private Transform _taskListContent;
    [SerializeField] private GameObject _taskItemPrefab;
    [SerializeField] private TextMeshProUGUI _taskStatusHeader;

    [Header("페이지네이션")]
    [SerializeField] private Button _pagePrevBtn;
    [SerializeField] private Button _pageNextBtn;
    [SerializeField] private TextMeshProUGUI _pageLabel;
    [SerializeField] private int _pageSize = 10;

    [Header("탭 색상")]
    [SerializeField] private Color _tabActiveColor = new Color(0.25f, 0.60f, 1.00f);
    [SerializeField] private Color _tabInactiveColor = new Color(0.25f, 0.25f, 0.25f);

    [Header("Settings")]
    [SerializeField] private int _maxLogLines = 50;

    // ── 내부 상태 ─────────────────────────
    private bool _isOpen = false;
    private readonly List<string> _logLines = new();
    private readonly List<GameObject> _taskItems = new();

    private ScenarioData _cachedData;
    private int _currentIndex;
    private TaskStatus _currentStatus;

    private int _currentPage = 0;
    private int _totalPages = 1;

    // ── 생명주기 ──────────────────────────

    private void Awake()
    {
        if (!DebugBootstrapper.IsDebugMode)
        {
            gameObject.SetActive(false);
            return;
        }

        _root.SetActive(false);
        _toggleButton.onClick.AddListener(ToggleConsole);
        DebugLogger.OnLogAdded += AppendLog;

        _tabCommandsBtn?.onClick.AddListener(() => SwitchTab(false));
        _tabTasksBtn?.onClick.AddListener(() => SwitchTab(true));

        _pagePrevBtn?.onClick.AddListener(PrevPage);
        _pageNextBtn?.onClick.AddListener(NextPage);
    }

    private void OnEnable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated += OnTaskStateUpdated;
        ScenarioStateReceiver.OnSnapshotReceived += OnSnapshotReceived;
    }

    private void OnDisable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated -= OnTaskStateUpdated;
        ScenarioStateReceiver.OnSnapshotReceived -= OnSnapshotReceived;
    }

    private void OnDestroy()
    {
        DebugLogger.OnLogAdded -= AppendLog;
    }

    private void Start()
    {
        if (!DebugBootstrapper.IsDebugMode) return;
        BuildCommandButtons();
        SwitchTab(false);
    }

    // ── 탭 전환 ───────────────────────────

    private void SwitchTab(bool showTasks)
    {
        _commandsPanel?.SetActive(!showTasks);
        _tasksPanel?.SetActive(showTasks);
        SetTabColor(_tabCommandsBtn, !showTasks);
        SetTabColor(_tabTasksBtn, showTasks);
        if (showTasks) RefreshTaskList();
    }

    private void SetTabColor(Button btn, bool active)
    {
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        if (img != null) img.color = active ? _tabActiveColor : _tabInactiveColor;
    }

    // ── Commands 탭 ───────────────────────

    private void BuildCommandButtons()
    {
        if (DebugBootstrapper.Registry == null) return;

        foreach (var (key, command) in DebugBootstrapper.Registry.Commands)
        {
            var go = Instantiate(_commandButtonPrefab, _buttonContainer);
            var btn = go.GetComponent<Button>();
            var lbl = go.GetComponentInChildren<TextMeshProUGUI>();

            if (lbl != null) lbl.text = $"[{command.Category}] {command.Name}";

            string capturedKey = key;
            btn?.onClick.AddListener(() => SendCommand(capturedKey));
        }
    }

    private void SendCommand(string commandKey)
    {
        string argsJson = "";
        string[] args = System.Array.Empty<string>();
        if (!string.IsNullOrEmpty(_argsInput?.text))
        {
            args = _argsInput.text.Trim().Split(' ');
            argsJson = JsonConvert.SerializeObject(args);
        }

        // Host 모드(서버+클라이언트) — Registry 직접 실행
        if (InstanceFinder.IsServerStarted && DebugBootstrapper.Registry != null)
        {
            int callerId = InstanceFinder.ClientManager?.Connection?.ClientId ?? -99;
            DebugBootstrapper.Registry.Execute(commandKey, args, callerId);
            Debug.Log($"[DebugConsoleUI] Registry 직접 실행 → {commandKey}");
            return;
        }

        // 데디케이티드 서버 구성 — Broadcast
        int id = InstanceFinder.ClientManager?.Connection?.ClientId ?? -1;
        InstanceFinder.ClientManager?.Broadcast(new DebugCommandBroadcast
        {
            commandKey = commandKey,
            argsJson = argsJson,
            callerId = id,
        });
    }

    private void AppendLog(DebugLogEntry entry)
    {
        _logLines.Add(entry.ToString());
        if (_logLines.Count > _maxLogLines)
            _logLines.RemoveAt(0);
        if (_logText != null)
            _logText.text = string.Join("\n", _logLines);
    }

    // ── Tasks 탭: 이벤트 수신 ─────────────

    private void OnTaskStateUpdated(TaskStateBroadcast broadcast)
    {
        _currentIndex = broadcast.currentTask.taskIndex;
        _currentStatus = broadcast.currentTask.status;

        UpdateStatusHeader(_currentIndex, broadcast.totalTaskCount, _currentStatus);

        if (_tasksPanel != null && _tasksPanel.activeSelf)
        {
            int targetPage = _currentIndex / _pageSize;
            if (targetPage != _currentPage)
            {
                _currentPage = targetPage;
                RenderPage();
            }
            else
            {
                RefreshRowColors();
            }
        }
    }

    private void OnSnapshotReceived(ScenarioSnapshotBroadcast broadcast)
    {
        _currentIndex = broadcast.currentTask.taskIndex;
        _currentStatus = broadcast.currentTask.status;
        _cachedData = broadcast.scenarioData;

        UpdateStatusHeader(_currentIndex, broadcast.totalTaskCount, _currentStatus);

        if (_tasksPanel != null && _tasksPanel.activeSelf)
            RefreshTaskList();
    }

    // ── Tasks 탭: 리스트 초기화 ───────────

    private void RefreshTaskList()
    {
        if (_cachedData == null)
        {
            var receiver = Object.FindFirstObjectByType<ScenarioReceiver>();
            _cachedData = receiver?.ReceivedData;
        }
        if (_cachedData?.scenario?.tasks == null) return;

        var stateReceiver = Object.FindFirstObjectByType<ScenarioStateReceiver>();
        _currentIndex = stateReceiver?.CurrentTaskState.taskIndex ?? _currentIndex;
        _currentStatus = stateReceiver?.CurrentTaskState.status ?? _currentStatus;

        int taskCount = _cachedData.scenario.tasks.Count;
        _totalPages = Mathf.CeilToInt((float)taskCount / _pageSize);
        _currentPage = _currentIndex / _pageSize;

        RenderPage();
        UpdateStatusHeader(_currentIndex, taskCount, _currentStatus);
    }

    // ── Tasks 탭: 페이지 렌더링 ───────────

    private void RenderPage()
    {
        foreach (var item in _taskItems)
            if (item != null) Destroy(item);
        _taskItems.Clear();

        if (_cachedData?.scenario?.tasks == null) return;

        int start = _currentPage * _pageSize;
        int end = Mathf.Min(start + _pageSize, _cachedData.scenario.tasks.Count);

        for (int i = start; i < end; i++)
        {
            var taskDef = _cachedData.scenario.tasks[i];
            var config = _cachedData.GetModuleConfig(taskDef.moduleId);
            string desc = config?.GetExtra("description");
            if (string.IsNullOrEmpty(desc)) desc = taskDef.moduleId;

            var go = Instantiate(_taskItemPrefab, _taskListContent);
            SetupTaskItem(go, i, desc);
            _taskItems.Add(go);
        }

        UpdatePageLabel();
        UpdatePageButtons();
    }

    // ── Tasks 탭: 아이템 셋업 ─────────────

    private void SetupTaskItem(GameObject go, int index, string desc)
    {
        var texts = go.GetComponentsInChildren<TextMeshProUGUI>();
        if (texts.Length > 0) texts[0].text = $"{index + 1:D2}";
        if (texts.Length > 1) texts[1].text = desc;

        var btn = go.GetComponentInChildren<Button>();
        if (btn != null)
        {
            int captured = index;
            btn.onClick.AddListener(() => SendJumpCommand(captured));
        }

        ApplyRowColor(go, index);
    }

    // ── Tasks 탭: 행 색상 ─────────────────
    //
    //  이전 Task  → 초록  (완료 확정)
    //  현재 Task
    //    Running / Completed → 파랑
    //    Failed              → 빨강 (재도전 시 Running 수신 → 파랑 복귀)
    //  미진행 Task → 어두운 회색

    private void ApplyRowColor(GameObject go, int itemIndex)
    {
        var img = go.GetComponent<Image>();
        if (img == null) return;

        if (itemIndex < _currentIndex)
        {
            img.color = new Color(0.20f, 0.45f, 0.20f, 0.40f);     // 초록 — 완료
        }
        else if (itemIndex == _currentIndex)
        {
            img.color = _currentStatus switch
            {
                TaskStatus.Failed => new Color(0.60f, 0.20f, 0.20f, 0.65f), // 빨강 — 실패
                _ => new Color(0.20f, 0.50f, 0.90f, 0.65f), // 파랑 — Running/Completed
            };
        }
        else
        {
            img.color = new Color(0.12f, 0.12f, 0.12f, 0.40f);     // 회색 — 미진행
        }
    }

    private void RefreshRowColors()
    {
        int start = _currentPage * _pageSize;
        for (int i = 0; i < _taskItems.Count; i++)
            if (_taskItems[i] != null)
                ApplyRowColor(_taskItems[i], start + i);
    }

    // ── Tasks 탭: 헤더 / 페이지 라벨 ─────────

    private void UpdateStatusHeader(int currentIndex, int total, TaskStatus status)
    {
        if (_taskStatusHeader == null) return;
        _taskStatusHeader.text = $"Task {currentIndex + 1} / {total}  [{status}]";
    }

    private void UpdatePageLabel()
    {
        if (_pageLabel == null) return;
        _pageLabel.text = $"{_currentPage + 1} / {_totalPages}";
    }

    private void UpdatePageButtons()
    {
        if (_pagePrevBtn != null) _pagePrevBtn.interactable = _currentPage > 0;
        if (_pageNextBtn != null) _pageNextBtn.interactable = _currentPage < _totalPages - 1;
    }

    // ── 페이지 이동 ───────────────────────

    private void PrevPage()
    {
        if (_currentPage <= 0) return;
        _currentPage--;
        RenderPage();
    }

    private void NextPage()
    {
        if (_currentPage >= _totalPages - 1) return;
        _currentPage++;
        RenderPage();
    }

    // ── Tasks 탭: Jump 전송 ───────────────

    private void SendJumpCommand(int taskIndex)
    {
        string[] args = new[] { taskIndex.ToString() };
        string argsJson = JsonConvert.SerializeObject(args);

        // Host 모드 — Registry 직접 실행
        if (InstanceFinder.IsServerStarted && DebugBootstrapper.Registry != null)
        {
            int callerId = InstanceFinder.ClientManager?.Connection?.ClientId ?? -99;
            DebugBootstrapper.Registry.Execute("Scenario/JumpToTask", args, callerId);
            Debug.Log($"[DebugConsoleUI] JumpToTask({taskIndex}) Registry 직접 실행");
            return;
        }

        int id = InstanceFinder.ClientManager?.Connection?.ClientId ?? -1;
        InstanceFinder.ClientManager?.Broadcast(new DebugCommandBroadcast
        {
            commandKey = "Scenario/JumpToTask",
            argsJson = argsJson,
            callerId = id,
        });
        Debug.Log($"[DebugConsoleUI] JumpToTask({taskIndex}) Broadcast");
    }

    // ── 토글 ──────────────────────────────

    private void ToggleConsole()
    {
        _isOpen = !_isOpen;
        _root.SetActive(_isOpen);
    }
}