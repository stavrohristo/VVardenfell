using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindDamageSystemGroup))]
    [UpdateAfter(typeof(MorrowindHitAftermathStateSystem))]
    [UpdateBefore(typeof(MorrowindHitAftermathAnimationSystem))]
    public partial struct MorrowindDamageFeedbackSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindDamageAppliedEvent>();
            systemState.RequireForUpdate<MorrowindCombatRuntimeState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var combatState = SystemAPI.GetSingletonRW<MorrowindCombatRuntimeState>();
            uint randomState = combatState.ValueRO.RandomState;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][Damage] Damage feedback requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;
            bool hasAudioState = SystemAPI.TryGetSingletonEntity<InteractionAudioRequestState>(out Entity audioEntity);
            var audioState = hasAudioState
                ? systemState.EntityManager.GetComponentData<InteractionAudioRequestState>(audioEntity)
                : default;

            foreach (var (damage, entity) in
                     SystemAPI.Query<RefRO<MorrowindDamageAppliedEvent>>()
                         .WithEntityAccess())
            {
                EmitBlockImpactAudio(ref systemState, ref content, ref audioState, hasAudioState, ref ecb, damage.ValueRO);
                EmitArmorImpactAudio(ref systemState, ref content, ref audioState, hasAudioState, ref ecb, damage.ValueRO);
                EmitAppliedDamageAudio(ref systemState, ref content, ref audioState, hasAudioState, ref randomState, ref ecb, damage.ValueRO);
                ecb.DestroyEntity(entity);
            }

            combatState.ValueRW.RandomState = randomState == 0u ? 1u : randomState;
            if (hasAudioState)
                systemState.EntityManager.SetComponentData(audioEntity, audioState);
            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
        }

        static uint NextRandom(ref uint state)
        {
            state = state == 0u ? 1u : state;
            state = (1664525u * state) + 1013904223u;
            return state;
        }

        void EmitBlockImpactAudio(ref SystemState systemState, 
            ref RuntimeContentBlob content,
            ref InteractionAudioRequestState audioState,
            bool hasAudioState,
            ref EntityCommandBuffer ecb,
            in MorrowindDamageAppliedEvent damage)
        {
            var impact = damage.BlockImpact;
            if (impact.Blocked == 0)
                return;

            string soundId = impact.ShieldSkill switch
            {
                ActorSkillKind.LightArmor => "Light Armor Hit",
                ActorSkillKind.MediumArmor => "Medium Armor Hit",
                ActorSkillKind.HeavyArmor => "Heavy Armor Hit",
                _ => throw new InvalidOperationException($"[VVardenfell][Damage] Block impact has invalid shield skill {impact.ShieldSkill}."),
            };

            if (impact.Target == Entity.Null || !systemState.EntityManager.Exists(impact.Target))
                throw new InvalidOperationException("[VVardenfell][Damage] Block hit sound target entity is missing.");
            if (impact.Target != damage.Target)
                throw new InvalidOperationException("[VVardenfell][Damage] Block hit sound target does not match applied damage target.");
            if (!systemState.EntityManager.HasComponent<LocalTransform>(impact.Target))
                throw new InvalidOperationException("[VVardenfell][Damage] Block hit sound target has no LocalTransform.");

            MorrowindCombatAudioUtility.EmitRequiredSound(
                ref content,
                soundId,
                impact.Target,
                PlacedRefId(ref systemState, impact.Target),
                systemState.EntityManager.GetComponentData<LocalTransform>(impact.Target).Position,
                1f,
                1f,
                ref audioState,
                hasAudioState,
                ref ecb);
        }

        void EmitArmorImpactAudio(ref SystemState systemState, 
            ref RuntimeContentBlob content,
            ref InteractionAudioRequestState audioState,
            bool hasAudioState,
            ref EntityCommandBuffer ecb,
            in MorrowindDamageAppliedEvent damage)
        {
            var impact = damage.ArmorImpact;
            if (impact.HasEquippedArmor == 0)
                return;

            string soundId = impact.Skill switch
            {
                ActorSkillKind.LightArmor => "Light Armor Hit",
                ActorSkillKind.MediumArmor => "Medium Armor Hit",
                ActorSkillKind.HeavyArmor => "Heavy Armor Hit",
                _ => "Hand To Hand Hit",
            };

            if (impact.Target == Entity.Null || !systemState.EntityManager.Exists(impact.Target))
                throw new InvalidOperationException("[VVardenfell][Damage] Armor hit sound target entity is missing.");
            if (impact.Target != damage.Target)
                throw new InvalidOperationException("[VVardenfell][Damage] Armor hit sound target does not match applied damage target.");
            if (!systemState.EntityManager.HasComponent<LocalTransform>(impact.Target))
                throw new InvalidOperationException("[VVardenfell][Damage] Armor hit sound target has no LocalTransform.");

            MorrowindCombatAudioUtility.EmitRequiredSound(
                ref content,
                soundId,
                impact.Target,
                PlacedRefId(ref systemState, impact.Target),
                systemState.EntityManager.GetComponentData<LocalTransform>(impact.Target).Position,
                1f,
                1f,
                ref audioState,
                hasAudioState,
                ref ecb);
        }

        void EmitAppliedDamageAudio(ref SystemState systemState, 
            ref RuntimeContentBlob content,
            ref InteractionAudioRequestState audioState,
            bool hasAudioState,
            ref uint randomState,
            ref EntityCommandBuffer ecb,
            in MorrowindDamageAppliedEvent damage)
        {
            if (damage.Amount <= 0f)
                return;

            if (damage.Target == Entity.Null || !systemState.EntityManager.Exists(damage.Target))
                throw new InvalidOperationException("[VVardenfell][Damage] Damage sound target entity is missing.");
            if (!systemState.EntityManager.HasComponent<LocalTransform>(damage.Target))
                throw new InvalidOperationException("[VVardenfell][Damage] Damage sound target has no LocalTransform.");

            string soundId = ResolveDamageSoundId(damage, ref randomState);
            if (string.IsNullOrEmpty(soundId))
                return;

            MorrowindCombatAudioUtility.EmitRequiredSound(
                ref content,
                soundId,
                damage.Target,
                PlacedRefId(ref systemState, damage.Target),
                systemState.EntityManager.GetComponentData<LocalTransform>(damage.Target).Position,
                1f,
                1f,
                ref audioState,
                hasAudioState,
                ref ecb);
        }

        string ResolveDamageSoundId(in MorrowindDamageAppliedEvent damage, ref uint randomState)
        {
            if (damage.TargetVital == MorrowindDamageTargetVital.Health)
                return "Health Damage";

            if (damage.TargetVital == MorrowindDamageTargetVital.Fatigue
                && damage.SourceKind == MorrowindDamageSourceKind.HandToHand)
            {
                uint roll = NextRandom(ref randomState);
                return (roll & 1u) == 0u ? "Hand To Hand Hit" : "Hand To Hand Hit 2";
            }

            return null;
        }

        uint PlacedRefId(ref SystemState systemState, Entity entity)
            => systemState.EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? systemState.EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
    }
}


