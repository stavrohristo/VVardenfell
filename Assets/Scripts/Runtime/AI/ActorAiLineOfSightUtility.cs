using Unity.Mathematics;
using Unity.Physics;
using VVardenfell.Runtime.Interactions;

namespace VVardenfell.Runtime.AI
{
    static class ActorAiLineOfSightUtility
    {
        public static bool HasLineOfSight(PhysicsWorldSingleton physicsWorld, float3 source, float3 target)
        {
            float3 delta = target - source;
            float distance = math.length(delta);
            if (distance <= 0.001f)
                return true;

            var input = new RaycastInput
            {
                Start = source,
                End = source + delta / distance * math.max(0f, distance - 0.05f),
                Filter = InteractionCollisionLayers.LineOfSightQueryFilter,
            };
            return !physicsWorld.CastRay(input);
        }
    }
}
