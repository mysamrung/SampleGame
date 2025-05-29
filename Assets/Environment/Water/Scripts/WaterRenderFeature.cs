using UnityEngine;
using UnityEngine.Rendering.Universal;
public class WaterRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Setting
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    [SerializeField]
    private Setting _setting = new Setting();

    private WaterRenderPass _waterRenderPass;

    public override void Create()
    {
        this.name = "Water";

        // •`‰æƒpƒX‚Ì¶¬
        _waterRenderPass = new WaterRenderPass(
            _setting.renderPassEvent
        );
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(_waterRenderPass);
    }
}
