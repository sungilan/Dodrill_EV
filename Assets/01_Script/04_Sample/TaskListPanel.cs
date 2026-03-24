using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

public class TaskListPanel : MonoBehaviour
{
    [Header("Header References")]
    [SerializeField] private TextMeshProUGUI _subtitleText;
    [SerializeField] private TextMeshProUGUI _titleText;

    [Header("List References")]
    [SerializeField] private Transform _listContainer;
    [SerializeField] private GameObject _taskItemPrefab;
    [SerializeField] private GameObject _panelRoot;

    [Header("Timer (선택)")]
    [SerializeField] private TextMeshProUGUI _timerText;
    [SerializeField] private Image _progressBar;

    // TaskProgressUI 등 외부에서 구독할 수 있는 이벤트
    public static event System.Action<TaskState, ScenarioData, int> OnTaskRefreshed;

    private readonly List<TaskItemUI> _items = new();
    private ScenarioData _scenarioData;
    private TaskState _current;
    private int _totalCount;
    private bool _isTracking;

    // ─────────────────────────────────────────
    // 생명주기
    // ─────────────────────────────────────────

    private void Awake()
    {
        if (_panelRoot != null) _panelRoot.SetActive(false);
    }

    private void Start()
    {
        if (_scenarioData != null) return;

        // 컴포넌트 활성화 전에 스냅샷이 이미 수신된 경우 직접 가져옴
        var stateReceiver = FindFirstObjectByType<ScenarioStateReceiver>();
        if (stateReceiver?.CurrentScenario != null)
        {
            _scenarioData = stateReceiver.CurrentScenario;
            _current      = stateReceiver.CurrentTaskState;
            _totalCount   = stateReceiver.TotalTaskCount;
            _isTracking   = true;
            BuildList();
            RefreshHeader();
            UpdateCheckboxes(_current.taskIndex, _current.status);
            OnTaskRefreshed?.Invoke(_current, _scenarioData, _totalCount);
        }
    }

