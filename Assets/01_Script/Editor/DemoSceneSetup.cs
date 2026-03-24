using System.Collections.Generic;
using Autohand;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DoDrill;

// ============================================================
//  DemoSceneSetup.cs  —  Editor 전용
//  메뉴: DoDrill > Demo > Setup Demo in Current Scene
//
//  생성 구조:
//    Demo
//    ├── Zones            Zone 구체 (TaskInteractionZone)
//    │    └── Guide_{id}  반투명 백색 구체 (SceneGuideRegistry 등록)
//    ├── Items            FBX / 큐브 + SyncGrab + TaskItem
//    ├── ScenarioManager  FishNet 컴포넌트 + DemoGuideActivator
//    └── UI               World-space Canvas
//
//  멀티플랫폼 가이드 흐름:
//    PC / 모바일 : 아이템 클릭 → SyncGrab.OnPCClick()
//                → DOTween 비행 → holdPoint(=Zone Guide 위치)
//    VR          : 아이템 잡고 Zone 근처로 → TaskInteractionZone 감지
//    가이드 표시  : DemoGuideActivator → SceneGuideRegistry.Show(zoneId)
// ============================================================

public static class DemoSceneSetup
{
    private const string FontPath = "Assets/05_Font/Pretendard-Medium SDF.asset";
    private const string FbxRoot  = "Assets/03_3D/Object/O_FBX/";

    // ── 생성 도중 guide 오브젝트 임시 보관 ────────────────────
    private static readonly Dictionary<string, GameObject> _guideObjects = new();

    // ── Zone 정의 ──────────────────────────────────────────────
    private static readonly (string id, Vector3 pos, Color color)[] Zones =
    {
        ("PatientEar",          new Vector3(0.10f, 1.70f, 1.0f), new Color(1f,   0.4f, 0.4f)),
        ("PatientRadialArtery", new Vector3(0.55f, 0.90f, 0.0f), new Color(1f,   0.7f, 0.3f)),
        ("PatientChest",        new Vector3(0.00f, 1.30f, 1.0f), new Color(0.3f, 0.8f, 1f  )),
        ("PatientUpperArm",     new Vector3(0.60f, 1.10f, 0.5f), new Color(0.5f, 1f,   0.5f)),
        ("PatientFinger",       new Vector3(0.50f, 0.85f, 0.2f), new Color(1f,   1f,   0.3f)),
        ("PatientMouth",        new Vector3(0.00f, 1.60f, 1.0f), new Color(1f,   0.5f, 0.8f)),
        ("InjectionSite",       new Vector3(0.65f, 1.05f, 0.8f), new Color(0.8f, 0.5f, 1f  )),
        ("IVPort",              new Vector3(0.60f, 1.20f, 1.2f), new Color(0.4f, 1f,   0.8f)),
    };

