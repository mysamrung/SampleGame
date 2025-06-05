using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class MeshletRenderFeature : ScriptableRendererFeature
{
    class MeshletPass : ScriptableRenderPass
    {
        class PassData
        {
        }


        class MeshletDrawBufferData
        {
            public GraphicsBuffer visibilityBuffer;
            public GraphicsBuffer drawArgsBuffer;
            public GraphicsBuffer modelBuffer;

            public BufferHandle visibilityBufferHandle;
            public BufferHandle drawArgsBufferHandle;
            public BufferHandle modelBufferHandle;

            public BufferHandle cullingBufferHandle;

            public MeshletCacheData MeshletCacheData;
            public Material material;
        }

        struct ModelBuffer
        {
            public Matrix4x4 localToWorld;
            public Matrix4x4 mvp;
        }

        private ComputeShader cullShader;

        List<MeshletDrawBufferData> meshletDrawBufferDataList = new List<MeshletDrawBufferData>();
        public MeshletPass(ComputeShader compute, Material material)
        {
            this.cullShader = compute;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            //Dispose();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            Matrix4x4 view = cameraData.camera.worldToCameraMatrix;
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(cameraData.camera.projectionMatrix, true);
            Matrix4x4 vp = projection * view;

            int meshDrawPoolCount = 0;
            foreach (var mesh in MeshletManager.GetOriginalMeshList())
            {
                IList<MeshletObject> meshletList = MeshletManager.GetMeshletObjectListFromOrignalMesh(mesh);
                if (meshletList == null || meshletList.Count <= 0)
                    continue;

                MeshletCacheData meshletCache = MeshletManager.GetMeshletCacheDataFromOriginalMesh(mesh);

                if (meshDrawPoolCount >= meshletDrawBufferDataList.Count)
                    meshletDrawBufferDataList.Add(new MeshletDrawBufferData());

                CreateBuffer(renderGraph, meshletDrawBufferDataList[meshDrawPoolCount], meshletCache, meshletList, vp);
                meshDrawPoolCount++;
            }

            using (var builder = renderGraph.AddComputePass<PassData>("Cull Meshlets", out var passData))
            {
                builder.AllowPassCulling(false);
                builder.EnableAsyncCompute(false);
                builder.SetRenderFunc((PassData data, ComputeGraphContext context) =>
                {
                    CameraModelBufferData cameraModelBufferData = new CameraModelBufferData();

                    if (cameraData.isSceneViewCamera)
                        cameraModelBufferData.CameraPosition = Camera.main.transform.position;
                    else
                        cameraModelBufferData.CameraPosition = cameraData.camera.transform.position;

                    using (ComputeBuffer cameraBuffer = new ComputeBuffer(1, Marshal.SizeOf(typeof(CameraModelBufferData))))
                    {
                        cameraBuffer.SetData(new[] { cameraModelBufferData });

                        int meshDrawPoolIndex = 0;
                        foreach (var mesh in MeshletManager.GetOriginalMeshList())
                        {
                            IList<MeshletObject> meshletList = MeshletManager.GetMeshletObjectListFromOrignalMesh(mesh);
                            if (meshletList == null || meshletList.Count <= 0)
                                continue;

                            MeshletCacheData meshletCache = MeshletManager.GetMeshletCacheDataFromOriginalMesh(mesh);

                            ExecuteCullingGroup(renderGraph, context.cmd, meshletDrawBufferDataList[meshDrawPoolIndex], cameraBuffer);
                            meshDrawPoolIndex++;
                        }

                        cameraBuffer?.Dispose();
                    }
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Draw Meshlets", out var passData))
            {
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    foreach (var meshletDrawBufferData in meshletDrawBufferDataList)
                    {
                        RenderMeshletGroup(ctx.cmd, meshletDrawBufferData);
                    }
                });
            }
        }

        private void CreateBuffer(RenderGraph renderGraph, MeshletDrawBufferData meshletDrawBufferData, MeshletCacheData meshletCacheData, IList<MeshletObject> meshletObjects, Matrix4x4 vp)
        {
            try
            {
                List<ModelBuffer> modelBufferList = new List<ModelBuffer>();
                foreach (var meshletObject in meshletObjects)
                {
                    if (meshletObject == null || !meshletObject.enabled)
                        continue;

                    ModelBuffer modelBuffer = new ModelBuffer();
                    modelBuffer.localToWorld = meshletObject.transform.localToWorldMatrix;
                    modelBuffer.mvp = vp * modelBuffer.localToWorld;
                    modelBufferList.Add(modelBuffer);
                }

                if (modelBufferList.Count <= 0)
                    return;

                // Model Buffer
                if (meshletDrawBufferData.modelBuffer == null)
                    meshletDrawBufferData.modelBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, modelBufferList.Count, Marshal.SizeOf(typeof(ModelBuffer)));

                meshletDrawBufferData.modelBuffer.SetData(modelBufferList);

                // Visibility Buffer
                if (meshletDrawBufferData.visibilityBuffer == null)
                    meshletDrawBufferData.visibilityBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, meshletCacheData.cullData.Count * modelBufferList.Count, Marshal.SizeOf(typeof(MeshletVisible)));

                meshletDrawBufferData.visibilityBuffer.SetCounterValue(0);

                // DrawArgs Buffer
                if (meshletDrawBufferData.drawArgsBuffer == null)
                    meshletDrawBufferData.drawArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, 5 * sizeof(uint));

                uint[] args = new uint[]
                {
                (uint)MeshletGenerator.MAX_TRIANGLES * 3,       // index count per instance
                0,                                              // instance count (written by compute shader)
                0,                                              // start index location
                0,                                              // base vertex location
                0                                               // start instance location
                };
                meshletDrawBufferData.drawArgsBuffer.SetData(args);
                meshletDrawBufferData.material = meshletObjects[0].meshRenderer.sharedMaterial;


                // Model Buffer
                meshletDrawBufferData.modelBufferHandle = renderGraph.ImportBuffer(meshletDrawBufferData.modelBuffer);

                // Visibility Buffer
                meshletDrawBufferData.visibilityBufferHandle = renderGraph.ImportBuffer(meshletDrawBufferData.visibilityBuffer);

                // DrawArgs Buffer
                meshletDrawBufferData.drawArgsBufferHandle = renderGraph.ImportBuffer(meshletDrawBufferData.drawArgsBuffer);

                // Culling Buffer
                meshletDrawBufferData.cullingBufferHandle = renderGraph.ImportBuffer(meshletCacheData.meshletCullingDataBuffer);

                meshletDrawBufferData.MeshletCacheData = meshletCacheData;
            }
            catch
            {

            }
        }


        private void ExecuteCullingGroup(RenderGraph renderGraph, ComputeCommandBuffer cmd, MeshletDrawBufferData meshletDrawBufferData, ComputeBuffer cameraBuffer)
        {
            // Set compute shader buffers using cmd  
            int kernel = cullShader.FindKernel("CSMain");
            cmd.SetComputeBufferParam(cullShader, kernel, "MeshletCullingData", meshletDrawBufferData.cullingBufferHandle);
            cmd.SetComputeBufferParam(cullShader, kernel, "VisibleMeshlets", meshletDrawBufferData.visibilityBufferHandle);
            cmd.SetComputeBufferParam(cullShader, kernel, "Transform", meshletDrawBufferData.modelBufferHandle);
            cmd.SetComputeConstantBufferParam(cullShader, Shader.PropertyToID("CameraBuffer"), cameraBuffer, 0, cameraBuffer.stride);

            // Dispatch compute shader  
            cmd.DispatchCompute(cullShader, kernel, Mathf.CeilToInt((meshletDrawBufferData.MeshletCacheData.cullData.Count * meshletDrawBufferData.modelBuffer.count) / 64.0f), 1, 1);
            cmd.CopyCounterValue(meshletDrawBufferData.visibilityBufferHandle, meshletDrawBufferData.drawArgsBufferHandle, sizeof(uint)); // offset 4 bytes (index 1)
        }

        private void RenderMeshletGroup(RasterCommandBuffer cmd, MeshletDrawBufferData meshletDrawBufferData)
        {
            meshletDrawBufferData.material.SetBuffer("_VertexBuffer", meshletDrawBufferData.MeshletCacheData.vertexBuffer);
            meshletDrawBufferData.material.SetBuffer("_IndexBuffer", meshletDrawBufferData.MeshletCacheData.indexBuffer);
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

        public void Dispose()
        {
            // Release
            foreach (var meshletDrawBufferData in meshletDrawBufferDataList)
            {
                meshletDrawBufferData.modelBuffer?.Dispose();
                meshletDrawBufferData.modelBuffer = null;
                meshletDrawBufferData.visibilityBuffer?.Dispose();
                meshletDrawBufferData.visibilityBuffer = null;
                meshletDrawBufferData.drawArgsBuffer?.Dispose();
                meshletDrawBufferData.drawArgsBuffer = null;
            }
            meshletDrawBufferDataList.Clear();
        }
    }
    [SerializeField] ComputeShader computeShader;
    [SerializeField] Material drawMaterial;
    MeshletPass pass;

    public override void Create()
    {
        pass = new MeshletPass(computeShader, drawMaterial);

#if UNITY_EDITOR
        AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

        AssemblyReloadEvents.afterAssemblyReload -= OnBeforeAssemblyReload;
        AssemblyReloadEvents.afterAssemblyReload += OnBeforeAssemblyReload;
#endif
    }

#if UNITY_EDITOR
    private void OnBeforeAssemblyReload()
    {
        pass?.Dispose();
    }
#endif

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(pass);
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            pass?.Dispose();
        }
    }
}