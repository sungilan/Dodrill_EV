#ifndef LTC_URP_LIB_INCLUDED
#define LTC_URP_LIB_INCLUDED

#include "Packages/com.petabytes.ltclight/Shaders/LTC.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

void InitializeLTCInputData(InputData inputUrp, out LTCInputData ltcInput)
{
    ltcInput.normalWS = inputUrp.normalWS;
    ltcInput.positionWS = inputUrp.positionWS;
    ltcInput.viewDirectionWS = inputUrp.viewDirectionWS;
}

void InitializeLTCSurfaceData(SurfaceData surfaceData, out LTCSurfaceData ltcSurface)
{
    ltcSurface.albedo = surfaceData.albedo;
    ltcSurface.metallic = surfaceData.metallic;
    ltcSurface.alpha = surfaceData.alpha;
    ltcSurface.smoothness = surfaceData.smoothness;
    ltcSurface.specular = surfaceData.specular;
}

half4 CalculateFragmentLTC(InputData inputData, SurfaceData surfaceData)
{
    LTCInputData ltcInput;
    LTCSurfaceData ltcSurfaceData;
    InitializeLTCInputData(inputData, ltcInput);
    InitializeLTCSurfaceData(surfaceData, ltcSurfaceData);
    return CalculateFragmentLTC(ltcInput, ltcSurfaceData);
}

#endif
