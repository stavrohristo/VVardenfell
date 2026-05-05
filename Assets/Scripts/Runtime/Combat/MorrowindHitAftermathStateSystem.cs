using System;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindDamageSystemGroup))]
    [UpdateAfter(typeof(MorrowindDamageApplySystem))]
    [UpdateBefore(typeof(MorrowindDamageFeedbackSystem))]
    public partial class MorrowindHitAftermathStateSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindDamageAppliedEvent>();
            RequireForUpdate<MorrowindCombatRuntimeState>();
            RequireForUpdate<RuntimeContentBlobReference>();
        }

        protected override void OnUpdate()
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][Aftermath] Hit aftermath requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            float knockDownMult = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fKnockDownMult);
            int knockDownOddsMult = RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iKnockDownOddsMult);
            int knockDownOddsBase = RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iKnockDownOddsBase);
            var combatState = SystemAPI.GetSingletonRW<MorrowindCombatRuntimeState>();
            var random = new Unity.Mathematics.Random(combatState.ValueRO.RandomState == 0u ? 0x6E624EB7u : combatState.ValueRO.RandomState);

            foreach (var damage in SystemAPI.Query<RefRO<MorrowindDamageAppliedEvent>>())
            {
                ApplyAftermath(
                    damage.ValueRO,
                    knockDownMult,
                    knockDownOddsMult,
                    knockDownOddsBase,
                    ref random);
            }

            combatState.ValueRW.RandomState = random.state == 0u ? 0x6E624EB7u : random.state;
        }

        void ApplyAftermath(
            in MorrowindDamageAppliedEvent damage,
            float knockDownMult,
            int knockDownOddsMult,
            int knockDownOddsBase,
            ref Unity.Mathematics.Random random)
        {
            Entity target = damage.Target;
            RequireTargetAftermathComposition(target);

            var vitals = EntityManager.GetComponentData<ActorVitalSet>(target);
            var aftermath = EntityManager.GetComponentData<ActorHitAftermathState>(target);

            if (aftermath.Dead != 0 && vitals.CurrentHealth > 0f)
                throw new InvalidOperationException($"[VVardenfell][Aftermath] Actor ref={PlacedRefId(target)} is marked dead but still has positive health.");

            bool changed = false;
            if (aftermath.Dead == 0 && damage.Amount > 0f && damage.Attacker != Entity.Null)
            {
                if (damage.TargetVital == MorrowindDamageTargetVital.Fatigue
                    && aftermath.KnockedOut == 0
                    && aftermath.KnockedDown == 0
                    && (vitals.CurrentFatigue < 0f || vitals.ModifiedFatigueBase <= 0f))
                {
                    aftermath.KnockedOut = 1;
                    aftermath.KnockedDown = 1;
                    aftermath.HitRecovery = 0;
                    changed = true;
                }
                else if (damage.TargetVital == MorrowindDamageTargetVital.Health)
                {
                    if (damage.SourceKind != MorrowindDamageSourceKind.ElementalShield
                        && aftermath.KnockedDown == 0
                        && aftermath.KnockedOut == 0)
                    {
                        var attributes = EntityManager.GetComponentData<ActorAttributeSet>(target);
                        float agility = math.max(0f, attributes.Agility);
                        float agilityTerm = agility * knockDownMult;
                        float knockdownTerm = agility * knockDownOddsMult * 0.01f + knockDownOddsBase;
                        int roll = random.NextInt(100);

                        if (agilityTerm <= damage.Amount && knockdownTerm <= roll)
                        {
                            aftermath.KnockedDown = 1;
                            aftermath.HitRecovery = 0;
                        }
                        else
                        {
                            aftermath.HitRecovery = 1;
                        }

                        changed = true;
                    }
                }
            }

            bool markedDead = false;
            if (vitals.CurrentHealth <= 0f && aftermath.Dead == 0)
            {
                ActorHitAftermathStateUtility.MarkDead(ref aftermath);
                markedDead = true;
                changed = true;
                StopCombatIfPresent(target);
            }

            if (changed)
            {
                if (!markedDead)
                    ActorHitAftermathStateUtility.BumpSequence(ref aftermath);
                EntityManager.SetComponentData(target, aftermath);
            }
        }

        void RequireTargetAftermathComposition(Entity target)
        {
            if (target == Entity.Null || !EntityManager.Exists(target))
                throw new InvalidOperationException("[VVardenfell][Aftermath] Damage target entity is missing.");
            if (!EntityManager.HasComponent<ActorVitalSet>(target))
                throw new InvalidOperationException($"[VVardenfell][Aftermath] Actor ref={PlacedRefId(target)} has no ActorVitalSet.");
            if (!EntityManager.HasComponent<ActorAttributeSet>(target))
                throw new InvalidOperationException($"[VVardenfell][Aftermath] Actor ref={PlacedRefId(target)} has no ActorAttributeSet.");
            if (!EntityManager.HasComponent<ActorHitAftermathState>(target))
                throw new InvalidOperationException($"[VVardenfell][Aftermath] Actor ref={PlacedRefId(target)} has no ActorHitAftermathState.");
            if (!EntityManager.HasComponent<ActorScriptEventState>(target))
                throw new InvalidOperationException($"[VVardenfell][Aftermath] Actor ref={PlacedRefId(target)} has no ActorScriptEventState.");
        }

        void StopCombatIfPresent(Entity actor)
        {
            if (EntityManager.HasComponent<ActorCombatTargetState>(actor))
            {
                var combat = EntityManager.GetComponentData<ActorCombatTargetState>(actor);
                combat.Active = 0;
                combat.TargetEntity = Entity.Null;
                combat.TargetPlacedRefId = 0u;
                EntityManager.SetComponentData(actor, combat);
            }

            MorrowindCombatTargetUtility.TryStopCombat(EntityManager, actor);
        }
        uint PlacedRefId(Entity entity)
            => EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
    }
}


