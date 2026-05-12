namespace VVardenfell.Runtime.Streaming
{
    public struct RuntimeSpawnPrefabDescriptor
    {
        public int ModelPrefabIndex;
        public int CollisionIndex;
        public byte Supported;

        public bool IsSupported => Supported != 0 && ModelPrefabIndex >= 0;
    }
}
