using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindDamageSystemGroup))]
    [UpdateAfter(typeof(MorrowindNormalWeaponResistanceSystem))]
    [UpdateBefore(typeof(MorrowindArmorDamageSystem))]
    public partial struct MorrowindBlockDamageSystem : ISystem
    {
        static readonly short ParalyzeEffectId = RequireEffectId("sEffectParalyze");

        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindPendingDamageEvent>();
            systemState.RequireForUpdate<MorrowindCombatRuntimeState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][Block] Block damage requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            ref var combatState = ref SystemAPI.GetSingletonRW<MorrowindCombatRuntimeState>().ValueRW;
            var random = new Unity.Mathematics.Random(combatState.RandomState == 0u ? 0x6E624EB7u : combatState.RandomState);

            foreach (var damage in SystemAPI.Query<RefRW<MorrowindPendingDamageEvent>>())
            {
                if (damage.ValueRO.Amount <= 0f || !IsMeleeDamage(damage.ValueRO.SourceKind))
                    continue;

                if (TryBlockDamage(ref systemState, ref content, ref random, damage.ValueRO, out var impact))
                {
                    damage.ValueRW.Amount = 0f;
                    damage.ValueRW.BlockImpact = impact;
                    TriggerBlockAnimation(ref systemState, impact.Target);
                    SpendBlockFatigue(ref systemState, ref content, damage.ValueRO, impact.Target);
                    MorrowindArmorDamageUtility.ApplyShieldBlockConditionDamage(
                        ref content,
                        systemState.EntityManager,
                        impact.Target,
                        impact.ShieldContent,
                        (int)math.floor(math.max(0f, impact.IncomingDamage)));
                }
            }

            combatState.RandomState = random.state == 0u ? 0x6E624EB7u : random.state;
        }

        bool TryBlockDamage(ref SystemState systemState, 
            ref RuntimeContentBlob content,
            ref Unity.Mathematics.Random random,
            in MorrowindPendingDamageEvent damage,
            out MorrowindBlockImpact impact)
        {
            impact = default;
            Entity target = damage.Target;
            Entity attacker = damage.Attacker;
            RequireParticipant(ref systemState, target, "target");
            RequireParticipant(ref systemState, attacker, "attacker");

            if (!systemState.EntityManager.HasComponent<ActorHitAftermathState>(target))
                throw new InvalidOperationException($"[VVardenfell][Block] Target ref={PlacedRefId(ref systemState, target)} has no ActorHitAftermathState.");
            var aftermath = systemState.EntityManager.GetComponentData<ActorHitAftermathState>(target);
            if (ActorHitAftermathStateUtility.IsDead(systemState.EntityManager, target)
                || aftermath.KnockedDown != 0
                || aftermath.KnockedOut != 0
                || aftermath.HitRecovery != 0)
            {
                return false;
            }

            if (!systemState.EntityManager.HasBuffer<ActorActiveMagicEffect>(target))
                throw new InvalidOperationException($"[VVardenfell][Block] Target ref={PlacedRefId(ref systemState, target)} has no ActorActiveMagicEffect buffer.");
            var targetEffects = systemState.EntityManager.GetBuffer<ActorActiveMagicEffect>(target, true);
            if (MorrowindMeleeCombatMechanics.SumEffectMagnitude(targetEffects, ParalyzeEffectId) > 0f)
                return false;

            if (!TryResolveShield(ref systemState, ref content, target, out var shieldSlot, out var shield))
                return false;

            if (!IsCarriedLeftVisibleForBlock(ref systemState, target))
                return false;

            float angleDegrees = ComputeBlockAngleDegrees(ref systemState, attacker, target);
            float leftAngle = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fCombatBlockLeftAngle);
            float rightAngle = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fCombatBlockRightAngle);
            if (angleDegrees < leftAngle || angleDegrees > rightAngle)
                return false;

            if (!systemState.EntityManager.HasComponent<ActorAttributeSet>(target)
                || !systemState.EntityManager.HasComponent<ActorSkillSet>(target)
                || !systemState.EntityManager.HasComponent<ActorVitalSet>(target))
            {
                throw new InvalidOperationException($"[VVardenfell][Block] Target ref={PlacedRefId(ref systemState, target)} lacks required stats.");
            }
            if (!systemState.EntityManager.HasComponent<ActorAttributeSet>(attacker)
                || !systemState.EntityManager.HasComponent<ActorSkillSet>(attacker)
                || !systemState.EntityManager.HasComponent<ActorVitalSet>(attacker))
            {
                throw new InvalidOperationException($"[VVardenfell][Block] Attacker ref={PlacedRefId(ref systemState, attacker)} lacks required stats.");
            }

            var targetAttributes = systemState.EntityManager.GetComponentData<ActorAttributeSet>(target);
            var targetSkills = systemState.EntityManager.GetComponentData<ActorSkillSet>(target);
            var targetVitals = systemState.EntityManager.GetComponentData<ActorVitalSet>(target);
            var attackerAttributes = systemState.EntityManager.GetComponentData<ActorAttributeSet>(attacker);
            var attackerSkills = systemState.EntityManager.GetComponentData<ActorSkillSet>(attacker);
            var attackerVitals = systemState.EntityManager.GetComponentData<ActorVitalSet>(attacker);

            float blockTerm = targetSkills.Block + 0.2f * targetAttributes.Agility + 0.1f * targetAttributes.Luck;
            float swingTerm = math.saturate(damage.AttackStrength)
                              * RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fSwingBlockMult)
                              + RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fSwingBlockBase);
            float blockerTerm = blockTerm * swingTerm;
            if (IsNotMovingForward(ref systemState, target))
                blockerTerm *= RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fBlockStillBonus);
            blockerTerm *= MorrowindMeleeCombatMechanics.ComputeFatigueTerm(ref content, targetVitals);

            MorrowindMeleeCombatMechanics.ResolveWeaponEquipment(
                ref content,
                damage.SourceKind == MorrowindDamageSourceKind.Weapon ? damage.SourceContent : default,
                out bool hasWeapon,
                out _,
                out var weapon,
                out _);
            float attackerSkill = MorrowindMeleeCombatMechanics.ResolveWeaponSkill(attackerSkills, hasWeapon ? weapon.Type : -1);
            float attackerTerm = attackerSkill + 0.2f * attackerAttributes.Agility + 0.1f * attackerAttributes.Luck;
            attackerTerm *= MorrowindMeleeCombatMechanics.ComputeFatigueTerm(ref content, attackerVitals);

            int minChance = RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iBlockMinChance);
            int maxChance = RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentKnownHashes.iBlockMaxChance);
            int chance = math.clamp((int)(blockerTerm - attackerTerm), minChance, maxChance);
            int roll = random.NextInt(100);
            if (roll >= chance)
                return false;

            impact = new MorrowindBlockImpact
            {
                Target = target,
                ShieldContent = shieldSlot.Content,
                ShieldSkill = MorrowindArmorDamageUtility.ResolveArmorSkillKind(ref content, shield),
                IncomingDamage = damage.Amount,
                Chance = chance,
                Roll = roll,
                Blocked = 1,
            };
            return true;
        }

        static bool IsMeleeDamage(MorrowindDamageSourceKind sourceKind)
            => sourceKind == MorrowindDamageSourceKind.Weapon
               || sourceKind == MorrowindDamageSourceKind.HandToHand;

        void RequireParticipant(ref SystemState systemState, Entity entity, string role)
        {
            if (entity == Entity.Null || !systemState.EntityManager.Exists(entity))
                throw new InvalidOperationException($"[VVardenfell][Block] Damage {role} entity is missing.");
        }

        bool TryResolveShield(ref SystemState systemState, 
            ref RuntimeContentBlob content,
            Entity target,
            out ActorEquipmentSlot shieldSlot,
            out ItemEquipmentDef shield)
        {
            shieldSlot = default;
            shield = default;
            if (!systemState.EntityManager.HasBuffer<ActorEquipmentSlot>(target))
            {
                if (IsCreature(ref systemState, ref content, target))
                    return false;

                throw new InvalidOperationException($"[VVardenfell][Block] Non-creature target ref={PlacedRefId(ref systemState, target)} has no ActorEquipmentSlot buffer.");
            }

            var equipment = systemState.EntityManager.GetBuffer<ActorEquipmentSlot>(target, true);
            return MorrowindArmorDamageUtility.TryGetEquippedArmor(
                ref content,
                equipment,
                ItemEquipmentSlot.Shield,
                out shieldSlot,
                out shield);
        }

        bool IsCreature(ref SystemState systemState, ref RuntimeContentBlob content, Entity actor)
        {
            if (systemState.EntityManager.HasComponent<PlayerTag>(actor))
                return false;
            if (!systemState.EntityManager.HasComponent<ActorSpawnSource>(actor))
                throw new InvalidOperationException($"[VVardenfell][Block] Actor ref={PlacedRefId(ref systemState, actor)} has no ActorSpawnSource.");

            var source = systemState.EntityManager.GetComponentData<ActorSpawnSource>(actor);
            if (!source.Definition.IsValid)
                throw new InvalidOperationException($"[VVardenfell][Block] Actor ref={PlacedRefId(ref systemState, actor)} has invalid actor definition.");

            ref RuntimeActorDefBlob actorDef = ref RuntimeContentBlobUtility.Get(ref content, source.Definition);
            return actorDef.Kind == ActorDefKind.Creature;
        }

        bool IsCarriedLeftVisibleForBlock(ref SystemState systemState, Entity target)
        {
            if (systemState.EntityManager.HasComponent<PlayerTag>(target))
                return IsPlayerCarriedLeftVisibleForBlock(ref systemState, target);

            if (!systemState.EntityManager.HasComponent<ActorWeaponAnimationState>(target))
                return true;

            var weaponState = systemState.EntityManager.GetComponentData<ActorWeaponAnimationState>(target);
            if (weaponState.Drawn == 0 && weaponState.Phase != ActorWeaponAnimationPhase.Equipping)
                return true;

            return !IsTwoHandedPresentedWeaponType(weaponState.WeaponType);
        }

        bool IsPlayerCarriedLeftVisibleForBlock(ref SystemState systemState, Entity player)
        {
            if (!systemState.EntityManager.HasComponent<LocalPlayerPresentationState>(player))
                throw new InvalidOperationException("[VVardenfell][Block] Player target has no LocalPlayerPresentationState.");

            var presentation = systemState.EntityManager.GetComponentData<LocalPlayerPresentationState>(player);
            Entity visual = presentation.Mode == PlayerViewMode.FirstPerson
                ? presentation.FirstPersonVisual
                : presentation.ThirdPersonVisual;
            if (visual == Entity.Null || !systemState.EntityManager.Exists(visual))
                throw new InvalidOperationException("[VVardenfell][Block] Player target active visual is missing.");
            if (!systemState.EntityManager.HasComponent<ActorWeaponAnimationState>(visual))
                throw new InvalidOperationException("[VVardenfell][Block] Player target active visual has no ActorWeaponAnimationState.");

            var weaponState = systemState.EntityManager.GetComponentData<ActorWeaponAnimationState>(visual);
            if (weaponState.Drawn == 0 && weaponState.Phase != ActorWeaponAnimationPhase.Equipping)
                return true;

            return !IsTwoHandedPresentedWeaponType(weaponState.WeaponType);
        }

        static bool IsTwoHandedPresentedWeaponType(int weaponType)
            => weaponType == ActorWeaponAnimationUtility.NoWeaponType || IsTwoHandedWeaponType(weaponType);

        static bool IsTwoHandedWeaponType(int weaponType)
            => weaponType is 2 or 4 or 5 or 6 or 8;

        float ComputeBlockAngleDegrees(ref SystemState systemState, Entity attacker, Entity target)
        {
            if (!systemState.EntityManager.HasComponent<LocalTransform>(attacker))
                throw new InvalidOperationException($"[VVardenfell][Block] Attacker ref={PlacedRefId(ref systemState, attacker)} has no LocalTransform.");
            if (!systemState.EntityManager.HasComponent<LocalTransform>(target))
                throw new InvalidOperationException($"[VVardenfell][Block] Target ref={PlacedRefId(ref systemState, target)} has no LocalTransform.");

            var attackerTransform = systemState.EntityManager.GetComponentData<LocalTransform>(attacker);
            var targetTransform = systemState.EntityManager.GetComponentData<LocalTransform>(target);
            float3 toAttacker = attackerTransform.Position - targetTransform.Position;
            toAttacker.y = 0f;
            float lengthSq = math.lengthsq(toAttacker);
            if (lengthSq <= 0.000001f)
                return 0f;

            float3 local = math.mul(math.inverse(targetTransform.Rotation), math.normalize(toAttacker));
            return math.degrees(math.atan2(local.x, local.z));
        }

        bool IsNotMovingForward(ref SystemState systemState, Entity target)
        {
            if (!systemState.EntityManager.HasComponent<MorrowindMovementState>(target))
                throw new InvalidOperationException($"[VVardenfell][Block] Target ref={PlacedRefId(ref systemState, target)} has no MorrowindMovementState.");

            return systemState.EntityManager.GetComponentData<MorrowindMovementState>(target).LocalMove.y <= 0f;
        }

        static float ResolveAttackStrength(in MorrowindPendingDamageEvent damage)
        {
            if (damage.SourceKind != MorrowindDamageSourceKind.Weapon
                && damage.SourceKind != MorrowindDamageSourceKind.HandToHand)
            {
                return 0f;
            }

            return math.saturate(damage.AttackStrength);
        }

        void SpendBlockFatigue(ref SystemState systemState, ref RuntimeContentBlob content, in MorrowindPendingDamageEvent damage, Entity target)
        {
            if (!systemState.EntityManager.HasComponent<ActorVitalSet>(target))
                throw new InvalidOperationException($"[VVardenfell][Block] Target ref={PlacedRefId(ref systemState, target)} has no ActorVitalSet.");
            if (!systemState.EntityManager.HasComponent<ActorDerivedMovementStats>(target))
                throw new InvalidOperationException($"[VVardenfell][Block] Target ref={PlacedRefId(ref systemState, target)} has no ActorDerivedMovementStats.");

            var vitals = systemState.EntityManager.GetComponentData<ActorVitalSet>(target);
            var derived = systemState.EntityManager.GetComponentData<ActorDerivedMovementStats>(target);
            float fatigueLoss = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fFatigueBlockBase)
                                + math.saturate(derived.NormalizedEncumbrance) * RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fFatigueBlockMult);

            if (damage.SourceKind == MorrowindDamageSourceKind.Weapon)
            {
                MorrowindMeleeCombatMechanics.ResolveWeaponEquipment(
                    ref content,
                    damage.SourceContent,
                    out bool hasWeapon,
                    out _,
                    out var weapon,
                    out _);
                if (!hasWeapon)
                    throw new InvalidOperationException("[VVardenfell][Block] Weapon damage has no weapon equipment.");

                fatigueLoss += weapon.Weight * math.saturate(ResolveAttackStrength(damage)) * RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fWeaponFatigueBlockMult);
            }

            vitals.CurrentFatigue -= fatigueLoss;
            systemState.EntityManager.SetComponentData(target, vitals);
        }

        void TriggerBlockAnimation(ref SystemState systemState, Entity target)
        {
            if (!systemState.EntityManager.HasComponent<ActorBlockState>(target))
                throw new InvalidOperationException($"[VVardenfell][Block] Target ref={PlacedRefId(ref systemState, target)} has no ActorBlockState.");

            var state = systemState.EntityManager.GetComponentData<ActorBlockState>(target);
            state.Active = 1;
            state.Sequence++;
            if (state.Sequence == 0u)
                state.Sequence = 1u;
            systemState.EntityManager.SetComponentData(target, state);
        }

        static short RequireEffectId(string gmstId)
        {
            if (!MorrowindMagicEffectTextUtility.TryResolveEffectId(gmstId, out short effectId))
                throw new InvalidOperationException($"[VVardenfell][Block] Required magic effect GMST '{gmstId}' is not mapped.");
            return effectId;
        }

        uint PlacedRefId(ref SystemState systemState, Entity entity)
            => systemState.EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? systemState.EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
    }
}


