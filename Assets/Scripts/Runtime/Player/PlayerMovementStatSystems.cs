using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Magic;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup), OrderFirst = true)]
    public partial class ActorActiveMagicEffectSystem : SystemBase
    {
        const short BurdenEffectId = 7;
        const short FeatherEffectId = 8;
        const short JumpEffectId = 9;
        const bool InjectDebugBuffs = false;
        const string DebugBuffSourceId = "vv_debug_buff_test";

        EntityQuery _actorQuery;

        protected override void OnCreate()
        {
            _actorQuery = GetEntityQuery(
                ComponentType.ReadOnly<ActorKnownSpell>(),
                ComponentType.ReadWrite<ActorActiveMagicEffect>(),
                ComponentType.ReadWrite<ActorActiveSpell>(),
                ComponentType.ReadWrite<ActorEffectStatModifiers>(),
                ComponentType.ReadWrite<ActorVitalSet>(),
                ComponentType.ReadOnly<ActorActiveMagicEffectDirty>());

            RequireForUpdate(_actorQuery);
        }

        protected override void OnUpdate()
        {
            float dt = math.max(0f, SystemAPI.Time.DeltaTime);
            foreach (var (knownSpells, activeEffects, activeSpells, modifiers, vitals, entity) in
                     SystemAPI.Query<DynamicBuffer<ActorKnownSpell>, DynamicBuffer<ActorActiveMagicEffect>, DynamicBuffer<ActorActiveSpell>, RefRW<ActorEffectStatModifiers>, RefRW<ActorVitalSet>>()
                         .WithEntityAccess())
            {
                bool dirty = EntityManager.IsComponentEnabled<ActorActiveMagicEffectDirty>(entity);
                if (dirty)
                    RebuildPassiveSpellEffects(RuntimeContentDatabase.Active, knownSpells, activeSpells, activeEffects);
                InjectDebugActiveEffects(activeEffects);
                bool changed = ApplyAndTickActiveEffects(RuntimeContentDatabase.Active, entity, activeSpells, activeEffects, dt);

                if (dirty || changed)
                {
                    modifiers.ValueRW = BuildSupportedModifiers(activeEffects);
                    PlayerEncumbranceDirtyUtility.MarkIfPlayer(EntityManager, entity);
                    EntityManager.SetComponentEnabled<ActorActiveMagicEffectDirty>(entity, false);
                }
            }
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
                if (activeEffects[i].SourceId.Equals(RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(DebugBuffSourceId)))
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
                SourceName = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(sourceName),
                SourceId = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(DebugBuffSourceId),
            });
        }

        static void RebuildPassiveSpellEffects(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<ActorKnownSpell> knownSpells,
            DynamicBuffer<ActorActiveSpell> activeSpells,
            DynamicBuffer<ActorActiveMagicEffect> activeEffects)
        {
            for (int i = activeEffects.Length - 1; i >= 0; i--)
            {
                if (activeEffects[i].SourceKind == ActorActiveMagicEffectSourceKind.PassiveSpell)
                    activeEffects.RemoveAt(i);
            }

            for (int i = activeSpells.Length - 1; i >= 0; i--)
            {
                if (activeSpells[i].SourceKind == ActorActiveSpellSourceKind.PassiveSpell)
                    activeSpells.RemoveAt(i);
            }

            if (contentDb?.Data.Spells == null || contentDb.Data.MagicEffectInstances == null)
                return;

            for (int i = 0; i < knownSpells.Length; i++)
            {
                var handle = knownSpells[i].Spell;
                if (!handle.IsValid || handle.Index < 0 || handle.Index >= contentDb.Data.Spells.Length)
                    continue;

                ref readonly var spell = ref contentDb.Get(handle);
                if (!MorrowindActorMagicUtility.IsPassiveSpellType(spell.SpellType) || spell.EffectStartIndex < 0 || spell.EffectCount <= 0)
                    continue;

                int activeSpellId = -(i + 1);
                activeSpells.Add(new ActorActiveSpell
                {
                    ActiveSpellId = activeSpellId,
                    Spell = handle,
                    SourceKind = ActorActiveSpellSourceKind.PassiveSpell,
                    Flags = ActorActiveSpellFlags.SpellStore | ActorActiveSpellFlags.IgnoreResistances | ActorActiveSpellFlags.IgnoreReflect | ActorActiveSpellFlags.IgnoreSpellAbsorption,
                    SourceName = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(string.IsNullOrWhiteSpace(spell.Name) ? spell.Id : spell.Name.Trim()),
                    SourceId = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(spell.Id),
                });

                int available = math.max(0, contentDb.Data.MagicEffectInstances.Length - spell.EffectStartIndex);
                int count = math.min(spell.EffectCount, available);
                for (int effectIndex = 0; effectIndex < count; effectIndex++)
                {
                    var effect = contentDb.Data.MagicEffectInstances[spell.EffectStartIndex + effectIndex];
                    activeEffects.Add(new ActorActiveMagicEffect
                    {
                        ActiveSpellId = activeSpellId,
                        EffectId = effect.EffectId,
                        EffectIndex = (short)effectIndex,
                        Skill = effect.Skill,
                        Attribute = effect.Attribute,
                        Magnitude = math.max(effect.MagnitudeMin, effect.MagnitudeMax),
                        MagnitudeMin = effect.MagnitudeMin,
                        MagnitudeMax = effect.MagnitudeMax,
                        DurationSeconds = -1f,
                        TimeLeftSeconds = -1f,
                        Applied = 1,
                        IgnoreResistances = 1,
                        IgnoreReflect = 1,
                        IgnoreSpellAbsorption = 1,
                        SourceKind = ActorActiveMagicEffectSourceKind.PassiveSpell,
                        SourceName = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(string.IsNullOrWhiteSpace(spell.Name) ? spell.Id : spell.Name.Trim()),
                        SourceId = RuntimeFixedStringUtility.ToFixed64OrDefaultWhiteSpace(spell.Id),
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

        bool ApplyAndTickActiveEffects(RuntimeContentDatabase contentDb, Entity entity, DynamicBuffer<ActorActiveSpell> activeSpells, DynamicBuffer<ActorActiveMagicEffect> activeEffects, float deltaSeconds)
        {
            if (contentDb == null)
                throw new System.InvalidOperationException("[VVardenfell][Magic] Active magic effect update requires active runtime content.");

            bool changed = false;
            for (int i = 0; i < activeEffects.Length; i++)
                changed |= MorrowindMagicEffectApplicationUtility.ApplyOrTick(EntityManager, contentDb, entity, activeSpells, activeEffects, i, deltaSeconds);

            for (int i = activeEffects.Length - 1; i >= 0; i--)
            {
                if (activeEffects[i].Remove != 0)
                {
                    activeEffects.RemoveAt(i);
                    changed = true;
                }
            }

            return changed;
        }

    }

    [UpdateAfter(typeof(ActorActiveMagicEffectSystem))]
    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup), OrderFirst = true)]
    public partial class PlayerActorEncumbranceSystem : SystemBase
    {
        EntityQuery _dirtyPlayerQuery;

        protected override void OnCreate()
        {
            _dirtyPlayerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadWrite<ActorAttributeSet>(),
                ComponentType.ReadWrite<ActorEffectStatModifiers>(),
                ComponentType.ReadWrite<ActorDerivedMovementStats>(),
                ComponentType.ReadOnly<PlayerInventoryItem>(),
                ComponentType.ReadOnly<PlayerEncumbranceDirty>());

            RequireForUpdate(_dirtyPlayerQuery);
        }

        protected override void OnUpdate()
        {
            var contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
                return;

            foreach (var (attributes, effectModifiers, derived, inventory, entity) in
                     SystemAPI.Query<
                             RefRO<ActorAttributeSet>,
                             RefRO<ActorEffectStatModifiers>,
                             RefRW<ActorDerivedMovementStats>,
                             DynamicBuffer<PlayerInventoryItem>>()
                         .WithAll<PlayerTag, PlayerEncumbranceDirty>()
                         .WithEntityAccess())
            {
                float inventoryWeight = SumInventoryWeight(contentDb, inventory);

                derived.ValueRW.CarryCapacity = MorrowindActorMovementStats.ComputeCarryCapacity(contentDb, attributes.ValueRO);
                derived.ValueRW.Encumbrance = MorrowindActorMovementStats.ComputeEncumbrance(effectModifiers.ValueRO, inventoryWeight);
                derived.ValueRW.NormalizedEncumbrance = MorrowindActorMovementStats.ComputeNormalizedEncumbrance(
                    derived.ValueRO.Encumbrance,
                    derived.ValueRO.CarryCapacity);

                EntityManager.SetComponentEnabled<PlayerEncumbranceDirty>(entity, false);
            }
        }

        static float SumInventoryWeight(RuntimeContentDatabase contentDb, DynamicBuffer<PlayerInventoryItem> inventory)
        {
            float totalWeight = 0f;
            for (int i = 0; i < inventory.Length; i++)
            {
                var entry = inventory[i];
                if (entry.Count <= 0 || !entry.Content.IsValid)
                    continue;

                if (!RuntimeContentMetadataResolver.TryResolveCarryable(contentDb, entry.Content, out var metadata))
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
                ComponentType.ReadOnly<MorrowindMovementSpeed>(),
                ComponentType.ReadOnly<MorrowindMovementInput>(),
                ComponentType.ReadOnly<MorrowindMovementState>());

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
            var movementSpeed = _playerQuery.GetSingleton<MorrowindMovementSpeed>();
            var movementInput = _playerQuery.GetSingleton<MorrowindMovementInput>();
            var movementState = _playerQuery.GetSingleton<MorrowindMovementState>();

            MorrowindActorMovementStats.ApplyVitalBases(RuntimeContentDatabase.Active, attributes, ref vitals, initializeMissingCurrents: false);
            var context = MorrowindPlayerSpeedResolver.Build(RuntimeContentDatabase.Active, attributes, skills, vitals, effectModifiers, derived, movementSpeed);

            float fatigue = vitals.CurrentFatigue;
            fatigue -= context.GetMovementFatigueLossPerSecond(
                movementInput.RunHeld && !movementState.SneakHeld,
                movementState.SneakHeld,
                movementState.SpeedFactor) * dt;
            if (movementState.JumpAccepted)
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
                ComponentType.ReadWrite<ActorDerivedMovementStats>(),
                ComponentType.ReadWrite<MorrowindMovementSpeed>());

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
            ref var movementSpeed = ref _playerQuery.GetSingletonRW<MorrowindMovementSpeed>().ValueRW;
            movementSpeed = MorrowindActorMovementStats.BuildPlayerMovementSpeed(
                RuntimeContentDatabase.Active,
                attributes,
                skills,
                vitals,
                effectModifiers,
                derived);
        }
    }
}
