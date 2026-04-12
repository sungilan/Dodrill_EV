using DG.Tweening;
using DoDrill;
using EPOOutline;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// ============================================================
//  GuideSystem.cs  — 통합 가이드 매니저 (텍스트 위계 및 자동 소멸 추가)
// ============================================================
public class GuideSystem : MonoBehaviour
{
    public static GuideSystem Instance { get; private set; }
    public enum GuideLevel { Easy, Normal, Hard }

    [Header("난이도")]
    public GuideLevel currentLevel = GuideLevel.Easy;

    [Header("UI - 상단 (What)")]
    // guidePanel 직접 제어 필드 제거 (TaskProgressUI 등에서 처리됨)
    public TextMeshProUGUI titleText;        // 로컬라이징된 제목
    public TextMeshProUGUI taskIndexText;

    [Header("UI - 중앙 가이드 (Why)")]
    public CanvasGroup centerGuideGroup;     // 자동 페이드를 위한 그룹
    public TextMeshProUGUI mainGuideText;    // Why? (이유와 행동 설명)
    public TextMeshProUGUI subGuideText;     // Data? (측정값 등 부가 정보)

    [Header("설정")]
    public float autoHideDelay = 8f;         // 가이드 패널 자동 소멸 시간
    public GameObject hintButton;

    [Header("가이드 마커 프리팹")]
    public GameObject guidePrefab;
    public Color spawnGuideColor = new Color(0.2f, 0.9f, 1f);
    public Color targetGuideColor = new Color(1f, 0.85f, 0.1f);

    [Header("고스트 & 아웃라인")]
    public Material ghostMaterial;
    public Color outlineColor = new Color(0f, 1f, 1f);
    [Range(0, 10)] public float outlineWidth = 4f;
    public RenderStyle renderStyle = RenderStyle.Single;

    [Header("힌트 / 넛지")]
    public float hintDuration = 4f;
    public float nudgeIdleTime = 12f;

