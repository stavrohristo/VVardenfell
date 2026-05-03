using System;
using System.Collections.Generic;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.Shell
{
    public partial class RuntimeHudShellPresentationSystem
    {
        static DialogueWindowViewModel BuildDialogueModel(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            in MorrowindDialogueSession session,
            DynamicBuffer<MorrowindDialogueSessionLine> lines,
            DynamicBuffer<MorrowindKnownDialogueTopic> knownTopics,
            DynamicBuffer<MorrowindTopicJournalEntry> topicEntries,
            DynamicBuffer<MorrowindDialogueChoice> choices)
        {
            if (contentDb == null || session.Active == 0)
                return null;

            bool topicsEnabled = session.ChoiceActive == 0 && session.Goodbye == 0;
            bool goodbyeEnabled = session.ChoiceActive == 0 || session.Goodbye != 0;
            int disposition = ResolveDisposition(contentDb, entityManager, session, out bool dispositionVisible);
            string goodbyeText = ResolveGameSettingString(contentDb, "sGoodbye", "Goodbye");

            return new DialogueWindowViewModel
            {
                SpeakerName = ResolveSpeakerName(contentDb, session),
                Lines = BuildDialogueLines(contentDb, lines),
                Topics = BuildVisibleDialogueTopics(contentDb, entityManager, session, knownTopics, topicEntries),
                Choices = BuildDialogueChoices(session, choices),
                DispositionVisible = dispositionVisible,
                DispositionValue = disposition,
                DispositionFillNormalized = Math.Clamp(disposition / 100f, 0f, 1f),
                TopicsEnabled = topicsEnabled,
                GoodbyeEnabled = goodbyeEnabled,
                ShowInlineGoodbye = session.Goodbye != 0,
                GoodbyeText = goodbyeText,
            };
        }

        static DialogueResponseLineViewModel[] BuildDialogueLines(
            RuntimeContentDatabase contentDb,
            DynamicBuffer<MorrowindDialogueSessionLine> lines)
        {
            var rows = new List<DialogueResponseLineViewModel>(lines.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if ((uint)line.InfoIndex >= (uint)contentDb.DialogueInfoCount)
                    continue;

                string title = string.Empty;
                if (line.ShowTitle != 0 && (uint)line.DialogueIndex < (uint)contentDb.DialogueCount)
                    title = ResolveDialogueTitle(contentDb, line.DialogueIndex);

                string body = contentDb.Data.DialogueInfos[line.InfoIndex].Response;
                rows.Add(new DialogueResponseLineViewModel
                {
                    Title = title,
                    Body = string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim(),
                });
            }

            return rows.ToArray();
        }

        static DialogueTopicRowViewModel[] BuildVisibleDialogueTopics(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            in MorrowindDialogueSession session,
            DynamicBuffer<MorrowindKnownDialogueTopic> knownTopics,
            DynamicBuffer<MorrowindTopicJournalEntry> topicEntries)
        {
            int count = Math.Min(contentDb.DialogueCount, knownTopics.Length);
            var rows = new List<DialogueTopicRowViewModel>();
            for (int dialogueIndex = 0; dialogueIndex < count; dialogueIndex++)
            {
                if (knownTopics[dialogueIndex].Known == 0)
                    continue;

                ref readonly DialogueDef dialogue = ref contentDb.Data.Dialogues[dialogueIndex];
                if (dialogue.Type != DialogueDefType.Topic)
                    continue;

                if (!MorrowindDialogueFilterUtility.TryFindFirstMatchingInfo(
                        contentDb,
                        entityManager,
                        session.SpeakerEntity,
                        session.SpeakerActor,
                        dialogueIndex,
                        -1,
                        out int infoIndex,
                        out _))
                    continue;

                rows.Add(new DialogueTopicRowViewModel
                {
                    DialogueIndex = dialogueIndex,
                    Title = ResolveDialogueTitle(contentDb, dialogueIndex),
                    Selected = session.SelectedTopicDialogueIndex == dialogueIndex,
                    VisualState = ResolveTopicVisualState(
                        contentDb,
                        entityManager,
                        session,
                        knownTopics,
                        topicEntries,
                        dialogueIndex,
                        infoIndex),
                });
            }

            rows.Sort(static (left, right) => string.Compare(left.Title, right.Title, StringComparison.OrdinalIgnoreCase));
            return rows.ToArray();
        }

        static DialogueTopicVisualState ResolveTopicVisualState(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            in MorrowindDialogueSession session,
            DynamicBuffer<MorrowindKnownDialogueTopic> knownTopics,
            DynamicBuffer<MorrowindTopicJournalEntry> topicEntries,
            int dialogueIndex,
            int infoIndex)
        {
            if (contentDb == null || (uint)infoIndex >= (uint)contentDb.DialogueInfoCount)
                return DialogueTopicVisualState.Normal;

            ref readonly DialogueInfoDef info = ref contentDb.Data.DialogueInfos[infoIndex];
            bool inJournal = MorrowindDialogueUtility.ContainsTopicEntry(topicEntries, dialogueIndex, infoIndex);
            if (!inJournal && IsActorSpecificInfo(contentDb, session, info))
                return DialogueTopicVisualState.Specific;

            if (inJournal
                && !ExhaustedTopicRevealsUnknownActorTopic(contentDb, entityManager, session, knownTopics, info.Response))
                return DialogueTopicVisualState.Exhausted;

            return DialogueTopicVisualState.Normal;
        }

        static bool IsActorSpecificInfo(
            RuntimeContentDatabase contentDb,
            in MorrowindDialogueSession session,
            in DialogueInfoDef info)
        {
            if (string.IsNullOrWhiteSpace(info.ActorId)
                || !TryResolveSpeakerActor(contentDb, session, out var actor))
                return false;

            string normalizedActorId = ContentId.NormalizeId(info.ActorId);
            return string.Equals(ContentId.NormalizeId(actor.Id), normalizedActorId, StringComparison.Ordinal)
                   || string.Equals(ContentId.NormalizeId(actor.OriginalId), normalizedActorId, StringComparison.Ordinal);
        }

        static bool ExhaustedTopicRevealsUnknownActorTopic(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            in MorrowindDialogueSession session,
            DynamicBuffer<MorrowindKnownDialogueTopic> knownTopics,
            string response)
        {
            if (contentDb == null || string.IsNullOrWhiteSpace(response))
                return false;

            int count = Math.Min(contentDb.DialogueCount, knownTopics.Length);
            for (int dialogueIndex = 0; dialogueIndex < count; dialogueIndex++)
            {
                if (knownTopics[dialogueIndex].Known != 0)
                    continue;

                ref readonly DialogueDef dialogue = ref contentDb.Data.Dialogues[dialogueIndex];
                if (dialogue.Type != DialogueDefType.Topic)
                    continue;

                string id = dialogue.StringId ?? dialogue.Id;
                if (!MorrowindDialogueUtility.ResponseContainsTopicReference(response, id))
                    continue;

                if (MorrowindDialogueFilterUtility.TryFindFirstMatchingInfo(
                        contentDb,
                        entityManager,
                        session.SpeakerEntity,
                        session.SpeakerActor,
                        dialogueIndex,
                        -1,
                        out _,
                        out _))
                {
                    return true;
                }
            }

            return false;
        }

        static DialogueChoiceRowViewModel[] BuildDialogueChoices(
            in MorrowindDialogueSession session,
            DynamicBuffer<MorrowindDialogueChoice> choices)
        {
            if (session.ChoiceActive == 0 || choices.Length == 0)
                return Array.Empty<DialogueChoiceRowViewModel>();

            var rows = new DialogueChoiceRowViewModel[choices.Length];
            for (int i = 0; i < choices.Length; i++)
            {
                rows[i] = new DialogueChoiceRowViewModel
                {
                    Value = choices[i].Value,
                    Text = choices[i].Text.ToString(),
                };
            }

            return rows;
        }

        static string ResolveDialogueTitle(RuntimeContentDatabase contentDb, int dialogueIndex)
        {
            if (contentDb == null || (uint)dialogueIndex >= (uint)contentDb.DialogueCount)
                return string.Empty;

            ref readonly DialogueDef dialogue = ref contentDb.Data.Dialogues[dialogueIndex];
            if (!string.IsNullOrWhiteSpace(dialogue.StringId))
                return dialogue.StringId.Trim();

            return string.IsNullOrWhiteSpace(dialogue.Id) ? string.Empty : dialogue.Id.Trim();
        }

        static string ResolveSpeakerName(RuntimeContentDatabase contentDb, in MorrowindDialogueSession session)
        {
            if (TryResolveSpeakerActor(contentDb, session, out var actor))
            {
                if (!string.IsNullOrWhiteSpace(actor.Name))
                    return actor.Name.Trim();
                if (!string.IsNullOrWhiteSpace(actor.Id))
                    return actor.Id.Trim();
            }

            return session.SpeakerId.ToString();
        }

        static int ResolveDisposition(
            RuntimeContentDatabase contentDb,
            EntityManager entityManager,
            in MorrowindDialogueSession session,
            out bool visible)
        {
            visible = false;
            if (!TryResolveSpeakerActor(contentDb, session, out var actor) || actor.Kind != ActorDefKind.Npc)
                return 0;

            visible = true;
            if (session.SpeakerEntity != Entity.Null
                && entityManager.Exists(session.SpeakerEntity)
                && entityManager.HasComponent<ActorDispositionState>(session.SpeakerEntity))
            {
                return Math.Clamp(entityManager.GetComponentData<ActorDispositionState>(session.SpeakerEntity).BaseDisposition, 0, 100);
            }

            return Math.Clamp(actor.Disposition, 0, 100);
        }

        static bool TryResolveSpeakerActor(RuntimeContentDatabase contentDb, in MorrowindDialogueSession session, out ActorDef actor)
        {
            actor = default;
            if (contentDb == null || !session.SpeakerActor.IsValid)
                return false;

            int index = session.SpeakerActor.Index;
            if ((uint)index >= (uint)contentDb.ActorCount)
                return false;

            actor = contentDb.Data.Actors[index];
            return true;
        }

        static string ResolveGameSettingString(RuntimeContentDatabase contentDb, string id, string fallback)
        {
            if (contentDb != null && contentDb.TryGetGameSettingString(id, out string value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();

            return fallback;
        }
    }
}
