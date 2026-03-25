#if UNITY_EDITOR
using AdaptiveGI.Core;
using UnityEditor;
using UnityEngine;

namespace AdaptiveGI.Editor
{
    [CustomEditor(typeof(AdaptiveLight))]
    [CanEditMultipleObjects]
    public class AdaptiveLightEditor : UnityEditor.Editor
    {
        private const float lightSpawnDistance = 23;

        public override void OnInspectorGUI()
        {
            _ = serializedObject.UpdateIfRequiredOrScript();
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            AdaptiveLightType lightType = AdaptiveLightType.Point;
            while (iterator.NextVisible(enterChildren))
            {
                using (new EditorGUI.DisabledScope("m_Script" == iterator.propertyPath))
                {
                    if (iterator.name == "type")
                    {
                        lightType = (AdaptiveLightType)iterator.enumValueIndex;
                    }

                    if (iterator.name == "innerOuterSpotAngle")
                    {
                        if (lightType == AdaptiveLightType.Spot)
                        {
                            float innerSpotAngle = iterator.vector2Value.x;
                            float outerSpotAngle = iterator.vector2Value.y;
                            GUILayout.Space(10);
                            EditorGUILayout.LabelField("Shape", EditorStyles.boldLabel);
                            GUILayout.Label(new GUIContent($"Inner / Outer Spot Angle ({Mathf.Round(innerSpotAngle)}° to {Mathf.Round(outerSpotAngle)}°)", iterator.tooltip));
                            EditorGUILayout.MinMaxSlider(ref innerSpotAngle, ref outerSpotAngle, 0, 180);
                            iterator.vector2Value = new Vector2(innerSpotAngle, outerSpotAngle);
                        }
                        continue;
                    }
                    if (iterator.name == "conePower" && lightType != AdaptiveLightType.Spot)
                    {
                        continue;
                    }
                    _ = EditorGUILayout.PropertyField(iterator, true);
                }

                enterChildren = false;
            }

            _ = serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            AdaptiveLight adaptiveLight = (AdaptiveLight)target;
            Handles.color = adaptiveLight.Color;
            if (adaptiveLight.Type == AdaptiveLightType.Spot)
            {
                Vector3 center = adaptiveLight.transform.position;
                Vector3 normal = adaptiveLight.transform.up;
                {
                    Vector3 from = Quaternion.AngleAxis(-adaptiveLight.InnerOuterSpotAngle.y / 2, adaptiveLight.transform.up) * adaptiveLight.transform.forward;

                    Vector3 startPoint = center + (from * adaptiveLight.Range);
                    Vector3 endPoint = center + (Quaternion.AngleAxis(adaptiveLight.InnerOuterSpotAngle.y, normal) * from * adaptiveLight.Range);

                    Handles.DrawWireArc(center, normal, from, adaptiveLight.InnerOuterSpotAngle.y, adaptiveLight.Range);
                    Handles.DrawLine(center, startPoint);
                    Handles.DrawLine(center, endPoint);
                }
                {
                    Vector3 from = Quaternion.AngleAxis(-adaptiveLight.InnerOuterSpotAngle.x / 2, adaptiveLight.transform.up) * adaptiveLight.transform.forward;

                    Vector3 startPoint = center + (from * adaptiveLight.Range);
                    Vector3 endPoint = center + (Quaternion.AngleAxis(adaptiveLight.InnerOuterSpotAngle.x, normal) * from * adaptiveLight.Range);

                    Handles.DrawLine(center, startPoint);
                    Handles.DrawLine(center, endPoint);
                }
            }
        }

        [MenuItem("GameObject/Adaptive GI/Adaptive Point Light", priority = 4, secondaryPriority = 2)]
        private static void CreateAdaptivePointLight(MenuCommand menuCommand)
        {
            GameObject go = new("Adaptive Point Light");
            PositionLight(go);
            AdaptiveLight adaptiveLight = go.AddComponent<AdaptiveLight>();
            adaptiveLight.Type = AdaptiveLightType.Point;
            adaptiveLight.InnerOuterSpotAngle = new Vector2(180, 180);
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }

        [MenuItem("GameObject/Adaptive GI/Adaptive Spot Light", priority = 4, secondaryPriority = 3)]
        private static void CreateAdaptiveSpotLight(MenuCommand menuCommand)
        {
            GameObject go = new("Adaptive Spot Light");
            PositionLight(go);
            AdaptiveLight adaptiveLight = go.AddComponent<AdaptiveLight>();
            adaptiveLight.Type = AdaptiveLightType.Spot;
            adaptiveLight.InnerOuterSpotAngle = new Vector2(20, 30);
            adaptiveLight.Range = 5;
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            go.transform.rotation = Quaternion.Euler(90, 0, 0);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }

        private static void PositionLight(GameObject go)
        {
            if (EditorPrefs.HasKey("Create3DObject.PlaceAtWorldOrigin") && EditorPrefs.GetBool("Create3DObject.PlaceAtWorldOrigin"))
            {
                go.transform.position = Vector3.zero;
            }
            else if (SceneView.lastActiveSceneView != null && SceneView.lastActiveSceneView.camera != null)
            {
                Transform editorCam = SceneView.lastActiveSceneView.camera.transform;
                go.transform.position = editorCam.position + (editorCam.forward * lightSpawnDistance);
            }
        }
    }
}
#endif
