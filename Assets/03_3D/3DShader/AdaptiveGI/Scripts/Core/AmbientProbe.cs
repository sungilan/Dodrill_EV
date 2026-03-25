using Unity.Mathematics;
using UnityEngine;

namespace AdaptiveGI.Core
{
    public class AmbientProbe
    {
        public Vector4 shR;
        public Vector4 shG;
        public Vector4 shB;

        public AmbientProbe()
        {
            Clear();
        }

        public void Clear()
        {
            shR = Vector4.zero;
            shG = Vector4.zero;
            shB = Vector4.zero;
        }

        public void AddTriLight(Color skyColor, Color equatorColor, Color groundColor, float intensity)
        {
            AddDirectionalLight(Vector3.up, skyColor, intensity);
            AddDirectionalLight(Vector3.down, groundColor, intensity);
            AddAmbientLight(equatorColor, intensity);
        }

        public void AddAmbientLight(Color color, float intensity)
        {
            shR.w += color.r * intensity;
            shG.w += color.g * intensity;
            shB.w += color.b * intensity;
        }

        public void AddDirectionalLight(Vector3 direction, Color color, float intensity)
        {
            Vector3 dirNormalized = direction.normalized;
            Vector4 sh = new Vector4(dirNormalized.x, dirNormalized.y, dirNormalized.z, 1) * intensity;

            shR += sh * color.r;
            shG += sh * color.g;
            shB += sh * color.b;
        }

        public float3 Evaluate(Vector3 direction)
        {
            Vector4 vA = new Vector4(direction.x, direction.y, direction.z, 1);

            float3 x1;
            // Linear (L1) + constant (L0) polynomial terms
            x1.x = Vector4.Dot(shR, vA);
            x1.y = Vector4.Dot(shG, vA);
            x1.z = Vector4.Dot(shB, vA);

            return x1;
        }
    }
}
