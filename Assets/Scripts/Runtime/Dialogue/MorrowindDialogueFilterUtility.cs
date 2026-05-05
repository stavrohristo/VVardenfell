using System;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
using VVardenfell.Runtime.Cache;
using VVardenfell.Runtime.Combat;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;

namespace VVardenfell.Runtime.MorrowindScript
{
    static class MorrowindDialogueFilterUtility
    {
        public static bool TryFindFirstMatchingInfo(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            Entity speakerEntity,
            ActorDefHandle speakerHandle,
            int dialogueIndex,
            int choice,
            out int infoIndex,
            out string unsupportedReason)
        {
            infoIndex = -1;
            unsupportedReason = null;
            if (!speakerHandle.IsValid
                || (uint)dialogueIndex >= (uint)contentBlob.Dialogues.Length)
                return false;

            ref var actor = ref RuntimeContentBlobUtility.Get(ref contentBlob, speakerHandle);
            ref var dialogue = ref contentBlob.Dialogues[dialogueIndex];
            var worldCellBlob = RequireWorldCellBlob(entityManager);
            ref RuntimeWorldCellBlob worldCells = ref worldCellBlob.Value;
            int end = Math.Min(contentBlob.DialogueInfos.Length, dialogue.FirstInfoIndex + dialogue.InfoCount);
            for (int i = dialogue.FirstInfoIndex; i < end; i++)
            {
                ref var info = ref contentBlob.DialogueInfos[i];
                if (!MatchesStaticFilters(ref contentBlob, ref worldCells, entityManager, speakerEntity, ref actor, ref info, out unsupportedReason))
                    continue;

                if (!MatchesSelectRules(ref contentBlob, ref worldCells, entityManager, speakerEntity, ref actor, ref info, choice, out unsupportedReason))
                    continue;

                infoIndex = i;
                return true;
            }

            return false;
        }

        public static bool TryFindRandomMatchingVoicedInfo(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            Entity speakerEntity,
            ActorDefHandle speakerHandle,
            int dialogueIndex,
            int choice,
            ref uint randomState,
            Func<string, bool> isVoiceAvailable,
            out int infoIndex,
            out string unsupportedReason)
        {
            infoIndex = -1;
            unsupportedReason = null;
            if (!speakerHandle.IsValid
                || (uint)dialogueIndex >= (uint)contentBlob.Dialogues.Length)
            {
                return false;
            }

            ref var actor = ref RuntimeContentBlobUtility.Get(ref contentBlob, speakerHandle);
            ref var dialogue = ref contentBlob.Dialogues[dialogueIndex];
            var worldCellBlob = RequireWorldCellBlob(entityManager);
            ref RuntimeWorldCellBlob worldCells = ref worldCellBlob.Value;
            int end = Math.Min(contentBlob.DialogueInfos.Length, dialogue.FirstInfoIndex + dialogue.InfoCount);
            int matchingVoiceCount = 0;
            int firstMatchingVoiceIndex = -1;
            for (int i = dialogue.FirstInfoIndex; i < end; i++)
            {
                ref var info = ref contentBlob.DialogueInfos[i];
                if (!MatchesStaticFilters(ref contentBlob, ref worldCells, entityManager, speakerEntity, ref actor, ref info, out unsupportedReason))
                    continue;

                if (!MatchesSelectRules(ref contentBlob, ref worldCells, entityManager, speakerEntity, ref actor, ref info, choice, out unsupportedReason))
                    continue;

                if (!IsVoiceCandidateAvailable(info.SoundFile.ToString(), isVoiceAvailable))
                    continue;

                matchingVoiceCount++;
                if (firstMatchingVoiceIndex < 0)
                    firstMatchingVoiceIndex = i;
                if ((NextRandom(ref randomState) % (uint)matchingVoiceCount) == 0u)
                    infoIndex = i;
            }

            if (matchingVoiceCount > 1)
                return true;

            if (firstMatchingVoiceIndex >= 0
                && TryFindRandomSiblingVoiceInfo(ref contentBlob, dialogueIndex, firstMatchingVoiceIndex, ref randomState, isVoiceAvailable, out infoIndex))
            {
                return true;
            }

            return TryFindRandomActorVoiceGroupInfo(ref contentBlob, ref actor, dialogueIndex, ref randomState, isVoiceAvailable, out infoIndex);
        }

