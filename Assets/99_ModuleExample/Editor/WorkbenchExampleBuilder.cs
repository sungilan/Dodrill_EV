using DoDrill.Training;
using FishNet.Managing;
using FishNet.Managing.Transporting;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// ============================================================
//  WorkbenchExampleBuilder.cs — 범용 모듈 예제 씬 만들기 (2026-11-20)
//
//  메뉴: Tools ▸ DoDrill ▸ 범용 모듈 예제 씬 만들기 (렌치→작업대→버튼)
//  콘텐츠 코드 0줄 예제: 씬에 모듈 부품만 붙이고 시나리오는 JSON(example_workbench.json)으로만 정의한다.
//   · 엔진: ScenarioBootstrapper, ScenarioRunner, ScenarioAutoStarter, ScenarioReceiver, ScenarioStateReceiver, NetworkInteractionBridge
//   · 인터랙션: TaskItem(렌치), TaskInteractionZone(작업대 자리), TaskClickTarget(버튼)
//   · 아바타: SimplePcInteractor(카메라)    · UI: SimpleTaskHud
//  지금 열린 씬은 건드리지 않는다 — 새 씬을 추가로 만들어 저장한 뒤 닫는다. 빌드 씬 목록에는 넣지 않는다.
// ============================================================
public static class WorkbenchExampleBuilder
{
    private const string Root = "Assets/99_ModuleExample";
    private const string ScenePath = Root + "/Example_Workbench.unity";
    private const string JsonPath = Root + "/Scenarios/example_workbench.json";
    private const string MatDir = Root + "/Materials";

    [MenuItem("Tools/DoDrill/범용 모듈 예제 씬 만들기 (렌치→작업대→버튼)")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("범용 모듈 예제", "플레이 중에는 만들 수 없습니다. 플레이를 멈춘 뒤 다시 실행하세요.", "확인");
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
            !EditorUtility.DisplayDialog("범용 모듈 예제", "예제 씬이 이미 있습니다. 새로 만들어 덮어쓸까요?", "덮어쓰기", "취소"))
            return;

        var matFloor = Mat("Floor", new Color(0.55f, 0.57f, 0.6f));
        var matWood  = Mat("Table", new Color(0.55f, 0.38f, 0.22f));
        var matSteel = Mat("Steel", new Color(0.75f, 0.77f, 0.8f));
        var matRed   = Mat("Button", new Color(0.85f, 0.12f, 0.1f));
        var matDark  = Mat("Pedestal", new Color(0.2f, 0.2f, 0.22f));
        var matZone  = Mat("Zone", new Color(0.1f, 1f, 0.3f, 0.35f), transparent: true);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            GameObject Add(GameObject go) { SceneManager.MoveGameObjectToScene(go, scene); return go; }

