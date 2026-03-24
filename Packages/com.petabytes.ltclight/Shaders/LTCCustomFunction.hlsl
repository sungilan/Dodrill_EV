#ifndef LTC_CUSTOM_FUNCTION_INCLUDED
#define LTC_CUSTOM_FUNCTION_INCLUDED
#include "Packages/com.petabytes.ltclight/Shaders/LTC.hlsl"

void InitializeLTCInputData(float3 normalWS, float3 positionWS, float3 viewDirectionWS, out LTCInputData ltcInput)
{
    ltcInput.normalWS = normalWS;
    ltcInput.positionWS = positionWS;
    ltcInput.viewDirectionWS = viewDirectionWS;
}

void InitializeLTCSurfaceData(half3 BaseColor, half3 Specular, half Smoothness, half Metallic, half AO, half Alpha, out LTCSurfaceData ltcSurface)
{
    ltcSurface.albedo = BaseColor;
    ltcSurface.metallic = Metallic;
    ltcSurface.alpha = Alpha;
    ltcSurface.smoothness = Smoothness;
    ltcSurface.specular = Specular;
}

void LTCFunction_float(float3 Normal, float3 viewDirectionWS, float3 positionWS, float3 BaseColor, float3 Specular, float Smoothness, float Metallic, float AO, float Alpha, out float4 OutColor)
{
    LTCInputData ltcInput;
    LTCSurfaceData ltcSurface;
    InitializeLTCInputData(Normal, positionWS, viewDirectionWS, ltcInput);
    InitializeLTCSurfaceData(BaseColor, Specular, Smoothness, Metallic, AO, Alpha, ltcSurface);

    OutColor = CalculateFragmentLTC(ltcInput, ltcSurface);
}

void LTCFunction_half(half3 Normal, half3 viewDirectionWS, half3 positionWS, half3 BaseColor, float3 Specular, half Smoothness, half Metallic, half AO, half Alpha, out half4 OutColor)
{ 
    LTCInputData ltcInput;
    LTCSurfaceData ltcSurface;
    InitializeLTCInputData(Normal, positionWS, viewDirectionWS, ltcInput);
    InitializeLTCSurfaceData(BaseColor, Specular, Smoothness, Metallic, AO, Alpha, ltcSurface);

    OutColor = CalculateFragmentLTC(ltcInput, ltcSurface);
}

#endif
