#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditorInternal;
using UnityEngine;

// ============================================================
//  DoDrillSetup.cs — 새 프로젝트에 모듈을 가져온 직후 필요한 설정을 자동으로 맞춘다 (모듈화, 2026-11-20)
//
//  이 어셈블리(DoDrill.Setup.Editor)는 아무것도 참조하지 않는다 — 다른 모듈이 컴파일 오류(플러그인 asmdef 누락 등)로 막혀 있어도 컴파일되어 실행된다.
//  1) 플러그인 asmdef : 에셋스토어 Final IK 에는 asmdef 가 없다 → RootMotion / RootMotionEditor 를 만들어 준다.
//                       DOTween 은 Modules 폴더에 DOTween.Modules asmdef 를 만들어 준다(EPO 가 있을 때만 참조).
//  2) 태그            : HoldPoint, Anchor
//  3) 필수 패키지      : 없는 것만 한 번 물어보고 설치한다(메뉴로 다시 실행 가능)
//  메뉴: Tools ▸ DoDrill ▸ 가져오기 설정(asmdef·태그·패키지)
//  ※ FishNet · MasterServerToolkit · AutoHand 는 EV 수정본이 필요하다(별도 패키지 DoDrill_EV_Plugins).
// ============================================================
[InitializeOnLoad]
public static class DoDrillSetup
{
    private const string PromptedKey = "DoDrill.Setup.Prompted";

    // 이름, 버전 (EV 프로젝트 기준)
    private static readonly string[][] RequiredPackages =
    {
        new[] { "com.unity.localization", "1.5.11" },
        new[] { "com.unity.addressables", "2.9.0" },
        new[] { "com.unity.inputsystem", "1.18.0" },
        new[] { "com.unity.nuget.newtonsoft-json", "3.2.2" },
        new[] { "com.unity.collections", "2.6.2" },
        new[] { "com.unity.xr.management", "4.5.4" },
        new[] { "com.unity.xr.arfoundation", "6.4.0" },
        new[] { "com.unity.xr.openxr", "1.16.1" },
        new[] { "com.unity.xr.meta-openxr", "2.4.0" },
        new[] { "com.unity.xr.hands", "1.7.3" },
        new[] { "com.unity.xr.interaction.toolkit", "3.3.1" },
    };

    // 서버 빌드(Windows 에서 Linux 로)에만 필요 — 필수 목록에는 넣지 않고 안내만 한다
    private static readonly string[][] ServerBuildPackages =
    {
        new[] { "com.unity.sdk.linux-x86_64", "1.1.1" },
        new[] { "com.unity.toolchain.win-x86_64-linux", "1.1.1" },
    };

    static DoDrillSetup()
    {
        EditorApplication.delayCall += () => Run(false);
    }

    [MenuItem("Tools/DoDrill/가져오기 설정(asmdef·태그·패키지)")]
    public static void RunFromMenu() => Run(true);

    private static void Run(bool interactive)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        bool changed = false;
        changed |= EnsureFinalIkAsmdefs();
        changed |= EnsureDoTweenAsmdef();
        changed |= EnsureTags();
        changed |= EnsureServerOdinDefines();
        if (changed) AssetDatabase.Refresh();

