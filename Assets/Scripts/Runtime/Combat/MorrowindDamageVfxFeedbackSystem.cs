using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Systems;
using VVardenfell.Runtime.Vfx;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindDamageSystemGroup))]
    [UpdateAfter(typeof(MorrowindHitAftermathStateSystem))]
    [UpdateBefore(typeof(MorrowindDamageFeedbackSystem))]
    public partial struct MorrowindDamageVfxFeedbackSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<RuntimePresentationEnabled>();
            systemState.RequireForUpdate<MorrowindDamageAppliedEvent>();
            systemState.RequireForUpdate<MorrowindCombatRuntimeState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][Damage] Damage VFX requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            var combatState = SystemAPI.GetSingletonRW<MorrowindCombatRuntimeState>();
            uint randomState = combatState.ValueRO.RandomState;
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var damage in SystemAPI.Query<RefRO<MorrowindDamageAppliedEvent>>())
                EmitBloodVfxRequest(ref systemState, ref content, ref randomState, ref ecb, damage.ValueRO);

            combatState.ValueRW.RandomState = randomState == 0u ? 1u : randomState;
            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
        }

        static uint NextRandom(ref uint state)
        {
            state = state == 0u ? 1u : state;
            state = (1664525u * state) + 1013904223u;
            return state;
        }

        void EmitBloodVfxRequest(ref SystemState systemState,
            ref RuntimeContentBlob content,
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

            if (damage.Target == Entity.Null || !systemState.EntityManager.Exists(damage.Target))
                throw new InvalidOperationException("[VVardenfell][Damage] Blood VFX target entity is missing.");
            if (!systemState.EntityManager.HasComponent<ActorSpawnSource>(damage.Target))
                throw new InvalidOperationException($"[VVardenfell][Damage] Blood VFX target ref={PlacedRefId(ref systemState, damage.Target)} has no ActorSpawnSource.");
            if (!systemState.EntityManager.HasComponent<LogicalRefLocation>(damage.Target))
                throw new InvalidOperationException($"[VVardenfell][Damage] Blood VFX target ref={PlacedRefId(ref systemState, damage.Target)} has no LogicalRefLocation.");

            int modelVariant = (int)(NextRandom(ref randomState) % 3u);
            string model = "meshes/" + RuntimeContentBlobUtility.RequireGameSettingStringByIdHash(ref content, RuntimeContentStableHash.HashId($"Blood_Model_{modelVariant}"));
            var actorSource = systemState.EntityManager.GetComponentData<ActorSpawnSource>(damage.Target);
            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, actorSource.Definition);
            string texture = ResolveBloodTexture(ref content, actor.BloodType);
            var location = systemState.EntityManager.GetComponentData<LogicalRefLocation>(damage.Target);
            float3 bloodPosition = ResolveBloodVfxPosition(ref systemState, damage, ref randomState);

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

        float3 ResolveBloodVfxPosition(ref SystemState systemState, in MorrowindDamageAppliedEvent damage, ref uint randomState)
        {
            if (damage.Target == Entity.Null
                || damage.Attacker == Entity.Null
                || !systemState.EntityManager.Exists(damage.Target)
                || !systemState.EntityManager.Exists(damage.Attacker)
                || !systemState.EntityManager.HasComponent<LocalTransform>(damage.Target)
                || !systemState.EntityManager.HasComponent<LocalTransform>(damage.Attacker)
                || !systemState.EntityManager.HasComponent<ActorLocalBounds>(damage.Target))
            {
                return damage.HitPosition;
            }

            var targetTransform = systemState.EntityManager.GetComponentData<LocalTransform>(damage.Target);
            var attackerTransform = systemState.EntityManager.GetComponentData<LocalTransform>(damage.Attacker);
            var bounds = systemState.EntityManager.GetComponentData<ActorLocalBounds>(damage.Target);
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

        static string ResolveBloodTexture(ref RuntimeContentBlob content, int bloodType)
        {
            string typedId = $"Blood_Texture_{bloodType}";
            string typed = RuntimeContentBlobUtility.RequireGameSettingStringAllowEmptyByIdHash(ref content, RuntimeContentStableHash.HashId(typedId));
            if (!string.IsNullOrWhiteSpace(typed))
                return typed;

            return RuntimeContentBlobUtility.RequireGameSettingStringByIdHash(ref content, RuntimeContentKnownHashes.Blood_Texture_0);
        }

        uint PlacedRefId(ref SystemState systemState, Entity entity)
            => systemState.EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? systemState.EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
    }
}
