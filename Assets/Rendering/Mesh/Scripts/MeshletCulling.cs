using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class MeshletCulling : MonoBehaviour {
    private List<Meshlet> meshlets;
    private List<CullData> cullData;

    public ComputeBuffer meshletCullingDataBuffer;
    public ComputeBuffer cameraModelBufferDataBuffer;
    public ComputeBuffer visibilityBuffer;
    public ComputeBuffer drawArgsBuffer;

    private MeshRenderer meshRenderer;
    private Mesh mesh;

    [SerializeField]
    private ComputeShader cullCompute;

#if UNITY_EDITOR
    [SerializeField]
    private MeshletDebugger MeshletDebugger;
#endif

    private void OnEnable() {
        meshRenderer = GetComponent<MeshRenderer>();
        mesh = GetComponent<MeshFilter>().sharedMesh;
        MeshletGenerator.GenerateMeshlets(mesh, out meshlets, out List<int> vertexBuffer, out List<int> triangleBuffer);

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
        var layout = new[]
       {
            new VertexAttributeDescriptor(VertexAttribute.Position, mesh.GetVertexAttributeFormat(VertexAttribute.Position), 3),
        };

        mesh.SetVertexBufferParams(mesh.vertexCount, layout);
        mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
        mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;

        newMesh.vertices = vertices;
        newMesh.triangles = indices;
        newMesh.RecalculateNormals();
        newMesh.RecalculateBounds();

        mesh = newMesh;
    }

    [ContextMenu("Excute")]
    public void Execute() {
        meshletCullingDataBuffer = new ComputeBuffer(cullData.Count, Marshal.SizeOf(typeof(CullData)));
        meshletCullingDataBuffer.SetData(cullData);

        visibilityBuffer = new ComputeBuffer(cullData.Count, sizeof(uint), ComputeBufferType.Append);
        visibilityBuffer.SetCounterValue(0);

        CameraModelBufferData cameraModelBufferData = new CameraModelBufferData();
        Matrix4x4 model = transform.localToWorldMatrix;
        Matrix4x4 view = Camera.main.worldToCameraMatrix;
        Matrix4x4 projection = Camera.main.projectionMatrix;

        // Final MVP matrix
        Matrix4x4 VP = projection * view;

        cameraModelBufferData.VP = VP;
        cameraModelBufferData.LocalToWorld = model;
        cameraModelBufferData.CameraPosition = Camera.main.transform.position;

        cameraModelBufferDataBuffer = new ComputeBuffer(1, Marshal.SizeOf(typeof(CameraModelBufferData)));
        cameraModelBufferDataBuffer.SetData(new[] { cameraModelBufferData });

        /// DrawArgsBuffer
        drawArgsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        uint[] args = new uint[]
        {
            (uint)MeshletGenerator.MAX_TRIANGLES,       // index count per instance
            0,                                          // instance count (written by compute shader)
            0,                                          // start index location
            0,                                          // base vertex location
            0                                           // start instance location
        };
        drawArgsBuffer.SetData(args);

        // Set compute shader buffers
        int kernel = cullCompute.FindKernel("CSMain");
        cullCompute.SetBuffer(kernel, "MeshletCullingData", meshletCullingDataBuffer);
        cullCompute.SetBuffer(kernel, "VisibleMeshlets", visibilityBuffer);
        cullCompute.SetConstantBuffer("CameraModelBuffer", cameraModelBufferDataBuffer, 0, cameraModelBufferDataBuffer.stride);

        cullCompute.Dispatch(kernel, Mathf.CeilToInt(cullData.Count / 64.0f), 1, 1);
        ComputeBuffer.CopyCount(visibilityBuffer, drawArgsBuffer, sizeof(uint)); // offset 4 bytes (index 1)

        if (MeshletDebugger != null) {
            int[] result = new int[5];
            drawArgsBuffer.GetData(result);

            result = new int[result[1]];
            visibilityBuffer.GetData(result);

            MeshletDebugger.preview_meshletIndex.Clear();
            foreach (int i in result) {
                MeshletDebugger.preview_meshletIndex.Add(i);
            }

            visibilityBuffer.GetData(result);
        }
    }

    public void Render(RasterCommandBuffer cmd) {
        if (meshRenderer != null && drawArgsBuffer != null) {
            var vertexBuffer = mesh.GetVertexBuffer(0);
            var indexBuffer = mesh.GetIndexBuffer();

            foreach (var attr in mesh.GetVertexAttributes()) {
                Debug.Log($"Attribute: {attr.attribute}, Format: {attr.format}, Dimension: {attr.dimension}");
            }

            // 3. Bind buffers to material
            meshRenderer.material.SetBuffer("_VertexBuffer", vertexBuffer);
            meshRenderer.material.SetBuffer("_IndexBuffer", indexBuffer);
            meshRenderer.material.SetBuffer("_MeshletBuffer", meshletCullingDataBuffer);

            uint[] argsData = new uint[5];
            drawArgsBuffer.GetData(argsData);
            Debug.Log($"Indirect Args: indexCount = {argsData[0]}, instanceCount = {argsData[1]}");
            
            cmd.DrawProceduralIndirect(
                transform.localToWorldMatrix,
                meshRenderer.material,
                0,
                MeshTopology.Triangles,
                drawArgsBuffer
            );

            //cmd.DrawMesh(mesh, transform.localToWorldMatrix, meshRenderer.material);
        }
    }

    public GraphicsBuffer GetVertexBuffer() { return mesh.GetVertexBuffer(0); }
    public GraphicsBuffer GetIndexBuffer() { return mesh.GetIndexBuffer(); }

    private void OnDestroy() {
        meshletCullingDataBuffer?.Dispose();
        cameraModelBufferDataBuffer?.Dispose();
        visibilityBuffer?.Dispose();
        drawArgsBuffer?.Dispose();
    }
}
