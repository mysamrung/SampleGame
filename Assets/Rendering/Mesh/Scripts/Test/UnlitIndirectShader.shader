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
            struct v2f { float4 pos : SV_POSITION; };

            float3 ReadPosition(uint baseOffset)
            {
                float3 position = asfloat(_VertexBuffer.Load3(baseOffset));
                return position;
            }

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
                float3 vertex = ReadPosition(index * 3 * 4);

                o.pos = UnityObjectToClipPos(float4(vertex, 1));
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                return half4(1, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}