using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using VVardenfell.Runtime.Components;
using Collider = Unity.Physics.Collider;

namespace VVardenfell.Runtime.Physics
{
    public static class DeferredPhysicsQueryUtility
    {
        public const byte DefaultMaxResultAgeTicks = 1;

        public static uint EnqueueRay(
            EntityManager entityManager,
            Entity queueEntity,
            uint fixedTick,
            DeferredPhysicsQueryKind kind,
            Entity requesterEntity,
            Entity targetEntity,
            Entity ignoreEntity,
            float3 start,
            float3 end,
            CollisionFilter filter)
        {
            if (!math.all(math.isfinite(start)) || !math.all(math.isfinite(end)))
                throw new InvalidOperationException("[VVardenfell][Physics] Deferred ray query endpoints must be finite.");

            var runtime = entityManager.GetComponentData<DeferredPhysicsQueryRuntime>(queueEntity);
            runtime.NextSequence++;
            if (runtime.NextSequence == 0u)
                runtime.NextSequence = 1u;
            entityManager.SetComponentData(queueEntity, runtime);

            var requests = entityManager.GetBuffer<DeferredPhysicsQueryRequest>(queueEntity);
            requests.Add(new DeferredPhysicsQueryRequest
            {
                Sequence = runtime.NextSequence,
                Kind = kind,
                RequesterEntity = requesterEntity,
                TargetEntity = targetEntity,
                IgnoreEntity = ignoreEntity,
                Start = start,
                End = end,
                Filter = filter,
                RequestFixedTick = fixedTick,
            });
            entityManager.SetComponentEnabled<DeferredPhysicsQueryPending>(queueEntity, true);
            return runtime.NextSequence;
        }

        public static uint EnqueueColliderCast(
            EntityManager entityManager,
            Entity queueEntity,
            uint fixedTick,
            DeferredPhysicsQueryKind kind,
            Entity requesterEntity,
            Entity targetEntity,
            Entity ignoreEntity,
            BlobAssetReference<Collider> collider,
            float3 start,
            float3 end,
            quaternion rotation)
        {
            if (!collider.IsCreated)
                throw new InvalidOperationException("[VVardenfell][Physics] Deferred collider cast requires a collider.");
            if (!math.all(math.isfinite(start)) || !math.all(math.isfinite(end)))
                throw new InvalidOperationException("[VVardenfell][Physics] Deferred collider cast endpoints must be finite.");

            var runtime = entityManager.GetComponentData<DeferredPhysicsQueryRuntime>(queueEntity);
            runtime.NextSequence++;
            if (runtime.NextSequence == 0u)
                runtime.NextSequence = 1u;
            entityManager.SetComponentData(queueEntity, runtime);

            var requests = entityManager.GetBuffer<DeferredPhysicsQueryRequest>(queueEntity);
            requests.Add(new DeferredPhysicsQueryRequest
            {
                Sequence = runtime.NextSequence,
                Kind = kind,
                RequesterEntity = requesterEntity,
                TargetEntity = targetEntity,
                IgnoreEntity = ignoreEntity,
                Start = start,
                End = end,
                Collider = collider,
                Rotation = rotation,
                RequestFixedTick = fixedTick,
            });
            entityManager.SetComponentEnabled<DeferredPhysicsQueryPending>(queueEntity, true);
            return runtime.NextSequence;
        }

        public static bool TryGetResultBySequence(
            EntityManager entityManager,
            Entity queueEntity,
            uint fixedTick,
            DeferredPhysicsQueryKind kind,
            uint sequence,
            byte maxAgeTicks,
            out DeferredPhysicsQueryResult result)
        {
            result = default;
            if (sequence == 0u)
                return false;

            var results = entityManager.GetBuffer<DeferredPhysicsQueryResult>(queueEntity, true);
            for (int i = results.Length - 1; i >= 0; i--)
            {
                var candidate = results[i];
                if (candidate.Kind != kind || candidate.Sequence != sequence)
                    continue;
                if (!IsResultFresh(fixedTick, candidate, maxAgeTicks))
                    return false;

                result = candidate;
                return true;
            }

            return false;
        }

        public static bool TryGetLatestResult(
            EntityManager entityManager,
            Entity queueEntity,
            uint fixedTick,
            DeferredPhysicsQueryKind kind,
            byte maxAgeTicks,
            out DeferredPhysicsQueryResult result)
        {
            result = default;
            var results = entityManager.GetBuffer<DeferredPhysicsQueryResult>(queueEntity, true);
            for (int i = results.Length - 1; i >= 0; i--)
            {
                var candidate = results[i];
                if (candidate.Kind != kind)
                    continue;
                if (!IsResultFresh(fixedTick, candidate, maxAgeTicks))
                    return false;

                result = candidate;
                return true;
            }

            return false;
        }

        public static bool TryGetLineOfSightOrRequest(
            EntityManager entityManager,
            Entity queueEntity,
            uint fixedTick,
            Entity sourceEntity,
            Entity targetEntity,
            float3 source,
            float3 target,
            CollisionFilter filter,
            byte maxAgeTicks,
            out bool hasLineOfSight)
        {
            hasLineOfSight = false;
            if (math.distancesq(source, target) <= 0.0001f)
            {
                hasLineOfSight = true;
                return true;
            }

            var results = entityManager.GetBuffer<DeferredPhysicsQueryResult>(queueEntity, true);
            for (int i = results.Length - 1; i >= 0; i--)
            {
                var result = results[i];
                if (result.Kind != DeferredPhysicsQueryKind.LineOfSight
                    || result.RequesterEntity != sourceEntity
                    || result.TargetEntity != targetEntity)
                {
                    continue;
                }

                if (!IsResultFresh(fixedTick, result, maxAgeTicks))
                    break;

                hasLineOfSight = result.Status == DeferredPhysicsQueryStatus.Miss;
                return true;
            }

            QueueLineOfSightRequestIfMissing(
                entityManager,
                queueEntity,
                fixedTick,
                sourceEntity,
                targetEntity,
                source,
                target,
                filter);
            return false;
        }

        static void QueueLineOfSightRequestIfMissing(
            EntityManager entityManager,
            Entity queueEntity,
            uint fixedTick,
            Entity sourceEntity,
            Entity targetEntity,
            float3 source,
            float3 target,
            CollisionFilter filter)
        {
            var requests = entityManager.GetBuffer<DeferredPhysicsQueryRequest>(queueEntity, true);
            for (int i = requests.Length - 1; i >= 0; i--)
            {
                var request = requests[i];
                if (request.Kind == DeferredPhysicsQueryKind.LineOfSight
                    && request.RequesterEntity == sourceEntity
                    && request.TargetEntity == targetEntity)
                {
                    return;
                }
            }

            EnqueueRay(
                entityManager,
                queueEntity,
                fixedTick,
                DeferredPhysicsQueryKind.LineOfSight,
                sourceEntity,
                targetEntity,
                Entity.Null,
                source,
                target,
                filter);
        }

        static bool IsResultFresh(uint fixedTick, in DeferredPhysicsQueryResult result, byte maxAgeTicks)
        {
            if (maxAgeTicks == byte.MaxValue)
                return true;

            return fixedTick <= result.RequestFixedTick + maxAgeTicks;
        }
    }
}
