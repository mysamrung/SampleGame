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
 
    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
        // This adds a raster render pass to the graph, specifying the name and the data type that will be passed to the ExecutePass function.
        using (var builder = renderGraph.AddRasterRenderPass<WaterRenderPassData>(PassName, out var passData)) {

            //  RenderingData�łȂ��AContextContainer���玩���ŕK�v�ȃf�[�^���B��悤�ɂȂ���
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            WaterEffectRenderPass.WaterEffectResultData waterEffectData = frameData.Get<WaterEffectRenderPass.WaterEffectResultData>();

            // ���Ƀ^�[�Q�b�g��؂�ւ����ł͂Ȃ��Ă��A�^�[�Q�b�g�ݒ�����Ȃ��Ɠ{���܂��B
            passData.colorHandle = resourceData.activeColorTexture;
            builder.SetRenderAttachment(passData.colorHandle, 0);

            passData.depthHandle = resourceData.activeDepthTexture;
            builder.SetRenderAttachmentDepth(passData.depthHandle);

            builder.AllowGlobalStateModification(true);

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
                    context.cmd.SetGlobalVector(WaterEffectRenderPass._WaterDynamicEffectsCoordsID, waterEffectData.rendererCoords);
                    context.cmd.DrawRendererList(data.rendererList);
                }
            });
        }
    }
}