        static bool TryFindRandomSiblingVoiceInfo(
            ref RuntimeContentBlob contentBlob,
            int dialogueIndex,
            int selectedInfoIndex,
            ref uint randomState,
            Func<string, bool> isVoiceAvailable,
            out int infoIndex)
        {
            infoIndex = selectedInfoIndex;
            ref var selected = ref contentBlob.DialogueInfos[selectedInfoIndex];
            if (!TrySplitVoiceVariationKey(selected.SoundFile.ToString(), out string selectedDirectory, out string selectedStem))
                return false;

            ref var dialogue = ref contentBlob.Dialogues[dialogueIndex];
            int end = Math.Min(contentBlob.DialogueInfos.Length, dialogue.FirstInfoIndex + dialogue.InfoCount);
            int matchingVoiceCount = 0;
            for (int i = dialogue.FirstInfoIndex; i < end; i++)
            {
                ref var info = ref contentBlob.DialogueInfos[i];
                string soundFile = info.SoundFile.ToString();
                if (!IsVoiceCandidateAvailable(soundFile, isVoiceAvailable))
                    continue;

                if (!TrySplitVoiceVariationKey(soundFile, out string directory, out string stem))
                    continue;
                if (!string.Equals(directory, selectedDirectory, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(stem, selectedStem, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matchingVoiceCount++;
                if ((NextRandom(ref randomState) % (uint)matchingVoiceCount) == 0u)
                    infoIndex = i;
            }

            return matchingVoiceCount > 1;
        }

        static bool TryFindRandomActorVoiceGroupInfo(
            ref RuntimeContentBlob contentBlob,
            ref RuntimeActorDefBlob actor,
            int dialogueIndex,
            ref uint randomState,
            Func<string, bool> isVoiceAvailable,
            out int infoIndex)
        {
            infoIndex = -1;
            if (actor.Kind != ActorDefKind.Npc || !TryResolveVoiceRaceFolder(actor.RaceId.ToString(), out string raceFolder))
                return false;

            string genderFolder = (actor.Flags & 0x1u) != 0u ? "f" : "m";
            string targetDirectory = $"vo/{raceFolder}/{genderFolder}";
            ref var dialogue = ref contentBlob.Dialogues[dialogueIndex];
            int end = Math.Min(contentBlob.DialogueInfos.Length, dialogue.FirstInfoIndex + dialogue.InfoCount);
            int matchingVoiceCount = 0;
            for (int i = dialogue.FirstInfoIndex; i < end; i++)
            {
                ref var info = ref contentBlob.DialogueInfos[i];
                string soundFile = info.SoundFile.ToString();
                if (!IsVoiceCandidateAvailable(soundFile, isVoiceAvailable))
                    continue;

                if (!TrySplitVoiceVariationKey(soundFile, out string directory, out string stem))
                    continue;
                if (!string.Equals(directory, targetDirectory, StringComparison.OrdinalIgnoreCase)
                    || !stem.StartsWith("hit_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matchingVoiceCount++;
                if ((NextRandom(ref randomState) % (uint)matchingVoiceCount) == 0u)
                    infoIndex = i;
            }

            return infoIndex >= 0;
        }

        static bool IsVoiceCandidateAvailable(string soundFile, Func<string, bool> isVoiceAvailable)
            => !string.IsNullOrWhiteSpace(soundFile)
               && (isVoiceAvailable == null || isVoiceAvailable(soundFile));

        static bool TrySplitVoiceVariationKey(string soundFile, out string directory, out string stem)
        {
            directory = null;
            stem = null;
            if (string.IsNullOrWhiteSpace(soundFile))
                return false;

            string normalized = soundFile.Trim().Trim('"').Replace('\\', '/');
            int slashIndex = normalized.LastIndexOf('/');
            string fileName = slashIndex >= 0 ? normalized.Substring(slashIndex + 1) : normalized;
            int dotIndex = fileName.LastIndexOf('.');
            string fileStem = dotIndex >= 0 ? fileName.Substring(0, dotIndex) : fileName;
            int end = fileStem.Length;
            while (end > 0 && char.IsDigit(fileStem[end - 1]))
                end--;

            if (end <= 0)
                return false;

            directory = slashIndex >= 0 ? normalized.Substring(0, slashIndex).ToLowerInvariant() : string.Empty;
            stem = fileStem.Substring(0, end).ToLowerInvariant();
            return !string.IsNullOrWhiteSpace(stem);
        }

        static bool TryResolveVoiceRaceFolder(string raceId, out string folder)
        {
            switch (ContentId.NormalizeId(raceId))
            {
                case "argonian":
                    folder = "a";
                    return true;
                case "breton":
                    folder = "b";
                    return true;
                case "dark elf":
                    folder = "d";
                    return true;
                case "high elf":
                    folder = "h";
                    return true;
                case "imperial":
                    folder = "i";
                    return true;
                case "khajiit":
                    folder = "k";
                    return true;
                case "nord":
                    folder = "n";
                    return true;
                case "orc":
                    folder = "o";
                    return true;
                case "redguard":
                    folder = "r";
                    return true;
                case "wood elf":
                    folder = "w";
                    return true;
                default:
                    folder = null;
                    return false;
            }
        }

        static bool MatchesStaticFilters(
            ref RuntimeContentBlob contentBlob,
            ref RuntimeWorldCellBlob worldCells,
            EntityManager entityManager,
            Entity speakerEntity,
            ref RuntimeActorDefBlob actor,
            ref RuntimeDialogueInfoDefBlob info,
            out string unsupportedReason)
        {
            unsupportedReason = null;
            bool isCreature = actor.Kind != ActorDefKind.Npc;
            if (info.ActorIdHash != 0UL)
            {
                if (info.ActorIdHash != actor.IdHash && info.ActorIdHash != actor.OriginalIdHash)
                    return false;
            }
            else if (isCreature)
            {
                return false;
            }

            if (info.RaceIdHash != 0UL)
            {
                if (isCreature)
                    return true;
                if (info.RaceIdHash != actor.RaceIdHash)
                    return false;
            }

            if (info.ClassIdHash != 0UL)
            {
                if (isCreature)
                    return true;
                if (info.ClassIdHash != actor.ClassIdHash)
                    return false;
            }

            if (info.FactionLess)
            {
                if (isCreature)
                    return true;
                if (actor.FactionIdHash != 0UL)
                    return false;
            }
            else if (info.FactionIdHash != 0UL)
            {
                if (isCreature)
                    return true;
                if (info.FactionIdHash != actor.FactionIdHash)
                    return false;
                if (actor.Rank < info.Rank)
                    return false;
            }
            else if (info.Rank != -1)
            {
                if (isCreature)
                    return true;
                if (actor.Rank < info.Rank)
                    return false;
            }

            if (!isCreature && info.Gender != -1)
            {
                bool female = (actor.Flags & 0x1u) != 0u;
                int openMwRejectedGender = female ? 0 : 1;
                if (info.Gender == openMwRejectedGender)
                    return false;
            }

            if (!MatchesPlayerFactionAndRank(ref contentBlob, entityManager, ref actor, ref info))
                return false;

            string infoCellId = info.CellId.ToString();
            if (!string.IsNullOrWhiteSpace(infoCellId) && !MatchesPlayerCell(ref worldCells, entityManager, infoCellId))
                return false;

            if (actor.Kind == ActorDefKind.Npc
                && info.DispositionOrJournalIndex > 0
                && ResolveBaseDisposition(entityManager, speakerEntity, ref actor) < info.DispositionOrJournalIndex)
            {
                return false;
            }

            return true;
        }

        static int ResolveBaseDisposition(EntityManager entityManager, Entity speakerEntity, ref RuntimeActorDefBlob actor)
        {
            if (entityManager.Exists(speakerEntity) && entityManager.HasComponent<ActorDispositionState>(speakerEntity))
                return entityManager.GetComponentData<ActorDispositionState>(speakerEntity).BaseDisposition;

            return actor.Disposition;
        }

        static bool MatchesPlayerFactionAndRank(ref RuntimeContentBlob contentBlob, EntityManager entityManager, ref RuntimeActorDefBlob speaker, ref RuntimeDialogueInfoDefBlob info)
        {
            if (info.PcFactionIdHash == 0UL && info.PcRank == -1)
                return true;

            ulong requiredFactionHash = info.PcFactionIdHash != 0UL
                ? info.PcFactionIdHash
                : speaker.FactionIdHash;

            if (requiredFactionHash == 0UL
                || !RuntimeContentBlobUtility.TryGetFactionHandleByIdHash(ref contentBlob, requiredFactionHash, out var factionHandle)
                || !factionHandle.IsValid)
            {
                return false;
            }

            if (!TryReadPlayerFaction(entityManager, factionHandle.Index, out var membership)
                || membership.Joined == 0
                || membership.Expelled != 0)
            {
                return false;
            }

            return info.PcRank == -1 || membership.Rank >= info.PcRank;
        }

        static bool TryReadPlayerFaction(EntityManager entityManager, int factionIndex, out PlayerFactionMembership membership)
        {
            membership = default;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<PlayerFactionMembership>());
            if (query.IsEmptyIgnoreFilter)
                return false;

            var factions = entityManager.GetBuffer<PlayerFactionMembership>(query.GetSingletonEntity(), true);
            for (int i = 0; i < factions.Length; i++)
            {
                if (factions[i].FactionIndex == factionIndex)
                {
                    membership = factions[i];
                    return true;
                }
            }

            return false;
        }

        static bool MatchesPlayerCell(ref RuntimeWorldCellBlob worldCells, EntityManager entityManager, string cellPrefix)
        {
            if (string.IsNullOrWhiteSpace(cellPrefix))
                return true;

            if (!TryReadCurrentPlayerCell(ref worldCells, entityManager, out string cellName))
                return false;

            return cellName.StartsWith(cellPrefix, StringComparison.OrdinalIgnoreCase);
        }

        static bool TryReadCurrentPlayerCell(ref RuntimeWorldCellBlob worldCells, EntityManager entityManager, out string cellName)
        {
            cellName = string.Empty;
            using var transitionQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<InteriorTransitionState>());
            if (!transitionQuery.IsEmptyIgnoreFilter)
            {
                var transition = entityManager.GetComponentData<InteriorTransitionState>(transitionQuery.GetSingletonEntity());
                if (transition.InteriorActive != 0)
                {
                    cellName = transition.ActiveInteriorCellId.ToString();
                    return !string.IsNullOrWhiteSpace(cellName);
                }
            }

            using var streamingQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<StreamingConfig>());
            if (streamingQuery.IsEmptyIgnoreFilter)
                return false;

            var streaming = entityManager.GetComponentData<StreamingConfig>(streamingQuery.GetSingletonEntity());
            if (!RuntimeWorldCellBlobUtility.TryGetExteriorCellIndex(ref worldCells, streaming.CameraCell, out int cellIndex))
                return false;

            ref RuntimeWorldCellDefBlob cell = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, cellIndex);
            cellName = cell.CellId.ToString();
            return !string.IsNullOrWhiteSpace(cellName);
        }

        static bool TryReadActorCell(ref RuntimeWorldCellBlob worldCells, EntityManager entityManager, Entity actorEntity, out string cellName)
        {
            cellName = string.Empty;
            if (!entityManager.Exists(actorEntity))
                return false;

            if (entityManager.HasComponent<LogicalRefLocation>(actorEntity))
            {
                var location = entityManager.GetComponentData<LogicalRefLocation>(actorEntity);
                if (location.IsInterior != 0)
                {
                    cellName = location.InteriorCellId.ToString();
                    return !string.IsNullOrWhiteSpace(cellName);
                }

                if (RuntimeWorldCellBlobUtility.TryGetExteriorCellIndex(ref worldCells, location.ExteriorCell, out int cellIndex))
                {
                    ref RuntimeWorldCellDefBlob cell = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, cellIndex);
                    cellName = cell.CellId.ToString();
                    return !string.IsNullOrWhiteSpace(cellName);
                }
            }

            if (entityManager.HasComponent<CellLink>(actorEntity))
            {
                var link = entityManager.GetComponentData<CellLink>(actorEntity);
                if (RuntimeWorldCellBlobUtility.TryGetExteriorCellIndex(ref worldCells, link.Value, out int cellIndex))
                {
                    ref RuntimeWorldCellDefBlob cell = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, cellIndex);
                    cellName = cell.CellId.ToString();
                    return !string.IsNullOrWhiteSpace(cellName);
                }
            }

