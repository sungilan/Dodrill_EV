using Unity.Mathematics;

namespace AdaptiveGI.Core
{
    public struct LightToCalculate
    {
        public float3 color;
        public float intensity;
        public float conePower;
        public float power;
        public float deadzone;
        public float rangeSquared;
        public float outerSpotAngle;
        public float innerSpotAngle;
        public float3 position;
        public float3 direction;
        public float shadowStrength;
        public float shadowSoftness;
        public float shadowDepthBias;
        public float shadowStartBias;
        public SoftBounds bounds;
        public bool isActive;

        public LightToCalculate(IAdaptiveLight light, bool isActive)
        {
            if (light.GetLightType() == AdaptiveLightType.Point)
            {
                outerSpotAngle = 180;
                innerSpotAngle = 180;
            }
            else
            {
                outerSpotAngle = light.GetOuterSpotAngle() / 2 * math.TORADIANS;
                innerSpotAngle = outerSpotAngle - (((light.GetInnerSpotAngle() / 2) - 0.01f) * math.TORADIANS);
            }

            float3 worldPosition = light.GetPosition();
            float range = light.GetRange();
            color = math.max(0, light.GetColor());
            intensity = math.max(0, light.GetIntensity());
            conePower = math.max(0, light.GetConePower());
            power = math.max(0, light.GetPower());
            deadzone = math.max(0, light.GetDeadzone() * range * range);
            rangeSquared = math.max(0, range * range);
            position = worldPosition;
            direction = light.GetDirection();
            shadowStrength = light.GetShadowStrength();
            shadowSoftness = 1 - light.GetShadowSoftness();
            shadowDepthBias = light.GetShadowDepthBias();
            shadowStartBias = light.GetShadowStartBias();
            bounds = new SoftBounds(worldPosition, range);
            this.isActive = isActive;
        }
    }
}
