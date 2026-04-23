using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.WorldState
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    [UpdateAfter(typeof(InteractionRuntimeBootstrapSystem))]
    public partial class RuntimeSpawnBootstrapSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            Entity runtimeEntity;
            if (SystemAPI.HasSingleton<PlayerInteractionFocus>())
            {
                runtimeEntity = SystemAPI.GetSingletonEntity<PlayerInteractionFocus>();
            }
            else if (SystemAPI.HasSingleton<RuntimeSpawnState>())
            {
                runtimeEntity = SystemAPI.GetSingletonEntity<RuntimeSpawnState>();
            }
            else
            {
                runtimeEntity = EntityManager.CreateEntity();
                EntityManager.SetName(runtimeEntity, "VVardenfell.RuntimeSpawn");
            }

            EnsureComponent(runtimeEntity, new RuntimeSpawnState());
            EnsureComponent(runtimeEntity, new RuntimeSpawnResult
            {
                LogicalEntity = Entity.Null,
            });
            EnsureBuffer<RuntimeSpawnRequest>(runtimeEntity);
            EnsureBuffer<RuntimeSpawnedRef>(runtimeEntity);
            Enabled = false;
        }

        void EnsureComponent<T>(Entity entity, T value)
            where T : unmanaged, IComponentData
        {
            if (!EntityManager.HasComponent<T>(entity))
                EntityManager.AddComponentData(entity, value);
        }

        void EnsureBuffer<T>(Entity entity)
            where T : unmanaged, IBufferElementData
        {
            if (!EntityManager.HasBuffer<T>(entity))
                EntityManager.AddBuffer<T>(entity);
        }
    }

    public static class RuntimeSpawnRegistryUtility
    {
        const uint RuntimeRefHighBit = 0x80000000u;

        public static uint ComposeRuntimeRefId(uint ordinal)
        {
            uint nonZeroOrdinal = ordinal == 0u ? 1u : ordinal;
            return RuntimeRefHighBit | (nonZeroOrdinal & ~RuntimeRefHighBit);
        }

        public static bool IsRuntimeRefId(uint value) => (value & RuntimeRefHighBit) != 0;

        public static void MarkDestroyed(EntityManager entityManager, uint runtimeRefId)
        {
            RuntimeSpawnProjectionUtility.MarkDestroyed(entityManager, runtimeRefId);
        }
    }

    static class RuntimeSpawnFactory
    {
        public static Entity Spawn(
            EntityManager entityManager,
            RuntimeContentDatabase contentDb,
            in WorldResources.RuntimeSpawnPrefabDescriptor descriptor,
            ContentReference contentReference,
            uint runtimeRefId,
            float3 position,
            quaternion rotation,
            float scale,
            bool isInterior,
            int2 exteriorCell,
            FixedString128Bytes interiorCellId,
            bool exteriorActive,
            ref LogicalRefLookup logicalRefLookup,
            Entity interiorTransitionEntity,
            byte persistencePolicy)
        {
            if (WorldResources.ModelPrefabs == null
                || (uint)descriptor.ModelPrefabIndex >= (uint)WorldResources.ModelPrefabs.Length)
                return Entity.Null;

            if (!WorldBootstrap.EnsureModelPrefabBuilt(entityManager, WorldResources.Cache, descriptor.ModelPrefabIndex))
                return Entity.Null;

            Entity renderRoot = entityManager.Instantiate(WorldResources.ModelPrefabs[descriptor.ModelPrefabIndex]);
            ApplyRenderRootMetadata(
                entityManager,
                renderRoot,
                descriptor,
                runtimeRefId,
                position,
                rotation,
                scale,
                isInterior,
                exteriorCell,
                exteriorActive);

            Entity logicalEntity = CreateLogicalEntity(
                entityManager,
                contentDb,
                contentReference,
                runtimeRefId,
                position,
                rotation,
                scale,
                isInterior,
                exteriorCell,
                interiorCellId,
                persistencePolicy);

            AppendLogicalChildren(entityManager, logicalEntity, renderRoot);
            InteractionActivationProxyBuildUtility.EnsureQueued(entityManager, logicalEntity);
            TryAddLogicalLookup(ref logicalRefLookup, runtimeRefId, logicalEntity);

            if (!isInterior && !exteriorActive)
                SetExteriorActiveState(entityManager, logicalEntity, false);

            if (isInterior)
                entityManager.GetBuffer<InteriorSpawnedEntity>(interiorTransitionEntity).Add(new InteriorSpawnedEntity { Value = logicalEntity });

            return logicalEntity;
        }

        static Entity CreateLogicalEntity(
            EntityManager entityManager,
            RuntimeContentDatabase contentDb,
            ContentReference contentReference,
            uint runtimeRefId,
            float3 position,
            quaternion rotation,
            float scale,
            bool isInterior,
            int2 exteriorCell,
            FixedString128Bytes interiorCellId,
            byte persistencePolicy)
        {
            Entity logicalEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(logicalEntity, new LogicalRefTag());
            entityManager.AddComponentData(logicalEntity, new PlacedRefIdentity { Value = runtimeRefId });
            entityManager.AddComponentData(logicalEntity, new RuntimeSpawnedRefIdentity
            {
                RuntimeRefId = runtimeRefId,
                PersistencePolicy = persistencePolicy,
            });
            entityManager.AddComponentData(logicalEntity, new LogicalRefContentRef { Value = contentReference });
            entityManager.AddComponentData(logicalEntity, new LogicalRefLocation
            {
                ExteriorCell = exteriorCell,
                InteriorCellId = interiorCellId,
                IsInterior = (byte)(isInterior ? 1 : 0),
            });
            entityManager.AddBuffer<LogicalRefChild>(logicalEntity);
            entityManager.AddComponentData(logicalEntity, LocalTransform.FromPositionRotationScale(position, rotation, scale));
            entityManager.AddComponentData(logicalEntity, new LocalToWorld
            {
                Value = float4x4.TRS(position, rotation, new float3(scale)),
            });

            if (isInterior)
                entityManager.AddComponent<InteriorCellMember>(logicalEntity);
            else
                entityManager.AddComponentData(logicalEntity, new CellLink { Value = exteriorCell });

            LogicalRefAuthoringUtility.TryAttach(entityManager, logicalEntity, contentDb, contentReference);
            return logicalEntity;
        }

        static void ApplyRenderRootMetadata(
            EntityManager entityManager,
            Entity renderRoot,
            in WorldResources.RuntimeSpawnPrefabDescriptor descriptor,
            uint runtimeRefId,
            float3 position,
            quaternion rotation,
            float scale,
            bool isInterior,
            int2 exteriorCell,
            bool exteriorActive)
        {
            entityManager.SetComponentData(renderRoot, LocalTransform.FromPositionRotationScale(position, rotation, scale));
            entityManager.SetComponentData(renderRoot, new LocalToWorld
            {
                Value = float4x4.TRS(position, rotation, new float3(scale)),
            });

            entityManager.AddComponentData(renderRoot, new PlacedRefIdentity { Value = runtimeRefId });
            if (isInterior)
            {
                if (!entityManager.HasComponent<InteriorCellMember>(renderRoot))
                    entityManager.AddComponent<InteriorCellMember>(renderRoot);
                if (entityManager.HasComponent<CellLink>(renderRoot))
                    entityManager.RemoveComponent<CellLink>(renderRoot);
            }
            else
            {
                if (entityManager.HasComponent<CellLink>(renderRoot))
                    entityManager.SetComponentData(renderRoot, new CellLink { Value = exteriorCell });
                else
                    entityManager.AddComponentData(renderRoot, new CellLink { Value = exteriorCell });
            }

            var colliderBlobs = WorldResources.ColliderBlobs ?? System.Array.Empty<BlobAssetReference<Unity.Physics.Collider>>();
            if ((uint)descriptor.CollisionIndex < (uint)colliderBlobs.Length && colliderBlobs[descriptor.CollisionIndex].IsCreated)
            {
                var colliderBlob = colliderBlobs[descriptor.CollisionIndex];
                RuntimeColliderAttachmentUtility.AttachSource(
                    entityManager,
                    renderRoot,
                    colliderBlob,
                    RuntimeColliderKind.RuntimeSpawn,
                    active: isInterior || exteriorActive);
            }

            if (!entityManager.HasBuffer<LinkedEntityGroup>(renderRoot))
                return;

            var linked = entityManager.GetBuffer<LinkedEntityGroup>(renderRoot);
            var linkedEntities = new NativeArray<Entity>(linked.Length, Allocator.Temp);
            for (int i = 0; i < linked.Length; i++)
                linkedEntities[i] = linked[i].Value;

            for (int i = 0; i < linkedEntities.Length; i++)
            {
                Entity child = linkedEntities[i];
                if (child == renderRoot || !entityManager.Exists(child))
                    continue;

                if (isInterior)
                {
                    if (!entityManager.HasComponent<InteriorCellMember>(child))
                        entityManager.AddComponent<InteriorCellMember>(child);
                    if (entityManager.HasComponent<CellLink>(child))
                        entityManager.RemoveComponent<CellLink>(child);
                }
                else
                {
                    if (entityManager.HasComponent<CellLink>(child))
                        entityManager.SetComponentData(child, new CellLink { Value = exteriorCell });
                    else
                        entityManager.AddComponentData(child, new CellLink { Value = exteriorCell });
                }
            }

            linkedEntities.Dispose();
        }

        static void AppendLogicalChildren(EntityManager entityManager, Entity logicalEntity, Entity renderRoot)
        {
            if (!entityManager.HasBuffer<LinkedEntityGroup>(renderRoot))
            {
                LinkLogicalChild(entityManager, logicalEntity, renderRoot);
                return;
            }

            var linked = entityManager.GetBuffer<LinkedEntityGroup>(renderRoot);
            var linkedEntities = new NativeArray<Entity>(linked.Length, Allocator.Temp);
            for (int i = 0; i < linked.Length; i++)
                linkedEntities[i] = linked[i].Value;

            for (int i = 0; i < linkedEntities.Length; i++)
                LinkLogicalChild(entityManager, logicalEntity, linkedEntities[i]);

            linkedEntities.Dispose();
        }

        static void LinkLogicalChild(EntityManager entityManager, Entity logicalEntity, Entity child)
        {
            if (child == Entity.Null || !entityManager.Exists(child))
                return;

            if (entityManager.HasComponent<LogicalRefParent>(child))
                entityManager.SetComponentData(child, new LogicalRefParent { Value = logicalEntity });
            else
                entityManager.AddComponentData(child, new LogicalRefParent { Value = logicalEntity });

            entityManager.GetBuffer<LogicalRefChild>(logicalEntity).Add(new LogicalRefChild { Value = child });
        }

        static void TryAddLogicalLookup(ref LogicalRefLookup logicalRefLookup, uint runtimeRefId, Entity logicalEntity)
        {
            if (!logicalRefLookup.Map.IsCreated || runtimeRefId == 0u)
                return;

            if (logicalRefLookup.Map.TryAdd(runtimeRefId, logicalEntity))
                return;

            logicalRefLookup.Map.Remove(runtimeRefId);
            logicalRefLookup.Map.TryAdd(runtimeRefId, logicalEntity);
        }

        public static void SetExteriorActiveState(EntityManager entityManager, Entity logicalEntity, bool active)
        {
            if (logicalEntity == Entity.Null || !entityManager.Exists(logicalEntity))
                return;

            if (!entityManager.HasBuffer<LogicalRefChild>(logicalEntity))
                return;

            var children = entityManager.GetBuffer<LogicalRefChild>(logicalEntity);
            var childEntities = new NativeArray<Entity>(children.Length, Allocator.Temp);
            for (int i = 0; i < children.Length; i++)
                childEntities[i] = children[i].Value;

            for (int i = 0; i < childEntities.Length; i++)
            {
                Entity child = childEntities[i];
                if (child == Entity.Null || !entityManager.Exists(child))
                    continue;

                if (entityManager.HasComponent<MaterialMeshInfo>(child))
                    entityManager.SetComponentEnabled<MaterialMeshInfo>(child, active);

                if (entityManager.HasComponent<RuntimeColliderSource>(child))
                {
                    if (active)
                        RuntimeColliderAttachmentUtility.EnablePhysics(entityManager, child);
                    else
                        RuntimeColliderAttachmentUtility.DisablePhysics(entityManager, child);
                }
            }

            childEntities.Dispose();
        }
    }

    [UpdateInGroup(typeof(MorrowindWorldMutationSystemGroup))]
    public partial class RuntimeSpawnRequestSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<RuntimeSpawnState>();
            RequireForUpdate<RuntimeSpawnResult>();
            RequireForUpdate<RuntimeSpawnRequest>();
            RequireForUpdate<RuntimeSpawnedRef>();
            RequireForUpdate<WorldJournalEntry>();
            RequireForUpdate<LogicalRefLookup>();
            RequireForUpdate<LoadedCellsMap>();
            RequireForUpdate<AvailableCells>();
            RequireForUpdate<StreamingConfig>();
            RequireForUpdate<InteriorTransitionState>();
            RequireForUpdate<InteriorSpawnedEntity>();
        }

        protected override void OnUpdate()
        {
            var contentDb = RuntimeContentDatabase.Active;
            var spawnEntity = SystemAPI.GetSingletonEntity<RuntimeSpawnState>();
            ref var spawnState = ref SystemAPI.GetSingletonRW<RuntimeSpawnState>().ValueRW;
            ref var spawnResult = ref SystemAPI.GetSingletonRW<RuntimeSpawnResult>().ValueRW;
            var requests = EntityManager.GetBuffer<RuntimeSpawnRequest>(spawnEntity);
            if (requests.Length == 0)
                return;

            Entity lookupEntity = SystemAPI.GetSingletonEntity<LogicalRefLookup>();
            var logicalLookup = EntityManager.GetComponentData<LogicalRefLookup>(lookupEntity);
            var spawnedRegistry = EntityManager.GetBuffer<RuntimeSpawnedRef>(spawnEntity);
            Entity transitionEntity = SystemAPI.GetSingletonEntity<InteriorTransitionState>();
            var interiorTransition = SystemAPI.GetSingleton<InteriorTransitionState>();
            var loaded = SystemAPI.GetSingleton<LoadedCellsMap>();
            var available = SystemAPI.GetSingleton<AvailableCells>();
            var config = SystemAPI.GetSingleton<StreamingConfig>();

            for (int i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                ProcessRequest(
                    contentDb,
                    ref spawnState,
                    ref spawnResult,
                    ref logicalLookup,
                    spawnedRegistry,
                    transitionEntity,
                    loaded,
                    available,
                    config,
                    interiorTransition,
                    request);
            }

            requests.Clear();
            EntityManager.SetComponentData(lookupEntity, logicalLookup);
        }

        void ProcessRequest(
            RuntimeContentDatabase contentDb,
            ref RuntimeSpawnState spawnState,
            ref RuntimeSpawnResult spawnResult,
            ref LogicalRefLookup logicalLookup,
            DynamicBuffer<RuntimeSpawnedRef> spawnedRegistry,
            Entity transitionEntity,
            LoadedCellsMap loaded,
            AvailableCells available,
            StreamingConfig config,
            InteriorTransitionState interiorTransition,
            in RuntimeSpawnRequest request)
        {
            spawnResult = new RuntimeSpawnResult
            {
                Sequence = request.Sequence,
                LogicalEntity = Entity.Null,
            };

            if (contentDb == null)
            {
                CompleteFailure(ref spawnResult, RuntimeSpawnResultStatus.NotReady, "Runtime content database is not ready.");
                return;
            }

            if (!contentDb.IsValid(request.Content))
            {
                CompleteFailure(ref spawnResult, RuntimeSpawnResultStatus.InvalidContent, "Spawn content reference is invalid.");
                return;
            }

            if (request.PersistencePolicy != (byte)RuntimeSpawnPersistencePolicy.CellOwnedSession)
            {
                CompleteFailure(ref spawnResult, RuntimeSpawnResultStatus.Unsupported, "Only cell-owned session runtime spawns are supported in this pass.");
                return;
            }

            if (request.Content.Kind == ContentReferenceKind.Actor)
            {
                var actorHandle = new ActorDefHandle { Value = request.Content.HandleValue };
                ref readonly var actor = ref contentDb.Get(actorHandle);
                if (actor.Kind != ActorDefKind.Creature)
                {
                    CompleteFailure(ref spawnResult, RuntimeSpawnResultStatus.Unsupported, "NPC runtime spawning is deferred until the actor body-composition milestone.");
                    return;
                }
            }
            else if (request.Content.Kind != ContentReferenceKind.Item && request.Content.Kind != ContentReferenceKind.Light)
            {
                CompleteFailure(ref spawnResult, RuntimeSpawnResultStatus.Unsupported, "This runtime spawn surface currently supports creatures, items, and lights only.");
                return;
            }

            if (!WorldResources.TryGetRuntimeSpawnPrefab(request.Content, out var descriptor))
            {
                CompleteFailure(ref spawnResult, RuntimeSpawnResultStatus.MissingPrefab, "No spawnable model prefab is available for that content definition.");
                return;
            }

            if (request.IsInterior != 0)
            {
                if (interiorTransition.InteriorActive == 0 || interiorTransition.ActiveInteriorCellId != request.InteriorCellId)
                {
                    CompleteFailure(ref spawnResult, RuntimeSpawnResultStatus.InvalidLocation, "Runtime interior spawns currently require the requested interior to be the active loaded interior.");
                    return;
                }
            }
            else
            {
                EnsureExteriorCapacity(ref available);
                available.Set.Add(request.ExteriorCell);
            }

            spawnState.NextRuntimeRefId += 1u;
            uint runtimeRefId = RuntimeSpawnRegistryUtility.ComposeRuntimeRefId(spawnState.NextRuntimeRefId);
            bool exteriorActive = request.IsInterior != 0 || IsExteriorCellActiveNow(config, request.ExteriorCell);
            if (request.IsInterior == 0 && exteriorActive)
                loaded.Active.Add(request.ExteriorCell);

            Entity logicalEntity = RuntimeSpawnFactory.Spawn(
                EntityManager,
                contentDb,
                descriptor,
                request.Content,
                runtimeRefId,
                request.Position,
                request.Rotation,
                math.max(0.0001f, request.Scale),
                request.IsInterior != 0,
                request.ExteriorCell,
                request.InteriorCellId,
                exteriorActive,
                ref logicalLookup,
                transitionEntity,
                request.PersistencePolicy);

            if (logicalEntity == Entity.Null)
            {
                CompleteFailure(ref spawnResult, RuntimeSpawnResultStatus.NotReady, "Runtime spawn failed while constructing the logical ref graph.");
                return;
            }

            spawnedRegistry.Add(new RuntimeSpawnedRef
            {
                RuntimeRefId = runtimeRefId,
                Content = request.Content,
                Position = request.Position,
                Rotation = request.Rotation,
                Scale = request.Scale,
                ExteriorCell = request.ExteriorCell,
                InteriorCellId = request.InteriorCellId,
                LogicalEntity = logicalEntity,
                IsInterior = request.IsInterior,
                PersistencePolicy = request.PersistencePolicy,
                Alive = 1,
            });
            WorldJournalUtility.AppendRuntimeSpawn(EntityManager, spawnedRegistry[spawnedRegistry.Length - 1]);

            spawnResult.RuntimeRefId = runtimeRefId;
            spawnResult.LogicalEntity = logicalEntity;
            spawnResult.Status = (byte)RuntimeSpawnResultStatus.Success;
            spawnResult.Message = new FixedString128Bytes($"Spawned runtime ref 0x{runtimeRefId:X8}.");

            string label = request.Content.Kind.ToString().ToLowerInvariant();
            Debug.Log(
                request.IsInterior != 0
                    ? $"[VVardenfell][Spawn] spawned {label} runtime ref 0x{runtimeRefId:X8} in interior '{request.InteriorCellId}'."
                    : $"[VVardenfell][Spawn] spawned {label} runtime ref 0x{runtimeRefId:X8} in exterior cell ({request.ExteriorCell.x},{request.ExteriorCell.y}).");
        }

        static bool IsExteriorCellActiveNow(in StreamingConfig config, int2 exteriorCell)
        {
            if (config.ExteriorStreamingPaused)
                return false;

            int dx = math.abs(exteriorCell.x - config.CameraCell.x);
            int dy = math.abs(exteriorCell.y - config.CameraCell.y);
            return dx <= config.ViewRadius && dy <= config.ViewRadius;
        }

        static void EnsureExteriorCapacity(ref AvailableCells available)
        {
            if (!available.Set.IsCreated)
                return;

            int count = available.Set.Count;
            if (count < available.Set.Capacity)
                return;

            available.Set.Capacity = math.max(available.Set.Capacity * 2, count + 1);
        }

        static void CompleteFailure(ref RuntimeSpawnResult result, RuntimeSpawnResultStatus status, string message)
        {
            result.Status = (byte)status;
            result.Message = ToFixedString(message);
            Debug.LogWarning($"[VVardenfell][Spawn] {message}");
        }

        static FixedString128Bytes ToFixedString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return default;

            if (value.Length > 127)
                value = value.Substring(0, 127);

            return new FixedString128Bytes(value);
        }
    }
}