            return false;
        }

        static bool MatchesSelectRules(
            ref RuntimeContentBlob contentBlob,
            ref RuntimeWorldCellBlob worldCells,
            EntityManager entityManager,
            Entity speakerEntity,
            ref RuntimeActorDefBlob actor,
            ref RuntimeDialogueInfoDefBlob info,
            int choice,
            out string unsupportedReason)
        {
            unsupportedReason = null;
            if (info.SelectRuleCount <= 0 || info.FirstSelectRuleIndex < 0)
                return true;

            int end = Math.Min(contentBlob.DialogueConditions.Length, info.FirstSelectRuleIndex + info.SelectRuleCount);
            for (int i = info.FirstSelectRuleIndex; i < end; i++)
            {
                ref var rule = ref contentBlob.DialogueConditions[i];
                if (!TryEvaluateRule(ref contentBlob, ref worldCells, entityManager, speakerEntity, ref actor, ref rule, choice, out bool matched, out unsupportedReason))
                    return false;

                if (!matched)
                    return false;
            }

            return true;
        }

        static bool TryEvaluateRule(
            ref RuntimeContentBlob contentBlob,
            ref RuntimeWorldCellBlob worldCells,
            EntityManager entityManager,
            Entity speakerEntity,
            ref RuntimeActorDefBlob actor,
            ref RuntimeDialogueConditionDefBlob rule,
            int choice,
            out bool matched,
            out string unsupportedReason)
        {
            matched = false;
            unsupportedReason = null;
            var function = (DialogueConditionFunction)rule.Function;
            switch (function)
            {
                case DialogueConditionFunction.Global:
                    if (rule.VariableHash == 0UL
                        || !RuntimeContentBlobUtility.TryGetGlobalHandleByIdHash(ref contentBlob, rule.VariableHash, out var globalHandle)
                        || !globalHandle.IsValid)
                    {
                        matched = true;
                        return true;
                    }
                    if (!TryReadGlobal(entityManager, globalHandle.Index, out float globalValue))
                        return false;
                    matched = Compare(globalValue, ref rule);
                    return true;

                case DialogueConditionFunction.Local:
                    matched = TryReadLocal(ref contentBlob, entityManager, speakerEntity, actor.ScriptIdHash, rule.VariableHash, out float localValue)
                        && Compare(localValue, ref rule);
                    return true;

                case DialogueConditionFunction.NotLocal:
                    matched = !TryReadLocal(ref contentBlob, entityManager, speakerEntity, actor.ScriptIdHash, rule.VariableHash, out float notLocalValue)
                        || !Compare(notLocalValue, ref rule);
                    return true;

                case DialogueConditionFunction.Journal:
                    if (rule.VariableHash == 0UL
                        || !RuntimeContentBlobUtility.TryGetDialogueHandleByIdHash(ref contentBlob, rule.VariableHash, out var journalHandle)
                        || !journalHandle.IsValid)
                        return false;
                    if (!TryReadJournal(entityManager, journalHandle.Index, out int journalIndex))
                        return false;
                    matched = Compare(journalIndex, ref rule);
                    return true;

                case DialogueConditionFunction.Choice:
                    if (choice < 0)
                        return false;
                    matched = Compare(choice, ref rule);
                    return true;

                case DialogueConditionFunction.Fight:
                case DialogueConditionFunction.Hello:
                case DialogueConditionFunction.Alarm:
                case DialogueConditionFunction.Flee:
                    matched = Compare(ResolveAiSetting(entityManager, speakerEntity, ref actor, function, rule.Index), ref rule);
                    return true;

                case DialogueConditionFunction.NotId:
                    matched = rule.VariableHash != actor.IdHash && rule.VariableHash != actor.OriginalIdHash;
                    return true;

                case DialogueConditionFunction.NotFaction:
                    matched = rule.VariableHash != actor.FactionIdHash;
                    return true;

                case DialogueConditionFunction.NotClass:
                    matched = rule.VariableHash != actor.ClassIdHash;
                    return true;

                case DialogueConditionFunction.NotRace:
                    matched = rule.VariableHash != actor.RaceIdHash;
                    return true;

                case DialogueConditionFunction.NotCell:
                    if (!TryReadActorCell(ref worldCells, entityManager, speakerEntity, out string actorCell))
                        return false;
                    matched = !actorCell.StartsWith(rule.Variable.ToString(), StringComparison.OrdinalIgnoreCase);
                    return true;

                case DialogueConditionFunction.PcLevel:
                    if (TryReadPlayerIdentity(entityManager, out var identity))
                    {
                        matched = Compare(identity.Level, ref rule);
                        return true;
                    }
                    return false;

                case DialogueConditionFunction.PcReputation:
                    if (TryReadPlayerIdentity(entityManager, out var repIdentity))
                    {
                        matched = Compare(repIdentity.Reputation, ref rule);
                        return true;
                    }
                    return false;

                case DialogueConditionFunction.PcCrimeLevel:
                    if (TryReadPlayerCrime(entityManager, out var crime))
                    {
                        matched = Compare(crime.Bounty, ref rule);
                        return true;
                    }
                    return false;

                case DialogueConditionFunction.Alarmed:
                    matched = Compare(ResolveAlarmed(entityManager, speakerEntity), ref rule);
                    return true;

                case DialogueConditionFunction.Attacked:
                    matched = Compare(ResolveAttacked(entityManager, speakerEntity), ref rule);
                    return true;

                case DialogueConditionFunction.ShouldAttack:
                    matched = Compare(ResolveShouldAttack(entityManager, speakerEntity), ref rule);
                    return true;

                case DialogueConditionFunction.TalkedToPc:
                    matched = Compare(0, ref rule);
                    return true;

                case DialogueConditionFunction.PcClothingModifier:
                    if (TryReadPlayerClothingModifier(ref contentBlob, entityManager, out int clothingModifier))
                    {
                        matched = Compare(clothingModifier, ref rule);
                        return true;
                    }
                    return false;

                case DialogueConditionFunction.PcHealth:
                case DialogueConditionFunction.PcMagicka:
                case DialogueConditionFunction.PcFatigue:
                case DialogueConditionFunction.PcHealthPercent:
                    if (!TryReadPlayerVitals(entityManager, out var vitals))
                        return false;
                    matched = Compare(ResolvePlayerVital(function, vitals), ref rule);
                    return true;

                case DialogueConditionFunction.FacReactionLowest:
                case DialogueConditionFunction.FacReactionHighest:
                    matched = TryResolvePlayerFactionReaction(
                        ref contentBlob,
                        entityManager,
                        actor.FactionIdHash,
                        function == DialogueConditionFunction.FacReactionLowest,
                        out int reaction)
                        && Compare(reaction, ref rule);
                    return true;
            }

            if (TryResolvePlayerAttributeOrSkill(entityManager, function, out float stat))
            {
                matched = Compare(stat, ref rule);
                return true;
            }

            unsupportedReason = $"unsupported dialogue select function {function}.";
            return false;
        }

