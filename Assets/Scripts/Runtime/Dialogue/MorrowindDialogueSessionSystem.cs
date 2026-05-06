using Unity.Entities;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellActionSystem))]
    public partial struct MorrowindDialogueSessionSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<RuntimeShellState>();
            systemState.RequireForUpdate<MorrowindDialogueSession>();
            systemState.RequireForUpdate<MorrowindDialogueResponseRequest>();
            systemState.RequireForUpdate<MorrowindDialogueState>();
            systemState.RequireForUpdate<MorrowindTimeState>();
            systemState.RequireForUpdate<ActiveExplicitRefLookup>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;

            Entity shellEntity = SystemAPI.GetSingletonEntity<RuntimeShellState>();
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindDialogueState>();
            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var session = ref SystemAPI.GetSingletonRW<MorrowindDialogueSession>().ValueRW;
            ref var request = ref SystemAPI.GetSingletonRW<MorrowindDialogueResponseRequest>().ValueRW;
            var lines = systemState.EntityManager.GetBuffer<MorrowindDialogueSessionLine>(shellEntity);
            var choices = systemState.EntityManager.GetBuffer<MorrowindDialogueChoice>(shellEntity);
            var knownTopics = systemState.EntityManager.GetBuffer<MorrowindKnownDialogueTopic>(runtimeEntity);
            var topicEntries = systemState.EntityManager.GetBuffer<MorrowindTopicJournalEntry>(runtimeEntity);
            var factionReactionOverrides = systemState.EntityManager.GetBuffer<MorrowindFactionReactionOverride>(runtimeEntity);
            var scriptStartRequests = systemState.EntityManager.GetBuffer<MorrowindScriptStartRequest>(runtimeEntity);
            var refStateRequests = systemState.EntityManager.GetBuffer<MorrowindScriptRefStateRequest>(runtimeEntity);
            var transformRequests = systemState.EntityManager.GetBuffer<MorrowindScriptTransformRequest>(runtimeEntity);
            var jailRequests = systemState.EntityManager.GetBuffer<MorrowindScriptJailRequest>(runtimeEntity);
            var movementFlagRequests = systemState.EntityManager.GetBuffer<MorrowindScriptMovementFlagRequest>(runtimeEntity);
            var placeAtRequests = systemState.EntityManager.GetBuffer<MorrowindScriptPlaceAtRequest>(runtimeEntity);
            var castRequests = systemState.EntityManager.GetBuffer<ScriptedCastRequest>(runtimeEntity);
            var actorSpellRequests = systemState.EntityManager.GetBuffer<ActorSpellMutationRequest>(runtimeEntity);
            var shellMessageBoxRequests = systemState.EntityManager.GetBuffer<ShellMessageBoxRequest>(runtimeEntity);
            var globalMapRevealRequests = systemState.EntityManager.GetBuffer<GlobalMapRevealRequest>(runtimeEntity);
            var forceGreetingRequests = systemState.EntityManager.GetBuffer<ActorForceGreetingRequest>(runtimeEntity);
            var playerReputationRequests = systemState.EntityManager.GetBuffer<PlayerReputationMutationRequest>(runtimeEntity);
            var playerAttributeRequests = systemState.EntityManager.GetBuffer<ActorAttributeMutationRequest>(runtimeEntity);
            var playerSkillRequests = systemState.EntityManager.GetBuffer<PlayerSkillMutationRequest>(runtimeEntity);
            var playerFactionRequests = systemState.EntityManager.GetBuffer<PlayerFactionMutationRequest>(runtimeEntity);
            var actorFactionRequests = systemState.EntityManager.GetBuffer<ActorFactionRankMutationRequest>(runtimeEntity);
            ref var dialogueState = ref SystemAPI.GetSingletonRW<MorrowindDialogueState>().ValueRW;
            ref var questState = ref SystemAPI.GetSingletonRW<MorrowindQuestJournalState>().ValueRW;
            var questStates = systemState.EntityManager.GetBuffer<MorrowindQuestJournalIndex>(runtimeEntity);
            var questEntries = systemState.EntityManager.GetBuffer<MorrowindQuestJournalEntry>(runtimeEntity);
            var time = SystemAPI.GetSingleton<MorrowindTimeState>();
            var activeExplicitRefs = SystemAPI.GetSingleton<ActiveExplicitRefLookup>();

            if (shell.DialogueOpen == 0 || session.Active == 0)
            {
                request.Pending = 0;
                return;
            }

            if (session.NeedsGreeting != 0)
            {
                session.NeedsGreeting = 0;
                lines.Clear();
                choices.Clear();
                if (!TryAppendGreeting(ref systemState, ref contentBlob, activeExplicitRefs, ref shell, ref session, knownTopics, topicEntries, factionReactionOverrides, scriptStartRequests, refStateRequests, transformRequests, jailRequests, movementFlagRequests, placeAtRequests, castRequests, actorSpellRequests, shellMessageBoxRequests, globalMapRevealRequests, forceGreetingRequests, playerReputationRequests, playerAttributeRequests, playerSkillRequests, playerFactionRequests, actorFactionRequests, lines, choices, ref dialogueState, ref questState, time, questStates, questEntries))
                {
                    Debug.LogWarning($"[VVardenfell][Dialogue] no greeting matched speaker '{session.SpeakerId}'.");
                    RuntimeShellStateUtility.CloseDialogue(ref shell);
                    session = CreateInactiveSession();
                    return;
                }
            }

            if (request.Pending == 0)
                return;

            var action = (MorrowindDialogueResponseAction)request.Action;
            int requestedDialogueIndex = request.DialogueIndex;
            int requestedChoiceValue = request.ChoiceValue;
            request.Pending = 0;
            request.Action = 0;
            request.DialogueIndex = -1;
            request.ChoiceValue = 0;

            if (action == MorrowindDialogueResponseAction.Goodbye)
            {
                RuntimeShellStateUtility.CloseDialogue(ref shell);
                session = CreateInactiveSession();
                lines.Clear();
                choices.Clear();
                return;
            }

            if (action == MorrowindDialogueResponseAction.SelectTopic)
            {
                TryAppendTopic(ref systemState, 
                    ref contentBlob,
                    activeExplicitRefs,
                    requestedDialogueIndex,
                    ref shell,
                    ref session,
                    knownTopics,
                    topicEntries,
                    factionReactionOverrides,
                    scriptStartRequests,
                    refStateRequests,
                    transformRequests,
                    jailRequests,
                    movementFlagRequests,
                    placeAtRequests,
                    castRequests,
                    actorSpellRequests,
                    shellMessageBoxRequests,
                    globalMapRevealRequests,
                    forceGreetingRequests,
                    playerReputationRequests,
                    playerAttributeRequests,
                    playerSkillRequests,
                    playerFactionRequests,
                    actorFactionRequests,
                    lines,
                    ref dialogueState,
                    ref questState,
                    time,
                    questStates,
                    questEntries,
                    choices);
            }
            else if (action == MorrowindDialogueResponseAction.AnswerChoice)
            {
                TryAppendChoiceAnswer(ref systemState, 
                    ref contentBlob,
                    activeExplicitRefs,
                    requestedChoiceValue,
                    ref shell,
                    ref session,
                    knownTopics,
                    topicEntries,
                    factionReactionOverrides,
                    scriptStartRequests,
                    refStateRequests,
                    transformRequests,
                    jailRequests,
                    movementFlagRequests,
                    placeAtRequests,
                    castRequests,
                    actorSpellRequests,
                    shellMessageBoxRequests,
                    globalMapRevealRequests,
                    forceGreetingRequests,
                    playerReputationRequests,
                    playerAttributeRequests,
                    playerSkillRequests,
                    playerFactionRequests,
                    actorFactionRequests,
                    lines,
                    choices,
                    ref dialogueState,
                    ref questState,
                    time,
                    questStates,
                    questEntries);
            }
        }

        bool TryAppendGreeting(ref SystemState systemState, 
            ref RuntimeContentBlob contentBlob,
            ActiveExplicitRefLookup activeExplicitRefs,
            ref RuntimeShellState shell,
            ref MorrowindDialogueSession session,
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
            DynamicBuffer<MorrowindDialogueSessionLine> lines,
            DynamicBuffer<MorrowindDialogueChoice> choices,
            ref MorrowindDialogueState dialogueState,
            ref MorrowindQuestJournalState questState,
            MorrowindTimeState time,
            DynamicBuffer<MorrowindQuestJournalIndex> questStates,
            DynamicBuffer<MorrowindQuestJournalEntry> questEntries)
        {
            for (int dialogueIndex = 0; dialogueIndex < contentBlob.Dialogues.Length; dialogueIndex++)
            {
                ref var dialogue = ref contentBlob.Dialogues[dialogueIndex];
                if (dialogue.Type != DialogueDefType.Greeting)
                    continue;

                if (!MorrowindDialogueFilterUtility.TryFindFirstMatchingInfo(
                        ref contentBlob,
                        systemState.EntityManager,
                        session.SpeakerEntity,
                        session.SpeakerActor,
                        dialogueIndex,
                        -1,
                        out int infoIndex,
                        out string unsupportedReason))
                {
                    if (!string.IsNullOrWhiteSpace(unsupportedReason))
                        Debug.LogWarning($"[VVardenfell][Dialogue] greeting '{dialogue.Id.ToString()}' skipped: {unsupportedReason}");
                    continue;
                }

                AppendResponseLine(lines, dialogueIndex, infoIndex, showTitle: false);
                session.LastInfoIndex = infoIndex;
                session.SelectedTopicDialogueIndex = dialogueIndex;
                ref var info = ref contentBlob.DialogueInfos[infoIndex];
                string response = info.Response.ToString();
                MorrowindDialogueUtility.AddTopicsFromResponse(ref contentBlob, knownTopics, response, systemState.EntityManager, session.SpeakerEntity, session.SpeakerActor);
                if (MorrowindDialogueResultScriptUtility.ExecuteSupported(ref contentBlob, systemState.EntityManager, activeExplicitRefs, ref dialogueState, ref questState, time, knownTopics, topicEntries, factionReactionOverrides, scriptStartRequests, refStateRequests, transformRequests, jailRequests, movementFlagRequests, placeAtRequests, castRequests, actorSpellRequests, shellMessageBoxRequests, globalMapRevealRequests, forceGreetingRequests, playerReputationRequests, playerAttributeRequests, playerSkillRequests, playerFactionRequests, actorFactionRequests, questStates, questEntries, choices, response, info.ResultScript.ToString(), ref shell, ref session))
                    CloseDialogue(ref shell, ref session, lines, choices);
                return true;
            }

            return false;
        }

        void TryAppendTopic(ref SystemState systemState, 
            ref RuntimeContentBlob contentBlob,
            ActiveExplicitRefLookup activeExplicitRefs,
            int dialogueIndex,
            ref RuntimeShellState shell,
            ref MorrowindDialogueSession session,
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
            DynamicBuffer<MorrowindDialogueSessionLine> lines,
            ref MorrowindDialogueState dialogueState,
            ref MorrowindQuestJournalState questState,
            MorrowindTimeState time,
            DynamicBuffer<MorrowindQuestJournalIndex> questStates,
            DynamicBuffer<MorrowindQuestJournalEntry> questEntries,
            DynamicBuffer<MorrowindDialogueChoice> choices)
        {
            if (session.ChoiceActive != 0)
                return;

            if ((uint)dialogueIndex >= (uint)contentBlob.Dialogues.Length
                || (uint)dialogueIndex >= (uint)knownTopics.Length
                || knownTopics[dialogueIndex].Known == 0)
                return;

            ref var dialogue = ref contentBlob.Dialogues[dialogueIndex];
            if (dialogue.Type != DialogueDefType.Topic)
                return;

            if (!MorrowindDialogueFilterUtility.TryFindFirstMatchingInfo(
                    ref contentBlob,
                    systemState.EntityManager,
                    session.SpeakerEntity,
                    session.SpeakerActor,
                    dialogueIndex,
                    -1,
                    out int infoIndex,
                    out string unsupportedReason))
            {
                if (!string.IsNullOrWhiteSpace(unsupportedReason))
                    Debug.LogWarning($"[VVardenfell][Dialogue] topic '{dialogue.Id.ToString()}' skipped: {unsupportedReason}");
                return;
            }

            AppendResponseLine(lines, dialogueIndex, infoIndex, showTitle: true);
            session.LastInfoIndex = infoIndex;
            session.SelectedTopicDialogueIndex = dialogueIndex;
            MorrowindDialogueUtility.TryRecordTopicEntry(
                ref contentBlob,
                ref dialogueState,
                time,
                topicEntries,
                dialogueIndex,
                infoIndex,
                session.SpeakerPlacedRefId,
                session.SpeakerId);
            ref var info = ref contentBlob.DialogueInfos[infoIndex];
            string response = info.Response.ToString();
            MorrowindDialogueUtility.AddTopicsFromResponse(ref contentBlob, knownTopics, response, systemState.EntityManager, session.SpeakerEntity, session.SpeakerActor);
            if (MorrowindDialogueResultScriptUtility.ExecuteSupported(ref contentBlob, systemState.EntityManager, activeExplicitRefs, ref dialogueState, ref questState, time, knownTopics, topicEntries, factionReactionOverrides, scriptStartRequests, refStateRequests, transformRequests, jailRequests, movementFlagRequests, placeAtRequests, castRequests, actorSpellRequests, shellMessageBoxRequests, globalMapRevealRequests, forceGreetingRequests, playerReputationRequests, playerAttributeRequests, playerSkillRequests, playerFactionRequests, actorFactionRequests, questStates, questEntries, choices, response, info.ResultScript.ToString(), ref shell, ref session))
                CloseDialogue(ref shell, ref session, lines, choices);
        }

        void TryAppendChoiceAnswer(ref SystemState systemState, 
            ref RuntimeContentBlob contentBlob,
            ActiveExplicitRefLookup activeExplicitRefs,
            int choiceValue,
            ref RuntimeShellState shell,
            ref MorrowindDialogueSession session,
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
            DynamicBuffer<MorrowindDialogueSessionLine> lines,
            DynamicBuffer<MorrowindDialogueChoice> choices,
            ref MorrowindDialogueState dialogueState,
            ref MorrowindQuestJournalState questState,
            MorrowindTimeState time,
            DynamicBuffer<MorrowindQuestJournalIndex> questStates,
            DynamicBuffer<MorrowindQuestJournalEntry> questEntries)
        {
            if (session.ChoiceActive == 0 || choices.Length == 0)
                return;

            bool validChoice = false;
            for (int i = 0; i < choices.Length; i++)
            {
                if (choices[i].Value == choiceValue)
                {
                    validChoice = true;
                    break;
                }
            }

            if (!validChoice)
                return;

            int dialogueIndex = session.ChoiceDialogueIndex;
            session.ChoiceActive = 0;
            session.ChoiceDialogueIndex = -1;
            choices.Clear();

            if ((uint)dialogueIndex >= (uint)contentBlob.Dialogues.Length)
                return;

            ref var dialogue = ref contentBlob.Dialogues[dialogueIndex];
            if (dialogue.Type != DialogueDefType.Topic && dialogue.Type != DialogueDefType.Greeting)
                return;

            if (!MorrowindDialogueFilterUtility.TryFindFirstMatchingInfo(
                    ref contentBlob,
                    systemState.EntityManager,
                    session.SpeakerEntity,
                    session.SpeakerActor,
                    dialogueIndex,
                    choiceValue,
                    out int infoIndex,
                    out string unsupportedReason))
            {
                if (!string.IsNullOrWhiteSpace(unsupportedReason))
                    Debug.LogWarning($"[VVardenfell][Dialogue] choice for '{dialogue.Id.ToString()}' skipped: {unsupportedReason}");
                return;
            }

            AppendResponseLine(lines, dialogueIndex, infoIndex, showTitle: false);
            session.LastInfoIndex = infoIndex;
            session.SelectedTopicDialogueIndex = dialogueIndex;
            if (dialogue.Type == DialogueDefType.Topic)
            {
                MorrowindDialogueUtility.TryRecordTopicEntry(
                    ref contentBlob,
                    ref dialogueState,
                    time,
                    topicEntries,
                    dialogueIndex,
                    infoIndex,
                    session.SpeakerPlacedRefId,
                    session.SpeakerId);
            }

            ref var info = ref contentBlob.DialogueInfos[infoIndex];
            string response = info.Response.ToString();
            MorrowindDialogueUtility.AddTopicsFromResponse(ref contentBlob, knownTopics, response, systemState.EntityManager, session.SpeakerEntity, session.SpeakerActor);
            if (MorrowindDialogueResultScriptUtility.ExecuteSupported(ref contentBlob, systemState.EntityManager, activeExplicitRefs, ref dialogueState, ref questState, time, knownTopics, topicEntries, factionReactionOverrides, scriptStartRequests, refStateRequests, transformRequests, jailRequests, movementFlagRequests, placeAtRequests, castRequests, actorSpellRequests, shellMessageBoxRequests, globalMapRevealRequests, forceGreetingRequests, playerReputationRequests, playerAttributeRequests, playerSkillRequests, playerFactionRequests, actorFactionRequests, questStates, questEntries, choices, response, info.ResultScript.ToString(), ref shell, ref session))
                CloseDialogue(ref shell, ref session, lines, choices);
        }

        static void AppendResponseLine(DynamicBuffer<MorrowindDialogueSessionLine> lines, int dialogueIndex, int infoIndex, bool showTitle)
        {
            lines.Add(new MorrowindDialogueSessionLine
            {
                DialogueIndex = dialogueIndex,
                InfoIndex = infoIndex,
                ShowTitle = showTitle ? (byte)1 : (byte)0,
            });
        }

        static void CloseDialogue(ref RuntimeShellState shell, ref MorrowindDialogueSession session, DynamicBuffer<MorrowindDialogueSessionLine> lines, DynamicBuffer<MorrowindDialogueChoice> choices)
        {
            RuntimeShellStateUtility.CloseDialogue(ref shell);
            session = CreateInactiveSession();
            lines.Clear();
            choices.Clear();
        }

        static MorrowindDialogueSession CreateInactiveSession()
            => new()
            {
                SelectedTopicDialogueIndex = -1,
                ChoiceDialogueIndex = -1,
                LastInfoIndex = -1,
            };
    }
}
