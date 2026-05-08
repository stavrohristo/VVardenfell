using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.Shell
{
    static class RuntimeShellCharacterGenerationModelBuilder
    {
        const int RacePlayableFlag = 0x1;

        public static CharacterGenerationViewModel Build(
            ref RuntimeContentBlob content,
            in CharacterGenerationState state,
            in PlayerCustomClass customClass,
            DynamicBuffer<ActorKnownSpell> knownSpells,
            StatsWindowViewModel reviewStats)
        {
            string classId = state.CustomClassActive != 0 ? customClass.Name.ToString() : state.ClassId.ToString();
            FixedString64Bytes selectedBirthsignId = ResolveSelectedBirthsignId(ref content, state);
            string birthsignId = selectedBirthsignId.ToString();
            return new CharacterGenerationViewModel
            {
                Menu = (CharacterGenerationMenuView)state.CurrentMenu,
                Name = state.CharacterName.ToString(),
                NameLabel = RequireGmstString(ref content, "sName"),
                NameButtonText = RequireGmstString(ref content, state.Stage >= (byte)CharacterGenerationStage.NameChosen ? "sNext" : "sOK"),
                NameEmptyMessage = RequireGmstString(ref content, "sNotifyMessage37"),
                BackText = RequireGmstString(ref content, "sBack"),
                OkText = RequireGmstString(ref content, "sOK"),
                NextText = RequireGmstString(ref content, "sNext"),
                DoneText = RequireGmstString(ref content, "sDone"),
                RaceId = state.RaceId.ToString(),
                RaceName = BuildRaceName(ref content, state.RaceId),
                RaceTooltipText = BuildRaceTooltip(ref content, state.RaceId),
                RaceAppearanceLabel = RequireGmstString(ref content, "sRaceMenu1"),
                RaceGenderLabel = RequireGmstString(ref content, "sRaceMenu2"),
                RaceFaceLabel = RequireGmstString(ref content, "sRaceMenu3"),
                RaceHairLabel = RequireGmstString(ref content, "sRaceMenu4"),
                RaceListLabel = RequireGmstString(ref content, "sRaceMenu5"),
                RaceSkillBonusLabel = RequireGmstString(ref content, "sBonusSkillTitle"),
                RaceSpecialsLabel = RequireGmstString(ref content, "sRaceMenu7"),
                RaceBackButtonText = RequireGmstString(ref content, "sBack"),
                RaceOkButtonText = RequireGmstString(ref content, state.Stage >= (byte)CharacterGenerationStage.RaceChosen ? "sNext" : "sOK"),
                ClassId = classId,
                ClassName = state.CustomClassActive != 0 ? customClass.Name.ToString() : BuildClassName(ref content, state.ClassId),
                ClassTooltipText = BuildSelectedClassTooltip(ref content, state, customClass),
                BirthsignId = birthsignId,
                BirthsignName = BuildBirthsignName(ref content, selectedBirthsignId),
                BirthsignTooltipText = BuildBirthsignTooltip(ref content, selectedBirthsignId),
                Male = state.Male != 0,
                HeadIndex = BuildBodyPartIndex(ref content, state.RaceId, state.Male != 0, ActorBodyPartMeshPart.Head, state.HeadId),
                HairIndex = BuildBodyPartIndex(ref content, state.RaceId, state.Male != 0, ActorBodyPartMeshPart.Hair, state.HairId),
                RaceSkillBonuses = BuildRaceSkillBonuses(ref content, state.RaceId),
                RacePowers = BuildRacePowers(ref content, state.RaceId),
                GenerateStep = state.GenerateStep,
                GeneratedClassName = BuildClassName(ref content, state.GeneratedClassId),
                ClassChoiceGenerateText = RequireGmstString(ref content, "sClassChoiceMenu1"),
                ClassChoicePickText = RequireGmstString(ref content, "sClassChoiceMenu2"),
                ClassChoiceCreateText = RequireGmstString(ref content, "sClassChoiceMenu3"),
                ClassSpecializationLabel = RequireGmstString(ref content, "sChooseClassMenu1"),
                ClassFavoredAttributesLabel = RequireGmstString(ref content, "sChooseClassMenu2"),
                ClassMajorSkillsLabel = RequireGmstString(ref content, "sChooseClassMenu3"),
                ClassMinorSkillsLabel = RequireGmstString(ref content, "sChooseClassMenu4"),
                SkillClassMajorLabel = RequireGmstString(ref content, "sSkillClassMajor"),
                SkillClassMinorLabel = RequireGmstString(ref content, "sSkillClassMinor"),
                SkillClassMiscLabel = RequireGmstString(ref content, "sSkillClassMisc"),
                ClassOkText = RequireGmstString(ref content, state.Stage >= (byte)CharacterGenerationStage.ClassChosen ? "sNext" : "sOK"),
                ClassDescriptionButtonText = RequireGmstString(ref content, "sCreateClassMenu1"),
                CustomClassNameLabel = RequireGmstString(ref content, "sName"),
                CustomClassDefaultName = RequireGmstString(ref content, "sCustomClassName"),
                GeneratedClassReflectText = RequireGmstString(ref content, "sMessageQuestionAnswer1"),
                GeneratedClassBackText = RequireGmstString(ref content, "sMessageQuestionAnswer3"),
                GeneratedClassOkText = RequireGmstString(ref content, "sMessageQuestionAnswer2"),
                BirthsignAbilitiesLabel = RequireGmstString(ref content, "sBirthsignmenu1"),
                BirthsignPowersLabel = RequireGmstString(ref content, "sPowers"),
                BirthsignSpellsLabel = RequireGmstString(ref content, "sBirthsignmenu2"),
                BirthsignOkText = RequireGmstString(ref content, state.Stage >= (byte)CharacterGenerationStage.BirthSignChosen ? "sNext" : "sOK"),
                ReviewNameLabel = RequireGmstString(ref content, "sName"),
                ReviewRaceLabel = RequireGmstString(ref content, "sRace"),
                ReviewClassLabel = RequireGmstString(ref content, "sClass"),
                ReviewBirthsignLabel = RequireGmstString(ref content, "sBirthSign"),
                ReviewHealthLabel = RequireGmstString(ref content, "sHealth"),
                ReviewMagickaLabel = RequireGmstString(ref content, "sMagic"),
                ReviewFatigueLabel = RequireGmstString(ref content, "sFatigue"),
                ReviewAbilitiesLabel = RequireGmstString(ref content, "sTypeAbility"),
                ReviewPowersLabel = RequireGmstString(ref content, "sTypePower"),
                ReviewSpellsLabel = RequireGmstString(ref content, "sTypeSpell"),
                SelectedClass = BuildSelectedClassDetail(ref content, state, customClass),
                GeneratedClass = BuildClassDetailOrNull(ref content, state.GeneratedClassId),
                SelectedBirthsign = BuildBirthsignDetailOrNull(ref content, selectedBirthsignId),
                Races = BuildRaceChoices(ref content, state.RaceId),
                Classes = BuildClassChoices(ref content, state.ClassId),
                Birthsigns = BuildBirthsignChoices(ref content, selectedBirthsignId),
                Heads = BuildBodyPartChoices(ref content, state.RaceId, state.Male != 0, ActorBodyPartMeshPart.Head, state.HeadId),
                Hairs = BuildBodyPartChoices(ref content, state.RaceId, state.Male != 0, ActorBodyPartMeshPart.Hair, state.HairId),
                GenerateQuestion = BuildGenerateQuestion(ref content, state),
                CustomClass = BuildCustomClass(ref content, customClass),
                ReviewSpells = BuildReviewSpells(ref content, knownSpells),
                ReviewStats = reviewStats,
            };
        }

        static FixedString64Bytes ResolveSelectedBirthsignId(ref RuntimeContentBlob content, in CharacterGenerationState state)
        {
            if ((CharacterGenerationMenu)state.CurrentMenu == CharacterGenerationMenu.Birth)
            {
                if (!state.PendingBirthsignId.IsEmpty)
                    return state.PendingBirthsignId;
                if (!state.BirthsignId.IsEmpty)
                    return state.BirthsignId;
                return RequireFirstBirthsignId(ref content);
            }

            return state.BirthsignId.IsEmpty ? RequireFirstBirthsignId(ref content) : state.BirthsignId;
        }

        static CharacterGenerationChoiceViewModel[] BuildRaceChoices(ref RuntimeContentBlob content, FixedString64Bytes selectedId)
        {
            var result = new List<CharacterGenerationChoiceViewModel>();
            for (int i = 0; i < content.Races.Length; i++)
            {
                ref RuntimeRaceDefBlob race = ref content.Races[i];
                if ((race.Flags & RacePlayableFlag) == 0)
                    continue;
                string id = race.Id.ToString();
                result.Add(new CharacterGenerationChoiceViewModel
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(race.Name.ToString()) ? id : race.Name.ToString(),
                    Description = race.Description.ToString(),
                    Selected = Matches(selectedId, id),
                });
            }

            result.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
            return result.ToArray();
        }

        static string BuildClassName(ref RuntimeContentBlob content, FixedString64Bytes classId)
        {
            if (classId.IsEmpty)
                return string.Empty;
            ref RuntimeClassDefBlob classDef = ref CharacterGenerationUtility.RequireClass(ref content, classId);
            string name = classDef.Name.ToString();
            return string.IsNullOrWhiteSpace(name) ? classDef.Id.ToString() : name.Trim();
        }

        static string BuildBirthsignName(ref RuntimeContentBlob content, FixedString64Bytes birthsignId)
        {
            if (birthsignId.IsEmpty)
                return string.Empty;
            ref RuntimeGenericRecordDefBlob birthsign = ref CharacterGenerationUtility.RequireBirthsign(ref content, birthsignId);
            string name = birthsign.Name.ToString();
            return string.IsNullOrWhiteSpace(name) ? birthsign.Id.ToString() : name.Trim();
        }

        static FixedString64Bytes RequireFirstBirthsignId(ref RuntimeContentBlob content)
        {
            if (content.Birthsigns.Length == 0)
                throw new InvalidOperationException("[VVardenfell][CharGen] Content has no birthsigns.");

            string selectedId = null;
            string selectedName = null;
            for (int i = 0; i < content.Birthsigns.Length; i++)
            {
                ref RuntimeGenericRecordDefBlob birthsign = ref content.Birthsigns[i];
                string id = birthsign.Id.ToString();
                if (string.IsNullOrWhiteSpace(id))
                    throw new InvalidOperationException($"[VVardenfell][CharGen] Birthsign index {i} has no id.");
                string name = string.IsNullOrWhiteSpace(birthsign.Name.ToString()) ? id : birthsign.Name.ToString();
                if (selectedName == null || string.Compare(name, selectedName, StringComparison.Ordinal) < 0)
                {
                    selectedId = id;
                    selectedName = name;
                }
            }

            return new FixedString64Bytes(selectedId);
        }

        static string BuildRaceName(ref RuntimeContentBlob content, FixedString64Bytes raceId)
        {
            if (raceId.IsEmpty)
                return string.Empty;
            ref RuntimeRaceDefBlob race = ref CharacterGenerationUtility.RequireRace(ref content, raceId);
            string name = race.Name.ToString();
            return string.IsNullOrWhiteSpace(name) ? race.Id.ToString() : name.Trim();
        }

        static CharacterGenerationChoiceViewModel[] BuildClassChoices(ref RuntimeContentBlob content, FixedString64Bytes selectedId)
        {
            var result = new List<CharacterGenerationChoiceViewModel>();
            for (int i = 0; i < content.Classes.Length; i++)
            {
                ref RuntimeClassDefBlob classDef = ref content.Classes[i];
                if (classDef.Playable == 0)
                    continue;
                string id = classDef.Id.ToString();
                result.Add(new CharacterGenerationChoiceViewModel
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(classDef.Name.ToString()) ? id : classDef.Name.ToString(),
                    Description = classDef.Description.ToString(),
                    Selected = Matches(selectedId, id),
                });
            }

            result.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
            return result.ToArray();
        }

        static int BuildBodyPartIndex(
            ref RuntimeContentBlob content,
            FixedString64Bytes raceId,
            bool male,
            ActorBodyPartMeshPart part,
            FixedString64Bytes selectedId)
        {
            if (raceId.IsEmpty)
                return -1;

            int index = 0;
            for (int i = 0; i < content.ActorBodyParts.Length; i++)
            {
                ref RuntimeActorBodyPartDefBlob bodyPart = ref content.ActorBodyParts[i];
                if (!CharacterGenerationUtility.IsPlayableBodyPart(ref bodyPart, raceId, male, part))
                    continue;

                if (Matches(selectedId, bodyPart.Id.ToString()))
                    return index;
                index++;
            }

            return -1;
        }

        static CharacterGenerationRaceSkillBonusViewModel[] BuildRaceSkillBonuses(ref RuntimeContentBlob content, FixedString64Bytes raceId)
        {
            if (raceId.IsEmpty)
                return Array.Empty<CharacterGenerationRaceSkillBonusViewModel>();

            ref RuntimeRaceDefBlob race = ref CharacterGenerationUtility.RequireRace(ref content, raceId);
            RuntimeContentBlobUtility.RequireRange(race.FirstSkillBonusIndex, race.SkillBonusCount, content.RaceSkillBonuses.Length, "race skill bonus");
            var result = new List<CharacterGenerationRaceSkillBonusViewModel>();
            for (int i = 0; i < race.SkillBonusCount; i++)
            {
                RaceSkillBonusDef bonus = content.RaceSkillBonuses[race.FirstSkillBonusIndex + i];
                if ((uint)bonus.Skill >= 27u)
                    continue;

                result.Add(new CharacterGenerationRaceSkillBonusViewModel
                {
                    SkillName = RuntimeContentMetadataResolver.ResolveSkillName(bonus.Skill),
                    Bonus = bonus.Bonus,
                    TooltipText = BuildSkillTooltip(ref content, bonus.Skill),
                });
            }

            return result.ToArray();
        }

        static CharacterGenerationRacePowerViewModel[] BuildRacePowers(ref RuntimeContentBlob content, FixedString64Bytes raceId)
        {
            if (raceId.IsEmpty)
                return Array.Empty<CharacterGenerationRacePowerViewModel>();

            ref RuntimeRaceDefBlob race = ref CharacterGenerationUtility.RequireRace(ref content, raceId);
            RuntimeContentBlobUtility.RequireRange(race.FirstPowerSpellIdIndex, race.PowerSpellIdCount, content.RacePowerSpellIds.Length, "race power spell");
            var result = new CharacterGenerationRacePowerViewModel[race.PowerSpellIdCount];
            for (int i = 0; i < race.PowerSpellIdCount; i++)
            {
                string spellId = content.RacePowerSpellIds[race.FirstPowerSpellIdIndex + i].Value.ToString();
                if (string.IsNullOrWhiteSpace(spellId))
                    throw new InvalidOperationException($"[VVardenfell][CharGen] Race '{race.Id}' has an empty power spell id.");
                if (!RuntimeContentBlobUtility.TryGetSpellHandleByIdHash(ref content, RuntimeContentStableHash.HashId(spellId), out var handle) || !handle.IsValid)
                    throw new InvalidOperationException($"[VVardenfell][CharGen] Missing race power spell '{spellId}' for race '{race.Id}'.");

                ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref content, handle);
                result[i] = new CharacterGenerationRacePowerViewModel
                {
                    Id = spellId,
                    Name = RuntimeContentMetadataResolver.ResolveSpellName(ref spell),
                    TooltipText = BuildSpellPlainTooltip(ref content, ref spell),
                };
            }

            return result;
        }

        static CharacterGenerationChoiceViewModel[] BuildBirthsignChoices(ref RuntimeContentBlob content, FixedString64Bytes selectedId)
        {
            var result = new CharacterGenerationChoiceViewModel[content.Birthsigns.Length];
            for (int i = 0; i < content.Birthsigns.Length; i++)
            {
                ref RuntimeGenericRecordDefBlob birthsign = ref content.Birthsigns[i];
                string id = birthsign.Id.ToString();
                result[i] = new CharacterGenerationChoiceViewModel
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(birthsign.Name.ToString()) ? id : birthsign.Name.ToString(),
                    Description = birthsign.Text.ToString(),
                    Selected = Matches(selectedId, id),
                };
            }

            Array.Sort(result, (left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
            return result;
        }

        static CharacterGenerationChoiceViewModel[] BuildBodyPartChoices(
            ref RuntimeContentBlob content,
            FixedString64Bytes raceId,
            bool male,
            ActorBodyPartMeshPart part,
            FixedString64Bytes selectedId)
        {
            if (raceId.IsEmpty)
                return Array.Empty<CharacterGenerationChoiceViewModel>();

            var result = new List<CharacterGenerationChoiceViewModel>();
            for (int i = 0; i < content.ActorBodyParts.Length; i++)
            {
                ref RuntimeActorBodyPartDefBlob bodyPart = ref content.ActorBodyParts[i];
                if (bodyPart.Type != ActorBodyPartMeshType.Skin
                    || bodyPart.NotPlayable != 0
                    || bodyPart.Vampire != 0
                    || bodyPart.FirstPerson != 0
                    || bodyPart.Part != part
                    || bodyPart.Female == (male ? (byte)1 : (byte)0)
                    || !Matches(raceId, bodyPart.RaceId.ToString()))
                {
                    continue;
                }

                string id = bodyPart.Id.ToString();
                result.Add(new CharacterGenerationChoiceViewModel
                {
                    Id = id,
                    Name = id,
                    Selected = Matches(selectedId, id),
                });
            }

            if (result.Count == 0)
                throw new InvalidOperationException($"[VVardenfell][CharGen] Race '{raceId}' has no playable {part} body parts for {(male ? "male" : "female")} sex.");

            return result.ToArray();
        }

        static CharacterGenerationQuestionViewModel BuildGenerateQuestion(ref RuntimeContentBlob content, in CharacterGenerationState state)
        {
            if (state.GenerateStep >= 10)
                return null;

            int questionNumber = state.GenerateStep + 1;
            var question = RequireGmstString(ref content, $"Question_{questionNumber}_Question");
            var answers = new[]
            {
                new CharacterGenerationChoiceViewModel { Name = RequireGmstString(ref content, $"Question_{questionNumber}_AnswerOne"), IntValue = 0 },
                new CharacterGenerationChoiceViewModel { Name = RequireGmstString(ref content, $"Question_{questionNumber}_AnswerTwo"), IntValue = 1 },
                new CharacterGenerationChoiceViewModel { Name = RequireGmstString(ref content, $"Question_{questionNumber}_AnswerThree"), IntValue = 2 },
            };
            ShuffleAnswers(answers, state.GenerateRandomState + state.GenerateStep);
            return new CharacterGenerationQuestionViewModel
            {
                Question = question,
                Answers = answers,
            };
        }

        static CharacterGenerationCustomClassViewModel BuildCustomClass(ref RuntimeContentBlob content, in PlayerCustomClass customClass)
            => new CharacterGenerationCustomClassViewModel
            {
                Name = customClass.Name.ToString(),
                Description = customClass.Description.ToString(),
                Specialization = customClass.Specialization,
                SpecializationTooltipText = BuildSpecializationTooltip(ref content, customClass.Specialization),
                FavoredAttribute0 = customClass.FavoredAttribute0,
                FavoredAttribute1 = customClass.FavoredAttribute1,
                FavoredAttribute0TooltipText = BuildAttributeTooltip(ref content, customClass.FavoredAttribute0),
                FavoredAttribute1TooltipText = BuildAttributeTooltip(ref content, customClass.FavoredAttribute1),
                MajorSkills = new[] { customClass.MajorSkill0, customClass.MajorSkill1, customClass.MajorSkill2, customClass.MajorSkill3, customClass.MajorSkill4 },
                MajorSkillTooltips = new[]
                {
                    BuildSkillTooltip(ref content, customClass.MajorSkill0),
                    BuildSkillTooltip(ref content, customClass.MajorSkill1),
                    BuildSkillTooltip(ref content, customClass.MajorSkill2),
                    BuildSkillTooltip(ref content, customClass.MajorSkill3),
                    BuildSkillTooltip(ref content, customClass.MajorSkill4),
                },
                MinorSkills = new[] { customClass.MinorSkill0, customClass.MinorSkill1, customClass.MinorSkill2, customClass.MinorSkill3, customClass.MinorSkill4 },
                MinorSkillTooltips = new[]
                {
                    BuildSkillTooltip(ref content, customClass.MinorSkill0),
                    BuildSkillTooltip(ref content, customClass.MinorSkill1),
                    BuildSkillTooltip(ref content, customClass.MinorSkill2),
                    BuildSkillTooltip(ref content, customClass.MinorSkill3),
                    BuildSkillTooltip(ref content, customClass.MinorSkill4),
                },
            };

        static CharacterGenerationClassDetailViewModel BuildSelectedClassDetail(ref RuntimeContentBlob content, in CharacterGenerationState state, in PlayerCustomClass customClass)
        {
            if (state.CustomClassActive != 0)
                return BuildCustomClassDetail(ref content, customClass);
            return BuildClassDetailOrNull(ref content, state.ClassId);
        }

        static CharacterGenerationClassDetailViewModel BuildClassDetailOrNull(ref RuntimeContentBlob content, FixedString64Bytes classId)
        {
            if (classId.IsEmpty)
                return null;

            ref RuntimeClassDefBlob classDef = ref CharacterGenerationUtility.RequireClass(ref content, classId);
            return BuildClassDetail(ref content, ref classDef);
        }

        static CharacterGenerationClassDetailViewModel BuildClassDetail(ref RuntimeContentBlob content, ref RuntimeClassDefBlob classDef)
        {
            RuntimeContentBlobUtility.RequireRange(classDef.FirstMajorSkillIndex, classDef.MajorSkillCount, content.ClassMajorSkills.Length, "class major skill");
            RuntimeContentBlobUtility.RequireRange(classDef.FirstMinorSkillIndex, classDef.MinorSkillCount, content.ClassMinorSkills.Length, "class minor skill");
            return new CharacterGenerationClassDetailViewModel
            {
                Id = classDef.Id.ToString(),
                Name = string.IsNullOrWhiteSpace(classDef.Name.ToString()) ? classDef.Id.ToString() : classDef.Name.ToString().Trim(),
                Description = classDef.Description.ToString(),
                ImagePath = BuildClassImagePath(classDef.Id.ToString()),
                SpecializationName = SpecializationName(ref content, classDef.Specialization),
                SpecializationTooltipText = BuildSpecializationTooltip(ref content, classDef.Specialization),
                FavoredAttributes = new[]
                {
                    RequireAttributeName(classDef.FavoredAttribute0),
                    RequireAttributeName(classDef.FavoredAttribute1),
                },
                FavoredAttributeTooltips = new[]
                {
                    BuildAttributeTooltip(ref content, classDef.FavoredAttribute0),
                    BuildAttributeTooltip(ref content, classDef.FavoredAttribute1),
                },
                MajorSkills = BuildSkillNames(ref content.ClassMajorSkills, classDef.FirstMajorSkillIndex, classDef.MajorSkillCount, "class major skill"),
                MajorSkillTooltips = BuildSkillTooltips(ref content, ref content.ClassMajorSkills, classDef.FirstMajorSkillIndex, classDef.MajorSkillCount, "class major skill"),
                MinorSkills = BuildSkillNames(ref content.ClassMinorSkills, classDef.FirstMinorSkillIndex, classDef.MinorSkillCount, "class minor skill"),
                MinorSkillTooltips = BuildSkillTooltips(ref content, ref content.ClassMinorSkills, classDef.FirstMinorSkillIndex, classDef.MinorSkillCount, "class minor skill"),
                TooltipText = BuildClassTooltip(ref content, ref classDef),
            };
        }

        static CharacterGenerationClassDetailViewModel BuildCustomClassDetail(ref RuntimeContentBlob content, in PlayerCustomClass customClass)
        {
            if (customClass.Active == 0 || customClass.Name.IsEmpty)
                return null;

            return new CharacterGenerationClassDetailViewModel
            {
                Id = customClass.Id.ToString(),
                Name = customClass.Name.ToString(),
                Description = customClass.Description.ToString(),
                ImagePath = BuildClassImagePath(customClass.Name.ToString()),
                SpecializationName = SpecializationName(ref content, customClass.Specialization),
                SpecializationTooltipText = BuildSpecializationTooltip(ref content, customClass.Specialization),
                FavoredAttributes = new[]
                {
                    RequireAttributeName(customClass.FavoredAttribute0),
                    RequireAttributeName(customClass.FavoredAttribute1),
                },
                FavoredAttributeTooltips = new[]
                {
                    BuildAttributeTooltip(ref content, customClass.FavoredAttribute0),
                    BuildAttributeTooltip(ref content, customClass.FavoredAttribute1),
                },
                MajorSkills = new[]
                {
                    RequireSkillName(customClass.MajorSkill0),
                    RequireSkillName(customClass.MajorSkill1),
                    RequireSkillName(customClass.MajorSkill2),
                    RequireSkillName(customClass.MajorSkill3),
                    RequireSkillName(customClass.MajorSkill4),
                },
                MajorSkillTooltips = new[]
                {
                    BuildSkillTooltip(ref content, customClass.MajorSkill0),
                    BuildSkillTooltip(ref content, customClass.MajorSkill1),
                    BuildSkillTooltip(ref content, customClass.MajorSkill2),
                    BuildSkillTooltip(ref content, customClass.MajorSkill3),
                    BuildSkillTooltip(ref content, customClass.MajorSkill4),
                },
                MinorSkills = new[]
                {
                    RequireSkillName(customClass.MinorSkill0),
                    RequireSkillName(customClass.MinorSkill1),
                    RequireSkillName(customClass.MinorSkill2),
                    RequireSkillName(customClass.MinorSkill3),
                    RequireSkillName(customClass.MinorSkill4),
                },
                MinorSkillTooltips = new[]
                {
                    BuildSkillTooltip(ref content, customClass.MinorSkill0),
                    BuildSkillTooltip(ref content, customClass.MinorSkill1),
                    BuildSkillTooltip(ref content, customClass.MinorSkill2),
                    BuildSkillTooltip(ref content, customClass.MinorSkill3),
                    BuildSkillTooltip(ref content, customClass.MinorSkill4),
                },
                TooltipText = BuildCustomClassTooltip(ref content, customClass),
            };
        }

        static string[] BuildSkillNames(ref BlobArray<int> source, int start, int count, string label)
        {
            var result = new string[count];
            for (int i = 0; i < count; i++)
                result[i] = RequireSkillName(source[start + i]);
            return result;
        }

        static string[] BuildSkillTooltips(ref RuntimeContentBlob content, ref BlobArray<int> source, int start, int count, string label)
        {
            RuntimeContentBlobUtility.RequireRange(start, count, source.Length, label);
            var result = new string[count];
            for (int i = 0; i < count; i++)
                result[i] = BuildSkillTooltip(ref content, source[start + i]);
            return result;
        }

        static CharacterGenerationBirthsignDetailViewModel BuildBirthsignDetailOrNull(ref RuntimeContentBlob content, FixedString64Bytes birthsignId)
        {
            if (birthsignId.IsEmpty)
                return null;

            ref RuntimeGenericRecordDefBlob birthsign = ref CharacterGenerationUtility.RequireBirthsign(ref content, birthsignId);
            var abilities = new List<CharacterGenerationSpellRowViewModel>();
            var powers = new List<CharacterGenerationSpellRowViewModel>();
            var spells = new List<CharacterGenerationSpellRowViewModel>();
            RuntimeContentBlobUtility.RequireRange(birthsign.FirstPowerSpellIdIndex, birthsign.PowerSpellIdCount, content.GenericRecordPowerSpellIds.Length, "birthsign power spell");
            for (int i = 0; i < birthsign.PowerSpellIdCount; i++)
            {
                string spellId = content.GenericRecordPowerSpellIds[birthsign.FirstPowerSpellIdIndex + i].Value.ToString();
                AddCategorizedSpell(ref content, spellId, abilities, powers, spells);
            }

            return new CharacterGenerationBirthsignDetailViewModel
            {
                Id = birthsign.Id.ToString(),
                Name = string.IsNullOrWhiteSpace(birthsign.Name.ToString()) ? birthsign.Id.ToString() : birthsign.Name.ToString().Trim(),
                Description = birthsign.Text.ToString(),
                ImagePath = birthsign.Icon.ToString(),
                Abilities = abilities.ToArray(),
                Powers = powers.ToArray(),
                Spells = spells.ToArray(),
            };
        }

        static CharacterGenerationReviewSpellRowsViewModel BuildReviewSpells(ref RuntimeContentBlob content, DynamicBuffer<ActorKnownSpell> knownSpells)
        {
            var abilities = new List<CharacterGenerationSpellRowViewModel>();
            var powers = new List<CharacterGenerationSpellRowViewModel>();
            var spells = new List<CharacterGenerationSpellRowViewModel>();
            if (knownSpells.IsCreated)
            {
                for (int i = 0; i < knownSpells.Length; i++)
                {
                    if (!knownSpells[i].Spell.IsValid)
                        continue;
                    AddCategorizedSpell(ref content, knownSpells[i].Spell, abilities, powers, spells);
                }
            }

            return new CharacterGenerationReviewSpellRowsViewModel
            {
                Abilities = abilities.ToArray(),
                Powers = powers.ToArray(),
                Spells = spells.ToArray(),
            };
        }

        static void AddCategorizedSpell(
            ref RuntimeContentBlob content,
            string spellId,
            List<CharacterGenerationSpellRowViewModel> abilities,
            List<CharacterGenerationSpellRowViewModel> powers,
            List<CharacterGenerationSpellRowViewModel> spells)
        {
            if (string.IsNullOrWhiteSpace(spellId))
                throw new InvalidOperationException("[VVardenfell][CharGen] Birthsign contains an empty power spell id.");
            if (!RuntimeContentBlobUtility.TryGetSpellHandleByIdHash(ref content, RuntimeContentStableHash.HashId(spellId), out var handle) || !handle.IsValid)
                throw new InvalidOperationException($"[VVardenfell][CharGen] Missing referenced spell '{spellId}'.");

            AddCategorizedSpell(ref content, handle, abilities, powers, spells);
        }

        static void AddCategorizedSpell(
            ref RuntimeContentBlob content,
            SpellDefHandle handle,
            List<CharacterGenerationSpellRowViewModel> abilities,
            List<CharacterGenerationSpellRowViewModel> powers,
            List<CharacterGenerationSpellRowViewModel> spells)
        {
            ref RuntimeSpellDefBlob spell = ref RuntimeContentBlobUtility.Get(ref content, handle);
            var row = new CharacterGenerationSpellRowViewModel
            {
                Id = spell.Id.ToString(),
                Name = RuntimeContentMetadataResolver.ResolveSpellName(ref spell),
                Type = spell.SpellType,
                SpellTooltip = BuildSpellTooltip(ref content, ref spell),
            };

            switch (spell.SpellType)
            {
                case 1:
                    abilities.Add(row);
                    break;
                case 5:
                    powers.Add(row);
                    break;
                case 0:
                    spells.Add(row);
                    break;
            }
        }

        static string[] BuildSpecializationSkillNames(ref RuntimeContentBlob content, int specialization)
        {
            var result = new List<string>();
            for (int i = 0; i < content.Skills.Length; i++)
            {
                ref RuntimeGenericRecordDefBlob skill = ref content.Skills[i];
                if (skill.Int1 == specialization)
                    result.Add(RequireSkillName(skill.Int0));
            }

            return result.ToArray();
        }

        static string BuildSelectedClassTooltip(ref RuntimeContentBlob content, in CharacterGenerationState state, in PlayerCustomClass customClass)
        {
            if (state.CustomClassActive != 0)
                return BuildCustomClassTooltip(ref content, customClass);
            if (state.ClassId.IsEmpty)
                return string.Empty;
            ref RuntimeClassDefBlob classDef = ref CharacterGenerationUtility.RequireClass(ref content, state.ClassId);
            return BuildClassTooltip(ref content, ref classDef);
        }

        static string BuildRaceTooltip(ref RuntimeContentBlob content, FixedString64Bytes raceId)
        {
            if (raceId.IsEmpty)
                return string.Empty;
            ref RuntimeRaceDefBlob race = ref CharacterGenerationUtility.RequireRace(ref content, raceId);
            return JoinTooltip(string.IsNullOrWhiteSpace(race.Name.ToString()) ? race.Id.ToString() : race.Name.ToString(), race.Description.ToString());
        }

        static string BuildClassTooltip(ref RuntimeContentBlob content, ref RuntimeClassDefBlob classDef)
        {
            string name = string.IsNullOrWhiteSpace(classDef.Name.ToString()) ? classDef.Id.ToString() : classDef.Name.ToString();
            string specialization = SpecializationName(ref content, classDef.Specialization);
            return JoinTooltip(name, classDef.Description.ToString(), $"Specialization: {specialization}");
        }

        static string BuildCustomClassTooltip(ref RuntimeContentBlob content, in PlayerCustomClass customClass)
        {
            if (customClass.Active == 0 || customClass.Name.IsEmpty)
                return string.Empty;
            string specialization = SpecializationName(ref content, customClass.Specialization);
            return JoinTooltip(customClass.Name.ToString(), customClass.Description.ToString(), $"Specialization: {specialization}");
        }

        static string BuildBirthsignTooltip(ref RuntimeContentBlob content, FixedString64Bytes birthsignId)
        {
            if (birthsignId.IsEmpty)
                return string.Empty;

            var detail = BuildBirthsignDetailOrNull(ref content, birthsignId);
            if (detail == null)
                return string.Empty;

            return JoinTooltip(
                detail.Name,
                detail.Description,
                BuildSpellNameLine("Abilities", detail.Abilities),
                BuildSpellNameLine("Powers", detail.Powers),
                BuildSpellNameLine("Spells", detail.Spells),
                detail.ImagePath);
        }

        static string BuildSpellNameLine(string label, CharacterGenerationSpellRowViewModel[] rows)
        {
            if (rows == null || rows.Length == 0)
                return null;

            var names = new string[rows.Length];
            for (int i = 0; i < rows.Length; i++)
                names[i] = rows[i].Name;
            return $"{label}: {string.Join(" ", names)}";
        }

        static string BuildSpecializationTooltip(ref RuntimeContentBlob content, int specialization)
        {
            string title = SpecializationName(ref content, specialization);
            string[] skills = BuildSpecializationSkillNames(ref content, specialization);
            return JoinTooltip(title, skills.Length == 0 ? null : string.Join("\n", skills));
        }

        static string BuildAttributeTooltip(ref RuntimeContentBlob content, int attributeIndex)
        {
            string name = RequireAttributeName(attributeIndex);
            return JoinTooltip(name);
        }

        static string BuildSkillTooltip(ref RuntimeContentBlob content, int skillIndex)
        {
            string name = RequireSkillName(skillIndex);
            string description = string.Empty;
            string governingAttribute = string.Empty;
            string icon = string.Empty;
            for (int i = 0; i < content.Skills.Length; i++)
            {
                ref RuntimeGenericRecordDefBlob skill = ref content.Skills[i];
                if (skill.Int0 != skillIndex)
                    continue;

                if (!string.IsNullOrWhiteSpace(skill.Name.ToString()))
                    name = skill.Name.ToString().Trim();
                description = skill.Text.ToString();
                governingAttribute = RuntimeContentMetadataResolver.ResolveAttributeName(skill.Int1);
                icon = skill.Icon.ToString();
                break;
            }

            string attributeLine = string.IsNullOrWhiteSpace(governingAttribute) ? null : $"Governing Attribute: {governingAttribute}";
            return JoinTooltip(name, description, attributeLine, string.IsNullOrWhiteSpace(icon) ? null : icon);
        }

        static RuntimeSpellTooltipViewModel BuildSpellTooltip(ref RuntimeContentBlob content, ref RuntimeSpellDefBlob spell)
        {
            return new RuntimeSpellTooltipViewModel
            {
                Title = RuntimeContentMetadataResolver.ResolveSpellName(ref spell),
                SchoolText = spell.SpellType == 0 ? BuildSpellSchoolText(ref content, ref spell) : null,
                Effects = BuildSpellTooltipEffects(ref content, ref spell),
            };
        }

        static RuntimeSpellTooltipEffectRow[] BuildSpellTooltipEffects(ref RuntimeContentBlob content, ref RuntimeSpellDefBlob spell)
        {
            if (spell.EffectStartIndex < 0 || spell.EffectCount <= 0)
                return Array.Empty<RuntimeSpellTooltipEffectRow>();

            RuntimeContentBlobUtility.RequireRange(spell.EffectStartIndex, spell.EffectCount, content.MagicEffectInstances.Length, "spell effect");
            var rows = new RuntimeSpellTooltipEffectRow[spell.EffectCount];
            for (int i = 0; i < spell.EffectCount; i++)
            {
                var effect = content.MagicEffectInstances[spell.EffectStartIndex + i];
                rows[i] = new RuntimeSpellTooltipEffectRow
                {
                    EffectId = effect.EffectId,
                    IconPath = RuntimeContentMetadataResolver.ResolveMagicEffectIconPath(ref content, effect.EffectId),
                    Text = BuildSpellTooltipEffectText(ref content, effect),
                };
            }

            return rows;
        }

        static string BuildSpellSchoolText(ref RuntimeContentBlob content, ref RuntimeSpellDefBlob spell)
        {
            if (spell.EffectStartIndex < 0 || spell.EffectCount <= 0)
                return null;

            RuntimeContentBlobUtility.RequireRange(spell.EffectStartIndex, 1, content.MagicEffectInstances.Length, "spell effect");
            short effectId = content.MagicEffectInstances[spell.EffectStartIndex].EffectId;
            int school = -1;
            if (RuntimeContentBlobUtility.TryGetMagicEffectHandleByIndex(ref content, effectId, out var handle))
            {
                ref RuntimeMagicEffectDefBlob def = ref RuntimeContentBlobUtility.Get(ref content, handle);
                school = def.School;
            }

            string schoolName = RuntimeContentMetadataResolver.ResolveSchoolName(ref content, school);
            string schoolLabel = RuntimeContentMetadataResolver.ResolveGameSettingString(ref content, "sSchool", "School");
            return string.IsNullOrWhiteSpace(schoolName) ? null : $"{schoolLabel}: {schoolName}";
        }

        static string BuildSpellPlainTooltip(ref RuntimeContentBlob content, ref RuntimeSpellDefBlob spell)
        {
            var tooltip = BuildSpellTooltip(ref content, ref spell);
            var lines = new List<string> { tooltip.Title, tooltip.SchoolText };
            for (int i = 0; i < tooltip.Effects.Length; i++)
                lines.Add(tooltip.Effects[i].Text);
            return JoinTooltip(lines.ToArray());
        }

        static string BuildSpellTooltipEffectText(ref RuntimeContentBlob content, in MagicEffectInstanceDef effect)
        {
            string name = RuntimeContentMetadataResolver.ResolveMagicEffectName(ref content, effect.EffectId);
            string argument = ResolveEffectArgumentName(effect);
            if (!string.IsNullOrWhiteSpace(argument))
                name = $"{name} {argument}";

            string detail = BuildEffectDetail(effect);
            return string.IsNullOrWhiteSpace(detail) ? name : $"{name} {detail}";
        }

        static string BuildEffectDetail(in MagicEffectInstanceDef effect)
        {
            var parts = new List<string>(4);
            if (effect.MagnitudeMin != 0 || effect.MagnitudeMax != 0)
                parts.Add(effect.MagnitudeMin == effect.MagnitudeMax
                    ? $"{effect.MagnitudeMin} {Pluralize(effect.MagnitudeMin, "pt", "pts")}"
                    : $"{effect.MagnitudeMin} to {effect.MagnitudeMax} pts");
            if (effect.Duration > 0)
                parts.Add($"for {effect.Duration} {Pluralize(effect.Duration, "sec", "secs")}");
            if (effect.Area > 0)
                parts.Add($"in {effect.Area} ft");
            parts.Add(effect.Range switch
            {
                0 => "on Self",
                1 => "on Touch",
                2 => "on Target",
                _ => "range ?",
            });
            return string.Join(" ", parts);
        }

        static string ResolveEffectArgumentName(in MagicEffectInstanceDef effect)
        {
            if (effect.Attribute >= 0)
                return RuntimeContentMetadataResolver.ResolveAttributeName(effect.Attribute);
            if (effect.Skill >= 0)
                return RuntimeContentMetadataResolver.ResolveSkillName(effect.Skill);
            return string.Empty;
        }

        static string Pluralize(int value, string singular, string plural)
            => Math.Abs(value) == 1 ? singular : plural;

        static string JoinTooltip(params string[] lines)
        {
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;
                if (builder.Length > 0)
                    builder.Append('\n');
                builder.Append(lines[i].Trim());
            }

            return builder.ToString();
        }

        static string BuildClassImagePath(string classId)
            => string.IsNullOrWhiteSpace(classId) ? string.Empty : $"textures\\levelup\\{classId.Trim()}.dds";

        static string SpecializationName(ref RuntimeContentBlob content, int specialization)
        {
            string gmst = specialization switch
            {
                0 => "sSpecializationCombat",
                1 => "sSpecializationMagic",
                2 => "sSpecializationStealth",
                _ => null,
            };
            if (gmst == null)
                throw new InvalidOperationException($"[VVardenfell][CharGen] Invalid class specialization {specialization}.");
            return RequireGmstString(ref content, gmst);
        }

        static string RequireAttributeName(int attribute)
        {
            string name = RuntimeContentMetadataResolver.ResolveAttributeName(attribute);
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException($"[VVardenfell][CharGen] Invalid attribute index {attribute}.");
            return name;
        }

        static string RequireSkillName(int skill)
        {
            string name = RuntimeContentMetadataResolver.ResolveSkillName(skill);
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException($"[VVardenfell][CharGen] Invalid skill index {skill}.");
            return name;
        }

        static void ShuffleAnswers(CharacterGenerationChoiceViewModel[] answers, uint seed)
        {
            var random = new Unity.Mathematics.Random(seed == 0 ? 1u : seed);
            for (int i = answers.Length - 1; i > 0; i--)
            {
                int j = random.NextInt(0, i + 1);
                (answers[i], answers[j]) = (answers[j], answers[i]);
            }
        }

        static string RequireGmstString(ref RuntimeContentBlob content, string id)
            => RuntimeContentBlobUtility.RequireGameSettingStringByIdHash(ref content, RuntimeContentStableHash.HashId(id));

        static bool Matches(FixedString64Bytes selectedId, string id)
            => string.Equals(selectedId.ToString(), id, StringComparison.OrdinalIgnoreCase);
    }
}
