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
    public partial class MorrowindBlockDamageSystem : SystemBase
    {
        static readonly short ParalyzeEffectId = RequireEffectId("sEffectParalyze");

        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindPendingDamageEvent>();
            RequireForUpdate<MorrowindCombatRuntimeState>();
        }

        protected override void OnUpdate()
        {
            RuntimeContentDatabase contentDb = RuntimeContentDatabase.Active
                ?? throw new InvalidOperationException("[VVardenfell][Block] Runtime content database is not loaded.");

            ref var combatState = ref SystemAPI.GetSingletonRW<MorrowindCombatRuntimeState>().ValueRW;
            var random = new Unity.Mathematics.Random(combatState.RandomState == 0u ? 0x6E624EB7u : combatState.RandomState);

            foreach (var damage in SystemAPI.Query<RefRW<MorrowindPendingDamageEvent>>())
            {
                if (damage.ValueRO.Amount <= 0f || !IsMeleeDamage(damage.ValueRO.SourceKind))
                    continue;

                if (TryBlockDamage(contentDb, ref random, damage.ValueRO, out var impact))
                {
                    damage.ValueRW.Amount = 0f;
                    damage.ValueRW.BlockImpact = impact;
                    TriggerBlockAnimation(impact.Target);
                    SpendBlockFatigue(contentDb, damage.ValueRO, impact.Target);
                    MorrowindArmorDamageUtility.ApplyShieldBlockConditionDamage(
                        contentDb,
                        EntityManager,
                        impact.Target,
                        impact.ShieldContent,
                        (int)math.floor(math.max(0f, impact.IncomingDamage)));
                }
            }

            combatState.RandomState = random.state == 0u ? 0x6E624EB7u : random.state;
        }

        bool TryBlockDamage(
            RuntimeContentDatabase contentDb,
            ref Unity.Mathematics.Random random,
            in MorrowindPendingDamageEvent damage,
            out MorrowindBlockImpact impact)
        {
            impact = default;
            Entity target = damage.Target;
            Entity attacker = damage.Attacker;
            RequireParticipant(target, "target");
            RequireParticipant(attacker, "attacker");

            if (!EntityManager.HasComponent<ActorHitAftermathState>(target))
                throw new InvalidOperationException($"[VVardenfell][Block] Target ref={PlacedRefId(target)} has no ActorHitAftermathState.");
            var aftermath = EntityManager.GetComponentData<ActorHitAftermathState>(target);
            if (aftermath.Dead != 0
                || aftermath.KnockedDown != 0
                || aftermath.KnockedOut != 0
                || aftermath.HitRecovery != 0)
            {
                return false;
            }

            if (!EntityManager.HasBuffer<ActorActiveMagicEffect>(target))
                throw new InvalidOperationException($"[VVardenfell][Block] Target ref={PlacedRefId(target)} has no ActorActiveMagicEffect buffer.");
            var targetEffects = EntityManager.GetBuffer<ActorActiveMagicEffect>(target, true);
            if (MorrowindMeleeCombatMechanics.SumEffectMagnitude(targetEffects, ParalyzeEffectId) > 0f)
                return false;

            if (!TryResolveShield(contentDb, target, out var shieldSlot, out var shield))
                return false;

            if (!IsCarriedLeftVisibleForBlock(target))
                return false;

            float angleDegrees = ComputeBlockAngleDegrees(attacker, target);
            float leftAngle = contentDb.RequireGameSettingFloat("fCombatBlockLeftAngle");
            float rightAngle = contentDb.RequireGameSettingFloat("fCombatBlockRightAngle");
            if (angleDegrees < leftAngle || angleDegrees > rightAngle)
                return false;

            if (!EntityManager.HasComponent<ActorAttributeSet>(target)
                || !EntityManager.HasComponent<ActorSkillSet>(target)
                || !EntityManager.HasComponent<ActorVitalSet>(target))
            {
                throw new InvalidOperationException($"[VVardenfell][Block] Target ref={PlacedRefId(target)} lacks required stats.");
            }
            if (!EntityManager.HasComponent<ActorAttributeSet>(attacker)
                || !EntityManager.HasComponent<ActorSkillSet>(attacker)
                || !EntityManager.HasComponent<ActorVitalSet>(attacker))
            {
                throw new InvalidOperationException($"[VVardenfell][Block] Attacker ref={PlacedRefId(attacker)} lacks required stats.");
            }

            var targetAttributes = EntityManager.GetComponentData<ActorAttributeSet>(target);
            var targetSkills = EntityManager.GetComponentData<ActorSkillSet>(target);
            var targetVitals = EntityManager.GetComponentData<ActorVitalSet>(target);
            var attackerAttributes = EntityManager.GetComponentData<ActorAttributeSet>(attacker);
            var attackerSkills = EntityManager.GetComponentData<ActorSkillSet>(attacker);
            var attackerVitals = EntityManager.GetComponentData<ActorVitalSet>(attacker);

            float blockTerm = targetSkills.Block + 0.2f * targetAttributes.Agility + 0.1f * targetAttributes.Luck;
            float swingTerm = math.saturate(damage.AttackStrength)
                              * contentDb.RequireGameSettingFloat("fSwingBlockMult")
                              + contentDb.RequireGameSettingFloat("fSwingBlockBase");
            float blockerTerm = blockTerm * swingTerm;
            if (IsNotMovingForward(target))
                blockerTerm *= contentDb.RequireGameSettingFloat("fBlockStillBonus");
            blockerTerm *= MorrowindMeleeCombatMechanics.ComputeFatigueTerm(contentDb, targetVitals);

            MorrowindMeleeCombatMechanics.ResolveWeaponEquipment(
                contentDb,
                damage.SourceKind == MorrowindDamageSourceKind.Weapon ? damage.SourceContent : default,
                out bool hasWeapon,
                out _,
                out var weapon,
                out _);
            float attackerSkill = MorrowindMeleeCombatMechanics.ResolveWeaponSkill(attackerSkills, hasWeapon ? weapon.Type : -1);
            float attackerTerm = attackerSkill + 0.2f * attackerAttributes.Agility + 0.1f * attackerAttributes.Luck;
            attackerTerm *= MorrowindMeleeCombatMechanics.ComputeFatigueTerm(contentDb, attackerVitals);

            int minChance = contentDb.RequireGameSettingInt("iBlockMinChance");
            int maxChance = contentDb.RequireGameSettingInt("iBlockMaxChance");
            int chance = math.clamp((int)(blockerTerm - attackerTerm), minChance, maxChance);
            int roll = random.NextInt(100);
            if (roll >= chance)
                return false;

            impact = new MorrowindBlockImpact
            {
                Target = target,
                ShieldContent = shieldSlot.Content,
                ShieldSkill = MorrowindArmorDamageUtility.ResolveArmorSkillKind(contentDb, shield),
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

        void RequireParticipant(Entity entity, string role)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity))
                throw new InvalidOperationException($"[VVardenfell][Block] Damage {role} entity is missing.");
        }

        bool TryResolveShield(
            RuntimeContentDatabase contentDb,
            Entity target,
            out ActorEquipmentSlot shieldSlot,
            out ItemEquipmentDef shield)
        {
            shieldSlot = default;
            shield = default;
            if (!EntityManager.HasBuffer<ActorEquipmentSlot>(target))
            {
                if (IsCreature(contentDb, target))
                    return false;

                throw new InvalidOperationException($"[VVardenfell][Block] Non-creature target ref={PlacedRefId(target)} has no ActorEquipmentSlot buffer.");
            }

            var equipment = EntityManager.GetBuffer<ActorEquipmentSlot>(target, true);
            return MorrowindArmorDamageUtility.TryGetEquippedArmor(
                contentDb,
                equipment,
                ItemEquipmentSlot.Shield,
                out shieldSlot,
                out shield);
        }

        bool IsCreature(RuntimeContentDatabase contentDb, Entity actor)
        {
            if (EntityManager.HasComponent<PlayerTag>(actor))
                return false;
            if (!EntityManager.HasComponent<ActorSpawnSource>(actor))
                throw new InvalidOperationException($"[VVardenfell][Block] Actor ref={PlacedRefId(actor)} has no ActorSpawnSource.");

            var source = EntityManager.GetComponentData<ActorSpawnSource>(actor);
            if (!source.Definition.IsValid)
                throw new InvalidOperationException($"[VVardenfell][Block] Actor ref={PlacedRefId(actor)} has invalid actor definition.");

            ref readonly var actorDef = ref contentDb.Get(source.Definition);
            return actorDef.Kind == ActorDefKind.Creature;
        }

        bool IsCarriedLeftVisibleForBlock(Entity target)
        {
            if (EntityManager.HasComponent<PlayerTag>(target))
                return IsPlayerCarriedLeftVisibleForBlock(target);

            if (!EntityManager.HasComponent<ActorWeaponAnimationState>(target))
                return true;

            var weaponState = EntityManager.GetComponentData<ActorWeaponAnimationState>(target);
            if (weaponState.Drawn == 0 && weaponState.Phase != ActorWeaponAnimationPhase.Equipping)
                return true;

            return !IsTwoHandedPresentedWeaponType(weaponState.WeaponType);
        }

        bool IsPlayerCarriedLeftVisibleForBlock(Entity player)
        {
            if (!EntityManager.HasComponent<LocalPlayerPresentationState>(player))
                throw new InvalidOperationException("[VVardenfell][Block] Player target has no LocalPlayerPresentationState.");

            var presentation = EntityManager.GetComponentData<LocalPlayerPresentationState>(player);
            Entity visual = presentation.Mode == PlayerViewMode.FirstPerson
                ? presentation.FirstPersonVisual
                : presentation.ThirdPersonVisual;
            if (visual == Entity.Null || !EntityManager.Exists(visual))
                throw new InvalidOperationException("[VVardenfell][Block] Player target active visual is missing.");
            if (!EntityManager.HasComponent<ActorWeaponAnimationState>(visual))
                throw new InvalidOperationException("[VVardenfell][Block] Player target active visual has no ActorWeaponAnimationState.");

            var weaponState = EntityManager.GetComponentData<ActorWeaponAnimationState>(visual);
            if (weaponState.Drawn == 0 && weaponState.Phase != ActorWeaponAnimationPhase.Equipping)
                return true;

            return !IsTwoHandedPresentedWeaponType(weaponState.WeaponType);
        }

        static bool IsTwoHandedPresentedWeaponType(int weaponType)
            => weaponType == ActorWeaponAnimationUtility.NoWeaponType || IsTwoHandedWeaponType(weaponType);

        static bool IsTwoHandedWeaponType(int weaponType)
            => weaponType is 2 or 4 or 5 or 6 or 8;

        float ComputeBlockAngleDegrees(Entity attacker, Entity target)
        {
            if (!EntityManager.HasComponent<LocalTransform>(attacker))
                throw new InvalidOperationException($"[VVardenfell][Block] Attacker ref={PlacedRefId(attacker)} has no LocalTransform.");
            if (!EntityManager.HasComponent<LocalTransform>(target))
                throw new InvalidOperationException($"[VVardenfell][Block] Target ref={PlacedRefId(target)} has no LocalTransform.");

            var attackerTransform = EntityManager.GetComponentData<LocalTransform>(attacker);
            var targetTransform = EntityManager.GetComponentData<LocalTransform>(target);
            float3 toAttacker = attackerTransform.Position - targetTransform.Position;
            toAttacker.y = 0f;
            float lengthSq = math.lengthsq(toAttacker);
            if (lengthSq <= 0.000001f)
                return 0f;

            float3 local = math.mul(math.inverse(targetTransform.Rotation), math.normalize(toAttacker));
            return math.degrees(math.atan2(local.x, local.z));
        }

        bool IsNotMovingForward(Entity target)
        {
            if (!EntityManager.HasComponent<MorrowindMovementState>(target))
                throw new InvalidOperationException($"[VVardenfell][Block] Target ref={PlacedRefId(target)} has no MorrowindMovementState.");

            return EntityManager.GetComponentData<MorrowindMovementState>(target).LocalMove.y <= 0f;
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

        void SpendBlockFatigue(RuntimeContentDatabase contentDb, in MorrowindPendingDamageEvent damage, Entity target)
        {
            if (!EntityManager.HasComponent<ActorVitalSet>(target))
                throw new InvalidOperationException($"[VVardenfell][Block] Target ref={PlacedRefId(target)} has no ActorVitalSet.");
            if (!EntityManager.HasComponent<ActorDerivedMovementStats>(target))
                throw new InvalidOperationException($"[VVardenfell][Block] Target ref={PlacedRefId(target)} has no ActorDerivedMovementStats.");

            var vitals = EntityManager.GetComponentData<ActorVitalSet>(target);
            var derived = EntityManager.GetComponentData<ActorDerivedMovementStats>(target);
            float fatigueLoss = contentDb.RequireGameSettingFloat("fFatigueBlockBase")
                                + math.saturate(derived.NormalizedEncumbrance) * contentDb.RequireGameSettingFloat("fFatigueBlockMult");

            if (damage.SourceKind == MorrowindDamageSourceKind.Weapon)
            {
                MorrowindMeleeCombatMechanics.ResolveWeaponEquipment(
                    contentDb,
                    damage.SourceContent,
                    out bool hasWeapon,
                    out _,
                    out var weapon,
                    out _);
                if (!hasWeapon)
                    throw new InvalidOperationException("[VVardenfell][Block] Weapon damage has no weapon equipment.");

                fatigueLoss += weapon.Weight * math.saturate(ResolveAttackStrength(damage)) * contentDb.RequireGameSettingFloat("fWeaponFatigueBlockMult");
            }

            vitals.CurrentFatigue -= fatigueLoss;
            EntityManager.SetComponentData(target, vitals);
        }

        void TriggerBlockAnimation(Entity target)
        {
            if (!EntityManager.HasComponent<ActorBlockState>(target))
                throw new InvalidOperationException($"[VVardenfell][Block] Target ref={PlacedRefId(target)} has no ActorBlockState.");

            var state = EntityManager.GetComponentData<ActorBlockState>(target);
            state.Active = 1;
            state.Sequence++;
            if (state.Sequence == 0u)
                state.Sequence = 1u;
            EntityManager.SetComponentData(target, state);
        }

        static short RequireEffectId(string gmstId)
        {
            if (!MorrowindMagicEffectTextUtility.TryResolveEffectId(gmstId, out short effectId))
                throw new InvalidOperationException($"[VVardenfell][Block] Required magic effect GMST '{gmstId}' is not mapped.");
            return effectId;
        }

        uint PlacedRefId(Entity entity)
            => EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
    }
}
