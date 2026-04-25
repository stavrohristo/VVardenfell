using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
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

    public struct PathGridTraversalRequest : IComponentData
    {
        public int StartNodeIndex;
        public int GoalNodeIndex;
        public int MaxFineIterations;
        public int MaxAbstractIterations;
        public byte AllowPartial;
        public byte Pending;
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
    }

    public struct PathGridTraversalNode : IBufferElementData
    {
        public int NodeIndex;
    }

    public static class PathGridTraversalBridge
    {
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
            var settings = entityManager.GetComponentData<PathGridTraversalSettings>(owner);
            settings.AllowPartial = allowPartial ? (byte)1 : (byte)0;
            settings.Run = run ? (byte)1 : (byte)0;
            if (maxFineIterations > 0)
                settings.MaxFineIterations = maxFineIterations;
            if (maxAbstractIterations > 0)
                settings.MaxAbstractIterations = maxAbstractIterations;
            entityManager.SetComponentData(owner, settings);

            entityManager.SetComponentData(owner, new PathGridTraversalRequest
            {
                StartNodeIndex = startNodeIndex,
                GoalNodeIndex = goalNodeIndex,
                MaxFineIterations = maxFineIterations,
                MaxAbstractIterations = maxAbstractIterations,
                AllowPartial = allowPartial ? (byte)1 : (byte)0,
                Pending = 1,
            });

            var state = entityManager.GetComponentData<PathGridTraversalState>(owner);
            if (state.ActivePathRequestId > 0)
                PathGridPathfindingRequestBridge.TryCancelRequest(state.ActivePathRequestId, out _);
            state = new PathGridTraversalState
            {
                Status = (byte)PathGridTraversalStatus.Idle,
                LastStartNodeIndex = startNodeIndex,
                LastGoalNodeIndex = goalNodeIndex,
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
            if (entityManager.HasComponent<PathGridTraversalRequest>(owner))
                entityManager.SetComponentData(owner, new PathGridTraversalRequest());
            if (entityManager.HasComponent<PathGridTraversalNode>(owner))
                entityManager.GetBuffer<PathGridTraversalNode>(owner).Clear();

            error = null;
            return true;
        }

        static void EnsureTraversalComponents(EntityManager entityManager, Entity owner)
        {
            if (!entityManager.HasComponent<PathGridTraversalSettings>(owner))
                entityManager.AddComponentData(owner, PathGridTraversalSettings.Defaults);
            if (!entityManager.HasComponent<PathGridTraversalState>(owner))
                entityManager.AddComponentData(owner, new PathGridTraversalState());
            if (!entityManager.HasBuffer<PathGridTraversalNode>(owner))
                entityManager.AddBuffer<PathGridTraversalNode>(owner);
            if (!entityManager.HasComponent<PathGridTraversalRequest>(owner))
                entityManager.AddComponentData(owner, new PathGridTraversalRequest());
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

    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(PathGridPathfindingSystem))]
    public partial class PathGridTraversalRequestSystem : SystemBase
    {
        EntityQuery _query;

        protected override void OnCreate()
        {
            _query = GetEntityQuery(
                ComponentType.ReadWrite<PathGridTraversalState>(),
                ComponentType.ReadWrite<PathGridTraversalRequest>());
        }

        protected override void OnUpdate()
        {
            using var entities = _query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                var state = EntityManager.GetComponentData<PathGridTraversalState>(entity);
                var request = EntityManager.GetComponentData<PathGridTraversalRequest>(entity);
                if (request.Pending == 0 || state.ActivePathRequestId > 0)
                    continue;

                if (!PathGridPathfindingRequestBridge.TryRequestPath(
                        request.StartNodeIndex,
                        request.GoalNodeIndex,
                        out int requestId,
                        out _,
                        entity,
                        request.AllowPartial != 0,
                        request.MaxFineIterations,
                        request.MaxAbstractIterations))
                {
                    state.Status = (byte)PathGridTraversalStatus.Failed;
                    request.Pending = 0;
                    EntityManager.SetComponentData(entity, state);
                    EntityManager.SetComponentData(entity, request);
                    continue;
                }

                state.ActivePathRequestId = requestId;
                state.CurrentNodeOffset = 0;
                state.PathNodeCount = 0;
                state.LastStartNodeIndex = request.StartNodeIndex;
                state.LastGoalNodeIndex = request.GoalNodeIndex;
                state.Status = (byte)PathGridTraversalStatus.RequestingPath;
                request.Pending = 0;
                EntityManager.SetComponentData(entity, state);
                EntityManager.SetComponentData(entity, request);
            }
        }
    }

    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(PathGridTraversalRequestSystem))]
    public partial class PathGridTraversalResultSystem : SystemBase
    {
        EntityQuery _query;

        protected override void OnCreate()
        {
            _query = GetEntityQuery(
                ComponentType.ReadWrite<PathGridTraversalState>(),
                ComponentType.ReadWrite<PathGridTraversalNode>());
        }

        protected override void OnUpdate()
        {
            using var entities = _query.ToEntityArray(Allocator.Temp);
            for (int entityIndex = 0; entityIndex < entities.Length; entityIndex++)
            {
                Entity entity = entities[entityIndex];
                var state = EntityManager.GetComponentData<PathGridTraversalState>(entity);
                if (state.Status != (byte)PathGridTraversalStatus.RequestingPath || state.ActivePathRequestId <= 0)
                    continue;

                if (!PathGridPathfindingRequestBridge.TryHasResult(state.ActivePathRequestId, out bool hasResult, out _) || !hasResult)
                    continue;

                if (!PathGridPathfindingRequestBridge.TryConsumeResult(
                        state.ActivePathRequestId,
                        out var result,
                        out var nodeIndices,
                        out _))
                {
                    state.Status = (byte)PathGridTraversalStatus.Failed;
                    state.ActivePathRequestId = 0;
                    EntityManager.SetComponentData(entity, state);
                    continue;
                }

                var pathNodes = EntityManager.GetBuffer<PathGridTraversalNode>(entity);
                pathNodes.Clear();
                for (int i = 0; i < nodeIndices.Length; i++)
                    pathNodes.Add(new PathGridTraversalNode { NodeIndex = nodeIndices[i] });

                state.ActivePathRequestId = 0;
                state.PathCost = result.Cost;
                state.PathNodeCount = nodeIndices.Length;
                state.UsedAbstractRoute = result.UsedAbstractRoute;
                state.ReachedGoal = result.ReachedGoal;
                if ((PathGridPathStatus)result.Status == PathGridPathStatus.Failed || nodeIndices.Length == 0)
                {
                    state.Status = (byte)PathGridTraversalStatus.Failed;
                    EntityManager.SetComponentData(entity, state);
                    if (EntityManager.HasComponent<PathGridTraversalRequest>(entity))
                        EntityManager.SetComponentData(entity, new PathGridTraversalRequest());
                    continue;
                }

                state.CurrentNodeOffset = nodeIndices.Length > 1 ? 1 : 0;
                state.Status = state.CurrentNodeOffset >= nodeIndices.Length
                    ? (byte)PathGridTraversalStatus.Reached
                    : (byte)PathGridTraversalStatus.Traversing;
                EntityManager.SetComponentData(entity, state);
                if (EntityManager.HasComponent<PathGridTraversalRequest>(entity))
                    EntityManager.SetComponentData(entity, new PathGridTraversalRequest());
            }
        }
    }

    [UpdateInGroup(typeof(MorrowindPhysicsQuerySystemGroup), OrderFirst = true)]
    public partial struct PathGridTraversalSteeringSystem : ISystem
    {
        EntityQuery _query;
        ComponentTypeHandle<LocalTransform> _transformHandle;
        ComponentTypeHandle<MorrowindMovementIntent> _intentHandle;
        ComponentTypeHandle<PathGridTraversalState> _stateHandle;
        ComponentTypeHandle<PathGridTraversalSettings> _settingsHandle;
        BufferTypeHandle<PathGridTraversalNode> _pathNodeHandle;

        public void OnCreate(ref SystemState state)
        {
            _query = state.GetEntityQuery(
                ComponentType.ReadWrite<LocalTransform>(),
                ComponentType.ReadWrite<MorrowindMovementIntent>(),
                ComponentType.ReadWrite<PathGridTraversalState>(),
                ComponentType.ReadOnly<PathGridTraversalSettings>(),
                ComponentType.ReadOnly<PathGridTraversalNode>());
            _transformHandle = state.GetComponentTypeHandle<LocalTransform>(isReadOnly: false);
            _intentHandle = state.GetComponentTypeHandle<MorrowindMovementIntent>(isReadOnly: false);
            _stateHandle = state.GetComponentTypeHandle<PathGridTraversalState>(isReadOnly: false);
            _settingsHandle = state.GetComponentTypeHandle<PathGridTraversalSettings>(isReadOnly: true);
            _pathNodeHandle = state.GetBufferTypeHandle<PathGridTraversalNode>(isReadOnly: true);
        }

        public void OnUpdate(ref SystemState state)
        {
            var navigation = WorldResources.PathGridNavigation;
            if (!navigation.IsCreated)
                return;

            float dt = SystemAPI.Time.DeltaTime;
            _transformHandle.Update(ref state);
            _intentHandle.Update(ref state);
            _stateHandle.Update(ref state);
            _settingsHandle.Update(ref state);
            _pathNodeHandle.Update(ref state);

            state.Dependency = new PathGridTraversalSteeringJob
            {
                Navigation = navigation,
                DeltaTime = dt,
                TransformHandle = _transformHandle,
                IntentHandle = _intentHandle,
                StateHandle = _stateHandle,
                SettingsHandle = _settingsHandle,
                PathNodeHandle = _pathNodeHandle,
            }.ScheduleParallel(_query, state.Dependency);
        }

        [BurstCompile]
        struct PathGridTraversalSteeringJob : IJobChunk
        {
            [ReadOnly] public PathGridNavigationWorld Navigation;
            public float DeltaTime;
            public ComponentTypeHandle<LocalTransform> TransformHandle;
            public ComponentTypeHandle<MorrowindMovementIntent> IntentHandle;
            public ComponentTypeHandle<PathGridTraversalState> StateHandle;
            [ReadOnly] public ComponentTypeHandle<PathGridTraversalSettings> SettingsHandle;
            [ReadOnly] public BufferTypeHandle<PathGridTraversalNode> PathNodeHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
            {
                var transforms = chunk.GetNativeArray(ref TransformHandle);
                var intents = chunk.GetNativeArray(ref IntentHandle);
                var states = chunk.GetNativeArray(ref StateHandle);
                var settings = chunk.GetNativeArray(ref SettingsHandle);
                var pathNodes = chunk.GetBufferAccessor(ref PathNodeHandle);

                int count = chunk.Count;
                for (int i = 0; i < count; i++)
                {
                    var transform = transforms[i];
                    var intent = intents[i];
                    var traversalState = states[i];
                    var traversalSettings = settings[i];
                    var nodes = pathNodes[i];
                    if (traversalState.Status != (byte)PathGridTraversalStatus.Traversing || nodes.Length == 0)
                        continue;

                    if (AdvancePastReachedNodes(Navigation, transform.Position, nodes, traversalSettings, ref traversalState))
                    {
                        intent.LocalMove.x = 0f;
                        intent.LocalMove.y = 0f;
                        intent.RunHeld = false;
                        intents[i] = intent;
                        states[i] = traversalState;
                        continue;
                    }

                    int nodeIndex = nodes[traversalState.CurrentNodeOffset].NodeIndex;
                    if ((uint)nodeIndex >= (uint)Navigation.Nodes.Length)
                    {
                        traversalState.Status = (byte)PathGridTraversalStatus.Failed;
                        intent.LocalMove.x = 0f;
                        intent.LocalMove.y = 0f;
                        intents[i] = intent;
                        states[i] = traversalState;
                        continue;
                    }

                    float3 target = PathGridNavigationWorld.GetNodePosition(Navigation.Nodes[nodeIndex]);
                    float3 delta = target - transform.Position;
                    delta.y = 0f;
                    float3 worldDirection = math.normalizesafe(delta);
                    if (math.lengthsq(worldDirection) <= 1e-5f)
                        continue;

                    if (traversalSettings.FacePath != 0)
                    {
                        quaternion targetRotation = quaternion.LookRotationSafe(worldDirection, math.up());
                        float t = 1f - math.exp(-math.max(0f, traversalSettings.FacingSharpness) * DeltaTime);
                        transform.Rotation = math.slerp(transform.Rotation, targetRotation, math.saturate(t));
                    }

                    float3 localDirection = math.mul(math.inverse(transform.Rotation), worldDirection);
                    intent.LocalMove.x = math.clamp(localDirection.x, -1f, 1f);
                    intent.LocalMove.y = math.clamp(localDirection.z, -1f, 1f);
                    intent.RunHeld = traversalSettings.Run != 0;
                    intent.SneakHeld = false;
                    intent.JumpHeld = false;

                    transforms[i] = transform;
                    intents[i] = intent;
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
