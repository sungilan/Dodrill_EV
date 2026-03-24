using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace petabytes.LTCLight.Editor
{
    [CustomEditor(typeof(LTCConfig))]
    public class LTCConfigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            
            if (GUILayout.Button("Regenerate Config HLSL"))
            {
                LTCConfig.instance.GenerateConfigHLSL();                
            }
        }
    }
}