using Unity.Entities;
using Unity.Physics;
using VVardenfell.Runtime.Components;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Components
{
    public enum RuntimePhysicsMutationKind : byte
    {
        Enable = 1,
        Disable = 2,
        AttachSource = 3,
        SetPhysicsCollider = 4,
    }

    public struct RuntimePhysicsMutationQueueTag : IComponentData
    {
    }

    public struct PhysicsFlushRequested : IComponentData
    {
        public byte Pending;
    }

    public struct RuntimePhysicsMutationRequest : IBufferElementData
    {
        public RuntimePhysicsMutationKind Kind;
        public Entity Entity;
        public BlobAssetReference<Collider> Collider;
        public RuntimeColliderKind ColliderKind;
        public byte Active;
        public byte Temporary;
    }
}
