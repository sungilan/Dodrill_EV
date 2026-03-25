#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AdaptiveGI.Editor
{
    [CustomEditor(typeof(AdaptiveGI))]
    public class AdaptiveGIEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            bool needsInitialized = false;

            _ = serializedObject.UpdateIfRequiredOrScript();
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                using (new EditorGUI.DisabledScope("m_Script" == iterator.propertyPath))
                {
                    bool hasSerializeField = false;
                    FieldInfo field = GetFieldInfo(iterator);
                    if (field != null)
                    {
                        if (field.GetCustomAttribute<SerializeField>() != null)
                        {
                            hasSerializeField = true;
                        }
                    }
                    EditorGUI.BeginChangeCheck();
                    _ = EditorGUILayout.PropertyField(iterator, true);
                    if (hasSerializeField && EditorGUI.EndChangeCheck())
                    {
                        needsInitialized = true;
                    }
                }

                enterChildren = false;
            }

            _ = serializedObject.ApplyModifiedProperties();

            if (needsInitialized && AdaptiveGI.Instance != null)
            {
                AdaptiveGI.Instance.Initialize();
            }
        }

        private static FieldInfo GetFieldInfo(SerializedProperty iterator)
        {
            if (iterator == null)
            {
                return null;
            }

            System.Type t = iterator.serializedObject.targetObject.GetType();

            FieldInfo f = null;
            foreach (string name in iterator.propertyPath.Split('.'))
            {
                f = t.GetField(name, (BindingFlags)(-1));

                if (f == null)
                {
                    PropertyInfo p = t.GetProperty(name, (BindingFlags)(-1));
                    if (p == null)
                    {
                        return null;
                    }
                    t = p.PropertyType;
                }
                else
                {
                    t = f.FieldType;
                }
            }

            return f;
        }

        [MenuItem("GameObject/Adaptive GI/Adaptive GI", priority = 4, secondaryPriority = 1)]
        private static void CreateAdaptiveGI(MenuCommand menuCommand)
        {
            GameObject go = new("Adaptive GI");
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _ = go.AddComponent<AdaptiveGI>();
            if (go == null)
            {
                return;
            }

            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }
    }
}
#endif