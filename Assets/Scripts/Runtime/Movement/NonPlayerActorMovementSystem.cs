using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
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
    public partial struct NonPlayerActorMovementSystem : ISystem
    {
        EntityQuery _query;
        ComponentTypeHandle<LocalTransform> _transformHandle;
        ComponentTypeHandle<LocalToWorld> _localToWorldHandle;
        ComponentTypeHandle<MorrowindMovementInput> _inputHandle;
        ComponentTypeHandle<MorrowindMovementState> _movementStateHandle;
        ComponentTypeHandle<PhysicsCollider> _colliderHandle;
        ComponentTypeHandle<MorrowindMovementSpeed> _speedHandle;

        public void OnCreate(ref SystemState state)
        {
            _query = state.GetEntityQuery(new EntityQueryDesc
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

            _transformHandle = state.GetComponentTypeHandle<LocalTransform>(isReadOnly: false);
            _localToWorldHandle = state.GetComponentTypeHandle<LocalToWorld>(isReadOnly: false);
            _inputHandle = state.GetComponentTypeHandle<MorrowindMovementInput>(isReadOnly: false);
            _movementStateHandle = state.GetComponentTypeHandle<MorrowindMovementState>(isReadOnly: false);
            _colliderHandle = state.GetComponentTypeHandle<PhysicsCollider>(isReadOnly: true);
            _speedHandle = state.GetComponentTypeHandle<MorrowindMovementSpeed>(isReadOnly: true);

            state.RequireForUpdate(_query);
            state.RequireForUpdate<PhysicsWorldSingleton>();
            state.RequireForUpdate<MorrowindMovementSettings>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0f)
                return;

            var settings = SystemAPI.GetSingleton<MorrowindMovementSettings>();
            _transformHandle.Update(ref state);
            _localToWorldHandle.Update(ref state);
            _inputHandle.Update(ref state);
            _movementStateHandle.Update(ref state);
            _colliderHandle.Update(ref state);
            _speedHandle.Update(ref state);

            state.Dependency = new NonPlayerActorMovementJob
            {
                CollisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld,
                Settings = settings,
                DeltaTime = dt,
                TransformHandle = _transformHandle,
                LocalToWorldHandle = _localToWorldHandle,
                InputHandle = _inputHandle,
                MovementStateHandle = _movementStateHandle,
                ColliderHandle = _colliderHandle,
                SpeedHandle = _speedHandle,
            }.ScheduleParallel(_query, state.Dependency);
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

                    var result = MorrowindActorMovementSolver.SolveUnmanaged(
                        CollisionWorld,
                        colliders[i],
                        Settings,
                        speeds[i],
                        rotation,
                        ref position,
                        ref input,
                        ref movementState,
                        DeltaTime);

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
