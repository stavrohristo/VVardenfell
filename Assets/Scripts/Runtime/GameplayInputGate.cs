using VVardenfell.Runtime.Bootstrap;

namespace VVardenfell.Runtime
{
    public static class RuntimeShellPresentationGate
    {
        public static bool BlocksGameplayInput { get; set; }

        public static void Reset()
        {
            BlocksGameplayInput = false;
        }
    }

    public static class GameplayInputGate
    {
        public static bool BlocksGameplayInput => BootstrapPresentationGate.BlocksGameplayInput || RuntimeShellPresentationGate.BlocksGameplayInput;
    }
}
