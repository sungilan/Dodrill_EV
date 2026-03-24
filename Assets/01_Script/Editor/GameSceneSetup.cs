using System.Collections.Generic;
using Autohand;
using DoDrill;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ============================================================
//  GameSceneSetup.cs  —  Editor 전용
//  메뉴: DoDrill > Game > Setup NursingLab in Current Scene
//
//  생성 구조:
//    [NursingLab]
//    ├── Environment      가구 FBX (침대·책상·트레이) + 레이블
//    ├── SpawnPoints      색상 마커 + 한국어 레이블
//    │   ├── SP_Desk      🟡 책상    — 기본 아이템
//    │   ├── SP_Tray      🟠 처치 트레이 — 주사·투약·EKG
//    │   ├── SP_IVStand   🔵 IV 스탠드
//    │   └── SP_Wall      🟢 벽      — 손 소독제
//    ├── Zones            Zone 구체 + Guide 구체 (비활성)
//    └── Items            SpawnPoint별 그룹 + FBX/큐브
//
//  기존 씬의 ScenarioManager를 탐색하여
//  ScenarioStateReceiver / SceneGuideRegistry / ScenarioSpawner 추가
//  (기존 ScenarioBootstrapper · ScenarioRunner · ScenarioDistributor · ScenarioAutoLoader 유지)
// ============================================================

public static class GameSceneSetup
{
    private const string FontPath    = "Assets/05_Font/Pretendard-Medium SDF.asset";
    private const string FbxItemRoot = "Assets/03_3D/Object/O_FBX/";
    private const string FbxEnvRoot  = "Assets/03_3D/Environment/E_FBX/EF_Asset/";

    // Guide 오브젝트 임시 보관 (Zones → Items holdPoint 연결용)
    private static readonly Dictionary<string, GameObject> _guideObjects = new();
    // SpawnPoint Transform 임시 보관 (Items 배치용)
    private static readonly Dictionary<string, Transform>  _spawnPoints  = new();

    // ── Zone 정의 ─────────────────────────────────────────────
    private static readonly (string id, Vector3 localPos, Color color)[] Zones =
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

    // ── SpawnPoint 정의 ────────────────────────────────────────
    // (id, 월드 위치, 마커 색, 한국어 레이블)
    private static readonly (string id, Vector3 worldPos, Color color, string label)[] SpawnPointDefs =
    {
        ("SP_Desk",    new Vector3(-4.0f, 1.05f, -3.0f), new Color(1f,   0.9f, 0.2f), "책상\n기본 아이템"),
        ("SP_Tray",    new Vector3( 2.0f, 1.05f,  1.0f), new Color(1f,   0.5f, 0.1f), "처치 트레이\n주사·투약·EKG"),
        ("SP_IVStand", new Vector3( 2.0f, 1.50f,  4.0f), new Color(0.3f, 0.7f, 1f  ), "IV 스탠드\n수액"),
        ("SP_Wall",    new Vector3(-5.0f, 1.30f,  0.0f), new Color(0.3f, 1f,   0.5f), "벽\n손 소독제"),
    };

    // ── 환경 가구 정의 ─────────────────────────────────────────
    // (fbxName, 월드 위치, 스케일, 한국어 레이블, 환경/오브젝트 폴더 여부)
    private static readonly (string fbx, Vector3 pos, Vector3 scale, string label, bool isEnvFbx)[] Furniture =
    {
        ("SM_HospitalBed",  new Vector3( 0.0f, 0f,    2.0f), Vector3.one,         "환자 침대",  true ),
        ("SM_SmallTable",   new Vector3(-4.0f, 0f,   -3.0f), Vector3.one,         "책상",       true ),
        ("SM_Dset_Tray",    new Vector3( 2.0f, 0.9f,  1.0f), Vector3.one * 0.1f, "처치 트레이",false),
    };

