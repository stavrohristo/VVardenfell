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
        ComponentTypeHandle<MorrowindMovementIntent> _intentHandle;
        ComponentTypeHandle<MorrowindActorKinematicState> _kinematicHandle;
        ComponentTypeHandle<MorrowindMovementFrameTrace> _traceHandle;
        ComponentTypeHandle<PhysicsCollider> _colliderHandle;
        ComponentTypeHandle<MorrowindMovementTuning> _tuningHandle;
        ComponentTypeHandle<ActorDerivedMovementStats> _derivedStatsHandle;

        public void OnCreate(ref SystemState state)
        {
            _query = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<PassiveActorPresence>(),
                    ComponentType.ReadWrite<LocalTransform>(),
                    ComponentType.ReadWrite<LocalToWorld>(),
                    ComponentType.ReadWrite<PhysicsCollider>(),
                    ComponentType.ReadWrite<MorrowindMovementIntent>(),
                    ComponentType.ReadWrite<MorrowindActorKinematicState>(),
                    ComponentType.ReadWrite<MorrowindMovementFrameTrace>(),
                    ComponentType.ReadOnly<MorrowindMovementTuning>(),
                    ComponentType.ReadOnly<ActorDerivedMovementStats>(),
                },
                None = new ComponentType[]
                {
                    ComponentType.ReadOnly<PlayerTag>(),
                },
            });

            _transformHandle = state.GetComponentTypeHandle<LocalTransform>(isReadOnly: false);
            _localToWorldHandle = state.GetComponentTypeHandle<LocalToWorld>(isReadOnly: false);
            _intentHandle = state.GetComponentTypeHandle<MorrowindMovementIntent>(isReadOnly: false);
            _kinematicHandle = state.GetComponentTypeHandle<MorrowindActorKinematicState>(isReadOnly: false);
            _traceHandle = state.GetComponentTypeHandle<MorrowindMovementFrameTrace>(isReadOnly: false);
            _colliderHandle = state.GetComponentTypeHandle<PhysicsCollider>(isReadOnly: false);
            _tuningHandle = state.GetComponentTypeHandle<MorrowindMovementTuning>(isReadOnly: true);
            _derivedStatsHandle = state.GetComponentTypeHandle<ActorDerivedMovementStats>(isReadOnly: true);

            state.RequireForUpdate(_query);
            state.RequireForUpdate<PhysicsWorldSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0f)
                return;

            _transformHandle.Update(ref state);
            _localToWorldHandle.Update(ref state);
            _intentHandle.Update(ref state);
            _kinematicHandle.Update(ref state);
            _traceHandle.Update(ref state);
            _colliderHandle.Update(ref state);
            _tuningHandle.Update(ref state);
            _derivedStatsHandle.Update(ref state);

            state.Dependency = new NonPlayerActorMovementJob
            {
                CollisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld,
                DeltaTime = dt,
                TransformHandle = _transformHandle,
                LocalToWorldHandle = _localToWorldHandle,
                IntentHandle = _intentHandle,
                KinematicHandle = _kinematicHandle,
                TraceHandle = _traceHandle,
                ColliderHandle = _colliderHandle,
                TuningHandle = _tuningHandle,
                DerivedStatsHandle = _derivedStatsHandle,
            }.ScheduleParallel(_query, state.Dependency);
        }

        [BurstCompile]
        struct NonPlayerActorMovementJob : IJobChunk
        {
            [ReadOnly] public CollisionWorld CollisionWorld;
            public float DeltaTime;
            public ComponentTypeHandle<LocalTransform> TransformHandle;
            public ComponentTypeHandle<LocalToWorld> LocalToWorldHandle;
            public ComponentTypeHandle<MorrowindMovementIntent> IntentHandle;
            public ComponentTypeHandle<MorrowindActorKinematicState> KinematicHandle;
            public ComponentTypeHandle<MorrowindMovementFrameTrace> TraceHandle;
            public ComponentTypeHandle<PhysicsCollider> ColliderHandle;
            [ReadOnly] public ComponentTypeHandle<MorrowindMovementTuning> TuningHandle;
            [ReadOnly] public ComponentTypeHandle<ActorDerivedMovementStats> DerivedStatsHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                var transforms = chunk.GetNativeArray(ref TransformHandle);
                var localToWorlds = chunk.GetNativeArray(ref LocalToWorldHandle);
                var intents = chunk.GetNativeArray(ref IntentHandle);
                var kinematics = chunk.GetNativeArray(ref KinematicHandle);
                var traces = chunk.GetNativeArray(ref TraceHandle);
                var colliders = chunk.GetNativeArray(ref ColliderHandle);
                var tunings = chunk.GetNativeArray(ref TuningHandle);
                var derivedStats = chunk.GetNativeArray(ref DerivedStatsHandle);

                int count = chunk.Count;
                for (int i = 0; i < count; i++)
                {
                    var transform = transforms[i];
                    var intent = intents[i];
                    var kinematic = kinematics[i];
                    var trace = traces[i];
                    float3 position = transform.Position;
                    quaternion rotation = transform.Rotation;

                    var stats = MorrowindActorMovementStats.BuildUnmanaged(derivedStats[i]);
                    var result = MorrowindActorMovementSolver.SolveUnmanaged(
                        CollisionWorld,
                        colliders[i],
                        tunings[i],
                        stats,
                        rotation,
                        ref position,
                        ref intent,
                        ref kinematic,
                        trace,
                        DeltaTime);

                    intent.LocalMove = float3.zero;
                    intent.RunHeld = false;
                    intent.SneakHeld = false;
                    intent.JumpHeld = false;

                    transform = LocalTransform.FromPositionRotationScale(position, rotation, transform.Scale);
                    transforms[i] = transform;
                    localToWorlds[i] = new LocalToWorld
                    {
                        Value = float4x4.TRS(position, rotation, new float3(transform.Scale)),
                    };
                    intents[i] = intent;
                    kinematics[i] = kinematic;
                    traces[i] = result.Trace;
                }
            }
        }
    }
}
