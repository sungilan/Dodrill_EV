using DG.Tweening;
using DoDrill;
using EPOOutline;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using EPOOutline;

// ============================================================
//  GuideSystem.cs  — 통합 가이드 매니저 (완전 자동화)
//
//  스폰포인트: GuideMarker(하늘색) — 아이템 바운드 중앙에 배치
//  타겟 존:    GuideMarker(황금색) — targetGuideId GO 위치에 배치 + 고스트
//  그랩 시:    해당 아이템의 스폰포인트 마커 자동 제거
// ============================================================
public class GuideSystem : MonoBehaviour
{
    public static GuideSystem Instance { get; private set; }
    public enum GuideLevel { Easy, Normal, Hard }

    [Header("난이도")]
    public GuideLevel currentLevel = GuideLevel.Easy;

    [Header("UI")]
    public GameObject guidePanel;
    public TextMeshProUGUI taskIndexText;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI instructionText;
    public GameObject hintButton;

    [Header("가이드 마커 프리팹")]
    [Tooltip("GuideMarker.cs가 붙은 프리팹 (HighlightCircle + DescriptionText + Arrow)")]
    public GameObject guidePrefab;

    [Tooltip("스폰포인트 색 (하늘색)")]
    public Color spawnGuideColor = new Color(0.2f, 0.9f, 1f);
    [Tooltip("타겟 존 색 (황금색)")]
    public Color targetGuideColor = new Color(1f, 0.85f, 0.1f);

    [Header("고스트")]
    [Tooltip("반투명 홀로그램 머테리얼 (Rendering Mode: Transparent)")]
    public Material ghostMaterial;

    [Header("힌트 / 넛지")]
    public float hintDuration = 4f;
    public float nudgeIdleTime = 12f;

    [Header("아웃라인 설정 (EPOOutline)")]
    public Color outlineColor = new Color(0f, 1f, 1f);
    [Range(0, 10)]
    public float outlineWidth = 4f; // EPO의 Blur 혹은 Dilate 값으로 활용
    [Tooltip("EPOOutline의 RenderStyle 설정")]
    public RenderStyle renderStyle = RenderStyle.Single;

    // ── 내부 상태 ─────────────────────────────────────────
    private ScenarioData _scenarioData;
    private TaskState _currentTask;
    private int _totalTasks;
    private List<GameObject> _requiredItemObjects = new();
    private List<GameObject> _tempGuides = new();
    private GameObject _ghostObject;
    private GuideMarker _targetMarker;
    // targetObjName으로 찾은 ClickableAnimator 등 클릭 대상 GO
    private List<GameObject> _clickTargets = new();
    private float _idleTimer;
    private Coroutine _hintCoroutine;
    private bool _completed = false; // ★ 추가: Task 완료 상태

    // 스폰포인트 마커 추적: prefabId → 마커 GO
    private Dictionary<string, GameObject> _spawnMarkers = new();

    // ═══════════════════════════════════════════════════════
    // 생명주기
    // ═══════════════════════════════════════════════════════

