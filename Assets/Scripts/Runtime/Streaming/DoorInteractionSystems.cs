using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Profiling;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Player;

namespace VVardenfell.Runtime.Streaming
{
    static class DoorInteractableResolver
    {
        public static bool TryHydrate(EntityManager entityManager, Entity logicalEntity)
        {
            if (!entityManager.Exists(logicalEntity)
                || !entityManager.HasComponent<DoorAuthoring>(logicalEntity)
                || !entityManager.HasComponent<PlacedRefIdentity>(logicalEntity)
                || !entityManager.HasComponent<LogicalRefLocation>(logicalEntity))
            {
                return false;
            }

            uint placedRefId = entityManager.GetComponentData<PlacedRefIdentity>(logicalEntity).Value;
            var location = entityManager.GetComponentData<LogicalRefLocation>(logicalEntity);
            if (!TryBuild(location, placedRefId, out DoorInteractable interactable))
                return false;

            entityManager.AddComponentData(logicalEntity, interactable);
            Debug.Log($"[VVardenfell][Door] hydrated DoorInteractable for placedRef=0x{placedRefId:X8}.");
            return true;
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

    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class DoorRuntimeBootstrapSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (!SystemAPI.HasSingleton<PlayerInteractionFocus>())
            {
                var entity = EntityManager.CreateEntity();
                EntityManager.SetName(entity, "VVardenfell.DoorRuntime");
                EntityManager.AddComponentData(entity, new PlayerInteractionFocus
                {
                    TargetEntity = Entity.Null
                });
                EntityManager.AddComponentData(entity, new DoorActivationRequest
                {
                    TargetEntity = Entity.Null
                });
                EntityManager.AddComponentData(entity, new InteriorTransitionState());
                EntityManager.AddBuffer<InteriorSpawnedEntity>(entity);
            }

            Enabled = false;
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TransformSystemGroup))]
    public partial class PlayerInteractionFocusSystem : SystemBase
    {
        const float MaxInteractDistance = 2.25f;

        EntityQuery _viewQuery;
        EntityQuery _focusQuery;

        protected override void OnCreate()
        {
            _viewQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerViewComponent>(),
                ComponentType.ReadOnly<LocalToWorld>());
            _focusQuery = GetEntityQuery(ComponentType.ReadWrite<PlayerInteractionFocus>());
            RequireForUpdate(_viewQuery);
            RequireForUpdate(_focusQuery);
            RequireForUpdate<PhysicsWorldSingleton>();
            RequireForUpdate<LogicalRefLookup>();
        }

        protected override void OnUpdate()
        {
            EntityManager.CompleteDependencyBeforeRO<LocalToWorld>();

            var focusRef = _focusQuery.GetSingletonRW<PlayerInteractionFocus>();
            ref var focus = ref focusRef.ValueRW;
            focus.TargetEntity = Entity.Null;
            focus.PlacedRefId = 0u;
            focus.InteractKind = (byte)InteractableKind.None;
            focus.HitDistance = 0f;
            focus.HasTarget = 0;

            var viewTransform = _viewQuery.GetSingleton<LocalToWorld>().Value;
            float3 origin = viewTransform.c3.xyz;
            float3 forward = math.normalizesafe(viewTransform.c2.xyz, new float3(0f, 0f, 1f));
            var input = new RaycastInput
            {
                Start = origin,
                End = origin + forward * MaxInteractDistance,
                Filter = new CollisionFilter
                {
                    BelongsTo = ~0u,
                    CollidesWith = ~(1u << 1),
                    GroupIndex = 0
                }
            };

            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            if (!physicsWorld.CastRay(input, out Unity.Physics.RaycastHit hit))
                return;

            Entity hitEntity = hit.Entity;
            if (!TryResolveDoorFocus(hitEntity, out Entity targetEntity, out uint placedRefId))
            {
                return;
            }

            focus.TargetEntity = targetEntity;
            focus.PlacedRefId = placedRefId;
            focus.InteractKind = (byte)InteractableKind.Door;
            focus.HitDistance = hit.Fraction * MaxInteractDistance;
            focus.HasTarget = 1;
        }

        bool TryResolveDoorFocus(Entity hitEntity, out Entity targetEntity, out uint placedRefId)
        {
            targetEntity = Entity.Null;
            placedRefId = 0u;

            if (!EntityManager.Exists(hitEntity))
                return false;

            if (EntityManager.HasComponent<LogicalRefParent>(hitEntity))
            {
                hitEntity = EntityManager.GetComponentData<LogicalRefParent>(hitEntity).Value;
            }
            else if (EntityManager.HasComponent<PlacedRefIdentity>(hitEntity))
            {
                uint childPlacedRefId = EntityManager.GetComponentData<PlacedRefIdentity>(hitEntity).Value;
                if (childPlacedRefId != 0u && SystemAPI.HasSingleton<LogicalRefLookup>())
                {
                    var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
                    if (lookup.Map.IsCreated && lookup.Map.TryGetValue(childPlacedRefId, out Entity logicalEntity))
                        hitEntity = logicalEntity;
                }
            }

            if (!EntityManager.Exists(hitEntity)
                || !EntityManager.HasComponent<LogicalRefTag>(hitEntity)
                || !EntityManager.HasComponent<PlacedRefIdentity>(hitEntity))
            {
                return false;
            }

            if (!EntityManager.HasComponent<DoorInteractable>(hitEntity) && !DoorInteractableResolver.TryHydrate(EntityManager, hitEntity))
                return false;

            targetEntity = hitEntity;
            placedRefId = EntityManager.GetComponentData<PlacedRefIdentity>(hitEntity).Value;
            return placedRefId != 0u;
        }

    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerInteractionFocusSystem))]
    public partial class PlayerDoorActivationSystem : SystemBase
    {
        const float MaxInteractDistance = 2.25f;

        EntityQuery _playerQuery;
        EntityQuery _focusQuery;
        EntityQuery _requestQuery;
        EntityQuery _transitionQuery;
        EntityQuery _viewQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<PlayerCharacterControl>());
            _focusQuery = GetEntityQuery(ComponentType.ReadOnly<PlayerInteractionFocus>());
            _requestQuery = GetEntityQuery(ComponentType.ReadWrite<DoorActivationRequest>());
            _transitionQuery = GetEntityQuery(ComponentType.ReadOnly<InteriorTransitionState>());
            _viewQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerViewComponent>(),
                ComponentType.ReadOnly<LocalToWorld>());
            RequireForUpdate(_playerQuery);
            RequireForUpdate(_focusQuery);
            RequireForUpdate(_requestQuery);
            RequireForUpdate(_transitionQuery);
            RequireForUpdate(_viewQuery);
            RequireForUpdate<PhysicsWorldSingleton>();
            RequireForUpdate<LogicalRefLookup>();
        }

        protected override void OnUpdate()
        {
            var transition = _transitionQuery.GetSingleton<InteriorTransitionState>();
            if (transition.TransitionInProgress != 0)
                return;

            var control = _playerQuery.GetSingleton<PlayerCharacterControl>();
            if (!control.InteractPressed)
                return;

            Entity targetEntity = Entity.Null;
            uint placedRefId = 0u;

            var focus = _focusQuery.GetSingleton<PlayerInteractionFocus>();
            if (focus.HasTarget != 0 && focus.InteractKind == (byte)InteractableKind.Door)
            {
                targetEntity = focus.TargetEntity;
                placedRefId = focus.PlacedRefId;
            }
            else if (!TryResolveDoorFocusFromViewRay(out targetEntity, out placedRefId, out Entity hitEntity))
            {
                if (hitEntity != Entity.Null)
                {
                    Debug.Log(
                        $"[VVardenfell][Door] interact ray hit entity {hitEntity}, but it did not resolve to a logical door.");
                }
                else
                {
                    Debug.Log("[VVardenfell][Door] interact pressed but no door was hit within range.");
                }
                return;
            }

            var requestRef = _requestQuery.GetSingletonRW<DoorActivationRequest>();
            requestRef.ValueRW = new DoorActivationRequest
            {
                Pending = 1,
                TargetEntity = targetEntity
            };

            Debug.Log($"[VVardenfell][Door] queued activation for placedRef=0x{placedRefId:X8} entity={targetEntity}.");
        }

        bool TryResolveDoorFocusFromViewRay(out Entity targetEntity, out uint placedRefId, out Entity hitEntity)
        {
            targetEntity = Entity.Null;
            placedRefId = 0u;
            hitEntity = Entity.Null;

            EntityManager.CompleteDependencyBeforeRO<LocalToWorld>();

            var viewTransform = _viewQuery.GetSingleton<LocalToWorld>().Value;
            float3 origin = viewTransform.c3.xyz;
            float3 forward = math.normalizesafe(viewTransform.c2.xyz, new float3(0f, 0f, 1f));
            var input = new RaycastInput
            {
                Start = origin,
                End = origin + forward * MaxInteractDistance,
                Filter = new CollisionFilter
                {
                    BelongsTo = ~0u,
                    CollidesWith = ~(1u << 1),
                    GroupIndex = 0
                }
            };

            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            if (!physicsWorld.CastRay(input, out Unity.Physics.RaycastHit hit))
                return false;

            hitEntity = hit.Entity;
            return TryResolveDoorEntity(hit.Entity, out targetEntity, out placedRefId);
        }

        bool TryResolveDoorEntity(Entity hitEntity, out Entity targetEntity, out uint placedRefId)
        {
            targetEntity = Entity.Null;
            placedRefId = 0u;

            if (!EntityManager.Exists(hitEntity))
                return false;

            if (EntityManager.HasComponent<LogicalRefParent>(hitEntity))
            {
                hitEntity = EntityManager.GetComponentData<LogicalRefParent>(hitEntity).Value;
            }
            else if (EntityManager.HasComponent<PlacedRefIdentity>(hitEntity))
            {
                uint childPlacedRefId = EntityManager.GetComponentData<PlacedRefIdentity>(hitEntity).Value;
                if (childPlacedRefId != 0u)
                {
                    var lookup = SystemAPI.GetSingleton<LogicalRefLookup>();
                    if (lookup.Map.IsCreated && lookup.Map.TryGetValue(childPlacedRefId, out Entity logicalEntity))
                        hitEntity = logicalEntity;
                }
            }

            if (!EntityManager.Exists(hitEntity)
                || !EntityManager.HasComponent<LogicalRefTag>(hitEntity)
                || !EntityManager.HasComponent<PlacedRefIdentity>(hitEntity))
            {
                return false;
            }

            if (!EntityManager.HasComponent<DoorInteractable>(hitEntity) && !DoorInteractableResolver.TryHydrate(EntityManager, hitEntity))
                return false;

            targetEntity = hitEntity;
            placedRefId = EntityManager.GetComponentData<PlacedRefIdentity>(hitEntity).Value;
            return placedRefId != 0u;
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerDoorActivationSystem))]
    public partial class TeleportDoorTransitionSystem : SystemBase
    {
        static readonly float3 InteriorWorldOffset = new(0f, 0f, 200000f);
        static readonly ProfilerMarker k_Transition = new("VV.Streaming.TeleportDoorTransition");

        EntityQuery _requestQuery;
        EntityQuery _transitionQuery;
        EntityQuery _focusQuery;
        EntityQuery _streamingQuery;
        EntityQuery _playerQuery;
        EntityQuery _viewQuery;

        protected override void OnCreate()
        {
            _requestQuery = GetEntityQuery(ComponentType.ReadWrite<DoorActivationRequest>());
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
                ComponentType.ReadWrite<PlayerCharacterState>());
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
        }

        protected override void OnUpdate()
        {
            using var _ = k_Transition.Auto();

            var requestRef = _requestQuery.GetSingletonRW<DoorActivationRequest>();
            ref var request = ref requestRef.ValueRW;
            if (request.Pending == 0)
                return;

            CompleteDependency();

            var transitionEntity = _transitionQuery.GetSingletonEntity();
            var transitionRef = _transitionQuery.GetSingletonRW<InteriorTransitionState>();
            ref var transition = ref transitionRef.ValueRW;
            transition.TransitionInProgress = 1;

            Entity target = request.TargetEntity;
            request.Pending = 0;
            request.TargetEntity = Entity.Null;

            if (!EntityManager.Exists(target) || !EntityManager.HasComponent<DoorInteractable>(target))
            {
                Debug.LogWarning("[VVardenfell][Streaming] door activation request resolved to a missing or non-door logical entity.");
                ClearFocus();
                transition.TransitionInProgress = 0;
                return;
            }

            var door = EntityManager.GetComponentData<DoorInteractable>(target);
            if (door.IsTeleport == 0)
            {
                Debug.Log("[VVardenfell][Streaming] non-teleport door activated; transition deferred for this slice.");
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

            Debug.Log(
                goesToInterior
                    ? $"[VVardenfell][Streaming] entering interior '{door.DestinationCellId}' via teleport door."
                    : $"[VVardenfell][Streaming] exiting active interior to exterior destination ({door.DestinationPosition.x:F2}, {door.DestinationPosition.y:F2}, {door.DestinationPosition.z:F2}).");

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

            ClearFocus();
            transition.TransitionInProgress = 0;

            Debug.Log(
                goesToInterior
                    ? $"[VVardenfell][Streaming] interior transition complete: '{door.DestinationCellId}'."
                    : $"[VVardenfell][Streaming] exterior transition complete: camera cell=({configRef.CameraCell.x},{configRef.CameraCell.y}).");
        }

        void MovePlayerToDestination(float3 destinationPosition, quaternion bodyYawRotation)
        {
            Entity playerEntity = _playerQuery.GetSingletonEntity();
            var character = EntityManager.GetComponentData<PlayerCharacterComponent>(playerEntity);
            var control = EntityManager.GetComponentData<PlayerCharacterControl>(playerEntity);
            var state = EntityManager.GetComponentData<PlayerCharacterState>(playerEntity);
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
            EntityManager.SetComponentData(playerEntity, control);

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

            for (int i = 0; i < entitiesToDestroy.Length; i++)
            {
                if (EntityManager.Exists(entitiesToDestroy[i])
                    && EntityManager.HasComponent<LogicalRefTag>(entitiesToDestroy[i])
                    && EntityManager.HasComponent<PlacedRefIdentity>(entitiesToDestroy[i]))
                {
                    uint placedRefId = EntityManager.GetComponentData<PlacedRefIdentity>(entitiesToDestroy[i]).Value;
                    if (logicalRefLookup.Map.IsCreated && placedRefId != 0u)
                        logicalRefLookup.Map.Remove(placedRefId);
                }

                if (EntityManager.Exists(entitiesToDestroy[i]))
                    EntityManager.DestroyEntity(entitiesToDestroy[i]);
            }

            EntityManager.GetBuffer<InteriorSpawnedEntity>(transitionEntity).Clear();
        }

        void ClearFocus()
        {
            var focusRef = _focusQuery.GetSingletonRW<PlayerInteractionFocus>();
            focusRef.ValueRW = new PlayerInteractionFocus
            {
                TargetEntity = Entity.Null
            };
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
