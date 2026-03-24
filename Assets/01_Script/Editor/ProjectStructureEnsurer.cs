using UnityEditor;
using UnityEngine;

public static class ProjectStructureEnsurer
{
    private const string ROOT = "Assets";

    private static readonly string[] FolderPaths =
    {
        "Assets/00_Scene",
        "Assets/00_Scene/01_Main",

        "Assets/01_Script",
        "Assets/01_Script/01_Sample",

        "Assets/02_UI",
        "Assets/02_UI/01_Sample",

        "Assets/03_3D",
        "Assets/03_3D/3DScene",
        "Assets/03_3D/Character",
        "Assets/03_3D/Character/C_Materials",
        "Assets/03_3D/Character/C_Textures",
        "Assets/03_3D/Character/C_FBX",
        "Assets/03_3D/Character/C_Prefabs",
        "Assets/03_3D/Character/C_Animations",

        "Assets/03_3D/Environment",
        "Assets/03_3D/Environment/E_Materials",
        "Assets/03_3D/Environment/E_Textures",
        "Assets/03_3D/Environment/E_FBX",
        "Assets/03_3D/Environment/E_Prefabs",
        "Assets/03_3D/Environment/E_Animations",

        "Assets/03_3D/Object",
        "Assets/03_3D/Object/O_Materials",
        "Assets/03_3D/Object/O_Textures",
        "Assets/03_3D/Object/O_FBX",
        "Assets/03_3D/Object/O_Prefabs",
        "Assets/03_3D/Object/O_Animations",

        "Assets/04_2D",
        "Assets/04_2D/CharacterSprite",
        "Assets/04_2D/ObjectSprite",

        "Assets/05_Font",
        "Assets/06_ETC"
    };

    [MenuItem("Tools/Setup/Ensure Project Structure")]
    public static void EnsureStructure()
    {
        int createdCount = 0;

        foreach(var path in FolderPaths)
        {
            if(!AssetDatabase.IsValidFolder(path))
            {
                CreateFolderRecursive(path);
                createdCount++;
                Debug.Log($"Created: {path}");
            }
            else
            {
                Debug.Log($"Already Created: {path}");
            }
        }

        AssetDatabase.Refresh();

        Debug.Log($"Project Structure Check Complete. Created {createdCount} folders.");
    }

    private static void CreateFolderRecursive(string fullPath)
    {
        string[] parts = fullPath.Split('/');
        string current = parts[0]; // "Assets"

        for(int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];

            if(!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
                Debug.Log($"Created: {next}");
            }
            else
            {
                Debug.Log($"Already Created: {next}");
            }

            current = next;
        }
    }
}