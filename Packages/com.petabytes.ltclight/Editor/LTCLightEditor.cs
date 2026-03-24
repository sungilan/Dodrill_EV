using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace petabytes.LTCLight.Editor
{
    public class LTCLightEditor : UnityEditor.Editor
    {
        protected SerializedProperty color;
        protected  SerializedProperty intensity;
        protected  SerializedProperty lightMode;
        protected  SerializedProperty useWorldPosition;
        protected  SerializedProperty range;

        public virtual void OnEnable()
        {
            color = serializedObject.FindProperty("color_");
            intensity = serializedObject.FindProperty("intensity_");
            lightMode = serializedObject.FindProperty("lightMode_");
            useWorldPosition = serializedObject.FindProperty("useWorldPosition_");
            range = serializedObject.FindProperty("range_");
            Undo.undoRedoPerformed += UndoRedoCallback;
        }

        public void OnDisable()
        {
            Undo.undoRedoPerformed -= UndoRedoCallback;
        }
        
        protected virtual void UndoRedoCallback()
        {
            
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.PropertyField(color, new GUIContent("Color"));
            Vector2 intense;
            intense.x = EditorGUILayout.FloatField("Specular Intensity", intensity.vector2Value.x);
            intense.y = EditorGUILayout.FloatField("Diffuse Intensity", intensity.vector2Value.y);
            intensity.vector2Value = intense;
            EditorGUILayout.PropertyField(range, new GUIContent("Range"));
            EditorGUILayout.PropertyField(lightMode, new GUIContent("Light Mode"));
            EditorGUILayout.PropertyField(useWorldPosition, new GUIContent("Use World Position"));
        }

        // Must change these Setters to toggle data update in LTCLightManager
        protected virtual void ReassignSetters(bool fromSo = false)
        {
            if (fromSo)
            {
                var target = serializedObject.targetObject as LTCLight;
                target.Color = color.colorValue;
                target.Intensity = intensity.vector2Value;
                target.LightMode = (LTCLightMode)lightMode.enumValueIndex;
                target.UseWorldPosition = useWorldPosition.boolValue;
                target.Range = range.floatValue;
            }
            else
            {
                var target = serializedObject.targetObject as LTCLight;
                target.Color = target.Color;
                target.Intensity = target.Intensity;
                target.LightMode = target.LightMode;
                target.UseWorldPosition = target.UseWorldPosition;
                target.Range = target.Range;
            }
        }
    }
}