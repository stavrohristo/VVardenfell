using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using VVardenfell.Core.Cache;

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

    public struct RuntimeCellSectionResident : IComponentData, IEnableableComponent
    {
        public int2 ExteriorCoord;
        public ulong InteriorCellHash;
        public byte IsInterior;
    }

    public struct RuntimeCellSectionResourcesBound : IComponentData, IEnableableComponent
    {
    }

    public struct RuntimeCellSectionRenderEntity : IBufferElementData
    {
        public Entity Value;
    }

    public struct RuntimeCellSectionTerrainEntity : IBufferElementData
    {
        public Entity Value;
    }

    public struct RuntimeCellSectionCombinedRenderEntity : IBufferElementData
    {
        public Entity Value;
    }

    public struct RuntimeCellSectionColliderEntity : IBufferElementData
    {
        public Entity Value;
    }

    public struct RuntimeCellSectionLogicalRefEntity : IBufferElementData
    {
        public Entity Value;
    }

    public struct RuntimeCellSectionExplicitRefEntry : IBufferElementData
    {
        public int ContentKey;
        public uint PlacedRefId;
        public Entity Entity;
    }

    public struct RuntimeCellSectionActorInitEntity : IBufferElementData
    {
        public Entity Value;
    }

    public struct RuntimeCellSectionTransformRootEntity : IBufferElementData
    {
        public Entity Value;
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

    public struct RuntimeCellSectionMember : IComponentData
    {
        public Entity Section;
        public int2 ExteriorCoord;
        public ulong InteriorCellHash;
        public byte IsInterior;
    }

    public struct RuntimeCellSectionTerrainTag : IComponentData
    {
    }

    public struct RuntimeCellSectionTerrainRenderResource : IComponentData
    {
        public int MeshIndex;
        public int SplatSlice;
        public float3 BoundsCenter;
        public float3 BoundsExtents;
    }

    public struct RuntimeDistantTerrainPayloadHeader : IComponentData
    {
        public uint PipelineVersion;
        public int TerrainCount;
    }

    public struct RuntimeDistantTerrainTag : IComponentData
    {
    }

    public struct RuntimeCellSectionStaticColliderTag : IComponentData
    {
    }

    public struct RuntimeCellSectionActorNeedsInitialization : IComponentData, IEnableableComponent
    {
    }

    public struct RuntimeCellSectionRefOrder : IComponentData
    {
        public int Value;
    }

    public struct RuntimeCellSectionRenderRoot : IComponentData
    {
        public int ModelPrefabIndex;
        public int CollisionIndex;
    }

    public struct RuntimeCellSectionCombinedRenderResource : IComponentData
    {
        public int MeshIndex;
        public int MaterialIndex;
        public int TextureBucketKey;
        public int TileX;
        public int TileY;
        public float3 BoundsCenter;
        public float3 BoundsExtents;
        public int VertexCount;
        public int IndexCount;
        public uint MeshFlags;
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

    public struct RuntimeCellSectionDoorMetadata : IComponentData
    {
        public uint Flags;
        public float3 DestinationPosition;
        public quaternion DestinationRotation;
        public FixedString128Bytes DestinationCellId;
    }

}

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
        public VVardenfell.Core.Cache.ContentReference Value;
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

    public struct AudioEmitterAuthoring : IComponentData
    {
        public SoundDefHandle PrimarySound;
        public SoundDefHandle SecondarySound;
    }

    public struct BookTag : IComponentData
    {
    }

    public struct InteractionActivationProxyBuildPending : IComponentData
    {
    }

    public struct InteractionPickSurfaceTag : IComponentData
    {
    }

    public struct DoorInteractable : IComponentData
    {
        public byte IsTeleport;
        public FixedString128Bytes DestinationCellId;
        public ulong DestinationCellHash;
        public float3 DestinationPosition;
        public quaternion DestinationRotation;
    }

    public struct DoorActivated : IComponentData, IEnableableComponent
    {
    }

    public struct DoorMotionState : IComponentData
    {
        public float Progress;
        public float TargetProgress;
        public float RangeRadians;
        public float SpeedRadiansPerSecond;
        public byte Axis;
    }

    public enum MorrowindScriptInstanceStatus : byte
    {
        None = 0,
        Running = 1,
        Disabled = 2,
        Faulted = 3,
    }

    public struct MorrowindScriptInstance : IComponentData
    {
        public MorrowindScriptProgramDefHandle Program;
        public int ProgramIndex;
        public int ProgramCounter;
        public byte Status;
        public byte SuppressActivation;
        public FixedString128Bytes DisabledReason;
    }

    public struct MorrowindScriptLocalValue : IBufferElementData
    {
        public int IntValue;
        public float FloatValue;
        public byte ValueKind;
    }

    public struct MorrowindScriptStackValue : IBufferElementData
    {
        public int IntValue;
        public float FloatValue;
        public byte ValueKind;
    }

    public struct LightInstanceFlags : IComponentData
    {
        public byte Carry;
        public byte Negative;
        public byte Flicker;
        public byte FlickerSlow;
        public byte Pulse;
        public byte PulseSlow;
        public byte OffDefault;
    }

    public struct LightInstanceState : IComponentData
    {
        public byte Enabled;
        public float3 BaseColorRgb;
        public float BaseIntensity;
        public float BaseRange;
        public float CurrentIntensity;
        public float CurrentRange;
        public float AnimationTime;
    }

    public struct LightInstanceAnimated : IComponentData
    {
    }

    public struct LightPresentationLink : IComponentData
    {
        public int Slot;
    }

    public struct LightPresentationOffset : IComponentData
    {
        public float3 LocalPosition;
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

    public struct ActiveExplicitDynamicRefEntry
    {
        public int Key;
        public uint PlacedRefId;
    }

    public struct ActiveExplicitRefLookup : IComponentData
    {
        public NativeParallelHashMap<int, ActiveExplicitRefTarget> ByContentKey;
        public NativeParallelHashMap<int, ActiveExplicitRefTarget> AllByContentKey;
        public NativeParallelMultiHashMap<int, ActiveExplicitRefTarget> ActiveEntriesByContentKey;
        public NativeParallelHashMap<Entity, ActiveExplicitDynamicRefEntry> ActiveDynamicEntriesByEntity;
        public NativeParallelHashMap<int2, byte> ActiveExteriorCells;
    }

    public struct ActiveExplicitRefLookupDirty : IComponentData, IEnableableComponent
    {
    }

    public struct ActiveExplicitRefLookupWorkPending : IComponentData, IEnableableComponent
    {
    }

    public struct ActiveExplicitRefLookupFullRebuild : IComponentData, IEnableableComponent
    {
    }

    public enum ActiveExplicitDynamicRefOperation : byte
    {
        Add = 1,
        Remove = 2,
        Move = 3,
    }

    public struct ActiveExplicitSectionChange : IBufferElementData
    {
        public Entity Section;
        public byte Activate;
    }

    public struct ActiveExplicitDynamicRefChange : IBufferElementData
    {
        public Entity Entity;
        public int ContentKey;
        public uint PlacedRefId;
        public byte Operation;
        public byte WasActive;
        public byte IsActive;
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

    public enum RuntimeColliderKind : byte
    {
        None = 0,
        TerrainCell = 1,
        StaticCell = 2,
        PlacedRef = 3,
        ActivationProxy = 4,
        RuntimeSpawn = 5,
        Player = 6,
        Actor = 7,
        InteractionPick = 8,
        Projectile = 9,
    }

    public struct RuntimeColliderSource : IComponentData
    {
        public BlobAssetReference<Unity.Physics.Collider> Value;
        public RuntimeColliderKind Kind;
        public byte Temporary;
    }

    public struct RuntimeGeneratedColliderBlobCleanup : ICleanupComponentData
    {
        public BlobAssetReference<Unity.Physics.Collider> Value;
    }

    public struct InteriorCellMember : IComponentData
    {
    }
}

namespace VVardenfell.Runtime.Animation
{
    public struct ObjectAnimationState : IComponentData
    {
        public int ModelPrefabIndex;
        public int ClipIndex;
        public float PreviousTime;
        public float CurrentTime;
        public float StartTime;
        public float LoopStartTime;
        public float LoopStopTime;
        public float StopTime;
        public uint LoopCount;
        public byte Scripted;
        public byte Active;
    }

    public struct ObjectAnimationNode : IComponentData
    {
        public Entity Root;
        public int ModelPrefabIndex;
        public int NodeIndex;
        public int ParentIndex;
        public float3 BindPosition;
        public quaternion BindRotation;
        public float BindScale;
    }
}

namespace VVardenfell.Runtime.Streaming
{
    public struct CellCoord : IComponentData
    {
        public int2 Value;
    }

    public struct CellLink : IComponentData
    {
        public int2 Value;
    }

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
