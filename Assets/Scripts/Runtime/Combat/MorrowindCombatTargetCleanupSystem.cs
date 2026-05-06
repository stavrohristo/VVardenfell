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
    public partial struct MorrowindCombatTargetCleanupSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<ActorCombatTargetState>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            using var actorsToStop = new NativeList<Entity>(Allocator.Temp);

            foreach (var (combatRef, actor) in SystemAPI.Query<RefRO<ActorCombatTargetState>>().WithEntityAccess())
            {
                var combat = combatRef.ValueRO;
                if (combat.Active == 0)
                    continue;

                RequireCombatActorComposition(ref systemState, actor);
                if (IsActorDeadOrDisabled(ref systemState, actor))
                {
                    actorsToStop.Add(actor);
                    continue;
                }

                Entity target = combat.TargetEntity;
                if (target == Entity.Null || !systemState.EntityManager.Exists(target))
                {
                    actorsToStop.Add(actor);
                    continue;
                }

                RequireCombatTargetComposition(ref systemState, actor, target, combat.TargetPlacedRefId);
                if (IsActorDeadOrDisabled(ref systemState, target))
                    actorsToStop.Add(actor);
            }

            for (int i = 0; i < actorsToStop.Length; i++)
                MorrowindCombatTargetUtility.TryStopCombat(systemState.EntityManager, actorsToStop[i]);
        }

        void RequireCombatActorComposition(ref SystemState systemState, Entity actor)
        {
            if (actor == Entity.Null || !systemState.EntityManager.Exists(actor))
                throw new InvalidOperationException("[VVardenfell][CombatTarget] Active combat actor entity is missing.");
            if (!systemState.EntityManager.HasComponent<ActorSpawnSource>(actor))
                throw new InvalidOperationException($"[VVardenfell][CombatTarget] Active combat actor ref={PlacedRefId(ref systemState, actor)} has no ActorSpawnSource.");
            if (!systemState.EntityManager.HasComponent<ActorVitalSet>(actor))
                throw new InvalidOperationException($"[VVardenfell][CombatTarget] Active combat actor ref={PlacedRefId(ref systemState, actor)} has no ActorVitalSet.");
            if (!systemState.EntityManager.HasComponent<ActorHitAftermathState>(actor))
                throw new InvalidOperationException($"[VVardenfell][CombatTarget] Active combat actor ref={PlacedRefId(ref systemState, actor)} has no ActorHitAftermathState.");
        }

        void RequireCombatTargetComposition(ref SystemState systemState, Entity actor, Entity target, uint expectedPlacedRefId)
        {
            if (!systemState.EntityManager.HasComponent<ActorVitalSet>(target))
                throw new InvalidOperationException($"[VVardenfell][CombatTarget] Active combat actor ref={PlacedRefId(ref systemState, actor)} targets entity={target.Index}:{target.Version} with no ActorVitalSet.");
            if (!systemState.EntityManager.HasComponent<ActorHitAftermathState>(target))
                throw new InvalidOperationException($"[VVardenfell][CombatTarget] Active combat actor ref={PlacedRefId(ref systemState, actor)} targets entity={target.Index}:{target.Version} with no ActorHitAftermathState.");

            if (expectedPlacedRefId == 0u || systemState.EntityManager.HasComponent<PlayerTag>(target))
                return;

            if (!systemState.EntityManager.HasComponent<PlacedRefIdentity>(target))
                throw new InvalidOperationException($"[VVardenfell][CombatTarget] Active combat actor ref={PlacedRefId(ref systemState, actor)} targets ref=0x{expectedPlacedRefId:X8}, but target entity has no PlacedRefIdentity.");

            uint actualPlacedRefId = systemState.EntityManager.GetComponentData<PlacedRefIdentity>(target).Value;
            if (actualPlacedRefId != expectedPlacedRefId)
            {
                throw new InvalidOperationException(
                    $"[VVardenfell][CombatTarget] Active combat actor ref={PlacedRefId(ref systemState, actor)} target ref mismatch: state=0x{expectedPlacedRefId:X8}, entity=0x{actualPlacedRefId:X8}.");
            }
        }

        bool IsActorDeadOrDisabled(ref SystemState systemState, Entity actor)
        {
            if (systemState.EntityManager.HasComponent<PlacedRefRuntimeState>(actor)
                && systemState.EntityManager.GetComponentData<PlacedRefRuntimeState>(actor).Disabled != 0)
            {
                return true;
            }

            var vitals = systemState.EntityManager.GetComponentData<ActorVitalSet>(actor);
            var aftermath = systemState.EntityManager.GetComponentData<ActorHitAftermathState>(actor);
            if (aftermath.Dead != 0 && vitals.CurrentHealth > 0f)
                throw new InvalidOperationException($"[VVardenfell][CombatTarget] Actor ref={PlacedRefId(ref systemState, actor)} is marked dead but still has positive health.");

            return vitals.CurrentHealth <= 0f || aftermath.Dead != 0;
        }

        uint PlacedRefId(ref SystemState systemState, Entity entity)
            => entity != Entity.Null
               && systemState.EntityManager.Exists(entity)
               && systemState.EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? systemState.EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
    }
}