    // ── Item 정의 ─────────────────────────────────────────────
    // (prefabId, fbxName(null=큐브), scale, fallbackColor, primaryZoneId(null=없음))
    private static readonly (string id, string fbx, float scale, Color color, string zone)[] Items =
    {
        // 손 위생
        ("HandSanitizer",           "SM_HandSanitizer",     0.08f, new Color(0.2f,0.8f,0.2f), null                  ),
        // 활력징후
        ("TympanicThermometer",     "SM_Thermometer",       0.08f, new Color(1f,  0.4f,0.4f), "PatientEar"          ),
        ("ThermometerCap",          null,                   0.06f, new Color(1f,  0.6f,0.6f), "PatientEar"          ),
        ("SecondHandWatch",         "SM_StopWatch",         0.08f, new Color(0.8f,0.8f,0.2f), "PatientRadialArtery" ),
        ("Sphygmomanometer",        "SM_sphygmomanotmeter", 0.10f, new Color(0.9f,0.5f,0.2f), "PatientUpperArm"     ),
        ("Stethoscope",             "SM_stethoscope",       0.10f, new Color(0.3f,0.6f,1f  ), "PatientUpperArm"     ),
        // 혈당·산소포화도
        ("BSTMachine",              "SM_BST",               0.08f, new Color(0.7f,0.3f,0.7f), "PatientFinger"       ),
        ("Lancet",                  "SM_blood needle",      0.08f, new Color(1f,  0.3f,0.3f), "PatientFinger"       ),
        ("LancetDevice",            "SM_phlebotomist",      0.08f, new Color(0.9f,0.4f,0.4f), "PatientFinger"       ),
        ("TestStrip",               "SM_TestStrip",         0.08f, new Color(1f,  1f,  0.4f), "PatientFinger"       ),
        ("AlcoholSwab",             "SM_AlcoholSwap",       0.08f, new Color(0.9f,0.9f,0.9f), "InjectionSite"       ),
        ("PulseOximeter",           null,                   0.06f, new Color(0.3f,1f,  0.5f), "PatientFinger"       ),
        // EKG
        ("Electrode",               "SM_ElectrodePatch",    0.08f, new Color(0.5f,0.5f,1f  ), "PatientChest"        ),
        // 경구·설하 투약
        ("MedicationCard",          null,                   0.06f, new Color(1f,  0.9f,0.5f), null                  ),
        ("Medication_Nitroglycerin","SM_HyperTet",          0.08f, new Color(1f,  0.6f,0.2f), "PatientMouth"        ),
        ("WaterCup",                "SM_Cup",               0.08f, new Color(0.6f,0.8f,1f  ), "PatientMouth"        ),
        ("Medication_Bearse",       "SM_Bottle",            0.08f, new Color(0.8f,0.5f,0.9f), "PatientMouth"        ),
        // IM 주사
        ("SterileSyringe",          "SM_Syringe_3ml",       0.08f, new Color(0.7f,0.9f,1f  ), "InjectionSite"       ),
        ("Medication_Ketocin",      "SM_Bottle",            0.08f, new Color(1f,  0.5f,0.7f), "InjectionSite"       ),
        ("DistilledWater",          "SM_DistilledWater",    0.08f, new Color(0.5f,0.8f,1f  ), "InjectionSite"       ),
        // 저혈당
        ("IVFluid_10DW",            "SM_Saline",            0.10f, new Color(0.3f,0.7f,1f  ), "IVPort"              ),
        ("MedicationTray",          "SM_Dset_Tray",         0.10f, new Color(0.7f,0.7f,0.7f), null                  ),
    };

    // ──────────────────────────────────────────────────────────
    [MenuItem("DoDrill/Demo/Setup Demo in Current Scene", priority = 100)]
    public static void SetupDemo()
    {
        var existing = GameObject.Find("Demo");
        if (existing != null)
        {
            bool replace = EditorUtility.DisplayDialog("Demo 재설정",
                "이미 'Demo' 오브젝트가 있습니다. 삭제하고 다시 만드시겠습니까?",
                "재생성", "취소");
            if (!replace) return;
            Undo.DestroyObjectImmediate(existing);
        }

        // NursingLab 존재 여부 확인 (Zones 재사용)
        var nursingLab = GameObject.Find("[NursingLab]");
        if (nursingLab == null)
        {
            EditorUtility.DisplayDialog("NursingLab 없음",
                "[NursingLab]이 씬에 없습니다.\n\n" +
                "먼저 DoDrill > Game > Setup NursingLab in Current Scene 을 실행하세요.",
                "확인");
            return;
        }

        // NursingLab의 Guide 오브젝트 수집
        _guideObjects.Clear();
        foreach (var zone in Zones)
        {
            var guideGO = GameObject.Find($"Guide_{zone.id}");
            if (guideGO != null) _guideObjects[zone.id] = guideGO;
        }

        var demo = CreateGO("Demo", null, Vector3.zero);

        // NursingLab Zones를 재사용하므로 Zones 생성 생략
        // 생성 순서:
        // 1. Items (holdPoint → NursingLab Guide 연결)
        // 2. ScenarioManager
        // 3. UI
        SetupItems(demo);
        SetupScenarioManager(demo);
        SetupUI(demo);

        EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log("[DemoSceneSetup] Demo 설정 완료!");
        EditorUtility.DisplayDialog("완료",
            "Demo 설정 완료!\n\n" +
            "▶ Play → FishNet 호스트 자동 시작 → 시나리오 로드\n\n" +
            "[PC/모바일] 아이템 클릭 → 해당 Zone 가이드로 비행\n" +
            "[VR]        아이템 잡고 Zone 구체에 가져다 대면 완료\n" +
            "[완료 확인] Confirm 타입 태스크 완료\n" +
            "[강제 완료] 즉시 완료 (디버그)",
            "확인");

        _guideObjects.Clear();
    }

