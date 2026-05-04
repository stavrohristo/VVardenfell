using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindPreTransformSimulationSystemGroup))]
    [UpdateAfter(typeof(MorrowindDamageSystemGroup))]
    [UpdateBefore(typeof(ActorAiPlannerSystem))]
    public partial class MorrowindCombatTargetCleanupSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<ActorCombatTargetState>();
        }

        protected override void OnUpdate()
        {
            using var actorsToStop = new NativeList<Entity>(Allocator.Temp);

            foreach (var (combatRef, actor) in SystemAPI.Query<RefRO<ActorCombatTargetState>>().WithEntityAccess())
            {
                var combat = combatRef.ValueRO;
                if (combat.Active == 0)
                    continue;

                RequireCombatActorComposition(actor);
                if (IsActorDeadOrDisabled(actor))
                {
                    actorsToStop.Add(actor);
                    continue;
                }

                Entity target = combat.TargetEntity;
                if (target == Entity.Null || !EntityManager.Exists(target))
                {
                    actorsToStop.Add(actor);
                    continue;
                }

                RequireCombatTargetComposition(actor, target, combat.TargetPlacedRefId);
                if (IsActorDeadOrDisabled(target))
                    actorsToStop.Add(actor);
            }

            for (int i = 0; i < actorsToStop.Length; i++)
                MorrowindCombatTargetUtility.TryStopCombat(EntityManager, actorsToStop[i]);
        }

        void RequireCombatActorComposition(Entity actor)
        {
            if (actor == Entity.Null || !EntityManager.Exists(actor))
                throw new InvalidOperationException("[VVardenfell][CombatTarget] Active combat actor entity is missing.");
            if (!EntityManager.HasComponent<ActorSpawnSource>(actor))
                throw new InvalidOperationException($"[VVardenfell][CombatTarget] Active combat actor ref={PlacedRefId(actor)} has no ActorSpawnSource.");
            if (!EntityManager.HasComponent<ActorVitalSet>(actor))
                throw new InvalidOperationException($"[VVardenfell][CombatTarget] Active combat actor ref={PlacedRefId(actor)} has no ActorVitalSet.");
            if (!EntityManager.HasComponent<ActorHitAftermathState>(actor))
                throw new InvalidOperationException($"[VVardenfell][CombatTarget] Active combat actor ref={PlacedRefId(actor)} has no ActorHitAftermathState.");
        }

        void RequireCombatTargetComposition(Entity actor, Entity target, uint expectedPlacedRefId)
        {
            if (!EntityManager.HasComponent<ActorVitalSet>(target))
                throw new InvalidOperationException($"[VVardenfell][CombatTarget] Active combat actor ref={PlacedRefId(actor)} targets entity={target.Index}:{target.Version} with no ActorVitalSet.");
            if (!EntityManager.HasComponent<ActorHitAftermathState>(target))
                throw new InvalidOperationException($"[VVardenfell][CombatTarget] Active combat actor ref={PlacedRefId(actor)} targets entity={target.Index}:{target.Version} with no ActorHitAftermathState.");

            if (expectedPlacedRefId == 0u || EntityManager.HasComponent<PlayerTag>(target))
                return;

            if (!EntityManager.HasComponent<PlacedRefIdentity>(target))
                throw new InvalidOperationException($"[VVardenfell][CombatTarget] Active combat actor ref={PlacedRefId(actor)} targets ref=0x{expectedPlacedRefId:X8}, but target entity has no PlacedRefIdentity.");

            uint actualPlacedRefId = EntityManager.GetComponentData<PlacedRefIdentity>(target).Value;
            if (actualPlacedRefId != expectedPlacedRefId)
            {
                throw new InvalidOperationException(
                    $"[VVardenfell][CombatTarget] Active combat actor ref={PlacedRefId(actor)} target ref mismatch: state=0x{expectedPlacedRefId:X8}, entity=0x{actualPlacedRefId:X8}.");
            }
        }

        bool IsActorDeadOrDisabled(Entity actor)
        {
            if (EntityManager.HasComponent<PlacedRefRuntimeState>(actor)
                && EntityManager.GetComponentData<PlacedRefRuntimeState>(actor).Disabled != 0)
            {
                return true;
            }

            var vitals = EntityManager.GetComponentData<ActorVitalSet>(actor);
            var aftermath = EntityManager.GetComponentData<ActorHitAftermathState>(actor);
            if (aftermath.Dead != 0 && vitals.CurrentHealth > 0f)
                throw new InvalidOperationException($"[VVardenfell][CombatTarget] Actor ref={PlacedRefId(actor)} is marked dead but still has positive health.");

            return vitals.CurrentHealth <= 0f || aftermath.Dead != 0;
        }

        uint PlacedRefId(Entity entity)
            => entity != Entity.Null
               && EntityManager.Exists(entity)
               && EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
    }
}
