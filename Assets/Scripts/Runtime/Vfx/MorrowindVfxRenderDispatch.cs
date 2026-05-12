using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VVardenfell.Runtime.Vfx
{
    internal static class MorrowindVfxRenderDispatch
    {
        static bool s_Registered;
        static readonly MorrowindVfxRenderPass s_RenderPass = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            s_Registered = false;
            EnsureRegistered();
        }

        public static void EnsureRegistered()
        {
            if (s_Registered)
                return;

            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            s_Registered = true;
        }

        static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!RuntimeVfxPresentationResources.TryGetDefault(out var presentation))
                return;

            var resources = presentation.Resources;
            if (resources == null || resources.ParticleCount <= 0)
                return;

            if (camera == null || !camera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
                return;

            var renderer = cameraData.scriptableRenderer;
            if (renderer == null)
                return;

            s_RenderPass.SetCamera(camera);
            renderer.EnqueuePass(s_RenderPass);
        }

        static void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
        }
    }
}
