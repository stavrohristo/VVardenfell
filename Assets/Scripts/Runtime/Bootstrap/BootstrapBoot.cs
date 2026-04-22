using UnityEngine;

namespace VVardenfell.Runtime.Bootstrap
{
    public static class BootstrapBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (BootstrapController.Active != null || Object.FindAnyObjectByType<BootstrapController>() != null)
                return;

            var go = new GameObject("VVardenfell.Bootstrap");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<BootstrapController>();
        }
    }
}
