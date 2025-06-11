using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Jobs;
using System.Linq;
using Mono.Cecil.Cil;


#if UNITY_EDITOR
using UnityEditor;
#endif

public class MeshletRenderFeature : ScriptableRendererFeature {
    class MeshletPass : ScriptableRenderPass {
        [BurstCompile]
        struct ModelBufferJob : IJobParallelForTransform {
            public float4x4 vp;
            public NativeArray<ModelBuffer> output;

            public void Execute(int index, TransformAccess transform) {
                float4x4 model = float4x4.TRS(transform.position, transform.rotation, transform.localScale);
                output[index] = new ModelBuffer {
                    localToWorld = model,
                    mvp = math.mul(vp, model)
                };
            }
        }

        class PassData { }

        class MeshletDrawBufferData {
            public GraphicsBuffer visibilityBuffer;
            public GraphicsBuffer drawArgsBuffer;
            public GraphicsBuffer modelBuffer;

            public BufferHandle visibilityBufferHandle;
            public BufferHandle drawArgsBufferHandle;
            public BufferHandle modelBufferHandle;

            public BufferHandle cullingBufferHandle;

            public NativeArray<ModelBuffer> modelBufferArray;
            
            public MeshletCacheData meshletCacheData;
            public MeshletObjectReferenceData meshletObjectReferenceData;
            public Material material;
        }

