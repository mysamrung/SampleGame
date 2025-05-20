using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class WaterEffectRenderPass : ScriptableRenderPass
{
    private const string RenderTag = "WaterEffect";
    
    private const string WaterDynamicEffectsBufferName = "_WaterDynamicEffectsBuffer";
    private static readonly int _WaterDynamicEffectsBufferID = Shader.PropertyToID(WaterDynamicEffectsBufferName);
    private const string WaterDynamicEffectsCoordsName = "_WaterDynamicEffectsCoords";
    private static readonly int _WaterDynamicEffectsCoordsID = Shader.PropertyToID(WaterDynamicEffectsCoordsName);

    private readonly ProfilingSampler _profilingSampler = new ProfilingSampler(RenderTag);

    private readonly RenderPassEvent _renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    private readonly RenderQueueRange _renderQueueRange = RenderQueueRange.all;
    private readonly ShaderTagId _shaderTagId = new ShaderTagId(RenderTag);

    private FilteringSettings _filteringSettings;

    public static Vector4 rendererCoords;

    private static Vector3 centerPosition;
    private static int CurrentResolution;
    private static float orthoSize;

    // Setting
    private float fadeRange;
    private float renderRange;
    private int resolution;

    private static Matrix4x4 projection { set; get; }
    private static Matrix4x4 view { set; get; }

    private static Color m_clearColor = new Color(0.5f, 0.5f, 1.0f, 0);
    private static readonly Quaternion viewRotation = Quaternion.Euler(new Vector3(90f, 0f, 0f));
    private static readonly Vector3 viewScale = new Vector3(1, 1, -1);
    private static Rect viewportRect;
    private static Vector4 renderCoord;

    private static RTHandle renderTarget;

    public WaterEffectRenderPass(RenderPassEvent renderPassEvent)
    {
        _filteringSettings = new FilteringSettings(_renderQueueRange);
        this.renderPassEvent = renderPassEvent;
    }

    public void Setup(ScriptableRenderer renderer, in RenderingData renderingData, WaterEffectRenderFeature.Setting setting)
    {
        resolution = setting.resolution;
        fadeRange = setting.fadeRange;
        renderRange = setting.renderRange;
    }

    private static Vector3 StabilizeProjection(Vector3 pos, float texelSize)
    {
        float Snap(float coord, float cellSize) => Mathf.FloorToInt(coord / cellSize) * (cellSize) + (cellSize * 0.5f);

        return new Vector3(Snap(pos.x, texelSize), Snap(pos.y, texelSize), Snap(pos.z, texelSize));
    }
    private void SetupProjection(CommandBuffer cmd, Camera camera)
    {
        centerPosition = camera.transform.position + (camera.transform.forward * (orthoSize - fadeRange));
        Debug.Log(centerPosition);

        centerPosition = StabilizeProjection(centerPosition, (orthoSize * 2f) / resolution);

        var frustumHeight = orthoSize * 2f;
        centerPosition += (Vector3.up * frustumHeight * 0.5f);

        projection = Matrix4x4.Ortho(-orthoSize, orthoSize, -orthoSize, orthoSize, 0.03f, frustumHeight);

        view = Matrix4x4.TRS(centerPosition, viewRotation, viewScale).inverse;

        cmd.SetViewProjectionMatrices(view, projection);

        viewportRect.width = resolution;
        viewportRect.height = resolution;
        cmd.SetViewport(viewportRect);

        rendererCoords.x = centerPosition.x - orthoSize;
        rendererCoords.y = centerPosition.z - orthoSize;
        rendererCoords.z = orthoSize * 2f;
        rendererCoords.w = 1f; //Enable in shader
    }
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cmd = CommandBufferPool.Get(RenderTag);
        orthoSize = renderRange * 0.5f;

        if (resolution != CurrentResolution || renderTarget == null)
        {
            RTHandles.Release(renderTarget);
            renderTarget = RTHandles.Alloc(resolution, resolution, 1, DepthBits.None,
                GraphicsFormat.R8G8B8A8_UNorm,
                filterMode: FilterMode.Bilinear,
                wrapMode: TextureWrapMode.Clamp,
                useMipMap: true, //TODO: Expose option
                autoGenerateMips: true,
                useDynamicScale : true,
                enableRandomWrite: true,
                name: WaterDynamicEffectsBufferName);

            CurrentResolution = resolution;
        }


        using (new ProfilingScope(cmd, _profilingSampler))
        {
            ref CameraData cameraData = ref renderingData.cameraData;

            SetupProjection(cmd, cameraData.camera);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            cmd.SetRenderTarget(renderTarget);
            cmd.ClearRenderTarget(true, true, m_clearColor);

            var drawingSettings = CreateDrawingSettings(_shaderTagId, ref renderingData, SortingCriteria.CommonTransparent);
            var rendererListParams = new RendererListParams(renderingData.cullResults, drawingSettings, _filteringSettings);
            var rendererList = context.CreateRendererList(ref rendererListParams);
            cmd.DrawRendererList(rendererList);
        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);

    }

    public static void SetGlobalParameter(CommandBuffer commandBuffer)
    {
        commandBuffer.SetGlobalTexture(_WaterDynamicEffectsBufferID, renderTarget);
        commandBuffer.SetGlobalVector(_WaterDynamicEffectsCoordsID, rendererCoords);
    }
}
