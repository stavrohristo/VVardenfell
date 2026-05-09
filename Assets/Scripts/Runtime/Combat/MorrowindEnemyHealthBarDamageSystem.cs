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
    public partial struct MorrowindEnemyHealthBarDamageSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MorrowindDamageAppliedEvent>();
            state.RequireForUpdate<RuntimeEnemyHealthBarState>();
            state.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState state)
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

            ref var healthBarState = ref SystemAPI.GetSingletonRW<RuntimeEnemyHealthBarState>().ValueRW;
            foreach (var damage in SystemAPI.Query<RefRO<MorrowindDamageAppliedEvent>>())
            {
                if (!IsPlayerAttacker(state.EntityManager, damage.ValueRO.Attacker))
                    continue;

                RefreshEnemyHealthState(state.EntityManager, ref healthBarState, damage.ValueRO.Target, displaySeconds, fadeSeconds);
            }
        }

        static bool IsPlayerAttacker(EntityManager entityManager, Entity attacker)
        {
            return attacker != Entity.Null
                   && entityManager.Exists(attacker)
                   && entityManager.HasComponent<PlayerTag>(attacker);
        }

        static void RefreshEnemyHealthState(
            EntityManager entityManager,
            ref RuntimeEnemyHealthBarState state,
            Entity target,
            float displaySeconds,
            float fadeSeconds)
        {
            if (target == Entity.Null || !entityManager.Exists(target))
                throw new InvalidOperationException("[VVardenfell][HUD] Enemy health target entity is missing.");
            if (entityManager.HasComponent<PlayerTag>(target))
                return;
            if (!entityManager.HasComponent<ActorSpawnSource>(target))
                throw new InvalidOperationException($"[VVardenfell][HUD] Enemy health target entity={target.Index}:{target.Version} has no ActorSpawnSource.");
            if (!entityManager.HasComponent<PlacedRefIdentity>(target))
                throw new InvalidOperationException($"[VVardenfell][HUD] Enemy health target entity={target.Index}:{target.Version} has no PlacedRefIdentity.");
            if (!entityManager.HasComponent<ActorVitalSet>(target))
                throw new InvalidOperationException($"[VVardenfell][HUD] Enemy health target ref={PlacedRefId(entityManager, target)} has no ActorVitalSet.");
            if (!entityManager.HasComponent<ActorHitAftermathState>(target))
                throw new InvalidOperationException($"[VVardenfell][HUD] Enemy health target ref={PlacedRefId(entityManager, target)} has no ActorHitAftermathState.");

            var vitals = entityManager.GetComponentData<ActorVitalSet>(target);
            if (ActorHitAftermathStateUtility.IsDead(entityManager, target) && vitals.CurrentHealth <= 0f)
                return;

            state.Target = target;
            state.TargetPlacedRefId = PlacedRefId(entityManager, target);
            state.SecondsRemaining = displaySeconds;
            state.FadeSeconds = fadeSeconds;
            state.LastKnownHealthFill = ComputeHealthFill(entityManager, target, vitals);
            state.Visible = 1;
        }

        static float ComputeHealthFill(EntityManager entityManager, Entity target, in ActorVitalSet vitals)
        {
            if (vitals.ModifiedHealthBase <= 0f)
                throw new InvalidOperationException($"[VVardenfell][HUD] Enemy health target ref={PlacedRefId(entityManager, target)} has non-positive modified health base {vitals.ModifiedHealthBase}.");

            if (vitals.CurrentHealth < 1f)
                return 0f;

            return math.saturate(vitals.CurrentHealth / vitals.ModifiedHealthBase);
        }

        static uint PlacedRefId(EntityManager entityManager, Entity entity)
            => entityManager.GetComponentData<PlacedRefIdentity>(entity).Value;
    }
}


