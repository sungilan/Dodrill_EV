#if ADAPTIVE_GI_URP && UNITY_6000_0_OR_NEWER
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace AdaptiveGI.Rendering.URP
{
    public class AdaptiveGIRendererFeature : ScriptableRendererFeature
    {
        [Tooltip("Displays only the GI in the scene.")]
        [SerializeField] private bool debugGIOnly = false;

        private Material renderMaterial;
        private AdaptiveGIPass adaptiveGIPass;
        private const RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        private const ScriptableRenderPassInput requirements = ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal;

        private static readonly int PROPERTY_ADAPTIVEGI_RENDERER_FEATURE = Shader.PropertyToID("_AdaptiveGI_Renderer_Feature");
        private static readonly int PROPERTY_GBUFFER_0 = Shader.PropertyToID("_GBuffer0");
        private static readonly int PROPERTY_GBUFFER_0_ARRAY = Shader.PropertyToID("_GBuffer0Array");
        private static readonly int PROPERTY_GBUFFER_1 = Shader.PropertyToID("_GBuffer1");
        private static readonly int PROPERTY_GBUFFER_1_ARRAY = Shader.PropertyToID("_GBuffer1Array");
        private static readonly int PROPERTY_GBUFFER_2 = Shader.PropertyToID("_GBuffer2");
        private static readonly int PROPERTY_GBUFFER_2_ARRAY = Shader.PropertyToID("_GBuffer2Array");
        private const string KEYWORD_ADAPTIVEGI_RENDERER_FEATURE = "ADAPTIVEGI_RENDERER_FEATURE";
        private const string KEYWORD_GBUFFER_ARRAY = "GBUFFER_ARRAY";

        public override void Create()
        {
            Dispose(true);
            adaptiveGIPass = new AdaptiveGIPass(debugGIOnly)
            {
                renderPassEvent = renderPassEvent
            };
            SetGlobalStatus(isActive);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType == CameraType.Preview || UniversalRenderer.IsOffscreenDepthTexture(ref renderingData.cameraData))
            {
                return;
            }

            if (adaptiveGIPass == null)
            {
                return;
            }

            if (adaptiveGIPass.material == null)
            {
                Shader renderShader = debugGIOnly ? Resources.Load<Shader>("URP/RenderDebugAdaptiveGIOnly") : Resources.Load<Shader>("URP/RenderAdaptiveGI");
                renderMaterial = new Material(renderShader);
                adaptiveGIPass.material = renderMaterial;
            }

            adaptiveGIPass.ConfigureInput(requirements);
            renderer.EnqueuePass(adaptiveGIPass);
        }

        protected override void Dispose(bool disposing)
        {
            if (renderMaterial != null)
            {
                DestroyImmediate(renderMaterial);
            }
            SetGlobalStatus(false);
        }

        private void SetGlobalStatus(bool active)
        {
            if (active)
            {
                Shader.SetGlobalFloat(PROPERTY_ADAPTIVEGI_RENDERER_FEATURE, 1);
                Shader.EnableKeyword(KEYWORD_ADAPTIVEGI_RENDERER_FEATURE);
            }
            else
            {
                Shader.SetGlobalFloat(PROPERTY_ADAPTIVEGI_RENDERER_FEATURE, 0);
                Shader.DisableKeyword(KEYWORD_ADAPTIVEGI_RENDERER_FEATURE);
            }
        }

        public class AdaptiveGIPass : ScriptableRenderPass
        {
            public Material material;
            private readonly bool debugGIOnly;

            public AdaptiveGIPass(bool debugGIOnly)
            {
                this.debugGIOnly = debugGIOnly;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                TextureHandle gBuffer0 = debugGIOnly ? TextureHandle.nullHandle : resourceData.gBuffer[0];
                TextureHandle gBuffer1 = debugGIOnly ? TextureHandle.nullHandle : resourceData.gBuffer[1];
                TextureHandle gBuffer2 = debugGIOnly ? TextureHandle.nullHandle : resourceData.gBuffer[2];
                TextureHandle destination = resourceData.activeColorTexture;

                if (!debugGIOnly && !gBuffer0.IsValid())
                {
                    Debug.LogError("The Adaptive GI Renderer Feature only works with the deferred rendering path. Change the active renderer to use deferred rendering, or remove this renderer feature and change your materials to use compatible shaders. See the documentation for more information.");
                    return;
                }

                // The following line ensures that the render pass doesn't blit
                // from the back buffer.
                if (resourceData.isActiveTargetBackBuffer)
                {
                    return;
                }

                // This check is to avoid an error from the material preview in the scene
                if (!destination.IsValid())
                {
                    return;
                }

                using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<MainPassData>(passName, out MainPassData passData, profilingSampler);
                passData.material = material;
                passData.passIndex = 0;

                passData.gBuffer0 = gBuffer0;
                passData.gBuffer1 = gBuffer1;
                passData.gBuffer2 = gBuffer2;

                if (!debugGIOnly)
                {
                    builder.UseTexture(passData.gBuffer0, AccessFlags.Read);
                    builder.UseTexture(passData.gBuffer1, AccessFlags.Read);
                    builder.UseTexture(passData.gBuffer2, AccessFlags.Read);
                }

                Debug.Assert(resourceData.cameraDepthTexture.IsValid());
                builder.UseTexture(resourceData.cameraDepthTexture);

                Debug.Assert(resourceData.cameraNormalsTexture.IsValid());
                builder.UseTexture(resourceData.cameraNormalsTexture);

                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);

                builder.SetRenderFunc((MainPassData data, RasterGraphContext rgContext) =>
                {
                    ExecuteMainPass(rgContext.cmd, data.gBuffer0, data.gBuffer1, data.gBuffer2, data.material, data.passIndex);
                });
            }

            private static void ExecuteMainPass(RasterCommandBuffer cmd, RTHandle gBuffer0, RTHandle gBuffer1, RTHandle gBuffer2, Material material, int passIndex)
            {
                if (gBuffer0 != null)
                {
                    if (gBuffer0.rt.dimension == TextureDimension.Tex2D)
                    {
                        material.SetTexture(PROPERTY_GBUFFER_0, gBuffer0);
                        material.SetTexture(PROPERTY_GBUFFER_1, gBuffer1);
                        material.SetTexture(PROPERTY_GBUFFER_2, gBuffer2);
                        material.DisableKeyword(KEYWORD_GBUFFER_ARRAY);
                    }
                    else if (gBuffer0.rt.dimension == TextureDimension.Tex2DArray)
                    {
                        material.SetTexture(PROPERTY_GBUFFER_0_ARRAY, gBuffer0);
                        material.SetTexture(PROPERTY_GBUFFER_1_ARRAY, gBuffer1);
                        material.SetTexture(PROPERTY_GBUFFER_2_ARRAY, gBuffer2);
                        material.EnableKeyword(KEYWORD_GBUFFER_ARRAY);
                    }
                }

                cmd.DrawProcedural(Matrix4x4.identity, material, passIndex, MeshTopology.Triangles, 3, 1);
            }

            private class MainPassData
            {
                internal Material material;
                internal int passIndex;
                internal TextureHandle gBuffer0;
                internal TextureHandle gBuffer1;
                internal TextureHandle gBuffer2;
            }
        }
    }
}
#endif