    // ── Item 정의 ─────────────────────────────────────────────
    // (prefabId, fbxName(null=큐브), scale, fallbackColor, primaryZoneId, spawnPointId)
    private static readonly (string id, string fbx, float scale, Color color, string zone, string spawnPoint)[] Items =
    {
        // ── 책상 (SP_Desk) ─────────────────────────────────
        ("TympanicThermometer",     "SM_Thermometer",       0.08f, new Color(1f,   0.4f, 0.4f), "PatientEar",          "SP_Desk"    ),
        ("ThermometerCap",          null,                   0.06f, new Color(1f,   0.6f, 0.6f), "PatientEar",          "SP_Desk"    ),
        ("SecondHandWatch",         "SM_StopWatch",         0.08f, new Color(0.8f, 0.8f, 0.2f), "PatientRadialArtery", "SP_Desk"    ),
        ("Sphygmomanometer",        "SM_sphygmomanotmeter", 0.10f, new Color(0.9f, 0.5f, 0.2f), "PatientUpperArm",     "SP_Desk"    ),
        ("Stethoscope",             "SM_stethoscope",       0.10f, new Color(0.3f, 0.6f, 1f  ), "PatientUpperArm",     "SP_Desk"    ),
        ("PulseOximeter",           null,                   0.06f, new Color(0.3f, 1f,   0.5f), "PatientFinger",       "SP_Desk"    ),
        ("MedicationCard",          null,                   0.06f, new Color(1f,   0.9f, 0.5f), null,                  "SP_Desk"    ),

        // ── 처치 트레이 (SP_Tray) ──────────────────────────
        ("BSTMachine",              "SM_BST",               0.08f, new Color(0.7f, 0.3f, 0.7f), "PatientFinger",       "SP_Tray"    ),
        ("LancetDevice",            "SM_phlebotomist",      0.08f, new Color(0.9f, 0.4f, 0.4f), "PatientFinger",       "SP_Tray"    ),
        ("Lancet",                  "SM_blood needle",      0.08f, new Color(1f,   0.3f, 0.3f), "PatientFinger",       "SP_Tray"    ),
        ("TestStrip",               "SM_TestStrip",         0.08f, new Color(1f,   1f,   0.4f), "PatientFinger",       "SP_Tray"    ),
        ("AlcoholSwab",             "SM_AlcoholSwap",       0.08f, new Color(0.9f, 0.9f, 0.9f), "InjectionSite",       "SP_Tray"    ),
        ("Electrode",               "SM_ElectrodePatch",    0.08f, new Color(0.5f, 0.5f, 1f  ), "PatientChest",        "SP_Tray"    ),
        ("Medication_Nitroglycerin","SM_HyperTet",          0.08f, new Color(1f,   0.6f, 0.2f), "PatientMouth",        "SP_Tray"    ),
        ("WaterCup",                "SM_Cup",               0.08f, new Color(0.6f, 0.8f, 1f  ), "PatientMouth",        "SP_Tray"    ),
        ("Medication_Bearse",       "SM_Bottle",            0.08f, new Color(0.8f, 0.5f, 0.9f), "PatientMouth",        "SP_Tray"    ),
        ("SterileSyringe",          "SM_Syringe_3ml",       0.08f, new Color(0.7f, 0.9f, 1f  ), "InjectionSite",       "SP_Tray"    ),
        ("Medication_Ketocin",      "SM_Bottle",            0.08f, new Color(1f,   0.5f, 0.7f), "InjectionSite",       "SP_Tray"    ),
        ("DistilledWater",          "SM_DistilledWater",    0.08f, new Color(0.5f, 0.8f, 1f  ), "InjectionSite",       "SP_Tray"    ),
        ("MedicationTray",          "SM_Dset_Tray",         0.10f, new Color(0.7f, 0.7f, 0.7f), null,                  "SP_Tray"    ),

        // ── IV 스탠드 (SP_IVStand) ─────────────────────────
        ("IVFluid_10DW",            "SM_Saline",            0.10f, new Color(0.3f, 0.7f, 1f  ), "IVPort",              "SP_IVStand" ),

        // ── 벽 (SP_Wall) ───────────────────────────────────
        ("HandSanitizer",           "SM_HandSanitizer",     0.08f, new Color(0.2f, 0.8f, 0.2f), null,                  "SP_Wall"    ),
    };

