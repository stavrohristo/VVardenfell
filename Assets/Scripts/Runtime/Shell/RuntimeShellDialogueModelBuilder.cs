using System;
using System.Collections.Generic;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Combat;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.MorrowindScript;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.Shell
{
    public partial class RuntimeHudShellPresentationSystem
    {
        static DialogueWindowViewModel BuildDialogueModel(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            in MorrowindDialogueSession session,
            DynamicBuffer<MorrowindDialogueSessionLine> lines,
            DynamicBuffer<MorrowindKnownDialogueTopic> knownTopics,
            DynamicBuffer<MorrowindTopicJournalEntry> topicEntries,
            DynamicBuffer<MorrowindDialogueChoice> choices)
        {
            if (session.Active == 0)
                return null;

            bool topicsEnabled = session.ChoiceActive == 0 && session.Goodbye == 0;
            bool goodbyeEnabled = session.ChoiceActive == 0;
            int disposition = ResolveDisposition(ref contentBlob, entityManager, session, out bool dispositionVisible);
            string goodbyeText = ResolveGameSettingString(ref contentBlob, "sGoodbye", "Goodbye");

            return new DialogueWindowViewModel
            {
                SpeakerName = ResolveSpeakerName(ref contentBlob, session),
                Lines = BuildDialogueLines(ref contentBlob, lines),
                Topics = BuildVisibleDialogueTopics(ref contentBlob, entityManager, session, knownTopics, topicEntries),
                Choices = BuildDialogueChoices(session, choices),
                DispositionVisible = dispositionVisible,
                DispositionValue = disposition,
                DispositionFillNormalized = Math.Clamp(disposition / 100f, 0f, 1f),
                TopicsEnabled = topicsEnabled,
                GoodbyeEnabled = goodbyeEnabled,
                ShowInlineGoodbye = session.Goodbye != 0 && session.ChoiceActive == 0,
                GoodbyeText = goodbyeText,
            };
        }

        static DialogueResponseLineViewModel[] BuildDialogueLines(
            ref RuntimeContentBlob contentBlob,
            DynamicBuffer<MorrowindDialogueSessionLine> lines)
        {
            var rows = new List<DialogueResponseLineViewModel>(lines.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if ((uint)line.InfoIndex >= (uint)contentBlob.DialogueInfos.Length)
                    continue;

                string title = string.Empty;
                if (line.ShowTitle != 0 && (uint)line.DialogueIndex < (uint)contentBlob.Dialogues.Length)
                    title = ResolveRuntimeDialogueTitle(ref contentBlob, line.DialogueIndex);

                string body = contentBlob.DialogueInfos[line.InfoIndex].Response.ToString();
                rows.Add(new DialogueResponseLineViewModel
                {
                    Title = title,
                    Body = string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim(),
                });
            }

            return rows.ToArray();
        }

        static DialogueTopicRowViewModel[] BuildVisibleDialogueTopics(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            in MorrowindDialogueSession session,
            DynamicBuffer<MorrowindKnownDialogueTopic> knownTopics,
            DynamicBuffer<MorrowindTopicJournalEntry> topicEntries)
        {
            int count = Math.Min(contentBlob.Dialogues.Length, knownTopics.Length);
            var rows = new List<DialogueTopicRowViewModel>();
            for (int dialogueIndex = 0; dialogueIndex < count; dialogueIndex++)
            {
                if (knownTopics[dialogueIndex].Known == 0)
                    continue;

                ref var dialogue = ref contentBlob.Dialogues[dialogueIndex];
                if (dialogue.Type != DialogueDefType.Topic)
                    continue;

                if (!MorrowindDialogueFilterUtility.TryFindFirstMatchingInfo(
                        ref contentBlob,
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
                    Title = ResolveRuntimeDialogueTitle(ref contentBlob, dialogueIndex),
                    Selected = session.SelectedTopicDialogueIndex == dialogueIndex,
                    VisualState = ResolveTopicVisualState(
                        ref contentBlob,
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
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            in MorrowindDialogueSession session,
            DynamicBuffer<MorrowindKnownDialogueTopic> knownTopics,
            DynamicBuffer<MorrowindTopicJournalEntry> topicEntries,
            int dialogueIndex,
            int infoIndex)
        {
            if ((uint)infoIndex >= (uint)contentBlob.DialogueInfos.Length)
                return DialogueTopicVisualState.Normal;

            ref var info = ref contentBlob.DialogueInfos[infoIndex];
            bool inJournal = MorrowindDialogueUtility.ContainsTopicEntry(topicEntries, dialogueIndex, infoIndex);
            if (!inJournal && IsActorSpecificInfo(ref contentBlob, session, ref info))
                return DialogueTopicVisualState.Specific;

            if (inJournal
                && !ExhaustedTopicRevealsUnknownActorTopic(ref contentBlob, entityManager, session, knownTopics, info.Response.ToString()))
                return DialogueTopicVisualState.Exhausted;

            return DialogueTopicVisualState.Normal;
        }

        static bool IsActorSpecificInfo(
            ref RuntimeContentBlob contentBlob,
            in MorrowindDialogueSession session,
            ref RuntimeDialogueInfoDefBlob info)
        {
            string infoActorId = info.ActorId.ToString();
            if (string.IsNullOrWhiteSpace(infoActorId)
                || !TryResolveSpeakerActorIndex(ref contentBlob, session, out int actorIndex))
                return false;

            ref var actor = ref contentBlob.Actors[actorIndex];
            string normalizedActorId = ContentId.NormalizeId(infoActorId);
            return string.Equals(ContentId.NormalizeId(actor.Id.ToString()), normalizedActorId, StringComparison.Ordinal)
                   || string.Equals(ContentId.NormalizeId(actor.OriginalId.ToString()), normalizedActorId, StringComparison.Ordinal);
        }

        static bool ExhaustedTopicRevealsUnknownActorTopic(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            in MorrowindDialogueSession session,
            DynamicBuffer<MorrowindKnownDialogueTopic> knownTopics,
            string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return false;

            int count = Math.Min(contentBlob.Dialogues.Length, knownTopics.Length);
            for (int dialogueIndex = 0; dialogueIndex < count; dialogueIndex++)
            {
                if (knownTopics[dialogueIndex].Known != 0)
                    continue;

                ref var dialogue = ref contentBlob.Dialogues[dialogueIndex];
                if (dialogue.Type != DialogueDefType.Topic)
                    continue;

                string stringId = dialogue.StringId.ToString();
                string id = string.IsNullOrWhiteSpace(stringId) ? dialogue.Id.ToString() : stringId;
                if (!MorrowindDialogueUtility.ResponseContainsTopicReference(response, id))
                    continue;

                if (MorrowindDialogueFilterUtility.TryFindFirstMatchingInfo(
                        ref contentBlob,
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

        static string ResolveRuntimeDialogueTitle(ref RuntimeContentBlob contentBlob, int dialogueIndex)
        {
            if ((uint)dialogueIndex >= (uint)contentBlob.Dialogues.Length)
                return string.Empty;

            ref var dialogue = ref contentBlob.Dialogues[dialogueIndex];
            string stringId = dialogue.StringId.ToString();
            if (!string.IsNullOrWhiteSpace(stringId))
                return stringId.Trim();

            string id = dialogue.Id.ToString();
            return string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();
        }

        static string ResolveSpeakerName(ref RuntimeContentBlob contentBlob, in MorrowindDialogueSession session)
        {
            if (TryResolveSpeakerActorIndex(ref contentBlob, session, out int actorIndex))
            {
                ref var actor = ref contentBlob.Actors[actorIndex];
                string name = actor.Name.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                    return name.Trim();
                string id = actor.Id.ToString();
                if (!string.IsNullOrWhiteSpace(id))
                    return id.Trim();
            }

            return session.SpeakerId.ToString();
        }

        static int ResolveDisposition(
            ref RuntimeContentBlob contentBlob,
            EntityManager entityManager,
            in MorrowindDialogueSession session,
            out bool visible)
        {
            visible = false;
            if (!TryResolveSpeakerActorIndex(ref contentBlob, session, out int actorIndex))
                return 0;

            ref var actor = ref contentBlob.Actors[actorIndex];
            if (actor.Kind != ActorDefKind.Npc)
                return 0;

            visible = true;
            int modifier = 0;
            if (session.SpeakerEntity != Entity.Null
                && entityManager.Exists(session.SpeakerEntity)
                && entityManager.HasComponent<ActorCrimeState>(session.SpeakerEntity))
            {
                modifier = entityManager.GetComponentData<ActorCrimeState>(session.SpeakerEntity).CrimeDispositionModifier;
            }

            if (session.SpeakerEntity != Entity.Null
                && entityManager.Exists(session.SpeakerEntity)
                && entityManager.HasComponent<ActorDispositionState>(session.SpeakerEntity))
            {
                return Math.Clamp(entityManager.GetComponentData<ActorDispositionState>(session.SpeakerEntity).BaseDisposition + modifier, 0, 100);
            }

            return Math.Clamp(actor.Disposition + modifier, 0, 100);
        }

        static bool TryResolveSpeakerActorIndex(ref RuntimeContentBlob contentBlob, in MorrowindDialogueSession session, out int index)
        {
            index = -1;
            if (!session.SpeakerActor.IsValid)
                return false;

            index = session.SpeakerActor.Index;
            if ((uint)index >= (uint)contentBlob.Actors.Length)
                return false;

            return true;
        }

        static string ResolveGameSettingString(ref RuntimeContentBlob contentBlob, string id, string fallback)
        {
            string value = RuntimeContentBlobUtility.RequireGameSettingStringAllowEmptyByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(id));
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();

            return fallback;
        }
    }
}
