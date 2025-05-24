Shader "Unlit/WaterEffect"
{
    Properties
    {
        _MainTex ("Color Texture", 2D) = "white" 
        _NormalTex ("Normal Texture", 2D) = "white" {}
        _Intensity("Intensity", Float) = 1
    }
        SubShader
    {
        Tags
        {

            "LightMode" = "WaterEffect"
        }
        LOD 100
        ztest off
        zwrite off
        Pass
        {
            Blend One OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "TexturePacking.hlsl"
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
            };


            sampler2D _MainTex;
            float4 _MainTex_ST;

            sampler2D _NormalTex;
            float _NormalTex_ST;

            half _Intensity;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                fixed4 nor = tex2D(_NormalTex, i.uv);
                col *= i.color;
                nor *= _Intensity;

                col.rgb *= col.a;
                col.a = 0;

                nor.a = 0;

                float4 result = 0;
                result = col * 5;

                return result;
            }
            ENDCG
        }
    }
}
