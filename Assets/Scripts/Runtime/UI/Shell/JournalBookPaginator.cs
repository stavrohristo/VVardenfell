using System;
using System.Collections.Generic;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    enum JournalBookTextKind : byte
    {
        Body = 0,
        Header = 1,
    }

    sealed class JournalBookTextBlock
    {
        public string Text;
        public JournalBookTextKind Kind;
        public float SpaceAfter;
    }

    sealed class JournalBookLine
    {
        public string Text;
        public JournalBookTextKind Kind;
        public float Y;
    }

    sealed class JournalBookPage
    {
        public readonly List<JournalBookLine> Lines = new();
    }

    static class JournalBookPaginator
    {
        public static JournalBookPage[] Paginate(
            BitmapFontAsset font,
            float pixelHeight,
            float pageWidth,
            float pageHeight,
            IReadOnlyList<JournalBookTextBlock> blocks)
        {
            if (font == null)
                throw new InvalidOperationException("Journal book pagination requires a bitmap font.");

            var pages = new List<JournalBookPage>();
            var current = new JournalBookPage();
            pages.Add(current);

            float scale = font.LineHeight > 0f ? pixelHeight / font.LineHeight : 1f;
            float lineHeight = Math.Max(1f, font.LineHeight * scale);
            float y = 0f;

            if (blocks == null || blocks.Count == 0)
                return pages.ToArray();

            for (int b = 0; b < blocks.Count; b++)
            {
                var block = blocks[b];
                if (string.IsNullOrWhiteSpace(block?.Text))
                    continue;

                var lines = WrapText(font, scale, block.Text.Trim(), pageWidth);
                for (int i = 0; i < lines.Count; i++)
                {
                    if (y + lineHeight > pageHeight && current.Lines.Count > 0)
                    {
                        current = new JournalBookPage();
                        pages.Add(current);
                        y = 0f;
                    }

                    current.Lines.Add(new JournalBookLine
                    {
                        Text = lines[i],
                        Kind = block.Kind,
                        Y = y,
                    });
                    y += lineHeight;
                }

                if (block.SpaceAfter > 0f)
                {
                    if (y + block.SpaceAfter > pageHeight && current.Lines.Count > 0)
                    {
                        current = new JournalBookPage();
                        pages.Add(current);
                        y = 0f;
                    }
                    else
                        y += block.SpaceAfter;
                }
            }

            return pages.ToArray();
        }

        static List<string> WrapText(BitmapFontAsset font, float scale, string text, float maxWidth)
        {
            var result = new List<string>();
            string normalized = text.Replace("\r", "");
            string[] paragraphs = normalized.Split('\n');
            for (int p = 0; p < paragraphs.Length; p++)
            {
                string paragraph = paragraphs[p];
                if (string.IsNullOrWhiteSpace(paragraph))
                {
                    result.Add(string.Empty);
                    continue;
                }

                string[] words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0)
                    continue;

                string line = words[0];
                for (int i = 1; i < words.Length; i++)
                {
                    string candidate = line.Length == 0 ? words[i] : line + " " + words[i];
                    if (Measure(font, scale, candidate) <= maxWidth)
                        line = candidate;
                    else
                    {
                        result.Add(line);
                        line = words[i];
                    }
                }

                result.Add(line);
            }

            return result;
        }

        static float Measure(BitmapFontAsset font, float scale, string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0f;

            float width = 0f;
            for (int i = 0; i < value.Length; i++)
            {
                if (font.TryGetGlyph(value[i], out var glyph))
                    width += glyph.Advance * scale;
            }

            return width;
        }
    }
}
