#if UNITY_EDITOR && ADAPTIVE_GI_URP && UNITY_6000_0_OR_NEWER
using UnityEditor;
using UnityEngine;

namespace AdaptiveGI.Rendering.URP
{
    [CustomEditor(typeof(AdaptiveGIRendererFeature))]
    public class AdaptiveGIRendererFeatureEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            GUILayout.Label("This renderer feature only works with the deferred rendering path.");
            GUILayout.Label("Consult the documentation for more information.");
            base.OnInspectorGUI();
        }
    }
}
#endif
