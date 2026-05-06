using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Components;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindDamageSystemGroup))]
    [UpdateAfter(typeof(MorrowindDifficultyDamageSystem))]
    public partial struct MorrowindDamageApplySystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindPendingDamageEvent>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (damage, entity) in
                     SystemAPI.Query<RefRO<MorrowindPendingDamageEvent>>()
                         .WithEntityAccess())
            {
                ApplyDamage(ref systemState, damage.ValueRO);
                ecb.RemoveComponent<MorrowindPendingDamageEvent>(entity);
                ecb.AddComponent(entity, new MorrowindDamageAppliedEvent
                {
                    Attacker = damage.ValueRO.Attacker,
                    Target = damage.ValueRO.Target,
                    SourceContent = damage.ValueRO.SourceContent,
                    Amount = math.max(0f, damage.ValueRO.Amount),
                    AttackStrength = damage.ValueRO.AttackStrength,
                    TargetVital = damage.ValueRO.TargetVital,
                    SourceKind = damage.ValueRO.SourceKind,
                    FullDamage = damage.ValueRO.FullDamage,
                    BlockImpact = damage.ValueRO.BlockImpact,
                    ArmorImpact = damage.ValueRO.ArmorImpact,
                    HitPosition = damage.ValueRO.HitPosition,
                    HasHitPosition = damage.ValueRO.HasHitPosition,
                });
            }

            ecb.Playback(systemState.EntityManager);
            ecb.Dispose();
        }

        void ApplyDamage(ref SystemState systemState, in MorrowindPendingDamageEvent damage)
        {
            Entity target = damage.Target;
            if (target == Entity.Null || !systemState.EntityManager.Exists(target))
                throw new InvalidOperationException("[VVardenfell][Damage] Damage target entity is missing.");
            if (!systemState.EntityManager.HasComponent<ActorVitalSet>(target))
                throw new InvalidOperationException("[VVardenfell][Damage] Damage target has no ActorVitalSet.");
            if (!systemState.EntityManager.HasComponent<ActorScriptEventState>(target))
                throw new InvalidOperationException("[VVardenfell][Damage] Damage target has no ActorScriptEventState.");

            var vitals = systemState.EntityManager.GetComponentData<ActorVitalSet>(target);
            var eventState = systemState.EntityManager.GetComponentData<ActorScriptEventState>(target);
            float amount = math.max(0f, damage.Amount);

            if (damage.SourceKind == MorrowindDamageSourceKind.Weapon
                && (!damage.SourceContent.IsValid || damage.SourceContent.Kind != ContentReferenceKind.Item))
                throw new InvalidOperationException("[VVardenfell][Damage] Weapon damage is missing item source content.");

            switch (damage.TargetVital)
            {
                case MorrowindDamageTargetVital.Health:
                {
                    vitals.CurrentHealth = math.max(0f, vitals.CurrentHealth - amount);
                    break;
                }
                case MorrowindDamageTargetVital.Fatigue:
                {
                    vitals.CurrentFatigue -= amount;
                    break;
                }
                default:
                    throw new InvalidOperationException($"[VVardenfell][Damage] Unknown damage target vital {damage.TargetVital}.");
            }

            systemState.EntityManager.SetComponentData(target, vitals);
            systemState.EntityManager.SetComponentData(target, eventState);
        }
    }
}
