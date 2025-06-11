struct MeshletData
{
    int vertexOffset;
    int vertexCount;
    int triangleOffset;
    int triangleCount;
};


struct MeshletVisible
{
    uint meshletPackId; // MeshletId (16 bit) + ObjectId (16 bits)
};

struct TransformData
{
    float4x4 LocalToWorld;
    float4x4 MVP;
};

cbuffer CameraData : register(b0)
{
    float4x4 VP;
};

int PackVisibleMeshlet(uint meshletId, uint objectId)
{
    return meshletId | (objectId << 16);
}

uint2 UnpackVisibleMeshlet(uint meshletPack)
{
    uint meshletId = meshletPack & 0xFFFF;
    uint objectId  = (meshletPack >> 16) & 0xFFFF;

    return uint2(meshletId, objectId);
}