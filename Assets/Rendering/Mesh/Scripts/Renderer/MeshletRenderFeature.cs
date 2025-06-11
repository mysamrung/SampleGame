using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using System;





#if UNITY_EDITOR
using UnityEditor;
#endif

public class MeshletRenderFeature : ScriptableRendererFeature
{
    class MeshletPass : ScriptableRenderPass
    {
        [BurstCompile]
        struct ModelBufferJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Matrix4x4> localToWorlds;
            public Matrix4x4 vp;
            public NativeArray<ModelBuffer> output;

            public void Execute(int index)
            {
                output[index] = new ModelBuffer
                {
                    localToWorld = localToWorlds[index],
                    mvp = vp * localToWorlds[index]
                };
            }
        }

        class PassData { }

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

        [StructLayout(LayoutKind.Sequential)]
        class CameraBufferData
        {
            public GraphicsBuffer cameraCullingBuffer;
            public GraphicsBuffer cameraDrawingBuffer;
            public Matrix4x4 vp;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ModelBuffer
        {
            public Matrix4x4 localToWorld;
            public Matrix4x4 mvp;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CameraCullingBuffer
        {
            public Vector3 CameraPosition;
            public float Padding; // alignment

            public Vector4 leftPlane;
            public Vector4 rightPlane;
            public Vector4 downPlane;
            public Vector4 upPlane;
            public Vector4 frontPlane;
            public Vector4 backPlane;

        }

        [StructLayout(LayoutKind.Sequential)]
        struct CameraDrawingBuffer
        {
            public Matrix4x4 vp;
        }

        private readonly int SIZEOFMODELBUFFER = Marshal.SizeOf(typeof(ModelBuffer));
        private readonly int SIZEOFMESHLETVISIBLE = Marshal.SizeOf(typeof(MeshletVisible));
        private readonly int SIZEOFCAMERACULLINGBUFFER = Marshal.SizeOf(typeof(CameraCullingBuffer));
        private readonly int SIZEOFCAMERADRAWINGBUFFER = Marshal.SizeOf(typeof(CameraDrawingBuffer));

        private CameraBufferData cameraBufferData;
        private ComputeShader cullShader;

        List<MeshletDrawBufferData> meshletDrawBufferDataList = new List<MeshletDrawBufferData>();
        public MeshletPass(ComputeShader compute, Material material)
        {
            this.renderPassEvent = RenderPassEvent.AfterRenderingShadows;
            this.cullShader = compute;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            //Dispose();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            /// Camera Buffer
            if (cameraBufferData == null)
                cameraBufferData = new CameraBufferData();

            CreateCameraBuffer(cameraData, cameraBufferData);

            /// Draw Buffer
            int meshDrawPoolCount = 0;
            foreach (var mesh in MeshletManager.GetOriginalMeshList())
            {
                IList<MeshletObject> meshletList = MeshletManager.GetMeshletObjectListFromOrignalMesh(mesh);
                if (meshletList == null || meshletList.Count <= 0)
                    continue;

                MeshletCacheData meshletCache = MeshletManager.GetMeshletCacheDataFromOriginalMesh(mesh);

                if (meshDrawPoolCount >= meshletDrawBufferDataList.Count)
                    meshletDrawBufferDataList.Add(new MeshletDrawBufferData());

                CreateDrawBuffer(renderGraph, meshletDrawBufferDataList[meshDrawPoolCount], meshletCache, meshletList, cameraBufferData.vp);
                meshDrawPoolCount++;
            }


            using (var builder = renderGraph.AddComputePass<PassData>("Cull Meshlets", out var passData))
            {
                builder.AllowPassCulling(false);
                builder.EnableAsyncCompute(true);
                builder.SetRenderFunc((PassData data, ComputeGraphContext context) =>
                {
                    int meshDrawPoolIndex = 0;
                    foreach (var mesh in MeshletManager.GetOriginalMeshList())
                    {
                        IList<MeshletObject> meshletList = MeshletManager.GetMeshletObjectListFromOrignalMesh(mesh);
                        if (meshletList == null || meshletList.Count <= 0)
                            continue;

                        MeshletCacheData meshletCache = MeshletManager.GetMeshletCacheDataFromOriginalMesh(mesh);

                        ExecuteCullingGroup(renderGraph, context.cmd, meshletDrawBufferDataList[meshDrawPoolIndex], cameraBufferData.cameraCullingBuffer);
                        meshDrawPoolIndex++;
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
                        RenderMeshletGroup(ctx.cmd, meshletDrawBufferData, cameraBufferData.cameraDrawingBuffer);
                    }
                });
            }
        }

        private void CreateCameraBuffer(UniversalCameraData cameraData, CameraBufferData cameraBufferData)
        {
            Matrix4x4 view = cameraData.camera.worldToCameraMatrix;
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(cameraData.camera.projectionMatrix, true);
            Matrix4x4 vp = projection * view;

            Camera cameraTarget = cameraData.camera;
            if(cameraData.isSceneViewCamera)
                cameraTarget = Camera.main;

            /// Camera Culling Buffer
            CameraCullingBuffer cameraCullingBufferData = new CameraCullingBuffer();
            cameraCullingBufferData.CameraPosition = cameraTarget.transform.position;
    
            Plane[] cameraPlane = GeometryUtility.CalculateFrustumPlanes(cameraTarget);
            cameraCullingBufferData.leftPlane     = new Vector4(cameraPlane[0].normal.x, cameraPlane[0].normal.y, cameraPlane[0].normal.z, cameraPlane[0].distance);
            cameraCullingBufferData.rightPlane    = new Vector4(cameraPlane[1].normal.x, cameraPlane[1].normal.y, cameraPlane[1].normal.z, cameraPlane[1].distance);
            cameraCullingBufferData.downPlane     = new Vector4(cameraPlane[2].normal.x, cameraPlane[2].normal.y, cameraPlane[2].normal.z, cameraPlane[2].distance);
            cameraCullingBufferData.upPlane       = new Vector4(cameraPlane[3].normal.x, cameraPlane[3].normal.y, cameraPlane[3].normal.z, cameraPlane[3].distance);
            cameraCullingBufferData.frontPlane    = new Vector4(cameraPlane[4].normal.x, cameraPlane[4].normal.y, cameraPlane[4].normal.z, cameraPlane[4].distance);
            cameraCullingBufferData.backPlane     = new Vector4(cameraPlane[5].normal.x, cameraPlane[5].normal.y, cameraPlane[5].normal.z, cameraPlane[5].distance);


            if (cameraBufferData.cameraCullingBuffer == null || !cameraBufferData.cameraCullingBuffer.IsValid())
                cameraBufferData.cameraCullingBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, SIZEOFCAMERACULLINGBUFFER);

            cameraBufferData.cameraCullingBuffer.SetData(new[] { cameraCullingBufferData });

            /// Camera Drawing Buffer
            CameraDrawingBuffer cameraDrawingBufferData = new CameraDrawingBuffer();
            cameraDrawingBufferData.vp = vp;

            if (cameraBufferData.cameraDrawingBuffer == null || !cameraBufferData.cameraDrawingBuffer.IsValid())
                cameraBufferData.cameraDrawingBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, SIZEOFCAMERADRAWINGBUFFER);

