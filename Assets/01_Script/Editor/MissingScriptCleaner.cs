using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;

public class MissingScriptCleaner
{
    [MenuItem("Tools/Clean Missing Scripts In Current Scene")]
    public static void CleanMissingScripts()
    {
        int totalRemoved = 0;

        // 현재 씬 가져오기
        var scene = SceneManager.GetActiveScene();
        var rootObjects = scene.GetRootGameObjects();

        foreach(var root in rootObjects)
        {
            totalRemoved += RemoveMissingScriptsRecursive(root);
        }

        Debug.Log($"Missing Script 제거 완료. 총 {totalRemoved}개 제거됨.");
    }

    private static int RemoveMissingScriptsRecursive(GameObject obj)
    {
        int removed = 0;

        // 현재 오브젝트에서 Missing Script 제거
        removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(obj);

        // 자식들 순회
        foreach(Transform child in obj.transform)
        {
            removed += RemoveMissingScriptsRecursive(child.gameObject);
        }

        return removed;
    }
}