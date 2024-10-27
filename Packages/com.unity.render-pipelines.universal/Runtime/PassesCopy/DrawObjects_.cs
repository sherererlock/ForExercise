using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal.Internal
{
    public class DrawObjectsPass_ : ScriptableRenderPass
    {
        private RenderPassEvent renderPassEvent;
        private FilteringSettings m_FilteringSettings;
        private RenderStateBlock m_RenderStateBlock;
        private bool m_IsOpaque;
        private PassData m_PassData;
        private List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>();


        private static readonly int s_DrawObjectsPassData = Shader.PropertyToID("_DrawObjectsPassData");
        
        public DrawObjectsPass_(RenderPassEvent evt, bool isOpaque, bool useDetphPriming, RenderQueueRange renderQueueRange, LayerMask layerMask, StencilState stencilState, int reference)
        {
            this.renderPassEvent = evt;
            m_FilteringSettings = new FilteringSettings(renderQueueRange, layerMask);
            m_IsOpaque = isOpaque;
            m_PassData = new PassData();

            m_RenderStateBlock = new RenderStateBlock();
            if (stencilState.enabled)
            {
                m_RenderStateBlock.mask |= RenderStateMask.Stencil;
                m_RenderStateBlock.stencilState = stencilState;
            }

            if (useDetphPriming)
            {
                m_RenderStateBlock.mask |= RenderStateMask.Depth;
                DepthState depthState = new DepthState();
                depthState.compareFunction = CompareFunction.Equal;
                depthState.writeEnabled = false;
                m_RenderStateBlock.depthState = depthState;
            }
        }

        internal static void ExecutePass(RasterCommandBuffer cmd, PassData passData)
        {
            Vector4 drawObjectData = new Vector4(0.0f, 0.0f, 0.0f, passData.isOpaque ? 1.0f : 0.0f);
            cmd.SetGlobalVector(s_DrawObjectsPassData, drawObjectData);

            float flipSign = passData.yflip ? 1.0f : 0.0f;
            Vector4 scaleBias = new Vector4(flipSign, 0.0, passData.yflip ? -1.0f : 1.0f, 0.0);
            cmd.SetGlobalVector(ShaderPropertyId.scaleBias, scaleBias);
            
            RenderingUtils.DrawRendererListObjectsWithError(cmd, ref passData.errorRendererList);
            cmd.DrawRendererList(passData.rendererList);
        }

        internal void InitPassData(bool yflip, bool isopaque)
        {
            m_PassData.isOpaque = isopaque;
            m_PassData.yflip = yflip;
        }

        internal RendererListParams InitRendererList(UniversalRenderingData universalRenderingData, UniversalCameraData universalCameraData, UniversalLightData universalLightData,
            ref PassData passData, RenderGraph renderGraph, ScriptableRenderContext context, bool useRenderGraph)
        {
            
            SortingCriteria sortingFlag = m_IsOpaque ? SortingCriteria.CommonOpaque : SortingCriteria.CommonTransparent;
            
            DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(m_ShaderTagIdList,
                universalRenderingData, universalCameraData, universalLightData, sortingFlag);
            

            NativeArray<RenderStateBlock> stateBlocks = new NativeArray<RenderStateBlock>(1, Allocator.Temp);
            stateBlocks[0] = m_RenderStateBlock;
            RendererListParams rendererListParams = new RendererListParams(universalRenderingData.cullResults, drawingSettings, m_FilteringSettings)
            {
                stateBlocks = stateBlocks,
            };
            
            return rendererListParams;
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            InitPassData(false, m_IsOpaque);
            

            ExecutePass(CommandBufferHelpers.GetRasterCommandBuffer(renderingData.commandBuffer), m_PassData);
        }
        
        internal class PassData
        {
            internal TextureHandle depthHdl;
            internal TextureHandle colorHdl;

            internal bool isOpaque;
            internal bool yflip;

            internal RendererListHandle rendererListHandle;
            internal RendererListHandle errorRendererListHandle;

            
            internal RendererList rendererList;
            internal RendererList errorRendererList;
        }
        
    }
}