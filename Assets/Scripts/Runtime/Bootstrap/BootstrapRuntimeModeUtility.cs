namespace VVardenfell.Runtime.Bootstrap
{
    public static class BootstrapRuntimeModeUtility
    {
        public static bool IsSandboxMode(BootstrapRuntimeMode mode)
        {
            return mode == BootstrapRuntimeMode.Sandbox
                || mode == BootstrapRuntimeMode.VegetationSandbox;
        }
    }
}
