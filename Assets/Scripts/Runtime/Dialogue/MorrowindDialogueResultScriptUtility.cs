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
using VVardenfell.Runtime.Animation;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Magic;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.WorldState;
using VVardenfell.Runtime.WorldRefs;
using static VVardenfell.Core.MorrowindCommandTextUtility;

namespace VVardenfell.Runtime.MorrowindScript
{
    static class MorrowindDialogueResultScriptUtility
    {
        static readonly HashSet<string> s_UnsupportedResultWarnings = new(StringComparer.OrdinalIgnoreCase);

        public static bool ExecuteSupported(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ActiveExplicitRefLookup activeExplicitRefs,
            ref MorrowindDialogueState dialogueState,
            ref MorrowindQuestJournalState questState,
            MorrowindTimeState time,
            DynamicBuffer<MorrowindKnownDialogueTopic> knownTopics,
            DynamicBuffer<MorrowindTopicJournalEntry> topicEntries,
            DynamicBuffer<MorrowindFactionReactionOverride> factionReactionOverrides,
            DynamicBuffer<MorrowindScriptStartRequest> scriptStartRequests,
            DynamicBuffer<MorrowindScriptRefStateRequest> refStateRequests,
            DynamicBuffer<MorrowindScriptTransformRequest> transformRequests,
            DynamicBuffer<MorrowindScriptJailRequest> jailRequests,
            DynamicBuffer<MorrowindScriptMovementFlagRequest> movementFlagRequests,
            DynamicBuffer<MorrowindScriptPlaceAtRequest> placeAtRequests,
            DynamicBuffer<ScriptedCastRequest> castRequests,
            DynamicBuffer<ActorSpellMutationRequest> actorSpellRequests,
            DynamicBuffer<ShellMessageBoxRequest> shellMessageBoxRequests,
            DynamicBuffer<GlobalMapRevealRequest> globalMapRevealRequests,
            DynamicBuffer<ActorForceGreetingRequest> forceGreetingRequests,
            DynamicBuffer<PlayerReputationMutationRequest> playerReputationRequests,
            DynamicBuffer<PlayerFactionMutationRequest> playerFactionRequests,
            DynamicBuffer<ActorFactionRankMutationRequest> actorFactionRequests,
            DynamicBuffer<MorrowindQuestJournalIndex> questStates,
            DynamicBuffer<MorrowindQuestJournalEntry> questEntries,
            DynamicBuffer<MorrowindDialogueChoice> choices,
            string response,
            string script,
            ref RuntimeShellState shell,
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

                if (TryApplyChoiceResult(line, choices, ref choicesReset, out bool hasChoices))
                {
                    if (hasChoices)
                    {
                        session.ChoiceActive = 1;
                        session.ChoiceDialogueIndex = session.SelectedTopicDialogueIndex;
                    }

                    continue;
                }

                if (TryApplyShowMapResult(globalMapRevealRequests, line))
                    continue;

                if (TryApplyMessageBoxResult(shellMessageBoxRequests, line))
                    continue;

                if (TryApplyClearInfoActorResult(contentDb, topicEntries, ref session, line))
                    continue;

                if (TryApplyRefStateResult(contentDb, activeExplicitRefs, refStateRequests, ref session, line))
                    continue;

                if (TryApplyMovementFlagResult(contentDb, entityManager, activeExplicitRefs, movementFlagRequests, ref session, line))
                    continue;

                if (TryApplyPlaceAtPCResult(contentDb, placeAtRequests, line))
                    continue;

                if (TryApplyPositionCellResult(contentDb, activeExplicitRefs, transformRequests, ref session, line))
                    continue;

                if (TryApplyInventoryResult(contentDb, entityManager, activeExplicitRefs, ref session, line))
                    continue;

                if (TryApplyActorSpellResult(contentDb, entityManager, ref session, actorSpellRequests, line))
                    continue;

                if (TryApplyScriptedCastResult(contentDb, entityManager, ref session, castRequests, line))
                    continue;

                if (TryApplyDispositionResult(contentDb, entityManager, ref session, line))
                    continue;

                if (TryApplyActorFactionResult(contentDb, entityManager, ref session, actorFactionRequests, line))
                    continue;

                if (TryApplyPlayerFactionResult(contentDb, ref session, playerFactionRequests, line))
                    continue;

                if (TryApplyPlayerReputationResult(playerReputationRequests, line))
                    continue;

                if (TryApplyPlayerCrimeResult(entityManager, line))
                    continue;

                if (TryApplyGotoJailResult(contentDb, entityManager, jailRequests, line))
                {
                    goodbye = true;
                    continue;
                }

                if (TryApplyPlayerSkillResult(entityManager, line))
                    continue;

                if (TryApplyPlayerAttributeResult(entityManager, line))
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

                if (TryApplyForceGreetingResult(entityManager, ref session, forceGreetingRequests, line))
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

        static bool TryApplyForceGreetingResult(
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            DynamicBuffer<ActorForceGreetingRequest> forceGreetingRequests,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 1)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "forcegreeting", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(target))
                return false;

            Entity speaker = session.SpeakerEntity;
            if (speaker == Entity.Null
                || !entityManager.Exists(speaker)
                || !entityManager.HasComponent<ActorSpawnSource>(speaker))
            {
                return false;
            }

            if (entityManager.HasComponent<PlacedRefRuntimeState>(speaker)
                && entityManager.GetComponentData<PlacedRefRuntimeState>(speaker).Disabled != 0)
            {
                return false;
            }

            forceGreetingRequests.Add(new ActorForceGreetingRequest
            {
                TargetEntity = speaker,
                TargetPlacedRefId = session.SpeakerPlacedRefId,
            });
            return true;
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

        static bool TryApplyRefStateResult(
            RuntimeContentDatabase contentDb,
            ActiveExplicitRefLookup activeExplicitRefs,
            DynamicBuffer<MorrowindScriptRefStateRequest> refStateRequests,
            ref MorrowindDialogueSession session,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 1)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            byte disabled;
            if (string.Equals(command, "enable", StringComparison.OrdinalIgnoreCase))
                disabled = 0;
            else if (string.Equals(command, "disable", StringComparison.OrdinalIgnoreCase))
                disabled = 1;
            else
                return false;

            Entity targetEntity = Entity.Null;
            uint targetPlacedRefId;
            if (string.IsNullOrWhiteSpace(target))
            {
                targetEntity = session.SpeakerEntity;
                targetPlacedRefId = session.SpeakerPlacedRefId;
            }
            else
            {
                if (!TryResolveExplicitRefTarget(contentDb, activeExplicitRefs, target, out targetEntity, out targetPlacedRefId))
                    return false;
            }

