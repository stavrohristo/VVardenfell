using Unity.Entities;
using Unity.Mathematics;

namespace VVardenfell.Core.Cache
{
    public struct RuntimeModelPrefabBlob
    {
        public BlobArray<RuntimeModelPrefabDefBlob> Records;
        public BlobArray<RuntimeContentHashLookupBlob> ModelPathHashLookup;
        public BlobArray<RuntimeContentHashLookupBlob> ContentModelPathHashLookup;
    }

    public struct RuntimeModelPrefabDefBlob
    {
        public ulong ModelPathHash;
        public ulong ContentModelPathHash;
        public int ModelPrefabIndex;
        public int CollisionIndex;
        public float CollisionRadius;
        public int RootNodeIndex;
        public byte Supported;
        public byte ObjectAnimationEnabled;
        public byte HasAttachLightOffset;
        public float EffectControllerStopTime;
        public float3 AttachLightOffset;
    }

    public struct RuntimeModelPrefabBlobReference : IComponentData
    {
        public BlobAssetReference<RuntimeModelPrefabBlob> Blob;
    }
}
