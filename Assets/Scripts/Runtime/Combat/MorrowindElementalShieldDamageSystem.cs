using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindDamageSystemGroup))]
    [UpdateAfter(typeof(MorrowindOnHitAftermathSystem))]
    [UpdateBefore(typeof(MorrowindHitAftermathStateSystem))]
    public partial class MorrowindElementalShieldDamageSystem : SystemBase
    {
        static readonly short FireShieldEffectId = RequireEffectId("sEffectFireShield");
        static readonly short LightningShieldEffectId = RequireEffectId("sEffectLightningShield");
        static readonly short FrostShieldEffectId = RequireEffectId("sEffectFrostShield");
        static readonly short ResistFireEffectId = RequireEffectId("sEffectResistFire");
        static readonly short ResistShockEffectId = RequireEffectId("sEffectResistShock");
        static readonly short ResistFrostEffectId = RequireEffectId("sEffectResistFrost");
        static readonly short WeaknessToFireEffectId = RequireEffectId("sEffectWeaknessToFire");
        static readonly short WeaknessToShockEffectId = RequireEffectId("sEffectWeaknessToShock");
        static readonly short WeaknessToFrostEffectId = RequireEffectId("sEffectWeaknessToFrost");

        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindDamageAppliedEvent>();
            RequireForUpdate<MorrowindCombatRuntimeState>();
            RequireForUpdate<MorrowindCombatSettings>();
            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<RuntimeContentBlobReference>();
        }

        protected override void OnUpdate()
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][ElementalShield] Elemental shield damage requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            float shieldMult = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fElementalShieldMult);
            float difficultyMult = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fDifficultyMult);
            if (difficultyMult <= 0f)
                throw new InvalidOperationException($"[VVardenfell][ElementalShield] GMST fDifficultyMult must be positive; got {difficultyMult}.");

            var settings = SystemAPI.GetSingleton<MorrowindCombatSettings>();
            if (settings.Difficulty < -100 || settings.Difficulty > 100)
                throw new InvalidOperationException($"[VVardenfell][ElementalShield] Difficulty {settings.Difficulty} is outside Morrowind's -100..100 range.");

            ref var combatState = ref SystemAPI.GetSingletonRW<MorrowindCombatRuntimeState>().ValueRW;
            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            uint randomState = combatState.RandomState == 0u ? 0x6E624EB7u : combatState.RandomState;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var damage in SystemAPI.Query<RefRO<MorrowindDamageAppliedEvent>>())
            {
                if (!CanTriggerElementalShield(damage.ValueRO))
                    continue;

                ApplyElementalShieldDamage(
                    damage.ValueRO,
                    shieldMult,
                    difficultyMult,
                    settings.Difficulty,
                    ref randomState,
                    ref shell,
                    ref ecb);
            }

            combatState.RandomState = randomState == 0u ? 0x6E624EB7u : randomState;
            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        bool CanTriggerElementalShield(in MorrowindDamageAppliedEvent damage)
            => damage.Amount > 0f
               && damage.TargetVital == MorrowindDamageTargetVital.Health
               && (damage.SourceKind == MorrowindDamageSourceKind.Weapon
                   || damage.SourceKind == MorrowindDamageSourceKind.HandToHand)
               && damage.Attacker != Entity.Null
               && damage.Target != Entity.Null
               && EntityManager.Exists(damage.Attacker)
               && EntityManager.Exists(damage.Target);

        void ApplyElementalShieldDamage(
            in MorrowindDamageAppliedEvent triggeringDamage,
            float shieldMult,
            float difficultyMult,
            int difficulty,
            ref uint randomState,
            ref RuntimeShellState shell,
            ref EntityCommandBuffer ecb)
        {
            Entity attacker = triggeringDamage.Attacker;
            Entity victim = triggeringDamage.Target;
            RequireActorMagicAndStats(attacker, "attacker");
            RequireActorMagicAndStats(victim, "victim");

            var victimEffects = EntityManager.GetBuffer<ActorActiveMagicEffect>(victim, true);
            float fireShield = MorrowindMeleeCombatMechanics.SumEffectMagnitude(victimEffects, FireShieldEffectId);
            float lightningShield = MorrowindMeleeCombatMechanics.SumEffectMagnitude(victimEffects, LightningShieldEffectId);
            float frostShield = MorrowindMeleeCombatMechanics.SumEffectMagnitude(victimEffects, FrostShieldEffectId);
            if (fireShield <= 0f && lightningShield <= 0f && frostShield <= 0f)
                return;

            var attackerEffects = EntityManager.GetBuffer<ActorActiveMagicEffect>(attacker, true);
            float totalDamage = 0f;
            totalDamage += ComputeShieldDamage(attacker, attackerEffects, fireShield, ResistFireEffectId, WeaknessToFireEffectId, FireShieldEffectId, shieldMult, ref randomState);
            totalDamage += ComputeShieldDamage(attacker, attackerEffects, lightningShield, ResistShockEffectId, WeaknessToShockEffectId, LightningShieldEffectId, shieldMult, ref randomState);
            totalDamage += ComputeShieldDamage(attacker, attackerEffects, frostShield, ResistFrostEffectId, WeaknessToFrostEffectId, FrostShieldEffectId, shieldMult, ref randomState);
            totalDamage = ApplyDifficulty(totalDamage, victim, attacker, difficultyMult, difficulty);
            if (totalDamage <= 0f)
                return;

            var vitals = EntityManager.GetComponentData<ActorVitalSet>(attacker);
            vitals.CurrentHealth = math.max(0f, vitals.CurrentHealth - totalDamage);
            EntityManager.SetComponentData(attacker, vitals);
            if (IsPlayer(attacker))
                RuntimeShellStateUtility.ActivateHitOverlay(ref shell);

            Entity eventEntity = ecb.CreateEntity();
            ecb.AddComponent(eventEntity, new MorrowindDamageAppliedEvent
            {
                Attacker = victim,
                Target = attacker,
                SourceContent = default,
                Amount = totalDamage,
                AttackStrength = 1f,
                TargetVital = MorrowindDamageTargetVital.Health,
                SourceKind = MorrowindDamageSourceKind.ElementalShield,
                FullDamage = 1,
                HitPosition = default,
                HasHitPosition = 0,
            });
        }

        float ComputeShieldDamage(
            Entity attacker,
            DynamicBuffer<ActorActiveMagicEffect> attackerEffects,
            float shieldMagnitude,
            short resistEffectId,
            short weaknessEffectId,
            short matchingShieldEffectId,
            float shieldMult,
            ref uint randomState)
        {
            if (shieldMagnitude <= 0f)
                return 0f;

            float saveTerm = ComputeElementalShieldSaveTerm(attacker);
            float save = math.max(0f, saveTerm - NextRoll0To99(ref randomState));
            float resistance = MorrowindMeleeCombatMechanics.SumEffectMagnitude(attackerEffects, resistEffectId)
                               - MorrowindMeleeCombatMechanics.SumEffectMagnitude(attackerEffects, weaknessEffectId)
                               + MorrowindMeleeCombatMechanics.SumEffectMagnitude(attackerEffects, matchingShieldEffectId);
            save = math.min(100f, save + resistance);
            return shieldMult * shieldMagnitude * (1f - 0.01f * save);
        }

        float ComputeElementalShieldSaveTerm(Entity attacker)
        {
            var skills = EntityManager.GetComponentData<ActorSkillSet>(attacker);
            var attributes = EntityManager.GetComponentData<ActorAttributeSet>(attacker);
            var vitals = EntityManager.GetComponentData<ActorVitalSet>(attacker);
            float saveTerm = skills.Destruction
                             + 0.2f * attributes.Willpower
                             + 0.1f * attributes.Luck;
            float fatigueMax = vitals.ModifiedFatigueBase;
            float fatigueCurrent = vitals.CurrentFatigue;
            float normalizedFatigue = math.floor(fatigueMax) == 0f ? 1f : math.max(0f, fatigueCurrent / fatigueMax);
            return saveTerm * 1.25f * normalizedFatigue;
        }

        float ApplyDifficulty(float damage, Entity attacker, Entity target, float difficultyMult, int difficulty)
        {
            if (damage <= 0f)
                return 0f;

            bool attackerIsPlayer = IsPlayer(attacker);
            bool targetIsPlayer = IsPlayer(target);
            if (attackerIsPlayer == targetIsPlayer)
                return damage;

            float difficultyTerm = difficulty * 0.01f;
            float multiplierOffset;
            if (targetIsPlayer)
                multiplierOffset = difficultyTerm > 0f ? difficultyTerm * difficultyMult : difficultyTerm / difficultyMult;
            else
                multiplierOffset = difficultyTerm > 0f ? -difficultyTerm / difficultyMult : -difficultyTerm * difficultyMult;

            return math.max(0f, damage * (1f + multiplierOffset));
        }

        void RequireActorMagicAndStats(Entity actor, string role)
        {
            if (actor == Entity.Null || !EntityManager.Exists(actor))
                throw new InvalidOperationException($"[VVardenfell][ElementalShield] Hit {role} entity is missing.");
            if (!EntityManager.HasBuffer<ActorActiveMagicEffect>(actor))
                throw new InvalidOperationException($"[VVardenfell][ElementalShield] Hit {role} ref={PlacedRefId(actor)} has no ActorActiveMagicEffect buffer.");
            if (!EntityManager.HasComponent<ActorVitalSet>(actor))
                throw new InvalidOperationException($"[VVardenfell][ElementalShield] Hit {role} ref={PlacedRefId(actor)} has no ActorVitalSet.");
            if (!EntityManager.HasComponent<ActorSkillSet>(actor))
                throw new InvalidOperationException($"[VVardenfell][ElementalShield] Hit {role} ref={PlacedRefId(actor)} has no ActorSkillSet.");
            if (!EntityManager.HasComponent<ActorAttributeSet>(actor))
                throw new InvalidOperationException($"[VVardenfell][ElementalShield] Hit {role} ref={PlacedRefId(actor)} has no ActorAttributeSet.");
        }

        bool IsPlayer(Entity entity)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity))
                throw new InvalidOperationException("[VVardenfell][ElementalShield] Difficulty participant entity is missing.");

            return EntityManager.HasComponent<PlayerTag>(entity);
        }

        uint NextRoll0To99(ref uint state)
            => NextRandom(ref state) % 100u;

        static uint NextRandom(ref uint state)
        {
            state = state == 0u ? 1u : state;
            state = (1664525u * state) + 1013904223u;
            return state;
        }

        uint PlacedRefId(Entity entity)
            => EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;

        static short RequireEffectId(string gmstId)
        {
            if (!MorrowindMagicEffectTextUtility.TryResolveEffectId(gmstId, out short effectId))
                throw new InvalidOperationException($"[VVardenfell][ElementalShield] Unknown magic effect GMST id '{gmstId}'.");

            return effectId;
        }
    }
}


