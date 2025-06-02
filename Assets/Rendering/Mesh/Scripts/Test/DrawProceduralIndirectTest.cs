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
        
        var layout = new[]
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, mesh.GetVertexAttributeFormat(VertexAttribute.Position), 3),
        };
        mesh.SetVertexBufferParams(mesh.vertexCount, layout);
        mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
        mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
    }

    public void Execute() {
        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        uint[] args = new uint[]
        {
            (uint)mesh.triangles.Length,       // index count per instance
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