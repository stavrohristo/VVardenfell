using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.WorldRefs;

namespace VVardenfell.Runtime.MorrowindScript
{
    static class MorrowindCombatTargetUtility
    {
        const float CombatPursuitDistanceMw = 128f;

        public static bool TryStartCombat(
            ref RuntimeContentBlob content,
            EntityManager entityManager,
            Entity actor,
            uint actorPlacedRefId,
            Entity target,
            uint targetPlacedRefId)
        {
            if (actor == Entity.Null
                || target == Entity.Null
                || !entityManager.Exists(actor)
                || !entityManager.Exists(target)
                || !entityManager.HasComponent<ActorSpawnSource>(actor)
                || !entityManager.HasBuffer<ActorCombatTarget>(actor)
                || !entityManager.HasComponent<ActorActiveCombatTarget>(actor)
                || !entityManager.HasComponent<ActorVitalSet>(actor)
                || !entityManager.HasComponent<ActorVitalSet>(target))
            {
                return false;
            }

            if (actor == target
                || ActorHitAftermathStateUtility.IsDead(entityManager, actor)
                || ActorHitAftermathStateUtility.IsDead(entityManager, target))
                return false;

            var targets = entityManager.GetBuffer<ActorCombatTarget>(actor);
            for (int i = 0; i < targets.Length; i++)
            {
                var existing = targets[i];
                if (existing.TargetEntity == target || (targetPlacedRefId != 0u && existing.TargetPlacedRefId == targetPlacedRefId))
                    return true;
            }

            targets.Add(new ActorCombatTarget
            {
                TargetEntity = target,
                TargetPlacedRefId = targetPlacedRefId,
                Sequence = NextSequence(targets),
            });
            return true;
        }

        public static bool TryStopCombat(EntityManager entityManager, Entity actor)
        {
            if (actor == Entity.Null
                || !entityManager.Exists(actor)
                || !entityManager.HasComponent<ActorSpawnSource>(actor))
            {
                return false;
            }

            if (entityManager.HasBuffer<ActorCombatTarget>(actor))
                entityManager.GetBuffer<ActorCombatTarget>(actor).Clear();
            if (entityManager.HasComponent<ActorActiveCombatTarget>(actor))
            {
                entityManager.SetComponentData(actor, new ActorActiveCombatTarget());
                entityManager.SetComponentEnabled<ActorActiveCombatTarget>(actor, false);
            }

            MorrowindScriptAiPackageUtility.ClearCombatPackages(entityManager, actor);
            return true;
        }

        public static bool TryStopCombat(EntityManager entityManager, Entity actor, Entity target, uint targetPlacedRefId)
        {
            if (actor == Entity.Null
                || target == Entity.Null
                || !entityManager.Exists(actor)
                || !entityManager.HasBuffer<ActorCombatTarget>(actor))
            {
                return false;
            }

            var targets = entityManager.GetBuffer<ActorCombatTarget>(actor);
            for (int i = targets.Length - 1; i >= 0; i--)
            {
                var entry = targets[i];
                if (entry.TargetEntity == target || (targetPlacedRefId != 0u && entry.TargetPlacedRefId == targetPlacedRefId))
                    targets.RemoveAt(i);
            }

            if (entityManager.HasComponent<ActorActiveCombatTarget>(actor)
                && entityManager.IsComponentEnabled<ActorActiveCombatTarget>(actor))
            {
                var active = entityManager.GetComponentData<ActorActiveCombatTarget>(actor);
                if (active.TargetEntity == target || (targetPlacedRefId != 0u && active.TargetPlacedRefId == targetPlacedRefId))
                {
                    entityManager.SetComponentData(actor, new ActorActiveCombatTarget());
                    entityManager.SetComponentEnabled<ActorActiveCombatTarget>(actor, false);
                    MorrowindScriptAiPackageUtility.ClearCombatPackages(entityManager, actor);
                }
            }

            return true;
        }

        public static bool IsInCombatWith(EntityManager entityManager, Entity actor, Entity target)
        {
            if (actor == Entity.Null || target == Entity.Null || !entityManager.Exists(actor) || !entityManager.HasBuffer<ActorCombatTarget>(actor))
                return false;

            var targets = entityManager.GetBuffer<ActorCombatTarget>(actor, true);
            uint targetPlacedRefId = entityManager.Exists(target) && entityManager.HasComponent<PlacedRefIdentity>(target)
                ? entityManager.GetComponentData<PlacedRefIdentity>(target).Value
                : 0u;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i].TargetEntity == target || (targetPlacedRefId != 0u && targets[i].TargetPlacedRefId == targetPlacedRefId))
                    return true;
            }

            return false;
        }

        public static bool IsInCombat(EntityManager entityManager, Entity actor)
            => actor != Entity.Null
               && entityManager.Exists(actor)
               && entityManager.HasBuffer<ActorCombatTarget>(actor)
               && entityManager.GetBuffer<ActorCombatTarget>(actor, true).Length > 0;

        public static float CombatPursuitDistanceMeters => CombatPursuitDistanceMw * WorldScale.MwUnitsToMeters;

        static uint NextSequence(DynamicBuffer<ActorCombatTarget> targets)
        {
            uint max = 0u;
            for (int i = 0; i < targets.Length; i++)
                max = math.max(max, targets[i].Sequence);
            return max == uint.MaxValue ? 1u : max + 1u;
        }
    }
}
