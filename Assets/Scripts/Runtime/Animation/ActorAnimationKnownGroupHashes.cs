namespace VVardenfell.Runtime.Animation
{
    public static class ActorAnimationKnownGroupHashes
    {
        public const ulong Idle = 0x9E9B04C54923AE13UL;
        public const ulong IdleSneak = 0x2769B7FD2CE21FEDUL;
        public const ulong Jump = 0xEB1776DDF832A4CDUL;
        public const ulong WalkForward = 0xB62AD1A1423C8B5DUL;
        public const ulong WalkBack = 0xD43CCB142267564BUL;
        public const ulong WalkLeft = 0xF23EA2676D9CA591UL;
        public const ulong WalkRight = 0x701A3BE93FF23172UL;
        public const ulong RunForward = 0x95919554BFAE7CC7UL;
        public const ulong RunBack = 0xCEFFA3D4050FD779UL;
        public const ulong RunLeft = 0x328376C71BB8D4E7UL;
        public const ulong RunRight = 0xB66B76D4EAEFEE98UL;
        public const ulong SneakForward = 0x2F0E13F3AA8E42D4UL;
        public const ulong SneakBack = 0x8AC566E40B3F8B30UL;
        public const ulong SneakLeft = 0xC3B88C1899901F2EUL;
        public const ulong SneakRight = 0xF9CB260F703B1023UL;
        public const ulong SwimWalkForward = 0xAF16CE9D8794DC25UL;
        public const ulong SwimWalkBack = 0xC8B45B08D41EA683UL;
        public const ulong SwimWalkLeft = 0x8FC121D445CDF089UL;
        public const ulong SwimWalkRight = 0xBF7B89E70D24C6EAUL;
        public const ulong SwimRunForward = 0x5CD2FA747B46054FUL;
        public const ulong SwimRunBack = 0xA0E0AF2BF433FEF1UL;
        public const ulong SwimRunLeft = 0x2E81929E4AC7435FUL;
        public const ulong SwimRunRight = 0x97B36607C7A31490UL;

        public static ulong Movement(byte family, byte direction)
        {
            return family switch
            {
                4 => SwimRun(direction),
                3 => SwimWalk(direction),
                2 => Sneak(direction),
                1 => Run(direction),
                _ => Walk(direction),
            };
        }

        static ulong Walk(byte direction)
        {
            return direction switch
            {
                1 => WalkBack,
                2 => WalkLeft,
                3 => WalkRight,
                _ => WalkForward,
            };
        }

        static ulong Run(byte direction)
        {
            return direction switch
            {
                1 => RunBack,
                2 => RunLeft,
                3 => RunRight,
                _ => RunForward,
            };
        }

        static ulong Sneak(byte direction)
        {
            return direction switch
            {
                1 => SneakBack,
                2 => SneakLeft,
                3 => SneakRight,
                _ => SneakForward,
            };
        }

        static ulong SwimWalk(byte direction)
        {
            return direction switch
            {
                1 => SwimWalkBack,
                2 => SwimWalkLeft,
                3 => SwimWalkRight,
                _ => SwimWalkForward,
            };
        }

        static ulong SwimRun(byte direction)
        {
            return direction switch
            {
                1 => SwimRunBack,
                2 => SwimRunLeft,
                3 => SwimRunRight,
                _ => SwimRunForward,
            };
        }
    }
}