        [StructLayout(LayoutKind.Sequential)]
        class CameraBufferData {
            public GraphicsBuffer cameraCullingBuffer;
            public GraphicsBuffer cameraDrawingBuffer;
            public Camera camreaTarget;
            public Matrix4x4 vp;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ModelBuffer {
            public Matrix4x4 localToWorld;
            public Matrix4x4 mvp;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CameraCullingBuffer {
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
        struct CameraDrawingBuffer {
            public Matrix4x4 vp;
        }

        private readonly int SIZEOFMODELBUFFER = Marshal.SizeOf(typeof(ModelBuffer));
        private readonly int SIZEOFMESHLETVISIBLE = Marshal.SizeOf(typeof(MeshletVisible));
        private readonly int SIZEOFCAMERACULLINGBUFFER = Marshal.SizeOf(typeof(CameraCullingBuffer));
        private readonly int SIZEOFCAMERADRAWINGBUFFER = Marshal.SizeOf(typeof(CameraDrawingBuffer));

        private readonly uint[] ARGS = new uint[]
        {
            (uint)MeshletGenerator.MAX_TRIANGLES * 3,       // index count per instance
            0,                                              // instance count (written by compute shader)
            0,                                              // start index location
            0,                                              // base vertex location
            0                                               // start instance location
        };

        private CameraBufferData cameraBufferData;
        private ComputeShader cullShader;

        private List<MeshletDrawBufferData> meshletDrawBufferDataList = new List<MeshletDrawBufferData>();
        public MeshletPass(ComputeShader compute, Material material) {
            this.renderPassEvent = RenderPassEvent.AfterRenderingShadows;
            this.cullShader = compute;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
            //Dispose();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            /// Camera Buffer
            if (cameraBufferData == null)
                cameraBufferData = new CameraBufferData();

            CreateCameraBuffer(cameraData, cameraBufferData);

            /// Allocate MeshDraw Buffer Pool
            int meshDrawPoolCount = 0;
            foreach (var mesh in MeshletManager.GetOriginalMeshList()) {
                MeshletObjectReferenceData meshletReferenceData = MeshletManager.GetMeshletReferenceDataFromOrignalMesh(mesh);
                if (meshletReferenceData == null || meshletReferenceData.meshletObjects.Count <= 0)
                    continue;

                if (meshDrawPoolCount >= meshletDrawBufferDataList.Count)
                    meshletDrawBufferDataList.Add(new MeshletDrawBufferData());

                MeshletCacheData meshletCache = MeshletManager.GetMeshletCacheDataFromOriginalMesh(mesh);
                meshletDrawBufferDataList[meshDrawPoolCount].meshletObjectReferenceData = meshletReferenceData;
                meshletDrawBufferDataList[meshDrawPoolCount].meshletCacheData = meshletCache;
                meshDrawPoolCount++;
            }

            /// Calculate MVPs
            CalculateMVPs(meshletDrawBufferDataList, cameraBufferData.vp, meshDrawPoolCount);

            for(int i = 0; i < meshDrawPoolCount; i++) { 
                CreateDrawBuffer(renderGraph, meshletDrawBufferDataList[i]);
            }

            using (var builder = renderGraph.AddComputePass<PassData>("Cull Meshlets", out var passData)) {
                builder.AllowPassCulling(false);
                builder.EnableAsyncCompute(true);
                builder.SetRenderFunc((PassData data, ComputeGraphContext context) => {
                    int meshDrawPoolIndex = 0;
                    foreach (var mesh in MeshletManager.GetOriginalMeshList()) {
                        MeshletObjectReferenceData meshletReferenceData = MeshletManager.GetMeshletReferenceDataFromOrignalMesh(mesh);
                        if (meshletReferenceData == null || meshletReferenceData.meshletObjects.Count <= 0)
                            continue;

                        MeshletCacheData meshletCache = MeshletManager.GetMeshletCacheDataFromOriginalMesh(mesh);

                        ExecuteCullingGroup(renderGraph, context.cmd, meshletDrawBufferDataList[meshDrawPoolIndex], cameraBufferData.cameraCullingBuffer);
                        meshDrawPoolIndex++;
                    }
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Draw Meshlets", out var passData)) {
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) => {
                    foreach (var meshletDrawBufferData in meshletDrawBufferDataList) {
                        RenderMeshletGroup(ctx.cmd, meshletDrawBufferData, cameraBufferData.cameraDrawingBuffer);
                    }
                });
            }
        }

        private void CreateCameraBuffer(UniversalCameraData cameraData, CameraBufferData cameraBufferData) {
            Matrix4x4 view = cameraData.camera.worldToCameraMatrix;
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(cameraData.camera.projectionMatrix, true);
            Matrix4x4 vp = projection * view;

            Camera cameraTarget = cameraData.camera;
            if (cameraData.isSceneViewCamera)
                cameraTarget = Camera.main;

            /// Camera Culling Buffer
            CameraCullingBuffer cameraCullingBufferData = new CameraCullingBuffer();
            cameraCullingBufferData.CameraPosition = cameraTarget.transform.position;

            Plane[] cameraPlane = GeometryUtility.CalculateFrustumPlanes(cameraTarget);
            cameraCullingBufferData.leftPlane = new Vector4(cameraPlane[0].normal.x, cameraPlane[0].normal.y, cameraPlane[0].normal.z, cameraPlane[0].distance);
            cameraCullingBufferData.rightPlane = new Vector4(cameraPlane[1].normal.x, cameraPlane[1].normal.y, cameraPlane[1].normal.z, cameraPlane[1].distance);
            cameraCullingBufferData.downPlane = new Vector4(cameraPlane[2].normal.x, cameraPlane[2].normal.y, cameraPlane[2].normal.z, cameraPlane[2].distance);
            cameraCullingBufferData.upPlane = new Vector4(cameraPlane[3].normal.x, cameraPlane[3].normal.y, cameraPlane[3].normal.z, cameraPlane[3].distance);
            cameraCullingBufferData.frontPlane = new Vector4(cameraPlane[4].normal.x, cameraPlane[4].normal.y, cameraPlane[4].normal.z, cameraPlane[4].distance);
            cameraCullingBufferData.backPlane = new Vector4(cameraPlane[5].normal.x, cameraPlane[5].normal.y, cameraPlane[5].normal.z, cameraPlane[5].distance);


            if (cameraBufferData.cameraCullingBuffer == null || !cameraBufferData.cameraCullingBuffer.IsValid())
                cameraBufferData.cameraCullingBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, SIZEOFCAMERACULLINGBUFFER);

            cameraBufferData.cameraCullingBuffer.SetData(new[] { cameraCullingBufferData });

            /// Camera Drawing Buffer
            CameraDrawingBuffer cameraDrawingBufferData = new CameraDrawingBuffer();
            cameraDrawingBufferData.vp = vp;

            if (cameraBufferData.cameraDrawingBuffer == null || !cameraBufferData.cameraDrawingBuffer.IsValid())
                cameraBufferData.cameraDrawingBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, SIZEOFCAMERADRAWINGBUFFER);

            cameraBufferData.cameraDrawingBuffer.SetData(new[] { cameraDrawingBufferData });

            cameraBufferData.camreaTarget = cameraTarget;
            cameraBufferData.vp = vp;
        }