            cameraBufferData.cameraDrawingBuffer.SetData(new[] { cameraDrawingBufferData });

            cameraBufferData.vp = vp;
        }

        private void CreateDrawBuffer(RenderGraph renderGraph, MeshletDrawBufferData meshletDrawBufferData, MeshletCacheData meshletCacheData, IList<MeshletObject> meshletObjects, Matrix4x4 vp)
        {
            int validCount = 0;
            for (int i = 0; i < meshletObjects.Count; ++i)
            {
                var obj = meshletObjects[i];
                if (obj != null && obj.enabled)
                    validCount++;
            }

            if (validCount <= 0)
                return;

            NativeArray<Matrix4x4> localToWorlds = new NativeArray<Matrix4x4>(validCount, Allocator.TempJob);
            NativeArray<ModelBuffer> output = new NativeArray<ModelBuffer>(validCount, Allocator.TempJob);

            try
            {
                int writeIndex = 0;
                for (int i = 0; i < meshletObjects.Count; ++i)
                {
                    var meshletObject = meshletObjects[i];
                    if (meshletObject == null || !meshletObject.enabled)
                        continue;

                    localToWorlds[writeIndex++] = meshletObject.transform.localToWorldMatrix;
                }

                var job = new ModelBufferJob
                {
                    localToWorlds = localToWorlds,
                    vp = vp,
                    output = output
                };

                var handle = job.Schedule(validCount, 32);
                handle.Complete();
        
                // Model Buffer
                if (meshletDrawBufferData.modelBuffer == null || meshletDrawBufferData.modelBuffer.count != validCount)
                    meshletDrawBufferData.modelBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, validCount, SIZEOFMODELBUFFER);

                meshletDrawBufferData.modelBuffer.SetData(output);

                // Visibility Buffer
                if (meshletDrawBufferData.visibilityBuffer == null || meshletDrawBufferData.visibilityBuffer.count != meshletCacheData.cullData.Count * validCount)
                    meshletDrawBufferData.visibilityBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, meshletCacheData.cullData.Count * validCount, SIZEOFMESHLETVISIBLE);

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
            finally
            {
                localToWorlds.Dispose();
                output.Dispose();
            }

        }


        private void ExecuteCullingGroup(RenderGraph renderGraph, ComputeCommandBuffer cmd, MeshletDrawBufferData meshletDrawBufferData, GraphicsBuffer cameraBuffer)
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

        private void RenderMeshletGroup(RasterCommandBuffer cmd, MeshletDrawBufferData meshletDrawBufferData, GraphicsBuffer cameraBuffer)
        {
            meshletDrawBufferData.material.SetBuffer("_VertexBuffer", meshletDrawBufferData.MeshletCacheData.vertexBuffer);
            meshletDrawBufferData.material.SetBuffer("_IndexBuffer", meshletDrawBufferData.MeshletCacheData.indexBuffer);
            meshletDrawBufferData.material.SetBuffer("_MeshletBuffer", meshletDrawBufferData.MeshletCacheData.meshletBuffer);
            meshletDrawBufferData.material.SetBuffer("_VisibleMeshlets", meshletDrawBufferData.visibilityBuffer);
            meshletDrawBufferData.material.SetBuffer("_Transform", meshletDrawBufferData.modelBuffer);
            //meshletDrawBufferData.material.SetConstantBuffer(Shader.PropertyToID("CameraData"), cameraBuffer, 0, cameraBuffer.stride);

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
            cameraBufferData?.cameraCullingBuffer?.Dispose();
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