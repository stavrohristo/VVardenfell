using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Components
{    public enum PassiveActorFamily : byte
    {
        None = 0,
        Npc = 1,
        Creature = 2,
    }

    public struct PlacedRefIdentity : IComponentData
    {
        public uint Value;
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
