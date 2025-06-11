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
            #include "MeshletHeader.hlsl"

            StructuredBuffer<MeshletData> _MeshletBuffer : register(t0);
            StructuredBuffer<MeshletVisible> _VisibleMeshlets : register(t1);
            StructuredBuffer<TransformData> _Transform : register(t2);
 
            // Structured buffers passed from C# script
            ByteAddressBuffer  _VertexBuffer;  // All vertices
            ByteAddressBuffer  _IndexBuffer;     // All indices for meshlets
            
            #define VERTEX_STRIDE 10

            struct appdata
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float2 color : TEXCOORD1;
                float3 worldPos : TEXCOORD8; // For lighting calculations
            };

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
                MeshletVisible visibleMeshlet = _VisibleMeshlets[v.instanceID];
                uint2 meshletUnpack = UnpackVisibleMeshlet(visibleMeshlet.meshletPackId);

                MeshletData meshlet = _MeshletBuffer[meshletUnpack.x];

                // Get the actual index in the index buffer
                uint localOffset = v.vertexID;
                uint maxIndexCount = meshlet.triangleCount * 3;
                if(localOffset >= maxIndexCount)
                    return o;

                uint offsetBase = (meshlet.triangleOffset + localOffset);

                uint index = ReadUShort(_IndexBuffer, offsetBase * 2);
                
                float3 vertex = asfloat(_VertexBuffer.Load3(((index * VERTEX_STRIDE) + 0) * 4));
                float3 normal = asfloat(_VertexBuffer.Load3(((index * VERTEX_STRIDE) + 3) * 4));

                float4 worldPos = float4(vertex, 1.0);
                o.pos =  mul(_Transform[meshletUnpack.y].MVP, worldPos);
                o.normal = mul((float3x3)_Transform[meshletUnpack.y].LocalToWorld, normal);
                o.worldPos = mul(_Transform[meshletUnpack.y].LocalToWorld, worldPos).xyz;
                return o;
            }
            
            void frag(
                v2f i, 
                out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
                ,out uint outRenderingLayers : SV_Target1
#endif
            )
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
                outColor = half4(finalColor, 1);
                
#ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = 0;
#endif
            }

            ENDHLSL
        }
    }
    FallBack "Diffuse"
}