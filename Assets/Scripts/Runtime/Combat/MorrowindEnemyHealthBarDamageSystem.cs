using System;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindDamageSystemGroup))]
    [UpdateAfter(typeof(MorrowindDamageApplySystem))]
    [UpdateBefore(typeof(MorrowindHitAftermathStateSystem))]
    public partial class MorrowindEnemyHealthBarDamageSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindDamageAppliedEvent>();
            RequireForUpdate<RuntimeEnemyHealthBarState>();
            RequireForUpdate<RuntimeContentBlobReference>();
        }

        protected override void OnUpdate()
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][HUD] Enemy health bar damage requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            float displaySeconds = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fNPCHealthBarTime);
            float fadeSeconds = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fNPCHealthBarFade);
            if (displaySeconds < 0f)
                throw new InvalidOperationException($"[VVardenfell][HUD] GMST 'fNPCHealthBarTime' must be non-negative, got {displaySeconds}.");
            if (fadeSeconds < 0f)
                throw new InvalidOperationException($"[VVardenfell][HUD] GMST 'fNPCHealthBarFade' must be non-negative, got {fadeSeconds}.");

            ref var state = ref SystemAPI.GetSingletonRW<RuntimeEnemyHealthBarState>().ValueRW;
            foreach (var damage in SystemAPI.Query<RefRO<MorrowindDamageAppliedEvent>>())
            {
                if (!IsPlayerAttacker(damage.ValueRO.Attacker))
                    continue;

                RefreshEnemyHealthState(ref state, damage.ValueRO.Target, displaySeconds, fadeSeconds);
            }
        }

        bool IsPlayerAttacker(Entity attacker)
        {
            return attacker != Entity.Null
                   && EntityManager.Exists(attacker)
                   && EntityManager.HasComponent<PlayerTag>(attacker);
        }

        void RefreshEnemyHealthState(
            ref RuntimeEnemyHealthBarState state,
            Entity target,
            float displaySeconds,
            float fadeSeconds)
        {
            if (target == Entity.Null || !EntityManager.Exists(target))
                throw new InvalidOperationException("[VVardenfell][HUD] Enemy health target entity is missing.");
            if (EntityManager.HasComponent<PlayerTag>(target))
                return;
            if (!EntityManager.HasComponent<ActorSpawnSource>(target))
                throw new InvalidOperationException($"[VVardenfell][HUD] Enemy health target entity={target.Index}:{target.Version} has no ActorSpawnSource.");
            if (!EntityManager.HasComponent<PlacedRefIdentity>(target))
                throw new InvalidOperationException($"[VVardenfell][HUD] Enemy health target entity={target.Index}:{target.Version} has no PlacedRefIdentity.");
            if (!EntityManager.HasComponent<ActorVitalSet>(target))
                throw new InvalidOperationException($"[VVardenfell][HUD] Enemy health target ref={PlacedRefId(target)} has no ActorVitalSet.");
            if (!EntityManager.HasComponent<ActorHitAftermathState>(target))
                throw new InvalidOperationException($"[VVardenfell][HUD] Enemy health target ref={PlacedRefId(target)} has no ActorHitAftermathState.");

            var aftermath = EntityManager.GetComponentData<ActorHitAftermathState>(target);
            var vitals = EntityManager.GetComponentData<ActorVitalSet>(target);
            if (aftermath.Dead != 0 && vitals.CurrentHealth <= 0f)
                return;

            state.Target = target;
            state.TargetPlacedRefId = PlacedRefId(target);
            state.SecondsRemaining = displaySeconds;
            state.FadeSeconds = fadeSeconds;
            state.LastKnownHealthFill = ComputeHealthFill(target, vitals);
            state.Visible = 1;
        }

        float ComputeHealthFill(Entity target, in ActorVitalSet vitals)
        {
            if (vitals.ModifiedHealthBase <= 0f)
                throw new InvalidOperationException($"[VVardenfell][HUD] Enemy health target ref={PlacedRefId(target)} has non-positive modified health base {vitals.ModifiedHealthBase}.");

            if (vitals.CurrentHealth < 1f)
                return 0f;

            return math.saturate(vitals.CurrentHealth / vitals.ModifiedHealthBase);
        }

        uint PlacedRefId(Entity entity)
            => EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value;
    }
}


