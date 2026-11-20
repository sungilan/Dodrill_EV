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

    [Header("Dialogue Dim (CanvasGroup Alpha)")]
    [SerializeField] private CanvasGroup _dialogueDimCanvasGroup;
    [SerializeField] private float _dialogueDimAlpha = 0.7f;
    [SerializeField] private float _dialogueDimFadeSeconds = 0.2f;

    public static event System.Action<TaskState, ScenarioData, int> OnTaskRefreshed;

    private readonly List<TaskItemUI> _items = new();
    private ScenarioData _scenarioData;
    private TaskState _current;
    private int _totalCount;
    private bool _isTracking;
    private Coroutine _dialogueBlurRoutine;

    private void Awake()
    {
        if(_panelRoot != null) _panelRoot.SetActive(false);

        // Content RectTransform에 ContentSizeFitter가 없으면 자동으로 추가
        if(_listContainer is RectTransform contentRt)
        {
            var csf = contentRt.GetComponent<ContentSizeFitter>();
            if(csf == null)
            {
                csf = contentRt.gameObject.AddComponent<ContentSizeFitter>();
                csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
        }

        ResolveDialogueDimCanvasGroup();
        SetDialogueDimVisibleImmediate(false);
    }

    private void Start()
    {
        TryHydrateFromReceivers();
    }

    private void OnEnable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated += OnTaskStateUpdated;
        ScenarioStateReceiver.OnSnapshotReceived += OnSnapshotReceived;
        ScenarioReceiver.OnScenarioReceived += OnScenarioReceived;
        //PatientDialogueUI.OnDialogueStartedGlobal += OnDialogueStartedGlobal;
        //PatientDialogueUI.OnDialogueEndedGlobal += OnDialogueEndedGlobal;

        // VR_UI가 늦게 활성화될 때(최초 진입 포함) 브로드캐스트 이벤트를 놓친 상태라도
        // 현재 수신 캐시(ScenarioReceiver/ScenarioStateReceiver)에서 즉시 UI를 복원한다.
        TryHydrateFromReceivers();
    }

    private void OnDisable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated -= OnTaskStateUpdated;
        ScenarioStateReceiver.OnSnapshotReceived -= OnSnapshotReceived;
        ScenarioReceiver.OnScenarioReceived -= OnScenarioReceived;
        //PatientDialogueUI.OnDialogueStartedGlobal -= OnDialogueStartedGlobal;
        //PatientDialogueUI.OnDialogueEndedGlobal -= OnDialogueEndedGlobal;

        if(_dialogueBlurRoutine != null)
        {
            StopCoroutine(_dialogueBlurRoutine);
            _dialogueBlurRoutine = null;
        }
    }

    private void Update()
    {
        if(!_isTracking || _current.status != TaskStatus.Running) return;
        _current.elapsedTime += Time.deltaTime;
        RefreshTimer();
    }

    private void OnScenarioReceived(ScenarioData data)
    {
        if(data == null) return;
        ApplyScenarioData(data);
    }

    private void OnSnapshotReceived(ScenarioSnapshotBroadcast broadcast)
    {
        ApplyScenarioData(broadcast.scenarioData);
        ApplyTaskState(broadcast.currentTask, broadcast.totalTaskCount);
    }

    private void OnTaskStateUpdated(TaskStateBroadcast broadcast)
    {
        if(_scenarioData == null) return;
        ApplyTaskState(broadcast.currentTask, broadcast.totalTaskCount);
    }

    private void ApplyScenarioData(ScenarioData data)
    {
        if(data == null || data == _scenarioData) return;
        _scenarioData = data;
        BuildList();
        RefreshHeader();
    }

    private void ApplyTaskState(TaskState state, int totalCount)
    {
        _current = state;
        _totalCount = totalCount;
        _isTracking = true;

        if(_scenarioData == null) return;

        RefreshHeader();
        UpdateCheckboxes(_current.taskIndex, _current.status);
        OnTaskRefreshed?.Invoke(_current, _scenarioData, _totalCount);
    }

    private void TryHydrateFromReceivers()
    {
        // 1) 스냅샷 수신 상태(가장 완전한 데이터)
        var stateReceiver = FindFirstObjectByType<ScenarioStateReceiver>(FindObjectsInactive.Include);
        if(stateReceiver != null)
        {
            if(stateReceiver.CurrentScenario != null)
                ApplyScenarioData(stateReceiver.CurrentScenario);

            if(_scenarioData != null)
                ApplyTaskState(stateReceiver.CurrentTaskState, stateReceiver.TotalTaskCount);
        }

        // 2) 스냅샷이 아직 없어도 ScenarioDataBroadcast 캐시로 리스트는 먼저 채운다.
        if(_scenarioData == null)
        {
            var scenarioReceiver = FindFirstObjectByType<ScenarioReceiver>(FindObjectsInactive.Include);
            if(scenarioReceiver?.ReceivedData != null)
                ApplyScenarioData(scenarioReceiver.ReceivedData);
        }
    }

    private void BuildList()
    {
        foreach(var item in _items)
            if(item != null) Destroy(item.gameObject);
        _items.Clear();

        if(_panelRoot != null) _panelRoot.SetActive(true);
        if(_scenarioData == null) return;

        for(int i = 0; i < _scenarioData.scenario.tasks.Count; i++)
        {
            var taskDef = _scenarioData.scenario.tasks[i];
            var go = Instantiate(_taskItemPrefab, _listContainer);
            var item = go.GetComponent<TaskItemUI>();

            if(item == null)
            {
                Debug.LogWarning("[TaskListPanel] TaskItemUI 컴포넌트 없음");
                continue;
            }

            var config = _scenarioData.GetModuleConfig(taskDef.moduleId);
            string title = config != null ? config.GetExtra("description") : taskDef.moduleId;
            item.Setup(i + 1, title, TaskStatus.Pending);
            _items.Add(item);
        }

        StartCoroutine(ForceLayoutRebuildNextFrame());
    }

    // ContentSizeFitter가 Content 높이를 자동 계산하도록 한 프레임 후 강제 리빌드
    private IEnumerator ForceLayoutRebuildNextFrame()
    {
        yield return null;
        var rt = _listContainer as RectTransform;
        if(rt != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
    }

    private void RefreshHeader()
    {
        if(_scenarioData == null) return;

        if(_subtitleText != null)
            _subtitleText.text = _scenarioData.scenarioName?.GetLocalized()
                                 ?? _scenarioData.scenarioName?.ko ?? "";

        if(_titleText != null
            && _current.taskIndex >= 0
            && _current.taskIndex < _scenarioData.scenario.tasks.Count)
        {
            var taskDef = _scenarioData.scenario.tasks[_current.taskIndex];
            var config = _scenarioData.GetModuleConfig(taskDef.moduleId);
            _titleText.text = config != null ? config.GetExtra("description") : taskDef.moduleId;
        }
    }

    private void UpdateCheckboxes(int currentIndex, TaskStatus status)
    {
        for(int i = 0; i < _items.Count; i++)
        {
            if(i < currentIndex)
                _items[i].SetStatus(TaskStatus.Completed);
            else if(i == currentIndex)
                _items[i].SetStatus(status);
            else
                _items[i].SetStatus(TaskStatus.Pending);
        }
    }

    private void RefreshTimer()
    {
        if(_timerText == null) return;

        if(_current.timeoutSeconds > 0f)
        {
            float remaining = Mathf.Max(0f, _current.timeoutSeconds - _current.elapsedTime);
            _timerText.text = $"{remaining:F0}s";
            if(_progressBar != null)
                _progressBar.fillAmount = 1f - (remaining / _current.timeoutSeconds);
        }
        else
        {
            _timerText.text = "";
            if(_progressBar != null) _progressBar.fillAmount = 0f;
        }
    }

    private void OnDialogueStartedGlobal(string _)
    {
        SetDialogueDimVisible(true);
    }

    private void OnDialogueEndedGlobal(string _)
    {
        SetDialogueDimVisible(false);
    }

    private void ResolveDialogueDimCanvasGroup()
    {
        if(_dialogueDimCanvasGroup != null)
            return;

        Transform root = _panelRoot != null ? _panelRoot.transform : transform;
        _dialogueDimCanvasGroup = root.GetComponent<CanvasGroup>();
        if(_dialogueDimCanvasGroup == null)
            _dialogueDimCanvasGroup = root.gameObject.AddComponent<CanvasGroup>();
    }

    private void SetDialogueDimVisible(bool visible)
    {
        ResolveDialogueDimCanvasGroup();
        if(_dialogueDimCanvasGroup == null)
            return;

        if(_dialogueBlurRoutine != null)
            StopCoroutine(_dialogueBlurRoutine);
        float target = visible ? Mathf.Clamp01(_dialogueDimAlpha) : 1f;
        _dialogueBlurRoutine = StartCoroutine(CoFadeDialogueDim(target));
    }

    private void SetDialogueDimVisibleImmediate(bool visible)
    {
        ResolveDialogueDimCanvasGroup();
        if(_dialogueDimCanvasGroup == null)
            return;

        _dialogueDimCanvasGroup.alpha = visible ? Mathf.Clamp01(_dialogueDimAlpha) : 1f;
    }

    private IEnumerator CoFadeDialogueDim(float target)
    {
        float duration = Mathf.Max(0.01f, _dialogueDimFadeSeconds);
        float start = _dialogueDimCanvasGroup.alpha;
        float elapsed = 0f;
        while(elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(elapsed / duration);
            _dialogueDimCanvasGroup.alpha = Mathf.Lerp(start, target, p);
            yield return null;
        }

        _dialogueDimCanvasGroup.alpha = target;
        _dialogueBlurRoutine = null;
    }
}