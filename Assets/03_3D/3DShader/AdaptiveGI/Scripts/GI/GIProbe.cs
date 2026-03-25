using AdaptiveGI.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace AdaptiveGI.GI
{
    [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
    public class GIProbe : IAdaptiveLight
    {
        internal Vector3 position;
        internal Vector3 direction;

        internal bool isActive;
        internal bool isDespawning;

        private float3 raytracingLight;
        private float3 fastLight;

        private float updateInterval;
        private float lastUpdateTime;

        internal int bounceCount;

        internal float targetIntensity;
        internal float3 targetColor;

        internal int probeIndex;

        // Light data
        internal float3 color;
        internal AdaptiveLightType type;
        internal Vector2 innerOuterSpotAngle;
        internal float intensity;
        private const float MIN_INTENSITY = 0.001f;

        public GIProbe(float time, int i)
        {
            targetColor = 0;
            targetIntensity = 0;
            lastUpdateTime = time;
            isActive = false;
            isDespawning = false;
            type = AdaptiveLightType.Spot;
            innerOuterSpotAngle.x = 0;
            innerOuterSpotAngle.y = 360;
            probeIndex = i;
            updateInterval = 1;
        }

        internal void ScheduleOcclusionCommands(NativeArray<RaycastCommand> occlusionCommands, int occlusionIndex)
        {
            raytracingLight = MIN_INTENSITY;

            for (int r = occlusionIndex; r < (int)AdaptiveGI.Instance.gIRayCount + occlusionIndex; r++)
            {
                occlusionCommands[r] = new RaycastCommand(position, AdaptiveGI.Instance.rayDirs[r - occlusionIndex], new QueryParameters(AdaptiveGI.Instance.gIMask), Mathf.Infinity);
            }
        }

        internal int FinishOcclusionResults(NativeArray<RaycastHit> occlusionResults, int occlusionIndex, int lightGICount)
        {
            int bounceJobCount = 0;
            float3 ambientLight = float3.zero;
            for (int r = occlusionIndex; r < (int)AdaptiveGI.Instance.gIRayCount + occlusionIndex; r++)
            {
                RaycastHit hit = occlusionResults[r];
                int colliderId;
#if UNITY_6000_3_OR_NEWER
                colliderId = hit.colliderEntityId;
#else
                colliderId = hit.colliderInstanceID;
#endif
                if (colliderId != 0) // If we hit something
                {
                    // Spawn probes
                    AdaptiveGI.Instance.TrySpawnProbe(hit);
                    // Directional light GI
                    if (AdaptiveGI.Instance.directionalLightEnabled)
                    {
                        bounceJobCount++;
                    }
                    // Additional light GI
                    bounceJobCount += lightGICount;
                }
                else
                {
                    ambientLight += AdaptiveGI.Instance.ambientProbe.Evaluate(AdaptiveGI.Instance.rayDirs[r - occlusionIndex]) * AdaptiveGI.Instance.probeRange;
                }
            }

            raytracingLight += ambientLight / (int)AdaptiveGI.Instance.gIRayCount;
            bounceCount = bounceJobCount;
            return bounceJobCount;
        }

        internal int ScheduleBounceCommands(NativeArray<RaycastHit> occlusionResults, NativeArray<RaycastCommand> bounceCommands, int[] bounceLightIndexes, float3[] bounceColors, int occlusionIndex, int bounceIndex, LightGIToCalculate[] lightGIData, int lightGICount)
        {
            int jobIndex = bounceIndex;
            float3 emissiveLight = float3.zero;
            for (int r = occlusionIndex; r < (int)AdaptiveGI.Instance.gIRayCount + occlusionIndex; r++)
            {
                RaycastHit hit = occlusionResults[r];
                int colliderId;
#if UNITY_6000_3_OR_NEWER
                colliderId = hit.colliderEntityId;
#else
                colliderId = hit.colliderInstanceID;
#endif
                if (colliderId != 0) // If we hit something
                {
                    float3 bounceColor = 1;
                    // Emissive material GI
                    if (AdaptiveGIMaterial.colliderMaterialProperties.IsCreated && AdaptiveGIMaterial.colliderMaterialProperties.TryGetValue(colliderId, out AdaptiveGIMaterial.MaterialProperties material))
                    {
                        bounceColor = math.lerp(1, material.diffuse, AdaptiveGI.Instance.gIColorBounceIntensity);
                        float intensity = material.emissionIntensity * AdaptiveGI.Instance.gIIntensity * AdaptiveGI.Instance.probeRange;
                        float attenuation = intensity / Mathf.Max(hit.distance * hit.distance / intensity, AdaptiveGI.Instance.gIRange * AdaptiveGI.Instance.gIDeadzone);
                        emissiveLight += material.emission * attenuation;
                    }
                    // Directional light GI
                    if (AdaptiveGI.Instance.directionalLightEnabled)
                    {
                        bounceCommands[jobIndex] = new RaycastCommand(hit.point - (AdaptiveGI.Instance.rayDirs[r - occlusionIndex] * AdaptiveGI.Instance.probeSpacing * AdaptiveGI.CONTACT_OFFSET), AdaptiveGI.Instance.dirLightBack, new QueryParameters(AdaptiveGI.Instance.gIMask));
                        bounceLightIndexes[jobIndex] = -1;
                        bounceColors[jobIndex] = bounceColor;
                        jobIndex++;
                    }
                    // Additional light GI
                    for (int i = 0; i < lightGICount; i++)
                    {
                        LightGIToCalculate light = lightGIData[i];
                        Vector3 difference = (Vector3)light.position - hit.point;
                        bounceCommands[jobIndex] = new RaycastCommand(hit.point - (AdaptiveGI.Instance.rayDirs[r - occlusionIndex] * AdaptiveGI.Instance.probeSpacing * AdaptiveGI.CONTACT_OFFSET), difference.normalized, new QueryParameters(AdaptiveGI.Instance.gIMask), difference.magnitude);
                        bounceLightIndexes[jobIndex] = i;
                        bounceColors[jobIndex] = bounceColor;
                        jobIndex++;
                    }
                }
            }

            raytracingLight += emissiveLight / (int)AdaptiveGI.Instance.gIRayCount;
            return bounceCount;
        }

        internal void FinishBounceResults(NativeArray<RaycastCommand> bounceCommands, NativeArray<RaycastHit> bounceResults, int[] bounceLightIndexes, float3[] bounceColors, int bounceIndex, LightGIToCalculate[] lightGIData)
        {
            int importanceRays = 0;
            float3 bounceLight = float3.zero;
            for (int b = bounceIndex; b < bounceCount + bounceIndex; b++)
            {
                RaycastHit hit = bounceResults[b];
                int colliderId;
#if UNITY_6000_3_OR_NEWER
                colliderId = hit.colliderEntityId;
#else
                colliderId = hit.colliderInstanceID;
#endif
                if (colliderId == 0) // If we didn't hit anything
                {
                    RaycastCommand command = bounceCommands[b];
                    int lightIndex = bounceLightIndexes[b];
                    float3 color;
                    float attenuation;
                    if (lightIndex == -1) // Directional Light
                    {
                        color = AdaptiveGI.Instance.directionalLightColor;
                        float intensity = AdaptiveGI.Instance.directionalLightIntensity * AdaptiveGI.Instance.gIIntensity * AdaptiveGI.Instance.probeRange;
                        attenuation = intensity / Mathf.Max((command.from - position).sqrMagnitude / intensity, AdaptiveGI.Instance.gIRange * AdaptiveGI.Instance.gIDeadzone);
                    }
                    else // Point and Spot Light
                    {
                        LightGIToCalculate light = lightGIData[lightIndex];
                        color = light.color;
                        attenuation = light.intensity * light.bounceIntensity * AdaptiveGI.Instance.gIIntensity * AdaptiveGI.Instance.probeRange
                            / Mathf.Max(command.distance * command.distance, AdaptiveGI.Instance.gIRange * AdaptiveGI.Instance.gIDeadzone);
                        if (light.isSpotLight)
                        {
                            attenuation *= Mathf.Clamp01((Vector3.Dot((command.from - (Vector3)light.position).normalized, light.direction) - Mathf.Cos(light.innerOuterSpotAngle.y * Mathf.Deg2Rad))
                                / (1 - Mathf.Cos(light.innerOuterSpotAngle.y * Mathf.Deg2Rad)));
                        }
                    }
                    bounceLight += color * bounceColors[b] * attenuation;
                    importanceRays++;
                }
            }
            if (importanceRays > 0 && importanceRays < AdaptiveGI.Instance.gIImportanceRayCount)
            {
                raytracingLight += bounceLight * AdaptiveGI.Instance.gIImportanceRayIntensity / AdaptiveGI.Instance.gIImportanceRayCount;
            }
            else
            {
                raytracingLight += bounceLight / (int)AdaptiveGI.Instance.gIRayCount;
            }
        }

        internal void FastGI(NativeArray<LightGIToCalculate> lightGIData)
        {
            float3 convertedPosition = position;
            FastGIInternal(
                    ref convertedPosition,
                    AdaptiveGI.Instance.gIRange,
                    AdaptiveGI.Instance.gIDeadzone,
                    AdaptiveGI.Instance.gIIntensity,
                    ref lightGIData,
                    out fastLight
                    );
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private static void FastGIInternal(ref float3 position, float range, float deadzone, float gIIntensity, ref NativeArray<LightGIToCalculate> lightGIData, out float3 fastLight)
        {
            fastLight = 0.0001f;

            for (int i = 0; i < lightGIData.Length; i++)
            {
                LightGIToCalculate light = lightGIData[i];

                float3 difference = position - light.position;
                float attenuation = light.intensity * light.bounceIntensity * gIIntensity * range
                    / math.max(math.lengthsq(difference), range * deadzone);
                if (light.isSpotLight)
                {
                    attenuation *= math.clamp((math.dot(math.normalize(difference), light.direction) - math.cos(light.innerOuterSpotAngle.y * math.TORADIANS))
                        / (1 - math.cos(light.innerOuterSpotAngle.y * math.TORADIANS)), 0, 1);
                }

                fastLight += light.color * attenuation;
            }
        }

        internal void Apply(float time)
        {
            if (isDespawning)
            {
                if (intensity < MIN_INTENSITY)
                {
                    isActive = false;
                    isDespawning = false;
                    AdaptiveGI.Instance.probePositions[probeIndex] = float.MaxValue;
                }
            }
            else
            {
                float3 totalLight = (raytracingLight + fastLight) * (AdaptiveGI.Instance.enableShadows ? AdaptiveGI.Instance.shadowGIIntensityMultiplier : 1) / AdaptiveGI.Instance.probeResolution;
                targetIntensity = math.length(totalLight);
                targetColor = totalLight / targetIntensity;
            }

            updateInterval = (time - lastUpdateTime) + float.Epsilon;
            lastUpdateTime = time;

            AdaptiveGI.Instance.gIProbeLightsToCalculate[probeIndex] = new LightToCalculate(this, isActive);
        }

        internal void Lerp(float deltaTime)
        {
            LerpInternal(probeIndex, deltaTime, updateInterval, targetIntensity, ref targetColor, ref intensity, ref color, ref AdaptiveGI.Instance.gIProbeLightsToCalculate);
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
        private static void LerpInternal(int probeIndex, float deltaTime, float updateInterval, float targetIntensity, ref float3 targetColor, ref float intensity, ref float3 color, ref NativeArray<LightToCalculate> gIProbeLightsToCalculate)
        {
            float t = 1 - math.exp(-deltaTime / updateInterval);

            intensity = math.lerp(intensity, targetIntensity, t);
            color = math.lerp(color, targetColor, t);

            LightToCalculate updated = gIProbeLightsToCalculate[probeIndex];
            updated.color = color;
            updated.intensity = intensity;
            gIProbeLightsToCalculate[probeIndex] = updated;
        }

        AdaptiveLightType IAdaptiveLight.GetLightType()
        {
            return type;
        }

        float3 IAdaptiveLight.GetColor()
        {
            return color;
        }

        float IAdaptiveLight.GetIntensity()
        {
            return intensity;
        }

        float IAdaptiveLight.GetConePower()
        {
            return AdaptiveGI.Instance.gINormalPower;
        }

        float IAdaptiveLight.GetPower()
        {
            return AdaptiveGI.Instance.gIPower;
        }

        float IAdaptiveLight.GetDeadzone()
        {
            return AdaptiveGI.Instance.gIDeadzone;
        }

        float IAdaptiveLight.GetRange()
        {
            return AdaptiveGI.Instance.probeRange;
        }

        float IAdaptiveLight.GetInnerSpotAngle()
        {
            return innerOuterSpotAngle.x;
        }

        float IAdaptiveLight.GetOuterSpotAngle()
        {
            return innerOuterSpotAngle.y;
        }

        Vector3 IAdaptiveLight.GetPosition()
        {
            return position;
        }

        Vector3 IAdaptiveLight.GetDirection()
        {
            return direction;
        }

        float IAdaptiveLight.GetShadowStrength()
        {
            return AdaptiveGI.Instance.gIShadowStrength;
        }

        float IAdaptiveLight.GetShadowSoftness()
        {
            return AdaptiveGI.Instance.gIShadowSoftness;
        }

        float IAdaptiveLight.GetShadowDepthBias()
        {
            return AdaptiveGI.Instance.gIShadowDepthBias;
        }

        float IAdaptiveLight.GetShadowStartBias()
        {
            return AdaptiveGI.Instance.gIShadowStartBias;
        }
    }
}
