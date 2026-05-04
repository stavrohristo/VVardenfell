using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
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

        protected override void OnCreate()
        {
            RequireForUpdate<DeferredPhysicsQueryQueueTag>();
            RequireForUpdate<PhysicsWorldSingleton>();
            RequireForUpdate<MorrowindPhysicsFrameState>();
        }

        protected override void OnUpdate()
        {
            CompleteDependency();

            Entity queueEntity = SystemAPI.GetSingletonEntity<DeferredPhysicsQueryQueueTag>();
            var requests = EntityManager.GetBuffer<DeferredPhysicsQueryRequest>(queueEntity);
            var results = EntityManager.GetBuffer<DeferredPhysicsQueryResult>(queueEntity);

            var runtime = EntityManager.GetComponentData<DeferredPhysicsQueryRuntime>(queueEntity);
            var frame = SystemAPI.GetSingleton<MorrowindPhysicsFrameState>();
            runtime.LastResolvedBuildSequence = frame.BuildSequence;
            EntityManager.SetComponentData(queueEntity, runtime);
            PruneExpiredResults(results, frame.FixedTick);

            if (requests.Length == 0)
                return;

            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            using var hits = new NativeList<Unity.Physics.RaycastHit>(Allocator.Temp);
            using var colliderHits = new NativeList<ColliderCastHit>(Allocator.Temp);
            for (int i = 0; i < requests.Length; i++)
                ResolveRequest(physicsWorld, requests[i], runtime.LastResolvedBuildSequence, hits, colliderHits, results);

            requests.Clear();
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

        static void ResolveRequest(
            in PhysicsWorldSingleton physicsWorld,
            in DeferredPhysicsQueryRequest request,
            uint buildSequence,
            NativeList<Unity.Physics.RaycastHit> hits,
            NativeList<ColliderCastHit> colliderHits,
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
                ResolveColliderCast(physicsWorld, request, ref result, colliderHits);
                results.Add(result);
                return;
            }

            var input = new RaycastInput
            {
                Start = request.Start,
                End = request.End,
                Filter = request.Filter,
            };

            hits.Clear();
            if (!physicsWorld.CastRay(input, ref hits))
            {
                results.Add(result);
                return;
            }

            bool found = false;
            Unity.Physics.RaycastHit nearest = default;
            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (!IsUsableHit(hit, request))
                    continue;
                if (found && hit.Fraction >= nearest.Fraction)
                    continue;

                found = true;
                nearest = hit;
            }

            if (!found)
            {
                results.Add(result);
                return;
            }

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
            ref DeferredPhysicsQueryResult result,
            NativeList<ColliderCastHit> hits)
        {
            var input = new ColliderCastInput(request.Collider, request.Start, request.End, request.Rotation);
            hits.Clear();
            if (!physicsWorld.CollisionWorld.CastCollider(input, ref hits))
                return;

            bool found = false;
            ColliderCastHit nearest = default;
            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (!IsUsableHit(hit, request))
                    continue;
                if (found && hit.Fraction >= nearest.Fraction)
                    continue;

                found = true;
                nearest = hit;
            }

            if (!found)
                return;

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
    }
}
