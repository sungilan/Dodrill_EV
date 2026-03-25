using UnityEngine;

namespace AdaptiveGI
{
    public struct GIData
    {
        public Vector4 shR;
        public Vector4 shG;
        public Vector4 shB;

        public readonly Color Evaluate(Vector3 direction)
        {
            Vector4 vA = new Vector4(direction.x, direction.y, direction.z, 1f);
            return new Color(Vector4.Dot(shR, vA), Vector4.Dot(shG, vA), Vector4.Dot(shB, vA), 1);
        }
    }
}
