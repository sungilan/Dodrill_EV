using DoDrill.Training;
using FishNet.Component.Animating;
using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Managing.Transporting;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// ============================================================
//  GenericPrefabBuilder.cs — 콘텐츠 없는 범용 프리팹 만들기 (모듈화, 2026-11-20)
//
//  메뉴: Tools ▸ DoDrill ▸ 범용 프리팹 만들기
//  EV 콘텐츠·EV 스크립트 없이 모듈 스크립트만으로 멀티플레이 시나리오를 구성하는 데 쓰는 프리팹을 각 모듈 폴더에 만든다.
//  이미 있으면 덮어쓰지 않고 건너뛴다(다시 만들려면 프리팹을 지우고 실행).
//
//   Avatar/Prefabs/Players/GenericPlayer.prefab   PC 1인칭 플레이어(네트워크). 몸통+카메라+HoldPoint, 내 것만 조작·카메라 켬
//   Interaction/Prefabs/NetworkItem.prefab        집을 수 있는 네트워크 아이템(SyncGrab+TaskItem). 변형(Variant)을 만들어 prefabId·모양을 바꿔 쓴다
//   Interaction/Prefabs/TaskZone.prefab           놓는 자리(TaskInteractionZone, 단계 중에만 보임)
//   Interaction/Prefabs/TaskButton.prefab         누르는 버튼(TaskClickTarget)
//   Engine/Prefabs/ScenarioEngine.prefab          시나리오 엔진 묶음(Bootstrapper·Runner·Spawner·수신기·AutoStarter·신호 브리지)
//   Engine/Prefabs/SpawnPoint.prefab              아이템 스폰 자리
//   Engine/Prefabs/NetworkManagerHost.prefab      FishNet NetworkManager+Tugboat+PlayerSpawner(GenericPlayer)
//   Ui/Prefabs/SimpleTaskHud.prefab               단계 표시(제목·안내·완료)
// ============================================================
public static class GenericPrefabBuilder
{
    public const string PlayerPath = "Assets/01_Script/Avatar/Prefabs/Players/GenericPlayer.prefab";
    public const string AvatarPlayerPath = "Assets/01_Script/Avatar/Prefabs/Players/GenericAvatarPlayer.prefab";
    public const string MechanicAvatarPath = "Assets/01_Script/Avatar/Prefabs/Avatars/PcAvatar_Mechanic.prefab";
    public const string ItemPath ="Assets/01_Script/Interaction/Prefabs/NetworkItem.prefab";
    public const string ZonePath = "Assets/01_Script/Interaction/Prefabs/TaskZone.prefab";
    public const string ButtonPath = "Assets/01_Script/Interaction/Prefabs/TaskButton.prefab";
    public const string EnginePath = "Assets/01_Script/Engine/Prefabs/ScenarioEngine.prefab";
    public const string SpawnPointPath = "Assets/01_Script/Engine/Prefabs/SpawnPoint.prefab";
    public const string NetMgrPath = "Assets/01_Script/Engine/Prefabs/NetworkManagerHost.prefab";
    public const string HudPath = "Assets/01_Script/Ui/Prefabs/SimpleTaskHud.prefab";
    public const string ListPath = "Assets/01_Script/Engine/Prefabs/GenericSpawnablePrefabs.asset";

