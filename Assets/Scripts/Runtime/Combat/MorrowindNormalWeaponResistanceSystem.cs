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
    public partial struct MorrowindNormalWeaponResistanceSystem : ISystem
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
                throw new InvalidOperationException("[VVardenfell][ContentBlob] Normal weapon resistance requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            foreach (var damage in SystemAPI.Query<RefRW<MorrowindPendingDamageEvent>>())
            {
                if (damage.ValueRO.NormalWeapon == 0)
                    continue;

                Entity target = damage.ValueRO.Target;
                if (target == Entity.Null || !state.EntityManager.Exists(target))
                    throw new InvalidOperationException("[VVardenfell][Damage] Normal weapon damage target entity is missing.");
                if (!state.EntityManager.HasBuffer<ActorActiveMagicEffect>(target))
                    throw new InvalidOperationException("[VVardenfell][Damage] Normal weapon damage target has no ActorActiveMagicEffect buffer.");

                var targetEffects = state.EntityManager.GetBuffer<ActorActiveMagicEffect>(target, true);
                damage.ValueRW.Amount = MorrowindMeleeCombatMechanics.ApplyNormalWeaponResistanceEffects(
                    ref content,
                    targetEffects,
                    damage.ValueRO.Amount);
            }
        }
    }
}
