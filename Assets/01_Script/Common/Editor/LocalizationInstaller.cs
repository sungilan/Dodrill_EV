using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

// ============================================================
//  LocalizationInstaller.cs — Common/Localization 자산을 이 프로젝트의 Localization·Addressables 에 등록 (모듈화, 2026-11-20)
//
//  메뉴: Tools ▸ DoDrill ▸ Localization 설치   (로비 템플릿 설치 메뉴가 먼저 호출한다. 여러 번 실행해도 안전)
//  Common/Localization 에는 설정(Localization Settings)·로케일 3개·문자열 테이블(UI_Text, TaskNames)이 있다.
//  새 프로젝트에는 이 자산을 쓰도록 하는 프로젝트 설정(활성 Localization 설정)과 Addressables 항목이 없으므로 여기서 만든다.
//   1) Addressables 설정이 없으면 만든다
//   2) 활성 Localization 설정이 없으면 Common/Localization/Settings 의 것을 지정한다(이미 있으면 건드리지 않는다)
//   3) 로케일·공유 테이블 데이터·문자열 테이블을 Localization 규칙의 그룹/주소/라벨로 등록한다
//      Localization-Locales(주소=로케일 이름, 라벨 Locale) / Localization-Assets-Shared(주소=에셋 경로)
//      / Localization-String-Tables-<로케일 이름>(주소=테이블 이름, 라벨 Locale-<코드>)
//  EV 프로젝트에서 실행해도 이미 같은 상태라 변경이 없다.
// ============================================================
public static class LocalizationInstaller
{
    private const string Root = "Assets/01_Script/Common/Localization";

    [MenuItem("Tools/DoDrill/Localization 설치")]
    public static void InstallMenu() => Install(true);

    /// <summary>설치. 바뀐 항목 수를 돌려준다(0 이면 이미 설치된 상태).</summary>
    public static int Install(bool log = false)
    {
        if (!AssetDatabase.IsValidFolder(Root)) { Debug.LogWarning($"[LocalizationInstaller] {Root} 가 없습니다"); return 0; }
        int changed = 0;

        var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);   // 없으면 기본 설정(+Default Local Group)을 만든다
        if (settings == null) { Debug.LogError("[LocalizationInstaller] Addressables 설정을 만들지 못했습니다"); return 0; }

        var locSettings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>($"{Root}/Settings/Localization Settings.asset");
        if (locSettings != null && LocalizationEditorSettings.ActiveLocalizationSettings == null)
        {
            LocalizationEditorSettings.ActiveLocalizationSettings = locSettings;
            changed++;
            if (log) Debug.Log("[LocalizationInstaller] 활성 Localization 설정 지정");
        }

        var locales = AssetDatabase.FindAssets("t:Locale", new[] { Root }).Select(g => AssetDatabase.LoadAssetAtPath<Locale>(AssetDatabase.GUIDToAssetPath(g))).Where(l => l != null).ToList();
        foreach (var l in locales)
            changed += Register(settings, AssetDatabase.GetAssetPath(l), "Localization-Locales", l.name, new[] { "Locale" });

        foreach (var g in AssetDatabase.FindAssets("t:SharedTableData", new[] { Root }))
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            changed += Register(settings, path, "Localization-Assets-Shared", path, new string[0]);
        }

        foreach (var g in AssetDatabase.FindAssets("t:StringTable", new[] { Root }))
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            var table = AssetDatabase.LoadAssetAtPath<StringTable>(path);
            if (table == null) continue;
            string code = table.LocaleIdentifier.Code;
            var locale = locales.FirstOrDefault(l => l.Identifier.Code == code);
            string group = "Localization-String-Tables-" + (locale != null ? locale.name : code);
            changed += Register(settings, path, group, table.name, new[] { "Locale-" + code });
        }

        if (changed > 0) { EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets(); }
        if (log) Debug.Log($"[LocalizationInstaller] 완료 — 바뀐 항목 {changed}개 (0 이면 이미 설치됨)");
        return changed;
    }

    private static int Register(AddressableAssetSettings settings, string assetPath, string groupName, string address, string[] labels)
    {
        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        var group = settings.FindGroup(groupName) ?? settings.CreateGroup(groupName, false, false, false, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        var entry = settings.FindAssetEntry(guid);
        int changed = 0;
        if (entry == null || entry.parentGroup != group)
        {
            entry = settings.CreateOrMoveEntry(guid, group, false, false);
            changed++;
        }
        if (entry.address != address) { entry.SetAddress(address, false); changed++; }
        foreach (var label in labels)
            if (!entry.labels.Contains(label)) { entry.SetLabel(label, true, true, false); changed++; }
        return changed;
    }
}
