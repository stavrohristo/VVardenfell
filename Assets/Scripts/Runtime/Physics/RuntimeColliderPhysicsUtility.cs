using Unity.Entities;
using Unity.Physics;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Physics
{
    public static class RuntimeColliderPhysicsUtility
    {
        public static bool AttachSource(
            EntityManager entityManager,
            Entity entity,
            BlobAssetReference<Collider> collider,
            RuntimeColliderKind kind,
            bool active,
            bool temporary = false)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity) || !collider.IsCreated)
                return false;

            collider = PrepareColliderForKind(collider, kind, ref temporary);
            QueueReplacedTemporarySourceForDisposal(entityManager, entity, collider);

            var source = new RuntimeColliderSource
            {
                Value = collider,
                Kind = kind,
                Temporary = temporary ? (byte)1 : (byte)0,
            };

            if (entityManager.HasComponent<RuntimeColliderSource>(entity))
                entityManager.SetComponentData(entity, source);
            else
                entityManager.AddComponentData(entity, source);

            AttachGeneratedBlobCleanup(entityManager, entity, collider, temporary);

            if (active)
                return EnablePhysics(entityManager, entity);

            DisablePhysics(entityManager, entity);
            return true;
        }

        public static bool QueueAttachSource(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            Entity entity,
            BlobAssetReference<Collider> collider,
            RuntimeColliderKind kind,
            bool active,
            bool temporary = false)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity) || !collider.IsCreated)
                return false;

            collider = PrepareColliderForKind(collider, kind, ref temporary);
            QueueReplacedTemporarySourceForDisposal(entityManager, entity, collider);

            var source = new RuntimeColliderSource
            {
                Value = collider,
                Kind = kind,
                Temporary = temporary ? (byte)1 : (byte)0,
            };

            if (entityManager.HasComponent<RuntimeColliderSource>(entity))
                ecb.SetComponent(entity, source);
            else
                ecb.AddComponent(entity, source);

            QueueAttachGeneratedBlobCleanup(entityManager, ref ecb, entity, collider, temporary);

            if (active)
                RuntimePhysicsMutationQueueUtility.QueueEnablePhysics(entityManager, ref ecb, entity);
            else
                RuntimePhysicsMutationQueueUtility.QueueDisablePhysics(entityManager, ref ecb, entity);
            return true;
        }

        public static void QueueAttachNewSource(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            Entity entity,
            BlobAssetReference<Collider> collider,
            RuntimeColliderKind kind,
            bool active,
            bool temporary = false)
        {
            if (entity == Entity.Null || !collider.IsCreated)
                return;

            collider = PrepareColliderForKind(collider, kind, ref temporary);
            ecb.AddComponent(entity, new RuntimeColliderSource
            {
                Value = collider,
                Kind = kind,
                Temporary = temporary ? (byte)1 : (byte)0,
            });

            if (temporary)
            {
                ecb.AddComponent(entity, new RuntimeGeneratedColliderBlobCleanup
                {
                    Value = collider,
                });
            }

            if (active)
                RuntimePhysicsMutationQueueUtility.QueueEnablePhysics(entityManager, ref ecb, entity);
        }

        public static void QueueAttachInstantiatedSource(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            Entity prefab,
            Entity instance,
            BlobAssetReference<Collider> collider,
            RuntimeColliderKind kind,
            bool active,
            bool temporary = false)
        {
            if (instance == Entity.Null || !collider.IsCreated)
                return;

            collider = PrepareColliderForKind(collider, kind, ref temporary);
            if (entityManager.Exists(instance))
                QueueReplacedTemporarySourceForDisposal(entityManager, instance, collider);

            var source = new RuntimeColliderSource
            {
                Value = collider,
                Kind = kind,
                Temporary = temporary ? (byte)1 : (byte)0,
            };

            if (entityManager.HasComponent<RuntimeColliderSource>(prefab))
                ecb.SetComponent(instance, source);
            else
                ecb.AddComponent(instance, source);

            QueueAttachGeneratedBlobCleanup(entityManager, ref ecb, instance, collider, temporary);

            if (active)
                RuntimePhysicsMutationQueueUtility.QueueEnablePhysics(entityManager, ref ecb, instance);
            else if (entityManager.HasComponent<PhysicsCollider>(prefab))
                RuntimePhysicsMutationQueueUtility.QueueDisablePhysics(entityManager, ref ecb, instance);
        }

        public static bool EnablePhysics(EntityManager entityManager, Entity entity)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity))
                return false;
            if (!entityManager.HasComponent<RuntimeColliderSource>(entity))
                return false;

            var source = entityManager.GetComponentData<RuntimeColliderSource>(entity);
            if (!source.Value.IsCreated)
                return false;

            var collider = new PhysicsCollider { Value = source.Value };
            if (entityManager.HasComponent<PhysicsCollider>(entity))
            {
                var current = entityManager.GetComponentData<PhysicsCollider>(entity);
                if (current.Value.Equals(collider.Value)
                    && entityManager.HasComponent<PhysicsWorldIndex>(entity))
                {
                    EnsureTemporalCoherence(entityManager, entity, resetInfo: false);
                    EnsureMotionClassification(entityManager, entity, source.Kind);
                    return true;
                }

                entityManager.SetComponentData(entity, collider);
                EnsureTemporalCoherence(entityManager, entity, resetInfo: true);
            }
            else
            {
                entityManager.AddComponentData(entity, collider);
                EnsureTemporalCoherence(entityManager, entity, resetInfo: true);
            }

            if (!entityManager.HasComponent<PhysicsWorldIndex>(entity))
                entityManager.AddSharedComponent(entity, new PhysicsWorldIndex { Value = 0 });
            EnsureTemporalCoherence(entityManager, entity, resetInfo: false);
            EnsureMotionClassification(entityManager, entity, source.Kind);

            return true;
        }

        public static void EnsureTemporalCoherence(EntityManager entityManager, Entity entity, bool resetInfo)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity))
                return;

            if (!entityManager.HasComponent<PhysicsTemporalCoherenceTag>(entity))
                entityManager.AddComponent<PhysicsTemporalCoherenceTag>(entity);

            if (entityManager.HasComponent<PhysicsTemporalCoherenceInfo>(entity))
            {
                if (resetInfo)
                    entityManager.SetComponentData(entity, PhysicsTemporalCoherenceInfo.Default);
            }
            else
            {
                entityManager.AddComponentData(entity, PhysicsTemporalCoherenceInfo.Default);
            }
        }

        public static bool QueueEnablePhysics(EntityManager entityManager, ref EntityCommandBuffer ecb, Entity entity)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity))
                return false;
            if (!entityManager.HasComponent<RuntimeColliderSource>(entity))
                return false;

            return RuntimePhysicsMutationQueueUtility.QueueEnablePhysics(entityManager, ref ecb, entity);
        }

        public static void DisablePhysics(EntityManager entityManager, Entity entity)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity))
                return;
            if (entityManager.HasComponent<PhysicsCollider>(entity))
                entityManager.RemoveComponent<PhysicsCollider>(entity);
            if (entityManager.HasComponent<PhysicsVelocity>(entity))
                entityManager.RemoveComponent<PhysicsVelocity>(entity);
        }

        public static void QueueDisablePhysics(EntityManager entityManager, ref EntityCommandBuffer ecb, Entity entity)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity))
                return;
            RuntimePhysicsMutationQueueUtility.QueueDisablePhysics(entityManager, ref ecb, entity);
        }

        static void QueueReplacedTemporarySourceForDisposal(
            EntityManager entityManager,
            Entity entity,
            BlobAssetReference<Collider> replacement)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity))
                return;
            if (!entityManager.HasComponent<RuntimeColliderSource>(entity))
                return;

            var previous = entityManager.GetComponentData<RuntimeColliderSource>(entity);
            if (previous.Temporary == 0 || !previous.Value.IsCreated || previous.Value.Equals(replacement))
                return;

            RuntimeColliderBlobLifetime.DeferGeneratedBlobDisposal(entityManager, previous.Value);
        }

        static void AttachGeneratedBlobCleanup(
            EntityManager entityManager,
            Entity entity,
            BlobAssetReference<Collider> collider,
            bool temporary)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity))
                return;

            if (!temporary)
            {
                if (entityManager.HasComponent<RuntimeGeneratedColliderBlobCleanup>(entity))
                    entityManager.RemoveComponent<RuntimeGeneratedColliderBlobCleanup>(entity);
                return;
            }

            var cleanup = new RuntimeGeneratedColliderBlobCleanup
            {
                Value = collider,
            };
            if (entityManager.HasComponent<RuntimeGeneratedColliderBlobCleanup>(entity))
                entityManager.SetComponentData(entity, cleanup);
            else
                entityManager.AddComponentData(entity, cleanup);
        }

        static void QueueAttachGeneratedBlobCleanup(
            EntityManager entityManager,
            ref EntityCommandBuffer ecb,
            Entity entity,
            BlobAssetReference<Collider> collider,
            bool temporary)
        {
            if (entity == Entity.Null)
                return;

            if (!entityManager.Exists(entity))
            {
                if (temporary)
                    ecb.AddComponent(entity, new RuntimeGeneratedColliderBlobCleanup { Value = collider });
                return;
            }

            if (!temporary)
            {
                if (entityManager.HasComponent<RuntimeGeneratedColliderBlobCleanup>(entity))
                    ecb.RemoveComponent<RuntimeGeneratedColliderBlobCleanup>(entity);
                return;
            }

            var cleanup = new RuntimeGeneratedColliderBlobCleanup
            {
                Value = collider,
            };
            if (entityManager.HasComponent<RuntimeGeneratedColliderBlobCleanup>(entity))
                ecb.SetComponent(entity, cleanup);
            else
                ecb.AddComponent(entity, cleanup);
        }

        static BlobAssetReference<Collider> PrepareColliderForKind(
            BlobAssetReference<Collider> collider,
            RuntimeColliderKind kind,
            ref bool temporary)
        {
            if (!TryGetFilterForKind(kind, out var filter))
                return collider;

            var current = collider.Value.GetCollisionFilter();
            if (current.BelongsTo == filter.BelongsTo
                && current.CollidesWith == filter.CollidesWith
                && current.GroupIndex == filter.GroupIndex)
            {
                return collider;
            }

            if (RequiresKindSpecificColliderBlob(kind) && !temporary)
            {
                collider = collider.Value.Clone();
                temporary = true;
            }

            collider.Value.SetCollisionFilter(filter);
            return collider;
        }

        static bool TryGetFilterForKind(RuntimeColliderKind kind, out CollisionFilter filter)
        {
            switch (kind)
            {
                case RuntimeColliderKind.TerrainCell:
                case RuntimeColliderKind.StaticCell:
                case RuntimeColliderKind.PlacedRef:
                    filter = InteractionCollisionLayers.GeometryFilter;
                    return true;
                case RuntimeColliderKind.RuntimeSpawn:
                    filter = InteractionCollisionLayers.DynamicRefFilter;
                    return true;
                case RuntimeColliderKind.ActivationProxy:
                    filter = InteractionCollisionLayers.ActivationProxyFilter;
                    return true;
                case RuntimeColliderKind.InteractionPick:
                    filter = InteractionCollisionLayers.InteractionPickFilter;
                    return true;
                case RuntimeColliderKind.Projectile:
                    filter = InteractionCollisionLayers.ProjectileFilter;
                    return true;
                case RuntimeColliderKind.Player:
                case RuntimeColliderKind.Actor:
                    filter = InteractionCollisionLayers.PlayerBodyFilter;
                    return true;
                default:
                    filter = default;
                    return false;
            }
        }

        static bool RequiresKindSpecificColliderBlob(RuntimeColliderKind kind)
            => kind == RuntimeColliderKind.RuntimeSpawn;

        static void EnsureMotionClassification(EntityManager entityManager, Entity entity, RuntimeColliderKind kind)
        {
            if (!RequiresDynamicKinematicBody(entityManager, entity, kind))
            {
                if (entityManager.HasComponent<PhysicsVelocity>(entity))
                    entityManager.RemoveComponent<PhysicsVelocity>(entity);
                return;
            }

            if (entityManager.HasComponent<Unity.Transforms.Static>(entity))
                entityManager.RemoveComponent<Unity.Transforms.Static>(entity);
            if (!entityManager.HasComponent<PhysicsVelocity>(entity))
                entityManager.AddComponentData(entity, new PhysicsVelocity());
        }

        static bool RequiresDynamicKinematicBody(EntityManager entityManager, Entity entity, RuntimeColliderKind kind)
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
    }
}
