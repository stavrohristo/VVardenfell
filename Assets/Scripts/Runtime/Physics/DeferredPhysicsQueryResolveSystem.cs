using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Profiling;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Physics
{
    [UpdateInGroup(typeof(MorrowindPhysicsQuerySystemGroup))]
    [UpdateAfter(typeof(MorrowindPhysicsQueryFrameStampSystem))]
    public partial class DeferredPhysicsQueryResolveSystem : SystemBase
    {
        const float MinUsableHitFraction = 0.001f;
        const uint MaxRetainedResultAgeTicks = 8u;

        static readonly ProfilerMarker k_ResolveQueries = new("VV.Physics.DeferredQuery.Resolve");
        static readonly ProfilerMarker k_PruneResults = new("VV.Physics.DeferredQuery.PruneResults");
        static readonly ProfilerMarker k_ProcessRequests = new("VV.Physics.DeferredQuery.ProcessRequests");
        static readonly ProfilerMarker k_GenericRayRequests = new("VV.Physics.DeferredQuery.Kind.GenericRay");
        static readonly ProfilerMarker k_InteractionPickRequests = new("VV.Physics.DeferredQuery.Kind.InteractionPick");
        static readonly ProfilerMarker k_LineOfSightRequests = new("VV.Physics.DeferredQuery.Kind.LineOfSight");
        static readonly ProfilerMarker k_MeleeConfirmationRequests = new("VV.Physics.DeferredQuery.Kind.MeleeConfirmation");
        static readonly ProfilerMarker k_ProjectileSegmentRequests = new("VV.Physics.DeferredQuery.Kind.ProjectileSegment");

        protected override void OnCreate()
        {
            RequireForUpdate<DeferredPhysicsQueryQueueTag>();
            RequireForUpdate<PhysicsWorldSingleton>();
            RequireForUpdate<MorrowindPhysicsFrameState>();
        }

        protected override void OnUpdate()
        {
            using var resolveScope = k_ResolveQueries.Auto();
            Entity queueEntity = SystemAPI.GetSingletonEntity<DeferredPhysicsQueryQueueTag>();
            if (!EntityManager.HasComponent<DeferredPhysicsQueryPending>(queueEntity))
                throw new System.InvalidOperationException("[VVardenfell][Physics] Deferred physics query queue is missing its pending marker.");
            if (!SystemAPI.IsComponentEnabled<DeferredPhysicsQueryPending>(queueEntity))
                return;

            CompleteDependency();

            var requests = EntityManager.GetBuffer<DeferredPhysicsQueryRequest>(queueEntity);
            var results = EntityManager.GetBuffer<DeferredPhysicsQueryResult>(queueEntity);

            var runtime = EntityManager.GetComponentData<DeferredPhysicsQueryRuntime>(queueEntity);
            var frame = SystemAPI.GetSingleton<MorrowindPhysicsFrameState>();
            runtime.LastResolvedBuildSequence = frame.BuildSequence;
            EntityManager.SetComponentData(queueEntity, runtime);
            using (k_PruneResults.Auto())
            {
                PruneExpiredResults(results, frame.FixedTick);
            }

            if (requests.Length == 0)
            {
                EntityManager.SetComponentEnabled<DeferredPhysicsQueryPending>(queueEntity, false);
                return;
            }

            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            using (k_ProcessRequests.Auto())
            {
                for (int i = 0; i < requests.Length; i++)
                    ResolveRequestProfiled(physicsWorld, requests[i], runtime.LastResolvedBuildSequence, results);
            }

            requests.Clear();
            EntityManager.SetComponentEnabled<DeferredPhysicsQueryPending>(queueEntity, false);
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

        static void ResolveRequestProfiled(
            in PhysicsWorldSingleton physicsWorld,
            in DeferredPhysicsQueryRequest request,
            uint buildSequence,
            DynamicBuffer<DeferredPhysicsQueryResult> results)
        {
            switch (request.Kind)
            {
                case DeferredPhysicsQueryKind.GenericRay:
                    using (k_GenericRayRequests.Auto())
                        ResolveRequest(physicsWorld, request, buildSequence, results);
                    break;
                case DeferredPhysicsQueryKind.InteractionPick:
                    using (k_InteractionPickRequests.Auto())
                        ResolveRequest(physicsWorld, request, buildSequence, results);
                    break;
                case DeferredPhysicsQueryKind.LineOfSight:
                    using (k_LineOfSightRequests.Auto())
                        ResolveRequest(physicsWorld, request, buildSequence, results);
                    break;
                case DeferredPhysicsQueryKind.MeleeConfirmation:
                    using (k_MeleeConfirmationRequests.Auto())
                        ResolveRequest(physicsWorld, request, buildSequence, results);
                    break;
                case DeferredPhysicsQueryKind.ProjectileSegment:
                    using (k_ProjectileSegmentRequests.Auto())
                        ResolveRequest(physicsWorld, request, buildSequence, results);
                    break;
                default:
                    ResolveRequest(physicsWorld, request, buildSequence, results);
                    break;
            }
        }

        static void ResolveRequest(
            in PhysicsWorldSingleton physicsWorld,
            in DeferredPhysicsQueryRequest request,
            uint buildSequence,
            DynamicBuffer<DeferredPhysicsQueryResult> results)
        {
            var result = new DeferredPhysicsQueryResult
            {
                Sequence = request.Sequence,
                Kind = request.Kind,
                Status = DeferredPhysicsQueryStatus.Miss,
                RequesterEntity = request.RequesterEntity,
                TargetEntity = request.TargetEntity,
                HitEntity = Entity.Null,
                RequestFixedTick = request.RequestFixedTick,
                PhysicsBuildSequence = buildSequence,
            };

            float3 delta = request.End - request.Start;
            if (math.lengthsq(delta) <= 0.00000001f)
            {
                results.Add(result);
                return;
            }

            if (request.Collider.IsCreated)
            {
                ResolveColliderCast(physicsWorld, request, ref result);
                results.Add(result);
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
            if (!physicsWorld.CollisionWorld.CastRay(input, ref collector) || !collector.Found)
            {
                results.Add(result);
                return;
            }

            var nearest = collector.Nearest;
            result.Status = DeferredPhysicsQueryStatus.Hit;
            result.HitEntity = nearest.Entity;
            result.Position = nearest.Position;
            result.Normal = nearest.SurfaceNormal;
            result.Fraction = nearest.Fraction;
            results.Add(result);
        }

        static void ResolveColliderCast(
            in PhysicsWorldSingleton physicsWorld,
            in DeferredPhysicsQueryRequest request,
            ref DeferredPhysicsQueryResult result)
        {
            var input = new ColliderCastInput(request.Collider, request.Start, request.End, request.Rotation);
            var collector = new NearestUsableColliderCastCollector
            {
                Request = request,
            };
            if (!physicsWorld.CollisionWorld.CastCollider(input, ref collector) || !collector.Found)
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
