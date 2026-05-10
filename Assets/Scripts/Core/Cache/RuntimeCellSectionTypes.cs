using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace VVardenfell.Core.Cache
{
    public struct RuntimeCellSectionHeader : IComponentData
    {
        public uint PipelineVersion;
        public uint Flags;
        public int GridX;
        public int GridY;
        public byte IsInterior;
        public FixedString128Bytes CellId;
        public ulong InteriorCellHash;
        public CellEnvironmentDataBlob Environment;
    }

    public struct RuntimeCellSectionResident : IComponentData
    {
        public int2 ExteriorCoord;
        public ulong InteriorCellHash;
        public byte IsInterior;
    }

    public struct CellEnvironmentDataBlob
    {
        public byte HasMood;
        public byte HasWater;
        public uint AmbientColorRgba;
        public uint DirectionalColorRgba;
        public uint FogColorRgba;
        public float FogDensity;
        public float WaterHeight;
        public FixedString128Bytes RegionId;
    }

    public struct RuntimeCellSectionTerrainCollider : IComponentData
    {
        public BlobAssetReference<Collider> Blob;
    }

    public struct RuntimeCellSectionStaticCollider : IComponentData
    {
        public BlobAssetReference<Collider> Blob;
    }

    public struct RuntimeCellSectionTerrainHeight : IBufferElementData
    {
        public float Value;
    }

    public struct RuntimeCellSectionTerrainNormal : IBufferElementData
    {
        public sbyte Value;
    }

    public struct RuntimeCellSectionTerrainLayer : IBufferElementData
    {
        public ushort Value;
    }

    public struct RuntimeCellSectionWorldMapSample : IBufferElementData
    {
        public sbyte Value;
    }

    public struct RuntimeCellSectionRef : IBufferElementData
    {
        public RefEntry Value;
    }

    public struct RuntimeCellSectionDoor : IBufferElementData
    {
        public uint PlacedRefId;
        public uint Flags;
        public float3 DestinationPosition;
        public quaternion DestinationRotation;
        public FixedString128Bytes DestinationCellId;
    }

    public struct RuntimeCellSectionCapturedSoul : IBufferElementData
    {
        public uint PlacedRefId;
        public FixedString64Bytes SoulId;
    }

    public struct RuntimeCellSectionLockState : IBufferElementData
    {
        public uint PlacedRefId;
        public int LockLevel;
        public byte Locked;
        public FixedString64Bytes KeyId;
        public FixedString64Bytes TrapId;
    }

    public struct RuntimeCellSectionCombinedChunk : IBufferElementData
    {
        public int TileX;
        public int TileY;
        public int MaterialIndex;
        public int TextureBucketKey;
        public float3 BoundsCenter;
        public float3 BoundsExtents;
        public int VertexCount;
        public int IndexCount;
        public uint MeshFlags;
        public int FirstVertexByte;
        public int VertexByteCount;
        public int FirstIndexByte;
        public int IndexByteCount;
        public int FirstMember;
        public int MemberCount;
    }

    public struct RuntimeCellSectionCombinedVertexByte : IBufferElementData
    {
        public byte Value;
    }

    public struct RuntimeCellSectionCombinedIndexByte : IBufferElementData
    {
        public byte Value;
    }

    public struct RuntimeCellSectionCombinedMember : IBufferElementData
    {
        public uint PlacedRefId;
        public int NodeIndex;
    }
}
