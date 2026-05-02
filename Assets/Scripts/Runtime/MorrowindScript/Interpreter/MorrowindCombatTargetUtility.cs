using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;

namespace VVardenfell.Runtime.MorrowindScript
{
    static class MorrowindCombatTargetUtility
    {
        const float CombatPursuitDistanceMw = 128f;

        public static bool TryStartCombat(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity actor,
            uint actorPlacedRefId,
            Entity target,
            uint targetPlacedRefId)
        {
            if (contentDb == null
                || actor == Entity.Null
                || target == Entity.Null
                || !entityManager.Exists(actor)
                || !entityManager.Exists(target)
                || !entityManager.HasComponent<ActorSpawnSource>(actor)
                || !entityManager.HasComponent<ActorVitalSet>(target))
            {
                return false;
            }

            if (entityManager.GetComponentData<ActorVitalSet>(target).CurrentHealth <= 0f)
                return false;

            var state = new ActorCombatTargetState
            {
                TargetEntity = target,
                TargetPlacedRefId = targetPlacedRefId,
                Active = 1,
            };
            if (entityManager.HasComponent<ActorCombatTargetState>(actor))
                entityManager.SetComponentData(actor, state);
            else
                entityManager.AddComponentData(actor, state);

            return MorrowindScriptAiPackageUtility.TryApplyRequest(
                contentDb,
                entityManager,
                actor,
                new MorrowindScriptAiPackageRequest
                {
                    TargetEntity = actor,
                    TargetPlacedRefId = actorPlacedRefId,
                    FollowTargetEntity = target,
                    FollowTargetPlacedRefId = targetPlacedRefId,
                    PackageType = (byte)MorrowindScriptAiPackageRequestType.Follow,
                    ShouldRepeat = 1,
                    AllowPartial = 1,
                    TargetPosition = entityManager.HasComponent<LocalTransform>(target)
                        ? entityManager.GetComponentData<LocalTransform>(target).Position
                        : float3.zero,
                    FollowDistance = CombatPursuitDistanceMw * WorldScale.MwUnitsToMeters,
                    IdleSeconds = 0f,
                });
        }

        public static bool TryStopCombat(EntityManager entityManager, Entity actor)
        {
            if (actor == Entity.Null
                || !entityManager.Exists(actor)
                || !entityManager.HasComponent<ActorSpawnSource>(actor))
            {
                return false;
            }

            if (entityManager.HasComponent<ActorCombatTargetState>(actor))
            {
                var state = entityManager.GetComponentData<ActorCombatTargetState>(actor);
                state.Active = 0;
                state.TargetEntity = Entity.Null;
                state.TargetPlacedRefId = 0u;
                entityManager.SetComponentData(actor, state);
            }

            MorrowindScriptAiPackageUtility.ClearActorPackages(entityManager, actor);
            return true;
        }
    }
}
