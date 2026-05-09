using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Magic;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Player
{
    [UpdateInGroup(typeof(MorrowindPhysicsPostQueryMutationSystemGroup), OrderFirst = true)]
    public partial struct ActorActiveMagicEffectSystem : ISystem
    {
        const short BurdenEffectId = 7;
        const short FeatherEffectId = 8;
        const short JumpEffectId = 9;

        EntityQuery _dirtyActorQuery;
        EntityQuery _tickingActorQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _dirtyActorQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<ActorKnownSpell>(),
                ComponentType.ReadWrite<ActorActiveMagicEffect>(),
                ComponentType.ReadWrite<ActorActiveSpell>(),
                ComponentType.ReadWrite<ActorAttributeSet>(),
                ComponentType.ReadWrite<ActorAttributeBaseSet>(),
                ComponentType.ReadWrite<ActorAttributeDamageSet>(),
                ComponentType.ReadWrite<ActorAttributeModifierSet>(),
                ComponentType.ReadWrite<ActorSkillSet>(),
                ComponentType.ReadWrite<ActorSkillBaseSet>(),
                ComponentType.ReadWrite<ActorSkillDamageSet>(),
                ComponentType.ReadWrite<ActorSkillModifierSet>(),
                ComponentType.ReadWrite<ActorEffectStatModifiers>(),
                ComponentType.ReadWrite<ActorVitalSet>(),
                ComponentType.ReadWrite<ActorVitalBaseSet>(),
                ComponentType.ReadWrite<ActorVitalModifierSet>(),
                ComponentType.ReadOnly<ActorActiveMagicEffectDirty>());

            _tickingActorQuery = systemState.GetEntityQuery(
                ComponentType.ReadOnly<ActorKnownSpell>(),
                ComponentType.ReadWrite<ActorActiveMagicEffect>(),
                ComponentType.ReadWrite<ActorActiveSpell>(),
                ComponentType.ReadWrite<ActorAttributeSet>(),
                ComponentType.ReadWrite<ActorAttributeBaseSet>(),
                ComponentType.ReadWrite<ActorAttributeDamageSet>(),
                ComponentType.ReadWrite<ActorAttributeModifierSet>(),
                ComponentType.ReadWrite<ActorSkillSet>(),
                ComponentType.ReadWrite<ActorSkillBaseSet>(),
                ComponentType.ReadWrite<ActorSkillDamageSet>(),
                ComponentType.ReadWrite<ActorSkillModifierSet>(),
                ComponentType.ReadWrite<ActorEffectStatModifiers>(),
                ComponentType.ReadWrite<ActorVitalSet>(),
                ComponentType.ReadWrite<ActorVitalBaseSet>(),
                ComponentType.ReadWrite<ActorVitalModifierSet>(),
                ComponentType.ReadOnly<ActorActiveMagicEffectTicking>());

            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            int tickingCount = _tickingActorQuery.CalculateEntityCount();
            int dirtyCount = _dirtyActorQuery.CalculateEntityCount();
            if (tickingCount == 0 && dirtyCount == 0)
                return;

            float dt = math.max(0f, SystemAPI.Time.DeltaTime);
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][ContentBlob] Active magic effect update requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            if (tickingCount > 0 && dt > 0f)
                TickActiveMagicEffects(ref systemState, ref content, dt);

            if (dirtyCount > 0)
                RebuildDirtyActiveMagicEffects(ref systemState, ref content);
        }

        void TickActiveMagicEffects(ref SystemState systemState, ref RuntimeContentBlob content, float deltaSeconds)
        {
            foreach (var (knownSpells, activeEffects, activeSpells, modifiers, entity) in
                     SystemAPI.Query<DynamicBuffer<ActorKnownSpell>, DynamicBuffer<ActorActiveMagicEffect>, DynamicBuffer<ActorActiveSpell>, RefRW<ActorEffectStatModifiers>>()
                         .WithAll<ActorActiveMagicEffectTicking>()
                         .WithEntityAccess())
            {
                if (systemState.EntityManager.IsComponentEnabled<ActorActiveMagicEffectDirty>(entity))
                    continue;

                bool changed = ApplyAndTickActiveEffects(ref systemState, ref content, entity, knownSpells, activeSpells, activeEffects, deltaSeconds);
                if (changed)
                {
                    modifiers.ValueRW = BuildSupportedModifiers(activeEffects);
                    RecomputeMagicStatReadModel(systemState.EntityManager, entity, activeEffects, modifiers.ValueRW);
                    PlayerEncumbranceDirtyUtility.MarkIfPlayer(systemState.EntityManager, entity);
                }

                SetTickingEnabled(systemState.EntityManager, entity, RequiresActiveMagicEffectTick(activeEffects));
            }
        }

        void RebuildDirtyActiveMagicEffects(ref SystemState systemState, ref RuntimeContentBlob content)
        {
            foreach (var (knownSpells, activeEffects, activeSpells, modifiers, entity) in
                     SystemAPI.Query<DynamicBuffer<ActorKnownSpell>, DynamicBuffer<ActorActiveMagicEffect>, DynamicBuffer<ActorActiveSpell>, RefRW<ActorEffectStatModifiers>>()
                         .WithAll<ActorActiveMagicEffectDirty>()
                         .WithEntityAccess())
            {
                RebuildPassiveSpellEffects(ref content, knownSpells, activeSpells, activeEffects);
                ApplyAndTickActiveEffects(ref systemState, ref content, entity, knownSpells, activeSpells, activeEffects, 0f);

                modifiers.ValueRW = BuildSupportedModifiers(activeEffects);
                RecomputeMagicStatReadModel(systemState.EntityManager, entity, activeEffects, modifiers.ValueRW);
                PlayerEncumbranceDirtyUtility.MarkIfPlayer(systemState.EntityManager, entity);
                systemState.EntityManager.SetComponentEnabled<ActorActiveMagicEffectDirty>(entity, false);
                SetTickingEnabled(systemState.EntityManager, entity, RequiresActiveMagicEffectTick(activeEffects));
            }
        }

        static void SetTickingEnabled(EntityManager entityManager, Entity entity, bool enabled)
        {
            if (!entityManager.HasComponent<ActorActiveMagicEffectTicking>(entity))
                throw new System.InvalidOperationException($"[VVardenfell][Magic] Active magic effect actor {entity.Index}:{entity.Version} has no ActorActiveMagicEffectTicking marker.");
            entityManager.SetComponentEnabled<ActorActiveMagicEffectTicking>(entity, enabled);
        }

        static bool RequiresActiveMagicEffectTick(DynamicBuffer<ActorActiveMagicEffect> activeEffects)
        {
            for (int i = 0; i < activeEffects.Length; i++)
            {
                var effect = activeEffects[i];
                if (effect.Applied == 0 || effect.Remove != 0 || effect.DurationSeconds >= 0f)
                    return true;
            }

            return false;
        }

        static void RebuildPassiveSpellEffects(
            ref RuntimeContentBlob content,
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

            for (int i = 0; i < knownSpells.Length; i++)
            {
                var handle = knownSpells[i].Spell;
                if (!handle.IsValid || handle.Index < 0 || handle.Index >= content.Spells.Length)
                    throw new System.InvalidOperationException($"[VVardenfell][Magic] Passive known spell references invalid handle {handle.Value}.");

                ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref content, handle);
                if (!MorrowindActorMagicUtility.IsPassiveSpellType(spell.SpellType) || spell.EffectStartIndex < 0 || spell.EffectCount <= 0)
                    continue;
                MorrowindActorMagicUtility.RequireSpellEffectRange(ref content, ref spell);
                FixedString64Bytes sourceName = RuntimeFixedStringUtility.ToFixed64OrDefault(ref spell.Name);
                FixedString64Bytes sourceId = RuntimeFixedStringUtility.ToFixed64OrDefault(ref spell.Id);
                if (sourceName.IsEmpty)
                    sourceName = sourceId;

                int activeSpellId = -(i + 1);
                activeSpells.Add(new ActorActiveSpell
                {
                    ActiveSpellId = activeSpellId,
                    Spell = handle,
                    SourceKind = ActorActiveSpellSourceKind.PassiveSpell,
                    Flags = ActorActiveSpellFlags.SpellStore
                            | ActorActiveSpellFlags.IgnoreResistances
                            | ActorActiveSpellFlags.IgnoreReflect
                            | ActorActiveSpellFlags.IgnoreSpellAbsorption
                            | (spell.SpellType == 1 ? ActorActiveSpellFlags.AffectsBaseValues : ActorActiveSpellFlags.None),
                    SourceName = sourceName,
                    SourceId = sourceId,
                    SourceIdHash = spell.IdHash,
                });

                for (int effectIndex = 0; effectIndex < spell.EffectCount; effectIndex++)
                {
                    ref MagicEffectInstanceDef effect = ref content.MagicEffectInstances[spell.EffectStartIndex + effectIndex];
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
                        SourceName = sourceName,
                        SourceId = sourceId,
                        SourceIdHash = spell.IdHash,
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
                    case var effectId when effectId == MorrowindMagicEffectIds.FortifyMaximumMagicka:
                        modifiers.FortifyMaximumMagickaMagnitude += effect.Magnitude;
                        break;
                }
            }

            return modifiers;
        }

        static void RecomputeMagicStatReadModel(
            EntityManager entityManager,
            Entity entity,
            DynamicBuffer<ActorActiveMagicEffect> activeEffects,
            in ActorEffectStatModifiers effectModifiers)
        {
            var attributeBase = entityManager.GetComponentData<ActorAttributeBaseSet>(entity);
            var attributeDamage = entityManager.GetComponentData<ActorAttributeDamageSet>(entity);
            var attributeModifiers = entityManager.GetComponentData<ActorAttributeModifierSet>(entity);
            var skillBase = entityManager.GetComponentData<ActorSkillBaseSet>(entity);
            var skillDamage = entityManager.GetComponentData<ActorSkillDamageSet>(entity);
            var skillModifiers = entityManager.GetComponentData<ActorSkillModifierSet>(entity);
            var vitalBase = entityManager.GetComponentData<ActorVitalBaseSet>(entity);
            var vitalModifiers = new ActorVitalModifierSet();

            attributeModifiers.Value = default;
            skillModifiers.Value = default;
            for (int i = 0; i < activeEffects.Length; i++)
            {
                var effect = activeEffects[i];
                if (effect.Applied == 0 || effect.Remove != 0)
                    continue;
                if (effect.DurationSeconds >= 0f && effect.TimeLeftSeconds <= 0f)
                    continue;

                if (effect.EffectId == MorrowindMagicEffectIds.FortifyAttribute)
                    ActorMagicStatUtility.AddAttribute(ref attributeModifiers.Value, effect.Attribute, effect.Magnitude);
                else if (effect.EffectId == MorrowindMagicEffectIds.DrainAttribute || effect.EffectId == MorrowindMagicEffectIds.AbsorbAttribute)
                    ActorMagicStatUtility.AddAttribute(ref attributeModifiers.Value, effect.Attribute, -effect.Magnitude);
                else if (effect.EffectId == MorrowindMagicEffectIds.FortifySkill)
                    ActorMagicStatUtility.AddSkill(ref skillModifiers.Value, effect.Skill, effect.Magnitude);
                else if (effect.EffectId == MorrowindMagicEffectIds.DrainSkill || effect.EffectId == MorrowindMagicEffectIds.AbsorbSkill)
                    ActorMagicStatUtility.AddSkill(ref skillModifiers.Value, effect.Skill, -effect.Magnitude);
                else if (effect.EffectId == MorrowindMagicEffectIds.FortifyHealth)
                    vitalModifiers.Health += effect.Magnitude;
                else if (effect.EffectId == MorrowindMagicEffectIds.FortifyMagicka)
                    vitalModifiers.Magicka += effect.Magnitude;
                else if (effect.EffectId == MorrowindMagicEffectIds.FortifyFatigue)
                    vitalModifiers.Fatigue += effect.Magnitude;
                else if (effect.EffectId == MorrowindMagicEffectIds.DrainHealth)
                    vitalModifiers.Health -= effect.Magnitude;
                else if (effect.EffectId == MorrowindMagicEffectIds.DrainMagicka)
                    vitalModifiers.Magicka -= effect.Magnitude;
                else if (effect.EffectId == MorrowindMagicEffectIds.DrainFatigue)
                    vitalModifiers.Fatigue -= effect.Magnitude;
            }

            var seed = new ActorRuntimeStatSeed
            {
                AttributeBase = attributeBase.Value,
                AttributeDamage = attributeDamage.Value,
                AttributeModifiers = attributeModifiers.Value,
                SkillBase = skillBase.Value,
                SkillDamage = skillDamage.Value,
                SkillModifiers = skillModifiers.Value,
                VitalBase = vitalBase,
                VitalModifiers = vitalModifiers,
                Vitals = entityManager.GetComponentData<ActorVitalSet>(entity),
                EffectModifiers = effectModifiers,
            };
            ActorMagicStatUtility.RecomputeReadModel(ref seed);
            entityManager.SetComponentData(entity, seed.Attributes);
            entityManager.SetComponentData(entity, attributeModifiers);
            entityManager.SetComponentData(entity, seed.Skills);
            entityManager.SetComponentData(entity, skillModifiers);
            entityManager.SetComponentData(entity, seed.Vitals);
            entityManager.SetComponentData(entity, vitalModifiers);
        }

        bool ApplyAndTickActiveEffects(
            ref SystemState systemState,
            ref RuntimeContentBlob content,
            Entity entity,
            DynamicBuffer<ActorKnownSpell> knownSpells,
            DynamicBuffer<ActorActiveSpell> activeSpells,
            DynamicBuffer<ActorActiveMagicEffect> activeEffects,
            float deltaSeconds)
        {
            bool changed = false;
            for (int i = 0; i < activeEffects.Length; i++)
                changed |= MorrowindMagicEffectApplicationUtility.ApplyOrTick(systemState.EntityManager, ref content, entity, activeSpells, knownSpells, activeEffects, i, deltaSeconds);

            for (int i = activeEffects.Length - 1; i >= 0; i--)
            {
                if (activeEffects[i].Remove != 0)
                {
                    int activeSpellId = activeEffects[i].ActiveSpellId;
                    activeEffects.RemoveAt(i);
                    RemoveActiveSpellIfEmpty(activeSpells, activeEffects, activeSpellId);
                    changed = true;
                }
            }

            return changed;
        }

        static void RemoveActiveSpellIfEmpty(
            DynamicBuffer<ActorActiveSpell> activeSpells,
            DynamicBuffer<ActorActiveMagicEffect> activeEffects,
            int activeSpellId)
        {
            if (activeSpellId == 0)
                return;

            for (int i = 0; i < activeEffects.Length; i++)
            {
                if (activeEffects[i].ActiveSpellId == activeSpellId)
                    return;
            }

            for (int i = activeSpells.Length - 1; i >= 0; i--)
            {
                if (activeSpells[i].ActiveSpellId == activeSpellId)
                    activeSpells.RemoveAt(i);
            }
        }
    }
}
