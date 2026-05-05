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
    public partial class MorrowindKnockoutDamageMultiplierSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindPendingDamageEvent>();
            RequireForUpdate<RuntimeContentBlobReference>();
        }

        protected override void OnUpdate()
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
                RequireTargetComposition(target);
                if (damage.ValueRO.Amount <= 0f)
                    continue;

                var aftermath = EntityManager.GetComponentData<ActorHitAftermathState>(target);
                if (aftermath.KnockedDown == 0)
                    continue;

                damage.ValueRW.Amount *= knockoutDamageMult;
            }
        }

        static bool IsMeleeDamage(MorrowindDamageSourceKind sourceKind)
            => sourceKind == MorrowindDamageSourceKind.Weapon
               || sourceKind == MorrowindDamageSourceKind.HandToHand;

        void RequireTargetComposition(Entity target)
        {
            if (target == Entity.Null || !EntityManager.Exists(target))
                throw new InvalidOperationException("[VVardenfell][Damage] KO damage target entity is missing.");
            if (!EntityManager.HasComponent<ActorVitalSet>(target))
                throw new InvalidOperationException($"[VVardenfell][Damage] KO damage target ref={PlacedRefId(target)} has no ActorVitalSet.");
            if (!EntityManager.HasComponent<ActorHitAftermathState>(target))
                throw new InvalidOperationException($"[VVardenfell][Damage] KO damage target ref={PlacedRefId(target)} has no ActorHitAftermathState.");
        }

        uint PlacedRefId(Entity entity)
            => EntityManager.HasComponent<PlacedRefIdentity>(entity)
                ? EntityManager.GetComponentData<PlacedRefIdentity>(entity).Value
                : 0u;
    }
}


