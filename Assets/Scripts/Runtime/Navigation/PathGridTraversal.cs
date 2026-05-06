using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using VVardenfell.Runtime.Bootstrap;
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
        public int MaxFineIterations;
        public int MaxAbstractIterations;
        public byte AllowPartial;
        public byte Run;
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
        public byte Status;
        public byte UsedAbstractRoute;
        public byte ReachedGoal;
        public byte Run;
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

            if (s_SettingsQueryCreated)
                s_SettingsQuery.Dispose();

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
    [UpdateAfter(typeof(PathGridPathfindingSystem))]
    public partial class PathGridTraversalRequestSystem : SystemBase
    {
        EntityQuery _query;
        EntityQuery _pathfindingQuery;

        protected override void OnCreate()
        {
            _query = SystemAPI.QueryBuilder()
                .WithAll<PathGridTraversalState, PathGridTraversalPendingRequest>()
                .WithPresent<PathGridTraversalAwaitingResult>()
                .Build();
            _pathfindingQuery = GetEntityQuery(
                ComponentType.ReadWrite<PathGridPathfindingState>(),
                ComponentType.ReadWrite<PendingPathGridPathRequest>());
        }

        protected override void OnUpdate()
        {
            if (_pathfindingQuery.IsEmptyIgnoreFilter)
                return;

            Entity pathfindingEntity = _pathfindingQuery.GetSingletonEntity();
            var pathfindingState = EntityManager.GetComponentData<PathGridPathfindingState>(pathfindingEntity);
            var pendingRequests = EntityManager.GetBuffer<PendingPathGridPathRequest>(pathfindingEntity);

            using var entities = _query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                var state = EntityManager.GetComponentData<PathGridTraversalState>(entity);
                var request = EntityManager.GetComponentData<PathGridTraversalPendingRequest>(entity);
                if (state.ActivePathRequestId > 0)
                {
                    SystemAPI.SetComponentEnabled<PathGridTraversalPendingRequest>(entity, false);
                    continue;
                }

                if (request.StartNodeIndex < 0 || request.GoalNodeIndex < 0)
                {
                    var failedState = state;
                    failedState.Status = (byte)PathGridTraversalStatus.Failed;
                    EntityManager.SetComponentData(entity, failedState);
                    SystemAPI.SetComponentEnabled<PathGridTraversalPendingRequest>(entity, false);
                    SystemAPI.SetComponentEnabled<PathGridTraversalAwaitingResult>(entity, false);
                    continue;
                }

                int requestId = pathfindingState.NextRequestId++;
                pathfindingState.TotalSubmitted++;
                pendingRequests.Add(new PendingPathGridPathRequest
                {
                    RequestId = requestId,
                    Owner = entity,
                    StartNodeIndex = request.StartNodeIndex,
                    GoalNodeIndex = request.GoalNodeIndex,
                    AllowPartial = request.AllowPartial,
                    MaxFineIterations = request.MaxFineIterations,
                    MaxAbstractIterations = request.MaxAbstractIterations,
                    RequestedAt = UnityEngine.Time.realtimeSinceStartupAsDouble,
                });

                var nextState = state;
                nextState.ActivePathRequestId = requestId;
                nextState.CurrentNodeOffset = 0;
                nextState.PathNodeCount = 0;
                nextState.LastStartNodeIndex = request.StartNodeIndex;
                nextState.LastGoalNodeIndex = request.GoalNodeIndex;
                nextState.Status = (byte)PathGridTraversalStatus.RequestingPath;
                nextState.Run = request.Run;
                EntityManager.SetComponentData(entity, nextState);
                SystemAPI.SetComponentEnabled<PathGridTraversalPendingRequest>(entity, false);
                SystemAPI.SetComponentEnabled<PathGridTraversalAwaitingResult>(entity, true);
            }

            EntityManager.SetComponentData(pathfindingEntity, pathfindingState);
        }
    }

    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(PathGridTraversalRequestSystem))]
    public partial class PathGridTraversalResultSystem : SystemBase
    {
        EntityQuery _query;
        EntityQuery _pathfindingQuery;

        protected override void OnCreate()
        {
            _query = SystemAPI.QueryBuilder()
                .WithAll<PathGridTraversalState, PathGridTraversalAwaitingResult, PathGridTraversalNode>()
                .Build();
            _pathfindingQuery = GetEntityQuery(
                ComponentType.ReadWrite<CompletedPathGridPath>(),
                ComponentType.ReadWrite<CompletedPathGridPathNode>());
        }

        protected override void OnUpdate()
        {
            if (_pathfindingQuery.IsEmptyIgnoreFilter)
                return;

            Entity pathfindingEntity = _pathfindingQuery.GetSingletonEntity();
            var completed = EntityManager.GetBuffer<CompletedPathGridPath>(pathfindingEntity);
            var completedNodes = EntityManager.GetBuffer<CompletedPathGridPathNode>(pathfindingEntity);
            if (completed.Length <= 0)
                return;

            using var completedLookup = new NativeParallelHashMap<int, int>(completed.Length, Allocator.Temp);
            for (int i = 0; i < completed.Length; i++)
                completedLookup.TryAdd(completed[i].RequestId, i);

            var consumedCompletedIndices = new NativeList<int>(Allocator.Temp);
            using var entities = _query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                var state = EntityManager.GetComponentData<PathGridTraversalState>(entity);
                if (state.Status != (byte)PathGridTraversalStatus.RequestingPath || state.ActivePathRequestId <= 0)
                    continue;

                if (!completedLookup.TryGetValue(state.ActivePathRequestId, out int completedIndex))
                    continue;

                var result = completed[completedIndex];
                var pathNodes = EntityManager.GetBuffer<PathGridTraversalNode>(entity);
                pathNodes.Clear();
                if (result.FirstNodeIndex >= 0 && result.NodeCount > 0)
                {
                    for (int nodeOffset = 0; nodeOffset < result.NodeCount; nodeOffset++)
                        pathNodes.Add(new PathGridTraversalNode { NodeIndex = completedNodes[result.FirstNodeIndex + nodeOffset].NodeIndex });
                }

                var nextState = state;
                nextState.ActivePathRequestId = 0;
                nextState.PathCost = result.Cost;
                nextState.PathNodeCount = result.NodeCount;
                nextState.UsedAbstractRoute = result.UsedAbstractRoute;
                nextState.ReachedGoal = result.ReachedGoal;
                if ((PathGridPathStatus)result.Status == PathGridPathStatus.Failed || result.NodeCount == 0)
                {
                    nextState.Status = (byte)PathGridTraversalStatus.Failed;
                    EntityManager.SetComponentData(entity, nextState);
                    SystemAPI.SetComponentEnabled<PathGridTraversalAwaitingResult>(entity, false);
                    consumedCompletedIndices.Add(completedIndex);
                    continue;
                }

                nextState.CurrentNodeOffset = result.NodeCount > 1 ? 1 : 0;
                nextState.Status = nextState.CurrentNodeOffset >= result.NodeCount
                    ? (byte)PathGridTraversalStatus.Reached
                    : (byte)PathGridTraversalStatus.Traversing;
                EntityManager.SetComponentData(entity, nextState);
                SystemAPI.SetComponentEnabled<PathGridTraversalAwaitingResult>(entity, false);
                consumedCompletedIndices.Add(completedIndex);
            }

            if (consumedCompletedIndices.Length > 1)
                consumedCompletedIndices.Sort();

            for (int i = consumedCompletedIndices.Length - 1; i >= 0; i--)
                PathGridPathfindingSystem.RemoveCompletedAt(completed, completedNodes, consumedCompletedIndices[i]);

            consumedCompletedIndices.Dispose();
        }
    }

    [UpdateInGroup(typeof(MorrowindPhysicsQuerySystemGroup), OrderFirst = true)]
    public partial struct PathGridTraversalSteeringSystem : ISystem
    {
        EntityQuery _query;
        ComponentTypeHandle<LocalTransform> _transformHandle;
        ComponentTypeHandle<MorrowindMovementInput> _inputHandle;
        ComponentTypeHandle<PathGridTraversalState> _stateHandle;
        BufferTypeHandle<PathGridTraversalNode> _pathNodeHandle;

        public void OnCreate(ref SystemState state)
        {
            _query = state.GetEntityQuery(
                ComponentType.ReadWrite<LocalTransform>(),
                ComponentType.ReadWrite<MorrowindMovementInput>(),
                ComponentType.ReadWrite<PathGridTraversalState>(),
                ComponentType.ReadOnly<PathGridTraversalNode>());
            _transformHandle = state.GetComponentTypeHandle<LocalTransform>(isReadOnly: false);
            _inputHandle = state.GetComponentTypeHandle<MorrowindMovementInput>(isReadOnly: false);
            _stateHandle = state.GetComponentTypeHandle<PathGridTraversalState>(isReadOnly: false);
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
            _pathNodeHandle.Update(ref state);

            state.Dependency = new PathGridTraversalSteeringJob
            {
                Navigation = navigation,
                Settings = SystemAPI.GetSingleton<PathGridTraversalSettings>(),
                DeltaTime = dt,
                TransformHandle = _transformHandle,
                InputHandle = _inputHandle,
                StateHandle = _stateHandle,
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
                    if (traversalState.Status != (byte)PathGridTraversalStatus.Traversing || nodes.Length == 0)
                        continue;

                    if (AdvancePastReachedNodes(Navigation, transform.Position, nodes, Settings, ref traversalState))
                    {
                        input.LocalMove = float2.zero;
                        input.RunHeld = false;
                        inputs[i] = input;
                        states[i] = traversalState;
                        continue;
                    }

                    int nodeIndex = nodes[traversalState.CurrentNodeOffset].NodeIndex;
                    if ((uint)nodeIndex >= (uint)Navigation.Nodes.Length)
                    {
                        traversalState.Status = (byte)PathGridTraversalStatus.Failed;
                        input.LocalMove = float2.zero;
                        inputs[i] = input;
                        states[i] = traversalState;
                        continue;
                    }

                    float3 target = PathGridNavigationWorld.GetNodePosition(Navigation.Nodes[nodeIndex]);
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
                    }

                    float3 localDirection = math.mul(math.inverse(transform.Rotation), worldDirection);
                    input.LocalMove.x = math.clamp(localDirection.x, -1f, 1f);
                    input.LocalMove.y = math.clamp(localDirection.z, -1f, 1f);
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

            state.Status = (byte)PathGridTraversalStatus.Reached;
            return true;
        }
    }
}
