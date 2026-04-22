using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Player;

namespace VVardenfell.Runtime.Streaming
{
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
            if (!EntityManager.Exists(hitEntity)
                || !EntityManager.HasComponent<DoorInteractable>(hitEntity)
                || !EntityManager.HasComponent<PlacedRefIdentity>(hitEntity))
            {
                return;
            }

            focus.TargetEntity = hitEntity;
            focus.PlacedRefId = EntityManager.GetComponentData<PlacedRefIdentity>(hitEntity).Value;
            focus.InteractKind = (byte)InteractableKind.Door;
            focus.HitDistance = hit.Fraction * MaxInteractDistance;
            focus.HasTarget = 1;
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerInteractionFocusSystem))]
    public partial class PlayerDoorActivationSystem : SystemBase
    {
        EntityQuery _playerQuery;
        EntityQuery _focusQuery;
        EntityQuery _requestQuery;
        EntityQuery _transitionQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<PlayerCharacterControl>());
            _focusQuery = GetEntityQuery(ComponentType.ReadOnly<PlayerInteractionFocus>());
            _requestQuery = GetEntityQuery(ComponentType.ReadWrite<DoorActivationRequest>());
            _transitionQuery = GetEntityQuery(ComponentType.ReadOnly<InteriorTransitionState>());
            RequireForUpdate(_playerQuery);
            RequireForUpdate(_focusQuery);
            RequireForUpdate(_requestQuery);
            RequireForUpdate(_transitionQuery);
        }

        protected override void OnUpdate()
        {
            var transition = _transitionQuery.GetSingleton<InteriorTransitionState>();
            if (transition.TransitionInProgress != 0)
                return;

            var control = _playerQuery.GetSingleton<PlayerCharacterControl>();
            if (!control.InteractPressed)
                return;

            var focus = _focusQuery.GetSingleton<PlayerInteractionFocus>();
            if (focus.HasTarget == 0 || focus.InteractKind != (byte)InteractableKind.Door)
                return;

            var requestRef = _requestQuery.GetSingletonRW<DoorActivationRequest>();
            requestRef.ValueRW = new DoorActivationRequest
            {
                Pending = 1,
                TargetEntity = focus.TargetEntity
            };
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerDoorActivationSystem))]
    public partial class TeleportDoorTransitionSystem : SystemBase
    {
        static readonly float3 InteriorWorldOffset = new(0f, 0f, 200000f);

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
            var requestRef = _requestQuery.GetSingletonRW<DoorActivationRequest>();
            ref var request = ref requestRef.ValueRW;
            if (request.Pending == 0)
                return;

            CompleteDependency();

            var transitionEntity = _transitionQuery.GetSingletonEntity();
            var transitionRef = _transitionQuery.GetSingletonRW<InteriorTransitionState>();
            var spawnedBuffer = EntityManager.GetBuffer<InteriorSpawnedEntity>(transitionEntity);
            ref var transition = ref transitionRef.ValueRW;
            transition.TransitionInProgress = 1;

            Entity target = request.TargetEntity;
            request.Pending = 0;
            request.TargetEntity = Entity.Null;

            if (!EntityManager.Exists(target) || !EntityManager.HasComponent<DoorInteractable>(target))
            {
                ClearFocus();
                transition.TransitionInProgress = 0;
                return;
            }

            var door = EntityManager.GetComponentData<DoorInteractable>(target);
            if (door.IsTeleport == 0)
            {
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
                    Debug.LogWarning($"[VVardenfell] teleport destination interior '{destinationCellId}' was not preloaded.");
                    transition.TransitionInProgress = 0;
                    ClearFocus();
                    return;
                }
            }

            var streamingEntity = _streamingQuery.GetSingletonEntity();
            var configRef = EntityManager.GetComponentData<StreamingConfig>(streamingEntity);
            var available = EntityManager.GetComponentData<AvailableCells>(streamingEntity);
            var loaded = EntityManager.GetComponentData<LoadedCellsMap>(streamingEntity);

            if (goesToInterior)
            {
                DestroyInteriorEntities(spawnedBuffer);
                WorldSpawner.HideExteriorVisibility(World, ref loaded);
                WorldSpawner.SpawnInteriorCell(World, destinationInterior, InteriorWorldOffset, spawnedBuffer);
                configRef.ExteriorStreamingPaused = true;
                transition.InteriorActive = 1;
                transition.ActiveInteriorCellId = door.DestinationCellId;
            }
            else
            {
                DestroyInteriorEntities(spawnedBuffer);
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
            EntityManager.SetComponentData(streamingEntity, loaded);

            ClearFocus();
            transition.TransitionInProgress = 0;
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

        void DestroyInteriorEntities(DynamicBuffer<InteriorSpawnedEntity> spawnedBuffer)
        {
            for (int i = 0; i < spawnedBuffer.Length; i++)
            {
                Entity entity = spawnedBuffer[i].Value;
                if (EntityManager.Exists(entity))
                    EntityManager.DestroyEntity(entity);
            }

            spawnedBuffer.Clear();
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
