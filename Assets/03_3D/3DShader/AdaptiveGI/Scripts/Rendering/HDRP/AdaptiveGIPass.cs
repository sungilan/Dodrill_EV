#if ADAPTIVE_GI_HDRP
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace AdaptiveGI.Rendering.HDRP
{
    [System.Serializable]
    public sealed class AdaptiveGIPass : CustomPass
    {
        Material material;

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            name = "AdaptiveGI";
            targetColorBuffer = TargetBuffer.Camera;
            targetDepthBuffer = TargetBuffer.Camera;
            clearFlags = ClearFlag.None;

            Shader applyAdaptiveGIShader = Resources.Load<Shader>("HDRP/ApplyAdaptiveGI");
            if (applyAdaptiveGIShader != null)
            {
                material = new Material(applyAdaptiveGIShader);
            }

            if (injectionPoint != CustomPassInjectionPoint.AfterOpaqueAndSky)
            {
                Debug.LogError("Custom Pass injection point for AdaptiveGI is not set to AfterOpaqueAndSky");
            }
        }

        protected override void Execute(CustomPassContext ctx)
        {
            if (material == null)
            {
                return;
            }
            HDUtils.DrawFullScreen(ctx.cmd, material, ctx.cameraColorBuffer, null, 0);
        }

        protected override void Cleanup()
        {
            CoreUtils.Destroy(material);
        }
    }
}
#endif