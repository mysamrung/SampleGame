Shader "Custom/UnlitIndirectShader"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #include "unitycg.cginc"
 
            // Structured buffers passed from C# script
            ByteAddressBuffer  _VertexBuffer;  // All vertices
            ByteAddressBuffer  _IndexBuffer;     // All indices for meshlets

            struct appdata { uint vertexID : SV_VertexID; };
            struct v2f 
            { 
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
            };

            const int vertexLayoutElementSize = 3 + 3 + 4; 

            uint ReadUShort(ByteAddressBuffer buffer, uint offsetBytes) {
                uint raw = buffer.Load(offsetBytes & ~3); // align to 4-byte
                if ((offsetBytes & 2) != 0)
                    return (raw >> 16) & 0xFFFF;
                else
                    return raw & 0xFFFF;
            }

            v2f vert(appdata v)
            {
                v2f o;
                // Get the actual index in the index buffer
                uint index = ReadUShort(_IndexBuffer, v.vertexID * 2);
                float3 vertex = asfloat(_VertexBuffer.Load3(index * (vertexLayoutElementSize + 0)* 4));
                float3 normal = asfloat(_VertexBuffer.Load3(index * (vertexLayoutElementSize + 3)* 4));

                o.pos = UnityObjectToClipPos(float4(vertex, 1));
                o.normal = normal; 
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                return half4(i.normal, 1);
            }
            ENDHLSL
        }
    }
}