using Unity.Entities;
using VVardenfell.Runtime.Pathfinding;

namespace VVardenfell.Runtime.Movement
{
    public static class ActorMovementAuthoringUtility
    {
        public static void QueueEnsureMovableActor(
            ref EntityCommandBuffer ecb,
            Entity actor,
            in MorrowindMovementSpeed speed)
        {
            ecb.AddComponent(actor, new MorrowindMovementInput());
            ecb.AddComponent(actor, speed);

            ecb.AddComponent(actor, new PathGridTraversalState());
            ecb.AddComponent(actor, new PathGridTraversalPendingRequest());
            ecb.SetComponentEnabled<PathGridTraversalPendingRequest>(actor, false);
            ecb.AddComponent(actor, new PathGridTraversalAwaitingResult());
            ecb.SetComponentEnabled<PathGridTraversalAwaitingResult>(actor, false);
            ecb.AddBuffer<PathGridTraversalNode>(actor);
        }
    }
}
