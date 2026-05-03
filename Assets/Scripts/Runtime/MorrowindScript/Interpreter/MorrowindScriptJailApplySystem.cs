using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.WorldRefs;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindScriptShellApplySystem))]
    public partial class MorrowindScriptJailApplySystem : SystemBase
    {
        static readonly float3 InteriorWorldOffset = float3.zero;

        EntityQuery _runtimeQuery;
        EntityQuery _transitionQuery;
        EntityQuery _streamingQuery;
        EntityQuery _playerQuery;
        EntityQuery _viewQuery;

        protected override void OnCreate()
        {
            _runtimeQuery = GetEntityQuery(
                ComponentType.ReadOnly<MorrowindScriptRuntimeState>(),
                ComponentType.ReadWrite<MorrowindScriptJailRequest>());
            _transitionQuery = GetEntityQuery(
                ComponentType.ReadWrite<InteriorTransitionState>(),
                ComponentType.ReadWrite<InteriorSpawnedEntity>());
            _streamingQuery = GetEntityQuery(
                ComponentType.ReadWrite<StreamingConfig>(),
                ComponentType.ReadWrite<LogicalRefLookup>(),
                ComponentType.ReadOnly<AvailableCells>(),
                ComponentType.ReadWrite<LoadedCellsMap>());
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
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

            RequireForUpdate(_runtimeQuery);
            RequireForUpdate(_transitionQuery);
            RequireForUpdate(_streamingQuery);
            RequireForUpdate(_playerQuery);
            RequireForUpdate(_viewQuery);
            RequireForUpdate<MorrowindTimeAdvanceRequest>();
        }

        protected override void OnUpdate()
        {
            Entity runtimeEntity = _runtimeQuery.GetSingletonEntity();
            var requests = EntityManager.GetBuffer<MorrowindScriptJailRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;
            if (requests.Length > 1)
                throw new InvalidOperationException("[VVardenfell][MWScript] multiple GotoJail requests were queued in one frame.");

            var contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
                throw new InvalidOperationException("[VVardenfell][MWScript] GotoJail requested before runtime content database was ready.");

            if (!contentDb.TryResolvePlaceable("prisonmarker", out var prisonMarkerContent)
                || prisonMarkerContent.Kind != ContentReferenceKind.Door
                || !contentDb.IsValid(prisonMarkerContent))
            {
                throw new InvalidOperationException("[VVardenfell][MWScript] GotoJail requires a valid prisonmarker door content record.");
            }

            var playerEntity = _playerQuery.GetSingletonEntity();
            var playerTransform = EntityManager.GetComponentData<LocalTransform>(playerEntity);
            var transitionEntity = _transitionQuery.GetSingletonEntity();
            var transition = EntityManager.GetComponentData<InteriorTransitionState>(transitionEntity);

            if (!TryResolveClosestPrisonMarker(prisonMarkerContent, playerTransform.Position, transition, out var destination))
                throw new InvalidOperationException("[VVardenfell][MWScript] GotoJail could not find a prisonmarker destination.");

            int jailDays = math.max(1, requests[0].Days);
            ApplyJailTeleport(transitionEntity, ref transition, destination);
            MovePlayerToDestination(playerEntity, destination.Position, ExtractYawRotation(destination.Rotation));
            SheathePlayerWeapon(playerEntity);
            SystemAPI.GetSingletonBuffer<MorrowindTimeAdvanceRequest>().Add(new MorrowindTimeAdvanceRequest
            {
                Hours = jailDays * 24f,
                Kind = (byte)MorrowindTimeAdvanceKind.Rest,
            });

            if (SystemAPI.TryGetSingletonRW<RuntimeShellState>(out var shell))
            {
                RuntimeShellStateUtility.CloseDialogue(ref shell.ValueRW);
                RuntimeShellStateUtility.SyncGameplayGateAndCursor(ref shell.ValueRW);
            }

            EntityManager.SetComponentData(transitionEntity, transition);
            if (destination.IsInterior)
                RuntimeSpawnProjectionUtility.TryRestoreAliveRefsForCurrentWorld(EntityManager, contentDb);

            requests.Clear();
        }

        bool TryResolveClosestPrisonMarker(
            ContentReference prisonMarkerContent,
            float3 playerPosition,
            InteriorTransitionState transition,
            out PrisonMarkerDestination destination)
        {
            if (transition.InteriorActive != 0)
            {
                if (TryResolveInteriorPrisonMarker(prisonMarkerContent, transition.ActiveInteriorCellHash, out destination))
                    return true;
            }
            else if (TryResolveExteriorPrisonMarker(prisonMarkerContent, playerPosition, out destination))
            {
                return true;
            }

            destination = default;
            return false;
        }

        bool TryResolveInteriorPrisonMarker(
            ContentReference prisonMarkerContent,
            ulong activeInteriorCellHash,
            out PrisonMarkerDestination destination)
        {
            destination = default;
            if (activeInteriorCellHash == 0UL || !WorldResources.TryGetInteriorCell(activeInteriorCellHash, out var startCell))
                return false;

            var visited = new HashSet<ulong>();
            var queue = new Queue<CellData>();
            queue.Enqueue(startCell);
            visited.Add(activeInteriorCellHash);

            while (queue.Count > 0)
            {
                CellData cell = queue.Dequeue();
                if (TryFindMarkerInCell(prisonMarkerContent, cell, out destination))
                    return true;

                if (cell.Doors == null)
                    continue;

                for (int i = 0; i < cell.Doors.Length; i++)
                {
                    var door = cell.Doors[i];
                    if ((door.Flags & DoorRefEntry.FlagTeleport) == 0)
                        continue;

                    if (string.IsNullOrWhiteSpace(door.DestinationCellId))
                    {
                        if (TryResolveExteriorPrisonMarker(
                                prisonMarkerContent,
                                new float3(door.DestPosX, door.DestPosY, door.DestPosZ),
                                out destination))
                        {
                            return true;
                        }

                        continue;
                    }

                    ulong destinationHash = InteriorCellIdHash.Hash(door.DestinationCellId);
                    if (destinationHash == 0UL || visited.Contains(destinationHash))
                        continue;

                    if (!WorldResources.TryGetInteriorCell(destinationHash, out var nextCell))
                        throw new InvalidOperationException($"[VVardenfell][MWScript] GotoJail interior search reached missing cell '{door.DestinationCellId}'.");

                    visited.Add(destinationHash);
                    queue.Enqueue(nextCell);
                }
            }

            return false;
        }

        static bool TryResolveExteriorPrisonMarker(
            ContentReference prisonMarkerContent,
            float3 playerPosition,
            out PrisonMarkerDestination destination)
        {
            destination = default;
            bool found = false;
            float bestDistanceSq = 0f;

            foreach (var kv in WorldResources.Cells)
            {
                CellData cell = kv.Value;
                if (cell?.Refs == null)
                    continue;

                for (int i = 0; i < cell.Refs.Length; i++)
                {
                    var entry = cell.Refs[i];
                    if (!MatchesContent(entry, prisonMarkerContent))
                        continue;

                    float3 position = new(entry.PosX, entry.PosY, entry.PosZ);
                    float distanceSq = math.lengthsq(position.xz - playerPosition.xz);
                    if (found && distanceSq >= bestDistanceSq)
                        continue;

                    found = true;
                    bestDistanceSq = distanceSq;
                    destination = BuildExteriorDestination(cell, entry);
                }
            }

            return found;
        }

        static bool TryFindMarkerInCell(
            ContentReference prisonMarkerContent,
            CellData cell,
            out PrisonMarkerDestination destination)
        {
            destination = default;
            if (cell?.Refs == null)
                return false;

            for (int i = 0; i < cell.Refs.Length; i++)
            {
                var entry = cell.Refs[i];
                if (!MatchesContent(entry, prisonMarkerContent))
                    continue;

                destination = cell.IsInterior
                    ? BuildInteriorDestination(cell, entry)
                    : BuildExteriorDestination(cell, entry);
                return true;
            }

            return false;
        }

        static bool MatchesContent(in RefEntry entry, ContentReference content)
            => entry.ContentKind == (int)content.Kind
               && entry.ContentHandleValue == content.HandleValue;

        static PrisonMarkerDestination BuildInteriorDestination(CellData cell, in RefEntry entry)
        {
            FixedString128Bytes cellId = RuntimeFixedStringUtility.ToFixed128OrDefault(cell.CellId);
            if (cellId.IsEmpty)
                throw new InvalidOperationException("[VVardenfell][MWScript] GotoJail prisonmarker interior cell has no id.");

            return new PrisonMarkerDestination
            {
                IsInterior = true,
                InteriorCellId = cellId,
                InteriorCellHash = InteriorCellIdHash.Hash(cellId),
                InteriorCell = cell,
                Position = new float3(entry.PosX, entry.PosY, entry.PosZ) + InteriorWorldOffset,
                Rotation = new quaternion(entry.RotX, entry.RotY, entry.RotZ, entry.RotW),
            };
        }

        static PrisonMarkerDestination BuildExteriorDestination(CellData cell, in RefEntry entry)
        {
            return new PrisonMarkerDestination
            {
                IsInterior = false,
                ExteriorCell = new int2(cell.GridX, cell.GridY),
                Position = new float3(entry.PosX, entry.PosY, entry.PosZ),
                Rotation = new quaternion(entry.RotX, entry.RotY, entry.RotZ, entry.RotW),
            };
        }

        void ApplyJailTeleport(
            Entity transitionEntity,
            ref InteriorTransitionState transition,
            in PrisonMarkerDestination destination)
        {
            var streamingEntity = _streamingQuery.GetSingletonEntity();
            var config = EntityManager.GetComponentData<StreamingConfig>(streamingEntity);
            var logicalRefLookup = EntityManager.GetComponentData<LogicalRefLookup>(streamingEntity);
            var available = EntityManager.GetComponentData<AvailableCells>(streamingEntity);
            var loaded = EntityManager.GetComponentData<LoadedCellsMap>(streamingEntity);

            transition.TransitionInProgress = 1;
            if (destination.IsInterior)
            {
                DestroyInteriorEntities(transitionEntity, ref logicalRefLookup);
                WorldSpawner.HideExteriorVisibility(World, ref loaded);
                WorldSpawner.SpawnInteriorCell(World, destination.InteriorCell, InteriorWorldOffset, transitionEntity, ref logicalRefLookup);
                config.ExteriorStreamingPaused = true;
                transition.InteriorActive = 1;
                transition.ActiveInteriorCellId = destination.InteriorCellId;
                transition.ActiveInteriorCellHash = destination.InteriorCellHash;
            }
            else
            {
                DestroyInteriorEntities(transitionEntity, ref logicalRefLookup);
                config.ExteriorStreamingPaused = false;
                transition.InteriorActive = 0;
                transition.ActiveInteriorCellId = default;
                transition.ActiveInteriorCellHash = 0UL;
                config.CameraCell = destination.ExteriorCell;
                WorldSpawner.SyncExteriorVisibility(World, config, available, ref loaded);
            }

            EntityManager.SetComponentData(streamingEntity, config);
            EntityManager.SetComponentData(streamingEntity, logicalRefLookup);
            EntityManager.SetComponentData(streamingEntity, loaded);
            transition.TransitionInProgress = 0;
        }

        void MovePlayerToDestination(Entity playerEntity, float3 destinationPosition, quaternion bodyYawRotation)
        {
            var character = EntityManager.GetComponentData<PlayerCharacterComponent>(playerEntity);
            var control = EntityManager.GetComponentData<PlayerCharacterControl>(playerEntity);
            var state = EntityManager.GetComponentData<PlayerCharacterState>(playerEntity);
            var movementState = EntityManager.GetComponentData<MorrowindMovementState>(playerEntity);
            var playerTransform = EntityManager.GetComponentData<LocalTransform>(playerEntity);

            playerTransform.Position = destinationPosition;
            playerTransform.Rotation = bodyYawRotation;
            EntityManager.SetComponentData(playerEntity, playerTransform);
            EntityManager.SetComponentData(playerEntity, new LocalToWorld
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
            EntityManager.SetComponentData(playerEntity, control);

            EntityManager.SetComponentData(playerEntity, default(MorrowindMovementInput));

            movementState.Inertia = float3.zero;
            movementState.LastVelocity = float3.zero;
            movementState.LocalMove = float2.zero;
            movementState.SpeedFactor = 0f;
            movementState.Flags = 0;
            movementState.Grounded = true;
            movementState.SupportKind = (byte)MorrowindSupportKind.FlatGround;
            movementState.StandingOn = Entity.Null;
            movementState.GroundNormal = math.up();
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
                Value = float4x4.TRS(destinationPosition + math.rotate(bodyYawRotation, view.LocalEyeOffset), bodyYawRotation, new float3(1f)),
            });
        }

        void SheathePlayerWeapon(Entity playerEntity)
        {
            using var visualQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<LocalPlayerVisual>(),
                ComponentType.ReadWrite<ActorWeaponAnimationState>());
            using var entities = visualQuery.ToEntityArray(Allocator.Temp);
            using var visuals = visualQuery.ToComponentDataArray<LocalPlayerVisual>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (visuals[i].Player != playerEntity)
                    continue;

                var weaponState = EntityManager.GetComponentData<ActorWeaponAnimationState>(entities[i]);
                weaponState.Drawn = 0;
                weaponState.Phase = ActorWeaponAnimationPhase.Hidden;
                weaponState.AttackStrength = 0f;
                weaponState.ReadyWeaponTogglePressed = 0;
                weaponState.AttackHeld = 0;
                weaponState.AttackPressed = 0;
                weaponState.AttackReleased = 0;
                weaponState.ReleaseQueued = 0;
                EntityManager.SetComponentData(entities[i], weaponState);
            }
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
            public CellData InteriorCell;
            public float3 Position;
            public quaternion Rotation;
        }
    }
}
