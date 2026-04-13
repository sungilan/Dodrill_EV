using DG.Tweening;
using DoDrill;
using EPOOutline;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using XRAirpotrSecurity;

// ============================================================
//  GuideSystem.cs  — 통합 가이드 매니저 (플랫폼별 UI 관리 및 롤백 버전)
// ============================================================
public class GuideSystem : MonoBehaviour
{
    public static GuideSystem Instance { get; private set; }
    public enum GuideLevel { Easy, Normal, Hard }

    [Serializable]
    public struct PlatformUIGroup
    {
        public CanvasGroup group;
        public TextMeshProUGUI titleText;
        public TextMeshProUGUI mainText;
        public TextMeshProUGUI subText;
    }

    [Header("난이도")]
    public GuideLevel currentLevel = GuideLevel.Easy;

    [Header("UI - 플랫폼별 가이드 그룹 설정")]
    public PlatformUIGroup pcUI;
    public PlatformUIGroup mobileUI;
    public PlatformUIGroup vrUI;

    // ── 현재 활성화된 UI 참조 (내부 할당) ──
    private CanvasGroup centerGuideGroup;
    private TextMeshProUGUI activeTitleText;
    private TextMeshProUGUI mainGuideText;
    private TextMeshProUGUI subGuideText;

    [Header("설정")]
    public float autoHideDelay = 8f;
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

    private Sequence _currentGuideSequence;
    private int _lastActiveTaskIndex = -1;

