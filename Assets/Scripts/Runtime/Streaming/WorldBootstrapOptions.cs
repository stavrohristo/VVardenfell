using Unity.Mathematics;
using VVardenfell.Runtime.Bootstrap;

namespace VVardenfell.Runtime.Streaming
{
    public readonly struct WorldBootstrapOptions
    {
        public WorldBootstrapOptions(
            BootstrapRuntimeMode mode,
            float3 playerStartPosition,
            quaternion playerStartRotation,
            SandboxWorldProfile sandboxProfile = null)
        {
            Mode = mode;
            PlayerStartPosition = playerStartPosition;
            PlayerStartRotation = playerStartRotation;
            SandboxProfile = sandboxProfile;
        }

        public BootstrapRuntimeMode Mode { get; }
        public float3 PlayerStartPosition { get; }
        public quaternion PlayerStartRotation { get; }
        public SandboxWorldProfile SandboxProfile { get; }

        public bool IsSandbox => BootstrapRuntimeModeUtility.IsSandboxMode(Mode);
        public bool RequiresFullCellPreload => !IsSandbox;
        public bool QueueInitialExteriorCells => !IsSandbox || (SandboxProfile?.QueueInitialExteriorCells ?? false);
        public bool SpawnLocalPlayer => !IsSandbox || (SandboxProfile?.SpawnLocalPlayer ?? true);

        public static WorldBootstrapOptions Vanilla => new(
            BootstrapRuntimeMode.Vanilla,
            WorldBootstrap.DefaultPlayerSpawnPosition(),
            quaternion.identity);
    }
}
