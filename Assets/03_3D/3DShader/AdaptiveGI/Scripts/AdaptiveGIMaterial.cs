using System.ComponentModel;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace AdaptiveGI
{
    [ExecuteAlways]
    [DefaultExecutionOrder(3)]
    [DisallowMultipleComponent]
    [AddComponentMenu("AdaptiveGI/Adaptive GI Material")]
    [Description("Stores per object material information. This allows for color bouncing and emissive materials. There can only be one AdaptiveGIMaterial on each GameObject.")]
    public class AdaptiveGIMaterial : MonoBehaviour
    {
        internal static NativeHashMap<int, MaterialProperties> colliderMaterialProperties;

        [Header("Diffuse")]
        [SerializeField, Min(0), Tooltip("The diffuse multiplier of this material that can be used to artistically brighten or darken the light bouncing off this material.")] internal float diffuseMultiplier = 1;
        [SerializeField, Tooltip("The diffuse color of this material that is multiplied with light color to produce the final GI color.")] internal Color diffuseColor = Color.white;
        [Header("Emission")]
        [SerializeField, Min(0), Tooltip("The intensity of the light emitted from this material.")] internal float emissionIntensity = 0;
        [SerializeField, Tooltip("The color of the light emitted from this material.")] internal Color emissionColor = Color.black;
        [Header("Opacity")]
        [SerializeField, Range(0, 1), Tooltip("The opacity of this material.")] internal float opacity = 1;

        public float DiffuseMultiplier 
        { 
            get
            {
                return diffuseMultiplier;
            } 
            set
            {
                diffuseMultiplier = Mathf.Max(0, value);
                UpdateMaterial();
            }
        }

        public Color DiffuseColor
        {
            get
            {
                return diffuseColor;
            }
            set
            {
                diffuseColor = value;
                UpdateMaterial();
            }
        }

        public float EmissionIntensity
        {
            get
            {
                return emissionIntensity;
            }
            set
            {
                emissionIntensity = Mathf.Max(0, value);
                UpdateMaterial();
            }
        }

        public Color EmissionColor
        {
            get
            {
                return emissionColor;
            }
            set
            {
                emissionColor = value;
                UpdateMaterial();
            }
        }

        public float Opacity
        {
            get
            {
                return opacity;
            }
            set
            {
                opacity = Mathf.Clamp01(value);
                UpdateMaterial();
            }
        }

        private int[] colliderInstanceIds;
        private MaterialProperties materialProperties;

        private const int INITIAL_MATERIAL_PROPERTIES = 64;

        private void OnValidate()
        {
            UpdateMaterial();
        }

        private void OnEnable()
        {
            Initialize();
        }

        private void OnDisable()
        {
            Dispose();
        }

        [Description("Call this whenever you programatically add or remove a collider from the GameObject this script is on.")]
        public void Initialize()
        {
            Dispose();
            if (!colliderMaterialProperties.IsCreated)
            {
                colliderMaterialProperties = new NativeHashMap<int, MaterialProperties>(INITIAL_MATERIAL_PROPERTIES, Allocator.Persistent);
            }

            Collider[] colliders = GetComponents<Collider>();
            colliderInstanceIds = new int[colliders.Length];
            for (int i = 0; i < colliderInstanceIds.Length; i++)
            {
                colliderInstanceIds[i] = colliders[i].GetInstanceID();
                _ = colliderMaterialProperties.TryAdd(colliderInstanceIds[i], materialProperties);
            }

            UpdateMaterial();
        }

        private void Dispose()
        {
            if (colliderMaterialProperties.IsCreated && colliderInstanceIds != null)
            {
                for (int i = 0; i < colliderInstanceIds.Length; i++)
                {
                    _ = colliderMaterialProperties.Remove(colliderInstanceIds[i]);
                }
                if (colliderMaterialProperties.Count == 0)
                {
                    colliderMaterialProperties.Dispose();
                }
            }
        }

        private void UpdateMaterial()
        {
            materialProperties = new MaterialProperties(this);
            if (colliderInstanceIds != null && colliderMaterialProperties.IsCreated)
            {
                for (int i = 0; i < colliderInstanceIds.Length; i++)
                {
                    colliderMaterialProperties[colliderInstanceIds[i]] = materialProperties;
                }
            }
        }

        public struct MaterialProperties
        {
            public float3 diffuse;
            public float emissionIntensity;
            public float3 emission;
            public float opacity;

            public MaterialProperties(AdaptiveGIMaterial material)
            {
                diffuse = new float3(material.diffuseColor.r, material.diffuseColor.g, material.diffuseColor.b) * material.diffuseMultiplier;
                emissionIntensity = material.emissionIntensity;
                emission = new float3(material.emissionColor.r, material.emissionColor.g, material.emissionColor.b);
                opacity = material.opacity;
            }
        }
    }
}
