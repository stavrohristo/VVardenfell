using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using VVardenfell.Runtime.UI.Assets;
using VVardenfell.Runtime.UI.Framework;

namespace VVardenfell.Runtime.UI.Shell
{
    sealed class BookReaderMarkupFormatter
    {
        public sealed class Page
        {
            public readonly List<Element> Elements = new();
        }

        public sealed class Element
        {
            public string Text;
            public Sprite Image;
            public Color Color;
            public BitmapTextAlignment Alignment;
            public float X;
            public float Y;
            public float Width;
            public float Height;
        }

        struct Style
        {
            public Color Color;
            public BitmapTextAlignment Alignment;
        }

        readonly BitmapFontAsset _font;
        readonly RuntimeInventoryIconService _iconService;
        readonly float _pixelHeight;
        readonly float _lineHeight;
        readonly float _scale;

        public BookReaderMarkupFormatter(BitmapFontAsset font, RuntimeInventoryIconService iconService, float pixelHeight)
        {
            _font = font ?? throw new InvalidOperationException("[VVardenfell][Books] Book formatter requires a bitmap font.");
            _iconService = iconService ?? throw new ArgumentNullException(nameof(iconService));
            _pixelHeight = pixelHeight;
            _scale = _font.LineHeight > 0f ? pixelHeight / _font.LineHeight : 1f;
            _lineHeight = Math.Max(1f, _font.LineHeight * _scale);
        }

        public Page[] FormatPages(string rawText, float pageWidth, float pageHeight, bool shrinkEsm3Text)
        {
            var blocks = Parse(rawText, shrinkEsm3Text);
            var pages = new List<Page> { new Page() };
            float y = 0f;

            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                if (block.PageBreak)
                {
                    if (pages[^1].Elements.Count != 0)
                    {
                        pages.Add(new Page());
                        y = 0f;
                    }
                    continue;
                }

                if (block.Image != null)
                {
                    float height = Math.Max(1f, block.ImageHeight);
                    if (y + height > pageHeight && pages[^1].Elements.Count != 0)
                    {
                        pages.Add(new Page());
                        y = 0f;
                    }

                    float width = Math.Max(1f, block.ImageWidth);
                    float x = block.Style.Alignment switch
                    {
                        BitmapTextAlignment.Center => Math.Max(0f, (pageWidth - width) * 0.5f),
                        BitmapTextAlignment.Right => Math.Max(0f, pageWidth - width),
                        _ => 0f,
                    };

                    pages[^1].Elements.Add(new Element
                    {
                        Image = block.Image,
                        X = x,
                        Y = y,
                        Width = width,
                        Height = height,
                        Color = Color.white,
                        Alignment = block.Style.Alignment,
                    });
                    y += height + _lineHeight * 0.5f;
                    continue;
                }

                if (string.IsNullOrEmpty(block.Text))
                {
                    y += _lineHeight;
                    continue;
                }

                var lines = WrapText(block.Text, pageWidth);
                for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
                {
                    if (y + _lineHeight > pageHeight && pages[^1].Elements.Count != 0)
                    {
                        pages.Add(new Page());
                        y = 0f;
                    }

                    pages[^1].Elements.Add(new Element
                    {
                        Text = lines[lineIndex],
                        Color = block.Style.Color,
                        Alignment = block.Style.Alignment,
                        X = 0f,
                        Y = y,
                        Width = pageWidth,
                        Height = _lineHeight,
                    });
                    y += _lineHeight;
                }
            }

            return pages.ToArray();
        }

        public Page FormatScroll(string rawText, float width, bool shrinkEsm3Text, out float contentHeight)
        {
            Page[] pages = FormatPages(rawText, width, 10000f, shrinkEsm3Text);
            var page = pages.Length > 0 ? pages[0] : new Page();
            contentHeight = 0f;
            for (int i = 0; i < page.Elements.Count; i++)
                contentHeight = Math.Max(contentHeight, page.Elements[i].Y + page.Elements[i].Height);
            return page;
        }

        sealed class Block
        {
            public string Text;
            public Sprite Image;
            public float ImageWidth;
            public float ImageHeight;
            public Style Style;
            public bool PageBreak;
        }

        List<Block> Parse(string rawText, bool shrinkEsm3Text)
        {
            string text = Normalize(rawText, shrinkEsm3Text);
            var blocks = new List<Block>();
            var currentText = new StringBuilder();
            var style = new Style { Color = Color.black, Alignment = BitmapTextAlignment.Left };
            var colorStack = new Stack<Color>();

            void Flush()
            {
                if (currentText.Length == 0)
                    return;

                blocks.Add(new Block
                {
                    Text = currentText.ToString(),
                    Style = style,
                });
                currentText.Clear();
            }

            for (int i = 0; i < text.Length;)
            {
                if (StartsWith(text, i, "[pagebreak]\n"))
                {
                    Flush();
                    blocks.Add(new Block { PageBreak = true, Style = style });
                    i += "[pagebreak]\n".Length;
                    continue;
                }

                if (text[i] == '<')
                {
                    int close = text.IndexOf('>', i + 1);
                    if (close < 0)
                    {
                        currentText.Append(text[i++]);
                        continue;
                    }

                    string tag = text.Substring(i + 1, close - i - 1).Trim();
                    string lower = tag.ToLowerInvariant();
                    if (lower == "br" || lower.StartsWith("br "))
                    {
                        currentText.Append('\n');
                        i = close + 1;
                        continue;
                    }
                    if (lower == "p" || lower.StartsWith("p "))
                    {
                        currentText.Append('\n');
                        currentText.Append('\n');
                        i = close + 1;
                        continue;
                    }
                    if (lower.StartsWith("div"))
                    {
                        Flush();
                        style.Alignment = ParseAlignment(tag);
                        i = close + 1;
                        continue;
                    }
                    if (lower == "/div")
                    {
                        Flush();
                        style.Alignment = BitmapTextAlignment.Left;
                        i = close + 1;
                        continue;
                    }
                    if (lower.StartsWith("font"))
                    {
                        colorStack.Push(style.Color);
                        if (TryParseColor(tag, out Color color))
                            style.Color = color;
                        i = close + 1;
                        continue;
                    }
                    if (lower == "/font")
                    {
                        style.Color = colorStack.Count > 0 ? colorStack.Pop() : Color.black;
                        i = close + 1;
                        continue;
                    }
                    if (lower.StartsWith("img"))
                    {
                        Flush();
                        blocks.Add(ParseImage(tag, style));
                        i = close + 1;
                        continue;
                    }

                    i = close + 1;
                    continue;
                }

                currentText.Append(text[i]);
                i++;
            }

            Flush();
            return blocks;
        }

