using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.UI.Shell;

namespace VVardenfell.Runtime.Shell
{
    public partial class RuntimeHudShellPresentationSystem
    {
        static BookReaderViewModel BuildBookReaderModel(ref RuntimeContentBlob content, in BookReaderState state)
        {
            string text = RuntimeContentMetadataResolver.RequireBookText(ref content, state.Content);
            return new BookReaderViewModel
            {
                Visible = state.Visible != 0,
                IsScroll = (BookReaderKind)state.Kind == BookReaderKind.Scroll,
                AllowTake = state.AllowTake != 0,
                CurrentPage = state.CurrentPage,
                ScrollOffset = state.ScrollOffset,
                Title = state.Title.ToString(),
                Text = text,
                ContentSignature = ((ulong)(uint)state.Content.Kind << 32) | (uint)state.Content.HandleValue,
            };
        }
    }
}
