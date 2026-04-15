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
        _requiredItemObjects = FindRequiredItems(config);
        _idleTimer = 0f;

        if(currentLevel != GuideLevel.Hard)
        {
            ProcessClickTargets(config, taskDef);
            foreach(var item in _requiredItemObjects) SetOutline(item, true);
            BuildSpawnGuides(taskDef);
            SpeakGuide(config);
        }

        hintButton?.SetActive(currentLevel == GuideLevel.Normal);
    }

    private void OnTaskCompleted()
    {
        _completed = true; _lastActiveTaskIndex = -1;
        if(_currentGuideSequence != null) { _currentGuideSequence.Kill(); _currentGuideSequence = null; }
        ClearVisualGuides();
        if(centerGuideGroup != null) centerGuideGroup.DOFade(0f, 0.5f);
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

    private void SetOutline(GameObject target, bool on)
    {
        if(target == null) return;
        var outlinable = target.GetComponent<Outlinable>();
        int targetId = target.GetInstanceID();

        // 기존에 돌고 있던 트윈이 있다면 무조건 Kill
        if(_activeBlinkTweens.TryGetValue(targetId, out Tween existingTween))
        {
            existingTween.Kill();
            _activeBlinkTweens.Remove(targetId);
        }

        if(on)
        {
            if(outlinable == null) outlinable = target.AddComponent<Outlinable>();

            // 렌더러 타겟 설정
            var field = typeof(Outlinable).GetField("outlineTargets", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if(field != null) (field.GetValue(outlinable) as System.Collections.IList)?.Clear();

            var allRenderers = target.GetComponentsInChildren<Renderer>(true);
            foreach(var r in allRenderers)
            {
                if(r is CanvasRenderer || (r.GetComponent<MeshFilter>() == null && r is not SkinnedMeshRenderer)) continue;
                outlinable.AddRenderer(r);
            }

            outlinable.RenderStyle = renderStyle;
            outlinable.OutlineParameters.Color = outlineColor;
            outlinable.OutlineParameters.BlurShift = outlineWidth;
            outlinable.enabled = true;

            // 깜박임 트윈 시작
            Color targetColor = outlineColor;
            targetColor.a = 0.15f;
            Tween blinkTween = DOTween.To(() => outlinable.OutlineParameters.Color, x => outlinable.OutlineParameters.Color = x, targetColor, 0.6f)
                .SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetId(targetId);

            _activeBlinkTweens[targetId] = blinkTween;
        }
        else
        {
            // 아웃라인 끄기 로직 (보강)
            if(outlinable != null)
            {
                outlinable.enabled = false;
                // 색상 상태도 원래대로 복구 (깜박이다 멈춘 색이 남지 않도록)
                outlinable.OutlineParameters.Color = outlineColor;
            }
        }
    }

    private void ClearVisualGuides()
    {
        // 1. 미니맵 정리
        foreach(var mmc in _activeMiniMapBlinks) if(mmc != null) mmc.StopBlinking();
        _activeMiniMapBlinks.Clear();

        // 2. 아웃라인 트윈 정리 및 아웃라인 끄기 (보강된 부분)
        foreach(var kvp in _activeBlinkTweens)
        {
            if(kvp.Value != null) kvp.Value.Kill();

            // 인스턴스 ID를 통해 대상 오브젝트를 찾아 아웃라인을 물리적으로 끔
            // _requiredItemObjects나 _clickTargets 리스트를 활용하여 상태 복구
        }

        // 현재 단계에서 등록되었던 모든 타겟들의 아웃라인을 일괄 해제
        foreach(var obj in _requiredItemObjects) { if(obj != null) SetOutline(obj, false); }
        foreach(var obj in _clickTargets) { if(obj != null) SetOutline(obj, false); }

        _activeBlinkTweens.Clear();
        _requiredItemObjects.Clear();
        _clickTargets.Clear();

        // 3. 고스트 및 마커 정리
        foreach(var g in _tempGuides)
        {
            if(g != null)
            {
                g.transform.DOKill();
                DOTween.Kill(g);
                Destroy(g);
            }
        }
        _tempGuides.Clear(); _spawnMarkers.Clear();

        _ghostObject = null; _targetMarker = null;
    }

    private void BuildSpawnGuides(TaskDef taskDef)
    {
        if(taskDef == null) return;
        Debug.Log($"<color=cyan>[Guide-Log]</color> <b>BuildSpawnGuides 시작</b>: {taskDef.moduleId}");

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

                // ★ [핵심수정] 이미 씬에 있는 아이템이라도, 이번 단계에서 필요한 도구라면 미니맵 아이콘을 켜줍니다.
                if(existingItem != null && bundle.showGuide)
                {
                    var mmcs = existingItem.GetComponentsInChildren<MiniMapComponent>(true);
                    foreach(var mmc in mmcs)
                    {
                        mmc.StartBlinking();
                        if(!_activeMiniMapBlinks.Contains(mmc)) _activeMiniMapBlinks.Add(mmc);
                    }
                }

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
                ProcessTargetGuide(bundle.guideId, bundle.prefabId, config);
            }
        }
        else if(config != null && !string.IsNullOrEmpty(config.targetObjName))
        {
            Debug.Log($"<color=orange>[Guide-Log]</color> {taskDef.moduleId}: spawnObjects 없음. Config({config.targetObjName}) 기반 가이드 생성");
            ProcessTargetGuide(null, null, config);
        }
    }

    private void ProcessTargetGuide(string guideId, string prefabId, ModuleConfig config)
    {
        // ── 1. 목표 지점 결정 ──
        GameObject markerTargetGO = !string.IsNullOrEmpty(guideId) ? FindSpawnedItem(guideId) : null;
        if(markerTargetGO == null && config != null && !string.IsNullOrEmpty(config.targetObjName))
        {
            markerTargetGO = FindSpawnedItem(config.targetObjName);
        }

        // ── 2. 아이템 결정 ──
        GameObject itemGO = !string.IsNullOrEmpty(prefabId) ? FindSpawnedItem(prefabId) : null;

        // ── 3. 가이드 마커 및 미니맵 처리 (markerTargetGO 기준) ──
        if(markerTargetGO != null) // ★ 여기서 반드시 null 체크
        {
            var zoneData = markerTargetGO.GetComponent<GuideZoneData>();
            UpdateGuideUI(zoneData);

            bool shouldSpawnMarker = (zoneData == null) || zoneData.showGuide;
            if(shouldSpawnMarker)
            {
                // [에러 방지] zoneData가 null일 경우 기본 텍스트 사용
                //string label = (zoneData != null && zoneData.guideDescription != null && zoneData.guideDescription.RuntimeKeyIsValid)
                string label = "이곳으로 이동하세요";
                // [방어] 로컬라이징 키 유효성 체크 수정
                if(zoneData != null && zoneData.guideDescription != null)
                {
                    var tr = zoneData.guideDescription.TableReference;
                    var ter = zoneData.guideDescription.TableEntryReference;

                    // 테이블과 엔트리가 둘 다 설정되어 있을 때만 호출
                    if(!tr.ReferenceType.Equals(TableReference.Type.Empty) &&
                        !ter.ReferenceType.Equals(TableEntryReference.Type.Empty))
                    {
                        label = zoneData.guideDescription.GetLocalizedString();
                    }
                }

                var boltGroup = markerTargetGO.GetComponent<BoltGroupCounter>();
                // [에러 방지] markerTargetGO.name 참조 전 안전 확인
                bool isBoltTask = boltGroup != null || (markerTargetGO.name != null && markerTargetGO.name.Contains("Bolt"));

                Vector3 markerPos = isBoltTask ? GetBoundsCenter(markerTargetGO) : markerTargetGO.transform.position;

                var marker = SpawnMarker(markerPos, label, targetGuideColor, true);
                if(marker != null)
                {
                    marker.Setup(label, targetGuideColor, !isBoltTask && markerTargetGO.GetComponent<NoneHighlight>() == null);
                    _targetMarker = marker;
                    if(boltGroup != null) StartCoroutine(UpdateBoltProgressRoutine(marker, boltGroup));
                }
            }

            // 미니맵 컴포넌트 일괄 활성화
            foreach(var mmc in markerTargetGO.GetComponentsInChildren<MiniMapComponent>(true))
            {
                if(mmc != null)
                {
                    mmc.StartBlinking();
                    if(!_activeMiniMapBlinks.Contains(mmc)) _activeMiniMapBlinks.Add(mmc);
                }
            }
        }

        // ── 4. 아이템 하이라이트 및 고스트 ──
        if(itemGO != null)
        {
            SetOutline(itemGO, true);

            // 아이템의 미니맵 아이콘도 켜줌
            foreach(var immc in itemGO.GetComponentsInChildren<MiniMapComponent>(true))
            {
                if(immc != null)
                {
                    immc.StartBlinking();
                    if(!_activeMiniMapBlinks.Contains(immc)) _activeMiniMapBlinks.Add(immc);
                }
            }

            // 고스트 생성 시 markerTargetGO가 null인지 다시 확인 (에러 포인트)
            if(markerTargetGO != null && ghostMaterial != null && !markerTargetGO.name.Contains("Bolt"))
            {
                _ghostObject = SpawnGhost(itemGO, markerTargetGO.transform.position);
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
        var ghost = Instantiate(sourceItem, ghostPos, sourceItem.transform.rotation);
        foreach(var col in ghost.GetComponentsInChildren<Collider>()) Destroy(col);
        foreach(var rb in ghost.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
        foreach(var mono in ghost.GetComponentsInChildren<MonoBehaviour>()) Destroy(mono);
        foreach(var r in ghost.GetComponentsInChildren<Renderer>())
        {
            var mats = new Material[r.sharedMaterials.Length];
            for(int i = 0; i < mats.Length; i++) mats[i] = ghostMaterial;
            r.materials = mats;
        }
        _tempGuides.Add(ghost);
        ghost.transform.DOLocalMoveY(0.05f, 1.5f).SetLoops(-1, LoopType.Yoyo).SetRelative().SetId(ghost);
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
        var allObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        // 1. 완전 일치 우선 탐색
        foreach(var obj in allObjects) if(obj.name == id || obj.name == id + "(Clone)") return obj;

        // 2. ClickableAnimator uniqueId 탐색
        var allClickables = FindObjectsByType<ClickableAnimator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach(var clickable in allClickables) if(clickable.uniqueId == id) return clickable.gameObject;

        // 3. 부분 일치 (Icon_ 접두사 제외)
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
            SetOutline(t, true);
            if(taskDef.spawnObjects == null || taskDef.spawnObjects.Count == 0)
            {
                string label = GetExtraValue(config, "description", "이곳을 클릭하세요.");
                SpawnMarker(t.transform.position, label, targetGuideColor, true);
            }
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

    private string GetDisplayName(string id) => id switch
    {
        "GDS_Tablet" => "GDS 진단기",
        "InsulatedGloves" => "절연 장갑",
        "InsulatedShoes" => "절연화",
        "FaceShield" => "안면보호구",
        "SmartKey" => "스마트키",
        "LockBox" => "잠금박스",
        "ImpactWrench" => "임팩트 렌치",
        "BatteryJack" => "배터리 잭",
        "Multimeter" => "멀티미터",
        "InsulationTester" => "절연 저항 측정기",
        _ => id
    };
}