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
    public partial struct MorrowindElementalShieldDamageSystem : ISystem
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

        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindDamageAppliedEvent>();
            systemState.RequireForUpdate<MorrowindCombatRuntimeState>();
            systemState.RequireForUpdate<MorrowindCombatSettings>();
            systemState.RequireForUpdate<RuntimeShellState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
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
                if (!CanTriggerElementalShield(ref systemState, damage.ValueRO))
                    continue;

                ApplyElementalShieldDamage(ref systemState, 
                    damage.ValueRO,
                    shieldMult,
                    difficultyMult,
                    settings.Difficulty,
                    ref randomState,
                    ref shell,
                    ref ecb);
            }

            combatState.RandomState = randomState == 0u ? 0x6E624EB7u : randomState;
            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
        }

        bool CanTriggerElementalShield(ref SystemState systemState, in MorrowindDamageAppliedEvent damage)
            => damage.Amount > 0f
               && damage.TargetVital == MorrowindDamageTargetVital.Health
               && (damage.SourceKind == MorrowindDamageSourceKind.Weapon
                   || damage.SourceKind == MorrowindDamageSourceKind.HandToHand)
               && damage.Attacker != Entity.Null
               && damage.Target != Entity.Null
               && systemState.EntityManager.Exists(damage.Attacker)
               && systemState.EntityManager.Exists(damage.Target);

        void ApplyElementalShieldDamage(ref SystemState systemState, 
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
            RequireActorMagicAndStats(ref systemState, attacker, "attacker");
            RequireActorMagicAndStats(ref systemState, victim, "victim");

            var victimEffects = systemState.EntityManager.GetBuffer<ActorActiveMagicEffect>(victim, true);
            float fireShield = MorrowindMeleeCombatMechanics.SumEffectMagnitude(victimEffects, FireShieldEffectId);
            float lightningShield = MorrowindMeleeCombatMechanics.SumEffectMagnitude(victimEffects, LightningShieldEffectId);
            float frostShield = MorrowindMeleeCombatMechanics.SumEffectMagnitude(victimEffects, FrostShieldEffectId);
            if (fireShield <= 0f && lightningShield <= 0f && frostShield <= 0f)
                return;

            var attackerEffects = systemState.EntityManager.GetBuffer<ActorActiveMagicEffect>(attacker, true);
            float totalDamage = 0f;
            totalDamage += ComputeShieldDamage(ref systemState, attacker, attackerEffects, fireShield, ResistFireEffectId, WeaknessToFireEffectId, FireShieldEffectId, shieldMult, ref randomState);
            totalDamage += ComputeShieldDamage(ref systemState, attacker, attackerEffects, lightningShield, ResistShockEffectId, WeaknessToShockEffectId, LightningShieldEffectId, shieldMult, ref randomState);
            totalDamage += ComputeShieldDamage(ref systemState, attacker, attackerEffects, frostShield, ResistFrostEffectId, WeaknessToFrostEffectId, FrostShieldEffectId, shieldMult, ref randomState);
            totalDamage = ApplyDifficulty(ref systemState, totalDamage, victim, attacker, difficultyMult, difficulty);
            if (totalDamage <= 0f)
                return;

            var vitals = systemState.EntityManager.GetComponentData<ActorVitalSet>(attacker);
            vitals.CurrentHealth = math.max(0f, vitals.CurrentHealth - totalDamage);
            systemState.EntityManager.SetComponentData(attacker, vitals);
            if (IsPlayer(ref systemState, attacker))
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

        float ComputeShieldDamage(ref SystemState systemState, 
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

            float saveTerm = ComputeElementalShieldSaveTerm(ref systemState, attacker);
            float save = math.max(0f, saveTerm - NextRoll0To99(ref randomState));
            float resistance = MorrowindMeleeCombatMechanics.SumEffectMagnitude(attackerEffects, resistEffectId)
                               - MorrowindMeleeCombatMechanics.SumEffectMagnitude(attackerEffects, weaknessEffectId)
                               + MorrowindMeleeCombatMechanics.SumEffectMagnitude(attackerEffects, matchingShieldEffectId);
            save = math.min(100f, save + resistance);
            return shieldMult * shieldMagnitude * (1f - 0.01f * save);
        }

        float ComputeElementalShieldSaveTerm(ref SystemState systemState, Entity attacker)
        {
            var skills = systemState.EntityManager.GetComponentData<ActorSkillSet>(attacker);
            var attributes = systemState.EntityManager.GetComponentData<ActorAttributeSet>(attacker);
            var vitals = systemState.EntityManager.GetComponentData<ActorVitalSet>(attacker);
            float saveTerm = skills.Destruction
                             + 0.2f * attributes.Willpower
                             + 0.1f * attributes.Luck;
            float fatigueMax = vitals.ModifiedFatigueBase;
            float fatigueCurrent = vitals.CurrentFatigue;
            float normalizedFatigue = math.floor(fatigueMax) == 0f ? 1f : math.max(0f, fatigueCurrent / fatigueMax);
            return saveTerm * 1.25f * normalizedFatigue;
        }

        float ApplyDifficulty(ref SystemState systemState, float damage, Entity attacker, Entity target, float difficultyMult, int difficulty)
        {
            if (damage <= 0f)
                return 0f;

            bool attackerIsPlayer = IsPlayer(ref systemState, attacker);
            bool targetIsPlayer = IsPlayer(ref systemState, target);
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

        void RequireActorMagicAndStats(ref SystemState systemState, Entity actor, string role)
        {
            if (actor == Entity.Null || !systemState.EntityManager.Exists(actor))
                throw new InvalidOperationException($"[VVardenfell][ElementalShield] Hit {role} entity is missing.");
            if (!systemState.EntityManager.HasBuffer<ActorActiveMagicEffect>(actor))
                throw new InvalidOperationException($"[VVardenfell][ElementalShield] Hit {role} ref={PlacedRefId(ref systemState, actor)} has no ActorActiveMagicEffect buffer.");
            if (!systemState.EntityManager.HasComponent<ActorVitalSet>(actor))
                throw new InvalidOperationException($"[VVardenfell][ElementalShield] Hit {role} ref={PlacedRefId(ref systemState, actor)} has no ActorVitalSet.");
            if (!systemState.EntityManager.HasComponent<ActorSkillSet>(actor))
                throw new InvalidOperationException($"[VVardenfell][ElementalShield] Hit {role} ref={PlacedRefId(ref systemState, actor)} has no ActorSkillSet.");
            if (!systemState.EntityManager.HasComponent<ActorAttributeSet>(actor))
                throw new InvalidOperationException($"[VVardenfell][ElementalShield] Hit {role} ref={PlacedRefId(ref systemState, actor)} has no ActorAttributeSet.");
        }

        bool IsPlayer(ref SystemState systemState, Entity entity)
        {
            if (entity == Entity.Null || !systemState.EntityManager.Exists(entity))
                throw new InvalidOperationException("[VVardenfell][ElementalShield] Difficulty participant entity is missing.");

            return systemState.EntityManager.HasComponent<PlayerTag>(entity);
        }

        uint NextRoll0To99(ref uint state)
            => NextRandom(ref state) % 100u;

        static uint NextRandom(ref uint state)
        {
            state = state == 0u ? 1u : state;
            state = (1664525u * state) + 1013904223u;
            return state;
        }

        uint PlacedRefId(ref SystemState systemState, Entity entity)
            => systemState.EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? systemState.EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;

        static short RequireEffectId(string gmstId)
        {
            if (!MorrowindMagicEffectTextUtility.TryResolveEffectId(gmstId, out short effectId))
                throw new InvalidOperationException($"[VVardenfell][ElementalShield] Unknown magic effect GMST id '{gmstId}'.");

            return effectId;
        }
    }
}


