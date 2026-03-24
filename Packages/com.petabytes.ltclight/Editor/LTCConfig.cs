using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace petabytes.LTCLight.Editor
{
    [CreateAssetMenu(menuName = "Petabytes/LTC Config")]
    [InitializeOnLoad]
    public class LTCConfig : ScriptableObject
    {
        public static LTCConfig instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = AssetDatabase.LoadAssetAtPath<LTCConfig>(Path.Combine(kConfigFolderPath, kConfigSoFileName));
                    if (_instance == null)
                        _instance = ScriptableObject.CreateInstance<LTCConfig>();
                    
                }
                return _instance;
            }
        }
        private static LTCConfig _instance;
        
        private const string kConfigFolderPath = "Assets/RealTimeAreaLights/";
        private const string kConfigHlslFileName = "ltcconfig.hlsl";
        private const string kConfigSoFileName = "ltcconfig.asset";
        [Tooltip("Enable polygon light in shader")]
        [SerializeField] public bool EnablePolygonLight = true;
        [Tooltip("Enable linear light in shader")]
        [SerializeField] public bool EnableLinearLight = true;
        [Tooltip("Enable textured light in shader")]
        [SerializeField] public bool EnableTexturedLight = true;
        [Tooltip("Flip textured light horizontally")]
        [SerializeField] public bool TexturedFlipHorizontal = true;
        [Tooltip("Use perceptual roughness")]
        [SerializeField] public bool UsePerceptualRoughness = true;

        public void GenerateConfigHLSL()
        {
            var sw = File.CreateText(Path.Combine(kConfigFolderPath, kConfigHlslFileName));
            if (EnablePolygonLight)
                sw.WriteLine("#define ENABLE_POLYGON_LIGHT");
            if (EnableLinearLight)
                sw.WriteLine("#define ENABLE_LINEAR_LIGHT");
            if (EnableTexturedLight)
                sw.WriteLine("#define ENABLE_TEXTURED_LIGHT");
            if (TexturedFlipHorizontal)
                sw.WriteLine("#define ENABLE_TEXTURED_FLIP_HORIZONTAL");
            if (UsePerceptualRoughness)
                sw.WriteLine("#define ENABLE_PERCEPTUAL_RIGHT_LIGHT");
            sw.Close();

            if (!File.Exists(Path.Combine(kConfigFolderPath, kConfigSoFileName)))
                AssetDatabase.CreateAsset(instance, Path.Combine(kConfigFolderPath, kConfigSoFileName));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        
        static LTCConfig()
        {
            // Create empty config hlsl to avoid shader compile error
            if (!File.Exists(Path.Combine(kConfigFolderPath, kConfigHlslFileName)))
            {
                if (!Directory.Exists(kConfigFolderPath))
                    Directory.CreateDirectory(kConfigFolderPath);
                var sw = File.CreateText(Path.Combine(kConfigFolderPath, kConfigHlslFileName));
                sw.Close();
            }
            
            AssemblyReloadEvents.afterAssemblyReload += () =>
            {
                var ltcLights = FindObjectsOfType(typeof(LTCLight), true) as LTCLight[];
                foreach (var ltc in ltcLights)
                {
                    ltc.OnManagerDisabled();
                }

                EditorApplication.delayCall += instance.GenerateConfigHLSL;
            };
        }
    }
}