using UnityEngine;

namespace AdaptiveGI.Core
{
    public static class RayDirections
    {
        public static Vector3[] _5 = GenerateRayDirections(5);
        public static Vector3[] _10 = GenerateRayDirections(10);
        public static Vector3[] _15 = GenerateRayDirections(15);
        public static Vector3[] _20 = GenerateRayDirections(20);
        public static Vector3[] _25 = GenerateRayDirections(25);
        public static Vector3[] _30 = GenerateRayDirections(30);
        public static Vector3[] _35 = GenerateRayDirections(35);
        public static Vector3[] _40 = GenerateRayDirections(40);
        public static Vector3[] _45 = GenerateRayDirections(45);
        public static Vector3[] _50 = GenerateRayDirections(50);
        public static Vector3[] _60 = GenerateRayDirections(60);
        public static Vector3[] _70 = GenerateRayDirections(70);
        public static Vector3[] _80 = GenerateRayDirections(80);
        public static Vector3[] _90 = GenerateRayDirections(90);
        public static Vector3[] _100 = GenerateRayDirections(100);

        public static Vector3[] _10000 = GenerateRayDirections(10000);

        public static Vector3[] GetRayDirections(RayCount rayCount)
        {
            return rayCount switch
            {
                RayCount._5 => _5,
                RayCount._10 => _10,
                RayCount._15 => _15,
                RayCount._20 => _20,
                RayCount._25 => _25,
                RayCount._30 => _30,
                RayCount._35 => _35,
                RayCount._40 => _40,
                RayCount._45 => _45,
                RayCount._50 => _50,
                RayCount._60 => _60,
                RayCount._70 => _70,
                RayCount._80 => _80,
                RayCount._90 => _90,
                RayCount._100 => _100,
                _ => _5,
            };
        }

        public enum RayCount
        {
            _5 = 5,
            _10 = 10,
            _15 = 15,
            _20 = 20,
            _25 = 25,
            _30 = 30,
            _35 = 35,
            _40 = 40,
            _45 = 45,
            _50 = 50,
            _60 = 60,
            _70 = 70,
            _80 = 80,
            _90 = 90,
            _100 = 100
        }

        private static Vector3[] GenerateRayDirections(int rayCount)
        {
            Vector3[] directions = new Vector3[rayCount];
            for (int i = 0; i < rayCount; i++)
            {
                directions[i] = HaltonSpherePoint(i);
            }

            return directions;
        }

        private static Vector3 HaltonSpherePoint(int index)
        {
            float u = Halton(index, 2);
            float v = Halton(index, 3);

            float theta = 2 * Mathf.PI * u;
            float phi = Mathf.Acos((2 * v) - 1);

            return new Vector3(
                Mathf.Sin(phi) * Mathf.Cos(theta),
                Mathf.Sin(phi) * Mathf.Sin(theta),
                Mathf.Cos(phi)
            );
        }

        private static float Halton(int index, int @base)
        {
            float result = 0;
            float f = 1f / @base;
            int i = index;

            while (i > 0)
            {
                result += f * (i % @base);
                i /= @base;
                f /= @base;
            }

            return result;
        }
    }
}
