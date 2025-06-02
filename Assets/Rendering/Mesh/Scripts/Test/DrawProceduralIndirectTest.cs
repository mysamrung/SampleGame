using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class DrawIndirectTest : MonoBehaviour {
    public ComputeShader computeShader;
    public Material drawMaterial;

    private ComputeBuffer argsBuffer;
    private ComputeBuffer positionBuffer;

    private Vector4[] positions = new Vector4[] { new Vector4(0, 0, 0, 1) };

    private Mesh mesh;

    private void OnEnable() {
        mesh = GetComponent<MeshFilter>().mesh;
        MeshletGenerator.GenerateMeshlets(mesh, out List<Meshlet> meshlets, out List<int> vertexBuffer, out List<int> triangleBuffer);

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

        var layout = new[]
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, mesh.GetVertexAttributeFormat(VertexAttribute.Position), 3),
        };

        mesh.vertices = vertices;
        mesh.triangles = indices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        mesh.SetVertexBufferParams(mesh.vertexCount, layout);
        mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
        mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
        mesh.indexFormat = IndexFormat.UInt16;

    }

    public void Execute() {
        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        uint[] args = new uint[]
        {
            (uint)mesh.triangles.Length,                // index count per instance
            1,                                          // instance count (written by compute shader)
            0,                                          // start index location
            0,                                          // base vertex location
            0                                           // start instance location
        };
        argsBuffer.SetData(args);

        //positionBuffer = new ComputeBuffer(1, sizeof(float) * 4);
        //positionBuffer.SetData(positions);

        //int kernel = computeShader.FindKernel("CSMain");

        //computeShader.SetBuffer(kernel, "argsBuffer", argsBuffer);
        //computeShader.SetBuffer(kernel, "positions", positionBuffer);
        //computeShader.Dispatch(kernel, 1, 1, 1);
    }

    public void Render(RasterCommandBuffer cmd) {
        var vertexBuffer = mesh.GetVertexBuffer(0);
        var indexBuffer = mesh.GetIndexBuffer();

        drawMaterial.SetBuffer("_VertexBuffer", vertexBuffer);
        drawMaterial.SetBuffer("_IndexBuffer", indexBuffer);

        cmd.DrawProceduralIndirect(
            transform.localToWorldMatrix,
            drawMaterial,
            0,
            MeshTopology.Triangles,
            argsBuffer
        );
    }

    void OnDestroy() {
        if (mesh != null) {
            Destroy(mesh);
            mesh = null;
        }

        argsBuffer?.Release();
        positionBuffer?.Release();
    }
}