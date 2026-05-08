using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.AI;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Inventory;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Shell;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(RuntimeShellActionSystem))]
    public partial struct MorrowindDialogueSessionSystem : ISystem
    {
        EntityQuery _worldCellBlobQuery;
        MorrowindDialogueFilterUtility.QueryContext _filterQueryContext;

        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<RuntimeShellState>();
            systemState.RequireForUpdate<MorrowindDialogueSession>();
            systemState.RequireForUpdate<MorrowindDialogueResponseRequest>();
            systemState.RequireForUpdate<MorrowindDialogueServiceWindowState>();
            systemState.RequireForUpdate<MorrowindDialogueState>();
            systemState.RequireForUpdate<MorrowindTimeState>();
            systemState.RequireForUpdate<ActiveExplicitRefLookup>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate<RuntimeWorldCellBlobReference>();

            _worldCellBlobQuery = systemState.GetEntityQuery(ComponentType.ReadOnly<RuntimeWorldCellBlobReference>());
            _filterQueryContext = new MorrowindDialogueFilterUtility.QueryContext
            {
                PlayerFaction = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<PlayerFactionMembership>()),
                InteriorTransition = systemState.GetEntityQuery(ComponentType.ReadOnly<InteriorTransitionState>()),
                StreamingConfig = systemState.GetEntityQuery(ComponentType.ReadOnly<StreamingConfig>()),
                ScriptGlobal = systemState.GetEntityQuery(ComponentType.ReadOnly<MorrowindScriptGlobalValue>()),
                QuestJournal = systemState.GetEntityQuery(ComponentType.ReadOnly<MorrowindQuestJournalIndex>()),
                PlayerIdentity = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<ActorIdentitySet>()),
                PlayerCrime = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<PlayerCrimeState>()),
                Player = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>()),
                PlayerVitals = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<ActorVitalSet>()),
                PlayerInventory = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<PlayerInventoryItem>()),
                DialogueFactionReaction = systemState.GetEntityQuery(ComponentType.ReadOnly<MorrowindDialogueState>(), ComponentType.ReadOnly<MorrowindFactionReactionOverride>()),
                PlayerEquipment = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<ActorEquipmentSlot>()),
                PlayerAttribute = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<ActorAttributeSet>()),
                PlayerSkill = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<ActorSkillSet>()),
            };
            MorrowindDialogueResultScriptUtility.PrepareQueries(systemState.EntityManager);
        }

        public void OnUpdate(ref SystemState systemState)
        {
            Entity shellEntity = SystemAPI.GetSingletonEntity<RuntimeShellState>();
            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindDialogueState>();
            ref var shell = ref SystemAPI.GetSingletonRW<RuntimeShellState>().ValueRW;
            ref var session = ref SystemAPI.GetSingletonRW<MorrowindDialogueSession>().ValueRW;
            ref var request = ref SystemAPI.GetSingletonRW<MorrowindDialogueResponseRequest>().ValueRW;

            if (shell.DialogueOpen == 0 || session.Active == 0)
            {
                request.Pending = 0;
                return;
            }

            if (session.NeedsGreeting == 0 && request.Pending == 0)
                return;

            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            var worldCellBlob = RequireWorldCellBlob(_worldCellBlobQuery);
            ref RuntimeWorldCellBlob worldCells = ref worldCellBlob.Value;
            ref MorrowindDialogueFilterUtility.QueryContext filterQueryContext = ref _filterQueryContext;

            var lines = systemState.EntityManager.GetBuffer<MorrowindDialogueSessionLine>(shellEntity);
            var choices = systemState.EntityManager.GetBuffer<MorrowindDialogueChoice>(shellEntity);
            ref var service = ref SystemAPI.GetSingletonRW<MorrowindDialogueServiceWindowState>().ValueRW;
            var stagedBarterItems = systemState.EntityManager.GetBuffer<MorrowindDialogueBarterStagedItem>(shellEntity);
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

            if (session.NeedsGreeting != 0)
            {
                session.NeedsGreeting = 0;
                lines.Clear();
                choices.Clear();
                if (!TryAppendGreeting(ref systemState, ref contentBlob, ref worldCells, ref filterQueryContext, activeExplicitRefs, ref shell, ref session, knownTopics, topicEntries, factionReactionOverrides, scriptStartRequests, refStateRequests, transformRequests, jailRequests, movementFlagRequests, placeAtRequests, castRequests, actorSpellRequests, shellMessageBoxRequests, globalMapRevealRequests, forceGreetingRequests, playerReputationRequests, playerAttributeRequests, playerSkillRequests, playerFactionRequests, actorFactionRequests, lines, choices, ref dialogueState, ref questState, time, questStates, questEntries))
                {
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
            var requestedServiceKind = (MorrowindDialogueServiceKind)request.ServiceKind;
            var requestedServiceAction = (MorrowindDialogueServiceAction)request.ServiceAction;
            int requestedInt0 = request.Int0;
            int requestedInt1 = request.Int1;
            request.Pending = 0;
            request.Action = 0;
            request.DialogueIndex = -1;
            request.ChoiceValue = 0;
            request.ServiceKind = 0;
            request.ServiceAction = 0;
            request.Int0 = 0;
            request.Int1 = 0;

            if (action == MorrowindDialogueResponseAction.Goodbye)
            {
                if (session.ChoiceActive != 0)
                    return;

                RuntimeShellStateUtility.CloseDialogue(ref shell);
                CommitDialogueDisposition(systemState.EntityManager, ref session);
                CloseService(ref service, stagedBarterItems);
                session = CreateInactiveSession();
                lines.Clear();
                choices.Clear();
                return;
            }

            if (action == MorrowindDialogueResponseAction.SelectTopic)
            {
                TryAppendTopic(ref systemState, 
                    ref contentBlob,
                    ref worldCells,
                    ref filterQueryContext,
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
                    ref worldCells,
                    ref filterQueryContext,
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
            else if (action == MorrowindDialogueResponseAction.OpenService)
            {
                TryOpenService(ref systemState,
                    ref contentBlob,
                    ref worldCells,
                    ref filterQueryContext,
                    activeExplicitRefs,
                    requestedServiceKind,
                    ref shell,
                    ref service,
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
                    questEntries,
                    stagedBarterItems);
            }
            else if (action == MorrowindDialogueResponseAction.ServiceAction)
            {
                ApplyServiceAction(ref systemState,
                    ref contentBlob,
                    ref worldCells,
                    ref filterQueryContext,
                    activeExplicitRefs,
                    requestedServiceAction,
                    requestedInt0,
                    requestedInt1,
                    ref shell,
                    ref service,
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
                    questEntries,
                    stagedBarterItems);
            }
        }

        bool TryAppendGreeting(ref SystemState systemState, 
            ref RuntimeContentBlob contentBlob,
            ref RuntimeWorldCellBlob worldCells,
            ref MorrowindDialogueFilterUtility.QueryContext filterQueryContext,
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

                if (!MorrowindDialogueFilterUtility.TryFindFirstMatchingInfoBurst(
                        ref contentBlob,
                        ref worldCells,
                        systemState.EntityManager,
                        session.SpeakerEntity,
                        session.SpeakerActor,
                        dialogueIndex,
                        -1,
                        ref filterQueryContext,
                        out int infoIndex,
                        out _))
                {
                    continue;
                }

                AppendResponseLine(lines, dialogueIndex, infoIndex, showTitle: false);
                session.LastInfoIndex = infoIndex;
                session.SelectedTopicDialogueIndex = dialogueIndex;
                ref var info = ref contentBlob.DialogueInfos[infoIndex];
                FixedString512Bytes response = RuntimeFixedStringUtility.ToFixed512OrDefault(ref info.Response);
                bool closeDialogue = MorrowindDialogueResultScriptUtility.ExecuteSupported(ref contentBlob, systemState.EntityManager, activeExplicitRefs, ref dialogueState, ref questState, time, knownTopics, topicEntries, factionReactionOverrides, scriptStartRequests, refStateRequests, transformRequests, jailRequests, movementFlagRequests, placeAtRequests, castRequests, actorSpellRequests, shellMessageBoxRequests, globalMapRevealRequests, forceGreetingRequests, playerReputationRequests, playerAttributeRequests, playerSkillRequests, playerFactionRequests, actorFactionRequests, questStates, questEntries, lines, choices, info.ResultScript.ToString(), ref shell, ref session);
                MorrowindDialogueUtility.AddTopicsFromResponseBurst(ref contentBlob, ref worldCells, knownTopics, response, systemState.EntityManager, session.SpeakerEntity, session.SpeakerActor, ref filterQueryContext);
                if (ShouldCloseAfterResult(closeDialogue, in session))
                    CloseDialogue(ref shell, ref session, lines, choices);
                return true;
            }

            return false;
        }

        void TryAppendTopic(ref SystemState systemState, 
            ref RuntimeContentBlob contentBlob,
            ref RuntimeWorldCellBlob worldCells,
            ref MorrowindDialogueFilterUtility.QueryContext filterQueryContext,
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

            if (!MorrowindDialogueFilterUtility.TryFindFirstMatchingInfoBurst(
                    ref contentBlob,
                    ref worldCells,
                    systemState.EntityManager,
                    session.SpeakerEntity,
                    session.SpeakerActor,
                    dialogueIndex,
                    -1,
                    ref filterQueryContext,
                    out int infoIndex,
                    out _))
            {
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
            FixedString512Bytes response = RuntimeFixedStringUtility.ToFixed512OrDefault(ref info.Response);
            bool closeDialogue = MorrowindDialogueResultScriptUtility.ExecuteSupported(ref contentBlob, systemState.EntityManager, activeExplicitRefs, ref dialogueState, ref questState, time, knownTopics, topicEntries, factionReactionOverrides, scriptStartRequests, refStateRequests, transformRequests, jailRequests, movementFlagRequests, placeAtRequests, castRequests, actorSpellRequests, shellMessageBoxRequests, globalMapRevealRequests, forceGreetingRequests, playerReputationRequests, playerAttributeRequests, playerSkillRequests, playerFactionRequests, actorFactionRequests, questStates, questEntries, lines, choices, info.ResultScript.ToString(), ref shell, ref session);
            MorrowindDialogueUtility.AddTopicsFromResponseBurst(ref contentBlob, ref worldCells, knownTopics, response, systemState.EntityManager, session.SpeakerEntity, session.SpeakerActor, ref filterQueryContext);
            if (ShouldCloseAfterResult(closeDialogue, in session))
                CloseDialogue(ref shell, ref session, lines, choices);
        }

        void TryAppendChoiceAnswer(ref SystemState systemState, 
            ref RuntimeContentBlob contentBlob,
            ref RuntimeWorldCellBlob worldCells,
            ref MorrowindDialogueFilterUtility.QueryContext filterQueryContext,
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

            if (!MorrowindDialogueFilterUtility.TryFindFirstMatchingInfoBurst(
                    ref contentBlob,
                    ref worldCells,
                    systemState.EntityManager,
                    session.SpeakerEntity,
                    session.SpeakerActor,
                    dialogueIndex,
                    choiceValue,
                    ref filterQueryContext,
                    out int infoIndex,
                    out _))
            {
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
            FixedString512Bytes response = RuntimeFixedStringUtility.ToFixed512OrDefault(ref info.Response);
            bool closeDialogue = MorrowindDialogueResultScriptUtility.ExecuteSupported(ref contentBlob, systemState.EntityManager, activeExplicitRefs, ref dialogueState, ref questState, time, knownTopics, topicEntries, factionReactionOverrides, scriptStartRequests, refStateRequests, transformRequests, jailRequests, movementFlagRequests, placeAtRequests, castRequests, actorSpellRequests, shellMessageBoxRequests, globalMapRevealRequests, forceGreetingRequests, playerReputationRequests, playerAttributeRequests, playerSkillRequests, playerFactionRequests, actorFactionRequests, questStates, questEntries, lines, choices, info.ResultScript.ToString(), ref shell, ref session);
            MorrowindDialogueUtility.AddTopicsFromResponseBurst(ref contentBlob, ref worldCells, knownTopics, response, systemState.EntityManager, session.SpeakerEntity, session.SpeakerActor, ref filterQueryContext);
            if (session.Goodbye != 0 || ShouldCloseAfterResult(closeDialogue, in session))
                CloseDialogue(ref shell, ref session, lines, choices);
        }

        void TryOpenService(
            ref SystemState systemState,
            ref RuntimeContentBlob contentBlob,
            ref RuntimeWorldCellBlob worldCells,
            ref MorrowindDialogueFilterUtility.QueryContext filterQueryContext,
            ActiveExplicitRefLookup activeExplicitRefs,
            MorrowindDialogueServiceKind serviceKind,
            ref RuntimeShellState shell,
            ref MorrowindDialogueServiceWindowState service,
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
            DynamicBuffer<MorrowindQuestJournalEntry> questEntries,
            DynamicBuffer<MorrowindDialogueBarterStagedItem> stagedBarterItems)
        {
            if (session.ChoiceActive != 0 || session.Goodbye != 0 || serviceKind == MorrowindDialogueServiceKind.None)
                return;

            if (serviceKind == MorrowindDialogueServiceKind.Barter || serviceKind == MorrowindDialogueServiceKind.Travel)
            {
                if (TryAppendServiceRefusal(ref systemState,
                        ref contentBlob,
                        ref worldCells,
                        ref filterQueryContext,
                        activeExplicitRefs,
                        serviceKind == MorrowindDialogueServiceKind.Barter ? 1 : 5,
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
                        questEntries))
                {
                    CloseService(ref service, stagedBarterItems);
                    return;
                }
            }

            if (serviceKind == MorrowindDialogueServiceKind.Barter)
                EnsureActorBarterState(systemState.EntityManager, ref contentBlob, session.SpeakerEntity, session.SpeakerActor);

            service = new MorrowindDialogueServiceWindowState
            {
                Visible = 1,
                Mode = (byte)serviceKind,
                SpeakerEntity = session.SpeakerEntity,
                SpeakerPlacedRefId = session.SpeakerPlacedRefId,
                SpeakerActor = session.SpeakerActor,
                BarterOffer = 0,
            };
            session.ServiceOpen = (byte)serviceKind;
            stagedBarterItems.Clear();
        }

        bool TryAppendServiceRefusal(
            ref SystemState systemState,
            ref RuntimeContentBlob contentBlob,
            ref RuntimeWorldCellBlob worldCells,
            ref MorrowindDialogueFilterUtility.QueryContext filterQueryContext,
            ActiveExplicitRefLookup activeExplicitRefs,
            int serviceChoice,
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
            if (!RuntimeContentBlobUtility.TryGetDialogueHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId("Service Refusal"), out var dialogueHandle) || !dialogueHandle.IsValid)
                return false;

            int dialogueIndex = dialogueHandle.Index;
            if (TryAppendServiceRefusalChoice(ref systemState, ref contentBlob, ref worldCells, ref filterQueryContext, activeExplicitRefs, dialogueIndex, -1, ref shell, ref session, knownTopics, topicEntries, factionReactionOverrides, scriptStartRequests, refStateRequests, transformRequests, jailRequests, movementFlagRequests, placeAtRequests, castRequests, actorSpellRequests, shellMessageBoxRequests, globalMapRevealRequests, forceGreetingRequests, playerReputationRequests, playerAttributeRequests, playerSkillRequests, playerFactionRequests, actorFactionRequests, lines, choices, ref dialogueState, ref questState, time, questStates, questEntries))
                return true;

            return TryAppendServiceRefusalChoice(ref systemState, ref contentBlob, ref worldCells, ref filterQueryContext, activeExplicitRefs, dialogueIndex, serviceChoice, ref shell, ref session, knownTopics, topicEntries, factionReactionOverrides, scriptStartRequests, refStateRequests, transformRequests, jailRequests, movementFlagRequests, placeAtRequests, castRequests, actorSpellRequests, shellMessageBoxRequests, globalMapRevealRequests, forceGreetingRequests, playerReputationRequests, playerAttributeRequests, playerSkillRequests, playerFactionRequests, actorFactionRequests, lines, choices, ref dialogueState, ref questState, time, questStates, questEntries);
        }

        bool TryAppendServiceRefusalChoice(
            ref SystemState systemState,
            ref RuntimeContentBlob contentBlob,
            ref RuntimeWorldCellBlob worldCells,
            ref MorrowindDialogueFilterUtility.QueryContext filterQueryContext,
            ActiveExplicitRefLookup activeExplicitRefs,
            int dialogueIndex,
            int choice,
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
            if (!MorrowindDialogueFilterUtility.TryFindFirstMatchingInfoBurst(
                    ref contentBlob,
                    ref worldCells,
                    systemState.EntityManager,
                    session.SpeakerEntity,
                    session.SpeakerActor,
                    dialogueIndex,
                    choice,
                    ref filterQueryContext,
                    invertDisposition: true,
                    out int infoIndex,
                    out _))
            {
                return false;
            }

            AppendResponseLine(lines, dialogueIndex, infoIndex, showTitle: true);
            session.LastInfoIndex = infoIndex;
            session.SelectedTopicDialogueIndex = dialogueIndex;
            ref var info = ref contentBlob.DialogueInfos[infoIndex];
            FixedString512Bytes response = RuntimeFixedStringUtility.ToFixed512OrDefault(ref info.Response);
            bool closeDialogue = MorrowindDialogueResultScriptUtility.ExecuteSupported(ref contentBlob, systemState.EntityManager, activeExplicitRefs, ref dialogueState, ref questState, time, knownTopics, topicEntries, factionReactionOverrides, scriptStartRequests, refStateRequests, transformRequests, jailRequests, movementFlagRequests, placeAtRequests, castRequests, actorSpellRequests, shellMessageBoxRequests, globalMapRevealRequests, forceGreetingRequests, playerReputationRequests, playerAttributeRequests, playerSkillRequests, playerFactionRequests, actorFactionRequests, questStates, questEntries, lines, choices, info.ResultScript.ToString(), ref shell, ref session);
            MorrowindDialogueUtility.AddTopicsFromResponseBurst(ref contentBlob, ref worldCells, knownTopics, response, systemState.EntityManager, session.SpeakerEntity, session.SpeakerActor, ref filterQueryContext);
            if (ShouldCloseAfterResult(closeDialogue, in session))
                CloseDialogue(ref shell, ref session, lines, choices);
            return true;
        }

        void ApplyServiceAction(
            ref SystemState systemState,
            ref RuntimeContentBlob contentBlob,
            ref RuntimeWorldCellBlob worldCells,
            ref MorrowindDialogueFilterUtility.QueryContext filterQueryContext,
            ActiveExplicitRefLookup activeExplicitRefs,
            MorrowindDialogueServiceAction action,
            int int0,
            int int1,
            ref RuntimeShellState shell,
            ref MorrowindDialogueServiceWindowState service,
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
            DynamicBuffer<MorrowindQuestJournalEntry> questEntries,
            DynamicBuffer<MorrowindDialogueBarterStagedItem> stagedBarterItems)
        {
            if (action == MorrowindDialogueServiceAction.Close)
            {
                CloseService(ref service, stagedBarterItems);
                session.ServiceOpen = 0;
                return;
            }

            if (service.Visible == 0)
                return;

            switch (action)
            {
                case MorrowindDialogueServiceAction.Persuade:
                    ApplyPersuasion(ref systemState, ref contentBlob, ref worldCells, ref filterQueryContext, activeExplicitRefs, (MorrowindPersuasionAction)int0, ref shell, ref service, ref session, knownTopics, topicEntries, factionReactionOverrides, scriptStartRequests, refStateRequests, transformRequests, jailRequests, movementFlagRequests, placeAtRequests, castRequests, actorSpellRequests, shellMessageBoxRequests, globalMapRevealRequests, forceGreetingRequests, playerReputationRequests, playerAttributeRequests, playerSkillRequests, playerFactionRequests, actorFactionRequests, lines, choices, ref dialogueState, ref questState, time, questStates, questEntries);
                    break;
                case MorrowindDialogueServiceAction.Travel:
                    ApplyTravel(ref systemState, ref contentBlob, int0, int1, ref shell, ref service, ref session, transformRequests, stagedBarterItems);
                    break;
                case MorrowindDialogueServiceAction.StageMerchantItem:
                    StageBarterItem(systemState.EntityManager, ref contentBlob, service.SpeakerEntity, stagedBarterItems, owner: 1, int0);
                    break;
                case MorrowindDialogueServiceAction.StagePlayerItem:
                    StageBarterItem(systemState.EntityManager, ref contentBlob, Entity.Null, stagedBarterItems, owner: 2, int0);
                    break;
                case MorrowindDialogueServiceAction.ResetBarter:
                    stagedBarterItems.Clear();
                    service.BarterOffer = 0;
                    break;
                case MorrowindDialogueServiceAction.AdjustBarterOffer:
                    service.BarterOffer += int0;
                    break;
                case MorrowindDialogueServiceAction.OfferBarter:
                    ApplyBarterOffer(ref systemState, ref contentBlob, ref service, stagedBarterItems);
                    break;
            }
        }

        void ApplyPersuasion(
            ref SystemState systemState,
            ref RuntimeContentBlob contentBlob,
            ref RuntimeWorldCellBlob worldCells,
            ref MorrowindDialogueFilterUtility.QueryContext filterQueryContext,
            ActiveExplicitRefLookup activeExplicitRefs,
            MorrowindPersuasionAction action,
            ref RuntimeShellState shell,
            ref MorrowindDialogueServiceWindowState service,
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
            if (!TryResolvePlayer(systemState.EntityManager, ref filterQueryContext, out Entity player)
                || !systemState.EntityManager.HasBuffer<PlayerInventoryItem>(player))
            {
                throw new InvalidOperationException("[VVardenfell][Dialogue] Persuasion requires player inventory.");
            }

            int bribe = action switch
            {
                MorrowindPersuasionAction.Bribe10 => 10,
                MorrowindPersuasionAction.Bribe100 => 100,
                MorrowindPersuasionAction.Bribe1000 => 1000,
                _ => 0,
            };
            var playerInventory = systemState.EntityManager.GetBuffer<PlayerInventoryItem>(player);
            if (bribe > 0 && CountGold(ref contentBlob, playerInventory) < bribe)
                return;

            bool success = RollPersuasion(ref systemState, ref contentBlob, action, player, session.SpeakerEntity, session.SpeakerActor, ref dialogueState, out int tempDelta, out int permanentDelta, out int fightDelta, out int fleeDelta);
            session.TemporaryDispositionDelta += tempDelta;
            session.PermanentDispositionDelta += permanentDelta;
            if (bribe > 0 && success)
            {
                RemoveGold(ref contentBlob, playerInventory, bribe);
            }

            if ((fightDelta != 0 || fleeDelta != 0) && systemState.EntityManager.HasComponent<ActorAiSettingsState>(session.SpeakerEntity))
            {
                var ai = systemState.EntityManager.GetComponentData<ActorAiSettingsState>(session.SpeakerEntity);
                ai.Fight = Math.Clamp(ai.Fight + fightDelta, 0, 100);
                ai.Flee = Math.Clamp(ai.Flee + fleeDelta, 0, 100);
                systemState.EntityManager.SetComponentData(session.SpeakerEntity, ai);
            }

            string topic = action switch
            {
                MorrowindPersuasionAction.Admire => success ? "Admire Success" : "Admire Fail",
                MorrowindPersuasionAction.Intimidate => success ? "Intimidate Success" : "Intimidate Fail",
                MorrowindPersuasionAction.Taunt => success ? "Taunt Success" : "Taunt Fail",
                _ => success ? "Bribe Success" : "Bribe Fail",
            };
            Entity bribeRecipient = session.SpeakerEntity;
            ActorDefHandle bribeRecipientActor = session.SpeakerActor;
            TryAppendDialogueTopicById(ref systemState, ref contentBlob, ref worldCells, ref filterQueryContext, activeExplicitRefs, topic, ref shell, ref session, knownTopics, topicEntries, factionReactionOverrides, scriptStartRequests, refStateRequests, transformRequests, jailRequests, movementFlagRequests, placeAtRequests, castRequests, actorSpellRequests, shellMessageBoxRequests, globalMapRevealRequests, forceGreetingRequests, playerReputationRequests, playerAttributeRequests, playerSkillRequests, playerFactionRequests, actorFactionRequests, lines, choices, ref dialogueState, ref questState, time, questStates, questEntries);

            if (bribe > 0 && success)
            {
                EnsureActorBarterState(systemState.EntityManager, ref contentBlob, bribeRecipient, bribeRecipientActor);
                var barter = systemState.EntityManager.GetComponentData<ActorBarterState>(bribeRecipient);
                barter.Gold += bribe;
                systemState.EntityManager.SetComponentData(bribeRecipient, barter);
            }

            service = default;
            session.ServiceOpen = 0;
        }

        bool TryAppendDialogueTopicById(
            ref SystemState systemState,
            ref RuntimeContentBlob contentBlob,
            ref RuntimeWorldCellBlob worldCells,
            ref MorrowindDialogueFilterUtility.QueryContext filterQueryContext,
            ActiveExplicitRefLookup activeExplicitRefs,
            string topicId,
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
            if (!RuntimeContentBlobUtility.TryGetDialogueHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(topicId), out var dialogueHandle) || !dialogueHandle.IsValid)
                return false;

            int dialogueIndex = dialogueHandle.Index;
            if (!MorrowindDialogueFilterUtility.TryFindFirstMatchingInfoBurst(ref contentBlob, ref worldCells, systemState.EntityManager, session.SpeakerEntity, session.SpeakerActor, dialogueIndex, -1, ref filterQueryContext, out int infoIndex, out _))
                return false;

            AppendResponseLine(lines, dialogueIndex, infoIndex, showTitle: true);
            session.LastInfoIndex = infoIndex;
            session.SelectedTopicDialogueIndex = dialogueIndex;
            ref var info = ref contentBlob.DialogueInfos[infoIndex];
            FixedString512Bytes response = RuntimeFixedStringUtility.ToFixed512OrDefault(ref info.Response);
            bool closeDialogue = MorrowindDialogueResultScriptUtility.ExecuteSupported(ref contentBlob, systemState.EntityManager, activeExplicitRefs, ref dialogueState, ref questState, time, knownTopics, topicEntries, factionReactionOverrides, scriptStartRequests, refStateRequests, transformRequests, jailRequests, movementFlagRequests, placeAtRequests, castRequests, actorSpellRequests, shellMessageBoxRequests, globalMapRevealRequests, forceGreetingRequests, playerReputationRequests, playerAttributeRequests, playerSkillRequests, playerFactionRequests, actorFactionRequests, questStates, questEntries, lines, choices, info.ResultScript.ToString(), ref shell, ref session);
            MorrowindDialogueUtility.AddTopicsFromResponseBurst(ref contentBlob, ref worldCells, knownTopics, response, systemState.EntityManager, session.SpeakerEntity, session.SpeakerActor, ref filterQueryContext);
            if (ShouldCloseAfterResult(closeDialogue, in session))
                CloseDialogue(ref shell, ref session, lines, choices);
            return true;
        }

        static bool RollPersuasion(
            ref SystemState systemState,
            ref RuntimeContentBlob contentBlob,
            MorrowindPersuasionAction action,
            Entity player,
            Entity speaker,
            ActorDefHandle speakerActor,
            ref MorrowindDialogueState dialogueState,
            out int temporaryDelta,
            out int permanentDelta,
            out int fightDelta,
            out int fleeDelta)
        {
            temporaryDelta = 0;
            permanentDelta = 0;
            fightDelta = 0;
            fleeDelta = 0;

            var playerAttributes = systemState.EntityManager.GetComponentData<ActorAttributeSet>(player);
            var playerSkills = systemState.EntityManager.GetComponentData<ActorSkillSet>(player);
            var playerVitals = systemState.EntityManager.GetComponentData<ActorVitalSet>(player);
            var playerIdentity = systemState.EntityManager.GetComponentData<ActorIdentitySet>(player);
            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref contentBlob, speakerActor);
            ActorAttributeSet speakerAttributes = systemState.EntityManager.HasComponent<ActorAttributeSet>(speaker)
                ? systemState.EntityManager.GetComponentData<ActorAttributeSet>(speaker)
                : ToRuntimeSet(actor.Attributes);
            ActorSkillSet speakerSkills = systemState.EntityManager.HasComponent<ActorSkillSet>(speaker)
                ? systemState.EntityManager.GetComponentData<ActorSkillSet>(speaker)
                : ToRuntimeSet(actor.Skills);
            ActorVitalSet speakerVitals = systemState.EntityManager.HasComponent<ActorVitalSet>(speaker)
                ? systemState.EntityManager.GetComponentData<ActorVitalSet>(speaker)
                : new ActorVitalSet { CurrentFatigue = actor.Vitals.Fatigue, ModifiedFatigueBase = actor.Vitals.Fatigue };
            int currentDisposition = systemState.EntityManager.HasComponent<ActorDispositionState>(speaker)
                ? systemState.EntityManager.GetComponentData<ActorDispositionState>(speaker).BaseDisposition
                : actor.Disposition;

            float playerRating1 = (playerIdentity.Reputation * GmstFloat(ref contentBlob, "fReputationMod")
                                   + playerAttributes.Luck / GmstFloat(ref contentBlob, "fLuckMod")
                                   + playerAttributes.Personality / GmstFloat(ref contentBlob, "fPersonalityMod")
                                   + playerSkills.Speechcraft) * FatigueTerm(playerVitals);
            float playerRating2 = playerRating1 + playerIdentity.Level * GmstFloat(ref contentBlob, "fLevelMod");
            float playerRating3 = (playerSkills.Mercantile
                                   + playerAttributes.Luck / GmstFloat(ref contentBlob, "fLuckMod")
                                   + playerAttributes.Personality / GmstFloat(ref contentBlob, "fPersonalityMod")) * FatigueTerm(playerVitals);
            float npcRating1 = (actor.Reputation * GmstFloat(ref contentBlob, "fReputationMod")
                                + speakerAttributes.Luck / GmstFloat(ref contentBlob, "fLuckMod")
                                + speakerAttributes.Personality / GmstFloat(ref contentBlob, "fPersonalityMod")
                                + speakerSkills.Speechcraft) * FatigueTerm(speakerVitals);
            float npcRating2 = (actor.Level * GmstFloat(ref contentBlob, "fLevelMod")
                                + actor.Reputation * GmstFloat(ref contentBlob, "fReputationMod")
                                + speakerAttributes.Luck / GmstFloat(ref contentBlob, "fLuckMod")
                                + speakerAttributes.Personality / GmstFloat(ref contentBlob, "fPersonalityMod")
                                + speakerSkills.Speechcraft) * FatigueTerm(speakerVitals);
            float npcRating3 = (speakerSkills.Mercantile
                                + actor.Reputation * GmstFloat(ref contentBlob, "fReputationMod")
                                + speakerAttributes.Luck / GmstFloat(ref contentBlob, "fLuckMod")
                                + speakerAttributes.Personality / GmstFloat(ref contentBlob, "fPersonalityMod")) * FatigueTerm(speakerVitals);

            float dispositionFactor = 1f - 0.02f * math.abs(currentDisposition - 50);
            float bribeMod = action switch
            {
                MorrowindPersuasionAction.Bribe10 => GmstFloat(ref contentBlob, "fBribe10Mod"),
                MorrowindPersuasionAction.Bribe100 => GmstFloat(ref contentBlob, "fBribe100Mod"),
                MorrowindPersuasionAction.Bribe1000 => GmstFloat(ref contentBlob, "fBribe1000Mod"),
                _ => 0f,
            };
            float target = action switch
            {
                MorrowindPersuasionAction.Intimidate => dispositionFactor * (playerRating2 - npcRating2 + 50f),
                MorrowindPersuasionAction.Bribe10 or MorrowindPersuasionAction.Bribe100 or MorrowindPersuasionAction.Bribe1000 => dispositionFactor * (playerRating3 - npcRating3 + 50f) + bribeMod,
                _ => dispositionFactor * (playerRating1 - npcRating1 + 50f),
            };
            target = math.max(GmstInt(ref contentBlob, "iPerMinChance"), target);
            uint random = dialogueState.NextSessionSequence == 0 ? 1u : dialogueState.NextSessionSequence;
            random = random * 1664525u + 1013904223u;
            dialogueState.NextSessionSequence = random;
            int roll = (int)(random % 100u);
            bool success = roll <= target;
            float raw = math.floor((target - roll) * GmstFloat(ref contentBlob, "fPerDieRollMult"));
            int minChange = GmstInt(ref contentBlob, "iPerMinChange");
            float tempMult = GmstFloat(ref contentBlob, "fPerTempMult");

            if (action == MorrowindPersuasionAction.Taunt)
            {
                int value = success ? -Math.Max(minChange, (int)math.abs(raw)) : (int)-math.abs(raw);
                temporaryDelta = ClampTemp(currentDisposition, (int)math.floor(value * tempMult));
                permanentDelta = (int)math.floor(temporaryDelta / tempMult);
                if (success)
                {
                    int ai = Math.Max(minChange, (int)math.floor(math.abs(raw) * tempMult));
                    fightDelta = ai;
                    fleeDelta = -ai;
                }
                return success;
            }

            if (action == MorrowindPersuasionAction.Intimidate)
            {
                int value = success ? Math.Max(minChange, (int)math.abs(raw)) : -(int)math.abs(raw);
                temporaryDelta = ClampTemp(currentDisposition, (int)math.floor(value * tempMult));
                permanentDelta = success ? -(int)math.floor(temporaryDelta / tempMult) : (int)raw;
                if (success)
                {
                    int ai = Math.Max(minChange, (int)math.floor(math.abs(raw) * tempMult));
                    fleeDelta = ai;
                    fightDelta = -ai;
                }
                return success;
            }

            int delta = success ? Math.Max(minChange, (int)raw) : (int)raw;
            temporaryDelta = ClampTemp(currentDisposition, (int)math.floor(delta * tempMult));
            permanentDelta = (int)math.floor(temporaryDelta / tempMult);
            return success;
        }

        static float FatigueTerm(in ActorVitalSet vitals)
            => vitals.ModifiedFatigueBase > 0f ? 0.75f + 0.5f * math.clamp(vitals.CurrentFatigue / vitals.ModifiedFatigueBase, 0f, 1f) : 1f;

        static int ClampTemp(int disposition, int delta)
            => Math.Clamp(disposition + delta, 0, 100) - Math.Clamp(disposition, 0, 100);

        static float GmstFloat(ref RuntimeContentBlob content, string id)
            => RuntimeContentBlobUtility.RequireGameSettingFloatByIdHash(ref content, RuntimeContentStableHash.HashId(id));

        static int GmstInt(ref RuntimeContentBlob content, string id)
            => RuntimeContentBlobUtility.RequireGameSettingIntByIdHash(ref content, RuntimeContentStableHash.HashId(id));

        static void ApplyTravel(
            ref SystemState systemState,
            ref RuntimeContentBlob contentBlob,
            int destinationOffset,
            int price,
            ref RuntimeShellState shell,
            ref MorrowindDialogueServiceWindowState service,
            ref MorrowindDialogueSession session,
            DynamicBuffer<MorrowindScriptTransformRequest> transformRequests,
            DynamicBuffer<MorrowindDialogueBarterStagedItem> stagedBarterItems)
        {
            if (!service.SpeakerActor.IsValid)
                throw new InvalidOperationException("[VVardenfell][Dialogue] Travel requires a speaker actor.");
            if (!TryResolvePlayer(systemState.EntityManager, out Entity player) || !systemState.EntityManager.HasBuffer<PlayerInventoryItem>(player))
                throw new InvalidOperationException("[VVardenfell][Dialogue] Travel requires player inventory.");

            var inventory = systemState.EntityManager.GetBuffer<PlayerInventoryItem>(player);
            if (CountGold(ref contentBlob, inventory) < price)
                return;

            ref RuntimeActorDefBlob actor = ref RuntimeContentBlobUtility.Get(ref contentBlob, service.SpeakerActor);
            if ((uint)destinationOffset >= (uint)actor.TravelDestinationCount)
                throw new InvalidOperationException($"[VVardenfell][Dialogue] Travel destination {destinationOffset} is outside actor destination range.");

            ref RuntimeActorTravelDestinationDefBlob destination = ref contentBlob.ActorTravelDestinations[actor.FirstTravelDestinationIndex + destinationOffset];
            RemoveGold(ref contentBlob, inventory, price);
            EnsureActorBarterState(systemState.EntityManager, ref contentBlob, service.SpeakerEntity, service.SpeakerActor);
            var barter = systemState.EntityManager.GetComponentData<ActorBarterState>(service.SpeakerEntity);
            barter.Gold += price;
            systemState.EntityManager.SetComponentData(service.SpeakerEntity, barter);

            MarkPlayerTraveling(systemState.EntityManager);

            transformRequests.Add(new MorrowindScriptTransformRequest
            {
                TargetEntity = player,
                TargetPlacedRefId = 0u,
                Position = new float3(destination.PosX, destination.PosY, destination.PosZ),
                Radians = destination.RotZ,
                InteriorCellHash = destination.CellNameHash,
                Operation = destination.CellNameHash != 0UL ? (byte)2 : (byte)3,
            });

            RuntimeShellStateUtility.CloseDialogue(ref shell);
            CommitDialogueDisposition(systemState.EntityManager, ref session);
            CloseService(ref service, stagedBarterItems);
            session = CreateInactiveSession();
        }

        static void StageBarterItem(
            EntityManager entityManager,
            ref RuntimeContentBlob contentBlob,
            Entity merchant,
            DynamicBuffer<MorrowindDialogueBarterStagedItem> staged,
            byte owner,
            int sourceIndex)
        {
            if (owner == 1)
            {
                if (merchant == Entity.Null || !entityManager.Exists(merchant) || !entityManager.HasBuffer<ActorInventoryItem>(merchant))
                    throw new InvalidOperationException("[VVardenfell][Dialogue] Barter merchant has no actor inventory.");
                var inventory = entityManager.GetBuffer<ActorInventoryItem>(merchant, true);
                if ((uint)sourceIndex >= (uint)inventory.Length)
                    return;
                var item = inventory[sourceIndex];
                if (item.Count <= StagedCount(staged, owner, sourceIndex) || IsGold(ref contentBlob, item.Content))
                    return;
                staged.Add(new MorrowindDialogueBarterStagedItem
                {
                    Owner = owner,
                    SourceIndex = sourceIndex,
                    Content = item.Content,
                    SoulId = item.SoulId,
                    SoulActorHandleValue = item.SoulActorHandleValue,
                    Count = 1,
                    Condition = item.Condition,
                    EnchantmentCharge = item.EnchantmentCharge,
                });
                return;
            }

            if (!TryResolvePlayer(entityManager, out Entity player) || !entityManager.HasBuffer<PlayerInventoryItem>(player))
                throw new InvalidOperationException("[VVardenfell][Dialogue] Barter requires player inventory.");
            var playerInventory = entityManager.GetBuffer<PlayerInventoryItem>(player, true);
            if ((uint)sourceIndex >= (uint)playerInventory.Length)
                return;
            var playerItem = playerInventory[sourceIndex];
            if (playerItem.Count <= StagedCount(staged, owner, sourceIndex) || IsGold(ref contentBlob, playerItem.Content))
                return;
            staged.Add(new MorrowindDialogueBarterStagedItem
            {
                Owner = owner,
                SourceIndex = sourceIndex,
                Content = playerItem.Content,
                SoulId = playerItem.SoulId,
                SoulActorHandleValue = playerItem.SoulActorHandleValue,
                Count = 1,
                Condition = playerItem.Condition,
                EnchantmentCharge = playerItem.EnchantmentCharge,
            });
        }

        static void ApplyBarterOffer(
            ref SystemState systemState,
            ref RuntimeContentBlob contentBlob,
            ref MorrowindDialogueServiceWindowState service,
            DynamicBuffer<MorrowindDialogueBarterStagedItem> staged)
        {
            if (staged.Length == 0)
                return;
            if (!TryResolvePlayer(systemState.EntityManager, out Entity player) || !systemState.EntityManager.HasBuffer<PlayerInventoryItem>(player))
                throw new InvalidOperationException("[VVardenfell][Dialogue] Barter requires player inventory.");
            if (service.SpeakerEntity == Entity.Null || !systemState.EntityManager.Exists(service.SpeakerEntity) || !systemState.EntityManager.HasBuffer<ActorInventoryItem>(service.SpeakerEntity))
                throw new InvalidOperationException("[VVardenfell][Dialogue] Barter merchant has no actor inventory.");

            var playerInventory = systemState.EntityManager.GetBuffer<PlayerInventoryItem>(player);
            var merchantInventory = systemState.EntityManager.GetBuffer<ActorInventoryItem>(service.SpeakerEntity);
            int balance = service.BarterOffer;
            for (int i = 0; i < staged.Length; i++)
            {
                int value = ResolveItemValue(ref contentBlob, staged[i].Content) * staged[i].Count;
                balance += staged[i].Owner == 1 ? -value : value;
            }

            EnsureActorBarterState(systemState.EntityManager, ref contentBlob, service.SpeakerEntity, service.SpeakerActor);
            var barter = systemState.EntityManager.GetComponentData<ActorBarterState>(service.SpeakerEntity);
            if (balance < 0 && CountGold(ref contentBlob, playerInventory) < -balance)
                return;
            if (balance > 0 && barter.Gold < balance)
                return;

            if (balance < 0)
            {
                RemoveGold(ref contentBlob, playerInventory, -balance);
                barter.Gold += -balance;
            }
            else if (balance > 0)
            {
                AddGold(ref contentBlob, playerInventory, balance);
                barter.Gold -= balance;
            }

            for (int i = 0; i < staged.Length; i++)
            {
                var item = staged[i];
                if (item.Owner == 1)
                {
                    RemoveActorStack(merchantInventory, item);
                    AddPlayerStack(ref contentBlob, playerInventory, item);
                }
                else
                {
                    RemovePlayerStack(playerInventory, item);
                    AddActorStack(ref contentBlob, merchantInventory, item);
                }
            }

            systemState.EntityManager.SetComponentData(service.SpeakerEntity, barter);
            staged.Clear();
            service.BarterOffer = 0;
        }

        static void EnsureActorBarterState(EntityManager entityManager, ref RuntimeContentBlob contentBlob, Entity actorEntity, ActorDefHandle actorHandle)
        {
            if (actorEntity == Entity.Null || !entityManager.Exists(actorEntity))
                throw new InvalidOperationException("[VVardenfell][Dialogue] Service actor entity is not live.");
            if (entityManager.HasComponent<ActorBarterState>(actorEntity))
                return;

            int gold = actorHandle.IsValid ? RuntimeContentBlobUtility.Get(ref contentBlob, actorHandle).Gold : 0;
            entityManager.AddComponentData(actorEntity, new ActorBarterState { Gold = gold });
        }

        static void CommitDialogueDisposition(EntityManager entityManager, ref MorrowindDialogueSession session)
        {
            if (session.SpeakerEntity == Entity.Null
                || !entityManager.Exists(session.SpeakerEntity)
                || session.PermanentDispositionDelta == 0)
            {
                session.TemporaryDispositionDelta = 0;
                session.PermanentDispositionDelta = 0;
                return;
            }

            if (!entityManager.HasComponent<ActorDispositionState>(session.SpeakerEntity))
                throw new InvalidOperationException("[VVardenfell][Dialogue] Dialogue speaker has no ActorDispositionState for committed disposition.");

            var disposition = entityManager.GetComponentData<ActorDispositionState>(session.SpeakerEntity);
            disposition.BaseDisposition = Math.Clamp(disposition.BaseDisposition + session.PermanentDispositionDelta, 0, 100);
            entityManager.SetComponentData(session.SpeakerEntity, disposition);
            session.TemporaryDispositionDelta = 0;
            session.PermanentDispositionDelta = 0;
        }

        static void CloseService(ref MorrowindDialogueServiceWindowState service, DynamicBuffer<MorrowindDialogueBarterStagedItem> staged)
        {
            service = default;
            staged.Clear();
        }

        static void MarkPlayerTraveling(EntityManager entityManager)
        {
            var query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<PlayerTravelingState>());
            try
            {
                if (query.IsEmptyIgnoreFilter)
                    return;
                Entity entity = query.GetSingletonEntity();
                var state = entityManager.GetComponentData<PlayerTravelingState>(entity);
                state.Active = 1;
                entityManager.SetComponentData(entity, state);
            }
            finally
            {
                query.Dispose();
            }
        }

        static bool TryResolvePlayer(EntityManager entityManager, ref MorrowindDialogueFilterUtility.QueryContext queryContext, out Entity player)
        {
            player = Entity.Null;
            if (queryContext.Player.IsEmptyIgnoreFilter)
                return false;
            player = queryContext.Player.GetSingletonEntity();
            return player != Entity.Null && entityManager.Exists(player);
        }

        static bool TryResolvePlayer(EntityManager entityManager, out Entity player)
        {
            player = Entity.Null;
            var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerTag>());
            try
            {
                if (query.IsEmptyIgnoreFilter)
                    return false;
                player = query.GetSingletonEntity();
                return player != Entity.Null && entityManager.Exists(player);
            }
            finally
            {
                query.Dispose();
            }
        }

        static int StagedCount(DynamicBuffer<MorrowindDialogueBarterStagedItem> staged, byte owner, int sourceIndex)
        {
            int count = 0;
            for (int i = 0; i < staged.Length; i++)
            {
                if (staged[i].Owner == owner && staged[i].SourceIndex == sourceIndex)
                    count += staged[i].Count;
            }
            return count;
        }

        static int ResolveItemValue(ref RuntimeContentBlob content, ContentReference item)
        {
            if (item.Kind == ContentReferenceKind.Light)
                return RuntimeContentBlobUtility.Get(ref content, new LightDefHandle { Value = item.HandleValue }).Value;
            if (item.Kind == ContentReferenceKind.Item)
                return RuntimeContentBlobUtility.Get(ref content, new ItemDefHandle { Value = item.HandleValue }).Int0;
            return 0;
        }

        static bool IsGold(ref RuntimeContentBlob content, ContentReference item)
        {
            if (item.Kind != ContentReferenceKind.Item || item.HandleValue <= 0)
                return false;
            ref RuntimeBaseDefBlob def = ref RuntimeContentBlobUtility.Get(ref content, new ItemDefHandle { Value = item.HandleValue });
            return def.IdHash == RuntimeContentStableHash.HashId("gold_001");
        }

        static int CountGold(ref RuntimeContentBlob content, DynamicBuffer<PlayerInventoryItem> inventory)
        {
            int count = 0;
            for (int i = 0; i < inventory.Length; i++)
            {
                if (IsGold(ref content, inventory[i].Content))
                    count += Math.Max(0, inventory[i].Count);
            }
            return count;
        }

        static void RemoveGold(ref RuntimeContentBlob content, DynamicBuffer<PlayerInventoryItem> inventory, int count)
        {
            RemovePlayerContent(inventory, RequireGoldContent(ref content), count);
        }

        static void AddGold(ref RuntimeContentBlob content, DynamicBuffer<PlayerInventoryItem> inventory, int count)
        {
            if (count <= 0)
                return;
            ContainerLootUtility.AddInventoryStack(ref content, inventory, RequireGoldContent(ref content), count);
        }

        static ContentReference RequireGoldContent(ref RuntimeContentBlob content)
        {
            if (!RuntimeContentBlobUtility.TryResolvePlaceableByIdHash(ref content, RuntimeContentStableHash.HashId("gold_001"), out ContentReference gold)
                || gold.Kind != ContentReferenceKind.Item)
                throw new InvalidOperationException("[VVardenfell][Dialogue] Could not resolve gold_001.");
            return gold;
        }

        static void RemovePlayerStack(DynamicBuffer<PlayerInventoryItem> inventory, in MorrowindDialogueBarterStagedItem item)
            => RemovePlayerContent(inventory, item.Content, item.Count);

        static void RemovePlayerContent(DynamicBuffer<PlayerInventoryItem> inventory, ContentReference content, int count)
        {
            int remaining = count;
            for (int i = inventory.Length - 1; i >= 0 && remaining > 0; i--)
            {
                var entry = inventory[i];
                if (entry.Content.Kind != content.Kind || entry.Content.HandleValue != content.HandleValue)
                    continue;
                if (entry.Count <= remaining)
                {
                    remaining -= Math.Max(0, entry.Count);
                    inventory.RemoveAt(i);
                    continue;
                }
                entry.Count -= remaining;
                inventory[i] = entry;
                remaining = 0;
            }
        }

        static void RemoveActorStack(DynamicBuffer<ActorInventoryItem> inventory, in MorrowindDialogueBarterStagedItem item)
        {
            int remaining = item.Count;
            for (int i = inventory.Length - 1; i >= 0 && remaining > 0; i--)
            {
                var entry = inventory[i];
                if (entry.Content.Kind != item.Content.Kind || entry.Content.HandleValue != item.Content.HandleValue)
                    continue;
                if (entry.Count <= remaining)
                {
                    remaining -= Math.Max(0, entry.Count);
                    inventory.RemoveAt(i);
                    continue;
                }
                entry.Count -= remaining;
                inventory[i] = entry;
                remaining = 0;
            }
        }

        static void AddPlayerStack(ref RuntimeContentBlob content, DynamicBuffer<PlayerInventoryItem> inventory, in MorrowindDialogueBarterStagedItem item)
            => ContainerLootUtility.AddInventoryStack(ref content, inventory, item.Content, item.SoulId, item.SoulActorHandleValue, item.Count);

        static void AddActorStack(ref RuntimeContentBlob content, DynamicBuffer<ActorInventoryItem> inventory, in MorrowindDialogueBarterStagedItem item)
        {
            for (int i = 0; i < inventory.Length; i++)
            {
                var entry = inventory[i];
                if (entry.Content.Kind != item.Content.Kind
                    || entry.Content.HandleValue != item.Content.HandleValue
                    || !entry.SoulId.Equals(item.SoulId)
                    || entry.EnchantmentCharge != item.EnchantmentCharge
                    || entry.Condition != item.Condition)
                    continue;
                entry.Count += item.Count;
                inventory[i] = entry;
                return;
            }

            inventory.Add(new ActorInventoryItem
            {
                Content = item.Content,
                SoulId = item.SoulId,
                SoulActorHandleValue = item.SoulActorHandleValue,
                Count = item.Count,
                Condition = item.Condition > 0 ? item.Condition : InventoryConditionUtility.ResolveInitialCondition(ref content, item.Content),
                EnchantmentCharge = item.EnchantmentCharge,
                AuthoredOrder = inventory.Length,
            });
        }

        static ActorAttributeSet ToRuntimeSet(ActorAttributeDef attributes)
            => new()
            {
                Strength = attributes.Strength,
                Intelligence = attributes.Intelligence,
                Willpower = attributes.Willpower,
                Agility = attributes.Agility,
                Speed = attributes.Speed,
                Endurance = attributes.Endurance,
                Personality = attributes.Personality,
                Luck = attributes.Luck,
            };

        static ActorSkillSet ToRuntimeSet(ActorSkillDef skills)
            => new()
            {
                Block = skills.Block,
                Armorer = skills.Armorer,
                MediumArmor = skills.MediumArmor,
                HeavyArmor = skills.HeavyArmor,
                BluntWeapon = skills.BluntWeapon,
                LongBlade = skills.LongBlade,
                Axe = skills.Axe,
                Spear = skills.Spear,
                Athletics = skills.Athletics,
                Enchant = skills.Enchant,
                Destruction = skills.Destruction,
                Alteration = skills.Alteration,
                Illusion = skills.Illusion,
                Conjuration = skills.Conjuration,
                Mysticism = skills.Mysticism,
                Restoration = skills.Restoration,
                Alchemy = skills.Alchemy,
                Unarmored = skills.Unarmored,
                Security = skills.Security,
                Sneak = skills.Sneak,
                Acrobatics = skills.Acrobatics,
                LightArmor = skills.LightArmor,
                ShortBlade = skills.ShortBlade,
                Marksman = skills.Marksman,
                Mercantile = skills.Mercantile,
                Speechcraft = skills.Speechcraft,
                HandToHand = skills.HandToHand,
            };

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

        static bool ShouldCloseAfterResult(bool closeDialogue, in MorrowindDialogueSession session)
            => closeDialogue && session.ChoiceActive == 0;

        static BlobAssetReference<RuntimeWorldCellBlob> RequireWorldCellBlob(EntityQuery query)
        {
            if (query.CalculateEntityCount() != 1)
                throw new InvalidOperationException("[VVardenfell][WorldCellBlob] dialogue session requires exactly one RuntimeWorldCellBlobReference singleton.");

            var reference = query.GetSingleton<RuntimeWorldCellBlobReference>();
            if (!reference.Blob.IsCreated)
                throw new InvalidOperationException("[VVardenfell][WorldCellBlob] dialogue session requires runtime world cell blob.");

            return reference.Blob;
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
