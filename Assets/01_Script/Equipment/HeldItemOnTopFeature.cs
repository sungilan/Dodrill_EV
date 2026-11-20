using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// PC·모바일 1인칭: 손에 든 도구와 내 장갑을 화면 맨 앞에 다시 그린다.
///
/// 문제: 도구는 카메라 앞(HoldPoint)에 붙어 다녀서 차체 밑·벽에 가까이 대면 도구가 차체를 뚫고 들어가 잘려 보였다.
/// 해결: 일반 렌더링이 끝난 뒤(투명 물체까지) 깊이만 지우고, 표시(HeldItemOnTop.Bit)가 붙은 렌더러만 한 번 더 그린다.
///       물체의 실제 위치·충돌·레이어는 그대로라 볼트 감지 같은 판정에는 영향이 없고, 다른 사람 화면도 그대로다.
///
/// 표시는 GameObject 레이어가 아니라 Renderer.renderingLayerMask 의 한 비트로 한다(레이어를 바꾸면 물리·레이캐스트가 달라진다).
/// 사용: 렌더러 에셋(DoDrill_Renderer 등)의 Renderer Features 에 이 기능을 추가.
/// </summary>
public class HeldItemOnTopFeature : ScriptableRendererFeature
{
    [Tooltip("언제 다시 그릴지. 투명 물체 뒤, 후처리 앞이 기본.")]
    public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingTransparents;

    private DrawOnTopPass _pass;
    private XRayPass _xrayPass;

    /// <summary>런타임 안내(링·라벨)가 쓰는 레이어 — VR 카메라가 그리는 TransparentFX.</summary>
    private const int GuideLayer = 1;

    public override void Create()
    {
        _pass = new DrawOnTopPass { renderPassEvent = passEvent };
        // 엑스레이는 든 도구보다 먼저(뒤에) 그려, 손에 든 도구·손이 엑스레이 빛 위에 보이게 한다
        _xrayPass = new XRayPass { renderPassEvent = passEvent - 1 };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        bool held = HeldItemOnTop.Any;
        bool xray = XRayMode.HasTargets && XRayMode.OverrideMaterial != null;
        if (!held && !xray) return;
        var cam = renderingData.cameraData.camera;
        // 내 1인칭 화면(메인 카메라)만. 미니맵·UI·씬 뷰 카메라에서는 그리지 않는다.
        if (renderingData.cameraData.cameraType != CameraType.Game || cam == null || !cam.CompareTag("MainCamera")) return;
        if (xray) renderer.EnqueuePass(_xrayPass);
        if (held) renderer.EnqueuePass(_pass);
    }

    private class DrawOnTopPass : ScriptableRenderPass
    {
        private static readonly List<ShaderTagId> s_tags = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
        };

        private class PassData
        {
            public RendererListHandle list;
            public RendererListHandle uiList;
            public RendererListHandle guideList;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();

