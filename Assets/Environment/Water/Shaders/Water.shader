Shader "Unlit/Water"
{
    Properties
    {
        _ColorTexture("ColorTexture", 2D) = "white" {}
        _Normal1Texture("Normal1 Texture", 2D) = "bump" {}
        _Normal2Texture("Normal2 Texture", 2D) = "bump" {}
        _ColorBase("ColorBase", Color) = (1,1,1,1)
        _ColorDepthMin("ColorDepthMin", Color) = (1,1,1,1)
        _ColorDepthMax("ColorDepthMax", Color) = (1,1,1,1)
        _SpecularColor("SpecularColor", Color) = (1,1,1,1)
        _Normal1Strength("NormalStrength", Float) = 1.0
        _Normal2Strength("NormalStrength", Float) = 1.0
        _SpecularPower("SpecularPower", Float) = 1.0
        _SpecularIntensity("SpecularIntensity", Float) = 1.0
        _ShadowIntenstiy("ShadowIntenstiy", Float) = 1.0
        _LightIntensity("LightIntensity", Float) = 1.0
        _WaveSpeed("Wave Speed", Vector) = (0, 0, 0, 0)
        _WaveFrequency("Wave Frequency", Float) = 1.0
        _WaveScale("Wave Scale", Float) = 1.0
        _Depth("Depth", Float) = 10
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "Queue" = "Transparent"
        }

        Pass
        {
            Tags
            {
                "LightMode" = "Water"
            }
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // フォグ用のシェーダバリアントを生成するための記述
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE


            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float3 viewDir : TEXCOORD9;
                float3 worldPos : TEXCOORD1;
                float3 normal : TEXCOORD2;
                float4 tangent : TEXCOORD3;
                
                float2 colorUV : TEXCOORD4;
                float2 normal1UV : TEXCOORD5;
                float2 normal2UV : TEXCOORD6;

                half fogFactor : TEXCOORD8;
            };

            TEXTURE2D(_ColorTexture);
            SAMPLER(sampler_ColorTexture);

            TEXTURE2D(_Normal1Texture);
            SAMPLER(sampler_Normal1Texture);

            TEXTURE2D(_Normal2Texture);
            SAMPLER(sampler_Normal2Texture);

            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);

            TEXTURE2D(_WaterDynamicEffectsBuffer);
            SAMPLER(sampler_WaterDynamicEffectsBuffer);

            float4 _ColorTexture_ST;
            float4 _Normal1Texture_ST;
            float4 _Normal2Texture_ST;

            float4 _ColorBase;
            float4 _ColorDepthMin;
            float4 _ColorDepthMax;
            float4 _SpecularColor;

            float4 _WaveSpeed;

            half _LightIntensity;
            half _Normal1Strength;
            half _Normal2Strength;
            half _SpecularPower;
            half _SpecularIntensity;
            half _Depth;
            half _ShadowIntenstiy;
            half _WaveFrequency;
            half _WaveScale;

            float4 _WaterDynamicEffectsCoords;

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                float3 worldPosition = TransformObjectToWorld(IN.positionOS).xyz;
                float3 wavePosition = worldPosition;
                wavePosition.xz *= _WaveFrequency;
                wavePosition.xz += (length(_WaveSpeed.xyzw)) * _Time.y * 0.25;
                wavePosition.x = sin(wavePosition.x);
                wavePosition.z = cos(wavePosition.z);
                wavePosition.y = (wavePosition.x + wavePosition.z) * _WaveScale;
                worldPosition.y += wavePosition.y;

                OUT.worldPos = worldPosition;


                OUT.positionHCS = TransformWorldToHClip(worldPosition);
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);
                OUT.normal = TransformObjectToWorldNormal(IN.normal);
                OUT.viewDir = TransformWorldToView(OUT.worldPos);

                float sign = IN.tangent.w * GetOddNegativeScale();
                VertexNormalInputs vni = GetVertexNormalInputs(IN.normal, IN.tangent);
                OUT.tangent = float4(vni.tangentWS, sign);

                OUT.colorUV = TRANSFORM_TEX(OUT.worldPos.xz, _ColorTexture);
                OUT.normal1UV = TRANSFORM_TEX(OUT.worldPos.xz, _Normal1Texture) + (_WaveSpeed.xy * _Time.x);
                OUT.normal2UV = TRANSFORM_TEX(OUT.worldPos.xz, _Normal2Texture) + (_WaveSpeed.zw * _Time.x);

                OUT.fogFactor = ComputeFogFactor(OUT.positionHCS.z);
                return OUT;
            }

            inline half3 DecodeHDR(half4 data, half4 decodeInstructions)
            {
                // Take into account texture alpha if decodeInstructions.w is true(the alpha value affects the RGB channels)
                half alpha = decodeInstructions.w * (data.a - 1.0) + 1.0;

                // If Linear mode is not supported we can skip exponent part
#if defined(UNITY_COLORSPACE_GAMMA)
                return (decodeInstructions.x * alpha) * data.rgb;
#else
#   if defined(UNITY_USE_NATIVE_HDR)
                return decodeInstructions.x * data.rgb; // Multiplier for future HDRI relative to absolute conversion.
#   else
                return (decodeInstructions.x * pow(alpha, decodeInstructions.y)) * data.rgb;
#   endif
#endif
            }
            
            float2 DynamicEffectsSampleCoords(float3 positionWS)
            {
                return (positionWS.xz - _WaterDynamicEffectsCoords.xy) / (_WaterDynamicEffectsCoords.z);
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                float2 screenSpaceUV = IN.screenPos.xy / IN.screenPos.w;
                float cameraDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, screenSpaceUV).r;
                float3 worldPos = ComputeWorldSpacePosition(screenSpaceUV, cameraDepth, UNITY_MATRIX_I_VP);

                half depthDiff = (IN.worldPos.y - worldPos.y);
                half depthIntensity = saturate(depthDiff / _Depth);

                float4 depthColor = lerp(_ColorDepthMin, _ColorDepthMax, depthIntensity);

                float3 normal1TS = UnpackNormalScale(SAMPLE_TEXTURE2D(_Normal1Texture, sampler_Normal1Texture, IN.normal1UV), _Normal1Strength);
                float3 normal2TS = UnpackNormalScale(SAMPLE_TEXTURE2D(_Normal2Texture, sampler_Normal2Texture, IN.normal2UV), _Normal2Strength);

                float3 normalTS = normal1TS + normal2TS;
                float3 bitangent = IN.tangent.w * cross(IN.normal.xyz, IN.tangent.xyz);
                float3 normal = normalize(mul(normalTS, float3x3(IN.tangent.xyz, bitangent.xyz, IN.normal.xyz)));

                float2 effectUV = DynamicEffectsSampleCoords(IN.worldPos);
                float4 effect = SAMPLE_TEXTURE2D(_WaterDynamicEffectsBuffer, sampler_WaterDynamicEffectsBuffer, effectUV);
                effect.xy = effect.xy * 2.0 - 1.0;
                normal = normal + (effect * effect.w);

                Light mainLight = GetMainLight();
                float3 L = normalize(mainLight.direction);
                float3 V = normalize(_WorldSpaceCameraPos - IN.worldPos.xyz);
                float3 N = IN.normal;
                float3 N2 = normal;
                
                float specular = pow(max(0.0, dot(reflect(-L, N2), V) * dot(reflect(-L, N), V) ), _SpecularPower);  // reflection
                specular = specular * _SpecularIntensity;
                float4 specularColor = _SpecularColor * specular;

                float LN2 = max(0.0, dot(L, N2));
                half4 diffuse = 1;
                diffuse.rgb = LN2 * mainLight.color * _LightIntensity;

                half distanceXZ = length(IN.worldPos.xz - worldPos.xz);
                half shorline = (1 - saturate(distanceXZ / 1));
                shorline *= LN2;

                half4 shadowCoord = TransformWorldToShadowCoord(IN.worldPos.xyz);
                float attenuation = SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_MainLightShadowmapTexture, shadowCoord.xyz);
                attenuation = max(_ShadowIntenstiy, attenuation);

                half3 reflDir = reflect(-V, N2);
                half4 refColor = SAMPLE_TEXTURECUBE(unity_SpecCube0, samplerunity_SpecCube0, reflDir);
                refColor.rgb = DecodeHDR(refColor, unity_SpecCube0_HDR);

                // Indirect Specular
                half ndotv = abs(dot(N, V));
                refColor = refColor * lerp(0, 1, pow(1 - ndotv, 5)) * 2;

                float4 finalColor = saturate(((diffuse * _ColorBase) * depthColor) + (specularColor * attenuation) + 0) + refColor;
                finalColor.rgb = MixFog(finalColor.rgb, IN.fogFactor);

                return finalColor;
            }
            ENDHLSL
        }
    }
}
