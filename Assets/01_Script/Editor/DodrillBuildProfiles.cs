#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 빌드 전환 툴 — Tools/Dodrill/빌드 프로필.
/// 공군(AFXR) 프로젝트의 빌드 프로필 창과 같은 방식: 버튼 하나로 플랫폼·씬·XR·그래픽 API·앱 이름을 바꾸고(선택하면 빌드까지).
///  · PC 클라이언트 : Client+Game, Windows 64bit — 실행 시 헤드셋이 없으면 PC 플레이어로 동작
///  · VR (Quest)    : Client+Game, Android, OpenXR 켬 + 시작 시 XR 초기화 켬, Vulkan
///  · 모바일(폰)    : Client+Game, Android, OpenXR 끔 + 시작 시 XR 초기화 끔, Vulkan(+GLES3 대체), 가로 고정
///  · 마스터 서버   : Master+Client+Room+Spawner, Linux Dedicated Server — 로비/매치메이킹/스포너, MasterServer.x86_64
///  · 룸 서버       : Room+Game, Linux Dedicated Server — FishNet이 방마다 띄우는 전용 프로세스, RoomServer.x86_64
/// 설정은 프로젝트에 그대로 남는다(빌드 후 되돌리지 않음) — 지금 어떤 상태인지는 창 위쪽에 표시.
///
/// ⚠ 서버와 클라이언트는 같은 프로젝트 상태로 빌드해야 한다(RPC·SyncVar·네트워크 프리팹이 바뀌면 옛 서버와 접속이 안 된다).
///   → 네트워크 관련 수정 뒤에는 서버(마스터/룸)도 다시 빌드해서 올릴 것.
/// </summary>
public class DodrillBuildProfiles : EditorWindow
{
    private const string AppName = "DoDrill.EV";                  // 클라이언트(Player.log·저장 경로가 이 이름 기준: Remoshot/DoDrill.EV)
    // 패키지명: Android 는 이미 이 값, Standalone 은 이전 프로젝트 잔재(com.remoshot.XR-AirportSecurity)라 인수인계서에서 '잔재 ID'로 지적됨 → 함께 맞춘다
    private const string PackageId = "com.remoshot.DoDrill.EV";
    private const string MasterProductName = "DoDrill.EV_MasterServer";
    private const string RoomProductName = "DoDrill.EV_RoomServer";

    private enum Profile { PcClient, VrQuest, MobilePhone, MasterServer, RoomServer }

    private const string MasterScene = "Assets/00_Scene/Master.unity";
    private const string ClientScene = "Assets/00_Scene/Client.unity";
    private const string RoomScene = "Assets/00_Scene/Room.unity";
    private const string SpawnerScene = "Assets/00_Scene/Spawner.unity";
    private const string GameScene = "Assets/00_Scene/Game.unity";
    private const string OpenXRLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";