        private void CalculateMVPs(IList<MeshletDrawBufferData> meshletDrawBufferDataArr, Matrix4x4 vp, int count)
        {
            NativeArray<JobHandle> jobHandles = new NativeArray<JobHandle>(meshletDrawBufferDataArr.Count, Allocator.Temp);
            for (int i = 0; i < count; i++){
                MeshletDrawBufferData meshletDrawBufferData = meshletDrawBufferDataArr[i];

                // Model Buffer
                if (meshletDrawBufferData.modelBufferArray.IsCreated && meshletDrawBufferData.meshletObjectReferenceData.transformAccessArray.length != meshletDrawBufferData.modelBufferArray.Length)
                    meshletDrawBufferData.modelBufferArray.Dispose();

                if (!meshletDrawBufferData.modelBufferArray.IsCreated)
                    meshletDrawBufferData.modelBufferArray = new NativeArray<ModelBuffer>(meshletDrawBufferData.meshletObjectReferenceData.transformAccessArray.length, Allocator.Persistent);

                // Calculate MVP
                ModelBufferJob job = new ModelBufferJob {
                    vp = vp,
                    output = meshletDrawBufferData.modelBufferArray
                };

                JobHandle handle = job.Schedule(meshletDrawBufferData.meshletObjectReferenceData.transformAccessArray);
                jobHandles[i] = handle;
            }

            JobHandle combined = JobHandle.CombineDependencies(jobHandles);
            combined.Complete(); 

            jobHandles.Dispose();
        }


        private void CreateDrawBuffer(
            RenderGraph renderGraph, MeshletDrawBufferData meshletDrawBufferData
        ) {

            MeshletCacheData meshletCacheData = meshletDrawBufferData.meshletCacheData;
            IList<MeshletObject> meshletObjects = meshletDrawBufferData.meshletObjectReferenceData.meshletObjects;

            if (meshletDrawBufferData.modelBuffer == null || meshletDrawBufferData.modelBuffer.count != meshletObjects.Count)
                meshletDrawBufferData.modelBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, meshletObjects.Count, SIZEOFMODELBUFFER);

            meshletDrawBufferData.modelBuffer.SetData(meshletDrawBufferData.modelBufferArray);


            // Visibility Buffer
            if (meshletDrawBufferData.visibilityBuffer == null || meshletDrawBufferData.visibilityBuffer.count != meshletCacheData.cullData.Count * meshletObjects.Count)
                meshletDrawBufferData.visibilityBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, meshletCacheData.cullData.Count * meshletObjects.Count, SIZEOFMESHLETVISIBLE);

            meshletDrawBufferData.visibilityBuffer.SetCounterValue(0);

            // DrawArgs Buffer
            if (meshletDrawBufferData.drawArgsBuffer == null)
                meshletDrawBufferData.drawArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, 5 * sizeof(uint));

           
            meshletDrawBufferData.drawArgsBuffer.SetData(ARGS);
            meshletDrawBufferData.material = meshletObjects[0].meshRenderer.sharedMaterial;


            // Model Buffer
            meshletDrawBufferData.modelBufferHandle = renderGraph.ImportBuffer(meshletDrawBufferData.modelBuffer);

            // Visibility Buffer
            meshletDrawBufferData.visibilityBufferHandle = renderGraph.ImportBuffer(meshletDrawBufferData.visibilityBuffer);

