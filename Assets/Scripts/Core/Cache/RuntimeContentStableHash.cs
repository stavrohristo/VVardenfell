using System;
using Unity.Collections;

namespace VVardenfell.Core.Cache
{
    public static class RuntimeContentStableHash
    {
        const ulong Offset = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;

        public static ulong HashId(string value)
            => HashNormalized(ContentId.NormalizeId(value ?? string.Empty));

        public static ulong HashId(FixedString128Bytes value)
            => HashNormalized(value);

        public static ulong HashId(FixedString64Bytes value)
            => HashNormalized(value);

        public static ulong HashId(FixedString512Bytes value)
            => HashNormalized(value);

        public static ulong HashInteriorCellId(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0UL;

            ulong hash = Offset;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c >= 'A' && c <= 'Z')
                    c = (char)(c + 32);
                hash ^= c <= 0x7f ? (byte)c : (byte)'?';
                hash *= Prime;
            }

            return hash == 0UL ? 1UL : hash;
        }

        public static ulong HashPath(string value)
            => HashNormalized(NormalizePath(value));

        public static ulong HashNormalized(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0UL;

            ulong hash = Offset;
            string trimmed = value.Trim();
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = char.ToLowerInvariant(trimmed[i]);
                byte b = c <= 0x7f ? (byte)c : (byte)'?';
                hash ^= b;
                hash *= Prime;
            }

            return hash == 0UL ? 1UL : hash;
        }

        public static ulong HashNormalized(FixedString128Bytes value)
        {
            if (value.Length == 0)
                return 0UL;

            int start = 0;
            int end = value.Length - 1;
            while (start <= end && IsAsciiWhiteSpace(value[start]))
                start++;
            while (end >= start && IsAsciiWhiteSpace(value[end]))
                end--;
            if (start > end)
                return 0UL;

            ulong hash = Offset;
            for (int i = start; i <= end; i++)
                AppendNormalizedByte(ref hash, value[i]);

            return hash == 0UL ? 1UL : hash;
        }

        public static ulong HashNormalized(FixedString64Bytes value)
        {
            if (value.Length == 0)
                return 0UL;

            int start = 0;
            int end = value.Length - 1;
            while (start <= end && IsAsciiWhiteSpace(value[start]))
                start++;
            while (end >= start && IsAsciiWhiteSpace(value[end]))
                end--;
            if (start > end)
                return 0UL;

            ulong hash = Offset;
            for (int i = start; i <= end; i++)
                AppendNormalizedByte(ref hash, value[i]);

            return hash == 0UL ? 1UL : hash;
        }

        public static ulong HashNormalized(FixedString512Bytes value)
        {
            if (value.Length == 0)
                return 0UL;

            int start = 0;
            int end = value.Length - 1;
            while (start <= end && IsAsciiWhiteSpace(value[start]))
                start++;
            while (end >= start && IsAsciiWhiteSpace(value[end]))
                end--;
            if (start > end)
                return 0UL;

            ulong hash = Offset;
            for (int i = start; i <= end; i++)
                AppendNormalizedByte(ref hash, value[i]);

            return hash == 0UL ? 1UL : hash;
        }

        static void AppendNormalizedByte(ref ulong hash, byte value)
        {
            if (value >= (byte)'A' && value <= (byte)'Z')
                value = (byte)(value + 32);
            if (value > 0x7f)
                value = (byte)'?';
            hash ^= value;
            hash *= Prime;
        }

        static bool IsAsciiWhiteSpace(byte value)
            => value == (byte)' '
               || value == (byte)'\t'
               || value == (byte)'\n'
               || value == (byte)'\r'
               || value == (byte)'\f'
               || value == (byte)'\v';

        public static string NormalizePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = value.Trim().Replace('/', '\\');
            while (normalized.Contains("\\\\", StringComparison.Ordinal))
                normalized = normalized.Replace("\\\\", "\\");
            return normalized;
        }
    }
}
