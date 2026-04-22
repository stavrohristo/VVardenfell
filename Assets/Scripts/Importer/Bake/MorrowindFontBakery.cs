using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using VVardenfell.Core.Cache;

namespace VVardenfell.Importer.Bake
{
    public static class MorrowindFontBakery
    {
        public const uint PayloadMagic = 0x30544E46u; // 'FNT0'

        struct Point
        {
            public float X;
            public float Y;
        }

        struct GlyphInfo
        {
            public float U1;
            public Point TopLeft;
            public Point TopRight;
            public Point BottomLeft;
            public Point BottomRight;
            public float Width;
            public float Height;
            public float U2;
            public float Kerning;
            public float Ascent;
        }

        readonly struct AdditionalMapping
        {
            public AdditionalMapping(int glyphIndex, int codepoint)
            {
                GlyphIndex = glyphIndex;
                Codepoint = codepoint;
            }

            public int GlyphIndex { get; }
            public int Codepoint { get; }
        }

        static readonly AdditionalMapping[] AdditionalMappings =
        {
            new(156, 0x00A2), new(89, 0x00A5), new(221, 0x00A6), new(99, 0x00A9), new(97, 0x00AA),
            new(60, 0x00AB), new(45, 0x00AD), new(114, 0x00AE), new(45, 0x00AF), new(241, 0x00B1),
            new(50, 0x00B2), new(51, 0x00B3), new(44, 0x00B8), new(49, 0x00B9), new(111, 0x00BA),
            new(62, 0x00BB), new(63, 0x00BF), new(65, 0x00C6), new(79, 0x00D8), new(97, 0x00E6),
            new(111, 0x00F8), new(79, 0x0152), new(111, 0x0153), new(83, 0x015A), new(115, 0x015B),
            new(89, 0x0178), new(90, 0x017D), new(122, 0x017E), new(102, 0x0192), new(94, 0x02C6),
            new(126, 0x02DC), new(69, 0x0401), new(137, 0x0451), new(45, 0x2012), new(45, 0x2013),
            new(45, 0x2014), new(39, 0x2018), new(39, 0x2019), new(44, 0x201A), new(39, 0x201B),
            new(34, 0x201C), new(34, 0x201D), new(44, 0x201E), new(34, 0x201F), new(43, 0x2020),
            new(216, 0x2021), new(46, 0x2026), new(37, 0x2030), new(60, 0x2039), new(62, 0x203A),
            new(101, 0x20AC), new(84, 0x2122), new(45, 0x2212),
        };

        public static UiFontRecord Bake(string fontId, string fontFilePath, BinaryWriter payloadWriter, out UiSourceRecord[] sources)
        {
            using var fs = File.OpenRead(fontFilePath);
            using var r = new BinaryReader(fs, Encoding.ASCII, leaveOpen: false);

            float fontSize = r.ReadSingle();
            int one = r.ReadInt32();
            int two = r.ReadInt32();
            if (one != 1 || two != 1)
                throw new InvalidDataException($"Unexpected font header values in '{fontFilePath}'.");

            var nameBytes = r.ReadBytes(284);
            if (nameBytes.Length != 284)
                throw new InvalidDataException($"Font '{fontFilePath}' truncated before texture name.");

            string textureBaseName = ReadCString(nameBytes);

            var glyphs = new GlyphInfo[256];
            for (int i = 0; i < glyphs.Length; i++)
                glyphs[i] = ReadGlyph(r);

            string texFilePath = Path.Combine(Path.GetDirectoryName(fontFilePath) ?? "", textureBaseName + ".tex");
            using var texFs = File.OpenRead(texFilePath);
            using var texReader = new BinaryReader(texFs, Encoding.ASCII, leaveOpen: false);
            int texWidth = texReader.ReadInt32();
            int texHeight = texReader.ReadInt32();
            if (texWidth <= 0 || texHeight <= 0)
                throw new InvalidDataException($"Font atlas '{texFilePath}' has invalid dimensions.");

            int pixelByteCount = checked(texWidth * texHeight * 4);
            var pixelBytes = texReader.ReadBytes(pixelByteCount);
            if (pixelBytes.Length != pixelByteCount)
                throw new InvalidDataException($"Font atlas '{texFilePath}' is truncated.");

            long payloadOffset = payloadWriter.BaseStream.Position;
            WritePayload(payloadWriter, fontSize, texWidth, texHeight, glyphs, pixelBytes);
            int payloadLength = checked((int)(payloadWriter.BaseStream.Position - payloadOffset));

            sources = new[]
            {
                CreateLooseSource(fontFilePath),
                CreateLooseSource(texFilePath),
            };

            return new UiFontRecord
            {
                Id = fontId,
                SourcePath = NormalizeRelative(fontFilePath),
                DefaultHeight = fontSize,
                PayloadOffset = payloadOffset,
                PayloadLength = payloadLength,
            };
        }

