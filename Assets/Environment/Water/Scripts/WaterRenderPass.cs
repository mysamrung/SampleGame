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

    // 対象とするRenderQueue
    private readonly RenderQueueRange _renderQueueRange = RenderQueueRange.all;

    // ShaderのTagsでLightModeがこれになっているシェーダのみをレンダリング対象とする
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

            //  RenderingDataでなく、ContextContainerから自分で必要なデータを撮るようになった
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            WaterEffectRenderPass.WaterEffectResultData waterEffectData = frameData.Get<WaterEffectRenderPass.WaterEffectResultData>();

            // 特にターゲットを切り替える訳ではなくても、ターゲット設定をしないと怒られます。
            passData.colorHandle = resourceData.activeColorTexture;
            builder.SetRenderAttachment(passData.colorHandle, 0);

            passData.depthHandle = resourceData.activeDepthTexture;
            builder.SetRenderAttachmentDepth(passData.depthHandle);

            builder.AllowGlobalStateModification(true);

            // Render時のソート条件
            SortingCriteria sortingCriteria = SortingCriteria.CommonTransparent;
            DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(_shaderTagId, renderingData, cameraData, lightData, sortingCriteria);
          
            RendererListParams rendererListParams = new RendererListParams(renderingData.cullResults, drawSettings, _filteringSettings);
            passData.rendererList = renderGraph.CreateRendererList(rendererListParams);

            builder.UseRendererList(passData.rendererList);

            // UnsafePassの実行を関数を設定します(つまり旧来のExecuteで呼び出していたPassの描画周り)
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
