using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;

[StructLayout(LayoutKind.Sequential)]
public struct Meshlet {
    public int vertexOffset;
    public int vertexCount;
    public int triangleOffset;
    public int triangleCount;
}
public struct CullData {
    public Vector4 boundingSphere; // xyz = center, w = radius
    public byte normalX, normalY, normalZ, angleEncoded; // XMUBYTEN4
    public float apexOffset;
}
public struct CameraModelBufferData {
    public Vector3 CameraPosition;
    public float Padding; // alignment
}

public struct MeshletVisible {
    public uint meshletId;
}
public class MeshletGenerator
{

    public const int MAX_VERTICES = 64;
    public const int MAX_TRIANGLES = 126;

    public static void GenerateMeshlets(
        Mesh mesh,
        out List<Meshlet> meshlets,
        out List<int> meshletVertexBuffer,
        out List<int> meshletTriangleBuffer) {

        int[] triangles = mesh.triangles;
        Vector3[] vertices = mesh.vertices;

        Dictionary<int, List<int>> triAdjacency = BuildTriangleAdjacency(triangles);
        bool[] visited = new bool[triangles.Length / 3];

        meshlets = new List<Meshlet>();
        meshletVertexBuffer = new List<int>();
        meshletTriangleBuffer = new List<int>();

        for (int triIndex = 0; triIndex < visited.Length; triIndex++) {
            if (visited[triIndex])
                continue;

            Dictionary<int, int> localVertMap = new();  // global index Å® local index
            List<int> vertList = new();                  // global vertex indices for this meshlet
            List<int> triIndices = new();               // local triangle indices (triplets)

            Queue<int> queue = new Queue<int>();
            queue.Enqueue(triIndex);

            while (queue.Count > 0) {
                int currentTri = queue.Dequeue();
                if (visited[currentTri])
                    continue;

                int i0 = triangles[currentTri * 3 + 0];
                int i1 = triangles[currentTri * 3 + 1];
                int i2 = triangles[currentTri * 3 + 2];

                int[] triVerts = { i0, i1, i2 };
                int newVerts = triVerts.Count(v => !localVertMap.ContainsKey(v));

                if (triIndices.Count / 3 + 1 > MAX_TRIANGLES || localVertMap.Count + newVerts > MAX_VERTICES)
                    continue;

                // Add vertices and build local remap
                foreach (int v in triVerts) {
                    if (!localVertMap.ContainsKey(v)) {
                        byte localIndex = (byte)localVertMap.Count;
                        localVertMap[v] = localIndex;
                        vertList.Add(v); // global index list
                    }
                }

                // Add triangle using local indices
                triIndices.Add(localVertMap[i0]);
                triIndices.Add(localVertMap[i1]);
                triIndices.Add(localVertMap[i2]);

                visited[currentTri] = true;

                if (triAdjacency.TryGetValue(currentTri, out var neighbors)) {
                    foreach (int nbr in neighbors)
                        if (!visited[nbr])
                            queue.Enqueue(nbr);
                }
            }

            if (triIndices.Count == 0)
                continue;

            Meshlet m = new Meshlet {
                vertexOffset = meshletVertexBuffer.Count,
                vertexCount = vertList.Count,
                triangleOffset = meshletTriangleBuffer.Count,
                triangleCount = triIndices.Count / 3
            };

            meshlets.Add(m);
            meshletVertexBuffer.AddRange(vertList);
            meshletTriangleBuffer.AddRange(triIndices);
        }
    }

