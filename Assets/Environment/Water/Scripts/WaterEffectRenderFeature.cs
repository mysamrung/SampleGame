using UnityEngine;
using UnityEngine.Rendering.Universal;
public class WaterEffectRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Setting
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        public int resolution;

        public float fadeRange;

        public float renderRange;
    }

    [SerializeField]
    private Setting _setting = new Setting();

    private WaterEffectRenderPass _waterEffectRenderPass;

    public override void Create()
    {
        this.name = "Water";

        // •`‰æƒpƒX‚Ì¶¬
        _waterEffectRenderPass = new WaterEffectRenderPass(
            _setting.renderPassEvent
        );
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(_waterEffectRenderPass);
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        _waterEffectRenderPass.Setup(renderer, renderingData, _setting);
    }
}
