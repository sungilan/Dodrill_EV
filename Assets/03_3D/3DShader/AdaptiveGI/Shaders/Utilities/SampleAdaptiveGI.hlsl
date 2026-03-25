#pragma multi_compile _ ADAPTIVEGI_TRICUBIC_LIGHTING

TEXTURE3D(_GridSHR);
TEXTURE3D(_GridSHG);
TEXTURE3D(_GridSHB);

float3 _GridPosition;
float3 _GridSize;
float3 _GridDimension;
float3 _GridInvDimension;
float4 _FallbackSHR;
float4 _FallbackSHG;
float4 _FallbackSHB;
float _FadeStartDistance;

struct TricubicCoords
{
    float3 p0;
    float3 p1;
    half3 hw1;
};

inline TricubicCoords CalcTricubicCoords(float3 uvw)
{
    float3 x = uvw * _GridDimension - 0.5;
    float3 ix = floor(x);
    float3 fx = x - ix;

    float3 fx2 = fx * fx;
    float3 fx3 = fx2 * fx;

    float3 w0 = (1.0 - fx);
    w0 = (w0 * w0 * w0) / 6.0;
    float3 w1 = (4.0 - 6.0 * fx2 + 3.0 * fx3) / 6.0;
    float3 w3 = fx3 / 6.0;

    float3 weight0 = w0 + w1;
    float3 weight1 = 1.0 - weight0;

    TricubicCoords coords;
    coords.p0 = (ix - 0.5 + w1 / weight0) * _GridInvDimension;
    coords.p1 = (ix + 1.5 + w3 / weight1) * _GridInvDimension;
    coords.hw1 = (half3) weight1;
    
    return coords;
}

inline half4 SampleTricubicLevel(Texture3D tex, SamplerState samp, TricubicCoords coords, float lod)
{
    half4 c000 = tex.SampleLevel(samp, float3(coords.p0.x, coords.p0.y, coords.p0.z), lod);
    half4 c100 = tex.SampleLevel(samp, float3(coords.p1.x, coords.p0.y, coords.p0.z), lod);
    half4 c010 = tex.SampleLevel(samp, float3(coords.p0.x, coords.p1.y, coords.p0.z), lod);
    half4 c110 = tex.SampleLevel(samp, float3(coords.p1.x, coords.p1.y, coords.p0.z), lod);

    half4 c001 = tex.SampleLevel(samp, float3(coords.p0.x, coords.p0.y, coords.p1.z), lod);
    half4 c101 = tex.SampleLevel(samp, float3(coords.p1.x, coords.p0.y, coords.p1.z), lod);
    half4 c011 = tex.SampleLevel(samp, float3(coords.p0.x, coords.p1.y, coords.p1.z), lod);
    half4 c111 = tex.SampleLevel(samp, float3(coords.p1.x, coords.p1.y, coords.p1.z), lod);

    half4 c00 = lerp(c000, c100, coords.hw1.x);
    half4 c10 = lerp(c010, c110, coords.hw1.x);
    half4 c01 = lerp(c001, c101, coords.hw1.x);
    half4 c11 = lerp(c011, c111, coords.hw1.x);

    half4 c0 = lerp(c00, c10, coords.hw1.y);
    half4 c1 = lerp(c01, c11, coords.hw1.y);

    return lerp(c0, c1, coords.hw1.z);
}

inline half3 EvaluateAdaptiveGISH(half3 N, half4 shAr, half4 shAg, half4 shAb)
{
    half4 vA = half4(N, 1.0);
    return half3(dot(shAr, vA), dot(shAg, vA), dot(shAb, vA));
}

float3 SampleAdaptiveGI(float3 positionWS, float3 normalWS, SamplerState textureSampler)
{
    float3 uvw = (positionWS - _GridPosition) * _GridSize + 0.5;

    float3 edgeDist = min(uvw, 1.0 - uvw);
    float fade = saturate(min(edgeDist.x, min(edgeDist.y, edgeDist.z)) * _FadeStartDistance);

    half3 fallbackLighting = EvaluateAdaptiveGISH(normalWS, _FallbackSHR, _FallbackSHG, _FallbackSHB);

    half3 finalLighting;

    UNITY_BRANCH
    if (fade <= 0.0)
    {
        finalLighting = fallbackLighting;
    }
    else
    {
        half4 shR, shG, shB;
#if ADAPTIVEGI_TRICUBIC_LIGHTING
        TricubicCoords coords = CalcTricubicCoords(uvw);
        shR = SampleTricubicLevel(_GridSHR, textureSampler, coords, 0);
        shG = SampleTricubicLevel(_GridSHG, textureSampler, coords, 0);
        shB = SampleTricubicLevel(_GridSHB, textureSampler, coords, 0);
#else
        shR = _GridSHR.SampleLevel(textureSampler, uvw, 0);
        shG = _GridSHG.SampleLevel(textureSampler, uvw, 0);
        shB = _GridSHB.SampleLevel(textureSampler, uvw, 0);
#endif

        half3 gridLighting = EvaluateAdaptiveGISH(normalWS, shR, shG, shB);
        finalLighting = lerp(fallbackLighting, abs(gridLighting), fade);
    }
    
    return finalLighting;
}

void GetGridLighting_float(float3 positionWS, float3 normalWS, SamplerState textureSampler, out half3 color)
{
#ifdef SHADERGRAPH_PREVIEW
    color = 0;
#else
    color = SampleAdaptiveGI(positionWS, normalWS, textureSampler);
#endif
}

void GetGridLighting_half(float3 positionWS, half3 normalWS, SamplerState textureSampler, out half3 color)
{
#ifdef SHADERGRAPH_PREVIEW
    color = 0;
#else
    color = SampleAdaptiveGI(positionWS, normalWS, textureSampler);
#endif
}