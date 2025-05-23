using UnityEngine;
using UnityEngine.Rendering.Universal;
public class WaterEffectRenderFeature : ScriptableRendererFeature
{

    [SerializeField]
    private WaterEffectRenderPass.Setting _setting = new WaterEffectRenderPass.Setting();
    private WaterEffectRenderPass _waterEffectRenderPass;

    public override void Create()
    {
        this.name = "Water";

        // 描画パスの生成
        _waterEffectRenderPass = new WaterEffectRenderPass(
            _setting
        );
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(_waterEffectRenderPass);
    }
}
