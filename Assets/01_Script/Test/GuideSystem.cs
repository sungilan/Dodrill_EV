using DG.Tweening;
using DoDrill;
using EPOOutline;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Tables;
using XRAirpotrSecurity;
using UnityEngine.UI;

// ============================================================
// GuideSystem.cs — 통합 가이드 매니저 (아이템 연속 단계 대응 및 타겟 탐색 최적화)
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
        public UnityEngine.UI.Image guideImage;
        public TextMeshProUGUI guideObjectNameText;
        public Slider autoHideSlider;
    }

    [Header("난이도")]
    public GuideLevel currentLevel = GuideLevel.Easy;

    [Header("UI - 플랫폼별 가이드 그룹 설정")]
    public PlatformUIGroup pcUI;
    public PlatformUIGroup mobileUI;
    public PlatformUIGroup vrUI;

    private CanvasGroup centerGuideGroup;
    private TextMeshProUGUI activeTitleText;
    private TextMeshProUGUI mainGuideText;
    private TextMeshProUGUI subGuideText;
    private UnityEngine.UI.Image activeGuideImage;
    private TextMeshProUGUI activeGuideObjectNameText;
    private Slider activeAutoHideSlider;

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
    private List<GameObject> _requiredItemObjects = new();
    private List<GameObject> _tempGuides = new();
    private GameObject _ghostObject;
    private GuideMarker _targetMarker;
    private List<GameObject> _clickTargets = new();
    private float _idleTimer;
    private Coroutine _hintCoroutine;
    private bool _completed = false;
    private Dictionary<string, GameObject> _spawnMarkers = new();
    private Dictionary<int, Tween> _activeBlinkTweens = new();

    private List<MiniMapComponent> _activeMiniMapBlinks = new List<MiniMapComponent>();

    private Sequence _currentGuideSequence;
    private int _lastActiveTaskIndex = -1;

    private void Awake()
    {
        if(Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SetupPlatformUI();
    }

    private void Start()
    {
        hintButton?.SetActive(false);
        SetCenterAlpha(0f, "Start Initializing");
    }

    private void SetupPlatformUI()
    {
        PlatformType p = GameScenePlatformManager.CurrentPlatform;
        PlatformUIGroup selected;

        if(p == PlatformType.VR) selected = vrUI;
        else if(p == PlatformType.Mobile) selected = mobileUI;
        else selected = pcUI;

        centerGuideGroup = selected.group;
        activeTitleText = selected.titleText;
        mainGuideText = selected.mainText;
        subGuideText = selected.subText;
        activeGuideImage = selected.guideImage;
        activeGuideObjectNameText = selected.guideObjectNameText;
        activeAutoHideSlider = selected.autoHideSlider;

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
        if(_currentTask.status == TaskStatus.Running) { _completed = false; ShowGuideForCurrentTask(); }
        else if(_currentTask.status == TaskStatus.Completed) OnTaskCompleted();
    }

    private void OnSnapshotReceived(ScenarioSnapshotBroadcast broadcast)
    {
        _scenarioData = broadcast.scenarioData;
        _currentTask = broadcast.currentTask;
        if(_currentTask.status == TaskStatus.Running) { _completed = false; ShowGuideForCurrentTask(); }
    }

    private void OnScenarioReceived(ScenarioData data)
    {
        _scenarioData = data;
        if(_currentTask.status == TaskStatus.Running && !_completed) ShowGuideForCurrentTask();
    }

    private void ShowGuideForCurrentTask()
    {
        if(_scenarioData == null) return;
        int idx = _currentTask.taskIndex;

        if(_lastActiveTaskIndex == idx && !_completed) return;
        _lastActiveTaskIndex = idx;

        var tasks = _scenarioData.scenario?.tasks;
        if(tasks == null || idx >= tasks.Count) return;

        var taskDef = tasks[idx];
        var config = _scenarioData.GetModuleConfig(taskDef.moduleId);

        RefreshUI(idx, taskDef.moduleId, config);
        HandleCenterUI();

        ClearVisualGuides();

        // ★ [수정] requiredItemObjects는 초기화하되, BoltGroup 필터링은 나중에
        var rawRequiredItems = FindRequiredItems(config);
        _idleTimer = 0f;

        if(currentLevel != GuideLevel.Hard)
        {
            ProcessClickTargets(config, taskDef);

            // ★ [핵심] BoltGroup이 있으면 부모는 제외하고 자식만 추가
            foreach(var item in rawRequiredItems)
            {
                if(item == null) continue;

                var boltGroup = item.GetComponent<BoltGroupCounter>();
                if(boltGroup == null)
                {
                    boltGroup = item.GetComponentInChildren<BoltGroupCounter>();
                }

                // BoltGroup이 있으면 부모는 추가하지 말고 자식만 추가
                if(boltGroup != null)
                {
                    Debug.Log($"<color=orange>[ShowGuideForCurrentTask]</color> {item.name}은 BoltGroup 부모이므로 제외, 자식만 사용");
                    // 부모는 _requiredItemObjects에 추가하지 않음
                    // 자식은 나중에 BuildSpawnGuides에서 처리됨
                }
                else
                {
                    // BoltGroup 없으면 원본 추가
                    //_requiredItemObjects.Add(item);
                    //SetOutline(item, true);
                    Debug.Log($"<color=lime>[ShowGuideForCurrentTask]</color> {item.name}에 아웃라인 적용");
                }
            }

            BuildSpawnGuides(taskDef);
            SpeakGuide(config);
        }

        hintButton?.SetActive(currentLevel == GuideLevel.Normal);
    }

    private void OnTaskCompleted()
    {
        _completed = true;
        _lastActiveTaskIndex = -1;

        if(_currentGuideSequence != null)
        {
            _currentGuideSequence.Kill(true);
            _currentGuideSequence = null;
        }

        ClearVisualGuides();

        // ✅ 추가: 혹시 남은 Tween 전체 제거 (디버그용으로 매우 강력)
        DOTween.KillAll();

        if(centerGuideGroup != null)
            centerGuideGroup.DOFade(0f, 0.5f);

        TTSManager.Instance?.Stop();
    }

    private void SetCenterAlpha(float targetAlpha, string reason = "")
    {
        if(centerGuideGroup == null) return;
        centerGuideGroup.alpha = targetAlpha;
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
    public Transform GetCurrentGuideTransform()
    {
        // 1순위: 목적지 마커 (Zone, Target)
        if(_targetMarker != null)
            return _targetMarker.transform;

        // 2순위: 필요한 아이템
        if(_requiredItemObjects != null && _requiredItemObjects.Count > 0)
        {
            var obj = _requiredItemObjects[0];
            if(obj != null) return obj.transform;
        }

        return null;
    }

    public bool HasGuideTarget()
    {
        return _targetMarker != null || (_requiredItemObjects != null && _requiredItemObjects.Count > 0);
    }

    public bool IsCurrentTargetZone()
    {
        return _targetMarker != null;
    }

    public void SetOutline(GameObject target, bool on)
    {
        if(target == null) return;

        var outlinable = target.GetComponent<Outlinable>();
        int targetId = target.GetInstanceID();

        // ✅ 기존 트윈 무조건 제거
        if(_activeBlinkTweens.TryGetValue(targetId, out Tween existingTween))
        {
            existingTween.Kill(true);
            _activeBlinkTweens.Remove(targetId);
        }

        if(on)
        {
            var allRenderers = target.GetComponentsInChildren<Renderer>(true);
            foreach(var r in allRenderers)
            {
                if(r is CanvasRenderer) continue;
                if(!r.enabled) r.enabled = true;
            }

            if(outlinable == null) outlinable = target.AddComponent<Outlinable>();

            var field = typeof(Outlinable).GetField("outlineTargets",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if(field != null)
                (field.GetValue(outlinable) as System.Collections.IList)?.Clear();

            foreach(var r in allRenderers)
            {
                if(r is CanvasRenderer || (r.GetComponent<MeshFilter>() == null && r is not SkinnedMeshRenderer))
                    continue;

                outlinable.AddRenderer(r);
            }

            outlinable.RenderStyle = renderStyle;
            outlinable.OutlineParameters.Color = outlineColor;
            outlinable.OutlineParameters.BlurShift = outlineWidth;
            outlinable.enabled = true;

            Color targetColor = outlineColor;
            targetColor.a = 0.15f;

            // ✅ 핵심: SetLink + ID 둘 다 사용
            Tween blinkTween = DOTween.To(
                    () => outlinable.OutlineParameters.Color,
                    x => outlinable.OutlineParameters.Color = x,
                    targetColor,
                    0.6f)
                .SetLoops(-1, LoopType.Yoyo)
                .SetEase(Ease.InOutSine)
                .SetId(targetId)
                .SetLink(target); // 🔥 중요

            _activeBlinkTweens[targetId] = blinkTween;
        }
        else
        {
            if(outlinable != null)
            {
                outlinable.enabled = false;
                outlinable.OutlineParameters.Color = outlineColor;
            }
        }
    }

    private void ClearVisualGuides()
    {
        // 1. 미니맵
        foreach(var mmc in _activeMiniMapBlinks)
            if(mmc != null) mmc.StopBlinking();

        _activeMiniMapBlinks.Clear();

        // 2. Outline Tween 완전 제거
        foreach(var kvp in _activeBlinkTweens)
        {
            kvp.Value?.Kill(true);
        }
        _activeBlinkTweens.Clear();

        // 3. 아웃라인 OFF
        foreach(var obj in _requiredItemObjects)
            if(obj != null) SetOutline(obj, false);

        foreach(var obj in _clickTargets)
            if(obj != null) SetOutline(obj, false);

        _requiredItemObjects.Clear();
        _clickTargets.Clear();

        // 4. 가이드 오브젝트 제거
        foreach(var g in _tempGuides)
        {
            if(g != null)
            {
                // 🔥 핵심: 완전 Kill
                DOTween.Kill(g, true);
                DOTween.Kill(g.transform, true);

                Destroy(g);
            }
        }

        _tempGuides.Clear();
        _spawnMarkers.Clear();

        _ghostObject = null;
        _targetMarker = null;
    }

    //private void BuildSpawnGuides(TaskDef taskDef)
    //{
    //    if(taskDef == null) return;
    //    Debug.Log($"<color=cyan>[Guide-Log]</color> <b>BuildSpawnGuides 시작</b>: {taskDef.moduleId}");

    //    var allSP = FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    //    var spCache = new Dictionary<string, SpawnPoint>();
    //    foreach(var sp in allSP) if(!string.IsNullOrEmpty(sp.pointId)) spCache[sp.pointId] = sp;

    //    var config = _scenarioData.GetModuleConfig(taskDef.moduleId);

    //    if(taskDef.spawnObjects != null && taskDef.spawnObjects.Count > 0)
    //    {
    //        foreach(var bundle in taskDef.spawnObjects)
    //        {
    //            if(string.IsNullOrEmpty(bundle.prefabId)) continue;
    //            var existingItem = FindSpawnedItem(bundle.prefabId);

    //            // ★ [핵심수정] 이미 씬에 있는 아이템이라도, 이번 단계에서 필요한 도구라면 미니맵 아이콘을 켜줍니다.
    //            if(existingItem != null && bundle.showGuide)
    //            {
    //                var mmcs = existingItem.GetComponentsInChildren<MiniMapComponent>(true);
    //                foreach(var mmc in mmcs)
    //                {
    //                    mmc.StartBlinking();
    //                    if(!_activeMiniMapBlinks.Contains(mmc)) _activeMiniMapBlinks.Add(mmc);
    //                }
    //            }

    //            bool isGrabbed = (existingItem?.GetComponent<SyncGrab>()?.IsGrabbed ?? false);
    //            if(!isGrabbed && bundle.showGuide)
    //            {
    //                if(!string.IsNullOrEmpty(bundle.spawnPointId) && spCache.TryGetValue(bundle.spawnPointId, out var spPt))
    //                {
    //                    Vector3 pos = (existingItem != null) ? GetBoundsCenter(existingItem) : spPt.transform.position;
    //                    if(existingItem?.GetComponent<NoneHighlight>() == null)
    //                    {
    //                        var markerGO = SpawnMarker(pos, GetDisplayName(bundle.prefabId), spawnGuideColor, false);
    //                        if(markerGO != null) _spawnMarkers[bundle.prefabId] = markerGO.gameObject;
    //                    }
    //                }
    //            }
    //            ProcessTargetGuide(bundle.guideId, bundle.prefabId, config);
    //        }
    //    }
    //    else if(config != null && !string.IsNullOrEmpty(config.targetObjName))
    //    {
    //        Debug.Log($"<color=orange>[Guide-Log]</color> {taskDef.moduleId}: spawnObjects 없음. Config({config.targetObjName}) 기반 가이드 생성");
    //        ProcessTargetGuide(null, null, config);
    //    }
    //}
    private void BuildSpawnGuides(TaskDef taskDef)
    {
        if(taskDef == null) return;

        var allSP = FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var spCache = new Dictionary<string, SpawnPoint>();
        foreach(var sp in allSP) if(!string.IsNullOrEmpty(sp.pointId)) spCache[sp.pointId] = sp;

        var config = _scenarioData.GetModuleConfig(taskDef.moduleId);

        if(taskDef.spawnObjects != null && taskDef.spawnObjects.Count > 0)
        {
            foreach(var bundle in taskDef.spawnObjects)
            {
                if(string.IsNullOrEmpty(bundle.prefabId)) continue;
                var existingItem = FindSpawnedItem(bundle.prefabId);

                Debug.Log($"<color=yellow>[BuildSpawnGuides]</color> 스폰 아이템: {bundle.prefabId} → {existingItem?.name ?? "NULL"}");

                // ★ BoltGroup 찾기
                GameObject outlineTarget = null;

                if(existingItem != null)
                {
                    var innerBoltGroup = existingItem.GetComponent<BoltGroupCounter>();
                    if(innerBoltGroup == null)
                    {
                        innerBoltGroup = existingItem.GetComponentInChildren<BoltGroupCounter>();
                    }

                    // BoltGroup이 있으면 자식만 아웃라인
                    if(innerBoltGroup != null)
                    {
                        outlineTarget = innerBoltGroup.gameObject;
                        Debug.Log($"<color=lime>[BuildSpawnGuides]</color> BoltGroup 자식 아웃라인: {outlineTarget.name}");
                    }
                    else
                    {
                        // BoltGroup이 없으면 원본 아이템 (단, ShowGuideForCurrentTask에서 이미 처리했을 가능성 있음)
                        if(!_requiredItemObjects.Contains(existingItem))
                        {
                            outlineTarget = existingItem;
                            Debug.Log($"<color=yellow>[BuildSpawnGuides]</color> 원본 아이템 아웃라인: {outlineTarget.name}");
                        }
                        else
                        {
                            Debug.Log($"<color=gray>[BuildSpawnGuides]</color> {existingItem.name}은 이미 처리됨");
                        }
                    }
                }

                // 미니맵
                if(existingItem != null && bundle.showGuide)
                {
                    var mmcs = existingItem.GetComponentsInChildren<MiniMapComponent>(true);
                    foreach(var mmc in mmcs)
                    {
                        mmc.StartBlinking();
                        if(!_activeMiniMapBlinks.Contains(mmc)) _activeMiniMapBlinks.Add(mmc);
                    }
                }

                // 스폰 마커
                bool isGrabbed = (existingItem?.GetComponent<SyncGrab>()?.IsGrabbed ?? false);
                if(!isGrabbed && bundle.showGuide)
                {
                    if(!string.IsNullOrEmpty(bundle.spawnPointId) && spCache.TryGetValue(bundle.spawnPointId, out var spPt))
                    {
                        Vector3 pos = (existingItem != null) ? GetBoundsCenter(existingItem) : spPt.transform.position;
                        if(existingItem?.GetComponent<NoneHighlight>() == null)
                        {
                            var markerGO = SpawnMarker(pos, GetDisplayName(bundle.prefabId), spawnGuideColor, false);
                            if(markerGO != null) _spawnMarkers[bundle.prefabId] = markerGO.gameObject;
                        }
                    }
                }

                // ★ 아웃라인 적용 (중복 방지)
                if(outlineTarget != null && !_requiredItemObjects.Contains(outlineTarget))
                {
                    _requiredItemObjects.Add(outlineTarget);
                    SetOutline(outlineTarget, true);
                    Debug.Log($"<color=green>[BuildSpawnGuides]</color> {outlineTarget.name}에 아웃라인 최종 적용");
                }

                ProcessTargetGuide(bundle.guideId, bundle.prefabId, config);
            }
        }
        else if(config != null && !string.IsNullOrEmpty(config.targetObjName))
        {
            ProcessTargetGuide(null, null, config);
        }
    }

    private void ProcessTargetGuide(string guideId, string prefabId, ModuleConfig config)
    {
        // ── 1. 목표 지점(Target) 결정 ──
        // GuideZoneData가 붙은 객체를 우선적으로 찾도록 FindSpawnedItem 로직이 보강되었다고 가정합니다.
        GameObject markerTargetGO = !string.IsNullOrEmpty(guideId) ? FindSpawnedItem(guideId) : null;
        if(markerTargetGO == null && config != null && !string.IsNullOrEmpty(config.targetObjName))
        {
            markerTargetGO = FindSpawnedItem(config.targetObjName);
        }

        // ── 2. 기본 아이템(Item) 결정 ──
        GameObject itemGO = !string.IsNullOrEmpty(prefabId) ? FindSpawnedItem(prefabId) : null;

        // ── 3. 가이드 처리 시작 ──
        if(markerTargetGO != null)
        {
            var zoneData = markerTargetGO.GetComponent<GuideZoneData>();
            UpdateGuideUI(zoneData);

            bool hasZoneData = zoneData != null;

            // [판정] 데이터가 있다면 그 값을 따르고, 없다면 기본적으로 켬
            //bool shouldSpawnMarker = hasZoneData ? zoneData.showGuide : true;
            //bool shouldSpawnGhost = hasZoneData ? zoneData.showGhost : true;
            bool shouldSpawnMarker = hasZoneData && zoneData.showGuide;
            bool shouldSpawnGhost = hasZoneData && zoneData.showGhost;

            Debug.Log($"<color=yellow>[Guide-Check]</color> 타겟: <b>{markerTargetGO.name}</b> | " +
                      $"GuideZoneData 존재: {hasZoneData} | " +
                      $"마커 생성 결정: {shouldSpawnMarker} | 고스트 생성 결정: {shouldSpawnGhost}");

            // [A] 마커(화살표) 생성 처리
            if(shouldSpawnMarker)
            {
                string label = "이곳으로 이동하세요";
                if(zoneData != null && zoneData.guideDescription != null)
                {
                    var tr = zoneData.guideDescription.TableReference;
                    var ter = zoneData.guideDescription.TableEntryReference;

                    if(!tr.ReferenceType.Equals(UnityEngine.Localization.Tables.TableReference.Type.Empty) &&
                        !ter.ReferenceType.Equals(UnityEngine.Localization.Tables.TableEntryReference.Type.Empty))
                    {
                        label = zoneData.guideDescription.GetLocalizedString();
                    }
                }

                var boltGroup = markerTargetGO.GetComponent<BoltGroupCounter>();
                bool isBoltTask = boltGroup != null || (markerTargetGO.name != null && markerTargetGO.name.Contains("Bolt"));
                Vector3 markerPos = isBoltTask ? GetBoundsCenter(markerTargetGO) : markerTargetGO.transform.position;

                var marker = SpawnMarker(markerPos, label, targetGuideColor, true);
                if(marker != null)
                {
                    Debug.Log($"<color=cyan>[Guide-Success]</color> {markerTargetGO.name} 지점에 마커 생성 완료");
                    marker.Setup(label, targetGuideColor, !isBoltTask && markerTargetGO.GetComponent<NoneHighlight>() == null);
                    _targetMarker = marker;
                    if(boltGroup != null) StartCoroutine(UpdateBoltProgressRoutine(marker, boltGroup));
                }
            }
            else
            {
                Debug.Log($"<color=orange>[Guide-Skip]</color> {markerTargetGO.name}: showGuide가 false이므로 마커 생성을 건너뜁니다.");
            }

            // [B] 고스트(Ghost) 생성 처리
            //if(shouldSpawnGhost && ghostMaterial != null && !markerTargetGO.name.Contains("Bolt"))
            //{
            //    GameObject ghostSource = null;

            //    // 1순위: GuideZoneData에 지정된 전용 고스트가 있는지 확인
            //    if(hasZoneData && !string.IsNullOrEmpty(zoneData.customGhostPrefabId))
            //    {
            //        ghostSource = FindSpawnedItem(zoneData.customGhostPrefabId);
            //        Debug.Log($"<color=lime>[Ghost-Log]</color> 커스텀 고스트 원본 사용: {zoneData.customGhostPrefabId}");
            //    }

            //    // 2순위: 없다면 현재 태스크의 prefabId 아이템 사용
            //    if(ghostSource == null)
            //    {
            //        ghostSource = itemGO;
            //        if(ghostSource != null) Debug.Log($"<color=lime>[Ghost-Log]</color> 기본 아이템 고스트 사용: {ghostSource.name}");
            //    }

            //    if(ghostSource != null)
            //    {
            //        _ghostObject = SpawnGhost(ghostSource, markerTargetGO.transform.position);
            //    }
            //}

            // [C] 미니맵 처리 (가이드 ON/OFF와 상관없이 위치는 알려줌)
            foreach(var mmc in markerTargetGO.GetComponentsInChildren<MiniMapComponent>(true))
            {
                if(mmc != null)
                {
                    mmc.StartBlinking();
                    if(!_activeMiniMapBlinks.Contains(mmc)) _activeMiniMapBlinks.Add(mmc);
                }
            }
        }

        // ── 4. 필요 아이템(도구) 하이라이트 처리 ──
        if(itemGO != null)
        {
            //SetOutline(itemGO, true);

            // 아이템의 미니맵 아이콘도 켜줌
            foreach(var immc in itemGO.GetComponentsInChildren<MiniMapComponent>(true))
            {
                if(immc != null)
                {
                    immc.StartBlinking();
                    if(!_activeMiniMapBlinks.Contains(immc)) _activeMiniMapBlinks.Add(immc);
                }
            }
        }
    }

    private void UpdateGuideUI(GuideZoneData zoneData)
    {
        if(zoneData == null) return;
        if(activeGuideImage) { activeGuideImage.sprite = zoneData.targetImage; activeGuideImage.gameObject.SetActive(zoneData.targetImage != null); }
        if(activeGuideObjectNameText) { var ne = activeGuideObjectNameText.GetComponent<LocalizeStringEvent>(); if(ne) ne.StringReference = zoneData.targetName; activeGuideObjectNameText.gameObject.SetActive(true); }
        if(subGuideText) { var de = subGuideText.GetComponent<LocalizeStringEvent>(); if(de) de.StringReference = zoneData.guideDescription; subGuideText.gameObject.SetActive(true); }
    }

    private GameObject SpawnGhost(GameObject sourceItem, Vector3 ghostPos)
    {
        if(sourceItem == null) return null;

        // 1. 고스트 생성
        GameObject ghost = Instantiate(sourceItem, ghostPos, sourceItem.transform.rotation);
        if(ghost == null) return null;

        // 2. 물리 컴포넌트 제거
        foreach(var col in ghost.GetComponentsInChildren<Collider>(true)) Destroy(col);
        foreach(var rb in ghost.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);

        // 3. ★ 네트워크 컴포넌트 의존성 역순 제거 ★
        // FishNet의 의존성 구조: NetworkObserver -> NetworkObject
        // 따라서 반드시 Observer를 먼저 지워야 Object를 지울 수 있습니다.

        // 모든 자식들을 포함하여 NetworkObserver부터 제거
        var observers = ghost.GetComponentsInChildren<FishNet.Observing.NetworkObserver>(true);
        foreach(var obs in observers) DestroyImmediate(obs);

        // 그 다음 NetworkObject 제거
        var nObjects = ghost.GetComponentsInChildren<FishNet.Object.NetworkObject>(true);
        foreach(var nob in nObjects) DestroyImmediate(nob);

        // 나머지 모든 일반 스크립트 제거 (Transform 제외)
        var allScripts = ghost.GetComponentsInChildren<MonoBehaviour>(true);
        foreach(var script in allScripts)
        {
            if(script == null || script is Transform) continue;
            DestroyImmediate(script);
        }

        // 4. 레이어 변경 (UI 레이어)
        int uiLayer = LayerMask.NameToLayer("UI");
        if(uiLayer != -1) SetLayerRecursive(ghost, uiLayer);

        // 5. 머티리얼 적용
        if(ghostMaterial != null)
        {
            foreach(var r in ghost.GetComponentsInChildren<Renderer>(true))
            {
                if(r == null) continue;
                var mats = new Material[r.sharedMaterials.Length];
                for(int i = 0; i < mats.Length; i++) mats[i] = ghostMaterial;
                r.materials = mats;
            }
        }

        _tempGuides.Add(ghost);
        ghost.transform.DOLocalMoveY(0.05f, 1.5f).SetLoops(-1, LoopType.Yoyo).SetRelative().SetLink(ghost);

        return ghost;
    }

    private GuideMarker SpawnMarker(Vector3 worldPos, string description, Color color, bool isTarget)
    {
        if(guidePrefab == null) return null;
        var go = Instantiate(guidePrefab, worldPos, Quaternion.identity);
        var marker = go.GetComponent<GuideMarker>();
        if(marker != null) { marker.Setup(description, color); _tempGuides.Add(go); return marker; }
        Destroy(go); return null;
    }

    private IEnumerator UpdateBoltProgressRoutine(GuideMarker marker, BoltGroupCounter boltGroup)
    {
        int lastProgress = -1;
        while(marker != null && boltGroup != null && !_completed)
        {
            if(boltGroup.RemovedCount != lastProgress)
            {
                marker.UpdateProgressText($"{boltGroup.TotalCount - boltGroup.RemovedCount}/{boltGroup.TotalCount}");
                lastProgress = boltGroup.RemovedCount;
            }
            yield return new WaitForSeconds(0.1f);
        }
    }

    public void OnItemGrabbed(string prefabId)
    {
        if(_spawnMarkers.TryGetValue(prefabId, out var markerGO))
        {
            if(markerGO != null) { _tempGuides.Remove(markerGO); markerGO.transform.DOKill(); Destroy(markerGO); }
            _spawnMarkers.Remove(prefabId);
        }
    }

    private void TriggerNudge()
    {
        _targetMarker?.Nudge();
        if(_ghostObject != null) _ghostObject.transform.DOShakePosition(1.5f, 0.06f, 12, 90f);
    }

    private GameObject FindSpawnedItem(string id)
    {
        if(string.IsNullOrEmpty(id)) return null;

        // [핵심 보강 1] 볼트 그룹(BoltGroupCounter)을 최우선으로 탐색
        // 부모인 배터리 팩이 잡히는 것을 원천적으로 차단합니다.
        var allBoltGroups = FindObjectsByType<BoltGroupCounter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach(var bg in allBoltGroups)
        {
            if(bg.gameObject.name == id || bg.gameObject.name == id + "(Clone)")
            {
                return bg.gameObject;
            }
        }

        // [핵심 보강 2] GuideZoneData가 붙은 객체 우선 탐색
        var allZoneData = FindObjectsByType<GuideZoneData>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach(var zd in allZoneData)
        {
            if(zd.gameObject.name == id || zd.gameObject.name == id + "(Clone)") return zd.gameObject;
        }

        // 3. 그 외 일반 오브젝트 탐색
        var allObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach(var obj in allObjects)
        {
            if(obj.name == id || obj.name == id + "(Clone)") return obj;
        }

        var allClickables = FindObjectsByType<ClickableAnimator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach(var clickable in allClickables) if(clickable.uniqueId == id) return clickable.gameObject;

        foreach(var obj in allObjects) if(!obj.name.StartsWith("Icon_") && obj.name.Contains(id)) return obj;

        return null;
    }

    private string GetExtraValue(ModuleConfig config, string key, string fallback)
    {
        if(config?.extra == null) return fallback;
        foreach(var kv in config.extra) if(kv.key == key) return kv.GetLocalized();
        return fallback;
    }

    private void SpeakGuide(ModuleConfig config)
    {
        string text = GetExtraValue(config, "description", "");
        if(!string.IsNullOrEmpty(text)) TTSManager.Instance?.Speak(text);
    }

    private List<GameObject> FindClickTargets(ModuleConfig config)
    {
        var result = new List<GameObject>();
        if(config == null || string.IsNullOrEmpty(config.targetObjName)) return result;
        var go = FindSpawnedItem(config.targetObjName);
        if(go != null) result.Add(go);
        return result;
    }

    private List<GameObject> FindRequiredItems(ModuleConfig config)
    {
        var result = new List<GameObject>();
        if(config?.requiredItems == null) return result;
        foreach(var id in config.requiredItems) { var go = FindSpawnedItem(id); if(go != null) result.Add(go); }
        return result;
    }

    private Vector3 GetBoundsCenter(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if(renderers == null || renderers.Length == 0) return go.transform.position;
        var bounds = renderers[0].bounds;
        for(int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds.center;
    }

    private void ProcessClickTargets(ModuleConfig config, TaskDef taskDef)
    {
        _clickTargets = FindClickTargets(config);
        foreach(var t in _clickTargets)
        {
            // 아웃라인은 여기서 켜줍니다.
            SetOutline(t, true);

            // ★ 마커 생성 중복 방지 로직 ★
            // 이미 _targetMarker가 존재하거나 ProcessTargetGuide에서 처리되었다면 여기서 생성하지 않음
            var zoneData = t.GetComponent<GuideZoneData>();
            bool shouldSpawnMarker = (zoneData == null) || zoneData.showGuide;

            // targetMarker가 이미 생성되었는지 체크하거나, 
            // 아예 마커 생성 로직을 이 함수에서 제거하고 ProcessTargetGuide에 맡깁니다.

            /* 기존 마커 생성 코드 주석 처리 또는 삭제
            if(shouldSpawnMarker && (taskDef.spawnObjects == null || taskDef.spawnObjects.Count == 0))
            {
                // ... 생략 ...
                // SpawnMarker(...) -> 이 부분을 삭제하세요.
            }
            */

            Debug.Log($"<color=cyan>[Guide-ClickTarget]</color> {t.name} 아웃라인 설정 완료 (마커 생성은 통합 로직에서 관리)");
        }
    }

    // ★ 자동 숨기기 및 슬라이더 애니메이션 처리 핵심 로직
    private void HandleCenterUI()
    {
        if(centerGuideGroup == null) return;
        if(_currentGuideSequence != null) { _currentGuideSequence.Kill(true); _currentGuideSequence = null; }

        centerGuideGroup.gameObject.SetActive(true);
        centerGuideGroup.interactable = true;
        centerGuideGroup.blocksRaycasts = true;

        // 슬라이더가 있다면 초기값(1)으로 설정
        if(activeAutoHideSlider != null) activeAutoHideSlider.value = 1f;

        _currentGuideSequence = DOTween.Sequence();

        // 1. UI 나타남 (Fade In)
        _currentGuideSequence.Append(centerGuideGroup.DOFade(1f, 0.5f));

        // 2. 슬라이더가 있을 경우 대기 시간 동안 게이지 감소 시각화
        if(activeAutoHideSlider != null)
        {
            _currentGuideSequence.Append(activeAutoHideSlider.DOValue(0f, autoHideDelay).SetEase(Ease.Linear));
        }
        else
        {
            // 슬라이더가 없으면 그냥 시간만큼 대기
            _currentGuideSequence.AppendInterval(autoHideDelay);
        }

        // 3. UI 사라짐 (Fade Out)
        _currentGuideSequence.Append(centerGuideGroup.DOFade(0f, 0.5f))
            .OnComplete(() => {
                centerGuideGroup.interactable = false;
                centerGuideGroup.blocksRaycasts = false;
                _currentGuideSequence = null;
            });
    }

    // ═══════════════════════════════════════════
    //  외부 호출용 공개 메서드 (닫기 및 열기)
    // ═══════════════════════════════════════════

    /// <summary>
    /// 확인 버튼 클릭이나 특정 이벤트 발생 시 가이드 UI를 서서히 닫습니다.
    /// </summary>
    public void CloseGuideUI()
    {
        if(centerGuideGroup == null || centerGuideGroup.alpha <= 0) return;

        // 실행 중인 자동 숨기기 시퀀스가 있다면 중단
        if(_currentGuideSequence != null)
        {
            _currentGuideSequence.Kill();
            _currentGuideSequence = null;
        }

        Debug.Log("<color=orange>[Guide-System]</color> 사용자 요청에 의해 가이드 UI 닫기 실행");

        // 즉시 상호작용 차단 (두 번 클릭 방지)
        centerGuideGroup.interactable = false;
        centerGuideGroup.blocksRaycasts = false;

        // 페이드 아웃 애니메이션 실행
        centerGuideGroup.DOFade(0f, 0.5f).SetEase(Ease.InSine).OnComplete(() => {
            if(activeAutoHideSlider != null) activeAutoHideSlider.value = 0f;
            // 완전히 꺼졌음을 보장하기 위해 비활성화 (선택 사항)
            // centerGuideGroup.gameObject.SetActive(false); 
        });
    }

    /// <summary>
    /// 버튼 등 외부에서 호출하여 현재 가이드 UI를 다시 표시합니다.
    /// </summary>
    public void ReopenGuideUI()
    {
        if(_scenarioData == null || _currentTask.status != TaskStatus.Running || _completed)
        {
            return;
        }

        HandleCenterUI();
    }
    private void SetLayerRecursive(GameObject obj, int newLayer)
    {
        if(obj == null) return;

        obj.layer = newLayer;
        foreach(Transform child in obj.transform)
        {
            SetLayerRecursive(child.gameObject, newLayer);
        }
    }

    private string GetDisplayName(string id)
    {
        // 1. 씬에 스폰된 아이템 중 해당 prefabId를 가진 TaskItem을 찾습니다.
        GameObject go = FindSpawnedItem(id);
        if(go != null)
        {
            var taskItem = go.GetComponent<TaskItem>();
            // 2. TaskItem에 itemName이 설정되어 있다면 로컬라이징된 문자열 반환
            if(taskItem != null)
            {
                return taskItem.itemName.GetLocalizedString();
            }
        }

        // 3. 만약 아이템을 못 찾거나 이름 설정이 없다면 폴백(id) 반환
        return id;
    }

    private void OnDestroy()
    {
        DOTween.KillAll();
    }
}