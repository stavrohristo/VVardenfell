using Unity.Collections;
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
    public partial class PlayerActiveMagicEffectSystem : SystemBase
    {
        const short BurdenEffectId = 7;
        const short FeatherEffectId = 8;
        const short JumpEffectId = 9;
        const bool InjectDebugBuffs = false;
        const string DebugBuffSourceId = "vv_debug_buff_test";

        EntityQuery _playerQuery;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<PlayerKnownSpell>(),
                ComponentType.ReadWrite<ActorActiveMagicEffect>(),
                ComponentType.ReadWrite<ActorEffectStatModifiers>());

            RequireForUpdate(_playerQuery);
        }

        protected override void OnUpdate()
        {
            if (_playerQuery.IsEmptyIgnoreFilter)
                return;

            var knownSpells = _playerQuery.GetSingletonBuffer<PlayerKnownSpell>(isReadOnly: true);
            var activeEffects = _playerQuery.GetSingletonBuffer<ActorActiveMagicEffect>(isReadOnly: false);
            RebuildPassiveSpellEffects(RuntimeContentDatabase.Active, knownSpells, activeEffects);
            InjectDebugActiveEffects(activeEffects);

            ref var modifiers = ref _playerQuery.GetSingletonRW<ActorEffectStatModifiers>().ValueRW;
            modifiers = BuildSupportedModifiers(activeEffects);
        }

        static void InjectDebugActiveEffects(DynamicBuffer<ActorActiveMagicEffect> activeEffects)
        {
            if (!InjectDebugBuffs || HasDebugBuffs(activeEffects))
                return;

            AddDebugEffect(activeEffects, 0, 0f, "Water Breathing");
            AddDebugEffect(activeEffects, 4, 15f, "Fire Shield");
            AddDebugEffect(activeEffects, FeatherEffectId, 25f, "Feather");
            AddDebugEffect(activeEffects, JumpEffectId, 20f, "Jump");
        }

        static bool HasDebugBuffs(DynamicBuffer<ActorActiveMagicEffect> activeEffects)
        {
            for (int i = 0; i < activeEffects.Length; i++)
            {
                if (activeEffects[i].SourceId.Equals(ToFixed64(DebugBuffSourceId)))
                    return true;
            }

            return false;
        }

        static void AddDebugEffect(
            DynamicBuffer<ActorActiveMagicEffect> activeEffects,
            short effectId,
            float magnitude,
            string sourceName)
        {
            activeEffects.Add(new ActorActiveMagicEffect
            {
                EffectId = effectId,
                Skill = -1,
                Attribute = -1,
                Magnitude = magnitude,
                DurationSeconds = -1f,
                TimeLeftSeconds = -1f,
                Applied = 1,
                SourceKind = ActorActiveMagicEffectSourceKind.TimedSpell,
                SourceName = ToFixed64(sourceName),
                SourceId = ToFixed64(DebugBuffSourceId),
            });
        }

        static void RebuildPassiveSpellEffects(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<PlayerKnownSpell> knownSpells,
            DynamicBuffer<ActorActiveMagicEffect> activeEffects)
        {
            for (int i = activeEffects.Length - 1; i >= 0; i--)
            {
                if (activeEffects[i].SourceKind == ActorActiveMagicEffectSourceKind.PassiveSpell)
                    activeEffects.RemoveAt(i);
            }

            if (contentDb?.Data.Spells == null || contentDb.Data.MagicEffectInstances == null)
                return;

            for (int i = 0; i < knownSpells.Length; i++)
            {
                var handle = knownSpells[i].Spell;
                if (!handle.IsValid || handle.Index < 0 || handle.Index >= contentDb.Data.Spells.Length)
                    continue;

                ref readonly var spell = ref contentDb.Get(handle);
                if (!IsPassiveSpellType(spell.SpellType) || spell.EffectStartIndex < 0 || spell.EffectCount <= 0)
                    continue;

                int available = math.max(0, contentDb.Data.MagicEffectInstances.Length - spell.EffectStartIndex);
                int count = math.min(spell.EffectCount, available);
                for (int effectIndex = 0; effectIndex < count; effectIndex++)
                {
                    var effect = contentDb.Data.MagicEffectInstances[spell.EffectStartIndex + effectIndex];
                    activeEffects.Add(new ActorActiveMagicEffect
                    {
                        EffectId = effect.EffectId,
                        Skill = effect.Skill,
                        Attribute = effect.Attribute,
                        Magnitude = math.max(effect.MagnitudeMin, effect.MagnitudeMax),
                        DurationSeconds = -1f,
                        TimeLeftSeconds = -1f,
                        Applied = 1,
                        SourceKind = ActorActiveMagicEffectSourceKind.PassiveSpell,
                        SourceName = ToFixed64(string.IsNullOrWhiteSpace(spell.Name) ? spell.Id : spell.Name.Trim()),
                        SourceId = ToFixed64(spell.Id),
                    });
                }
            }
        }

        static ActorEffectStatModifiers BuildSupportedModifiers(DynamicBuffer<ActorActiveMagicEffect> activeEffects)
        {
            var modifiers = new ActorEffectStatModifiers();
            for (int i = 0; i < activeEffects.Length; i++)
            {
                var effect = activeEffects[i];
                if (effect.Applied == 0)
                    continue;
                if (effect.DurationSeconds >= 0f && effect.TimeLeftSeconds <= 0f)
                    continue;

                switch (effect.EffectId)
                {
                    case JumpEffectId:
                        modifiers.JumpMagnitude += math.max(0f, effect.Magnitude);
                        break;
                    case FeatherEffectId:
                        modifiers.FeatherMagnitude += math.max(0f, effect.Magnitude);
                        break;
                    case BurdenEffectId:
                        modifiers.BurdenMagnitude += math.max(0f, effect.Magnitude);
                        break;
                }
            }

            return modifiers;
        }

        static bool IsPassiveSpellType(int spellType)
            => spellType is 1 or 2 or 3 or 4;

        static FixedString64Bytes ToFixed64(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return default;

            var result = default(FixedString64Bytes);
            result.CopyFromTruncated(value);
            return result;
        }
    }

    [UpdateAfter(typeof(PlayerActiveMagicEffectSystem))]
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
