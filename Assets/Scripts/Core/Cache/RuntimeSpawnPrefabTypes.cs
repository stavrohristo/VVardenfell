using Unity.Entities;
using Unity.Mathematics;

namespace VVardenfell.Core.Cache
{
    public struct RuntimeSpawnPrefabCacheHeader : IComponentData
    {
        public uint FormatVersion;
        public uint PipelineVersion;
        public int ModelPrefabCount;
    }

    public struct RuntimeSpawnPrefabRoot : IComponentData
    {
        public int ModelPrefabIndex;
        public int CollisionIndex;
    }

    public struct RuntimeSpawnPrefabNode : IComponentData
    {
        public int ModelPrefabIndex;
        public int NodeIndex;
        public int ParentIndex;
        public byte Kind;
    }

    public struct RuntimeSpawnPrefabPickCollider : IComponentData
    {
        public int ColliderIndex;
    }

    public struct RuntimeSpawnPrefabRenderResource : IComponentData
    {
        public int ModelPrefabIndex;
        public int NodeIndex;
        public int MeshIndex;
        public int MaterialIndex;
        public int TextureIndex;
        public float3 BoundsCenter;
        public float3 BoundsExtents;
    }

    public struct RuntimeSpawnPrefabRegistry : IComponentData
    {
    }

    public struct RuntimeSpawnPrefabRegistryEntry : IBufferElementData
    {
        public int ModelPrefabIndex;
        public int CollisionIndex;
        public Entity PrefabRoot;
    }
}
