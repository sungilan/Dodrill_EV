/*
Copyright (c) 2017, Eric Heitz, Jonathan Dupuy, Stephen Hill and David Neubelt.
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

* If you use (or adapt) the source code in your own work, please include a 
reference to the paper:

Real-Time Polygonal-Light Shading with Linearly Transformed Cosines.
Eric Heitz, Jonathan Dupuy, Stephen Hill and David Neubelt.
ACM Transactions on Graphics (Proceedings of ACM SIGGRAPH 2016) 35(4), 2016.
Project page: https://eheitzresearch.wordpress.com/415-2/

* Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice,
this list of conditions and the following disclaimer in the documentation
and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/
// #pragma enable_d3d11_debug_symbols

#ifndef LTC_LIB_INCLUDED
#define LTC_LIB_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
#include "Assets/RealTimeAreaLights/ltcconfig.hlsl"

TEXTURE2D(ltc_1);
TEXTURE2D(ltc_2);
TEXTURE2D(LTCTexture);
SAMPLER(sampler_ltc_1);
SAMPLER(sampler_ltc_2);
SAMPLER(sampler_LTCTexture);

// Keep sync with LTC scripts
#define LTC_FLAG_ENABLED 0x1
#define LTC_FLAG_DOUBLE_SIDED 0x2
#define LTC_FLAG_DIFFUSE 0x4
#define LTC_FLAG_SPECULAR 0x8


uniform int LTCLightNum;
struct LTCLightInfo
{
    float4 color;
    float2 intensity;
    // World space vertex index 
    int vertStart;
    int vertEnd;
    // Lower 16 bits: LTCLightType
    // Higher 16 bits: LTCFlag
    int flags;
    // XYZ pos
    float3 position;
    float radius;
    float range;
    float2 startUV;
};

#define MAX_LTC_LIGHT_COUNT 32
#define LTC_LIGHT_INFO_STRIDE 4
#define MAX_LTC_LIGHT_VERTICES 1024

CBUFFER_START(LTCLightInfosBuffer)
float4 LTCLightInfos[MAX_LTC_LIGHT_COUNT * LTC_LIGHT_INFO_STRIDE];
CBUFFER_END

CBUFFER_START(LTCLightVerticesBuffer)
float4 LTCLightVertices[MAX_LTC_LIGHT_VERTICES];
CBUFFER_END

# define  LUT_SIZE  32.0
# define  LUT_SCALE ((LUT_SIZE - 1.0)/LUT_SIZE)
# define  LUT_BIAS  (0.5/LUT_SIZE)

int GetLTCTypeFromFlag(int flag)
{
    return flag & 0xFFFF;
}

int GetLTCFlagFromFlag(int flag, int bitMask)
{
    return (flag >> 16) & bitMask;
}

// This function assumes that inputs are well-behaved, e.i.
// that the line does not pass through the origin and
// that the light is (at least partially) above the surface.
float I_diffuse_line(float3 C, float3 A, float hl)
{
	// Solve C.z + h * A.z = 0.
	float h = -C.z * rcp(A.z); 	// May be Inf, but never NaN

	// Clip the line segment against the z-plane if necessary.
	float h2 = (A.z >= 0) ? max( hl, h)
						  : min( hl, h); // P2 = C + h2 * A
	float h1 = (A.z >= 0) ? max(-hl, h)
						  : min(-hl, h); // P1 = C + h1 * A

	// Normalize the tangent.
	float  as = dot(A, A); 		// |A|^2
	float  ar = rsqrt(as);  	// 1/|A|
	float  a  = as * ar;  		// |A|
	float3 T  = A * ar;	   		// A/|A|

	// Orthogonal 2D coordinates:
	// P(n, t) = n * N + t * T.
	float  tc = dot(T, C);		// C = n * N + tc * T
	float3 P0 = C - tc * T;  	// P(n, 0) = n * N
	float  ns = dot(P0, P0); 	// |P0|^2

    float nr = rsqrt(ns);       // 1/|P0|
    float n  = ns * nr;         // |P0|
    float Nz = P0.z * nr;       // N.z = P0.z/|P0|

    // P(n, t) - C = P0 + t * T - P0 - tc * T
    // = (t - tc) * T = h * A = (h * a) * T.
    float t2 = tc + h2 * a;     // P2.t
    float t1 = tc + h1 * a;     // P1.t
    float s2 = ns + t2 * t2;    // |P2|^2
    float s1 = ns + t1 * t1;    // |P1|^2
    float mr = rsqrt(s1 * s2);  // 1/(|P1|*|P2|)
    float r2 = s1 * (mr * mr);  // 1/|P2|^2
    float r1 = s2 * (mr * mr);  // 1/|P1|^2

    // I = (i1 + i2 + i3) / Pi.
    // i1 =  N.z * (P2.t / |P2|^2 - P1.t / |P1|^2).
    // i2 = -T.z * (P2.n / |P2|^2 - P1.n / |P1|^2).
    // i3 =  N.z * ArcCos[Dot[P1, P2] / (|P1| * |P2|)] / |P0|.
    float i12 = (Nz * t2 - (T.z * n)) * r2
              - (Nz * t1 - (T.z * n)) * r1;
    // Guard against numerical errors.
    float dt  = min(1, (ns + t1 * t2) * mr);
    float i3  = acos(dt) * (Nz * nr); // angle * cos(θ) / r^2

    // Guard against numerical errors.
    return INV_PI * max(0, i12 + i3);
}

// Computes 1 / length(mul(transpose(inverse(invM)), normalize(ortho))).
float ComputeLineWidthFactor(float3x3 invM, float3 ortho, float orthoSq)
{
    // transpose(inverse(invM)) = 1 / determinant(invM) * cofactor(invM).
    // Take into account the sparsity of the matrix:
    // {{a,0,b},
    //  {0,c,0},
    //  {d,0,1}}
    float a = invM[0][0];
    float b = invM[0][2];
    float c = invM[1][1];
    float d = invM[2][0];

    float  det = c * (a - b * d);
    float3 X   = float3(c * (ortho.x - d * ortho.z),
                            (ortho.y * (a - b * d)),
                        c * (-b * ortho.x + a * ortho.z));  // mul(cof, ortho)

    // 1 / length(1/s * X) = abs(s) / length(X).
    return abs(det) * rsqrt(dot(X, X) * orthoSq) * orthoSq; // rsqrt(x^2) * x^2 = x
}

float I_ltc_line(float3x3 invM, float3 center, float3 axis, float halfLength)
{
    float3 ortho   = cross(center, axis);
    float  orthoSq = dot(ortho, ortho);

    // Transform into the diffuse configuration.
    // This is a sparse matrix multiplication.
    // Pay attention to the multiplication order
    // (in case your matrices are transposed).
    float3 C = mul(invM, center);
    float3 A = mul(invM, axis);
    float w = ComputeLineWidthFactor(invM, ortho, orthoSq);
    return I_diffuse_line(C, A, halfLength) * w;
}


float3 IntegrateEdgeVec(float3 v1, float3 v2)
{
    float x = dot(v1, v2);
    float y = abs(x);

    float a = 0.8543985 + (0.4965155 + 0.0145206*y)*y;
    float b = 3.4175940 + (4.1616724 + y)*y;
    float v = a / b;

    float theta_sintheta = (x > 0.0) ? v : 0.5*rsqrt(max(1.0 - x*x, 1e-7)) - v;

    return cross(v1, v2)*theta_sintheta;
}

float IntegrateEdge(float3 v1, float3 v2)
{
    return IntegrateEdgeVec(v1, v2).z;
}

#define FIRST_VERTEX LTCLightVertices[info.vertStart]
#define LAST_VERTEX LTCLightVertices[info.vertEnd - 1]
#define LTC_TYPE_POLYGON 0
#define LTC_TYPE_DISK 1
#define LTC_TYPE_LINEAR 2
#define LTC_TYPE_TEXTURED 3

bool RayTriangleIntersect(float3 orig, float3 dir, float3 v0, float3 v1, float3 v2, out float2 bary) {
    float3 v0v1 = v1 - v0;
    float3 v0v2 = v2 - v0;
    float3 pvec = cross(dir, v0v2);
    float det = dot(v0v1, pvec);
    float invDet = 1 / det;

    float3 tvec = orig - v0;
    bary.x = dot(tvec, pvec) * invDet;

    float3 qvec = cross(tvec, v0v1);
    bary.y = dot(dir, qvec) * invDet;

    // return false when other triangle of quad should be sampled,
    // i.e. we went over the diagonal line
    return bary.x >= 0;
}

float3 LTCPolygonLight(float3 P, float3x3 Minv, float3x3 tangentMat, LTCLightInfo info, bool behind, bool twoSided)
{
    // integrate
    float sum = 0.0;
    float3 vsum = float3(0, 0, 0);
    {
        float3 preVert = normalize(mul(Minv, FIRST_VERTEX - P));
        float3 firstVert = preVert;
        bool allBelowHorizon = true;
        for (int i = info.vertStart + 1; i < info.vertEnd; i++)
        {
            float3 l = LTCLightVertices[i] - P;
            float3 localVert = mul(tangentMat, l);
            if (localVert.z > 0)
                allBelowHorizon = false;
            float3 curVertex = normalize(mul(Minv, l));
            vsum += IntegrateEdgeVec(preVert, curVertex);            
            preVert = curVertex;
        }
        if (allBelowHorizon)
            return float3(0, 0, 0);
        vsum += IntegrateEdgeVec(preVert, firstVert);
    }

    float len = length(vsum);
    vsum /= len;
    float z = vsum.z;
    if (behind)
        z = -z;

    float2 uv = float2(z*0.5 + 0.5, len);
    uv = uv*LUT_SCALE + LUT_BIAS;

    float scale = SAMPLE_TEXTURE2D(ltc_2, sampler_ltc_2, uv).w;
    sum = len*scale;
    sum = twoSided ? abs(sum) : max(0.0, sum);
    return float3(sum, sum, sum);
}

// All parameters are in rect light local space
bool GetOccludedRect(bool doubleSdied, float3 shadingPos, float2 originMinMax[2], float3 barnDoorMinMax[2], float2 sinCosBarnDorrAngle, out float2 outMinMax[2])
{
    float barnPlaneDist = originMinMax[1].x * sinCosBarnDorrAngle.x;
    float2 barnPlaneDir = float2(sinCosBarnDorrAngle.x, -sinCosBarnDorrAngle.y);
    // Only do culling in positive quadrant due to symmetry 
    float3 shadingPosAbs = float3(abs(shadingPos.xy), shadingPos.z);
    if (doubleSdied)
    {
        shadingPosAbs.z = abs(shadingPosAbs.z);
        shadingPos.z = abs(shadingPos.z);
    }
    float2 testPos = float2(shadingPosAbs.x, shadingPosAbs.z);
    bool outFrustum = dot(testPos, barnPlaneDir) > barnPlaneDist;
    
    testPos = float2(shadingPosAbs.y, shadingPosAbs.z);
    barnPlaneDist = originMinMax[1].y * sinCosBarnDorrAngle.x;
    outFrustum = outFrustum || dot(testPos, barnPlaneDir) > barnPlaneDist;

    // If inside frsutum, just use original rect
    if (!outFrustum)
    {
        outMinMax[0] = originMinMax[0];
        outMinMax[1] = originMinMax[1];
        return true;
    }
    // If shading pos is behind barndoor rect and outside frustum, its totally occluded
    else if (abs(shadingPos.z) <= barnDoorMinMax[0].z)
    {
        return false;
    }
    // Project barn door rect onto rect light plane
    float3 dir0 = barnDoorMinMax[0] - shadingPos;
    float3 dir1 = barnDoorMinMax[1] - shadingPos;
    float t = -shadingPos.z / dir0.z; 
    float3 projectedMin = shadingPos + dir0 * t;
    float3 projectedMax = shadingPos + dir1 * t;

    // Get intersection
    outMinMax[0] = max(originMinMax[0], projectedMin.xy);
    outMinMax[1] = min(originMinMax[1], projectedMax.xy);
    if (any(outMinMax[0] >= outMinMax[1]))
        return false;
    return true;
}

float3 LTCRectLight(float3 rct[4], float3 P, float3x3 Minv, float3x3 tangentMat, bool behind, bool twoSided, out float3 L[4])
{
    // integrate
    float sum = 0.0;
    float3 vsum = float3(0, 0, 0);
    {
        L[0] = mul(Minv, rct[0] - P);
        L[1] = mul(Minv, rct[1] - P);
        L[2] = mul(Minv, rct[2] - P);
        L[3] = mul(Minv, rct[3] - P);

        UNITY_LOOP
        for (int i = 0; i < 4; ++i)
        {
            int next = i + 1;
            if (i == 3)
                next = 0;
            vsum += IntegrateEdgeVec(normalize(L[i]), normalize(L[next]));            
        }
    }

    float len = length(vsum);
    vsum /= len;
    L[3] = vsum;
    float z = vsum.z;
    if (behind)
        z = -z;

    float2 uv = float2(z*0.5 + 0.5, len);
    uv = uv*LUT_SCALE + LUT_BIAS;

    float scale = SAMPLE_TEXTURE2D(ltc_2, sampler_ltc_2, uv).w;
    sum = len*scale;
    sum = twoSided ? abs(sum) : max(0.0, sum);
    return float3(sum, sum, sum);
}

struct LTCInputData
{
    float3 normalWS;
    float3 viewDirectionWS;
    float3 positionWS;
};

struct LTCSurfaceData
{
    half3 albedo;
    half alpha;
    half smoothness;
    half3 specular;
    half metallic;
};
#define LIGHT_BORDER_WIDTH 0.5

float CalculateFilterLod(float3 L[4])
{
    float3 V1 = L[0] - L[1];
    float3 V2 = L[2] - L[1];
    float3 planeOrtho = (cross(V1, V2));
    float planeAreaSquared = dot(planeOrtho, planeOrtho);
    float planeDistxPlaneArea = dot(planeOrtho, L[1]);
    float d = abs(planeDistxPlaneArea) / pow(planeAreaSquared, 0.75);
    return log(512*d)/log(3.0);
}

half4 CalculateFragmentLTC(LTCInputData inputData, LTCSurfaceData surfaceData)
{
    BRDFData brdfData;
    InitializeBRDFData(surfaceData.albedo, surfaceData.metallic, surfaceData.specular, surfaceData.smoothness, surfaceData.alpha, brdfData);
    #ifdef ENABLE_PERCEPTUAL_RIGHT_LIGHT
    float roughness = brdfData.perceptualRoughness;
    #else
    float roughness = brdfData.roughness;
    #endif
    float3 normal = normalize(inputData.normalWS);
    float3 view = normalize(inputData.viewDirectionWS);
    float NdotV = max(dot(normal, view), 0.0);
    
    float2 uv = float2(roughness, sqrt(1.0 - NdotV));
    uv = uv*LUT_SCALE + LUT_BIAS;
                
    float4 t1 = SAMPLE_TEXTURE2D(ltc_1, sampler_ltc_1, uv);
    float4 t2 = SAMPLE_TEXTURE2D(ltc_2, sampler_ltc_2, uv);

    float3x3 Minv = float3x3(
        float3(t1.x,   0, t1.z),
        float3(   0,   1,    0),
        float3(t1.y,   0, t1.w)
    );

    // construct orthonormal basis around N
    float3 T1, T2;
    T1 = normalize(view - normal*NdotV);
    T2 = cross(normal, T1);
    float3x3 TangentMat = float3x3(T1, T2, normal);
    // rotate area light in (T1, T2, N) basis
    float3x3 MT = mul(Minv, TangentMat);
    
    float3 c = float3(0, 0, 0);
    for (int i = 0; i < LTCLightNum; ++i)
    {
        LTCLightInfo info;
        info.color = LTCLightInfos[i * LTC_LIGHT_INFO_STRIDE];
        float4 tmp = LTCLightInfos[i * LTC_LIGHT_INFO_STRIDE+1];
        info.intensity = tmp.xy;
        info.vertStart = asint(tmp.z);
        info.vertEnd = asint(tmp.w);
        tmp = LTCLightInfos[i * LTC_LIGHT_INFO_STRIDE+2];
        info.flags = asint(tmp.x);
        info.position = tmp.yzw;
        tmp = LTCLightInfos[i * LTC_LIGHT_INFO_STRIDE+3];
        info.radius = tmp.x;
        info.range = tmp.y;
        info.startUV = tmp.zw;

        float distance = length(inputData.positionWS - info.position);
        int type = GetLTCTypeFromFlag(info.flags);
        bool isSpec = GetLTCFlagFromFlag(info.flags, LTC_FLAG_SPECULAR);
        bool isDiff = GetLTCFlagFromFlag(info.flags, LTC_FLAG_DIFFUSE);
        bool twoSided = GetLTCFlagFromFlag(info.flags, LTC_FLAG_DOUBLE_SIDED);
        switch (type)
        {
#ifdef ENABLE_POLYGON_LIGHT
        case LTC_TYPE_POLYGON:
            if (distance < info.range)
            {
                float3 lightNormal = cross(LAST_VERTEX - info.position, FIRST_VERTEX - info.position);
                float3 dir = FIRST_VERTEX - inputData.positionWS;

                // Skip fragments behind one-sided light
                bool behind = dot(dir, lightNormal) < 0.0;
                if(twoSided || !behind)
                {
                    float3 spec = 0, diff = 0;
                    if (isSpec)
                    {
                        spec = LTCPolygonLight(inputData.positionWS, MT, TangentMat, info, behind, twoSided);
                        spec *= brdfData.specular*t2.x + (1.0 - brdfData.specular)*t2.y;    
                    }

                    if (isDiff)
                    {
                        diff = LTCPolygonLight(inputData.positionWS, TangentMat, TangentMat, info, behind, twoSided);
                    }
                    float3 iradiance = clamp(info.color * (spec * info.intensity.x + brdfData.diffuse*diff * info.intensity.y), 0, max(info.intensity.x, info.intensity.y));
                    // Fade according to fragment distance
                    iradiance *= smoothstep(0, info.range * LIGHT_BORDER_WIDTH, info.range - distance);
                    c += iradiance;
                }
            }
            break;
#endif
#ifdef ENABLE_TEXTURED_LIGHT
        case LTC_TYPE_TEXTURED:
            {
                // Rect light geomdata
                float4 xAxis = LTCLightVertices[info.vertStart];
                float4 yAxis = LTCLightVertices[info.vertStart + 1];
                float4 zAxis = LTCLightVertices[info.vertStart + 2];
                float4 barnDoorExtent = LTCLightVertices[info.vertStart + 3];

                // Convert to rect light local space
                float3x3 mat = float3x3(xAxis.xyz, yAxis.xyz, zAxis.xyz);
                float2 originalRct[2] = {float2(-xAxis.w * 0.5, -yAxis.w * 0.5), float2(xAxis.w * 0.5, yAxis.w * 0.5)};
                float2 clippedRct[2];
                float3 barnDoorRct[2] = {float3(-barnDoorExtent.x * 0.5, -barnDoorExtent.y * 0.5, zAxis.w), float3(barnDoorExtent.x * 0.5, barnDoorExtent.y * 0.5, zAxis.w)};
                float3 spLightSpace = mul(mat, inputData.positionWS - info.position);

                // Skip fragments behind one-sided light
                bool behind = spLightSpace.z < 0;

                if ((twoSided || !behind) && GetOccludedRect(twoSided, spLightSpace, originalRct, barnDoorRct, barnDoorExtent.zw, clippedRct))
                {
                    float3 rct[4];
                    rct[0] = info.position + xAxis.xyz * clippedRct[1].x + yAxis.xyz * clippedRct[0].y;
                    rct[1] = info.position + xAxis.xyz * clippedRct[0].x + yAxis.xyz * clippedRct[0].y;
                    rct[2] = info.position + xAxis.xyz * clippedRct[0].x + yAxis.xyz * clippedRct[1].y;
                    rct[3] = info.position + xAxis.xyz * clippedRct[1].x + yAxis.xyz * clippedRct[1].y;

                    float3 L[4];
                    float3 spec = 0, diff = 0;
                    if (isSpec)
                    {
                        // Transform shading pos cosine local space to light space
                        float LTCDet = t1.x * t1.w - t1.y * t1.z;
                        float4 InvLTCMat = t1 / LTCDet;
                        float3x3 InvLTC = {
                            float3( InvLTCMat.w, 0,-InvLTCMat.z ),
                            float3(	          0, 1,           0 ),
                            float3(-InvLTCMat.y, 0, InvLTCMat.x )
                        };
                        float3x3 convertMat = mul(mat, mul(transpose(TangentMat), InvLTC));

                        spec = LTCRectLight(rct, inputData.positionWS, MT, TangentMat,behind, twoSided, L);
                        spec *= brdfData.specular*t2.x + (1.0 - brdfData.specular)*t2.y;
                        // Calculate UV and sample textured light
                        float3 dir = mul(convertMat, L[3]);
                        float t = -spLightSpace.z/dir.z;
                        float3 hitPos = spLightSpace + dir * t;

                        float2 bary = (hitPos.xy - originalRct[0])/float2(xAxis.w, yAxis.w);
#ifdef ENABLE_TEXTURED_FLIP_HORIZONTAL
                        bary.x = 1 - bary.x;
#endif
                        bary = bary * 0.25 + info.startUV;
                        spec *= SAMPLE_TEXTURE2D_LOD(LTCTexture, sampler_LTCTexture, bary, CalculateFilterLod(L));
                    }

                    if (isDiff)
                    {
                        diff = LTCRectLight(rct, inputData.positionWS, TangentMat, TangentMat, behind, twoSided, L);
                        // Calculate UV and sample textured light
                        float3x3 convertMat = mul(mat, transpose(TangentMat));
                        float3 dir = mul(convertMat, L[3]);
                        float t = -spLightSpace.z/dir.z;
                        float3 hitPos = spLightSpace + dir * t;

                        float2 bary = (hitPos.xy - originalRct[0])/float2(xAxis.w, yAxis.w);
                        bary = bary * 0.25 + info.startUV;
                        diff *= SAMPLE_TEXTURE2D_LOD(LTCTexture, sampler_LTCTexture, bary, CalculateFilterLod(L));
                    }
                    float3 iradiance = clamp(info.color * (spec * info.intensity.x + brdfData.diffuse*diff * info.intensity.y), 0, max(info.intensity.x, info.intensity.y));
                    // Fade according to fragment distance
                    iradiance *= smoothstep(0, info.range * LIGHT_BORDER_WIDTH, info.range - distance);
                    c += iradiance;
                }
            }
            break;
#endif
#ifdef ENABLE_LINEAR_LIGHT
        case LTC_TYPE_LINEAR:
            {
                // Calculate capsule range
                float3 v = inputData.positionWS - FIRST_VERTEX;
                float3 p = LAST_VERTEX - FIRST_VERTEX;
                float3 dir = normalize(p);
                float len = length(p);
                float t = clamp(dot(v, dir), 0, len);
                float3 nearestP = FIRST_VERTEX + t * dir;
                float dist = length(inputData.positionWS - nearestP);
                
                float3 p1 = mul(TangentMat, FIRST_VERTEX - inputData.positionWS);
                float3 p2 = mul(TangentMat, LAST_VERTEX - inputData.positionWS);

                if ((p1.z > 0.0 || p2.z > 0.0) && dist < info.range)
                {
                    float3 center = (p1 + p2) * 0.5;
                    float3 axis = p1 - p2;
                    float halfLength = length(axis) * 0.5;
                    axis = normalize(axis);

                    float3 spec = 0, diff = 0;
                    if (isSpec)
                    {
                        spec = I_ltc_line(Minv, center, axis, halfLength);
                        spec *= brdfData.specular*t2.x + (1.0 - brdfData.specular)*t2.y;
                    }

                    if (isDiff)
                    {
                        diff = I_ltc_line(float3x3( 1.0, 0.0, 0.0,
                                                    0.0, 1.0, 0.0,
                                                    0.0, 0.0, 1.0), center, axis, halfLength);
                    }
                    float3 irradiance = info.radius * info.color * clamp((spec *info.intensity.x + brdfData.diffuse*diff * info.intensity.y) * INV_TWO_PI, 0, max(info.intensity.x, info.intensity.y));
                    irradiance *= smoothstep(0, info.range * LIGHT_BORDER_WIDTH, info.range - dist);
                    c+= irradiance;;
                }
            }
            break;
#endif
        default:
            break;
        }
    } 
    return float4(c, surfaceData.alpha);
}

#endif
