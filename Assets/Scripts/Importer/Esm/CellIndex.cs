using System.Collections.Generic;
using System.IO;
using VVardenfell.Core.Cache;

namespace VVardenfell.Importer.Esm
{
    /// <summary>
    /// Iterates an ESM and yields a CellHeader for every CELL record, without parsing references.
    /// </summary>
    public static class CellIndex
    {
        public static IEnumerable<CellHeader> Enumerate(EsmReader esm)
        {
            esm.Seek(0);
            while (true)
            {
                long recordStart = esm.Position;
                if (!esm.ReadRecordHeader(out var rec)) yield break;

                if (rec.Tag != EsmFourCC.CELL)
                {
                    esm.SkipRecord();
                    continue;
                }

                string name = "";
                CellFlags flags = 0;
                int gridX = 0, gridY = 0;
                var environment = default(CellEnvironmentData);
                bool gotData = false;
                bool reachedRefs = false;

                while (esm.ReadSubrecordHeader(out var sub))
                {
                    if (sub.Tag == EsmFourCC.NAME)
                    {
                        name = esm.ReadSubrecordString();
                    }
                    else if (sub.Tag == EsmFourCC.DATA && sub.Size >= 12)
                    {
                        flags = (CellFlags)esm.ReadUInt32();
                        gridX = esm.ReadInt32();
                        gridY = esm.ReadInt32();
                        gotData = true;
                        // DATA may be larger; skip any tail
                        if (esm.SubrecordBytesLeft > 0) esm.SkipSubrecord();
                    }
                    else if (sub.Tag == EsmFourCC.RGNN)
                    {
                        environment.RegionId = esm.ReadSubrecordString();
                    }
                    else if (sub.Tag == EsmFourCC.WHGT && sub.Size >= 4)
                    {
                        environment.HasWater = 1;
                        environment.WaterHeight = esm.ReadFloat();
                        if (esm.SubrecordBytesLeft > 0) esm.SkipSubrecord();
                    }
                    else if (sub.Tag == EsmFourCC.AMBI && sub.Size >= 16)
                    {
                        environment.HasMood = 1;
                        environment.AmbientColorRgba = esm.ReadUInt32();
                        environment.DirectionalColorRgba = esm.ReadUInt32();
                        environment.FogColorRgba = esm.ReadUInt32();
                        environment.FogDensity = esm.ReadFloat();
                        if (esm.SubrecordBytesLeft > 0) esm.SkipSubrecord();
                    }
                    else if (sub.Tag == EsmFourCC.FRMR)
                    {
                        reachedRefs = true;
                        break;
                    }
                    else
                    {
                        esm.SkipSubrecord();
                    }
                }

                esm.SkipRecord();

                if (!gotData)
                    throw new InvalidDataException($"CELL without DATA subrecord at 0x{recordStart:X}");

                if (reachedRefs)
                {
                    // Reference stream starts at FRMR; the remaining record is intentionally skipped.
                }

                yield return new CellHeader(name, flags, gridX, gridY, environment, recordStart);
            }
        }
    }
}
