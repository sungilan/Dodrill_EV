using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace petabytes.LTCLight.Editor
{
    [CustomEditor(typeof(LTCTexturedLight))]
    public class LTCTexturedLightEditor : LTCLightEditor
    {
        private SerializedProperty doubleSided;
        private SerializedProperty width;
        private SerializedProperty height;
        private SerializedProperty texture;
        private SerializedProperty isRealTime;
        private SerializedProperty barnDoorAngle;
        private SerializedProperty barnDoorLength;
        
        public override void OnEnable()
        {
            base.OnEnable();
            doubleSided = serializedObject.FindProperty("doubleSided_");
            width = serializedObject.FindProperty("_width");
            height = serializedObject.FindProperty("_height");
            texture = serializedObject.FindProperty("_texture");
            isRealTime = serializedObject.FindProperty("_isRealTime");
            barnDoorAngle = serializedObject.FindProperty("_barnDoorAngle");
            barnDoorLength = serializedObject.FindProperty("_barnDoorLength");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            base.OnInspectorGUI();
            EditorGUILayout.PropertyField(doubleSided, new GUIContent("Double Sided"));
            EditorGUILayout.PropertyField(isRealTime, new GUIContent("Is Real Time"));
            EditorGUILayout.PropertyField(texture, new GUIContent("Texture"));
            
            float newValue = EditorGUILayout.FloatField("Width", width.floatValue);
            if (newValue > 0)
            {
                width.floatValue = newValue;
            }
            
            newValue = EditorGUILayout.FloatField("Height", height.floatValue);
            if (newValue > 0)
            {
                height.floatValue = newValue;                
            }
            
            newValue = EditorGUILayout.FloatField("Barn Door Angle", barnDoorAngle.floatValue);
            if (newValue >= 0 && newValue <= 90)
            {
                barnDoorAngle.floatValue = newValue;
            }
            
            newValue = EditorGUILayout.FloatField("Barn Door Length", barnDoorLength.floatValue);
            if (newValue >= 0)
            {
                barnDoorLength.floatValue = newValue;
            }
            
            if (serializedObject.hasModifiedProperties)
            {
                Undo.RecordObject(target, "Modify LTCTexturedLight");
                ReassignSetters(true);
            }
            serializedObject.ApplyModifiedProperties();
        }

        protected override void ReassignSetters(bool fromSo = false)
        {
            base.ReassignSetters(fromSo);
            var target = serializedObject.targetObject as LTCTexturedLight;
            if (fromSo)
            {
                target.DoubleSided = doubleSided.boolValue;
                target.Width = width.floatValue;
                target.Height = height.floatValue;
                target.Texture = texture.objectReferenceValue as Texture;
                target.IsRealTime = isRealTime.boolValue;
                target.BarnDoorAngle = barnDoorAngle.floatValue;
                target.BarnDoorLength = barnDoorLength.floatValue;
            }
            else
            {
                target.DoubleSided = target.DoubleSided;
                target.Width = target.Width;
                target.Height = target.Height;
                target.Texture = target.Texture;
                target.IsRealTime = target.IsRealTime;
                target.BarnDoorAngle = target.BarnDoorAngle;
                target.BarnDoorLength = target.BarnDoorLength;
            }
        }

        protected override void UndoRedoCallback()
        {
            ReassignSetters();
        }
    }
    
}
