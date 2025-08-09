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
using UnityEngine.Profiling;
using System;
using System;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;







#if UNITY_EDITOR
using UnityEditor;
#endif

public class MeshletRenderFeature : ScriptableRendererFeature {
    class MeshletPass : ScriptableRenderPass {
        [BurstCompile]
        struct ModelBufferJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Matrix4x4> models;
            [ReadOnly] public NativeArray<Bounds> bounds;
            [ReadOnly] public float4x4 vp;
            [ReadOnly] public NativeArray<float4> planes; // 6 planes

            [WriteOnly] public NativeList<ModelBuffer>.ParallelWriter output;

            public void Execute(int index) {
                Bounds b = bounds[index];
                float3 center = b.center;
                float3 extents = b.extents;

                // SAT-based frustum culling
                for (int i = 0; i < 6; i++) {
                    float4 p = planes[i];
                    float3 normal = p.xyz;

                    // Project half extents onto plane normal
                    float r = extents.x * math.abs(normal.x) +
                              extents.y * math.abs(normal.y) +
                              extents.z * math.abs(normal.z);

                    float distance = math.dot(normal, center) + p.w;

                    if (distance + r < 0f) {
                        return; // Outside, early exit
                    }
                }

                output.AddNoResize(new ModelBuffer {
                    localToWorld = models[index],
                    mvp = math.mul(vp, models[index])
                });
            }
        }
        [BurstCompile]
        struct ModelBufferJobTransform : IJobParallelForTransform {
            [ReadOnly] public NativeArray<Matrix4x4> models;
            [ReadOnly] public NativeArray<Bounds> bounds;
            [ReadOnly] public float4x4 vp;
            [ReadOnly] public NativeArray<float4> planes; // 6 planes

            [NoAlias][NativeDisableParallelForRestriction] 
            public NativeArray<ModelBuffer> output;
            
            [NativeDisableParallelForRestriction] 
            public NativeCounter.ParallelWriter counter;

