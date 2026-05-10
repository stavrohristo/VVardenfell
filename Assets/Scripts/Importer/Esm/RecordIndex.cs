using System;
using System.Collections.Generic;

namespace VVardenfell.Importer.Esm
{
    /// <summary>
    /// Record types that are legitimately referenceable from a CELL's reference block.
    /// DIAL/INFO/SCPT/LAND/PGRD/GMST/etc. share the NAME subrecord for unrelated purposes
    /// and must be excluded, or a DIAL topic like "mudcrab" shadows the real CREA record.
    /// </summary>
    internal static class PlaceableTags
    {
        private static readonly HashSet<uint> _set = new()
        {
            EsmFourCC.Make('S','T','A','T'),
            EsmFourCC.Make('A','C','T','I'),
            EsmFourCC.Make('D','O','O','R'),
            EsmFourCC.Make('C','O','N','T'),
            EsmFourCC.Make('L','I','G','H'),
            EsmFourCC.Make('N','P','C','_'),
            EsmFourCC.Make('C','R','E','A'),
            EsmFourCC.Make('M','I','S','C'),
            EsmFourCC.Make('W','E','A','P'),
            EsmFourCC.Make('A','R','M','O'),
            EsmFourCC.Make('C','L','O','T'),
            EsmFourCC.Make('B','O','O','K'),
            EsmFourCC.Make('A','L','C','H'),
            EsmFourCC.Make('I','N','G','R'),
            EsmFourCC.Make('A','P','P','A'),
            EsmFourCC.Make('P','R','O','B'),
            EsmFourCC.Make('R','E','P','A'),
            EsmFourCC.Make('L','O','C','K'),
            EsmFourCC.Make('L','E','V','I'),
            EsmFourCC.Make('L','E','V','C'),
        };

        public static bool Contains(uint tag) => _set.Contains(tag);
    }

    /// <summary>
    /// Case-insensitive lookup of base records by id. Built by scanning an ESM once,
    /// capturing NAME/MODL/FNAM from every non-CELL record.
    /// </summary>
    public sealed class RecordIndex
    {
        private readonly Dictionary<string, BaseRecord> _byId;

        public int Count => _byId.Count;

        private RecordIndex(Dictionary<string, BaseRecord> byId) => _byId = byId;

        public bool TryGet(string id, out BaseRecord record) => _byId.TryGetValue(id, out record);

        /// <summary>Builds an index from the given ESM. Skips CELL records (they have a different layout).</summary>
        public static RecordIndex Build(EsmReader esm)
        {
            var map = new Dictionary<string, BaseRecord>(16 * 1024, StringComparer.OrdinalIgnoreCase);
            AddRecords(esm, map);
            return new RecordIndex(map);
        }

        public static RecordIndex Build(string[] sourcePaths)
        {
            var map = new Dictionary<string, BaseRecord>(16 * 1024, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < (sourcePaths?.Length ?? 0); i++)
            {
                using var esm = new EsmReader(sourcePaths[i]);
                AddRecords(esm, map);
            }

            return new RecordIndex(map);
        }

        static void AddRecords(EsmReader esm, Dictionary<string, BaseRecord> map)
        {
            esm.Seek(0);
            while (esm.ReadRecordHeader(out var rec))
            {
                if (!PlaceableTags.Contains(rec.Tag))
                {
                    esm.SkipRecord();
                    continue;
                }

                string id = null, model = null, displayName = null;

                while (esm.ReadSubrecordHeader(out var sub))
                {
                    if (sub.Tag == EsmFourCC.NAME && id == null)
                        id = esm.ReadSubrecordString();
                    else if (sub.Tag == EsmFourCC.MODL && model == null)
                        model = esm.ReadSubrecordString();
                    else if (sub.Tag == EsmFourCC.FNAM && displayName == null)
                        displayName = esm.ReadSubrecordString();
                    else
                        esm.SkipSubrecord();
                }

                if (!string.IsNullOrEmpty(id))
                    map[id] = new BaseRecord(rec.Tag, id, model ?? "", displayName ?? "");
            }
        }
    }
}
