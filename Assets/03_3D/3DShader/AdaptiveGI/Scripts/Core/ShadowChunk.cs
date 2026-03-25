using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace AdaptiveGI.Core
{
    [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    public struct ShadowChunk
    {
        public NativeArray<byte> data;
        public int3 position;

        public ShadowChunk(int3 position, int size)
        {
            data = new NativeArray<byte>(size * size * size, Allocator.Persistent);
            this.position = position;
        }

        public void Dispose()
        {
            if (data.IsCreated)
            {
                data.Dispose();
            }
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        public static void WorldToVoxel(ref float3 worldPos, ref int3 position, int size, float resolution, out int3 voxelPos)
        {
            voxelPos = (int3)math.floor(((worldPos - ((float3)position * size * resolution)) / resolution) + new float3(size * 0.5f));
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        public static void VoxelToWorld(ref int3 voxelPos, ref int3 position, int size, float resolution, out float3 worldPos)
        {
            worldPos = ((float3)position * (size * resolution)) + ((voxelPos - new float3(size * 0.5f) + new float3(0.5f)) * resolution);
        }
    }

    public enum ShadowChunkSize
    {
        _8 = 8,
        _12 = 12,
        _16 = 16,
        _24 = 24,
        _32 = 32,
        _48 = 48,
        _64 = 64
    }
}
