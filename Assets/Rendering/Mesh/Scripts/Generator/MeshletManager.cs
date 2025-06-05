using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

public class MeshletCacheData {
    public List<Meshlet> meshlets;
    public List<CullData> cullData;
    public Mesh convertedMesh;

    public GraphicsBuffer meshletCullingDataBuffer;
    public GraphicsBuffer meshletBuffer;

    public GraphicsBuffer vertexBuffer;
    public GraphicsBuffer indexBuffer;

    public uint refCounter;
}

public class MeshletManager
{
    private static Dictionary<Mesh, MeshletCacheData> meshletCacheDataDic = new Dictionary<Mesh, MeshletCacheData>();
    private static Dictionary<Mesh, List<MeshletObject>> referenceDic = new Dictionary<Mesh, List<MeshletObject>>();

    public static IList<Mesh> GetOriginalMeshList() {
        return meshletCacheDataDic.Keys.ToList();
    }

    public static IList<MeshletObject> GetMeshletObjectListFromOrignalMesh(Mesh mesh) {
        return referenceDic[mesh];
    }

    public static MeshletCacheData GetMeshletCacheDataFromOriginalMesh(Mesh mesh) {
        return meshletCacheDataDic[mesh];
    }
    public static MeshletCacheData CreateMeshletCacheData(Mesh mesh, MeshletObject meshletCulling) {
        if (!meshletCacheDataDic.ContainsKey(mesh)) {

            MeshletCacheData meshletCacheData = new MeshletCacheData();

            MeshletGenerator.GenerateMeshlets(mesh, out meshletCacheData.meshlets, out List<int> vertexBuffer, out List<int> triangleBuffer);

            meshletCacheData.cullData = new List<CullData>();
            for (int meshletIndex = 0; meshletIndex < meshletCacheData.meshlets.Count; meshletIndex++) {
                meshletCacheData.cullData.Add(MeshletCullDataGenerator.ComputeCompactCullData(mesh, meshletCacheData.meshlets[meshletIndex], vertexBuffer, triangleBuffer));
            }

            Vector3[] vertices = new Vector3[vertexBuffer.Count];
            int[] indices = new int[triangleBuffer.Count];

            for (int meshletIndex = 0; meshletIndex < meshletCacheData.meshlets.Count; meshletIndex++) {
                var meshlet = meshletCacheData.meshlets[meshletIndex];
                for (int i = 0; i < meshlet.triangleCount * 3; i += 3) {
                    int triBase = meshlet.triangleOffset + i;

                    // Indices into meshlet-local vertex list
                    int localI0 = triangleBuffer[triBase + 0];
                    int localI1 = triangleBuffer[triBase + 1];
                    int localI2 = triangleBuffer[triBase + 2];

                    // Convert to global vertex indices
                    int v0 = vertexBuffer[meshlet.vertexOffset + localI0];
                    int v1 = vertexBuffer[meshlet.vertexOffset + localI1];
                    int v2 = vertexBuffer[meshlet.vertexOffset + localI2];

                    Vector3 p0 = mesh.vertices[v0];
                    Vector3 p1 = mesh.vertices[v1];
                    Vector3 p2 = mesh.vertices[v2];

                    vertices[v0] = p0;
                    vertices[v1] = p1;
                    vertices[v2] = p2;

                    indices[triBase + 0] = v0;
                    indices[triBase + 1] = v1;
                    indices[triBase + 2] = v2;
                }
            }

            meshletCacheData.convertedMesh = new Mesh();
            meshletCacheData.convertedMesh.vertices = vertices;
            meshletCacheData.convertedMesh.triangles = indices;
            meshletCacheData.convertedMesh.RecalculateNormals();
            meshletCacheData.convertedMesh.RecalculateBounds();

            var layout = new[] {
                new VertexAttributeDescriptor(VertexAttribute.Position, meshletCacheData.convertedMesh.GetVertexAttributeFormat(VertexAttribute.Position), 3),
            };

            meshletCacheData.convertedMesh.SetVertexBufferParams(meshletCacheData.convertedMesh.vertexCount, layout);
            meshletCacheData.convertedMesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            meshletCacheData.convertedMesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
            meshletCacheData.convertedMesh.indexFormat = IndexFormat.UInt16;

            CreateMeshletBuffer(meshletCacheData);

            meshletCacheData.vertexBuffer = meshletCacheData.convertedMesh.GetVertexBuffer(0);
            meshletCacheData.indexBuffer = meshletCacheData.convertedMesh.GetIndexBuffer();

            meshletCacheData.refCounter = 0;
            meshletCacheDataDic.Add(mesh, meshletCacheData);

            if (!referenceDic.ContainsKey(mesh))
                referenceDic.Add(mesh, new List<MeshletObject>());
        }


        meshletCacheDataDic[mesh].refCounter++;
        referenceDic[mesh].Add(meshletCulling);

        return meshletCacheDataDic[mesh];
    }

    private static void CreateMeshletBuffer(MeshletCacheData meshletCacheData) {
        meshletCacheData.meshletCullingDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, meshletCacheData.cullData.Count, Marshal.SizeOf(typeof(CullData)));
        meshletCacheData.meshletCullingDataBuffer.SetData(meshletCacheData.cullData);

        meshletCacheData.meshletBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, meshletCacheData.meshlets.Count, Marshal.SizeOf(typeof(Meshlet)));
        meshletCacheData.meshletBuffer.SetData(meshletCacheData.meshlets);
    }
    public static void ReleaseAll()
    {
        foreach (var pair in meshletCacheDataDic)
        {
            pair.Value.meshletBuffer?.Dispose();
            pair.Value.meshletCullingDataBuffer?.Dispose();
            GameObject.DestroyImmediate(pair.Value.convertedMesh);
        }

        meshletCacheDataDic.Clear();
        referenceDic.Clear();
    }

    public static void ReleaseMeshlet(MeshletCacheData meshletCacheData, MeshletObject meshletCulling) {
        var pair = meshletCacheDataDic.SingleOrDefault(s => s.Value == meshletCacheData);

        if (pair.Value != null) {
            referenceDic[pair.Key].Remove(meshletCulling);
            pair.Value.refCounter--; 

            if (pair.Value.refCounter <= 0) {
                pair.Value.meshletBuffer?.Dispose();
                pair.Value.meshletCullingDataBuffer?.Dispose();
                pair.Value.vertexBuffer?.Dispose();
                pair.Value.indexBuffer?.Dispose();  

                GameObject.DestroyImmediate(meshletCacheData.convertedMesh);
                meshletCacheDataDic.Remove(pair.Key);
            }
        } else {
            Debug.LogError("Not found meshlet in cache manage");
        }
    }

}
