using UnityEngine;
using UnityEngine.Rendering;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.Rendering
{
    internal static class ActorGpuAnimationRenderDispatch
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            var resources = WorldResources.ActorGpuAnimation;
            if (resources == null || !resources.HasPreparedFrame)
                return;

            CommandBuffer cmd = CommandBufferPool.Get("VV ActorGpuAnimation RenderDispatch");
            resources.RecordPreparedFrameDispatch(cmd);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