            if (targetPlacedRefId == 0u)
                return false;

            refStateRequests.Add(new MorrowindScriptRefStateRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Disabled = disabled,
            });
            return true;
        }

        static bool TryApplyMovementFlagResult(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ActiveExplicitRefLookup activeExplicitRefs,
            DynamicBuffer<MorrowindScriptMovementFlagRequest> movementFlagRequests,
            ref MorrowindDialogueSession session,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 1)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            bool enable;
            if (string.Equals(command, "forcesneak", StringComparison.OrdinalIgnoreCase))
                enable = true;
            else if (string.Equals(command, "clearforcesneak", StringComparison.OrdinalIgnoreCase))
                enable = false;
            else
                return false;

            Entity targetEntity = Entity.Null;
            uint targetPlacedRefId = 0u;
            if (string.IsNullOrWhiteSpace(target))
            {
                targetEntity = session.SpeakerEntity;
                targetPlacedRefId = session.SpeakerPlacedRefId;
            }
            else if (string.Equals(NormalizeToken(target).Trim('"'), "player", StringComparison.OrdinalIgnoreCase))
            {
                using var playerQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>());
                if (playerQuery.IsEmptyIgnoreFilter)
                    return false;

                targetEntity = playerQuery.GetSingletonEntity();
            }
            else
            {
                if (!TryResolveExplicitRefTarget(contentDb, activeExplicitRefs, target, out targetEntity, out targetPlacedRefId))
                    return false;
            }

            if (targetEntity == Entity.Null && targetPlacedRefId == 0u)
                return false;

            movementFlagRequests.Add(new MorrowindScriptMovementFlagRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                FlagKind = (byte)MorrowindScriptMovementFlagKind.ForceSneak,
                Enabled = enable ? (byte)1 : (byte)0,
            });
            return true;
        }

        static bool TryApplyPlaceAtPCResult(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<MorrowindScriptPlaceAtRequest> placeAtRequests,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "placeatpc", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(target) || tokens.Length != 5)
                return false;

            string contentId = tokens[1].Trim('"');
            if (!TryResolvePlaceAtContent(contentDb, contentId, out ContentReference content))
                return false;

            if (!int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
                || count < 0
                || !float.TryParse(tokens[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float distance)
                || !int.TryParse(tokens[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int direction)
                || direction < 0
                || direction > 3)
            {
                return false;
            }

            placeAtRequests.Add(new MorrowindScriptPlaceAtRequest
            {
                Content = content,
                Count = count,
                Distance = math.max(0f, distance) * WorldScale.MwUnitsToMeters,
                Direction = (byte)direction,
            });
            return true;
        }

        static bool TryResolvePlaceAtContent(
            RuntimeContentDatabase contentDb,
            string contentId,
            out ContentReference content)
        {
            content = default;
            if (contentDb == null
                || string.IsNullOrWhiteSpace(contentId)
                || !contentDb.TryResolvePlaceable(contentId, out content)
                || !contentDb.IsValid(content))
            {
                return false;
            }

            if (content.Kind == ContentReferenceKind.Actor)
            {
                ref readonly var actor = ref contentDb.Get(new ActorDefHandle { Value = content.HandleValue });
                return actor.Kind == ActorDefKind.Creature;
            }

            return content.Kind == ContentReferenceKind.Item || content.Kind == ContentReferenceKind.Light;
        }

        static bool TryResolveExplicitRefTarget(
            RuntimeContentDatabase contentDb,
            ActiveExplicitRefLookup activeExplicitRefs,
            string target,
            out Entity targetEntity,
            out uint targetPlacedRefId)
            => MorrowindRuntimeTargetResolver.TryResolveExplicitRefTarget(contentDb, activeExplicitRefs, target, out targetEntity, out targetPlacedRefId);

        static bool TryApplyPositionCellResult(
            RuntimeContentDatabase contentDb,
            ActiveExplicitRefLookup activeExplicitRefs,
            DynamicBuffer<MorrowindScriptTransformRequest> transformRequests,
            ref MorrowindDialogueSession session,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 6)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "positioncell", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!float.TryParse(NormalizeToken(tokens[1]), NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(NormalizeToken(tokens[2]), NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                || !float.TryParse(NormalizeToken(tokens[3]), NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
                || !float.TryParse(NormalizeToken(tokens[4]), NumberStyles.Float, CultureInfo.InvariantCulture, out float zRotMinutes))
            {
                return false;
            }

            string cellId = NormalizeToken(tokens[5]).Trim('"');
            ulong cellHash = HashInteriorCellId(cellId);
            if (cellHash == 0UL)
                return false;

            Entity targetEntity = Entity.Null;
            uint targetPlacedRefId;
            if (string.IsNullOrWhiteSpace(target))
            {
                targetEntity = session.SpeakerEntity;
                targetPlacedRefId = session.SpeakerPlacedRefId;
            }
            else
            {
                if (!TryResolveExplicitRefTarget(contentDb, activeExplicitRefs, target, out targetEntity, out targetPlacedRefId))
                    return false;
            }

            if (targetPlacedRefId == 0u)
                return false;

            transformRequests.Add(new MorrowindScriptTransformRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Position = new float3(x, z, y) * WorldScale.MwUnitsToMeters,
                Radians = math.radians(zRotMinutes / 60f),
                InteriorCellHash = cellHash,
                Operation = 2,
            });
            return true;
        }

        static bool TryApplySetResult(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (!TryParseSetResult(tokens, out string target, out SetExpression expression))
            {
                return false;
            }

            if (TryApplyLocalSet(contentDb, entityManager, ref session, target, expression, out bool localResolved))
                return true;

            if (localResolved)
                return true;

            return TryApplyGlobalSet(contentDb, entityManager, target, expression);
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

            if (tokens.Length != 1 && (tokens.Length != 2 || !IsPlayerTarget(tokens[1])))
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
                || !TryResolveAiFollowTarget(contentDb, entityManager, ref session, tokens[1], out Entity followTargetEntity, out uint followTargetPlacedRefId))
                return false;

            if (!TryParseAiFollowNumbers(tokens, 2, out _, out float x, out float y, out float z))
                return false;

            for (int i = 6; i < tokens.Length; i++)
            {
                if (!float.TryParse(NormalizeToken(tokens[i]), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    return false;
            }

            return TryApplyFollowPackage(
                contentDb,
                entityManager,
                targetEntity,
                targetPlacedRefId,
                followTargetEntity,
                followTargetPlacedRefId,
                new float3(x, z, y) * WorldScale.MwUnitsToMeters,
                0UL,
                tokens.Length > 6);
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
                || !TryResolveAiFollowTarget(contentDb, entityManager, ref session, tokens[1], out Entity followTargetEntity, out uint followTargetPlacedRefId))
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

            return TryApplyFollowPackage(
                contentDb,
                entityManager,
                targetEntity,
                targetPlacedRefId,
                followTargetEntity,
                followTargetPlacedRefId,
                new float3(x, z, y) * WorldScale.MwUnitsToMeters,
                HashInteriorCellId(cellId),
                tokens.Length > 7);
        }

        static bool TryApplyFollowPackage(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity targetEntity,
            uint targetPlacedRefId,
            Entity followTargetEntity,
            uint followTargetPlacedRefId,
            float3 destination,
            ulong destinationInteriorCellHash,
            bool repeat)
        {
            if (followTargetEntity == Entity.Null)
                return false;

            return MorrowindScriptAiPackageUtility.TryApplyRequest(
                contentDb,
                entityManager,
                targetEntity,
                new MorrowindScriptAiPackageRequest
                {
                    TargetEntity = targetEntity,
                    TargetPlacedRefId = targetPlacedRefId,
                    FollowTargetEntity = followTargetEntity,
                    FollowTargetPlacedRefId = followTargetPlacedRefId,
                    PackageType = (byte)MorrowindScriptAiPackageRequestType.Follow,
                    ShouldRepeat = repeat ? (byte)1 : (byte)0,
                    AllowPartial = 1,
                    TargetPosition = destination,
                    DestinationInteriorCellHash = destinationInteriorCellHash,
                    FollowDistance = 256f * WorldScale.MwUnitsToMeters,
                    IdleSeconds = 0.5f,
                });
        }

        static bool TryResolveAiFollowTarget(
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
                return targetEntity != Entity.Null;
            }

            return TryResolveAiCommandTarget(contentDb, entityManager, ref session, target, out targetEntity, out targetPlacedRefId);
        }

        static bool TryResolveAiCommandTarget(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string target,
            out Entity targetEntity,
            out uint targetPlacedRefId)
            => MorrowindRuntimeTargetResolver.TryResolveDefaultOrUniqueActorById(
                contentDb,
                entityManager,
                target,
                session.SpeakerEntity,
                session.SpeakerPlacedRefId,
                out targetEntity,
                out targetPlacedRefId);

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
            SetExpression expression,
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

            if (TryApplyActorLocalSet(contentDb, entityManager, ref session, target, expression, out localResolved))
                return true;

            if (localResolved)
                return true;

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

            float value = EvaluateSetExpression(expression, locals[localIndex].FloatValue);
            locals[localIndex] = BuildScriptValue(value, valueKind);
            return true;
        }

        static bool TryApplyActorLocalSet(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string target,
            SetExpression expression,
            out bool localResolved)
        {
            localResolved = false;
            if (!TrySplitActorLocalTarget(target, out string actorId, out string localName))
                return false;

            if (!TryFindActorLocal(contentDb, actorId, localName, out var expectedActorHandle, out int localIndex, out byte valueKind, out string scriptId))
                return false;

            localResolved = true;
            if (!TryResolveAiCommandTarget(contentDb, entityManager, ref session, actorId, out Entity targetEntity, out _))
                throw new InvalidOperationException($"[VVardenfell][Dialogue] actor-local Set target '{actorId}.{localName}' resolved in content, but no unique loaded actor instance was found.");

            if (!entityManager.HasComponent<ActorSpawnSource>(targetEntity))
                throw new InvalidOperationException($"[VVardenfell][Dialogue] actor-local Set target '{actorId}.{localName}' resolved to a non-actor entity.");

            var source = entityManager.GetComponentData<ActorSpawnSource>(targetEntity);
            if (!source.Definition.IsValid || source.Definition.Value != expectedActorHandle.Value)
                throw new InvalidOperationException($"[VVardenfell][Dialogue] actor-local Set target '{actorId}.{localName}' resolved to the wrong actor definition.");

            if (!entityManager.HasBuffer<MorrowindScriptLocalValue>(targetEntity))
                throw new InvalidOperationException($"[VVardenfell][Dialogue] actor '{actorId}' has script '{scriptId}' local '{localName}', but no runtime local buffer.");

            var locals = entityManager.GetBuffer<MorrowindScriptLocalValue>(targetEntity);
            if ((uint)localIndex >= (uint)locals.Length)
                throw new InvalidOperationException($"[VVardenfell][Dialogue] actor '{actorId}' local buffer is too small for script '{scriptId}' local '{localName}'.");

            float value = EvaluateSetExpression(expression, locals[localIndex].FloatValue);
            locals[localIndex] = BuildScriptValue(value, valueKind);
            return true;
        }

        static bool TryFindActorLocal(
            RuntimeContentDatabase contentDb,
            string actorId,
            string localName,
            out ActorDefHandle actorHandle,
            out int localIndex,
            out byte valueKind,
            out string scriptId)
        {
            actorHandle = default;
            localIndex = -1;
            valueKind = 0;
            scriptId = string.Empty;
            string normalizedActorId = ContentId.NormalizeId(actorId);
            if (string.IsNullOrEmpty(normalizedActorId) || string.IsNullOrWhiteSpace(localName))
                return false;

            for (int i = 0; i < contentDb.Data.Actors.Length; i++)
            {
                ref readonly var actor = ref contentDb.Data.Actors[i];
                if (!ActorIdMatches(actor, normalizedActorId))
                    continue;

                if (string.IsNullOrWhiteSpace(actor.ScriptId)
                    || !contentDb.TryGetMorrowindScriptProgramHandle(actor.ScriptId, out var programHandle)
                    || !programHandle.IsValid)
                {
                    return false;
                }

                var localsDef = contentDb.GetMorrowindScriptLocals(programHandle);
                for (int local = 0; local < localsDef.Length; local++)
                {
                    if (!string.Equals(localsDef[local].Name, localName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    actorHandle = ActorDefHandle.FromIndex(i);
                    localIndex = local;
                    valueKind = localsDef[local].ValueKind;
                    scriptId = actor.ScriptId;
                    return true;
                }

                return false;
            }

            return false;
        }

        static bool TrySplitActorLocalTarget(string target, out string actorId, out string localName)
        {
            actorId = string.Empty;
            localName = string.Empty;
            if (string.IsNullOrWhiteSpace(target))
                return false;

            int dot = target.LastIndexOf('.');
            if (dot <= 0 || dot >= target.Length - 1)
                return false;

            actorId = target.Substring(0, dot).Trim().Trim('"');
            localName = target.Substring(dot + 1).Trim().Trim('"');
            return actorId.Length > 0 && localName.Length > 0;
        }

        static bool TryApplyGlobalSet(RuntimeContentDatabase contentDb, EntityManager entityManager, string target, SetExpression expression)
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
            float value = EvaluateSetExpression(expression, globals[globalHandle.Index].FloatValue);
            globals[globalHandle.Index] = BuildGlobalValue(value, ResolveGlobalKind(global));
            return true;
        }

        static bool TryParseSetResult(string[] tokens, out string target, out SetExpression expression)
        {
            target = string.Empty;
            expression = default;
            if (tokens.Length < 4
                || !string.Equals(tokens[0], "set", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(tokens[2], "to", StringComparison.OrdinalIgnoreCase)
                || tokens[1].IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            target = tokens[1];
            if (tokens.Length == 4 && TryParseNumericLiteral(tokens[3], out float literalValue))
            {
                expression = new SetExpression
                {
                    LiteralValue = literalValue,
                };
                return true;
            }

            if (tokens.Length == 6
                && string.Equals(tokens[3], target, StringComparison.OrdinalIgnoreCase)
                && TryResolveSetOperator(tokens[4], out int operatorSign)
                && TryParseNumericLiteral(tokens[5], out float operand))
            {
                expression = new SetExpression
                {
                    UsesCurrentValue = true,
                    OperatorSign = operatorSign,
                    LiteralValue = operand,
                };
                return true;
            }

            return false;
        }

        static bool TryResolveSetOperator(string token, out int operatorSign)
        {
            if (string.Equals(token, "+", StringComparison.Ordinal))
            {
                operatorSign = 1;
                return true;
            }

            if (string.Equals(token, "-", StringComparison.Ordinal))
            {
                operatorSign = -1;
                return true;
            }

            operatorSign = 0;
            return false;
        }

        static float EvaluateSetExpression(SetExpression expression, float currentValue)
        {
            if (expression.UsesCurrentValue)
                return currentValue + expression.OperatorSign * expression.LiteralValue;

            return expression.LiteralValue;
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

        static bool TryApplyChoiceResult(string line, DynamicBuffer<MorrowindDialogueChoice> choices, ref bool choicesReset, out bool hasChoices)
        {
            hasChoices = false;
            if (!StartsWithCommand(line, "choice"))
                return false;

            if (!TryParseChoicePairs(line, out var parsedChoices))
                return false;

            if (parsedChoices.Count == 0)
                return true;

            if (!choicesReset)
            {
                choices.Clear();
                choicesReset = true;
            }

            for (int i = 0; i < parsedChoices.Count; i++)
            {
                choices.Add(new MorrowindDialogueChoice
                {
                    Value = parsedChoices[i].Value,
                    Text = RuntimeFixedStringUtility.ToFixed512OrDefault(parsedChoices[i].Text),
                });
            }

            hasChoices = true;
            return true;
        }

        static bool TryParseChoicePairs(string line, out List<ChoicePair> choices)
        {
            choices = new List<ChoicePair>();
            string text = ExtractCommandArgumentText(line, "choice");
            int index = 0;
            while (true)
            {
                SkipChoiceSeparators(text, ref index);
                if (index >= text.Length)
                    return true;

                string choiceText;
                bool quotedChoice = false;
                int closingQuoteIndex = -1;
                if (text[index] == '"')
                {
                    quotedChoice = true;
                    index++;
                    int textStart = index;
                    while (index < text.Length && text[index] != '"')
                        index++;

                    if (index >= text.Length)
                        return false;

                    choiceText = text.Substring(textStart, index - textStart);
                    closingQuoteIndex = index;
                    index++;
                }
                else
                {
                    int textStart = index;
                    while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] != ',')
                        index++;

                    if (textStart == index)
                        return false;

                    choiceText = text.Substring(textStart, index - textStart);
                }

                SkipChoiceSeparators(text, ref index);

                if (!TryReadChoiceInteger(text, ref index, out int value))
                {
                    if (!quotedChoice
                        || closingQuoteIndex < 0
                        || !TrySplitTrailingChoiceInteger(choiceText, out choiceText, out value))
                    {
                        return false;
                    }

                    index = closingQuoteIndex;
                }

                choices.Add(new ChoicePair
                {
                    Text = choiceText,
                    Value = value,
                });
            }
        }

        static void SkipChoiceSeparators(string text, ref int index)
        {
            while (index < text.Length && (char.IsWhiteSpace(text[index]) || text[index] == ','))
                index++;
        }

        static bool TryReadChoiceInteger(string text, ref int index, out int value)
        {
            int valueStart = index;
            if (index < text.Length && (text[index] == '+' || text[index] == '-'))
                index++;
            while (index < text.Length && char.IsDigit(text[index]))
                index++;

            if (valueStart == index
                || !int.TryParse(text.Substring(valueStart, index - valueStart), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                index = valueStart;
                value = 0;
                return false;
            }

            return true;
        }

        static bool TrySplitTrailingChoiceInteger(string text, out string choiceText, out int value)
        {
            choiceText = string.Empty;
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            int end = text.Length;
            while (end > 0 && char.IsWhiteSpace(text[end - 1]))
                end--;

            int start = end;
            while (start > 0 && char.IsDigit(text[start - 1]))
                start--;

            if (start == end)
                return false;

            if (start > 0 && (text[start - 1] == '+' || text[start - 1] == '-'))
                start--;

            if (start <= 0 || !char.IsWhiteSpace(text[start - 1]))
                return false;

            string valueText = text.Substring(start, end - start);
            if (!int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return false;

            choiceText = text.Substring(0, start).TrimEnd();
            return choiceText.Length > 0;
        }

        static bool TryApplyPlayerReputationResult(DynamicBuffer<PlayerReputationMutationRequest> playerReputationRequests, string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 2 || !int.TryParse(tokens[1], out int value))
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "modreputation", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(target) && !string.Equals(target, "player", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            playerReputationRequests.Add(new PlayerReputationMutationRequest
            {
                Delta = value,
            });
            return true;
        }

        static bool TryApplyPlayerCrimeResult(EntityManager entityManager, string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadWrite<PlayerCrimeState>());
            if (query.IsEmptyIgnoreFilter)
                return false;

            var entity = query.GetSingletonEntity();
            var crime = entityManager.GetComponentData<PlayerCrimeState>(entity);
            if (tokens.Length == 1 && string.Equals(tokens[0], "payfine", StringComparison.OrdinalIgnoreCase))
            {
                crime.Bounty = 0;
                crime.PaidCrimeId = crime.CurrentCrimeId;
                entityManager.SetComponentData(entity, crime);
                SheathePlayerWeapon(entityManager, entity);
                return true;
            }

            if (tokens.Length == 1 && string.Equals(tokens[0], "payfinethief", StringComparison.OrdinalIgnoreCase))
            {
                crime.Bounty = 0;
                crime.PaidCrimeId = crime.CurrentCrimeId;
                entityManager.SetComponentData(entity, crime);
                return true;
            }

            if (tokens.Length != 2
                || !string.Equals(tokens[0], "setpccrimelevel", StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int bounty))
            {
                return false;
            }

            crime.Bounty = math.max(0, bounty);
            if (crime.Bounty == 0)
                crime.PaidCrimeId = crime.CurrentCrimeId;
            entityManager.SetComponentData(entity, crime);
            return true;
        }

        static bool TryApplyGotoJailResult(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            DynamicBuffer<MorrowindScriptJailRequest> jailRequests,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 1 || !string.Equals(tokens[0], "gotojail", StringComparison.OrdinalIgnoreCase))
                return false;

            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadWrite<PlayerCrimeState>());
            if (query.IsEmptyIgnoreFilter)
                return false;

            var entity = query.GetSingletonEntity();
            var crime = entityManager.GetComponentData<PlayerCrimeState>(entity);
            int bounty = math.max(0, crime.Bounty);
            crime.Bounty = 0;
            crime.PaidCrimeId = crime.CurrentCrimeId;
            entityManager.SetComponentData(entity, crime);
            SheathePlayerWeapon(entityManager, entity);

            int daysMod = 100;
            if (contentDb != null
                && contentDb.TryGetGameSettingFloat("iDaysinPrisonMod", out float gmstDaysMod)
                && gmstDaysMod > 0f)
            {
                daysMod = math.max(1, (int)gmstDaysMod);
            }

            jailRequests.Add(new MorrowindScriptJailRequest
            {
                Days = math.max(1, bounty / daysMod),
            });
            return true;
        }

        static void SheathePlayerWeapon(EntityManager entityManager, Entity player)
        {
            if (entityManager.HasComponent<PlayerCharacterControl>(player))
            {
                var control = entityManager.GetComponentData<PlayerCharacterControl>(player);
                control.ReadyWeaponTogglePressed = false;
                control.AttackHeld = false;
                control.AttackPressed = false;
                control.AttackReleased = false;
                entityManager.SetComponentData(player, control);
            }

            using var visualQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<LocalPlayerVisual>(),
                ComponentType.ReadWrite<ActorWeaponAnimationState>());

            using NativeArray<Entity> entities = visualQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            using NativeArray<LocalPlayerVisual> visuals = visualQuery.ToComponentDataArray<LocalPlayerVisual>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (visuals[i].Player != player)
                    continue;

                var weaponState = entityManager.GetComponentData<ActorWeaponAnimationState>(entities[i]);
                weaponState.Drawn = 0;
                weaponState.Phase = ActorWeaponAnimationPhase.Hidden;
                weaponState.AttackStrength = 0f;
                weaponState.ReadyWeaponTogglePressed = 0;
                weaponState.AttackHeld = 0;
                weaponState.AttackPressed = 0;
                weaponState.AttackReleased = 0;
                weaponState.ReleaseQueued = 0;
                entityManager.SetComponentData(entities[i], weaponState);
            }
        }

        static bool TryApplyPlayerSkillResult(EntityManager entityManager, string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 2 || !int.TryParse(tokens[1], out int value))
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!TryApplyPlayerSkillCommand(ref command, ref value, entityManager, target))
                return false;

            return true;
        }

        static bool TryApplyPlayerSkillCommand(
            ref string command,
            ref int value,
            EntityManager entityManager,
            string target)
        {
            if (!TryResolvePlayerSkillCommand(command, out PlayerSkillKind skillKind)
                || (!string.IsNullOrWhiteSpace(target) && !string.Equals(target, "player", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadWrite<ActorSkillSet>());
            if (query.IsEmptyIgnoreFilter)
                return false;

            var entity = query.GetSingletonEntity();
            var skills = entityManager.GetComponentData<ActorSkillSet>(entity);
            ApplyPlayerSkillDelta(ref skills, skillKind, value);
            entityManager.SetComponentData(entity, skills);
            return true;
        }

        static bool TryResolvePlayerSkillCommand(string command, out PlayerSkillKind skillKind)
        {
            skillKind = PlayerSkillKind.None;
            if (string.IsNullOrWhiteSpace(command)
                || command.Length <= 3
                || !command.StartsWith("mod", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string skill = command.Substring(3);
            if (string.Equals(skill, "block", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.Block;
            else if (string.Equals(skill, "armorer", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.Armorer;
            else if (string.Equals(skill, "mediumarmor", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.MediumArmor;
            else if (string.Equals(skill, "heavyarmor", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.HeavyArmor;
            else if (string.Equals(skill, "bluntweapon", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.BluntWeapon;
            else if (string.Equals(skill, "longblade", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.LongBlade;
            else if (string.Equals(skill, "axe", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.Axe;
            else if (string.Equals(skill, "spear", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.Spear;
            else if (string.Equals(skill, "athletics", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.Athletics;
            else if (string.Equals(skill, "enchant", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.Enchant;
            else if (string.Equals(skill, "destruction", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.Destruction;
            else if (string.Equals(skill, "alteration", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.Alteration;
            else if (string.Equals(skill, "illusion", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.Illusion;
            else if (string.Equals(skill, "conjuration", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.Conjuration;
            else if (string.Equals(skill, "mysticism", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.Mysticism;
            else if (string.Equals(skill, "restoration", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.Restoration;
            else if (string.Equals(skill, "alchemy", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.Alchemy;
            else if (string.Equals(skill, "unarmored", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.Unarmored;
            else if (string.Equals(skill, "security", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.Security;
            else if (string.Equals(skill, "sneak", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.Sneak;
            else if (string.Equals(skill, "acrobatics", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.Acrobatics;
            else if (string.Equals(skill, "lightarmor", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.LightArmor;
            else if (string.Equals(skill, "shortblade", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.ShortBlade;
            else if (string.Equals(skill, "marksman", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.Marksman;
            else if (string.Equals(skill, "mercantile", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.Mercantile;
            else if (string.Equals(skill, "speechcraft", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.Speechcraft;
            else if (string.Equals(skill, "handtohand", StringComparison.OrdinalIgnoreCase)) skillKind = PlayerSkillKind.HandToHand;

            return skillKind != PlayerSkillKind.None;
        }

        static void ApplyPlayerSkillDelta(ref ActorSkillSet skills, PlayerSkillKind skillKind, int value)
        {
            switch (skillKind)
            {
                case PlayerSkillKind.Block: skills.Block += value; break;
                case PlayerSkillKind.Armorer: skills.Armorer += value; break;
                case PlayerSkillKind.MediumArmor: skills.MediumArmor += value; break;
                case PlayerSkillKind.HeavyArmor: skills.HeavyArmor += value; break;
                case PlayerSkillKind.BluntWeapon: skills.BluntWeapon += value; break;
                case PlayerSkillKind.LongBlade: skills.LongBlade += value; break;
                case PlayerSkillKind.Axe: skills.Axe += value; break;
                case PlayerSkillKind.Spear: skills.Spear += value; break;
                case PlayerSkillKind.Athletics: skills.Athletics += value; break;
                case PlayerSkillKind.Enchant: skills.Enchant += value; break;
                case PlayerSkillKind.Destruction: skills.Destruction += value; break;
                case PlayerSkillKind.Alteration: skills.Alteration += value; break;
                case PlayerSkillKind.Illusion: skills.Illusion += value; break;
                case PlayerSkillKind.Conjuration: skills.Conjuration += value; break;
                case PlayerSkillKind.Mysticism: skills.Mysticism += value; break;
                case PlayerSkillKind.Restoration: skills.Restoration += value; break;
                case PlayerSkillKind.Alchemy: skills.Alchemy += value; break;
                case PlayerSkillKind.Unarmored: skills.Unarmored += value; break;
                case PlayerSkillKind.Security: skills.Security += value; break;
                case PlayerSkillKind.Sneak: skills.Sneak += value; break;
                case PlayerSkillKind.Acrobatics: skills.Acrobatics += value; break;
                case PlayerSkillKind.LightArmor: skills.LightArmor += value; break;
                case PlayerSkillKind.ShortBlade: skills.ShortBlade += value; break;
                case PlayerSkillKind.Marksman: skills.Marksman += value; break;
                case PlayerSkillKind.Mercantile: skills.Mercantile += value; break;
                case PlayerSkillKind.Speechcraft: skills.Speechcraft += value; break;
                case PlayerSkillKind.HandToHand: skills.HandToHand += value; break;
            }
        }

        static bool TryApplyPlayerAttributeResult(EntityManager entityManager, string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 2 || !int.TryParse(tokens[1], out int value))
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "modstrength", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(target) && !string.Equals(target, "player", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadWrite<ActorAttributeSet>());
            if (query.IsEmptyIgnoreFilter)
                return false;

            var entity = query.GetSingletonEntity();
            var attributes = entityManager.GetComponentData<ActorAttributeSet>(entity);
            attributes.Strength += value;
            entityManager.SetComponentData(entity, attributes);
            return true;
        }

        static bool TryApplyPlayerFactionResult(
            RuntimeContentDatabase contentDb,
            ref MorrowindDialogueSession session,
            DynamicBuffer<PlayerFactionMutationRequest> playerFactionRequests,
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

            playerFactionRequests.Add(new PlayerFactionMutationRequest
            {
                SourceEntity = session.SpeakerEntity,
                SourcePlacedRefId = session.SpeakerPlacedRefId,
                FactionIndex = factionIndex,
                Value = value,
                Kind = (byte)(modRep
                    ? PlayerFactionMutationKind.ModReputation
                    : raiseRank
                        ? PlayerFactionMutationKind.RaiseRank
                        : joinFaction
                            ? PlayerFactionMutationKind.Join
                            : expell
                                ? PlayerFactionMutationKind.Expel
                                : PlayerFactionMutationKind.ClearExpelled),
            });
            return true;
        }

        static bool TryApplyActorFactionResult(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            DynamicBuffer<ActorFactionRankMutationRequest> actorFactionRequests,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 1)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "raiserank", StringComparison.OrdinalIgnoreCase))
                return false;

            Entity targetEntity;
            if (IsPlayerTarget(target))
            {
                targetEntity = ResolvePlayerEntity(entityManager);
                return targetEntity != Entity.Null;
            }

            if (!TryResolveAiCommandTarget(contentDb, entityManager, ref session, target, out targetEntity, out _))
                return false;

            actorFactionRequests.Add(new ActorFactionRankMutationRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = entityManager.HasComponent<PlacedRefIdentity>(targetEntity)
                    ? entityManager.GetComponentData<PlacedRefIdentity>(targetEntity).Value
                    : 0u,
            });
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

        static int FindActorFactionIndex(DynamicBuffer<ActorFactionMembership> factions, int factionIndex)
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
            ActiveExplicitRefLookup activeExplicitRefs,
            ref MorrowindDialogueSession session,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            NormalizeSeparatedExplicitCommand(tokens, out tokens);
            if (tokens.Length != 3)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            bool add = string.Equals(command, "additem", StringComparison.OrdinalIgnoreCase);
            bool remove = string.Equals(command, "removeitem", StringComparison.OrdinalIgnoreCase);
            if (!add && !remove)
                return false;

            string itemId = tokens[1];
            if (!TryResolveDialogueInventoryCount(contentDb, entityManager, target, remove, itemId, tokens[2], out int count))
                return false;

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

            if (string.IsNullOrWhiteSpace(target))
            {
                return TryApplyActorInventoryResult(
                    contentDb,
                    entityManager,
                    session.SpeakerEntity,
                    session.SpeakerPlacedRefId,
                    itemId,
                    count,
                    add);
            }

            if (TryResolveAiCommandTarget(contentDb, entityManager, ref session, target, out Entity actorEntity, out uint actorPlacedRefId)
                && TryApplyActorInventoryResult(contentDb, entityManager, actorEntity, actorPlacedRefId, itemId, count, add))
            {
                return true;
        }

        static bool TryResolveDialogueInventoryCount(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            string target,
            bool remove,
            string itemId,
            string countToken,
            out int count)
        {
            if (int.TryParse(countToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out count))
                return true;

            count = 0;
            if (!remove
                || !string.Equals(target, "player", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(NormalizeGoldId(itemId), "gold_001", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!TryReadPlayerCrime(entityManager, out var crime))
                return false;

            int bounty = math.max(0, crime.Bounty);
            if (string.Equals(countToken, "getpccrimelevel", StringComparison.OrdinalIgnoreCase))
            {
                count = bounty;
                return true;
            }

            if (string.Equals(countToken, "crimegolddiscount", StringComparison.OrdinalIgnoreCase))
                return TryCalculateCrimeGold(contentDb, bounty, "fCrimeGoldDiscountMult", out count);

            if (string.Equals(countToken, "crimegoldturnin", StringComparison.OrdinalIgnoreCase))
                return TryCalculateCrimeGold(contentDb, bounty, "fCrimeGoldTurnInMult", out count);

            return false;
        }

        static bool TryReadPlayerCrime(EntityManager entityManager, out PlayerCrimeState crime)
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<PlayerCrimeState>());
            if (query.IsEmptyIgnoreFilter)
            {
                crime = default;
                return false;
            }

            crime = entityManager.GetComponentData<PlayerCrimeState>(query.GetSingletonEntity());
            return true;
        }

        static bool TryCalculateCrimeGold(RuntimeContentDatabase contentDb, int bounty, string gmstId, out int value)
        {
            value = 0;
            if (contentDb == null || !contentDb.TryGetGameSettingFloat(gmstId, out float multiplier))
                return false;

            value = (int)(bounty * multiplier);
            if (bounty > 0 && value < 1)
                value = 1;
            return true;
        }

            if (!TryResolveExplicitRefTarget(contentDb, activeExplicitRefs, target, out Entity explicitEntity, out uint placedRefId))
                return false;

            if (explicitEntity == Entity.Null
                && !TryFindEntityByPlacedRef(entityManager, placedRefId, out explicitEntity))
            {
                return false;
            }

            if (TryApplyActorInventoryResult(contentDb, entityManager, explicitEntity, placedRefId, itemId, count, add))
                return true;

            return TryApplyContainerInventoryResult(contentDb, entityManager, explicitEntity, placedRefId, itemId, count, add);
        }

        static bool TryApplyActorInventoryResult(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity actorEntity,
            uint placedRefId,
            string itemId,
            int count,
            bool add)
        {
            if (actorEntity == Entity.Null
                || !entityManager.Exists(actorEntity)
                || !entityManager.HasBuffer<ActorInventoryItem>(actorEntity))
            {
                return false;
            }

            var actorInventory = entityManager.GetBuffer<ActorInventoryItem>(actorEntity);
            uint resolutionSeed = placedRefId != 0u
                ? placedRefId
                : unchecked((uint)actorEntity.Index + 1u);
            return add
                ? InventoryMutationUtility.TryAddActorItem(contentDb, actorInventory, itemId, count, resolutionSeed)
                : InventoryMutationUtility.TryRemoveActorItem(contentDb, actorInventory, itemId, count);
        }

        static bool TryApplyContainerInventoryResult(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity containerEntity,
            uint placedRefId,
            string itemId,
            int count,
            bool add)
        {
            if (contentDb == null
                || containerEntity == Entity.Null
                || placedRefId == 0u
                || !entityManager.Exists(containerEntity)
                || !entityManager.HasComponent<ContainerAuthoring>(containerEntity))
            {
                return false;
            }

            count = NormalizeScriptCount(count);
            if (count == 0)
                return true;

            if (add)
            {
                if (!TryResolveContainerAddContent(contentDb, itemId, placedRefId, out ContentReference content))
                    return false;

                return TryApplyContainerDelta(contentDb, entityManager, containerEntity, placedRefId, content, count);
            }

            if (!ContainerLootUtility.TryResolveDirectCarryable(contentDb, NormalizeGoldId(itemId), out ContentReference removeContent, out _))
                return false;

            return TryApplyContainerDelta(contentDb, entityManager, containerEntity, placedRefId, removeContent, -count);
        }

        static bool TryResolveContainerAddContent(RuntimeContentDatabase contentDb, string itemId, uint placedRefId, out ContentReference content)
        {
            if (ContainerLootUtility.TryResolveDirectCarryable(contentDb, NormalizeGoldId(itemId), out content, out _))
                return true;

            if (contentDb.TryGetItemLeveledListHandle(itemId, out ItemLeveledListDefHandle listHandle)
                && ContainerLootUtility.TryResolveLooseLeveledCarryable(contentDb, listHandle, placedRefId, out content, out _))
            {
                return content.IsValid;
            }

            content = default;
            return false;
        }

        static bool TryApplyContainerDelta(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            Entity containerEntity,
            uint placedRefId,
            ContentReference content,
            int deltaCount)
        {
            Entity runtimeEntity = WorldStateEntityQueryUtility.GetSingletonBufferOwner<ContainerSessionItem>(entityManager);
            if (runtimeEntity == Entity.Null
                || !entityManager.HasBuffer<ContainerSessionHeader>(runtimeEntity)
                || !WorldJournalUtility.TryGetJournalEntity(entityManager, out Entity journalEntity))
            {
                return false;
            }

            var headers = entityManager.GetBuffer<ContainerSessionHeader>(runtimeEntity);
            var items = entityManager.GetBuffer<ContainerSessionItem>(runtimeEntity);
            var journal = entityManager.GetBuffer<WorldJournalEntry>(journalEntity);
            var authoring = entityManager.GetComponentData<ContainerAuthoring>(containerEntity);
            EnsureContainerSessionInitialized(contentDb, journal, headers, items, placedRefId, authoring.Definition);
            ContainerLootUtility.ApplyContainerDelta(items, placedRefId, content, deltaCount);
            WorldJournalUtility.AppendContainerDelta(entityManager, placedRefId, content, deltaCount);
            return true;
        }

        static void EnsureContainerSessionInitialized(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<WorldJournalEntry> journal,
            DynamicBuffer<ContainerSessionHeader> headers,
            DynamicBuffer<ContainerSessionItem> items,
            uint placedRefId,
            ContainerDefHandle definition)
        {
            if (ContainerLootUtility.FindHeaderIndex(headers, placedRefId) >= 0)
                return;

            headers.Add(new ContainerSessionHeader
            {
                PlacedRefId = placedRefId,
                Definition = definition,
            });

            var diagnostics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ContainerLootUtility.MaterializeContainerContents(contentDb, items, placedRefId, definition, diagnostics);
            WorldJournalUtility.ApplyContainerDeltas(placedRefId, journal, items);
        }

        static bool TryFindEntityByPlacedRef(EntityManager entityManager, uint placedRefId, out Entity entity)
        {
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlacedRefIdentity>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var refs = query.ToComponentDataArray<PlacedRefIdentity>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (refs[i].Value == placedRefId)
                {
                    entity = entities[i];
                    return true;
                }
            }

            entity = Entity.Null;
            return false;
        }

        static bool TryApplyActorSpellResult(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            DynamicBuffer<ActorSpellMutationRequest> actorSpellRequests,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 2)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            bool add = string.Equals(command, "addspell", StringComparison.OrdinalIgnoreCase);
            bool remove = string.Equals(command, "removespell", StringComparison.OrdinalIgnoreCase);
            if (!add && !remove)
                return false;

            if (contentDb == null
                || !contentDb.TryGetSpellHandle(tokens[1], out var spellHandle)
                || !spellHandle.IsValid)
            {
                return false;
            }

            if (!TryResolveActorSpellTarget(contentDb, entityManager, ref session, target, out Entity actorEntity, out uint actorPlacedRefId))
            {
                return false;
            }

            actorSpellRequests.Add(new ActorSpellMutationRequest
            {
                TargetEntity = actorEntity,
                TargetPlacedRefId = actorPlacedRefId,
                Spell = spellHandle,
                Remove = remove ? (byte)1 : (byte)0,
            });
            return true;
        }

        static bool TryApplyScriptedCastResult(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            DynamicBuffer<ScriptedCastRequest> castRequests,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 3)
                return false;

            ParseTargetCommand(tokens[0], out string casterTarget, out string command);
            if (!string.Equals(command, "cast", StringComparison.OrdinalIgnoreCase))
                return false;

            if (contentDb == null
                || !TryResolveActorSpellTarget(contentDb, entityManager, ref session, casterTarget, out Entity casterEntity, out uint casterPlacedRefId)
                || IsPlayerEntity(entityManager, casterEntity)
                || !contentDb.TryGetSpellHandle(tokens[1], out var spellHandle)
                || !spellHandle.IsValid
                || !TryResolveActorSpellTarget(contentDb, entityManager, ref session, tokens[2], out Entity targetEntity, out uint targetPlacedRefId))
            {
                return false;
            }

            castRequests.Add(new ScriptedCastRequest
            {
                CasterEntity = casterEntity,
                CasterPlacedRefId = casterPlacedRefId,
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Spell = spellHandle,
            });
            return true;
        }

        static bool TryResolveActorSpellTarget(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string target,
            out Entity actorEntity)
        {
            return TryResolveActorSpellTarget(contentDb, entityManager, ref session, target, out actorEntity, out _);
        }

        static bool TryResolveActorSpellTarget(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string target,
            out Entity actorEntity,
            out uint actorPlacedRefId)
        {
            actorEntity = Entity.Null;
            actorPlacedRefId = 0u;
            if (IsPlayerTarget(target))
            {
                actorEntity = ResolvePlayerEntity(entityManager);
                return actorEntity != Entity.Null && entityManager.Exists(actorEntity);
            }

            return TryResolveAiCommandTarget(contentDb, entityManager, ref session, target, out actorEntity, out actorPlacedRefId)
                   && actorEntity != Entity.Null
                   && entityManager.Exists(actorEntity);
        }

        static bool IsPlayerEntity(EntityManager entityManager, Entity entity)
            => MorrowindRuntimeTargetResolver.IsPlayerEntity(entityManager, entity);

        static bool TryApplyShowMapResult(DynamicBuffer<GlobalMapRevealRequest> globalMapRevealRequests, string line)
        {
            if (!StartsWithCommand(line, "showmap"))
                return false;

            string cellNamePrefix = ExtractCommandArgumentText(line, "showmap").Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(cellNamePrefix))
                return false;

            globalMapRevealRequests.Add(new GlobalMapRevealRequest
            {
                CellNamePrefix = cellNamePrefix,
            });
            return true;
        }

        static bool TryApplyMessageBoxResult(DynamicBuffer<ShellMessageBoxRequest> shellMessageBoxRequests, string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if ((tokens.Length != 2 && tokens.Length != 3)
                || !string.Equals(tokens[0], "messagebox", StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.IsNullOrWhiteSpace(tokens[1]))
                return false;

            if (tokens.Length == 3 && !IsOkButton(tokens[2]))
                return false;

            shellMessageBoxRequests.Add(new ShellMessageBoxRequest
            {
                Body = tokens[1],
            });
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
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 3)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "journal", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(target) && !string.Equals(target, "player", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

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
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 2)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "addtopic", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(target) && !string.Equals(target, "player", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

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

        static string NormalizeGoldId(string itemId)
        {
            if (string.Equals(itemId, "gold_005", StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemId, "gold_010", StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemId, "gold_025", StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemId, "gold_100", StringComparison.OrdinalIgnoreCase))
            {
                return "gold_001";
            }

            return itemId;
        }

        static int NormalizeScriptCount(int count)
            => count < 0 ? (ushort)count : count;

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

        static Entity ResolvePlayerEntity(EntityManager entityManager)
            => MorrowindRuntimeTargetResolver.ResolvePlayerEntity(entityManager);

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

        enum PlayerSkillKind : byte
        {
            None = 0,
            Block = 1,
            Armorer = 2,
            MediumArmor = 3,
            HeavyArmor = 4,
            BluntWeapon = 5,
            LongBlade = 6,
            Axe = 7,
            Spear = 8,
            Athletics = 9,
            Enchant = 10,
            Destruction = 11,
            Alteration = 12,
            Illusion = 13,
            Conjuration = 14,
            Mysticism = 15,
            Restoration = 16,
            Alchemy = 17,
            Unarmored = 18,
            Security = 19,
            Sneak = 20,
            Acrobatics = 21,
            LightArmor = 22,
            ShortBlade = 23,
            Marksman = 24,
            Mercantile = 25,
            Speechcraft = 26,
            HandToHand = 27,
        }

        struct SetExpression
        {
            public bool UsesCurrentValue;
            public int OperatorSign;
            public float LiteralValue;
        }

        struct ChoicePair
        {
            public string Text;
            public int Value;
        }
    }
}
