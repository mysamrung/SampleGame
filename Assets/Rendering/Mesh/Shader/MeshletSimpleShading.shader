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

            // Structured buffers passed from C# script
            StructuredBuffer<float3> _VertexBuffer;  // All vertices
            StructuredBuffer<uint> _IndexBuffer;     // All indices for meshlets

            float4 _Color;

            struct appdata
            {
                uint vertexID : SV_VertexID; // Vertex index in the draw call
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
            };

            // Input: vertexID corresponds to index in _IndexBuffer, which points to vertex in _VertexBuffer
            v2f vert(appdata v)
            {
                v2f o;

                // Fetch index for this vertex
                uint vertexIndex = _IndexBuffer[v.vertexID];

                // Fetch vertex position
                float3 pos = _VertexBuffer[vertexIndex];

                // Simple MVP (assuming unity_ObjectToClipMatrix)
                o.pos = UnityObjectToClipPos(float4(pos, 1));

                o.color = _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return i.color;
            }

            ENDHLSL
        }
    }
    FallBack "Diffuse"
}