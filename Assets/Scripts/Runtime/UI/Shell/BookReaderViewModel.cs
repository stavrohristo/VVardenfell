namespace VVardenfell.Runtime.UI.Shell
{
    public sealed class BookReaderViewModel
    {
        public bool Visible;
        public bool IsScroll;
        public bool AllowTake;
        public int CurrentPage;
        public float ScrollOffset;
        public string Title;
        public string Text;
        public ulong ContentSignature;
    }
}