    // ──────────────────────────────────────────────────────────
    [MenuItem("DoDrill/Game/Setup NursingLab in Current Scene", priority = 50)]
    public static void SetupGameScene()
    {
        var existing = GameObject.Find("[NursingLab]");
        if (existing != null)
        {
            bool replace = EditorUtility.DisplayDialog("[NursingLab] 재설정",
                "이미 '[NursingLab]' 오브젝트가 있습니다.\n삭제하고 다시 만드시겠습니까?",
                "재생성", "취소");
            if (!replace) return;
            Undo.DestroyObjectImmediate(existing);
        }

        _guideObjects.Clear();
        _spawnPoints.Clear();

        var root = CreateGO("[NursingLab]", null, Vector3.zero);

        // 생성 순서 중요:
        // 1. Environment — 가구 배치
        // 2. SpawnPoints — 위치 마커 (_spawnPoints 채움)
        // 3. Zones       — Guide 오브젝트 생성 (_guideObjects 채움)
        // 4. Items       — SpawnPoint 위치에 배치, holdPoint → Guide 연결
        // 5. Scenario    — 기존 ScenarioManager에 컴포넌트 추가
        SetupEnvironment(root);
        SetupSpawnPoints(root);
        SetupZones(root);
        SetupItems(root);
        SetupScenarioComponents();

        EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        _guideObjects.Clear();
        _spawnPoints.Clear();

        Debug.Log("[GameSceneSetup] NursingLab 설정 완료!");
        EditorUtility.DisplayDialog("완료",
            "[NursingLab] 설정 완료!\n\n" +
            "🟡 SP_Desk    — 책상 (기본 아이템)\n" +
            "🟠 SP_Tray    — 처치 트레이 (주사·투약·EKG)\n" +
            "🔵 SP_IVStand — IV 스탠드 (수액)\n" +
            "🟢 SP_Wall    — 벽 (손 소독제)\n\n" +
            "▶ 런타임 시 태스크에 맞는 아이템만 자동 표시됩니다.",
            "확인");
    }

    // ── 1. 환경/가구 ──────────────────────────────────────────
    private static void SetupEnvironment(GameObject root)
    {
        var envRoot = CreateGO("Environment", root, Vector3.zero);

        foreach (var (fbxName, pos, scale, label, isEnv) in Furniture)
        {
            string fbxPath = isEnv
                ? $"{FbxEnvRoot}{fbxName}.fbx"
                : $"{FbxItemRoot}{fbxName}.fbx";

            var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            GameObject go;

            if (fbxAsset != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
                go.name = fbxName;
            }
            else
            {
                Debug.LogWarning($"[GameSceneSetup] 가구 FBX 없음: {fbxPath} — 큐브 대체");
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"{fbxName}_placeholder";
                SetOpaque(go, new Color(0.6f, 0.5f, 0.4f));
            }

            go.transform.SetParent(envRoot.transform, false);
            go.transform.position   = pos;
            go.transform.localScale = scale;
            AddLabel(go, label, 0.15f, Vector3.up * 1.5f);
            Undo.RegisterCreatedObjectUndo(go, $"Create Furniture {fbxName}");
        }

        Debug.Log($"[GameSceneSetup] 환경 가구 {Furniture.Length}개 배치");
    }

    // ── 2. SpawnPoint 마커 ─────────────────────────────────────
    private static void SetupSpawnPoints(GameObject root)
    {
        var spRoot = CreateGO("SpawnPoints", root, Vector3.zero);

        foreach (var (id, worldPos, color, label) in SpawnPointDefs)
        {
            var spGO = CreateGO(id, spRoot, Vector3.zero);
            spGO.transform.position = worldPos;

            // SpawnPoint 컴포넌트
            var sp = spGO.AddComponent<SpawnPoint>();
            sp.pointId = id;

            // 색상 구체 마커 (시각적 식별용)
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Marker";
            marker.transform.SetParent(spGO.transform, false);
            marker.transform.localScale = Vector3.one * 0.1f;
            SetTransparent(marker, new Color(color.r, color.g, color.b, 0.75f));
            DestroyCollider(marker);

            // 한국어 레이블
            AddLabel(spGO, label, 0.12f, Vector3.up * 0.25f);

            _spawnPoints[id] = spGO.transform;
            Undo.RegisterCreatedObjectUndo(spGO, $"Create SpawnPoint {id}");
        }

        Debug.Log($"[GameSceneSetup] SpawnPoint {SpawnPointDefs.Length}개 생성");
    }

