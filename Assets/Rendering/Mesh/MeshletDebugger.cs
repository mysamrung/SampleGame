using System.Collections.Generic;
using System.Linq;
using Unity.Android.Gradle;
using UnityEngine;
using static UnityEditor.PlayerSettings.SplashScreen;

[ExecuteAlways]
public class MeshletDebugger : MonoBehaviour
{
    private List<Meshlet> meshlets;
    private List<CullData> cullData;
    private List<int> vertexBuffer;
    private List<int> triangleBuffer;

    private Mesh mesh;

    [SerializeField]
    private int preview_meshletIndex;

    private void OnEnable()
    {
        mesh = GetComponent<MeshFilter>().sharedMesh;
        MeshletGenerator.GenerateMeshlets(mesh, out meshlets, out vertexBuffer, out triangleBuffer);

        cullData = new List<CullData>();
        for (int meshletIndex = 0; meshletIndex < meshlets.Count; meshletIndex++) {
            cullData.Add(MeshletCullDataGenerator.ComputeCompactCullData(mesh, meshlets[meshletIndex], vertexBuffer, triangleBuffer));
        }

        Vector3[] vertices = new Vector3[vertexBuffer.Count];
        int[] indices = new int[triangleBuffer.Count];

        for (int meshletIndex = 0; meshletIndex < meshlets.Count; meshletIndex++) {
            var meshlet = meshlets[meshletIndex];
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

        Mesh newMesh = new Mesh();
        newMesh.vertices = vertices;
        newMesh.triangles = indices;
        newMesh.RecalculateNormals();
        newMesh.RecalculateBounds();

        mesh = newMesh;
    }

    private void OnDrawGizmosSelected() {
        if (meshlets != null )
        {
            for (int meshletIndex = 0; meshletIndex < meshlets.Count; meshletIndex++) {
                if (meshletIndex != preview_meshletIndex)
                    continue;

                var meshlet = meshlets[meshletIndex];
                for (int i = 0; i < meshlet.triangleCount * 3; i += 3) {
                    int triBase = meshlet.triangleOffset + i;

                    // Indices into meshlet-local vertex list
                    int v0 = mesh.triangles[triBase + 0];
                    int v1 = mesh.triangles[triBase + 1];
                    int v2 = mesh.triangles[triBase + 2];

                    Vector3 p0 = transform.TransformPoint(mesh.vertices[v0]);
                    Vector3 p1 = transform.TransformPoint(mesh.vertices[v1]);
                    Vector3 p2 = transform.TransformPoint(mesh.vertices[v2]);

                    Vector3 center = cullData[meshletIndex].boundingSphere;
                    float radius = cullData[meshletIndex].boundingSphere.w;
                    Vector3 forwad = new Vector3(
                        MeshletCullDataGenerator.UnpackByteToFloat(cullData[meshletIndex].normalX),
                        MeshletCullDataGenerator.UnpackByteToFloat(cullData[meshletIndex].normalY),
                        MeshletCullDataGenerator.UnpackByteToFloat(cullData[meshletIndex].normalZ)
                    );

                    center = transform.TransformPoint(center);
                    forwad = transform.TransformDirection(forwad);
                    center += forwad * cullData[meshletIndex].apexOffset;
                    DrawCone(16, center, radius, forwad, 0.25f);
                    Gizmos.color = GetColorFromSeed(meshletIndex);
                    Gizmos.DrawLine(p0, p1);
                    Gizmos.DrawLine(p1, p2);
                    Gizmos.DrawLine(p2, p0);
                }
            }
        }
    }

    private void DrawCone(int resolution, Vector3 origin, float radius, Vector3 forward, float coneLength) {
        // Draw circle base of the cone
        for (int i = 0; i < resolution; i++) {
            float angle1 = i * Mathf.PI * 2 / resolution;
            float angle2 = (i + 1) * Mathf.PI * 2 / resolution;

            Vector3 dir1 = new Vector3(Mathf.Cos(angle1), Mathf.Sin(angle1), 0);
            Vector3 dir2 = new Vector3(Mathf.Cos(angle2), Mathf.Sin(angle2), 0);

            Quaternion rotation = Quaternion.LookRotation(forward);
            Vector3 point1 = origin + rotation * dir1 * radius + forward * coneLength;
            Vector3 point2 = origin + rotation * dir2 * radius + forward * coneLength;

            // Draw base circle line
            Gizmos.DrawLine(point1, point2);

            // Draw lines from origin to circle points
            Gizmos.DrawLine(origin, point1);
        }
    }

    private Color GetColorFromSeed(int seed) {
        System.Random random = new System.Random(seed);

        // Generate RGB values between 0.0 and 1.0
        float r = (float)random.NextDouble();
        float g = (float)random.NextDouble();
        float b = (float)random.NextDouble();

        return new Color(r, g, b);
    }
}
