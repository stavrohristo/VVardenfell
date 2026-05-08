using System;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.UI.Shell
{
    public enum DialogueServiceWindowLayoutKind
    {
        List = 0,
        Persuasion = 1,
    }

    public sealed class DialogueServiceWindowViewModel
    {
        public DialogueServiceWindowLayoutKind LayoutKind;
        public string Title;
        public string Header;
        public string FooterText;
        public DialogueServiceRowViewModel[] Rows = Array.Empty<DialogueServiceRowViewModel>();
        public DialogueServiceButtonViewModel[] Buttons = Array.Empty<DialogueServiceButtonViewModel>();
    }

    public sealed class DialogueServiceRowViewModel
    {
        public string LeftText;
        public string RightText;
        public bool Enabled = true;
        public MorrowindDialogueServiceAction Action;
        public int Int0;
        public int Int1;
    }

    public sealed class DialogueServiceButtonViewModel
    {
        public string Text;
        public bool Enabled = true;
        public MorrowindDialogueServiceAction Action;
        public int Int0;
        public int Int1;
    }
}
