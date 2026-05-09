using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Pathfinding
{
    public enum PathGridTraversalStatus : byte
    {
        Idle = 0,
        RequestingPath = 1,
        Traversing = 2,
        Reached = 3,
        Failed = 4,
        Canceled = 5,
    }

    public struct PathGridTraversalSettings : IComponentData
    {
        public float NodeArrivalDistance;
        public float FinalArrivalDistance;
        public float FacingSharpness;
        public int MaxFineIterations;
        public int MaxAbstractIterations;
        public byte AllowPartial;
        public byte Run;
        public byte FacePath;

        public static PathGridTraversalSettings Defaults => new PathGridTraversalSettings
        {
            NodeArrivalDistance = 0.35f,
            FinalArrivalDistance = 0.45f,
            FacingSharpness = 12f,
            MaxFineIterations = 4096,
            MaxAbstractIterations = 4096,
            AllowPartial = 0,
            Run = 0,
            FacePath = 1,
        };
    }

    public struct PathGridTraversalPendingRequest : IComponentData, IEnableableComponent
    {
        public int StartNodeIndex;
        public int GoalNodeIndex;
        public float3 FinalTargetPosition;
        public int MaxFineIterations;
        public int MaxAbstractIterations;
        public byte AllowPartial;
        public byte Run;
        public byte UseFinalTargetPosition;
    }

    public struct PathGridTraversalAwaitingResult : IComponentData, IEnableableComponent
    {
    }

    public struct PathGridTraversalState : IComponentData
    {
        public int ActivePathRequestId;
        public int CurrentNodeOffset;
        public int PathNodeCount;
        public int LastStartNodeIndex;
        public int LastGoalNodeIndex;
        public float PathCost;
        public float3 FinalTargetPosition;
        public byte Status;
        public byte UsedAbstractRoute;
        public byte ReachedGoal;
        public byte Run;
        public byte UseFinalTargetPosition;
    }

    public struct PathGridTraversalNode : IBufferElementData
    {
        public int NodeIndex;
    }

    public static class PathGridTraversalBridge
    {
        static World s_SettingsQueryWorld;
        static EntityQuery s_SettingsQuery;
        static bool s_SettingsQueryCreated;

        public static bool TryRequestNodeTraversal(
            Entity owner,
            int startNodeIndex,
            int goalNodeIndex,
            out string error,
            bool allowPartial = false,
            bool run = false,
            int maxFineIterations = 0,
            int maxAbstractIterations = 0)
        {
            if (owner == Entity.Null)
            {
                error = "Pathgrid traversal owner entity is invalid.";
                return false;
            }

            if (startNodeIndex < 0 || goalNodeIndex < 0)
            {
                error = "Pathgrid traversal has invalid node indices.";
                return false;
            }

            if (!TryGetWorld(out var entityManager, out error))
                return false;

            if (!entityManager.Exists(owner))
            {
                error = "Pathgrid traversal owner entity does not exist.";
                return false;
            }

            EnsureTraversalComponents(entityManager, owner);
            var settings = ResolveSettings(entityManager);

            entityManager.SetComponentData(owner, new PathGridTraversalPendingRequest
            {
                StartNodeIndex = startNodeIndex,
                GoalNodeIndex = goalNodeIndex,
                MaxFineIterations = maxFineIterations > 0 ? maxFineIterations : settings.MaxFineIterations,
                MaxAbstractIterations = maxAbstractIterations > 0 ? maxAbstractIterations : settings.MaxAbstractIterations,
                AllowPartial = allowPartial ? (byte)1 : settings.AllowPartial,
                Run = run ? (byte)1 : settings.Run,
                UseFinalTargetPosition = 0,
            });
            entityManager.SetComponentEnabled<PathGridTraversalPendingRequest>(owner, true);
            entityManager.SetComponentEnabled<PathGridTraversalAwaitingResult>(owner, false);

            var state = entityManager.GetComponentData<PathGridTraversalState>(owner);
            if (state.ActivePathRequestId > 0)
                PathGridPathfindingRequestBridge.TryCancelRequest(state.ActivePathRequestId, out _);
            state = new PathGridTraversalState
            {
                Status = (byte)PathGridTraversalStatus.Idle,
                LastStartNodeIndex = startNodeIndex,
                LastGoalNodeIndex = goalNodeIndex,
                Run = run ? (byte)1 : settings.Run,
            };
            entityManager.SetComponentData(owner, state);
            entityManager.GetBuffer<PathGridTraversalNode>(owner).Clear();

            error = null;
            return true;
        }

        public static bool TryRequestExteriorTraversal(
            Entity owner,
            int2 startCell,
            float3 startWorldPosition,
            int2 goalCell,
            float3 goalWorldPosition,
            out string error,
            bool allowPartial = false,
            bool run = false,
            int maxFineIterations = 0,
            int maxAbstractIterations = 0)
        {
            var navigation = WorldResources.PathGridNavigation;
            if (!navigation.IsCreated)
            {
                error = "Pathgrid navigation world is not ready.";
                return false;
            }

            if (!navigation.TryResolveNearestExteriorNode(startCell.x, startCell.y, startWorldPosition, out int startNodeIndex))
            {
                error = $"No exterior pathgrid node found for start cell ({startCell.x}, {startCell.y}).";
                return false;
            }

            if (!navigation.TryResolveNearestExteriorNode(goalCell.x, goalCell.y, goalWorldPosition, out int goalNodeIndex))
            {
                error = $"No exterior pathgrid node found for goal cell ({goalCell.x}, {goalCell.y}).";
                return false;
            }

            return TryRequestNodeTraversal(
                owner,
                startNodeIndex,
                goalNodeIndex,
                out error,
                allowPartial,
                run,
                maxFineIterations,
                maxAbstractIterations);
        }

        public static bool TryCancelTraversal(Entity owner, out string error)
        {
            if (!TryGetWorld(out var entityManager, out error))
                return false;

            if (owner == Entity.Null || !entityManager.Exists(owner) || !entityManager.HasComponent<PathGridTraversalState>(owner))
            {
                error = "Pathgrid traversal owner is not active.";
                return false;
            }

            var state = entityManager.GetComponentData<PathGridTraversalState>(owner);
            if (state.ActivePathRequestId > 0)
                PathGridPathfindingRequestBridge.TryCancelRequest(state.ActivePathRequestId, out _);

            state.ActivePathRequestId = 0;
            state.Status = (byte)PathGridTraversalStatus.Canceled;
            state.CurrentNodeOffset = 0;
            state.PathNodeCount = 0;
            entityManager.SetComponentData(owner, state);
            if (entityManager.HasComponent<PathGridTraversalPendingRequest>(owner))
            {
                entityManager.SetComponentData(owner, default(PathGridTraversalPendingRequest));
                entityManager.SetComponentEnabled<PathGridTraversalPendingRequest>(owner, false);
            }
            if (entityManager.HasComponent<PathGridTraversalAwaitingResult>(owner))
                entityManager.SetComponentEnabled<PathGridTraversalAwaitingResult>(owner, false);
            if (entityManager.HasComponent<PathGridTraversalNode>(owner))
                entityManager.GetBuffer<PathGridTraversalNode>(owner).Clear();

            error = null;
            return true;
        }

        static void EnsureTraversalComponents(EntityManager entityManager, Entity owner)
        {
            if (!entityManager.HasComponent<PathGridTraversalState>(owner))
                entityManager.AddComponentData(owner, new PathGridTraversalState());
            if (!entityManager.HasBuffer<PathGridTraversalNode>(owner))
                entityManager.AddBuffer<PathGridTraversalNode>(owner);
            if (!entityManager.HasComponent<PathGridTraversalPendingRequest>(owner))
            {
                entityManager.AddComponentData(owner, new PathGridTraversalPendingRequest());
                entityManager.SetComponentEnabled<PathGridTraversalPendingRequest>(owner, false);
            }
            if (!entityManager.HasComponent<PathGridTraversalAwaitingResult>(owner))
            {
                entityManager.AddComponentData(owner, new PathGridTraversalAwaitingResult());
                entityManager.SetComponentEnabled<PathGridTraversalAwaitingResult>(owner, false);
            }
        }

        static PathGridTraversalSettings ResolveSettings(EntityManager entityManager)
        {
            EntityQuery query = GetSettingsQuery(entityManager);
            return query.IsEmptyIgnoreFilter
                ? PathGridTraversalSettings.Defaults
                : query.GetSingleton<PathGridTraversalSettings>();
        }

        static EntityQuery GetSettingsQuery(EntityManager entityManager)
        {
            World world = entityManager.World;
            if (s_SettingsQueryCreated && s_SettingsQueryWorld == world)
                return s_SettingsQuery;

            s_SettingsQueryWorld = world;
            s_SettingsQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PathGridTraversalSettings>());
            s_SettingsQueryCreated = true;
            return s_SettingsQuery;
        }

        static bool TryGetWorld(out EntityManager entityManager, out string error)
        {
            entityManager = default;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                error = "Default ECS world is not ready.";
                return false;
            }

            entityManager = world.EntityManager;
            error = null;
            return true;
        }
    }

    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    public partial struct PathGridTraversalSettingsBootstrapSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PathGridTraversalSettingsBootstrapRequest>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.HasSingleton<PathGridTraversalSettings>())
            {
                RuntimeBootstrapRequestUtility.Consume<PathGridTraversalSettingsBootstrapRequest>(state.EntityManager);
                return;
            }

            Entity entity = state.EntityManager.CreateEntity();
            state.EntityManager.SetName(entity, "VVardenfell.PathGridTraversalSettings");
            state.EntityManager.AddComponentData(entity, PathGridTraversalSettings.Defaults);
            RuntimeBootstrapRequestUtility.Consume<PathGridTraversalSettingsBootstrapRequest>(state.EntityManager);
        }
    }

    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(ActorAiPlannerSystem))]
    [UpdateBefore(typeof(PathGridPathfindingSystem))]
    public partial struct PathGridTraversalRequestSystem : ISystem
    {
        EntityQuery _query;
        EntityQuery _pathfindingQuery;
        EntityTypeHandle _entityHandle;
        ComponentTypeHandle<PathGridTraversalState> _stateHandle;
        ComponentTypeHandle<PathGridTraversalPendingRequest> _pendingRequestHandle;
        ComponentTypeHandle<PathGridTraversalAwaitingResult> _awaitingResultHandle;
        NativeList<PendingPathGridPathRequest> _pendingRequests;
        NativeArray<int> _requestCounters;

        public void OnCreate(ref SystemState systemState)
        {
            _query = SystemAPI.QueryBuilder()
                .WithAll<PathGridTraversalState, PathGridTraversalPendingRequest>()
                .WithPresent<PathGridTraversalAwaitingResult>()
                .Build();
            _entityHandle = systemState.GetEntityTypeHandle();
            _stateHandle = systemState.GetComponentTypeHandle<PathGridTraversalState>(isReadOnly: false);
            _pendingRequestHandle = systemState.GetComponentTypeHandle<PathGridTraversalPendingRequest>(isReadOnly: false);
            _awaitingResultHandle = systemState.GetComponentTypeHandle<PathGridTraversalAwaitingResult>(isReadOnly: false);
            _pathfindingQuery = systemState.GetEntityQuery(
                ComponentType.ReadWrite<PathGridPathfindingState>(),
                ComponentType.ReadWrite<PendingPathGridPathRequest>());
            _pendingRequests = new NativeList<PendingPathGridPathRequest>(Allocator.Persistent);
            _requestCounters = new NativeArray<int>(2, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState systemState)
        {
            if (_pendingRequests.IsCreated)
                _pendingRequests.Dispose();
            if (_requestCounters.IsCreated)
                _requestCounters.Dispose();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            if (_pathfindingQuery.IsEmptyIgnoreFilter)
                return;

            Entity pathfindingEntity = _pathfindingQuery.GetSingletonEntity();
            var pathfindingState = systemState.EntityManager.GetComponentData<PathGridPathfindingState>(pathfindingEntity);
            var pendingRequests = systemState.EntityManager.GetBuffer<PendingPathGridPathRequest>(pathfindingEntity);

            _entityHandle.Update(ref systemState);
            _stateHandle.Update(ref systemState);
            _pendingRequestHandle.Update(ref systemState);
            _awaitingResultHandle.Update(ref systemState);

            int requestCapacity = math.max(1, _query.CalculateEntityCount());
            if (_pendingRequests.Capacity < requestCapacity)
                _pendingRequests.Capacity = requestCapacity;
            _pendingRequests.Clear();
            _requestCounters[0] = pathfindingState.NextRequestId;
            _requestCounters[1] = 0;

            new BuildTraversalPathRequestsJob
            {
                RequestedAt = SystemAPI.Time.ElapsedTime,
                EntityHandle = _entityHandle,
                StateHandle = _stateHandle,
                PendingRequestHandle = _pendingRequestHandle,
                AwaitingResultHandle = _awaitingResultHandle,
                PendingRequests = _pendingRequests,
                RequestCounters = _requestCounters,
            }.Schedule(_query, systemState.Dependency).Complete();

            for (int i = 0; i < _pendingRequests.Length; i++)
                pendingRequests.Add(_pendingRequests[i]);

            pathfindingState.NextRequestId = _requestCounters[0];
            pathfindingState.TotalSubmitted += _requestCounters[1];
            systemState.EntityManager.SetComponentData(pathfindingEntity, pathfindingState);
        }

        [BurstCompile]
        struct BuildTraversalPathRequestsJob : IJobChunk
        {
            public double RequestedAt;
            [ReadOnly] public EntityTypeHandle EntityHandle;
            public ComponentTypeHandle<PathGridTraversalState> StateHandle;
            public ComponentTypeHandle<PathGridTraversalPendingRequest> PendingRequestHandle;
            public ComponentTypeHandle<PathGridTraversalAwaitingResult> AwaitingResultHandle;
            public NativeList<PendingPathGridPathRequest> PendingRequests;
            public NativeArray<int> RequestCounters;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                var entities = chunk.GetNativeArray(EntityHandle);
                var states = chunk.GetNativeArray(ref StateHandle);
                var requests = chunk.GetNativeArray(ref PendingRequestHandle);
                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    var traversalState = states[i];
                    if (traversalState.ActivePathRequestId > 0)
                    {
                        chunk.SetComponentEnabled(ref PendingRequestHandle, i, false);
                        continue;
                    }

                    var request = requests[i];
                    if (request.StartNodeIndex < 0 || request.GoalNodeIndex < 0)
                    {
                        traversalState.Status = (byte)PathGridTraversalStatus.Failed;
                        states[i] = traversalState;
                        chunk.SetComponentEnabled(ref PendingRequestHandle, i, false);
                        chunk.SetComponentEnabled(ref AwaitingResultHandle, i, false);
                        continue;
                    }

                    int requestId = RequestCounters[0]++;
                    RequestCounters[1]++;
                    PendingRequests.AddNoResize(new PendingPathGridPathRequest
                    {
                        RequestId = requestId,
                        Owner = entities[i],
                        StartNodeIndex = request.StartNodeIndex,
                        GoalNodeIndex = request.GoalNodeIndex,
                        AllowPartial = request.AllowPartial,
                        MaxFineIterations = request.MaxFineIterations,
                        MaxAbstractIterations = request.MaxAbstractIterations,
                        RequestedAt = RequestedAt,
                    });

                    traversalState.ActivePathRequestId = requestId;
                    traversalState.CurrentNodeOffset = 0;
                    traversalState.PathNodeCount = 0;
                    traversalState.LastStartNodeIndex = request.StartNodeIndex;
                    traversalState.LastGoalNodeIndex = request.GoalNodeIndex;
                    traversalState.FinalTargetPosition = request.FinalTargetPosition;
                    traversalState.Status = (byte)PathGridTraversalStatus.RequestingPath;
                    traversalState.Run = request.Run;
                    traversalState.UseFinalTargetPosition = request.UseFinalTargetPosition;
                    states[i] = traversalState;
                    chunk.SetComponentEnabled(ref PendingRequestHandle, i, false);
                    chunk.SetComponentEnabled(ref AwaitingResultHandle, i, true);
                }
            }
        }
    }

    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(PathGridTraversalRequestSystem))]
    [UpdateAfter(typeof(PathGridPathfindingSystem))]
    public partial struct PathGridTraversalResultSystem : ISystem
    {
        EntityQuery _query;
        EntityQuery _pathfindingQuery;
        ComponentTypeHandle<PathGridTraversalState> _stateHandle;
        ComponentTypeHandle<PathGridTraversalAwaitingResult> _awaitingResultHandle;
        BufferTypeHandle<PathGridTraversalNode> _pathNodeHandle;
        NativeParallelHashMap<int, int> _completedLookup;
        NativeList<int> _consumedCompletedIndices;

        public void OnCreate(ref SystemState systemState)
        {
            _query = SystemAPI.QueryBuilder()
                .WithAll<PathGridTraversalState, PathGridTraversalAwaitingResult, PathGridTraversalNode>()
                .Build();
            _stateHandle = systemState.GetComponentTypeHandle<PathGridTraversalState>(isReadOnly: false);
            _awaitingResultHandle = systemState.GetComponentTypeHandle<PathGridTraversalAwaitingResult>(isReadOnly: false);
            _pathNodeHandle = systemState.GetBufferTypeHandle<PathGridTraversalNode>(isReadOnly: false);
            _pathfindingQuery = systemState.GetEntityQuery(
                ComponentType.ReadWrite<CompletedPathGridPath>(),
                ComponentType.ReadWrite<CompletedPathGridPathNode>());
            _completedLookup = new NativeParallelHashMap<int, int>(16, Allocator.Persistent);
            _consumedCompletedIndices = new NativeList<int>(16, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState systemState)
        {
            if (_completedLookup.IsCreated)
                _completedLookup.Dispose();
            if (_consumedCompletedIndices.IsCreated)
                _consumedCompletedIndices.Dispose();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            if (_pathfindingQuery.IsEmptyIgnoreFilter)
                return;

            Entity pathfindingEntity = _pathfindingQuery.GetSingletonEntity();
            var completed = systemState.EntityManager.GetBuffer<CompletedPathGridPath>(pathfindingEntity);
            var completedNodes = systemState.EntityManager.GetBuffer<CompletedPathGridPathNode>(pathfindingEntity);
            if (completed.Length <= 0)
                return;

            if (_completedLookup.Capacity < completed.Length)
                _completedLookup.Capacity = completed.Length;
            _completedLookup.Clear();
            for (int i = 0; i < completed.Length; i++)
                _completedLookup.TryAdd(completed[i].RequestId, i);

            if (_consumedCompletedIndices.Capacity < completed.Length)
                _consumedCompletedIndices.Capacity = completed.Length;
            _consumedCompletedIndices.Clear();

            _stateHandle.Update(ref systemState);
            _awaitingResultHandle.Update(ref systemState);
            _pathNodeHandle.Update(ref systemState);

            new ApplyTraversalPathResultsJob
            {
                Completed = completed.AsNativeArray(),
                CompletedNodes = completedNodes.AsNativeArray(),
                CompletedLookup = _completedLookup,
                ConsumedCompletedIndices = _consumedCompletedIndices,
                StateHandle = _stateHandle,
                AwaitingResultHandle = _awaitingResultHandle,
                PathNodeHandle = _pathNodeHandle,
            }.Schedule(_query, systemState.Dependency).Complete();

            if (_consumedCompletedIndices.Length > 1)
                _consumedCompletedIndices.Sort();

            int lastRemoved = -1;
            for (int i = _consumedCompletedIndices.Length - 1; i >= 0; i--)
            {
                int completedIndex = _consumedCompletedIndices[i];
                if (completedIndex == lastRemoved)
                    continue;
                PathGridPathfindingSystem.RemoveCompletedAt(completed, completedNodes, completedIndex);
                lastRemoved = completedIndex;
            }
        }

        [BurstCompile]
        struct ApplyTraversalPathResultsJob : IJobChunk
        {
            [ReadOnly] public NativeArray<CompletedPathGridPath> Completed;
            [ReadOnly] public NativeArray<CompletedPathGridPathNode> CompletedNodes;
            [ReadOnly] public NativeParallelHashMap<int, int> CompletedLookup;
            public NativeList<int> ConsumedCompletedIndices;
            public ComponentTypeHandle<PathGridTraversalState> StateHandle;
            public ComponentTypeHandle<PathGridTraversalAwaitingResult> AwaitingResultHandle;
            public BufferTypeHandle<PathGridTraversalNode> PathNodeHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                var states = chunk.GetNativeArray(ref StateHandle);
                var pathNodes = chunk.GetBufferAccessor(ref PathNodeHandle);
                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    var traversalState = states[i];
                    if (traversalState.Status != (byte)PathGridTraversalStatus.RequestingPath || traversalState.ActivePathRequestId <= 0)
                        continue;
                    if (!CompletedLookup.TryGetValue(traversalState.ActivePathRequestId, out int completedIndex))
                        continue;

                    var result = Completed[completedIndex];
                    var nodes = pathNodes[i];
                    nodes.Clear();
                    if (result.FirstNodeIndex >= 0 && result.NodeCount > 0)
                    {
                        for (int nodeOffset = 0; nodeOffset < result.NodeCount; nodeOffset++)
                            nodes.Add(new PathGridTraversalNode { NodeIndex = CompletedNodes[result.FirstNodeIndex + nodeOffset].NodeIndex });
                    }

                    traversalState.ActivePathRequestId = 0;
                    traversalState.PathCost = result.Cost;
                    traversalState.PathNodeCount = result.NodeCount;
                    traversalState.UsedAbstractRoute = result.UsedAbstractRoute;
                    traversalState.ReachedGoal = result.ReachedGoal;
                    if ((PathGridPathStatus)result.Status == PathGridPathStatus.Failed || result.NodeCount == 0)
                    {
                        traversalState.Status = (byte)PathGridTraversalStatus.Failed;
                    }
                    else
                    {
                        traversalState.CurrentNodeOffset = result.NodeCount > 1 ? 1 : 0;
                        traversalState.Status = traversalState.CurrentNodeOffset >= result.NodeCount && traversalState.UseFinalTargetPosition == 0
                            ? (byte)PathGridTraversalStatus.Reached
                            : (byte)PathGridTraversalStatus.Traversing;
                    }

                    states[i] = traversalState;
                    chunk.SetComponentEnabled(ref AwaitingResultHandle, i, false);
                    ConsumedCompletedIndices.AddNoResize(completedIndex);
                }
            }
        }
    }

    [UpdateInGroup(typeof(MorrowindPhysicsQuerySystemGroup), OrderFirst = true)]
    public partial struct PathGridTraversalSteeringSystem : ISystem
    {
        EntityQuery _query;
        ComponentTypeHandle<LocalTransform> _transformHandle;
        ComponentTypeHandle<MorrowindMovementInput> _inputHandle;
        ComponentTypeHandle<PathGridTraversalState> _stateHandle;
        ComponentTypeHandle<ActorDead> _deadHandle;
        BufferTypeHandle<PathGridTraversalNode> _pathNodeHandle;

        public void OnCreate(ref SystemState state)
        {
            _query = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadWrite<LocalTransform>(),
                    ComponentType.ReadWrite<MorrowindMovementInput>(),
                    ComponentType.ReadWrite<PathGridTraversalState>(),
                    ComponentType.ReadOnly<PathGridTraversalNode>(),
                    ComponentType.ReadOnly<ActorDead>(),
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });
            _transformHandle = state.GetComponentTypeHandle<LocalTransform>(isReadOnly: false);
            _inputHandle = state.GetComponentTypeHandle<MorrowindMovementInput>(isReadOnly: false);
            _stateHandle = state.GetComponentTypeHandle<PathGridTraversalState>(isReadOnly: false);
            _deadHandle = state.GetComponentTypeHandle<ActorDead>(isReadOnly: true);
            _pathNodeHandle = state.GetBufferTypeHandle<PathGridTraversalNode>(isReadOnly: true);
            state.RequireForUpdate<PathGridTraversalSettings>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var navigation = WorldResources.PathGridNavigation;
            if (!navigation.IsCreated)
                return;

            float dt = SystemAPI.Time.DeltaTime;
            _transformHandle.Update(ref state);
            _inputHandle.Update(ref state);
            _stateHandle.Update(ref state);
            _deadHandle.Update(ref state);
            _pathNodeHandle.Update(ref state);

            state.Dependency = new PathGridTraversalSteeringJob
            {
                Navigation = navigation,
                Settings = SystemAPI.GetSingleton<PathGridTraversalSettings>(),
                DeltaTime = dt,
                TransformHandle = _transformHandle,
                InputHandle = _inputHandle,
                StateHandle = _stateHandle,
                DeadHandle = _deadHandle,
                PathNodeHandle = _pathNodeHandle,
            }.ScheduleParallel(_query, state.Dependency);
        }

        [BurstCompile]
        struct PathGridTraversalSteeringJob : IJobChunk
        {
            [ReadOnly] public PathGridNavigationWorld Navigation;
            public PathGridTraversalSettings Settings;
            public float DeltaTime;
            public ComponentTypeHandle<LocalTransform> TransformHandle;
            public ComponentTypeHandle<MorrowindMovementInput> InputHandle;
            public ComponentTypeHandle<PathGridTraversalState> StateHandle;
            [ReadOnly] public ComponentTypeHandle<ActorDead> DeadHandle;
            [ReadOnly] public BufferTypeHandle<PathGridTraversalNode> PathNodeHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                var transforms = chunk.GetNativeArray(ref TransformHandle);
                var inputs = chunk.GetNativeArray(ref InputHandle);
                var states = chunk.GetNativeArray(ref StateHandle);
                var pathNodes = chunk.GetBufferAccessor(ref PathNodeHandle);

                int count = chunk.Count;
                for (int i = 0; i < count; i++)
                {
                    var transform = transforms[i];
                    var input = inputs[i];
                    var traversalState = states[i];
                    var nodes = pathNodes[i];

                    if (chunk.IsComponentEnabled(ref DeadHandle, i))
                    {
                        input.LocalMove = float2.zero;
                        input.RunHeld = false;
                        input.SneakHeld = false;
                        input.JumpPressed = false;
                        traversalState = default;
                        inputs[i] = input;
                        states[i] = traversalState;
                        continue;
                    }

                    if (traversalState.Status != (byte)PathGridTraversalStatus.Traversing)
                        continue;

                    if (AdvancePastReachedNodes(Navigation, transform.Position, nodes, Settings, ref traversalState))
                    {
                        input.LocalMove = float2.zero;
                        input.RunHeld = false;
                        inputs[i] = input;
                        states[i] = traversalState;
                        continue;
                    }

                    if (!TryResolveSteeringTarget(Navigation, nodes, traversalState, out float3 target))
                    {
                        traversalState.Status = (byte)PathGridTraversalStatus.Failed;
                        input.LocalMove = float2.zero;
                        inputs[i] = input;
                        states[i] = traversalState;
                        continue;
                    }

                    float3 delta = target - transform.Position;
                    delta.y = 0f;
                    float3 worldDirection = math.normalizesafe(delta);
                    if (math.lengthsq(worldDirection) <= 1e-5f)
                        continue;

                    if (Settings.FacePath != 0)
                    {
                        quaternion targetRotation = quaternion.LookRotationSafe(worldDirection, math.up());
                        float t = 1f - math.exp(-math.max(0f, Settings.FacingSharpness) * DeltaTime);
                        transform.Rotation = math.slerp(transform.Rotation, targetRotation, math.saturate(t));
                        input.LocalMove = new float2(0f, 1f);
                    }
                    else
                    {
                        float3 localDirection = math.mul(math.inverse(transform.Rotation), worldDirection);
                        input.LocalMove.x = math.clamp(localDirection.x, -1f, 1f);
                        input.LocalMove.y = math.clamp(localDirection.z, -1f, 1f);
                    }

                    input.RunHeld = traversalState.Run != 0;
                    input.SneakHeld = false;
                    input.JumpPressed = false;

                    transforms[i] = transform;
                    inputs[i] = input;
                    states[i] = traversalState;
                }
            }
        }

        static bool AdvancePastReachedNodes(
            PathGridNavigationWorld navigation,
            float3 position,
            DynamicBuffer<PathGridTraversalNode> pathNodes,
            in PathGridTraversalSettings settings,
            ref PathGridTraversalState state)
        {
            while ((uint)state.CurrentNodeOffset < (uint)pathNodes.Length)
            {
                int nodeIndex = pathNodes[state.CurrentNodeOffset].NodeIndex;
                if ((uint)nodeIndex >= (uint)navigation.Nodes.Length)
                {
                    state.Status = (byte)PathGridTraversalStatus.Failed;
                    return true;
                }

                float3 target = PathGridNavigationWorld.GetNodePosition(navigation.Nodes[nodeIndex]);
                float3 delta = target - position;
                delta.y = 0f;
                float arrivalDistance = state.CurrentNodeOffset == pathNodes.Length - 1
                    ? math.max(0.01f, settings.FinalArrivalDistance)
                    : math.max(0.01f, settings.NodeArrivalDistance);
                if (math.lengthsq(delta) > arrivalDistance * arrivalDistance)
                    return false;

                state.CurrentNodeOffset++;
            }

            if (state.UseFinalTargetPosition != 0)
            {
                float3 delta = state.FinalTargetPosition - position;
                delta.y = 0f;
                float arrivalDistance = math.max(0.01f, settings.FinalArrivalDistance);
                if (math.lengthsq(delta) > arrivalDistance * arrivalDistance)
                    return false;
            }

            state.Status = (byte)PathGridTraversalStatus.Reached;
            return true;
        }

        static bool TryResolveSteeringTarget(
            PathGridNavigationWorld navigation,
            DynamicBuffer<PathGridTraversalNode> pathNodes,
            in PathGridTraversalState state,
            out float3 target)
        {
            if ((uint)state.CurrentNodeOffset < (uint)pathNodes.Length)
            {
                int nodeIndex = pathNodes[state.CurrentNodeOffset].NodeIndex;
                if ((uint)nodeIndex >= (uint)navigation.Nodes.Length)
                {
                    target = default;
                    return false;
                }

                target = PathGridNavigationWorld.GetNodePosition(navigation.Nodes[nodeIndex]);
                return true;
            }

            if (state.UseFinalTargetPosition != 0)
            {
                target = state.FinalTargetPosition;
                return true;
            }

            target = default;
            return false;
        }
    }
}
