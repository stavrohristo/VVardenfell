using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindDamageSystemGroup))]
    [UpdateAfter(typeof(PlayerMeleeHitSystem))]
    [UpdateBefore(typeof(MorrowindNormalWeaponResistanceSystem))]
    public partial class MorrowindMeleeDamageRollSystem : SystemBase
    {
        static readonly short ParalyzeEffectId = RequireEffectId("sEffectParalyze");

        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindMeleeHitEvent>();
            RequireForUpdate<MorrowindCombatRuntimeState>();
        }

        protected override void OnUpdate()
        {
            RuntimeContentDatabase contentDb = RuntimeContentDatabase.Active
                ?? throw new InvalidOperationException("[VVardenfell][Damage] Runtime content database is not loaded.");

            ref var combatState = ref SystemAPI.GetSingletonRW<MorrowindCombatRuntimeState>().ValueRW;
            var random = new Unity.Mathematics.Random(combatState.RandomState == 0u ? 0x6E624EB7u : combatState.RandomState);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (hit, entity) in
                     SystemAPI.Query<RefRO<MorrowindMeleeHitEvent>>()
                         .WithEntityAccess())
            {
                var pendingDamage = ResolveDamage(contentDb, ref random, hit.ValueRO);
                ecb.RemoveComponent<MorrowindMeleeHitEvent>(entity);
                ecb.AddComponent(entity, pendingDamage);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
            combatState.RandomState = random.state == 0u ? 0x6E624EB7u : random.state;
        }

        MorrowindPendingDamageEvent ResolveDamage(
            RuntimeContentDatabase contentDb,
            ref Unity.Mathematics.Random random,
            in MorrowindMeleeHitEvent hit)
        {
            Entity attacker = hit.Attacker;
            Entity target = hit.Target;
            RequireAttackerCombatComposition(attacker);
            RequireTargetCombatComposition(target);
            if (!EntityManager.HasComponent<ActorHitAftermathState>(target))
                throw new InvalidOperationException($"[VVardenfell][Damage] Melee hit target ref={PlacedRefId(target)} has no ActorHitAftermathState.");

            var attackerAttributes = EntityManager.GetComponentData<ActorAttributeSet>(attacker);
            var attackerSkills = EntityManager.GetComponentData<ActorSkillSet>(attacker);
            var attackerVitals = EntityManager.GetComponentData<ActorVitalSet>(attacker);
            var attackerEffects = EntityManager.GetBuffer<ActorActiveMagicEffect>(attacker, true);
            var targetAttributes = EntityManager.GetComponentData<ActorAttributeSet>(target);
            var targetVitals = EntityManager.GetComponentData<ActorVitalSet>(target);
            var targetEffects = EntityManager.GetBuffer<ActorActiveMagicEffect>(target, true);
            bool targetInCombat = EntityManager.HasComponent<ActorCombatTargetState>(target)
                                  && EntityManager.GetComponentData<ActorCombatTargetState>(target).Active != 0;

            MorrowindMeleeCombatMechanics.ResolveWeaponEquipment(
                contentDb,
                hit.WeaponContent,
                out bool hasWeapon,
                out ItemDefHandle weaponHandle,
                out ItemEquipmentDef weapon,
                out bool weaponHasEnchantment);

            float hitChance = MorrowindMeleeCombatMechanics.ComputeHitChance(
                contentDb,
                attackerAttributes,
                attackerSkills,
                attackerVitals,
                attackerEffects,
                hasWeapon,
                weapon,
                targetAttributes,
                targetVitals,
                targetEffects,
                targetInCombat);
            bool fullDamage = MorrowindMeleeCombatMechanics.IsFullDamageHit((uint)random.NextInt(100), hitChance);

            if (hasWeapon)
            {
                float amount = MorrowindMeleeCombatMechanics.ComputeWeaponDamage(
                    contentDb,
                    attackerAttributes,
                    weapon,
                    hit.AttackType,
                    fullDamage);
                return new MorrowindPendingDamageEvent
                {
                    Attacker = attacker,
                    Target = target,
                    SourceContent = hit.WeaponContent,
                    Amount = amount,
                    AttackStrength = hit.AttackStrength,
                    TargetVital = MorrowindDamageTargetVital.Health,
                    SourceKind = MorrowindDamageSourceKind.Weapon,
                    NormalWeapon = MorrowindMeleeCombatMechanics.IsNormalWeaponDamage(weapon, weaponHasEnchantment) ? (byte)1 : (byte)0,
                    FullDamage = fullDamage ? (byte)1 : (byte)0,
                    HitPosition = hit.HitPosition,
                    HasHitPosition = hit.HasHitPosition,
                };
            }

            var aftermath = EntityManager.GetComponentData<ActorHitAftermathState>(target);
            bool healthDamage = aftermath.KnockedDown != 0
                                || aftermath.KnockedOut != 0
                                || MorrowindMeleeCombatMechanics.SumEffectMagnitude(targetEffects, ParalyzeEffectId) > 0f;
            float handToHandDamage = MorrowindMeleeCombatMechanics.ComputeHandToHandDamage(
                contentDb,
                attackerAttributes,
                attackerSkills,
                fullDamage,
                healthDamage);
            return new MorrowindPendingDamageEvent
            {
                Attacker = attacker,
                Target = target,
                SourceContent = default,
                Amount = handToHandDamage,
                AttackStrength = hit.AttackStrength,
                TargetVital = healthDamage ? MorrowindDamageTargetVital.Health : MorrowindDamageTargetVital.Fatigue,
                SourceKind = MorrowindDamageSourceKind.HandToHand,
                NormalWeapon = 0,
                FullDamage = fullDamage ? (byte)1 : (byte)0,
                HitPosition = hit.HitPosition,
                HasHitPosition = hit.HasHitPosition,
            };
        }

        void RequireAttackerCombatComposition(Entity actor)
        {
            if (actor == Entity.Null || !EntityManager.Exists(actor))
                throw new InvalidOperationException("[VVardenfell][Damage] Melee hit attacker entity is missing.");
            if (!EntityManager.HasComponent<ActorAttributeSet>(actor)
                || !EntityManager.HasComponent<ActorSkillSet>(actor)
                || !EntityManager.HasComponent<ActorVitalSet>(actor)
                || !EntityManager.HasBuffer<ActorActiveMagicEffect>(actor))
            {
                throw new InvalidOperationException($"[VVardenfell][Damage] Melee hit attacker ref={PlacedRefId(actor)} lacks required combat stats/effects.");
            }
        }

        void RequireTargetCombatComposition(Entity actor)
        {
            if (actor == Entity.Null || !EntityManager.Exists(actor))
                throw new InvalidOperationException("[VVardenfell][Damage] Melee hit target entity is missing.");
            if (!EntityManager.HasComponent<ActorAttributeSet>(actor)
                || !EntityManager.HasComponent<ActorVitalSet>(actor)
                || !EntityManager.HasBuffer<ActorActiveMagicEffect>(actor)
                || !EntityManager.HasComponent<ActorHitAftermathState>(actor))
            {
                throw new InvalidOperationException($"[VVardenfell][Damage] Melee hit target ref={PlacedRefId(actor)} lacks required combat stats/effects/aftermath state.");
            }
        }

        static short RequireEffectId(string gmstId)
        {
            if (!MorrowindMagicEffectTextUtility.TryResolveEffectId(gmstId, out short effectId))
                throw new InvalidOperationException($"[VVardenfell][Damage] Required magic effect GMST '{gmstId}' is not mapped.");
            return effectId;
        }

        uint PlacedRefId(Entity entity)
            => EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
    }
}
