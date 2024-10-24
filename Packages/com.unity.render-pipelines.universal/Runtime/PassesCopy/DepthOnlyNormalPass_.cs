using System;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal.Internal
{
    public class DepthNormalOnlyPass_ : ScriptableRenderPass
    {
        private static readonly ShaderTagId k_ShaderTagID = new ShaderTagId("DepthNormalOnly");
        private static readonly List<ShaderTagId> k_DepthNormals = new List<ShaderTagId> { new ShaderTagId("DepthNormals"), new ShaderTagId("DepthNormalsOnly") };
        private static readonly int s_CameraDepthTextureID = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int s_CameraNormalsTextureID = Shader.PropertyToID("_CameraNormalsTexture");
        
        private RTHandle depthRT;
        private RTHandle normalRT;
        private FilteringSettings m_FilteringSettings;
        private RenderPassEvent renderpassEvent;
        private PassData passData;
        internal List<ShaderTagId> shaderTagIds { get; set; }

        public DepthNormalOnlyPass_(RenderPassEvent evt, RenderQueueRange renderQueueRange, LayerMask layerMask)
        {
            base.profilingSampler = new ProfilingSampler(nameof(DepthNormalOnlyPass_));
            renderpassEvent = evt;
            m_FilteringSettings = new FilteringSettings(renderQueueRange, layerMask);
            passData = new PassData();
            shaderTagIds = k_DepthNormals;
        }

        public void Setup(RTHandle depthAttachment, RTHandle normalRT)
        {
            this.depthRT = depthAttachment;
            this.normalRT = normalRT;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            ConfigureTarget(normalRT, depthRT);
            ConfigureClear(ClearFlag.All, Color.black);
        }

        private RendererListParams InitRendererListParams(UniversalRenderingData renderingData,
            UniversalCameraData cameraData, UniversalLightData lightData)
        {
            SortingCriteria sortingFlag = cameraData.defaultOpaqueSortFlags;
            DrawingSettings drawingSetting =
                RenderingUtils.CreateDrawingSettings(shaderTagIds, renderingData, cameraData, lightData, sortingFlag);
            drawingSetting.perObjectData = PerObjectData.None;
            return new RendererListParams(renderingData.cullResults, drawingSetting, m_FilteringSettings);
        }

        private static void ExecutePass(RasterCommandBuffer cmd, RendererList rendererList)
        {
            using (new ProfilingScope(ProfilingSampler.Get(URPProfileId.DrawDepthNormalPrepass)))
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
        
        private class PassData
        {
            internal RendererListHandle rendererListHanlde;
        }

        public void Render(RenderGraph renderGraph, ContextContainer framedata, TextureHandle normalRT, TextureHandle depthRT, bool setGlobalDepth, bool setGlobalNormal)
        {
            UniversalRenderingData universalRenderingData = framedata.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = framedata.Get<UniversalCameraData>();
            UniversalLightData lightData = framedata.Get<UniversalLightData>();

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("DepthNormalOnlyPass", out PassData data, base.profilingSampler))
            {
                RendererListParams rendererListParams = InitRendererListParams(universalRenderingData, cameraData, lightData);
                data.rendererListHanlde = renderGraph.CreateRendererList(rendererListParams);

                builder.UseRendererList(data.rendererListHanlde);

                builder.SetRenderAttachment(normalRT, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(depthRT, AccessFlags.Write);
                
                if(setGlobalDepth)
                    builder.SetGlobalTextureAfterPass(depthRT, s_CameraDepthTextureID);
                
                if(setGlobalNormal)
                    builder.SetGlobalTextureAfterPass(normalRT, s_CameraNormalsTextureID);

                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    {
                        ExecutePass(context.cmd, data.rendererListHanlde);
                    }
                );
            }
        }
    }
}