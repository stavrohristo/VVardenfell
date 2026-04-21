using System.Collections.Generic;

namespace VVardenfell.Importer.Esm
{
    /// <summary>
    /// One scan over the ESM collects every LTEX record into an <c>mIndex → BSA texture path</c>
    /// map. Keyed by the LTEX's own <c>INTV</c> index — VTEX subrecord values reference this same
    /// index but offset by +1 (OpenMW <c>storage.cpp:374</c> "All vtex ids are +1 compared to the
    /// ltex ids"). Index 0 in VTEX means "default terrain texture" and does not come from any LTEX.
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
        /// Resolve a VTEX subrecord value (0 = default, otherwise ltex-index + 1) to a BSA texture
        /// path. Returns <see cref="DefaultTexturePath"/> for unknown indices so we never fail to
        /// bake a layer.
        /// </summary>
        public static string ResolveVtex(ushort vtex, Dictionary<int, string> ltexMap)
        {
            if (vtex == 0) return DefaultTexturePath;
            return ltexMap.TryGetValue(vtex - 1, out var path) ? path : DefaultTexturePath;
        }
    }
}
