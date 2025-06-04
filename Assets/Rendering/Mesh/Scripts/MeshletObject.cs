using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class MeshletObject : MonoBehaviour {
    private MeshletCacheData MeshletCacheData;

    public ComputeBuffer cameraModelBufferDataBuffer;
    public ComputeBuffer visibilityBuffer;
    public ComputeBuffer drawArgsBuffer;

    public MeshRenderer meshRenderer;
    private Mesh mesh;

#if UNITY_EDITOR
    [SerializeField]
    private MeshletDebugger MeshletDebugger;
#endif

    private void OnEnable() {
        meshRenderer = GetComponent<MeshRenderer>();

        MeshFilter meshFilter = GetComponent<MeshFilter>();
        
        MeshletCacheData = MeshletManager.CreateMeshletCacheData(meshFilter.sharedMesh, this);
        mesh = MeshletCacheData.convertedMesh;
    }

    public void Execute(ComputeShader cullCompute) {

        CameraModelBufferData cameraModelBufferData = new CameraModelBufferData();
        Matrix4x4 model = transform.localToWorldMatrix;
        Matrix4x4 view = Camera.main.worldToCameraMatrix;
        Matrix4x4 projection = Camera.main.projectionMatrix;

        // Final MVP matrix
        Matrix4x4 VP = projection * view;

        cameraModelBufferData.CameraPosition = Camera.main.transform.position;

        cameraModelBufferDataBuffer = new ComputeBuffer(1, Marshal.SizeOf(typeof(CameraModelBufferData)));
        cameraModelBufferDataBuffer.SetData(new[] { cameraModelBufferData });

        /// DrawArgsBuffer
        drawArgsBuffer?.Dispose();
        drawArgsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        uint[] args = new uint[]
        {
            (uint)MeshletGenerator.MAX_TRIANGLES * 3,       // index count per instance
            0,                                              // instance count (written by compute shader)
            0,                                              // start index location
            0,                                              // base vertex location
            0                                               // start instance location
        };
        drawArgsBuffer.SetData(args);

        visibilityBuffer?.Dispose();
        visibilityBuffer = new ComputeBuffer(MeshletCacheData.cullData.Count, Marshal.SizeOf(typeof(MeshletVisible)), ComputeBufferType.Append);
        visibilityBuffer.SetCounterValue(0);

        // Set compute shader buffers
        int kernel = cullCompute.FindKernel("CSMain");
        cullCompute.SetBuffer(kernel, "MeshletCullingData", MeshletCacheData.meshletCullingDataBuffer);
        cullCompute.SetBuffer(kernel, "VisibleMeshlets", visibilityBuffer);
        cullCompute.SetConstantBuffer("CameraModelBuffer", cameraModelBufferDataBuffer, 0, cameraModelBufferDataBuffer.stride);

        cullCompute.Dispatch(kernel, Mathf.CeilToInt(MeshletCacheData.cullData.Count / 64.0f), 1, 1);
        ComputeBuffer.CopyCount(visibilityBuffer, drawArgsBuffer, sizeof(uint)); // offset 4 bytes (index 1)

        if (MeshletDebugger != null) {
            int[] result = new int[5];
            drawArgsBuffer.GetData(result);

            var resultVisible = new MeshletVisible[result[1]];
            visibilityBuffer.GetData(result);

            MeshletDebugger.preview_meshletIndex.Clear();
            foreach (var data in resultVisible) {
                MeshletDebugger.preview_meshletIndex.Add((int)(data.meshletId & 0xFFFF));
            }

            visibilityBuffer.GetData(result);
        }
    }

    public void Render(RasterCommandBuffer cmd) {
        if (meshRenderer != null && drawArgsBuffer != null) {
            var vertexBuffer = mesh.GetVertexBuffer(0);
            var indexBuffer = mesh.GetIndexBuffer();

            meshRenderer.sharedMaterial.SetBuffer("_VertexBuffer", vertexBuffer);
            meshRenderer.sharedMaterial.SetBuffer("_IndexBuffer", indexBuffer);
            meshRenderer.sharedMaterial.SetBuffer("_MeshletBuffer", MeshletCacheData.meshletBuffer);
            meshRenderer.sharedMaterial.SetBuffer("_VisibleMeshlets", visibilityBuffer);

            uint[] argsData = new uint[5];
            drawArgsBuffer.GetData(argsData);
            
            cmd.DrawProceduralIndirect(
                transform.localToWorldMatrix,
                meshRenderer.sharedMaterial,
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
        MeshletManager.ReleaseMeshlet(MeshletCacheData, this);
        MeshletCacheData = null;

        cameraModelBufferDataBuffer?.Dispose();
        visibilityBuffer?.Dispose();
        drawArgsBuffer?.Dispose();
    }
}
