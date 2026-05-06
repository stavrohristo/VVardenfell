using System;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindDamageSystemGroup))]
    [UpdateAfter(typeof(MorrowindNormalWeaponResistanceSystem))]
    [UpdateBefore(typeof(MorrowindBlockDamageSystem))]
    public partial struct MorrowindKnockoutDamageMultiplierSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MorrowindPendingDamageEvent>();
            state.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][Damage] KO damage multiplier requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;
            float knockoutDamageMult = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fCombatKODamageMult);

            foreach (var damage in SystemAPI.Query<RefRW<MorrowindPendingDamageEvent>>())
            {
                if (!IsMeleeDamage(damage.ValueRO.SourceKind))
                    continue;
                if (damage.ValueRO.TargetVital != MorrowindDamageTargetVital.Health)
                    continue;

                Entity target = damage.ValueRO.Target;
                RequireTargetComposition(state.EntityManager, target);
                if (damage.ValueRO.Amount <= 0f)
                    continue;

                var aftermath = state.EntityManager.GetComponentData<ActorHitAftermathState>(target);
                if (aftermath.KnockedDown == 0)
                    continue;

                damage.ValueRW.Amount *= knockoutDamageMult;
            }
        }

        static bool IsMeleeDamage(MorrowindDamageSourceKind sourceKind)
            => sourceKind == MorrowindDamageSourceKind.Weapon
               || sourceKind == MorrowindDamageSourceKind.HandToHand;

        static void RequireTargetComposition(EntityManager entityManager, Entity target)
        {
            if (target == Entity.Null || !entityManager.Exists(target))
                throw new InvalidOperationException("[VVardenfell][Damage] KO damage target entity is missing.");
            if (!entityManager.HasComponent<ActorVitalSet>(target))
                throw new InvalidOperationException($"[VVardenfell][Damage] KO damage target ref={PlacedRefId(entityManager, target)} has no ActorVitalSet.");
            if (!entityManager.HasComponent<ActorHitAftermathState>(target))
                throw new InvalidOperationException($"[VVardenfell][Damage] KO damage target ref={PlacedRefId(entityManager, target)} has no ActorHitAftermathState.");
        }

        static uint PlacedRefId(EntityManager entityManager, Entity entity)
            => entityManager.HasComponent<PlacedRefIdentity>(entity)
                ? entityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
    }
}