    // ── 내부 상태 ─────────────────────────────────────────
    private ScenarioData _scenarioData;
    private TaskState _currentTask;
    private int _totalTasks;
    private List<GameObject> _requiredItemObjects = new();
    private List<GameObject> _tempGuides = new();
    private GameObject _ghostObject;
    private GuideMarker _targetMarker;
    private List<GameObject> _clickTargets = new();
    private float _idleTimer;
    private Coroutine _hintCoroutine;
    private bool _completed = false;
    private Dictionary<string, GameObject> _spawnMarkers = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated += OnTaskUpdated;
        ScenarioStateReceiver.OnSnapshotReceived += OnSnapshotReceived;
        ScenarioReceiver.OnScenarioReceived += OnScenarioReceived;
    }

    private void OnDisable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated -= OnTaskUpdated;
        ScenarioStateReceiver.OnSnapshotReceived -= OnSnapshotReceived;
        ScenarioReceiver.OnScenarioReceived -= OnScenarioReceived;
    }

    private void Start()
    {
        hintButton?.SetActive(false);
        // 초기 상태에서 중앙 가이드는 숨김
        if (centerGuideGroup != null) centerGuideGroup.alpha = 0;
    }

    private void Update()
    {
        if (currentLevel == GuideLevel.Hard) return;
        if (_ghostObject == null && _requiredItemObjects.Count == 0 && _clickTargets.Count == 0) return;

        _idleTimer += Time.deltaTime;
        if (_idleTimer >= nudgeIdleTime) { _idleTimer = 0f; TriggerNudge(); }
    }

    private void OnTaskUpdated(TaskStateBroadcast broadcast)
    {
        _currentTask = broadcast.currentTask;
        _totalTasks = broadcast.totalTaskCount;

        if (_currentTask.status == TaskStatus.Running)
        {
            _completed = false;
            ShowGuideForCurrentTask();
        }
        else if (_currentTask.status == TaskStatus.Completed)
        {
            OnTaskCompleted();
        }
    }

    private void OnSnapshotReceived(ScenarioSnapshotBroadcast broadcast)
    {
        _scenarioData = broadcast.scenarioData;
        _currentTask = broadcast.currentTask;
        _totalTasks = broadcast.totalTaskCount;
        if (_currentTask.status == TaskStatus.Running)
        {
            _completed = false;
            ShowGuideForCurrentTask();
        }
    }

    private void OnScenarioReceived(ScenarioData data)
    {
        _scenarioData = data;
        // 시나리오 데이터가 처음 도착했을 때 현재 작업이 진행 중이면 즉시 표시 (첫 단계 해결)
        if (_currentTask.status == TaskStatus.Running && !_completed)
        {
            ShowGuideForCurrentTask();
        }
    }

    // ═══════════════════════════════════════════════════════
    // 가이드 표시 및 UI 제어
    // ═══════════════════════════════════════════════════════

    private void ShowGuideForCurrentTask()
    {
        if (_scenarioData == null) return;
        int idx = _currentTask.taskIndex;
        var tasks = _scenarioData.scenario?.tasks;
        if (tasks == null || idx >= tasks.Count) return;

        var taskDef = tasks[idx];
        var config = _scenarioData.GetModuleConfig(taskDef.moduleId);

        // 1. UI 텍스트 갱신 (Why/Data 적용)
        RefreshUI(idx, taskDef.moduleId, config);

        // 2. 중앙 가이드 그룹 연출 (알파 0 문제 수정)
        if (centerGuideGroup != null)
        {
            DOTween.Kill("CenterGuideFade");
            // 단계 시작 시 확실하게 보이도록 설정
            centerGuideGroup.alpha = 1f;
            centerGuideGroup.gameObject.SetActive(true);

            // [수정] 일정 시간 후 자동으로 페이드 아웃 로직 실행
            DOVirtual.DelayedCall(autoHideDelay, () => {
                if (!_completed) centerGuideGroup.DOFade(0f, 1.5f).SetId("CenterGuideFade");
            }).SetId("CenterGuideFade");
        }

        // 3. 물리 가이드 초기화 및 생성
        ClearVisualGuides();
        _requiredItemObjects = FindRequiredItems(config);
        _idleTimer = 0f;

        if (currentLevel != GuideLevel.Hard)
        {
            _clickTargets = FindClickTargets(config);
            foreach (var t in _clickTargets)
            {
                SetOutline(t, true);
                if (taskDef.spawnObjects == null || taskDef.spawnObjects.Count == 0)
                {
                    string label = GetExtraValue(config, "description", "이곳을 클릭하세요.");
                    SpawnMarker(t.transform.position, label, targetGuideColor, isTarget: true);
                }
            }

            foreach (var item in _requiredItemObjects) SetOutline(item, true);
            BuildSpawnGuides(taskDef);
            SpeakGuide(config);
        }

        hintButton?.SetActive(currentLevel == GuideLevel.Normal);
    }

    // 버튼 등을 통해 가이드를 다시 보고 싶을 때 호출
    public void ForceShowGuide()
    {
        if (centerGuideGroup == null) return;
        DOTween.Kill("CenterGuideFade");
        centerGuideGroup.DOFade(1f, 0.5f);
    }

    private void RefreshUI(int taskIdx, string moduleId, ModuleConfig config)
    {
        if (taskIndexText) taskIndexText.text = $"{taskIdx + 1} / {_totalTasks}";

        // Title: moduleId 대신 extra.title 사용
        if (titleText) titleText.text = GetExtraValue(config, "title", moduleId);

        // MainGuide: Why? (중앙 큰 텍스트)
        if (mainGuideText) mainGuideText.text = GetExtraValue(config, "guideText", "");

        // SubGuide: Data/Spec (중앙 하단 작은 텍스트)
        if (subGuideText)
        {
            string sub = GetExtraValue(config, "subguideText", "");
            subGuideText.text = sub;
            subGuideText.gameObject.SetActive(!string.IsNullOrEmpty(sub));
        }
    }

    // ═══════════════════════════════════════════════════════
    // 마커 + 고스트 생성 및 기존 로직 (생략 없이 유지)
    // ═══════════════════════════════════════════════════════

    private void BuildSpawnGuides(TaskDef taskDef)
    {
        if (taskDef.spawnObjects == null) return;
        var allSP = UnityEngine.Object.FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var spCache = new Dictionary<string, SpawnPoint>();
        foreach (var sp in allSP) if (!string.IsNullOrEmpty(sp.pointId)) spCache[sp.pointId] = sp;

        foreach (var bundle in taskDef.spawnObjects)
        {
            if (string.IsNullOrEmpty(bundle.prefabId) || !bundle.showGuide) continue;
            var existingItem = FindSpawnedItem(bundle.prefabId);
            bool isGrabbed = (existingItem?.GetComponent<SyncGrab>()?.IsGrabbed ?? false);

            if (!isGrabbed && !string.IsNullOrEmpty(bundle.spawnPointId))
            {
                if (spCache.TryGetValue(bundle.spawnPointId, out var spPt))
                {
                    Vector3 pos = (existingItem != null) ? GetBoundsCenter(existingItem) : spPt.transform.position;
                    if (existingItem?.GetComponent<NoneHighlight>() == null)
                    {
                        string label = GetDisplayName(bundle.prefabId);
                        var markerGO = SpawnMarker(pos, label, spawnGuideColor, isTarget: false);
                        if (markerGO != null) _spawnMarkers[bundle.prefabId] = markerGO.gameObject;
                    }
                }
            }

            if (!string.IsNullOrEmpty(bundle.guideId))
            {
                var targetGO = FindSpawnedItem(bundle.guideId);
                if (targetGO != null)
                {
                    var boltGroup = targetGO.GetComponent<BoltGroupCounter>();
                    bool isBoltTask = boltGroup != null || targetGO.name.Contains("Bolt");
                    string label = isBoltTask && boltGroup != null
                        ? $"볼트를 임팩트 렌치로 {(boltGroup.assembleMode ? "체결" : "제거")}하세요."
                        : $"{GetDisplayName(bundle.prefabId)}을(를) 여기로 이동하세요";

                    Vector3 markerPos = isBoltTask ? GetBoundsCenter(targetGO) : targetGO.transform.position;
                    var marker = SpawnMarker(markerPos, label, targetGuideColor, isTarget: true);
                    if (marker != null)
                    {
                        marker.Setup(label, targetGuideColor, !isBoltTask && targetGO.GetComponent<NoneHighlight>() == null);
                        _targetMarker = marker;
                        if (isBoltTask && boltGroup != null) StartCoroutine(UpdateBoltProgressRoutine(marker, boltGroup));
                    }
                    SetOutline(targetGO, true);
                    if (existingItem != null && ghostMaterial != null && !isBoltTask) _ghostObject = SpawnGhost(existingItem, targetGO.transform.position);
                }
            }
        }
    }

    private IEnumerator UpdateBoltProgressRoutine(GuideMarker marker, BoltGroupCounter boltGroup)
    {
        int lastProgress = -1;
        while (marker != null && boltGroup != null && !_completed)
        {
            if (boltGroup.RemovedCount != lastProgress)
            {
                marker.UpdateProgressText($"{boltGroup.TotalCount - boltGroup.RemovedCount}/{boltGroup.TotalCount}");
                lastProgress = boltGroup.RemovedCount;
            }
            yield return new WaitForSeconds(0.1f);
        }
    }

    private GuideMarker SpawnMarker(Vector3 worldPos, string description, Color color, bool isTarget)
    {
        if (guidePrefab == null) return null;
        var go = Instantiate(guidePrefab, worldPos, Quaternion.identity);
        var marker = go.GetComponent<GuideMarker>();
        if (marker != null) { marker.Setup(description, color); _tempGuides.Add(go); return marker; }
        Destroy(go); return null;
    }

    public void OnItemGrabbed(string prefabId)
    {
        if (_spawnMarkers.TryGetValue(prefabId, out var markerGO))
        {
            if (markerGO != null) { _tempGuides.Remove(markerGO); Destroy(markerGO); }
            _spawnMarkers.Remove(prefabId);
        }
    }

    private GameObject SpawnGhost(GameObject sourceItem, Vector3 ghostPos)
    {
        var ghost = Instantiate(sourceItem, ghostPos, sourceItem.transform.rotation);
        foreach (var col in ghost.GetComponentsInChildren<Collider>()) Destroy(col);
        foreach (var rb in ghost.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
        foreach (var mono in ghost.GetComponentsInChildren<MonoBehaviour>()) Destroy(mono);
        foreach (var r in ghost.GetComponentsInChildren<Renderer>())
        {
            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = ghostMaterial;
            r.materials = mats;
        }
        _tempGuides.Add(ghost);
        ghost.transform.DOLocalMoveY(0.05f, 1.5f).SetLoops(-1, LoopType.Yoyo).SetRelative();
        return ghost;
    }

    private void TriggerNudge() { _targetMarker?.Nudge(); if (_ghostObject != null) _ghostObject.transform.DOShakePosition(1.5f, 0.06f, 12, 90f); }

    public void ShowHint() { if (_hintCoroutine != null) StopCoroutine(_hintCoroutine); _hintCoroutine = StartCoroutine(ShowHintRoutine()); }

    private IEnumerator ShowHintRoutine()
    {
        foreach (var item in _requiredItemObjects) SetOutline(item, true);
        TTSManager.Instance?.Speak("목표 위치와 도구를 확인하세요.");
        yield return new WaitForSeconds(hintDuration);
        foreach (var item in _requiredItemObjects) SetOutline(item, false);
    }

    private void OnTaskCompleted()
    {
        _completed = true;
        ClearVisualGuides();
        if (centerGuideGroup != null) centerGuideGroup.DOFade(0f, 0.5f);
        TTSManager.Instance?.Stop();
        if (currentLevel != GuideLevel.Hard) TTSManager.Instance?.Speak("정비를 완료했습니다.");
    }

    private void SetOutline(GameObject target, bool on)
    {
        if (target == null) return;
        var outlinable = target.GetComponent<Outlinable>() ?? target.AddComponent<Outlinable>();
        if (on)
        {
            outlinable.AddAllChildRenderersToRenderingList();
            outlinable.RenderStyle = renderStyle; outlinable.enabled = true;
            outlinable.OutlineParameters.Color = outlineColor; outlinable.OutlineParameters.BlurShift = outlineWidth;
        }
        else outlinable.enabled = false;
    }

    private Vector3 GetBoundsCenter(GameObject go) { var renderers = go.GetComponentsInChildren<Renderer>(); if (renderers == null || renderers.Length == 0) return go.transform.position; var bounds = renderers[0].bounds; for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds); return bounds.center; }
    private List<GameObject> FindClickTargets(ModuleConfig config) { var result = new List<GameObject>(); if (config == null || string.IsNullOrEmpty(config.targetObjName)) return result; var go = GameObject.Find(config.targetObjName); if (go == null) { foreach (var ca in FindObjectsByType<ClickableAnimator>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (ca.uniqueId == config.targetObjName) go = ca.gameObject; } if (go != null) result.Add(go); return result; }
    private List<GameObject> FindRequiredItems(ModuleConfig config) { var result = new List<GameObject>(); if (config?.requiredItems == null) return result; foreach (var id in config.requiredItems) { var go = FindSpawnedItem(id); if (go != null) result.Add(go); } return result; }
    private GameObject FindSpawnedItem(string prefabId) { var go = GameObject.Find(prefabId) ?? GameObject.Find(prefabId + "(Clone)"); if (go != null) return go; foreach (var obj in FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (obj.name.Contains(prefabId)) return obj; return null; }
    private string GetExtraValue(ModuleConfig config, string key, string fallback) { if (config?.extra == null) return fallback; foreach (var kv in config.extra) if (kv.key == key) return kv.GetLocalized(); return fallback; }
    private void SpeakGuide(ModuleConfig config) { string text = GetExtraValue(config, "description", ""); if (!string.IsNullOrEmpty(text)) TTSManager.Instance?.Speak(text); }
    private void ClearVisualGuides() { foreach (var g in _tempGuides) if (g) Destroy(g); _tempGuides.Clear(); _spawnMarkers.Clear(); foreach (var item in _requiredItemObjects) SetOutline(item, false); _requiredItemObjects.Clear(); foreach (var t in _clickTargets) SetOutline(t, false); _clickTargets.Clear(); _ghostObject = null; _targetMarker = null; }
    private string GetDisplayName(string id) => id switch { "GDS_Tablet" => "GDS 진단기", "InsulatedGloves" => "절연 장갑", "InsulatedShoes" => "절연화", "FaceShield" => "안면보호구", "SmartKey" => "스마트키", "LockBox" => "잠금박스", "ImpactWrench" => "임팩트 렌치", "BatteryJack" => "배터리 잭", "Multimeter" => "멀티미터", "InsulationTester" => "절연 저항 측정기", _ => id };
}