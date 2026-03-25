#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace AdaptiveGI.Editor
{
    public class ConvertMaterialsTool
    {
        const string URP_LIT = "Universal Render Pipeline/Lit";
        const string URP_TERRAIN_LIT = "Universal Render Pipeline/Terrain/Lit";
        const string URP_AUTODESK_AUTODESK = "Universal Render Pipeline/Autodesk Interactive/AutodeskInteractive";
        const string URP_AUTODESK_AUTODESK_MASKED = "Universal Render Pipeline/Autodesk Interactive/AutodeskInteractiveMasked";
        const string URP_AUTODESK_AUTODESK_TRANSPARENT = "Universal Render Pipeline/Autodesk Interactive/AutodeskInteractiveTransparent";
        const string URP_DECAL = "Shader Graphs/Decal";

        const string ADAPTIVEGI_LIT = "AdaptiveGI/Lit";
        const string ADAPTIVEGI_OPTIMIZED_LIT = "AdaptiveGI/Optimized Lit";
        const string ADAPTIVEGI_TERRAIN_LIT = "AdaptiveGI/Terrain/Lit";
        const string ADAPTIVEGI_AUTODESK_AUTODESK = "AdaptiveGI/Autodesk Interactive/AutodeskInteractive";
        const string ADAPTIVEGI_AUTODESK_AUTODESK_MASKED = "AdaptiveGI/Autodesk Interactive/AutodeskInteractiveMasked";
        const string ADAPTIVEGI_AUTODESK_AUTODESK_TRANSPARENT = "AdaptiveGI/Autodesk Interactive/AutodeskInteractiveTransparent";
        const string ADAPTIVEGI_DECAL = "AdaptiveGI/Decal";

        [MenuItem("Tools/Adaptive GI/Convert Selected Materials to Adaptive GI Compatible Shaders")]
        private static void ConvertMaterials()
        {
            Shader adaptiveGILit = Shader.Find(ADAPTIVEGI_LIT);
            Shader adaptiveGITerrainLit = Shader.Find(ADAPTIVEGI_TERRAIN_LIT);
            Shader adaptiveGIAutodesk = Shader.Find(ADAPTIVEGI_AUTODESK_AUTODESK);
            Shader adaptiveGIAutodeskMasked = Shader.Find(ADAPTIVEGI_AUTODESK_AUTODESK_MASKED);
            Shader adaptiveGIAutodeskTransparent = Shader.Find(ADAPTIVEGI_AUTODESK_AUTODESK_TRANSPARENT);
            Shader adaptiveGIDecal = Shader.Find(ADAPTIVEGI_DECAL);

            int convertedCount = 0;
            foreach (Object o in Selection.objects)
            {
                Material material = o as Material;
                if (material != null)
                {
                    switch (material.shader.name)
                    {
                        case URP_LIT:
                            material.shader = adaptiveGILit;
                            convertedCount++;
                            break;
                        case URP_TERRAIN_LIT:
                            material.shader = adaptiveGITerrainLit;
                            convertedCount++;
                            break;
                        case URP_AUTODESK_AUTODESK:
                            material.shader = adaptiveGIAutodesk;
                            convertedCount++;
                            break;
                        case URP_AUTODESK_AUTODESK_MASKED:
                            material.shader = adaptiveGIAutodeskMasked;
                            convertedCount++;
                            break;
                        case URP_AUTODESK_AUTODESK_TRANSPARENT:
                            material.shader = adaptiveGIAutodeskTransparent;
                            convertedCount++;
                            break;
                        case URP_DECAL:
                            material.shader = adaptiveGIDecal;
                            convertedCount++;
                            break;
                        case ADAPTIVEGI_LIT:
                            break;
                        case ADAPTIVEGI_TERRAIN_LIT:
                            break;
                        case ADAPTIVEGI_AUTODESK_AUTODESK:
                            break;
                        case ADAPTIVEGI_AUTODESK_AUTODESK_MASKED:
                            break;
                        case ADAPTIVEGI_AUTODESK_AUTODESK_TRANSPARENT:
                            break;
                        case ADAPTIVEGI_DECAL:
                            break;
                        default:
                            if (material.shader.name.Contains(ADAPTIVEGI_OPTIMIZED_LIT))
                            {
                                break;
                            }
                            Debug.LogWarning($"{material.name} was unable to be converted.");
                            break;
                    }
                }
            }

            Debug.Log($"{convertedCount} materials were converted to equivalent Adaptive GI shaders.");
        }
    }
}
#endif
