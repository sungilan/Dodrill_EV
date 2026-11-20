using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// ============================================================
//  MultiplayerExampleBuilder.cs — 멀티플레이 노코드 예제 씬 만들기 (모듈화, 2026-11-20)
//
//  메뉴: Tools ▸ DoDrill ▸ 멀티플레이 예제 씬 만들기 (범용 프리팹)
//  콘텐츠 코드 0줄. 씬은 범용 프리팹 인스턴스(NetworkManagerHost, ScenarioEngine, SimpleTaskHud, SpawnPoint,
//  TaskZone, TaskButton)와 바닥·테이블뿐이고, 렌치는 NetworkItem 의 변형(Variant)이다.
//  호스트로 시작해 접속한 사람마다 GenericPlayer 가 생기고, 렌치(SyncGrab)는 서버 권한으로 동기화된다.
//  지금 열린 씬은 건드리지 않는다. 빌드 씬 목록에는 넣지 않는다.
// ============================================================
public static class MultiplayerExampleBuilder
{
    private const string Root = "Assets/99_ModuleExample";
    private const string ScenePath = Root + "/Example_Multiplayer.unity";
    private const string JsonPath = Root + "/Scenarios/example_multiplayer.json";
    private const string WrenchPath = Root + "/Prefabs/Item_Wrench.prefab";
    private const string ListPath = Root + "/ExampleMultiplayerPrefabs.asset";
    private const string MatDir = Root + "/Materials";

    [MenuItem("Tools/DoDrill/멀티플레이 예제 씬 만들기 (범용 프리팹)")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("멀티플레이 예제", "플레이 중에는 만들 수 없습니다. 플레이를 멈춘 뒤 다시 실행하세요.", "확인");
            return;
        }
        // 범용 프리팹이 없으면 먼저 만든다
        if (AssetDatabase.LoadAssetAtPath<GameObject>(GenericPrefabBuilder.PlayerPath) == null ||
            AssetDatabase.LoadAssetAtPath<GameObject>(GenericPrefabBuilder.EnginePath) == null)
            GenericPrefabBuilder.BuildAll();

        if (!Application.isBatchMode && AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
            !EditorUtility.DisplayDialog("멀티플레이 예제", "예제 씬이 이미 있습니다. 새로 만들어 덮어쓸까요?", "덮어쓰기", "취소"))
            return;

        var matFloor = GenericPrefabBuilder.Mat(MatDir, "Example_Floor", new Color(0.55f, 0.57f, 0.6f));
        var matWood = GenericPrefabBuilder.Mat(MatDir, "Example_Table", new Color(0.55f, 0.38f, 0.22f));
        var matDark = GenericPrefabBuilder.Mat(MatDir, "Example_Pedestal", new Color(0.2f, 0.2f, 0.22f));
        var matSteel = GenericPrefabBuilder.Mat(MatDir, "Example_Steel", new Color(0.75f, 0.77f, 0.8f));