    [MenuItem("Tools/DoDrill/범용 프리팹 만들기")]
    public static void BuildAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("범용 프리팹", "플레이 중에는 만들 수 없습니다. 플레이를 멈춘 뒤 다시 실행하세요.", "확인");
            return;
        }
        int made = 0;
        made += Build(PlayerPath, MakePlayer) ? 1 : 0;
        made += Build(AvatarPlayerPath, MakeAvatarPlayer) ? 1 : 0;
        made += Build(ItemPath, MakeItem) ? 1 : 0;
        made += Build(ZonePath, MakeZone) ? 1 : 0;
        made += Build(ButtonPath, MakeButton) ? 1 : 0;
        made += Build(SpawnPointPath, MakeSpawnPoint) ? 1 : 0;
        made += Build(EnginePath, MakeEngine) ? 1 : 0;
        made += Build(HudPath, MakeHud) ? 1 : 0;
        EnsureSpawnableList();
        made += Build(NetMgrPath, MakeNetworkManager) ? 1 : 0;
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[GenericPrefab] 범용 프리팹 {made}개 새로 만듦(이미 있는 것은 건너뜀)");
    }

    // ── 공통 ─────────────────────────────
    private delegate GameObject Maker();

    private static bool Build(string path, Maker make)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return false;
        EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
        var go = make();
        if (go == null) return false;
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return true;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
    }

    private static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(t);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    public static Material Mat(string folder, string name, Color color, bool transparent = false)
    {
        EnsureFolder(folder);
        string path = $"{folder}/{name}.mat";
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

    // ── 플레이어 ─────────────────────────────
    private static GameObject MakePlayer()
    {
        var body = Mat("Assets/01_Script/Avatar/Art/Materials", "Generic_PlayerBody", new Color(0.25f, 0.55f, 0.95f));
        var face = Mat("Assets/01_Script/Avatar/Art/Materials", "Generic_PlayerFace", new Color(0.95f, 0.95f, 0.95f));

        var root = new GameObject("GenericPlayer");
        root.AddComponent<NetworkObject>();
        root.AddComponent<LocalNetworkTransform>();

        var capsule = Prim(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0f, 0.9f, 0f), new Vector3(0.5f, 0.9f, 0.5f), body, false);
        var nose = Prim(PrimitiveType.Cube, "Face", root.transform, new Vector3(0f, 1.55f, 0.22f), new Vector3(0.12f, 0.08f, 0.12f), face, false);

        var pivot = new GameObject("CameraPivot");
        pivot.transform.SetParent(root.transform, false);
        pivot.transform.localPosition = new Vector3(0f, 1.6f, 0f);

        var camGo = new GameObject("Camera", typeof(Camera), typeof(AudioListener));
        camGo.transform.SetParent(pivot.transform, false);
        var cam = camGo.GetComponent<Camera>();
        cam.nearClipPlane = 0.05f;
        cam.enabled = false;                       // 내 플레이어일 때만 켠다(GenericNetworkPlayer)
        var listener = camGo.GetComponent<AudioListener>();
        listener.enabled = false;

        var hold = new GameObject("HoldPoint");
        hold.tag = "HoldPoint";                    // SyncGrab 이 이 태그로 내 손 위치를 찾는다 — 프로젝트 Tags 에 "HoldPoint" 필요
        hold.transform.SetParent(camGo.transform, false);
        hold.transform.localPosition = new Vector3(0.25f, -0.2f, 0.6f);

        var interactor = root.AddComponent<SimplePcInteractor>();
        interactor.cam = cam;
        interactor.pitchPivot = pivot.transform;
        interactor.flyVertical = false;
        interactor.enabled = false;

        var player = root.AddComponent<GenericNetworkPlayer>();
        var so = new SerializedObject(player);
        so.FindProperty("cam").objectReferenceValue = cam;
        so.FindProperty("listener").objectReferenceValue = listener;
        so.FindProperty("interactor").objectReferenceValue = interactor;
        var arr = so.FindProperty("bodyRenderers");
        arr.arraySize = 2;
        arr.GetArrayElementAtIndex(0).objectReferenceValue = capsule.GetComponent<Renderer>();
        arr.GetArrayElementAtIndex(1).objectReferenceValue = nose.GetComponent<Renderer>();
        so.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    // ── 아바타 플레이어 ─────────────────────────────
    // GenericPlayer(캡슐) 대신 아바타 프리팹(PcAvatar_Mechanic)을 몸으로 쓰는 플레이어.
    // PcMobileAvatarController 가 SimplePcInteractor(IPcLookController)의 이동·시점 값으로 걷기 애니메이션·IK·손 뻗기·머리 방향을 구동한다.
    // (EV 의 PcAvatarInstaller 와 같은 연결을 모듈 안에서 직접 한다 — 이식할 때 Editor 기본 어셈블리의 도구에 기대지 않기 위해)
    private const float EyeHeight = 1.7f;

    private static GameObject MakeAvatarPlayer()
    {
        var avatarAsset = AssetDatabase.LoadAssetAtPath<GameObject>(MechanicAvatarPath);
        if (avatarAsset == null) { Debug.LogError($"[GenericPrefab] 아바타 프리팹이 없습니다: {MechanicAvatarPath}"); return null; }

        var root = new GameObject("GenericAvatarPlayer");
        root.AddComponent<NetworkObject>();
        root.AddComponent<LocalNetworkTransform>();

        var pivot = new GameObject("CameraPivot");
        pivot.transform.SetParent(root.transform, false);
        pivot.transform.localPosition = new Vector3(0f, EyeHeight, 0f);

        var camGo = new GameObject("Camera", typeof(Camera), typeof(AudioListener));
        camGo.transform.SetParent(pivot.transform, false);
        var cam = camGo.GetComponent<Camera>();
        cam.nearClipPlane = 0.01f;
        cam.enabled = false;
        var listener = camGo.GetComponent<AudioListener>();
        listener.enabled = false;

        var hold = new GameObject("HoldPoint");
        hold.tag = "HoldPoint";
        hold.transform.SetParent(camGo.transform, false);
        hold.transform.localPosition = new Vector3(0.25f, -0.2f, 0.6f);

        var interactor = root.AddComponent<SimplePcInteractor>();
        interactor.cam = cam;
        interactor.pitchPivot = pivot.transform;
        interactor.flyVertical = false;
        interactor.enabled = false;

        var player = root.AddComponent<GenericNetworkPlayer>();
        var pso = new SerializedObject(player);
        pso.FindProperty("cam").objectReferenceValue = cam;
        pso.FindProperty("listener").objectReferenceValue = listener;
        pso.FindProperty("interactor").objectReferenceValue = interactor;
        pso.ApplyModifiedPropertiesWithoutUndo();

        var avatarGo = (GameObject)PrefabUtility.InstantiatePrefab(avatarAsset, root.transform);
        avatarGo.name = "Avatar";
        avatarGo.transform.localPosition = Vector3.zero;
        avatarGo.transform.localRotation = Quaternion.identity;

        var anim = avatarGo.GetComponent<Animator>();
        var locomotion = avatarGo.GetComponent<PcLocomotionAnimator>();
        var armReach = avatarGo.GetComponent<PcArmReach>();
        var headAim = avatarGo.GetComponent<PcHeadAim>();
        if (anim == null || locomotion == null || armReach == null)
        {
            Debug.LogError("[GenericPrefab] 아바타 프리팹에 Animator / PcLocomotionAnimator / PcArmReach 가 없습니다.");
            Object.DestroyImmediate(root);
            return null;
        }
        SetRef(headAim, "ownerCamera", cam.transform);
        SetRef(armReach, "bodyRef", root.transform);

        var heads = new System.Collections.Generic.List<Renderer>();
        foreach (var r in avatarGo.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            string n = r.name.ToLowerInvariant();
            if (n.Contains("head") || n.Contains("hair") || n.Contains("eyes") || n.Contains("facemask") || n.Contains("faceshield") || n.Contains("glasses")) heads.Add(r);
        }

        var ctrl = root.AddComponent<PcMobileAvatarController>();
        var so = new SerializedObject(ctrl);
        so.FindProperty("avatarAnimator").objectReferenceValue = anim;
        so.FindProperty("cameraPivot").objectReferenceValue = pivot.transform;
        so.FindProperty("lookControllerBehaviour").objectReferenceValue = interactor;
        so.FindProperty("locomotion").objectReferenceValue = locomotion;
        so.FindProperty("armReach").objectReferenceValue = armReach;
        so.FindProperty("eyeHeight").floatValue = EyeHeight;
        so.FindProperty("pinRootToGround").boolValue = false;       // 바닥 높이를 유지하며 걷는다(수직 비행 없음)
        var hide = so.FindProperty("hideRenderersForOwner");
        hide.arraySize = heads.Count;
        for (int i = 0; i < heads.Count; i++) hide.GetArrayElementAtIndex(i).objectReferenceValue = heads[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        // 걷기·웅크리기 애니메이션 파라미터를 다른 사람 화면에 전파
        var na = root.AddComponent<NetworkAnimator>();
        var nso = new SerializedObject(na);
        SetObj(nso, "_animator", anim);
        SetBool(nso, "_smoothFloats", true);
        SetInt(nso, "_interpolation", 2);
        SetBool(nso, "_clientAuthoritative", true);
        SetBool(nso, "_sendToOwner", false);
        nso.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    private static void SetRef(Object target, string field, Object value)
    {
        if (target == null) return;
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null) { Debug.LogWarning($"[GenericPrefab] {target.GetType().Name}.{field} 필드를 찾지 못했습니다."); return; }
        p.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void SetObj(SerializedObject so, string name, Object v) { var p = so.FindProperty(name); if (p != null) p.objectReferenceValue = v; else Debug.LogWarning($"[GenericPrefab] NetworkAnimator.{name} 없음"); }
    private static void SetBool(SerializedObject so, string name, bool v) { var p = so.FindProperty(name); if (p != null) p.boolValue = v; else Debug.LogWarning($"[GenericPrefab] NetworkAnimator.{name} 없음"); }
    private static void SetInt(SerializedObject so, string name, int v) { var p = so.FindProperty(name); if (p != null) p.intValue = v; else Debug.LogWarning($"[GenericPrefab] NetworkAnimator.{name} 없음"); }

    // ── 아이템·구역·버튼 ─────────────────────────────
    private static GameObject MakeItem()
    {
        var mat = Mat("Assets/01_Script/Interaction/Art/Materials", "Generic_Item", new Color(0.75f, 0.77f, 0.8f));
        var root = new GameObject("NetworkItem");
        root.AddComponent<NetworkObject>();
        var rb = root.AddComponent<Rigidbody>();
        rb.mass = 1f;
        rb.isKinematic = true;                      // 서버가 잡힘·놓음에 따라 켜고 끈다(SyncGrab)
        var col = root.AddComponent<BoxCollider>();
        col.size = new Vector3(0.2f, 0.2f, 0.2f);
        Prim(PrimitiveType.Cube, "Model", root.transform, Vector3.zero, new Vector3(0.2f, 0.2f, 0.2f), mat, false);
        root.AddComponent<LocalNetworkTransform>();
        root.AddComponent<TaskItem>().prefabId = "NetworkItem";
        var sg = root.AddComponent<SyncGrab>();
        sg.drawOnTopWhileHeld = false;              // 맨 앞에 그리기는 EV 의 렌더러 기능이 필요 — 범용에서는 끔
        return root;
    }

    private static GameObject MakeZone()
    {
        var mat = Mat("Assets/01_Script/Interaction/Art/Materials", "Generic_Zone", new Color(0.1f, 1f, 0.3f, 0.35f), true);
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "TaskZone";
        go.transform.localScale = new Vector3(0.4f, 0.03f, 0.3f);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        go.GetComponent<Collider>().isTrigger = true;
        go.AddComponent<TaskInteractionZone>().zoneId = "Zone";   // 인스턴스마다 JSON 의 targetZoneId 와 맞춘다
        return go;
    }

    private static GameObject MakeButton()
    {
        var mat = Mat("Assets/01_Script/Interaction/Art/Materials", "Generic_Button", new Color(0.85f, 0.12f, 0.1f));
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "TaskButton";
        go.transform.localScale = new Vector3(0.12f, 0.02f, 0.12f);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        var ct = go.AddComponent<TaskClickTarget>();
        ct.targetId = "Button";
        ct.displayName = "버튼";
        return go;
    }

    // ── 엔진·UI ─────────────────────────────
    private static GameObject MakeSpawnPoint()
    {
        var go = new GameObject("SpawnPoint");
        go.AddComponent<SpawnPoint>().pointId = "SpawnPoint";
        return go;
    }

    private static GameObject MakeEngine()
    {
        // 루트는 네트워크 오브젝트가 아니다 — 씬에 놓인 NetworkObject 는 FishNet 이 서버가 켜질 때까지 꺼 두므로,
        // 서버를 켜는 ScenarioAutoStarter 가 그 위에 있으면 영영 시작하지 못한다. 네트워크 부품은 자식(Network)에 둔다.
        var go = new GameObject("ScenarioEngine");
        var boot = go.AddComponent<ScenarioBootstrapper>();
        var runner = go.AddComponent<ScenarioRunner>();
        go.AddComponent<ScenarioReceiver>();
        go.AddComponent<ScenarioStateReceiver>();
        var starter = go.AddComponent<ScenarioAutoStarter>();

        var net = new GameObject("Network");
        net.transform.SetParent(go.transform, false);
        net.AddComponent<NetworkObject>();                         // ScenarioSpawner·NetworkInteractionBridge 가 NetworkBehaviour
        var spawner = net.AddComponent<ScenarioSpawner>();
        net.AddComponent<NetworkInteractionBridge>();

        var rso = new SerializedObject(runner);
        rso.FindProperty("_bootstrapper").objectReferenceValue = boot;
        rso.FindProperty("_spawner").objectReferenceValue = spawner;
        rso.ApplyModifiedPropertiesWithoutUndo();
        var sso = new SerializedObject(starter);
        sso.FindProperty("runner").objectReferenceValue = runner;
        sso.ApplyModifiedPropertiesWithoutUndo();
        return go;
    }

    private static GameObject MakeHud()
    {
        return new GameObject("SimpleTaskHud", typeof(SimpleTaskHud));
    }

    // ── 네트워크 매니저 ─────────────────────────────
    private static void EnsureSpawnableList()
    {
        var list = AssetDatabase.LoadAssetAtPath<SinglePrefabObjects>(ListPath);
        if (list == null)
        {
            list = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            AssetDatabase.CreateAsset(list, ListPath);
        }
        foreach (var p in new[] { PlayerPath, AvatarPlayerPath, ItemPath })
        {
            var nob = AssetDatabase.LoadAssetAtPath<GameObject>(p)?.GetComponent<NetworkObject>();
            if (nob != null) list.AddObject(nob, true, false);
        }
        EditorUtility.SetDirty(list);
    }

    private static GameObject MakeNetworkManager()
    {
        var go = new GameObject("NetworkManagerHost");
        var nm = go.AddComponent<NetworkManager>();
        var tm = go.AddComponent<TransportManager>();
        var tug = go.AddComponent<Tugboat>();
        var ps = go.AddComponent<PlayerSpawner>();

        var tmSo = new SerializedObject(tm);
        var tp = tmSo.FindProperty("Transport");
        if (tp != null) { tp.objectReferenceValue = tug; tmSo.ApplyModifiedPropertiesWithoutUndo(); }

        var nmSo = new SerializedObject(nm);
        var sp = nmSo.FindProperty("_spawnablePrefabs");
        if (sp != null) { sp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Object>(ListPath); nmSo.ApplyModifiedPropertiesWithoutUndo(); }

        var psSo = new SerializedObject(ps);
        var pp = psSo.FindProperty("_playerPrefab");
        if (pp != null) { pp.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath)?.GetComponent<NetworkObject>(); psSo.ApplyModifiedPropertiesWithoutUndo(); }
        return go;
    }
}
