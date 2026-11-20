#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// ============================================================
//  LobbyBuildProfiles.cs — 로비 서버·클라이언트 빌드 버튼 (모듈화, 2026-11-20)
//
//  메뉴: Tools ▸ DoDrill ▸ 로비 빌드 프로필
//   · 룸 서버     : 씬 Room + 게임 씬            → RoomServer(.x86_64/.exe)
//   · 마스터 서버 : 씬 Master (안의 스포너가 룸 서버 파일을 띄우도록 경로 지정) → MasterServer(.x86_64/.exe)
//   · 서버 둘 다  : 룸 → 마스터 순서로 빌드(마스터가 룸 서버 경로를 알아야 하므로 이 순서)
//   · PC 클라이언트: 씬 Client + 게임 씬, Windows → <제품명>.exe
//  서버는 Linux Dedicated Server(EV 방식)가 기본. Windows 서버는 셰이더 변형 준비가 매우 오래 걸릴 수 있다.
//  씬이 없으면 먼저 로비 템플릿 씬 설치를 실행하고, 게임 씬이 없으면 빈 게임 씬을 만든다.
//  설정(출력 폴더·서버 OS·주소)은 이 에디터에만 저장된다(EditorPrefs).
//  배치 모드: -executeMethod LobbyBuildProfiles.BatchBuildAllServers (환경변수 LOBBY_BUILD_DIR 로 출력 폴더 지정)
// ============================================================
public class LobbyBuildProfiles : EditorWindow
{
    private enum Profile { RoomServer, MasterServer, AllServers, PcClient }

    private const string PendingKey = "DoDrill.LobbyBuild.Pending";   // 플랫폼 전환(도메인 리로드) 뒤 이어서 할 빌드
    private const string MasterScene = "Assets/Scenes/Lobby/Master.unity";
    private const string RoomScene = "Assets/Scenes/Lobby/Room.unity";
    private const string ClientScene = "Assets/Scenes/Lobby/Client.unity";

    // ── 설정(EditorPrefs, 프로젝트별) ──
    private static string Key(string n) => "DoDrill.LobbyBuild." + Application.dataPath.GetHashCode() + "." + n;
    private static string OutputRoot
    {
        get
        {
            string v = EditorPrefs.GetString(Key("out"), "");
            if (!string.IsNullOrEmpty(v)) return v;
            string proj = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(Path.GetDirectoryName(proj), Path.GetFileName(proj) + "_Build");
        }
        set => EditorPrefs.SetString(Key("out"), value);
    }
    private static bool WindowsServer { get => EditorPrefs.GetBool(Key("winsrv"), false); set => EditorPrefs.SetBool(Key("winsrv"), value); }
    private static string SpawnerIp { get => EditorPrefs.GetString(Key("ip"), "127.0.0.1"); set => EditorPrefs.SetString(Key("ip"), value); }
    private static string RoomExeOverride { get => EditorPrefs.GetString(Key("roomexe"), ""); set => EditorPrefs.SetString(Key("roomexe"), value); }

    [MenuItem("Tools/DoDrill/로비 빌드 프로필")]
    public static void Open() => GetWindow<LobbyBuildProfiles>("로비 빌드 프로필").minSize = new Vector2(440, 520);

