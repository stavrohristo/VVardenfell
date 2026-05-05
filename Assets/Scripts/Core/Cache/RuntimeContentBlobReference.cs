using Unity.Entities;

namespace VVardenfell.Core.Cache
{
    public struct RuntimeContentBlobReference : IComponentData
    {
        public BlobAssetReference<RuntimeContentBlob> Blob;
    }
}
