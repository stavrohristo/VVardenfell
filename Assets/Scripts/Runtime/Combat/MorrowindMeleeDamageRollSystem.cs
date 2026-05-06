using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindDamageSystemGroup))]
    [UpdateAfter(typeof(ActorMeleeHitSystem))]
    [UpdateBefore(typeof(MorrowindNormalWeaponResistanceSystem))]
    public partial struct MorrowindMeleeDamageRollSystem : ISystem
    {
        static readonly short ParalyzeEffectId = RequireEffectId("sEffectParalyze");

        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindMeleeHitEvent>();
            systemState.RequireForUpdate<MorrowindCombatRuntimeState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][ContentBlob] Melee damage roll requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            ref var combatState = ref SystemAPI.GetSingletonRW<MorrowindCombatRuntimeState>().ValueRW;
            var random = new Unity.Mathematics.Random(combatState.RandomState == 0u ? 0x6E624EB7u : combatState.RandomState);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (hit, entity) in
                     SystemAPI.Query<RefRO<MorrowindMeleeHitEvent>>()
                         .WithEntityAccess())
            {
                var pendingDamage = ResolveDamage(ref systemState, ref content, ref random, hit.ValueRO);
                ecb.RemoveComponent<MorrowindMeleeHitEvent>(entity);
                ecb.AddComponent(entity, pendingDamage);
            }

            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
            combatState.RandomState = random.state == 0u ? 0x6E624EB7u : random.state;
        }

        MorrowindPendingDamageEvent ResolveDamage(ref SystemState systemState, 
            ref RuntimeContentBlob content,
            ref Unity.Mathematics.Random random,
            in MorrowindMeleeHitEvent hit)
        {
            Entity attacker = hit.Attacker;
            Entity target = hit.Target;
            RequireAttackerCombatComposition(ref systemState, attacker);
            RequireTargetCombatComposition(ref systemState, target);
            if (!systemState.EntityManager.HasComponent<ActorHitAftermathState>(target))
                throw new InvalidOperationException($"[VVardenfell][Damage] Melee hit target ref={PlacedRefId(ref systemState, target)} has no ActorHitAftermathState.");

            var attackerAttributes = systemState.EntityManager.GetComponentData<ActorAttributeSet>(attacker);
            var attackerSkills = systemState.EntityManager.GetComponentData<ActorSkillSet>(attacker);
            var attackerVitals = systemState.EntityManager.GetComponentData<ActorVitalSet>(attacker);
            var attackerEffects = systemState.EntityManager.GetBuffer<ActorActiveMagicEffect>(attacker, true);
            var targetAttributes = systemState.EntityManager.GetComponentData<ActorAttributeSet>(target);
            var targetVitals = systemState.EntityManager.GetComponentData<ActorVitalSet>(target);
            var targetEffects = systemState.EntityManager.GetBuffer<ActorActiveMagicEffect>(target, true);
            bool targetInCombat = systemState.EntityManager.HasComponent<ActorCombatTargetState>(target)
                                  && systemState.EntityManager.GetComponentData<ActorCombatTargetState>(target).Active != 0;

            MorrowindMeleeCombatMechanics.ResolveWeaponEquipment(
                ref content,
                hit.WeaponContent,
                out bool hasWeapon,
                out ItemDefHandle weaponHandle,
                out ItemEquipmentDef weapon,
                out bool weaponHasEnchantment);

            float hitChance = MorrowindMeleeCombatMechanics.ComputeHitChance(
                ref content,
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
                    ref content,
                    attackerAttributes,
                    weapon,
                    hit.AttackType,
                    fullDamage);
                amount *= MorrowindWeaponConditionUtility.ResolveEquippedConditionMultiplier(
                    ref content,
                    systemState.EntityManager,
                    attacker,
                    hit.WeaponContent,
                    weapon);
                MorrowindWeaponConditionUtility.ApplyWeaponConditionDamage(
                    ref content,
                    systemState.EntityManager,
                    attacker,
                    hit.WeaponContent,
                    weapon,
                    amount);
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

            var aftermath = systemState.EntityManager.GetComponentData<ActorHitAftermathState>(target);
            bool healthDamage = aftermath.KnockedDown != 0
                                || aftermath.KnockedOut != 0
                                || MorrowindMeleeCombatMechanics.SumEffectMagnitude(targetEffects, ParalyzeEffectId) > 0f;
            float handToHandDamage = MorrowindMeleeCombatMechanics.ComputeHandToHandDamage(
                ref content,
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

        void RequireAttackerCombatComposition(ref SystemState systemState, Entity actor)
        {
            if (actor == Entity.Null || !systemState.EntityManager.Exists(actor))
                throw new InvalidOperationException("[VVardenfell][Damage] Melee hit attacker entity is missing.");
            if (!systemState.EntityManager.HasComponent<ActorAttributeSet>(actor)
                || !systemState.EntityManager.HasComponent<ActorSkillSet>(actor)
                || !systemState.EntityManager.HasComponent<ActorVitalSet>(actor)
                || !systemState.EntityManager.HasBuffer<ActorActiveMagicEffect>(actor))
            {
                throw new InvalidOperationException($"[VVardenfell][Damage] Melee hit attacker ref={PlacedRefId(ref systemState, actor)} lacks required combat stats/effects.");
            }
        }

        void RequireTargetCombatComposition(ref SystemState systemState, Entity actor)
        {
            if (actor == Entity.Null || !systemState.EntityManager.Exists(actor))
                throw new InvalidOperationException("[VVardenfell][Damage] Melee hit target entity is missing.");
            if (!systemState.EntityManager.HasComponent<ActorAttributeSet>(actor)
                || !systemState.EntityManager.HasComponent<ActorVitalSet>(actor)
                || !systemState.EntityManager.HasBuffer<ActorActiveMagicEffect>(actor)
                || !systemState.EntityManager.HasComponent<ActorHitAftermathState>(actor))
            {
                throw new InvalidOperationException($"[VVardenfell][Damage] Melee hit target ref={PlacedRefId(ref systemState, actor)} lacks required combat stats/effects/aftermath state.");
            }
        }

        static short RequireEffectId(string gmstId)
        {
            if (!MorrowindMagicEffectTextUtility.TryResolveEffectId(gmstId, out short effectId))
                throw new InvalidOperationException($"[VVardenfell][Damage] Required magic effect GMST '{gmstId}' is not mapped.");
            return effectId;
        }

        uint PlacedRefId(ref SystemState systemState, Entity entity)
            => systemState.EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? systemState.EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
    }
}