        Block ParseImage(string tag, Style style)
        {
            string src = ReadAttribute(tag, "src");
            if (string.IsNullOrWhiteSpace(src))
                throw new InvalidOperationException("[VVardenfell][Books] Book image tag is missing src.");

            bool imgProtocol = src.StartsWith("img://", StringComparison.OrdinalIgnoreCase);
            if (imgProtocol)
                src = src.Substring("img://".Length);

            int width = ReadIntAttribute(tag, "width", imgProtocol ? 50 : -1);
            int height = ReadIntAttribute(tag, "height", imgProtocol ? 50 : -1);
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException($"[VVardenfell][Books] Book image '{src}' requires width and height.");

            return new Block
            {
                Image = _iconService.RequireTextureSprite(src, "book art"),
                ImageWidth = width,
                ImageHeight = height,
                Style = style,
            };
        }

        List<string> WrapText(string value, float maxWidth)
        {
            var lines = new List<string>();
            string[] paragraphs = value.Replace('\r', '\n').Split('\n');
            for (int p = 0; p < paragraphs.Length; p++)
            {
                string paragraph = paragraphs[p].Trim();
                if (paragraph.Length == 0)
                {
                    lines.Add(string.Empty);
                    continue;
                }

                string[] words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string line = string.Empty;
                for (int i = 0; i < words.Length; i++)
                {
                    string candidate = line.Length == 0 ? words[i] : line + " " + words[i];
                    if (line.Length == 0 || Measure(candidate) <= maxWidth)
                    {
                        line = candidate;
                        continue;
                    }

                    lines.Add(line);
                    line = words[i];
                }

                if (line.Length != 0)
                    lines.Add(line);
            }

            return lines;
        }

        float Measure(string value)
        {
            float width = 0f;
            for (int i = 0; i < value.Length; i++)
            {
                if (_font.TryGetGlyph(value[i], out var glyph))
                    width += glyph.Advance * _scale;
            }

            return width;
        }

        static string Normalize(string rawText, bool shrinkEsm3Text)
        {
            string text = (rawText ?? string.Empty).Replace("\r", string.Empty);
            int start = 0;
            while (start < text.Length && (text[start] == '\n' || text[start] == ' '))
                start++;
            if (start > 0)
                text = text.Substring(start);

            if (!shrinkEsm3Text)
                return text;

            int br = text.LastIndexOf("<br", StringComparison.OrdinalIgnoreCase);
            int p = text.LastIndexOf("<p", StringComparison.OrdinalIgnoreCase);
            int boundary = Math.Max(br, p);
            if (boundary <= 0)
                return text.TrimEnd('\n');

            return text.Substring(0, boundary).TrimEnd('\n');
        }

        static bool StartsWith(string text, int index, string value)
            => index + value.Length <= text.Length
               && string.Compare(text, index, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0;

        static BitmapTextAlignment ParseAlignment(string tag)
        {
            string align = ReadAttribute(tag, "align");
            return align?.ToLowerInvariant() switch
            {
                "center" => BitmapTextAlignment.Center,
                "right" => BitmapTextAlignment.Right,
                _ => BitmapTextAlignment.Left,
            };
        }

        static bool TryParseColor(string tag, out Color color)
        {
            string value = ReadAttribute(tag, "color");
            if (string.IsNullOrWhiteSpace(value))
            {
                color = Color.black;
                return false;
            }

            value = value.Trim().TrimStart('#');
            if (value.Length == 6
                && byte.TryParse(value.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r)
                && byte.TryParse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g)
                && byte.TryParse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            {
                color = new Color32(r, g, b, 255);
                return true;
            }

            throw new InvalidOperationException($"[VVardenfell][Books] Unsupported book font color '{value}'.");
        }

        static int ReadIntAttribute(string tag, string name, int fallback)
        {
            string value = ReadAttribute(tag, name);
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : fallback;
        }

        static string ReadAttribute(string tag, string name)
        {
            int index = tag.IndexOf(name, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return null;

            index += name.Length;
            while (index < tag.Length && char.IsWhiteSpace(tag[index]))
                index++;
            if (index >= tag.Length || tag[index] != '=')
                return null;
            index++;
            while (index < tag.Length && char.IsWhiteSpace(tag[index]))
                index++;
            if (index >= tag.Length)
                return string.Empty;

            char quote = tag[index] == '"' || tag[index] == '\'' ? tag[index++] : '\0';
            int start = index;
            if (quote != '\0')
            {
                int end = tag.IndexOf(quote, start);
                return end >= 0 ? tag.Substring(start, end - start) : tag.Substring(start);
            }

            while (index < tag.Length && !char.IsWhiteSpace(tag[index]))
                index++;
            return tag.Substring(start, index - start);
        }
    }
}
