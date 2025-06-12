using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Jobs;
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

public class MeshletObjectReferenceData {
    public List<MeshletObject> meshletObjects = new List<MeshletObject>();
    public List<Transform> meshletTransforms = new List<Transform>();

    public NativeArray<Matrix4x4> matrixArray;
    public NativeArray<Bounds> boundArray;
}

public class MeshletManager : MonoBehaviour 
{
    private static Dictionary<Mesh, MeshletCacheData> meshletCacheDataDic = new Dictionary<Mesh, MeshletCacheData>();
    private static Dictionary<Mesh, MeshletObjectReferenceData> referenceDic = new Dictionary<Mesh, MeshletObjectReferenceData>();

    private static HashSet<MeshletObjectReferenceData> dirtyReferenceDataHashSet = new HashSet<MeshletObjectReferenceData>();

    private void LateUpdate() {
        if (dirtyReferenceDataHashSet.Count > 0) {
            foreach (MeshletObjectReferenceData dirtyRefData in dirtyReferenceDataHashSet) {
                if (dirtyRefData == null)
                    continue;

                // Matrix Array 
                if (dirtyRefData.matrixArray.IsCreated && dirtyRefData.meshletTransforms.Count != dirtyRefData.matrixArray.Length)
                    dirtyRefData.matrixArray.Dispose();

                if (!dirtyRefData.matrixArray.IsCreated)
                {
                    dirtyRefData.matrixArray = new NativeArray<Matrix4x4>(dirtyRefData.meshletTransforms.Count, Allocator.Persistent);
                    for(int i = 0; i < dirtyRefData.matrixArray.Length; i++)
                    {
                        dirtyRefData.matrixArray[i] = dirtyRefData.meshletTransforms[i].localToWorldMatrix;
                    }
                }

                // Bound Array
                if (dirtyRefData.boundArray.IsCreated && dirtyRefData.meshletTransforms.Count != dirtyRefData.boundArray.Length)
                    dirtyRefData.boundArray.Dispose();

                if (!dirtyRefData.boundArray.IsCreated) {
                    dirtyRefData.boundArray = new NativeArray<Bounds>(dirtyRefData.meshletTransforms.Count, Allocator.Persistent);
                    for (int i = 0; i < dirtyRefData.matrixArray.Length; i++) {
                        dirtyRefData.boundArray[i] = dirtyRefData.meshletObjects[i].meshRenderer.bounds;
                    }
                }
            }
            dirtyReferenceDataHashSet.Clear();
        }
    }

    public static IEnumerable<Mesh> GetOriginalMeshList() {
        return meshletCacheDataDic.Keys;
    }

    public static MeshletObjectReferenceData GetMeshletReferenceDataFromOrignalMesh(Mesh mesh) {
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
            Vector3[] normals = new Vector3[vertexBuffer.Count];
            Vector4[] tangents = new Vector4[vertexBuffer.Count];
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

                    Vector3 n0 = mesh.normals[v0];
                    Vector3 n1 = mesh.normals[v1];
                    Vector3 n2 = mesh.normals[v2];

                    normals[v0] = n0;
                    normals[v1] = n1;
                    normals[v2] = n2;

                    Vector4 t0 = mesh.tangents[v0];
                    Vector4 t1 = mesh.tangents[v1];
                    Vector4 t2 = mesh.tangents[v2];

                    tangents[v0] = t0;
                    tangents[v1] = t1;
                    tangents[v2] = t2;

                    indices[triBase + 0] = v0;
                    indices[triBase + 1] = v1;
                    indices[triBase + 2] = v2;
                }
            }

            meshletCacheData.convertedMesh = new Mesh();
            meshletCacheData.convertedMesh.vertices = vertices;
            meshletCacheData.convertedMesh.triangles = indices;
            meshletCacheData.convertedMesh.normals = normals;
            meshletCacheData.convertedMesh.tangents = tangents;
            meshletCacheData.convertedMesh.RecalculateBounds();

            var layout = new[] {
                new VertexAttributeDescriptor(VertexAttribute.Position, meshletCacheData.convertedMesh.GetVertexAttributeFormat(VertexAttribute.Position), 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, meshletCacheData.convertedMesh.GetVertexAttributeFormat(VertexAttribute.Normal), 3),
                new VertexAttributeDescriptor(VertexAttribute.Color, meshletCacheData.convertedMesh.GetVertexAttributeFormat(VertexAttribute.Color), 4),
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
                referenceDic.Add(mesh, new MeshletObjectReferenceData());
        }


        meshletCacheDataDic[mesh].refCounter++;

        referenceDic[mesh].meshletTransforms.Add(meshletCulling.transform);
        referenceDic[mesh].meshletObjects.Add(meshletCulling);

        dirtyReferenceDataHashSet.Add(referenceDic[mesh]);

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

        foreach(var pair in referenceDic) {
            if(pair.Value.matrixArray.IsCreated)
                pair.Value.matrixArray.Dispose();
        }

        dirtyReferenceDataHashSet.Clear();
        meshletCacheDataDic.Clear();
        referenceDic.Clear();
    }

    public static void ReleaseMeshlet(MeshletCacheData meshletCacheData, MeshletObject meshletCulling) {
        var pair = meshletCacheDataDic.SingleOrDefault(s => s.Value == meshletCacheData);

        if (pair.Value != null) {
            referenceDic[pair.Key].meshletObjects.Remove(meshletCulling);
            referenceDic[pair.Key].meshletTransforms.Remove(meshletCulling.transform);
            pair.Value.refCounter--;

            if (pair.Value.refCounter <= 0) {
                if (referenceDic[pair.Key].matrixArray.IsCreated)
                    referenceDic[pair.Key].matrixArray.Dispose();

                referenceDic.Remove(pair.Key);

                pair.Value.meshletBuffer?.Dispose();
                pair.Value.meshletCullingDataBuffer?.Dispose();
                pair.Value.vertexBuffer?.Dispose();
                pair.Value.indexBuffer?.Dispose();  

                GameObject.DestroyImmediate(meshletCacheData.convertedMesh);
                meshletCacheDataDic.Remove(pair.Key);
            } else {
                dirtyReferenceDataHashSet.Add(referenceDic[pair.Key]);
            }
        } else {
            Debug.LogError("Not found meshlet in cache manage");
        }
    }

}
