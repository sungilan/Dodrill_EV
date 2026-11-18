// 열 왜곡(아지랑이) — 화재 사고 연출용. 불 위에 세운 빌보드 판이 뒤 화면(_CameraOpaqueTexture)을 흔들어 다시 그린다.
// URP 에서 'Opaque Texture' 가 켜져 있어야 한다(AccidentFx 가 파이프라인 에셋에서 켠다). 꺼져 있으면 판을 만들지 않는다.
Shader "Dodrill/AccidentHeat"
{
    Properties
    {
        _Strength ("Strength", Float) = 0.03
        _Speed ("Speed", Float) = 1.6
        _Noise ("Noise Scale", Float) = 5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Heat"
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 screen : TEXCOORD1; };

            float _Strength;
            float _Speed;
            float _Noise;

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float VNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.screen = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _Speed;
                float2 n = float2(VNoise(i.uv * _Noise + float2(0, -t)), VNoise(i.uv * _Noise + float2(5.2, -t * 1.3))) - 0.5;
                // 가장자리는 투명하게 — 판의 윤곽이 보이지 않게
                float mask = smoothstep(0.0, 0.3, i.uv.x) * smoothstep(1.0, 0.7, i.uv.x) * smoothstep(0.0, 0.15, i.uv.y) * smoothstep(1.0, 0.45, i.uv.y);
                float2 suv = i.screen.xy / i.screen.w + n * _Strength * mask;
                half3 col = SampleSceneColor(suv);
                return half4(col, mask);
            }
            ENDHLSL
        }
    }
}
