using Unity.Burst;
using Unity.Mathematics;

namespace AdaptiveGI.Core
{
    [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    public static class GridUtils
    {
        [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        public static int FlattenIndex(ref int3 index, int gridWidth, int gridHeight)
        {
            return (index.z * gridWidth * gridHeight) +
            (index.y * gridWidth) +
            index.x;
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        public static void UnflattenIndex(int flatIndex, int gridWidth, int gridHeight, out int3 index)
        {
            index.z = flatIndex / (gridWidth * gridHeight);
            index.y = flatIndex % (gridWidth * gridHeight) / gridWidth;
            index.x = flatIndex % gridWidth;
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        public static bool GridContainsPoint(ref int3 point, int gridWidth, int gridHeight, int gridDepth)
        {
            return math.all(point < new int3(gridWidth, gridHeight, gridDepth)) && math.all(point >= 0);
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        public static void WorldToLocal(ref float3 worldPosition, ref float3 worldGridPosition, ref float3 worldToGridOffset, float gridResolution, out float3 localPosition)
        {
            localPosition = (worldPosition - worldGridPosition + worldToGridOffset) * gridResolution;
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        public static void GridToWorld(ref int3 gridPosition, ref float3 worldGridPosition, ref float3 gridToWorldOffset, float inverseGridResolution, out float3 worldPosition)
        {
            worldPosition = ((float3)gridPosition * inverseGridResolution) + gridToWorldOffset + worldGridPosition;
        }
    }
}
