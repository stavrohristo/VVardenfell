using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Pathfinding
{
    public struct PathGridPathfindingState : IComponentData
    {
        public int NextRequestId;
        public int BatchSize;
        public int MaxActiveBatches;
        public int DefaultMaxFineIterations;
        public int DefaultMaxAbstractIterations;
        public float CompletedRetentionSeconds;
        public int MaxCompletedResults;
        public int TotalSubmitted;
        public int TotalCompleted;
        public int TotalCanceled;
        public int ActiveBatchCount;

        public static PathGridPathfindingState Defaults => new PathGridPathfindingState
        {
            NextRequestId = 1,
            BatchSize = 8,
            MaxActiveBatches = 2,
            DefaultMaxFineIterations = 4096,
            DefaultMaxAbstractIterations = 4096,
            CompletedRetentionSeconds = 5f,
            MaxCompletedResults = 256,
        };
    }

    public struct PendingPathGridPathRequest : IBufferElementData
    {
        public int RequestId;
        public Entity Owner;
        public int StartNodeIndex;
        public int GoalNodeIndex;
        public int MaxFineIterations;
        public int MaxAbstractIterations;
        public byte AllowPartial;
        public double RequestedAt;
    }

    public struct CompletedPathGridPath : IBufferElementData
    {
        public int RequestId;
        public Entity Owner;
        public byte Status;
        public float Cost;
        public int FirstNodeIndex;
        public int NodeCount;
        public byte UsedAbstractRoute;
        public byte ReachedGoal;
        public double CompletedAt;
    }

    public struct CompletedPathGridPathNode : IBufferElementData
    {
        public int NodeIndex;
    }

    public struct CanceledPathGridPathRequest : IBufferElementData
    {
        public int RequestId;
    }

    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    public partial struct PathGridPathfindingSystem : ISystem
    {
        struct JobEntry : IDisposable
        {
            public int RequestId;
            public Entity Owner;
            public JobHandle Handle;
            public PathGridPathWorkingSet WorkingSet;
            public NativeList<int> OutputPath;
            public NativeArray<PathGridPathResult> Result;

            public void Dispose()
            {
                WorkingSet.Dispose();
                if (OutputPath.IsCreated)
                    OutputPath.Dispose();
                if (Result.IsCreated)
                    Result.Dispose();
            }

            public void DisposeResult()
            {
                if (Result.IsCreated)
                    Result.Dispose();
                Result = default;
            }
        }

        NativeList<JobEntry> _activeEntries;
        Entity _singleton;

        public void OnCreate(ref SystemState state)
        {
            _activeEntries = new NativeList<JobEntry>(Allocator.Persistent);
            _singleton = state.EntityManager.CreateEntity();
            state.EntityManager.SetName(_singleton, "VVardenfell.PathGridPathfinding");
            state.EntityManager.AddComponentData(_singleton, PathGridPathfindingState.Defaults);
            state.EntityManager.AddBuffer<PendingPathGridPathRequest>(_singleton);
            state.EntityManager.AddBuffer<CompletedPathGridPath>(_singleton);
            state.EntityManager.AddBuffer<CompletedPathGridPathNode>(_singleton);
            state.EntityManager.AddBuffer<CanceledPathGridPathRequest>(_singleton);
        }

        public void OnDestroy(ref SystemState state)
        {
            if (!_activeEntries.IsCreated)
                return;

            for (int i = 0; i < _activeEntries.Length; i++)
            {
                var entry = _activeEntries[i];
                entry.Handle.Complete();
                entry.Dispose();
            }

            _activeEntries.Dispose();
            _activeEntries = default;
        }

        public void OnUpdate(ref SystemState systemState)
        {
            if (_singleton == Entity.Null || !systemState.EntityManager.Exists(_singleton))
                return;

            double now = SystemAPI.Time.ElapsedTime;
            var state = systemState.EntityManager.GetComponentData<PathGridPathfindingState>(_singleton);
            var pending = systemState.EntityManager.GetBuffer<PendingPathGridPathRequest>(_singleton);
            var completed = systemState.EntityManager.GetBuffer<CompletedPathGridPath>(_singleton);
            var completedNodes = systemState.EntityManager.GetBuffer<CompletedPathGridPathNode>(_singleton);
            var canceled = systemState.EntityManager.GetBuffer<CanceledPathGridPathRequest>(_singleton);

            CompleteFinishedBatches(ref state, completed, completedNodes, canceled, now);
            RetireCompleted(completed, completedNodes, now, math.max(0f, state.CompletedRetentionSeconds), math.max(1, state.MaxCompletedResults));
            RetireStaleCanceledIds(canceled);

            int batchSize = math.max(1, state.BatchSize);
            int maxActiveEntries = batchSize * math.max(1, state.MaxActiveBatches);
            if (pending.Length > 0 && _activeEntries.Length < maxActiveEntries)
            {
                if (!WorldResources.PathGridNavigation.IsCreated)
                    FailPendingWithoutGraph(ref state, pending, completed, now);
                else
                    SchedulePendingBatch(ref state, pending, batchSize, maxActiveEntries);
            }

            state.ActiveBatchCount = (_activeEntries.Length + batchSize - 1) / batchSize;
            systemState.EntityManager.SetComponentData(_singleton, state);
        }

        void CompleteFinishedBatches(
            ref PathGridPathfindingState state,
            DynamicBuffer<CompletedPathGridPath> completed,
            DynamicBuffer<CompletedPathGridPathNode> completedNodes,
            DynamicBuffer<CanceledPathGridPathRequest> canceled,
            double now)
        {
            for (int entryIndex = _activeEntries.Length - 1; entryIndex >= 0; entryIndex--)
            {
                var entry = _activeEntries[entryIndex];
                if (!entry.Handle.IsCompleted)
                    continue;

                entry.Handle.Complete();
                if (IsCanceled(canceled, entry.RequestId))
                {
                    state.TotalCanceled++;
                    entry.Dispose();
                    _activeEntries.RemoveAtSwapBack(entryIndex);
                    continue;
                }

                var result = entry.Result.IsCreated && entry.Result.Length > 0
                    ? entry.Result[0]
                    : new PathGridPathResult { Status = PathGridPathStatus.Failed, Cost = float.PositiveInfinity };
                PublishResult(
                    completed,
                    completedNodes,
                    entry.RequestId,
                    entry.Owner,
                    result,
                    entry.OutputPath,
                    now);
                state.TotalCompleted++;
                entry.Dispose();
                _activeEntries.RemoveAtSwapBack(entryIndex);
            }
        }

        void SchedulePendingBatch(
            ref PathGridPathfindingState state,
            DynamicBuffer<PendingPathGridPathRequest> pending,
            int batchSize,
            int maxActiveEntries)
        {
            int capacity = math.max(0, maxActiveEntries - _activeEntries.Length);
            int count = math.min(math.min(batchSize, pending.Length), capacity);

            for (int i = 0; i < count; i++)
            {
                var pendingRequest = pending[0];
                pending.RemoveAt(0);

                var entry = new JobEntry
                {
                    RequestId = pendingRequest.RequestId,
                    Owner = pendingRequest.Owner,
                    WorkingSet = WorldResources.PathGridNavigation.CreateWorkingSet(Allocator.Persistent),
                    OutputPath = new NativeList<int>(Allocator.Persistent),
                    Result = new NativeArray<PathGridPathResult>(1, Allocator.Persistent),
                };

                var job = new PathGridPathfindingJob
                {
                    World = WorldResources.PathGridNavigation,
                    Request = new PathGridPathRequest
                    {
                        StartNodeIndex = pendingRequest.StartNodeIndex,
                        GoalNodeIndex = pendingRequest.GoalNodeIndex,
                        AllowPartial = pendingRequest.AllowPartial,
                        MaxFineIterations = pendingRequest.MaxFineIterations > 0 ? pendingRequest.MaxFineIterations : state.DefaultMaxFineIterations,
                        MaxAbstractIterations = pendingRequest.MaxAbstractIterations > 0 ? pendingRequest.MaxAbstractIterations : state.DefaultMaxAbstractIterations,
                    },
                    WorkingSet = entry.WorkingSet,
                    OutputPath = entry.OutputPath,
                    Result = entry.Result,
                    ResultIndex = 0,
                };

                entry.Handle = job.Schedule();
                _activeEntries.Add(entry);
            }
        }

        void FailPendingWithoutGraph(
            ref PathGridPathfindingState state,
            DynamicBuffer<PendingPathGridPathRequest> pending,
            DynamicBuffer<CompletedPathGridPath> completed,
            double now)
        {
            while (pending.Length > 0)
            {
                var request = pending[0];
                pending.RemoveAt(0);
                completed.Add(new CompletedPathGridPath
                {
                    RequestId = request.RequestId,
                    Owner = request.Owner,
                    Status = (byte)PathGridPathStatus.Failed,
                    Cost = float.PositiveInfinity,
                    FirstNodeIndex = -1,
                    NodeCount = 0,
                    CompletedAt = now,
                });
                state.TotalCompleted++;
            }
        }

        static void PublishResult(
            DynamicBuffer<CompletedPathGridPath> completed,
            DynamicBuffer<CompletedPathGridPathNode> completedNodes,
            int requestId,
            Entity owner,
            PathGridPathResult result,
            NativeList<int> outputPath,
            double now)
        {
            int firstNode = outputPath.IsCreated && outputPath.Length > 0 ? completedNodes.Length : -1;
            int nodeCount = outputPath.IsCreated ? outputPath.Length : 0;
            for (int i = 0; i < nodeCount; i++)
                completedNodes.Add(new CompletedPathGridPathNode { NodeIndex = outputPath[i] });

            completed.Add(new CompletedPathGridPath
            {
                RequestId = requestId,
                Owner = owner,
                Status = (byte)result.Status,
                Cost = result.Cost,
                FirstNodeIndex = firstNode,
                NodeCount = nodeCount,
                UsedAbstractRoute = result.UsedAbstractRoute,
                ReachedGoal = result.ReachedGoal,
                CompletedAt = now,
            });
        }

        static void RetireCompleted(
            DynamicBuffer<CompletedPathGridPath> completed,
            DynamicBuffer<CompletedPathGridPathNode> completedNodes,
            double now,
            float retentionSeconds,
            int maxCompletedResults)
        {
            if (retentionSeconds > 0f)
            {
                for (int i = completed.Length - 1; i >= 0; i--)
                {
                    if (now - completed[i].CompletedAt <= retentionSeconds)
                        continue;

                    RemoveCompletedAt(completed, completedNodes, i);
                }
            }

            while (completed.Length > maxCompletedResults)
                RemoveCompletedAt(completed, completedNodes, 0);
        }

        internal static void RemoveCompletedAt(
            DynamicBuffer<CompletedPathGridPath> completed,
            DynamicBuffer<CompletedPathGridPathNode> completedNodes,
            int completedIndex)
        {
            var header = completed[completedIndex];
            if (header.FirstNodeIndex >= 0 && header.NodeCount > 0)
            {
                for (int i = 0; i < header.NodeCount; i++)
                    completedNodes.RemoveAt(header.FirstNodeIndex);

                for (int i = 0; i < completed.Length; i++)
                {
                    if (i == completedIndex)
                        continue;

                    var other = completed[i];
                    if (other.FirstNodeIndex > header.FirstNodeIndex)
                    {
                        other.FirstNodeIndex -= header.NodeCount;
                        completed[i] = other;
                    }
                }
            }

            completed.RemoveAt(completedIndex);
        }

        static bool IsCanceled(DynamicBuffer<CanceledPathGridPathRequest> canceled, int requestId)
        {
            for (int i = 0; i < canceled.Length; i++)
            {
                if (canceled[i].RequestId == requestId)
                    return true;
            }

            return false;
        }

        void RetireStaleCanceledIds(DynamicBuffer<CanceledPathGridPathRequest> canceled)
        {
            for (int i = canceled.Length - 1; i >= 0; i--)
            {
                int requestId = canceled[i].RequestId;
                if (HasActiveRequest(requestId))
                    continue;

                canceled.RemoveAt(i);
            }
        }

        bool HasActiveRequest(int requestId)
        {
            for (int i = 0; i < _activeEntries.Length; i++)
            {
                if (_activeEntries[i].RequestId == requestId)
                    return true;
            }

            return false;
        }
    }

    public static class PathGridPathfindingRequestBridge
    {
        static World s_PathfindingQueryWorld;
        static EntityQuery s_PathfindingQuery;
        static bool s_PathfindingQueryCreated;

        public static bool TryRequestPath(
            int startNodeIndex,
            int goalNodeIndex,
            out int requestId,
            out string error,
            Entity owner = default,
            bool allowPartial = false,
            int maxFineIterations = 0,
            int maxAbstractIterations = 0)
        {
            requestId = 0;
            if (startNodeIndex < 0 || goalNodeIndex < 0)
            {
                error = "Pathgrid path request has invalid node indices.";
                return false;
            }

            if (!TryGetPathfindingEntity(out var entityManager, out Entity pathfindingEntity, out error))
                return false;

            var state = entityManager.GetComponentData<PathGridPathfindingState>(pathfindingEntity);
            requestId = state.NextRequestId++;
            state.TotalSubmitted++;
            entityManager.SetComponentData(pathfindingEntity, state);

            entityManager.GetBuffer<PendingPathGridPathRequest>(pathfindingEntity).Add(new PendingPathGridPathRequest
            {
                RequestId = requestId,
                Owner = owner,
                StartNodeIndex = startNodeIndex,
                GoalNodeIndex = goalNodeIndex,
                AllowPartial = allowPartial ? (byte)1 : (byte)0,
                MaxFineIterations = maxFineIterations,
                MaxAbstractIterations = maxAbstractIterations,
                RequestedAt = Time.realtimeSinceStartupAsDouble,
            });

            error = null;
            return true;
        }

        public static bool TryRequestExteriorPath(
            int2 startCell,
            float3 startWorldPosition,
            int2 goalCell,
            float3 goalWorldPosition,
            out int requestId,
            out string error,
            Entity owner = default,
            bool allowPartial = false,
            int maxFineIterations = 0,
            int maxAbstractIterations = 0)
        {
            requestId = 0;
            var navigation = WorldResources.PathGridNavigation;
            if (!navigation.IsCreated)
            {
                error = "Pathgrid navigation world is not ready.";
                return false;
            }

            if (!navigation.TryResolveNearestExteriorNode(startCell.x, startCell.y, startWorldPosition, out int startNode))
            {
                error = $"No exterior pathgrid node found for start cell ({startCell.x}, {startCell.y}).";
                return false;
            }

            if (!navigation.TryResolveNearestExteriorNode(goalCell.x, goalCell.y, goalWorldPosition, out int goalNode))
            {
                error = $"No exterior pathgrid node found for goal cell ({goalCell.x}, {goalCell.y}).";
                return false;
            }

            return TryRequestPath(
                startNode,
                goalNode,
                out requestId,
                out error,
                owner,
                allowPartial,
                maxFineIterations,
                maxAbstractIterations);
        }

        public static bool TryHasResult(int requestId, out bool hasResult, out string error)
        {
            hasResult = false;
            if (!TryGetPathfindingEntity(out var entityManager, out Entity pathfindingEntity, out error))
                return false;

            var completed = entityManager.GetBuffer<CompletedPathGridPath>(pathfindingEntity);
            for (int i = 0; i < completed.Length; i++)
            {
                if (completed[i].RequestId == requestId)
                {
                    hasResult = true;
                    break;
                }
            }

            error = null;
            return true;
        }

        public static bool TryConsumeResult(
            int requestId,
            out CompletedPathGridPath result,
            out int[] pathNodeIndices,
            out string error)
        {
            result = default;
            pathNodeIndices = Array.Empty<int>();
            if (!TryGetPathfindingEntity(out var entityManager, out Entity pathfindingEntity, out error))
                return false;

            var completed = entityManager.GetBuffer<CompletedPathGridPath>(pathfindingEntity);
            var completedNodes = entityManager.GetBuffer<CompletedPathGridPathNode>(pathfindingEntity);
            for (int i = 0; i < completed.Length; i++)
            {
                if (completed[i].RequestId != requestId)
                    continue;

                result = completed[i];
                if (result.FirstNodeIndex >= 0 && result.NodeCount > 0)
                {
                    pathNodeIndices = new int[result.NodeCount];
                    for (int nodeOffset = 0; nodeOffset < result.NodeCount; nodeOffset++)
                        pathNodeIndices[nodeOffset] = completedNodes[result.FirstNodeIndex + nodeOffset].NodeIndex;
                }

                PathGridPathfindingSystem.RemoveCompletedAt(completed, completedNodes, i);
                error = null;
                return true;
            }

            error = $"Pathgrid path result {requestId} is not complete.";
            return false;
        }

        public static bool TryCancelRequest(int requestId, out string error)
        {
            if (!TryGetPathfindingEntity(out var entityManager, out Entity pathfindingEntity, out error))
                return false;

            var pending = entityManager.GetBuffer<PendingPathGridPathRequest>(pathfindingEntity);
            for (int i = pending.Length - 1; i >= 0; i--)
            {
                if (pending[i].RequestId == requestId)
                {
                    pending.RemoveAt(i);
                    IncrementCanceled(entityManager, pathfindingEntity);
                    error = null;
                    return true;
                }
            }

            var canceled = entityManager.GetBuffer<CanceledPathGridPathRequest>(pathfindingEntity);
            for (int i = 0; i < canceled.Length; i++)
            {
                if (canceled[i].RequestId == requestId)
                {
                    error = null;
                    return true;
                }
            }

            canceled.Add(new CanceledPathGridPathRequest { RequestId = requestId });
            error = null;
            return true;
        }

        static void IncrementCanceled(EntityManager entityManager, Entity pathfindingEntity)
        {
            var state = entityManager.GetComponentData<PathGridPathfindingState>(pathfindingEntity);
            state.TotalCanceled++;
            entityManager.SetComponentData(pathfindingEntity, state);
        }

        static bool TryGetPathfindingEntity(out EntityManager entityManager, out Entity pathfindingEntity, out string error)
        {
            entityManager = default;
            pathfindingEntity = Entity.Null;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                error = "Default ECS world is not ready.";
                return false;
            }

            entityManager = world.EntityManager;
            EntityQuery query = GetPathfindingQuery(entityManager);
            if (query.IsEmptyIgnoreFilter)
            {
                error = "Pathgrid pathfinding service is not ready.";
                return false;
            }

            pathfindingEntity = query.GetSingletonEntity();
            error = null;
            return true;
        }

        static EntityQuery GetPathfindingQuery(EntityManager entityManager)
        {
            World world = entityManager.World;
            if (s_PathfindingQueryCreated && s_PathfindingQueryWorld == world)
                return s_PathfindingQuery;

            if (s_PathfindingQueryCreated)
                s_PathfindingQuery.Dispose();

            s_PathfindingQueryWorld = world;
            s_PathfindingQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PathGridPathfindingState>(),
                ComponentType.ReadWrite<PendingPathGridPathRequest>(),
                ComponentType.ReadWrite<CompletedPathGridPath>(),
                ComponentType.ReadWrite<CompletedPathGridPathNode>(),
                ComponentType.ReadWrite<CanceledPathGridPathRequest>());
            s_PathfindingQueryCreated = true;
            return s_PathfindingQuery;
        }

    }
}
