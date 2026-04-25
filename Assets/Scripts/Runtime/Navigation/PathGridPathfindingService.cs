using System;
using System.Collections.Generic;
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
    public partial class PathGridPathfindingSystem : SystemBase
    {
        sealed class JobEntry : IDisposable
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

        sealed class ActiveBatch : IDisposable
        {
            public readonly List<JobEntry> Entries = new();
            public JobHandle CombinedHandle;

            public void Dispose()
            {
                for (int i = 0; i < Entries.Count; i++)
                    Entries[i].Dispose();
                Entries.Clear();
            }
        }

        readonly List<ActiveBatch> _activeBatches = new();
        readonly Queue<PathGridPathWorkingSet> _workingSetPool = new();
        readonly Queue<NativeList<int>> _outputPathPool = new();
        Entity _singleton;

        protected override void OnCreate()
        {
            _singleton = EntityManager.CreateEntity();
            EntityManager.SetName(_singleton, "VVardenfell.PathGridPathfinding");
            EntityManager.AddComponentData(_singleton, PathGridPathfindingState.Defaults);
            EntityManager.AddBuffer<PendingPathGridPathRequest>(_singleton);
            EntityManager.AddBuffer<CompletedPathGridPath>(_singleton);
            EntityManager.AddBuffer<CompletedPathGridPathNode>(_singleton);
            EntityManager.AddBuffer<CanceledPathGridPathRequest>(_singleton);
        }

        protected override void OnDestroy()
        {
            for (int i = 0; i < _activeBatches.Count; i++)
            {
                _activeBatches[i].CombinedHandle.Complete();
                _activeBatches[i].Dispose();
            }
            _activeBatches.Clear();

            while (_workingSetPool.Count > 0)
            {
                var workingSet = _workingSetPool.Dequeue();
                workingSet.Dispose();
            }

            while (_outputPathPool.Count > 0)
            {
                var outputPath = _outputPathPool.Dequeue();
                if (outputPath.IsCreated)
                    outputPath.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            if (_singleton == Entity.Null || !EntityManager.Exists(_singleton))
                return;

            double now = SystemAPI.Time.ElapsedTime;
            var state = EntityManager.GetComponentData<PathGridPathfindingState>(_singleton);
            var pending = EntityManager.GetBuffer<PendingPathGridPathRequest>(_singleton);
            var completed = EntityManager.GetBuffer<CompletedPathGridPath>(_singleton);
            var completedNodes = EntityManager.GetBuffer<CompletedPathGridPathNode>(_singleton);
            var canceled = EntityManager.GetBuffer<CanceledPathGridPathRequest>(_singleton);

            CompleteFinishedBatches(ref state, completed, completedNodes, canceled, now);
            RetireCompleted(completed, completedNodes, now, math.max(0f, state.CompletedRetentionSeconds), math.max(1, state.MaxCompletedResults));
            RetireStaleCanceledIds(canceled);

            if (pending.Length > 0 && _activeBatches.Count < math.max(1, state.MaxActiveBatches))
            {
                if (!WorldResources.PathGridNavigation.IsCreated)
                    FailPendingWithoutGraph(ref state, pending, completed, now);
                else
                    SchedulePendingBatch(ref state, pending);
            }

            state.ActiveBatchCount = _activeBatches.Count;
            EntityManager.SetComponentData(_singleton, state);
        }

        void CompleteFinishedBatches(
            ref PathGridPathfindingState state,
            DynamicBuffer<CompletedPathGridPath> completed,
            DynamicBuffer<CompletedPathGridPathNode> completedNodes,
            DynamicBuffer<CanceledPathGridPathRequest> canceled,
            double now)
        {
            for (int batchIndex = _activeBatches.Count - 1; batchIndex >= 0; batchIndex--)
            {
                var batch = _activeBatches[batchIndex];
                if (!batch.CombinedHandle.IsCompleted)
                    continue;

                batch.CombinedHandle.Complete();
                for (int i = 0; i < batch.Entries.Count; i++)
                {
                    var entry = batch.Entries[i];
                    if (IsCanceled(canceled, entry.RequestId))
                    {
                        state.TotalCanceled++;
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
                }

                RecycleCompletedBatch(batch);
                _activeBatches.RemoveAt(batchIndex);
            }
        }

        void SchedulePendingBatch(ref PathGridPathfindingState state, DynamicBuffer<PendingPathGridPathRequest> pending)
        {
            int count = math.min(math.max(1, state.BatchSize), pending.Length);
            var batch = new ActiveBatch();
            JobHandle combined = default;

            for (int i = 0; i < count; i++)
            {
                var pendingRequest = pending[0];
                pending.RemoveAt(0);

                var entry = new JobEntry
                {
                    RequestId = pendingRequest.RequestId,
                    Owner = pendingRequest.Owner,
                    WorkingSet = RentWorkingSet(),
                    OutputPath = RentOutputPath(),
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
                combined = i == 0
                    ? entry.Handle
                    : JobHandle.CombineDependencies(combined, entry.Handle);
                batch.Entries.Add(entry);
            }

            if (batch.Entries.Count > 0)
            {
                batch.CombinedHandle = combined;
                _activeBatches.Add(batch);
            }
            else
            {
                batch.Dispose();
            }
        }

        PathGridPathWorkingSet RentWorkingSet()
        {
            if (_workingSetPool.Count > 0)
            {
                var workingSet = _workingSetPool.Dequeue();
                workingSet.Clear();
                return workingSet;
            }

            return WorldResources.PathGridNavigation.CreateWorkingSet(Allocator.Persistent);
        }

        NativeList<int> RentOutputPath()
        {
            if (_outputPathPool.Count > 0)
            {
                var outputPath = _outputPathPool.Dequeue();
                outputPath.Clear();
                return outputPath;
            }

            return new NativeList<int>(Allocator.Persistent);
        }

        void RecycleCompletedBatch(ActiveBatch batch)
        {
            for (int i = 0; i < batch.Entries.Count; i++)
            {
                var entry = batch.Entries[i];
                entry.DisposeResult();

                if (entry.WorkingSet.NodeG.IsCreated)
                {
                    entry.WorkingSet.Clear();
                    _workingSetPool.Enqueue(entry.WorkingSet);
                    entry.WorkingSet = default;
                }

                if (entry.OutputPath.IsCreated)
                {
                    entry.OutputPath.Clear();
                    _outputPathPool.Enqueue(entry.OutputPath);
                    entry.OutputPath = default;
                }
            }

            batch.Entries.Clear();
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
            for (int batchIndex = 0; batchIndex < _activeBatches.Count; batchIndex++)
            {
                var batch = _activeBatches[batchIndex];
                for (int i = 0; i < batch.Entries.Count; i++)
                {
                    if (batch.Entries[i].RequestId == requestId)
                        return true;
                }
            }

            return false;
        }
    }

    public static class PathGridPathfindingRequestBridge
    {
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
            using var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PathGridPathfindingState>(),
                ComponentType.ReadWrite<PendingPathGridPathRequest>(),
                ComponentType.ReadWrite<CompletedPathGridPath>(),
                ComponentType.ReadWrite<CompletedPathGridPathNode>(),
                ComponentType.ReadWrite<CanceledPathGridPathRequest>());
            if (query.IsEmptyIgnoreFilter)
            {
                error = "Pathgrid pathfinding service is not ready.";
                return false;
            }

            pathfindingEntity = query.GetSingletonEntity();
            error = null;
            return true;
        }

    }
}
