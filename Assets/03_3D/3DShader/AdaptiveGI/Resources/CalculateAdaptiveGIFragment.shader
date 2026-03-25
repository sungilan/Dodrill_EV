Shader "Hidden/CalculateAdaptiveGIFragment"
{
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma multi_compile _ ENABLE_SHADOWS
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct Light
            {
                float3 color;
                float intensity;
                float conePower;
                float power;
                float deadzone;
                float rangeSquared;
                float outerSpotAngle;
                float innerSpotAngle;
                float3 position;
                float3 direction;
                float shadowStrength;
                float shadowSoftness;
                float shadowDepthBias;
                float shadowStartBias;
            };

            Texture2D<float4> _LightTexture;
            int _LightStartIndex;
            int _LightEndIndex;

            Texture3D<half> _ShadowGrid;
            SamplerState sampler_ShadowGrid;

            int3 _GridSize;
            float _GridResolution;
            int _CurrentSlice;

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            struct FragOutput
            {
                half4 shR : SV_Target0;
                half4 shG : SV_Target1;
                half4 shB : SV_Target2;
            };

            Light DecodeLightData(int lightIndex)
            {
                Light light;
                float4 data1 = _LightTexture.Load(int3(0, lightIndex, 0));
                float4 data2 = _LightTexture.Load(int3(1, lightIndex, 0));
                float4 data3 = _LightTexture.Load(int3(2, lightIndex, 0));
                float4 data4 = _LightTexture.Load(int3(3, lightIndex, 0));
                float4 data5 = _LightTexture.Load(int3(4, lightIndex, 0));
    
                light.color = data1.rgb;
                light.intensity = data1.a;

                light.conePower = data2.r;
                light.power = data2.g;
                light.deadzone = data2.b;
                light.rangeSquared = data2.a;

                light.outerSpotAngle = data3.r;
                light.innerSpotAngle = data3.g;
                light.position = float3(data3.b, data3.a, data4.r);

                light.direction = float3(data4.g, data4.b, data4.a);

                light.shadowStrength = data5.r;
                light.shadowSoftness = data5.g;
                light.shadowDepthBias = data5.b;
                light.shadowStartBias = data5.a;

                return light;
            }

            FragOutput frag(v2f i)
            {
                FragOutput o;

                const uint3 gridPos = uint3(
                    i.uv.x * _GridSize.x,
                    i.uv.y * _GridSize.y,
                    _CurrentSlice
                );

                half4 lightR = 0;
                half4 lightG = 0;
                half4 lightB = 0;

                for (int i = _LightStartIndex; i < _LightEndIndex; i++)
                {
                    Light light = DecodeLightData(i);
                    const half3 difference = (light.position - gridPos) / _GridResolution;
                    const half lengthSqDifference = dot(difference, difference);
                    if (lengthSqDifference > light.rangeSquared)
                    {
                        continue;
                    }

                    const half3 direction = normalize(difference);
                    const half angle = acos(saturate(dot(-direction, light.direction)));
                    if (angle > light.outerSpotAngle)
                    {
                        continue;
                    }

                    const half coneFalloff = pow(abs(1 - smoothstep(light.outerSpotAngle - light.innerSpotAngle, light.outerSpotAngle, angle)), light.conePower);
                    const half edgeFalloff = max(0, 1 - lengthSqDifference / light.rangeSquared);
                    half attenuation = edgeFalloff * coneFalloff * pow(abs(light.intensity / max(lengthSqDifference, light.deadzone)), light.power);
					
					#if ENABLE_SHADOWS
                    int numSteps = int(_GridResolution * (sqrt(lengthSqDifference) - light.shadowDepthBias - light.shadowStartBias));

                    half rayLight = 1;
                    float3 currentSamplePos = light.position - direction * light.shadowStartBias * _GridResolution;
        
                    [loop]
                    for (int s = 0; s < numSteps; s++)
                    {
                        float3 uvw = (currentSamplePos + 0.5) / _GridSize;
                        rayLight -= _ShadowGrid.SampleLevel(sampler_ShadowGrid, uvw, 0) * rayLight * light.shadowSoftness;
            
                        currentSamplePos -= direction;
                    }
        
                    attenuation *= 1 - ((1 - saturate(rayLight)) * light.shadowStrength);
                    #endif

                    const half4 sh = half4(direction.x, direction.y, direction.z, 1) * attenuation;

                    lightR += sh * light.color.r;
                    lightG += sh * light.color.g;
                    lightB += sh * light.color.b;
                }

                o.shR = lightR;
                o.shG = lightG;
                o.shB = lightB;

                return o;
            }
            ENDCG
        }
    }
}
