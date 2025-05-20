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

    // FrameDebuggerやProfiler用の名前
    private readonly ProfilingSampler _profilingSampler = new ProfilingSampler(RenderTag);

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

    public void Setup(ScriptableRenderer renderer, in RenderingData renderingData)
    {
    }
 
    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
        // This adds a raster render pass to the graph, specifying the name and the data type that will be passed to the ExecutePass function.
        using (var builder = renderGraph.AddRasterRenderPass<WaterRenderPassData>(PassName, out var passData)) {

            //  RenderingDataでなく、ContextContainerから自分で必要なデータを撮るようになった
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            // 特にターゲットを切り替える訳ではなくても、ターゲット設定をしないと怒られます。
            passData.colorHandle = resourceData.activeColorTexture;
            builder.SetRenderAttachment(passData.colorHandle, 0);

            passData.depthHandle = resourceData.activeDepthTexture;
            builder.SetRenderAttachmentDepth(passData.depthHandle);
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
