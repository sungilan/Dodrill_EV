using Unity.Mathematics;
using UnityEngine;

namespace AdaptiveGI.Core
{
    public interface IAdaptiveLight
    {
        public AdaptiveLightType GetLightType();
        public float3 GetColor();
        public float GetIntensity();
        public float GetConePower();
        public float GetPower();
        public float GetDeadzone();
        public float GetRange();
        public float GetInnerSpotAngle();
        public float GetOuterSpotAngle();
        public Vector3 GetPosition();
        public Vector3 GetDirection();
        public float GetShadowStrength();
        public float GetShadowSoftness();
        public float GetShadowDepthBias();
        public float GetShadowStartBias();
    }
}

