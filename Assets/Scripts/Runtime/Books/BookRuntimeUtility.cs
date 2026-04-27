using Unity.Collections;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Inventory;

namespace VVardenfell.Runtime.Books
{
    static class BookRuntimeUtility
    {
        static readonly uint BookTag = MakeTag('B', 'O', 'O', 'K');

        public static bool TryResolveBook(
            RuntimeContentDatabase contentDb,
            ContentReference content,
            out BookRuntimeMetadata metadata)
        {
            metadata = default;
            if (contentDb == null || content.Kind != ContentReferenceKind.Item || content.HandleValue <= 0)
                return false;

            var handle = new ItemDefHandle { Value = content.HandleValue };
            if (!handle.IsValid)
                return false;

            ref readonly var item = ref contentDb.Get(handle);
            if (item.RecordTag != BookTag)
                return false;

            string displayName = InventoryWindowStateSystem.ResolveDisplayName(item);
            metadata = new BookRuntimeMetadata(
                content,
                displayName,
                false,
                -1,
                0);
            return true;
        }

        public static BookReaderKind ResolveKind(in BookRuntimeMetadata metadata)
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

        static uint MakeTag(char a, char b, char c, char d)
            => (uint)a | ((uint)b << 8) | ((uint)c << 16) | ((uint)d << 24);
    }

    readonly struct BookRuntimeMetadata
    {
        public readonly ContentReference Content;
        public readonly string Title;
        public readonly bool IsScroll;
        public readonly int SkillId;
        public readonly int EnchantPoints;

        public BookRuntimeMetadata(
            ContentReference content,
            string title,
            bool isScroll,
            int skillId,
            int enchantPoints)
        {
            Content = content;
            Title = title;
            IsScroll = isScroll;
            SkillId = skillId;
            EnchantPoints = enchantPoints;
        }
    }
}