        if (interactive || !SessionState.GetBool(PromptedKey, false))
        {
            SessionState.SetBool(PromptedKey, true);
            CheckPackages(interactive);
        }
    }

    // ── 1) asmdef ───────────────────────────────────────────
    private static List<string> AsmdefNames()
    {
        var names = new List<string>();
        foreach (var g in AssetDatabase.FindAssets("t:AssemblyDefinitionAsset"))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (!p.StartsWith("Assets/")) continue;
            try
            {
                string t = File.ReadAllText(p);
                int i = t.IndexOf("\"name\"", StringComparison.Ordinal);
                if (i < 0) continue;
                int a = t.IndexOf('"', t.IndexOf(':', i) + 1) + 1, b = t.IndexOf('"', a);
                names.Add(t.Substring(a, b - a));
            }
            catch { }
        }
        return names;
    }

    private static string FindScriptDir(string fileName)
    {
        foreach (var g in AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(fileName)))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (p.StartsWith("Assets/") && Path.GetFileName(p) == fileName) return Path.GetDirectoryName(p).Replace('\\', '/');
        }
        return null;
    }

    private static bool EnsureFinalIkAsmdefs()
    {
        var names = AsmdefNames();
        // Final IK: .../RootMotion/FinalIK/IK Solvers/VRIK.cs
        string dir = FindScriptDir("VRIK.cs");
        if (dir == null) return false;
        int i = dir.IndexOf("/RootMotion/", StringComparison.Ordinal);
        if (i < 0) return false;
        string root = dir.Substring(0, i) + "/RootMotion";
        bool changed = false;

        if (!names.Contains("RootMotion"))
        {
            // 최신 Final IK 데모 스크립트가 UnityEngine.InputSystem 을 쓴다
            bool input = true;   // InputSystem 패키지는 필수 목록에 있다(설치 전까지는 참조 경고가 날 수 있음)
            File.WriteAllText(root + "/RootMotion.Runtime.asmdef",
                input ? "{\n\t\"name\": \"RootMotion\",\n\t\"references\": [ \"Unity.InputSystem\" ]\n}\n" : "{\n\t\"name\": \"RootMotion\"\n}\n");
            Debug.Log("[DoDrillSetup] RootMotion asmdef 생성: " + root + "/RootMotion.Runtime.asmdef");
            changed = true;
        }
        if (!names.Contains("RootMotionEditor") && Directory.Exists(root + "/Editor"))
        {
            File.WriteAllText(root + "/Editor/RootMotion.Editor.asmdef",
                "{\n\t\"name\": \"RootMotionEditor\",\n\t\"references\": [ \"RootMotion\" ],\n\t\"includePlatforms\": [ \"Editor\" ],\n\t\"excludePlatforms\": []\n}\n");
            Debug.Log("[DoDrillSetup] RootMotionEditor asmdef 생성: " + root + "/Editor/RootMotion.Editor.asmdef");
            changed = true;
        }
        return changed;
    }

    private static bool EnsureDoTweenAsmdef()
    {
        var names = AsmdefNames();
        if (names.Contains("DOTween.Modules")) return false;
        string dir = FindScriptDir("DOTweenModuleUI.cs");
        if (dir == null) return false;
        var refs = new List<string> { "UnityEngine.UI", "Unity.TextMeshPro" };
        if (names.Contains("EPO")) refs.Add("EPO");   // EV 의 DOTweenModuleEPOOutline 용 — EPO 가 없는 프로젝트는 넣지 않는다
        File.WriteAllText(dir + "/DOTween.Modules.asmdef",
            "{\n  \"name\": \"DOTween.Modules\",\n  \"references\": [ " + string.Join(", ", refs.Select(r => "\"" + r + "\"")) + " ],\n" +
            "  \"overrideReferences\": true,\n  \"precompiledReferences\": [ \"DOTween.dll\" ],\n  \"autoReferenced\": true\n}\n");
        Debug.Log("[DoDrillSetup] DOTween.Modules asmdef 생성: " + dir);
        return true;
    }

    // ── 2) 태그 ─────────────────────────────────────────────
    private static bool EnsureTags()
    {
        bool changed = false;
        foreach (var tag in new[] { "HoldPoint", "Anchor" })
        {
            if (InternalEditorUtility.tags.Contains(tag)) continue;
            InternalEditorUtility.AddTag(tag);
            Debug.Log("[DoDrillSetup] 태그 추가: " + tag);
            changed = true;
        }
        return changed;
    }

    // ── 2-1) Odin 정의 심볼(Dedicated Server) ───────────────────
    // Odin 은 ODIN_INSPECTOR 를 Standalone·Android 에만 넣는다. Dedicated Server(NamedBuildTarget.Server)에는 없어서, 이 대상으로 전환하면
    // Common 의 OdinShim(Odin 이 없을 때만 쓰는 같은 이름 속성)이 켜져 'FolderPathAttribute 가 DoDrill.Common 과 Sirenix 양쪽에 있다'(CS0433)가 난다.
    // Odin 이 설치돼 있으면 서버 대상에도 같은 심볼을 넣어 준다.
    private const string OdinDefines = "ODIN_INSPECTOR;ODIN_INSPECTOR_3;ODIN_INSPECTOR_3_1;ODIN_INSPECTOR_3_2;ODIN_INSPECTOR_3_3";

    private static bool EnsureServerOdinDefines()
    {
        try
        {
            bool odin = false;
            foreach (var g in AssetDatabase.FindAssets("Sirenix.OdinInspector.Attributes"))
                if (AssetDatabase.GUIDToAssetPath(g).EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) { odin = true; break; }
            if (!odin) return false;
            string cur = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Server);
            if (cur.Contains("ODIN_INSPECTOR")) return false;
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Server, string.IsNullOrEmpty(cur) ? OdinDefines : cur + ";" + OdinDefines);
            Debug.Log("[DoDrillSetup] Dedicated Server 대상에 ODIN_INSPECTOR 심볼 추가(OdinShim 충돌 방지)");
            return true;
        }
        catch (Exception e) { Debug.Log("[DoDrillSetup] Odin 심볼 건너뜀: " + e.Message); return false; }
    }

    // ── 3) 패키지 ───────────────────────────────────────────
    private static ListRequest _list;
    private static bool _interactive;

    private static bool PackageInstalled(string name)
    {
        string lockPath = "Packages/packages-lock.json";
        try { return File.Exists(lockPath) && File.ReadAllText(lockPath).Contains("\"" + name + "\""); } catch { return false; }
    }

    private static void CheckPackages(bool interactive)
    {
        _interactive = interactive;
        _list = Client.List(true);
        EditorApplication.update += PollList;
    }

    private static void PollList()
    {
        if (_list == null || !_list.IsCompleted) return;
        EditorApplication.update -= PollList;
        if (_list.Status != StatusCode.Success) { Debug.LogWarning("[DoDrillSetup] 패키지 목록을 읽지 못했습니다."); return; }

        var have = _list.Result.Select(p => p.name).ToList();
        var missing = RequiredPackages.Where(p => !have.Contains(p[0])).ToList();
        var serverMissing = ServerBuildPackages.Where(p => !have.Contains(p[0])).ToList();

        if (missing.Count == 0)
        {
            Debug.Log("[DoDrillSetup] 필수 패키지 모두 설치됨" + (serverMissing.Count > 0 ? " (서버 빌드용 Linux 툴체인 2종은 선택: " + string.Join(", ", serverMissing.Select(p => p[0])) + ")" : ""));
            if (_interactive && serverMissing.Count > 0 &&
                EditorUtility.DisplayDialog("가져오기 설정", "서버를 Windows 에서 Linux 로 빌드하려면 다음 패키지도 필요합니다.\n" + string.Join("\n", serverMissing.Select(p => p[0] + " " + p[1])) + "\n\n지금 설치할까요?", "설치", "건너뜀"))
                Client.AddAndRemove(serverMissing.Select(p => p[0] + "@" + p[1]).ToArray());
            return;
        }

        string msg = "모듈에 필요한 패키지가 " + missing.Count + "개 없습니다:\n\n" + string.Join("\n", missing.Select(p => "  " + p[0] + " " + p[1])) +
                     "\n\n지금 설치할까요? (설치 중 Unity 가 잠시 멈추고 다시 컴파일됩니다)";
        if (EditorUtility.DisplayDialog("DoDrill 가져오기 설정", msg, "설치", "나중에"))
        {
            Debug.Log("[DoDrillSetup] 패키지 설치 요청: " + string.Join(", ", missing.Select(p => p[0])));
            Client.AddAndRemove(missing.Select(p => p[0] + "@" + p[1]).ToArray());
        }
        else Debug.LogWarning("[DoDrillSetup] 필수 패키지가 없어 컴파일 오류가 날 수 있습니다. 메뉴 Tools ▸ DoDrill ▸ 가져오기 설정 으로 다시 실행하세요.");
    }
}
#endif
