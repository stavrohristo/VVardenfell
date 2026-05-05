using System;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindDamageSystemGroup))]
    [UpdateAfter(typeof(MorrowindMeleeDamageRollSystem))]
    [UpdateBefore(typeof(MorrowindArmorDamageSystem))]
    public partial class MorrowindNormalWeaponResistanceSystem : SystemBase
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
                throw new InvalidOperationException("[VVardenfell][ContentBlob] Normal weapon resistance requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            foreach (var damage in SystemAPI.Query<RefRW<MorrowindPendingDamageEvent>>())
            {
                if (damage.ValueRO.NormalWeapon == 0)
                    continue;

                Entity target = damage.ValueRO.Target;
                if (target == Entity.Null || !EntityManager.Exists(target))
                    throw new InvalidOperationException("[VVardenfell][Damage] Normal weapon damage target entity is missing.");
                if (!EntityManager.HasBuffer<ActorActiveMagicEffect>(target))
                    throw new InvalidOperationException("[VVardenfell][Damage] Normal weapon damage target has no ActorActiveMagicEffect buffer.");

                var targetEffects = EntityManager.GetBuffer<ActorActiveMagicEffect>(target, true);
                damage.ValueRW.Amount = MorrowindMeleeCombatMechanics.ApplyNormalWeaponResistanceEffects(
                    ref content,
                    targetEffects,
                    damage.ValueRO.Amount);
            }
        }
    }
}