        static uint NextRandom(ref uint state)
        {
            state = state == 0u ? 1u : state;
            state = (1664525u * state) + 1013904223u;
            return state;
        }

        static int ResolveAiSetting(
            EntityManager entityManager,
            Entity speakerEntity,
            ref RuntimeActorDefBlob actor,
            DialogueConditionFunction function,
            byte argument)
        {
            ActorAiSettingsState settings = entityManager.Exists(speakerEntity) && entityManager.HasComponent<ActorAiSettingsState>(speakerEntity)
                ? entityManager.GetComponentData<ActorAiSettingsState>(speakerEntity)
                : new ActorAiSettingsState
                {
                    Hello = actor.AiData.Hello,
                    Fight = actor.AiData.Fight,
                    Flee = actor.AiData.Flee,
                    Alarm = actor.AiData.Alarm,
                };

            return argument switch
            {
                0 => settings.Hello,
                1 => settings.Fight,
                2 => settings.Flee,
                3 => settings.Alarm,
                _ => function switch
                {
                    DialogueConditionFunction.Hello => settings.Hello,
                    DialogueConditionFunction.Fight => settings.Fight,
                    DialogueConditionFunction.Flee => settings.Flee,
                    DialogueConditionFunction.Alarm => settings.Alarm,
                    _ => 0,
                },
            };
        }

