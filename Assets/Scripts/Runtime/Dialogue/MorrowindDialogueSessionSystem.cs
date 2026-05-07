using System;
using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
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
                DialogueFactionReaction = systemState.GetEntityQuery(ComponentType.ReadOnly<MorrowindDialogueState>(), ComponentType.ReadOnly<MorrowindFactionReactionOverride>()),
                PlayerEquipment = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<ActorEquipmentSlot>()),
                PlayerAttribute = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<ActorAttributeSet>()),
                PlayerSkill = systemState.GetEntityQuery(ComponentType.ReadOnly<PlayerTag>(), ComponentType.ReadOnly<ActorSkillSet>()),
            };
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
            request.Pending = 0;
            request.Action = 0;
            request.DialogueIndex = -1;
            request.ChoiceValue = 0;

            if (action == MorrowindDialogueResponseAction.Goodbye)
            {
                if (session.ChoiceActive != 0)
                    return;

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
                bool closeDialogue = MorrowindDialogueResultScriptUtility.ExecuteSupported(ref contentBlob, systemState.EntityManager, activeExplicitRefs, ref dialogueState, ref questState, time, knownTopics, topicEntries, factionReactionOverrides, scriptStartRequests, refStateRequests, transformRequests, jailRequests, movementFlagRequests, placeAtRequests, castRequests, actorSpellRequests, shellMessageBoxRequests, globalMapRevealRequests, forceGreetingRequests, playerReputationRequests, playerAttributeRequests, playerSkillRequests, playerFactionRequests, actorFactionRequests, questStates, questEntries, choices, info.ResultScript.ToString(), ref shell, ref session);
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
            bool closeDialogue = MorrowindDialogueResultScriptUtility.ExecuteSupported(ref contentBlob, systemState.EntityManager, activeExplicitRefs, ref dialogueState, ref questState, time, knownTopics, topicEntries, factionReactionOverrides, scriptStartRequests, refStateRequests, transformRequests, jailRequests, movementFlagRequests, placeAtRequests, castRequests, actorSpellRequests, shellMessageBoxRequests, globalMapRevealRequests, forceGreetingRequests, playerReputationRequests, playerAttributeRequests, playerSkillRequests, playerFactionRequests, actorFactionRequests, questStates, questEntries, choices, info.ResultScript.ToString(), ref shell, ref session);
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
            bool closeDialogue = MorrowindDialogueResultScriptUtility.ExecuteSupported(ref contentBlob, systemState.EntityManager, activeExplicitRefs, ref dialogueState, ref questState, time, knownTopics, topicEntries, factionReactionOverrides, scriptStartRequests, refStateRequests, transformRequests, jailRequests, movementFlagRequests, placeAtRequests, castRequests, actorSpellRequests, shellMessageBoxRequests, globalMapRevealRequests, forceGreetingRequests, playerReputationRequests, playerAttributeRequests, playerSkillRequests, playerFactionRequests, actorFactionRequests, questStates, questEntries, choices, info.ResultScript.ToString(), ref shell, ref session);
            MorrowindDialogueUtility.AddTopicsFromResponseBurst(ref contentBlob, ref worldCells, knownTopics, response, systemState.EntityManager, session.SpeakerEntity, session.SpeakerActor, ref filterQueryContext);
            if (session.Goodbye != 0 || ShouldCloseAfterResult(closeDialogue, in session))
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
