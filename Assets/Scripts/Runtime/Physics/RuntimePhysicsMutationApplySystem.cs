using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using Unity.Transforms;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Physics
{
    [UpdateInGroup(typeof(MorrowindPhysicsPreBuildSystemGroup), OrderLast = true)]
    public partial struct RuntimePhysicsMutationApplySystem : ISystem
    {
        const byte PendingEnable = 1;
        const byte PendingDisable = 2;

        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<RuntimePhysicsMutationQueueTag>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            systemState.Dependency.Complete();

            Entity queueEntity = SystemAPI.GetSingletonEntity<RuntimePhysicsMutationQueueTag>();
            var mutations = systemState.EntityManager.GetBuffer<RuntimePhysicsMutationRequest>(queueEntity);
            var flush = systemState.EntityManager.GetComponentData<PhysicsFlushRequested>(queueEntity);
            if (mutations.Length == 0 && flush.Pending == 0)
                return;

            using var snapshot = mutations.ToNativeArray(Allocator.Temp);
            mutations.Clear();
            systemState.EntityManager.SetComponentData(queueEntity, new PhysicsFlushRequested());

            var pendingStates = new NativeParallelHashMap<Entity, byte>(System.Math.Max(snapshot.Length, 1), Allocator.Temp);
            try
            {
                for (int i = 0; i < snapshot.Length; i++)
                {
                    var request = snapshot[i];
                    switch (request.Kind)
                    {
                        case RuntimePhysicsMutationKind.Enable:
                            if (request.Entity != Entity.Null)
                                pendingStates[request.Entity] = PendingEnable;
                            break;
                        case RuntimePhysicsMutationKind.Disable:
                            if (request.Entity != Entity.Null)
                                pendingStates[request.Entity] = PendingDisable;
                            break;
                        default:
                            FlushPendingStates(ref systemState, pendingStates);
                            Apply(ref systemState, request);
                            break;
                    }
                }

                FlushPendingStates(ref systemState, pendingStates);
            }
            finally
            {
                pendingStates.Dispose();
            }
        }

        static void FlushPendingStates(ref SystemState systemState, NativeParallelHashMap<Entity, byte> pendingStates)
        {
            if (pendingStates.Count() == 0)
                return;

            using var entities = pendingStates.GetKeyArray(Allocator.Temp);
            using var disableCollider = new NativeList<Entity>(Allocator.Temp);
            using var disableVelocity = new NativeList<Entity>(Allocator.Temp);
            using var addCollider = new NativeList<Entity>(Allocator.Temp);
            using var addColliderValues = new NativeList<PhysicsCollider>(Allocator.Temp);
            using var addWorldIndex = new NativeList<Entity>(Allocator.Temp);
            using var addTemporalTag = new NativeList<Entity>(Allocator.Temp);
            using var addTemporalInfo = new NativeList<Entity>(Allocator.Temp);
            using var resetTemporalInfo = new NativeList<Entity>(Allocator.Temp);
            using var addVelocity = new NativeList<Entity>(Allocator.Temp);
            using var removeVelocity = new NativeList<Entity>(Allocator.Temp);
            using var removeStatic = new NativeList<Entity>(Allocator.Temp);

            var em = systemState.EntityManager;
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (!pendingStates.TryGetValue(entity, out byte state))
                    continue;
                if (entity == Entity.Null || !em.Exists(entity))
                    continue;

                if (state == PendingDisable)
                {
                    if (em.HasComponent<PhysicsCollider>(entity))
                        disableCollider.Add(entity);
                    if (em.HasComponent<PhysicsVelocity>(entity))
                        disableVelocity.Add(entity);
                    continue;
                }

                if (state != PendingEnable || !em.HasComponent<RuntimeColliderSource>(entity))
                    continue;

                var source = em.GetComponentData<RuntimeColliderSource>(entity);
                if (!source.Value.IsCreated)
                    continue;

                var physicsCollider = new PhysicsCollider { Value = source.Value };
                bool resetInfo = true;
                if (em.HasComponent<PhysicsCollider>(entity))
                {
                    var current = em.GetComponentData<PhysicsCollider>(entity);
                    if (current.Value.Equals(physicsCollider.Value) && em.HasComponent<PhysicsWorldIndex>(entity))
                    {
                        resetInfo = false;
                    }
                    else
                    {
                        em.SetComponentData(entity, physicsCollider);
                    }
                }
                else
                {
                    addCollider.Add(entity);
                    addColliderValues.Add(physicsCollider);
                }

                if (!em.HasComponent<PhysicsWorldIndex>(entity))
                    addWorldIndex.Add(entity);
                QueueTemporalCoherence(em, entity, resetInfo, addTemporalTag, addTemporalInfo, resetTemporalInfo);
                QueueMotionClassification(em, entity, source.Kind, addVelocity, removeVelocity, removeStatic);
            }

            if (disableCollider.Length > 0)
                em.RemoveComponent(disableCollider.AsArray(), ComponentType.ReadWrite<PhysicsCollider>());
            if (disableVelocity.Length > 0)
                em.RemoveComponent(disableVelocity.AsArray(), ComponentType.ReadWrite<PhysicsVelocity>());
            if (addCollider.Length > 0)
            {
                em.AddComponent(addCollider.AsArray(), ComponentType.ReadWrite<PhysicsCollider>());
                for (int i = 0; i < addCollider.Length; i++)
                    em.SetComponentData(addCollider[i], addColliderValues[i]);
            }

            if (addWorldIndex.Length > 0)
                em.AddSharedComponentManaged(addWorldIndex.AsArray(), new PhysicsWorldIndex { Value = 0 });
            if (addTemporalTag.Length > 0)
                em.AddComponent(addTemporalTag.AsArray(), ComponentType.ReadWrite<PhysicsTemporalCoherenceTag>());
            if (addTemporalInfo.Length > 0)
                em.AddComponent(addTemporalInfo.AsArray(), ComponentType.ReadWrite<PhysicsTemporalCoherenceInfo>());
            for (int i = 0; i < resetTemporalInfo.Length; i++)
                em.SetComponentData(resetTemporalInfo[i], PhysicsTemporalCoherenceInfo.Default);
            if (removeStatic.Length > 0)
                em.RemoveComponent(removeStatic.AsArray(), ComponentType.ReadWrite<Unity.Transforms.Static>());
            if (removeVelocity.Length > 0)
                em.RemoveComponent(removeVelocity.AsArray(), ComponentType.ReadWrite<PhysicsVelocity>());
            if (addVelocity.Length > 0)
                em.AddComponent(addVelocity.AsArray(), ComponentType.ReadWrite<PhysicsVelocity>());

            pendingStates.Clear();
        }

        static void QueueTemporalCoherence(
            EntityManager em,
            Entity entity,
            bool resetInfo,
            NativeList<Entity> addTemporalTag,
            NativeList<Entity> addTemporalInfo,
            NativeList<Entity> resetTemporalInfo)
        {
            if (!em.HasComponent<PhysicsTemporalCoherenceTag>(entity))
                addTemporalTag.Add(entity);

            if (em.HasComponent<PhysicsTemporalCoherenceInfo>(entity))
            {
                if (resetInfo)
                    resetTemporalInfo.Add(entity);
            }
            else
            {
                addTemporalInfo.Add(entity);
            }
        }

        static void QueueMotionClassification(
            EntityManager em,
            Entity entity,
            RuntimeColliderKind kind,
            NativeList<Entity> addVelocity,
            NativeList<Entity> removeVelocity,
            NativeList<Entity> removeStatic)
        {
            if (!RequiresDynamicKinematicBody(kind))
            {
                if (em.HasComponent<PhysicsVelocity>(entity))
                    removeVelocity.Add(entity);
                return;
            }

            if (em.HasComponent<Unity.Transforms.Static>(entity))
                removeStatic.Add(entity);
            if (!em.HasComponent<PhysicsVelocity>(entity))
                addVelocity.Add(entity);
        }

        static bool RequiresDynamicKinematicBody(RuntimeColliderKind kind)
        {
            switch (kind)
            {
                case RuntimeColliderKind.Actor:
                case RuntimeColliderKind.Player:
                case RuntimeColliderKind.Projectile:
                    return true;
                default:
                    return false;
            }
        }

        void Apply(ref SystemState systemState, in RuntimePhysicsMutationRequest request)
        {
            switch (request.Kind)
            {
                case RuntimePhysicsMutationKind.Enable:
                    RuntimeColliderPhysicsUtility.EnablePhysics(systemState.EntityManager, request.Entity);
                    break;
                case RuntimePhysicsMutationKind.Disable:
                    RuntimeColliderPhysicsUtility.DisablePhysics(systemState.EntityManager, request.Entity);
                    break;
                case RuntimePhysicsMutationKind.AttachSource:
                    RuntimeColliderPhysicsUtility.AttachSource(
                        systemState.EntityManager,
                        request.Entity,
                        request.Collider,
                        request.ColliderKind,
                        request.Active != 0,
                        request.Temporary != 0);
                    break;
                case RuntimePhysicsMutationKind.SetPhysicsCollider:
                    ApplyPhysicsColliderSwap(ref systemState, request.Entity, request.Collider);
                    break;
                default:
                    throw new InvalidOperationException($"[VVardenfell][Physics] Unsupported runtime physics mutation kind {request.Kind}.");
            }
        }

        void ApplyPhysicsColliderSwap(ref SystemState systemState, Entity entity, BlobAssetReference<Collider> collider)
        {
            if (entity == Entity.Null || !systemState.EntityManager.Exists(entity))
                return;
            if (!collider.IsCreated)
                throw new InvalidOperationException("[VVardenfell][Physics] Cannot apply an empty PhysicsCollider swap.");

            var physicsCollider = new PhysicsCollider { Value = collider };
            if (systemState.EntityManager.HasComponent<PhysicsCollider>(entity))
            {
                systemState.EntityManager.SetComponentData(entity, physicsCollider);
                RuntimeColliderPhysicsUtility.EnsureTemporalCoherence(systemState.EntityManager, entity, resetInfo: true);
            }
            else
            {
                systemState.EntityManager.AddComponentData(entity, physicsCollider);
                RuntimeColliderPhysicsUtility.EnsureTemporalCoherence(systemState.EntityManager, entity, resetInfo: true);
            }

            if (!systemState.EntityManager.HasComponent<PhysicsWorldIndex>(entity))
                systemState.EntityManager.AddSharedComponent(entity, new PhysicsWorldIndex { Value = 0 });
            RuntimeColliderPhysicsUtility.EnsureTemporalCoherence(systemState.EntityManager, entity, resetInfo: false);
            if (!systemState.EntityManager.HasComponent<PhysicsVelocity>(entity))
                systemState.EntityManager.AddComponentData(entity, new PhysicsVelocity());
        }

    }
}
