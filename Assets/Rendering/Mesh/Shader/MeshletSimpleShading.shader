Shader "Custom/MeshletShader"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            
            #include "unitycg.cginc"

            struct MeshletData
            {
                int vertexOffset;
                int vertexCount;
                int triangleOffset;
                int triangleCount;
            };
            StructuredBuffer<MeshletData> _MeshletBuffer : register(t0);
            StructuredBuffer<uint> _VisibleMeshlets : register(t1);
 
            // Structured buffers passed from C# script
            ByteAddressBuffer  _VertexBuffer;  // All vertices
            ByteAddressBuffer  _IndexBuffer;     // All indices for meshlets

            float4 _Color;

            struct appdata
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
            };

            float3 ReadPosition(ByteAddressBuffer buffer, uint offsetBytes)
            {
                float3 position = asfloat(buffer.Load3(offsetBytes));
                return position;
            }

            uint ReadUShort(ByteAddressBuffer buffer, uint offsetBytes) {
                uint raw = buffer.Load(offsetBytes & ~3); // align to 4-byte
                if ((offsetBytes & 2) != 0)
                    return (raw >> 16) & 0xFFFF;
                else
                    return raw & 0xFFFF;
            }

            // Input: vertexID corresponds to index in _IndexBuffer, which points to vertex in _VertexBuffer
            v2f vert(appdata v)
            {
                v2f o;

                // Get meshlet instance
                MeshletData meshlet = _MeshletBuffer[_VisibleMeshlets[v.instanceID]];

                // Get the actual index in the index buffer
                uint localOffset = v.vertexID;
                uint maxIndexCount = meshlet.triangleCount * 3;
                if(localOffset >= maxIndexCount)
                    return o;

                uint offsetBase = (meshlet.triangleOffset + localOffset);

                uint index = ReadUShort(_IndexBuffer, offsetBase * 2);
                float3 vertex = ReadPosition(_VertexBuffer, index * 3 * 4);

                float4 worldPos = float4(vertex, 1.0);
                o.pos = UnityObjectToClipPos(worldPos);
                o.color = _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return 1;
            }

            ENDHLSL
        }
    }
    FallBack "Diffuse"
}