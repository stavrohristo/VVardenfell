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
        public const byte FrameMaxResultAgeTicks = 8;

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
            => EnqueueRay(
                entityManager,
                queueEntity,
                fixedTick,
                kind,
                requesterEntity,
                targetEntity,
                ignoreEntity,
                start,
                end,
                filter,
                markPending: true);

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
            CollisionFilter filter,
            bool markPending)
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
            if (markPending && !entityManager.IsComponentEnabled<DeferredPhysicsQueryPending>(queueEntity))
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
            => EnqueueColliderCast(
                entityManager,
                queueEntity,
                fixedTick,
                kind,
                requesterEntity,
                targetEntity,
                ignoreEntity,
                collider,
                start,
                end,
                rotation,
                markPending: true);

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
            quaternion rotation,
            bool markPending)
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
            if (markPending && !entityManager.IsComponentEnabled<DeferredPhysicsQueryPending>(queueEntity))
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
            => TryGetLineOfSightOrRequest(
                entityManager,
                queueEntity,
                fixedTick,
                sourceEntity,
                targetEntity,
                source,
                target,
                filter,
                maxAgeTicks,
                markPending: true,
                out hasLineOfSight,
                out _);

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
            bool markPending,
            out bool hasLineOfSight,
            out bool queuedRequest)
        {
            hasLineOfSight = false;
            queuedRequest = false;
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

            queuedRequest = QueueLineOfSightRequestIfMissing(
                entityManager,
                queueEntity,
                fixedTick,
                sourceEntity,
                targetEntity,
                source,
                target,
                filter,
                markPending);
            return false;
        }

        static bool QueueLineOfSightRequestIfMissing(
            EntityManager entityManager,
            Entity queueEntity,
            uint fixedTick,
            Entity sourceEntity,
            Entity targetEntity,
            float3 source,
            float3 target,
            CollisionFilter filter,
            bool markPending)
        {
            var requests = entityManager.GetBuffer<DeferredPhysicsQueryRequest>(queueEntity, true);
            for (int i = requests.Length - 1; i >= 0; i--)
            {
                var request = requests[i];
                if (request.Kind == DeferredPhysicsQueryKind.LineOfSight
                    && request.RequesterEntity == sourceEntity
                    && request.TargetEntity == targetEntity)
                {
                    return false;
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
                filter,
                markPending);
            return true;
        }

        static bool IsResultFresh(uint fixedTick, in DeferredPhysicsQueryResult result, byte maxAgeTicks)
        {
            if (maxAgeTicks == byte.MaxValue)
                return true;

            return fixedTick <= result.RequestFixedTick + maxAgeTicks;
        }
    }
}
