using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldState;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptRefStateApplySystem))]
    public partial struct MorrowindScriptTransformApplySystem : ISystem
    {
        EntityQuery _runtimeQuery;
        EntityQuery _standingActorQuery;
        EntityQuery _mutationQueueQuery;
        EntityQuery _activeExplicitRefLookupQuery;
        EntityQuery _playerViewQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _runtimeQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<MorrowindScriptRuntimeState>(),
                ComponentType.ReadWrite<MorrowindScriptTransformRequest>());
            _standingActorQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<MorrowindMovementState>(),
                ComponentType.ReadOnly<LocalTransform>());
            _mutationQueueQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<RuntimePhysicsMutationQueueTag>(),
                ComponentType.ReadWrite<RuntimePhysicsMutationRequest>(),
                ComponentType.ReadWrite<PhysicsFlushRequested>());
            _activeExplicitRefLookupQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<ActiveExplicitRefLookup>());
            _playerViewQuery = systemState.GetEntityQuery(
                ComponentType.ReadWrite<PlayerViewComponent>(),
                ComponentType.ReadWrite<LocalTransform>(),
                ComponentType.ReadWrite<LocalToWorld>());

            systemState.RequireForUpdate(_runtimeQuery);
            systemState.RequireForUpdate(_mutationQueueQuery);
            systemState.RequireForUpdate<LogicalRefLookup>();
            systemState.RequireForUpdate<LoadedCellsMap>();
            systemState.RequireForUpdate<RuntimeWorldCellBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = _runtimeQuery.GetSingletonEntity();
            var requests = systemState.EntityManager.GetBuffer<MorrowindScriptTransformRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            using var requestSnapshot = requests.ToNativeArray(Allocator.Temp);
            requests.Clear();

            var logicalRefLookup = SystemAPI.GetSingleton<LogicalRefLookup>();
            var loadedCells = SystemAPI.GetSingleton<LoadedCellsMap>();
            var worldCellReference = SystemAPI.GetSingleton<RuntimeWorldCellBlobReference>();
            if (!worldCellReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][WorldCellBlob] script transform requires runtime world cell blob.");
            ref RuntimeWorldCellBlob worldCells = ref worldCellReference.Blob.Value;
            byte interiorActive = 0;
            ulong activeInteriorCellHash = 0UL;
            if (SystemAPI.TryGetSingleton<InteriorTransitionState>(out var interiorTransition) && interiorTransition.InteriorActive != 0)
            {
                interiorActive = 1;
                activeInteriorCellHash = interiorTransition.ActiveInteriorCellHash;
            }

            for (int i = 0; i < requestSnapshot.Length; i++)
            {
                var request = requestSnapshot[i];
                Entity target = ResolveLiveTarget(ref systemState, request, logicalRefLookup);
                if (target == Entity.Null || !systemState.EntityManager.Exists(target))
                {
                    if (request.Operation == 6)
                        throw new InvalidOperationException($"SetAtStart target ref={request.TargetPlacedRefId} is not live.");

                    continue;
                }

                CombinedCellRenderDecombineUtility.DecombineIfLinked(systemState.EntityManager, target);
                if (request.Operation == 2)
                {
                    if (systemState.EntityManager.HasComponent<PlayerTag>(target))
                        ApplyPlayerPositionCell(ref systemState, target, request, ref worldCells);
                    else
                    {
                        ApplyPositionCell(ref systemState, target, request, loadedCells, ref worldCells, interiorActive, activeInteriorCellHash);
                        CommitTransformIfPlacedRef(ref systemState, target, request.TargetPlacedRefId);
                    }
                    continue;
                }

                if (request.Operation == 3)
                {
                    ApplyPosition(ref systemState, target, request, loadedCells, interiorActive, activeInteriorCellHash);
                    CommitTransformIfPlacedRef(ref systemState, target, request.TargetPlacedRefId);
                    continue;
                }

                if (request.Operation == 4)
                {
                    ApplyPositionOnly(ref systemState, target, request, loadedCells, interiorActive, activeInteriorCellHash);
                    CommitTransformIfPlacedRef(ref systemState, target, request.TargetPlacedRefId);
                    continue;
                }

                if (request.Operation == 5)
                {
                    ApplyMove(ref systemState, target, request, loadedCells, interiorActive, activeInteriorCellHash);
                    CommitTransformIfPlacedRef(ref systemState, target, request.TargetPlacedRefId);
                    continue;
                }

                if (request.Operation == 6)
                {
                    ApplySetAtStart(ref systemState, target, request, loadedCells, interiorActive, activeInteriorCellHash);
                    CommitTransformIfPlacedRef(ref systemState, target, request.TargetPlacedRefId);
                    continue;
                }

                if (request.Operation == 7)
                {
                    quaternion worldDelta = quaternion.AxisAngle(
                        -LogicalRefRotationUtility.ResolveAxis(request.Axis),
                        request.Radians);
                    LogicalRefRotationUtility.ApplyWorldDelta(systemState.EntityManager, target, worldDelta);
                    CommitTransformIfPlacedRef(ref systemState, target, request.TargetPlacedRefId);
                    continue;
                }

                if (request.Operation == 1)
                {
                    LogicalRefRotationUtility.SetAngle(systemState.EntityManager, target, request.Axis, request.Radians);
                    CommitTransformIfPlacedRef(ref systemState, target, request.TargetPlacedRefId);
                    continue;
                }

                quaternion delta = quaternion.AxisAngle(
                    LogicalRefRotationUtility.ResolveAxis(request.Axis),
                    request.Radians);
                LogicalRefRotationUtility.ApplyDelta(systemState.EntityManager, target, delta);
                CommitTransformIfPlacedRef(ref systemState, target, request.TargetPlacedRefId);
            }
        }

        void CommitTransformIfPlacedRef(ref SystemState systemState, Entity target, uint requestPlacedRefId)
        {
            uint placedRefId = requestPlacedRefId;
            if (placedRefId == 0u
                && target != Entity.Null
                && systemState.EntityManager.Exists(target)
                && systemState.EntityManager.HasComponent<PlacedRefIdentity>(target))
            {
                placedRefId = systemState.EntityManager.GetComponentData<PlacedRefIdentity>(target).Value;
            }

            if (placedRefId == 0u)
                return;

            ScriptVisibleSaveStateUtility.UpsertTransform(systemState.EntityManager, target, placedRefId);
        }

        void ApplyPlayerPositionCell(
            ref SystemState systemState,
            Entity playerEntity,
            in MorrowindScriptTransformRequest request,
            ref RuntimeWorldCellBlob worldCells)
        {
            if (!RuntimeWorldCellBlobUtility.TryGetInteriorCellIndex(ref worldCells, request.InteriorCellHash, out int cellIndex))
                throw new InvalidOperationException($"[VVardenfell][MWScript] Player PositionCell target interior cell hash 0x{request.InteriorCellHash:X16} is missing from the world cell blob.");

            ref RuntimeWorldCellDefBlob cell = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, cellIndex);
            FixedString128Bytes cellId = !cell.InteriorCellId.IsEmpty ? cell.InteriorCellId : cell.CellId;
            if (cellId.IsEmpty)
                throw new InvalidOperationException($"[VVardenfell][MWScript] Player PositionCell target interior cell 0x{request.InteriorCellHash:X16} has no cell id.");

            Entity streamingEntity = SystemAPI.GetSingletonEntity<StreamingConfig>();
            Entity transitionEntity = SystemAPI.GetSingletonEntity<InteriorTransitionState>();
            var config = systemState.EntityManager.GetComponentData<StreamingConfig>(streamingEntity);
            var logicalRefLookup = systemState.EntityManager.GetComponentData<LogicalRefLookup>(streamingEntity);
            var loaded = systemState.EntityManager.GetComponentData<LoadedCellsMap>(streamingEntity);
            var transition = systemState.EntityManager.GetComponentData<InteriorTransitionState>(transitionEntity);

            transition.TransitionInProgress = 1;
            DestroyInteriorEntities(ref systemState, transitionEntity, ref logicalRefLookup);
            WorldSpawner.HideExteriorVisibility(systemState.World, ref loaded);
            if (!WorldSpawner.TrySpawnInteriorCellByHash(
                    systemState.World,
                    request.InteriorCellHash,
                    float3.zero,
                    transitionEntity,
                    ref logicalRefLookup,
                    out FixedString128Bytes spawnedInteriorCellId))
            {
                throw new InvalidOperationException($"[VVardenfell][MWScript] Player PositionCell destination '{cellId}' was not preloaded.");
            }

            config.ExteriorStreamingPaused = true;
            transition.InteriorActive = 1;
            transition.ActiveInteriorCellId = spawnedInteriorCellId.IsEmpty ? cellId : spawnedInteriorCellId;
            transition.ActiveInteriorCellHash = request.InteriorCellHash;
            transition.TransitionInProgress = 0;

            quaternion rotation = quaternion.AxisAngle(new float3(0f, 1f, 0f), math.radians(request.Radians));
            MovePlayerToDestination(ref systemState, playerEntity, request.Position, rotation);

            systemState.EntityManager.SetComponentData(streamingEntity, config);
            systemState.EntityManager.SetComponentData(streamingEntity, logicalRefLookup);
            systemState.EntityManager.SetComponentData(streamingEntity, loaded);
            systemState.EntityManager.SetComponentData(transitionEntity, transition);
            RuntimeSpawnProjectionUtility.TryRestoreAliveRefsForCurrentWorld(systemState.EntityManager);
        }

        void ApplyPositionCell(ref SystemState systemState, 
            Entity target,
            in MorrowindScriptTransformRequest request,
            in LoadedCellsMap loadedCells,
            ref RuntimeWorldCellBlob worldCells,
            byte interiorActive,
            ulong activeInteriorCellHash)
        {
            if (!RuntimeWorldCellBlobUtility.TryGetInteriorCellIndex(ref worldCells, request.InteriorCellHash, out int cellIndex))
                throw new InvalidOperationException($"PositionCell target interior cell hash 0x{request.InteriorCellHash:X16} is missing from the world cell blob.");

            ref RuntimeWorldCellDefBlob cell = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, cellIndex);
            FixedString128Bytes cellId = !cell.InteriorCellId.IsEmpty ? cell.InteriorCellId : cell.CellId;
            if (cellId.IsEmpty)
                throw new InvalidOperationException($"PositionCell target interior cell 0x{request.InteriorCellHash:X16} has no cell id.");

            float3 previousPosition = systemState.EntityManager.HasComponent<LocalTransform>(target)
                ? systemState.EntityManager.GetComponentData<LocalTransform>(target).Position
                : request.Position;
            quaternion rotation = quaternion.AxisAngle(new float3(0f, 1f, 0f), math.radians(request.Radians / 60f));
            MoveEntity(ref systemState, target, request.Position, rotation);
            ResetAnimationMotion(ref systemState, target, request.Position);
            UpdateInteriorMembership(ref systemState, target, cellId, request.InteriorCellHash);

            if (systemState.EntityManager.HasBuffer<LogicalRefChild>(target))
            {
                float3 delta = request.Position - previousPosition;
                var children = systemState.EntityManager.GetBuffer<LogicalRefChild>(target);
                for (int i = 0; i < children.Length; i++)
                {
                    Entity child = children[i].Value;
                    if (child == Entity.Null || child == target || !systemState.EntityManager.Exists(child))
                        continue;

                    if (!systemState.EntityManager.HasComponent<Parent>(child) && systemState.EntityManager.HasComponent<LocalTransform>(child))
                    {
                        var childTransform = systemState.EntityManager.GetComponentData<LocalTransform>(child);
                        childTransform.Position += delta;
                        systemState.EntityManager.SetComponentData(child, childTransform);
                        ResetAnimationMotion(ref systemState, child, childTransform.Position);
                        if (systemState.EntityManager.HasComponent<LocalToWorld>(child))
                        {
                            systemState.EntityManager.SetComponentData(child, new LocalToWorld
                            {
                                Value = float4x4.TRS(childTransform.Position, childTransform.Rotation, new float3(childTransform.Scale)),
                            });
                        }
                    }

                    UpdateInteriorMembership(ref systemState, child, cellId, request.InteriorCellHash);
                }
            }

            bool active = !systemState.EntityManager.HasComponent<LogicalRefLocation>(target)
                          || IsPositionCellTargetActive(ref systemState, target, loadedCells, interiorActive, activeInteriorCellHash);
            ProjectPositionCellTarget(ref systemState, target, active);
        }

        void ApplyPosition(ref SystemState systemState, 
            Entity target,
            in MorrowindScriptTransformRequest request,
            in LoadedCellsMap loadedCells,
            byte interiorActive,
            ulong activeInteriorCellHash)
        {
            if (!systemState.EntityManager.HasComponent<LogicalRefLocation>(target))
                throw new InvalidOperationException($"Position target ref={request.TargetPlacedRefId} has no logical location.");

            float3 previousPosition = systemState.EntityManager.HasComponent<LocalTransform>(target)
                ? systemState.EntityManager.GetComponentData<LocalTransform>(target).Position
                : request.Position;
            quaternion rotation = quaternion.AxisAngle(new float3(0f, 1f, 0f), request.Radians);
            MoveEntity(ref systemState, target, request.Position, rotation);
            ResetAnimationMotion(ref systemState, target, request.Position);
            MoveUnparentedChildrenByDelta(ref systemState, target, request.Position - previousPosition);

            bool active = IsPositionCellTargetActive(ref systemState, target, loadedCells, interiorActive, activeInteriorCellHash);
            ProjectPositionCellTarget(ref systemState, target, active);
        }

        void ApplyPositionOnly(ref SystemState systemState, 
            Entity target,
            in MorrowindScriptTransformRequest request,
            in LoadedCellsMap loadedCells,
            byte interiorActive,
            ulong activeInteriorCellHash)
        {
            if (!systemState.EntityManager.HasComponent<LogicalRefLocation>(target))
                throw new InvalidOperationException($"SetPos target ref={request.TargetPlacedRefId} has no logical location.");

            float3 previousPosition = systemState.EntityManager.HasComponent<LocalTransform>(target)
                ? systemState.EntityManager.GetComponentData<LocalTransform>(target).Position
                : request.Position;

            if (!systemState.EntityManager.HasComponent<LocalTransform>(target))
                throw new InvalidOperationException($"SetPos target ref={request.TargetPlacedRefId} has no LocalTransform.");

            var transform = systemState.EntityManager.GetComponentData<LocalTransform>(target);
            MoveEntity(ref systemState, target, request.Position, transform.Rotation);
            ResetAnimationMotion(ref systemState, target, request.Position);
            MoveUnparentedChildrenByDelta(ref systemState, target, request.Position - previousPosition);

            bool active = IsPositionCellTargetActive(ref systemState, target, loadedCells, interiorActive, activeInteriorCellHash);
            ProjectPositionCellTarget(ref systemState, target, active);
        }

        void ApplyMove(ref SystemState systemState, 
            Entity target,
            in MorrowindScriptTransformRequest request,
            in LoadedCellsMap loadedCells,
            byte interiorActive,
            ulong activeInteriorCellHash)
        {
            if (!systemState.EntityManager.HasComponent<LogicalRefLocation>(target))
                throw new InvalidOperationException($"Move target ref={request.TargetPlacedRefId} has no logical location.");

            if (!systemState.EntityManager.HasComponent<LocalTransform>(target))
                throw new InvalidOperationException($"Move target ref={request.TargetPlacedRefId} has no LocalTransform.");

            var transform = systemState.EntityManager.GetComponentData<LocalTransform>(target);
            float3 delta = request.Position - transform.Position;
            MoveEntity(ref systemState, target, request.Position, transform.Rotation);
            MoveUnparentedChildrenByDelta(ref systemState, target, delta);
            MoveStandingActorsByDelta(ref systemState, target, delta);

            bool active = IsPositionCellTargetActive(ref systemState, target, loadedCells, interiorActive, activeInteriorCellHash);
            ProjectPositionCellTarget(ref systemState, target, active);
        }

        void ApplySetAtStart(ref SystemState systemState, 
            Entity target,
            in MorrowindScriptTransformRequest request,
            in LoadedCellsMap loadedCells,
            byte interiorActive,
            ulong activeInteriorCellHash)
        {
            if (!systemState.EntityManager.HasComponent<LogicalRefLocation>(target))
                throw new InvalidOperationException($"SetAtStart target ref={request.TargetPlacedRefId} has no logical location.");

            if (!systemState.EntityManager.HasComponent<PlacedRefInitialTransform>(target))
                throw new InvalidOperationException($"SetAtStart target ref={request.TargetPlacedRefId} has no initial transform.");

            if (!systemState.EntityManager.HasComponent<LocalTransform>(target))
                throw new InvalidOperationException($"SetAtStart target ref={request.TargetPlacedRefId} has no LocalTransform.");

            var initial = systemState.EntityManager.GetComponentData<PlacedRefInitialTransform>(target);
            var current = systemState.EntityManager.GetComponentData<LocalTransform>(target);
            float3 delta = initial.Position - current.Position;
            MoveEntity(ref systemState, target, initial.Position, initial.Rotation, initial.Scale);
            ResetAnimationMotion(ref systemState, target, initial.Position);
            MoveUnparentedChildrenByDelta(ref systemState, target, delta);
            MoveStandingActorsByDelta(ref systemState, target, delta);

            bool active = IsPositionCellTargetActive(ref systemState, target, loadedCells, interiorActive, activeInteriorCellHash);
            ProjectPositionCellTarget(ref systemState, target, active);
        }

        void MoveUnparentedChildrenByDelta(ref SystemState systemState, Entity target, float3 delta)
        {
            if (!systemState.EntityManager.HasBuffer<LogicalRefChild>(target))
                return;

            var children = systemState.EntityManager.GetBuffer<LogicalRefChild>(target);
            for (int i = 0; i < children.Length; i++)
            {
                Entity child = children[i].Value;
                if (child == Entity.Null || child == target || !systemState.EntityManager.Exists(child))
                    continue;

                if (systemState.EntityManager.HasComponent<Parent>(child) || !systemState.EntityManager.HasComponent<LocalTransform>(child))
                    continue;

                var childTransform = systemState.EntityManager.GetComponentData<LocalTransform>(child);
                childTransform.Position += delta;
                systemState.EntityManager.SetComponentData(child, childTransform);
                ResetAnimationMotion(ref systemState, child, childTransform.Position);
                if (systemState.EntityManager.HasComponent<LocalToWorld>(child))
                {
                    systemState.EntityManager.SetComponentData(child, new LocalToWorld
                    {
                        Value = float4x4.TRS(childTransform.Position, childTransform.Rotation, new float3(childTransform.Scale)),
                    });
                }
            }
        }

        void MoveStandingActorsByDelta(ref SystemState systemState, Entity target, float3 delta)
        {
            if (math.lengthsq(delta) <= 0f)
                return;

            using var entities = _standingActorQuery.ToEntityArray(Allocator.Temp);
            using var states = _standingActorQuery.ToComponentDataArray<MorrowindMovementState>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (states[i].StandingOn != target || entities[i] == target || !systemState.EntityManager.Exists(entities[i]))
                    continue;

                if (!systemState.EntityManager.HasComponent<LocalTransform>(entities[i]))
                    continue;

                var transform = systemState.EntityManager.GetComponentData<LocalTransform>(entities[i]);
                MoveEntity(ref systemState, entities[i], transform.Position + delta, transform.Rotation);
                ResetAnimationMotion(ref systemState, entities[i], transform.Position + delta);
            }
        }

        void MovePlayerToDestination(
            ref SystemState systemState,
            Entity playerEntity,
            float3 destinationPosition,
            quaternion bodyYawRotation)
        {
            if (_playerViewQuery.CalculateEntityCount() != 1)
                throw new InvalidOperationException("[VVardenfell][MWScript] Player PositionCell requires exactly one player view.");

            Entity viewEntity = _playerViewQuery.GetSingletonEntity();
            var character = systemState.EntityManager.GetComponentData<PlayerCharacterComponent>(playerEntity);
            var control = systemState.EntityManager.GetComponentData<PlayerCharacterControl>(playerEntity);
            var state = systemState.EntityManager.GetComponentData<PlayerCharacterState>(playerEntity);
            var movementState = systemState.EntityManager.GetComponentData<MorrowindMovementState>(playerEntity);
            var playerTransform = systemState.EntityManager.GetComponentData<LocalTransform>(playerEntity);

            playerTransform.Position = destinationPosition;
            playerTransform.Rotation = bodyYawRotation;
            systemState.EntityManager.SetComponentData(playerEntity, playerTransform);
            systemState.EntityManager.SetComponentData(playerEntity, new LocalToWorld
            {
                Value = float4x4.TRS(destinationPosition, bodyYawRotation, new float3(playerTransform.Scale)),
            });

            control.LookDeltaDegrees = float2.zero;
            control.MoveVectorWorld = float3.zero;
            control.InteractPressed = false;
            control.JumpThisFixedTick = false;
            control.ReadyWeaponTogglePressed = false;
            control.AttackHeld = false;
            control.AttackPressed = false;
            control.AttackReleased = false;
            control.JumpPressedEvent.Clear();
            systemState.EntityManager.SetComponentData(playerEntity, control);

            systemState.EntityManager.SetComponentData(playerEntity, default(MorrowindMovementInput));

            movementState.Inertia = float3.zero;
            movementState.LastVelocity = float3.zero;
            movementState.LocalMove = float2.zero;
            movementState.SpeedFactor = 0f;
            movementState.Flags = 0;
            movementState.Grounded = true;
            movementState.SupportKind = (byte)MorrowindSupportKind.FlatGround;
            movementState.StandingOn = Entity.Null;
            movementState.GroundNormal = math.up();
            systemState.EntityManager.SetComponentData(playerEntity, movementState);

            state.WorldVelocity = float3.zero;
            state.Grounded = true;
            state.WasGrounded = true;
            state.GroundedTime = 0.001f;
            state.AirborneTime = 0f;
            systemState.EntityManager.SetComponentData(playerEntity, state);

            var view = systemState.EntityManager.GetComponentData<PlayerViewComponent>(viewEntity);
            float eyeHeight = state.Crouched ? character.CrouchingEyeHeight : character.StandingEyeHeight;
            view.LocalPitchDegrees = 0f;
            view.LocalViewRotation = quaternion.identity;
            view.LocalEyeOffset = new float3(0f, eyeHeight, 0f);
            systemState.EntityManager.SetComponentData(viewEntity, view);
            systemState.EntityManager.SetComponentData(viewEntity, LocalTransform.FromPositionRotationScale(
                view.LocalEyeOffset,
                quaternion.identity,
                1f));
            systemState.EntityManager.SetComponentData(viewEntity, new LocalToWorld
            {
                Value = float4x4.TRS(destinationPosition + math.rotate(bodyYawRotation, view.LocalEyeOffset), bodyYawRotation, new float3(1f)),
            });
        }

        void DestroyInteriorEntities(ref SystemState systemState, Entity transitionEntity, ref LogicalRefLookup logicalRefLookup)
        {
            var spawnedBuffer = systemState.EntityManager.GetBuffer<InteriorSpawnedEntity>(transitionEntity);
            if (spawnedBuffer.Length == 0)
                return;

            var entitiesToDestroy = new NativeArray<Entity>(spawnedBuffer.Length, Allocator.Temp);
            try
            {
                for (int i = 0; i < spawnedBuffer.Length; i++)
                    entitiesToDestroy[i] = spawnedBuffer[i].Value;

                var ecb = new EntityCommandBuffer(Allocator.Temp);
                try
                {
                    for (int i = 0; i < entitiesToDestroy.Length; i++)
                    {
                        if (systemState.EntityManager.Exists(entitiesToDestroy[i])
                            && systemState.EntityManager.HasComponent<LogicalRefTag>(entitiesToDestroy[i]))
                        {
                            LogicalRefDestroyUtility.QueueDestroyLogicalRef(
                                systemState.EntityManager,
                                ref ecb,
                                entitiesToDestroy[i],
                                ref logicalRefLookup,
                                preserveRuntimeSpawnRegistration: true);
                            continue;
                        }

                        if (systemState.EntityManager.Exists(entitiesToDestroy[i]))
                            ecb.DestroyEntity(entitiesToDestroy[i]);
                    }

                    ecb.Playback(systemState.EntityManager);
                }
                finally
                {
                    ecb.Dispose();
                }
            }
            finally
            {
                entitiesToDestroy.Dispose();
            }

            systemState.EntityManager.GetBuffer<InteriorSpawnedEntity>(transitionEntity).Clear();
        }

        void MoveEntity(ref SystemState systemState, Entity entity, float3 position, quaternion rotation)
            => MoveEntity(ref systemState, entity, position, rotation, float.NaN);

        void ResetAnimationMotion(ref SystemState systemState, Entity entity, float3 position)
        {
            if (!systemState.EntityManager.HasComponent<ActorAnimationMotionState>(entity))
                return;

            var motion = systemState.EntityManager.GetComponentData<ActorAnimationMotionState>(entity);
            motion.PreviousPosition = position;
            motion.LocalMove = float2.zero;
            motion.LastVelocity = float3.zero;
            motion.Initialized = 1;
            motion.Moving = 0;
            systemState.EntityManager.SetComponentData(entity, motion);
        }

        void MoveEntity(ref SystemState systemState, Entity entity, float3 position, quaternion rotation, float scale)
        {
            if (!systemState.EntityManager.HasComponent<LocalTransform>(entity))
                return;

            if (systemState.EntityManager.HasComponent<Static>(entity))
                systemState.EntityManager.RemoveComponent<Static>(entity);

            var transform = systemState.EntityManager.GetComponentData<LocalTransform>(entity);
            transform.Position = position;
            transform.Rotation = rotation;
            if (!float.IsNaN(scale))
                transform.Scale = scale;
            systemState.EntityManager.SetComponentData(entity, transform);

            if (systemState.EntityManager.HasComponent<LocalToWorld>(entity))
            {
                systemState.EntityManager.SetComponentData(entity, new LocalToWorld
                {
                    Value = float4x4.TRS(transform.Position, transform.Rotation, new float3(transform.Scale)),
                });
            }
        }

        void UpdateInteriorMembership(ref SystemState systemState, Entity entity, FixedString128Bytes cellId, ulong cellHash)
        {
            if (systemState.EntityManager.HasComponent<LogicalRefLocation>(entity))
            {
                systemState.EntityManager.SetComponentData(entity, new LogicalRefLocation
                {
                    InteriorCellId = cellId,
                    InteriorCellHash = cellHash,
                    IsInterior = 1,
                });
                MarkActiveExplicitRefLookupDirty(ref systemState);
                MarkActorAiNavigationAnchorDirty(ref systemState, entity);
            }

            if (!systemState.EntityManager.HasComponent<InteriorCellMember>(entity))
                systemState.EntityManager.AddComponent<InteriorCellMember>(entity);
            if (systemState.EntityManager.HasComponent<CellLink>(entity))
                systemState.EntityManager.RemoveComponent<CellLink>(entity);
        }

        void MarkActiveExplicitRefLookupDirty(ref SystemState systemState)
        {
            if (_activeExplicitRefLookupQuery.IsEmptyIgnoreFilter)
                return;

            Entity lookupEntity = _activeExplicitRefLookupQuery.GetSingletonEntity();
            if (!systemState.EntityManager.HasComponent<ActiveExplicitRefLookupDirty>(lookupEntity))
                throw new InvalidOperationException("[VVardenfell][WorldRefs] active explicit-ref lookup exists without its dirty marker.");

            systemState.EntityManager.SetComponentEnabled<ActiveExplicitRefLookupDirty>(lookupEntity, true);
        }

        void MarkActorAiNavigationAnchorDirty(ref SystemState systemState, Entity entity)
        {
            if (!systemState.EntityManager.HasComponent<ActorAiNavigationAnchor>(entity))
                return;

            if (!systemState.EntityManager.HasComponent<ActorAiNavigationAnchorDirty>(entity))
                throw new InvalidOperationException($"[VVardenfell][AI] actor entity={entity.Index}:{entity.Version} has ActorAiNavigationAnchor without ActorAiNavigationAnchorDirty.");

            systemState.EntityManager.SetComponentEnabled<ActorAiNavigationAnchorDirty>(entity, true);
        }

        bool IsPositionCellTargetActive(ref SystemState systemState, Entity target, in LoadedCellsMap loadedCells, byte interiorActive, ulong activeInteriorCellHash)
        {
            if (systemState.EntityManager.HasComponent<PlacedRefRuntimeState>(target)
                && systemState.EntityManager.GetComponentData<PlacedRefRuntimeState>(target).Disabled != 0)
                return false;

            if (!systemState.EntityManager.HasComponent<LogicalRefLocation>(target))
                return false;

            var location = systemState.EntityManager.GetComponentData<LogicalRefLocation>(target);
            if (location.IsInterior != 0)
                return interiorActive != 0 && location.InteriorCellHash == activeInteriorCellHash;

            return loadedCells.Active.IsCreated && loadedCells.Active.Contains(location.ExteriorCell);
        }

        void ProjectPositionCellTarget(ref SystemState systemState, Entity target, bool active)
        {
            ProjectEntity(ref systemState, target, active, isActorRoot: systemState.EntityManager.HasComponent<ActorSpawnSource>(target));

            if (!systemState.EntityManager.HasBuffer<LogicalRefChild>(target))
                return;

            bool isActor = systemState.EntityManager.HasComponent<ActorSpawnSource>(target);
            var children = systemState.EntityManager.GetBuffer<LogicalRefChild>(target);
            for (int i = 0; i < children.Length; i++)
            {
                Entity child = children[i].Value;
                if (child == Entity.Null || !systemState.EntityManager.Exists(child))
                    continue;

                ProjectEntity(ref systemState, child, active, isActor);
            }
        }

        void ProjectEntity(ref SystemState systemState, Entity entity, bool active, bool isActorRoot)
        {
            if (systemState.EntityManager.HasComponent<ActorRenderVisible>(entity))
                systemState.EntityManager.SetComponentEnabled<ActorRenderVisible>(entity, active);

            if (systemState.EntityManager.HasComponent<ActorShadowCasterVisible>(entity))
                systemState.EntityManager.SetComponentEnabled<ActorShadowCasterVisible>(entity, active);

            if (systemState.EntityManager.HasComponent<MaterialMeshInfo>(entity) && (!isActorRoot || !active))
                systemState.EntityManager.SetComponentEnabled<MaterialMeshInfo>(entity, active);

            if (!systemState.EntityManager.HasComponent<RuntimeColliderSource>(entity))
                return;

            Entity queueEntity = _mutationQueueQuery.GetSingletonEntity();
            var mutations = systemState.EntityManager.GetBuffer<RuntimePhysicsMutationRequest>(queueEntity);
            if (active)
                RuntimePhysicsMutationQueueUtility.EnqueueEnable(ref mutations, entity);
            else
                RuntimePhysicsMutationQueueUtility.EnqueueDisable(ref mutations, entity);
            RuntimePhysicsMutationQueueUtility.MarkFlushRequested(systemState.EntityManager, queueEntity);
        }

        Entity ResolveLiveTarget(ref SystemState systemState, in MorrowindScriptTransformRequest request, in LogicalRefLookup lookup)
        {
            if (request.TargetEntity != Entity.Null && systemState.EntityManager.Exists(request.TargetEntity))
                return request.TargetEntity;

            if (lookup.Map.IsCreated && lookup.Map.TryGetValue(request.TargetPlacedRefId, out Entity target))
                return target;

            return Entity.Null;
        }
    }
}
