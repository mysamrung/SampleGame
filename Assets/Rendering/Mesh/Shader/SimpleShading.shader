Shader "Custom/SimpleShading"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white"
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            
            #include "unitycg.cginc"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float2 color : TEXCOORD1;
                float3 worldPos : TEXCOORD8; // For lighting calculations
            };


            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.pos = UnityObjectToClipPos(IN.positionOS);
                OUT.normal = mul((float3x3)unity_ObjectToWorld, IN.normal);
                OUT.worldPos = mul(unity_ObjectToWorld, IN.positionOS);
                return OUT;
            }

            half4 frag(Varyings i) : SV_Target
            {  
                float3 normal = normalize(i.normal);

                float diff = saturate(dot(normal, _WorldSpaceLightPos0));
                float3 diffuse = diff;
                
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 reflectDir = reflect(-_WorldSpaceLightPos0, normal);
                
                // Specular
                float spec = pow(saturate(dot(viewDir, reflectDir)), 10);
                float3 specular = spec;

                float3 finalColor = (diff * 0.5) + spec;
                return half4(finalColor, 1);
                
            }
            ENDHLSL
        }
    }
}
