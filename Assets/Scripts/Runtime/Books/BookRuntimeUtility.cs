using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;

namespace VVardenfell.Runtime.Books
{
    static class BookRuntimeUtility
    {
        public static BookReaderKind ResolveKind(in BookContentMetadata metadata)
        {
            return metadata.IsScroll ? BookReaderKind.Scroll : BookReaderKind.Book;
        }
    }
}
