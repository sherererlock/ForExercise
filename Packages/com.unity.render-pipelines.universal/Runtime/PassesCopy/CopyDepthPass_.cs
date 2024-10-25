using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal.Internal
{
    public class CopyDepthPass_ : ScriptableRenderPass
    {
        private RTHandle destination;
        private RTHandle source;
        private bool copyToBackBuffer;
        private bool copyToDepth;
        private int msaaSample = -1;
        private Material copyToDepthMaterial;
        private PassData passData;
        private RenderPassEvent renderPassEvent;

        static class ShaderConsts
        {
            public static readonly int _CameraDepthAttachment = Shader.PropertyToID("_CameraDepthAttachment");
            public static readonly int _CameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
            public static readonly int _ZWriteShaderHandle = Shader.PropertyToID("_ZWrite");
        }
        
        public CopyDepthPass_(RenderPassEvent evt, Shader copyToDepthShader, bool copyToDepth, bool copyToBackbuffer, int msaaSample)
        {
            renderPassEvent = evt;
            copyToDepthMaterial = CoreUtils.CreateEngineMaterial(copyToDepthShader);
            this.copyToDepth = copyToDepth;
            this.copyToBackBuffer = copyToBackbuffer;
            this.msaaSample = msaaSample;
        }

        public void Setup(RTHandle destination, RTHandle source)
        {
            this.destination = destination;
            this.source = source;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            ConfigureTarget(destination);
            ConfigureClear(ClearFlag.All, Color.black);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            passData.copyDepthMaterial = copyToDepthMaterial;
            passData.msaaSample = msaaSample;
            passData.destination = destination;
            passData.source = source;
            passData.copyToDepth = copyToDepth;
            passData.copyToBackBuffer = copyToBackBuffer;
            passData.cameraData = renderingData.frameData.Get<UniversalCameraData>();

            RasterCommandBuffer cmd = CommandBufferHelpers.GetRasterCommandBuffer(renderingData.commandBuffe);
            ExecutePass(cmd, passData);
        }

        public override void Dispose()
        {
            CoreUtils.Destroy(copyToDepthMaterial);
        }

        private static void ExecutePass(RasterCommandBuffer cmd, PassData data)
        {
            using (new ProfilingScope(ProfilingSampler.Get(URPProfileId.CopyDepth)))
            {
                int cameraSample = -1;
                if (data.msaaSample == -1)
                {
                    cameraSample = data.source.rt.antiAliasing;
                }
                else
                {
                    cameraSample = data.msaaSample;
                }
                
                switch (cameraSample)
                {
                    case 8:
                        cmd.SetKeyword(ShaderGlobalKeywords.DepthMsaa2, false);
                        cmd.SetKeyword(ShaderGlobalKeywords.DepthMsaa4, false);
                        cmd.SetKeyword(ShaderGlobalKeywords.DepthMsaa8, true);
                        break;
                    case 4:
                        cmd.SetKeyword(ShaderGlobalKeywords.DepthMsaa2, false);
                        cmd.SetKeyword(ShaderGlobalKeywords.DepthMsaa4, true);
                        cmd.SetKeyword(ShaderGlobalKeywords.DepthMsaa8, false);
                        break;
                    case 2:
                        cmd.SetKeyword(ShaderGlobalKeywords.DepthMsaa2, false);
                        cmd.SetKeyword(ShaderGlobalKeywords.DepthMsaa4, false);
                        cmd.SetKeyword(ShaderGlobalKeywords.DepthMsaa8, true);
                        break;
                    default:
                        cmd.SetKeyword(ShaderGlobalKeywords.DepthMsaa2, false);
                        cmd.SetKeyword(ShaderGlobalKeywords.DepthMsaa4, false);
                        cmd.SetKeyword(ShaderGlobalKeywords.DepthMsaa8, false);
                        break;
                }
                
                cmd.SetKeyword(ShaderGlobalKeywords._OUTPUT_DEPTH, data.copyToDepth);
                
                bool yflip = data.cameraData.IsHandleYFlipped(data.source) && data.copyToBackBuffer;

                Vector2 viewPortScale = data.source.useScaling
                    ? new Vector2(data.source.rtHandleProperties.rtHandleScale.x,
                        data.source.rtHandleProperties.rtHandleScale.y)
                    : Vector2.one;
                Vector4 scaleBias = yflip
                    ? new Vector4(viewPortScale.x, -viewPortScale.y, 0, viewPortScale.y)
                    : new Vector4(viewPortScale.x, -viewPortScale.y, 0, viewPortScale.y);

                if(data.copyToBackBuffer)
                    cmd.SetViewport(data.cameraData.pixelRect);

                data.copyDepthMaterial.SetTexture(ShaderConsts._CameraDepthAttachment, data.source);
                data.copyDepthMaterial.SetFloat(ShaderConsts._ZWriteShaderHandle, data.copyToDepth ? 1 : 0);
                Blitter.BlitTexture(cmd, data.source, scaleBias, data.copyDepthMaterial, 0);
            }
        }

        internal class PassData
        {
            internal Material copyDepthMaterial;
            internal int msaaSample;
            internal RTHandle source;
            internal bool copyToDepth;
            internal bool copyToBackBuffer;
            internal UniversalCameraData cameraData;
        }

        public void Render(RenderGraph renderGraph, ContextContainer frameData, TextureHandle destination, TextureHandle source, bool bindAsCameraDepth, string passname = "copy detph")
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            using (var builder =
                   renderGraph.AddRasterRenderPass<PassData>("Copy Depth Pass", out PassData data,
                       base.profilingSampler))
            {
                data.copyDepthMaterial = copyToDepthMaterial;
                data.msaaSample = msaaSample;
                data.source = source;
                data.copyToDepth = copyToDepth;
                data.cameraData = cameraData;
                data.copyToBackBuffer = copyToBackBuffer;
                
                builder.UseTexture(source, AccessFlags.Read);
                if (copyToDepth)
                {
                    builder.SetRenderAttachmentDepth(destination, AccessFlags.Write);
#if UNITY_EDITOR
                    // binding a dummy color target as a workaround to an OSX issue in Editor scene view (UUM-47698).
                    // Also required for preview camera rendering for grid drawn with builtin RP (UUM-55171).
                    if (cameraData.isSceneViewCamera || cameraData.isPreviewCamera)
                        builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
#endif
                }
                else
                {
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                }
                
                if (bindAsCameraDepth && destination.IsValid())
                    builder.SetGlobalTextureAfterPass(destination, ShaderConsts._CameraDepthTexture);
                
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    ExecutePass(context.cmd, passData);
                });
            }
        }
    }
}