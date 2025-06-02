using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class MeshletRenderFeature : ScriptableRendererFeature {
    class MeshletPass : ScriptableRenderPass {
        class PassData {
            public Material material;
            public ComputeBuffer visibleMeshlets;
            public ComputeBuffer argsBuffer;
            public GraphicsBuffer vertexBuffer;
            public GraphicsBuffer indicesBuffer;
        }

        ComputeShader cullShader;
        Material drawMaterial;
        ComputeBuffer meshletSpheres;
        ComputeBuffer visibleMeshlets;
        ComputeBuffer argsBuffer;

        public MeshletPass(ComputeShader compute, Material material) {
            this.cullShader = compute;
            this.drawMaterial = material;
        }

        public void SetupBuffers(int meshletCount) {
            meshletSpheres = new ComputeBuffer(meshletCount, sizeof(float) * 4);
            visibleMeshlets = new ComputeBuffer(meshletCount, sizeof(uint), ComputeBufferType.Append);
            argsBuffer = new ComputeBuffer(1, 4 * sizeof(uint), ComputeBufferType.IndirectArguments);

            visibleMeshlets.SetCounterValue(0);
            argsBuffer.SetData(new uint[] { 64, 0, 0, 0 }); // 64 indices per meshlet
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
            MeshletCulling[] meshletCullingList = GameObject.FindObjectsByType<MeshletCulling>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            using (var builder = renderGraph.AddComputePass<PassData>("Cull Meshlets", out var passData)) {
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData data, ComputeGraphContext context) => {
                    if(meshletCullingList != null) {
                        meshletCullingList[0].Execute();
                    }
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Draw Meshlets", out var passData)) {
                //  RenderingDataでなく、ContextContainerから自分で必要なデータを撮るようになった
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture);

                builder.AllowPassCulling(false);

                //passData.material = drawMaterial;
                //passData.visibleMeshlets = meshletCullingList[0].visibilityBuffer;
                //passData.vertexBuffer = meshletCullingList[0].GetVertexBuffer();
                //passData.indicesBuffer = meshletCullingList[0].GetIndexBuffer();
                //passData.argsBuffer = meshletCullingList[0].drawArgsBuffer;

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) => {
                    if (meshletCullingList != null) {
                        meshletCullingList[0].Render(ctx.cmd);
                    }
                });
            }
        }
    }

    [SerializeField] ComputeShader computeShader;
    [SerializeField] Material drawMaterial;
    MeshletPass pass;

    public override void Create() {
        pass = new MeshletPass(computeShader, drawMaterial);
        pass.SetupBuffers(512); // Example: 512 meshlets
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        renderer.EnqueuePass(pass);
    }
}