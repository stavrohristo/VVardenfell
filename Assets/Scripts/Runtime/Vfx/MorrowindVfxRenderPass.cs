using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.Vfx
{
    internal sealed class MorrowindVfxRenderPass : ScriptableRenderPass
    {
        Camera _camera;

        sealed class PassData
        {
            public Camera Camera;
            public MorrowindVfxResources Resources;
            public TextureHandle ColorTarget;
            public TextureHandle DepthTarget;
        }

        public MorrowindVfxRenderPass()
        {
            profilingSampler = new ProfilingSampler("VV Morrowind VFX");
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        public void SetCamera(Camera camera)
        {
            _camera = camera;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = WorldResources.Vfx;
            if (resources == null || resources.ParticleCount <= 0)
                return;

            if (_camera == null)
                throw new InvalidOperationException("[VVardenfell][VFX] Render pass was enqueued without a camera.");

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            using var builder = renderGraph.AddUnsafePass<PassData>("VV Morrowind VFX", out var passData, profilingSampler);
            passData.Camera = _camera;
            passData.Resources = resources;
            passData.ColorTarget = resourceData.activeColorTexture;
            passData.DepthTarget = resourceData.activeDepthTexture;

            builder.UseTexture(passData.ColorTarget, AccessFlags.ReadWrite);
            builder.UseTexture(passData.DepthTarget, AccessFlags.ReadWrite);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
            {
                context.cmd.SetRenderTarget(data.ColorTarget, data.DepthTarget);
                CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                data.Resources.RecordDispatch(cmd, data.Camera, bindCameraTarget: false);
            });
        }
    }
}