        var wrench = EnsureWrenchPrefab(matSteel);
        var list = EnsureSpawnableList(wrench);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            T Add<T>(T obj) where T : Object
            {
                var go = obj is GameObject g ? g : ((Component)(object)obj).gameObject;
                SceneManager.MoveGameObjectToScene(go, scene);
                return obj;
            }
            GameObject Inst(string path, string name, Vector3 pos)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
                go.name = name;
                go.transform.position = pos;
                return go;
            }

            var light = Add(new GameObject("Directional Light", typeof(Light)));
            light.GetComponent<Light>().type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // 위치 동기화 기준 — 태그 Anchor 오브젝트가 있으면 3초 기다리지 않고 바로 동기화한다(LocalNetworkTransform)
            var anchor = Add(new GameObject("Anchor"));
            anchor.tag = "Anchor";

            Box("Floor", new Vector3(0f, -0.05f, 0f), new Vector3(12f, 0.1f, 12f), matFloor, scene);
            Box("Table", new Vector3(-1.2f, 0.375f, 0f), new Vector3(0.9f, 0.75f, 0.6f), matWood, scene);
            Box("Workbench", new Vector3(0f, 0.45f, 0.4f), new Vector3(1.2f, 0.9f, 0.6f), matWood, scene);
            Box("Pedestal", new Vector3(1.2f, 0.5f, 0f), new Vector3(0.3f, 1.0f, 0.3f), matDark, scene);

            // 범용 프리팹 인스턴스
            var sp = Inst(GenericPrefabBuilder.SpawnPointPath, "SpawnPoint_Table", new Vector3(-1.2f, 0.7f, 0f));
            sp.GetComponent<SpawnPoint>().pointId = "SpawnPoint_Table";

            var zone = Inst(GenericPrefabBuilder.ZonePath, "Zone_Bench", new Vector3(0f, 0.915f, 0.4f));
            zone.GetComponent<TaskInteractionZone>().zoneId = "Zone_Bench";

            var btn = Inst(GenericPrefabBuilder.ButtonPath, "StartButton", new Vector3(1.2f, 1.02f, 0f));
            var ct = btn.GetComponent<TaskClickTarget>();
            ct.targetId = "StartButton";
            ct.displayName = "시작 버튼";

            Inst(GenericPrefabBuilder.HudPath, "Hud", Vector3.zero);

            var net = Inst(GenericPrefabBuilder.NetMgrPath, "NetworkManager", Vector3.zero);
            var nmSo = new SerializedObject(net.GetComponent<NetworkManager>());
            nmSo.FindProperty("_spawnablePrefabs").objectReferenceValue = list;     // 플레이어 + 렌치
            nmSo.ApplyModifiedPropertiesWithoutUndo();

            var engine = Inst(GenericPrefabBuilder.EnginePath, "ScenarioEngine", Vector3.zero);
            var spawner = engine.GetComponentInChildren<ScenarioSpawner>(true);
            var spSo = new SerializedObject(spawner);
            var reg = spSo.FindProperty("_prefabRegistry");
            reg.arraySize = 1;
            var e0 = reg.GetArrayElementAtIndex(0);
            e0.FindPropertyRelative("prefabId").stringValue = "Wrench";
            e0.FindPropertyRelative("prefab").objectReferenceValue = wrench.GetComponent<NetworkObject>();
            spSo.ApplyModifiedPropertiesWithoutUndo();
            var starter = engine.GetComponent<ScenarioAutoStarter>();
            var stSo = new SerializedObject(starter);
            stSo.FindProperty("scenarioJson").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
            stSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            // 씬에 놓인 NetworkObject(ScenarioEngine)의 SceneId — 저장된 씬 경로로 만든 뒤 한 번 더 저장
            AssignSceneIds(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Example] 멀티플레이 예제 씬 생성: {ScenePath} — 열어서 Play 하세요");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
    }

    /// <summary>NetworkItem 의 변형(Variant): prefabId "Wrench", 렌치 모양.</summary>
    private static GameObject EnsureWrenchPrefab(Material steel)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(WrenchPath);
        if (existing != null) return existing;
        if (!AssetDatabase.IsValidFolder(Root + "/Prefabs")) AssetDatabase.CreateFolder(Root, "Prefabs");

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(GenericPrefabBuilder.ItemPath));
        inst.name = "Item_Wrench";
        inst.GetComponent<TaskItem>().prefabId = "Wrench";
        var col = inst.GetComponent<BoxCollider>();
        col.size = new Vector3(0.3f, 0.06f, 0.1f);                       // 클릭하기 쉽게 모델보다 조금 크게
        col.center = new Vector3(0.01f, 0f, 0f);
        Object.DestroyImmediate(inst.transform.Find("Model").gameObject);   // 큐브 대신 렌치 모양
        MakePart("Handle", inst.transform, new Vector3(0f, 0f, 0f), new Vector3(0.22f, 0.02f, 0.03f), steel);
        MakePart("Head", inst.transform, new Vector3(0.12f, 0f, 0f), new Vector3(0.05f, 0.025f, 0.07f), steel);
        var path = AssetDatabase.GenerateUniqueAssetPath(WrenchPath);
        var saved = PrefabUtility.SaveAsPrefabAsset(inst, path);
        Object.DestroyImmediate(inst);
        return saved;
    }

    private static void MakePart(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        Object.DestroyImmediate(go.GetComponent<Collider>());
    }

    private static SinglePrefabObjects EnsureSpawnableList(GameObject wrench)
    {
        var list = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(ListPath);
        if (list == null)
        {
            list = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            AssetDatabase.CreateAsset(list, ListPath);
        }
        list.Clear();
        foreach (var go in new[] { AssetDatabase.LoadAssetAtPath<GameObject>(GenericPrefabBuilder.PlayerPath), wrench })
        {
            var nob = go != null ? go.GetComponent<NetworkObject>() : null;
            if (nob != null) list.AddObject(nob, true, false);
        }
        EditorUtility.SetDirty(list);
        return list;
    }

    private static void AssignSceneIds(Scene scene)
    {
        var m = typeof(NetworkObject).GetMethod("CreateSceneId",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
            null, new[] { typeof(Scene), typeof(bool), typeof(int).MakeByRefType() }, null);
        if (m == null) { Debug.LogWarning("[Example] FishNet SceneId 생성 함수를 찾지 못했습니다 — Fish-Networking 메뉴의 Reserialize 로 씬을 다시 저장하세요"); return; }
        m.Invoke(null, new object[] { scene, true, 0 });
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static GameObject Box(string name, Vector3 pos, Vector3 size, Material mat, Scene scene)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.position = pos;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }
}
