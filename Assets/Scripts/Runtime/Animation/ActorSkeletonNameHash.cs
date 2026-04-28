using Unity.Collections;

namespace VVardenfell.Runtime.Animation
{
    public static class ActorSkeletonNameHash
    {
        const ulong Offset = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;

        public const ulong Bip01Head = 0x05362AC273B5850BUL;
        public const ulong WeaponBoneLeft = 0xF12F7001C1EF08CAUL;
        public const ulong WeaponBone = 0x07D4A7E4F9011281UL;
        public const ulong Bip01RHand = 0xA1CE17BA767A07BEUL;
        public const ulong ShieldBone = 0x4706134AF0A28FDEUL;
        public const ulong Bip01LForearm = 0x4F20300DB61C303BUL;

        public static ulong Hash(FixedString64Bytes value)
        {
            ulong hash = Offset;
            for (int i = 0; i < value.Length; i++)
            {
                byte c = value[i];
                if (c >= (byte)'A' && c <= (byte)'Z')
                    c = (byte)(c + 32);
                hash ^= c;
                hash *= Prime;
            }

            return hash;
        }
    }
}
