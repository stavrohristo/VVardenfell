using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup), OrderFirst = true)]
    public partial class PlayerActorEncumbranceSystem : SystemBase
    {
        EntityQuery _playerQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<ActorAttributeSet>(),
                ComponentType.ReadWrite<ActorEffectStatModifiers>(),
                ComponentType.ReadWrite<ActorDerivedMovementStats>());

            RequireForUpdate(_playerQuery);
            RequireForUpdate<PlayerInventoryItem>();
        }

        protected override void OnUpdate()
        {
            if (_playerQuery.IsEmptyIgnoreFilter)
                return;

            var inventory = SystemAPI.GetSingletonBuffer<PlayerInventoryItem>();
            var attributes = _playerQuery.GetSingleton<ActorAttributeSet>();
            var effectModifiers = _playerQuery.GetSingleton<ActorEffectStatModifiers>();
            ref var derived = ref _playerQuery.GetSingletonRW<ActorDerivedMovementStats>().ValueRW;

            float inventoryWeight = SumInventoryWeight(RuntimeContentDatabase.Active, inventory);
            derived.CarryCapacity = MorrowindActorMovementStats.ComputeCarryCapacity(RuntimeContentDatabase.Active, attributes);
            derived.Encumbrance = MorrowindActorMovementStats.ComputeEncumbrance(effectModifiers, inventoryWeight);
            derived.NormalizedEncumbrance = MorrowindActorMovementStats.ComputeNormalizedEncumbrance(
                derived.Encumbrance,
                derived.CarryCapacity);
        }

        static float SumInventoryWeight(RuntimeContentDatabase contentDb, DynamicBuffer<PlayerInventoryItem> inventory)
        {
            if (contentDb == null)
                return 0f;

            float totalWeight = 0f;
            for (int i = 0; i < inventory.Length; i++)
            {
                var entry = inventory[i];
                if (entry.Count <= 0 || !entry.Content.IsValid)
                    continue;

                if (!InventoryWindowStateSystem.TryResolveCarryableMetadata(contentDb, entry.Content, out var metadata))
                    continue;

                if (metadata.Weight < 0f)
                    continue;

                totalWeight += metadata.Weight * entry.Count;
            }

            return totalWeight;
        }
    }

    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup))]
    [UpdateAfter(typeof(PlayerActorEncumbranceSystem))]
    public partial class PlayerActorFatigueSystem : SystemBase
    {
        EntityQuery _playerQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<ActorAttributeSet>(),
                ComponentType.ReadOnly<ActorSkillSet>(),
                ComponentType.ReadWrite<ActorVitalSet>(),
                ComponentType.ReadOnly<ActorEffectStatModifiers>(),
                ComponentType.ReadOnly<ActorDerivedMovementStats>(),
                ComponentType.ReadOnly<MorrowindMovementIntent>(),
                ComponentType.ReadOnly<MorrowindMovementFrameTrace>());

            RequireForUpdate(_playerQuery);
        }

        protected override void OnUpdate()
        {
            if (_playerQuery.IsEmptyIgnoreFilter)
                return;

            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0f)
                return;

            var attributes = _playerQuery.GetSingleton<ActorAttributeSet>();
            var skills = _playerQuery.GetSingleton<ActorSkillSet>();
            ref var vitals = ref _playerQuery.GetSingletonRW<ActorVitalSet>().ValueRW;
            var effectModifiers = _playerQuery.GetSingleton<ActorEffectStatModifiers>();
            var derived = _playerQuery.GetSingleton<ActorDerivedMovementStats>();
            var intent = _playerQuery.GetSingleton<MorrowindMovementIntent>();
            var trace = _playerQuery.GetSingleton<MorrowindMovementFrameTrace>();

            MorrowindActorMovementStats.ApplyVitalBases(RuntimeContentDatabase.Active, attributes, ref vitals, initializeMissingCurrents: false);
            var context = MorrowindPlayerSpeedResolver.Build(RuntimeContentDatabase.Active, attributes, skills, vitals, effectModifiers, derived);

            float fatigue = vitals.CurrentFatigue;
            fatigue -= context.GetMovementFatigueLossPerSecond(intent.RunHeld && !intent.SneakHeld, intent.SneakHeld, intent.SpeedFactor) * dt;
            if (trace.JumpAccepted != 0)
                fatigue -= context.GetJumpFatigueLoss();

            if (fatigue < vitals.ModifiedFatigueBase)
                fatigue = math.min(vitals.ModifiedFatigueBase, fatigue + context.GetFatigueRestorePerSecond() * dt);

            vitals.CurrentFatigue = fatigue;
        }
    }

    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup))]
    [UpdateAfter(typeof(PlayerActorFatigueSystem))]
    public partial class PlayerActorDerivedMovementStatsSystem : SystemBase
    {
        EntityQuery _playerQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<ActorAttributeSet>(),
                ComponentType.ReadOnly<ActorSkillSet>(),
                ComponentType.ReadWrite<ActorVitalSet>(),
                ComponentType.ReadOnly<ActorEffectStatModifiers>(),
                ComponentType.ReadWrite<ActorDerivedMovementStats>());

            RequireForUpdate(_playerQuery);
        }

        protected override void OnUpdate()
        {
            if (_playerQuery.IsEmptyIgnoreFilter)
                return;

            var attributes = _playerQuery.GetSingleton<ActorAttributeSet>();
            var skills = _playerQuery.GetSingleton<ActorSkillSet>();
            ref var vitals = ref _playerQuery.GetSingletonRW<ActorVitalSet>().ValueRW;
            var effectModifiers = _playerQuery.GetSingleton<ActorEffectStatModifiers>();
            ref var derived = ref _playerQuery.GetSingletonRW<ActorDerivedMovementStats>().ValueRW;
            MorrowindActorMovementStats.ApplyVitalBases(RuntimeContentDatabase.Active, attributes, ref vitals, initializeMissingCurrents: false);
            MorrowindActorMovementStats.ApplyMovementDerived(RuntimeContentDatabase.Active, attributes, skills, vitals, effectModifiers, ref derived);
        }
    }
}
