using FishNet.Managing;
using FishNet.Managing.Transporting;
using FishNet.Transporting.Tugboat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// ============================================================
//  ExampleSceneBuilder.cs — 모듈 최소 예제 씬 만들기 (모듈화 적용 가이드, 2026-11-20)
//
//  메뉴: Tools ▸ DoDrill ▸ 모듈 최소 예제 씬 만들기
//  지금 열린 씬은 건드리지 않는다 — 새 씬을 '추가로' 만들어 저장한 뒤 바로 닫는다.
//  만든 씬(Assets/99_ModuleExample/Example_MinimalScenario.unity)을 열고 Play 하면
//  호스트 시작 → '잠시 대기'(3초) → '스페이스 키 누르기' → 시나리오 종료 순서로 로그가 화면에 뜬다.
//  빌드 설정(씬 목록)에는 넣지 않는다.
// ============================================================
public static class ExampleSceneBuilder
{
    private const string Root = "Assets/99_ModuleExample";
    private const string ScenePath = Root + "/Example_MinimalScenario.unity";
    private const string JsonPath = Root + "/Scenarios/example_minimal.json";

    [MenuItem("Tools/DoDrill/모듈 최소 예제 씬 만들기")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("모듈 최소 예제", "플레이 중에는 만들 수 없습니다. 플레이를 멈춘 뒤 다시 실행하세요.", "확인");
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
            !EditorUtility.DisplayDialog("모듈 최소 예제", "예제 씬이 이미 있습니다. 새로 만들어 덮어쓸까요?", "덮어쓰기", "취소"))
            return;

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            // 카메라·조명
            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cam.tag = "MainCamera";
            cam.transform.position = new Vector3(0f, 1.6f, -3f);
            SceneManager.MoveGameObjectToScene(cam, scene);
            var light = new GameObject("Directional Light", typeof(Light));
            light.GetComponent<Light>().type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            SceneManager.MoveGameObjectToScene(light, scene);

            // FishNet 네트워크 매니저 (호스트로 실행)
            var net = new GameObject("NetworkManager");
            SceneManager.MoveGameObjectToScene(net, scene);
            var nm = net.AddComponent<NetworkManager>();
            var tm = net.AddComponent<TransportManager>();
            var tug = net.AddComponent<Tugboat>();
            var tmSo = new SerializedObject(tm);
            var tp = tmSo.FindProperty("Transport");
            if (tp != null) { tp.objectReferenceValue = tug; tmSo.ApplyModifiedPropertiesWithoutUndo(); }
            // 스폰 프리팹 목록: 예제는 네트워크 오브젝트를 스폰하지 않으므로 빈 목록 에셋을 연결한다(비어 있으면 FishNet 이 시작 시 오류)
            var nmSo = new SerializedObject(nm);
            var spProp = nmSo.FindProperty("_spawnablePrefabs");
            if (spProp != null)
            {
                spProp.objectReferenceValue = EmptyPrefabList();
                nmSo.ApplyModifiedPropertiesWithoutUndo();
            }

            // 시나리오 엔진 + 예제 시작기
            var sc = new GameObject("Scenario");
            SceneManager.MoveGameObjectToScene(sc, scene);
            var boot = sc.AddComponent<ScenarioBootstrapper>();
            var runner = sc.AddComponent<ScenarioRunner>();
            var rso = new SerializedObject(runner);
            rso.FindProperty("_bootstrapper").objectReferenceValue = boot;
            rso.ApplyModifiedPropertiesWithoutUndo();

            var starter = sc.AddComponent<ExampleScenarioStarter>();
            var sso = new SerializedObject(starter);
            sso.FindProperty("runner").objectReferenceValue = runner;
            sso.FindProperty("scenarioJson").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
            sso.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Example] 예제 씬 생성: {ScenePath} — 열어서 Play 하세요");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
        AssetDatabase.Refresh();
        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
    }

    private static FishNet.Managing.Object.SinglePrefabObjects EmptyPrefabList()
    {
        const string path = Root + "/ExampleSpawnablePrefabs.asset";
        var a = AssetDatabase.LoadAssetAtPath<FishNet.Managing.Object.SinglePrefabObjects>(path);
        if (a != null) return a;
        a = ScriptableObject.CreateInstance<FishNet.Managing.Object.SinglePrefabObjects>();
        AssetDatabase.CreateAsset(a, path);
        AssetDatabase.SaveAssets();
        return a;
    }
}