        private static void WritePayload(BinaryWriter w, float fontSize, int texWidth, int texHeight, GlyphInfo[] glyphs, byte[] pixels)
        {
            int glyphCount = 256 + AdditionalMappings.Length;

            w.Write(PayloadMagic);
            w.Write(fontSize);
            w.Write(texWidth);
            w.Write(texHeight);
            w.Write(glyphCount);

            for (int i = 0; i < glyphs.Length; i++)
                WriteGlyphPayload(w, MapBaseCodepoint(i), texWidth, texHeight, fontSize, glyphs[i]);

            for (int i = 0; i < AdditionalMappings.Length; i++)
            {
                var mapping = AdditionalMappings[i];
                WriteGlyphPayload(w, mapping.Codepoint, texWidth, texHeight, fontSize, glyphs[mapping.GlyphIndex]);
            }

            w.Write(pixels.Length);
            w.Write(pixels);
        }

        private static void WriteGlyphPayload(BinaryWriter w, int codepoint, int texWidth, int texHeight, float fontSize, GlyphInfo glyph)
        {
            float x = glyph.TopLeft.X * texWidth;
            float y = glyph.TopLeft.Y * texHeight;
            float width = (glyph.TopRight.X * texWidth) - x;
            float height = (glyph.BottomLeft.Y * texHeight) - y;
            float bearingY = fontSize - glyph.Ascent;

            w.Write(codepoint);
            w.Write(x);
            w.Write(y);
            w.Write(width);
            w.Write(height);
            w.Write(glyph.Width);
            w.Write(glyph.Kerning);
            w.Write(bearingY);
        }

        private static GlyphInfo ReadGlyph(BinaryReader r)
        {
            return new GlyphInfo
            {
                U1 = r.ReadSingle(),
                TopLeft = new Point { X = r.ReadSingle(), Y = r.ReadSingle() },
                TopRight = new Point { X = r.ReadSingle(), Y = r.ReadSingle() },
                BottomLeft = new Point { X = r.ReadSingle(), Y = r.ReadSingle() },
                BottomRight = new Point { X = r.ReadSingle(), Y = r.ReadSingle() },
                Width = r.ReadSingle(),
                Height = r.ReadSingle(),
                U2 = r.ReadSingle(),
                Kerning = r.ReadSingle(),
                Ascent = r.ReadSingle(),
            };
        }

        private static int MapBaseCodepoint(int glyphIndex)
        {
            if (glyphIndex >= 32 && glyphIndex <= 126)
                return glyphIndex;
            if (glyphIndex == 9)
                return '\t';
            return glyphIndex;
        }

        private static string ReadCString(byte[] bytes)
        {
            int end = Array.IndexOf(bytes, (byte)0);
            if (end < 0)
                end = bytes.Length;
            return Encoding.ASCII.GetString(bytes, 0, end);
        }

        private static UiSourceRecord CreateLooseSource(string path)
        {
            var info = new FileInfo(path);
            return new UiSourceRecord
            {
                Kind = UiSourceKind.LooseFile,
                Path = NormalizeRelative(path),
                Size = info.Length,
                MtimeTicks = info.LastWriteTimeUtc.Ticks,
            };
        }

        private static string NormalizeRelative(string fullPath)
        {
            string morrowindRoot = Path.GetPathRoot(fullPath) ?? "";
            string normalized = fullPath.Replace('/', '\\');
            if (normalized.Contains("\\Data Files\\", StringComparison.OrdinalIgnoreCase))
            {
                int idx = normalized.IndexOf("\\Data Files\\", StringComparison.OrdinalIgnoreCase);
                return normalized.Substring(idx + 1);
            }

            if (normalized.EndsWith("Morrowind.ini", StringComparison.OrdinalIgnoreCase))
                return "Morrowind.ini";

            return normalized.Replace(morrowindRoot, "").TrimStart('\\');
        }
    }
}
