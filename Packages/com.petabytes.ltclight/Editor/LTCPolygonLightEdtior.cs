using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace petabytes.LTCLight.Editor
{
    [CustomEditor(typeof(LTCPolygonLight))]
    class LTCPolygonLightInpsector : LTCLightEditor
    {
        private SerializedProperty polygonPreset;
        private  SerializedProperty doubleSided;
        private SerializedProperty polygonPoints;

        public override void OnEnable()
        {
            base.OnEnable();
            polygonPreset = serializedObject.FindProperty("polygonPreset_");
            doubleSided = serializedObject.FindProperty("doubleSided_");
            polygonPoints =  serializedObject.FindProperty("polygonPoints_");
        }
        
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(polygonPreset, new GUIContent("Polygon Preset"));
            bool polygonPresetChanged = EditorGUI.EndChangeCheck();
            
            
            var target = serializedObject.targetObject as LTCPolygonLight;
            if (target.Preset == LTCPolygonLight.PolygonPreset.Custom)
            {
                EditorGUILayout.PropertyField(polygonPoints, new GUIContent("Polygon Light Vertices"));
            }
            
            base.OnInspectorGUI();
            EditorGUILayout.PropertyField(doubleSided, new GUIContent("Double Sided"));
            if (serializedObject.hasModifiedProperties)
            {
                Undo.RecordObject(target, "Modify LTCPolygonLight");
                ReassignSetters(true);
            }
            serializedObject.ApplyModifiedProperties();
            
            // Must do this after serializedObject.ApplyModifiedProperties, otherwise polygonVertices will be overriden.
            if (polygonPresetChanged && target.Preset != LTCPolygonLight.PolygonPreset.Custom)
                target.Preset = (LTCPolygonLight.PolygonPreset)polygonPreset.enumValueIndex;
        }

        protected override void ReassignSetters(bool fromSo = false)
        {
            base.ReassignSetters(fromSo);
            if (fromSo)
            {
                var target = serializedObject.targetObject as LTCPolygonLight;
                target.DoubleSided = doubleSided.boolValue;
                if ((LTCPolygonLight.PolygonPreset)polygonPreset.enumValueIndex == LTCPolygonLight.PolygonPreset.Custom)
                {
                    List<Vector3> pointsTmp = new List<Vector3>(polygonPoints.arraySize);
                    for (int i = 0; i < polygonPoints.arraySize; i++)
                    {
                        pointsTmp.Add(polygonPoints.GetArrayElementAtIndex(i).vector3Value);
                    }
                    target.PolygonPoints = pointsTmp;
                }
            }
            else
            {
                var target = serializedObject.targetObject as LTCPolygonLight;
                target.DoubleSided = target.DoubleSided;
                if (target.Preset == LTCPolygonLight.PolygonPreset.Custom)
                {
                    target.PolygonPoints = target.PolygonPoints;
                }
            }
        }

        protected override void UndoRedoCallback()
        {
            ReassignSetters();
            var target = serializedObject.targetObject as LTCPolygonLight;
            target.Preset = target.Preset;
        }
    }
}
