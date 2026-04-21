using UnityEngine;

namespace VVardenfell.Runtime.Bootstrap
{
    public static class BootstrapBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            var go = new GameObject("VVardenfell.Bootstrap");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<BootstrapController>();
        }
    }
}
