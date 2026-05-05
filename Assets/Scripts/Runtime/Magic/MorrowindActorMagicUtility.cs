using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Magic
{
    static class MorrowindActorMagicUtility
    {
        public const short CureCommonDiseaseEffectId = 69;
        public const short CureBlightDiseaseEffectId = 70;

        const int SpellTypeAbility = 1;
        public const int SpellTypeBlight = 2;
        public const int SpellTypeDisease = 3;
        public const int SpellTypeCurse = 4;

        public static bool AddKnownSpell(DynamicBuffer<ActorKnownSpell> knownSpells, SpellDefHandle spellHandle)
        {
            if (!spellHandle.IsValid)
                return false;

            if (FindKnownSpellIndex(knownSpells, spellHandle) >= 0)
                return true;

            knownSpells.Add(new ActorKnownSpell { Spell = spellHandle });
            return true;
        }

        public static bool RemoveKnownSpell(
            ref RuntimeContentBlob content,
            DynamicBuffer<ActorKnownSpell> knownSpells,
            DynamicBuffer<ActorActiveMagicEffect> activeEffects,
            SpellDefHandle spellHandle)
        {
            if (!spellHandle.IsValid)
                return false;

            int index = FindKnownSpellIndex(knownSpells, spellHandle);
            if (index >= 0)
                knownSpells.RemoveAt(index);

            ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref content, spellHandle);
            if (IsPassiveSpellType(spell.SpellType))
                RemoveActiveEffectsBySource(activeEffects, spell.IdHash);

            return true;
        }

        public static bool CanRepresentScriptedCast(ref RuntimeContentBlob content, SpellDefHandle spellHandle)
        {
            if (!spellHandle.IsValid)
                return false;

            ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref content, spellHandle);
            RequireSpellEffectRange(ref content, ref spell);
            for (int i = 0; i < spell.EffectCount; i++)
            {
                MagicEffectInstanceDef effect = content.MagicEffectInstances[spell.EffectStartIndex + i];
                if (!CanRepresentScriptedEffect(effect.EffectId))
                    return false;
            }

            return true;
        }

        public static bool ApplyScriptedCast(
            ref RuntimeContentBlob content,
            DynamicBuffer<ActorActiveMagicEffect> targetEffects,
            SpellDefHandle spellHandle)
        {
            if (!spellHandle.IsValid)
                return false;

            ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref content, spellHandle);
            RequireSpellEffectRange(ref content, ref spell);
            if (!CanRepresentScriptedCast(ref content, spellHandle))
                return false;

            for (int i = 0; i < spell.EffectCount; i++)
            {
                MagicEffectInstanceDef effect = content.MagicEffectInstances[spell.EffectStartIndex + i];
                switch (effect.EffectId)
                {
                    case CureCommonDiseaseEffectId:
                        RemoveEffectsBySpellType(ref content, targetEffects, SpellTypeDisease);
                        break;
                    case CureBlightDiseaseEffectId:
                        RemoveEffectsBySpellType(ref content, targetEffects, SpellTypeBlight);
                        break;
                    default:
                        AppendRepresentedEffect(targetEffects, ref spell, effect);
                        break;
                }
            }

            return true;
        }

        public static bool IsPassiveSpellType(int spellType)
            => spellType is SpellTypeAbility or SpellTypeBlight or SpellTypeDisease or SpellTypeCurse;

        public static bool HasKnownSpell(DynamicBuffer<ActorKnownSpell> knownSpells, SpellDefHandle spellHandle)
            => FindKnownSpellIndex(knownSpells, spellHandle) >= 0;

        public static void RequireSpellEffectRange(ref RuntimeContentBlob content, ref RuntimeSpellDefBlob spell)
            => RuntimeContentBlobUtility.RequireRange(spell.EffectStartIndex, spell.EffectCount, content.MagicEffectInstances.Length, $"spell contentId=0x{spell.ContentId.Value:X16} effects");

        static bool CanRepresentScriptedEffect(short effectId)
            => effectId is CureCommonDiseaseEffectId or CureBlightDiseaseEffectId;

        static int FindKnownSpellIndex(DynamicBuffer<ActorKnownSpell> knownSpells, SpellDefHandle spellHandle)
        {
            for (int i = 0; i < knownSpells.Length; i++)
            {
                if (knownSpells[i].Spell.Value == spellHandle.Value)
                    return i;
            }

            return -1;
        }

        static void AppendRepresentedEffect(
            DynamicBuffer<ActorActiveMagicEffect> targetEffects,
            ref RuntimeSpellDefBlob spell,
            in MagicEffectInstanceDef effect)
        {
            float duration = effect.Duration <= 0 ? -1f : effect.Duration;
            FixedString64Bytes sourceName = RuntimeFixedStringUtility.ToFixed64OrDefault(ref spell.Name);
            FixedString64Bytes sourceId = RuntimeFixedStringUtility.ToFixed64OrDefault(ref spell.Id);
            if (sourceName.IsEmpty)
                sourceName = sourceId;
            targetEffects.Add(new ActorActiveMagicEffect
            {
                EffectId = effect.EffectId,
                Skill = effect.Skill,
                Attribute = effect.Attribute,
                Magnitude = math.max(effect.MagnitudeMin, effect.MagnitudeMax),
                DurationSeconds = duration,
                TimeLeftSeconds = duration,
                Applied = 1,
                SourceKind = duration < 0f ? ActorActiveMagicEffectSourceKind.PassiveSpell : ActorActiveMagicEffectSourceKind.TimedSpell,
                SourceName = sourceName,
                SourceId = sourceId,
                SourceIdHash = spell.IdHash,
            });
        }

        static void RemoveActiveEffectsBySource(DynamicBuffer<ActorActiveMagicEffect> activeEffects, ulong sourceIdHash)
        {
            if (sourceIdHash == 0UL)
                return;

            for (int i = activeEffects.Length - 1; i >= 0; i--)
            {
                if (activeEffects[i].SourceIdHash == sourceIdHash)
                    activeEffects.RemoveAt(i);
            }
        }

        static void RemoveEffectsBySpellType(
            ref RuntimeContentBlob content,
            DynamicBuffer<ActorActiveMagicEffect> activeEffects,
            int spellType)
        {
            for (int i = activeEffects.Length - 1; i >= 0; i--)
            {
                ulong sourceIdHash = activeEffects[i].SourceIdHash;
                if (sourceIdHash == 0UL
                    || !RuntimeContentBlobUtility.TryGetSpellHandleByIdHash(ref content, sourceIdHash, out SpellDefHandle sourceHandle)
                    || !sourceHandle.IsValid)
                {
                    continue;
                }

                ref RuntimeSpellDefBlob sourceSpell = ref RuntimeContentBlobUtility.Get(ref content, sourceHandle);
                if (sourceSpell.SpellType == spellType)
                    activeEffects.RemoveAt(i);
            }
        }
    }
}
