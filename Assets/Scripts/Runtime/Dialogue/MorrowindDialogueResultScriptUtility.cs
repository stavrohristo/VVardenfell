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

        public static void PrepareQueries(EntityManager entityManager)
        {
            PlayerQueryCache.Get(entityManager);
            ScriptGlobalQueryCache.Get(entityManager);
            PlayerCrimeWriteQueryCache.Get(entityManager);
            PlayerCrimeReadQueryCache.Get(entityManager);
            LocalPlayerVisualWeaponQueryCache.Get(entityManager);
            PlacedRefIdentityQueryCache.Get(entityManager);
        }

        public static bool ExecuteSupported(
            ref RuntimeContentBlob contentBlob,
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
            DynamicBuffer<ActorAttributeMutationRequest> playerAttributeRequests,
            DynamicBuffer<PlayerSkillMutationRequest> playerSkillRequests,
            DynamicBuffer<PlayerFactionMutationRequest> playerFactionRequests,
            DynamicBuffer<ActorFactionRankMutationRequest> actorFactionRequests,
            DynamicBuffer<MorrowindQuestJournalIndex> questStates,
            DynamicBuffer<MorrowindQuestJournalEntry> questEntries,
            DynamicBuffer<MorrowindDialogueChoice> choices,
            string script,
            ref RuntimeShellState shell,
            ref MorrowindDialogueSession session)
        {
            if (string.IsNullOrWhiteSpace(script))
                return false;

            bool choicesReset = false;
            string[] lines = script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = StripComment(lines[i]).Trim();
                if (line.Length == 0)
                    continue;

                if (TryApplyJournalResult(ref contentBlob, ref questState, time, questStates, questEntries, line))
                    continue;

                if (TryApplySetJournalIndexResult(ref contentBlob, ref questState, time, questStates, questEntries, line))
                    continue;

                if (TryApplyAddTopicResult(ref contentBlob, knownTopics, line))
                    continue;

                if (StartsWithCommand(line, "filljournal"))
                {
                    MorrowindDialogueUtility.TryFillJournal(
                        ref contentBlob,
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

                if (TryApplyClearInfoActorResult(ref contentBlob, topicEntries, ref session, line))
                    continue;

                if (TryApplyRefStateResult(ref contentBlob, activeExplicitRefs, refStateRequests, ref session, line))
                    continue;

                if (TryApplyMovementFlagResult(ref contentBlob, entityManager, activeExplicitRefs, movementFlagRequests, ref session, line))
                    continue;

                if (TryApplyPlaceAtPCResult(ref contentBlob, placeAtRequests, line))
                    continue;

                if (TryApplyPositionCellResult(ref contentBlob, activeExplicitRefs, transformRequests, ref session, line))
                    continue;

                if (TryApplyInventoryResult(ref contentBlob, entityManager, activeExplicitRefs, ref session, line))
                    continue;

                if (TryApplyActorSpellResult(ref contentBlob, entityManager, ref session, actorSpellRequests, line))
                    continue;

                if (TryApplyScriptedCastResult(ref contentBlob, entityManager, ref session, castRequests, line))
                    continue;

                if (TryApplyDispositionResult(ref contentBlob, entityManager, ref session, line))
                    continue;

                if (TryApplyActorFactionResult(ref contentBlob, entityManager, ref session, actorFactionRequests, line))
                    continue;

                if (TryApplyPlayerFactionResult(ref contentBlob, ref session, playerFactionRequests, line))
                    continue;

                if (TryApplyPlayerReputationResult(playerReputationRequests, line))
                    continue;

                if (TryApplyPlayerCrimeResult(entityManager, line))
                    continue;

                if (TryApplyGotoJailResult(ref contentBlob, entityManager, jailRequests, line))
                {
                    session.Goodbye = 1;
                    continue;
                }

                if (TryApplyPlayerSkillResult(playerSkillRequests, line))
                    continue;

                if (TryApplyPlayerAttributeResult(entityManager, playerAttributeRequests, line))
                    continue;

                if (TryApplyFactionReactionResult(ref contentBlob, factionReactionOverrides, line))
                    continue;

                if (TryApplyStartScriptResult(ref contentBlob, scriptStartRequests, ref session, line))
                    continue;

                if (TryApplySetResult(ref contentBlob, entityManager, ref session, line))
                    continue;

                if (TryApplyActorAiSettingResult(ref contentBlob, entityManager, ref session, line))
                    continue;

                if (TryApplyCombatTargetResult(ref contentBlob, entityManager, ref session, line))
                    continue;

                if (TryApplyAiWanderResult(ref contentBlob, entityManager, ref session, line))
                    continue;

                if (TryApplyAiTravelResult(ref contentBlob, entityManager, ref session, line))
                    continue;

                if (TryApplyAiFollowResult(ref contentBlob, entityManager, ref session, line))
                    continue;

                if (TryApplyAiFollowCellResult(ref contentBlob, entityManager, ref session, line))
                    continue;

                if (TryApplyForceGreetingResult(entityManager, ref session, forceGreetingRequests, line))
                    continue;

                if (StartsWithCommand(line, "goodbye"))
                {
                    session.Goodbye = 1;
                    continue;
                }

                if (s_UnsupportedResultWarnings.Add(line))
                    Debug.LogWarning($"[VVardenfell][Dialogue] unsupported V1 dialogue result command: '{line}'.");
            }

            return false;
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
            ref RuntimeContentBlob contentBlob,
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
            if (!RuntimeContentBlobUtility.TryGetMorrowindScriptProgramHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(scriptId), out var programHandle)
                || !programHandle.IsValid)
            {
                return false;
            }

            ref var program = ref RuntimeContentBlobUtility.Get(ref contentBlob, programHandle);
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
            ref RuntimeContentBlob contentBlob,
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

            if (!RuntimeContentBlobUtility.TryGetFactionHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(tokens[1]), out var source)
                || !source.IsValid
                || !RuntimeContentBlobUtility.TryGetFactionHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(tokens[2]), out var targetFaction)
                || !targetFaction.IsValid)
            {
                return false;
            }

            return mod
                ? MorrowindDialogueUtility.TryModFactionReaction(ref contentBlob, factionReactionOverrides, source.Index, targetFaction.Index, value)
                : MorrowindDialogueUtility.TrySetFactionReaction(ref contentBlob, factionReactionOverrides, source.Index, targetFaction.Index, value);
        }

        static bool TryApplyClearInfoActorResult(
            ref RuntimeContentBlob contentBlob,
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
                ref contentBlob,
                topicEntries,
                session.SelectedTopicDialogueIndex,
                session.SpeakerId);
        }

        static bool TryApplyRefStateResult(
            ref RuntimeContentBlob contentBlob,
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
                if (!TryResolveExplicitRefTarget(ref contentBlob, activeExplicitRefs, target, out targetEntity, out targetPlacedRefId))
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
            ref RuntimeContentBlob contentBlob,
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
                EntityQuery playerQuery = PlayerQueryCache.Get(entityManager);
                if (playerQuery.IsEmptyIgnoreFilter)
                    return false;

                targetEntity = playerQuery.GetSingletonEntity();
            }
            else
            {
                if (!TryResolveExplicitRefTarget(ref contentBlob, activeExplicitRefs, target, out targetEntity, out targetPlacedRefId))
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
            ref RuntimeContentBlob contentBlob,
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
            if (!TryResolvePlaceAtContent(ref contentBlob, contentId, out ContentReference content))
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
            ref RuntimeContentBlob contentBlob,
            string contentId,
            out ContentReference content)
        {
            content = default;
            if (string.IsNullOrWhiteSpace(contentId)
                || !RuntimeContentBlobUtility.TryResolvePlaceableByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(contentId), out content)
                || !RuntimeContentBlobUtility.IsValid(ref contentBlob, content))
            {
                return false;
            }

            if (content.Kind == ContentReferenceKind.Actor)
            {
                ref var actor = ref RuntimeContentBlobUtility.Get(ref contentBlob, new ActorDefHandle { Value = content.HandleValue });
                return actor.Kind == ActorDefKind.Creature;
            }

            return content.Kind == ContentReferenceKind.Item || content.Kind == ContentReferenceKind.Light;
        }

        static bool TryResolveExplicitRefTarget(
            ref RuntimeContentBlob contentBlob,
            ActiveExplicitRefLookup activeExplicitRefs,
            string target,
            out Entity targetEntity,
            out uint targetPlacedRefId)
        {
            return MorrowindRuntimeTargetResolver.TryResolveExplicitRefTarget(ref contentBlob, activeExplicitRefs, target, out targetEntity, out targetPlacedRefId);
        }

        static bool TryApplyPositionCellResult(
            ref RuntimeContentBlob contentBlob,
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
                if (!TryResolveExplicitRefTarget(ref contentBlob, activeExplicitRefs, target, out targetEntity, out targetPlacedRefId))
                    return false;
            }

            if (targetPlacedRefId == 0u)
                return false;

            transformRequests.Add(new MorrowindScriptTransformRequest
            {
                TargetEntity = targetEntity,
                TargetPlacedRefId = targetPlacedRefId,
                Position = new float3(x, z, y) * WorldScale.MwUnitsToMeters,
                Radians = zRotMinutes,
                InteriorCellHash = cellHash,
                Operation = 2,
            });
            return true;
        }

        static bool TryApplySetResult(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (!TryParseSetResult(tokens, out string target, out SetExpression expression))
            {
                return false;
            }

            if (TryApplyLocalSet(ref contentBlob, entityManager, ref session, target, expression, out bool localResolved))
                return true;

            if (localResolved)
                return true;

            return TryApplyGlobalSet(ref contentBlob, entityManager, target, expression);
        }

        static bool TryApplyActorAiSettingResult(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 2 || !int.TryParse(tokens[1], out int value))
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!TryResolveActorAiSettingCommand(command, out ActorAiSettingKind kind, out bool isMod)
                || !TryResolveAiCommandTarget(ref contentBlob, entityManager, ref session, target, out Entity targetEntity, out _))
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
            ref RuntimeContentBlob contentBlob,
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

            if (!TryResolveAiCommandTarget(ref contentBlob, entityManager, ref session, commandTarget, out Entity actorEntity, out uint actorPlacedRefId))
                return false;

            if (startCombat)
            {
                if (tokens.Length != 2
                    || !TryResolveCombatTarget(ref contentBlob, entityManager, ref session, tokens[1], out Entity combatTargetEntity, out uint combatTargetPlacedRefId))
                {
                    return false;
                }

                return MorrowindCombatTargetUtility.TryStartCombat(
                    ref contentBlob,
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
            ref RuntimeContentBlob contentBlob,
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

            if (!TryResolveAiCommandTarget(ref contentBlob, entityManager, ref session, target, out Entity targetEntity, out uint targetPlacedRefId))
                return false;

            if (!float.TryParse(NormalizeToken(tokens[1]), NumberStyles.Float, CultureInfo.InvariantCulture, out float range))
                return false;

            for (int i = 2; i < tokens.Length; i++)
            {
                if (!float.TryParse(NormalizeToken(tokens[i]), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    return false;
            }

            return MorrowindScriptAiPackageUtility.TryApplyRequest(
                ref contentBlob,
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
            ref RuntimeContentBlob contentBlob,
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

            if (!TryResolveAiCommandTarget(ref contentBlob, entityManager, ref session, target, out Entity targetEntity, out uint targetPlacedRefId))
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
                ref contentBlob,
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
            ref RuntimeContentBlob contentBlob,
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

            if (!TryResolveAiCommandTarget(ref contentBlob, entityManager, ref session, target, out Entity targetEntity, out uint targetPlacedRefId)
                || !TryResolveAiFollowTarget(ref contentBlob, entityManager, ref session, tokens[1], out Entity followTargetEntity, out uint followTargetPlacedRefId))
                return false;

            if (!TryParseAiFollowNumbers(tokens, 2, out _, out float x, out float y, out float z))
                return false;

            for (int i = 6; i < tokens.Length; i++)
            {
                if (!float.TryParse(NormalizeToken(tokens[i]), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    return false;
            }

            return TryApplyFollowPackage(
                ref contentBlob,
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
            ref RuntimeContentBlob contentBlob,
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

            if (!TryResolveAiCommandTarget(ref contentBlob, entityManager, ref session, target, out Entity targetEntity, out uint targetPlacedRefId)
                || !TryResolveAiFollowTarget(ref contentBlob, entityManager, ref session, tokens[1], out Entity followTargetEntity, out uint followTargetPlacedRefId))
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
                ref contentBlob,
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
            ref RuntimeContentBlob contentBlob,
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
                ref contentBlob,
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
            ref RuntimeContentBlob contentBlob,
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

            return TryResolveAiCommandTarget(ref contentBlob, entityManager, ref session, target, out targetEntity, out targetPlacedRefId);
        }

        static bool TryResolveAiCommandTarget(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string target,
            out Entity targetEntity,
            out uint targetPlacedRefId)
        {
            return MorrowindRuntimeTargetResolver.TryResolveDefaultOrUniqueActorById(
                ref contentBlob,
                entityManager,
                target,
                session.SpeakerEntity,
                session.SpeakerPlacedRefId,
                out targetEntity,
                out targetPlacedRefId);
        }

        static bool ActorIdMatches(ref RuntimeActorDefBlob actor, string normalizedTarget)
            => string.Equals(ContentId.NormalizeId(actor.Id.ToString()), normalizedTarget, StringComparison.OrdinalIgnoreCase)
               || string.Equals(ContentId.NormalizeId(actor.OriginalId.ToString()), normalizedTarget, StringComparison.OrdinalIgnoreCase);

        static bool TryResolveCombatTarget(
            ref RuntimeContentBlob contentBlob,
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

            return TryResolveAiCommandTarget(ref contentBlob, entityManager, ref session, target, out targetEntity, out targetPlacedRefId);
        }

        static bool TryApplyLocalSet(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string target,
            SetExpression expression,
            out bool localResolved)
        {
            localResolved = false;
            if (!session.SpeakerActor.IsValid
                || session.SpeakerEntity == Entity.Null
                || !entityManager.Exists(session.SpeakerEntity))
            {
                return false;
            }

            if (TryApplyActorLocalSet(ref contentBlob, entityManager, ref session, target, expression, out localResolved))
                return true;

            if (localResolved)
                return true;

            ref var actor = ref RuntimeContentBlobUtility.Get(ref contentBlob, session.SpeakerActor);
            string actorScriptId = actor.ScriptId.ToString();
            if (string.IsNullOrWhiteSpace(actorScriptId)
                || !RuntimeContentBlobUtility.TryGetMorrowindScriptProgramHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(actorScriptId), out var programHandle)
                || !programHandle.IsValid)
            {
                return false;
            }

            ref var program = ref RuntimeContentBlobUtility.Get(ref contentBlob, programHandle);
            ref var localsDef = ref RuntimeContentBlobUtility.GetMorrowindScriptLocals(ref contentBlob, programHandle);
            int localIndex = -1;
            byte valueKind = 0;
            for (int i = 0; i < program.LocalCount; i++)
            {
                int absoluteLocalIndex = program.FirstLocalIndex + i;
                if (string.Equals(localsDef[absoluteLocalIndex].Name.ToString(), target, StringComparison.OrdinalIgnoreCase))
                {
                    localIndex = i;
                    valueKind = localsDef[absoluteLocalIndex].ValueKind;
                    break;
                }
            }

            if (localIndex < 0)
                return false;

            localResolved = true;
            if (!entityManager.HasBuffer<MorrowindScriptLocalValue>(session.SpeakerEntity))
                throw new InvalidOperationException($"[VVardenfell][Dialogue] speaker '{actor.Id.ToString()}' has script '{actorScriptId}' local '{target}', but no runtime local buffer.");

            var locals = entityManager.GetBuffer<MorrowindScriptLocalValue>(session.SpeakerEntity);
            if ((uint)localIndex >= (uint)locals.Length)
                throw new InvalidOperationException($"[VVardenfell][Dialogue] speaker '{actor.Id.ToString()}' local buffer is too small for script '{actorScriptId}' local '{target}'.");

            float value = EvaluateSetExpression(expression, locals[localIndex].FloatValue);
            locals[localIndex] = BuildScriptValue(value, valueKind);
            return true;
        }

        static bool TryApplyActorLocalSet(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string target,
            SetExpression expression,
            out bool localResolved)
        {
            localResolved = false;
            if (!TrySplitActorLocalTarget(target, out string actorId, out string localName))
                return false;

            if (!TryFindActorLocal(ref contentBlob, actorId, localName, out var expectedActorHandle, out int localIndex, out byte valueKind, out string scriptId))
                return false;

            localResolved = true;
            if (!TryResolveAiCommandTarget(ref contentBlob, entityManager, ref session, actorId, out Entity targetEntity, out _))
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
            ref RuntimeContentBlob contentBlob,
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

            for (int i = 0; i < contentBlob.Actors.Length; i++)
            {
                ref var actor = ref contentBlob.Actors[i];
                if (!ActorIdMatches(ref actor, normalizedActorId))
                    continue;

                string actorScriptId = actor.ScriptId.ToString();
                if (string.IsNullOrWhiteSpace(actorScriptId)
                    || !RuntimeContentBlobUtility.TryGetMorrowindScriptProgramHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(actorScriptId), out var programHandle)
                    || !programHandle.IsValid)
                {
                    return false;
                }

                ref var program = ref RuntimeContentBlobUtility.Get(ref contentBlob, programHandle);
                ref var localsDef = ref RuntimeContentBlobUtility.GetMorrowindScriptLocals(ref contentBlob, programHandle);
                for (int local = 0; local < program.LocalCount; local++)
                {
                    int absoluteLocalIndex = program.FirstLocalIndex + local;
                    if (!string.Equals(localsDef[absoluteLocalIndex].Name.ToString(), localName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    actorHandle = ActorDefHandle.FromIndex(i);
                    localIndex = local;
                    valueKind = localsDef[absoluteLocalIndex].ValueKind;
                    scriptId = actorScriptId;
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

        static bool TryApplyGlobalSet(ref RuntimeContentBlob contentBlob, EntityManager entityManager, string target, SetExpression expression)
        {
            if (!RuntimeContentBlobUtility.TryGetGlobalHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(target), out var globalHandle)
                || !globalHandle.IsValid)
            {
                return false;
            }

            EntityQuery query = ScriptGlobalQueryCache.Get(entityManager);
            if (query.IsEmptyIgnoreFilter)
                throw new InvalidOperationException($"[VVardenfell][Dialogue] global '{target}' was set before MWScript globals were bootstrapped.");

            var globals = entityManager.GetBuffer<MorrowindScriptGlobalValue>(query.GetSingletonEntity());
            if ((uint)globalHandle.Index >= (uint)globals.Length)
                throw new InvalidOperationException($"[VVardenfell][Dialogue] global buffer is too small for '{target}'.");

            ref var global = ref RuntimeContentBlobUtility.GetGlobal(ref contentBlob, globalHandle);
            float value = EvaluateSetExpression(expression, globals[globalHandle.Index].FloatValue);
            globals[globalHandle.Index] = BuildGlobalValue(value, ResolveGlobalKind(ref global));
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

        static byte ResolveGlobalKind(ref RuntimeGenericRecordDefBlob global)
        {
            string name = global.Name.ToString();
            if (!string.IsNullOrWhiteSpace(name) && name[0] == 'f')
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

            EntityQuery query = PlayerCrimeWriteQueryCache.Get(entityManager);
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
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            DynamicBuffer<MorrowindScriptJailRequest> jailRequests,
            string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 1 || !string.Equals(tokens[0], "gotojail", StringComparison.OrdinalIgnoreCase))
                return false;

            EntityQuery query = PlayerCrimeWriteQueryCache.Get(entityManager);
            if (query.IsEmptyIgnoreFilter)
                return false;

            var entity = query.GetSingletonEntity();
            var crime = entityManager.GetComponentData<PlayerCrimeState>(entity);
            int bounty = math.max(0, crime.Bounty);
            crime.Bounty = 0;
            crime.PaidCrimeId = crime.CurrentCrimeId;
            entityManager.SetComponentData(entity, crime);
            SheathePlayerWeapon(entityManager, entity);

            int daysMod = RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref contentBlob, RuntimeContentKnownHashes.iDaysinPrisonMod);
            if (daysMod <= 0)
                throw new InvalidOperationException("[VVardenfell][Dialogue] GMST iDaysinPrisonMod must be greater than zero.");

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

            EntityQuery visualQuery = LocalPlayerVisualWeaponQueryCache.Get(entityManager);

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

        static bool TryApplyPlayerSkillResult(DynamicBuffer<PlayerSkillMutationRequest> playerSkillRequests, string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 2 || !float.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!MorrowindActorSkillTextUtility.TryResolveSkillCommand(command, out byte skill, out byte mutation)
                || mutation != MorrowindActorSkillTextUtility.MutationMod
                || (!string.IsNullOrWhiteSpace(target) && !string.Equals(target, "player", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            playerSkillRequests.Add(new PlayerSkillMutationRequest
            {
                Skill = skill,
                Kind = mutation,
                Value = value,
            });
            return true;
        }

        static bool TryApplyPlayerAttributeResult(EntityManager entityManager, DynamicBuffer<ActorAttributeMutationRequest> playerAttributeRequests, string line)
        {
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 2 || !float.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!MorrowindActorAttributeTextUtility.TryResolveAttributeCommand(command, out byte attribute, out byte mutation)
                || mutation != MorrowindActorAttributeTextUtility.MutationMod
                || (!string.IsNullOrWhiteSpace(target) && !string.Equals(target, "player", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            Entity player = MorrowindRuntimeTargetResolver.ResolvePlayerEntity(entityManager);
            if (player == Entity.Null)
                return false;

            playerAttributeRequests.Add(new ActorAttributeMutationRequest
            {
                TargetEntity = player,
                TargetPlacedRefId = 0u,
                Attribute = attribute,
                Kind = mutation,
                Value = value,
            });
            return true;
        }

        static bool TryApplyPlayerFactionResult(
            ref RuntimeContentBlob contentBlob,
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

            if (!TryResolveFactionArgument(ref contentBlob, session.SpeakerActor, tokens, modRep, out int factionIndex, out int value))
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
            ref RuntimeContentBlob contentBlob,
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

            if (!TryResolveAiCommandTarget(ref contentBlob, entityManager, ref session, target, out targetEntity, out _))
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
            ref RuntimeContentBlob contentBlob,
            ActorDefHandle speakerActor,
            string[] tokens,
            bool valueThenFaction,
            out int factionIndex,
            out int value)
        {
            factionIndex = -1;
            value = 0;
            string factionId;
            if (valueThenFaction)
            {
                if ((tokens.Length != 2 && tokens.Length != 3) || !int.TryParse(tokens[1], out value))
                    return false;

                factionId = tokens.Length == 3 ? tokens[2] : (speakerActor.IsValid ? RuntimeContentBlobUtility.Get(ref contentBlob, speakerActor).FactionId.ToString() : string.Empty);
            }
            else
            {
                if (tokens.Length > 2)
                    return false;
                factionId = tokens.Length == 2 ? tokens[1] : (speakerActor.IsValid ? RuntimeContentBlobUtility.Get(ref contentBlob, speakerActor).FactionId.ToString() : string.Empty);
            }

            if (string.IsNullOrWhiteSpace(factionId)
                || !RuntimeContentBlobUtility.TryGetFactionHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(factionId), out var factionHandle)
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
            ref RuntimeContentBlob contentBlob,
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

            if (!TryResolveAiCommandTarget(ref contentBlob, entityManager, ref session, target, out Entity targetEntity, out _))
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
            ref RuntimeContentBlob contentBlob,
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
            if (!TryResolveDialogueInventoryCount(ref contentBlob, entityManager, target, remove, itemId, tokens[2], out int count))
                return false;

            int playerLevel = MorrowindLeveledItemResolverUtility.ResolvePlayerLevel(entityManager);
            if (string.Equals(target, "player", StringComparison.OrdinalIgnoreCase))
            {
                Entity playerInventoryEntity = WorldStateEntityQueryUtility.GetSingletonEntity<PlayerTag>(entityManager);
                if (playerInventoryEntity == Entity.Null || !entityManager.HasBuffer<PlayerInventoryItem>(playerInventoryEntity))
                    return false;

                var inventory = entityManager.GetBuffer<PlayerInventoryItem>(playerInventoryEntity);
                bool changed = add
                    ? InventoryMutationUtility.TryAddPlayerItem(ref contentBlob, inventory, itemId, count, playerLevel)
                    : InventoryMutationUtility.TryRemovePlayerItem(ref contentBlob, inventory, itemId, count);
                if (changed)
                    PlayerEncumbranceDirtyUtility.MarkPlayerDirty(entityManager, playerInventoryEntity);
                return changed;
            }

            if (string.IsNullOrWhiteSpace(target))
            {
                return TryApplyActorInventoryResult(
                    ref contentBlob,
                    entityManager,
                    session.SpeakerEntity,
                    session.SpeakerPlacedRefId,
                    itemId,
                    count,
                    playerLevel,
                    add);
            }

            if (TryResolveAiCommandTarget(ref contentBlob, entityManager, ref session, target, out Entity actorEntity, out uint actorPlacedRefId)
                && TryApplyActorInventoryResult(ref contentBlob, entityManager, actorEntity, actorPlacedRefId, itemId, count, playerLevel, add))
            {
                return true;
            }

            if (!TryResolveExplicitRefTarget(ref contentBlob, activeExplicitRefs, target, out Entity explicitEntity, out uint placedRefId))
                return false;

            if (explicitEntity == Entity.Null
                && !TryFindEntityByPlacedRef(entityManager, placedRefId, out explicitEntity))
            {
                return false;
            }

            if (TryApplyActorInventoryResult(ref contentBlob, entityManager, explicitEntity, placedRefId, itemId, count, playerLevel, add))
                return true;

            return TryApplyContainerInventoryResult(ref contentBlob, entityManager, explicitEntity, placedRefId, itemId, count, playerLevel, add);
        }

        static bool TryResolveDialogueInventoryCount(
            ref RuntimeContentBlob contentBlob,
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
                return TryCalculateCrimeGold(ref contentBlob, bounty, "fCrimeGoldDiscountMult", out count);

            if (string.Equals(countToken, "crimegoldturnin", StringComparison.OrdinalIgnoreCase))
                return TryCalculateCrimeGold(ref contentBlob, bounty, "fCrimeGoldTurnInMult", out count);

            return false;
        }

        static bool TryReadPlayerCrime(EntityManager entityManager, out PlayerCrimeState crime)
        {
            EntityQuery query = PlayerCrimeReadQueryCache.Get(entityManager);
            if (query.IsEmptyIgnoreFilter)
            {
                crime = default;
                return false;
            }

            crime = entityManager.GetComponentData<PlayerCrimeState>(query.GetSingletonEntity());
            return true;
        }

        static bool TryCalculateCrimeGold(ref RuntimeContentBlob contentBlob, int bounty, string gmstId, out int value)
        {
            value = 0;
            float multiplier = RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(gmstId));

            value = (int)(bounty * multiplier);
            if (bounty > 0 && value < 1)
                value = 1;
            return true;
        }

        static bool TryApplyActorInventoryResult(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            Entity actorEntity,
            uint placedRefId,
            string itemId,
            int count,
            int playerLevel,
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
                ? InventoryMutationUtility.TryAddActorItem(ref contentBlob, actorInventory, itemId, count, playerLevel, resolutionSeed)
                : InventoryMutationUtility.TryRemoveActorItem(ref contentBlob, actorInventory, itemId, count);
        }

        static bool TryApplyContainerInventoryResult(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            Entity containerEntity,
            uint placedRefId,
            string itemId,
            int count,
            int playerLevel,
            bool add)
        {
            if (containerEntity == Entity.Null
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
                return TryApplyContainerAddItems(ref contentBlob, entityManager, containerEntity, placedRefId, itemId, count, playerLevel);

            if (!ContainerLootUtility.TryResolveDirectCarryable(ref contentBlob, NormalizeGoldId(itemId), out ContentReference removeContent, out _))
                return false;

            return TryApplyContainerDelta(ref contentBlob, entityManager, containerEntity, placedRefId, removeContent, -count, playerLevel);
        }

        static bool TryApplyContainerAddItems(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            Entity containerEntity,
            uint placedRefId,
            string itemId,
            int count,
            int playerLevel)
        {
            string normalizedId = NormalizeGoldId(itemId);
            ulong idHash = RuntimeContentStableHash.HashId(normalizedId);
            if (MorrowindLeveledItemResolverUtility.TryResolveDirectCarryableByIdHash(ref contentBlob, idHash, out ContentReference directContent))
                return TryApplyContainerDelta(ref contentBlob, entityManager, containerEntity, placedRefId, directContent, count, playerLevel);

            if (!RuntimeContentBlobUtility.TryGetItemLeveledListHandleByIdHash(ref contentBlob, idHash, out ItemLeveledListDefHandle listHandle))
                return false;

            bool changed = false;
            using var resolvedItems = new NativeList<MorrowindResolvedLeveledItem>(Allocator.Temp);
            MorrowindLeveledItemResolverUtility.ResolveIntoInventory(
                ref contentBlob,
                listHandle,
                playerLevel,
                MorrowindLeveledItemResolverUtility.BuildResolutionSeed(placedRefId, 0, 0),
                count,
                resolvedItems);
            for (int i = 0; i < resolvedItems.Length; i++)
            {
                var resolved = resolvedItems[i];
                changed |= TryApplyContainerDelta(ref contentBlob, entityManager, containerEntity, placedRefId, resolved.Content, resolved.Count, playerLevel);
            }

            return changed;
        }

        static bool TryApplyContainerDelta(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            Entity containerEntity,
            uint placedRefId,
            ContentReference content,
            int deltaCount,
            int playerLevel)
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
            EnsureContainerSessionInitialized(ref contentBlob, journal, headers, items, placedRefId, authoring.Definition, playerLevel);
            ContainerLootUtility.ApplyContainerDelta(items, placedRefId, content, deltaCount);
            WorldJournalUtility.AppendContainerDelta(entityManager, placedRefId, content, deltaCount);
            return true;
        }

        static void EnsureContainerSessionInitialized(
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<WorldJournalEntry> journal,
            DynamicBuffer<ContainerSessionHeader> headers,
            DynamicBuffer<ContainerSessionItem> items,
            uint placedRefId,
            ContainerDefHandle definition,
            int playerLevel)
        {
            if (ContainerLootUtility.FindHeaderIndex(headers, placedRefId) >= 0)
                return;

            headers.Add(new ContainerSessionHeader
            {
                PlacedRefId = placedRefId,
                Definition = definition,
            });

            ContainerLootUtility.MaterializeContainerContents(ref contentBlob, items, placedRefId, definition, playerLevel);
            WorldJournalUtility.ApplyContainerDeltas(placedRefId, journal, items);
        }

        static bool TryFindEntityByPlacedRef(EntityManager entityManager, uint placedRefId, out Entity entity)
        {
            EntityQuery query = PlacedRefIdentityQueryCache.Get(entityManager);
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
            ref RuntimeContentBlob contentBlob,
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

            if (!RuntimeContentBlobUtility.TryGetSpellHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(tokens[1]), out var spellHandle)
                || !spellHandle.IsValid)
            {
                return false;
            }

            if (!TryResolveActorSpellTarget(ref contentBlob, entityManager, ref session, target, out Entity actorEntity, out uint actorPlacedRefId))
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
            ref RuntimeContentBlob contentBlob,
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

            if (!TryResolveActorSpellTarget(ref contentBlob, entityManager, ref session, casterTarget, out Entity casterEntity, out uint casterPlacedRefId)
                || IsPlayerEntity(entityManager, casterEntity)
                || !RuntimeContentBlobUtility.TryGetSpellHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(tokens[1]), out var spellHandle)
                || !spellHandle.IsValid
                || !TryResolveActorSpellTarget(ref contentBlob, entityManager, ref session, tokens[2], out Entity targetEntity, out uint targetPlacedRefId))
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
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            ref MorrowindDialogueSession session,
            string target,
            out Entity actorEntity)
        {
            return TryResolveActorSpellTarget(ref contentBlob, entityManager, ref session, target, out actorEntity, out _);
        }

        static bool TryResolveActorSpellTarget(
            ref RuntimeContentBlob contentBlob,
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

            return TryResolveAiCommandTarget(ref contentBlob, entityManager, ref session, target, out actorEntity, out actorPlacedRefId)
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
            ref RuntimeContentBlob contentBlob,
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
                || !RuntimeContentBlobUtility.TryGetDialogueHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(id), out var handle)
                || !handle.IsValid)
                return false;

            ref var dialogue = ref RuntimeContentBlobUtility.Get(ref contentBlob, handle);
            if (dialogue.Type != DialogueDefType.Journal)
                return false;

            int infoIndex = ResolveJournalInfoIndex(ref contentBlob, handle.Index, stage, out byte questStatus);
            return MorrowindQuestJournalUtility.TryApplyRequest(
                ref contentBlob,
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
            ref RuntimeContentBlob contentBlob,
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
                || !RuntimeContentBlobUtility.TryGetDialogueHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(id), out var handle)
                || !handle.IsValid)
                return false;

            return MorrowindQuestJournalUtility.TryApplyRequest(
                ref contentBlob,
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

        static bool TryApplyAddTopicResult(ref RuntimeContentBlob contentBlob, DynamicBuffer<MorrowindKnownDialogueTopic> knownTopics, string line)
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
            return RuntimeContentBlobUtility.TryGetDialogueHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(id), out var handle)
                   && handle.IsValid
                   && MorrowindDialogueUtility.TryAddTopic(ref contentBlob, knownTopics, handle.Index);
        }

        static int ResolveJournalInfoIndex(ref RuntimeContentBlob contentBlob, int dialogueIndex, int stage, out byte questStatus)
        {
            questStatus = 0;
            ref var dialogue = ref contentBlob.Dialogues[dialogueIndex];
            int end = Math.Min(contentBlob.DialogueInfos.Length, dialogue.FirstInfoIndex + dialogue.InfoCount);
            for (int i = dialogue.FirstInfoIndex; i < end; i++)
            {
                ref var info = ref contentBlob.DialogueInfos[i];
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

        static class PlayerQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<PlayerTag>());
        }

        static class ScriptGlobalQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadWrite<MorrowindScriptGlobalValue>());
        }

        static class PlayerCrimeWriteQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadWrite<PlayerCrimeState>());
        }

        static class PlayerCrimeReadQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<PlayerCrimeState>());
        }

        static class LocalPlayerVisualWeaponQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<LocalPlayerVisual>(), ComponentType.ReadWrite<ActorWeaponAnimationState>());
        }

        static class PlacedRefIdentityQueryCache
        {
            static World s_World;
            static EntityQuery s_Query;
            static bool s_QueryCreated;

            public static EntityQuery Get(EntityManager entityManager)
                => GetQuery(entityManager, ref s_World, ref s_Query, ref s_QueryCreated, ComponentType.ReadOnly<PlacedRefIdentity>());
        }

        static EntityQuery GetQuery(
            EntityManager entityManager,
            ref World worldCache,
            ref EntityQuery queryCache,
            ref bool queryCreated,
            params ComponentType[] componentTypes)
        {
            World world = entityManager.World;
            if (queryCreated && worldCache == world)
                return queryCache;

            worldCache = world;
            queryCache = entityManager.CreateEntityQuery(componentTypes);
            queryCreated = true;
            return queryCache;
        }
    }
}