    private void Awake()
    {
        if(Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // 플랫폼에 따른 UI 할당
        SetupPlatformUI();
    }

    private void Start()
    {
        hintButton?.SetActive(false);
        SetCenterAlpha(0f, "Start Initializing");
    }

    private void SetupPlatformUI()
    {
        // 1. 플랫폼 판별
        PlatformType p = GameScenePlatformManager.CurrentPlatform;
        PlatformUIGroup selected;

        if(p == PlatformType.VR) selected = vrUI;
        else if(p == PlatformType.Mobile) selected = mobileUI;
        else selected = pcUI;

        // 2. 내부 참조 연결
        centerGuideGroup = selected.group;
        activeTitleText = selected.titleText;
        mainGuideText = selected.mainText;
        subGuideText = selected.subText;

        // 3. 사용하지 않는 플랫폼 UI 비활성화
        if(pcUI.group != null) pcUI.group.gameObject.SetActive(p == PlatformType.PC);
        if(mobileUI.group != null) mobileUI.group.gameObject.SetActive(p == PlatformType.Mobile);
        if(vrUI.group != null) vrUI.group.gameObject.SetActive(p == PlatformType.VR);
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

    private void Update()
    {
        if(currentLevel == GuideLevel.Hard) return;
        if(_ghostObject == null && _requiredItemObjects.Count == 0 && _clickTargets.Count == 0) return;
        _idleTimer += Time.deltaTime;
        if(_idleTimer >= nudgeIdleTime) { _idleTimer = 0f; TriggerNudge(); }
    }

    private void OnTaskUpdated(TaskStateBroadcast broadcast)
    {
        _currentTask = broadcast.currentTask;
        _totalTasks = broadcast.totalTaskCount;

        if(_currentTask.status == TaskStatus.Running)
        {
            _completed = false;
            ShowGuideForCurrentTask();
        }
        else if(_currentTask.status == TaskStatus.Completed)
        {
            OnTaskCompleted();
        }
    }

    private void OnSnapshotReceived(ScenarioSnapshotBroadcast broadcast)
    {
        _scenarioData = broadcast.scenarioData;
        _currentTask = broadcast.currentTask;
        _totalTasks = broadcast.totalTaskCount;
        if(_currentTask.status == TaskStatus.Running)
        {
            _completed = false;
            ShowGuideForCurrentTask();
        }
    }

    private void OnScenarioReceived(ScenarioData data)
    {
        _scenarioData = data;
        if(_currentTask.status == TaskStatus.Running && !_completed)
        {
            ShowGuideForCurrentTask();
        }
    }

    private void ShowGuideForCurrentTask()
    {
        if(_scenarioData == null) return;
        int idx = _currentTask.taskIndex;

        // 중복 수신 방지
        //if(_lastActiveTaskIndex == idx && !_completed) return;
        //_lastActiveTaskIndex = idx;

        // [방어 1] 동일한 단계의 패킷이 중복으로 올 경우 중복 실행 차단 (범인 검거)

        if(_lastActiveTaskIndex == idx && !_completed)
        {
            Debug.Log($"<color=white>[GuideAlpha]</color> Task[{idx}] 가이드 중복 수신 무시");
            return;
        }
        _lastActiveTaskIndex = idx;

        var tasks = _scenarioData.scenario?.tasks;
        if(tasks == null || idx >= tasks.Count) return;

        var taskDef = tasks[idx];
        var config = _scenarioData.GetModuleConfig(taskDef.moduleId);

        // 1. UI 텍스트 갱신
        RefreshUI(idx, taskDef.moduleId, config);

        // 2. 중앙 가이드 시퀀스 (페이드 연출)
        if(centerGuideGroup != null)
        {
            if(_currentGuideSequence != null) { _currentGuideSequence.Kill(true); _currentGuideSequence = null; }

            SetCenterAlpha(0f, $"ShowGuide - Task[{idx}] Started");
            centerGuideGroup.gameObject.SetActive(true);

            _currentGuideSequence = DOTween.Sequence().SetId("CenterGuideFadeLogic");
            _currentGuideSequence.Append(centerGuideGroup.DOFade(1f, 1.0f));
            _currentGuideSequence.AppendInterval(autoHideDelay);
            _currentGuideSequence.Append(centerGuideGroup.DOFade(0f, 1.5f));
            _currentGuideSequence.OnComplete(() => {
                _currentGuideSequence = null;
                //if(!_completed) centerGuideGroup.gameObject.SetActive(false);
            });
        }

        // 3. 물리 가이드 생성
        ClearVisualGuides();
        _requiredItemObjects = FindRequiredItems(config);
        _idleTimer = 0f;

        if(currentLevel != GuideLevel.Hard)
        {
            _clickTargets = FindClickTargets(config);
            foreach(var t in _clickTargets)
            {
                SetOutline(t, true);
                if(taskDef.spawnObjects == null || taskDef.spawnObjects.Count == 0)
                {
                    string label = GetExtraValue(config, "description", "이곳을 클릭하세요.");
                    SpawnMarker(t.transform.position, label, targetGuideColor, isTarget: true);
                }
            }

            foreach(var item in _requiredItemObjects) SetOutline(item, true);
            BuildSpawnGuides(taskDef);
            SpeakGuide(config);
        }

        hintButton?.SetActive(currentLevel == GuideLevel.Normal);
    }

    public void ForceShowGuide()
    {
        if(centerGuideGroup == null) return;
        if(_currentGuideSequence != null) { _currentGuideSequence.Kill(); _currentGuideSequence = null; }

        Debug.Log("<color=cyan>[GuideSystem]</color> ForceShowGuide - 가이드 다시 표시");
        centerGuideGroup.gameObject.SetActive(true);
        centerGuideGroup.DOFade(1f, 0.5f);
    }

    private void OnTaskCompleted()
    {
        _completed = true;
        _lastActiveTaskIndex = -1;

        if(_currentGuideSequence != null) { _currentGuideSequence.Kill(); _currentGuideSequence = null; }

        ClearVisualGuides();

        if(centerGuideGroup != null)
        {
            Debug.Log("<color=red>[GuideAlpha]</color> OnTaskCompleted - 가이드 숨김");
            centerGuideGroup.DOFade(0f, 0.5f);
        }

        TTSManager.Instance?.Stop();
    }

    private void SetCenterAlpha(float targetAlpha, string reason)
    {
        if(centerGuideGroup == null) return;
        centerGuideGroup.alpha = targetAlpha;
        Debug.Log($"<color=yellow>[GuideAlpha]</color> <b>Alpha Set to {targetAlpha}</b> | 사유: {reason}");
    }

    private void RefreshUI(int taskIdx, string moduleId, ModuleConfig config)
    {
        if(activeTitleText) activeTitleText.text = GetExtraValue(config, "title", moduleId);
        if(mainGuideText) mainGuideText.text = GetExtraValue(config, "guideText", "");

        if(subGuideText)
        {
            string sub = GetExtraValue(config, "subguideText", "");
            subGuideText.text = sub;
            subGuideText.gameObject.SetActive(!string.IsNullOrEmpty(sub));
        }
    }

    // ── 물리 가이드 로직 (유지) ──
    private void BuildSpawnGuides(TaskDef taskDef) { if(taskDef.spawnObjects == null) return; var allSP = FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None); var spCache = new Dictionary<string, SpawnPoint>(); foreach(var sp in allSP) if(!string.IsNullOrEmpty(sp.pointId)) spCache[sp.pointId] = sp; foreach(var bundle in taskDef.spawnObjects) { if(string.IsNullOrEmpty(bundle.prefabId) || !bundle.showGuide) continue; var existingItem = FindSpawnedItem(bundle.prefabId); bool isGrabbed = (existingItem?.GetComponent<SyncGrab>()?.IsGrabbed ?? false); if(!isGrabbed && !string.IsNullOrEmpty(bundle.spawnPointId)) { if(spCache.TryGetValue(bundle.spawnPointId, out var spPt)) { Vector3 pos = (existingItem != null) ? GetBoundsCenter(existingItem) : spPt.transform.position; if(existingItem?.GetComponent<NoneHighlight>() == null) { string label = GetDisplayName(bundle.prefabId); var markerGO = SpawnMarker(pos, label, spawnGuideColor, isTarget: false); if(markerGO != null) _spawnMarkers[bundle.prefabId] = markerGO.gameObject; } } } if(!string.IsNullOrEmpty(bundle.guideId)) { var targetGO = FindSpawnedItem(bundle.guideId); if(targetGO != null) { var boltGroup = targetGO.GetComponent<BoltGroupCounter>(); bool isBoltTask = boltGroup != null || targetGO.name.Contains("Bolt"); string label = isBoltTask && boltGroup != null ? $"볼트를 임팩트 렌치로 {(boltGroup.assembleMode ? "체결" : "제거")}하세요." : $"{GetDisplayName(bundle.prefabId)}을(를) 여기로 이동하세요"; Vector3 markerPos = isBoltTask ? GetBoundsCenter(targetGO) : targetGO.transform.position; var marker = SpawnMarker(markerPos, label, targetGuideColor, isTarget: true); if(marker != null) { marker.Setup(label, targetGuideColor, !isBoltTask && targetGO.GetComponent<NoneHighlight>() == null); _targetMarker = marker; if(isBoltTask && boltGroup != null) StartCoroutine(UpdateBoltProgressRoutine(marker, boltGroup)); } SetOutline(targetGO, true); if(existingItem != null && ghostMaterial != null && !isBoltTask) _ghostObject = SpawnGhost(existingItem, targetGO.transform.position); } } } }
    private IEnumerator UpdateBoltProgressRoutine(GuideMarker marker, BoltGroupCounter boltGroup) { int lastProgress = -1; while(marker != null && boltGroup != null && !_completed) { if(boltGroup.RemovedCount != lastProgress) { marker.UpdateProgressText($"{boltGroup.TotalCount - boltGroup.RemovedCount}/{boltGroup.TotalCount}"); lastProgress = boltGroup.RemovedCount; } yield return new WaitForSeconds(0.1f); } }
    private GuideMarker SpawnMarker(Vector3 worldPos, string description, Color color, bool isTarget) { if(guidePrefab == null) return null; var go = Instantiate(guidePrefab, worldPos, Quaternion.identity); var marker = go.GetComponent<GuideMarker>(); if(marker != null) { marker.Setup(description, color); _tempGuides.Add(go); return marker; } Destroy(go); return null; }
    public void OnItemGrabbed(string prefabId) { if(_spawnMarkers.TryGetValue(prefabId, out var markerGO)) { if(markerGO != null) { _tempGuides.Remove(markerGO); Destroy(markerGO); } _spawnMarkers.Remove(prefabId); } }
    private GameObject SpawnGhost(GameObject sourceItem, Vector3 ghostPos) { var ghost = Instantiate(sourceItem, ghostPos, sourceItem.transform.rotation); foreach(var col in ghost.GetComponentsInChildren<Collider>()) Destroy(col); foreach(var rb in ghost.GetComponentsInChildren<Rigidbody>()) Destroy(rb); foreach(var mono in ghost.GetComponentsInChildren<MonoBehaviour>()) Destroy(mono); foreach(var r in ghost.GetComponentsInChildren<Renderer>()) { var mats = new Material[r.sharedMaterials.Length]; for(int i = 0; i < mats.Length; i++) mats[i] = ghostMaterial; r.materials = mats; } _tempGuides.Add(ghost); ghost.transform.DOLocalMoveY(0.05f, 1.5f).SetLoops(-1, LoopType.Yoyo).SetRelative(); return ghost; }
    private void TriggerNudge() { _targetMarker?.Nudge(); if(_ghostObject != null) _ghostObject.transform.DOShakePosition(1.5f, 0.06f, 12, 90f); }
    public void ShowHint() { if(_hintCoroutine != null) StopCoroutine(_hintCoroutine); _hintCoroutine = StartCoroutine(ShowHintRoutine()); }
    private IEnumerator ShowHintRoutine() { foreach(var item in _requiredItemObjects) SetOutline(item, true); TTSManager.Instance?.Speak("목표 위치와 도구를 확인하세요."); yield return new WaitForSeconds(hintDuration); foreach(var item in _requiredItemObjects) SetOutline(item, false); }
    private void SetOutline(GameObject target, bool on) { if(target == null) return; var outlinable = target.GetComponent<Outlinable>() ?? target.AddComponent<Outlinable>(); if(on) { outlinable.AddAllChildRenderersToRenderingList(); outlinable.RenderStyle = renderStyle; outlinable.enabled = true; outlinable.OutlineParameters.Color = outlineColor; outlinable.OutlineParameters.BlurShift = outlineWidth; } else outlinable.enabled = false; }
    private Vector3 GetBoundsCenter(GameObject go) { var renderers = go.GetComponentsInChildren<Renderer>(); if(renderers == null || renderers.Length == 0) return go.transform.position; var bounds = renderers[0].bounds; for(int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds); return bounds.center; }
    private List<GameObject> FindClickTargets(ModuleConfig config) { var result = new List<GameObject>(); if(config == null || string.IsNullOrEmpty(config.targetObjName)) return result; var go = GameObject.Find(config.targetObjName); if(go == null) { foreach(var ca in FindObjectsByType<ClickableAnimator>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if(ca.uniqueId == config.targetObjName) go = ca.gameObject; } if(go != null) result.Add(go); return result; }
    private List<GameObject> FindRequiredItems(ModuleConfig config) { var result = new List<GameObject>(); if(config?.requiredItems == null) return result; foreach(var id in config.requiredItems) { var go = FindSpawnedItem(id); if(go != null) result.Add(go); } return result; }
    private GameObject FindSpawnedItem(string prefabId) { var go = GameObject.Find(prefabId) ?? GameObject.Find(prefabId + "(Clone)"); if(go != null) return go; foreach(var obj in FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if(obj.name.Contains(prefabId)) return obj; return null; }
    private string GetExtraValue(ModuleConfig config, string key, string fallback) { if(config?.extra == null) return fallback; foreach(var kv in config.extra) if(kv.key == key) return kv.GetLocalized(); return fallback; }
    private void SpeakGuide(ModuleConfig config) { string text = GetExtraValue(config, "description", ""); if(!string.IsNullOrEmpty(text)) TTSManager.Instance?.Speak(text); }
    private void ClearVisualGuides() { foreach(var g in _tempGuides) if(g) Destroy(g); _tempGuides.Clear(); _spawnMarkers.Clear(); foreach(var item in _requiredItemObjects) SetOutline(item, false); _requiredItemObjects.Clear(); foreach(var t in _clickTargets) SetOutline(t, false); _clickTargets.Clear(); _ghostObject = null; _targetMarker = null; }
    private string GetDisplayName(string id) => id switch { "GDS_Tablet" => "GDS 진단기", "InsulatedGloves" => "절연 장갑", "InsulatedShoes" => "절연화", "FaceShield" => "안면보호구", "SmartKey" => "스마트키", "LockBox" => "잠금박스", "ImpactWrench" => "임팩트 렌치", "BatteryJack" => "배터리 잭", "Multimeter" => "멀티미터", "InsulationTester" => "절연 저항 측정기", _ => id };
}