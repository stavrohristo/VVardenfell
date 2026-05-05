using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace VVardenfell.Core.Cache
{
    public struct RuntimeWorldCellBlob
    {
        public BlobArray<RuntimeWorldCellDefBlob> Cells;
        public BlobArray<RefEntry> Refs;
        public BlobArray<RuntimeWorldDoorRefDefBlob> Doors;
        public BlobArray<RuntimeWorldPlacedRefLockStateBlob> LockStates;
        public BlobArray<RuntimeWorldPlacedRefCapturedSoulBlob> CapturedSouls;
        public BlobArray<float> TerrainHeights;
        public BlobArray<sbyte> WorldMapSamples;
        public BlobArray<RuntimeWorldCellExteriorLookupBlob> ExteriorCellLookup;
        public BlobArray<RuntimeContentHashLookupBlob> InteriorCellHashLookup;
    }

    public struct RuntimeWorldCellDefBlob
    {
        public int2 ExteriorCoord;
        public FixedString128Bytes CellId;
        public FixedString128Bytes InteriorCellId;
        public ulong InteriorCellHash;
        public byte IsInterior;
        public byte HasTerrain;
        public byte HasWorldMap;
        public byte HasStaticCollider;
        public byte HasTerrainCollider;
        public int FirstRefIndex;
        public int RefCount;
        public int FirstDoorIndex;
        public int DoorCount;
        public int FirstLockStateIndex;
        public int LockStateCount;
        public int FirstCapturedSoulIndex;
        public int CapturedSoulCount;
        public int FirstTerrainHeightIndex;
        public int TerrainHeightCount;
        public int FirstWorldMapSampleIndex;
        public int WorldMapSampleCount;
        public RuntimeWorldCellEnvironmentDefBlob Environment;
    }

    public struct RuntimeWorldCellEnvironmentDefBlob
    {
        public byte HasMood;
        public byte HasWater;
        public uint AmbientColorRgba;
        public uint DirectionalColorRgba;
        public uint FogColorRgba;
        public float FogDensity;
        public float WaterHeight;
        public ulong RegionIdHash;
    }

    public struct RuntimeWorldDoorRefDefBlob
    {
        public uint PlacedRefId;
        public uint Flags;
        public float DestPosX;
        public float DestPosY;
        public float DestPosZ;
        public float DestRotX;
        public float DestRotY;
        public float DestRotZ;
        public float DestRotW;
        public FixedString128Bytes DestinationCellId;
        public ulong DestinationCellHash;
    }

    public struct RuntimeWorldPlacedRefLockStateBlob
    {
        public uint PlacedRefId;
        public int LockLevel;
        public byte Locked;
        public FixedString64Bytes KeyId;
        public FixedString64Bytes TrapId;
    }

    public struct RuntimeWorldPlacedRefCapturedSoulBlob
    {
        public uint PlacedRefId;
        public FixedString64Bytes SoulId;
        public ulong SoulIdHash;
    }

    public struct RuntimeWorldCellExteriorLookupBlob
    {
        public int2 Coord;
        public int CellIndex;
    }

    public struct RuntimeWorldCellBlobReference : IComponentData
    {
        public BlobAssetReference<RuntimeWorldCellBlob> Blob;
    }
}
