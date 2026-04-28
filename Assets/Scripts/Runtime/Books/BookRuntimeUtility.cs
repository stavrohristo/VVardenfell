using Unity.Collections;
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

        public static FixedString128Bytes ToFixed128(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return default;

            var result = default(FixedString128Bytes);
            result.CopyFromTruncated(value);
            return result;
        }
    }
}
