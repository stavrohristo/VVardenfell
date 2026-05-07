using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Magic;
using VVardenfell.Runtime.Movement;

namespace VVardenfell.Runtime.Player
{
    public static class CharacterGenerationUtility
    {
        const int RacePlayableFlag = 0x1;
        const int SpellFlagPcStart = 0x2;
        const int MagicEffectFlagTargetSkill = 0x1;
        const int MagicEffectFlagTargetAttribute = 0x2;

        static readonly ulong fAutoPCSpellChanceHash = RuntimeContentStableHash.HashId("fAutoPCSpellChance");
        static readonly ulong iAutoPCSpellMaxHash = RuntimeContentStableHash.HashId("iAutoPCSpellMax");
        static readonly ulong iAutoSpellAttSkillMinHash = RuntimeContentStableHash.HashId("iAutoSpellAttSkillMin");

        public static CharacterGenerationState CreateInitialState(ref RuntimeContentBlob content, in ActorIdentitySet identity)
        {
            FixedString64Bytes raceId = identity.RaceName;
            if (raceId.IsEmpty)
                raceId = RequireFirstPlayableRaceId(ref content);

            FixedString64Bytes classId = identity.ClassName;
            if (classId.IsEmpty)
                classId = RequireFirstPlayableClassId(ref content);

            var state = new CharacterGenerationState
            {
                Initialized = 1,
                Finalized = 0,
                CurrentMenu = (byte)CharacterGenerationMenu.None,
                Stage = (byte)CharacterGenerationStage.NotStarted,
                Male = 1,
                CharacterName = identity.CharacterName.IsEmpty ? new FixedString64Bytes("Player") : identity.CharacterName,
                RaceId = raceId,
                ClassId = classId,
                BirthsignId = identity.BirthSignName,
                GenerateRandomState = 0x6D2B79F5u,
            };
            state.HeadId = RequireFirstPlayableBodyPartId(ref content, state.RaceId, male: true, ActorBodyPartMeshPart.Head);
            state.HairId = RequireFirstPlayableBodyPartId(ref content, state.RaceId, male: true, ActorBodyPartMeshPart.Hair);
            return state;
        }

        public static void OpenMenu(ref CharacterGenerationState state, CharacterGenerationMenu menu)
        {
            state.Initialized = 1;
            state.CurrentMenu = (byte)menu;
        }

        public static void Close(ref CharacterGenerationState state)
        {
            state.CurrentMenu = (byte)CharacterGenerationMenu.None;
        }

        public static void ApplyToPlayer(
            EntityManager entityManager,
            Entity player,
            ref RuntimeContentBlob content,
            ref CharacterGenerationState state)
        {
            if (player == Entity.Null || !entityManager.Exists(player))
                throw new InvalidOperationException("[VVardenfell][CharGen] Cannot apply character generation without a live player.");

            var identity = entityManager.GetComponentData<ActorIdentitySet>(player);
            identity.CharacterName = state.CharacterName.IsEmpty ? identity.CharacterName : state.CharacterName;
            identity.RaceName = state.RaceId;
            identity.ClassName = state.CustomClassActive != 0
                ? ResolveCustomClassId(entityManager, player)
                : state.ClassId;
            identity.BirthSignName = state.BirthsignId;
            identity.Level = math.max(1, identity.Level);
            entityManager.SetComponentData(player, identity);

            var customClass = entityManager.HasComponent<PlayerCustomClass>(player)
                ? entityManager.GetComponentData<PlayerCustomClass>(player)
                : default;
            BuildStats(ref content, state, customClass, out var attributes, out var skills);
            var vitals = entityManager.GetComponentData<ActorVitalSet>(player);
            MorrowindActorMovementStats.ApplyVitalBases(ref content, attributes, ref vitals, initializeMissingCurrents: true);
            var effectModifiers = entityManager.GetComponentData<ActorEffectStatModifiers>(player);
            var derived = MorrowindActorMovementStats.BuildDerived(ref content, attributes, skills, vitals, effectModifiers, 0f);
            entityManager.SetComponentData(player, attributes);
            entityManager.SetComponentData(player, skills);
            entityManager.SetComponentData(player, vitals);
            entityManager.SetComponentData(player, derived);
            entityManager.SetComponentData(player, MorrowindActorMovementStats.BuildPlayerMovementSpeed(ref content, attributes, skills, vitals, effectModifiers, derived));

            if (!entityManager.HasBuffer<ActorKnownSpell>(player))
                entityManager.AddBuffer<ActorKnownSpell>(player);
            var knownSpells = entityManager.GetBuffer<ActorKnownSpell>(player);
            knownSpells.Clear();
            AddRacePowers(ref content, state.RaceId, knownSpells);
            AddBirthsignPowers(ref content, state.BirthsignId, knownSpells);
            AddAutocalculatedPlayerSpells(ref content, state.RaceId, attributes, skills, knownSpells);

            if (entityManager.HasComponent<PlayerRaceAppearance>(player))
            {
                var appearance = entityManager.GetComponentData<PlayerRaceAppearance>(player);
                appearance.RaceId = state.RaceId;
                appearance.Male = state.Male;
                appearance.HeadId = state.HeadId;
                appearance.HairId = state.HairId;
                appearance.Dirty = 1;
                entityManager.SetComponentData(player, appearance);
            }

            RebuildPlayerEquipmentForRace(entityManager, player, ref content, state.RaceId);
        }

