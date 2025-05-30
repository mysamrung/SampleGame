using System.Collections.Generic;
using UnityEngine;

public class MeshletGenerator
{
    public struct Meshlet
    {
        public List<int> Triangles;  // Triangle indices (triplets)
        public HashSet<int> Vertices; // Unique vertex indices used by this meshlet
    }

    // Max limits per meshlet
    const int MAX_VERTICES = 64;
    const int MAX_TRIANGLES = 124;

    // Generate meshlets with connected triangles
    public static List<Meshlet> GenerateMeshlets(Mesh mesh)
    {
        int[] triangles = mesh.triangles;
        Vector3[] vertices = mesh.vertices;

        // Build adjacency: triangle to neighboring triangles by shared edges
        Dictionary<int, List<int>> triAdjacency = BuildTriangleAdjacency(triangles);

        bool[] visited = new bool[triangles.Length / 3];
        List<Meshlet> meshlets = new List<Meshlet>();

        for (int triIndex = 0; triIndex < visited.Length; triIndex++)
        {
            if (visited[triIndex])
                continue;

            Meshlet meshlet = new Meshlet()
            {
                Triangles = new List<int>(),
                Vertices = new HashSet<int>()
            };

            Queue<int> queue = new Queue<int>();
            queue.Enqueue(triIndex);

            while (queue.Count > 0)
            {
                int currentTri = queue.Dequeue();
                if (visited[currentTri])
                    continue;

                // Check if adding this triangle exceeds limits
                int[] triVerts = {
                    triangles[currentTri * 3],
                    triangles[currentTri * 3 + 1],
                    triangles[currentTri * 3 + 2]
                };

                // Compute how many new vertices would be added if this triangle is included
                int newVertsCount = 0;
                foreach (var v in triVerts)
                    if (!meshlet.Vertices.Contains(v))
                        newVertsCount++;

                // If adding this triangle exceeds limits, skip it
                if (meshlet.Triangles.Count / 3 + 1 > MAX_TRIANGLES ||
                    meshlet.Vertices.Count + newVertsCount > MAX_VERTICES)
                    continue;

                // Add triangle and its vertices
                meshlet.Triangles.AddRange(triVerts);
                foreach (var v in triVerts)
                    meshlet.Vertices.Add(v);

                visited[currentTri] = true;

                // Enqueue neighbors
                if (triAdjacency.TryGetValue(currentTri, out var neighbors))
                {
                    foreach (int nbr in neighbors)
                        if (!visited[nbr])
                            queue.Enqueue(nbr);
                }
            }

            if (meshlet.Triangles.Count > 0)
                meshlets.Add(meshlet);
        }

        return meshlets;
    }

    // Build adjacency map from triangle index to neighboring triangle indices
    static Dictionary<int, List<int>> BuildTriangleAdjacency(int[] triangles)
    {
        Dictionary<(int, int), List<int>> edgeToTriangles = new Dictionary<(int, int), List<int>>();

        // Helper to create ordered edge key (min, max)
        (int, int) MakeEdge(int a, int b) => a < b ? (a, b) : (b, a);

        int triCount = triangles.Length / 3;
        for (int i = 0; i < triCount; i++)
        {
            int i0 = triangles[i * 3];
            int i1 = triangles[i * 3 + 1];
            int i2 = triangles[i * 3 + 2];

            var edges = new (int, int)[]
            {
                MakeEdge(i0, i1),
                MakeEdge(i1, i2),
                MakeEdge(i2, i0)
            };

            foreach (var edge in edges)
            {
                if (!edgeToTriangles.TryGetValue(edge, out var triList))
                {
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

        foreach (var kvp in edgeToTriangles)
        {
            var connectedTris = kvp.Value;
            if (connectedTris.Count < 2)
                continue; // edge belongs to only one triangle

            // For each pair of triangles sharing this edge, add adjacency
            for (int j = 0; j < connectedTris.Count; j++)
            {
                for (int k = j + 1; k < connectedTris.Count; k++)
                {
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