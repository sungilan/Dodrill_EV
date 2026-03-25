using Unity.Burst;
using Unity.Mathematics;

namespace AdaptiveGI.Core
{
    [BurstCompile(FloatPrecision.High, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    public struct SoftBounds
    {
        public float3 min;
        public float3 max;

        public SoftBounds(float3 position, float3 halfSize)
        {
            min = position - halfSize;
            max = position + halfSize;
        }

        [BurstCompile(FloatPrecision.High, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        public static bool Intersects(ref SoftBounds bounds1, ref SoftBounds bounds2)
        {
            return math.all(bounds1.min <= bounds2.max) && math.all(bounds1.max >= bounds2.min);
        }
    }
}