        public static void BuildStats(
            ref RuntimeContentBlob content,
            in CharacterGenerationState state,
            in PlayerCustomClass customClass,
            out ActorAttributeSet attributes,
            out ActorSkillSet skills)
        {
            if (state.RaceId.IsEmpty)
                throw new InvalidOperationException("[VVardenfell][CharGen] Race selection is required before stat rebuild.");

            ref RuntimeRaceDefBlob race = ref RequireRace(ref content, state.RaceId);
            attributes = BuildRaceAttributes(ref content, ref race, state.Male != 0);
            skills = BuildRaceSkills(ref content, ref race);

            if (state.CustomClassActive != 0)
                ApplyCustomClass(ref content, ref attributes, ref skills, customClass);
            else if (!state.ClassId.IsEmpty)
                ApplyContentClass(ref content, ref attributes, ref skills, state.ClassId);
        }

        public static ref RuntimeRaceDefBlob RequireRace(ref RuntimeContentBlob content, FixedString64Bytes raceId)
        {
            if (!RuntimeContentBlobUtility.TryGetRaceHandleByIdHash(ref content, RuntimeContentStableHash.HashId(raceId.ToString()), out var handle) || !handle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][CharGen] Missing race '{raceId}'.");
            return ref RuntimeContentBlobUtility.GetRace(ref content, handle);
        }

        public static ref RuntimeClassDefBlob RequireClass(ref RuntimeContentBlob content, FixedString64Bytes classId)
        {
            if (!RuntimeContentBlobUtility.TryGetClassHandleByIdHash(ref content, RuntimeContentStableHash.HashId(classId.ToString()), out var handle) || !handle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][CharGen] Missing class '{classId}'.");
            return ref RuntimeContentBlobUtility.GetClass(ref content, handle);
        }

        public static ref RuntimeGenericRecordDefBlob RequireBirthsign(ref RuntimeContentBlob content, FixedString64Bytes birthsignId)
        {
            if (!RuntimeContentBlobUtility.TryGetBirthsignHandleByIdHash(ref content, RuntimeContentStableHash.HashId(birthsignId.ToString()), out var handle) || !handle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][CharGen] Missing birthsign '{birthsignId}'.");
            return ref RuntimeContentBlobUtility.GetBirthsign(ref content, handle);
        }

        public static FixedString64Bytes RequireFirstPlayableRaceId(ref RuntimeContentBlob content)
        {
            for (int i = 0; i < content.Races.Length; i++)
            {
                ref RuntimeRaceDefBlob race = ref content.Races[i];
                if ((race.Flags & RacePlayableFlag) != 0)
                    return RuntimeFixedStringUtility.ToFixed64OrDefault(ref race.Id);
            }

            throw new InvalidOperationException("[VVardenfell][CharGen] Content has no playable races.");
        }

        public static FixedString64Bytes RequireFirstPlayableClassId(ref RuntimeContentBlob content)
        {
            for (int i = 0; i < content.Classes.Length; i++)
            {
                ref RuntimeClassDefBlob classDef = ref content.Classes[i];
                if (classDef.Playable != 0)
                    return RuntimeFixedStringUtility.ToFixed64OrDefault(ref classDef.Id);
            }

            throw new InvalidOperationException("[VVardenfell][CharGen] Content has no playable classes.");
        }

