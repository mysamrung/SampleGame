using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[ExecuteAlways]
public class MeshletDebugger : MonoBehaviour
{
    private List<MeshletGenerator.Meshlet> meshlets;

    private Mesh mesh;

    [SerializeField]
    private int preview_meshletIndex;

    private void OnEnable()
    {
        mesh = GetComponent<MeshFilter>().mesh;
        meshlets = MeshletGenerator.GenerateMeshlets(mesh);
    }

    private void OnDrawGizmosSelected()
    {
        if(meshlets != null && preview_meshletIndex < meshlets.Count)
        {
            for(int i = 0; i < meshlets[preview_meshletIndex].Triangles.Count; i += 3)
            {
                int v0 = meshlets[preview_meshletIndex].Triangles[i + 0];
                int v1 = meshlets[preview_meshletIndex].Triangles[i + 1];
                int v2 = meshlets[preview_meshletIndex].Triangles[i + 2];

                Vector3 p0 = transform.TransformPoint(mesh.vertices[v0]);
                Vector3 p1 = transform.TransformPoint(mesh.vertices[v1]);
                Vector3 p2 = transform.TransformPoint(mesh.vertices[v2]);
                Gizmos.color = Color.red;
                Gizmos.DrawLine(p0, p1);
                Gizmos.DrawLine(p1, p2);
                Gizmos.DrawLine(p2, p0);
            }
        }
    }
}
