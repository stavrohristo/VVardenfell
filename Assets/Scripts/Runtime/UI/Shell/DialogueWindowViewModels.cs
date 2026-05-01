using System;

namespace VVardenfell.Runtime.UI.Shell
{
    public sealed class DialogueWindowViewModel
    {
        public string SpeakerName;
        public DialogueResponseLineViewModel[] Lines = Array.Empty<DialogueResponseLineViewModel>();
        public DialogueTopicRowViewModel[] Topics = Array.Empty<DialogueTopicRowViewModel>();
        public DialogueChoiceRowViewModel[] Choices = Array.Empty<DialogueChoiceRowViewModel>();
    }

    public sealed class DialogueResponseLineViewModel
    {
        public string Title;
        public string Body;
    }

    public sealed class DialogueTopicRowViewModel
    {
        public int DialogueIndex;
        public string Title;
        public bool Selected;
    }

    public sealed class DialogueChoiceRowViewModel
    {
        public int Value;
        public string Text;
    }
}