        public static FixedString64Bytes ResolveGeneratedClassId(byte combat, byte magic, byte stealth)
        {
            string className;
            if (combat > 7) className = "Warrior";
            else if (magic > 7) className = "Mage";
            else if (stealth > 7) className = "Thief";
            else if (combat == 4) className = "Rogue";
            else if (combat == 5) className = stealth == 3 ? "Scout" : "Archer";
            else if (combat == 6) className = stealth == 1 ? "Barbarian" : stealth == 3 ? "Crusader" : "Knight";
            else if (combat == 7) className = "Warrior";
            else if (magic == 4) className = "Spellsword";
            else if (magic == 5) className = "Witchhunter";
            else if (magic == 6) className = combat == 2 ? "Sorcerer" : combat == 3 ? "Healer" : "Battlemage";
            else if (magic == 7) className = "Mage";
            else if (stealth == 3) className = "Warrior";
            else if (stealth == 5) className = magic == 3 ? "Monk" : "Pilgrim";
            else if (stealth == 6) className = magic == 1 ? "Agent" : magic == 3 ? "Assassin" : "Acrobat";
            else if (stealth == 7) className = "Thief";
            else className = "Warrior";

            return new FixedString64Bytes(className);
        }

        public static FixedString64Bytes RequireFirstPlayableBodyPartId(
            ref RuntimeContentBlob content,
            FixedString64Bytes raceId,
            bool male,
            ActorBodyPartMeshPart part)
        {
            for (int i = 0; i < content.ActorBodyParts.Length; i++)
            {
                ref RuntimeActorBodyPartDefBlob bodyPart = ref content.ActorBodyParts[i];
                if (IsPlayableBodyPart(ref bodyPart, raceId, male, part))
                    return RuntimeFixedStringUtility.ToFixed64OrDefault(ref bodyPart.Id);
            }

            throw new InvalidOperationException($"[VVardenfell][CharGen] Race '{raceId}' has no playable {part} body part for {(male ? "male" : "female")}.");
        }

