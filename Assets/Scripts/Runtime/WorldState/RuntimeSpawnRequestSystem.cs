using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.WorldState
{
    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup))]
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
            RequireForUpdate<RuntimeContentBlobReference>();
        }

        protected override void OnUpdate()
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][ContentBlob] Runtime spawn requests require runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;
            var spawnEntity = SystemAPI.GetSingletonEntity<RuntimeSpawnState>();
            var spawnState = SystemAPI.GetSingleton<RuntimeSpawnState>();
            var spawnResult = SystemAPI.GetSingleton<RuntimeSpawnResult>();
            var requests = EntityManager.GetBuffer<RuntimeSpawnRequest>(spawnEntity);
            if (requests.Length == 0)
                return;

            var requestSnapshot = new RuntimeSpawnRequest[requests.Length];
            for (int i = 0; i < requests.Length; i++)
                requestSnapshot[i] = requests[i];
            requests.Clear();

            Entity lookupEntity = SystemAPI.GetSingletonEntity<LogicalRefLookup>();
            var logicalLookup = EntityManager.GetComponentData<LogicalRefLookup>(lookupEntity);
            Entity loadedEntity = SystemAPI.GetSingletonEntity<LoadedCellsMap>();
            Entity transitionEntity = SystemAPI.GetSingletonEntity<InteriorTransitionState>();
            var interiorTransition = SystemAPI.GetSingleton<InteriorTransitionState>();
            var loaded = SystemAPI.GetSingleton<LoadedCellsMap>();
            uint startActiveRevision = loaded.ActiveRevision;
            var available = SystemAPI.GetSingleton<AvailableCells>();
            var config = SystemAPI.GetSingleton<StreamingConfig>();
            bool activeExplicitRefsDirty = false;

            for (int i = 0; i < requestSnapshot.Length; i++)
            {
                var request = requestSnapshot[i];
                ProcessRequest(
                    spawnEntity,
                    ref content,
                    ref spawnState,
                    ref spawnResult,
                    ref logicalLookup,
                    transitionEntity,
                    ref loaded,
                    available,
                    config,
                    interiorTransition,
                    ref activeExplicitRefsDirty,
                    request);
            }

            EntityManager.SetComponentData(spawnEntity, spawnState);
            EntityManager.SetComponentData(spawnEntity, spawnResult);
            EntityManager.SetComponentData(lookupEntity, logicalLookup);
            EntityManager.SetComponentData(loadedEntity, loaded);
            if (activeExplicitRefsDirty || loaded.ActiveRevision != startActiveRevision)
                ActiveExplicitRefLookupLifecycleUtility.MarkDirty(EntityManager);
        }

        void ProcessRequest(
            Entity spawnEntity,
            ref RuntimeContentBlob content,
            ref RuntimeSpawnState spawnState,
            ref RuntimeSpawnResult spawnResult,
            ref LogicalRefLookup logicalLookup,
            Entity transitionEntity,
            ref LoadedCellsMap loaded,
            AvailableCells available,
            StreamingConfig config,
            InteriorTransitionState interiorTransition,
            ref bool activeExplicitRefsDirty,
            in RuntimeSpawnRequest request)
        {
            spawnResult = new RuntimeSpawnResult
            {
                Sequence = request.Sequence,
                LogicalEntity = Entity.Null,
            };

            if (!RuntimeContentBlobUtility.IsValid(ref content, request.Content))
            {
                CompleteFailure(ref spawnResult, RuntimeSpawnResultStatus.InvalidContent, "Spawn content reference is invalid.");
                return;
            }

            if (request.PersistencePolicy != (byte)RuntimeSpawnPersistencePolicy.CellOwnedSession)
            {
                CompleteFailure(ref spawnResult, RuntimeSpawnResultStatus.Unsupported, "Only cell-owned session runtime spawns are supported in this pass.");
                return;
            }

            bool actorSpawn = request.Content.Kind == ContentReferenceKind.Actor;
            if (!actorSpawn && request.Content.Kind != ContentReferenceKind.Item && request.Content.Kind != ContentReferenceKind.Light)
            {
                CompleteFailure(ref spawnResult, RuntimeSpawnResultStatus.Unsupported, "This runtime spawn surface currently supports actors, items, and lights only.");
                return;
            }

            WorldResources.RuntimeSpawnPrefabDescriptor descriptor = default;
            if (!actorSpawn && !WorldResources.TryGetRuntimeSpawnPrefab(request.Content, out descriptor))
            {
                CompleteFailure(ref spawnResult, RuntimeSpawnResultStatus.MissingPrefab, "No spawnable model prefab is available for that content definition.");
                return;
            }

            if (request.IsInterior != 0)
            {
                if (interiorTransition.InteriorActive == 0 || interiorTransition.ActiveInteriorCellHash != request.InteriorCellHash)
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
            {
                if (loaded.Active.Add(request.ExteriorCell))
                {
                    loaded.ActiveRevision++;
                    activeExplicitRefsDirty = true;
                }
            }

            var createEcb = new EntityCommandBuffer(Allocator.Temp);
            bool queued = actorSpawn
                ? RuntimeSpawnFactory.QueueActorSpawn(
                    EntityManager,
                    ref createEcb,
                    ref content,
                    request.Content,
                    runtimeRefId,
                    request.Position,
                    request.Rotation,
                    math.max(0.0001f, request.Scale),
                    request.IsInterior != 0,
                    request.ExteriorCell,
                    request.InteriorCellId,
                    request.PersistencePolicy)
                : RuntimeSpawnFactory.QueueSpawn(
                    EntityManager,
                    ref createEcb,
                    ref content,
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
            createEcb.Playback(EntityManager);
            createEcb.Dispose();

            if (!queued)
            {
                CompleteFailure(ref spawnResult, RuntimeSpawnResultStatus.NotReady, "Runtime spawn failed while constructing the logical ref graph.");
                return;
            }

            var materializeEcb = new EntityCommandBuffer(Allocator.Temp);
            Entity logicalEntity = RuntimeSpawnFactory.QueueMaterializeSpawn(
                EntityManager,
                ref materializeEcb,
                runtimeRefId,
                request.IsInterior != 0,
                request.ExteriorCell,
                exteriorActive,
                ref logicalLookup,
                transitionEntity);
            materializeEcb.Playback(EntityManager);
            materializeEcb.Dispose();

            if (logicalEntity == Entity.Null)
            {
                CompleteFailure(ref spawnResult, RuntimeSpawnResultStatus.NotReady, "Runtime spawn failed while materializing the logical ref graph.");
                return;
            }

            var spawnedRegistry = EntityManager.GetBuffer<RuntimeSpawnedRef>(spawnEntity);
            spawnedRegistry.Add(new RuntimeSpawnedRef
            {
                RuntimeRefId = runtimeRefId,
                Content = request.Content,
                Position = request.Position,
                Rotation = request.Rotation,
                Scale = request.Scale,
                ExteriorCell = request.ExteriorCell,
                InteriorCellId = request.InteriorCellId,
                InteriorCellHash = request.InteriorCellHash,
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
            activeExplicitRefsDirty = true;
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
            result.Message = RuntimeFixedStringUtility.ToFixed128OrDefault(message);
            Debug.LogWarning($"[VVardenfell][Spawn] {message}");
        }
    }
}