    // Build adjacency map from triangle index to neighboring triangle indices
    private static Dictionary<int, List<int>> BuildTriangleAdjacency(int[] triangles) {
        Dictionary<(int, int), List<int>> edgeToTriangles = new Dictionary<(int, int), List<int>>();

        // Helper to create ordered edge key (min, max)
        (int, int) MakeEdge(int a, int b) => a < b ? (a, b) : (b, a);

        int triCount = triangles.Length / 3;
        for (int i = 0; i < triCount; i++) {
            int i0 = triangles[i * 3];
            int i1 = triangles[i * 3 + 1];
            int i2 = triangles[i * 3 + 2];

            var edges = new (int, int)[]
            {
                MakeEdge(i0, i1),
                MakeEdge(i1, i2),
                MakeEdge(i2, i0)
            };

            foreach (var edge in edges) {
                if (!edgeToTriangles.TryGetValue(edge, out var triList)) {
                    triList = new List<int>();
                    edgeToTriangles[edge] = triList;
                }
                triList.Add(i);
            }
        }

        // Build adjacency list for each triangle
        Dictionary<int, List<int>> adjacency = new Dictionary<int, List<int>>();
        for (int i = 0; i < triCount; i++)
            adjacency[i] = new List<int>();

        foreach (var kvp in edgeToTriangles) {
            var connectedTris = kvp.Value;
            if (connectedTris.Count < 2)
                continue; // edge belongs to only one triangle

            // For each pair of triangles sharing this edge, add adjacency
            for (int j = 0; j < connectedTris.Count; j++) {
                for (int k = j + 1; k < connectedTris.Count; k++) {
                    int t0 = connectedTris[j];
                    int t1 = connectedTris[k];
                    adjacency[t0].Add(t1);
                    adjacency[t1].Add(t0);
                }
            }
        }

        return adjacency;
    }
}

public class MeshletCullDataGenerator {

    public static byte PackFloatToByte(float f) => (byte)Mathf.Clamp(Mathf.RoundToInt(f * 127.0f + 128.0f), 0, 255);
    public static float UnpackByteToFloat(byte b) => (b - 128) / 127.0f;

    public static CullData ComputeCompactCullData(Mesh mesh, Meshlet meshlet, List<int> vertexBuffer, List<int> triangleBuffer) {
        Vector3[] vertices = mesh.vertices;

        // Compute triangle normals
        List<Vector3> normals = new List<Vector3>();
        Vector3 center = Vector3.zero;

        for (int i = 0; i < meshlet.triangleCount; i++) {
            int triBase = meshlet.triangleOffset + i * 3;
            int i0 = vertexBuffer[meshlet.vertexOffset + triangleBuffer[triBase + 0]];
            int i1 = vertexBuffer[meshlet.vertexOffset + triangleBuffer[triBase + 1]];
            int i2 = vertexBuffer[meshlet.vertexOffset + triangleBuffer[triBase + 2]];

            Vector3 v0 = vertices[i0];
            Vector3 v1 = vertices[i1];
            Vector3 v2 = vertices[i2];

            center += v0 + v1 + v2;

            Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
            normals.Add(normal);
        }

        center /= (meshlet.triangleCount * 3);

        // Average cone axis
        Vector3 axis = Vector3.zero;
        foreach (var n in normals)
            axis += n;
        axis.Normalize();

        // Compute minimum dot product (cosine of angle to axis)
        float minDot = 1f;
        foreach (var n in normals)
            minDot = Mathf.Min(minDot, Vector3.Dot(axis, n));

        float coneAngle = Mathf.Acos(minDot); // radians
        float anglePlus90 = coneAngle + Mathf.Deg2Rad * 90f;
        float minusCos = -Mathf.Cos(anglePlus90 + (5 * Mathf.Deg2Rad));

        // Bounding sphere
        Vector3 sphereCenter = Vector3.zero;
        float maxRadius = 0f;
        for (int i = 0; i < meshlet.vertexCount; i++) {
            int vi = vertexBuffer[meshlet.vertexOffset + i];
            Vector3 p = vertices[vi];
            sphereCenter += p;
        }
        sphereCenter /= meshlet.vertexCount;

        for (int i = 0; i < meshlet.vertexCount; i++) {
            int vi = vertexBuffer[meshlet.vertexOffset + i];
            maxRadius = Mathf.Max(maxRadius, Vector3.Distance(sphereCenter, vertices[vi]));
        }

        // Compute apex offset = distance from sphere center to apex along cone axis
        float apexOffset = Vector3.Dot(sphereCenter - center, axis);

        // Pack cone axis as bytes (from -1..1 to 0..255)
        byte bx = PackFloatToByte(axis.x);
        byte by = PackFloatToByte(axis.y);
        byte bz = PackFloatToByte(axis.z);
        byte ba = PackFloatToByte(minusCos); // angle encoded

        return new CullData {
            boundingSphere = new Vector4(sphereCenter.x, sphereCenter.y, sphereCenter.z, maxRadius),
            normalX = bx,
            normalY = by,
            normalZ = bz,
            angleEncoded = ba,
            apexOffset = apexOffset
        };
    }
}