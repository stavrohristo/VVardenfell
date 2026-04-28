using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Components
{    public enum RuntimeSpawnPersistencePolicy : byte
    {
        None = 0,
        CellOwnedSession = 1,
    }

    public enum RuntimeSpawnResultStatus : byte
    {
        None = 0,
        Success = 1,
        Unsupported = 2,
        InvalidContent = 3,
        MissingPrefab = 4,
        InvalidLocation = 5,
        NotReady = 6,
    }

    public struct RuntimeSpawnState : IComponentData
    {
        public uint NextRequestSequence;
        public uint NextRuntimeRefId;
    }

    public struct RuntimeSpawnResult : IComponentData
    {
        public uint Sequence;
        public uint RuntimeRefId;
        public Entity LogicalEntity;
        public byte Status;
        public FixedString128Bytes Message;
    }

    public struct RuntimeSpawnedRefIdentity : IComponentData
    {
        public uint RuntimeRefId;
        public byte PersistencePolicy;
    }

    public struct RuntimeSpawnRenderRootTag : IComponentData
    {
    }

    public struct RuntimeSpawnRequest : IBufferElementData
    {
        public uint Sequence;
        public ContentReference Content;
        public float3 Position;
        public quaternion Rotation;
        public float Scale;
        public int2 ExteriorCell;
        public FixedString128Bytes InteriorCellId;
        public ulong InteriorCellHash;
        public byte IsInterior;
        public byte PersistencePolicy;
    }

    public struct RuntimeSpawnedRef : IBufferElementData
    {
        public uint RuntimeRefId;
        public ContentReference Content;
        public float3 Position;
        public quaternion Rotation;
        public float Scale;
        public int2 ExteriorCell;
        public FixedString128Bytes InteriorCellId;
        public ulong InteriorCellHash;
        public Entity LogicalEntity;
        public byte IsInterior;
        public byte PersistencePolicy;
        public byte Alive;
    }

    public enum WorldJournalEntryKind : byte
    {
        None = 0,
        LooseItemRemoved = 1,
        ContainerDelta = 2,
        RuntimeSpawned = 3,
        RuntimeDestroyed = 4,
    }

    public struct WorldJournalState : IComponentData
    {
        public uint NextSequence;
    }

    public struct WorldJournalEntry : IBufferElementData
    {
        public uint Sequence;
        public byte Kind;
        public uint PlacedRefId;
        public uint RuntimeRefId;
        public ContentReference Content;
        public int DeltaCount;
        public float3 Position;
        public quaternion Rotation;
        public float Scale;
        public int2 ExteriorCell;
        public FixedString128Bytes InteriorCellId;
        public ulong InteriorCellHash;
        public byte IsInterior;
        public byte PersistencePolicy;
    }
}
