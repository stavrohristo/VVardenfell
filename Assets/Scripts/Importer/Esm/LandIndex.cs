using System.IO;

namespace VVardenfell.Importer.Esm
{
    /// <summary>
    /// Locate a LAND record for a given exterior cell grid, parse its subrecords,
    /// and decode the delta-encoded VHGT heightmap.
    /// </summary>
    public static class LandIndex
    {
        public static LandRecord FindForCell(EsmReader esm, int gridX, int gridY)
        {
            esm.Seek(0);
            while (esm.ReadRecordHeader(out var rec))
            {
                if (rec.Tag != EsmFourCC.LAND)
                {
                    esm.SkipRecord();
                    continue;
                }

                var land = ParseLandRecord(esm);
                if (land != null && land.GridX == gridX && land.GridY == gridY)
                    return land;
            }
            return null;
        }

        /// <summary>
        /// Scan the ESM once, returning a map of (gridX, gridY) → absolute record-header offset
        /// for every LAND record. Use with <see cref="ReadAt"/> to parse cells on demand.
        /// </summary>
        public static System.Collections.Generic.Dictionary<(int, int), long> BuildOffsetMap(EsmReader esm)
        {
            esm.Seek(0);
            var map = new System.Collections.Generic.Dictionary<(int, int), long>(2048);
            while (true)
            {
                long headerStart = esm.Position;
                if (!esm.ReadRecordHeader(out var rec)) break;
                if (rec.Tag != EsmFourCC.LAND)
                {
                    esm.SkipRecord();
                    continue;
                }

                int gx = 0, gy = 0;
                bool hasIntv = false;
                while (esm.ReadSubrecordHeader(out var sub))
                {
                    if (sub.Tag == EsmFourCC.INTV && sub.Size >= 8)
                    {
                        gx = esm.ReadInt32();
                        gy = esm.ReadInt32();
                        hasIntv = true;
                        if (esm.SubrecordBytesLeft > 0) esm.SkipSubrecord();
                        break;
                    }
                    esm.SkipSubrecord();
                }
                esm.SkipRecord();
                if (hasIntv) map[(gx, gy)] = headerStart;
            }
            return map;
        }

        /// <summary>Parse a LAND record whose header starts at <paramref name="recordOffset"/>.</summary>
        public static LandRecord ReadAt(EsmReader esm, long recordOffset)
        {
            esm.Seek(recordOffset);
            if (!esm.ReadRecordHeader(out var rec) || rec.Tag != EsmFourCC.LAND)
                throw new InvalidDataException($"Expected LAND at 0x{recordOffset:X}");
            return ParseLandRecord(esm);
        }

        private static LandRecord ParseLandRecord(EsmReader esm)
        {
            var land = new LandRecord();
            bool hasIntv = false;

            while (esm.ReadSubrecordHeader(out var sub))
            {
                if (sub.Tag == EsmFourCC.INTV && sub.Size >= 8)
                {
                    land.GridX = esm.ReadInt32();
                    land.GridY = esm.ReadInt32();
                    hasIntv = true;
                    if (esm.SubrecordBytesLeft > 0) esm.SkipSubrecord();
                }
                else if (sub.Tag == EsmFourCC.VHGT && sub.Size >= 4 + LandRecord.NumVerts + 3)
                {
                    float offset = esm.ReadFloat();
                    var bytes = ReadBytes(esm, LandRecord.NumVerts);
                    esm.SkipSubrecord(); // skip 3-byte padding remainder

                    var heights = new float[LandRecord.NumVerts];
                    float min = float.PositiveInfinity, max = float.NegativeInfinity;
                    float rowOffset = offset;
                    for (int y = 0; y < LandRecord.Size; y++)
                    {
                        rowOffset += (sbyte)bytes[y * LandRecord.Size];
                        heights[y * LandRecord.Size] = rowOffset * LandRecord.HeightScale;
                        if (heights[y * LandRecord.Size] < min) min = heights[y * LandRecord.Size];
                        if (heights[y * LandRecord.Size] > max) max = heights[y * LandRecord.Size];

                        float colOffset = rowOffset;
                        for (int x = 1; x < LandRecord.Size; x++)
                        {
                            colOffset += (sbyte)bytes[y * LandRecord.Size + x];
                            int idx = y * LandRecord.Size + x;
                            heights[idx] = colOffset * LandRecord.HeightScale;
                            if (heights[idx] < min) min = heights[idx];
                            if (heights[idx] > max) max = heights[idx];
                        }
                    }
                    land.Heights = heights;
                    land.HasHeights = true;
                    land.MinHeight = min;
                    land.MaxHeight = max;
                }
                else if (sub.Tag == EsmFourCC.VNML && sub.Size >= 3 * LandRecord.NumVerts)
                {
                    var nrm = ReadBytes(esm, 3 * LandRecord.NumVerts);
                    land.Normals = new sbyte[nrm.Length];
                    for (int i = 0; i < nrm.Length; i++) land.Normals[i] = (sbyte)nrm[i];
                    if (esm.SubrecordBytesLeft > 0) esm.SkipSubrecord();
                }
                else if (sub.Tag == EsmFourCC.VCLR && sub.Size >= 3 * LandRecord.NumVerts)
                {
                    land.Colors = ReadBytes(esm, 3 * LandRecord.NumVerts);
                    if (esm.SubrecordBytesLeft > 0) esm.SkipSubrecord();
                }
                else if (sub.Tag == EsmFourCC.VTEX && sub.Size >= 2 * LandRecord.NumTextures)
                {
                    // Morrowind's on-disk layout chops the 16x16 grid into sixteen 4x4 blocks stored
                    // in nested quadrant-order. OpenMW unscrambles it via the quad loop in
                    // loadland.cpp:31-39; we copy that formula verbatim into row-major output.
                    var raw = new ushort[LandRecord.NumTextures];
                    for (int i = 0; i < LandRecord.NumTextures; i++) raw[i] = esm.ReadUInt16();
                    var rowMajor = new ushort[LandRecord.NumTextures];
                    int readPos = 0;
                    for (int y1 = 0; y1 < 4; y1++)
                        for (int x1 = 0; x1 < 4; x1++)
                            for (int y2 = 0; y2 < 4; y2++)
                                for (int x2 = 0; x2 < 4; x2++)
                                    rowMajor[(y1 * 4 + y2) * 16 + (x1 * 4 + x2)] = raw[readPos++];
                    land.VtexIndices = rowMajor;
                    if (esm.SubrecordBytesLeft > 0) esm.SkipSubrecord();
                }
                else
                {
                    esm.SkipSubrecord();
                }
            }

            if (!hasIntv) throw new InvalidDataException("LAND without INTV subrecord");
            return land;
        }

        private static byte[] ReadBytes(EsmReader esm, int count)
        {
            var buf = new byte[count];
            for (int i = 0; i < count; i++) buf[i] = esm.ReadByte();
            return buf;
        }
    }
}
