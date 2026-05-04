using System;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Combat
{
    [UpdateInGroup(typeof(MorrowindDamageSystemGroup))]
    [UpdateAfter(typeof(MorrowindArmorDamageSystem))]
    [UpdateBefore(typeof(MorrowindDamageApplySystem))]
    public partial class MorrowindDifficultyDamageSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindPendingDamageEvent>();
        }

        protected override void OnUpdate()
        {
            RuntimeContentDatabase contentDb = RuntimeContentDatabase.Active
                ?? throw new InvalidOperationException("[VVardenfell][Damage] Runtime content database is not loaded.");
            if (!SystemAPI.TryGetSingleton<MorrowindCombatSettings>(out var settings))
                throw new InvalidOperationException("[VVardenfell][Damage] Missing MorrowindCombatSettings singleton.");
            if (settings.Difficulty < -100 || settings.Difficulty > 100)
                throw new InvalidOperationException($"[VVardenfell][Damage] Difficulty {settings.Difficulty} is outside Morrowind's -100..100 range.");

            float difficultyMult = contentDb.RequireGameSettingFloat("fDifficultyMult");
            if (difficultyMult <= 0f)
                throw new InvalidOperationException($"[VVardenfell][Damage] GMST fDifficultyMult must be positive; got {difficultyMult}.");

            foreach (var damage in SystemAPI.Query<RefRW<MorrowindPendingDamageEvent>>())
            {
                if (damage.ValueRO.TargetVital != MorrowindDamageTargetVital.Health || damage.ValueRO.Amount <= 0f)
                    continue;

                bool attackerIsPlayer = IsPlayer(damage.ValueRO.Attacker);
                bool targetIsPlayer = IsPlayer(damage.ValueRO.Target);
                if (attackerIsPlayer == targetIsPlayer)
                    continue;

                float difficultyTerm = settings.Difficulty * 0.01f;
                float multiplierOffset;
                if (targetIsPlayer)
                    multiplierOffset = difficultyTerm > 0f ? difficultyTerm * difficultyMult : difficultyTerm / difficultyMult;
                else
                    multiplierOffset = difficultyTerm > 0f ? -difficultyTerm / difficultyMult : -difficultyTerm * difficultyMult;

                damage.ValueRW.Amount = math.max(0f, damage.ValueRO.Amount * (1f + multiplierOffset));
            }
        }

        bool IsPlayer(Entity entity)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity))
                throw new InvalidOperationException("[VVardenfell][Damage] Difficulty scaling attack participant entity is missing.");

            return EntityManager.HasComponent<PlayerTag>(entity);
        }
    }
}