    // ── 1. Zones 설정 ─────────────────────────────────────────
    private static void SetupZones(GameObject parent)
    {
        var patientRoot = new Vector3(0f, 0f, 2f);
        var zonesRoot   = CreateGO("Zones", parent, patientRoot);

        // 환자 실루엣
        var patientVisual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        patientVisual.name = "Patient_Visual";
        patientVisual.transform.SetParent(zonesRoot.transform);
        patientVisual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        patientVisual.transform.localScale    = new Vector3(0.4f, 0.9f, 0.4f);
        SetTransparent(patientVisual, new Color(0.8f, 0.7f, 0.6f, 0.25f));
        DestroyCollider(patientVisual);

        foreach (var (id, localPos, color) in Zones)
        {
            // ─ Zone 구체 (트리거) ──────────────────
            var zoneGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            zoneGO.name = $"Zone_{id}";
            zoneGO.transform.SetParent(zonesRoot.transform);
            zoneGO.transform.localPosition = localPos;
            zoneGO.transform.localScale    = Vector3.one * 0.18f;
            SetTransparent(zoneGO, new Color(color.r, color.g, color.b, 0.30f));

            var col = zoneGO.GetComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius    = 1.0f;

            var zone = zoneGO.AddComponent<TaskInteractionZone>();
            zone.zoneId        = id;
            zone.startDisabled = false;

            AddLabel(zoneGO, id, 0.22f);
            Undo.RegisterCreatedObjectUndo(zoneGO, "Create Zone");

            // ─ Guide 구체 (활성 태스크에서만 표시) ──
            var guideGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            guideGO.name = $"Guide_{id}";
            guideGO.transform.SetParent(zoneGO.transform, false);
            guideGO.transform.localPosition = Vector3.zero;
            guideGO.transform.localScale    = Vector3.one * 1.35f;  // zone보다 약간 크게
            // 흰색 글로우 효과 (zone 색 위에 반투명 흰색 겹침)
            SetTransparent(guideGO, new Color(1f, 1f, 1f, 0.55f));
            DestroyCollider(guideGO);
            guideGO.SetActive(false);  // 기본 비활성 — DemoGuideActivator가 제어

            _guideObjects[id] = guideGO;
            Undo.RegisterCreatedObjectUndo(guideGO, "Create Guide");
        }

        Debug.Log($"[DemoSceneSetup] Zone {Zones.Length}개 + Guide {Zones.Length}개 생성");
    }

