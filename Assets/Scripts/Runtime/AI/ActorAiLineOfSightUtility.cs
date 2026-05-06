using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Interactions;
using VVardenfell.Runtime.Physics;

namespace VVardenfell.Runtime.AI
{
    static class ActorAiLineOfSightUtility
    {
        public static bool HasLineOfSightOrRequest(
            EntityManager entityManager,
            Entity queueEntity,
            uint fixedTick,
            Entity sourceEntity,
            Entity targetEntity,
            float3 source,
            float3 target)
        {
            float3 delta = target - source;
            float distance = math.length(delta);
            if (distance <= 0.001f)
                return true;

            return DeferredPhysicsQueryUtility.TryGetLineOfSightOrRequest(
                       entityManager,
                       queueEntity,
                       fixedTick,
                       sourceEntity,
                       targetEntity,
                       source,
                       source + delta / distance * math.max(0f, distance - 0.05f),
                       InteractionCollisionLayers.LineOfSightQueryFilter,
                       DeferredPhysicsQueryUtility.DefaultMaxResultAgeTicks,
                       out bool hasLineOfSight)
                   && hasLineOfSight;
        }
    }
}
