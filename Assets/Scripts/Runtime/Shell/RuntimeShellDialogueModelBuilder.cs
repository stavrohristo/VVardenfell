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
            DynamicBuffer<MorrowindDialogueChoice> choices)
        {
            if (contentDb == null || session.Active == 0)
                return null;

            return new DialogueWindowViewModel
            {
                SpeakerName = session.SpeakerId.ToString(),
                Lines = BuildDialogueLines(contentDb, lines),
                Topics = session.ChoiceActive != 0
                    ? Array.Empty<DialogueTopicRowViewModel>()
                    : BuildVisibleDialogueTopics(contentDb, entityManager, session, knownTopics),
                Choices = BuildDialogueChoices(session, choices),
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
            DynamicBuffer<MorrowindKnownDialogueTopic> knownTopics)
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
                        out _,
                        out _))
                    continue;

                rows.Add(new DialogueTopicRowViewModel
                {
                    DialogueIndex = dialogueIndex,
                    Title = ResolveDialogueTitle(contentDb, dialogueIndex),
                    Selected = session.SelectedTopicDialogueIndex == dialogueIndex,
                });
            }

            rows.Sort(static (left, right) => string.Compare(left.Title, right.Title, StringComparison.OrdinalIgnoreCase));
            return rows.ToArray();
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
    }
}
