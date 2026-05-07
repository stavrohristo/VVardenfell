using Unity.Entities;
using Unity.Mathematics;

namespace VVardenfell.Runtime.Streaming
{
    public struct CombinedCellRenderChunk : IComponentData
    {
        public int2 Cell;
        public int TileX;
        public int TileY;
        public int MaterialIndex;
        public int TextureBucketKey;
        public byte Disabled;
    }

    public struct CombinedCellRenderChunkMember : IBufferElementData
    {
        public Entity RenderEntity;
        public Entity LogicalRefEntity;
        public uint PlacedRefId;
        public int NodeIndex;
    }

    public struct CombinedCellRenderLink : IBufferElementData
    {
        public Entity Chunk;
    }

    public struct CombinedCellRenderSuppressed : IComponentData
    {
    }
}
