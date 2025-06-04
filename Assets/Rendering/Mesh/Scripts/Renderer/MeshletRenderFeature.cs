using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
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


        class MeshletDrawBufferData {
            public ComputeBuffer visibilityBuffer;
            public ComputeBuffer drawArgsBuffer;
            public ComputeBuffer modelBuffer;

            public MeshletCacheData MeshletCacheData;
            public Material material;
        }

        struct ModelBuffer {
            public Matrix4x4 localToWorld;
            public Matrix4x4 mvp;
        }

        private ComputeBuffer cameraBuffer;

        private List<MeshletDrawBufferData> meshletDrawBufferDataList = new List<MeshletDrawBufferData>();

        public MeshletPass(ComputeShader compute, Material material) {
            this.cullShader = compute;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
            //  RenderingDataでなく、ContextContainerから自分で必要なデータを撮るようになった
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            using (var builder = renderGraph.AddComputePass<PassData>("Cull Meshlets", out var passData)) {
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData data, ComputeGraphContext context) => {

                    CameraModelBufferData cameraModelBufferData = new CameraModelBufferData();
                    Matrix4x4 view = cameraData.camera.worldToCameraMatrix;
                    Matrix4x4 projection = GL.GetGPUProjectionMatrix(cameraData.camera.projectionMatrix, true);
                    Matrix4x4 vp = projection * view;

                    if(cameraData.isSceneViewCamera)
                        cameraModelBufferData.CameraPosition = Camera.main.transform.position;
                    else
                        cameraModelBufferData.CameraPosition = cameraData.camera.transform.position;

                    cameraBuffer = new ComputeBuffer(1, Marshal.SizeOf(typeof(CameraModelBufferData)));
                    cameraBuffer.SetData(new[] { cameraModelBufferData });


                    foreach (var mesh in MeshletManager.GetOriginalMeshList()) {
                        IList<MeshletObject> meshletList = MeshletManager.GetMeshletObjectListFromOrignalMesh(mesh);
                        if(meshletList == null || meshletList.Count <= 0)
                            continue;

                        MeshletCacheData meshletCache = MeshletManager.GetMeshletCacheDataFromOriginalMesh(mesh);
                        var result = ExecuteCullingGroup(meshletCache, meshletList, vp);

                        if(result != null)
                            meshletDrawBufferDataList.Add(result);
                    }
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Draw Meshlets", out var passData)) {
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture);

                builder.AllowPassCulling(false);

                //passData.material = drawMaterial;
                //passData.visibleMeshlets = meshletCullingList[0].visibilityBuffer;
                //passData.vertexBuffer = meshletCullingList[0].GetVertexBuffer();
                //passData.indicesBuffer = meshletCullingList[0].GetIndexBuffer();
                //passData.argsBuffer = meshletCullingList[0].drawArgsBuffer;

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) => {
                    foreach (var meshletDrawBufferData in meshletDrawBufferDataList) {
                        RenderMeshletGroup(ctx.cmd, meshletDrawBufferData);
                    }
                });
            }

            // Release
            foreach(var meshletDrawBufferData in meshletDrawBufferDataList) {
                meshletDrawBufferData.modelBuffer?.Dispose();
                meshletDrawBufferData.visibilityBuffer?.Dispose();
                meshletDrawBufferData.drawArgsBuffer?.Dispose();
            }
            meshletDrawBufferDataList.Clear();
            cameraBuffer?.Dispose();

        }

        private MeshletDrawBufferData ExecuteCullingGroup(MeshletCacheData meshletCacheData, IList<MeshletObject> meshletObjects, Matrix4x4 vp) {
            List<ModelBuffer> modelBufferList = new List<ModelBuffer>();
            foreach(var meshletObject in meshletObjects) {
                if (meshletObject == null || !meshletObject.enabled)
                    continue;

                ModelBuffer modelBuffer = new ModelBuffer();
                modelBuffer.localToWorld = meshletObject.transform.localToWorldMatrix;
                modelBuffer.mvp = vp * modelBuffer.localToWorld;
                modelBufferList.Add(modelBuffer);
            }

            if (modelBufferList.Count <= 0)
                return null;

            MeshletDrawBufferData meshletDrawBufferData = new MeshletDrawBufferData();
            meshletDrawBufferData.modelBuffer = new ComputeBuffer(modelBufferList.Count, Marshal.SizeOf(typeof(ModelBuffer)));
            meshletDrawBufferData.modelBuffer.SetData(modelBufferList);

            meshletDrawBufferData.visibilityBuffer = new ComputeBuffer(meshletCacheData.cullData.Count * modelBufferList.Count, Marshal.SizeOf(typeof(MeshletVisible)), ComputeBufferType.Append);
            meshletDrawBufferData.visibilityBuffer.SetCounterValue(0);

            meshletDrawBufferData.drawArgsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
            uint[] args = new uint[]
            {
                (uint)MeshletGenerator.MAX_TRIANGLES * 3,       // index count per instance
                0,                                              // instance count (written by compute shader)
                0,                                              // start index location
                0,                                              // base vertex location
                0                                               // start instance location
            };
            meshletDrawBufferData.drawArgsBuffer.SetData(args);

            meshletDrawBufferData.MeshletCacheData = meshletCacheData;
            meshletDrawBufferData.material = meshletObjects[0].meshRenderer.sharedMaterial;

            // Set compute shader buffers
            int kernel = cullShader.FindKernel("CSMain");
            cullShader.SetBuffer(kernel, "MeshletCullingData", meshletCacheData.meshletCullingDataBuffer);
            cullShader.SetBuffer(kernel, "VisibleMeshlets", meshletDrawBufferData.visibilityBuffer);
            cullShader.SetBuffer(kernel, "Transform", meshletDrawBufferData.modelBuffer);
            cullShader.SetConstantBuffer("CameraBuffer", cameraBuffer, 0, cameraBuffer.stride);

            cullShader.Dispatch(kernel, Mathf.CeilToInt((meshletCacheData.cullData.Count * modelBufferList.Count) / 64.0f), 1, 1);
            ComputeBuffer.CopyCount(meshletDrawBufferData.visibilityBuffer, meshletDrawBufferData.drawArgsBuffer, sizeof(uint)); // offset 4 bytes (index 1)

            return meshletDrawBufferData;
        }

        private void RenderMeshletGroup(RasterCommandBuffer cmd, MeshletDrawBufferData meshletDrawBufferData) {
            var vertexBuffer = meshletDrawBufferData.MeshletCacheData.convertedMesh.GetVertexBuffer(0);
            var indexBuffer = meshletDrawBufferData.MeshletCacheData.convertedMesh.GetIndexBuffer();

            meshletDrawBufferData.material.SetBuffer("_VertexBuffer", vertexBuffer);
            meshletDrawBufferData.material.SetBuffer("_IndexBuffer", indexBuffer);
            meshletDrawBufferData.material.SetBuffer("_MeshletBuffer", meshletDrawBufferData.MeshletCacheData.meshletBuffer);
            meshletDrawBufferData.material.SetBuffer("_VisibleMeshlets", meshletDrawBufferData.visibilityBuffer);
            meshletDrawBufferData.material.SetBuffer("_Transform", meshletDrawBufferData.modelBuffer);

            cmd.DrawProceduralIndirect(
                Matrix4x4.identity,
                meshletDrawBufferData.material,
                0,
                MeshTopology.Triangles,
                meshletDrawBufferData.drawArgsBuffer
            );
        }
    }

    [SerializeField] ComputeShader computeShader;
    [SerializeField] Material drawMaterial;
    MeshletPass pass;

    public override void Create() {
        pass = new MeshletPass(computeShader, drawMaterial);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        renderer.EnqueuePass(pass);
    }
}