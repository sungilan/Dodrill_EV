using System;
using System.Collections;
using UnityEngine;
using Debug = UnityEngine.Debug;

// ============================================================
//  StandaloneScenarioRunner.cs
//  FishNet 네트워크 없이 로컬 단독 실행용 시나리오 러너
//  데모/테스트 전용 — 실제 멀티플레이 빌드에는 ScenarioRunner 사용
// ============================================================

public class StandaloneScenarioRunner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ScenarioBootstrapper _bootstrapper;

    [Header("Scenario")]
    [Tooltip("Resources/Scenarios/ 에 있는 파일명 (확장자 제외)")]
    public string scenarioId = "0_test_all_features";

    [Header("Retry Settings")]
    [SerializeField] private int   _maxRetries    = 0;
    [SerializeField] private float _failRetryDelay = 1.5f;

    // ── 상태 ─────────────────────────────────────────
    private ScenarioData _data;
    private ITaskModule  _currentModule;
    private int          _currentIndex;
    private int          _retryCount;
    private bool         _running;
    private float        _elapsed;

    // ── 이벤트 (DemoTaskUI 등에서 구독) ─────────────
    /// <summary>태스크 전환 시 발생 (taskIndex, moduleId, totalCount)</summary>
    public static event Action<int, string, int> OnTaskChanged;

    /// <summary>태스크 상태 변경 시 발생 (status 문자열)</summary>
    public static event Action<string> OnStatusChanged;

    // ── 외부 읽기용 ──────────────────────────────────
    public int    CurrentIndex    => _currentIndex;
    public int    TotalCount      => _data?.scenario.tasks.Count ?? 0;
    public string CurrentModuleId => GetModuleId(_currentIndex);
    public bool   IsRunning       => _running;

    // ── 생명주기 ─────────────────────────────────────

    private void Start()
    {
        if (_bootstrapper == null)
            _bootstrapper = GetComponent<ScenarioBootstrapper>();

        if (_bootstrapper == null)
        {
            Debug.LogError("[StandaloneScenarioRunner] ScenarioBootstrapper 없음");
            return;
        }

        _data = ScenarioLoader.Load(scenarioId);
        if (_data == null || !ScenarioLoader.Validate(_data)) return;

        Debug.Log($"[StandaloneRunner] 시나리오 시작: {_data.scenarioName} ({_data.scenario.tasks.Count}개 태스크)");
        StartTask(0);
    }

    private void Update()
    {
        if (!_running || _currentModule == null) return;

        _elapsed += Time.deltaTime;
        _currentModule.OnUpdate(Time.deltaTime);

        // 타임아웃 체크
        var taskDef = _data.scenario.tasks[_currentIndex];
        if (taskDef.timeout > 0f && _elapsed >= taskDef.timeout)
        {
            Debug.LogWarning($"[StandaloneRunner] Task[{_currentIndex}] 타임아웃");
            FailCurrentTask();
        }
    }

    // ── Task 전이 ─────────────────────────────────────

    private void StartTask(int index)
    {
        if (_data == null || index >= _data.scenario.tasks.Count)
        {
            Debug.Log("[StandaloneRunner] 모든 Task 완료!");
            _running = false;
            OnStatusChanged?.Invoke("완료");
            return;
        }

        var taskDef = _data.scenario.tasks[index];
        var config  = _data.GetModuleConfig(taskDef.moduleId);
        var module  = _bootstrapper.Registry.Get(taskDef.moduleId);

        if (module == null)
        {
            Debug.LogWarning($"[StandaloneRunner] 모듈 없음: {taskDef.moduleId} — 건너뜀");
            StartTask(index + 1);
            return;
        }

        _currentIndex  = index;
        _currentModule = module;
        _elapsed       = 0f;
        _running       = true;

        module.OnStart(config, CompleteCurrentTask, FailCurrentTask);

        string info = _retryCount > 0 ? $" (재도전 #{_retryCount})" : "";
        Debug.Log($"[StandaloneRunner] Task[{index}/{TotalCount - 1}] 시작: {taskDef.moduleId}{info}");

        OnTaskChanged?.Invoke(index, taskDef.moduleId, TotalCount);
        OnStatusChanged?.Invoke("진행중");
    }

    private void CompleteCurrentTask()
    {
        if (!_running) return;

        _currentModule?.OnComplete();
        _retryCount = 0;
        _running    = false;

        Debug.Log($"[StandaloneRunner] Task[{_currentIndex}] 완료");
        OnStatusChanged?.Invoke("완료");

        StartTask(_currentIndex + 1);
    }

    private void FailCurrentTask()
    {
        if (!_running) return;

        _currentModule?.OnFail();
        _retryCount++;
        _running = false;

        Debug.LogWarning($"[StandaloneRunner] Task[{_currentIndex}] 실패 → 재도전 #{_retryCount}");
        OnStatusChanged?.Invoke("실패");

        if (_maxRetries > 0 && _retryCount > _maxRetries)
        {
            Debug.LogError($"[StandaloneRunner] 최대 재도전 초과 → 중단");
            return;
        }

        int retryIndex = _currentIndex;
        StartCoroutine(RetryAfterDelay(retryIndex, _failRetryDelay));
    }

    private IEnumerator RetryAfterDelay(int index, float delay)
    {
        yield return new WaitForSeconds(delay);
        StartTask(index);
    }

    // ── 디버그 공개 API ───────────────────────────────

    public void ForceComplete() => CompleteCurrentTask();
    public void ForceFail()     => FailCurrentTask();

    public void JumpToTask(int index)
    {
        _currentModule?.OnFail();
        _retryCount = 0;
        StartTask(index);
    }

    // ── 내부 유틸 ─────────────────────────────────────

    private string GetModuleId(int index)
    {
        if (_data == null || index < 0 || index >= _data.scenario.tasks.Count)
            return string.Empty;
        return _data.scenario.tasks[index].moduleId;
    }
}
