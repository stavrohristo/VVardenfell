using System.Runtime.CompilerServices;

namespace VVardenfell.Importer.Esm
{
    /// <summary>
    /// 4-char record/subrecord tag packed as little-endian uint32 for fast equality.
    /// </summary>
    public static class EsmFourCC
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Make(char a, char b, char c, char d)
            => (uint)a | ((uint)b << 8) | ((uint)c << 16) | ((uint)d << 24);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToAscii(uint tag)
        {
            return new string(new[]
            {
                (char)(tag & 0xFF),
                (char)((tag >> 8) & 0xFF),
                (char)((tag >> 16) & 0xFF),
                (char)((tag >> 24) & 0xFF),
            });
        }

        public static readonly uint TES3 = Make('T', 'E', 'S', '3');
        public static readonly uint CELL = Make('C', 'E', 'L', 'L');
        public static readonly uint NAME = Make('N', 'A', 'M', 'E');
        public static readonly uint DATA = Make('D', 'A', 'T', 'A');
        public static readonly uint FRMR = Make('F', 'R', 'M', 'R');
        public static readonly uint XSCL = Make('X', 'S', 'C', 'L');
        public static readonly uint DNAM = Make('D', 'N', 'A', 'M');
        public static readonly uint DODT = Make('D', 'O', 'D', 'T');
        public static readonly uint DELE = Make('D', 'E', 'L', 'E');
        public static readonly uint NAM0 = Make('N', 'A', 'M', '0');
        public static readonly uint MODL = Make('M', 'O', 'D', 'L');
        public static readonly uint FNAM = Make('F', 'N', 'A', 'M');
        public static readonly uint LAND = Make('L', 'A', 'N', 'D');
        public static readonly uint INTV = Make('I', 'N', 'T', 'V');
        public static readonly uint VHGT = Make('V', 'H', 'G', 'T');
        public static readonly uint VNML = Make('V', 'N', 'M', 'L');
        public static readonly uint VCLR = Make('V', 'C', 'L', 'R');
        public static readonly uint VTEX = Make('V', 'T', 'E', 'X');
        public static readonly uint WNAM = Make('W', 'N', 'A', 'M');
        public static readonly uint LTEX = Make('L', 'T', 'E', 'X');
    }
}