    private void Awake()
    {
        if(Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        ScenarioStateReceiver.OnTaskStateUpdated += OnTaskUpdated;
        ScenarioStateReceiver.OnSnapshotReceived += OnSnapshotReceived;
        // 시나리오 최초 수신 시 데이터 확보
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
        guidePanel?.SetActive(false);
        hintButton?.SetActive(false);
    }

    private void Update()
    {
        if(currentLevel == GuideLevel.Hard) return;
        if(_ghostObject == null && _requiredItemObjects.Count == 0) return;
        if(guidePanel == null || !guidePanel.activeSelf) return;
        _idleTimer += Time.deltaTime;
        if(_idleTimer >= nudgeIdleTime) { _idleTimer = 0f; TriggerNudge(); }
    }

    // ═══════════════════════════════════════════════════════
    // 이벤트 수신
    // ═══════════════════════════════════════════════════════

    private void OnTaskUpdated(TaskStateBroadcast broadcast)
    {
        _currentTask = broadcast.currentTask;
        _totalTasks = broadcast.totalTaskCount;

        if(_currentTask.status == TaskStatus.Running)
        {
            _completed = false; // ★ Task 시작 시 완료 상태 초기화
            if(_scenarioData == null)
            {
                Debug.LogWarning("[GuideSystem] 시나리오 데이터가 없음");
            }
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
            _completed = false; // ★ Task 시작 시 완료 상태 초기화
            ShowGuideForCurrentTask();
        }
    }

    // ScenarioReceiver.OnScenarioReceived — 시나리오 최초 로드 시 데이터 확보
    private void OnScenarioReceived(ScenarioData data)
    {
        _scenarioData = data;
        Debug.Log($"[GuideSystem] ScenarioData 수신: {data?.scenarioId}");
        // TaskStateBroadcast가 이미 도착해서 ShowGuide를 대기 중이었으면 재시도
        if(_currentTask.status == TaskStatus.Running && !_completed)
            ShowGuideForCurrentTask();
    }

    // ═══════════════════════════════════════════════════════
    // 가이드 표시
    // ═══════════════════════════════════════════════════════

    private void ShowGuideForCurrentTask()
    {
        if(_scenarioData == null) return;
        int idx = _currentTask.taskIndex;
        var tasks = _scenarioData.scenario?.tasks;
        if(tasks == null || idx >= tasks.Count) return;

        var taskDef = tasks[idx];
        var config = _scenarioData.GetModuleConfig(taskDef.moduleId);

        RefreshUI(idx, taskDef.moduleId, config);
        ClearVisualGuides();

        _requiredItemObjects = FindRequiredItems(config);
        _idleTimer = 0f;

        if(currentLevel != GuideLevel.Hard)
        {
            // 1. 클릭 대상(targetObjName) 찾기 및 아웃라인
            _clickTargets = FindClickTargets(config);
            foreach(var t in _clickTargets)
            {
                SetOutline(t, true);

                // ★ 추가: 스폰 물체가 없는 단계(클릭 태스크)라면 targetObjName 위치에 마커 생성
                if(taskDef.spawnObjects == null || taskDef.spawnObjects.Count == 0)
                {
                    string label = GetExtraValue(config, "description", "이곳을 클릭하세요.");
                    SpawnMarker(t.transform.position, label, targetGuideColor, isTarget: true);
                    Debug.Log($"[Guide] 클릭 대상({t.name})에 직접 마커 생성");
                }
            }

            // 2. 기존 스폰 물체 가이드 로직
            foreach(var item in _requiredItemObjects) SetOutline(item, true);
            BuildSpawnGuides(taskDef);
            SpeakGuide(config);
        }

        hintButton?.SetActive(currentLevel == GuideLevel.Normal);
        guidePanel?.SetActive(true);
    }

    // ═══════════════════════════════════════════════════════
    // 마커 + 고스트 생성
    // ═══════════════════════════════════════════════════════

    private void BuildSpawnGuides(TaskDef taskDef)
    {
        if(taskDef.spawnObjects == null) return;

        var allSP = UnityEngine.Object.FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var spCache = new Dictionary<string, SpawnPoint>();
        foreach(var sp in allSP)
            if(!string.IsNullOrEmpty(sp.pointId)) spCache[sp.pointId] = sp;

        foreach(var bundle in taskDef.spawnObjects)
        {
            if(string.IsNullOrEmpty(bundle.prefabId)) continue;

            // showGuide = false면 가이드 생성 전체 스킵
            if(!bundle.showGuide) continue;

            // --- [STEP 1] 아이템 탐색 및 상태 확인 ---
            var existingItem = FindSpawnedItem(bundle.prefabId);
            bool isGrabbed = false;

            if(existingItem != null)
            {
                var sg = existingItem.GetComponent<SyncGrab>();
                isGrabbed = (sg != null && sg.IsGrabbed);
            }

            // --- [STEP 2] ① 스폰포인트 마커 (하늘색) 처리 ---
            // 플레이어가 도구를 이미 들고 있다면 스폰 마커(하늘색 원)를 생성하지 않음
            if(isGrabbed)
            {
                Debug.Log($"[Guide] {bundle.prefabId}를 이미 들고 있음. 스폰 가이드 생략 및 아웃라인 유지.");
                SetOutline(existingItem, true); // 들고 있는 물체 강조 유지
            }
            else if(!string.IsNullOrEmpty(bundle.spawnPointId))
            {
                if(spCache.TryGetValue(bundle.spawnPointId, out var spPt))
                {
                    // 아이템이 씬에 있지만 놓여진 상태면 그 위치에, 없으면 스폰포인트에 생성
                    Vector3 pos = (existingItem != null) ? GetBoundsCenter(existingItem) : spPt.transform.position;

                    // NoneHighlight 컴포넌트 체크 (특정 물체 가이드 제외 로직)
                    bool isNoneHighlight = existingItem != null && existingItem.GetComponent<NoneHighlight>() != null;

                    if(!isNoneHighlight)
                    {
                        string label = GetDisplayName(bundle.prefabId);
                        var markerGO = SpawnMarker(pos, label, spawnGuideColor, isTarget: false);
                        if(markerGO != null) _spawnMarkers[bundle.prefabId] = markerGO.gameObject;
                    }
                }
            }

            // --- [STEP 3] ② 타겟 존 마커 (황금색) + 고스트 + 볼트 아웃라인 처리 ---
            // ★ 중요: 위에서 continue를 하지 않으므로 볼트 아웃라인 로직이 항상 실행됨
            if(!string.IsNullOrEmpty(bundle.guideId))
            {
                var targetGO = FindSpawnedItem(bundle.guideId);

                if(targetGO != null)
                {
                    // 볼트 그룹 및 커넥터 여부 확인
                    var boltGroup = targetGO.GetComponent<BoltGroupCounter>();
                    bool isBoltTask = boltGroup != null || targetGO.name.Contains("Bolt");
                    bool isConnecterTask = targetGO.GetComponent<NoneHighlight>() != null;

                    bool shouldShowCircle = !isBoltTask && !isConnecterTask;

                    // 라벨 및 진행도 설정
                    string label;
                    if(isBoltTask && boltGroup != null)
                    {
                        string actionStr = boltGroup.assembleMode ? "체결" : "제거";
                        label = $"볼트를 임팩트 렌치로 {actionStr}하세요.";
                    }
                    else
                    {
                        label = $"{GetDisplayName(bundle.prefabId)}을(를) 여기로 이동하세요";
                    }

                    // 마커 생성
                    Vector3 markerPos = isBoltTask ? GetBoundsCenter(targetGO) : targetGO.transform.position;
                    var marker = SpawnMarker(markerPos, label, targetGuideColor, isTarget: true);

                    if(marker != null)
                    {
                        marker.Setup(label, targetGuideColor, shouldShowCircle);
                        _targetMarker = marker;

                        // 볼트 진행도 업데이트 코루틴 시작
                        if(isBoltTask && boltGroup != null)
                        {
                            StartCoroutine(UpdateBoltProgressRoutine(marker, boltGroup));
                        }
                    }

                    // 타겟 오브젝트(볼트들) 아웃라인 강제 적용
                    SetOutline(targetGO, true);

                    // 고스트 생성 (볼트 작업이 아닐 때만)
                    if(existingItem != null && ghostMaterial != null && !isBoltTask)
                    {
                        _ghostObject = SpawnGhost(existingItem, targetGO.transform.position);
                    }
                }
                else
                {
                    Debug.LogWarning($"[Guide] targetGuideId '{bundle.guideId}' 씬에 없음");
                }
            }
        }
    }

    /// <summary>
    /// 볼트 진행도를 실시간으로 업데이트
    /// </summary>
    private IEnumerator UpdateBoltProgressRoutine(GuideMarker marker, BoltGroupCounter boltGroup)
    {
        int lastProgress = -1;

        while(marker != null && boltGroup != null && !_completed)
        {
            int currentProgress = boltGroup.RemovedCount;
            int totalBolts = boltGroup.TotalCount;

            // 진행도가 변경되었을 때만 업데이트
            if(currentProgress != lastProgress)
            {
                string progressText = $"{totalBolts - currentProgress}/{totalBolts}";
                marker.UpdateProgressText(progressText);
                lastProgress = currentProgress;

                Debug.Log($"[Guide] 볼트 진행도 업데이트: {progressText}");
            }

            yield return new WaitForSeconds(0.1f);
        }

        Debug.Log($"[Guide] 볼트 진행도 업데이트 완료");
    }

    // ── 마커 스폰 ────────────────────────────────────────

    private GuideMarker SpawnMarker(Vector3 worldPos, string description,
                                    Color color, bool isTarget)
    {
        if(guidePrefab == null)
        {
            // 폴백
            var fallback = CreateFallbackMarker(worldPos, description, color);
            _tempGuides.Add(fallback);
            return null;
        }

        var go = Instantiate(guidePrefab, worldPos, Quaternion.identity);
        var marker = go.GetComponent<GuideMarker>();
        if(marker == null)
        {
            Debug.LogWarning("[Guide] guidePrefab에 GuideMarker 컴포넌트 없음");
            Destroy(go);
            var fallback = CreateFallbackMarker(worldPos, description, color);
            _tempGuides.Add(fallback);
            return null;
        }

        marker.Setup(description, color);
        _tempGuides.Add(go);
        return marker;
    }

    private GameObject CreateFallbackMarker(Vector3 worldPos, string description, Color color)
    {
        var root = new GameObject("GuideMarker_Fallback");
        root.transform.position = worldPos;
        var arrow = CreateDefaultArrow(worldPos + Vector3.up * 0.5f, color);
        arrow.transform.SetParent(root.transform, true);
        arrow.transform.DOLocalMoveY(0.25f, 0.7f).SetLoops(-1, LoopType.Yoyo).SetRelative();
        SpawnTextLabel(description, worldPos + Vector3.up * 1.1f, color, root.transform);
        return root;
    }

    // ═══════════════════════════════════════════════════════
    // 아이템 그랩 시 스폰포인트 마커 제거 (외부 호출)
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// SyncGrab.OnGrabbed 시 FreeLookController 또는 SyncGrab에서 호출.
    /// 해당 아이템의 스폰포인트 마커를 제거.
    /// </summary>
    public void OnItemGrabbed(string prefabId)
    {
        if(_spawnMarkers.TryGetValue(prefabId, out var markerGO))
        {
            if(markerGO != null)
            {
                _tempGuides.Remove(markerGO);
                Destroy(markerGO);
                Debug.Log($"[Guide] 스폰 마커 제거: {prefabId}");
            }
            _spawnMarkers.Remove(prefabId);
        }
    }

    // ═══════════════════════════════════════════════════════
    // 고스트
    // ═══════════════════════════════════════════════════════

    private GameObject SpawnGhost(GameObject sourceItem, Vector3 ghostPos)
    {
        var ghost = Instantiate(sourceItem, ghostPos, sourceItem.transform.rotation);
        ghost.name = "Ghost_" + sourceItem.name;
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

        ghost.transform.DOLocalMoveY(0.05f, 1.5f).SetLoops(-1, LoopType.Yoyo).SetRelative();
        foreach(var r in ghost.GetComponentsInChildren<Renderer>())
            r.material.DOFade(0.3f, 1.2f).SetLoops(-1, LoopType.Yoyo);

        return ghost;
    }

    // ═══════════════════════════════════════════════════════
    // 넛지
    // ═══════════════════════════════════════════════════════

    private void TriggerNudge()
    {
        if(_ghostObject != null)
            _ghostObject.transform.DOShakePosition(1.5f, 0.06f, 12, 90f, false, true);
        _targetMarker?.Nudge();
        if(currentLevel == GuideLevel.Easy)
        {
            var config = GetCurrentConfig();
            if(config != null) SpeakGuide(config);
        }
    }

    // ═══════════════════════════════════════════════════════
    // 힌트 버튼
    // ═══════════════════════════════════════════════════════

    public void ShowHint()
    {
        if(_hintCoroutine != null) StopCoroutine(_hintCoroutine);
        _hintCoroutine = StartCoroutine(ShowHintRoutine());
    }

    private IEnumerator ShowHintRoutine()
    {
        foreach(var item in _requiredItemObjects) SetOutline(item, true);
        string msg = _requiredItemObjects.Count > 0
            ? $"{_requiredItemObjects[0].name}을(를) 목표 위치로 가져가세요."
            : "목표 위치를 확인하세요.";
        TTSManager.Instance?.Speak(msg);
        yield return new WaitForSeconds(hintDuration);
        foreach(var item in _requiredItemObjects) SetOutline(item, false);
    }

    // ═══════════════════════════════════════════════════════
    // Task 완료
    // ═══════════════════════════════════════════════════════

    private void OnTaskCompleted()
    {
        _completed = true; // ★ Task 완료 상태 설정
        ClearVisualGuides();
        TTSManager.Instance?.Stop();
        if(currentLevel != GuideLevel.Hard)
            TTSManager.Instance?.Speak("잘 하셨습니다! 다음 단계로 넘어갑니다.");
        Debug.Log($"[Guide] Task 완료");
    }

    // ═══════════════════════════════════════════════════════
    // 외부 API
    // ═══════════════════════════════════════════════════════

    public void SetDifficulty(GuideLevel level)
    {
        currentLevel = level;
        hintButton?.SetActive(level == GuideLevel.Normal);
    }

    public void ResetIdleTimer() => _idleTimer = 0f;

    // ═══════════════════════════════════════════════════════
    // 내부 유틸
    // ═══════════════════════════════════════════════════════

    private void RefreshUI(int taskIdx, string moduleId, ModuleConfig config)
    {
        if(taskIndexText) taskIndexText.text = $"{taskIdx + 1} / {_totalTasks}";
        if(titleText) titleText.text = GetExtraValue(config, "title", moduleId);
        string desc = GetExtraValue(config, "description", "다음 작업을 수행하세요.");
        string guide = GetExtraValue(config, "guideText", "");
        if(instructionText)
            instructionText.text = (currentLevel == GuideLevel.Easy && !string.IsNullOrEmpty(guide))
                ? desc + "\n\n<size=85%><color=#AAFFAA>" + guide + "</color></size>"
                : desc;
    }

    private void SpeakGuide(ModuleConfig config)
    {
        string text = GetExtraValue(config, "description", "");
        if(!string.IsNullOrEmpty(text)) TTSManager.Instance?.Speak(text);
    }

    private void ClearVisualGuides()
    {
        foreach(var g in _tempGuides) if(g) Destroy(g);
        _tempGuides.Clear();
        _spawnMarkers.Clear();
        foreach(var item in _requiredItemObjects) SetOutline(item, false);
        _requiredItemObjects.Clear();
        foreach(var t in _clickTargets) SetOutline(t, false);
        _clickTargets.Clear();
        _ghostObject = null;
        _targetMarker = null;
        hintButton?.SetActive(false);
    }

    private void SetOutline(GameObject target, bool on)
    {
        if(target == null) return;

        // 1. 타겟 혹은 부모에 Outlinable 컴포넌트가 있는지 확인
        var outlinable = target.GetComponent<Outlinable>();

        if(on)
        {
            // 컴포넌트가 없으면 추가
            if(outlinable == null)
            {
                outlinable = target.AddComponent<Outlinable>();

                // 해당 오브젝트와 모든 자식의 Renderer를 아웃라인 대상으로 자동 등록
                outlinable.AddAllChildRenderersToRenderingList();
            }

            // 아웃라인 스타일 설정
            outlinable.RenderStyle = renderStyle;
            outlinable.enabled = true;

            // 색상 및 파라미터 적용 (Single 모드 기준)
            var properties = outlinable.OutlineParameters;
            properties.Color = outlineColor;
            properties.BlurShift = outlineWidth; // 선의 굵기 느낌 조절
            properties.Enabled = true;

            // 만약 FrontBack 모드라면 양쪽 다 적용
            if(renderStyle == RenderStyle.FrontBack)
            {
                outlinable.FrontParameters.Color = outlineColor;
                outlinable.FrontParameters.Enabled = true;
                outlinable.BackParameters.Color = outlineColor;
                outlinable.BackParameters.Enabled = true;
            }
        }
        else
        {
            // 끌 때는 컴포넌트만 비활성화 (나중에 재사용 가능하게 Destroy는 하지 않음)
            if(outlinable != null)
            {
                outlinable.enabled = false;
            }
        }
    }

    private void SpawnTextLabel(string text, Vector3 worldPos, Color color, Transform parent = null)
    {
        var go = new GameObject("Label");
        go.transform.position = worldPos;
        if(parent != null) go.transform.SetParent(parent, true);
        var tm = go.AddComponent<TextMesh>();
        tm.text = text; tm.fontSize = 28; tm.color = color;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.characterSize = 0.05f;
        go.AddComponent<GuideLabelBillboard>();
        if(parent == null) _tempGuides.Add(go);
    }

    private GameObject CreateDefaultArrow(Vector3 pos, Color color)
    {
        var go = new GameObject("Arrow");
        go.transform.position = pos;
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.SetPosition(0, Vector3.up * 0.3f); lr.SetPosition(1, Vector3.zero);
        lr.startWidth = lr.endWidth = 0.04f; lr.useWorldSpace = false;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = color;
        var head = new GameObject("Head");
        head.transform.SetParent(go.transform, false);
        var hlr = head.AddComponent<LineRenderer>();
        hlr.positionCount = 4;
        hlr.SetPosition(0, new Vector3(-0.08f, 0.08f, 0)); hlr.SetPosition(1, Vector3.zero);
        hlr.SetPosition(2, new Vector3(0.08f, 0.08f, 0)); hlr.SetPosition(3, new Vector3(-0.08f, 0.08f, 0));
        hlr.startWidth = hlr.endWidth = 0.03f; hlr.useWorldSpace = false;
        hlr.material = lr.material; hlr.startColor = hlr.endColor = color;
        return go;
    }

    /// <summary>아이템 GO의 Renderer 바운드 중앙 반환 (없으면 GO 위치)</summary>
    private Vector3 GetBoundsCenter(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if(renderers == null || renderers.Length == 0) return go.transform.position;
        var bounds = renderers[0].bounds;
        for(int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds.center;
    }

    /// <summary>
    /// targetObjName + targetZoneId로 씬에서 클릭 대상 GO 탐색.
    /// ClickableAnimator, InteractablePart 등 클릭으로 완료하는 오브젝트에 아웃라인 적용.
    /// </summary>
    private List<GameObject> FindClickTargets(ModuleConfig config)
    {
        var result = new List<GameObject>();
        if(config == null) return result;

        if(!string.IsNullOrEmpty(config.targetObjName))
        {
            var go = FindByNameOrClickableId(config.targetObjName);
            if(go != null)
            {
                Debug.Log($"[Guide] targetObjName 찾음: {go.name}");
                result.Add(go);
            }
            else
            {
                Debug.LogWarning($"[Guide] targetObjName 못 찾음: {config.targetObjName}");
            }
        }
        return result;
    }

    /// <summary>
    /// 이름으로 GO 탐색. ClickableAnimator.uniqueId와도 매칭.
    /// </summary>
    private GameObject FindByNameOrClickableId(string id)
    {
        // 1. 이름 직접 탐색
        var go = GameObject.Find(id);
        if(go != null) return go;

        // 2. ClickableAnimator.uniqueId로 탐색
        foreach(var ca in UnityEngine.Object.FindObjectsByType<ClickableAnimator>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if(ca.uniqueId == id) return ca.gameObject;
        }

        return null;
    }

    private List<GameObject> FindRequiredItems(ModuleConfig config)
    {
        var result = new List<GameObject>();
        if(config?.requiredItems == null) return result;
        foreach(var id in config.requiredItems)
        {
            if(string.IsNullOrEmpty(id)) continue;
            var go = FindSpawnedItem(id);
            if(go != null) result.Add(go);
        }
        return result;
    }

    private GameObject FindSpawnedItem(string prefabId)
    {
        // 1. 이름이 완전히 일치하거나 (Clone)인 것 찾기
        var go = GameObject.Find(prefabId);
        if(go != null) return go;

        go = GameObject.Find(prefabId + "(Clone)");
        if(go != null) return go;

        // 2. [추가] 프리팹 ID를 포함하는 모든 오브젝트 검색 (손에 들려있는 경우 대비)
        var allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach(var obj in allObjects)
        {
            if(obj.name.Contains(prefabId))
            {
                // SyncGrab이 붙어있고 이미 누군가 잡고 있다면 우선순위로 반환
                var sg = obj.GetComponent<SyncGrab>();
                if(sg != null && sg.IsGrabbed) return obj;

                return obj; // 일단 발견된 것 반환
            }
        }
        return null;
    }

    private ModuleConfig GetCurrentConfig()
    {
        if(_scenarioData == null) return null;
        var tasks = _scenarioData.scenario?.tasks;
        if(tasks == null || _currentTask.taskIndex >= tasks.Count) return null;
        return _scenarioData.GetModuleConfig(tasks[_currentTask.taskIndex].moduleId);
    }

    private string GetExtraValue(ModuleConfig config, string key, string fallback)
    {
        if(config?.extra == null) return fallback;
        foreach(var kv in config.extra) if(kv.key == key) return kv.GetLocalized();
        return fallback;
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
        "MultimeterKit" => "멀티미터",
        "Multimeter" => "멀티미터",
        "TwoPoleTester" => "이상 전압 테스터",
        "AirGun" => "에어건",
        "SealGasket_New" => "신품 가스켓",
        "TorqueWrench" => "토크 렌치",
        _ => id
    };
}

// ── Billboard ─────────────────────────────────────────────
public class GuideLabelBillboard : MonoBehaviour
{
    private Camera _cam;
    private void Start() => _cam = Camera.main;
    private void LateUpdate()
    {
        if(_cam == null) _cam = Camera.main;
        if(_cam == null) return;
        transform.LookAt(transform.position + _cam.transform.rotation * Vector3.forward,
                         _cam.transform.rotation * Vector3.up);
    }
}