            var drawing = RenderingUtils.CreateDrawingSettings(s_tags, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
            var filtering = new FilteringSettings(RenderQueueRange.opaque, ~0, HeldItemOnTop.Bit);
            var listParams = new RendererListParams(renderingData.cullResults, drawing, filtering);

            // 도구에 붙은 월드 UI(멀티미터·메거 LCD 등). Canvas 에는 renderingLayerMask 가 없어 전용 레이어로 구분한다.
            // 이것을 같이 그리지 않으면 다시 그린 본체가 LCD 글자를 덮어 화면에 값이 안 보인다.
            var uiDrawing = RenderingUtils.CreateDrawingSettings(s_tags, renderingData, cameraData, lightData, SortingCriteria.CommonTransparent);
            var uiFiltering = new FilteringSettings(RenderQueueRange.transparent, 1 << HeldItemOnTop.UILayer);
            var uiParams = new RendererListParams(renderingData.cullResults, uiDrawing, uiFiltering);

            // 안내 링·라벨(TransparentFX 레이어, 큐 3500 이상, 깊이 무시)을 다시 그린 도구·손 위에 한 번 더 그린다.
            // 이것이 없으면 VR 에서 메거를 들고 있을 때 맨 앞에 다시 그린 메거·손이 단자 안내 라벨을 덮었다.
            var guideFiltering = new FilteringSettings(new RenderQueueRange(3500, 5000), 1 << GuideLayer);
            var guideParams = new RendererListParams(renderingData.cullResults, uiDrawing, guideFiltering);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Held Item On Top", out var data))
            {
                data.list = renderGraph.CreateRendererList(listParams);
                data.uiList = renderGraph.CreateRendererList(uiParams);
                data.guideList = renderGraph.CreateRendererList(guideParams);
                builder.UseRendererList(data.list);
                builder.UseRendererList(data.uiList);
                builder.UseRendererList(data.guideList);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                {
                    // 차체·벽의 깊이를 지우고 도구·장갑만 다시 그린다(도구끼리는 깊이로 정상 가림)
                    ctx.cmd.ClearRenderTarget(RTClearFlags.Depth, Color.clear, 1f, 0);
                    ctx.cmd.DrawRendererList(d.list);
                    ctx.cmd.DrawRendererList(d.uiList);
                    ctx.cmd.DrawRendererList(d.guideList);
                });
            }
        }
    }
}

/// <summary>
/// 엑스레이: 표시(XRayMode.Bit)가 붙은 렌더러를 깊이를 무시하고 하늘색 반투명 재질로 덮어 그린다(차체 너머로 비침).
/// 그 위에 안내 링·라벨을 한 번 더 그려 엑스레이 빛에 묻히지 않게 한다.
/// </summary>
class XRayPass : ScriptableRenderPass
{
    private static readonly List<ShaderTagId> s_tags = new List<ShaderTagId>
    {
        new ShaderTagId("UniversalForward"),
        new ShaderTagId("UniversalForwardOnly"),
        new ShaderTagId("SRPDefaultUnlit"),
    };

    private class PassData
    {
        public RendererListHandle list;
        public RendererListHandle guideList;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        var mat = XRayMode.OverrideMaterial;
        if (mat == null) return;
        var resources = frameData.Get<UniversalResourceData>();
        var renderingData = frameData.Get<UniversalRenderingData>();
        var cameraData = frameData.Get<UniversalCameraData>();
        var lightData = frameData.Get<UniversalLightData>();

        var drawing = RenderingUtils.CreateDrawingSettings(s_tags, renderingData, cameraData, lightData, SortingCriteria.CommonTransparent);
        drawing.overrideMaterial = mat;
        drawing.overrideMaterialPassIndex = 0;
        var filtering = new FilteringSettings(RenderQueueRange.all, ~0, XRayMode.Bit);
        var listParams = new RendererListParams(renderingData.cullResults, drawing, filtering);

        var guideDrawing = RenderingUtils.CreateDrawingSettings(s_tags, renderingData, cameraData, lightData, SortingCriteria.CommonTransparent);
        var guideFiltering = new FilteringSettings(new RenderQueueRange(3500, 5000), 1 << 1);
        var guideParams = new RendererListParams(renderingData.cullResults, guideDrawing, guideFiltering);

        using (var builder = renderGraph.AddRasterRenderPass<PassData>("XRay Targets", out var data))
        {
            data.list = renderGraph.CreateRendererList(listParams);
            data.guideList = renderGraph.CreateRendererList(guideParams);
            builder.UseRendererList(data.list);
            builder.UseRendererList(data.guideList);
            builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
            builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
            {
                // 차체 깊이를 지우고 대상만 덮어 그린다(반투명이라 보이는 부분은 살짝 물들고, 가린 부분은 비쳐 보인다)
                ctx.cmd.ClearRenderTarget(RTClearFlags.Depth, Color.clear, 1f, 0);
                ctx.cmd.DrawRendererList(d.list);
                ctx.cmd.DrawRendererList(d.guideList);
            });
        }
    }
}
