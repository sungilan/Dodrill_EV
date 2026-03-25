using System.Collections.Generic;
using System.ComponentModel;
using AdaptiveGI.Core;
using Unity.Mathematics;
using UnityEngine;

namespace AdaptiveGI
{
    [ExecuteAlways]
    [DefaultExecutionOrder(1)]
    [DisallowMultipleComponent]
    [AddComponentMenu("AdaptiveGI/Adaptive Light")]
    [Description("AdaptiveGI's more performant light. Unlike Unity's lights, there can be hundreds of these lights in a scene, even when using forward rendering. Only works when within AdaptiveGI's render volume.")]
    public class AdaptiveLight : MonoBehaviour, IAdaptiveLight
    {
        internal static List<AdaptiveLight> Instances;

        [Header("General")]
        [SerializeField, Tooltip("The type of the light.")] internal AdaptiveLightType type = AdaptiveLightType.Point;
        [SerializeField, Tooltip("Controls the inner and outer angle of the spot light in degrees.")] internal Vector2 innerOuterSpotAngle = new(180, 180);
        [SerializeField, Min(1), Tooltip("The power of the cone formed by spot lights. Higher values increase the attenuation factor of the cone.")] internal float conePower = 1;
        [Header("Emission")]
        [SerializeField, Tooltip("The color this light emits.")] internal Color color = Color.white;
        [SerializeField, Min(0), Tooltip("The intensity of the light. The color is multiplied by this value.")] internal float intensity = 1;
        [SerializeField, Min(0), Tooltip("The power of the light. Higher values increase the attenuation factor of the light.")] internal float power = 1;
        [SerializeField, Min(0), Tooltip("The range of the light. How performant this light is depends on this value.")] internal float range = 10;
        [SerializeField, Range(0, 1), Tooltip("The deadzone around the light. Higher values increase the area around the light where attenuation doesn't change.")] internal float deadzone = 0;
        [Header("Shadows")]
        [SerializeField, Range(0, 1), Tooltip("The strength of shadows of this light.")] internal float shadowStrength = 1;
        [SerializeField, Range(0, 1), Tooltip("The softness of shadows cast by light.")] internal float shadowSoftness = 0;
        [SerializeField, Range(0, 10), Tooltip("The depth bias of shadows for this light.")] internal float shadowDepthBias = 1;
        [SerializeField, Range(0, 10), Tooltip("How far from the light shadows start being cast.")] internal float shadowStartBias = 0;

        public AdaptiveLightType Type 
        { 
            get 
            { 
                return type; 
            } 
            set
            {
                type = value;
            }
        }

        public Vector2 InnerOuterSpotAngle
        {
            get
            {
                return innerOuterSpotAngle;
            }
            set
            {
                innerOuterSpotAngle = new Vector2(Mathf.Clamp(value.x, 0, 180), Mathf.Clamp(value.y, 0, 180));
            }
        }

        public float ConePower
        {
            get
            {
                return conePower;
            }
            set
            {
                conePower = Mathf.Max(1, value);
            }
        }

        public Color Color
        {
            get
            {
                return color;
            }
            set
            {
                color = value;
            }
        }

        public float Intensity
        {
            get
            {
                return intensity;
            }
            set
            {
                intensity = Mathf.Max(0, value);
            }
        }

        public float Power
        {
            get
            {
                return power;
            }
            set
            {
                power = Mathf.Max(0, value);
            }
        }

        public float Range
        {
            get
            {
                return range;
            }
            set
            {
                range = Mathf.Max(0, value);
            }
        }

        public float Deadzone
        {
            get
            {
                return deadzone;
            }
            set
            {
                deadzone = Mathf.Clamp01(value);
            }
        }

        public float ShadowStrength
        {
            get
            {
                return shadowStrength;
            }
            set
            {
                shadowStrength = Mathf.Clamp01(value);
            }
        }

        public float ShadowSoftness
        {
            get
            {
                return shadowSoftness;
            }
            set
            {
                shadowSoftness = Mathf.Clamp01(value);
            }
        }

        public float ShadowDepthBias
        {
            get
            {
                return shadowDepthBias;
            }
            set
            {
                shadowDepthBias = Mathf.Max(0, value);
            }
        }

        public float ShadowStartBias
        {
            get
            {
                return shadowStartBias;
            }
            set
            {
                shadowStartBias = Mathf.Max(0, value);
            }
        }

        internal bool isActive;

        private void OnEnable()
        {
            isActive = true;
            Instances ??= new List<AdaptiveLight>();
            if (!Instances.Contains(this))
            {
                Instances.Add(this);
            }
        }

        private void OnDisable()
        {
            isActive = false;
        }

        private void OnDestroy()
        {
            if (Instances != null && Instances.Contains(this))
            {
                _ = Instances.Remove(this);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = color;
            if (type == AdaptiveLightType.Point)
            {
                Gizmos.DrawWireSphere(transform.position, range);
            }
        }

        AdaptiveLightType IAdaptiveLight.GetLightType()
        {
            return type;
        }

        float3 IAdaptiveLight.GetColor()
        {
            return new float3(color.r, color.g, color.b);
        }

        float IAdaptiveLight.GetIntensity()
        {
            return intensity;
        }

        float IAdaptiveLight.GetConePower()
        {
            return conePower;
        }

        float IAdaptiveLight.GetPower()
        {
            return power;
        }

        float IAdaptiveLight.GetDeadzone()
        {
            return deadzone;
        }

        float IAdaptiveLight.GetRange()
        {
            return range;
        }

        float IAdaptiveLight.GetInnerSpotAngle()
        {
            return innerOuterSpotAngle.x;
        }

        float IAdaptiveLight.GetOuterSpotAngle()
        {
            return innerOuterSpotAngle.y;
        }

        Vector3 IAdaptiveLight.GetPosition()
        {
            return transform.position;
        }

        Vector3 IAdaptiveLight.GetDirection()
        {
            return transform.forward;
        }

        float IAdaptiveLight.GetShadowStrength()
        {
            return shadowStrength;
        }

        float IAdaptiveLight.GetShadowSoftness()
        {
            return shadowSoftness;
        }

        float IAdaptiveLight.GetShadowDepthBias()
        {
            return shadowDepthBias;
        }

        float IAdaptiveLight.GetShadowStartBias()
        {
            return shadowStartBias;
        }
    }
}
