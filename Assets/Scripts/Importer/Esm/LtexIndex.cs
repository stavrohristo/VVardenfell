using System.Collections.Generic;

namespace VVardenfell.Importer.Esm
{
    /// <summary>
    /// One scan over an ESM collects every LTEX record into an <c>mIndex -> texture path</c>
    /// map. LTEX indices are plugin-local: VTEX subrecord values reference the LAND record's own
    /// source plugin LTEX table, offset by +1. Index 0 in VTEX means "default terrain texture" and
    /// does not come from any LTEX.
    /// </summary>
    public static class LtexIndex
    {
        /// <summary>The hardcoded texture used when VTEX entry is 0.</summary>
        public const string DefaultTexturePath = "textures\\_land_default.dds";

        /// <summary>
        /// Key = LTEX.mIndex (int, same as appears in VTEX-minus-one).
        /// Value = texture path as stored in the LTEX DATA subrecord (already includes the
        /// <c>textures\\</c> prefix for vanilla MW).
        /// </summary>
        public static Dictionary<int, string> Build(EsmReader esm)
        {
            esm.Seek(0);
            var map = new Dictionary<int, string>(256);
            while (esm.ReadRecordHeader(out var rec))
            {
                if (rec.Tag != EsmFourCC.LTEX)
                {
                    esm.SkipRecord();
                    continue;
                }

                string name = null;
                int index = -1;
                string texture = null;
                while (esm.ReadSubrecordHeader(out var sub))
                {
                    if (sub.Tag == EsmFourCC.NAME)
                    {
                        name = esm.ReadSubrecordString();
                    }
                    else if (sub.Tag == EsmFourCC.INTV && sub.Size >= 4)
                    {
                        index = esm.ReadInt32();
                        if (esm.SubrecordBytesLeft > 0) esm.SkipSubrecord();
                    }
                    else if (sub.Tag == EsmFourCC.DATA)
                    {
                        texture = esm.ReadSubrecordString();
                    }
                    else
                    {
                        esm.SkipSubrecord();
                    }
                }
                if (index >= 0 && !string.IsNullOrEmpty(texture))
                {
                    // Paths in LTEX DATA are sometimes already prefixed with "textures\\", sometimes not.
                    // Normalise so the BSA lookup always succeeds.
                    string normalized = texture.Replace('/', '\\');
                    if (!normalized.StartsWith("textures\\", System.StringComparison.OrdinalIgnoreCase))
                        normalized = "textures\\" + normalized;
                    map[index] = normalized;
                }
            }
            return map;
        }

        /// <summary>
        public static string ResolveVtex(ushort vtex, Dictionary<int, string> ltexMap)
        {
            if (vtex == 0) return DefaultTexturePath;
            return ltexMap.TryGetValue(vtex - 1, out var path) ? path : DefaultTexturePath;
        }

        public static string ResolveVtexRequired(ushort vtex, Dictionary<int, string> ltexMap, string context)
        {
            if (vtex == 0)
                return DefaultTexturePath;

            int ltexIndex = vtex - 1;
            if (ltexMap != null && ltexMap.TryGetValue(ltexIndex, out var path) && !string.IsNullOrWhiteSpace(path))
                return path;

            throw new System.IO.InvalidDataException(
                $"{context} references missing LTEX index {ltexIndex} from VTEX value {vtex}; native terrain atlas requires every non-zero VTEX entry to resolve to an LTEX texture.");
        }

        public static string ResolveVtexRequired(
            ushort vtex,
            IReadOnlyDictionary<string, Dictionary<int, string>> ltexMapsBySource,
            string sourcePath,
            string context)
        {
            if (vtex == 0)
                return DefaultTexturePath;

            int ltexIndex = vtex - 1;
            if (!string.IsNullOrWhiteSpace(sourcePath)
                && ltexMapsBySource != null
                && ltexMapsBySource.TryGetValue(sourcePath, out var sourceMap)
                && sourceMap != null
                && sourceMap.TryGetValue(ltexIndex, out var path)
                && !string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            string sourceName = string.IsNullOrWhiteSpace(sourcePath)
                ? "<unknown>"
                : System.IO.Path.GetFileName(sourcePath);
            throw new System.IO.InvalidDataException(
                $"{context} references missing LTEX index {ltexIndex} from VTEX value {vtex} in LAND source '{sourceName}'; native terrain atlas requires every non-zero VTEX entry to resolve against the LAND record's source content file.");
        }
    }
}