    // ── 3. Zones 설정 ─────────────────────────────────────────
    private static void SetupZones(GameObject root)
    {
        var patientRoot = new Vector3(0f, 0f, 2f);
        var zonesRoot   = CreateGO("Zones", root, patientRoot);

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
            // Zone 구체 (트리거)
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

            AddLabel(zoneGO, id, 0.12f);
            Undo.RegisterCreatedObjectUndo(zoneGO, $"Create Zone {id}");

            // Guide 구체 (기본 비활성 — ScenarioSpawner가 제어)
            var guideGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            guideGO.name = $"Guide_{id}";
            guideGO.transform.SetParent(zoneGO.transform, false);
            guideGO.transform.localPosition = Vector3.zero;
            guideGO.transform.localScale    = Vector3.one * 1.35f;
            SetTransparent(guideGO, new Color(1f, 1f, 1f, 0.55f));
            DestroyCollider(guideGO);
            guideGO.SetActive(false);

            _guideObjects[id] = guideGO;
            Undo.RegisterCreatedObjectUndo(guideGO, $"Create Guide {id}");
        }

        Debug.Log($"[GameSceneSetup] Zone {Zones.Length}개 + Guide {Zones.Length}개 생성");
    }

    // ── 4. Items 설정 ─────────────────────────────────────────
    private static void SetupItems(GameObject root)
    {
        var itemsRoot = CreateGO("Items", root, Vector3.zero);

        // SpawnPoint별 아이템 그룹화 및 배치
        var groupRoots = new Dictionary<string, (GameObject go, int count)>();
        foreach (var sp in SpawnPointDefs)
            groupRoots[sp.id] = (CreateGO($"Items_{sp.id}", itemsRoot, Vector3.zero), 0);

        const float spacing = 0.20f;
        int fbxCount = 0, cubeCount = 0;

        for (int i = 0; i < Items.Length; i++)
        {
            var (id, fbxName, scale, fallback, primaryZone, spawnPointId) = Items[i];

            // SpawnPoint 기준으로 아이템 오프셋 계산
            var (groupGO, colIdx) = groupRoots[spawnPointId];
            var spawnTf = _spawnPoints.TryGetValue(spawnPointId, out var spt) ? spt : null;
            var basePos = spawnTf != null ? spawnTf.position : Vector3.zero;
            var itemWorldPos = basePos + new Vector3(colIdx * spacing, 0f, 0f);
            groupRoots[spawnPointId] = (groupGO, colIdx + 1);

            // ─ 메쉬 생성 (FBX 우선, 없으면 큐브) ──────────
            GameObject itemGO = null;

            if (!string.IsNullOrEmpty(fbxName))
            {
                var fbxPath  = $"{FbxItemRoot}{fbxName}.fbx";
                var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);

                if (fbxAsset != null)
                {
                    itemGO = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
                    itemGO.name = $"Item_{id}";
                    itemGO.transform.SetParent(groupGO.transform, false);
                    itemGO.transform.position   = itemWorldPos;
                    itemGO.transform.localScale = Vector3.one * scale;
                    fbxCount++;
                }
                else
                {
                    Debug.LogWarning($"[GameSceneSetup] FBX 없음: {fbxPath} — 큐브 대체");
                }
            }

            if (itemGO == null)
            {
                itemGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
                itemGO.name = $"Item_{id}";
                itemGO.transform.SetParent(groupGO.transform, false);
                itemGO.transform.position   = itemWorldPos;
                itemGO.transform.localScale = Vector3.one * 0.12f;
                SetOpaque(itemGO, fallback);
                cubeCount++;
            }

            // ─ MeshCollider 적용 (기존 Collider 전부 제거 후 교체) ──
            foreach (var col in itemGO.GetComponentsInChildren<Collider>())
                Object.DestroyImmediate(col);

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
                itemGO.AddComponent<BoxCollider>();
            }

            // ─ Rigidbody (Kinematic — SyncGrab 제어) ───────
            var rb = itemGO.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity  = false;

            // ─ Grabbable (VR 그랩) ──────────────────────────
            itemGO.AddComponent<Grabbable>();

            // ─ NetworkObject (FishNet 씬 오브젝트) ───────────
            itemGO.AddComponent<NetworkObject>();

            // ─ SyncGrab — holdPoint → Zone Guide 연결 ────────
            var syncGrab = itemGO.AddComponent<SyncGrab>();
            if (!string.IsNullOrEmpty(primaryZone) &&
                _guideObjects.TryGetValue(primaryZone, out var guideGO))
            {
                var soSg = new SerializedObject(syncGrab);
                var hp   = soSg.FindProperty("holdPoint");
                if (hp != null) hp.objectReferenceValue = guideGO.transform;
                soSg.ApplyModifiedProperties();
            }

            // ─ TaskItem ────────────────────────────────────
            var taskItem = itemGO.AddComponent<TaskItem>();
            taskItem.prefabId = id;

            // ─ 레이블 ──────────────────────────────────────
            AddLabel(itemGO, id, 0.08f, Vector3.up * 0.15f);

            Undo.RegisterCreatedObjectUndo(itemGO, $"Create Item {id}");
        }

        Debug.Log($"[GameSceneSetup] Item {Items.Length}개 생성 (FBX:{fbxCount} / 큐브:{cubeCount})");
    }

    // ── 5. 기존 ScenarioManager에 컴포넌트 추가 ───────────────
    private static void SetupScenarioComponents()
    {
        // 기존 ScenarioManager 탐색 (없으면 씬 루트에 생성)
        var mgrGO = GameObject.Find("ScenarioManager");
        if (mgrGO == null)
        {
            Debug.LogWarning("[GameSceneSetup] ScenarioManager 없음 — 새로 생성. " +
                             "기존 ScenarioBootstrapper·ScenarioRunner·ScenarioDistributor·ScenarioAutoLoader를 수동으로 연결하세요.");
            mgrGO = new GameObject("ScenarioManager");
            Undo.RegisterCreatedObjectUndo(mgrGO, "Create ScenarioManager");
        }

        // ─ ScenarioStateReceiver (없으면 추가) ─────────────
        var stateReceiver = mgrGO.GetComponent<ScenarioStateReceiver>()
                         ?? mgrGO.AddComponent<ScenarioStateReceiver>();

        // ─ SceneGuideRegistry (없으면 추가 + Guide 등록) ───
        var registry  = mgrGO.GetComponent<SceneGuideRegistry>()
                     ?? mgrGO.AddComponent<SceneGuideRegistry>();
        var soReg     = new SerializedObject(registry);
        var guidesArr = soReg.FindProperty("_guides");
        if (guidesArr != null)
        {
            guidesArr.arraySize = 0;
            foreach (var zone in Zones)
            {
                if (!_guideObjects.TryGetValue(zone.id, out var guideGO)) continue;
                guidesArr.InsertArrayElementAtIndex(guidesArr.arraySize);
                var elem = guidesArr.GetArrayElementAtIndex(guidesArr.arraySize - 1);
                elem.FindPropertyRelative("guideId").stringValue              = zone.id;
                elem.FindPropertyRelative("guideObject").objectReferenceValue = guideGO;
            }
            soReg.ApplyModifiedProperties();
        }

        // ─ ScenarioSpawner (없으면 추가, Task 시작/종료 시 FishNet Spawn/Despawn) ───
        var spawner = mgrGO.GetComponent<ScenarioSpawner>()
                   ?? mgrGO.AddComponent<ScenarioSpawner>();
        var soSpawner = new SerializedObject(spawner);

        // _guideRegistry 연결
        SetProp(soSpawner, "_guideRegistry", registry);
        soSpawner.ApplyModifiedProperties();

        Debug.Log("[GameSceneSetup] ScenarioManager 컴포넌트 설정 완료 " +
                  "(ScenarioStateReceiver + SceneGuideRegistry + ScenarioSpawner)\n" +
                  "※ ScenarioSpawner Inspector의 [프리팹 자동 등록] 버튼으로 프리팹을 등록하세요.");
    }

    // ── 유틸 ──────────────────────────────────────────────────

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

    private static void SetProp(SerializedObject so, string propName, Object value)
    {
        var prop = so.FindProperty(propName);
        if (prop != null) prop.objectReferenceValue = value;
        else Debug.LogWarning($"[GameSceneSetup] 프로퍼티 없음: {propName}");
    }
}
