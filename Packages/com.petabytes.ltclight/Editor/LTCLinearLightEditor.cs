using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace petabytes.LTCLight.Editor
{
    [CustomEditor(typeof(LTCLinearLight))]
    public class LTCLinearLightEditor : LTCLightEditor
    {
        private SerializedProperty startPoint_;
        private SerializedProperty endPoint_;
        private SerializedProperty radius_;

        public override void OnEnable()
        {
            base.OnEnable();
            startPoint_ = serializedObject.FindProperty("startPoint_");
            endPoint_ = serializedObject.FindProperty("endPoint_");
            radius_ = serializedObject.FindProperty("radius_");
        }
        
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            base.OnInspectorGUI();
            
            Vector3 newVal = EditorGUILayout.Vector3Field("Start Point",  startPoint_.vector3Value);
            if ((newVal - endPoint_.vector3Value).magnitude > 0.001f)
            {
                startPoint_.vector3Value = newVal;
            }
            
            newVal = EditorGUILayout.Vector3Field("End Point",  endPoint_.vector3Value);
            if ((newVal - startPoint_.vector3Value).magnitude > 0.001f)
            {
                endPoint_.vector3Value = newVal;
            }
            
            EditorGUILayout.PropertyField(radius_, new GUIContent("Radius"));

            if (serializedObject.hasModifiedProperties)
            {
                Undo.RecordObject(target, "Modify LTCLinearLight");
                ReassignSetters(true);
            }
            
            serializedObject.ApplyModifiedProperties();
        }

        protected override void ReassignSetters(bool fromSo = false)
        {
            base.ReassignSetters(fromSo);
            if (fromSo)
            {
                var target = serializedObject.targetObject as LTCLinearLight;
                target.StartPoint = startPoint_.vector3Value;
                target.EndPoint = endPoint_.vector3Value;
                target.Radius = radius_.floatValue;
            }
            else
            {
                var target = serializedObject.targetObject as LTCLinearLight;
                target.StartPoint = target.StartPoint;
                target.EndPoint = target.EndPoint;
                target.Radius = target.Radius;
            }
        }
        
        protected override void UndoRedoCallback()
        {
            ReassignSetters();
        }
    }
}
