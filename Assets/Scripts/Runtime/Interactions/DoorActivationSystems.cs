using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Profiling;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Player;
using BoxCollider = Unity.Physics.BoxCollider;
using Collider = Unity.Physics.Collider;

using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Physics;
using VVardenfell.Runtime.WorldState;
using VVardenfell.Runtime.WorldRefs;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Interactions
{

    static class DoorInteractableResolver
    {
        public static bool TryResolve(
            EntityManager entityManager,
            ref RuntimeWorldCellBlob worldCells,
            Entity logicalEntity,
            out DoorInteractable interactable)
        {
            interactable = default;
            if (!entityManager.Exists(logicalEntity)
                || !entityManager.HasComponent<PlacedRefIdentity>(logicalEntity)
                || !entityManager.HasComponent<LogicalRefLocation>(logicalEntity))
            {
                return false;
            }

            uint placedRefId = entityManager.GetComponentData<PlacedRefIdentity>(logicalEntity).Value;
            var location = entityManager.GetComponentData<LogicalRefLocation>(logicalEntity);
            return TryBuild(ref worldCells, location, placedRefId, out interactable);
        }

        static bool TryBuild(ref RuntimeWorldCellBlob worldCells, in LogicalRefLocation location, uint placedRefId, out DoorInteractable interactable)
        {
            interactable = default;
            int cellIndex;
            if (location.IsInterior != 0)
            {
                if (!RuntimeWorldCellBlobUtility.TryGetInteriorCellIndex(ref worldCells, location.InteriorCellHash, out cellIndex))
                    return false;
            }
            else
            {
                if (!RuntimeWorldCellBlobUtility.TryGetExteriorCellIndex(ref worldCells, location.ExteriorCell, out cellIndex))
                    return false;
            }

            ref RuntimeWorldCellDefBlob cell = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, cellIndex);
            if (!RuntimeWorldCellBlobUtility.TryGetDoorByPlacedRefId(ref worldCells, ref cell, placedRefId, out var door))
                return false;

            interactable = new DoorInteractable
            {
                IsTeleport = (byte)((door.Flags & DoorRefEntry.FlagTeleport) != 0 ? 1 : 0),
                DestinationCellId = door.DestinationCellId,
                DestinationCellHash = door.DestinationCellHash,
                DestinationPosition = new float3(door.DestPosX, door.DestPosY, door.DestPosZ),
                DestinationRotation = new quaternion(door.DestRotX, door.DestRotY, door.DestRotZ, door.DestRotW),
            };
            return true;
        }
    }


    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup))]
    [UpdateAfter(typeof(DoorMotionActivationSystem))]
    [UpdateBefore(typeof(DoorMotionSystem))]
    public partial class TeleportDoorTransitionSystem : SystemBase
    {
        static readonly float3 InteriorWorldOffset = float3.zero;
        const float TeleportGroundProbeLiftMw = 20f;
        const float TeleportGroundRayCorrectionThresholdMw = 35f;
        const float TeleportDiagnosticCastWidthMw = 512f;
        const float TeleportDiagnosticCastHeightMw = 128f;
        const int TeleportDiagnosticMaxBodyLines = 24;
        const int TeleportDiagnosticMaxHitLines = 12;
        const float SupportSlopeReportingThreshold = 0.97f;
        static readonly ProfilerMarker k_Transition = new("VV.Streaming.TeleportDoorTransition");

        readonly HashSet<uint> _loggedMissingInteractionSounds = new();

        EntityQuery _requestQuery;
        EntityQuery _transitionQuery;
        EntityQuery _focusQuery;
        EntityQuery _streamingQuery;
        EntityQuery _playerQuery;
        EntityQuery _viewQuery;
        EntityQuery _groundingColliderQuery;

        protected override void OnCreate()
        {
            _requestQuery = GetEntityQuery(ComponentType.ReadWrite<InteractionActivationRequest>());
            _transitionQuery = GetEntityQuery(ComponentType.ReadWrite<InteriorTransitionState>(), ComponentType.ReadWrite<InteriorSpawnedEntity>());
            _focusQuery = GetEntityQuery(ComponentType.ReadWrite<PlayerInteractionFocus>());
            _streamingQuery = GetEntityQuery(
                ComponentType.ReadWrite<StreamingConfig>(),
                ComponentType.ReadWrite<LogicalRefLookup>(),
                ComponentType.ReadOnly<AvailableCells>(),
                ComponentType.ReadWrite<LoadedCellsMap>());
            _playerQuery = GetEntityQuery(
                ComponentType.ReadWrite<PlayerTag>(),
                ComponentType.ReadWrite<LocalTransform>(),
                ComponentType.ReadWrite<LocalToWorld>(),
                ComponentType.ReadWrite<PlayerCharacterComponent>(),
                ComponentType.ReadWrite<PlayerCharacterControl>(),
                ComponentType.ReadWrite<PlayerCharacterState>(),
                ComponentType.ReadWrite<MorrowindMovementInput>(),
                ComponentType.ReadWrite<MorrowindMovementState>());
            _viewQuery = GetEntityQuery(
                ComponentType.ReadWrite<PlayerViewComponent>(),
                ComponentType.ReadWrite<LocalTransform>(),
                ComponentType.ReadWrite<LocalToWorld>());
            _groundingColliderQuery = GetEntityQuery(ComponentType.ReadOnly<PhysicsCollider>());

            RequireForUpdate(_requestQuery);
            RequireForUpdate(_transitionQuery);
            RequireForUpdate(_focusQuery);
            RequireForUpdate(_streamingQuery);
            RequireForUpdate(_playerQuery);
            RequireForUpdate(_viewQuery);
            RequireForUpdate<InteractionRuntimeState>();
            RequireForUpdate<InteractionAudioRequestState>();
            RequireForUpdate<InteractionAudioRequest>();
            RequireForUpdate<MorrowindMovementSettings>();
            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<RuntimeWorldCellBlobReference>();
        }

        protected override void OnUpdate()
        {
            using var _ = k_Transition.Auto();

            var requestRef = _requestQuery.GetSingletonRW<InteractionActivationRequest>();
            ref var request = ref requestRef.ValueRW;
            if (request.Pending == 0 || request.Kind != (byte)InteractableKind.Door)
                return;

            CompleteDependency();

            var transitionEntity = _transitionQuery.GetSingletonEntity();
            var transitionRef = _transitionQuery.GetSingletonRW<InteriorTransitionState>();
            ref var transition = ref transitionRef.ValueRW;
            transition.TransitionInProgress = 1;

            Entity target = request.TargetEntity;
            request.Pending = 0;
            request.TargetEntity = Entity.Null;

            var worldCellReference = SystemAPI.GetSingleton<RuntimeWorldCellBlobReference>();
            if (!worldCellReference.Blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][WorldCellBlob] teleport door transition requires runtime world cell blob.");
            ref RuntimeWorldCellBlob worldCells = ref worldCellReference.Blob.Value;

            if (!EntityManager.Exists(target)
                || (!EntityManager.HasComponent<DoorInteractable>(target)
                    && !DoorInteractableResolver.TryResolve(EntityManager, ref worldCells, target, out DoorInteractable _)))
            {
                Debug.LogWarning("[VVardenfell][Interaction] door activation request resolved to a missing or non-door logical entity.");
                ClearFocus();
                transition.TransitionInProgress = 0;
                return;
            }

            var door = EntityManager.HasComponent<DoorInteractable>(target)
                ? EntityManager.GetComponentData<DoorInteractable>(target)
                : DoorInteractableResolver.TryResolve(EntityManager, ref worldCells, target, out DoorInteractable resolvedDoor)
                    ? resolvedDoor
                    : default;
            if (door.IsTeleport == 0)
            {
                TryQueueInteractionAudio(target, InteractionAudioKind.Door, "door");
                ClearFocus();
                transition.TransitionInProgress = 0;
                return;
            }

            if (SystemAPI.GetSingleton<RuntimeShellState>().TeleportingDisabled != 0)
            {
                ClearFocus();
                transition.TransitionInProgress = 0;
                return;
            }

            bool goesToInterior = door.DestinationCellHash != 0UL;
            BlobAssetReference<Collider> destinationInteriorStaticCollider = default;
            if (goesToInterior)
            {
                if (!RuntimeWorldCellBlobUtility.TryGetInteriorCellIndex(ref worldCells, door.DestinationCellHash, out int destinationCellIndex))
                {
                    string destinationCellId = door.DestinationCellId.ToString();
                    throw new System.InvalidOperationException($"[VVardenfell][Streaming] teleport destination interior '{destinationCellId}' is missing from the world cell blob.");
                }

                WorldSpawner.TryGetInteriorStaticCollider(door.DestinationCellHash, out destinationInteriorStaticCollider);
            }

            TryQueueInteractionAudio(target, InteractionAudioKind.Door, "door");


            var streamingEntity = _streamingQuery.GetSingletonEntity();
            var configRef = EntityManager.GetComponentData<StreamingConfig>(streamingEntity);
            var logicalRefLookup = EntityManager.GetComponentData<LogicalRefLookup>(streamingEntity);
            var available = EntityManager.GetComponentData<AvailableCells>(streamingEntity);
            var loaded = EntityManager.GetComponentData<LoadedCellsMap>(streamingEntity);

            if (goesToInterior)
            {
                DestroyInteriorEntities(transitionEntity, ref logicalRefLookup);
                WorldSpawner.HideExteriorVisibility(World, ref loaded);
                if (!WorldSpawner.TrySpawnInteriorCellByHash(World, door.DestinationCellHash, InteriorWorldOffset, transitionEntity, ref logicalRefLookup, out FixedString128Bytes spawnedInteriorCellId))
                    throw new System.InvalidOperationException($"[VVardenfell][Streaming] teleport destination interior '{door.DestinationCellId}' was not preloaded.");
                configRef.ExteriorStreamingPaused = true;
                transition.InteriorActive = 1;
                transition.ActiveInteriorCellId = spawnedInteriorCellId.IsEmpty ? door.DestinationCellId : spawnedInteriorCellId;
                transition.ActiveInteriorCellHash = door.DestinationCellHash;
                ref var runtimeState = ref SystemAPI.GetSingletonRW<InteractionRuntimeState>().ValueRW;
                runtimeState.PendingPickedItemPrune = 1;
            }
            else
            {
                DestroyInteriorEntities(transitionEntity, ref logicalRefLookup);
                configRef.ExteriorStreamingPaused = false;
                transition.InteriorActive = 0;
                transition.ActiveInteriorCellId = default;
                transition.ActiveInteriorCellHash = 0UL;
            }

            float3 destinationPosition = door.DestinationPosition + (goesToInterior ? InteriorWorldOffset : float3.zero);
            if (!goesToInterior)
            {
                float cellM = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
                configRef.CameraCell = new int2(
                    (int)math.floor(destinationPosition.x / cellM),
                    (int)math.floor(destinationPosition.z / cellM));
                WorldSpawner.SyncExteriorVisibility(World, configRef, available, ref loaded);
            }

            quaternion bodyYawRotation = ExtractYawRotation(door.DestinationRotation);
            if (!TryGroundTeleportDestination(destinationPosition, bodyYawRotation, goesToInterior, destinationInteriorStaticCollider, out var grounded))
            {
                string destinationLabel = goesToInterior
                    ? $"interior '{door.DestinationCellId.ToString()}'"
                    : $"exterior position {destinationPosition}";
                LogTeleportGroundingFailureDiagnostics(destinationPosition, bodyYawRotation, goesToInterior, destinationInteriorStaticCollider, destinationLabel);
                throw new System.InvalidOperationException($"[VVardenfell][Streaming] teleport destination in {destinationLabel} could not be grounded.");
            }

            MovePlayerToDestination(grounded.Position, bodyYawRotation, grounded);

            EntityManager.SetComponentData(streamingEntity, configRef);
            EntityManager.SetComponentData(streamingEntity, logicalRefLookup);
            EntityManager.SetComponentData(streamingEntity, loaded);
            EntityManager.SetComponentData(transitionEntity, transition);

            if (goesToInterior)
                RuntimeSpawnProjectionUtility.TryRestoreAliveRefsForCurrentWorld(EntityManager);

            ClearFocus();
            transition.TransitionInProgress = 0;

        }

        void MovePlayerToDestination(float3 destinationPosition, quaternion bodyYawRotation, in GroundedTeleportDestination grounded)
        {
            Entity playerEntity = _playerQuery.GetSingletonEntity();
            var character = EntityManager.GetComponentData<PlayerCharacterComponent>(playerEntity);
            var control = EntityManager.GetComponentData<PlayerCharacterControl>(playerEntity);
            var state = EntityManager.GetComponentData<PlayerCharacterState>(playerEntity);
            var movementInput = EntityManager.GetComponentData<MorrowindMovementInput>(playerEntity);
            var movementState = EntityManager.GetComponentData<MorrowindMovementState>(playerEntity);
            var playerTransform = EntityManager.GetComponentData<LocalTransform>(playerEntity);

            playerTransform.Position = destinationPosition;
            playerTransform.Rotation = bodyYawRotation;
            EntityManager.SetComponentData(playerEntity, playerTransform);
            EntityManager.SetComponentData(playerEntity, new LocalToWorld
            {
                Value = float4x4.TRS(destinationPosition, bodyYawRotation, new float3(playerTransform.Scale))
            });

            control.LookDeltaDegrees = float2.zero;
            control.MoveVectorWorld = float3.zero;
            control.InteractPressed = false;
            control.JumpThisFixedTick = false;
            control.JumpPressedEvent.Clear();
            EntityManager.SetComponentData(playerEntity, control);

            movementInput = default;
            EntityManager.SetComponentData(playerEntity, movementInput);

            movementState.Inertia = float3.zero;
            movementState.LastVelocity = float3.zero;
            movementState.LocalMove = float2.zero;
            movementState.SpeedFactor = 0f;
            movementState.Flags = 0;
            movementState.Grounded = true;
            movementState.SupportKind = grounded.SupportKind;
            movementState.StandingOn = grounded.StandingOn;
            movementState.GroundNormal = grounded.GroundNormal;
            EntityManager.SetComponentData(playerEntity, movementState);

            state.WorldVelocity = float3.zero;
            state.Grounded = true;
            state.WasGrounded = true;
            state.GroundedTime = 0.001f;
            state.AirborneTime = 0f;
            EntityManager.SetComponentData(playerEntity, state);

            Entity viewEntity = _viewQuery.GetSingletonEntity();
            var view = EntityManager.GetComponentData<PlayerViewComponent>(viewEntity);
            float eyeHeight = state.Crouched ? character.CrouchingEyeHeight : character.StandingEyeHeight;
            view.LocalPitchDegrees = 0f;
            view.LocalViewRotation = quaternion.identity;
            view.LocalEyeOffset = new float3(0f, eyeHeight, 0f);
            EntityManager.SetComponentData(viewEntity, view);
            EntityManager.SetComponentData(viewEntity, LocalTransform.FromPositionRotationScale(
                view.LocalEyeOffset,
                quaternion.identity,
                1f));
            EntityManager.SetComponentData(viewEntity, new LocalToWorld
            {
                Value = float4x4.TRS(destinationPosition + math.rotate(bodyYawRotation, view.LocalEyeOffset), bodyYawRotation, new float3(1f))
            });
        }

        bool TryGroundTeleportDestination(
            float3 destinationPosition,
            quaternion bodyYawRotation,
            bool destinationIsInterior,
            BlobAssetReference<Collider> destinationInteriorStaticCollider,
            out GroundedTeleportDestination grounded)
        {
            grounded = default;

            Entity playerEntity = _playerQuery.GetSingletonEntity();
            if (!EntityManager.HasComponent<PhysicsCollider>(playerEntity))
                throw new System.InvalidOperationException("[VVardenfell][Streaming] player teleport grounding requires a PhysicsCollider on the player.");

            var playerCollider = EntityManager.GetComponentData<PhysicsCollider>(playerEntity);
            if (!playerCollider.Value.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][Streaming] player teleport grounding requires a valid player collider blob.");

            var movementSettings = SystemAPI.GetSingleton<MorrowindMovementSettings>();
            float probeLift = TeleportGroundProbeLiftMw * WorldScale.MwUnitsToMeters;
            float probeDistance = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            float3 start = destinationPosition + new float3(0f, probeLift, 0f);
            float3 end = start - new float3(0f, probeDistance, 0f);
            var input = new ColliderCastInput(playerCollider.Value, start, end, bodyYawRotation);

            bool foundWalkable = false;
            ColliderCastHit bestWalkableHit = default;
            var colliderHits = new NativeList<ColliderCastHit>(Allocator.Temp);
            try
            {
                if (destinationIsInterior)
                {
                    if (destinationInteriorStaticCollider.IsCreated)
                    {
                        var body = BuildGroundingBody(destinationInteriorStaticCollider, Entity.Null, InteriorWorldOffset);
                        TryFindGroundingColliderHit(
                            body,
                            input,
                            movementSettings.MaxSlopeCosine,
                            ref colliderHits,
                            ref foundWalkable,
                            ref bestWalkableHit);
                    }
                }
                else
                {
                    float cellM = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
                    var coord = new int2(
                        (int)math.floor(destinationPosition.x / cellM),
                        (int)math.floor(destinationPosition.z / cellM));
                    float3 cellOrigin = new float3(coord.x * cellM, 0f, coord.y * cellM);
                    if (WorldResources.TryGetTerrainCollider(coord, out var terrainCollider))
                    {
                        var body = BuildGroundingBody(terrainCollider, Entity.Null, cellOrigin);
                        TryFindGroundingColliderHit(
                            body,
                            input,
                            movementSettings.MaxSlopeCosine,
                            ref colliderHits,
                            ref foundWalkable,
                            ref bestWalkableHit);
                    }
                    if (WorldResources.TryGetStaticCellCollider(coord, out var staticCollider))
                    {
                        var body = BuildGroundingBody(staticCollider, Entity.Null, cellOrigin);
                        TryFindGroundingColliderHit(
                            body,
                            input,
                            movementSettings.MaxSlopeCosine,
                            ref colliderHits,
                            ref foundWalkable,
                            ref bestWalkableHit);
                    }
                }
            }
            finally
            {
                if (colliderHits.IsCreated)
                    colliderHits.Dispose();
            }

            using var entities = _groundingColliderQuery.ToEntityArray(Allocator.Temp);
            using var colliders = _groundingColliderQuery.ToComponentDataArray<PhysicsCollider>(Allocator.Temp);
            colliderHits = new NativeList<ColliderCastHit>(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity entity = entities[i];
                    if (entity == playerEntity || !colliders[i].Value.IsCreated)
                        continue;
                    if (!IsTeleportGroundingCollider(entity))
                        continue;
                    if (!TryBuildRigidBody(entity, colliders[i], out RigidBody body))
                        continue;
                    TryFindGroundingColliderHit(
                        body,
                        input,
                        movementSettings.MaxSlopeCosine,
                        ref colliderHits,
                        ref foundWalkable,
                        ref bestWalkableHit);
                }
            }
            finally
            {
                if (colliderHits.IsCreated)
                    colliderHits.Dispose();
            }

            bool foundRay = TryFindTeleportGroundingRayHit(
                start,
                probeDistance,
                destinationIsInterior,
                destinationInteriorStaticCollider,
                destinationPosition,
                entities,
                colliders,
                out Unity.Physics.RaycastHit bestRayHit);

            if (foundRay)
            {
                bool useRay = !foundWalkable;
                if (!useRay)
                {
                    float capsuleHitDistance = probeDistance * bestWalkableHit.Fraction;
                    float3 capsuleHitPosition = start - new float3(0f, capsuleHitDistance, 0f);
                    float correctionThreshold = TeleportGroundRayCorrectionThresholdMw * WorldScale.MwUnitsToMeters;
                    useRay = math.lengthsq(bestRayHit.Position - capsuleHitPosition) > correctionThreshold * correctionThreshold;
                }

                if (useRay)
                {
                    grounded = new GroundedTeleportDestination
                    {
                        Position = bestRayHit.Position + new float3(0f, movementSettings.GroundOffset, 0f),
                        GroundNormal = bestRayHit.SurfaceNormal.y > 0f ? bestRayHit.SurfaceNormal : math.up(),
                        StandingOn = bestRayHit.Entity,
                        SupportKind = bestRayHit.SurfaceNormal.y >= SupportSlopeReportingThreshold
                            ? (byte)MorrowindSupportKind.FlatGround
                            : (byte)MorrowindSupportKind.WalkableSlope,
                    };
                    return true;
                }
            }

            if (!foundWalkable)
                return false;

            float hitDistance = probeDistance * bestWalkableHit.Fraction;
            float3 hitPosition = start - new float3(0f, hitDistance, 0f);
            float3 supportedPosition = hitPosition + new float3(0f, movementSettings.GroundOffset, 0f);
            grounded = new GroundedTeleportDestination
            {
                Position = supportedPosition,
                GroundNormal = bestWalkableHit.SurfaceNormal,
                StandingOn = bestWalkableHit.Entity,
                SupportKind = bestWalkableHit.SurfaceNormal.y >= SupportSlopeReportingThreshold
                    ? (byte)MorrowindSupportKind.FlatGround
                    : (byte)MorrowindSupportKind.WalkableSlope,
            };
            return true;
        }

        static RigidBody BuildGroundingBody(BlobAssetReference<Collider> collider, Entity entity, float3 position)
        {
            return new RigidBody
            {
                Collider = collider,
                Entity = entity,
                WorldFromBody = new RigidTransform(quaternion.identity, position),
                Scale = 1f,
            };
        }

        static void TryFindGroundingColliderHit(
            RigidBody body,
            in ColliderCastInput input,
            float maxSlopeCosine,
            ref NativeList<ColliderCastHit> hits,
            ref bool foundWalkable,
            ref ColliderCastHit bestWalkableHit)
        {
            hits.Clear();
            if (!body.CastCollider(input, ref hits))
                return;

            for (int hitIndex = 0; hitIndex < hits.Length; hitIndex++)
            {
                ColliderCastHit hit = hits[hitIndex];
                if (hit.SurfaceNormal.y <= maxSlopeCosine)
                    continue;

                if (foundWalkable && hit.Fraction >= bestWalkableHit.Fraction)
                    continue;

                foundWalkable = true;
                bestWalkableHit = hit;
            }
        }

        bool TryFindTeleportGroundingRayHit(
            float3 start,
            float probeDistance,
            bool destinationIsInterior,
            BlobAssetReference<Collider> destinationInteriorStaticCollider,
            float3 destinationPosition,
            NativeArray<Entity> entities,
            NativeArray<PhysicsCollider> colliders,
            out Unity.Physics.RaycastHit bestHit)
        {
            bestHit = default;
            var input = new RaycastInput
            {
                Start = start,
                End = start - new float3(0f, probeDistance, 0f),
                Filter = CollisionFilter.Default,
            };

            bool found = false;
            if (destinationIsInterior)
            {
                if (destinationInteriorStaticCollider.IsCreated)
                {
                    var body = BuildGroundingBody(destinationInteriorStaticCollider, Entity.Null, InteriorWorldOffset);
                    TryFindTeleportGroundingRayHit(body, input, ref found, ref bestHit);
                }
            }
            else
            {
                float cellM = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
                var coord = new int2(
                    (int)math.floor(destinationPosition.x / cellM),
                    (int)math.floor(destinationPosition.z / cellM));
                float3 cellOrigin = new float3(coord.x * cellM, 0f, coord.y * cellM);
                if (WorldResources.TryGetTerrainCollider(coord, out var terrainCollider))
                {
                    var body = BuildGroundingBody(terrainCollider, Entity.Null, cellOrigin);
                    TryFindTeleportGroundingRayHit(body, input, ref found, ref bestHit);
                }
                if (WorldResources.TryGetStaticCellCollider(coord, out var staticCollider))
                {
                    var body = BuildGroundingBody(staticCollider, Entity.Null, cellOrigin);
                    TryFindTeleportGroundingRayHit(body, input, ref found, ref bestHit);
                }
            }

            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                if (!colliders[i].Value.IsCreated)
                    continue;
                if (!IsTeleportGroundingCollider(entity))
                    continue;
                if (!TryBuildRigidBody(entity, colliders[i], out RigidBody body))
                    continue;
                TryFindTeleportGroundingRayHit(body, input, ref found, ref bestHit);
            }

            return found;
        }

        static void TryFindTeleportGroundingRayHit(
            RigidBody body,
            in RaycastInput input,
            ref bool found,
            ref Unity.Physics.RaycastHit bestHit)
        {
            if (!body.CastRay(input, out Unity.Physics.RaycastHit hit))
                return;
            if (found && hit.Fraction >= bestHit.Fraction)
                return;

            found = true;
            bestHit = hit;
        }

        void LogTeleportGroundingFailureDiagnostics(
            float3 destinationPosition,
            quaternion bodyYawRotation,
            bool destinationIsInterior,
            BlobAssetReference<Collider> destinationInteriorStaticCollider,
            string destinationLabel)
        {
            float probeLift = TeleportGroundProbeLiftMw * WorldScale.MwUnitsToMeters;
            float probeDistance = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
            float3 start = destinationPosition + new float3(0f, probeLift, 0f);
            float3 end = start - new float3(0f, probeDistance, 0f);
            float diagnosticWidth = TeleportDiagnosticCastWidthMw * WorldScale.MwUnitsToMeters;
            float diagnosticHeight = TeleportDiagnosticCastHeightMw * WorldScale.MwUnitsToMeters;

            var diagnosticCollider = BoxCollider.Create(new BoxGeometry
            {
                Center = float3.zero,
                Orientation = quaternion.identity,
                Size = new float3(diagnosticWidth, diagnosticHeight, diagnosticWidth),
                BevelRadius = 0f,
            });

            var log = new StringBuilder(2048);
            log.Append("[VVardenfell][Streaming] teleport grounding diagnostics for ")
                .Append(destinationLabel)
                .AppendLine(":");
            log.Append("  destination=").Append(FormatFloat3(destinationPosition))
                .Append(" castStart=").Append(FormatFloat3(start))
                .Append(" castEnd=").Append(FormatFloat3(end))
                .Append(" diagnosticBoxSize=").Append(FormatFloat3(new float3(diagnosticWidth, diagnosticHeight, diagnosticWidth)))
                .AppendLine();

            int directBodyCount = 0;
            int activePhysicsColliderCount = 0;
            int activeGroundingCandidateCount = 0;
            int activeBodyBuildFailureCount = 0;
            int hitBodyCount = 0;
            int hitCount = 0;
            int bodyLineCount = 0;
            int hitLineCount = 0;

            var input = new ColliderCastInput(diagnosticCollider, start, end, bodyYawRotation);
            var hits = new NativeList<ColliderCastHit>(Allocator.Temp);
            try
            {
                if (destinationIsInterior)
                {
                    bool hasInteriorCollider = destinationInteriorStaticCollider.IsCreated;
                    log.Append("  destinationInteriorStaticCollider=").Append(hasInteriorCollider).AppendLine();
                    if (hasInteriorCollider)
                    {
                        directBodyCount++;
                        var body = BuildGroundingBody(destinationInteriorStaticCollider, Entity.Null, InteriorWorldOffset);
                        AddTeleportDiagnosticColliderCast(
                            "destination interior static blob",
                            body,
                            input,
                            ref hits,
                            log,
                            ref hitBodyCount,
                            ref hitCount,
                            ref bodyLineCount,
                            ref hitLineCount);
                    }
                }
                else
                {
                    float cellM = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
                    var coord = new int2(
                        (int)math.floor(destinationPosition.x / cellM),
                        (int)math.floor(destinationPosition.z / cellM));
                    float3 cellOrigin = new float3(coord.x * cellM, 0f, coord.y * cellM);
                    log.Append("  destinationExteriorCell=").Append(coord).AppendLine();

                    if (WorldResources.TryGetTerrainCollider(coord, out var terrainCollider))
                    {
                        directBodyCount++;
                        var body = BuildGroundingBody(terrainCollider, Entity.Null, cellOrigin);
                        AddTeleportDiagnosticColliderCast(
                            "destination exterior terrain blob",
                            body,
                            input,
                            ref hits,
                            log,
                            ref hitBodyCount,
                            ref hitCount,
                            ref bodyLineCount,
                            ref hitLineCount);
                    }

                    if (WorldResources.TryGetStaticCellCollider(coord, out var staticCollider))
                    {
                        directBodyCount++;
                        var body = BuildGroundingBody(staticCollider, Entity.Null, cellOrigin);
                        AddTeleportDiagnosticColliderCast(
                            "destination exterior static blob",
                            body,
                            input,
                            ref hits,
                            log,
                            ref hitBodyCount,
                            ref hitCount,
                            ref bodyLineCount,
                            ref hitLineCount);
                    }
                }

                using var entities = _groundingColliderQuery.ToEntityArray(Allocator.Temp);
                using var colliders = _groundingColliderQuery.ToComponentDataArray<PhysicsCollider>(Allocator.Temp);
                activePhysicsColliderCount = entities.Length;

                for (int i = 0; i < entities.Length; i++)
                {
                    Entity entity = entities[i];
                    if (!colliders[i].Value.IsCreated)
                        continue;
                    if (!IsTeleportGroundingCollider(entity))
                        continue;

                    activeGroundingCandidateCount++;
                    if (!TryBuildRigidBody(entity, colliders[i], out RigidBody body))
                    {
                        activeBodyBuildFailureCount++;
                        continue;
                    }

                    AddTeleportDiagnosticColliderCast(
                        $"active {GetRuntimeColliderKindLabel(entity)} collider entity {entity.Index}:{entity.Version}",
                        body,
                        input,
                        ref hits,
                        log,
                        ref hitBodyCount,
                        ref hitCount,
                        ref bodyLineCount,
                        ref hitLineCount);
                }

                log.Append("  directBodies=").Append(directBodyCount)
                    .Append(" activePhysicsColliders=").Append(activePhysicsColliderCount)
                    .Append(" activeGroundingCandidates=").Append(activeGroundingCandidateCount)
                    .Append(" activeBodyBuildFailures=").Append(activeBodyBuildFailureCount)
                    .Append(" diagnosticHitBodies=").Append(hitBodyCount)
                    .Append(" diagnosticHits=").Append(hitCount)
                    .AppendLine();
                if (hitCount > hitLineCount)
                {
                    log.Append("  hitLogTruncated=true omittedHits=").Append(hitCount - hitLineCount).AppendLine();
                }
                int testedBodyCount = directBodyCount + activeGroundingCandidateCount - activeBodyBuildFailureCount;
                if (testedBodyCount > bodyLineCount)
                {
                    log.Append("  bodyLogTruncated=true omittedBodies=").Append(testedBodyCount - bodyLineCount).AppendLine();
                }
            }
            finally
            {
                if (hits.IsCreated)
                    hits.Dispose();
                if (diagnosticCollider.IsCreated)
                    diagnosticCollider.Dispose();
            }

            Debug.LogWarning(log.ToString());
        }

        static void AddTeleportDiagnosticColliderCast(
            string label,
            RigidBody body,
            in ColliderCastInput input,
            ref NativeList<ColliderCastHit> hits,
            StringBuilder log,
            ref int hitBodyCount,
            ref int hitCount,
            ref int bodyLineCount,
            ref int hitLineCount)
        {
            hits.Clear();
            Aabb aabb = body.CalculateAabb();
            bool didHit = body.CastCollider(input, ref hits);
            if (didHit)
            {
                hitBodyCount++;
                hitCount += hits.Length;
            }

            if (bodyLineCount < TeleportDiagnosticMaxBodyLines)
            {
                log.Append("  diagnosticBody label=").Append(label)
                    .Append(" hit=").Append(didHit)
                    .Append(" hitCount=").Append(hits.Length)
                    .Append(" bodyAabbMin=").Append(FormatFloat3(aabb.Min))
                    .Append(" bodyAabbMax=").Append(FormatFloat3(aabb.Max))
                    .AppendLine();
                bodyLineCount++;
            }

            for (int i = 0; i < hits.Length && hitLineCount < TeleportDiagnosticMaxHitLines; i++, hitLineCount++)
            {
                ColliderCastHit hit = hits[i];
                log.Append("    hit fraction=").Append(hit.Fraction.ToString("F5"))
                    .Append(" position=").Append(FormatFloat3(hit.Position))
                    .Append(" normal=").Append(FormatFloat3(hit.SurfaceNormal))
                    .Append(" entity=").Append(hit.Entity.Index).Append(':').Append(hit.Entity.Version)
                    .Append(" colliderKey=").Append(hit.ColliderKey.Value)
                    .AppendLine();
            }
        }

        string GetRuntimeColliderKindLabel(Entity entity)
        {
            if (!EntityManager.HasComponent<RuntimeColliderSource>(entity))
                return "untracked";
            return EntityManager.GetComponentData<RuntimeColliderSource>(entity).Kind.ToString();
        }

        static string FormatFloat3(float3 value)
        {
            return $"({value.x:F3}, {value.y:F3}, {value.z:F3})";
        }

        bool IsTeleportGroundingCollider(Entity entity)
        {
            if (!EntityManager.HasComponent<RuntimeColliderSource>(entity))
                return false;

            RuntimeColliderKind kind = EntityManager.GetComponentData<RuntimeColliderSource>(entity).Kind;
            return kind == RuntimeColliderKind.TerrainCell
                   || kind == RuntimeColliderKind.StaticCell;
        }

        bool TryBuildRigidBody(Entity entity, in PhysicsCollider collider, out RigidBody body)
        {
            body = default;

            if (EntityManager.HasComponent<LocalToWorld>(entity))
            {
                var matrix = EntityManager.GetComponentData<LocalToWorld>(entity).Value;
                float3 c0 = matrix.c0.xyz;
                float3 c1 = matrix.c1.xyz;
                float3 c2 = matrix.c2.xyz;
                float scale = math.length(c0);
                if (scale <= 1e-5f)
                    return false;

                var rotation = new quaternion(new float3x3(c0 / scale, c1 / scale, c2 / scale));
                body = new RigidBody
                {
                    Collider = collider.Value,
                    Entity = entity,
                    WorldFromBody = new RigidTransform(rotation, matrix.c3.xyz),
                    Scale = scale,
                };
                return true;
            }

            if (!EntityManager.HasComponent<LocalTransform>(entity))
                return false;

            var transform = EntityManager.GetComponentData<LocalTransform>(entity);
            body = new RigidBody
            {
                Collider = collider.Value,
                Entity = entity,
                WorldFromBody = new RigidTransform(transform.Rotation, transform.Position),
                Scale = transform.Scale,
            };
            return true;
        }

        struct GroundedTeleportDestination
        {
            public float3 Position;
            public float3 GroundNormal;
            public Entity StandingOn;
            public byte SupportKind;
        }

        void DestroyInteriorEntities(Entity transitionEntity, ref LogicalRefLookup logicalRefLookup)
        {
            var spawnedBuffer = EntityManager.GetBuffer<InteriorSpawnedEntity>(transitionEntity);
            if (spawnedBuffer.Length == 0)
                return;

            var entitiesToDestroy = new Entity[spawnedBuffer.Length];
            for (int i = 0; i < spawnedBuffer.Length; i++)
                entitiesToDestroy[i] = spawnedBuffer[i].Value;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            for (int i = 0; i < entitiesToDestroy.Length; i++)
            {
                if (EntityManager.Exists(entitiesToDestroy[i])
                    && EntityManager.HasComponent<LogicalRefTag>(entitiesToDestroy[i]))
                {
                    LogicalRefDestroyUtility.QueueDestroyLogicalRef(
                        EntityManager,
                        ref ecb,
                        entitiesToDestroy[i],
                        ref logicalRefLookup,
                        preserveRuntimeSpawnRegistration: true);
                    continue;
                }

                if (EntityManager.Exists(entitiesToDestroy[i]))
                    ecb.DestroyEntity(entitiesToDestroy[i]);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
            EntityManager.GetBuffer<InteriorSpawnedEntity>(transitionEntity).Clear();
        }

        void ClearFocus()
        {
            var focusRef = _focusQuery.GetSingletonRW<PlayerInteractionFocus>();
            focusRef.ValueRW = new PlayerInteractionFocus
            {
                TargetEntity = Entity.Null,
            };
        }

        void TryQueueInteractionAudio(Entity target, InteractionAudioKind kind, string label)
        {
            if (!EntityManager.Exists(target) || !EntityManager.HasComponent<PlacedRefIdentity>(target))
                return;

            uint placedRefId = EntityManager.GetComponentData<PlacedRefIdentity>(target).Value;
            if (!EntityManager.HasComponent<AudioEmitterAuthoring>(target))
            {
                WarnMissingInteractionSoundOnce(placedRefId, label, "has no AudioEmitterAuthoring component; skipping interaction one-shot.");
                return;
            }

            var emitter = EntityManager.GetComponentData<AudioEmitterAuthoring>(target);
            if (!emitter.PrimarySound.IsValid)
            {
                WarnMissingInteractionSoundOnce(placedRefId, label, "has no primary interaction sound; skipping interaction one-shot.");
                return;
            }

            float3 position = ResolveAudioPosition(target);
            ref var audioState = ref SystemAPI.GetSingletonRW<InteractionAudioRequestState>().ValueRW;
            uint sequence = audioState.NextSequence + 1u;
            audioState.NextSequence = sequence;

            var requests = SystemAPI.GetSingletonBuffer<InteractionAudioRequest>();
            requests.Add(new InteractionAudioRequest
            {
                Sequence = sequence,
                Sound = emitter.PrimarySound,
                Position = position,
                SourcePlacedRefId = placedRefId,
                Kind = (byte)kind,
            });

        }

        float3 ResolveAudioPosition(Entity target)
        {
            if (EntityManager.HasComponent<LocalToWorld>(target))
                return EntityManager.GetComponentData<LocalToWorld>(target).Value.c3.xyz;

            if (EntityManager.HasComponent<LocalTransform>(target))
                return EntityManager.GetComponentData<LocalTransform>(target).Position;

            return float3.zero;
        }

        void WarnMissingInteractionSoundOnce(uint placedRefId, string label, string reason)
        {
            if (placedRefId == 0u || !_loggedMissingInteractionSounds.Add(placedRefId))
                return;

        }

        static quaternion ExtractYawRotation(quaternion sourceRotation)
        {
            float3 forward = math.rotate(sourceRotation, new float3(0f, 0f, 1f));
            forward.y = 0f;
            if (math.lengthsq(forward) < 1e-5f)
                return quaternion.identity;
            forward = math.normalize(forward);
            return quaternion.LookRotationSafe(forward, math.up());
        }
    }
}
