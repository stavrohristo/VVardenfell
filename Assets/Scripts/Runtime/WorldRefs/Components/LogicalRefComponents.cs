using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Components
{
    public enum PassiveActorFamily : byte
    {
        None = 0,
        Npc = 1,
        Creature = 2,
    }

    public struct PlacedRefIdentity : IComponentData
    {
        public uint Value;
    }

    public struct LogicalRefContent : IComponentData
    {
        public ContentReference Value;
    }

    public struct PlacedRefRuntimeState : IComponentData
    {
        public byte Disabled;
    }

    public struct PlacedRefInitialTransform : IComponentData
    {
        public float3 Position;
        public quaternion Rotation;
        public float Scale;
    }

    public struct PlacedRefLockState : IComponentData
    {
        public int LockLevel;
        public byte Locked;
        public FixedString64Bytes KeyId;
        public FixedString64Bytes TrapId;
    }

    public struct PlacedRefLockRequest : IBufferElementData
    {
        public Entity TargetEntity;
        public uint TargetPlacedRefId;
        public int LockLevel;
        public byte Operation;
    }

    public struct PlacedRefCapturedSoul : IComponentData
    {
        public FixedString64Bytes SoulId;
        public int SoulActorHandleValue;
    }

    public struct LogicalRefTag : IComponentData
    {
    }

    public struct LogicalRefLocation : IComponentData
    {
        public int2 ExteriorCell;
        public FixedString128Bytes InteriorCellId;
        public ulong InteriorCellHash;
        public byte IsInterior;
    }

    public struct LogicalRefParent : IComponentData
    {
        public Entity Value;
    }

    public struct LogicalRefChild : IBufferElementData
    {
        public Entity Value;
    }

    public struct ModelPrefabRoot : IComponentData
    {
        public int ModelPrefabIndex;
    }

    public struct ModelPrefabNodeTag : IComponentData
    {
    }

    public struct ModelPrefabRenderLeaf : IComponentData
    {
        public int NodeIndex;
        public int MeshIndex;
        public int MaterialIndex;
        public int TextureIndex;
    }

    public struct ModelBillboardTag : IComponentData
    {
    }

    public struct ModelBillboardState : IComponentData
    {
        public quaternion BaseLocalRotation;
    }

    public struct LogicalRefLookup : IComponentData
    {
        public NativeParallelHashMap<uint, Entity> Map;
    }

    public struct PlacedRefRuntimeStateLookup : IComponentData
    {
        public NativeParallelHashMap<uint, byte> DisabledByPlacedRef;
    }

    public struct ActiveExplicitRefTarget
    {
        public Entity Entity;
        public uint PlacedRefId;
        public byte Ambiguous;
    }

    public struct ActiveExplicitRefLookup : IComponentData
    {
        public NativeParallelHashMap<int, ActiveExplicitRefTarget> ByContentKey;
        public NativeParallelHashMap<int, ActiveExplicitRefTarget> AllByContentKey;
    }

    public struct ActiveExplicitRefLookupDirty : IComponentData, IEnableableComponent
    {
    }

    public struct ActiveExplicitRefLookupBuildState : IComponentData
    {
        public uint LastActiveRevision;
        public ulong LastActiveInteriorCellHash;
        public int LastEntityCount;
        public int LastOrderVersion;
        public byte LastInteriorActive;
        public byte HasBuilt;
    }

    public struct DoorAuthoring : IComponentData
    {
        public DoorDefHandle Definition;
    }

    public struct LightSourceAuthoring : IComponentData
    {
        public LightDefHandle Definition;
    }

    public struct ActorSpawnSource : IComponentData
    {
        public ActorDefHandle Definition;
        public byte FirstPerson;
    }

    public struct ItemPickupAuthoring : IComponentData
    {
        public ItemDefHandle Definition;
    }

    public struct ContainerAuthoring : IComponentData
    {
        public ContainerDefHandle Definition;
    }

    public struct ActivatorAuthoring : IComponentData
    {
        public ActivatorDefHandle Definition;
    }

    public struct StaticRefAuthoring : IComponentData
    {
        public GenericRecordDefHandle Definition;
    }

    public struct LeveledItemAuthoring : IComponentData
    {
        public ItemLeveledListDefHandle Definition;
    }

    public struct LeveledCreatureAuthoring : IComponentData
    {
        public CreatureLeveledListDefHandle Definition;
    }

    public struct PassiveActorPresence : IComponentData
    {
        public byte Family;
        public byte CanTalk;
    }
}
