Shader "Unlit/Water"
{
    Properties
    {
        [Header(Normal)]
        [Space(5)]
        _Normal1Texture("Normal1 Texture", 2D) = "bump" {}
        _Normal2Texture("Normal2 Texture", 2D) = "bump" {}
        
        _Normal1Strength("NormalStrength", Float) = 1.0
        _Normal2Strength("NormalStrength", Float) = 1.0

        [Header(Depth)]
        [Space(5)]
        _ColorDepthMin("ColorDepthMin", Color) = (1,1,1,1)
        _ColorDepthMax("ColorDepthMax", Color) = (1,1,1,1)
        _Depth("Depth", Float) = 10

        [Header(Specular)]
        [Space(5)]
        _SpecularColor("SpecularColor", Color) = (1,1,1,1)
        _SpecularIntensity("SpecularIntensity", Float) = 1.0
        _SpecularPower("SpecularPower", Float) = 1.0

        [Header(Light)]
        [Space(5)]
        _ColorBaseBright("ColorBaseBright", Color) = (1,1,1,1)
        _ColorBaseDark("ColorBaseDark", Color) = (1,1,1,1)
        _LightIntensity("LightIntensity", Float) = 1.0
        _ShadowIntenstiy("ShadowIntenstiy", Float) = 1.0

        [Header(Foam)]
        [Space(5)]
        _FoamTexture("FoamTexture", 2D) = "white" {}
        _FoamHeight("FoamHeight", Float) = 1
        _FoamStep("FoamStep", Float) = 0.5

        [Header(Distortion)]
        [Space(5)]
        _DistortionTexture("DistortionTexture", 2D) = "bump" {}
        _DistortionStrengthX("DistortionStrengthX", Float) = 1
        _DistortionStrengthY("DistortionStrengthY", Float) = 1

        [Header(Reflection)]
        [Space(5)]
        _ReflectionCubemap("ReflectionCubemap", CUBE) = "" {}
        _ReflectionIntensity("ReflectionIntensity", Float) = 1
        _ReflectionPower("ReflectionPower", Float) = 5

        [Header(Wave)]
        [Space(5)]
        _WaveSpeed("Wave Speed", Vector) = (0, 0, 0, 0)
        _WaveFrequency("Wave Frequency", Float) = 1.0
        _WaveScale("Wave Scale", Float) = 1.0
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

            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // フォグ用のシェーダバリアントを生成するための記述
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ _FORWARD_PLUS


            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "TexturePacking.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float3 viewDir : TEXCOORD9;
                float3 worldPos : TEXCOORD1;
                float3 normal : TEXCOORD2;
                float3 tangent : TEXCOORD3;
                float3 bitangent : TEXCOORD4;
                
                float2 normal1UV : TEXCOORD5;
                float2 normal2UV : TEXCOORD6;
                float2 foamUV : TEXCOORD7;
                float2 distortionUV : TEXCOORD8;

                half fogFactor : TEXCOORD10;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_CameraOpaqueTexture);
            SAMPLER(sampler_CameraOpaqueTexture);
            
            TEXTURE2D(_CameraDepthTexture);
            SAMPLER(sampler_CameraDepthTexture);
            
            TEXTURE2D(_WaterDynamicEffectsBuffer);
            SAMPLER(sampler_WaterDynamicEffectsBuffer);
            
            TEXTURE2D(_Normal1Texture);
            SAMPLER(sampler_Normal1Texture);

            TEXTURE2D(_Normal2Texture);
            SAMPLER(sampler_Normal2Texture);

            TEXTURE2D(_FoamTexture);
            SAMPLER(sampler_FoamTexture);

            TEXTURE2D(_DistortionTexture);
            SAMPLER(sampler_DistortionTexture);

            TEXTURECUBE(_ReflectionCubemap);
            SAMPLER(sampler_ReflectionCubemap);

            // Normal
            float4 _Normal1Texture_ST;
            float4 _Normal2Texture_ST;
            half _Normal1Strength;
            half _Normal2Strength;

            // Distortion
            float4 _DistortionTexture_ST;
            half _DistortionStrengthX;
            half _DistortionStrengthY;

            // Light
            float4 _ColorBaseBright;
            float4 _ColorBaseDark;
            half _LightIntensity;
            half _ShadowIntenstiy;
            
            // Depth
            float4 _ColorDepthMin;
            float4 _ColorDepthMax;
            half _Depth;

            // Specular
            float4 _SpecularColor;
            half _SpecularIntensity;
            half _SpecularPower;

            // Wave
            half4 _WaveSpeed;
            half _WaveFrequency;
            half _WaveScale;

            // Foam
            float4 _FoamTexture_ST;
            half _FoamHeight;
            half _FoamStep;

            // Reflection
            half _ReflectionIntensity;
            half _ReflectionPower;

            // Effect
            float4 _WaterDynamicEffectsCoords;

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

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

                VertexNormalInputs vni = GetVertexNormalInputs(IN.normal, IN.tangent);
                float sign = IN.tangent.w * GetOddNegativeScale();
                OUT.tangent = float4(vni.tangentWS, sign).xyz;
                
                float3 bitangent = cross(OUT.normal.xyz, OUT.tangent.xyz);
                OUT.bitangent = bitangent;

                // UV
                OUT.normal1UV = TRANSFORM_TEX(OUT.worldPos.xz, _Normal1Texture) + (_WaveSpeed.xy * _Time.x);
                OUT.normal2UV = TRANSFORM_TEX(OUT.worldPos.xz, _Normal2Texture) + (_WaveSpeed.zw * _Time.x);
                OUT.foamUV = TRANSFORM_TEX(OUT.worldPos.xz, _FoamTexture) + (_Time.x);
                OUT.distortionUV = TRANSFORM_TEX(OUT.worldPos.xz, _DistortionTexture) + (_WaveSpeed.xy * _Time.x);

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
            
            void CalculateShorline(float3 worldPos, float3 selfPos, float shorlineHeight, out float shoreline)
            {
                float leng = length(worldPos - selfPos);
                shoreline = saturate((shorlineHeight - leng) / shorlineHeight);
            }
            
            float4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float2 screenSpaceUV = IN.screenPos.xy / IN.screenPos.w;

                // Distortion
                float2 distortion = SAMPLE_TEXTURE2D(_DistortionTexture, sampler_DistortionTexture, IN.distortionUV).xy;
                distortion = distortion - 0.5;
                distortion.x *= _DistortionStrengthX;
                distortion.y *= _DistortionStrengthY;

                distortion.x += 0.015;
                screenSpaceUV.xy += distortion;

                float cameraDepth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, screenSpaceUV).r;
                float3 worldPos = ComputeWorldSpacePosition(screenSpaceUV, cameraDepth, UNITY_MATRIX_I_VP);

                // Depth
                half depthDiff = (IN.worldPos.y - worldPos.y);
                half depthIntensity = saturate(depthDiff / _Depth);
                float4 depthColor = lerp(_ColorDepthMin, _ColorDepthMax, depthIntensity);

                // Normal
                float3 normal1TS = UnpackNormalScale(SAMPLE_TEXTURE2D(_Normal1Texture, sampler_Normal1Texture, IN.normal1UV + distortion), _Normal1Strength);
                float3 normal2TS = UnpackNormalScale(SAMPLE_TEXTURE2D(_Normal2Texture, sampler_Normal2Texture, IN.normal2UV + distortion), _Normal2Strength);
                float3 normalTS = normal1TS + normal2TS;
                float3 normal = mul(normalTS, float3x3(IN.tangent.xyz, IN.bitangent.xyz, IN.normal.xyz));

                // Effect
                float2 effectUV = DynamicEffectsSampleCoords(IN.worldPos);
                float4 effect = SAMPLE_TEXTURE2D(_WaterDynamicEffectsBuffer, sampler_WaterDynamicEffectsBuffer, effectUV + distortion);
                float4 effectColor = effect;
                
                normal = normalize(normal);

                // Light 
                Light mainLight = GetMainLight();
                float3 L = mainLight.direction;
                float3 V = normalize(_WorldSpaceCameraPos - IN.worldPos.xyz);
                float3 N = IN.normal;
                float3 N2 = normal;

                float LN2 = saturate(dot(L, N2));
                float DRLN2V = dot(reflect(-L, N2), V);
                float DRLNV = dot(reflect(-L, N), V);

                // Specular
                float specular = pow(max(0.0, DRLN2V * DRLNV), _SpecularPower);
                float4 specularColor = _SpecularColor * specular * _SpecularIntensity;

                // Diffuse
                half4 diffuse = float4(1,1,1,1);
                diffuse.rgb = lerp(_ColorBaseDark, _ColorBaseBright, LN2) * mainLight.color * _LightIntensity;
                
                // Foam
                half shoreline = 0;
                CalculateShorline(IN.worldPos, worldPos, _FoamHeight, shoreline);
                
                float4 foam = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, IN.foamUV);
                foam = step(foam, _FoamStep);
                foam = saturate(foam * shoreline + shoreline);
                

                // Shadow
                float4 shadowCoord = TransformWorldToShadowCoord(IN.worldPos.xyz);
                float attenuation = SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_MainLightShadowmapTexture, shadowCoord.xyz);
                attenuation = max(_ShadowIntenstiy, attenuation);

                // Reflection
                half3 reflDir = reflect(-V, N2);
                half3 refColor = SAMPLE_TEXTURECUBE(_ReflectionCubemap, sampler_ReflectionCubemap, reflDir).rgb;
                refColor *= _ReflectionIntensity;

                // Indirect Specular
                half ndotv = abs(dot(N, V));
                half3 inRefColor = refColor * lerp(0, 1, pow(1 - ndotv, _ReflectionPower));


                float4 baseColor = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, screenSpaceUV);
                baseColor *= 1 - depthIntensity;

                float4 finalColor = baseColor + ((diffuse * depthColor) * depthColor.w);
                finalColor.rgb += (specularColor * attenuation).rgb;
                finalColor.rgb += effectColor.rgb;
                finalColor.rgb += inRefColor.rgb;
                finalColor += foam;

                finalColor.rgb = MixFog(finalColor.rgb, IN.fogFactor);

                return finalColor;
            }
            ENDHLSL
        }
    }
}
