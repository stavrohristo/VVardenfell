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
using VVardenfell.Runtime.Vfx;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindDamageSystemGroup))]
    [UpdateAfter(typeof(MorrowindHitAftermathStateSystem))]
    [UpdateBefore(typeof(MorrowindHitAftermathAnimationSystem))]
    public partial class MorrowindDamageFeedbackSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindDamageAppliedEvent>();
            RequireForUpdate<MorrowindCombatRuntimeState>();
        }

        protected override void OnUpdate()
        {
            var combatState = SystemAPI.GetSingletonRW<MorrowindCombatRuntimeState>();
            uint randomState = combatState.ValueRO.RandomState;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            RuntimeContentDatabase contentDb = RuntimeContentDatabase.Active
                ?? throw new InvalidOperationException("[VVardenfell][Damage] Hit feedback has no runtime content database.");
            bool hasAudioState = SystemAPI.TryGetSingletonEntity<InteractionAudioRequestState>(out Entity audioEntity);
            var audioState = hasAudioState
                ? EntityManager.GetComponentData<InteractionAudioRequestState>(audioEntity)
                : default;

            foreach (var (damage, entity) in
                     SystemAPI.Query<RefRO<MorrowindDamageAppliedEvent>>()
                         .WithEntityAccess())
            {
                EmitBlockImpactAudio(contentDb, ref audioState, hasAudioState, ref ecb, damage.ValueRO);
                EmitArmorImpactAudio(contentDb, ref audioState, hasAudioState, ref ecb, damage.ValueRO);
                EmitAppliedDamageAudio(contentDb, ref audioState, hasAudioState, ref randomState, ref ecb, damage.ValueRO);
                EmitBloodVfxRequest(contentDb, ref randomState, ref ecb, damage.ValueRO);
                ecb.DestroyEntity(entity);
            }

            combatState.ValueRW.RandomState = randomState == 0u ? 1u : randomState;
            if (hasAudioState)
                EntityManager.SetComponentData(audioEntity, audioState);
            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        static uint NextRandom(ref uint state)
        {
            state = state == 0u ? 1u : state;
            state = (1664525u * state) + 1013904223u;
            return state;
        }

        void EmitBlockImpactAudio(
            RuntimeContentDatabase contentDb,
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

            if (impact.Target == Entity.Null || !EntityManager.Exists(impact.Target))
                throw new InvalidOperationException("[VVardenfell][Damage] Block hit sound target entity is missing.");
            if (impact.Target != damage.Target)
                throw new InvalidOperationException("[VVardenfell][Damage] Block hit sound target does not match applied damage target.");
            if (!EntityManager.HasComponent<LocalTransform>(impact.Target))
                throw new InvalidOperationException("[VVardenfell][Damage] Block hit sound target has no LocalTransform.");

            MorrowindCombatAudioUtility.EmitRequiredSound(
                contentDb,
                soundId,
                impact.Target,
                PlacedRefId(impact.Target),
                EntityManager.GetComponentData<LocalTransform>(impact.Target).Position,
                1f,
                1f,
                ref audioState,
                hasAudioState,
                ref ecb);
        }

        void EmitArmorImpactAudio(
            RuntimeContentDatabase contentDb,
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

            if (impact.Target == Entity.Null || !EntityManager.Exists(impact.Target))
                throw new InvalidOperationException("[VVardenfell][Damage] Armor hit sound target entity is missing.");
            if (impact.Target != damage.Target)
                throw new InvalidOperationException("[VVardenfell][Damage] Armor hit sound target does not match applied damage target.");
            if (!EntityManager.HasComponent<LocalTransform>(impact.Target))
                throw new InvalidOperationException("[VVardenfell][Damage] Armor hit sound target has no LocalTransform.");

            MorrowindCombatAudioUtility.EmitRequiredSound(
                contentDb,
                soundId,
                impact.Target,
                PlacedRefId(impact.Target),
                EntityManager.GetComponentData<LocalTransform>(impact.Target).Position,
                1f,
                1f,
                ref audioState,
                hasAudioState,
                ref ecb);
        }

        void EmitAppliedDamageAudio(
            RuntimeContentDatabase contentDb,
            ref InteractionAudioRequestState audioState,
            bool hasAudioState,
            ref uint randomState,
            ref EntityCommandBuffer ecb,
            in MorrowindDamageAppliedEvent damage)
        {
            if (damage.Amount <= 0f)
                return;

            if (damage.Target == Entity.Null || !EntityManager.Exists(damage.Target))
                throw new InvalidOperationException("[VVardenfell][Damage] Damage sound target entity is missing.");
            if (!EntityManager.HasComponent<LocalTransform>(damage.Target))
                throw new InvalidOperationException("[VVardenfell][Damage] Damage sound target has no LocalTransform.");

            string soundId = ResolveDamageSoundId(damage, ref randomState);
            if (string.IsNullOrEmpty(soundId))
                return;

            MorrowindCombatAudioUtility.EmitRequiredSound(
                contentDb,
                soundId,
                damage.Target,
                PlacedRefId(damage.Target),
                EntityManager.GetComponentData<LocalTransform>(damage.Target).Position,
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

        void EmitBloodVfxRequest(
            RuntimeContentDatabase contentDb,
            ref uint randomState,
            ref EntityCommandBuffer ecb,
            in MorrowindDamageAppliedEvent damage)
        {
            if (damage.Amount <= 0f
                || damage.TargetVital != MorrowindDamageTargetVital.Health
                || damage.HasHitPosition == 0)
            {
                return;
            }

            if (damage.Target == Entity.Null || !EntityManager.Exists(damage.Target))
                throw new InvalidOperationException("[VVardenfell][Damage] Blood VFX target entity is missing.");
            if (!EntityManager.HasComponent<ActorSpawnSource>(damage.Target))
                throw new InvalidOperationException($"[VVardenfell][Damage] Blood VFX target ref={PlacedRefId(damage.Target)} has no ActorSpawnSource.");
            if (!EntityManager.HasComponent<LogicalRefLocation>(damage.Target))
                throw new InvalidOperationException($"[VVardenfell][Damage] Blood VFX target ref={PlacedRefId(damage.Target)} has no LogicalRefLocation.");

            int modelVariant = (int)(NextRandom(ref randomState) % 3u);
            string model = "meshes/" + contentDb.RequireGameSettingString($"Blood_Model_{modelVariant}");
            var actorSource = EntityManager.GetComponentData<ActorSpawnSource>(damage.Target);
            ref readonly var actor = ref contentDb.Get(actorSource.Definition);
            string texture = ResolveBloodTexture(contentDb, actor.BloodType);
            var location = EntityManager.GetComponentData<LogicalRefLocation>(damage.Target);
            float3 bloodPosition = ResolveBloodVfxPosition(damage, ref randomState);

            Entity requestEntity = ecb.CreateEntity();
            ecb.AddComponent(requestEntity, new MorrowindVfxSpawnRequest
            {
                ModelPath = model,
                TextureOverridePath = texture,
                Position = bloodPosition,
                Rotation = quaternion.identity,
                Scale = 1f,
                ExteriorCell = location.ExteriorCell,
                InteriorCellId = location.InteriorCellId,
                InteriorCellHash = location.InteriorCellHash,
                IsInterior = location.IsInterior,
            });
        }

        float3 ResolveBloodVfxPosition(in MorrowindDamageAppliedEvent damage, ref uint randomState)
        {
            if (damage.Target == Entity.Null
                || damage.Attacker == Entity.Null
                || !EntityManager.Exists(damage.Target)
                || !EntityManager.Exists(damage.Attacker)
                || !EntityManager.HasComponent<LocalTransform>(damage.Target)
                || !EntityManager.HasComponent<LocalTransform>(damage.Attacker)
                || !EntityManager.HasComponent<ActorLocalBounds>(damage.Target))
            {
                return damage.HitPosition;
            }

            var targetTransform = EntityManager.GetComponentData<LocalTransform>(damage.Target);
            var attackerTransform = EntityManager.GetComponentData<LocalTransform>(damage.Attacker);
            var bounds = EntityManager.GetComponentData<ActorLocalBounds>(damage.Target);
            float scale = math.max(0.01f, targetTransform.Scale);
            float3 extents = bounds.Extents * scale;
            float3 targetBase = targetTransform.Position;
            float3 directionToAttacker = math.normalizesafe(
                new float3(attackerTransform.Position.x - targetBase.x, 0f, attackerTransform.Position.z - targetBase.z),
                new float3(0f, 0f, 1f));

            float forwardRadius = math.max(0.01f, extents.z);
            float width = math.max(0.01f, extents.x * 2f);
            float height = math.max(0.01f, extents.y * 2f);
            float xOffset = (NextRandom01(ref randomState) - 0.5f) * 0.5f;
            float heightT = 0.2f + NextRandom01(ref randomState) * 0.8f;

            return targetBase
                   + directionToAttacker * forwardRadius
                   + new float3(width * xOffset, height * heightT, 0f);
        }

        static float NextRandom01(ref uint randomState)
            => (NextRandom(ref randomState) & 0x00FFFFFFu) / 16777216f;

        static string ResolveBloodTexture(RuntimeContentDatabase contentDb, int bloodType)
        {
            string typedId = $"Blood_Texture_{bloodType}";
            if (!contentDb.TryGetGameSettingHandle(typedId, out var handle) || !handle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][Damage] Missing GMST '{typedId}'.");

            ref readonly var setting = ref contentDb.GetGameSetting(handle);
            if (setting.ValueKind != GenericRecordValueKind.String)
                throw new InvalidOperationException($"[VVardenfell][Damage] GMST '{typedId}' is not a string game setting.");
            if (!string.IsNullOrWhiteSpace(setting.Text))
                return setting.Text;

            return contentDb.RequireGameSettingString("Blood_Texture_0");
        }

        uint PlacedRefId(Entity entity)
            => EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
    }
}
