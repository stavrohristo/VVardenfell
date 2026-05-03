using Unity.Entities;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindInputSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellActionSystem))]
    public partial class MorrowindDialogueSessionSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<RuntimeShellState>();
            RequireForUpdate<MorrowindDialogueSession>();
            RequireForUpdate<MorrowindDialogueResponseRequest>();
            RequireForUpdate<MorrowindDialogueState>();
            RequireForUpdate<MorrowindTimeState>();
            RequireForUpdate<ActiveExplicitRefLookup>();
        }

        protected override void OnUpdate()
        {
            var contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
                return;

            Entity shellEntity = SystemAPI.GetSingletonEntity<RuntimeShellState>();
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindDialogueState>();
            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var session = ref SystemAPI.GetSingletonRW<MorrowindDialogueSession>().ValueRW;
            ref var request = ref SystemAPI.GetSingletonRW<MorrowindDialogueResponseRequest>().ValueRW;
            var lines = EntityManager.GetBuffer<MorrowindDialogueSessionLine>(shellEntity);
            var choices = EntityManager.GetBuffer<MorrowindDialogueChoice>(shellEntity);
            var knownTopics = EntityManager.GetBuffer<MorrowindKnownDialogueTopic>(runtimeEntity);
            var topicEntries = EntityManager.GetBuffer<MorrowindTopicJournalEntry>(runtimeEntity);
            var factionReactionOverrides = EntityManager.GetBuffer<MorrowindFactionReactionOverride>(runtimeEntity);
            var scriptStartRequests = EntityManager.GetBuffer<MorrowindScriptStartRequest>(runtimeEntity);
            var refStateRequests = EntityManager.GetBuffer<MorrowindScriptRefStateRequest>(runtimeEntity);
            var transformRequests = EntityManager.GetBuffer<MorrowindScriptTransformRequest>(runtimeEntity);
            var jailRequests = EntityManager.GetBuffer<MorrowindScriptJailRequest>(runtimeEntity);
            var movementFlagRequests = EntityManager.GetBuffer<MorrowindScriptMovementFlagRequest>(runtimeEntity);
            var placeAtRequests = EntityManager.GetBuffer<MorrowindScriptPlaceAtRequest>(runtimeEntity);
            var castRequests = EntityManager.GetBuffer<ScriptedCastRequest>(runtimeEntity);
            var actorSpellRequests = EntityManager.GetBuffer<ActorSpellMutationRequest>(runtimeEntity);
            var shellMessageBoxRequests = EntityManager.GetBuffer<ShellMessageBoxRequest>(runtimeEntity);
            var globalMapRevealRequests = EntityManager.GetBuffer<GlobalMapRevealRequest>(runtimeEntity);
            var forceGreetingRequests = EntityManager.GetBuffer<ActorForceGreetingRequest>(runtimeEntity);
            var playerReputationRequests = EntityManager.GetBuffer<PlayerReputationMutationRequest>(runtimeEntity);
            var playerFactionRequests = EntityManager.GetBuffer<PlayerFactionMutationRequest>(runtimeEntity);
            var actorFactionRequests = EntityManager.GetBuffer<ActorFactionRankMutationRequest>(runtimeEntity);
            ref var dialogueState = ref SystemAPI.GetSingletonRW<MorrowindDialogueState>().ValueRW;
            ref var questState = ref SystemAPI.GetSingletonRW<MorrowindQuestJournalState>().ValueRW;
            var questStates = EntityManager.GetBuffer<MorrowindQuestJournalIndex>(runtimeEntity);
            var questEntries = EntityManager.GetBuffer<MorrowindQuestJournalEntry>(runtimeEntity);
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
                if (!TryAppendGreeting(contentDb, activeExplicitRefs, ref shell, ref session, knownTopics, topicEntries, factionReactionOverrides, scriptStartRequests, refStateRequests, transformRequests, jailRequests, movementFlagRequests, placeAtRequests, castRequests, actorSpellRequests, shellMessageBoxRequests, globalMapRevealRequests, forceGreetingRequests, playerReputationRequests, playerFactionRequests, actorFactionRequests, lines, choices, ref dialogueState, ref questState, time, questStates, questEntries))
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
                TryAppendTopic(
                    contentDb,
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
                TryAppendChoiceAnswer(
                    contentDb,
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

        bool TryAppendGreeting(
            RuntimeContentDatabase contentDb,
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
            for (int dialogueIndex = 0; dialogueIndex < contentDb.DialogueCount; dialogueIndex++)
            {
                ref readonly var dialogue = ref contentDb.Data.Dialogues[dialogueIndex];
                if (dialogue.Type != DialogueDefType.Greeting)
                    continue;

                if (!MorrowindDialogueFilterUtility.TryFindFirstMatchingInfo(
                        contentDb,
                        EntityManager,
                        session.SpeakerEntity,
                        session.SpeakerActor,
                        dialogueIndex,
                        -1,
                        out int infoIndex,
                        out string unsupportedReason))
                {
                    if (!string.IsNullOrWhiteSpace(unsupportedReason))
                        Debug.LogWarning($"[VVardenfell][Dialogue] greeting '{dialogue.Id}' skipped: {unsupportedReason}");
                    continue;
                }

                AppendResponseLine(lines, dialogueIndex, infoIndex, showTitle: false);
                session.LastInfoIndex = infoIndex;
                session.SelectedTopicDialogueIndex = dialogueIndex;
                string response = contentDb.Data.DialogueInfos[infoIndex].Response;
                MorrowindDialogueUtility.AddTopicsFromResponse(contentDb, knownTopics, response, EntityManager, session.SpeakerEntity, session.SpeakerActor);
                if (MorrowindDialogueResultScriptUtility.ExecuteSupported(contentDb, EntityManager, activeExplicitRefs, ref dialogueState, ref questState, time, knownTopics, topicEntries, factionReactionOverrides, scriptStartRequests, refStateRequests, transformRequests, jailRequests, movementFlagRequests, placeAtRequests, castRequests, actorSpellRequests, shellMessageBoxRequests, globalMapRevealRequests, forceGreetingRequests, playerReputationRequests, playerFactionRequests, actorFactionRequests, questStates, questEntries, choices, response, contentDb.Data.DialogueInfos[infoIndex].ResultScript, ref shell, ref session))
                    CloseDialogue(ref shell, ref session, lines, choices);
                return true;
            }

            return false;
        }

        void TryAppendTopic(
            RuntimeContentDatabase contentDb,
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

            if ((uint)dialogueIndex >= (uint)contentDb.DialogueCount
                || (uint)dialogueIndex >= (uint)knownTopics.Length
                || knownTopics[dialogueIndex].Known == 0)
                return;

            ref readonly var dialogue = ref contentDb.Data.Dialogues[dialogueIndex];
            if (dialogue.Type != DialogueDefType.Topic)
                return;

            if (!MorrowindDialogueFilterUtility.TryFindFirstMatchingInfo(
                    contentDb,
                    EntityManager,
                    session.SpeakerEntity,
                    session.SpeakerActor,
                    dialogueIndex,
                    -1,
                    out int infoIndex,
                    out string unsupportedReason))
            {
                if (!string.IsNullOrWhiteSpace(unsupportedReason))
                    Debug.LogWarning($"[VVardenfell][Dialogue] topic '{dialogue.Id}' skipped: {unsupportedReason}");
                return;
            }

            AppendResponseLine(lines, dialogueIndex, infoIndex, showTitle: true);
            session.LastInfoIndex = infoIndex;
            session.SelectedTopicDialogueIndex = dialogueIndex;
            MorrowindDialogueUtility.TryRecordTopicEntry(
                contentDb,
                ref dialogueState,
                time,
                topicEntries,
                dialogueIndex,
                infoIndex,
                session.SpeakerPlacedRefId,
                session.SpeakerId);
            string response = contentDb.Data.DialogueInfos[infoIndex].Response;
            MorrowindDialogueUtility.AddTopicsFromResponse(contentDb, knownTopics, response, EntityManager, session.SpeakerEntity, session.SpeakerActor);
            if (MorrowindDialogueResultScriptUtility.ExecuteSupported(contentDb, EntityManager, activeExplicitRefs, ref dialogueState, ref questState, time, knownTopics, topicEntries, factionReactionOverrides, scriptStartRequests, refStateRequests, transformRequests, jailRequests, movementFlagRequests, placeAtRequests, castRequests, actorSpellRequests, shellMessageBoxRequests, globalMapRevealRequests, forceGreetingRequests, playerReputationRequests, playerFactionRequests, actorFactionRequests, questStates, questEntries, choices, response, contentDb.Data.DialogueInfos[infoIndex].ResultScript, ref shell, ref session))
                CloseDialogue(ref shell, ref session, lines, choices);
        }

        void TryAppendChoiceAnswer(
            RuntimeContentDatabase contentDb,
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

            if ((uint)dialogueIndex >= (uint)contentDb.DialogueCount)
                return;

            ref readonly var dialogue = ref contentDb.Data.Dialogues[dialogueIndex];
            if (dialogue.Type != DialogueDefType.Topic && dialogue.Type != DialogueDefType.Greeting)
                return;

            if (!MorrowindDialogueFilterUtility.TryFindFirstMatchingInfo(
                    contentDb,
                    EntityManager,
                    session.SpeakerEntity,
                    session.SpeakerActor,
                    dialogueIndex,
                    choiceValue,
                    out int infoIndex,
                    out string unsupportedReason))
            {
                if (!string.IsNullOrWhiteSpace(unsupportedReason))
                    Debug.LogWarning($"[VVardenfell][Dialogue] choice for '{dialogue.Id}' skipped: {unsupportedReason}");
                return;
            }

            AppendResponseLine(lines, dialogueIndex, infoIndex, showTitle: false);
            session.LastInfoIndex = infoIndex;
            session.SelectedTopicDialogueIndex = dialogueIndex;
            if (dialogue.Type == DialogueDefType.Topic)
            {
                MorrowindDialogueUtility.TryRecordTopicEntry(
                    contentDb,
                    ref dialogueState,
                    time,
                    topicEntries,
                    dialogueIndex,
                    infoIndex,
                    session.SpeakerPlacedRefId,
                    session.SpeakerId);
            }

            string response = contentDb.Data.DialogueInfos[infoIndex].Response;
            MorrowindDialogueUtility.AddTopicsFromResponse(contentDb, knownTopics, response, EntityManager, session.SpeakerEntity, session.SpeakerActor);
            if (MorrowindDialogueResultScriptUtility.ExecuteSupported(contentDb, EntityManager, activeExplicitRefs, ref dialogueState, ref questState, time, knownTopics, topicEntries, factionReactionOverrides, scriptStartRequests, refStateRequests, transformRequests, jailRequests, movementFlagRequests, placeAtRequests, castRequests, actorSpellRequests, shellMessageBoxRequests, globalMapRevealRequests, forceGreetingRequests, playerReputationRequests, playerFactionRequests, actorFactionRequests, questStates, questEntries, choices, response, contentDb.Data.DialogueInfos[infoIndex].ResultScript, ref shell, ref session))
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