            // 카메라(최소 PC 조작부) · 조명 · 바닥
            var cam = Add(new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)));
            cam.tag = "MainCamera";
            cam.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, -2.2f), Quaternion.Euler(20f, 0f, 0f));
            cam.GetComponent<Camera>().nearClipPlane = 0.05f;
            cam.AddComponent<SimplePcInteractor>();

            var light = Add(new GameObject("Directional Light", typeof(Light)));
            light.GetComponent<Light>().type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            Box("Floor", null, new Vector3(0f, -0.05f, 0f), new Vector3(10f, 0.1f, 10f), matFloor, scene);

            // 왼쪽 테이블 + 렌치(작업 아이템)
            var table = Box("Table", null, new Vector3(-1.2f, 0.375f, 0f), new Vector3(0.9f, 0.75f, 0.6f), matWood, scene);
            var wrench = new GameObject("Wrench");
            Add(wrench);
            wrench.transform.position = new Vector3(-1.2f, 0.77f, 0f);
            Box("Handle", wrench.transform, new Vector3(-1.2f, 0.77f, 0f), new Vector3(0.22f, 0.02f, 0.03f), matSteel, scene, keepCollider: false);
            Box("Head", wrench.transform, new Vector3(-1.08f, 0.77f, 0f), new Vector3(0.05f, 0.025f, 0.07f), matSteel, scene, keepCollider: false);
            var wc = wrench.AddComponent<BoxCollider>();
            wc.center = new Vector3(0.01f, 0f, 0f);
            wc.size = new Vector3(0.3f, 0.06f, 0.1f);   // 클릭하기 쉽게 조금 크게
            wrench.AddComponent<TaskItem>().prefabId = "Wrench";

            // 가운데 작업대 + 놓는 자리(작업 구역)
            Box("Workbench", null, new Vector3(0f, 0.45f, 0.4f), new Vector3(1.2f, 0.9f, 0.6f), matWood, scene);
            var zone = GameObject.CreatePrimitive(PrimitiveType.Cube);
            zone.name = "Zone_Bench";
            Add(zone);
            zone.transform.position = new Vector3(0f, 0.915f, 0.4f);   // 놓은 렌치가 이 높이로 끼워진다(스냅)
            zone.transform.localScale = new Vector3(0.4f, 0.03f, 0.3f);
            zone.GetComponent<MeshRenderer>().sharedMaterial = matZone;
            zone.GetComponent<Collider>().isTrigger = true;
            zone.AddComponent<TaskInteractionZone>().zoneId = "Zone_Bench";   // 단계가 시작되면 켜진다(startDisabled)

            // 오른쪽 받침대 + 시작 버튼(클릭 대상)
            Box("Pedestal", null, new Vector3(1.2f, 0.5f, 0f), new Vector3(0.3f, 1.0f, 0.3f), matDark, scene);
            var btn = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            btn.name = "StartButton";
            Add(btn);
            btn.transform.position = new Vector3(1.2f, 1.02f, 0f);
            btn.transform.localScale = new Vector3(0.12f, 0.02f, 0.12f);
            btn.GetComponent<MeshRenderer>().sharedMaterial = matRed;
            var ct = btn.AddComponent<TaskClickTarget>();
            ct.targetId = "StartButton";
            ct.displayName = "시작 버튼";

            // FishNet 네트워크 매니저 (호스트)
            var net = Add(new GameObject("NetworkManager"));
            var nm = net.AddComponent<NetworkManager>();
            var tm = net.AddComponent<TransportManager>();
            var tug = net.AddComponent<Tugboat>();
            var tmSo = new SerializedObject(tm);
            var tp = tmSo.FindProperty("Transport");
            if (tp != null) { tp.objectReferenceValue = tug; tmSo.ApplyModifiedPropertiesWithoutUndo(); }
            var nmSo = new SerializedObject(nm);
            var sp = nmSo.FindProperty("_spawnablePrefabs");
            if (sp != null) { sp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Object>(Root + "/ExampleSpawnablePrefabs.asset"); nmSo.ApplyModifiedPropertiesWithoutUndo(); }

            // 시나리오 엔진 (서버) + 수신기 (클라이언트)
            var sc = Add(new GameObject("Scenario"));
            var boot = sc.AddComponent<ScenarioBootstrapper>();
            var runner = sc.AddComponent<ScenarioRunner>();
            var rso = new SerializedObject(runner);
            rso.FindProperty("_bootstrapper").objectReferenceValue = boot;
            rso.ApplyModifiedPropertiesWithoutUndo();
            sc.AddComponent<ScenarioReceiver>();
            sc.AddComponent<ScenarioStateReceiver>();
            var starter = sc.AddComponent<ScenarioAutoStarter>();
            var sso = new SerializedObject(starter);
            sso.FindProperty("runner").objectReferenceValue = runner;
            sso.FindProperty("scenarioJson").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
            sso.ApplyModifiedPropertiesWithoutUndo();

            // 클라이언트 → 서버 신호 전달
            var bridge = Add(new GameObject("InteractionBridge"));
            bridge.AddComponent<NetworkObject>();
            bridge.AddComponent<NetworkInteractionBridge>();

            // 화면 표시
            Add(new GameObject("Hud", typeof(SimpleTaskHud)));

            EditorSceneManager.SaveScene(scene, ScenePath);
            // 씬 네트워크 오브젝트(브리지)의 SceneId 는 저장된 씬 경로로 만든다 — 저장 뒤 다시 만들고 한 번 더 저장
            AssignSceneIds(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Example] 범용 모듈 예제 씬 생성: {ScenePath} — 열어서 Play 하세요");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
    }

    private static void AssignSceneIds(Scene scene)
    {
        // FishNet 의 NetworkObject.CreateSceneId(Scene, bool, out int) 는 internal — 메뉴(Fish-Networking ▸ Reserialize)와 같은 동작
        var m = typeof(NetworkObject).GetMethod("CreateSceneId",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
            null, new[] { typeof(Scene), typeof(bool), typeof(int).MakeByRefType() }, null);
        if (m == null) { Debug.LogWarning("[Example] FishNet SceneId 생성 함수를 찾지 못했습니다 — Fish-Networking 메뉴의 Reserialize 로 씬을 다시 저장하세요"); return; }
        var args = new object[] { scene, true, 0 };
        m.Invoke(null, args);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, Scene scene, bool keepCollider = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        if (parent == null) SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.position = pos;
        go.transform.localScale = size;
        if (parent != null) go.transform.SetParent(parent, true);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    private static Material Mat(string name, Color color, bool transparent = false)
    {
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(Root, "Materials");
        string path = $"{MatDir}/Example_{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        m = new Material(shader);
        m.SetColor("_BaseColor", color);
        m.color = color;
        if (transparent)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
