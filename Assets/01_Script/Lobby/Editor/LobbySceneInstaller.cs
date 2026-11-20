using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// ============================================================
//  LobbySceneInstaller.cs — 로비 템플릿 씬을 이 프로젝트에 설치 (모듈화, 2026-11-20)
//
//  메뉴: Tools ▸ DoDrill ▸ 로비 템플릿 씬 설치
//  Lobby/Scenes 의 템플릿(Client·Master·Spawner·Room)을 Assets/Scenes/Lobby 로 복사하고
//   · FishNet DefaultScene 의 오프라인/온라인 씬 경로를 설치된 경로로 맞춘다(온라인 = LobbySettings.gameSceneName 과 이름이 같은 씬)
//   · 클라이언트 씬(Client)과 게임 씬을 빌드 설정에 넣는다
//  이미 있는 파일은 덮어쓰지 않는다. 서버 주소는 템플릿에서 127.0.0.1 이므로 씬의 ClientToMasterConnector.serverIp 를 바꿔 쓴다(README).
// ============================================================
public static class LobbySceneInstaller
{
    private const string SourceDir = "Assets/01_Script/Lobby/Scenes";
    private const string TargetDir = "Assets/Scenes/Lobby";
    private static readonly string[] SceneNames = { "Client", "Master", "Spawner", "Room" };

    [MenuItem("Tools/DoDrill/로비 템플릿 씬 설치")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("로비 템플릿", "플레이 중에는 설치할 수 없습니다.", "확인");
            return;
        }
        // 로비 UI 문구(UI_Text 테이블)를 쓰려면 Localization 설정·Addressables 항목이 필요하다
        LocalizationInstaller.Install(true);

        EnsureFolder("Assets/Scenes");
        EnsureFolder(TargetDir);

        var installed = new Dictionary<string, string>();
        foreach (var n in SceneNames)
        {
            string src = $"{SourceDir}/{n}.unity", dst = $"{TargetDir}/{n}.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(src) == null) { Debug.LogWarning($"[LobbyInstaller] 템플릿 없음: {src}"); continue; }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(dst) != null) { Debug.Log($"[LobbyInstaller] 이미 있음(건너뜀): {dst}"); installed[n] = dst; continue; }
            if (AssetDatabase.CopyAsset(src, dst)) { installed[n] = dst; Debug.Log($"[LobbyInstaller] 설치: {dst}"); }
            else Debug.LogError($"[LobbyInstaller] 복사 실패: {src} → {dst}");
        }

        string gamePath = FindGameScene();
        FixDefaultScene(installed.GetValueOrDefault("Client"), installed.GetValueOrDefault("Client"), gamePath);
        FixDefaultScene(installed.GetValueOrDefault("Room"), installed.GetValueOrDefault("Client"), gamePath);
        AddToBuildSettings(installed.GetValueOrDefault("Client"), gamePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[LobbyInstaller] 완료. 게임 씬: {(gamePath ?? "없음 — LobbySettings.gameSceneName 과 같은 이름의 씬을 만든 뒤 다시 실행")}. 서버 주소는 씬의 ClientToMasterConnector 에서 바꾸세요.");
    }

    private static string FindGameScene()
    {
        string name = LobbySettings.Current.gameSceneName;
        foreach (var g in AssetDatabase.FindAssets($"{name} t:Scene"))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (System.IO.Path.GetFileNameWithoutExtension(p) == name && !p.StartsWith("Assets/01_Script/Lobby/")) return p;
        }
        return null;
    }

    /// <summary>씬 안의 FishNet DefaultScene(_offlineScene·_onlineScene)을 설치 경로로 바꾼다(타입 참조 없이 이름으로 찾음).</summary>
    private static void FixDefaultScene(string scenePath, string offline, string online)
    {
        if (string.IsNullOrEmpty(scenePath)) return;
        var sc = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        bool dirty = false;
        foreach (var r in sc.GetRootGameObjects())
            foreach (var c in r.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c.GetType().Name != "DefaultScene") continue;
                var so = new SerializedObject(c);
                var off = so.FindProperty("_offlineScene"); var on = so.FindProperty("_onlineScene");
                if (off != null && !string.IsNullOrEmpty(offline) && off.stringValue != offline) { off.stringValue = offline; dirty = true; }
                if (on != null && !string.IsNullOrEmpty(online) && on.stringValue != online) { on.stringValue = online; dirty = true; }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        if (dirty) { EditorSceneManager.MarkSceneDirty(sc); EditorSceneManager.SaveScene(sc); Debug.Log($"[LobbyInstaller] DefaultScene 경로 보정: {scenePath}"); }
        EditorSceneManager.CloseScene(sc, true);
    }

    private static void AddToBuildSettings(string clientPath, string gamePath)
    {
        var list = EditorBuildSettings.scenes.ToList();
        void Add(string p)
        {
            if (string.IsNullOrEmpty(p) || list.Any(s => s.path == p)) return;
            list.Add(new EditorBuildSettingsScene(p, true));
            Debug.Log($"[LobbyInstaller] 빌드 설정에 추가: {p}");
        }
        Add(clientPath); Add(gamePath);
        EditorBuildSettings.scenes = list.ToArray();
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(folder).Replace('\\', '/'), System.IO.Path.GetFileName(folder));
    }
}
