using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindGameplayMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptShellApplySystem))]
    public partial struct MorrowindScriptJailApplySystem : ISystem
    {
        static readonly float3 InteriorWorldOffset = float3.zero;

        EntityQuery _runtimeQuery;
        EntityQuery _transitionQuery;
        EntityQuery _streamingQuery;
        EntityQuery _playerQuery;
        EntityQuery _viewQuery;
        EntityQuery _playerVisualQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _runtimeQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<MorrowindScriptRuntimeState>(),
                ComponentType.ReadWrite<MorrowindScriptJailRequest>());
            _transitionQuery = systemState.GetEntityQuery(
                ComponentType.ReadWrite<InteriorTransitionState>(),
                ComponentType.ReadWrite<InteriorSpawnedEntity>());
            _streamingQuery = systemState.GetEntityQuery(
                ComponentType.ReadWrite<StreamingConfig>(),
                ComponentType.ReadWrite<LogicalRefLookup>(),
                ComponentType.ReadOnly<AvailableCells>(),
                ComponentType.ReadWrite<LoadedCellsMap>());
            _playerQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<LocalTransform>(),
                ComponentType.ReadWrite<LocalToWorld>(),
                ComponentType.ReadWrite<PlayerCharacterComponent>(),
                ComponentType.ReadWrite<PlayerCharacterControl>(),
                ComponentType.ReadWrite<PlayerCharacterState>(),
                ComponentType.ReadWrite<MorrowindMovementInput>(),
                ComponentType.ReadWrite<MorrowindMovementState>());
            _viewQuery = systemState.GetEntityQuery(
                ComponentType.ReadWrite<PlayerViewComponent>(),
                ComponentType.ReadWrite<LocalTransform>(),
                ComponentType.ReadWrite<LocalToWorld>());
            _playerVisualQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<LocalPlayerVisual>(),
                ComponentType.ReadWrite<ActorWeaponAnimationState>());

            systemState.RequireForUpdate(_runtimeQuery);
            systemState.RequireForUpdate(_transitionQuery);
            systemState.RequireForUpdate(_streamingQuery);
            systemState.RequireForUpdate(_playerQuery);
            systemState.RequireForUpdate(_viewQuery);
            systemState.RequireForUpdate<MorrowindTimeAdvanceRequest>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate<RuntimeWorldCellBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity runtimeEntity = _runtimeQuery.GetSingletonEntity();
            var requests = systemState.EntityManager.GetBuffer<MorrowindScriptJailRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;
            if (requests.Length > 1)
                throw new InvalidOperationException("[VVardenfell][MWScript] multiple GotoJail requests were queued in one frame.");

            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][ContentBlob] GotoJail requested before runtime content blob was ready.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;
            var worldCellReference = SystemAPI.GetSingleton<RuntimeWorldCellBlobReference>();
            if (!worldCellReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][WorldCellBlob] GotoJail requested before runtime world cell blob was ready.");
            ref RuntimeWorldCellBlob worldCells = ref worldCellReference.Blob.Value;

            if (!RuntimeContentBlobUtility.TryResolvePlaceableByIdHash(ref content, RuntimeContentKnownHashes.prisonmarker, out var prisonMarkerContent)
                || prisonMarkerContent.Kind != ContentReferenceKind.Door
                || !RuntimeContentBlobUtility.IsValid(ref content, prisonMarkerContent))
            {
                throw new InvalidOperationException("[VVardenfell][MWScript] GotoJail requires a valid prisonmarker door content record.");
            }

            var playerEntity = _playerQuery.GetSingletonEntity();
            var playerTransform = systemState.EntityManager.GetComponentData<LocalTransform>(playerEntity);
            var transitionEntity = _transitionQuery.GetSingletonEntity();
            var transition = systemState.EntityManager.GetComponentData<InteriorTransitionState>(transitionEntity);

            if (!TryResolveClosestPrisonMarker(ref worldCells, prisonMarkerContent, playerTransform.Position, transition, out var destination))
                throw new InvalidOperationException("[VVardenfell][MWScript] GotoJail could not find a prisonmarker destination.");

            int jailDays = math.max(1, requests[0].Days);
            ApplyJailTeleport(ref systemState, transitionEntity, ref transition, destination);
            MovePlayerToDestination(ref systemState, playerEntity, destination.Position, ExtractYawRotation(destination.Rotation));
            SheathePlayerWeapon(ref systemState, playerEntity);
            SystemAPI.GetSingletonBuffer<MorrowindTimeAdvanceRequest>().Add(new MorrowindTimeAdvanceRequest
            {
                Hours = jailDays * 24f,
                Kind = (byte)MorrowindTimeAdvanceKind.Rest,
            });

            if (SystemAPI.TryGetSingletonRW<RuntimeShellState>(out var shell))
                RuntimeShellStateUtility.CloseDialogue(ref shell.ValueRW);

            systemState.EntityManager.SetComponentData(transitionEntity, transition);
            if (destination.IsInterior)
                RuntimeSpawnProjectionUtility.TryRestoreAliveRefsForCurrentWorld(systemState.EntityManager);

            requests.Clear();
        }

        bool TryResolveClosestPrisonMarker(
            ref RuntimeWorldCellBlob worldCells,
            ContentReference prisonMarkerContent,
            float3 playerPosition,
            InteriorTransitionState transition,
            out PrisonMarkerDestination destination)
        {
            if (transition.InteriorActive != 0)
            {
                if (TryResolveInteriorPrisonMarker(ref worldCells, prisonMarkerContent, transition.ActiveInteriorCellHash, out destination))
                    return true;
            }
            else if (TryResolveExteriorPrisonMarker(ref worldCells, prisonMarkerContent, playerPosition, out destination))
            {
                return true;
            }

            destination = default;
            return false;
        }

        bool TryResolveInteriorPrisonMarker(
            ref RuntimeWorldCellBlob worldCells,
            ContentReference prisonMarkerContent,
            ulong activeInteriorCellHash,
            out PrisonMarkerDestination destination)
        {
            destination = default;
            if (activeInteriorCellHash == 0UL || !RuntimeWorldCellBlobUtility.TryGetInteriorCellIndex(ref worldCells, activeInteriorCellHash, out _))
                return false;

            using var visited = new NativeHashSet<ulong>(16, Allocator.Temp);
            using var queue = new NativeList<ulong>(16, Allocator.Temp);
            queue.Add(activeInteriorCellHash);
            visited.Add(activeInteriorCellHash);

            for (int cursor = 0; cursor < queue.Length; cursor++)
            {
                ulong cellHash = queue[cursor];
                if (!RuntimeWorldCellBlobUtility.TryGetInteriorCellIndex(ref worldCells, cellHash, out int cellIndex))
                    throw new InvalidOperationException($"[VVardenfell][MWScript] GotoJail interior search reached missing cell hash 0x{cellHash:X16}.");

                ref RuntimeWorldCellDefBlob cell = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, cellIndex);
                if (TryFindMarkerInCell(ref worldCells, ref cell, prisonMarkerContent, out destination))
                    return true;

                ref BlobArray<RuntimeWorldDoorRefDefBlob> doors = ref RuntimeWorldCellBlobUtility.GetDoors(ref worldCells, ref cell, out int firstDoor, out int doorCount);
                for (int i = 0; i < doorCount; i++)
                {
                    ref readonly var door = ref doors[firstDoor + i];
                    if ((door.Flags & DoorRefEntry.FlagTeleport) == 0)
                        continue;

                    if (door.DestinationCellHash == 0UL)
                    {
                        if (TryResolveExteriorPrisonMarker(
                                ref worldCells,
                                prisonMarkerContent,
                                new float3(door.DestPosX, door.DestPosY, door.DestPosZ),
                                out destination))
                        {
                            return true;
                        }

                        continue;
                    }

                    ulong destinationHash = door.DestinationCellHash;
                    if (destinationHash == 0UL || visited.Contains(destinationHash))
                        continue;

                    if (!RuntimeWorldCellBlobUtility.TryGetInteriorCellIndex(ref worldCells, destinationHash, out _))
                        throw new InvalidOperationException($"[VVardenfell][MWScript] GotoJail interior search reached missing cell '{door.DestinationCellId}'.");

                    visited.Add(destinationHash);
                    queue.Add(destinationHash);
                }
            }

            return false;
        }

        static bool TryResolveExteriorPrisonMarker(
            ref RuntimeWorldCellBlob worldCells,
            ContentReference prisonMarkerContent,
            float3 playerPosition,
            out PrisonMarkerDestination destination)
        {
            destination = default;
            if (!RuntimeWorldCellBlobUtility.TryFindNearestExteriorRefWithContent(
                    ref worldCells,
                    prisonMarkerContent,
                    playerPosition,
                    out int cellIndex,
                    out var entry))
            {
                return false;
            }

            ref RuntimeWorldCellDefBlob cell = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, cellIndex);
            destination = BuildExteriorDestination(ref cell, entry);
            return true;
        }

        static bool TryFindMarkerInCell(
            ref RuntimeWorldCellBlob worldCells,
            ref RuntimeWorldCellDefBlob cell,
            ContentReference prisonMarkerContent,
            out PrisonMarkerDestination destination)
        {
            destination = default;
            if (!RuntimeWorldCellBlobUtility.TryFindFirstRefWithContent(ref worldCells, ref cell, prisonMarkerContent, out var entry))
                return false;

            destination = cell.IsInterior != 0
                ? BuildInteriorDestination(ref cell, entry)
                : BuildExteriorDestination(ref cell, entry);
            return true;
        }

        static PrisonMarkerDestination BuildInteriorDestination(ref RuntimeWorldCellDefBlob cell, in RefEntry entry)
        {
            FixedString128Bytes cellId = cell.InteriorCellId;
            if (cellId.IsEmpty)
                throw new InvalidOperationException("[VVardenfell][MWScript] GotoJail prisonmarker interior cell has no id.");

            return new PrisonMarkerDestination
            {
                IsInterior = true,
                InteriorCellId = cellId,
                InteriorCellHash = cell.InteriorCellHash,
                Position = new float3(entry.PosX, entry.PosY, entry.PosZ) + InteriorWorldOffset,
                Rotation = new quaternion(entry.RotX, entry.RotY, entry.RotZ, entry.RotW),
            };
        }

        static PrisonMarkerDestination BuildExteriorDestination(ref RuntimeWorldCellDefBlob cell, in RefEntry entry)
        {
            return new PrisonMarkerDestination
            {
                IsInterior = false,
                ExteriorCell = cell.ExteriorCoord,
                Position = new float3(entry.PosX, entry.PosY, entry.PosZ),
                Rotation = new quaternion(entry.RotX, entry.RotY, entry.RotZ, entry.RotW),
            };
        }

        void ApplyJailTeleport(ref SystemState systemState, 
            Entity transitionEntity,
            ref InteriorTransitionState transition,
            in PrisonMarkerDestination destination)
        {
            var streamingEntity = _streamingQuery.GetSingletonEntity();
            var config = systemState.EntityManager.GetComponentData<StreamingConfig>(streamingEntity);
            var logicalRefLookup = systemState.EntityManager.GetComponentData<LogicalRefLookup>(streamingEntity);
            var available = systemState.EntityManager.GetComponentData<AvailableCells>(streamingEntity);
            var loaded = systemState.EntityManager.GetComponentData<LoadedCellsMap>(streamingEntity);

            transition.TransitionInProgress = 1;
            if (destination.IsInterior)
            {
                DestroyInteriorEntities(ref systemState, transitionEntity, ref logicalRefLookup);
                WorldSpawner.HideExteriorVisibility(systemState.World, ref loaded);
                if (!WorldSpawner.TrySpawnInteriorCellByHash(systemState.World, destination.InteriorCellHash, InteriorWorldOffset, transitionEntity, ref logicalRefLookup, out FixedString128Bytes spawnedInteriorCellId))
                    throw new InvalidOperationException($"[VVardenfell][MWScript] GotoJail destination interior '{destination.InteriorCellId}' was not preloaded.");
                config.ExteriorStreamingPaused = true;
                transition.InteriorActive = 1;
                transition.ActiveInteriorCellId = spawnedInteriorCellId.IsEmpty ? destination.InteriorCellId : spawnedInteriorCellId;
                transition.ActiveInteriorCellHash = destination.InteriorCellHash;
            }
            else
            {
                DestroyInteriorEntities(ref systemState, transitionEntity, ref logicalRefLookup);
                config.ExteriorStreamingPaused = false;
                transition.InteriorActive = 0;
                transition.ActiveInteriorCellId = default;
                transition.ActiveInteriorCellHash = 0UL;
                config.CameraCell = destination.ExteriorCell;
                WorldSpawner.SyncExteriorVisibility(systemState.World, config, available, ref loaded);
            }

            systemState.EntityManager.SetComponentData(streamingEntity, config);
            systemState.EntityManager.SetComponentData(streamingEntity, logicalRefLookup);
            systemState.EntityManager.SetComponentData(streamingEntity, loaded);
            transition.TransitionInProgress = 0;
        }

        void MovePlayerToDestination(ref SystemState systemState, Entity playerEntity, float3 destinationPosition, quaternion bodyYawRotation)
        {
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

            Entity viewEntity = _viewQuery.GetSingletonEntity();
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

        void SheathePlayerWeapon(ref SystemState systemState, Entity playerEntity)
        {
            using var entities = _playerVisualQuery.ToEntityArray(Allocator.Temp);
            using var visuals = _playerVisualQuery.ToComponentDataArray<LocalPlayerVisual>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (visuals[i].Player != playerEntity)
                    continue;

                var weaponState = systemState.EntityManager.GetComponentData<ActorWeaponAnimationState>(entities[i]);
                weaponState.Drawn = 0;
                weaponState.Phase = ActorWeaponAnimationPhase.Hidden;
                weaponState.AttackStrength = 0f;
                weaponState.ReadyWeaponTogglePressed = 0;
                weaponState.AttackHeld = 0;
                weaponState.AttackPressed = 0;
                weaponState.AttackReleased = 0;
                weaponState.ReleaseQueued = 0;
                systemState.EntityManager.SetComponentData(entities[i], weaponState);
            }
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
            spawnedBuffer.Clear();
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

        struct PrisonMarkerDestination
        {
            public bool IsInterior;
            public int2 ExteriorCell;
            public FixedString128Bytes InteriorCellId;
            public ulong InteriorCellHash;
            public float3 Position;
            public quaternion Rotation;
        }
    }
}
