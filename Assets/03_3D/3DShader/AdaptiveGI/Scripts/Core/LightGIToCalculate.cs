using Unity.Burst;
using Unity.Mathematics;

namespace AdaptiveGI.Core
{
    [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    public struct LightGIToCalculate
    {
        public float intensity;
        public float bounceIntensity;
        public float3 color;
        public bool isSpotLight;
        public float2 innerOuterSpotAngle;

        public float3 position;
        public float3 direction;
    }
}
