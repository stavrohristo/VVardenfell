using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.AI;
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
            DynamicBuffer<MorrowindFactionReactionOverride> factionReactionOverrides,
            DynamicBuffer<MorrowindScriptStartRequest> scriptStartRequests,
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

                if (TryApplyClearInfoActorResult(contentDb, topicEntries, ref session, line))
                    continue;

                if (TryApplyInventoryResult(contentDb, entityManager, ref session, line))
                    continue;

                if (TryApplyDispositionResult(contentDb, entityManager, ref session, line))
                    continue;

                if (TryApplyPlayerFactionResult(contentDb, entityManager, ref session, line))
                    continue;

                if (TryApplyPlayerReputationResult(entityManager, line))
                    continue;

                if (TryApplyFactionReactionResult(contentDb, factionReactionOverrides, line))
                    continue;

                if (TryApplyStartScriptResult(contentDb, scriptStartRequests, ref session, line))
                    continue;

                if (TryApplySetResult(contentDb, entityManager, ref session, line))
                    continue;

                if (TryApplyActorAiSettingResult(contentDb, entityManager, ref session, line))
                    continue;

                if (TryApplyCombatTargetResult(contentDb, entityManager, ref session, line))
                    continue;

                if (TryApplyAiWanderResult(contentDb, entityManager, ref session, line))
                    continue;

                if (TryApplyAiTravelResult(contentDb, entityManager, ref session, line))
                    continue;

                if (TryApplyAiFollowResult(contentDb, entityManager, ref session, line))
                    continue;

                if (TryApplyAiFollowCellResult(contentDb, entityManager, ref session, line))
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

        static bool TryApplyStartScriptResult(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<MorrowindScriptStartRequest> scriptStartRequests,
            ref MorrowindDialogueSession session,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 2)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "startscript", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(target))
                return false;

            string scriptId = tokens[1].Trim('"');
            if (contentDb == null
                || !contentDb.TryGetMorrowindScriptProgramHandle(scriptId, out var programHandle)
                || !programHandle.IsValid)
            {
                return false;
            }

            ref readonly var program = ref contentDb.Get(programHandle);
            if (program.Status != (byte)MorrowindScriptProgramStatus.Compiled)
                return false;

            scriptStartRequests.Add(new MorrowindScriptStartRequest
            {
                Program = programHandle,
                ProgramIndex = programHandle.Index,
                TargetEntity = session.SpeakerEntity,
                TargetPlacedRefId = session.SpeakerPlacedRefId,
            });
            return true;
        }

        static bool TryApplyFactionReactionResult(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<MorrowindFactionReactionOverride> factionReactionOverrides,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 4 || !int.TryParse(tokens[3], out int value))
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.IsNullOrWhiteSpace(target))
                return false;

            bool mod = string.Equals(command, "modfactionreaction", StringComparison.OrdinalIgnoreCase);
            bool set = string.Equals(command, "setfactionreaction", StringComparison.OrdinalIgnoreCase);
            if (!mod && !set)
                return false;

            if (contentDb == null
                || !contentDb.TryGetFactionHandle(tokens[1], out var source)
                || !source.IsValid
                || !contentDb.TryGetFactionHandle(tokens[2], out var targetFaction)
                || !targetFaction.IsValid)
            {
                return false;
            }

            return mod
                ? MorrowindDialogueUtility.TryModFactionReaction(contentDb, factionReactionOverrides, source.Index, targetFaction.Index, value)
                : MorrowindDialogueUtility.TrySetFactionReaction(contentDb, factionReactionOverrides, source.Index, targetFaction.Index, value);
        }

        static bool TryApplyClearInfoActorResult(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<MorrowindTopicJournalEntry> topicEntries,
            ref MorrowindDialogueSession session,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 1)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "clearinfoactor", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(target))
                return false;

            return MorrowindDialogueUtility.TryRemoveLastAddedTopicResponse(
                contentDb,
                topicEntries,
                session.SelectedTopicDialogueIndex,
                session.SpeakerId);
        }

        static bool TryApplySetResult(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 4
                || !string.Equals(tokens[0], "set", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(tokens[2], "to", StringComparison.OrdinalIgnoreCase)
                || tokens[1].IndexOf("->", StringComparison.Ordinal) >= 0
                || !TryParseNumericLiteral(tokens[3], out float value))
            {
                return false;
            }

            string target = tokens[1];
            if (TryApplyLocalSet(contentDb, entityManager, ref session, target, value, out bool localResolved))
                return true;

            if (localResolved)
                return true;

            return TryApplyGlobalSet(contentDb, entityManager, target, value);
        }

        static bool TryApplyActorAiSettingResult(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 2 || !int.TryParse(tokens[1], out int value))
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!TryResolveActorAiSettingCommand(command, out ActorAiSettingKind kind, out bool isMod)
                || !TryResolveAiCommandTarget(contentDb, entityManager, ref session, target, out Entity targetEntity, out _))
            {
                return false;
            }

            if (!entityManager.HasComponent<ActorAiSettingsState>(targetEntity))
            {
                return true;
            }

            var settings = entityManager.GetComponentData<ActorAiSettingsState>(targetEntity);
            switch (kind)
            {
                case ActorAiSettingKind.Hello:
                    settings.Hello = isMod ? settings.Hello + value : value;
                    break;
                case ActorAiSettingKind.Fight:
                    settings.Fight = isMod ? settings.Fight + value : value;
                    break;
                case ActorAiSettingKind.Flee:
                    settings.Flee = isMod ? settings.Flee + value : value;
                    break;
                case ActorAiSettingKind.Alarm:
                    settings.Alarm = isMod ? settings.Alarm + value : value;
                    break;
                default:
                    return false;
            }

            entityManager.SetComponentData(targetEntity, settings);
            return true;
        }

        static bool TryResolveActorAiSettingCommand(string command, out ActorAiSettingKind kind, out bool isMod)
        {
            kind = ActorAiSettingKind.None;
            isMod = false;
            if (string.Equals(command, "sethello", StringComparison.OrdinalIgnoreCase))
            {
                kind = ActorAiSettingKind.Hello;
                return true;
            }

            if (string.Equals(command, "modhello", StringComparison.OrdinalIgnoreCase))
            {
                kind = ActorAiSettingKind.Hello;
                isMod = true;
                return true;
            }

            if (string.Equals(command, "setfight", StringComparison.OrdinalIgnoreCase))
            {
                kind = ActorAiSettingKind.Fight;
                return true;
            }

            if (string.Equals(command, "modfight", StringComparison.OrdinalIgnoreCase))
            {
                kind = ActorAiSettingKind.Fight;
                isMod = true;
                return true;
            }

            if (string.Equals(command, "setflee", StringComparison.OrdinalIgnoreCase))
            {
                kind = ActorAiSettingKind.Flee;
                return true;
            }

            if (string.Equals(command, "modflee", StringComparison.OrdinalIgnoreCase))
            {
                kind = ActorAiSettingKind.Flee;
                isMod = true;
                return true;
            }

            if (string.Equals(command, "setalarm", StringComparison.OrdinalIgnoreCase))
            {
                kind = ActorAiSettingKind.Alarm;
                return true;
            }

            if (string.Equals(command, "modalarm", StringComparison.OrdinalIgnoreCase))
            {
                kind = ActorAiSettingKind.Alarm;
                isMod = true;
                return true;
            }

            return false;
        }

        static bool TryApplyCombatTargetResult(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string commandTarget, out string command);
            bool startCombat = string.Equals(command, "startcombat", StringComparison.OrdinalIgnoreCase);
            bool stopCombat = string.Equals(command, "stopcombat", StringComparison.OrdinalIgnoreCase);
            if (!startCombat && !stopCombat)
                return false;

            if (!TryResolveAiCommandTarget(contentDb, entityManager, ref session, commandTarget, out Entity actorEntity, out uint actorPlacedRefId))
                return false;

            if (startCombat)
            {
                if (tokens.Length != 2
                    || !TryResolveCombatTarget(contentDb, entityManager, ref session, tokens[1], out Entity combatTargetEntity, out uint combatTargetPlacedRefId))
                {
                    return false;
                }

                return MorrowindCombatTargetUtility.TryStartCombat(
                    contentDb,
                    entityManager,
                    actorEntity,
                    actorPlacedRefId,
                    combatTargetEntity,
                    combatTargetPlacedRefId);
            }

            if (tokens.Length != 1)
                return false;

            return MorrowindCombatTargetUtility.TryStopCombat(entityManager, actorEntity);
        }

        static bool TryApplyAiWanderResult(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length < 4)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "aiwander", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!TryResolveAiCommandTarget(contentDb, entityManager, ref session, target, out Entity targetEntity, out uint targetPlacedRefId))
                return false;

            if (!float.TryParse(NormalizeToken(tokens[1]), NumberStyles.Float, CultureInfo.InvariantCulture, out float range))
                return false;

            for (int i = 2; i < tokens.Length; i++)
            {
                if (!float.TryParse(NormalizeToken(tokens[i]), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    return false;
            }

            return MorrowindScriptAiPackageUtility.TryApplyRequest(
                contentDb,
                entityManager,
                targetEntity,
                new MorrowindScriptAiPackageRequest
                {
                    TargetEntity = targetEntity,
                    TargetPlacedRefId = targetPlacedRefId,
                    PackageType = (byte)MorrowindScriptAiPackageRequestType.Wander,
                    ShouldRepeat = ResolveAiWanderRepeat(tokens),
                    WanderRadius = math.max(0f, range) * WorldScale.MwUnitsToMeters,
                    IdleSeconds = 1.5f,
                });
        }

        static bool TryApplyAiTravelResult(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length < 4)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "aitravel", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!TryResolveAiCommandTarget(contentDb, entityManager, ref session, target, out Entity targetEntity, out uint targetPlacedRefId))
                return false;

            if (!float.TryParse(NormalizeToken(tokens[1]), NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(NormalizeToken(tokens[2]), NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                || !float.TryParse(NormalizeToken(tokens[3]), NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                return false;
            }

            for (int i = 4; i < tokens.Length; i++)
            {
                if (!float.TryParse(NormalizeToken(tokens[i]), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    return false;
            }

            return MorrowindScriptAiPackageUtility.TryApplyRequest(
                contentDb,
                entityManager,
                targetEntity,
                new MorrowindScriptAiPackageRequest
                {
                    TargetEntity = targetEntity,
                    TargetPlacedRefId = targetPlacedRefId,
                    PackageType = (byte)MorrowindScriptAiPackageRequestType.Travel,
                    ShouldRepeat = tokens.Length > 4 ? (byte)1 : (byte)0,
                    TargetPosition = new float3(x, z, y) * WorldScale.MwUnitsToMeters,
                    IdleSeconds = 0.5f,
                });
        }

        static bool TryApplyAiFollowResult(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length < 6)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "aifollow", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!TryResolveAiCommandTarget(contentDb, entityManager, ref session, target, out Entity targetEntity, out uint targetPlacedRefId)
                || !IsPlayerTarget(tokens[1]))
                return false;

            if (!TryParseAiFollowNumbers(tokens, 2, out _, out float x, out float y, out float z))
                return false;

            for (int i = 6; i < tokens.Length; i++)
            {
                if (!float.TryParse(NormalizeToken(tokens[i]), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    return false;
            }

            return TryApplyFollowPackage(contentDb, entityManager, targetEntity, targetPlacedRefId, new float3(x, z, y) * WorldScale.MwUnitsToMeters, 0UL, tokens.Length > 6);
        }

        static bool TryApplyAiFollowCellResult(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length < 7)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "aifollowcell", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!TryResolveAiCommandTarget(contentDb, entityManager, ref session, target, out Entity targetEntity, out uint targetPlacedRefId)
                || !IsPlayerTarget(tokens[1]))
                return false;

            string cellId = NormalizeToken(tokens[2]).Trim('"');
            if (string.IsNullOrWhiteSpace(cellId)
                || !TryParseAiFollowNumbers(tokens, 3, out _, out float x, out float y, out float z))
            {
                return false;
            }

            for (int i = 7; i < tokens.Length; i++)
            {
                if (!float.TryParse(NormalizeToken(tokens[i]), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    return false;
            }

            return TryApplyFollowPackage(contentDb, entityManager, targetEntity, targetPlacedRefId, new float3(x, z, y) * WorldScale.MwUnitsToMeters, HashInteriorCellId(cellId), tokens.Length > 7);
        }

        static bool TryApplyFollowPackage(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity targetEntity,
            uint targetPlacedRefId,
            float3 destination,
            ulong destinationInteriorCellHash,
            bool repeat)
        {
            Entity player = ResolvePlayerEntity(entityManager);
            if (player == Entity.Null)
                return false;

            return MorrowindScriptAiPackageUtility.TryApplyRequest(
                contentDb,
                entityManager,
                targetEntity,
                new MorrowindScriptAiPackageRequest
                {
                    TargetEntity = targetEntity,
                    TargetPlacedRefId = targetPlacedRefId,
                    FollowTargetEntity = player,
                    PackageType = (byte)MorrowindScriptAiPackageRequestType.Follow,
                    ShouldRepeat = repeat ? (byte)1 : (byte)0,
                    AllowPartial = 1,
                    TargetPosition = destination,
                    DestinationInteriorCellHash = destinationInteriorCellHash,
                    FollowDistance = 256f * WorldScale.MwUnitsToMeters,
                    IdleSeconds = 0.5f,
                });
        }

        static bool TryResolveAiCommandTarget(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string target,
            out Entity targetEntity,
            out uint targetPlacedRefId)
        {
            targetEntity = Entity.Null;
            targetPlacedRefId = 0u;
            if (string.IsNullOrWhiteSpace(target))
            {
                targetEntity = session.SpeakerEntity;
                targetPlacedRefId = session.SpeakerPlacedRefId;
                return targetEntity != Entity.Null
                       && entityManager.Exists(targetEntity)
                       && entityManager.HasComponent<ActorSpawnSource>(targetEntity);
            }

            if (contentDb == null)
                return false;

            string normalizedTarget = ContentId.NormalizeId(target);
            if (string.IsNullOrEmpty(normalizedTarget))
                return false;

            Entity matchEntity = Entity.Null;
            uint matchPlacedRefId = 0u;
            int matchCount = 0;
            using var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ActorSpawnSource>(),
                ComponentType.ReadOnly<PlacedRefIdentity>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var sources = query.ToComponentDataArray<ActorSpawnSource>(Allocator.Temp);
            using var placedRefs = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                ActorDefHandle actorHandle = sources[i].Definition;
                if (!actorHandle.IsValid)
                    continue;

                ref readonly var actor = ref contentDb.Get(actorHandle);
                if (!ActorIdMatches(actor, normalizedTarget))
                    continue;

                matchCount++;
                matchEntity = entities[i];
                matchPlacedRefId = placedRefs[i].Value;
                if (matchCount > 1)
                    return false;
            }

            if (matchCount != 1)
                return false;

            targetEntity = matchEntity;
            targetPlacedRefId = matchPlacedRefId;
            return true;
        }

        static bool ActorIdMatches(in ActorDef actor, string normalizedTarget)
            => string.Equals(ContentId.NormalizeId(actor.Id), normalizedTarget, StringComparison.OrdinalIgnoreCase)
               || string.Equals(ContentId.NormalizeId(actor.OriginalId), normalizedTarget, StringComparison.OrdinalIgnoreCase);

        static bool TryResolveCombatTarget(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string target,
            out Entity targetEntity,
            out uint targetPlacedRefId)
        {
            targetEntity = Entity.Null;
            targetPlacedRefId = 0u;
            if (IsPlayerTarget(target))
            {
                targetEntity = ResolvePlayerEntity(entityManager);
                if (targetEntity == Entity.Null)
                    return false;

                targetPlacedRefId = entityManager.HasComponent<PlacedRefIdentity>(targetEntity)
                    ? entityManager.GetComponentData<PlacedRefIdentity>(targetEntity).Value
                    : 0u;
                return true;
            }

            return TryResolveAiCommandTarget(contentDb, entityManager, ref session, target, out targetEntity, out targetPlacedRefId);
        }

        static bool TryApplyLocalSet(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string target,
            float value,
            out bool localResolved)
        {
            localResolved = false;
            if (contentDb == null
                || !session.SpeakerActor.IsValid
                || session.SpeakerEntity == Entity.Null
                || !entityManager.Exists(session.SpeakerEntity))
            {
                return false;
            }

            ref readonly var actor = ref contentDb.Get(session.SpeakerActor);
            if (string.IsNullOrWhiteSpace(actor.ScriptId)
                || !contentDb.TryGetMorrowindScriptProgramHandle(actor.ScriptId, out var programHandle)
                || !programHandle.IsValid)
            {
                return false;
            }

            var localsDef = contentDb.GetMorrowindScriptLocals(programHandle);
            int localIndex = -1;
            byte valueKind = 0;
            for (int i = 0; i < localsDef.Length; i++)
            {
                if (string.Equals(localsDef[i].Name, target, StringComparison.OrdinalIgnoreCase))
                {
                    localIndex = i;
                    valueKind = localsDef[i].ValueKind;
                    break;
                }
            }

            if (localIndex < 0)
                return false;

            localResolved = true;
            if (!entityManager.HasBuffer<MorrowindScriptLocalValue>(session.SpeakerEntity))
                throw new InvalidOperationException($"[VVardenfell][Dialogue] speaker '{actor.Id}' has script '{actor.ScriptId}' local '{target}', but no runtime local buffer.");

            var locals = entityManager.GetBuffer<MorrowindScriptLocalValue>(session.SpeakerEntity);
            if ((uint)localIndex >= (uint)locals.Length)
                throw new InvalidOperationException($"[VVardenfell][Dialogue] speaker '{actor.Id}' local buffer is too small for script '{actor.ScriptId}' local '{target}'.");

            locals[localIndex] = BuildScriptValue(value, valueKind);
            return true;
        }

        static bool TryApplyGlobalSet(RuntimeContentDatabase contentDb, EntityManager entityManager, string target, float value)
        {
            if (contentDb == null
                || !contentDb.TryGetGlobalHandle(target, out var globalHandle)
                || !globalHandle.IsValid)
            {
                return false;
            }

            using var query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<MorrowindScriptGlobalValue>());
            if (query.IsEmptyIgnoreFilter)
                throw new InvalidOperationException($"[VVardenfell][Dialogue] global '{target}' was set before MWScript globals were bootstrapped.");

            var globals = entityManager.GetBuffer<MorrowindScriptGlobalValue>(query.GetSingletonEntity());
            if ((uint)globalHandle.Index >= (uint)globals.Length)
                throw new InvalidOperationException($"[VVardenfell][Dialogue] global buffer is too small for '{target}'.");

            ref readonly var global = ref contentDb.GetGlobal(globalHandle);
            globals[globalHandle.Index] = BuildGlobalValue(value, ResolveGlobalKind(global));
            return true;
        }

        static MorrowindScriptLocalValue BuildScriptValue(float value, byte valueKind)
        {
            if (valueKind == (byte)MorrowindScriptValueKind.Float)
            {
                return new MorrowindScriptLocalValue
                {
                    FloatValue = value,
                    IntValue = (int)value,
                    ValueKind = valueKind,
                };
            }

            int intValue = (int)value;
            return new MorrowindScriptLocalValue
            {
                IntValue = intValue,
                FloatValue = intValue,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            };
        }

        static MorrowindScriptGlobalValue BuildGlobalValue(float value, byte valueKind)
        {
            if (valueKind == (byte)MorrowindScriptValueKind.Float)
            {
                return new MorrowindScriptGlobalValue
                {
                    FloatValue = value,
                    IntValue = (int)value,
                    ValueKind = valueKind,
                };
            }

            int intValue = (int)value;
            return new MorrowindScriptGlobalValue
            {
                IntValue = intValue,
                FloatValue = intValue,
                ValueKind = (byte)MorrowindScriptValueKind.Integer,
            };
        }

        static byte ResolveGlobalKind(in GenericRecordDef global)
        {
            if (!string.IsNullOrWhiteSpace(global.Name) && global.Name[0] == 'f')
                return (byte)MorrowindScriptValueKind.Float;

            return (byte)MorrowindScriptValueKind.Integer;
        }

        static bool TryParseNumericLiteral(string token, out float value)
            => float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

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
            bool expell = string.Equals(command, "pcexpell", StringComparison.OrdinalIgnoreCase);
            bool clearExpelled = string.Equals(command, "pcclearexpelled", StringComparison.OrdinalIgnoreCase);
            if (!modRep && !raiseRank && !joinFaction && !expell && !clearExpelled)
                return false;

            if (!TryResolveFactionArgument(contentDb, session.SpeakerActor, tokens, modRep, out int factionIndex, out int value))
                return false;

            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadWrite<PlayerFactionMembership>());
            if (query.IsEmptyIgnoreFilter)
                return false;

            var factions = entityManager.GetBuffer<PlayerFactionMembership>(query.GetSingletonEntity());
            int index = FindPlayerFactionIndex(factions, factionIndex);
            if (expell)
            {
                if (index < 0)
                {
                    factions.Add(new PlayerFactionMembership
                    {
                        FactionIndex = factionIndex,
                        Rank = -1,
                        Expelled = 1,
                    });
                }
                else
                {
                    var membership = factions[index];
                    membership.Expelled = 1;
                    factions[index] = membership;
                }

                return true;
            }

            if (clearExpelled)
            {
                if (index >= 0)
                {
                    var membership = factions[index];
                    membership.Expelled = 0;
                    factions[index] = membership;
                }

                return true;
            }

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
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 2 || !int.TryParse(tokens[1], out int value))
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            bool mod = string.Equals(command, "moddisposition", StringComparison.OrdinalIgnoreCase);
            bool set = string.Equals(command, "setdisposition", StringComparison.OrdinalIgnoreCase);
            if (!mod && !set)
                return false;

            if (!TryResolveAiCommandTarget(contentDb, entityManager, ref session, target, out Entity targetEntity, out _))
                return false;

            if (!entityManager.HasComponent<ActorDispositionState>(targetEntity))
            {
                return true;
            }

            var disposition = entityManager.GetComponentData<ActorDispositionState>(targetEntity);
            disposition.BaseDisposition = mod ? disposition.BaseDisposition + value : value;
            entityManager.SetComponentData(targetEntity, disposition);
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

        static byte ResolveAiWanderRepeat(string[] tokens)
        {
            int optionalArgCount = tokens.Length - 4;
            if (optionalArgCount <= 0)
                return 0;
            if (optionalArgCount <= 8)
                return 1;

            return int.TryParse(NormalizeToken(tokens[12]), NumberStyles.Integer, CultureInfo.InvariantCulture, out int repeat) && repeat != 0
                ? (byte)1
                : (byte)0;
        }

        static string NormalizeToken(string token)
            => (token ?? string.Empty).Trim().Trim(',');

        static bool TryParseAiFollowNumbers(
            string[] tokens,
            int start,
            out float duration,
            out float x,
            out float y,
            out float z)
        {
            duration = 0f;
            x = 0f;
            y = 0f;
            z = 0f;
            return float.TryParse(NormalizeToken(tokens[start]), NumberStyles.Float, CultureInfo.InvariantCulture, out duration)
                   && float.TryParse(NormalizeToken(tokens[start + 1]), NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                   && float.TryParse(NormalizeToken(tokens[start + 2]), NumberStyles.Float, CultureInfo.InvariantCulture, out y)
                   && float.TryParse(NormalizeToken(tokens[start + 3]), NumberStyles.Float, CultureInfo.InvariantCulture, out z);
        }

        static bool IsPlayerTarget(string target)
            => string.Equals(NormalizeToken(target).Trim('"'), "player", StringComparison.OrdinalIgnoreCase);

        static Entity ResolvePlayerEntity(EntityManager entityManager)
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>());
            return query.IsEmptyIgnoreFilter ? Entity.Null : query.GetSingletonEntity();
        }

        static ulong HashInteriorCellId(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0UL;

            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c >= 'A' && c <= 'Z')
                    c = (char)(c + 32);
                hash ^= c;
                hash *= 1099511628211UL;
            }

            return hash == 0UL ? 1UL : hash;
        }

        enum ActorAiSettingKind : byte
        {
            None = 0,
            Hello = 1,
            Fight = 2,
            Flee = 3,
            Alarm = 4,
        }
    }
}