            public void Execute(int index, TransformAccess transform) {
                Bounds b = bounds[index];
                float3 center = b.center;
                float3 extents = b.extents;

                // SAT-based frustum culling
                for (int i = 0; i < 6; i++) {
                    float4 p = planes[i];
                    float3 normal = p.xyz;

                    // Project half extents onto plane normal
                    float r = extents.x * math.abs(normal.x) +
                              extents.y * math.abs(normal.y) +
                              extents.z * math.abs(normal.z);

                    float distance = math.dot(normal, center) + p.w;

                    if (distance + r < 0f) {
                        return; // Outside, early exit
                    }
                }

                output[index] = new ModelBuffer {
                    localToWorld = transform.localToWorldMatrix,
                    mvp = math.mul(vp, transform.localToWorldMatrix)
                };

                counter.Increment();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        [NativeContainer]
        public unsafe struct NativeCounter {
            // The actual pointer to the allocated count needs to have restrictions relaxed so jobs can be scheduled with this container
            [NativeDisableUnsafePtrRestriction]
            private int* countIntegers;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            private AtomicSafetyHandle m_Safety;

            // The dispose sentinel tracks memory leaks. It is a managed type so it is cleared to null when scheduling a job
            // The job cannot dispose the container, and no one else can dispose it until the job has run, so it is ok to not pass it along
            // This attribute is required, without it this NativeContainer cannot be passed to a job; since that would give the job access to a managed object
            [NativeSetClassTypeToNullOnSchedule]
            private DisposeSentinel m_DisposeSentinel;
#endif

            // Keep track of where the memory for this was allocated
            private readonly Allocator m_AllocatorLabel;

            public const int INTS_PER_CACHE_LINE = JobsUtility.CacheLineSize / sizeof(int);

            public NativeCounter(Allocator label) {
                // This check is redundant since we always use an int that is blittable.
                // It is here as an example of how to check for type correctness for generic types.
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (!UnsafeUtility.IsBlittable<int>()) {
                    throw new ArgumentException(
                        string.Format("{0} used in NativeQueue<{0}> must be blittable", typeof(int)));
                }
#endif
                this.m_AllocatorLabel = label;

                // Allocate native memory for a single integer
                this.countIntegers = (int*)UnsafeUtility.Malloc(
                    UnsafeUtility.SizeOf<int>() * INTS_PER_CACHE_LINE * JobsUtility.MaxJobThreadCount, 4, label);

                // Create a dispose sentinel to track memory leaks. This also creates the AtomicSafetyHandle
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                DisposeSentinel.Create(out this.m_Safety, out this.m_DisposeSentinel, 0, label);
#endif
                // Initialize the count to 0 to avoid uninitialized data
                this.Count = 0;
            }

            public void Increment() {
                // Verify that the caller has write permission on this data. 
                // This is the race condition protection, without these checks the AtomicSafetyHandle is useless
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(this.m_Safety);
#endif
                (*this.countIntegers)++;
            }

            public int Count {
                get {
                    // Verify that the caller has read permission on this data. 
                    // This is the race condition protection, without these checks the AtomicSafetyHandle is useless
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.CheckReadAndThrow(this.m_Safety);
#endif
                    int count = 0;
                    for (int i = 0; i < JobsUtility.MaxJobThreadCount; ++i) {
                        count += this.countIntegers[INTS_PER_CACHE_LINE * i];
                    }

                    return count;
                }

                set {
                    // Verify that the caller has write permission on this data. 
                    // This is the race condition protection, without these checks the AtomicSafetyHandle is useless
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.CheckWriteAndThrow(this.m_Safety);
#endif
                    // Clear all locally cached counts, 
                    // set the first one to the required value
                    for (int i = 1; i < JobsUtility.MaxJobThreadCount; ++i) {
                        this.countIntegers[INTS_PER_CACHE_LINE * i] = 0;
                    }

                    *this.countIntegers = value;
                }
            }

            public bool IsCreated {
                get {
                    return this.countIntegers != null;
                }
            }

            public void Dispose() {
                // Let the dispose sentinel know that the data has been freed so it does not report any memory leaks
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                DisposeSentinel.Dispose(ref this.m_Safety, ref this.m_DisposeSentinel);
#endif

                UnsafeUtility.Free(this.countIntegers, this.m_AllocatorLabel);
                this.countIntegers = null;
            }

            [NativeContainer]
            // This attribute is what makes it possible to use NativeCounter.Concurrent in a ParallelFor job
            [NativeContainerIsAtomicWriteOnly]
            public struct ParallelWriter {
                // Copy of the pointer from the full NativeCounter
                [NativeDisableUnsafePtrRestriction]
                private int* countIntegers;

                // Copy of the AtomicSafetyHandle from the full NativeCounter. The dispose sentinel is not copied since this inner struct does not own the memory and is not responsible for freeing it.
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                private AtomicSafetyHandle m_Safety;
#endif

                // The current worker thread index; it must use this exact name since it is injected
                [NativeSetThreadIndex]
                int m_ThreadIndex;

                // This is what makes it possible to assign to NativeCounter.Concurrent from NativeCounter
                public static implicit operator ParallelWriter(NativeCounter cnt) {
                    ParallelWriter parallelWriter;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.CheckWriteAndThrow(cnt.m_Safety);
                    parallelWriter.m_Safety = cnt.m_Safety;
                    AtomicSafetyHandle.UseSecondaryVersion(ref parallelWriter.m_Safety);
#endif

                    parallelWriter.countIntegers = cnt.countIntegers;
                    parallelWriter.m_ThreadIndex = 0;

                    return parallelWriter;
                }

                public void Increment() {
                    // Increment still needs to check for write permissions
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.CheckWriteAndThrow(this.m_Safety);
#endif

                    // No need for atomics any more since we are just incrementing the local count
                    ++this.countIntegers[INTS_PER_CACHE_LINE * this.m_ThreadIndex];
                }

                public int Count() {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.CheckReadAndThrow(this.m_Safety);
#endif
                    int count = 0;
                    for (int i = 0; i < JobsUtility.MaxJobThreadCount; ++i) {
                        count += this.countIntegers[INTS_PER_CACHE_LINE * i];
                    }

                    return count;
                }
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
            public NativeCounter nativeCounter;
            
            public MeshletCacheData meshletCacheData;
            public MeshletObjectReferenceData meshletObjectReferenceData;
            public Material material;
        }

        [StructLayout(LayoutKind.Sequential)]
        class CameraBufferData {
            public GraphicsBuffer cameraCullingBuffer;
            public GraphicsBuffer cameraDrawingBuffer;

            public NativeArray<float4> planes = new NativeArray<float4>(6, Allocator.Persistent);

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

        class JobGroupHandle
        {
            public NativeArray<JobHandle> jobHandles;
            public JobHandle jobHandle;
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
        private JobGroupHandle mvpJobHandle = new JobGroupHandle();

        private bool refresh = true;

        public MeshletPass(ComputeShader compute, Material material) {
            this.renderPassEvent = RenderPassEvent.AfterRenderingShadows;
            this.cullShader = compute;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            Profiler.BeginSample("Prepare for Meshlets");

            /// Camera Buffer
            Profiler.BeginSample("Create CameraBuffer");
            if (cameraBufferData == null)
                cameraBufferData = new CameraBufferData();

            CreateCameraBuffer(cameraData, cameraBufferData);
            Profiler.EndSample();

            /// Allocate MeshDraw Buffer Pool
            Profiler.BeginSample("Allocate MeshletDraw Buffer");
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
            Profiler.EndSample();


            // Prepare buffer
            Profiler.BeginSample("Prepare Buffer");
            for (int i = 0; i < meshDrawPoolCount; i++) {
                CreateDrawBuffer(renderGraph, meshletDrawBufferDataList[i]);
                ImportDrawBuffer(renderGraph, meshletDrawBufferDataList[i]);
            }
            Profiler.EndSample();


            /// Calculate MVPs
            Profiler.BeginSample("Calculate MVP");
            CalculateMVPs(meshletDrawBufferDataList, cameraBufferData, meshDrawPoolCount, mvpJobHandle);
            Profiler.EndSample();


            Profiler.EndSample();


            if(MeshletManager.instance == null || !MeshletManager.instance.ignoreCulling) { 
                using (var builder = renderGraph.AddComputePass<PassData>("Cull Meshlets", out var passData)) {
                    builder.AllowPassCulling(false);
                    builder.EnableAsyncCompute(true);
                    builder.SetRenderFunc((PassData data, ComputeGraphContext context) => {

                        if (MeshletManager.instance == null || !MeshletManager.instance.ignoreMVPCalcuate) {
                            Profiler.BeginSample("Retrieve MVPs");
                            JobHandle combined = JobHandle.CombineDependencies(mvpJobHandle.jobHandles);
                            combined.Complete();

                            mvpJobHandle.jobHandles.Dispose();
                            Profiler.EndSample();
                        }


                        if (MeshletManager.instance == null || !MeshletManager.instance.ignoreSetBuffer) {
                            Profiler.BeginSample("Set DrawBuffer");
                            for (int i = 0; i < meshDrawPoolCount; i++) {
                                SetDrawBuffer(renderGraph, meshletDrawBufferDataList[i]);
                            }
                            Profiler.EndSample();
                        }

                        if (MeshletManager.instance == null || !MeshletManager.instance.ignoreExcuteCullingCP) {
                            int meshDrawPoolIndex = 0;
                            foreach (var mesh in MeshletManager.GetOriginalMeshList()) {
                                MeshletObjectReferenceData meshletReferenceData = MeshletManager.GetMeshletReferenceDataFromOrignalMesh(mesh);
                                if (meshletReferenceData == null || meshletReferenceData.meshletObjects.Count <= 0)
                                    continue;

                                MeshletCacheData meshletCache = MeshletManager.GetMeshletCacheDataFromOriginalMesh(mesh);

                                ExecuteCullingGroup(renderGraph, context.cmd, meshletDrawBufferDataList[meshDrawPoolIndex], cameraBufferData.cameraCullingBuffer);
                                meshDrawPoolIndex++;
                            }
                        }
                    });
                }
            }

            if (MeshletManager.instance == null || !MeshletManager.instance.ignoreDrawing) {
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

            cameraBufferData.planes[0] = cameraCullingBufferData.leftPlane;
            cameraBufferData.planes[1] = cameraCullingBufferData.rightPlane;
            cameraBufferData.planes[2] = cameraCullingBufferData.downPlane;
            cameraBufferData.planes[3] = cameraCullingBufferData.upPlane;
            cameraBufferData.planes[4] = cameraCullingBufferData.frontPlane;
            cameraBufferData.planes[5] = cameraCullingBufferData.backPlane;

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

        private void CalculateMVPs(IList<MeshletDrawBufferData> meshletDrawBufferDataArr, CameraBufferData cameraBufferData, int count, JobGroupHandle mvpJobHandle)
        {
            mvpJobHandle.jobHandles = new NativeArray<JobHandle>(count, Allocator.Temp);
            for (int i = 0; i < count; i++){
                MeshletDrawBufferData meshletDrawBufferData = meshletDrawBufferDataArr[i];

                int matrixCount = meshletDrawBufferData.meshletObjectReferenceData.matrixArray.Length;

                // Calculate MVP
                meshletDrawBufferData.modelBuffer.UnlockBufferAfterWrite<ModelBuffer>(matrixCount);
                meshletDrawBufferData.modelBufferArray = meshletDrawBufferData.modelBuffer.LockBufferForWrite<ModelBuffer>(0, matrixCount);


                meshletDrawBufferData.nativeCounter = new NativeCounter(Allocator.TempJob);
                ModelBufferJobTransform job = new ModelBufferJobTransform {
                    vp = cameraBufferData.vp,
                    planes = cameraBufferData.planes,
                    models = meshletDrawBufferData.meshletObjectReferenceData.matrixArray,
                    bounds = meshletDrawBufferData.meshletObjectReferenceData.boundArray,
                    output = meshletDrawBufferData.modelBufferArray,
                    counter = meshletDrawBufferData.nativeCounter
                };

                mvpJobHandle.jobHandles[i] = job.ScheduleReadOnly(meshletDrawBufferData.meshletObjectReferenceData.TransformAccessArray, 128);
            }
        }


        private void CreateDrawBuffer(RenderGraph renderGraph, MeshletDrawBufferData meshletDrawBufferData) {

            MeshletCacheData meshletCacheData = meshletDrawBufferData.meshletCacheData;
            IList<MeshletObject> meshletObjects = meshletDrawBufferData.meshletObjectReferenceData.meshletObjects;

            // Model Buffer
            if (meshletDrawBufferData.modelBuffer == null || meshletDrawBufferData.modelBuffer.count != meshletObjects.Count) {
                meshletDrawBufferData.modelBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, GraphicsBuffer.UsageFlags.LockBufferForWrite, meshletObjects.Count, SIZEOFMODELBUFFER);
                meshletDrawBufferData.modelBufferArray = meshletDrawBufferData.modelBuffer.LockBufferForWrite<ModelBuffer>(0, meshletObjects.Count);
                for (var i = 0; i < meshletObjects.Count; i++)
                    meshletDrawBufferData.modelBufferArray[i] = new ModelBuffer();
            }

            // Visibility Buffer
            if (meshletDrawBufferData.visibilityBuffer == null || meshletDrawBufferData.visibilityBuffer.count != meshletCacheData.cullData.Count * meshletObjects.Count)
                meshletDrawBufferData.visibilityBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Append, meshletCacheData.cullData.Count * meshletObjects.Count, SIZEOFMESHLETVISIBLE);

            // DrawArgs Buffer
            if (meshletDrawBufferData.drawArgsBuffer == null)
                meshletDrawBufferData.drawArgsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, 5 * sizeof(uint));

            meshletDrawBufferData.material = meshletObjects[0].meshRenderer.sharedMaterial;
        }

        private void SetDrawBuffer(RenderGraph renderGraph, MeshletDrawBufferData meshletDrawBufferData)
        {
            //// Model Buffer
            //meshletDrawBufferData.modelBuffer.SetData(meshletDrawBufferData.modelBufferArray.AsArray());

            // Visibility Buffer
            meshletDrawBufferData.visibilityBuffer.SetCounterValue(0);

            // DrawArgs Buffer
            meshletDrawBufferData.drawArgsBuffer.SetData(ARGS);
        }

        private void ImportDrawBuffer(RenderGraph renderGraph, MeshletDrawBufferData meshletDrawBufferData)
        {
            MeshletCacheData meshletCacheData = meshletDrawBufferData.meshletCacheData;

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
            cmd.DispatchCompute(cullShader, kernel, Mathf.CeilToInt((meshletDrawBufferData.meshletCacheData.cullData.Count * meshletDrawBufferData.nativeCounter.Count) / 64.0f), 1, 1);
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