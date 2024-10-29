using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal.Internal
{
    public class CopyColorPass_ : ScriptableRenderPass
    {
        private RTHandle source;
        private RTHandle destination;
        private Downsampling m_DownSamplingMethod;
        private Material m_CopyColorMaterial;

        private int m_SampleOffsetHandle;
        private Material m_SamplingMaterial;

        private PassData m_PassData;
        
        public CopyColorPass_(RenderPassEvent evt, Material copyClolorMat, Material samplingMat, string customPassName = "")
        {
            profilingSampler = customPassName != ""
                ? new ProfilingSampler(customPassName)
                : ProfilingSampler.Get(URPProfileId.CopyColor);
            
            renderPassEvent = evt;
            m_CopyColorMaterial = copyClolorMat;
            m_SamplingMaterial = samplingMat;
            m_DownSamplingMethod = Downsampling.None;
        }
        
        public class PassData
        {
            internal Material m_CopyColorMaterial;
            internal Material m_SamplingMaterial;
            internal Downsampling m_DownSamplingMethod;

            internal int sampleOffsetHandle;

            internal TextureHandle source;
        }
        
        public void Setup(RTHandle source, RTHandle destination, Downsampling downsampling)
        {
            this.source = source;
            this.destination = destination;
            m_DownSamplingMethod = downsampling;
        }

        /// <inheritdoc />
        [Obsolete(DeprecationMessage.CompatibilityScriptingAPIObsolete, false)]
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            cmd.SetGlobalTexture(destination.name, destination.nameID);
        }

        public static void ConfigureDescriptor(Downsampling method, ref RenderTextureDescriptor textureDescriptor,
            out FilterMode filtermode)
        {
            textureDescriptor.msaaSamples = 1;
            textureDescriptor.depthStencilFormat = GraphicsFormat.None;
            if (method == Downsampling._2xBilinear)
            {
                textureDescriptor.width = Mathf.Max(1, textureDescriptor.width / 2);
                textureDescriptor.height = Mathf.Max(1, textureDescriptor.height / 2);
            }
            else if (method == Downsampling._4xBilinear || method == Downsampling._4xBox)
            {
                textureDescriptor.width = Mathf.Max(1, textureDescriptor.width / 4 );
                textureDescriptor.height = Mathf.Max(1, textureDescriptor.height / 4);
            }

            filtermode = method == Downsampling.None ? FilterMode.Point : FilterMode.Bilinear;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            m_PassData.m_CopyColorMaterial = m_CopyColorMaterial;
            m_PassData.m_SamplingMaterial = m_SamplingMaterial;
            m_PassData.m_DownSamplingMethod = m_DownSamplingMethod;
            m_PassData.sampleOffsetHandle = m_SampleOffsetHandle;

            CommandBuffer cmd = renderingData.commandBuffer;

            RasterCommandBuffer rcmd = CommandBufferHelpers.GetRasterCommandBuffer(cmd);

            ScriptableRenderer.SetRenderTarget(cmd, destination, k_CameraTarget, clearFlag, clearColor);
            ExecutePass(rcmd, m_PassData, source);
        }

        public void Render(RenderGraph renderGraph, ContextContainer frameData, out TextureHandle destination, in TextureHandle source, Downsampling downsampling)
        {
            m_DownSamplingMethod = downsampling;
            
            var cameraData = frameData.Get<UniversalCameraData>();
            var descriptor = cameraData.cameraTargetDescriptor;
            ConfigureDescriptor(downsampling, ref descriptor, out var filtermode);

            destination = UniversalRenderer.CreateRenderGraphTexture(renderGraph, descriptor, "_CameraOpaqueTexture", true, filtermode);
            
            using (var builder =
                   renderGraph.AddRasterRenderPass<PassData>("Copy Color Pass", out var passdata, profilingSampler))
            {
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.UseTexture(source, AccessFlags.Read);

                passdata.source = source;
                passdata.m_CopyColorMaterial = m_CopyColorMaterial;
                passdata.m_SamplingMaterial = m_SamplingMaterial;
                passdata.m_DownSamplingMethod = m_DownSamplingMethod;
                passdata.sampleOffsetHandle = m_SampleOffsetHandle;
                
                if (destination.IsValid())
                    builder.SetGlobalTextureAfterPass(destination, Shader.PropertyToID("_CameraOpaqueTexture"));

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    ExecutePass(context.cmd, data, data.source);
                });
            }
        }

        public static void ExecutePass(RasterCommandBuffer cmd, PassData data, RTHandle source)
        {
            Material copyClolorMat = data.m_CopyColorMaterial;
            Material samplingMat = data.m_SamplingMaterial;
            Downsampling downsampling = data.m_DownSamplingMethod;
            int sampleOffsetHandle = data.sampleOffsetHandle;

            using (new ProfilingScope(ProfilingSampler.Get(URPProfileId.CopyColor)))
            {
                Vector2 viewPortScale = source.useScaling
                    ? new Vector2(source.rtHandleProperties.rtHandleScale.x, source.rtHandleProperties.rtHandleScale.y)
                    : Vector2.one;
                switch (downsampling)
                {
                    case Downsampling.None:
                        Blitter.BlitTexture(cmd, source, viewPortScale, copyClolorMat, 0);
                        break;
                    case Downsampling._2xBilinear:
                        Blitter.BlitTexture(cmd, source, viewPortScale, copyClolorMat, 0);
                        break;
                    case Downsampling._4xBox:
                        samplingMat.SetFloat(sampleOffsetHandle, 2);
                        Blitter.BlitTexture(cmd, source, viewPortScale, samplingMat, 0);
                        break;
                    case Downsampling._4xBilinear:
                        Blitter.BlitTexture(cmd, source, viewPortScale, copyClolorMat, 1);
                        break;                    
                }
            }
        }
        
    }
}