using System.Collections.Generic;
using System.IO;

namespace VVardenfell.Importer.Esm
{
    /// <summary>
    /// Reads the reference block of a CELL record. Seeks to the cell's record offset,
    /// skips cell-level subrecords, then groups reference subrecords by FRMR boundaries.
    /// </summary>
    public static class CellReader
    {
        public static List<CellReference> ReadReferences(EsmReader esm, CellHeader cell)
        {
            esm.Seek(cell.RecordOffset);
            if (!esm.ReadRecordHeader(out var rec) || rec.Tag != EsmFourCC.CELL)
                throw new InvalidDataException($"Expected CELL record at 0x{cell.RecordOffset:X}");

            var result = new List<CellReference>();

            // Reference state — written as subrecords stream in; emitted on next FRMR or end.
            bool inRef = false;
            uint formId = 0;
            string baseId = "";
            float px = 0, py = 0, pz = 0, rx = 0, ry = 0, rz = 0;
            float scale = 1f;
            bool deleted = false;
            string soulId = "";
            int lockLevel = 0;
            string keyId = "";
            string trapId = "";
            bool isDoor = false;
            string doorDest = "";
            float ddx = 0, ddy = 0, ddz = 0, ddrx = 0, ddry = 0, ddrz = 0;

            void Emit()
            {
                if (!inRef) return;
                result.Add(new CellReference(
                    formId, baseId,
                    px, py, pz, rx, ry, rz,
                    scale, deleted,
                    soulId, lockLevel, keyId, trapId,
                    isDoor, doorDest,
                    ddx, ddy, ddz, ddrx, ddry, ddrz));
            }

            void ResetRef()
            {
                inRef = true;
                formId = 0; baseId = ""; scale = 1f; deleted = false; soulId = "";
                lockLevel = 0; keyId = ""; trapId = "";
                px = py = pz = rx = ry = rz = 0;
                isDoor = false; doorDest = "";
                ddx = ddy = ddz = ddrx = ddry = ddrz = 0;
            }

            while (esm.ReadSubrecordHeader(out var sub))
            {
                uint tag = sub.Tag;

                if (tag == EsmFourCC.FRMR)
                {
                    Emit();
                    ResetRef();
                    formId = esm.ReadUInt32();
                    if (esm.SubrecordBytesLeft > 0) esm.SkipSubrecord();
                }
                else if (!inRef)
                {
                    // Cell-level subrecord (NAME/DATA/RGNN/NAM0/NAM5/WHGT/AMBI/INTV). Skip.
                    esm.SkipSubrecord();
                }
                else if (tag == EsmFourCC.NAME)
                {
                    baseId = esm.ReadSubrecordString();
                }
                else if (tag == EsmFourCC.DATA && sub.Size >= 24)
                {
                    px = esm.ReadFloat(); py = esm.ReadFloat(); pz = esm.ReadFloat();
                    rx = esm.ReadFloat(); ry = esm.ReadFloat(); rz = esm.ReadFloat();
                    if (esm.SubrecordBytesLeft > 0) esm.SkipSubrecord();
                }
                else if (tag == EsmFourCC.XSCL && sub.Size >= 4)
                {
                    scale = esm.ReadFloat();
                    if (esm.SubrecordBytesLeft > 0) esm.SkipSubrecord();
                }
                else if (tag == EsmFourCC.XSOL)
                {
                    soulId = esm.ReadSubrecordString();
                }
                else if (tag == EsmFourCC.FLTV && sub.Size >= 4)
                {
                    lockLevel = esm.ReadInt32();
                    if (esm.SubrecordBytesLeft > 0) esm.SkipSubrecord();
                }
                else if (tag == EsmFourCC.KNAM)
                {
                    keyId = esm.ReadSubrecordString();
                }
                else if (tag == EsmFourCC.TNAM)
                {
                    trapId = esm.ReadSubrecordString();
                }
                else if (tag == EsmFourCC.DODT && sub.Size >= 24)
                {
                    isDoor = true;
                    ddx = esm.ReadFloat(); ddy = esm.ReadFloat(); ddz = esm.ReadFloat();
                    ddrx = esm.ReadFloat(); ddry = esm.ReadFloat(); ddrz = esm.ReadFloat();
                    if (esm.SubrecordBytesLeft > 0) esm.SkipSubrecord();
                }
                else if (tag == EsmFourCC.DNAM)
                {
                    doorDest = esm.ReadSubrecordString();
                }
                else if (tag == EsmFourCC.DELE)
                {
                    deleted = true;
                    esm.SkipSubrecord();
                }
                else
                {
                    esm.SkipSubrecord();
                }
            }

            Emit();
            return result;
        }
    }
}
