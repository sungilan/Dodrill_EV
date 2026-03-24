Shader "Custom/Shader_Liquid"
{
    Properties
    {
        [Header(Main)]
        _LiquidTop("Liquid Top Offset", float) = 1
        _LiquidBottom("Liquid Bottom Offset", float) = -1
        _Fill("Fill", Range(0, 1)) = 1
        [HDR]_TopColor("Top Color", Color) = (0, 0.75, 0.75, 0.25)
        [HDR]_SideColor("Side Color", Color) = (0, 0.75, 0.25, 0.25)

        _TopTex("Top Texture", 2D) = "white" {}
        _SideTex("Side Texture", 2D) = "white" {}
        
        [Header(Rim)]
        _RimColor("Rim Color", Color) = (0.75, 1, 0.25, 1)
        _RimSize("Rim Power", Range(0, 1)) = 0.75
        _RimSmooth ("Smooth", Range(0, 3)) = 0.5
        
        [Header(Foam)]
        _FoamColor("Foam Color", Color) = (0.25, 1, 0.75, 1)
        _FoamSmoothness("Foam Smoothness", float) = 0
        _FoamWidth("Foam Width", float) = 0.05
        
        [Header(From Scrpit)]
        _WobbleX("Wobble X", Float) = 0
        _WobbleZ("Wobble Z", Float) = 0
        _BoundsCenter("Bounds Center", vector) = (0,0,0,0)
        [HideInInspector] _QueueOffset("_QueueOffset", Float) = 0
        [HideInInspector] _QueueControl("_QueueControl", Float) = -1

        [Header(Transparency)]
        _Opacity("Opacity", Range(0,1)) = 0.8

        [Header(Reflection)]
        _Smoothness("Smoothness", Range(0,1)) = 0.5
        _SpecColor("Specular Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "DisableBatching"="True"
        }

        Pass
        {
            Name "Liquid"
            Tags
            {
                "LightMode"="UniversalForward"
            }
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            
            HLSLPROGRAM
            
            // Pragmas
            #pragma vertex vert
            #pragma fragment frag

            // Keywords

            // Includes
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            // Main
            float _LiquidTop;
            float _LiquidBottom;
            float _Fill;
            half4 _TopColor;
            half4 _SideColor;
            half _Opacity;
            half _Smoothness;
            half4 _SpecColor;
            // Rim
            half4 _RimColor;
            half _RimSize;
            half _RimSmooth;
            // Foam
            half4 _FoamColor;
            half _FoamSmoothness;
            half _FoamWidth;
            // From Script
            half _WobbleX;
            half _WobbleZ;
            float4 _BoundsCenter;
            CBUFFER_END

            TEXTURE2D(_TopTex);
            SAMPLER(sampler_TopTex);

            TEXTURE2D(_SideTex);
            SAMPLER(sampler_SideTex);

            
            struct Attributes
            {
                float2 uv : TEXCOORD0;
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
                float3 fillPosition: TEXCOORD3;
                float2 uv : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.uv = IN.uv;
                // Position
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                
                // Normal and View Vector
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = normalize(GetWorldSpaceViewDir(OUT.positionWS));
                
                // Fill Remap
                _LiquidTop *= 0.01;
                _LiquidBottom *= 0.01;
                const float fillAmount = _LiquidBottom + _Fill * (_LiquidTop - _LiquidBottom);

                // Fill Position Offset
                const float3 boundsCenterWS = TransformObjectToWorld(_BoundsCenter.xyz);
                OUT.fillPosition = OUT.positionWS - boundsCenterWS;
                OUT.fillPosition.y -= fillAmount;

                // Wobble Ratate
                float3 rotatePos = OUT.fillPosition;
                // Rotate Z Axis
                OUT.fillPosition += float3(rotatePos.y, rotatePos.x, 0) * _WobbleX;
                // Rotate X Axis
                OUT.fillPosition += float3(rotatePos.x, rotatePos.z, -rotatePos.y) * _WobbleZ;
                
                return OUT;
            }

            half4 frag(Varyings IN, bool cullFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                IN.normalWS = NormalizeNormalPerPixel(IN.normalWS);
                IN.viewDirWS = SafeNormalize(IN.viewDirWS);
                 
                half4 outColor = half4(0,0,0,1);

                // Fill Cutout
                clip(step(IN.fillPosition.y, 0) - 0.5);

                half4 texSide = SAMPLE_TEXTURE2D(_SideTex, sampler_SideTex, IN.uv);
                half4 texTop = SAMPLE_TEXTURE2D(_TopTex, sampler_TopTex, IN.uv);

                half4 outSideColor = texSide * _SideColor;
                half4 outTopColor = texTop * _TopColor;

                // Specular
                float3 lightDir = normalize(float3(0.5,1,0.3));
                float3 halfDir = normalize(lightDir + IN.viewDirWS);
                float spec = pow(saturate(dot(IN.normalWS, halfDir)), _Smoothness * 128);
                half3 specular = spec * _SpecColor.rgb;

                // Rim
                float rim = 1.0 - saturate(dot(IN.normalWS, IN.viewDirWS));
                float rimAmont = smoothstep(_RimSize, _RimSize + _RimSmooth, rim);
                half3 rimColor = _RimColor.rgb * rimAmont;
                
                // Foam Front
                const float foamWidth = max(_FoamWidth, 0) * 0.01;
                const float foamSmoothness = max(_FoamSmoothness, 0) * 0.01;
                float frontFoamArea = smoothstep(0.5 - foamWidth - foamSmoothness, 0.5 - foamWidth, IN.fillPosition.y);
                const half3 sideFoamColor = _FoamColor.rgb * frontFoamArea;
                // Foam Back
                float backFoamArea = step(0.5 - foamWidth, IN.fillPosition.y);
                const half3 topFoamColor = _FoamColor.rgb * backFoamArea;
                
                outSideColor.rgb += saturate(rimColor + sideFoamColor) + specular;
                outTopColor.rgb += saturate(topFoamColor) + specular;
                
                // Liquid Color
                half4 finalColor = cullFace ? outSideColor : outTopColor;

                outColor.rgb = finalColor.rgb;
                outColor.a = finalColor.a * _Opacity;
                
                return outColor;
            }
            ENDHLSL
        }
    }
}