    // ── 2. Items 설정 ─────────────────────────────────────────
    private static void SetupItems(GameObject parent)
    {
        var shelfOrigin = new Vector3(-4f, 1.05f, -3f);
        var itemsRoot   = CreateGO("Items", parent, shelfOrigin);

        const float spacing = 0.7f;
        const int   perRow  = 11;

        int fbxCount = 0, cubeCount = 0;

        for (int i = 0; i < Items.Length; i++)
        {
            var (id, fbxName, scale, fallback, primaryZone) = Items[i];

            int row = i / perRow;
            int col = i % perRow;
            var localPos = new Vector3(col * spacing, -row * spacing, 0f);

            // ─ 메쉬 생성 (FBX 우선, 없으면 큐브) ──
            GameObject itemGO = null;

            if (!string.IsNullOrEmpty(fbxName))
            {
                var fbxPath  = $"{FbxRoot}{fbxName}.fbx";
                var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);

                if (fbxAsset != null)
                {
                    itemGO = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
                    itemGO.name = $"Item_{id}";
                    itemGO.transform.SetParent(itemsRoot.transform, false);
                    itemGO.transform.localPosition = localPos;
                    itemGO.transform.localScale    = Vector3.one;
                    fbxCount++;
                }
                else
                {
                    Debug.LogWarning($"[DemoSceneSetup] FBX 없음: {fbxPath} — 큐브 대체");
                }
            }

            if (itemGO == null)
            {
                itemGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
                itemGO.name = $"Item_{id}";
                itemGO.transform.SetParent(itemsRoot.transform, false);
                itemGO.transform.localPosition = localPos;
                itemGO.transform.localScale    = Vector3.one * 0.12f;
                SetOpaque(itemGO, fallback);
                cubeCount++;
            }

            // ─ MeshCollider 적용 (기존 Collider 전부 제거 후 교체) ──
            foreach (var existingCol in itemGO.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(existingCol);

            var meshFilters = itemGO.GetComponentsInChildren<MeshFilter>();
            if (meshFilters.Length > 0)
            {
                foreach (var mf in meshFilters)
                    if (mf.sharedMesh != null)
                    {
                        var mc = mf.gameObject.AddComponent<MeshCollider>();
                        mc.convex = true;
                    }
            }
            else
            {
                // 큐브 폴백 — BoxCollider 유지
                itemGO.AddComponent<BoxCollider>();
            }

            // ─ Rigidbody (Kinematic — SyncGrab이 제어) ──
            var rb = itemGO.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity  = false;

            // ─ Grabbable (AutoHand — VR 그랩용) ────────
            // ※ VR 그랩은 AutoHand 레이어 설정 필요
            //   PC/모바일은 SyncGrab.OnPCClick()으로 동작
            itemGO.AddComponent<Grabbable>();

            // ─ NetworkObject (FishNet 씬 오브젝트) ──────
            itemGO.AddComponent<NetworkObject>();

            // ─ SyncGrab (클릭→비행 / VR 동기화) ─────────
            var syncGrab = itemGO.AddComponent<SyncGrab>();

            // holdPoint → 해당 아이템의 주 Zone Guide로 연결
            if (!string.IsNullOrEmpty(primaryZone) &&
                _guideObjects.TryGetValue(primaryZone, out var guideGO))
            {
                var soSg = new SerializedObject(syncGrab);
                SetProp(soSg, "holdPoint", guideGO.transform);
                soSg.ApplyModifiedProperties();
            }

            // ─ TaskItem ───────────────────────────────
            var taskItem = itemGO.AddComponent<TaskItem>();
            taskItem.prefabId = id;

            // ─ 레이블 ────────────────────────────────
            AddLabel(itemGO, id, 0.18f, Vector3.up * 0.3f);

            Undo.RegisterCreatedObjectUndo(itemGO, "Create Item");
        }

        Debug.Log($"[DemoSceneSetup] Item {Items.Length}개 생성 (FBX:{fbxCount} / 큐브:{cubeCount})");
    }

    // ── 3. ScenarioManager 설정 ───────────────────────────────
    private static void SetupScenarioManager(GameObject parent)
    {
        var mgr = CreateGO("ScenarioManager", parent, Vector3.zero);

        // FishNet 시나리오 컴포넌트
        var bootstrapper   = mgr.AddComponent<ScenarioBootstrapper>();
        var runner         = mgr.AddComponent<ScenarioRunner>();
        var distributor    = mgr.AddComponent<ScenarioDistributor>();
        var networkStarter = mgr.AddComponent<DemoNetworkStarter>();
        var stateReceiver  = mgr.AddComponent<ScenarioStateReceiver>();

        // ScenarioRunner ← Bootstrapper
        {
            var so = new SerializedObject(runner);
            SetProp(so, "_bootstrapper", bootstrapper);
            so.ApplyModifiedProperties();
        }
        // Distributor ← Runner
        {
            var so = new SerializedObject(distributor);
            SetProp(so, "_scenarioRunner", runner);
            so.ApplyModifiedProperties();
        }
        // NetworkStarter ← Distributor
        {
            var so = new SerializedObject(networkStarter);
            SetProp(so, "_distributor", distributor);
            so.ApplyModifiedProperties();
        }

        // ─ SceneGuideRegistry — NursingLab 것을 찾아 재사용 ──
        var registry = Object.FindFirstObjectByType<SceneGuideRegistry>();
        if (registry == null)
        {
            Debug.LogWarning("[DemoSceneSetup] SceneGuideRegistry 없음 — NursingLab을 먼저 Setup하세요.");
        }

        // ─ ScenarioSpawner (Task 시작/종료 시 FishNet Spawn/Despawn) ──
        var spawner   = mgr.AddComponent<ScenarioSpawner>();
        var soSpawner = new SerializedObject(spawner);
        SetProp(soSpawner, "_guideRegistry", registry);
        soSpawner.ApplyModifiedProperties();

        Debug.Log("[DemoSceneSetup] ScenarioManager 생성 완료 (FishNet + Guide + ScenarioSpawner)\n" +
                  "※ ScenarioSpawner Inspector의 [프리팹 자동 등록] 버튼으로 프리팹을 등록하세요.");
    }

