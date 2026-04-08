Shader "Hidden/ApplyAdaptiveGI"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SphericalHarmonics.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/NormalBuffer.hlsl"
    #include_with_pragmas "Assets/AdaptiveGI/Shaders/Utilities/SampleAdaptiveGI.hlsl"

    TEXTURE2D_X(_GBufferTexture0);

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 texcoord   : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
        output.texcoord = GetFullScreenTriangleTexCoord(input.vertexID);
        return output;
    }

    inline float3 GetNormalWS(uint2 positionSS)
    {
        NormalData normalData;
        const float4 normalBuffer = LOAD_TEXTURE2D_X(_NormalBufferTexture, positionSS);
        DecodeFromNormalBuffer(normalBuffer, positionSS, normalData);
        return normalData.normalWS;
    }

    float4 ApplyAdaptiveGIPass(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        uint2 positionSS = input.texcoord * _ScreenSize.xy;
        float depth = LoadCameraDepth(positionSS);
        if (depth == UNITY_RAW_FAR_CLIP_VALUE)
        {
            return 0;
        }

        PositionInputs posInput = GetPositionInput(input.positionCS.xy, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
        float3 positionAWS = posInput.positionWS + _WorldSpaceCameraPos.xyz;
        float3 albedo = LOAD_TEXTURE2D_X(_GBufferTexture0, positionSS).rgb;
        float ao = 1 - LOAD_TEXTURE2D_X(_AmbientOcclusionTexture, positionSS).r;
        float3 gi = SampleAdaptiveGI(positionAWS, GetNormalWS(positionSS), s_linear_clamp_sampler);
        return float4(albedo * gi * ao * GetCurrentExposureMultiplier(), 0);
    }

    ENDHLSL
    SubShader
    {
        Pass
        {
            PackageRequirements 
            {
                "com.unity.render-pipelines.high-definition"
            }
            Name "ApplyAdaptiveGI"
            ZWrite Off
            ZTest Always
            Blend One One
            Cull Off

            HLSLPROGRAM
                #pragma fragment ApplyAdaptiveGIPass
                #pragma vertex Vert
            ENDHLSL
        }
    }

    Fallback Off
}