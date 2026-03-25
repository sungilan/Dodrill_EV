using UnityEngine;
using System.Collections.Generic;
using AdaptiveGI.Core;
using Unity.Mathematics;
using System.ComponentModel;

namespace AdaptiveGI
{
    [ExecuteAlways]
    [DefaultExecutionOrder(1)]
    [DisallowMultipleComponent]
    [AddComponentMenu("AdaptiveGI/Adaptive Shadow Caster")]
    [Description("A dynamic shadow caster for AdaptiveLights.")]
    public class AdaptiveShadowCaster : MonoBehaviour
    {
        internal static List<AdaptiveShadowCaster> Instances;

        [Header("General")]
        [SerializeField, Tooltip("The shape of the shadow caster.")] internal AdaptiveShadowCasterType type = AdaptiveShadowCasterType.Cube;
        [SerializeField, Range(0, 1), Tooltip("The opacity of the shadow caster.")] internal float opacity = 1;

        public AdaptiveShadowCasterType Type
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

        public float Opacity
        {
            get
            {
                return opacity;
            }
            set
            {
                opacity = Mathf.Clamp01(value);
            }
        }

        internal bool isActive;

        private void OnEnable()
        {
            isActive = true;
            Instances ??= new List<AdaptiveShadowCaster>();
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
    }
}