    // ── 4. UI 설정 ────────────────────────────────────────────
    // CNV_TaskList 가 씬에 있으면:
    //   ① TaskListPanel._taskItemPrefab 연결 (태스크 목록 표시)
    //   ② 디버그 버튼 전용 소형 패널만 생성
    // CNV_TaskList 가 없으면:
    //   기존 방식으로 전체 UI 직접 생성 (폴백)
    private static void SetupUI(GameObject parent)
    {
        var mgrGO         = GameObject.Find("ScenarioManager");
        var runner        = mgrGO != null ? mgrGO.GetComponent<ScenarioRunner>()        : null;
        var stateReceiver = mgrGO != null ? mgrGO.GetComponent<ScenarioStateReceiver>() : null;

        // ── CNV_TaskList 사용 경로 ─────────────────────────────
        var cnvTaskList = GameObject.Find("CNV_TaskList");
        if (cnvTaskList != null)
        {
            // TaskListPanel._taskItemPrefab 연결
            var taskListPanel = cnvTaskList.GetComponentInChildren<TaskListPanel>(true);
            if (taskListPanel != null)
            {
                var taskItemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/02_UI/02_Prefabs/Preb_TaskItem.prefab");

                var soPanel = new SerializedObject(taskListPanel);
                SetProp(soPanel, "_taskItemPrefab", taskItemPrefab);
                soPanel.ApplyModifiedProperties();

                Debug.Log("[DemoSceneSetup] CNV_TaskList 발견 — TaskListPanel._taskItemPrefab 연결 완료");
            }
            else
            {
                Debug.LogWarning("[DemoSceneSetup] CNV_TaskList 안에 TaskListPanel 없음 — 확인 필요");
            }

            // 디버그 버튼 전용 소형 패널 생성
            SetupDebugButtonsOnly(parent, runner, stateReceiver);
            return;
        }

        // ── 폴백: CNV_TaskList 없으면 전체 UI 직접 생성 ──────
        Debug.LogWarning("[DemoSceneSetup] CNV_TaskList 없음 — 전체 UI 직접 생성");
        SetupFullUI(parent, runner, stateReceiver);
    }

    // 디버그 버튼 전용 소형 패널 (CNV_TaskList 사용 시)
    private static void SetupDebugButtonsOnly(GameObject parent,
        ScenarioRunner runner, ScenarioStateReceiver stateReceiver)
    {
        var uiRoot   = CreateGO("UI_Debug", parent, new Vector3(0f, 1.0f, 0.8f));
        var canvasGO = CreateGO("Canvas_Debug", uiRoot, Vector3.zero);

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var cr = canvasGO.GetComponent<RectTransform>();
        cr.sizeDelta  = new Vector2(480, 110);
        cr.localScale = Vector3.one * 0.002f;

        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        // 배경
        var panel     = CreateGO("Panel", canvasGO, Vector3.zero);
        var panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        panel.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.1f, 0.88f);

        // 힌트 텍스트 (1줄)
        var txtHint = MakeText(panel, "Txt_Hint", new Vector2(0, 28), new Vector2(480, 30), 13, "힌트");

