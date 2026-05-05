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
    [UpdateAfter(typeof(MorrowindNormalWeaponResistanceSystem))]
    [UpdateBefore(typeof(MorrowindDifficultyDamageSystem))]
    public partial class MorrowindArmorDamageSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindPendingDamageEvent>();
            RequireForUpdate<MorrowindCombatRuntimeState>();
            RequireForUpdate<RuntimeContentBlobReference>();
        }

        protected override void OnUpdate()
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][Damage] Armor damage requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;
            ref var combatState = ref SystemAPI.GetSingletonRW<MorrowindCombatRuntimeState>().ValueRW;
            var random = new Unity.Mathematics.Random(combatState.RandomState == 0u ? 0x6E624EB7u : combatState.RandomState);

            foreach (var damage in SystemAPI.Query<RefRW<MorrowindPendingDamageEvent>>())
            {
                if (!IsArmorMitigatedDamage(damage.ValueRO.SourceKind)
                    || damage.ValueRO.TargetVital != MorrowindDamageTargetVital.Health
                    || damage.ValueRO.Amount <= 0f)
                {
                    continue;
                }

                float original = damage.ValueRO.Amount;
                damage.ValueRW.Amount = MorrowindArmorDamageUtility.ApplyArmorToHealthDamage(
                    ref content,
                    EntityManager,
                    damage.ValueRO.Target,
                    original,
                    (uint)random.NextInt(100),
                    out var impact);
                if (!IsUnarmedCreatureAttack(ref content, damage.ValueRO))
                    MorrowindArmorDamageUtility.ApplyArmorConditionDamage(ref content, EntityManager, impact);
                damage.ValueRW.ArmorImpact = impact;
            }

            combatState.RandomState = random.state == 0u ? 0x6E624EB7u : random.state;
        }

        bool IsUnarmedCreatureAttack(ref RuntimeContentBlob content, in MorrowindPendingDamageEvent damage)
        {
            if (damage.SourceKind != MorrowindDamageSourceKind.HandToHand)
                return false;
            if (damage.Attacker == Entity.Null || !EntityManager.Exists(damage.Attacker))
                throw new InvalidOperationException("[VVardenfell][Damage] Armor condition attack has a missing attacker.");
            if (EntityManager.HasComponent<PlayerTag>(damage.Attacker))
                return false;
            if (!EntityManager.HasComponent<ActorSpawnSource>(damage.Attacker))
                throw new InvalidOperationException($"[VVardenfell][Damage] Armor condition attacker entity={damage.Attacker.Index}:{damage.Attacker.Version} has no ActorSpawnSource.");

            var source = EntityManager.GetComponentData<ActorSpawnSource>(damage.Attacker);
            if (!source.Definition.IsValid)
                throw new InvalidOperationException($"[VVardenfell][Damage] Armor condition attacker entity={damage.Attacker.Index}:{damage.Attacker.Version} has invalid actor definition.");

            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref content, source.Definition);
            return actor.Kind == ActorDefKind.Creature;
        }

        static bool IsArmorMitigatedDamage(MorrowindDamageSourceKind sourceKind)
            => sourceKind == MorrowindDamageSourceKind.Weapon
               || sourceKind == MorrowindDamageSourceKind.HandToHand;
    }
}