        public static bool IsPlayableBodyPart(
            ref RuntimeActorBodyPartDefBlob bodyPart,
            FixedString64Bytes raceId,
            bool male,
            ActorBodyPartMeshPart part)
        {
            return bodyPart.Type == ActorBodyPartMeshType.Skin
                   && bodyPart.NotPlayable == 0
                   && bodyPart.Vampire == 0
                   && bodyPart.FirstPerson == 0
                   && bodyPart.Part == part
                   && bodyPart.Female == (male ? (byte)0 : (byte)1)
                   && string.Equals(bodyPart.RaceId.ToString(), raceId.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        static FixedString64Bytes ResolveCustomClassId(EntityManager entityManager, Entity player)
        {
            if (!entityManager.HasComponent<PlayerCustomClass>(player))
                throw new InvalidOperationException("[VVardenfell][CharGen] Custom class selected but player has no custom class component.");

            var customClass = entityManager.GetComponentData<PlayerCustomClass>(player);
            if (customClass.Name.IsEmpty)
                throw new InvalidOperationException("[VVardenfell][CharGen] Custom class requires a non-empty name.");
            return customClass.Name;
        }

        static ActorAttributeSet BuildRaceAttributes(ref RuntimeContentBlob content, ref RuntimeRaceDefBlob race, bool male)
        {
            int first = male ? race.FirstMaleAttributeIndex : race.FirstFemaleAttributeIndex;
            int count = male ? race.MaleAttributeCount : race.FemaleAttributeCount;
            RuntimeContentBlobUtility.RequireRange(first, count, male ? content.RaceMaleAttributes.Length : content.RaceFemaleAttributes.Length, "race attributes");
            if (count < 8)
                throw new InvalidOperationException($"[VVardenfell][CharGen] Race '{race.Id}' has {count} attributes.");

            var result = new ActorAttributeSet();
            if (male)
            {
                for (int i = 0; i < 8; i++)
                    SetAttribute(ref result, i, content.RaceMaleAttributes[first + i]);
            }
            else
            {
                for (int i = 0; i < 8; i++)
                    SetAttribute(ref result, i, content.RaceFemaleAttributes[first + i]);
            }

            return result;
        }

        static ActorSkillSet BuildRaceSkills(ref RuntimeContentBlob content, ref RuntimeRaceDefBlob race)
        {
            var result = CreateBaseSkillSet(5f);
            RuntimeContentBlobUtility.RequireRange(race.FirstSkillBonusIndex, race.SkillBonusCount, content.RaceSkillBonuses.Length, "race skill bonus");
            for (int i = 0; i < race.SkillBonusCount; i++)
            {
                RaceSkillBonusDef bonus = content.RaceSkillBonuses[race.FirstSkillBonusIndex + i];
                if ((uint)bonus.Skill >= 27u)
                    continue;
                AddSkill(ref result, bonus.Skill, bonus.Bonus);
            }

            return result;
        }

        static void ApplyContentClass(ref RuntimeContentBlob content, ref ActorAttributeSet attributes, ref ActorSkillSet skills, FixedString64Bytes classId)
        {
            ref RuntimeClassDefBlob classDef = ref RequireClass(ref content, classId);
            AddAttribute(ref attributes, classDef.FavoredAttribute0, 10f);
            AddAttribute(ref attributes, classDef.FavoredAttribute1, 10f);
            RuntimeContentBlobUtility.RequireRange(classDef.FirstMajorSkillIndex, classDef.MajorSkillCount, content.ClassMajorSkills.Length, "class major skill");
            RuntimeContentBlobUtility.RequireRange(classDef.FirstMinorSkillIndex, classDef.MinorSkillCount, content.ClassMinorSkills.Length, "class minor skill");
            for (int i = 0; i < classDef.MajorSkillCount; i++)
                AddSkill(ref skills, content.ClassMajorSkills[classDef.FirstMajorSkillIndex + i], 25f);
            for (int i = 0; i < classDef.MinorSkillCount; i++)
                AddSkill(ref skills, content.ClassMinorSkills[classDef.FirstMinorSkillIndex + i], 10f);
            ApplySpecialization(ref content, ref skills, classDef.Specialization);
        }

        static void ApplyCustomClass(ref RuntimeContentBlob content, ref ActorAttributeSet attributes, ref ActorSkillSet skills, in PlayerCustomClass customClass)
        {
            if (customClass.Active == 0 || customClass.Name.IsEmpty)
                throw new InvalidOperationException("[VVardenfell][CharGen] Custom class is incomplete.");

            AddAttribute(ref attributes, customClass.FavoredAttribute0, 10f);
            AddAttribute(ref attributes, customClass.FavoredAttribute1, 10f);
            AddSkill(ref skills, customClass.MajorSkill0, 25f);
            AddSkill(ref skills, customClass.MajorSkill1, 25f);
            AddSkill(ref skills, customClass.MajorSkill2, 25f);
            AddSkill(ref skills, customClass.MajorSkill3, 25f);
            AddSkill(ref skills, customClass.MajorSkill4, 25f);
            AddSkill(ref skills, customClass.MinorSkill0, 10f);
            AddSkill(ref skills, customClass.MinorSkill1, 10f);
            AddSkill(ref skills, customClass.MinorSkill2, 10f);
            AddSkill(ref skills, customClass.MinorSkill3, 10f);
            AddSkill(ref skills, customClass.MinorSkill4, 10f);
            ApplySpecialization(ref content, ref skills, customClass.Specialization);
        }

        static void ApplySpecialization(ref RuntimeContentBlob content, ref ActorSkillSet skills, int specialization)
        {
            for (int i = 0; i < content.Skills.Length; i++)
            {
                ref RuntimeGenericRecordDefBlob skill = ref content.Skills[i];
                if (skill.Int1 == specialization)
                    AddSkill(ref skills, skill.Int0, 5f);
            }
        }

        static void AddRacePowers(ref RuntimeContentBlob content, FixedString64Bytes raceId, DynamicBuffer<ActorKnownSpell> knownSpells)
        {
            ref RuntimeRaceDefBlob race = ref RequireRace(ref content, raceId);
            RuntimeContentBlobUtility.RequireRange(race.FirstPowerSpellIdIndex, race.PowerSpellIdCount, content.RacePowerSpellIds.Length, "race power spell");
            for (int i = 0; i < race.PowerSpellIdCount; i++)
                AddKnownSpellById(ref content, content.RacePowerSpellIds[race.FirstPowerSpellIdIndex + i].Value.ToString(), knownSpells);
        }

        static void AddBirthsignPowers(ref RuntimeContentBlob content, FixedString64Bytes birthsignId, DynamicBuffer<ActorKnownSpell> knownSpells)
        {
            if (birthsignId.IsEmpty)
                return;

            ref RuntimeGenericRecordDefBlob birthsign = ref RequireBirthsign(ref content, birthsignId);
            RuntimeContentBlobUtility.RequireRange(birthsign.FirstPowerSpellIdIndex, birthsign.PowerSpellIdCount, content.GenericRecordPowerSpellIds.Length, "birthsign power spell");
            for (int i = 0; i < birthsign.PowerSpellIdCount; i++)
                AddKnownSpellById(ref content, content.GenericRecordPowerSpellIds[birthsign.FirstPowerSpellIdIndex + i].Value.ToString(), knownSpells);
        }

        static void AddAutocalculatedPlayerSpells(
            ref RuntimeContentBlob content,
            FixedString64Bytes raceId,
            in ActorAttributeSet attributes,
            in ActorSkillSet skills,
            DynamicBuffer<ActorKnownSpell> knownSpells)
        {
            float baseMagicka = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fPCbaseMagickaMult) * attributes.Intelligence;
            float chanceThreshold = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, fAutoPCSpellChanceHash);
            int maxSpells = RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, iAutoPCSpellMaxHash);
            int attributeSkillMinimum = RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, iAutoSpellAttSkillMinHash);
            if (maxSpells < 0)
                throw new InvalidOperationException($"[VVardenfell][CharGen] Invalid iAutoPCSpellMax {maxSpells}.");

