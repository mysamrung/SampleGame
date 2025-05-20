using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class WaterRenderPass : ScriptableRenderPass
{
    internal class WaterRenderPassData {
        internal TextureHandle colorHandle;
        internal TextureHandle depthHandle;
        internal RendererListHandle rendererList;
    }

    private const string RenderTag = "Water";
    private const string PassName = "Water Pass";

    // FrameDebugger��Profiler�p�̖��O
    private readonly ProfilingSampler _profilingSampler = new ProfilingSampler(RenderTag);

    // �ΏۂƂ���RenderQueue
    private readonly RenderQueueRange _renderQueueRange = RenderQueueRange.all;

    // Shader��Tags��LightMode������ɂȂ��Ă���V�F�[�_�݂̂������_�����O�ΏۂƂ���
    private readonly ShaderTagId _shaderTagId = new ShaderTagId(RenderTag);

    private FilteringSettings _filteringSettings;

    public WaterRenderPass(RenderPassEvent renderPassEvent)
    {
        _filteringSettings = new FilteringSettings(_renderQueueRange);
        this.renderPassEvent = renderPassEvent;
    }

    public void Setup(ScriptableRenderer renderer, in RenderingData renderingData)
    {
    }
 
    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
        // This adds a raster render pass to the graph, specifying the name and the data type that will be passed to the ExecutePass function.
        using (var builder = renderGraph.AddRasterRenderPass<WaterRenderPassData>(PassName, out var passData)) {

            //  RenderingData�łȂ��AContextContainer���玩���ŕK�v�ȃf�[�^���B��悤�ɂȂ���
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            // ���Ƀ^�[�Q�b�g��؂�ւ����ł͂Ȃ��Ă��A�^�[�Q�b�g�ݒ�����Ȃ��Ɠ{���܂��B
            passData.colorHandle = resourceData.activeColorTexture;
            builder.SetRenderAttachment(passData.colorHandle, 0);

            passData.depthHandle = resourceData.activeDepthTexture;
            builder.SetRenderAttachmentDepth(passData.depthHandle);
            // Render���̃\�[�g����
            SortingCriteria sortingCriteria = SortingCriteria.CommonTransparent;
            DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(_shaderTagId, renderingData, cameraData, lightData, sortingCriteria);
          
            RendererListParams rendererListParams = new RendererListParams(renderingData.cullResults, drawSettings, _filteringSettings);
            passData.rendererList = renderGraph.CreateRendererList(rendererListParams);

            builder.UseRendererList(passData.rendererList);

            // UnsafePass�̎��s���֐���ݒ肵�܂�(�܂苌����Execute�ŌĂяo���Ă���Pass�̕`�����)
            builder.SetRenderFunc((WaterRenderPassData data, RasterGraphContext context) =>
            {
                using (new ProfilingScope(context.cmd, profilingSampler)) {
                    context.cmd.DrawRendererList(data.rendererList);
                }
            });
        }


        //    var cmd = CommandBufferPool.Get(RenderTag);

        //using (new ProfilingScope(cmd, _profilingSampler))
        //{
        //    context.ExecuteCommandBuffer(cmd);
        //    cmd.Clear();
        //    WaterEffectRenderPass.SetGlobalParameter(cmd);

        //    var drawingSettings = CreateDrawingSettings(_shaderTagId, ref renderingData, SortingCriteria.CommonTransparent);
        //    var rendererListParams = new RendererListParams(renderingData.cullResults, drawingSettings, _filteringSettings);
        //    var rendererList = context.CreateRendererList(ref rendererListParams);
        //    cmd.DrawRendererList(rendererList);
        //}

        //context.ExecuteCommandBuffer(cmd);
        //CommandBufferPool.Release(cmd);
    }
}