            // DrawArgs Buffer
            meshletDrawBufferData.drawArgsBufferHandle = renderGraph.ImportBuffer(meshletDrawBufferData.drawArgsBuffer);

            // Culling Buffer
            meshletDrawBufferData.cullingBufferHandle = renderGraph.ImportBuffer(meshletCacheData.meshletCullingDataBuffer);
        }


        private void ExecuteCullingGroup(RenderGraph renderGraph, ComputeCommandBuffer cmd, MeshletDrawBufferData meshletDrawBufferData, GraphicsBuffer cameraBuffer) {
            // Set compute shader buffers using cmd  
            int kernel = cullShader.FindKernel("CSMain");
            cmd.SetComputeBufferParam(cullShader, kernel, "MeshletCullingData", meshletDrawBufferData.cullingBufferHandle);
            cmd.SetComputeBufferParam(cullShader, kernel, "VisibleMeshlets", meshletDrawBufferData.visibilityBufferHandle);
            cmd.SetComputeBufferParam(cullShader, kernel, "Transform", meshletDrawBufferData.modelBufferHandle);
            cmd.SetComputeConstantBufferParam(cullShader, Shader.PropertyToID("CameraBuffer"), cameraBuffer, 0, cameraBuffer.stride);

            // Dispatch compute shader  
            cmd.DispatchCompute(cullShader, kernel, Mathf.CeilToInt((meshletDrawBufferData.meshletCacheData.cullData.Count * meshletDrawBufferData.modelBuffer.count) / 64.0f), 1, 1);
            cmd.CopyCounterValue(meshletDrawBufferData.visibilityBufferHandle, meshletDrawBufferData.drawArgsBufferHandle, sizeof(uint)); // offset 4 bytes (index 1)
        }

        private void RenderMeshletGroup(RasterCommandBuffer cmd, MeshletDrawBufferData meshletDrawBufferData, GraphicsBuffer cameraBuffer) {
            meshletDrawBufferData.material.SetBuffer("_VertexBuffer", meshletDrawBufferData.meshletCacheData.vertexBuffer);
            meshletDrawBufferData.material.SetBuffer("_IndexBuffer", meshletDrawBufferData.meshletCacheData.indexBuffer);
            meshletDrawBufferData.material.SetBuffer("_MeshletBuffer", meshletDrawBufferData.meshletCacheData.meshletBuffer);
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

        public void Dispose() {
            // Release
            foreach (var meshletDrawBufferData in meshletDrawBufferDataList) {
                meshletDrawBufferData.modelBuffer?.Dispose();
                meshletDrawBufferData.modelBuffer = null;
                meshletDrawBufferData.visibilityBuffer?.Dispose();
                meshletDrawBufferData.visibilityBuffer = null;
                meshletDrawBufferData.drawArgsBuffer?.Dispose();
                meshletDrawBufferData.drawArgsBuffer = null;

                if (meshletDrawBufferData.modelBufferArray.IsCreated)
                    meshletDrawBufferData.modelBufferArray.Dispose();
            }
            meshletDrawBufferDataList.Clear();
            cameraBufferData?.cameraCullingBuffer?.Dispose();
            cameraBufferData?.cameraDrawingBuffer?.Dispose();

        }
    }
    [SerializeField] ComputeShader computeShader;
    [SerializeField] Material drawMaterial;
    MeshletPass pass;

    public override void Create() {
        pass = new MeshletPass(computeShader, drawMaterial);

#if UNITY_EDITOR
        AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

        AssemblyReloadEvents.afterAssemblyReload -= OnBeforeAssemblyReload;
        AssemblyReloadEvents.afterAssemblyReload += OnBeforeAssemblyReload;
#endif
    }

#if UNITY_EDITOR
    private void OnBeforeAssemblyReload() {
        pass?.Dispose();
    }
#endif

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        renderer.EnqueuePass(pass);
    }
    protected override void Dispose(bool disposing) {
        if (disposing) {
            pass?.Dispose();
        }
    }
}