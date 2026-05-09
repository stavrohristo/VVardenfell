using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Profiling;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Physics
{
    [Flags]
    internal enum DeferredPhysicsQueryKindMask : byte
    {
        GenericRay = 1 << 0,
        InteractionPick = 1 << 1,
        LineOfSight = 1 << 2,
        MeleeConfirmation = 1 << 3,
        ProjectileSegment = 1 << 4,
    }

    internal enum DeferredPhysicsQueryResolveDomain : byte
    {
        Frame = 0,
        Fixed = 1,
    }

    internal struct DeferredPhysicsQueryResolveStats
    {
        public int OwnedRequests;
        public int RemainingRequests;
    }

    internal struct DeferredPhysicsQueryResolveScratch : IDisposable
    {
        public NativeList<DeferredPhysicsQueryRequest> OwnedRequests;
        public NativeList<DeferredPhysicsQueryRequest> RemainingRequests;
        public NativeList<DeferredPhysicsQueryResult> ResolvedResults;

        public bool IsCreated => OwnedRequests.IsCreated;

        public DeferredPhysicsQueryResolveScratch(int initialCapacity, Allocator allocator)
        {
            OwnedRequests = new NativeList<DeferredPhysicsQueryRequest>(initialCapacity, allocator);
            RemainingRequests = new NativeList<DeferredPhysicsQueryRequest>(initialCapacity, allocator);
            ResolvedResults = new NativeList<DeferredPhysicsQueryResult>(initialCapacity, allocator);
        }

        public void Dispose()
        {
            if (OwnedRequests.IsCreated)
                OwnedRequests.Dispose();
            if (RemainingRequests.IsCreated)
                RemainingRequests.Dispose();
            if (ResolvedResults.IsCreated)
                ResolvedResults.Dispose();
        }
    }

    internal static class DeferredPhysicsQueryResolveUtility
    {
        const float MinUsableHitFraction = 0.001f;
        const uint MaxRetainedResultAgeTicks = DeferredPhysicsQueryUtility.FrameMaxResultAgeTicks;

        static readonly ProfilerMarker k_ResolveQueries = new("VV.Physics.DeferredQuery.Resolve");
        static readonly ProfilerMarker k_PruneResults = new("VV.Physics.DeferredQuery.PruneResults");
        static readonly ProfilerMarker k_ProcessRequests = new("VV.Physics.DeferredQuery.ProcessRequests");
        static readonly ProfilerMarker k_FrameOwnedRequests = new("VV.Physics.DeferredQuery.FrameOwnedRequests");
        static readonly ProfilerMarker k_FixedOwnedRequests = new("VV.Physics.DeferredQuery.FixedOwnedRequests");

        public static DeferredPhysicsQueryResolveStats ResolveOwnedRequests(
            EntityManager entityManager,
            Entity queueEntity,
            in PhysicsWorldSingleton physicsWorld,
            uint fixedTick,
            uint buildSequence,
            DeferredPhysicsQueryKindMask ownedKinds,
            DeferredPhysicsQueryResolveDomain domain,
            ref DeferredPhysicsQueryResolveScratch scratch)
        {
            using var resolveScope = k_ResolveQueries.Auto();
            var requests = entityManager.GetBuffer<DeferredPhysicsQueryRequest>(queueEntity);
            var results = entityManager.GetBuffer<DeferredPhysicsQueryResult>(queueEntity);

            using (k_PruneResults.Auto())
            {
                PruneExpiredResults(results, fixedTick);
            }

            var stats = new DeferredPhysicsQueryResolveStats();
            if (requests.Length == 0)
            {
                entityManager.SetComponentEnabled<DeferredPhysicsQueryPending>(queueEntity, false);
                return stats;
            }

            using (DomainMarker(domain).Auto())
            using (k_ProcessRequests.Auto())
            {
                SplitOwnedRequests(requests, ownedKinds, ref scratch, out stats);
                requests.Clear();
                requests.EnsureCapacity(scratch.RemainingRequests.Length);
                for (int i = 0; i < scratch.RemainingRequests.Length; i++)
                    requests.Add(scratch.RemainingRequests[i]);

                if (scratch.OwnedRequests.Length > 0)
                {
                    EnsureListLength(ref scratch.ResolvedResults, scratch.OwnedRequests.Length);
                    var job = new ResolveDeferredPhysicsQueriesJob
                    {
                        CollisionWorld = physicsWorld.CollisionWorld,
                        BuildSequence = buildSequence,
                        Requests = scratch.OwnedRequests.AsArray(),
                        Results = scratch.ResolvedResults.AsArray(),
                    };
                    job.Schedule(scratch.OwnedRequests.Length, 32).Complete();

                    results.EnsureCapacity(results.Length + scratch.ResolvedResults.Length);
                    for (int i = 0; i < scratch.ResolvedResults.Length; i++)
                        results.Add(scratch.ResolvedResults[i]);
                }
            }

            stats.RemainingRequests = requests.Length;
            if (stats.OwnedRequests > 0)
            {
                var runtime = entityManager.GetComponentData<DeferredPhysicsQueryRuntime>(queueEntity);
                runtime.LastResolvedBuildSequence = buildSequence;
                entityManager.SetComponentData(queueEntity, runtime);
            }

            entityManager.SetComponentEnabled<DeferredPhysicsQueryPending>(queueEntity, requests.Length > 0);
            return stats;
        }

        static void SplitOwnedRequests(
            DynamicBuffer<DeferredPhysicsQueryRequest> requests,
            DeferredPhysicsQueryKindMask ownedKinds,
            ref DeferredPhysicsQueryResolveScratch scratch,
            out DeferredPhysicsQueryResolveStats stats)
        {
            EnsureListCapacity(ref scratch.OwnedRequests, requests.Length);
            EnsureListCapacity(ref scratch.RemainingRequests, requests.Length);
            scratch.OwnedRequests.Clear();
            scratch.RemainingRequests.Clear();
            scratch.ResolvedResults.Clear();

            stats = default;
            for (int i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                if (Owns(request.Kind, ownedKinds))
                {
                    scratch.OwnedRequests.AddNoResize(request);
                    stats.OwnedRequests++;
                }
                else
                {
                    scratch.RemainingRequests.AddNoResize(request);
                }
            }
        }

        static void EnsureListCapacity<T>(ref NativeList<T> list, int capacity)
            where T : unmanaged
        {
            if (list.Capacity < capacity)
                list.Capacity = capacity;
        }

        static void EnsureListLength<T>(ref NativeList<T> list, int length)
            where T : unmanaged
        {
            EnsureListCapacity(ref list, length);
            list.ResizeUninitialized(length);
        }

        static ProfilerMarker DomainMarker(DeferredPhysicsQueryResolveDomain domain)
            => domain == DeferredPhysicsQueryResolveDomain.Fixed ? k_FixedOwnedRequests : k_FrameOwnedRequests;

        static bool Owns(DeferredPhysicsQueryKind kind, DeferredPhysicsQueryKindMask ownedKinds)
        {
            return kind switch
            {
                DeferredPhysicsQueryKind.GenericRay => (ownedKinds & DeferredPhysicsQueryKindMask.GenericRay) != 0,
                DeferredPhysicsQueryKind.InteractionPick => (ownedKinds & DeferredPhysicsQueryKindMask.InteractionPick) != 0,
                DeferredPhysicsQueryKind.LineOfSight => (ownedKinds & DeferredPhysicsQueryKindMask.LineOfSight) != 0,
                DeferredPhysicsQueryKind.MeleeConfirmation => (ownedKinds & DeferredPhysicsQueryKindMask.MeleeConfirmation) != 0,
                DeferredPhysicsQueryKind.ProjectileSegment => (ownedKinds & DeferredPhysicsQueryKindMask.ProjectileSegment) != 0,
                _ => false,
            };
        }

        static void PruneExpiredResults(DynamicBuffer<DeferredPhysicsQueryResult> results, uint fixedTick)
        {
            for (int i = results.Length - 1; i >= 0; i--)
            {
                var result = results[i];
                if (fixedTick <= result.RequestFixedTick + MaxRetainedResultAgeTicks)
                    continue;

                results.RemoveAt(i);
            }
        }

        [BurstCompile]
        struct ResolveDeferredPhysicsQueriesJob : IJobParallelFor
        {
            [ReadOnly] public CollisionWorld CollisionWorld;
            public uint BuildSequence;
            [ReadOnly] public NativeArray<DeferredPhysicsQueryRequest> Requests;
            [NativeDisableParallelForRestriction] public NativeArray<DeferredPhysicsQueryResult> Results;

            public void Execute(int index)
            {
                var request = Requests[index];
                var result = new DeferredPhysicsQueryResult
                {
                    Sequence = request.Sequence,
                    Kind = request.Kind,
                    Status = DeferredPhysicsQueryStatus.Miss,
                    RequesterEntity = request.RequesterEntity,
                    TargetEntity = request.TargetEntity,
                    HitEntity = Entity.Null,
                    RequestFixedTick = request.RequestFixedTick,
                    PhysicsBuildSequence = BuildSequence,
                };

                float3 delta = request.End - request.Start;
                if (math.lengthsq(delta) <= 0.00000001f)
                {
                    Results[index] = result;
                    return;
                }

                if (request.Collider.IsCreated)
                {
                    ResolveColliderCast(CollisionWorld, request, ref result);
                    Results[index] = result;
                    return;
                }

                var input = new RaycastInput
                {
                    Start = request.Start,
                    End = request.End,
                    Filter = request.Filter,
                };

                var collector = new NearestUsableRaycastCollector
                {
                    Request = request,
                };
                if (!CollisionWorld.CastRay(input, ref collector) || !collector.Found)
                {
                    Results[index] = result;
                    return;
                }

                var nearest = collector.Nearest;
                result.Status = DeferredPhysicsQueryStatus.Hit;
                result.HitEntity = nearest.Entity;
                result.Position = nearest.Position;
                result.Normal = nearest.SurfaceNormal;
                result.Fraction = nearest.Fraction;
                Results[index] = result;
            }
        }

        static void ResolveColliderCast(
            in CollisionWorld collisionWorld,
            in DeferredPhysicsQueryRequest request,
            ref DeferredPhysicsQueryResult result)
        {
            var input = new ColliderCastInput(request.Collider, request.Start, request.End, request.Rotation);
            var collector = new NearestUsableColliderCastCollector
            {
                Request = request,
            };
            if (!collisionWorld.CastCollider(input, ref collector) || !collector.Found)
                return;

            var nearest = collector.Nearest;
            result.Status = DeferredPhysicsQueryStatus.Hit;
            result.HitEntity = nearest.Entity;
            result.Position = nearest.Position;
            result.Normal = nearest.SurfaceNormal;
            result.Fraction = nearest.Fraction;
        }

        static bool IsUsableHit(in Unity.Physics.RaycastHit hit, in DeferredPhysicsQueryRequest request)
        {
            if (hit.Fraction <= MinUsableHitFraction)
                return false;
            if (hit.Entity == Entity.Null)
                return false;
            if (request.IgnoreEntity != Entity.Null && hit.Entity == request.IgnoreEntity)
                return false;
            if (request.RequesterEntity != Entity.Null && hit.Entity == request.RequesterEntity)
                return false;

            return true;
        }

        static bool IsUsableHit(in ColliderCastHit hit, in DeferredPhysicsQueryRequest request)
        {
            if (hit.Fraction <= MinUsableHitFraction)
                return false;
            if (hit.Entity == Entity.Null)
                return false;
            if (request.IgnoreEntity != Entity.Null && hit.Entity == request.IgnoreEntity)
                return false;
            if (request.RequesterEntity != Entity.Null && hit.Entity == request.RequesterEntity)
                return false;

            return true;
        }

        struct NearestUsableRaycastCollector : ICollector<Unity.Physics.RaycastHit>
        {
            public DeferredPhysicsQueryRequest Request;
            public Unity.Physics.RaycastHit Nearest;
            public bool Found;

            public bool EarlyOutOnFirstHit => false;
            public float MaxFraction => Found ? Nearest.Fraction : 1f;
            public int NumHits => Found ? 1 : 0;

            public bool AddHit(Unity.Physics.RaycastHit hit)
            {
                if (!IsUsableHit(hit, Request))
                    return false;
                if (Found && hit.Fraction >= Nearest.Fraction)
                    return false;

                Found = true;
                Nearest = hit;
                return true;
            }
        }

        struct NearestUsableColliderCastCollector : ICollector<ColliderCastHit>
        {
            public DeferredPhysicsQueryRequest Request;
            public ColliderCastHit Nearest;
            public bool Found;

            public bool EarlyOutOnFirstHit => false;
            public float MaxFraction => Found ? Nearest.Fraction : 1f;
            public int NumHits => Found ? 1 : 0;

            public bool AddHit(ColliderCastHit hit)
            {
                if (!IsUsableHit(hit, Request))
                    return false;
                if (Found && hit.Fraction >= Nearest.Fraction)
                    return false;

                Found = true;
                Nearest = hit;
                return true;
            }
        }
    }
}
