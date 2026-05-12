namespace VVardenfell.Core.Cache
{
    public sealed class CombinedCellRenderChunkDef
    {
        public int TileX;
        public int TileY;
        public int MaterialIndex;
        public int TextureBucketKey;
        public float BoundsCenterX;
        public float BoundsCenterY;
        public float BoundsCenterZ;
        public float BoundsExtentsX;
        public float BoundsExtentsY;
        public float BoundsExtentsZ;
        public int GlobalMeshIndex = -1;
        public int VertexCount;
        public int IndexCount;
        public uint MeshFlags;
        public byte[] VertexBytes;
        public byte[] IndexBytes;
        public CombinedCellRenderChunkMemberDef[] Members;
    }
}