    // ══════════════════════════════════════════════════════
    //  창
    // ══════════════════════════════════════════════════════
    private void OnGUI()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("현재 설정", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            $"플랫폼: {EditorUserBuildSettings.activeBuildTarget} ({EditorUserBuildSettings.standaloneBuildSubtarget})\n" +
            $"씬: {string.Join(", ", EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => Path.GetFileNameWithoutExtension(s.path)))}\n" +
            $"게임 씬 이름: {LobbySettings.Current.gameSceneName}", MessageType.None);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("출력 폴더", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            OutputRoot = EditorGUILayout.TextField(OutputRoot);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string p = EditorUtility.OpenFolderPanel("빌드 출력 폴더", OutputRoot, "");
                if (!string.IsNullOrEmpty(p)) OutputRoot = p;
            }
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("서버 옵션", EditorStyles.boldLabel);
        bool win = EditorGUILayout.ToggleLeft("Windows 서버로 빌드 (기본은 Linux Dedicated Server — Windows 는 매우 느릴 수 있음)", WindowsServer);
        if (win != WindowsServer) WindowsServer = win;
        SpawnerIp = EditorGUILayout.TextField(new GUIContent("스포너 machineIp", "룸 서버가 마스터에 등록할 때 알리는 이 서버의 주소. 한 PC에서 시험하면 127.0.0.1"), SpawnerIp);
        RoomExeOverride = EditorGUILayout.TextField(new GUIContent("룸 서버 실행 경로(선택)", "비워 두면 이번에 빌드한 룸 서버 파일 경로. 서버를 다른 곳에 올릴 때 그 경로를 적는다"), RoomExeOverride);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("빌드", EditorStyles.boldLabel);
        if (Button("룸 서버 빌드", "씬 Room + 게임 씬")) Run(Profile.RoomServer);
        if (Button("마스터 서버 빌드", "씬 Master (스포너에 룸 서버 경로 지정)")) Run(Profile.MasterServer);
        if (Button("서버 둘 다 빌드 (룸 → 마스터)", "처음 시험할 때 이것 하나면 된다")) Run(Profile.AllServers);
        EditorGUILayout.Space(4);
        if (Button("PC 클라이언트 빌드 (Windows)", "씬 Client + 게임 씬")) Run(Profile.PcClient);

