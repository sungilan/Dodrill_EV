using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;

namespace AdaptiveGI.Native
{
    public static class NativeUtils
    {
        public static unsafe void ClearNativeArray<T>(NativeArray<T> to_clear) where T : struct
        {
            UnsafeUtility.MemClear(
                NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(to_clear),
                (long)UnsafeUtility.SizeOf<T>() * to_clear.Length);
        }

        public static unsafe void CopyNativeArray<T>(NativeArray<T> from, NativeArray<T> to) where T : struct
        {
            UnsafeUtility.MemCpy(
                NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(to),
                NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(from),
                (long)UnsafeUtility.SizeOf<T>() * from.Length);
        }
    }
}