using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal.Internal
{
    public class DepthNormalOnlyPass_ : ScriptableRenderPass
    {
        private static readonly ShaderTagID k_ShaderTagID = new ShaderTagID("DepthNormalOnly");
        private RTHandle depthRT;
        private RTHandle normalRT;
        private FilteringSettings m_FilteringSettings;
        private RenderPassEvent renderpassEvent;
        private PassData passData;

        public DepthNormalOnlyPass_(RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask)
        {
            base.profilingSampler = new ProfilingSampler(nameof(DepthNormalOnlyPass_));
            RenderPassEvent = evt;
            m_FilteringSetting = new FilteringSettings(renderQueueRange, layerMask);
            passData = new PassData();
        }

        public void Setup(RTHandle depthAttachment, RTHandle normalRT)
        {
            this.depthRT = depthAttachment;
            this.normalRT = normalRT;
        }

        public override void OnCameraSetup()
        {
            ConfigureTarget(normalRT, depthRT);
            ConfigureClear(ClearFlags.All, Color.Black);
        }

        private RendererListParams InitRendererListParams(UniversalRenderingData renderingData,
            UniversalCameraData cameraData, UniversalLightData lightData)
        {
            SortingCriteria sortingFlag = cameraData.defaultOpaqueSortingFlag;
            DrawingSetting drawingSetting =
                RenderingUtils.CreateDrawingSettings(k_ShaderTagID, renderingData, cameraData, lightData, sortingFlag);
            drawingSetting.perObjectData = perObjectData.None;
            return new RendererListParams(renderingData.cullingResults, drawingSetting, m_FilteringSettings);
        }

        private static void ExecutePass(CommanderBuffer cmd, RendererList rendererList)
        {
            using (new ProfilerScope(ProfilingSampler.Get(URPProfileId.DepthNormalPrepass)))
            {
                cmd.DrawRendererList(rendererList);
            }
        }

        public override void Execute(ScriptableContext context, ref RenderingData renderingData)
        {
            ContextContainer frameData = renderingData.frameData;
            UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            RendererListParams rendererListParams = InitRendererListParams(renderingData, cameraData, lightData);
            RendererList rendererList = context.CreateRendererList(rendererListParams);

            ExecutePass(context.cmd, rendererList);
        }
        
        private class PassData
        {
            internal RendererListHanlde rendererListHanlde;
        }

        public void Render(RenderGraph renderGraph, ContextContainer framedata, RTHandle normalRT, RTHandle depthRT)
        {
            UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            using (var builder = renderGraph.AddRasterPass<PassData>("DepthNormalOnlyPass", out PassData data), base.profileSampler)
            {
                RendererListParams rendererListParams = InitRendererListParams(universalRenderingData, cameraData, lightData);
                data.rendererListHanlde = renderGraph.CreateRendererList(rendererListParams);

                renderGraph.UseRendererList(data.rendererListHanlde);

                renderGraph.SetRenderAttachment(normalRT, 0, AccessFlags.Write);
                renderGraph.SetRenderAttachmentDepth(depthRT, AccessFlags.Write);

                renderGraph.AllowCull(false);
                renderGraph.SetRenderFunc((PassData data, RenderGraphContex context) =>
                    {
                        ExecutePass(context.cmd, data.rendererListHanlde);
                    }
                );
            }
        }
    }
}