            bool reachedLimit = false;
            SpellDefHandle weakestSpell = default;
            int minCost = int.MaxValue;
            var selected = new NativeArray<SpellDefHandle>(content.Spells.Length, Allocator.Temp);
            int selectedCount = 0;
            try
            {
                for (int i = 0; i < content.Spells.Length; i++)
                {
                    ref RuntimeSpellDefBlob spell = ref content.Spells[i];
                    if (spell.SpellType != MorrowindSpellCostUtility.SpellTypeSpell || (spell.Flags & SpellFlagPcStart) == 0)
                        continue;

                    int spellCost = MorrowindSpellCostUtility.CalculateSpellCost(ref content, ref spell);
                    if (reachedLimit && spellCost <= minCost)
                        continue;
                    if (IsRacePower(ref content, raceId, spell.IdHash))
                        continue;
                    if (baseMagicka < spellCost)
                        continue;
                    if (CalculateAutoCastChance(ref content, ref spell, attributes, skills) < chanceThreshold)
                        continue;
                    if (!PassesAttributeSkillCheck(ref content, ref spell, attributes, skills, attributeSkillMinimum))
                        continue;

                    var handle = SpellDefHandle.FromIndex(i);
                    if (reachedLimit)
                    {
                        RemoveSelected(selected, ref selectedCount, weakestSpell);
                        selected[selectedCount++] = handle;
                        RecomputeWeakest(ref content, selected, selectedCount, out weakestSpell, out minCost);
                        continue;
                    }

                    selected[selectedCount++] = handle;
                    if (spellCost < minCost)
                    {
                        weakestSpell = handle;
                        minCost = spellCost;
                    }

                    if (selectedCount == maxSpells)
                        reachedLimit = true;
                }

                for (int i = 0; i < selectedCount; i++)
                    MorrowindActorMagicUtility.AddKnownSpell(knownSpells, selected[i]);
            }
            finally
            {
                selected.Dispose();
            }
        }

        static float CalculateAutoCastChance(ref RuntimeContentBlob content, ref RuntimeSpellDefBlob spell, in ActorAttributeSet attributes, in ActorSkillSet skills)
        {
            if ((spell.Flags & MorrowindSpellCostUtility.SpellFlagAlways) != 0)
                return 100f;

            ActorSkillKind school = ResolveWeakestSchool(ref content, ref spell, skills);
            float skillTerm = 2f * PlayerSkillMutationApplySystem.GetSkill(skills, school);
            return skillTerm
                   - MorrowindSpellCostUtility.CalculateSpellCost(ref content, ref spell)
                   + 0.2f * attributes.Willpower
                   + 0.1f * attributes.Luck;
        }

        static ActorSkillKind ResolveWeakestSchool(ref RuntimeContentBlob content, ref RuntimeSpellDefBlob spell, in ActorSkillSet skills)
        {
            MorrowindActorMagicUtility.RequireSpellEffectRange(ref content, ref spell);
            float lowest = float.PositiveInfinity;
            ActorSkillKind result = ActorSkillKind.None;
            for (int i = 0; i < spell.EffectCount; i++)
            {
                ref MagicEffectInstanceDef instance = ref content.MagicEffectInstances[spell.EffectStartIndex + i];
                ref RuntimeMagicEffectDefBlob effect = ref MorrowindSpellCostUtility.RequireMagicEffect(ref content, instance.EffectId, spell.ContentId.Value);
                float minMagnitude = 1f;
                float maxMagnitude = 1f;
                if ((effect.Flags & MorrowindSpellCostUtility.MagicEffectFlagNoMagnitude) == 0)
                {
                    minMagnitude = instance.MagnitudeMin;
                    maxMagnitude = instance.MagnitudeMax;
                }

                float duration = 0f;
                if ((effect.Flags & MorrowindSpellCostUtility.MagicEffectFlagNoDuration) == 0)
                    duration = instance.Duration;
                if ((effect.Flags & MorrowindSpellCostUtility.MagicEffectFlagAppliedOnce) == 0)
                    duration = math.max(1f, duration);

                float x = 0.5f * (math.max(1f, minMagnitude) + math.max(1f, maxMagnitude));
                x *= 0.1f * effect.BaseCost;
                x *= 1f + duration;
                x += 0.05f * math.max(1f, instance.Area) * effect.BaseCost;
                x *= RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentKnownHashes.fEffectCostMult);
                if (instance.Range == MorrowindMagicRange.Target)
                    x *= 1.5f;

                ActorSkillKind school = MorrowindSpellCostUtility.ResolveSchool(effect.School);
                float chance = 2f * PlayerSkillMutationApplySystem.GetSkill(skills, school) - x;
                if (chance < lowest)
                {
                    lowest = chance;
                    result = school;
                }
            }

            if (result == ActorSkillKind.None)
                throw new InvalidOperationException($"[VVardenfell][CharGen] Starter spell '{spell.Id}' has no effective school.");
            return result;
        }

        static bool PassesAttributeSkillCheck(ref RuntimeContentBlob content, ref RuntimeSpellDefBlob spell, in ActorAttributeSet attributes, in ActorSkillSet skills, int minimum)
        {
            MorrowindActorMagicUtility.RequireSpellEffectRange(ref content, ref spell);
            for (int i = 0; i < spell.EffectCount; i++)
            {
                ref MagicEffectInstanceDef instance = ref content.MagicEffectInstances[spell.EffectStartIndex + i];
                ref RuntimeMagicEffectDefBlob effect = ref MorrowindSpellCostUtility.RequireMagicEffect(ref content, instance.EffectId, spell.ContentId.Value);
                if ((effect.Flags & MagicEffectFlagTargetSkill) != 0 && PlayerSkillMutationApplySystem.GetSkill(skills, ToSkillKind(instance.Skill)) < minimum)
                    return false;
                if ((effect.Flags & MagicEffectFlagTargetAttribute) != 0 && GetAttribute(attributes, instance.Attribute) < minimum)
                    return false;
            }

            return true;
        }

        static bool IsRacePower(ref RuntimeContentBlob content, FixedString64Bytes raceId, ulong spellIdHash)
        {
            ref RuntimeRaceDefBlob race = ref RequireRace(ref content, raceId);
            RuntimeContentBlobUtility.RequireRange(race.FirstPowerSpellIdIndex, race.PowerSpellIdCount, content.RacePowerSpellIds.Length, "race power spell");
            for (int i = 0; i < race.PowerSpellIdCount; i++)
            {
                if (RuntimeContentStableHash.HashId(content.RacePowerSpellIds[race.FirstPowerSpellIdIndex + i].Value.ToString()) == spellIdHash)
                    return true;
            }

            return false;
        }

        static void AddKnownSpellById(ref RuntimeContentBlob content, string spellId, DynamicBuffer<ActorKnownSpell> knownSpells)
        {
            if (string.IsNullOrWhiteSpace(spellId))
                throw new InvalidOperationException("[VVardenfell][CharGen] Empty spell id in charGen power list.");
            if (!RuntimeContentBlobUtility.TryGetSpellHandleByIdHash(ref content, RuntimeContentStableHash.HashId(spellId), out var handle) || !handle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][CharGen] Missing charGen spell '{spellId}'.");
            MorrowindActorMagicUtility.AddKnownSpell(knownSpells, handle);
        }

        static void RemoveSelected(NativeArray<SpellDefHandle> selected, ref int count, SpellDefHandle handle)
        {
            for (int i = 0; i < count; i++)
            {
                if (selected[i].Value != handle.Value)
                    continue;
                for (int j = i + 1; j < count; j++)
                    selected[j - 1] = selected[j];
                count--;
                return;
            }
        }

        static void RecomputeWeakest(ref RuntimeContentBlob content, NativeArray<SpellDefHandle> selected, int count, out SpellDefHandle weakest, out int minCost)
        {
            weakest = default;
            minCost = int.MaxValue;
            for (int i = 0; i < count; i++)
            {
                ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref content, selected[i]);
                int cost = MorrowindSpellCostUtility.CalculateSpellCost(ref content, ref spell);
                if (cost < minCost)
                {
                    minCost = cost;
                    weakest = selected[i];
                }
            }
        }

        static void RebuildPlayerEquipmentForRace(EntityManager entityManager, Entity player, ref RuntimeContentBlob content, FixedString64Bytes raceId)
        {
            if (!entityManager.HasBuffer<PlayerInventoryItem>(player) || !entityManager.HasBuffer<ActorEquipmentSlot>(player))
                return;

            var inventory = entityManager.GetBuffer<PlayerInventoryItem>(player);
            var equipment = entityManager.GetBuffer<ActorEquipmentSlot>(player);
            equipment.Clear();

            bool isBeast = ActorEquipmentRuntimeUtility.IsBeastRace(ref content, RuntimeContentStableHash.HashId(raceId.ToString()));
            for (int i = 0; i < inventory.Length; i++)
            {
                var item = inventory[i];
                if (item.Count <= 0 || item.Content.Kind != ContentReferenceKind.Item)
                    continue;

                var handle = new ItemDefHandle { Value = item.Content.HandleValue };
                if (!RuntimeContentBlobUtility.TryGetItemEquipment(ref content, handle, out var itemEquipment))
                    continue;
                if (isBeast && HasBeastForbiddenPart(ref content, itemEquipment))
                    continue;
                if (itemEquipment.Slot == ItemEquipmentSlot.None)
                    continue;

                equipment.Add(new ActorEquipmentSlot
                {
                    Slot = itemEquipment.Slot,
                    Content = item.Content,
                    InventoryIndex = i,
                    Condition = ActorEquipmentConditionUtility.ResolveInitialCondition(itemEquipment, item.Count, item.Condition, item.Content),
                    VisualMode = ActorEquipmentRuntimeUtility.ResolveEquipmentVisualMode(itemEquipment),
                });
            }
        }

        static bool HasBeastForbiddenPart(ref RuntimeContentBlob content, in ItemEquipmentDef equipment)
        {
            RuntimeContentBlobUtility.RequireRange(equipment.FirstBodyPartIndex, equipment.BodyPartCount, content.ItemEquipmentBodyParts.Length, "item equipment body part");
            for (int i = 0; i < equipment.BodyPartCount; i++)
            {
                ItemEquipmentPartReference part = content.ItemEquipmentBodyParts[equipment.FirstBodyPartIndex + i].Part;
                if (part == ItemEquipmentPartReference.Head || part == ItemEquipmentPartReference.LeftFoot || part == ItemEquipmentPartReference.RightFoot)
                    return true;
            }

            return false;
        }

        static ActorSkillSet CreateBaseSkillSet(float value)
            => new ActorSkillSet
            {
                Block = value,
                Armorer = value,
                MediumArmor = value,
                HeavyArmor = value,
                BluntWeapon = value,
                LongBlade = value,
                Axe = value,
                Spear = value,
                Athletics = value,
                Enchant = value,
                Destruction = value,
                Alteration = value,
                Illusion = value,
                Conjuration = value,
                Mysticism = value,
                Restoration = value,
                Alchemy = value,
                Unarmored = value,
                Security = value,
                Sneak = value,
                Acrobatics = value,
                LightArmor = value,
                ShortBlade = value,
                Marksman = value,
                Mercantile = value,
                Speechcraft = value,
                HandToHand = value,
            };

        static void AddAttribute(ref ActorAttributeSet attributes, int index, float value)
            => SetAttribute(ref attributes, index, GetAttribute(attributes, index) + value);

        static float GetAttribute(in ActorAttributeSet attributes, int index)
            => index switch
            {
                0 => attributes.Strength,
                1 => attributes.Intelligence,
                2 => attributes.Willpower,
                3 => attributes.Agility,
                4 => attributes.Speed,
                5 => attributes.Endurance,
                6 => attributes.Personality,
                7 => attributes.Luck,
                _ => throw new InvalidOperationException($"[VVardenfell][CharGen] Unknown attribute index {index}."),
            };

        static void SetAttribute(ref ActorAttributeSet attributes, int index, float value)
        {
            switch (index)
            {
                case 0: attributes.Strength = value; break;
                case 1: attributes.Intelligence = value; break;
                case 2: attributes.Willpower = value; break;
                case 3: attributes.Agility = value; break;
                case 4: attributes.Speed = value; break;
                case 5: attributes.Endurance = value; break;
                case 6: attributes.Personality = value; break;
                case 7: attributes.Luck = value; break;
                default: throw new InvalidOperationException($"[VVardenfell][CharGen] Unknown attribute index {index}.");
            }
        }

        static void AddSkill(ref ActorSkillSet skills, int index, float value)
            => SetSkill(ref skills, index, GetSkill(skills, index) + value);

        static float GetSkill(in ActorSkillSet skills, int index)
            => PlayerSkillMutationApplySystem.GetSkill(skills, ToSkillKind(index));

        static void SetSkill(ref ActorSkillSet skills, int index, float value)
        {
            switch (ToSkillKind(index))
            {
                case ActorSkillKind.Block: skills.Block = value; break;
                case ActorSkillKind.Armorer: skills.Armorer = value; break;
                case ActorSkillKind.MediumArmor: skills.MediumArmor = value; break;
                case ActorSkillKind.HeavyArmor: skills.HeavyArmor = value; break;
                case ActorSkillKind.BluntWeapon: skills.BluntWeapon = value; break;
                case ActorSkillKind.LongBlade: skills.LongBlade = value; break;
                case ActorSkillKind.Axe: skills.Axe = value; break;
                case ActorSkillKind.Spear: skills.Spear = value; break;
                case ActorSkillKind.Athletics: skills.Athletics = value; break;
                case ActorSkillKind.Enchant: skills.Enchant = value; break;
                case ActorSkillKind.Destruction: skills.Destruction = value; break;
                case ActorSkillKind.Alteration: skills.Alteration = value; break;
                case ActorSkillKind.Illusion: skills.Illusion = value; break;
                case ActorSkillKind.Conjuration: skills.Conjuration = value; break;
                case ActorSkillKind.Mysticism: skills.Mysticism = value; break;
                case ActorSkillKind.Restoration: skills.Restoration = value; break;
                case ActorSkillKind.Alchemy: skills.Alchemy = value; break;
                case ActorSkillKind.Unarmored: skills.Unarmored = value; break;
                case ActorSkillKind.Security: skills.Security = value; break;
                case ActorSkillKind.Sneak: skills.Sneak = value; break;
                case ActorSkillKind.Acrobatics: skills.Acrobatics = value; break;
                case ActorSkillKind.LightArmor: skills.LightArmor = value; break;
                case ActorSkillKind.ShortBlade: skills.ShortBlade = value; break;
                case ActorSkillKind.Marksman: skills.Marksman = value; break;
                case ActorSkillKind.Mercantile: skills.Mercantile = value; break;
                case ActorSkillKind.Speechcraft: skills.Speechcraft = value; break;
                case ActorSkillKind.HandToHand: skills.HandToHand = value; break;
                default: throw new InvalidOperationException($"[VVardenfell][CharGen] Unknown skill index {index}.");
            }
        }

        static ActorSkillKind ToSkillKind(int index)
        {
            if ((uint)index >= 27u)
                throw new InvalidOperationException($"[VVardenfell][CharGen] Unknown skill index {index}.");
            return (ActorSkillKind)(index + 1);
        }
    }
}
