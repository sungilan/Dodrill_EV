using UnityEngine;

namespace AdaptiveGI.Core
{
    [System.Serializable]
    public class AmbientLight
    {
        [Tooltip("The direction this ambient light is coming from.")]
        public Vector3 direction;
        [Tooltip("The color of this ambient light.")]
        public Color color;
        [Min(0), Tooltip("The intensity of this ambient light.")] 
        public float intensity;
    }
}
