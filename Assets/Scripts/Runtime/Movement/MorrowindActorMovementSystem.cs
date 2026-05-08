using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Pathfinding;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Movement
{
    [UpdateInGroup(typeof(MorrowindPhysicsQuerySystemGroup))]
    [UpdateAfter(typeof(PathGridTraversalSteeringSystem))]
    [BurstCompile]
    public partial struct MorrowindActorMovementSystem : ISystem
    {
        EntityQuery _playerQuery;
        EntityQuery _viewQuery;
        EntityQuery _nonPlayerQuery;
        EntityQuery _mutationQueueQuery;

        ComponentTypeHandle<LocalTransform> _transformHandle;
        ComponentTypeHandle<LocalToWorld> _localToWorldHandle;
        ComponentTypeHandle<MorrowindMovementInput> _inputHandle;
        ComponentTypeHandle<MorrowindMovementState> _movementStateHandle;
        ComponentTypeHandle<PhysicsCollider> _colliderHandle;
        ComponentTypeHandle<MorrowindMovementSpeed> _speedHandle;

        ComponentLookup<LocalTransform> _transformLookup;
        ComponentLookup<LocalToWorld> _localToWorldLookup;
        ComponentLookup<PhysicsCollider> _colliderLookup;
        ComponentLookup<PlayerCharacterComponent> _characterLookup;
        ComponentLookup<PlayerCharacterControl> _controlLookup;
        ComponentLookup<PlayerCharacterState> _playerStateLookup;
        ComponentLookup<PlayerStanceColliders> _stanceColliderLookup;
        ComponentLookup<MorrowindMovementInput> _inputLookup;
        ComponentLookup<MorrowindMovementState> _movementStateLookup;
        ComponentLookup<MorrowindMovementSpeed> _speedLookup;
        ComponentLookup<PlayerViewComponent> _viewLookup;
        ComponentLookup<PlayerTag> _playerTagLookup;
        ComponentLookup<PassiveActorPresence> _passiveActorLookup;

        const int ParallelNonPlayerMovementThreshold = 64;

        public void OnCreate(ref SystemState state)
        {
            _playerQuery = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<PlayerTag>(),
                    ComponentType.ReadWrite<LocalTransform>(),
                    ComponentType.ReadWrite<LocalToWorld>(),
                    ComponentType.ReadOnly<PhysicsCollider>(),
                    ComponentType.ReadOnly<PlayerCharacterComponent>(),
                    ComponentType.ReadWrite<PlayerCharacterControl>(),
                    ComponentType.ReadWrite<PlayerCharacterState>(),
                    ComponentType.ReadWrite<MorrowindMovementInput>(),
                    ComponentType.ReadWrite<MorrowindMovementState>(),
                    ComponentType.ReadOnly<PlayerStanceColliders>(),
                    ComponentType.ReadOnly<MorrowindMovementSpeed>(),
                },
            });
            _viewQuery = state.GetEntityQuery(
                ComponentType.ReadWrite<PlayerViewComponent>(),
                ComponentType.ReadWrite<LocalTransform>());
            _nonPlayerQuery = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<PassiveActorPresence>(),
                    ComponentType.ReadWrite<LocalTransform>(),
                    ComponentType.ReadWrite<LocalToWorld>(),
                    ComponentType.ReadOnly<PhysicsCollider>(),
                    ComponentType.ReadWrite<MorrowindMovementInput>(),
                    ComponentType.ReadWrite<MorrowindMovementState>(),
                    ComponentType.ReadOnly<MorrowindMovementSpeed>(),
                },
                None = new ComponentType[]
                {
                    ComponentType.ReadOnly<PlayerTag>(),
                },
            });
            _mutationQueueQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<RuntimePhysicsMutationQueueTag>(),
                ComponentType.ReadWrite<RuntimePhysicsMutationRequest>(),
                ComponentType.ReadWrite<PhysicsFlushRequested>());

            _transformHandle = state.GetComponentTypeHandle<LocalTransform>(isReadOnly: false);
            _localToWorldHandle = state.GetComponentTypeHandle<LocalToWorld>(isReadOnly: false);
            _inputHandle = state.GetComponentTypeHandle<MorrowindMovementInput>(isReadOnly: false);
            _movementStateHandle = state.GetComponentTypeHandle<MorrowindMovementState>(isReadOnly: false);
            _colliderHandle = state.GetComponentTypeHandle<PhysicsCollider>(isReadOnly: true);
            _speedHandle = state.GetComponentTypeHandle<MorrowindMovementSpeed>(isReadOnly: true);

            _transformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: false);
            _localToWorldLookup = state.GetComponentLookup<LocalToWorld>(isReadOnly: false);
            _colliderLookup = state.GetComponentLookup<PhysicsCollider>(isReadOnly: true);
            _characterLookup = state.GetComponentLookup<PlayerCharacterComponent>(isReadOnly: true);
            _controlLookup = state.GetComponentLookup<PlayerCharacterControl>(isReadOnly: false);
            _playerStateLookup = state.GetComponentLookup<PlayerCharacterState>(isReadOnly: false);
            _stanceColliderLookup = state.GetComponentLookup<PlayerStanceColliders>(isReadOnly: true);
            _inputLookup = state.GetComponentLookup<MorrowindMovementInput>(isReadOnly: false);
            _movementStateLookup = state.GetComponentLookup<MorrowindMovementState>(isReadOnly: false);
            _speedLookup = state.GetComponentLookup<MorrowindMovementSpeed>(isReadOnly: true);
            _viewLookup = state.GetComponentLookup<PlayerViewComponent>(isReadOnly: false);
            _playerTagLookup = state.GetComponentLookup<PlayerTag>(isReadOnly: true);
            _passiveActorLookup = state.GetComponentLookup<PassiveActorPresence>(isReadOnly: true);

            state.RequireForUpdate(_playerQuery);
            state.RequireForUpdate(_viewQuery);
            state.RequireForUpdate(_mutationQueueQuery);
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<MorrowindMovementSettings>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0f)
                return;

            int playerCount = _playerQuery.CalculateEntityCount();
            if (playerCount != 1)
                throw new InvalidOperationException($"[VVardenfell][Movement] Expected exactly one player movement entity, found {playerCount}.");

            int viewCount = _viewQuery.CalculateEntityCount();
            if (viewCount != 1)
                throw new InvalidOperationException($"[VVardenfell][Movement] Expected exactly one player view entity, found {viewCount}.");

            int mutationQueueCount = _mutationQueueQuery.CalculateEntityCount();
            if (mutationQueueCount != 1)
                throw new InvalidOperationException($"[VVardenfell][Movement] Expected exactly one runtime physics mutation queue, found {mutationQueueCount}.");

            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            var settings = SystemAPI.GetSingleton<MorrowindMovementSettings>();

            _transformLookup.Update(ref state);
            _localToWorldLookup.Update(ref state);
            _colliderLookup.Update(ref state);
            _characterLookup.Update(ref state);
            _controlLookup.Update(ref state);
            _playerStateLookup.Update(ref state);
            _stanceColliderLookup.Update(ref state);
            _inputLookup.Update(ref state);
            _movementStateLookup.Update(ref state);
            _speedLookup.Update(ref state);
            _viewLookup.Update(ref state);
            _playerTagLookup.Update(ref state);
            _passiveActorLookup.Update(ref state);

            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            state.Dependency.Complete();
            new PlayerActorMovementJob
            {
                PlayerEntity = _playerQuery.GetSingletonEntity(),
                ViewEntity = _viewQuery.GetSingletonEntity(),
                MutationQueueEntity = _mutationQueueQuery.GetSingletonEntity(),
                CommandBuffer = ecb.AsParallelWriter(),
                CollisionWorld = physicsWorld.CollisionWorld,
                Settings = settings,
                DeltaTime = dt,
                TransformLookup = _transformLookup,
                LocalToWorldLookup = _localToWorldLookup,
                ColliderLookup = _colliderLookup,
                CharacterLookup = _characterLookup,
                ControlLookup = _controlLookup,
                PlayerStateLookup = _playerStateLookup,
                StanceColliderLookup = _stanceColliderLookup,
                InputLookup = _inputLookup,
                MovementStateLookup = _movementStateLookup,
                SpeedLookup = _speedLookup,
                ViewLookup = _viewLookup,
                PlayerTagLookup = _playerTagLookup,
                PassiveActorLookup = _passiveActorLookup,
            }.Run();
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
            _playerTagLookup.Update(ref state);
            _passiveActorLookup.Update(ref state);

            int nonPlayerCount = _nonPlayerQuery.CalculateEntityCount();
            if (nonPlayerCount == 0)
            {
                state.Dependency = default;
                return;
            }

            _transformHandle.Update(ref state);
            _localToWorldHandle.Update(ref state);
            _inputHandle.Update(ref state);
            _movementStateHandle.Update(ref state);
            _colliderHandle.Update(ref state);
            _speedHandle.Update(ref state);

            var nonPlayerJob = new NonPlayerActorMovementJob
            {
                CollisionWorld = physicsWorld.CollisionWorld,
                Settings = settings,
                DeltaTime = dt,
                TransformHandle = _transformHandle,
                LocalToWorldHandle = _localToWorldHandle,
                InputHandle = _inputHandle,
                MovementStateHandle = _movementStateHandle,
                ColliderHandle = _colliderHandle,
                SpeedHandle = _speedHandle,
                PlayerTagLookup = _playerTagLookup,
                PassiveActorLookup = _passiveActorLookup,
            };

            if (nonPlayerCount < ParallelNonPlayerMovementThreshold)
            {
                nonPlayerJob.Run(_nonPlayerQuery);
            }
            else
            {
                state.Dependency = nonPlayerJob.ScheduleParallel(_nonPlayerQuery, default);
                state.Dependency.Complete();
            }

            state.Dependency = default;
        }

        [BurstCompile]
        struct PlayerActorMovementJob : IJob
        {
            public Entity PlayerEntity;
            public Entity ViewEntity;
            public Entity MutationQueueEntity;
            public EntityCommandBuffer.ParallelWriter CommandBuffer;
            [ReadOnly] public CollisionWorld CollisionWorld;
            public MorrowindMovementSettings Settings;
            public float DeltaTime;
            public ComponentLookup<LocalTransform> TransformLookup;
            public ComponentLookup<LocalToWorld> LocalToWorldLookup;
            [ReadOnly] public ComponentLookup<PhysicsCollider> ColliderLookup;
            [ReadOnly] public ComponentLookup<PlayerCharacterComponent> CharacterLookup;
            public ComponentLookup<PlayerCharacterControl> ControlLookup;
            public ComponentLookup<PlayerCharacterState> PlayerStateLookup;
            [ReadOnly] public ComponentLookup<PlayerStanceColliders> StanceColliderLookup;
            public ComponentLookup<MorrowindMovementInput> InputLookup;
            public ComponentLookup<MorrowindMovementState> MovementStateLookup;
            [ReadOnly] public ComponentLookup<MorrowindMovementSpeed> SpeedLookup;
            public ComponentLookup<PlayerViewComponent> ViewLookup;
            [ReadOnly] public ComponentLookup<PlayerTag> PlayerTagLookup;
            [ReadOnly] public ComponentLookup<PassiveActorPresence> PassiveActorLookup;

            [BurstCompile]
            public void Execute()
            {
                var transform = TransformLookup[PlayerEntity];
                var currentCollider = ColliderLookup[PlayerEntity];
                var character = CharacterLookup[PlayerEntity];
                var control = ControlLookup[PlayerEntity];
                var playerState = PlayerStateLookup[PlayerEntity];
                var stanceColliders = StanceColliderLookup[PlayerEntity];
                var input = InputLookup[PlayerEntity];
                var movementState = MovementStateLookup[PlayerEntity];
                var movementSpeed = SpeedLookup[PlayerEntity];
                var view = ViewLookup[ViewEntity];

                if (view.ControlledCharacter != PlayerEntity)
                    throw new InvalidOperationException("[VVardenfell][Movement] Player view is not controlled by the player movement entity.");

                bool requestedCrouch = input.SneakHeld || movementState.ForceSneak;
                bool crouched = ResolveCrouchedState(
                    CollisionWorld,
                    stanceColliders,
                    character,
                    transform.Position,
                    transform.Rotation,
                    playerState.Crouched,
                    requestedCrouch);
                input.SneakHeld = crouched;
                control.CrouchHeld = crouched;

                PhysicsCollider activeCollider = new()
                {
                    Value = crouched ? stanceColliders.Crouching : stanceColliders.Standing,
                };
                if (!currentCollider.Value.Equals(activeCollider.Value))
                {
                    CommandBuffer.AppendToBuffer(0, MutationQueueEntity, new RuntimePhysicsMutationRequest
                    {
                        Kind = RuntimePhysicsMutationKind.SetPhysicsCollider,
                        Entity = PlayerEntity,
                        Collider = activeCollider.Value,
                    });
                    CommandBuffer.SetComponent(0, MutationQueueEntity, new PhysicsFlushRequested { Pending = 1 });
                }

                float eyeHeight = crouched
                    ? character.CrouchingEyeHeight
                    : character.StandingEyeHeight;
                view.LocalEyeOffset = new float3(0f, eyeHeight, 0f);
                TransformLookup[ViewEntity] = LocalTransform.FromPositionRotationScale(
                    view.LocalEyeOffset,
                    view.LocalViewRotation,
                    1f);

                float3 position = transform.Position;
                quaternion rotation = transform.Rotation;
                bool previousGrounded = playerState.Grounded;

                MorrowindActorMovementKernel.Solve(
                    CollisionWorld,
                    activeCollider,
                    Settings,
                    movementSpeed,
                    PlayerTagLookup,
                    PassiveActorLookup,
                    rotation,
                    ref position,
                    ref input,
                    ref movementState,
                    DeltaTime, out var result);

                input.JumpPressed = false;

                playerState.WasGrounded = previousGrounded;
                playerState.Grounded = movementState.Grounded;
                playerState.WorldVelocity = movementState.LastVelocity;
                playerState.Crouched = crouched;
                playerState.Sprinting = input.RunHeld && !movementState.SneakHeld && movementState.SpeedFactor > 0f;
                if (playerState.Grounded)
                {
                    playerState.GroundedTime = playerState.WasGrounded ? playerState.GroundedTime + DeltaTime : DeltaTime;
                    playerState.AirborneTime = 0f;
                }
                else
                {
                    playerState.AirborneTime = playerState.WasGrounded ? DeltaTime : playerState.AirborneTime + DeltaTime;
                    playerState.GroundedTime = 0f;
                }

                control.MoveInput = result.PlanarInput;
                control.MoveVectorWorld = result.LocalMoveWorld;
                control.JumpThisFixedTick = movementState.JumpAccepted;

                transform = LocalTransform.FromPositionRotationScale(position, rotation, transform.Scale);
                TransformLookup[PlayerEntity] = transform;
                LocalToWorldLookup[PlayerEntity] = new LocalToWorld
                {
                    Value = float4x4.TRS(position, rotation, new float3(transform.Scale)),
                };
                ControlLookup[PlayerEntity] = control;
                PlayerStateLookup[PlayerEntity] = playerState;
                InputLookup[PlayerEntity] = input;
                MovementStateLookup[PlayerEntity] = movementState;
                ViewLookup[ViewEntity] = view;
            }

            [BurstCompile]
            static bool ResolveCrouchedState(
                in CollisionWorld world,
                in PlayerStanceColliders stanceColliders,
                in PlayerCharacterComponent character,
                in float3 position,
                in quaternion rotation,
                bool currentlyCrouched,
                bool crouchRequested)
            {
                if (!stanceColliders.Standing.IsCreated || !stanceColliders.Crouching.IsCreated)
                    throw new InvalidOperationException("[VVardenfell][Movement] Player crouch requires both standing and crouching stance colliders.");

                if (crouchRequested)
                    return true;

                if (!currentlyCrouched)
                    return false;

                return !CanStand(world, stanceColliders, character, position, rotation);
            }

            [BurstCompile]
            static bool CanStand(
                in CollisionWorld world,
                in PlayerStanceColliders stanceColliders,
                in PlayerCharacterComponent character,
                in float3 position,
                in quaternion rotation)
            {
                float standDelta = character.StandingHeight - character.CrouchingHeight;
                if (standDelta <= 0f)
                    return true;

                var input = new ColliderCastInput(
                    stanceColliders.Crouching,
                    position,
                    position + new float3(0f, standDelta, 0f),
                    rotation);
                return !world.CastCollider(input);
            }
        }

        [BurstCompile]
        struct NonPlayerActorMovementJob : IJobChunk
        {
            [ReadOnly] public CollisionWorld CollisionWorld;
            public MorrowindMovementSettings Settings;
            public float DeltaTime;
            public ComponentTypeHandle<LocalTransform> TransformHandle;
            public ComponentTypeHandle<LocalToWorld> LocalToWorldHandle;
            public ComponentTypeHandle<MorrowindMovementInput> InputHandle;
            public ComponentTypeHandle<MorrowindMovementState> MovementStateHandle;
            [ReadOnly] public ComponentTypeHandle<PhysicsCollider> ColliderHandle;
            [ReadOnly] public ComponentTypeHandle<MorrowindMovementSpeed> SpeedHandle;
            [ReadOnly] public ComponentLookup<PlayerTag> PlayerTagLookup;
            [ReadOnly] public ComponentLookup<PassiveActorPresence> PassiveActorLookup;

            [BurstCompile]
            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                var transforms = chunk.GetNativeArray(ref TransformHandle);
                var localToWorlds = chunk.GetNativeArray(ref LocalToWorldHandle);
                var inputs = chunk.GetNativeArray(ref InputHandle);
                var movementStates = chunk.GetNativeArray(ref MovementStateHandle);
                var colliders = chunk.GetNativeArray(ref ColliderHandle);
                var speeds = chunk.GetNativeArray(ref SpeedHandle);

                int count = chunk.Count;
                for (int i = 0; i < count; i++)
                {
                    var transform = transforms[i];
                    var input = inputs[i];
                    var movementState = movementStates[i];
                    float3 position = transform.Position;
                    quaternion rotation = transform.Rotation;

                    MorrowindActorMovementKernel.Solve(
                        CollisionWorld,
                        colliders[i],
                        Settings,
                        speeds[i],
                        PlayerTagLookup,
                        PassiveActorLookup,
                        rotation,
                        ref position,
                        ref input,
                        ref movementState,
                        DeltaTime, out _);

                    input = default;

                    transform = LocalTransform.FromPositionRotationScale(position, rotation, transform.Scale);
                    transforms[i] = transform;
                    localToWorlds[i] = new LocalToWorld
                    {
                        Value = float4x4.TRS(position, rotation, new float3(transform.Scale)),
                    };
                    inputs[i] = input;
                    movementStates[i] = movementState;
                }
            }
        }
    }
}
