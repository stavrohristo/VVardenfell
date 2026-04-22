namespace VVardenfell.Runtime.Bootstrap
{
    public static class BootstrapPresentationGate
    {
        public static bool BlocksGameplayInput { get; set; }

        public static void Reset()
        {
            BlocksGameplayInput = false;
        }
    }
}