        static bool TryReadGlobal(EntityManager entityManager, int index, out float value)
        {
            value = 0f;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MorrowindScriptGlobalValue>());
            if (query.IsEmptyIgnoreFilter)
                return false;
            var globals = entityManager.GetBuffer<MorrowindScriptGlobalValue>(query.GetSingletonEntity(), true);
            if ((uint)index >= (uint)globals.Length)
                return false;
            value = globals[index].ValueKind == (byte)MorrowindScriptValueKind.Float ? globals[index].FloatValue : globals[index].IntValue;
            return true;
        }

        static bool TryReadJournal(EntityManager entityManager, int index, out int value)
        {
            value = 0;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MorrowindQuestJournalIndex>());
            if (query.IsEmptyIgnoreFilter)
                return false;
            var journal = entityManager.GetBuffer<MorrowindQuestJournalIndex>(query.GetSingletonEntity(), true);
            if ((uint)index >= (uint)journal.Length)
                return false;
            value = journal[index].Index;
            return true;
        }

        static bool TryReadLocal(ref RuntimeContentBlob contentBlob, EntityManager entityManager, Entity speakerEntity, ulong scriptIdHash, ulong localNameHash, out float value)
        {
            value = 0f;
            if (scriptIdHash == 0UL
                || localNameHash == 0UL
                || !RuntimeContentBlobUtility.TryGetMorrowindScriptProgramHandleByIdHash(ref contentBlob, scriptIdHash, out var programHandle)
                || !programHandle.IsValid)
                return false;

            ref var localsDef = ref RuntimeContentBlobUtility.GetMorrowindScriptLocals(ref contentBlob, programHandle);
            ref var program = ref RuntimeContentBlobUtility.Get(ref contentBlob, programHandle);
            int localIndex = -1;
            for (int i = 0; i < program.LocalCount; i++)
            {
                if (localsDef[program.FirstLocalIndex + i].NameHash == localNameHash)
                {
                    localIndex = i;
                    break;
                }
            }

            if (localIndex < 0)
                return false;

            if (!entityManager.Exists(speakerEntity) || !entityManager.HasBuffer<MorrowindScriptLocalValue>(speakerEntity))
            {
                value = 0f;
                return true;
            }

            var locals = entityManager.GetBuffer<MorrowindScriptLocalValue>(speakerEntity, true);
            if ((uint)localIndex >= (uint)locals.Length)
            {
                value = 0f;
                return true;
            }

            value = locals[localIndex].ValueKind == (byte)MorrowindScriptValueKind.Float ? locals[localIndex].FloatValue : locals[localIndex].IntValue;
            return true;
        }

        static bool TryReadPlayerIdentity(EntityManager entityManager, out ActorIdentitySet identity)
        {
            identity = default;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<ActorIdentitySet>());
            if (query.IsEmptyIgnoreFilter)
                return false;
            identity = entityManager.GetComponentData<ActorIdentitySet>(query.GetSingletonEntity());
            return true;
        }

        static bool TryReadPlayerCrime(EntityManager entityManager, out PlayerCrimeState crime)
        {
            crime = default;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<PlayerCrimeState>());
            if (query.IsEmptyIgnoreFilter)
                return false;
            crime = entityManager.GetComponentData<PlayerCrimeState>(query.GetSingletonEntity());
            return true;
        }

        static int ResolveAlarmed(EntityManager entityManager, Entity speakerEntity)
        {
            if (speakerEntity == Entity.Null
                || !entityManager.Exists(speakerEntity)
                || !entityManager.HasComponent<ActorCrimeState>(speakerEntity))
            {
                return 0;
            }

            return entityManager.GetComponentData<ActorCrimeState>(speakerEntity).Alarmed != 0 ? 1 : 0;
        }

        static int ResolveAttacked(EntityManager entityManager, Entity speakerEntity)
        {
            if (speakerEntity == Entity.Null
                || !entityManager.Exists(speakerEntity)
                || !entityManager.HasComponent<ActorScriptEventState>(speakerEntity))
            {
                return 0;
            }

            return entityManager.GetComponentData<ActorScriptEventState>(speakerEntity).Attacked != 0 ? 1 : 0;
        }

        static int ResolveShouldAttack(EntityManager entityManager, Entity speakerEntity)
        {
            if (speakerEntity == Entity.Null || !entityManager.Exists(speakerEntity))
                return 0;

            if (TryReadPlayerEntity(entityManager, out Entity player)
                && entityManager.HasComponent<ActorCombatTargetState>(speakerEntity))
            {
                var combat = entityManager.GetComponentData<ActorCombatTargetState>(speakerEntity);
                uint playerRef = player != Entity.Null && entityManager.HasComponent<PlacedRefIdentity>(player)
                    ? entityManager.GetComponentData<PlacedRefIdentity>(player).Value
                    : 0u;
                if (combat.Active != 0 && (combat.TargetEntity == player || (playerRef != 0u && combat.TargetPlacedRefId == playerRef)))
                    return 1;
            }

            if (!entityManager.HasComponent<ActorAiSettingsState>(speakerEntity))
                return 0;

            return entityManager.GetComponentData<ActorAiSettingsState>(speakerEntity).Fight >= 100 ? 1 : 0;
        }

        static bool TryReadPlayerEntity(EntityManager entityManager, out Entity player)
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>());
            if (query.IsEmptyIgnoreFilter)
            {
                player = Entity.Null;
                return false;
            }

            player = query.GetSingletonEntity();
            return true;
        }

        static bool TryReadPlayerVitals(EntityManager entityManager, out ActorVitalSet vitals)
        {
            vitals = default;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<ActorVitalSet>());
            if (query.IsEmptyIgnoreFilter)
                return false;
            vitals = entityManager.GetComponentData<ActorVitalSet>(query.GetSingletonEntity());
            return true;
        }

        static bool TryResolvePlayerFactionReaction(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            ulong speakerFactionIdHash,
            bool lowest,
            out int value)
        {
            value = 0;
            if (speakerFactionIdHash == 0UL
                || !RuntimeContentBlobUtility.TryGetFactionHandleByIdHash(ref contentBlob, speakerFactionIdHash, out var speakerFaction)
                || !speakerFaction.IsValid)
            {
                return true;
            }

            using var playerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<PlayerFactionMembership>());
            if (playerQuery.IsEmptyIgnoreFilter)
                return true;

            using var dialogueQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<MorrowindDialogueState>(), ComponentType.ReadOnly<MorrowindFactionReactionOverride>());
            if (dialogueQuery.IsEmptyIgnoreFilter)
                return true;

            var playerFactions = entityManager.GetBuffer<PlayerFactionMembership>(playerQuery.GetSingletonEntity(), true);
            var overrides = entityManager.GetBuffer<MorrowindFactionReactionOverride>(dialogueQuery.GetSingletonEntity(), true);
            for (int i = 0; i < playerFactions.Length; i++)
            {
                if (playerFactions[i].Joined == 0)
                    continue;

                int reaction = MorrowindDialogueUtility.GetFactionReaction(
                    ref contentBlob,
                    overrides,
                    speakerFaction.Index,
                    playerFactions[i].FactionIndex);
                if (lowest ? reaction < value : reaction > value)
                    value = reaction;
            }

            return true;
        }

        static bool TryReadPlayerClothingModifier(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            out int value)
        {
            value = 0;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<ActorEquipmentSlot>());
            if (query.IsEmptyIgnoreFilter)
                return false;

            var equipment = entityManager.GetBuffer<ActorEquipmentSlot>(query.GetSingletonEntity(), true);
            for (int i = 0; i < equipment.Length; i++)
            {
                var slot = equipment[i];
                if (!IsPcClothingModifierSlot(slot.Slot))
                    continue;

                if (slot.Content.Kind != ContentReferenceKind.Item || slot.Content.HandleValue <= 0)
                    return false;

                var itemHandle = new ItemDefHandle { Value = slot.Content.HandleValue };
                if (!RuntimeContentBlobUtility.TryGetItemEquipment(ref contentBlob, itemHandle, out var itemEquipment))
                    return false;

                value += itemEquipment.Value;
            }

            return true;
        }

        static bool IsPcClothingModifierSlot(ItemEquipmentSlot slot)
            => slot != ItemEquipmentSlot.None
               && slot != ItemEquipmentSlot.Weapon
               && slot != ItemEquipmentSlot.Shield;

        static bool TryResolvePlayerAttributeOrSkill(EntityManager entityManager, DialogueConditionFunction function, out float value)
        {
            value = 0f;
            using var attrQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<ActorAttributeSet>());
            using var skillQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<ActorSkillSet>());
            if (!attrQuery.IsEmptyIgnoreFilter)
            {
                var attributes = entityManager.GetComponentData<ActorAttributeSet>(attrQuery.GetSingletonEntity());
                switch (function)
                {
                    case DialogueConditionFunction.PcStrength: value = attributes.Strength; return true;
                    case DialogueConditionFunction.PcIntelligence: value = attributes.Intelligence; return true;
                    case DialogueConditionFunction.PcWillpower: value = attributes.Willpower; return true;
                    case DialogueConditionFunction.PcAgility: value = attributes.Agility; return true;
                    case DialogueConditionFunction.PcSpeed: value = attributes.Speed; return true;
                    case DialogueConditionFunction.PcEndurance: value = attributes.Endurance; return true;
                    case DialogueConditionFunction.PcPersonality: value = attributes.Personality; return true;
                    case DialogueConditionFunction.PcLuck: value = attributes.Luck; return true;
                }
            }

            if (!skillQuery.IsEmptyIgnoreFilter)
            {
                var skills = entityManager.GetComponentData<ActorSkillSet>(skillQuery.GetSingletonEntity());
                switch (function)
                {
                    case DialogueConditionFunction.PcBlock: value = skills.Block; return true;
                    case DialogueConditionFunction.PcArmorer: value = skills.Armorer; return true;
                    case DialogueConditionFunction.PcMediumArmor: value = skills.MediumArmor; return true;
                    case DialogueConditionFunction.PcHeavyArmor: value = skills.HeavyArmor; return true;
                    case DialogueConditionFunction.PcBluntWeapon: value = skills.BluntWeapon; return true;
                    case DialogueConditionFunction.PcLongBlade: value = skills.LongBlade; return true;
                    case DialogueConditionFunction.PcAxe: value = skills.Axe; return true;
                    case DialogueConditionFunction.PcSpear: value = skills.Spear; return true;
                    case DialogueConditionFunction.PcAthletics: value = skills.Athletics; return true;
                    case DialogueConditionFunction.PcEnchant: value = skills.Enchant; return true;
                    case DialogueConditionFunction.PcDestruction: value = skills.Destruction; return true;
                    case DialogueConditionFunction.PcAlteration: value = skills.Alteration; return true;
                    case DialogueConditionFunction.PcIllusion: value = skills.Illusion; return true;
                    case DialogueConditionFunction.PcConjuration: value = skills.Conjuration; return true;
                    case DialogueConditionFunction.PcMysticism: value = skills.Mysticism; return true;
                    case DialogueConditionFunction.PcRestoration: value = skills.Restoration; return true;
                    case DialogueConditionFunction.PcAlchemy: value = skills.Alchemy; return true;
                    case DialogueConditionFunction.PcUnarmored: value = skills.Unarmored; return true;
                    case DialogueConditionFunction.PcSecurity: value = skills.Security; return true;
                    case DialogueConditionFunction.PcSneak: value = skills.Sneak; return true;
                    case DialogueConditionFunction.PcAcrobatics: value = skills.Acrobatics; return true;
                    case DialogueConditionFunction.PcLightArmor: value = skills.LightArmor; return true;
                    case DialogueConditionFunction.PcShortBlade: value = skills.ShortBlade; return true;
                    case DialogueConditionFunction.PcMarksman: value = skills.Marksman; return true;
                    case DialogueConditionFunction.PcMercantile: value = skills.Mercantile; return true;
                    case DialogueConditionFunction.PcSpeechcraft: value = skills.Speechcraft; return true;
                    case DialogueConditionFunction.PcHandToHand: value = skills.HandToHand; return true;
                }
            }

            return false;
        }

        static float ResolvePlayerVital(DialogueConditionFunction function, in ActorVitalSet vitals)
        {
            return function switch
            {
                DialogueConditionFunction.PcHealth => vitals.CurrentHealth,
                DialogueConditionFunction.PcMagicka => vitals.CurrentMagicka,
                DialogueConditionFunction.PcFatigue => vitals.CurrentFatigue,
                DialogueConditionFunction.PcHealthPercent => vitals.ModifiedHealthBase > 0f ? vitals.CurrentHealth / vitals.ModifiedHealthBase * 100f : 0f,
                _ => 0f,
            };
        }

        static bool Compare(float actual, ref RuntimeDialogueConditionDefBlob rule)
        {
            float expected = rule.ValueKind == (byte)MorrowindScriptValueKind.Float ? rule.FloatValue : rule.IntValue;
            return (DialogueConditionComparison)rule.Comparison switch
            {
                DialogueConditionComparison.Equal => Math.Abs(actual - expected) <= 0.0001f,
                DialogueConditionComparison.NotEqual => Math.Abs(actual - expected) > 0.0001f,
                DialogueConditionComparison.Greater => actual > expected,
                DialogueConditionComparison.GreaterOrEqual => actual >= expected,
                DialogueConditionComparison.Less => actual < expected,
                DialogueConditionComparison.LessOrEqual => actual <= expected,
                _ => false,
            };
        }

        static bool IdEquals(string a, string b)
            => string.Equals(ContentId.NormalizeId(a), ContentId.NormalizeId(b), StringComparison.Ordinal);

        static BlobAssetReference<RuntimeWorldCellBlob> RequireWorldCellBlob(EntityManager entityManager)
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<RuntimeWorldCellBlobReference>());
            if (query.CalculateEntityCount() != 1)
                throw new InvalidOperationException("[VVardenfell][WorldCellBlob] dialogue filtering requires exactly one RuntimeWorldCellBlobReference singleton.");

            var reference = query.GetSingleton<RuntimeWorldCellBlobReference>();
            if (!reference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][WorldCellBlob] dialogue filtering requires runtime world cell blob.");
            return reference.Blob;
        }

    }
}
