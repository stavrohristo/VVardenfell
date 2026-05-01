using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.WorldState;

namespace VVardenfell.Runtime.MorrowindScript
{
    static class MorrowindDialogueResultScriptUtility
    {
        static readonly HashSet<string> s_UnsupportedResultWarnings = new(StringComparer.OrdinalIgnoreCase);

        public static bool ExecuteSupported(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ref MorrowindDialogueState dialogueState,
            ref MorrowindQuestJournalState questState,
            MorrowindTimeState time,
            DynamicBuffer<MorrowindKnownDialogueTopic> knownTopics,
            DynamicBuffer<MorrowindTopicJournalEntry> topicEntries,
            DynamicBuffer<MorrowindQuestJournalIndex> questStates,
            DynamicBuffer<MorrowindQuestJournalEntry> questEntries,
            DynamicBuffer<MorrowindDialogueChoice> choices,
            string response,
            string script,
            ref MorrowindDialogueSession session)
        {
            if (string.IsNullOrWhiteSpace(script))
                return false;

            bool goodbye = false;
            bool choicesReset = false;
            string[] lines = script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = StripComment(lines[i]).Trim();
                if (line.Length == 0)
                    continue;

                if (TryApplyJournalResult(contentDb, ref questState, time, questStates, questEntries, line))
                    continue;

                if (TryApplySetJournalIndexResult(contentDb, ref questState, time, questStates, questEntries, line))
                    continue;

                if (TryApplyAddTopicResult(contentDb, knownTopics, line))
                    continue;

                if (StartsWithCommand(line, "filljournal"))
                {
                    MorrowindDialogueUtility.TryFillJournal(
                        contentDb,
                        ref dialogueState,
                        ref questState,
                        time,
                        knownTopics,
                        topicEntries,
                        questStates,
                        questEntries,
                        session.SpeakerPlacedRefId,
                        session.SpeakerId);
                    continue;
                }

                if (TryApplyChoiceResult(line, choices, ref choicesReset))
                {
                    session.ChoiceActive = 1;
                    session.ChoiceDialogueIndex = session.SelectedTopicDialogueIndex;
                    continue;
                }

                if (TryApplyShowMapResult(line))
                    continue;

                if (TryApplyInventoryResult(contentDb, entityManager, ref session, line))
                    continue;

                if (TryApplyDispositionResult(entityManager, ref session, line))
                    continue;

                if (TryApplyPlayerFactionResult(contentDb, entityManager, ref session, line))
                    continue;

                if (TryApplyPlayerReputationResult(entityManager, line))
                    continue;

                if (StartsWithCommand(line, "goodbye"))
                {
                    goodbye = true;
                    continue;
                }

                if (s_UnsupportedResultWarnings.Add(line))
                    Debug.LogWarning($"[VVardenfell][Dialogue] unsupported V1 dialogue result command: '{line}'.");
            }

            MorrowindDialogueUtility.AddTopicsFromResponse(contentDb, knownTopics, response, entityManager, session.SpeakerEntity, session.SpeakerActor);
            return goodbye;
        }

        static bool TryApplyChoiceResult(string line, DynamicBuffer<MorrowindDialogueChoice> choices, ref bool choicesReset)
        {
            if (!StartsWithCommand(line, "choice"))
                return false;

            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length < 3 || tokens.Length % 2 == 0)
                return false;

            if (!choicesReset)
            {
                choices.Clear();
                choicesReset = true;
            }

            for (int i = 1; i < tokens.Length; i += 2)
            {
                if (!int.TryParse(tokens[i + 1], out int value))
                    return false;

                choices.Add(new MorrowindDialogueChoice
                {
                    Value = value,
                    Text = RuntimeFixedStringUtility.ToFixed512OrDefault(tokens[i]),
                });
            }

