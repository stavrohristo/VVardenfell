using UnityEngine;
using UnityEngine.Rendering;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.Rendering
{
    internal static class StaticRefInstanceRenderDispatch
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        static void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            WorldResources.StaticRefInstanceRenderer?.Draw(context, camera);
        }
    }
}
