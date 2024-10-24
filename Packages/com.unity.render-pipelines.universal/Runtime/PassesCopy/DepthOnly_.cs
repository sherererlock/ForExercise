using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal.Internal
{
    public class DepthOnlyPass_ : ScriptableRenderPass
    {
        private static readonly ShaderTagId k_ShaderTagID = new ShaderTagId("DepthOnly");
        private RTHandle destination;

        private RenderPassEvent renderPassEvent;
        
        private FilteringSettings m_FilteringSettings;

        private PassData passData;
        
        public DepthOnlyPass_(RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask)
        {
            renderPassEvent = evt;
            m_FilteringSettings = new FilteringSettings(renderQueueRange, layerMask);
            base.profilingSampler = new ProfilingSampler(nameof(DepthOnlyPass_));
            passData = new PassData();
        }

        public void SetUp(RenderTextureDescriptor renderTextureDescriptor, RTHandle destination)
        {
            this.destination = destination;
        }
        
        private class PassData
        {
            internal RendererListHandle rendererList;
        }

        private RendererListParams InitRendererListParams(UniversalRenderingData renderingData, UniversalCameraData cameraData, UniversalLightData lightData)
        {
            SortingCriteria sortingCriteria = cameraData.defaultOpaqueSortFlags;
            DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(k_ShaderTagID, renderingData, cameraData, lightData, sortingCriteria);
            drawingSettings.perObjectData = PerObjectData.None;
            return new RendererListParams(renderingData.cullResults, drawingSettings, m_FilteringSettings);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            ConfigureTarget(destination);
            ConfigureClear(ClearFlag.All, Color.black);
        }

        private static void ExecutePass(RasterCommandBuffer cmd, RendererList rendererList)
        {
            using (new ProfilingScope(cmd, ProfilingSampler.Get(URPProfileId.DepthPrepass)))
            {
                cmd.DrawRendererList(rendererList);
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            ContextContainer frameData = renderingData.frameData;
            UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            RendererListParams rendererListParams = InitRendererListParams(universalRenderingData, cameraData, lightData);
            RendererList rendererList = context.CreateRendererList(ref rendererListParams);
            ExecutePass(CommandBufferHelpers.GetRasterCommandBuffer(renderingData.commandBuffer), rendererList);
        }

        public void Render(RenderGraph renderGraph, ref RenderingData renderingData, ref TextureHandle cameraDepthTexture)
        {
            ContextContainer frameData = renderingData.frameData;
            UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("DepthOnly Prepass",out PassData passData, base.profilingSampler))
            {
                RendererListParams rendererListParams = InitRendererListParams(universalRenderingData, cameraData, lightData);
                passData.rendererList = renderGraph.CreateRendererList(rendererListParams);

                builder.UseRendererList(passData.rendererList);
                
                builder.SetRenderAttachmentDepth(cameraDepthTexture, AccessFlags.Write);
                
                //  TODO RENDERGRAPH: culling? force culling off for testing
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.EnableFoveatedRasterization(cameraData.xr.supportsFoveatedRendering);

                builder.SetRenderFunc((PassData passData, RasterGraphContext context) =>
                {
                    ExecutePass(context.cmd, passData.rendererList);
                });
                
            }
        }
    }
}