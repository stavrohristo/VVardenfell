using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Components
{
    public enum DeferredPhysicsQueryKind : byte
    {
        GenericRay = 1,
        InteractionPick = 2,
        LineOfSight = 3,
        MeleeConfirmation = 4,
        ProjectileSegment = 5,
    }

    public enum DeferredPhysicsQueryStatus : byte
    {
        Pending = 0,
        Miss = 1,
        Hit = 2,
    }

    public struct DeferredPhysicsQueryQueueTag : IComponentData
    {
    }

    public struct DeferredPhysicsQueryPending : IComponentData, IEnableableComponent
    {
    }

    public struct DeferredPhysicsQueryRuntime : IComponentData
    {
        public uint NextSequence;
        public uint LastResolvedBuildSequence;
    }

    public struct DeferredPhysicsQueryRequest : IBufferElementData
    {
        public uint Sequence;
        public DeferredPhysicsQueryKind Kind;
        public Entity RequesterEntity;
        public Entity TargetEntity;
        public Entity IgnoreEntity;
        public float3 Start;
        public float3 End;
        public CollisionFilter Filter;
        public BlobAssetReference<Collider> Collider;
        public quaternion Rotation;
        public uint RequestFixedTick;
    }

    public struct DeferredPhysicsQueryResult : IBufferElementData
    {
        public uint Sequence;
        public DeferredPhysicsQueryKind Kind;
        public DeferredPhysicsQueryStatus Status;
        public Entity RequesterEntity;
        public Entity TargetEntity;
        public Entity HitEntity;
        public float3 Position;
        public float3 Normal;
        public float Fraction;
        public uint RequestFixedTick;
        public uint PhysicsBuildSequence;
    }
}