        // 버튼 행 1: 완료확인 / 강제완료 / 강제실패
        var btnConfirm   = MakeButton(panel, "Btn_Confirm",      new Vector2(-150, -15), new Vector2(140, 40), "완료 확인", new Color(0.2f,0.7f,0.2f));
        var btnForceOK   = MakeButton(panel, "Btn_ForceComplete", new Vector2(  10, -15), new Vector2(120, 40), "강제 완료", new Color(0.5f,0.5f,0.9f));
        var btnForceFail = MakeButton(panel, "Btn_ForceFail",     new Vector2( 145, -15), new Vector2(105, 40), "강제 실패", new Color(0.8f,0.3f,0.3f));

        // 버튼 행 2: ◀ 이전 / 다음 ▶
        var btnPrev = MakeButton(panel, "Btn_Prev", new Vector2(-200, -58), new Vector2(75, 35), "◀ 이전", new Color(0.4f,0.4f,0.4f));
        var btnNext = MakeButton(panel, "Btn_Next", new Vector2(-115, -58), new Vector2(75, 35), "다음 ▶", new Color(0.4f,0.4f,0.4f));

        // DemoTaskUI (버튼 + 힌트만 — 텍스트 표시는 CNV_TaskList가 담당)
        var ui = canvasGO.AddComponent<DemoTaskUI>();
        var so = new SerializedObject(ui);
        SetProp(so, "_runner",              runner);
        SetProp(so, "_stateReceiver",       stateReceiver);
        SetProp(so, "_typeHintText",        txtHint);       // 힌트만
        SetProp(so, "_confirmButton",       btnConfirm);
        SetProp(so, "_forceCompleteButton", btnForceOK);
        SetProp(so, "_forceFailButton",     btnForceFail);
        SetProp(so, "_prevButton",          btnPrev);
        SetProp(so, "_nextButton",          btnNext);
        // _taskIndexText / _moduleIdText / _statusText 는 CNV_TaskList가 표시 → 연결 생략
        so.ApplyModifiedProperties();

