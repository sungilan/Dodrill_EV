Shader "LTC/PrefilterTexture"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "Blur"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // #pragma enable_d3d11_debug_symbols

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                uint vertexID   : SV_VertexID;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            int _SampleCount;
            float _FilterSize;
            float _Deviation;
            float2 _FilterDir;

            v2f vert (appdata v)
            {
                const float3 vertices[3] =
                {
                    float3(-3, -1, 0),
                    float3(1, -1, 0),
                    float3(1, 3, 0),
                };

                const float2 uvs[3] =
                {
                    float2(-1, 1),
                    float2(1, 1),
                    float2(1, -1)
                };
                v2f o;
                o.vertex = float4(vertices[v.vertexID], 1);
                o.uv = TRANSFORM_TEX(uvs[v.vertexID], _MainTex);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                float z = ceil(max(0, log2(_FilterSize * _MainTex_TexelSize.w / _SampleCount)));
                // sample the texture
                float4 col = 0;
                float accWeight = 0;
                float sq = _Deviation * _Deviation;
                for (int k = 0; k < _SampleCount; ++k)
                {
                    float x = k / (_SampleCount - 1.0) - 0.5;
                    // When using mipmap to accelerate blur, the weight is better using exp(-2*x^2)
                    // instead of original Gaussian weight, refer to this:
                    // https://www.shadertoy.com/view/WtKfD3
                    // float w = rsqrt(2*PI*sq) * exp(-(x*x/(2*sq)));
                    float w = exp(-(x*x * 2));
                    accWeight += w;
                    
                    // TODO: Utilize hardware filter to reduce texture fetch
                    // https://lisyarus.github.io/blog/posts/compute-blur.html#section-separable-hw
                    col += w * SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, i.uv + _FilterDir * x * _FilterSize, z);
                }
                return col / accWeight;
            }
            ENDHLSL
        }
    }
}