    private void OnEnable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated += OnTaskStateUpdated;
        ScenarioStateReceiver.OnSnapshotReceived += OnSnapshotReceived;
        ScenarioReceiver.OnScenarioReceived      += OnScenarioReceived;
    }

    private void OnDisable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated -= OnTaskStateUpdated;
        ScenarioStateReceiver.OnSnapshotReceived -= OnSnapshotReceived;
        ScenarioReceiver.OnScenarioReceived      -= OnScenarioReceived;
    }

    private void Update()
    {
        if (!_isTracking || _current.status != TaskStatus.Running) return;
        _current.elapsedTime += Time.deltaTime;
        RefreshTimer();
    }

    // ─────────────────────────────────────────
    // 이벤트 수신
    // ─────────────────────────────────────────

    // ScenarioDataBroadcast 수신 즉시 → 리스트 구성 (TaskStateBroadcast보다 빠름)
    private void OnScenarioReceived(ScenarioData data)
    {
        if (data == null) return;
        _scenarioData = data;
        BuildList();
        RefreshHeader();
    }

    private void OnSnapshotReceived(ScenarioSnapshotBroadcast broadcast)
    {
        _scenarioData = broadcast.scenarioData;
        _current      = broadcast.currentTask;
        _totalCount   = broadcast.totalTaskCount;
        _isTracking   = true;

        BuildList();
        RefreshHeader();
        UpdateCheckboxes(_current.taskIndex, _current.status);
        OnTaskRefreshed?.Invoke(_current, _scenarioData, _totalCount);
    }

    private void OnTaskStateUpdated(TaskStateBroadcast broadcast)
    {
        _current    = broadcast.currentTask;
        _totalCount = broadcast.totalTaskCount;
        _isTracking = true;

        if (_scenarioData == null)
        {
            // 우선순위 1: ScenarioStateReceiver (스냅샷 데이터 보관)
            var stateReceiver = FindFirstObjectByType<ScenarioStateReceiver>();
            if (stateReceiver?.CurrentScenario != null)
            {
                _scenarioData = stateReceiver.CurrentScenario;
                BuildList();
            }
            else
            {
                // 우선순위 2: ScenarioReceiver (ScenarioDataBroadcast 수신)
                var receiver = FindFirstObjectByType<ScenarioReceiver>();
                if (receiver?.ReceivedData != null)
                {
                    _scenarioData = receiver.ReceivedData;
                    BuildList();
                }
            }
        }

        RefreshHeader();
        UpdateCheckboxes(_current.taskIndex, _current.status);
        OnTaskRefreshed?.Invoke(_current, _scenarioData, _totalCount);
    }

    // ─────────────────────────────────────────
    // 리스트 구성
    // ─────────────────────────────────────────

    private void BuildList()
    {
        foreach (var item in _items)
            if (item != null) Destroy(item.gameObject);
        _items.Clear();

        if (_panelRoot != null) _panelRoot.SetActive(true);
        if (_scenarioData == null) return;

        for (int i = 0; i < _scenarioData.scenario.tasks.Count; i++)
        {
            var taskDef = _scenarioData.scenario.tasks[i];
            var go = Instantiate(_taskItemPrefab, _listContainer);
            var item = go.GetComponent<TaskItemUI>();

            if (item == null)
            {
                Debug.LogWarning("[TaskListPanel] TaskItemUI 컴포넌트 없음");
                continue;
            }

            var config = _scenarioData.GetModuleConfig(taskDef.moduleId);
            string title = config != null ? config.GetExtra("description") : taskDef.moduleId;

            item.Setup(i + 1, title, TaskStatus.Pending);
            _items.Add(item);
        }

        // 아이템 생성 완료 후 한 프레임 대기하고 높이 조절
        StartCoroutine(UpdatePanelHeightNextFrame());
    }

    // ─────────────────────────────────────────
    // 배경 높이 자동 조절
    // ─────────────────────────────────────────

    private IEnumerator UpdatePanelHeightNextFrame()
    {
        // LayoutGroup 이 레이아웃 계산을 마칠 때까지 한 프레임 대기
        yield return new WaitForEndOfFrame();
        UpdatePanelHeight();
    }

    private void UpdatePanelHeight()
    {
        var rt = _listContainer as RectTransform;
        if (rt == null || _items.Count == 0) return;

        const float itemHeight = 72f;

        var layout = rt.GetComponent<VerticalLayoutGroup>();
        float spacing = layout != null ? layout.spacing : 0f;
        float paddingTop = layout != null ? layout.padding.top : 0f;
        float paddingBot = layout != null ? layout.padding.bottom : 0f;

        float total = (itemHeight * _items.Count)
                    + (spacing * (_items.Count - 1))
                    + paddingTop + paddingBot;

        rt.sizeDelta = new Vector2(rt.sizeDelta.x, total);
    }
    // ─────────────────────────────────────────
    // 헤더 갱신
    // ─────────────────────────────────────────

    private void RefreshHeader()
    {
        if (_scenarioData == null) return;

        if (_subtitleText != null)
            _subtitleText.text = _scenarioData.scenarioName?.GetLocalized()
                                 ?? _scenarioData.scenarioName?.ko ?? "";

        if (_titleText != null
            && _current.taskIndex >= 0
            && _current.taskIndex < _scenarioData.scenario.tasks.Count)
        {
            var taskDef = _scenarioData.scenario.tasks[_current.taskIndex];
            var config = _scenarioData.GetModuleConfig(taskDef.moduleId);
            _titleText.text = config != null ? config.GetExtra("description") : taskDef.moduleId;
        }
    }

    // ─────────────────────────────────────────
    // 체크박스 상태 갱신
    // ─────────────────────────────────────────

    private void UpdateCheckboxes(int currentIndex, TaskStatus status)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (i < currentIndex)
                _items[i].SetStatus(TaskStatus.Completed);
            else if (i == currentIndex)
                _items[i].SetStatus(status);
            else
                _items[i].SetStatus(TaskStatus.Pending);
        }
    }

    // ─────────────────────────────────────────
    // 타이머 갱신
    // ─────────────────────────────────────────

    private void RefreshTimer()
    {
        if (_timerText == null) return;

        if (_current.timeoutSeconds > 0f)
        {
            float remaining = Mathf.Max(0f, _current.timeoutSeconds - _current.elapsedTime);
            _timerText.text = $"{remaining:F0}s";
            if (_progressBar != null)
                _progressBar.fillAmount = 1f - (remaining / _current.timeoutSeconds);
        }
        else
        {
            _timerText.text = "";
            if (_progressBar != null) _progressBar.fillAmount = 0f;
        }
    }
}