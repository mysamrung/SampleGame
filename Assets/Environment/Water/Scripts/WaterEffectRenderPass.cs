using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using static WaterRenderPass;

public class WaterEffectRenderPass : ScriptableRenderPass {

    [System.Serializable]
    public class Setting {
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        public int resolution;
        public float fadeRange;
        public float renderRange;
    }

    internal class WaterEffectRenderPassPassData {
        internal RendererListHandle rendererList;
    }

    public class WaterEffectResultData : ContextItem {
        public Vector4 rendererCoords;

        public override void Reset() {
            rendererCoords = Vector4.zero;
        }
    }

    private const string RenderTag = "WaterEffect";
    private const string PassName = "WaterEffect Pass";

    private const string WaterDynamicEffectsBufferName = "_WaterDynamicEffectsBuffer";
    public static readonly int _WaterDynamicEffectsBufferID = Shader.PropertyToID(WaterDynamicEffectsBufferName);
    private const string WaterDynamicEffectsCoordsName = "_WaterDynamicEffectsCoords";
    public static readonly int _WaterDynamicEffectsCoordsID = Shader.PropertyToID(WaterDynamicEffectsCoordsName);

    private readonly RenderQueueRange _renderQueueRange = RenderQueueRange.all;
    private readonly ShaderTagId _shaderTagId = new ShaderTagId(RenderTag);

    private FilteringSettings _filteringSettings;

    private Vector4 rendererCoords;
    private Vector3 centerPosition;

    private Matrix4x4 projection { set; get; }
    private Matrix4x4 view { set; get; }

    private static readonly Quaternion viewRotation = Quaternion.Euler(new Vector3(90f, 0f, 0f));
    private static readonly Vector3 viewScale = new Vector3(1, 1, -1);
    private static Rect viewportRect;

    private static RTHandle renderTarget;
    private Setting setting;

    public WaterEffectRenderPass(Setting setting)
    {
        _filteringSettings = new FilteringSettings(_renderQueueRange);
        renderPassEvent = setting.renderPassEvent;
        this.setting = setting;
    }
    private static Vector3 StabilizeProjection(Vector3 pos, float texelSize)
    {
        float Snap(float coord, float cellSize) => Mathf.FloorToInt(coord / cellSize) * (cellSize) + (cellSize * 0.5f);
        return new Vector3(Snap(pos.x, texelSize), Snap(pos.y, texelSize), Snap(pos.z, texelSize));
    }

    private void SetupProjection(RasterCommandBuffer cmd, Camera camera)
    {
        centerPosition = camera.transform.position + (camera.transform.forward * (setting.renderRange - setting.fadeRange));

        centerPosition = StabilizeProjection(centerPosition, (setting.renderRange * 2f) / setting.resolution);

        var frustumHeight = setting.renderRange * 2f;
        centerPosition += (Vector3.up * frustumHeight * 0.5f);

        projection = Matrix4x4.Ortho(-setting.renderRange, setting.renderRange, -setting.renderRange, setting.renderRange, 0.03f, frustumHeight);

        view = Matrix4x4.TRS(centerPosition, viewRotation, viewScale).inverse;

        cmd.SetViewProjectionMatrices(view, projection);

        viewportRect.width = setting.resolution;
        viewportRect.height = setting.resolution;
        cmd.SetViewport(viewportRect);

        rendererCoords.x = centerPosition.x - setting.renderRange;
        rendererCoords.y = centerPosition.z - setting.renderRange;
        rendererCoords.z = setting.renderRange * 2f;
        rendererCoords.w = 1f; //Enable in shaderx
    }


    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) 
    {
        // This adds a raster render pass to the graph, specifying the name and the data type that will be passed to the ExecutePass function.
        using (var builder = renderGraph.AddRasterRenderPass<WaterEffectRenderPassPassData>(PassName, out var passData)) {

            //  RenderingDataでなく、ContextContainerから自分で必要なデータを撮るようになった
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            WaterEffectResultData waterEffectData = frameData.Create<WaterEffectResultData>();

            // Create a temporary texture and set it as the render target
            RenderTextureDescriptor textureProperties = new RenderTextureDescriptor(setting.resolution, setting.resolution, RenderTextureFormat.ARGBFloat);
            TextureHandle texture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, textureProperties, WaterDynamicEffectsBufferName, false);
            builder.SetRenderAttachment(texture, 0, AccessFlags.Write);
            builder.SetGlobalTextureAfterPass(texture, _WaterDynamicEffectsBufferID);

            builder.AllowPassCulling(false);

            // Render時のソート条件
            SortingCriteria sortingCriteria = SortingCriteria.CommonTransparent;
            DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(_shaderTagId, renderingData, cameraData, lightData, sortingCriteria);

            RendererListParams rendererListParams = new RendererListParams(renderingData.cullResults, drawSettings, _filteringSettings);
            passData.rendererList = renderGraph.CreateRendererList(rendererListParams);

            builder.UseRendererList(passData.rendererList);

            // UnsafePassの実行を関数を設定します(つまり旧来のExecuteで呼び出していたPassの描画周り)
            builder.SetRenderFunc((WaterEffectRenderPassPassData data, RasterGraphContext context) => {
                using (new ProfilingScope(context.cmd, profilingSampler)) {
                    SetupProjection(context.cmd, cameraData.camera);
                    context.cmd.DrawRendererList(data.rendererList);

                    waterEffectData.rendererCoords = rendererCoords;
                }
            });
        }
    }
}
