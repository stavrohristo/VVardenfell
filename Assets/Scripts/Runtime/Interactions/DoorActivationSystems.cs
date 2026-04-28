using System.Collections.Generic;
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
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Content;
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
        public static bool TryResolve(EntityManager entityManager, Entity logicalEntity, out DoorInteractable interactable)
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
            return TryBuild(location, placedRefId, out interactable);
        }

        static bool TryBuild(in LogicalRefLocation location, uint placedRefId, out DoorInteractable interactable)
        {
            interactable = default;

            CellData cell = null;
            if (location.IsInterior != 0)
            {
                string interiorCellId = location.InteriorCellId.ToString();
                if (!WorldResources.InteriorCells.TryGetValue(interiorCellId, out cell) || cell == null)
                    return false;
            }
            else
            {
                if (!WorldResources.Cells.TryGetValue(location.ExteriorCell, out cell) || cell == null)
                    return false;
            }

            var refs = cell.Refs;
            var doors = cell.Doors;
            if (refs == null || doors == null)
                return false;

            for (int i = 0; i < refs.Length; i++)
            {
                ref readonly var entry = ref refs[i];
                if (entry.PlacedRefId != placedRefId || entry.DoorMetaIndex < 0 || entry.DoorMetaIndex >= doors.Length)
                    continue;

                var door = doors[entry.DoorMetaIndex];
                interactable = new DoorInteractable
                {
                    IsTeleport = (byte)((door.Flags & DoorRefEntry.FlagTeleport) != 0 ? 1 : 0),
                    DestinationCellId = new FixedString128Bytes(door.DestinationCellId ?? string.Empty),
                    DestinationPosition = new float3(door.DestPosX, door.DestPosY, door.DestPosZ),
                    DestinationRotation = new quaternion(door.DestRotX, door.DestRotY, door.DestRotZ, door.DestRotW),
                };
                return true;
            }

            return false;
        }
    }


    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup))]
    public partial class TeleportDoorTransitionSystem : SystemBase
    {
        static readonly float3 InteriorWorldOffset = float3.zero;
        static readonly ProfilerMarker k_Transition = new("VV.Streaming.TeleportDoorTransition");

        readonly HashSet<uint> _loggedMissingInteractionSounds = new();

        EntityQuery _requestQuery;
        EntityQuery _transitionQuery;
        EntityQuery _focusQuery;
        EntityQuery _streamingQuery;
        EntityQuery _playerQuery;
        EntityQuery _viewQuery;

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

            RequireForUpdate(_requestQuery);
            RequireForUpdate(_transitionQuery);
            RequireForUpdate(_focusQuery);
            RequireForUpdate(_streamingQuery);
            RequireForUpdate(_playerQuery);
            RequireForUpdate(_viewQuery);
            RequireForUpdate<InteractionRuntimeState>();
            RequireForUpdate<InteractionAudioRequestState>();
            RequireForUpdate<InteractionAudioRequest>();
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

            if (!EntityManager.Exists(target)
                || (!EntityManager.HasComponent<DoorInteractable>(target)
                    && !DoorInteractableResolver.TryResolve(EntityManager, target, out DoorInteractable _)))
            {
                Debug.LogWarning("[VVardenfell][Interaction] door activation request resolved to a missing or non-door logical entity.");
                ClearFocus();
                transition.TransitionInProgress = 0;
                return;
            }

            var door = EntityManager.HasComponent<DoorInteractable>(target)
                ? EntityManager.GetComponentData<DoorInteractable>(target)
                : DoorInteractableResolver.TryResolve(EntityManager, target, out DoorInteractable resolvedDoor)
                    ? resolvedDoor
                    : default;
            if (door.IsTeleport == 0)
            {
                TryQueueInteractionAudio(target, InteractionAudioKind.Door, "door");
                ClearFocus();
                transition.TransitionInProgress = 0;
                return;
            }

            bool goesToInterior = door.DestinationCellId.Length > 0;
            CellData destinationInterior = null;
            if (goesToInterior)
            {
                string destinationCellId = door.DestinationCellId.ToString();
                if (!WorldResources.InteriorCells.TryGetValue(destinationCellId, out destinationInterior) || destinationInterior == null)
                {
                    Debug.LogWarning($"[VVardenfell][Streaming] teleport destination interior '{destinationCellId}' was not preloaded; transition aborted.");
                    transition.TransitionInProgress = 0;
                    ClearFocus();
                    return;
                }
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
                WorldSpawner.SpawnInteriorCell(World, destinationInterior, InteriorWorldOffset, transitionEntity, ref logicalRefLookup);
                configRef.ExteriorStreamingPaused = true;
                transition.InteriorActive = 1;
                transition.ActiveInteriorCellId = door.DestinationCellId;
                ref var runtimeState = ref SystemAPI.GetSingletonRW<InteractionRuntimeState>().ValueRW;
                runtimeState.PendingPickedItemPrune = 1;
            }
            else
            {
                DestroyInteriorEntities(transitionEntity, ref logicalRefLookup);
                configRef.ExteriorStreamingPaused = false;
                transition.InteriorActive = 0;
                transition.ActiveInteriorCellId = default;
            }

            float3 destinationPosition = door.DestinationPosition + (goesToInterior ? InteriorWorldOffset : float3.zero);
            quaternion bodyYawRotation = ExtractYawRotation(door.DestinationRotation);
            MovePlayerToDestination(destinationPosition, bodyYawRotation);

            if (!goesToInterior)
            {
                float cellM = LandRecordSize.CellUnitsMw * WorldScale.MwUnitsToMeters;
                configRef.CameraCell = new int2(
                    (int)math.floor(destinationPosition.x / cellM),
                    (int)math.floor(destinationPosition.z / cellM));
                WorldSpawner.SyncExteriorVisibility(World, configRef, available, ref loaded);
            }

            EntityManager.SetComponentData(streamingEntity, configRef);
            EntityManager.SetComponentData(streamingEntity, logicalRefLookup);
            EntityManager.SetComponentData(streamingEntity, loaded);
            EntityManager.SetComponentData(transitionEntity, transition);

            if (goesToInterior)
                RestoreAliveRefsForCurrentWorld();

            ClearFocus();
            transition.TransitionInProgress = 0;

        }

        void MovePlayerToDestination(float3 destinationPosition, quaternion bodyYawRotation)
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
            movementState.SupportKind = 0;
            movementState.StandingOn = Entity.Null;
            movementState.GroundNormal = math.up();
            EntityManager.SetComponentData(playerEntity, movementState);

            state.WorldVelocity = float3.zero;
            state.Grounded = false;
            state.WasGrounded = false;
            state.GroundedTime = 0f;
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

        void RestoreAliveRefsForCurrentWorld()
        {
            var createEcb = new EntityCommandBuffer(Allocator.Temp);
            if (!RuntimeSpawnProjectionUtility.TryQueueRestoreAliveRefsCreatePhase(
                    EntityManager,
                    RuntimeContentDatabase.Active,
                    ref createEcb,
                    out var projection))
            {
                createEcb.Dispose();
                return;
            }

            createEcb.Playback(EntityManager);
            createEcb.Dispose();

            var materializeEcb = new EntityCommandBuffer(Allocator.Temp);
            RuntimeSpawnProjectionUtility.QueueRestoreAliveRefsMaterializePhase(
                EntityManager,
                ref materializeEcb,
                ref projection);
            materializeEcb.Playback(EntityManager);
            materializeEcb.Dispose();
            RuntimeSpawnProjectionUtility.ApplyRestoreAliveRefsProjection(EntityManager, projection);
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