            return true;
        }

        static bool TryApplyPlayerReputationResult(EntityManager entityManager, string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 2 || !int.TryParse(tokens[1], out int value))
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(target, "player", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(command, "modreputation", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadWrite<ActorIdentitySet>());
            if (query.IsEmptyIgnoreFilter)
                return false;

            var entity = query.GetSingletonEntity();
            var identity = entityManager.GetComponentData<ActorIdentitySet>(entity);
            identity.Reputation += value;
            entityManager.SetComponentData(entity, identity);
            return true;
        }

        static bool TryApplyPlayerFactionResult(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.IsNullOrWhiteSpace(target))
                return false;

            bool modRep = string.Equals(command, "modpcfacrep", StringComparison.OrdinalIgnoreCase);
            bool raiseRank = string.Equals(command, "pcraiserank", StringComparison.OrdinalIgnoreCase);
            bool joinFaction = string.Equals(command, "pcjoinfaction", StringComparison.OrdinalIgnoreCase);
            if (!modRep && !raiseRank && !joinFaction)
                return false;

            if (!TryResolveFactionArgument(contentDb, session.SpeakerActor, tokens, modRep, out int factionIndex, out int value))
                return false;

            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadWrite<PlayerFactionMembership>());
            if (query.IsEmptyIgnoreFilter)
                return false;

            var factions = entityManager.GetBuffer<PlayerFactionMembership>(query.GetSingletonEntity());
            int index = FindPlayerFactionIndex(factions, factionIndex);
            if (modRep)
            {
                if (index < 0)
                {
                    factions.Add(new PlayerFactionMembership
                    {
                        FactionIndex = factionIndex,
                        Rank = -1,
                        Reputation = value,
                    });
                }
                else
                {
                    var membership = factions[index];
                    membership.Reputation += value;
                    factions[index] = membership;
                }

                return true;
            }

            if (index < 0)
            {
                factions.Add(new PlayerFactionMembership
                {
                    FactionIndex = factionIndex,
                    Rank = 0,
                    Joined = 1,
                });
                return true;
            }

            if (joinFaction)
            {
                var membership = factions[index];
                membership.Joined = 1;
                if (membership.Rank < 0)
                    membership.Rank = 0;
                factions[index] = membership;
                return true;
            }

            if (raiseRank)
            {
                var membership = factions[index];
                membership.Joined = 1;
                if (membership.Rank < 0)
                    membership.Rank = 0;
                else
                    membership.Rank += 1;
                factions[index] = membership;
            }

            return true;
        }

        static bool TryResolveFactionArgument(
            RuntimeContentDatabase contentDb,
            ActorDefHandle speakerActor,
            string[] tokens,
            bool valueThenFaction,
            out int factionIndex,
            out int value)
        {
            factionIndex = -1;
            value = 0;
            if (contentDb == null)
                return false;

            string factionId;
            if (valueThenFaction)
            {
                if ((tokens.Length != 2 && tokens.Length != 3) || !int.TryParse(tokens[1], out value))
                    return false;

                factionId = tokens.Length == 3 ? tokens[2] : (speakerActor.IsValid ? contentDb.Get(speakerActor).FactionId : string.Empty);
            }
            else
            {
                if (tokens.Length > 2)
                    return false;
                factionId = tokens.Length == 2 ? tokens[1] : (speakerActor.IsValid ? contentDb.Get(speakerActor).FactionId : string.Empty);
            }

            if (string.IsNullOrWhiteSpace(factionId)
                || !contentDb.TryGetFactionHandle(factionId, out var factionHandle)
                || !factionHandle.IsValid)
            {
                return false;
            }

            factionIndex = factionHandle.Index;
            return true;
        }

        static int FindPlayerFactionIndex(DynamicBuffer<PlayerFactionMembership> factions, int factionIndex)
        {
            for (int i = 0; i < factions.Length; i++)
            {
                if (factions[i].FactionIndex == factionIndex)
                    return i;
            }

            return -1;
        }

        static bool TryApplyDispositionResult(
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 2 || !int.TryParse(tokens[1], out int value))
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.IsNullOrWhiteSpace(target))
                return false;

            bool mod = string.Equals(command, "moddisposition", StringComparison.OrdinalIgnoreCase);
            bool set = string.Equals(command, "setdisposition", StringComparison.OrdinalIgnoreCase);
            if (!mod && !set)
                return false;

            if (session.SpeakerEntity == Entity.Null
                || !entityManager.Exists(session.SpeakerEntity)
                || !entityManager.HasComponent<ActorDispositionState>(session.SpeakerEntity))
            {
                return true;
            }

            var disposition = entityManager.GetComponentData<ActorDispositionState>(session.SpeakerEntity);
            disposition.BaseDisposition = mod ? disposition.BaseDisposition + value : value;
            entityManager.SetComponentData(session.SpeakerEntity, disposition);
            return true;
        }

        static bool TryApplyInventoryResult(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            NormalizeSeparatedExplicitCommand(tokens, out tokens);
            if (tokens.Length != 3 || !int.TryParse(tokens[2], out int count))
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            bool add = string.Equals(command, "additem", StringComparison.OrdinalIgnoreCase);
            bool remove = string.Equals(command, "removeitem", StringComparison.OrdinalIgnoreCase);
            if (!add && !remove)
                return false;

            string itemId = tokens[1];
            if (string.Equals(target, "player", StringComparison.OrdinalIgnoreCase))
            {
                Entity playerInventoryEntity = WorldStateEntityQueryUtility.GetSingletonBufferOwner<PlayerInventoryItem>(entityManager);
                if (playerInventoryEntity == Entity.Null)
                    return false;

                var inventory = entityManager.GetBuffer<PlayerInventoryItem>(playerInventoryEntity);
                return add
                    ? InventoryMutationUtility.TryAddPlayerItem(contentDb, inventory, itemId, count)
                    : InventoryMutationUtility.TryRemovePlayerItem(contentDb, inventory, itemId, count);
            }

            if (!string.IsNullOrWhiteSpace(target))
                return false;

            if (session.SpeakerEntity == Entity.Null
                || !entityManager.Exists(session.SpeakerEntity)
                || !entityManager.HasBuffer<ActorInventoryItem>(session.SpeakerEntity))
            {
                return false;
            }

            var actorInventory = entityManager.GetBuffer<ActorInventoryItem>(session.SpeakerEntity);
            uint resolutionSeed = session.SpeakerPlacedRefId != 0u
                ? session.SpeakerPlacedRefId
                : unchecked((uint)session.SpeakerEntity.Index + 1u);
            return add
                ? InventoryMutationUtility.TryAddActorItem(contentDb, actorInventory, itemId, count, resolutionSeed)
                : InventoryMutationUtility.TryRemoveActorItem(contentDb, actorInventory, itemId, count);
        }

        static bool TryApplyShowMapResult(string line)
        {
            if (!StartsWithCommand(line, "showmap"))
                return false;

            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 2 || string.IsNullOrWhiteSpace(tokens[1]))
                return false;

            GlobalMapPresentationCache.AddVisitedLocationsByCellNamePrefix(tokens[1]);
            return true;
        }

        static bool TryApplyJournalResult(
            RuntimeContentDatabase contentDb,
            ref MorrowindQuestJournalState questState,
            MorrowindTimeState time,
            DynamicBuffer<MorrowindQuestJournalIndex> questStates,
            DynamicBuffer<MorrowindQuestJournalEntry> questEntries,
            string line)
        {
            if (!StartsWithCommand(line, "journal"))
                return false;

            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 3)
                return false;

            string id = tokens[1].Trim('"');
            if (!int.TryParse(tokens[2], out int stage)
                || !contentDb.TryGetDialogueHandle(id, out var handle)
                || !handle.IsValid)
                return false;

            ref readonly var dialogue = ref contentDb.Get(handle);
            if (dialogue.Type != DialogueDefType.Journal)
                return false;

            int infoIndex = ResolveJournalInfoIndex(contentDb, handle.Index, stage, out byte questStatus);
            return MorrowindQuestJournalUtility.TryApplyRequest(
                contentDb,
                ref questState,
                time,
                questStates,
                questEntries,
                new MorrowindQuestJournalRequest
                {
                    DialogueIndex = handle.Index,
                    JournalIndex = stage,
                    InfoIndex = infoIndex,
                    QuestStatus = questStatus,
                    Operation = (byte)MorrowindQuestJournalRequestOperation.Journal,
                });
        }

        static bool TryApplySetJournalIndexResult(
            RuntimeContentDatabase contentDb,
            ref MorrowindQuestJournalState questState,
            MorrowindTimeState time,
            DynamicBuffer<MorrowindQuestJournalIndex> questStates,
            DynamicBuffer<MorrowindQuestJournalEntry> questEntries,
            string line)
        {
            if (!StartsWithCommand(line, "setjournalindex"))
                return false;

            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 3)
                return false;

            string id = tokens[1].Trim('"');
            if (!int.TryParse(tokens[2], out int stage)
                || !contentDb.TryGetDialogueHandle(id, out var handle)
                || !handle.IsValid)
                return false;

            return MorrowindQuestJournalUtility.TryApplyRequest(
                contentDb,
                ref questState,
                time,
                questStates,
                questEntries,
                new MorrowindQuestJournalRequest
                {
                    DialogueIndex = handle.Index,
                    JournalIndex = stage,
                    InfoIndex = -1,
                    Operation = (byte)MorrowindQuestJournalRequestOperation.SetIndex,
                });
        }

        static bool TryApplyAddTopicResult(RuntimeContentDatabase contentDb, DynamicBuffer<MorrowindKnownDialogueTopic> knownTopics, string line)
        {
            if (!StartsWithCommand(line, "addtopic"))
                return false;

            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 2)
                return false;

            string id = tokens[1].Trim('"');
            return contentDb.TryGetDialogueHandle(id, out var handle)
                   && handle.IsValid
                   && MorrowindDialogueUtility.TryAddTopic(contentDb, knownTopics, handle.Index);
        }

        static int ResolveJournalInfoIndex(RuntimeContentDatabase contentDb, int dialogueIndex, int stage, out byte questStatus)
        {
            questStatus = 0;
            ref readonly var dialogue = ref contentDb.Data.Dialogues[dialogueIndex];
            int end = Math.Min(contentDb.DialogueInfoCount, dialogue.FirstInfoIndex + dialogue.InfoCount);
            for (int i = dialogue.FirstInfoIndex; i < end; i++)
            {
                ref readonly var info = ref contentDb.Data.DialogueInfos[i];
                if (info.DispositionOrJournalIndex != stage)
                    continue;

                questStatus = info.QuestStatus;
                return i;
            }

            return -1;
        }

        static string StripComment(string line)
        {
            int semicolon = line.IndexOf(';');
            return semicolon >= 0 ? line.Substring(0, semicolon) : line;
        }

        static bool StartsWithCommand(string line, string command)
            => line.Length >= command.Length
               && string.Compare(line, 0, command, 0, command.Length, StringComparison.OrdinalIgnoreCase) == 0
               && (line.Length == command.Length || char.IsWhiteSpace(line[command.Length]) || line[command.Length] == ',');

        static void ParseTargetCommand(string token, out string target, out string command)
        {
            int arrow = token.IndexOf("->", StringComparison.Ordinal);
            if (arrow < 0)
            {
                target = string.Empty;
                command = token;
                return;
            }

            target = token.Substring(0, arrow).Trim().Trim('"');
            command = token.Substring(arrow + 2).Trim();
        }

        static void NormalizeSeparatedExplicitCommand(string[] tokens, out string[] normalized)
        {
            normalized = tokens;
            if (tokens == null || tokens.Length < 2 || !tokens[0].EndsWith("->", StringComparison.Ordinal))
                return;

            normalized = new string[tokens.Length - 1];
            normalized[0] = tokens[0] + tokens[1];
            for (int i = 2; i < tokens.Length; i++)
                normalized[i - 1] = tokens[i];
        }

        static string[] SplitCommandTokens(string line)
        {
            var tokens = new List<string>();
            var current = new System.Text.StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"')
                {
                    quoted = !quoted;
                    continue;
                }

                if (!quoted && (char.IsWhiteSpace(ch) || ch == ','))
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Length = 0;
                    }
                    continue;
                }

                current.Append(ch);
            }

            if (current.Length > 0)
                tokens.Add(current.ToString());
            return tokens.ToArray();
        }
    }
}
