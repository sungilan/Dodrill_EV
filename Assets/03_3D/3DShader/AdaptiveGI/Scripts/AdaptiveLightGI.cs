using System.Collections.Generic;
using System.ComponentModel;
using AdaptiveGI.Core;
using UnityEngine;

namespace AdaptiveGI
{
    [ExecuteAlways]
    [DefaultExecutionOrder(2)]
    [DisallowMultipleComponent]
    [AddComponentMenu("AdaptiveGI/Adaptive Light GI")]
    [Description("Adds global illumination to either an AdaptiveLight or a Unity light. Only one light and corresponding AdaptiveLightGI can be on one GameObject.")]
    public class AdaptiveLightGI : MonoBehaviour
    {
        internal static List<AdaptiveLightGI> Instances;

        [Min(0), Tooltip("The intensity of the bounce light sampled by GI Probes.")] 
        public float bounceIntensity = 1;
        [Tooltip("Whether to use raytracing or a fast approximation of GI for this light. Has an extremely high performance impact per light this is enabled on.")]
        public bool useRaytracing = false;

        internal float Intensity => attachedLightType switch
        {
            AttachedLightType.Adaptive => attachedAdaptiveLight.intensity,
            AttachedLightType.Unity => attachedUnityLight.intensity,
            _ => 0
        };
        internal float BounceIntensity => Mathf.Max(0, bounceIntensity);
        internal bool UseColorTemperature => attachedLightType switch
        {
            AttachedLightType.Adaptive => false,
            AttachedLightType.Unity => attachedUnityLight.useColorTemperature,
            _ => false
        };
        internal float ColorTemperature => attachedLightType switch
        {
            AttachedLightType.Adaptive => 0,
            AttachedLightType.Unity => attachedUnityLight.colorTemperature,
            _ => 0
        };
        internal Color Color => attachedLightType switch
        {
            AttachedLightType.Adaptive => attachedAdaptiveLight.color,
            AttachedLightType.Unity => attachedUnityLight.color,
            _ => Color.black
        };
        internal bool IsSpotLight => (attachedLightType == AttachedLightType.Adaptive && attachedAdaptiveLight.type == AdaptiveLightType.Spot) ||
                    (attachedLightType == AttachedLightType.Unity && attachedUnityLight.type == LightType.Spot);
        internal Vector2 InnerOuterSpotAngle => attachedLightType switch
        {
            AttachedLightType.Adaptive => attachedAdaptiveLight.innerOuterSpotAngle,
            AttachedLightType.Unity => new Vector2(attachedUnityLight.innerSpotAngle, attachedUnityLight.spotAngle),
            _ => Vector2.zero
        };

        internal bool lightIsActiveAndEnabled;
        private AttachedLightType attachedLightType;
        private AdaptiveLight attachedAdaptiveLight;
        private Light attachedUnityLight;

        private void OnEnable()
        {
            Instances ??= new List<AdaptiveLightGI>();
            if (!Instances.Contains(this))
            {
                Instances.Add(this);
            }

            attachedAdaptiveLight = GetComponent<AdaptiveLight>();
            attachedUnityLight = GetComponent<Light>();

            if (attachedAdaptiveLight != null && attachedUnityLight != null)
            {
                Debug.LogError("Two different types of lights on the same GameObject as AdaptiveLightGI. This is unsupported, destroying.");
                attachedLightType = AttachedLightType.None;
                attachedAdaptiveLight = null;
                attachedUnityLight = null;
            }
            else if (attachedAdaptiveLight != null)
            {
                attachedLightType = AttachedLightType.Adaptive;
            }
            else if (attachedUnityLight != null)
            {
                attachedLightType = AttachedLightType.Unity;
            }
            else
            {
                Debug.LogError("No Unity light or Adaptive light found on the same GameObject as AdaptiveLightGI, destroying.");
                attachedLightType = AttachedLightType.None;
                attachedAdaptiveLight = null;
                attachedUnityLight = null;
            }
        }

        private void OnDisable()
        {
            if (Instances != null && Instances.Contains(this))
            {
                _ = Instances.Remove(this);
            }
        }

        internal void UpdateStatus()
        {
            if (attachedAdaptiveLight == null && attachedUnityLight == null)
            {
                attachedLightType = AttachedLightType.None;
                DestroyImmediate(this);
            }

            lightIsActiveAndEnabled = attachedLightType switch
            {
                AttachedLightType.Adaptive => attachedAdaptiveLight.isActiveAndEnabled,
                AttachedLightType.Unity => attachedUnityLight.isActiveAndEnabled,
                _ => false
            };
        }

        private enum AttachedLightType
        {
            Adaptive,
            Unity,
            None
        }
    }
}
