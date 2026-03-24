using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Extentions
{
    public static string GetHierarchyPath(this Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    public static T GetOrAddComponent<T>(this GameObject go) where T : Component
    {
        T obj = go.GetComponent<T>();
        if (obj != null)
            return obj;
        else
        {
            return go.AddComponent<T>();
        }
    }
}
