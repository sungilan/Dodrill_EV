using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FishNet.Broadcast;
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Object.Synchronizing.Internal;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 빌드할 때 네트워크 계약을 해시해 Resources 에 기록한다 (BuildCompat 이 접속 시 비교, N-8).
/// 사람이 올리는 버전 번호는 잊기 쉬우므로 실제로 서버·클라이언트가 맞아야 하는 것만 코드에서 읽는다.
/// 해시가 바뀌면 그 커밋의 network_protocol.txt 변경으로 '서버 재배포 필요'가 드러난다.
/// </summary>
public class NetworkProtocolHash : IPreprocessBuildWithReport
{
    private const string OutputPath = "Assets/Resources/network_protocol.txt";
    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    // FishNet 의 프리팹 목록 자동 갱신이 끝난 뒤에 읽어야 한다
    public int callbackOrder => int.MaxValue;

    public void OnPreprocessBuild(BuildReport report)
    {
        Refresh();
    }

    [MenuItem("Tools/Dodrill/네트워크 계약 해시 갱신")]
    private static void RefreshFromMenu()
    {
        Debug.Log($"[NetworkProtocolHash] {Refresh()}");
    }

    public static string Refresh()
    {
        string hash = Compute();
        string current = File.Exists(OutputPath) ? File.ReadAllText(OutputPath).Trim() : string.Empty;
        if(hash != current)
        {
            File.WriteAllText(OutputPath, hash);
            AssetDatabase.ImportAsset(OutputPath);
            Debug.Log($"[NetworkProtocolHash] 네트워크 계약 변경 {current} → {hash}. 서버와 전 플랫폼을 다시 빌드해야 합니다.");
        }
        return hash;
    }

    public static string Compute()
    {
        var bytes = Encoding.UTF8.GetBytes(BuildManifest());
        using(var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(bytes), 0, 4).Replace("-", string.Empty).ToLowerInvariant();
    }

    /// <summary>해시 입력 전체. 해시가 예상과 다르게 바뀌면 이 내용을 비교한다.</summary>
    public static string BuildManifest()
    {
        var lines = new List<string>();

        foreach(var type in TypeCache.GetTypesDerivedFrom<NetworkBehaviour>().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            var members = new List<string>();
            foreach(var method in type.GetMethods(Declared))
            {
                string kind = RpcKind(method);
                if(kind == null) continue;
                string args = string.Join(",", method.GetParameters().Select(p => p.ParameterType.ToString()));
                members.Add($"{kind} {method.Name}({args})");
            }
            foreach(var field in type.GetFields(Declared))
            {
                if(typeof(SyncBase).IsAssignableFrom(field.FieldType))
                    members.Add($"sync {field.FieldType} {field.Name}");
            }

            if(members.Count == 0) continue;
            members.Sort(StringComparer.Ordinal);
            lines.Add(type.FullName);
            lines.AddRange(members.Select(m => "  " + m));
        }

        foreach(var type in TypeCache.GetTypesDerivedFrom<IBroadcast>().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public)
                             .Select(f => $"{f.FieldType} {f.Name}")
                             .OrderBy(f => f, StringComparer.Ordinal);
            lines.Add($"broadcast {type.FullName}: {string.Join(";", fields)}");
        }

        // 프리팹 순서가 FishNet 의 프리팹 ID 이므로 정렬하지 않는다
        foreach(var guid in AssetDatabase.FindAssets("t:" + nameof(DefaultPrefabObjects)).OrderBy(g => g, StringComparer.Ordinal))
        {
            var list = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(AssetDatabase.GUIDToAssetPath(guid));
            for(int i = 0; i < list.GetObjectCount(); i++)
            {
                var prefab = list.GetObject(true, i);
                if(prefab == null) { lines.Add($"prefab {i}: null"); continue; }
                string behaviours = string.Join(",", prefab.GetComponentsInChildren<NetworkBehaviour>(true).Select(b => b.GetType().Name));
                lines.Add($"prefab {i} {prefab.name}: {behaviours}");
            }
        }

        return string.Join("\n", lines);
    }

    private static string RpcKind(MethodInfo method)
    {
        if(method.IsDefined(typeof(ServerRpcAttribute), false)) return "server";
        if(method.IsDefined(typeof(ObserversRpcAttribute), false)) return "observers";
        if(method.IsDefined(typeof(TargetRpcAttribute), false)) return "target";
        return null;
    }
}