    // 빌드 결과 기본 위치 — 기존 작업 방식(바탕화면 Build_Dodrill 폴더)을 따른다
    private static string BuildRoot => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory), "Build_Dodrill");

    [MenuItem("Tools/Dodrill/빌드 프로필")]
    public static void Open() => GetWindow<DodrillBuildProfiles>("Dodrill 빌드 프로필").minSize = new Vector2(420, 520);

    private Vector2 _scroll;

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("현재 설정", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(CurrentSummary(), MessageType.None);
        EditorGUILayout.Space(4);
        DrawServerCheck();
        EditorGUILayout.Space(8);

        DrawProfile(Profile.PcClient, "PC 클라이언트 (Windows)",
            "Client + Game · Windows 64bit · " + AppName + "\n헤드셋이 없으면 PC 플레이어로 접속 — 서버가 새 빌드여야 접속됨");
        DrawProfile(Profile.VrQuest, "VR (Quest)",
            "Client + Game · Android · " + AppName + "\nOpenXR 켬 · 시작 시 XR 초기화 켬 · Vulkan");
        DrawProfile(Profile.MobilePhone, "모바일 (스마트폰)",
            "Client + Game · Android · " + AppName + "\nOpenXR 끔 · 시작 시 XR 초기화 끔 · Vulkan + GLES3 대체 · 가로 고정");
        DrawProfile(Profile.MasterServer, "마스터 서버 (Linux)",
            "Master 씬만 · Linux Dedicated Server (Master.unity 안에 Spawner 포함)\n" +
            "로비/매치메이킹/스포너 역할 — MasterServer.x86_64\n" +
            "실행: ./MasterServer.x86_64 -batchmode -nographics -mstStartMaster -mstMasterIp 0.0.0.0 -mstMasterPort <포트>");
        DrawProfile(Profile.RoomServer, "룸 서버 (Linux)",
            "Room + Game · Linux Dedicated Server · Master/Client/Spawner 없음\n" +
            "FishNet 이 방마다 띄우는 전용 프로세스 — RoomServer.x86_64, Room.unity 로 부팅 후 Game 씬 로드");
        EditorGUILayout.EndScrollView();
    }

    // ── 서버 주소·포트 점검 (씬 파일을 텍스트로 읽는다 — 씬을 열지 않음) ──
    private string _serverCheck; private bool _serverCheckWarn;

    private void OnEnable() { RefreshServerCheck(); }

    private void RefreshServerCheck() { _serverCheck = BuildServerCheck(out _serverCheckWarn); }

    private void DrawServerCheck()
    {
        if (_serverCheck == null) RefreshServerCheck();
        EditorGUILayout.LabelField("서버 주소·포트 점검 (인수인계서 기준)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(_serverCheck, _serverCheckWarn ? MessageType.Warning : MessageType.Info);
        if (GUILayout.Button("점검 새로고침", GUILayout.Width(110))) RefreshServerCheck();
    }

    private static string ReadScene(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; } catch { return null; }
    }

    /// <summary>Master/Spawner/Room/Client 씬에 저장된 마스터 주소·포트, 스포너의 RoomServer 경로를 모아 서로 맞는지 본다.</summary>
    public static string BuildServerCheck(out bool warn)
    {
        warn = false;
        var sb = new System.Text.StringBuilder();
        var ports = new HashSet<string>();
        foreach (var scenePath in new[] { MasterScene, SpawnerScene, RoomScene, ClientScene })
        {
            string name = Path.GetFileNameWithoutExtension(scenePath);
            string txt = ReadScene(scenePath);
            if (txt == null) { sb.AppendLine(name + ": 씬 파일 없음"); warn = true; continue; }

            var ips = Regex.Matches(txt, @"^\s*serverIp:\s*(\S+)", RegexOptions.Multiline).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
            var ps = Regex.Matches(txt, @"^\s*serverPort:\s*(\d+)", RegexOptions.Multiline).Cast<Match>().Select(m => m.Groups[1].Value)
                .Concat(Regex.Matches(txt, @"propertyPath: serverPort\s*\n\s*value:\s*(\d+)").Cast<Match>().Select(m => m.Groups[1].Value)).Distinct().ToList();
            foreach (var x in ps) ports.Add(x);
            sb.Append(name + ": 마스터 " + (ips.Count > 0 ? string.Join("/", ips) : "-") + ":" + (ps.Count > 0 ? string.Join("/", ps) : "-"));

            if (scenePath == ClientScene && ips.Any(i => i == "localhost" || i == "127.0.0.1"))
            { sb.Append("  ⚠ 클라이언트가 localhost 를 가리킴(배포 빌드면 서버 주소로)"); warn = true; }

            var exe = Regex.Match(txt, @"^\s*executableFilePath:\s*(\S+)", RegexOptions.Multiline);
            var machine = Regex.Match(txt, @"^\s*machineIp:\s*(\S+)", RegexOptions.Multiline);
            var maxp = Regex.Match(txt, @"^\s*maxProcesses:\s*(\d+)", RegexOptions.Multiline);
            if (exe.Success)
            {
                sb.Append("\n   스포너: " + machine.Groups[1].Value + " · " + exe.Groups[1].Value + " · 동시 룸 최대 " + maxp.Groups[1].Value);
                if (!exe.Groups[1].Value.EndsWith("/RoomServer.x86_64")) { sb.Append("  ⚠ 룸 서버 빌드 이름(RoomServer.x86_64)과 다름"); warn = true; }
            }
            sb.AppendLine();
        }
        if (ports.Count > 1) { sb.AppendLine("⚠ 마스터 포트가 씬마다 다릅니다: " + string.Join(", ", ports)); warn = true; }
        else if (ports.Count == 1) sb.AppendLine("마스터 포트 일치: " + ports.First() + "  ·  방화벽: 이 포트 + 룸 포트(마스터 할당) + HTTP");
        sb.AppendLine("서버는 바꾼 코드로 다시 빌드해 올려야 접속됩니다(RPC·네트워크 프리팹 변경 시).");
        return sb.ToString().TrimEnd();
    }

    private void DrawProfile(Profile p, string title, string desc)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(desc, EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                // 플랫폼 전환·빌드는 GUI 그리기 밖에서(창 그리는 도중 실행하면 "EndLayoutGroup" 오류)
                if (GUILayout.Button("설정 적용", GUILayout.Height(26)))
                {
                    EditorApplication.delayCall += () => { Apply(p); if (this != null) { ShowNotification(new GUIContent(title + " 설정 적용")); Repaint(); } };
                    GUIUtility.ExitGUI();
                }
                string buildLabel = IsServer(p) ? "설정 적용 + 서버 빌드" : p == Profile.PcClient ? "설정 적용 + 빌드" : "설정 적용 + APK 빌드";
                if (GUILayout.Button(buildLabel, GUILayout.Height(26)))
                {
                    EditorApplication.delayCall += () => { Apply(p); Build(p); };
                    GUIUtility.ExitGUI();
                }
            }
        }
        EditorGUILayout.Space(4);
    }

    private static bool IsServer(Profile p) => p == Profile.MasterServer || p == Profile.RoomServer;

    // ══════════════════════════════════════════════════════
    //  적용
    // ══════════════════════════════════════════════════════
    public static void ApplyPcClient() => Apply(Profile.PcClient);
    public static void ApplyRoomServer() => Apply(Profile.RoomServer);

    private static void Apply(Profile p)
    {
        if (Application.isPlaying) { EditorUtility.DisplayDialog("빌드 프로필", "플레이 모드를 끄고 실행하세요.", "확인"); return; }

        // 1) 플랫폼
        // 선택 그룹·대상·서브타깃은 "이미 그 플랫폼이어도" 항상 맞춘다 — AFXR 에서 active=Linux 인데 Build Settings 창의 선택 그룹이
        // Android 로 남아 Addressables(SBP) 빌드가 "Unable to build with the current configuration" 으로 실패한 적이 있다.
        if (IsServer(p))
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneLinux64)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64);
            EditorUserBuildSettings.selectedBuildTargetGroup = BuildTargetGroup.Standalone;
            EditorUserBuildSettings.selectedStandaloneTarget = BuildTarget.StandaloneLinux64;
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;
        }
        else if (p == Profile.PcClient)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            EditorUserBuildSettings.selectedBuildTargetGroup = BuildTargetGroup.Standalone;
            EditorUserBuildSettings.selectedStandaloneTarget = BuildTarget.StandaloneWindows64;
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
        }
        else
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            EditorUserBuildSettings.selectedBuildTargetGroup = BuildTargetGroup.Android;
        }

        // 2) 씬 — 켜고 끄기만 한다. ⚠ 씬 "순서"는 바꾸지 않는다(기존 순서: Master, Client, Room, Spawner, Game).
        // 룸 서버는 Room(마스터 등록)이 Game 보다 앞(인덱스 0)이어야 하는데, 순서를 바꾸면 Game 으로 바로 시작해 방 시작이 멈춘다(AFXR 261001).
        string[] scenes;
        switch (p)
        {
            // 인수인계서: "Linux 헤드리스 Master 빌드: Master.unity만 활성". Master.unity 안에 SpawnerBehaviour(machineIp·RoomServer 경로·maxProcesses)가
            // 이미 들어 있어 Spawner.unity 를 따로 넣을 필요가 없다(Spawner.unity 는 분리 기동용).
            case Profile.MasterServer: scenes = new[] { MasterScene }; break;
            case Profile.RoomServer: scenes = new[] { RoomScene, GameScene }; break;
            default: scenes = new[] { ClientScene, GameScene }; break;
        }
        var list = EditorBuildSettings.scenes.ToList();
        foreach (var s in list) s.enabled = scenes.Contains(s.path);
        foreach (var path in scenes)
            if (!list.Any(s => s.path == path))
            {
                if (!File.Exists(path)) { Debug.LogError($"[빌드 프로필] 씬 없음: {path}"); continue; }
                list.Add(new EditorBuildSettingsScene(path, true));
            }
        EditorBuildSettings.scenes = list.ToArray();

        // 패키지명 정리 (Standalone 잔재 ID 제거)
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, PackageId);
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageId);

        // 3) 서버 — Standalone XR 자동 초기화 끔(방어적) + productName 고정(이전 프로필 이름을 쓰면 Player.log 가 엉뚱한 폴더에 쓰임)
        if (IsServer(p))
        {
            SetStandaloneXrInit(false);
            PlayerSettings.productName = p == Profile.MasterServer ? MasterProductName : RoomProductName;
            AssetDatabase.SaveAssets();
            Debug.Log($"[빌드 프로필] {p} 적용 완료 (씬/플랫폼만 설정, Android 전용 설정은 건드리지 않음)");
            return;
        }

        // 4) PC 클라이언트 — XR 설정은 건드리지 않는다(헤드셋 없으면 초기화 실패 로그만 남고 PC 플레이어로 동작)
        PlayerSettings.productName = AppName;
        if (p == Profile.PcClient)
        {
            AssetDatabase.SaveAssets();
            Debug.Log($"[빌드 프로필] {p} 적용 완료");
            return;
        }

        // 5) Android — VR / 모바일
        bool vr = p == Profile.VrQuest;

        // 그래픽 API: VR 은 Vulkan 만, 모바일은 Vulkan 우선 + 지원 안 되는 기기용 GLES3 대체
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
            vr ? new[] { GraphicsDeviceType.Vulkan } : new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });

        // XR Plug-in Management (Android 탭)
        var gs = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
        if (gs == null) Debug.LogError("[빌드 프로필] XR Plug-in Management 의 Android 설정이 없습니다(Project Settings > XR Plug-in Management > Android 탭을 한 번 열어 주세요).");
        else
        {
            gs.InitManagerOnStart = vr;
            if (gs.Manager != null)
            {
                bool ok = vr
                    ? XRPackageMetadataStore.AssignLoader(gs.Manager, OpenXRLoader, BuildTargetGroup.Android)
                    : XRPackageMetadataStore.RemoveLoader(gs.Manager, OpenXRLoader, BuildTargetGroup.Android);
                if (!ok) Debug.LogWarning($"[빌드 프로필] OpenXR 로더 {(vr ? "켜기" : "끄기")} 결과가 false — 이미 그 상태이거나 실패(창의 현재 설정 확인).");
                EditorUtility.SetDirty(gs.Manager);
            }
            EditorUtility.SetDirty(gs);
        }

        // 인수인계서: Android(Quest VR) = ARM64 · IL2CPP. 다른 설정으로 흘러가지 않게 고정한다.
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft; // 가로 고정(VR 은 무관)
        EditorUserBuildSettings.buildAppBundle = false;
        AssetDatabase.SaveAssets();
        Debug.Log($"[빌드 프로필] {p} 적용 완료\n{CurrentSummary()}");
    }

    private static void SetStandaloneXrInit(bool on)
    {
        var sgs = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
        if (sgs != null) { sgs.InitManagerOnStart = on; EditorUtility.SetDirty(sgs); }
    }

    // ══════════════════════════════════════════════════════
    //  빌드
    // ══════════════════════════════════════════════════════
    private static void Build(Profile p)
    {
        if (Application.isPlaying) return;

        string folder, defName, ext;
        switch (p)
        {
            case Profile.PcClient: folder = BuildRoot; defName = AppName + ".exe"; ext = "exe"; break;
            case Profile.MasterServer: folder = Path.Combine(BuildRoot, "MasterServer"); defName = "MasterServer.x86_64"; ext = ""; break;
            case Profile.RoomServer: folder = Path.Combine(BuildRoot, "RoomServer"); defName = "RoomServer.x86_64"; ext = ""; break;
            case Profile.VrQuest: folder = Path.Combine(BuildRoot, "VR"); defName = "DoDrill.EV_VR.apk"; ext = "apk"; break;
            default: folder = Path.Combine(BuildRoot, "Mobile"); defName = "DoDrill.EV_Mobile.apk"; ext = "apk"; break;
        }
        Directory.CreateDirectory(folder);
        string path = EditorUtility.SaveFilePanel("빌드 저장 위치 (" + p + ")", folder, defName, ext);
        if (string.IsNullOrEmpty(path)) return;

        // 빌드 직전에 스크립트가 바뀌어 에디터가 옛 코드인 채로 빌드되면 앱 코드와 에셋 데이터가 어긋난다(AFXR 261006:
        // 실행 즉시 "sharedassets0.assets is corrupted"). 반영 안 된 변경이 있으면 먼저 컴파일시키고, 끝나면 같은 설정으로 자동 빌드한다.
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (EditorApplication.isCompiling)
        {
            SessionState.SetString(PendingBuildKey, $"{p}|{path}");
            Debug.LogWarning("[빌드 프로필] 바뀐 스크립트를 반영하는 중 — 컴파일이 끝나면 자동으로 빌드합니다. (빌드 중에는 스크립트를 수정하지 마세요)");
            return;
        }
        if (EditorUtility.scriptCompilationFailed)
        {
            EditorUtility.DisplayDialog("빌드 중단", "스크립트 컴파일 오류가 있어 빌드하지 않았습니다. 콘솔의 오류를 먼저 고쳐 주세요.", "확인");
            return;
        }
        RunBuild(p, path);
    }

    private const string PendingBuildKey = "DodrillBuildProfiles.PendingBuild";

    [InitializeOnLoadMethod]
    private static void ResumePendingBuild()
    {
        // AssetImportWorker 등 보조 프로세스에서는 아무것도 하지 않는다
        if (Application.isBatchMode || AssetDatabase.IsAssetImportWorkerProcess()) return;
        if (string.IsNullOrEmpty(SessionState.GetString(PendingBuildKey, ""))) return;
        EditorApplication.delayCall += TryRunPending;
    }

    private static void TryRunPending()
    {
        string pending = SessionState.GetString(PendingBuildKey, "");
        if (string.IsNullOrEmpty(pending)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += TryRunPending; return; }
        SessionState.EraseString(PendingBuildKey);
        var parts = pending.Split('|');
        if (parts.Length != 2 || !System.Enum.TryParse(parts[0], out Profile p)) return;
        if (EditorUtility.scriptCompilationFailed)
        {
            EditorUtility.DisplayDialog("빌드 중단", "스크립트 컴파일 오류가 있어 예약된 빌드를 하지 않았습니다.", "확인");
            return;
        }
        Debug.Log($"[빌드 프로필] 스크립트 반영 완료 — 예약된 {p} 빌드 시작");
        Apply(p); // 리로드로 바뀐 게 없도록 프로필 설정을 한 번 더 확인
        RunBuild(p, parts[1]);
    }

    private static void RunBuild(Profile p, string path)
    {
        bool server = IsServer(p);
        bool pc = p == Profile.PcClient;
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = path,
            target = server ? BuildTarget.StandaloneLinux64 : pc ? BuildTarget.StandaloneWindows64 : BuildTarget.Android,
            targetGroup = server || pc ? BuildTargetGroup.Standalone : BuildTargetGroup.Android,
            // 서버는 Dedicated Server 서브타깃으로 명시(안 주면 빌드 창 설정에 따라 일반 플레이어로 빌드될 수 있다)
            subtarget = server ? (int)StandaloneBuildSubtarget.Server : pc ? (int)StandaloneBuildSubtarget.Player : 0,
            // 안드로이드는 LZ4HC 압축(무압축은 data.unity3d 가 2GB 를 넘어 실패한 사례가 있다). 서버·PC 는 그대로.
            options = server || pc ? BuildOptions.None : BuildOptions.CompressWithLz4HC,
        });
        Debug.Log($"[빌드 프로필] {p} 빌드 결과: {report.summary.result} ({report.summary.totalSize / (1024 * 1024)} MB) → {path}");

        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            EditorUtility.RevealInFinder(path);
        else
        {
            // 실패 원인을 한 곳에 모아 보여 준다 (콘솔이 비워져도 찾을 수 있게)
            var errs = report.steps.SelectMany(s => s.messages)
                .Where(m => m.type == LogType.Error || m.type == LogType.Exception).Select(m => m.content).Take(5);
            Debug.LogError($"[빌드 프로필] {p} 빌드 실패 — 오류 {report.summary.totalErrors}건\n" + string.Join("\n", errs));
        }
    }

    private static string CurrentSummary()
    {
        var gsA = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
        string loaders = gsA != null && gsA.Manager != null ? string.Join(", ", gsA.Manager.activeLoaders.Select(l => l != null ? l.GetType().Name : "null")) : "-";
        if (string.IsNullOrEmpty(loaders)) loaders = "없음";
        return $"플랫폼: {EditorUserBuildSettings.activeBuildTarget} ({EditorUserBuildSettings.standaloneBuildSubtarget})\n" +
               $"씬: {string.Join(", ", EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => Path.GetFileNameWithoutExtension(s.path)))}\n" +
               $"이름: {PlayerSettings.productName}  /  패키지: {PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)}\n" +
               $"그래픽 API(Android): {string.Join(", ", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android))}\n" +
               $"XR 로더(Android): {loaders}  /  시작 시 XR 초기화: {(gsA != null && gsA.InitManagerOnStart ? "켬" : "끔")}\n" +
               $"화면 방향: {PlayerSettings.defaultInterfaceOrientation}";
    }
}
#endif
