using Unity.Collections;

namespace VVardenfell.Runtime
{
    public static class InteriorCellIdHash
    {
        const ulong Offset = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;

        public static ulong Hash(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0UL;

            ulong hash = Offset;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c >= 'A' && c <= 'Z')
                    c = (char)(c + 32);
                hash ^= c;
                hash *= Prime;
            }

            return hash == 0UL ? 1UL : hash;
        }

        public static ulong Hash(FixedString128Bytes value)
        {
            if (value.IsEmpty)
                return 0UL;

            ulong hash = Offset;
            for (int i = 0; i < value.Length; i++)
            {
                byte c = value[i];
                if (c >= (byte)'A' && c <= (byte)'Z')
                    c = (byte)(c + 32);
                hash ^= c;
                hash *= Prime;
            }

            return hash == 0UL ? 1UL : hash;
        }
    }
}