        Debug.Log("[DemoSceneSetup] 디버그 버튼 패널 생성 완료 (CNV_TaskList 연동)");
    }

    // 전체 UI 직접 생성 (CNV_TaskList 없을 때 폴백)
    private static void SetupFullUI(GameObject parent,
        ScenarioRunner runner, ScenarioStateReceiver stateReceiver)
    {
        var uiRoot   = CreateGO("UI", parent, new Vector3(0f, 1.4f, 0.8f));
        var canvasGO = CreateGO("Canvas_Demo", uiRoot, Vector3.zero);

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var cr = canvasGO.GetComponent<RectTransform>();
        cr.sizeDelta  = new Vector2(500, 340);
        cr.localScale = Vector3.one * 0.002f;

        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        var panel     = CreateGO("Panel", canvasGO, Vector3.zero);
        var panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        panel.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.1f, 0.92f);

        var txtIndex  = MakeText(panel, "Txt_Index",  new Vector2(0, 140), new Vector2(500, 40), 22, "0 / 35");
        var txtModule = MakeText(panel, "Txt_Module", new Vector2(0,  95), new Vector2(500, 40), 20, "ModuleId");
        var txtStatus = MakeText(panel, "Txt_Status", new Vector2(0,  55), new Vector2(500, 35), 18, "대기중");
        var txtHint   = MakeText(panel, "Txt_Hint",   new Vector2(0,  15), new Vector2(490, 40), 14, "힌트");

        var btnConfirm   = MakeButton(panel, "Btn_Confirm",       new Vector2(-180,-50), new Vector2(150,45), "완료 확인", new Color(0.2f,0.7f,0.2f));
        var btnForceOK   = MakeButton(panel, "Btn_ForceComplete",  new Vector2( -10,-50), new Vector2(130,45), "강제 완료", new Color(0.5f,0.5f,0.9f));
        var btnForceFail = MakeButton(panel, "Btn_ForceFail",      new Vector2( 130,-50), new Vector2(110,45), "강제 실패", new Color(0.8f,0.3f,0.3f));
        var btnPrev      = MakeButton(panel, "Btn_Prev",           new Vector2(-210,-110),new Vector2( 80,40), "◀ 이전",   new Color(0.4f,0.4f,0.4f));
        var btnNext      = MakeButton(panel, "Btn_Next",           new Vector2(-120,-110),new Vector2( 80,40), "다음 ▶",   new Color(0.4f,0.4f,0.4f));

        var ui = canvasGO.AddComponent<DemoTaskUI>();
        var so = new SerializedObject(ui);
        SetProp(so, "_runner",              runner);
        SetProp(so, "_stateReceiver",       stateReceiver);
        SetProp(so, "_taskIndexText",       txtIndex);
        SetProp(so, "_moduleIdText",        txtModule);
        SetProp(so, "_statusText",          txtStatus);
        SetProp(so, "_typeHintText",        txtHint);
        SetProp(so, "_confirmButton",       btnConfirm);
        SetProp(so, "_forceCompleteButton", btnForceOK);
        SetProp(so, "_forceFailButton",     btnForceFail);
        SetProp(so, "_prevButton",          btnPrev);
        SetProp(so, "_nextButton",          btnNext);
        so.ApplyModifiedProperties();

        Debug.Log("[DemoSceneSetup] 전체 UI 생성 완료 (폴백)");
    }

    // ── 유틸 ─────────────────────────────────────────────────

    private static GameObject CreateGO(string name, GameObject parent, Vector3 localPos)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
        return go;
    }

    private static void SetOpaque(GameObject go, Color color)
    {
        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null) return;
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetFloat("_Surface", 0);
        mat.color = color;
        mr.sharedMaterial = mat;
    }

    private static void SetTransparent(GameObject go, Color color)
    {
        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null) return;
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetFloat("_Surface",   1);
        mat.SetFloat("_Blend",     0);
        mat.SetFloat("_AlphaClip", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite",   0);
        mat.renderQueue = 3000;
        mat.color = color;
        mr.sharedMaterial = mat;
    }

    private static void DestroyCollider(GameObject go)
    {
        var col = go.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);
    }

    private static TMP_FontAsset LoadFont()
        => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

    private static void AddLabel(GameObject go, string text, float size, Vector3 offset = default)
    {
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(go.transform, false);
        labelGO.transform.localPosition = offset == default ? Vector3.up * 0.7f : offset;
        labelGO.transform.localScale    = Vector3.one * size;
        var tmp = labelGO.AddComponent<TextMeshPro>();
        tmp.text         = text;
        tmp.fontSize     = 4;
        tmp.alignment    = TextAlignmentOptions.Center;
        tmp.color        = Color.white;
        tmp.outlineWidth = 0.2f;
        tmp.outlineColor = Color.black;
        var font = LoadFont();
        if (font != null) tmp.font = font;
    }

    private static TMP_Text MakeText(GameObject parent, string name, Vector2 anchoredPos,
                                      Vector2 size, float fontSize, string defaultText)
    {
        var go   = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var rect = go.AddComponent<RectTransform>();
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta        = size;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = defaultText;
        tmp.fontSize  = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = Color.white;
        var font = LoadFont();
        if (font != null) tmp.font = font;
        return tmp;
    }

    private static Button MakeButton(GameObject parent, string name, Vector2 anchoredPos,
                                      Vector2 size, string label, Color color)
    {
        var go   = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var rect = go.AddComponent<RectTransform>();
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta        = size;
        go.AddComponent<Image>().color = color;
        var btn = go.AddComponent<Button>();

        var txtGO   = new GameObject("Label");
        txtGO.transform.SetParent(go.transform, false);
        var txtRect = txtGO.AddComponent<RectTransform>();
        txtRect.anchorMin = Vector2.zero;
        txtRect.anchorMax = Vector2.one;
        txtRect.offsetMin = Vector2.zero;
        txtRect.offsetMax = Vector2.zero;
        var tmp = txtGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = 14;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = Color.white;
        var font = LoadFont();
        if (font != null) tmp.font = font;

        return btn;
    }

    private static void SetProp(SerializedObject so, string propName, Object value)
    {
        var prop = so.FindProperty(propName);
        if (prop != null) prop.objectReferenceValue = value;
        else Debug.LogWarning($"[DemoSceneSetup] 프로퍼티 없음: {propName}");
    }
}