        EditorGUILayout.Space(8);
        if (GUILayout.Button("출력 폴더 열기")) { Directory.CreateDirectory(OutputRoot); EditorUtility.RevealInFinder(OutputRoot); }
        EditorGUILayout.HelpBox("서버는 Linux 빌드 모듈(Unity Hub ▸ Linux Dedicated Server Build Support)이 필요하다. Windows 에서 Linux 로 빌드하려면 패키지 com.unity.toolchain.win-x86_64-linux, com.unity.sdk.linux-x86_64 도 필요하다. 네트워크 관련 수정 뒤에는 서버와 클라이언트를 모두 다시 빌드한다.", MessageType.Info);
    }

    private static bool Button(string label, string tip)
    {
        var r = GUILayout.Button(new GUIContent(label, tip), GUILayout.Height(30));
        return r;
    }

    // ══════════════════════════════════════════════════════
    //  실행
    // ══════════════════════════════════════════════════════
    private static BuildTarget ServerTarget => WindowsServer ? BuildTarget.StandaloneWindows64 : BuildTarget.StandaloneLinux64;

    private static BuildTarget TargetFor(Profile p) => p == Profile.PcClient ? BuildTarget.StandaloneWindows64 : ServerTarget;

    private static void Run(Profile p)
    {
        if (Application.isPlaying) { EditorUtility.DisplayDialog("로비 빌드", "플레이 모드를 끄고 실행하세요.", "확인"); return; }
        var target = TargetFor(p);
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
        {
            EditorUtility.DisplayDialog("로비 빌드",
                $"이 에디터에 {target} 빌드 모듈이 없다.\nUnity Hub ▸ Installs ▸ 이 에디터 ▸ Add modules 에서 " +
                (target == BuildTarget.StandaloneLinux64 ? "'Linux Dedicated Server Build Support'(와 Linux Build Support)" : "Windows Build Support") + " 를 설치한 뒤 다시 실행하세요.", "확인");
            return;
        }
        if (!EditorUtility.DisplayDialog("로비 빌드", $"{Label(p)}을(를) 빌드합니다.\n플랫폼이 {target} 으로 바뀌며 처음에는 다시 임포트되어 시간이 걸릴 수 있습니다.\n출력: {OutputRoot}", "빌드", "취소")) return;

        // 플랫폼 전환은 도메인 리로드를 일으키므로, 바뀌어야 하면 예약해 두고 리로드 뒤에 이어서 한다
        if (EditorUserBuildSettings.activeBuildTarget != target)
        {
            SessionState.SetString(PendingKey, p.ToString());
            EditorUserBuildSettings.selectedStandaloneTarget = target;
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, target);
            return;
        }
        Execute(p);
    }

    [InitializeOnLoadMethod]
    private static void ResumePending() => EditorApplication.delayCall += TryResume;

    private static void TryResume()
    {
        string pending = SessionState.GetString(PendingKey, "");
        if (string.IsNullOrEmpty(pending)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryResume; return; }
        SessionState.EraseString(PendingKey);
        if (!Enum.TryParse(pending, out Profile p)) return;
        if (EditorUtility.scriptCompilationFailed) { EditorUtility.DisplayDialog("로비 빌드", "스크립트 컴파일 오류가 있어 예약된 빌드를 하지 않았습니다.", "확인"); return; }
        Debug.Log($"[LobbyBuild] 플랫폼 전환 완료 — 예약된 {Label(p)} 빌드 시작");
        Execute(p);
    }

    private static string Label(Profile p) => p switch
    {
        Profile.RoomServer => "룸 서버",
        Profile.MasterServer => "마스터 서버",
        Profile.AllServers => "서버 둘 다(룸 → 마스터)",
        _ => "PC 클라이언트"
    };

    private static void Execute(Profile p)
    {
        bool ok = true;
        if (p == Profile.RoomServer || p == Profile.AllServers) ok = BuildRoom() && ok;
        if (ok && (p == Profile.MasterServer || p == Profile.AllServers)) ok = BuildMaster();
        if (p == Profile.PcClient) ok = BuildClient();
        if (ok && !Application.isBatchMode) EditorUtility.RevealInFinder(OutputRoot);
    }

    // ══════════════════════════════════════════════════════
    //  개별 빌드
    // ══════════════════════════════════════════════════════
    private static string Ext(BuildTarget t) => t == BuildTarget.StandaloneLinux64 ? ".x86_64" : ".exe";
    private static string RoomPath() => Path.GetFullPath(Path.Combine(OutputRoot, "RoomServer", "RoomServer" + Ext(ServerTarget)));
    private static string MasterPath() => Path.GetFullPath(Path.Combine(OutputRoot, "MasterServer", "MasterServer" + Ext(ServerTarget)));

    private static bool BuildRoom()
    {
        if (!EnsureScenes(out string game)) return false;
        return Build(new[] { RoomScene, game }, RoomPath(), ServerTarget, true, "RoomServer");   // Room 이 Game 보다 앞(인덱스 0)이어야 한다
    }

    private static bool BuildMaster()
    {
        if (!EnsureScenes(out _)) return false;
        string room = string.IsNullOrWhiteSpace(RoomExeOverride) ? RoomPath() : RoomExeOverride.Trim();
        PatchSpawner(room);
        return Build(new[] { MasterScene }, MasterPath(), ServerTarget, true, "MasterServer");
    }

    private static bool BuildClient()
    {
        if (!EnsureScenes(out string game)) return false;
        string exe = Path.GetFullPath(Path.Combine(OutputRoot, "Client", PlayerSettings.productName + ".exe"));
        return Build(new[] { ClientScene, game }, exe, BuildTarget.StandaloneWindows64, false, null);
    }

    /// <summary>템플릿 씬이 없으면 설치하고, 게임 씬이 없으면 빈 씬을 만든 뒤 설치를 다시 한다.</summary>
    private static bool EnsureScenes(out string gameScene)
    {
        gameScene = FindGameScene();
        if (gameScene == null)
        {
            // 설치기가 씬을 추가로 열려면 저장된 씬이 열려 있어야 한다
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            string name = LobbySettings.Current.gameSceneName;
            gameScene = $"Assets/Scenes/{name}.unity";
            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            var s = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(s, gameScene);
            Debug.Log($"[LobbyBuild] 게임 씬이 없어 빈 씬을 만들었습니다: {gameScene} (콘텐츠가 이 씬을 채웁니다)");
            LobbySceneInstaller.Install();   // 게임 씬이 생겼으니 FishNet DefaultScene 경로를 맞춘다
        }
        bool missing = AssetDatabase.LoadAssetAtPath<SceneAsset>(MasterScene) == null
                    || AssetDatabase.LoadAssetAtPath<SceneAsset>(RoomScene) == null
                    || AssetDatabase.LoadAssetAtPath<SceneAsset>(ClientScene) == null;
        if (missing)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            LobbySceneInstaller.Install();
            AssetDatabase.Refresh();
        }
        foreach (var s in new[] { MasterScene, RoomScene, ClientScene })
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(s) == null) { Debug.LogError($"[LobbyBuild] 씬 없음: {s}"); return false; }
        return true;
    }

    private static string FindGameScene()
    {
        string name = LobbySettings.Current.gameSceneName;
        foreach (var g in AssetDatabase.FindAssets($"{name} t:Scene"))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (Path.GetFileNameWithoutExtension(p) == name && !p.StartsWith("Assets/01_Script/Lobby/")) return p;
        }
        return null;
    }

    /// <summary>마스터 씬 안의 스포너가 띄울 룸 서버 경로·주소를 지정한다(타입 참조 없이 이름으로 찾음).</summary>
    private static void PatchSpawner(string roomExe)
    {
        var cur = EditorSceneManager.GetActiveScene();
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var sc = EditorSceneManager.OpenScene(MasterScene, OpenSceneMode.Additive);
        int n = 0;
        foreach (var r in sc.GetRootGameObjects())
            foreach (var c in r.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c.GetType().Name != "SpawnerBehaviour") continue;
                var so = new SerializedObject(c);
                var exe = so.FindProperty("executableFilePath"); var ip = so.FindProperty("machineIp");
                if (exe != null) exe.stringValue = roomExe.Replace('\\', '/');
                if (ip != null) ip.stringValue = SpawnerIp;
                so.ApplyModifiedPropertiesWithoutUndo();
                n++;
            }
        if (n > 0) { EditorSceneManager.MarkSceneDirty(sc); EditorSceneManager.SaveScene(sc); }
        EditorSceneManager.CloseScene(sc, true);
        Debug.Log(n > 0 ? $"[LobbyBuild] 스포너 룸 서버 경로 = {roomExe} / machineIp = {SpawnerIp}" : "[LobbyBuild] Master 씬에서 SpawnerBehaviour 를 찾지 못했습니다 — 스포너 경로는 지정되지 않았습니다.");
    }

    private static bool Build(string[] scenes, string exePath, BuildTarget target, bool server, string productName)
    {
        // 씬 목록: 이번에 쓸 씬을 순서대로 켜고, 나머지는 끈 채로 뒤에 둔다
        var rest = EditorBuildSettings.scenes.Where(s => !scenes.Contains(s.path)).Select(s => new EditorBuildSettingsScene(s.path, false));
        EditorBuildSettings.scenes = scenes.Select(s => new EditorBuildSettingsScene(s, true)).Concat(rest).ToArray();

        EditorUserBuildSettings.selectedStandaloneTarget = target;
        EditorUserBuildSettings.standaloneBuildSubtarget = server ? StandaloneBuildSubtarget.Server : StandaloneBuildSubtarget.Player;
        if (server) { SetStandaloneXrInit(false); PreCreateOpenXrSettings(); }

        string oldName = PlayerSettings.productName;
        if (productName != null) PlayerSettings.productName = oldName + "_" + productName;
        Directory.CreateDirectory(Path.GetDirectoryName(exePath));

        BuildReport rep = null;
        // 새 프로젝트의 OpenXR 는 첫 빌드에서 "설정이 아직 로드되지 않았다"며 실패하기도 한다 → 같은 에디터에서 최대 2번 시도
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            rep = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = exePath,
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
                subtarget = server ? (int)StandaloneBuildSubtarget.Server : (int)StandaloneBuildSubtarget.Player,
                options = BuildOptions.None,
            });
            Debug.Log($"[LobbyBuild] {Path.GetFileName(exePath)} 시도 {attempt}: {rep.summary.result} ({rep.summary.totalSize / (1024 * 1024)} MB, 오류 {rep.summary.totalErrors})");
            if (rep.summary.result == BuildResult.Succeeded) break;
        }
        PlayerSettings.productName = oldName;

        if (rep.summary.result != BuildResult.Succeeded)
        {
            var errs = rep.steps.SelectMany(s => s.messages).Where(m => m.type == LogType.Error || m.type == LogType.Exception).Select(m => m.content).Take(5);
            Debug.LogError($"[LobbyBuild] 빌드 실패 — {exePath}\n" + string.Join("\n", errs));
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("로비 빌드", "빌드 실패. 콘솔의 [LobbyBuild] 오류를 확인하세요.", "확인");
            return false;
        }
        Debug.Log($"[LobbyBuild] 완료 → {exePath}");
        return true;
    }

    // ── XR 관련(패키지가 없는 프로젝트에서도 컴파일되도록 리플렉션) ──
    /// <summary>서버 빌드에는 XR 이 필요 없다 — Standalone XR 자동 초기화를 끈다(EV 빌드 프로필도 같은 설정).</summary>
    private static void SetStandaloneXrInit(bool on)
    {
        try
        {
            var t = Type.GetType("UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget, Unity.XR.Management.Editor");
            var m = t?.GetMethod("XRGeneralSettingsForBuildTarget", new[] { typeof(BuildTargetGroup) });
            var gs = m?.Invoke(null, new object[] { BuildTargetGroup.Standalone });
            var prop = gs?.GetType().GetProperty("InitManagerOnStart");
            if (prop != null) { prop.SetValue(gs, on); EditorUtility.SetDirty((UnityEngine.Object)gs); AssetDatabase.SaveAssets(); }
        }
        catch (Exception e) { Debug.Log("[LobbyBuild] XR 설정 건너뜀: " + e.Message); }
    }

    /// <summary>OpenXR 패키지는 설정 에셋이 없으면 "빌드 도중"에 만들려다 BuildFailedException 을 낸다 — 빌드 전에 미리 만들어 둔다.</summary>
    private static void PreCreateOpenXrSettings()
    {
        try
        {
            var t = Type.GetType("UnityEditor.XR.OpenXR.OpenXRPackageSettings, Unity.XR.OpenXR.Editor");
            var m = t?.GetMethod("GetOrCreateInstance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (m != null) { m.Invoke(null, null); AssetDatabase.SaveAssets(); }
        }
        catch (Exception e) { Debug.Log("[LobbyBuild] OpenXR 사전 생성 건너뜀: " + e.Message); }
    }

    // ══════════════════════════════════════════════════════
    //  배치 모드 진입점 (-executeMethod)
    // ══════════════════════════════════════════════════════
    public static void BatchBuildAllServers() => Batch(Profile.AllServers);
    public static void BatchBuildRoomServer() => Batch(Profile.RoomServer);
    public static void BatchBuildMasterServer() => Batch(Profile.MasterServer);
    public static void BatchBuildClient() => Batch(Profile.PcClient);

    private static void Batch(Profile p)
    {
        string dir = Environment.GetEnvironmentVariable("LOBBY_BUILD_DIR");
        if (!string.IsNullOrEmpty(dir)) OutputRoot = dir;
        bool ok;
        try
        {
            if (p == Profile.RoomServer || p == Profile.AllServers) { ok = BuildRoom(); if (!ok) { EditorApplication.Exit(1); return; } }
            if (p == Profile.MasterServer || p == Profile.AllServers) { ok = BuildMaster(); if (!ok) { EditorApplication.Exit(1); return; } }
            if (p == Profile.PcClient) { ok = BuildClient(); if (!ok) { EditorApplication.Exit(1); return; } }
        }
        catch (Exception e) { Debug.LogError("[LobbyBuild] " + e); EditorApplication.Exit(1); return; }
        EditorApplication.Exit(0);
    }
}
#endif
