using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindWorldMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptInterpreterSystem))]
    public partial class MorrowindScriptRefStateApplySystem : SystemBase
    {
        EntityQuery _runtimeQuery;
        EntityQuery _logicalRefQuery;

        protected override void OnCreate()
        {
            _runtimeQuery = GetEntityQuery(
                ComponentType.ReadOnly<MorrowindScriptRuntimeState>(),
                ComponentType.ReadWrite<MorrowindScriptRefStateRequest>());
            _logicalRefQuery = GetEntityQuery(
                ComponentType.ReadOnly<LogicalRefTag>(),
                ComponentType.ReadOnly<PlacedRefIdentity>(),
                ComponentType.ReadWrite<PlacedRefRuntimeState>(),
                ComponentType.ReadOnly<LogicalRefLocation>());

            RequireForUpdate(_runtimeQuery);
            RequireForUpdate(_logicalRefQuery);
            RequireForUpdate<LogicalRefLookup>();
            RequireForUpdate<PlacedRefRuntimeStateLookup>();
            RequireForUpdate<LoadedCellsMap>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = _runtimeQuery.GetSingletonEntity();
            var requests = EntityManager.GetBuffer<MorrowindScriptRefStateRequest>(runtimeEntity);
            var logicalRefLookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            var stateLookup = SystemAPI.GetSingleton<PlacedRefRuntimeStateLookup>();
            if (requests.Length == 0
                && (!stateLookup.DisabledByPlacedRef.IsCreated || stateLookup.DisabledByPlacedRef.Count() == 0))
            {
                return;
            }

            var loadedCells = SystemAPI.GetSingleton<LoadedCellsMap>();
            byte interiorActive = 0;
            ulong activeInteriorCellHash = 0;
            if (SystemAPI.TryGetSingleton<InteriorTransitionState>(out var interiorTransition) && interiorTransition.InteriorActive != 0)
            {
                interiorActive = 1;
                activeInteriorCellHash = interiorTransition.ActiveInteriorCellHash;
            }

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            for (int i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                if (request.TargetPlacedRefId == 0u)
                    continue;

                stateLookup.DisabledByPlacedRef[request.TargetPlacedRefId] = request.Disabled;
                Entity target = ResolveLiveTarget(request, logicalRefLookup);
                if (target == Entity.Null || !EntityManager.Exists(target))
                    continue;

                CommitAndProject(
                    ref ecb,
                    target,
                    request.TargetPlacedRefId,
                    request.Disabled,
                    loadedCells,
                    interiorActive,
                    activeInteriorCellHash);
            }

            requests.Clear();
            ProjectReloadedRefs(
                ref ecb,
                stateLookup,
                loadedCells,
                interiorActive,
                activeInteriorCellHash);

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        Entity ResolveLiveTarget(in MorrowindScriptRefStateRequest request, in LogicalRefLookup lookup)
        {
            if (request.TargetEntity != Entity.Null && EntityManager.Exists(request.TargetEntity))
                return request.TargetEntity;

            if (lookup.Map.IsCreated && lookup.Map.TryGetValue(request.TargetPlacedRefId, out Entity target))
                return target;

            return Entity.Null;
        }

        void ProjectReloadedRefs(
            ref EntityCommandBuffer ecb,
            in PlacedRefRuntimeStateLookup stateLookup,
            in LoadedCellsMap loadedCells,
            byte interiorActive,
            ulong activeInteriorCellHash)
        {
            using var entities = _logicalRefQuery.ToEntityArray(Allocator.Temp);
            using var identities = _logicalRefQuery.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
            using var states = _logicalRefQuery.ToComponentDataArray<PlacedRefRuntimeState>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                uint placedRefId = identities[i].Value;
                if (placedRefId == 0u
                    || !stateLookup.DisabledByPlacedRef.IsCreated
                    || !stateLookup.DisabledByPlacedRef.TryGetValue(placedRefId, out byte disabled)
                    || disabled == states[i].Disabled)
                {
                    continue;
                }

                CommitAndProject(
                    ref ecb,
                    entities[i],
                    placedRefId,
                    disabled,
                    loadedCells,
                    interiorActive,
                    activeInteriorCellHash);
            }
        }

        void CommitAndProject(
            ref EntityCommandBuffer ecb,
            Entity logicalEntity,
            uint placedRefId,
            byte disabled,
            in LoadedCellsMap loadedCells,
            byte interiorActive,
            ulong activeInteriorCellHash)
        {
            if (!EntityManager.Exists(logicalEntity))
                return;

            if (!EntityManager.HasComponent<PlacedRefRuntimeState>(logicalEntity))
            {
                Debug.LogWarning($"[VVardenfell][MWScript] placed ref 0x{placedRefId:X8} is missing PlacedRefRuntimeState.");
                return;
            }

            ecb.SetComponent(logicalEntity, new PlacedRefRuntimeState { Disabled = disabled });
            if (disabled != 0)
            {
                ProjectLogicalRef(ref ecb, logicalEntity, false);
                return;
            }

            if (IsLocationActive(logicalEntity, loadedCells, interiorActive, activeInteriorCellHash))
                ProjectLogicalRef(ref ecb, logicalEntity, true);
        }

        bool IsLocationActive(Entity logicalEntity, in LoadedCellsMap loadedCells, byte interiorActive, ulong activeInteriorCellHash)
        {
            if (!EntityManager.HasComponent<LogicalRefLocation>(logicalEntity))
                return false;

            var location = EntityManager.GetComponentData<LogicalRefLocation>(logicalEntity);
            if (interiorActive != 0)
                return location.IsInterior != 0 && location.InteriorCellHash == activeInteriorCellHash;

            return location.IsInterior == 0
                && loadedCells.Active.IsCreated
                && loadedCells.Active.Contains(location.ExteriorCell);
        }

        void ProjectLogicalRef(ref EntityCommandBuffer ecb, Entity logicalEntity, bool active)
        {
            ProjectEntity(ref ecb, logicalEntity, active, isActorRoot: EntityManager.HasComponent<ActorSpawnSource>(logicalEntity));

            if (!EntityManager.HasBuffer<LogicalRefChild>(logicalEntity))
                return;

            bool isActor = EntityManager.HasComponent<ActorSpawnSource>(logicalEntity);
            var children = EntityManager.GetBuffer<LogicalRefChild>(logicalEntity);
            for (int i = 0; i < children.Length; i++)
            {
                Entity child = children[i].Value;
                if (child == Entity.Null || !EntityManager.Exists(child))
                    continue;

                ProjectEntity(ref ecb, child, active, isActor);
            }
        }

        void ProjectEntity(ref EntityCommandBuffer ecb, Entity entity, bool active, bool isActorRoot)
        {
            if (EntityManager.HasComponent<ActorRenderVisible>(entity))
                ecb.SetComponentEnabled<ActorRenderVisible>(entity, active);

            if (EntityManager.HasComponent<ActorShadowCasterVisible>(entity))
                ecb.SetComponentEnabled<ActorShadowCasterVisible>(entity, active);

            if (EntityManager.HasComponent<MaterialMeshInfo>(entity) && (!isActorRoot || !active))
                ecb.SetComponentEnabled<MaterialMeshInfo>(entity, active);

            if (EntityManager.HasComponent<RuntimeColliderSource>(entity))
            {
                if (active)
                    RuntimeColliderAttachmentUtility.QueueEnablePhysics(EntityManager, ref ecb, entity);
                else
                    RuntimeColliderAttachmentUtility.QueueDisablePhysics(EntityManager, ref ecb, entity);
            }
        }
    }
}
