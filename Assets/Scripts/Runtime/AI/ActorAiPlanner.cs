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
        Wander = 0,
        Travel = 1,
        Escort = 2,
        Follow = 3,
        Activate = 4,
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
        public float LastPackageActionTime;
        public uint RandomSeed;
        public byte Status;
        public byte FollowActive;
        public byte PendingIdleGroup;
        public ulong ActiveIdleGroupHash;
    }

    public struct ActorAiNavigationAnchor : IComponentData
    {
        public int PathGridIndex;
        public int GridX;
        public int GridY;
        public ulong InteriorCellHash;
        public byte IsResolved;
        public byte IsInterior;
    }

    public struct ActorAiPackageRuntime : IBufferElementData
    {
        public Entity FollowTargetEntity;
        public byte Type;
        public byte ShouldRepeat;
        public byte AllowPartial;
        public int SourcePackageIndex;
        public int TargetPathGridIndex;
        public float3 TargetPosition;
        public float WanderRadius;
        public float IdleSeconds;
        public float DurationHours;
        public float RemainingDurationHours;
        public float FollowDistance;
        public ulong DestinationInteriorCellHash;
        public uint FollowTargetPlacedRefId;
        public byte IdleChance0;
        public byte IdleChance1;
        public byte IdleChance2;
        public byte IdleChance3;
        public byte IdleChance4;
        public byte IdleChance5;
        public byte IdleChance6;
        public byte IdleChance7;
        public FixedString128Bytes TargetId;
    }

    public static class ActorAiRuntimeAuthoringUtility
    {
        public static bool HasPackage(RuntimeContentDatabase contentDb, ActorDefHandle actorHandle)
        {
            return contentDb.GetActorAiPackages(actorHandle).Length > 0;
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
                        DurationHours = math.max(0, package.Duration),
                        RemainingDurationHours = math.max(0, package.Duration),
                        IdleChance0 = package.Idle0,
                        IdleChance1 = package.Idle1,
                        IdleChance2 = package.Idle2,
                        IdleChance3 = package.Idle3,
                        IdleChance4 = package.Idle4,
                        IdleChance5 = package.Idle5,
                        IdleChance6 = package.Idle6,
                        IdleChance7 = package.Idle7,
                    });
                }
                else if (package.Type == ActorAiPackageType.Follow)
                {
                    target.Add(new ActorAiPackageRuntime
                    {
                        Type = (byte)ActorAiRuntimePackageType.Follow,
                        ShouldRepeat = package.ShouldRepeat,
                        AllowPartial = 1,
                        SourcePackageIndex = i,
                        TargetPathGridIndex = ResolvePackagePathGrid(contentDb, package.CellName, anchor),
                        TargetPosition = ConvertMwPosition(package.X, package.Y, package.Z),
                        FollowDistance = 256f * WorldScale.MwUnitsToMeters,
                        IdleSeconds = 0.5f,
                        DurationHours = math.max(0, package.Duration),
                        RemainingDurationHours = math.max(0, package.Duration),
                        DestinationInteriorCellHash = ResolveInteriorCellHash(package.CellName),
                        TargetId = RuntimeFixedStringUtility.ToFixed128OrDefault(package.TargetId),
                    });
                }
                else if (package.Type == ActorAiPackageType.Escort)
                {
                    target.Add(new ActorAiPackageRuntime
                    {
                        Type = (byte)ActorAiRuntimePackageType.Escort,
                        ShouldRepeat = package.ShouldRepeat,
                        AllowPartial = 1,
                        SourcePackageIndex = i,
                        TargetPathGridIndex = ResolvePackagePathGrid(contentDb, package.CellName, anchor),
                        TargetPosition = ConvertMwPosition(package.X, package.Y, package.Z),
                        FollowDistance = 450f * WorldScale.MwUnitsToMeters,
                        IdleSeconds = 0.5f,
                        DurationHours = math.max(0, package.Duration),
                        RemainingDurationHours = math.max(0, package.Duration),
                        DestinationInteriorCellHash = ResolveInteriorCellHash(package.CellName),
                        TargetId = RuntimeFixedStringUtility.ToFixed128OrDefault(package.TargetId),
                    });
                }
                else if (package.Type == ActorAiPackageType.Activate)
                {
                    target.Add(new ActorAiPackageRuntime
                    {
                        Type = (byte)ActorAiRuntimePackageType.Activate,
                        ShouldRepeat = package.ShouldRepeat,
                        AllowPartial = 1,
                        SourcePackageIndex = i,
                        TargetPathGridIndex = anchor.IsResolved != 0 ? anchor.PathGridIndex : -1,
                        FollowDistance = 128f * WorldScale.MwUnitsToMeters,
                        IdleSeconds = 0.25f,
                        TargetId = RuntimeFixedStringUtility.ToFixed128OrDefault(package.TargetId),
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

        static ulong ResolveInteriorCellHash(string cellName)
        {
            if (string.IsNullOrWhiteSpace(cellName))
                return 0UL;

            return InteriorCellIdHash.Hash(cellName);
        }
    }

    [UpdateInGroup(typeof(MorrowindWorldMutationSystemGroup))]
    public partial class ActorAiNavigationAnchorSyncSystem : SystemBase
    {
        EntityQuery _dirtyQuery;

        protected override void OnCreate()
        {
            _dirtyQuery = GetEntityQuery(
                ComponentType.ReadWrite<ActorAiNavigationAnchor>(),
                ComponentType.ReadOnly<ActorAiNavigationAnchorDirty>(),
                ComponentType.ReadOnly<ActorAiState>());
            RequireForUpdate(_dirtyQuery);
        }

        protected override void OnUpdate()
        {
            RuntimeContentDatabase contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
                return;

            foreach (var (anchor, entity) in SystemAPI.Query<RefRW<ActorAiNavigationAnchor>>()
                         .WithAll<ActorAiState, ActorAiNavigationAnchorDirty>()
                         .WithEntityAccess())
            {
                if (EntityManager.HasComponent<CellLink>(entity))
                {
                    var cellLink = EntityManager.GetComponentData<CellLink>(entity);
                    SyncExteriorAnchor(contentDb, cellLink.Value, anchor);
                    EntityManager.SetComponentEnabled<ActorAiNavigationAnchorDirty>(entity, false);
                    continue;
                }

                if (!EntityManager.HasComponent<LogicalRefLocation>(entity))
                    throw new System.InvalidOperationException($"[VVardenfell][AI] actor entity={entity.Index}:{entity.Version} has a dirty navigation anchor but no CellLink or LogicalRefLocation.");

                var location = EntityManager.GetComponentData<LogicalRefLocation>(entity);
                if (location.IsInterior != 0)
                {
                    SyncInteriorAnchor(contentDb, location.InteriorCellHash, anchor);
                }
                else
                {
                    SyncExteriorAnchor(contentDb, location.ExteriorCell, anchor);
                }

                EntityManager.SetComponentEnabled<ActorAiNavigationAnchorDirty>(entity, false);
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
                InteriorCellHash = 0UL,
                IsInterior = 0,
            };
            if (contentDb.TryGetExteriorPathGridHandle(coord.x, coord.y, out var handle) && handle.IsValid)
            {
                next.PathGridIndex = handle.Index;
                next.IsResolved = 1;
            }

            current = next;
        }

        static void SyncInteriorAnchor(RuntimeContentDatabase contentDb, ulong interiorCellHash, RefRW<ActorAiNavigationAnchor> anchor)
        {
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
            if (contentDb.TryGetInteriorPathGridHandle(interiorCellHash, out var handle) && handle.IsValid)
            {
                next.PathGridIndex = handle.Index;
                next.IsResolved = 1;
            }

            current = next;
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
        ComponentLookup<LocalTransform> _targetTransformLookup;

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
            _packageHandle = state.GetBufferTypeHandle<ActorAiPackageRuntime>(isReadOnly: false);
            _targetTransformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: true);
            state.RequireForUpdate<MorrowindTimeState>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var navigation = WorldResources.PathGridNavigation;
            if (!navigation.IsCreated)
                return;

            var contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
                throw new System.InvalidOperationException("[VVardenfell][AI] Missing GMST fIdleChanceMultiplier for AiWander.");

            float idleChanceMultiplier = contentDb.RequireGameSettingFloat("fIdleChanceMultiplier");
            var time = SystemAPI.GetSingleton<MorrowindTimeState>();

            _transformHandle.Update(ref state);
            _aiStateHandle.Update(ref state);
            _anchorHandle.Update(ref state);
            _traversalStateHandle.Update(ref state);
            _traversalPendingRequestHandle.Update(ref state);
            _traversalAwaitingResultHandle.Update(ref state);
            _packageHandle.Update(ref state);
            _targetTransformLookup.Update(ref state);

            state.Dependency = new ActorAiPlannerJob
            {
                Navigation = navigation,
                ElapsedTime = (float)SystemAPI.Time.ElapsedTime,
                DeltaGameHours = math.max(0f, SystemAPI.Time.DeltaTime) * math.max(0f, time.TimeScale) / 3600f,
                IdleChanceMultiplier = math.saturate(idleChanceMultiplier),
                TargetTransforms = _targetTransformLookup,
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
        [ReadOnly] public ComponentLookup<LocalTransform> TargetTransforms;
        public float ElapsedTime;
        [ReadOnly] public ComponentTypeHandle<LocalTransform> TransformHandle;
        public ComponentTypeHandle<ActorAiState> AiStateHandle;
        [ReadOnly] public ComponentTypeHandle<ActorAiNavigationAnchor> AnchorHandle;
        public ComponentTypeHandle<PathGridTraversalState> TraversalStateHandle;
        public ComponentTypeHandle<PathGridTraversalPendingRequest> TraversalPendingRequestHandle;
        public ComponentTypeHandle<PathGridTraversalAwaitingResult> TraversalAwaitingResultHandle;
        public BufferTypeHandle<ActorAiPackageRuntime> PackageHandle;
        public float DeltaGameHours;
        public float IdleChanceMultiplier;

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
            if (UpdateDuration(ref package, ref aiState))
            {
                packages[aiState.CurrentPackageIndex] = package;
                FinishPackage(packages, ref aiState, ref traversalState);
                return;
            }

            packages[aiState.CurrentPackageIndex] = package;
            if (!TryResolveNearestNode(anchor.PathGridIndex, actorPosition, out int startNode))
            {
                aiState.Status = (byte)ActorAiPlannerStatus.Failed;
                return;
            }

            bool waiting = false;
            bool scheduled;
            if (package.Type == (byte)ActorAiRuntimePackageType.Travel)
            {
                scheduled = TryScheduleTravel(package, startNode, ref aiState, ref traversalRequest);
            }
            else if (package.Type == (byte)ActorAiRuntimePackageType.Escort)
            {
                scheduled = TryScheduleEscort(package, startNode, actorPosition, ref aiState, ref traversalRequest, out waiting);
            }
            else if (package.Type == (byte)ActorAiRuntimePackageType.Follow)
            {
                scheduled = TryScheduleFollow(package, startNode, actorPosition, ref aiState, ref traversalRequest, out waiting);
            }
            else if (package.Type == (byte)ActorAiRuntimePackageType.Activate)
            {
                scheduled = TryScheduleActivate(package, startNode, actorPosition, ref aiState, ref traversalRequest, out waiting);
            }
            else
            {
                scheduled = TryScheduleWander(package, anchor.PathGridIndex, startNode, actorPosition, ref aiState, ref traversalRequest, out waiting);
            }

            if (scheduled)
            {
                aiState.CurrentNodeIndex = startNode;
                aiState.Status = (byte)ActorAiPlannerStatus.Traversing;
                hasPendingRequest = true;
            }
            else if (waiting)
            {
                if (aiState.Status != (byte)ActorAiPlannerStatus.Waiting)
                    aiState.Status = (byte)ActorAiPlannerStatus.Idle;
                traversalState.Status = (byte)PathGridTraversalStatus.Idle;
            }
            else
            {
                FinishPackage(packages, ref aiState, ref traversalState);
            }
        }

        bool UpdateDuration(ref ActorAiPackageRuntime package, ref ActorAiState aiState)
        {
            if (package.DurationHours <= 0f)
                return false;

            if (package.Type == (byte)ActorAiRuntimePackageType.Follow && aiState.FollowActive == 0)
                return false;

            if (package.RemainingDurationHours <= 0f)
                package.RemainingDurationHours = package.DurationHours;

            package.RemainingDurationHours = math.max(0f, package.RemainingDurationHours - DeltaGameHours);
            if (package.RemainingDurationHours > 0f)
                return false;

            aiState.FollowActive = 0;
            return true;
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

        bool TryScheduleEscort(
            in ActorAiPackageRuntime package,
            int startNode,
            float3 actorPosition,
            ref ActorAiState aiState,
            ref PathGridTraversalPendingRequest traversalRequest,
            out bool waiting)
        {
            waiting = false;
            if (package.FollowTargetEntity == Entity.Null || !TargetTransforms.HasComponent(package.FollowTargetEntity))
            {
                waiting = true;
                return false;
            }

            float3 targetPosition = TargetTransforms[package.FollowTargetEntity].Position;
            float maxDistance = math.max(0.5f, package.FollowDistance);
            if (math.lengthsq(FlatDelta(targetPosition, actorPosition)) > maxDistance * maxDistance)
            {
                waiting = true;
                return false;
            }

            if (package.DestinationInteriorCellHash == 0UL
                && math.lengthsq(FlatDelta(package.TargetPosition, actorPosition)) <= maxDistance * maxDistance)
            {
                return false;
            }

            return TryScheduleTravel(package, startNode, ref aiState, ref traversalRequest);
        }

        bool TryScheduleFollow(
            in ActorAiPackageRuntime package,
            int startNode,
            float3 actorPosition,
            ref ActorAiState aiState,
            ref PathGridTraversalPendingRequest traversalRequest,
            out bool waiting)
        {
            waiting = false;
            float followDistance = math.max(0.5f, package.FollowDistance);
            if (package.DestinationInteriorCellHash == 0UL
                && math.lengthsq(FlatDelta(package.TargetPosition, float3.zero)) > 0.01f
                && math.lengthsq(FlatDelta(package.TargetPosition, actorPosition)) <= followDistance * followDistance)
            {
                return false;
            }

            if (package.FollowTargetEntity == Entity.Null || !TargetTransforms.HasComponent(package.FollowTargetEntity))
            {
                waiting = true;
                return false;
            }

            float3 targetPosition = TargetTransforms[package.FollowTargetEntity].Position;
            float distanceSq = math.lengthsq(FlatDelta(targetPosition, actorPosition));
            if (aiState.FollowActive == 0)
            {
                float activeRange = followDistance + 384f * WorldScale.MwUnitsToMeters;
                if (distanceSq > activeRange * activeRange)
                {
                    waiting = true;
                    return false;
                }

                aiState.FollowActive = 1;
            }

            float stopDistance = followDistance + (aiState.Status == (byte)ActorAiPlannerStatus.Traversing ? 0f : 30f * WorldScale.MwUnitsToMeters);
            if (distanceSq <= stopDistance * stopDistance)
            {
                waiting = true;
                return false;
            }

            int pathGridIndex = package.TargetPathGridIndex >= 0 ? package.TargetPathGridIndex : Navigation.Nodes[startNode].PathGridIndex;
            if (!TryResolveNearestNode(pathGridIndex, targetPosition, out int goalNode))
            {
                waiting = true;
                return false;
            }

            WriteTraversalRequest(startNode, goalNode, package.AllowPartial, ref traversalRequest, distanceSq > FollowRunDistanceSq());
            aiState.GoalNodeIndex = goalNode;
            return true;
        }

        bool TryScheduleActivate(
            in ActorAiPackageRuntime package,
            int startNode,
            float3 actorPosition,
            ref ActorAiState aiState,
            ref PathGridTraversalPendingRequest traversalRequest,
            out bool waiting)
        {
            waiting = false;
            if (package.FollowTargetEntity == Entity.Null || !TargetTransforms.HasComponent(package.FollowTargetEntity))
            {
                waiting = true;
                return false;
            }

            float3 targetPosition = TargetTransforms[package.FollowTargetEntity].Position;
            float activateDistance = math.max(0.5f, package.FollowDistance);
            if (math.lengthsq(FlatDelta(targetPosition, actorPosition)) <= activateDistance * activateDistance)
            {
                aiState.WaitUntilTime = ElapsedTime + 0.01f;
                aiState.Status = (byte)ActorAiPlannerStatus.Waiting;
                waiting = true;
                return false;
            }

            waiting = true;
            return false;
        }

        bool TryScheduleWander(
            in ActorAiPackageRuntime package,
            int pathGridIndex,
            int startNode,
            float3 actorPosition,
            ref ActorAiState aiState,
            ref PathGridTraversalPendingRequest traversalRequest,
            out bool waiting)
        {
            waiting = false;
            if (TryChooseWanderIdle(package, ref aiState.RandomSeed, out byte idleGroup))
            {
                aiState.PendingIdleGroup = idleGroup;
                aiState.WaitUntilTime = float.PositiveInfinity;
                aiState.Status = (byte)ActorAiPlannerStatus.Waiting;
                waiting = true;
                return false;
            }

            if (package.WanderRadius <= 0f)
            {
                aiState.WaitUntilTime = ElapsedTime + 0.5f;
                aiState.Status = (byte)ActorAiPlannerStatus.Waiting;
                waiting = true;
                return false;
            }

            if (!TryChooseWanderNode(pathGridIndex, startNode, actorPosition, package.WanderRadius, ref aiState.RandomSeed, out int goalNode))
                return false;

            WriteTraversalRequest(startNode, goalNode, package.AllowPartial, ref traversalRequest);
            aiState.GoalNodeIndex = goalNode;
            return true;
        }

        bool TryChooseWanderIdle(in ActorAiPackageRuntime package, ref uint randomSeed, out byte idleGroup)
        {
            idleGroup = 0;
            var random = new Unity.Mathematics.Random(randomSeed == 0u ? 1u : randomSeed);
            if (random.NextFloat() > IdleChanceMultiplier)
            {
                randomSeed = random.state == 0u ? 1u : random.state;
                return false;
            }

            float maxRoll = 0f;
            for (int i = 0; i < 8; i++)
            {
                byte chance = GetIdleChance(package, i);
                if (chance == 0)
                    continue;

                float roll = random.NextFloat() * 100f;
                if (roll <= chance && roll > maxRoll)
                {
                    maxRoll = roll;
                    idleGroup = (byte)(i + 2);
                }
            }

            randomSeed = random.state == 0u ? 1u : random.state;
            return idleGroup != 0;
        }

        static byte GetIdleChance(in ActorAiPackageRuntime package, int index)
        {
            return index switch
            {
                0 => package.IdleChance0,
                1 => package.IdleChance1,
                2 => package.IdleChance2,
                3 => package.IdleChance3,
                4 => package.IdleChance4,
                5 => package.IdleChance5,
                6 => package.IdleChance6,
                _ => package.IdleChance7,
            };
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
            package.RemainingDurationHours = package.DurationHours;
            packages[aiState.CurrentPackageIndex] = package;
            aiState.FollowActive = 0;
            aiState.PendingIdleGroup = 0;
            aiState.ActiveIdleGroupHash = 0UL;
            if (package.ShouldRepeat == 0)
                aiState.CurrentPackageIndex++;

            if ((uint)aiState.CurrentPackageIndex >= (uint)packages.Length)
                aiState.Status = (byte)ActorAiPlannerStatus.Complete;
            else
                aiState.Status = (byte)ActorAiPlannerStatus.Waiting;
        }

        void WriteTraversalRequest(int startNode, int goalNode, byte allowPartial, ref PathGridTraversalPendingRequest traversalRequest, bool run = false)
        {
            traversalRequest = new PathGridTraversalPendingRequest
            {
                StartNodeIndex = startNode,
                GoalNodeIndex = goalNode,
                AllowPartial = allowPartial,
                MaxFineIterations = 4096,
                MaxAbstractIterations = 4096,
                Run = run ? (byte)1 : (byte)0,
            };
        }

        static float FollowRunDistanceSq()
        {
            float runDistance = 450f * WorldScale.MwUnitsToMeters;
            return runDistance * runDistance;
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
            var random = new Unity.Mathematics.Random(randomSeed == 0u ? 1u : randomSeed);

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
