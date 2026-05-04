using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptRefStateApplySystem))]
    public partial class MorrowindScriptTransformApplySystem : SystemBase
    {
        EntityQuery _runtimeQuery;

        protected override void OnCreate()
        {
            _runtimeQuery = GetEntityQuery(
                ComponentType.ReadOnly<MorrowindScriptRuntimeState>(),
                ComponentType.ReadWrite<MorrowindScriptTransformRequest>());

            RequireForUpdate(_runtimeQuery);
            RequireForUpdate<LogicalRefLookup>();
            RequireForUpdate<LoadedCellsMap>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = _runtimeQuery.GetSingletonEntity();
            var requests = EntityManager.GetBuffer<MorrowindScriptTransformRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            var logicalRefLookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            var loadedCells = SystemAPI.GetSingleton<LoadedCellsMap>();
            byte interiorActive = 0;
            ulong activeInteriorCellHash = 0UL;
            if (SystemAPI.TryGetSingleton<InteriorTransitionState>(out var interiorTransition) && interiorTransition.InteriorActive != 0)
            {
                interiorActive = 1;
                activeInteriorCellHash = interiorTransition.ActiveInteriorCellHash;
            }

            for (int i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                Entity target = ResolveLiveTarget(request, logicalRefLookup);
                if (target == Entity.Null || !EntityManager.Exists(target))
                {
                    if (request.Operation == 6)
                        throw new InvalidOperationException($"SetAtStart target ref={request.TargetPlacedRefId} is not live.");

                    continue;
                }

                if (request.Operation == 2)
                {
                    ApplyPositionCell(target, request, loadedCells, interiorActive, activeInteriorCellHash);
                    continue;
                }

                if (request.Operation == 3)
                {
                    ApplyPosition(target, request, loadedCells, interiorActive, activeInteriorCellHash);
                    continue;
                }

                if (request.Operation == 4)
                {
                    ApplyPositionOnly(target, request, loadedCells, interiorActive, activeInteriorCellHash);
                    continue;
                }

                if (request.Operation == 5)
                {
                    ApplyMove(target, request, loadedCells, interiorActive, activeInteriorCellHash);
                    continue;
                }

                if (request.Operation == 6)
                {
                    ApplySetAtStart(target, request, loadedCells, interiorActive, activeInteriorCellHash);
                    continue;
                }

                if (request.Operation == 7)
                {
                    quaternion worldDelta = quaternion.AxisAngle(
                        -LogicalRefRotationUtility.ResolveAxis(request.Axis),
                        request.Radians);
                    LogicalRefRotationUtility.ApplyWorldDelta(EntityManager, target, worldDelta);
                    continue;
                }

                if (request.Operation == 1)
                {
                    LogicalRefRotationUtility.SetAngle(EntityManager, target, request.Axis, request.Radians);
                    continue;
                }

                quaternion delta = quaternion.AxisAngle(
                    LogicalRefRotationUtility.ResolveAxis(request.Axis),
                    request.Radians);
                LogicalRefRotationUtility.ApplyDelta(EntityManager, target, delta);
            }

            requests.Clear();
        }

        void ApplyPositionCell(
            Entity target,
            in MorrowindScriptTransformRequest request,
            in LoadedCellsMap loadedCells,
            byte interiorActive,
            ulong activeInteriorCellHash)
        {
            if (!WorldResources.TryGetInteriorCell(request.InteriorCellHash, out var cell))
                throw new InvalidOperationException($"PositionCell target interior cell hash 0x{request.InteriorCellHash:X16} is not loaded in world resources.");

            FixedString128Bytes cellId = RuntimeFixedStringUtility.ToFixed128OrDefault(cell.CellId);
            if (cellId.IsEmpty)
                throw new InvalidOperationException($"PositionCell target interior cell 0x{request.InteriorCellHash:X16} has no cell id.");

            float3 previousPosition = EntityManager.HasComponent<LocalTransform>(target)
                ? EntityManager.GetComponentData<LocalTransform>(target).Position
                : request.Position;
            quaternion rotation = quaternion.AxisAngle(new float3(0f, 1f, 0f), request.Radians);
            MoveEntity(target, request.Position, rotation);
            UpdateInteriorMembership(target, cellId, request.InteriorCellHash);

            if (EntityManager.HasBuffer<LogicalRefChild>(target))
            {
                float3 delta = request.Position - previousPosition;
                var children = EntityManager.GetBuffer<LogicalRefChild>(target);
                for (int i = 0; i < children.Length; i++)
                {
                    Entity child = children[i].Value;
                    if (child == Entity.Null || child == target || !EntityManager.Exists(child))
                        continue;

                    if (!EntityManager.HasComponent<Parent>(child) && EntityManager.HasComponent<LocalTransform>(child))
                    {
                        var childTransform = EntityManager.GetComponentData<LocalTransform>(child);
                        childTransform.Position += delta;
                        EntityManager.SetComponentData(child, childTransform);
                        if (EntityManager.HasComponent<LocalToWorld>(child))
                        {
                            EntityManager.SetComponentData(child, new LocalToWorld
                            {
                                Value = float4x4.TRS(childTransform.Position, childTransform.Rotation, new float3(childTransform.Scale)),
                            });
                        }
                    }

                    UpdateInteriorMembership(child, cellId, request.InteriorCellHash);
                }
            }

            bool active = !EntityManager.HasComponent<LogicalRefLocation>(target)
                          || IsPositionCellTargetActive(target, loadedCells, interiorActive, activeInteriorCellHash);
            ProjectPositionCellTarget(target, active);
        }

        void ApplyPosition(
            Entity target,
            in MorrowindScriptTransformRequest request,
            in LoadedCellsMap loadedCells,
            byte interiorActive,
            ulong activeInteriorCellHash)
        {
            if (!EntityManager.HasComponent<LogicalRefLocation>(target))
                throw new InvalidOperationException($"Position target ref={request.TargetPlacedRefId} has no logical location.");

            float3 previousPosition = EntityManager.HasComponent<LocalTransform>(target)
                ? EntityManager.GetComponentData<LocalTransform>(target).Position
                : request.Position;
            quaternion rotation = quaternion.AxisAngle(new float3(0f, 1f, 0f), request.Radians);
            MoveEntity(target, request.Position, rotation);
            MoveUnparentedChildrenByDelta(target, request.Position - previousPosition);

            bool active = IsPositionCellTargetActive(target, loadedCells, interiorActive, activeInteriorCellHash);
            ProjectPositionCellTarget(target, active);
        }

        void ApplyPositionOnly(
            Entity target,
            in MorrowindScriptTransformRequest request,
            in LoadedCellsMap loadedCells,
            byte interiorActive,
            ulong activeInteriorCellHash)
        {
            if (!EntityManager.HasComponent<LogicalRefLocation>(target))
                throw new InvalidOperationException($"SetPos target ref={request.TargetPlacedRefId} has no logical location.");

            float3 previousPosition = EntityManager.HasComponent<LocalTransform>(target)
                ? EntityManager.GetComponentData<LocalTransform>(target).Position
                : request.Position;

            if (!EntityManager.HasComponent<LocalTransform>(target))
                throw new InvalidOperationException($"SetPos target ref={request.TargetPlacedRefId} has no LocalTransform.");

            var transform = EntityManager.GetComponentData<LocalTransform>(target);
            MoveEntity(target, request.Position, transform.Rotation);
            MoveUnparentedChildrenByDelta(target, request.Position - previousPosition);

            bool active = IsPositionCellTargetActive(target, loadedCells, interiorActive, activeInteriorCellHash);
            ProjectPositionCellTarget(target, active);
        }

        void ApplyMove(
            Entity target,
            in MorrowindScriptTransformRequest request,
            in LoadedCellsMap loadedCells,
            byte interiorActive,
            ulong activeInteriorCellHash)
        {
            if (!EntityManager.HasComponent<LogicalRefLocation>(target))
                throw new InvalidOperationException($"Move target ref={request.TargetPlacedRefId} has no logical location.");

            if (!EntityManager.HasComponent<LocalTransform>(target))
                throw new InvalidOperationException($"Move target ref={request.TargetPlacedRefId} has no LocalTransform.");

            var transform = EntityManager.GetComponentData<LocalTransform>(target);
            float3 delta = request.Position - transform.Position;
            MoveEntity(target, request.Position, transform.Rotation);
            MoveUnparentedChildrenByDelta(target, delta);
            MoveStandingActorsByDelta(target, delta);

            bool active = IsPositionCellTargetActive(target, loadedCells, interiorActive, activeInteriorCellHash);
            ProjectPositionCellTarget(target, active);
        }

        void ApplySetAtStart(
            Entity target,
            in MorrowindScriptTransformRequest request,
            in LoadedCellsMap loadedCells,
            byte interiorActive,
            ulong activeInteriorCellHash)
        {
            if (!EntityManager.HasComponent<LogicalRefLocation>(target))
                throw new InvalidOperationException($"SetAtStart target ref={request.TargetPlacedRefId} has no logical location.");

            if (!EntityManager.HasComponent<PlacedRefInitialTransform>(target))
                throw new InvalidOperationException($"SetAtStart target ref={request.TargetPlacedRefId} has no initial transform.");

            if (!EntityManager.HasComponent<LocalTransform>(target))
                throw new InvalidOperationException($"SetAtStart target ref={request.TargetPlacedRefId} has no LocalTransform.");

            var initial = EntityManager.GetComponentData<PlacedRefInitialTransform>(target);
            var current = EntityManager.GetComponentData<LocalTransform>(target);
            float3 delta = initial.Position - current.Position;
            MoveEntity(target, initial.Position, initial.Rotation, initial.Scale);
            MoveUnparentedChildrenByDelta(target, delta);
            MoveStandingActorsByDelta(target, delta);

            bool active = IsPositionCellTargetActive(target, loadedCells, interiorActive, activeInteriorCellHash);
            ProjectPositionCellTarget(target, active);
        }

        void MoveUnparentedChildrenByDelta(Entity target, float3 delta)
        {
            if (!EntityManager.HasBuffer<LogicalRefChild>(target))
                return;

            var children = EntityManager.GetBuffer<LogicalRefChild>(target);
            for (int i = 0; i < children.Length; i++)
            {
                Entity child = children[i].Value;
                if (child == Entity.Null || child == target || !EntityManager.Exists(child))
                    continue;

                if (EntityManager.HasComponent<Parent>(child) || !EntityManager.HasComponent<LocalTransform>(child))
                    continue;

                var childTransform = EntityManager.GetComponentData<LocalTransform>(child);
                childTransform.Position += delta;
                EntityManager.SetComponentData(child, childTransform);
                if (EntityManager.HasComponent<LocalToWorld>(child))
                {
                    EntityManager.SetComponentData(child, new LocalToWorld
                    {
                        Value = float4x4.TRS(childTransform.Position, childTransform.Rotation, new float3(childTransform.Scale)),
                    });
                }
            }
        }

        void MoveStandingActorsByDelta(Entity target, float3 delta)
        {
            if (math.lengthsq(delta) <= 0f)
                return;

            using var query = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<MorrowindMovementState>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var states = query.ToComponentDataArray<MorrowindMovementState>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (states[i].StandingOn != target || entities[i] == target || !EntityManager.Exists(entities[i]))
                    continue;

                if (!EntityManager.HasComponent<LocalTransform>(entities[i]))
                    continue;

                var transform = EntityManager.GetComponentData<LocalTransform>(entities[i]);
                MoveEntity(entities[i], transform.Position + delta, transform.Rotation);
            }
        }

        void MoveEntity(Entity entity, float3 position, quaternion rotation)
            => MoveEntity(entity, position, rotation, float.NaN);

        void MoveEntity(Entity entity, float3 position, quaternion rotation, float scale)
        {
            if (!EntityManager.HasComponent<LocalTransform>(entity))
                return;

            if (EntityManager.HasComponent<Static>(entity))
                EntityManager.RemoveComponent<Static>(entity);

            var transform = EntityManager.GetComponentData<LocalTransform>(entity);
            transform.Position = position;
            transform.Rotation = rotation;
            if (!float.IsNaN(scale))
                transform.Scale = scale;
            EntityManager.SetComponentData(entity, transform);

            if (EntityManager.HasComponent<LocalToWorld>(entity))
            {
                EntityManager.SetComponentData(entity, new LocalToWorld
                {
                    Value = float4x4.TRS(transform.Position, transform.Rotation, new float3(transform.Scale)),
                });
            }
        }

        void UpdateInteriorMembership(Entity entity, FixedString128Bytes cellId, ulong cellHash)
        {
            if (EntityManager.HasComponent<LogicalRefLocation>(entity))
            {
                EntityManager.SetComponentData(entity, new LogicalRefLocation
                {
                    InteriorCellId = cellId,
                    InteriorCellHash = cellHash,
                    IsInterior = 1,
                });
                ActiveExplicitRefLookupLifecycleUtility.MarkDirty(EntityManager);
                MarkActorAiNavigationAnchorDirty(entity);
            }

            if (!EntityManager.HasComponent<InteriorCellMember>(entity))
                EntityManager.AddComponent<InteriorCellMember>(entity);
            if (EntityManager.HasComponent<CellLink>(entity))
                EntityManager.RemoveComponent<CellLink>(entity);
        }

        void MarkActorAiNavigationAnchorDirty(Entity entity)
        {
            if (!EntityManager.HasComponent<ActorAiNavigationAnchor>(entity))
                return;

            if (!EntityManager.HasComponent<ActorAiNavigationAnchorDirty>(entity))
                throw new InvalidOperationException($"[VVardenfell][AI] actor entity={entity.Index}:{entity.Version} has ActorAiNavigationAnchor without ActorAiNavigationAnchorDirty.");

            EntityManager.SetComponentEnabled<ActorAiNavigationAnchorDirty>(entity, true);
        }

        bool IsPositionCellTargetActive(Entity target, in LoadedCellsMap loadedCells, byte interiorActive, ulong activeInteriorCellHash)
        {
            if (EntityManager.HasComponent<PlacedRefRuntimeState>(target)
                && EntityManager.GetComponentData<PlacedRefRuntimeState>(target).Disabled != 0)
                return false;

            if (!EntityManager.HasComponent<LogicalRefLocation>(target))
                return false;

            var location = EntityManager.GetComponentData<LogicalRefLocation>(target);
            if (location.IsInterior != 0)
                return interiorActive != 0 && location.InteriorCellHash == activeInteriorCellHash;

            return loadedCells.Active.IsCreated && loadedCells.Active.Contains(location.ExteriorCell);
        }

        void ProjectPositionCellTarget(Entity target, bool active)
        {
            ProjectEntity(target, active, isActorRoot: EntityManager.HasComponent<ActorSpawnSource>(target));

            if (!EntityManager.HasBuffer<LogicalRefChild>(target))
                return;

            bool isActor = EntityManager.HasComponent<ActorSpawnSource>(target);
            var children = EntityManager.GetBuffer<LogicalRefChild>(target);
            for (int i = 0; i < children.Length; i++)
            {
                Entity child = children[i].Value;
                if (child == Entity.Null || !EntityManager.Exists(child))
                    continue;

                ProjectEntity(child, active, isActor);
            }
        }

        void ProjectEntity(Entity entity, bool active, bool isActorRoot)
        {
            if (EntityManager.HasComponent<ActorRenderVisible>(entity))
                EntityManager.SetComponentEnabled<ActorRenderVisible>(entity, active);

            if (EntityManager.HasComponent<ActorShadowCasterVisible>(entity))
                EntityManager.SetComponentEnabled<ActorShadowCasterVisible>(entity, active);

            if (EntityManager.HasComponent<MaterialMeshInfo>(entity) && (!isActorRoot || !active))
                EntityManager.SetComponentEnabled<MaterialMeshInfo>(entity, active);

            if (!EntityManager.HasComponent<RuntimeColliderSource>(entity))
                return;

            if (active)
                RuntimePhysicsMutationQueueUtility.EnqueueEnable(EntityManager, entity);
            else
                RuntimePhysicsMutationQueueUtility.EnqueueDisable(EntityManager, entity);
        }

        Entity ResolveLiveTarget(in MorrowindScriptTransformRequest request, in LogicalRefLookup lookup)
        {
            if (request.TargetEntity != Entity.Null && EntityManager.Exists(request.TargetEntity))
                return request.TargetEntity;

            if (lookup.Map.IsCreated && lookup.Map.TryGetValue(request.TargetPlacedRefId, out Entity target))
                return target;

            return Entity.Null;
        }
    }
}
