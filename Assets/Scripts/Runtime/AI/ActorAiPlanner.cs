using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Pathfinding;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.AI
{
    public enum ActorAiRuntimePackageType : byte
    {
        Wander = 1,
        Travel = 2,
    }

    public enum ActorAiPlannerStatus : byte
    {
        Idle = 0,
        Waiting = 1,
        Traversing = 2,
        Complete = 3,
        Failed = 4,
    }

    public struct ActorAiState : IComponentData
    {
        public int CurrentPackageIndex;
        public int CurrentNodeIndex;
        public int GoalNodeIndex;
        public float3 HomePosition;
        public float WaitUntilTime;
        public uint RandomSeed;
        public byte Status;
    }

    public struct ActorAiNavigationAnchor : IComponentData
    {
        public int PathGridIndex;
        public int GridX;
        public int GridY;
        public uint InteriorCellHash;
        public byte IsResolved;
        public byte IsInterior;
    }

    public struct ActorAiPackageRuntime : IBufferElementData
    {
        public byte Type;
        public byte ShouldRepeat;
        public byte AllowPartial;
        public int SourcePackageIndex;
        public int TargetPathGridIndex;
        public float3 TargetPosition;
        public float WanderRadius;
        public float IdleSeconds;
    }

    public static class ActorAiRuntimeAuthoringUtility
    {
        public static bool HasSupportedPackage(RuntimeContentDatabase contentDb, ActorDefHandle actorHandle)
        {
            var packages = contentDb.GetActorAiPackages(actorHandle);
            for (int i = 0; i < packages.Length; i++)
            {
                var package = packages[i];
                if (package.Type == ActorAiPackageType.Travel || package.Type == ActorAiPackageType.Wander)
                    return true;
            }

            return false;
        }

        public static void HydratePackages(
            RuntimeContentDatabase contentDb,
            ActorDefHandle actorHandle,
            in ActorAiNavigationAnchor anchor,
            DynamicBuffer<ActorAiPackageRuntime> target)
        {
            var packages = contentDb.GetActorAiPackages(actorHandle);
            for (int i = 0; i < packages.Length; i++)
            {
                var package = packages[i];
                if (package.Type == ActorAiPackageType.Travel)
                {
                    target.Add(new ActorAiPackageRuntime
                    {
                        Type = (byte)ActorAiRuntimePackageType.Travel,
                        ShouldRepeat = package.ShouldRepeat,
                        SourcePackageIndex = i,
                        TargetPathGridIndex = ResolvePackagePathGrid(contentDb, package.CellName, anchor),
                        TargetPosition = ConvertMwPosition(package.X, package.Y, package.Z),
                        IdleSeconds = 0.5f,
                    });
                }
                else if (package.Type == ActorAiPackageType.Wander)
                {
                    target.Add(new ActorAiPackageRuntime
                    {
                        Type = (byte)ActorAiRuntimePackageType.Wander,
                        ShouldRepeat = package.ShouldRepeat,
                        SourcePackageIndex = i,
                        TargetPathGridIndex = -1,
                        WanderRadius = math.max(0f, package.WanderDistance) * WorldScale.MwUnitsToMeters,
                        IdleSeconds = 1.5f,
                    });
                }
            }
        }

        static int ResolvePackagePathGrid(RuntimeContentDatabase contentDb, string cellName, in ActorAiNavigationAnchor anchor)
        {
            if (!string.IsNullOrWhiteSpace(cellName) &&
                contentDb.TryGetInteriorPathGridHandle(cellName, out var handle) &&
                handle.IsValid)
            {
                return handle.Index;
            }

            return anchor.IsResolved != 0 ? anchor.PathGridIndex : -1;
        }

        static float3 ConvertMwPosition(float x, float y, float z)
            => new float3(x, z, y) * WorldScale.MwUnitsToMeters;
    }

    [UpdateInGroup(typeof(MorrowindWorldMutationSystemGroup))]
    public partial class ActorAiNavigationAnchorSyncSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<ActorAiState>();
            RequireForUpdate<ActorAiNavigationAnchor>();
        }

        protected override void OnUpdate()
        {
            RuntimeContentDatabase contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
                return;

            foreach (var (anchor, cellLink) in SystemAPI.Query<RefRW<ActorAiNavigationAnchor>, RefRO<CellLink>>())
            {
                SyncExteriorAnchor(contentDb, cellLink.ValueRO.Value, anchor);
            }

            foreach (var (anchor, location) in SystemAPI.Query<RefRW<ActorAiNavigationAnchor>, RefRO<LogicalRefLocation>>().WithNone<CellLink>())
            {
                if (location.ValueRO.IsInterior != 0)
                {
                    SyncInteriorAnchor(contentDb, location.ValueRO.InteriorCellId, anchor);
                }
                else
                {
                    SyncExteriorAnchor(contentDb, location.ValueRO.ExteriorCell, anchor);
                }
            }
        }

        static void SyncExteriorAnchor(RuntimeContentDatabase contentDb, int2 coord, RefRW<ActorAiNavigationAnchor> anchor)
        {
            ref var current = ref anchor.ValueRW;
            if (current.IsInterior == 0 &&
                current.GridX == coord.x &&
                current.GridY == coord.y)
            {
                return;
            }

            var next = new ActorAiNavigationAnchor
            {
                PathGridIndex = -1,
                GridX = coord.x,
                GridY = coord.y,
                InteriorCellHash = 0u,
                IsInterior = 0,
            };
            if (contentDb.TryGetExteriorPathGridHandle(coord.x, coord.y, out var handle) && handle.IsValid)
            {
                next.PathGridIndex = handle.Index;
                next.IsResolved = 1;
            }

            current = next;
        }

        static void SyncInteriorAnchor(RuntimeContentDatabase contentDb, FixedString128Bytes interiorCellId, RefRW<ActorAiNavigationAnchor> anchor)
        {
            uint interiorCellHash = HashInteriorCellId(interiorCellId);
            ref var current = ref anchor.ValueRW;
            if (current.IsInterior != 0 && current.InteriorCellHash == interiorCellHash)
            {
                return;
            }

            var next = new ActorAiNavigationAnchor
            {
                PathGridIndex = -1,
                InteriorCellHash = interiorCellHash,
                IsInterior = 1,
            };
            if (contentDb.TryGetInteriorPathGridHandle(interiorCellId.ToString(), out var handle) && handle.IsValid)
            {
                next.PathGridIndex = handle.Index;
                next.IsResolved = 1;
            }

            current = next;
        }

        static uint HashInteriorCellId(FixedString128Bytes interiorCellId)
        {
            uint hash = 2166136261u;
            for (int i = 0; i < interiorCellId.Length; i++)
            {
                hash ^= interiorCellId[i];
                hash *= 16777619u;
            }

            return hash == 0u ? 1u : hash;
        }
    }

    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    public partial struct ActorAiPlannerSystem : ISystem
    {
        EntityQuery _query;
        ComponentTypeHandle<LocalTransform> _transformHandle;
        ComponentTypeHandle<ActorAiState> _aiStateHandle;
        ComponentTypeHandle<ActorAiNavigationAnchor> _anchorHandle;
        ComponentTypeHandle<PathGridTraversalState> _traversalStateHandle;
        ComponentTypeHandle<PathGridTraversalPendingRequest> _traversalPendingRequestHandle;
        ComponentTypeHandle<PathGridTraversalAwaitingResult> _traversalAwaitingResultHandle;
        BufferTypeHandle<ActorAiPackageRuntime> _packageHandle;

        public void OnCreate(ref SystemState state)
        {
            _query = state.GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadWrite<ActorAiState>(),
                    ComponentType.ReadOnly<ActorAiNavigationAnchor>(),
                    ComponentType.ReadWrite<PathGridTraversalState>(),
                    ComponentType.ReadWrite<PathGridTraversalPendingRequest>(),
                    ComponentType.ReadWrite<PathGridTraversalAwaitingResult>(),
                    ComponentType.ReadOnly<ActorAiPackageRuntime>(),
                },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });
            _transformHandle = state.GetComponentTypeHandle<LocalTransform>(isReadOnly: true);
            _aiStateHandle = state.GetComponentTypeHandle<ActorAiState>(isReadOnly: false);
            _anchorHandle = state.GetComponentTypeHandle<ActorAiNavigationAnchor>(isReadOnly: true);
            _traversalStateHandle = state.GetComponentTypeHandle<PathGridTraversalState>(isReadOnly: false);
            _traversalPendingRequestHandle = state.GetComponentTypeHandle<PathGridTraversalPendingRequest>(isReadOnly: false);
            _traversalAwaitingResultHandle = state.GetComponentTypeHandle<PathGridTraversalAwaitingResult>(isReadOnly: false);
            _packageHandle = state.GetBufferTypeHandle<ActorAiPackageRuntime>(isReadOnly: true);
        }

        public void OnUpdate(ref SystemState state)
        {
            var navigation = WorldResources.PathGridNavigation;
            if (!navigation.IsCreated)
                return;

            _transformHandle.Update(ref state);
            _aiStateHandle.Update(ref state);
            _anchorHandle.Update(ref state);
            _traversalStateHandle.Update(ref state);
            _traversalPendingRequestHandle.Update(ref state);
            _traversalAwaitingResultHandle.Update(ref state);
            _packageHandle.Update(ref state);

            state.Dependency = new ActorAiPlannerJob
            {
                Navigation = navigation,
                ElapsedTime = (float)SystemAPI.Time.ElapsedTime,
                TransformHandle = _transformHandle,
                AiStateHandle = _aiStateHandle,
                AnchorHandle = _anchorHandle,
                TraversalStateHandle = _traversalStateHandle,
                TraversalPendingRequestHandle = _traversalPendingRequestHandle,
                TraversalAwaitingResultHandle = _traversalAwaitingResultHandle,
                PackageHandle = _packageHandle,
            }.ScheduleParallel(_query, state.Dependency);
        }
    }

    [BurstCompile]
    public struct ActorAiPlannerJob : IJobChunk
    {
        [ReadOnly] public PathGridNavigationWorld Navigation;
        public float ElapsedTime;
        [ReadOnly] public ComponentTypeHandle<LocalTransform> TransformHandle;
        public ComponentTypeHandle<ActorAiState> AiStateHandle;
        [ReadOnly] public ComponentTypeHandle<ActorAiNavigationAnchor> AnchorHandle;
        public ComponentTypeHandle<PathGridTraversalState> TraversalStateHandle;
        public ComponentTypeHandle<PathGridTraversalPendingRequest> TraversalPendingRequestHandle;
        public ComponentTypeHandle<PathGridTraversalAwaitingResult> TraversalAwaitingResultHandle;
        [ReadOnly] public BufferTypeHandle<ActorAiPackageRuntime> PackageHandle;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in Unity.Burst.Intrinsics.v128 chunkEnabledMask)
        {
            var transforms = chunk.GetNativeArray(ref TransformHandle);
            var aiStates = chunk.GetNativeArray(ref AiStateHandle);
            var anchors = chunk.GetNativeArray(ref AnchorHandle);
            var traversalStates = chunk.GetNativeArray(ref TraversalStateHandle);
            var traversalPendingRequests = chunk.GetNativeArray(ref TraversalPendingRequestHandle);
            var packages = chunk.GetBufferAccessor(ref PackageHandle);

            int count = chunk.Count;
            for (int i = 0; i < count; i++)
            {
                var aiState = aiStates[i];
                var anchor = anchors[i];
                var traversalState = traversalStates[i];
                var traversalPendingRequest = traversalPendingRequests[i];
                bool hasPendingRequest = chunk.IsComponentEnabled(ref TraversalPendingRequestHandle, i);
                bool awaitingResult = chunk.IsComponentEnabled(ref TraversalAwaitingResultHandle, i);
                var packageBuffer = packages[i];

                PlanActor(
                    transforms[i].Position,
                    anchor,
                    packageBuffer,
                    ref hasPendingRequest,
                    awaitingResult,
                    ref aiState,
                    ref traversalState,
                    ref traversalPendingRequest);

                aiStates[i] = aiState;
                traversalStates[i] = traversalState;
                traversalPendingRequests[i] = traversalPendingRequest;
                chunk.SetComponentEnabled(ref TraversalPendingRequestHandle, i, hasPendingRequest);
            }
        }

        void PlanActor(
            float3 actorPosition,
            in ActorAiNavigationAnchor anchor,
            DynamicBuffer<ActorAiPackageRuntime> packages,
            ref bool hasPendingRequest,
            bool awaitingResult,
            ref ActorAiState aiState,
            ref PathGridTraversalState traversalState,
            ref PathGridTraversalPendingRequest traversalRequest)
        {
            if (packages.Length == 0)
            {
                aiState.Status = (byte)ActorAiPlannerStatus.Idle;
                return;
            }

            if (anchor.IsResolved == 0 || !IsValidPathGrid(anchor.PathGridIndex))
            {
                aiState.Status = (byte)ActorAiPlannerStatus.Idle;
                return;
            }

            if (hasPendingRequest ||
                awaitingResult ||
                traversalState.ActivePathRequestId > 0 ||
                traversalState.Status == (byte)PathGridTraversalStatus.RequestingPath ||
                traversalState.Status == (byte)PathGridTraversalStatus.Traversing)
            {
                aiState.Status = (byte)ActorAiPlannerStatus.Traversing;
                return;
            }

            if (traversalState.Status == (byte)PathGridTraversalStatus.Reached ||
                traversalState.Status == (byte)PathGridTraversalStatus.Failed)
            {
                FinishPackage(packages, ref aiState, ref traversalState);
            }

            if (aiState.Status == (byte)ActorAiPlannerStatus.Waiting && ElapsedTime < aiState.WaitUntilTime)
                return;

            if ((uint)aiState.CurrentPackageIndex >= (uint)packages.Length)
            {
                aiState.Status = (byte)ActorAiPlannerStatus.Complete;
                return;
            }

            var package = packages[aiState.CurrentPackageIndex];
            if (!TryResolveNearestNode(anchor.PathGridIndex, actorPosition, out int startNode))
            {
                aiState.Status = (byte)ActorAiPlannerStatus.Failed;
                return;
            }

            bool scheduled = package.Type == (byte)ActorAiRuntimePackageType.Travel
                ? TryScheduleTravel(package, startNode, ref aiState, ref traversalRequest)
                : TryScheduleWander(package, anchor.PathGridIndex, startNode, actorPosition, ref aiState, ref traversalRequest);

            if (scheduled)
            {
                aiState.CurrentNodeIndex = startNode;
                aiState.Status = (byte)ActorAiPlannerStatus.Traversing;
                hasPendingRequest = true;
            }
            else
            {
                FinishPackage(packages, ref aiState, ref traversalState);
            }
        }

        bool TryScheduleTravel(
            in ActorAiPackageRuntime package,
            int startNode,
            ref ActorAiState aiState,
            ref PathGridTraversalPendingRequest traversalRequest)
        {
            int pathGridIndex = package.TargetPathGridIndex >= 0 ? package.TargetPathGridIndex : Navigation.Nodes[startNode].PathGridIndex;
            if (!TryResolveNearestNode(pathGridIndex, package.TargetPosition, out int goalNode))
                return false;

            WriteTraversalRequest(startNode, goalNode, package.AllowPartial, ref traversalRequest);
            aiState.GoalNodeIndex = goalNode;
            return true;
        }

        bool TryScheduleWander(
            in ActorAiPackageRuntime package,
            int pathGridIndex,
            int startNode,
            float3 actorPosition,
            ref ActorAiState aiState,
            ref PathGridTraversalPendingRequest traversalRequest)
        {
            if (!TryChooseWanderNode(pathGridIndex, startNode, actorPosition, package.WanderRadius, ref aiState.RandomSeed, out int goalNode))
                return false;

            WriteTraversalRequest(startNode, goalNode, package.AllowPartial, ref traversalRequest);
            aiState.GoalNodeIndex = goalNode;
            return true;
        }

        void FinishPackage(
            DynamicBuffer<ActorAiPackageRuntime> packages,
            ref ActorAiState aiState,
            ref PathGridTraversalState traversalState)
        {
            traversalState.Status = (byte)PathGridTraversalStatus.Idle;
            if ((uint)aiState.CurrentPackageIndex >= (uint)packages.Length)
            {
                aiState.Status = (byte)ActorAiPlannerStatus.Complete;
                return;
            }

            var package = packages[aiState.CurrentPackageIndex];
            aiState.WaitUntilTime = ElapsedTime + math.max(0f, package.IdleSeconds);
            if (package.ShouldRepeat == 0)
                aiState.CurrentPackageIndex++;

            if ((uint)aiState.CurrentPackageIndex >= (uint)packages.Length)
                aiState.Status = (byte)ActorAiPlannerStatus.Complete;
            else
                aiState.Status = (byte)ActorAiPlannerStatus.Waiting;
        }

        void WriteTraversalRequest(int startNode, int goalNode, byte allowPartial, ref PathGridTraversalPendingRequest traversalRequest)
        {
            traversalRequest = new PathGridTraversalPendingRequest
            {
                StartNodeIndex = startNode,
                GoalNodeIndex = goalNode,
                AllowPartial = allowPartial,
                MaxFineIterations = 4096,
                MaxAbstractIterations = 4096,
            };
        }

        bool TryChooseWanderNode(
            int pathGridIndex,
            int startNode,
            float3 actorPosition,
            float radius,
            ref uint randomSeed,
            out int goalNode)
        {
            goalNode = -1;
            if (!IsValidPathGrid(pathGridIndex) || (uint)startNode >= (uint)Navigation.Nodes.Length)
                return false;

            var pathGrid = Navigation.PathGrids[pathGridIndex];
            if (pathGrid.NodeCount <= 1)
                return false;

            float searchRadius = math.max(0.25f, radius);
            float radiusSq = searchRadius * searchRadius;
            int startComponent = Navigation.Nodes[startNode].ComponentId;
            var random = new Random(randomSeed == 0u ? 1u : randomSeed);

            int attempts = math.min(32, pathGrid.NodeCount * 2);
            for (int i = 0; i < attempts; i++)
            {
                int candidate = pathGrid.FirstNodeIndex + random.NextInt(0, pathGrid.NodeCount);
                if (IsValidWanderCandidate(candidate, startNode, startComponent, actorPosition, radiusSq))
                {
                    goalNode = candidate;
                    randomSeed = random.state == 0u ? 1u : random.state;
                    return true;
                }
            }

            float bestDistanceSq = float.PositiveInfinity;
            for (int i = 0; i < pathGrid.NodeCount; i++)
            {
                int candidate = pathGrid.FirstNodeIndex + i;
                if (!IsValidWanderCandidate(candidate, startNode, startComponent, actorPosition, radiusSq))
                    continue;

                float distanceSq = math.lengthsq(FlatDelta(PathGridNavigationWorld.GetNodePosition(Navigation.Nodes[candidate]), actorPosition));
                if (distanceSq < bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
                    goalNode = candidate;
                }
            }

            randomSeed = random.state == 0u ? 1u : random.state;
            return goalNode >= 0;
        }

        bool IsValidWanderCandidate(int candidate, int startNode, int component, float3 actorPosition, float radiusSq)
        {
            if ((uint)candidate >= (uint)Navigation.Nodes.Length || candidate == startNode)
                return false;

            var node = Navigation.Nodes[candidate];
            if (node.ComponentId != component)
                return false;

            return math.lengthsq(FlatDelta(PathGridNavigationWorld.GetNodePosition(node), actorPosition)) <= radiusSq;
        }

        bool TryResolveNearestNode(int pathGridIndex, float3 worldPosition, out int nodeIndex)
        {
            nodeIndex = -1;
            if (!IsValidPathGrid(pathGridIndex))
                return false;

            var pathGrid = Navigation.PathGrids[pathGridIndex];
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < pathGrid.NodeCount; i++)
            {
                int candidate = pathGrid.FirstNodeIndex + i;
                if ((uint)candidate >= (uint)Navigation.Nodes.Length)
                    continue;

                float distance = math.lengthsq(PathGridNavigationWorld.GetNodePosition(Navigation.Nodes[candidate]) - worldPosition);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    nodeIndex = candidate;
                }
            }

            return nodeIndex >= 0;
        }

        bool IsValidPathGrid(int pathGridIndex)
            => Navigation.PathGrids.IsCreated && (uint)pathGridIndex < (uint)Navigation.PathGrids.Length;

        static float3 FlatDelta(float3 a, float3 b)
        {
            float3 delta = a - b;
            delta.y = 0f;
            return delta;
        }
    }
}
