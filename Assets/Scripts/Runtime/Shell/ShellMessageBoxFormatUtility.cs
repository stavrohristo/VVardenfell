using System;
using System.Globalization;
using System.Text;
using Unity.Collections;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Shell
{
    static class ShellMessageBoxFormatUtility
    {
        public static FixedString512Bytes Format(in ShellMessageBoxRequest request)
        {
            string message = request.Body.ToString();
            if (request.ArgCount == 0 || string.IsNullOrEmpty(message))
                return RuntimeFixedStringUtility.ToFixed512OrDefault(message);

            var formatted = new StringBuilder(message.Length + 32);
            int argIndex = 0;
            for (int i = 0; i < message.Length; i++)
            {
                char c = message[i];
                if (c != '%')
                {
                    formatted.Append(c);
                    continue;
                }

                if (++i >= message.Length)
                    break;
                if (message[i] == '%')
                {
                    formatted.Append('%');
                    continue;
                }

                int flags = 0;
                while (i < message.Length)
                {
                    if (message[i] == '-')
                        flags |= 4;
                    else if (message[i] == '+')
                        flags |= 2;
                    else if (message[i] == ' ')
                        flags |= 1;
                    else if (message[i] == '0')
                        flags |= 8;
                    else if (message[i] == '#')
                        flags |= 16;
                    else
                        break;
                    i++;
                }

                int width = ParseNumber(message, ref i, -1);
                int precision = -1;
                if (i < message.Length && message[i] == '.')
                {
                    i++;
                    precision = ParseNumber(message, ref i, 0);
                }

                if (i >= message.Length)
                    break;

                char placeholder = message[i];
                if (!IsSupportedPlaceholder(placeholder))
                {
                    formatted.Append(placeholder);
                    continue;
                }

                if (argIndex >= request.ArgCount)
                    throw new InvalidOperationException("[VVardenfell][MWScript] MessageBox format argument underflow.");

                formatted.Append(FormatArgument(request, argIndex++, placeholder, flags, width, precision));
            }

            return RuntimeFixedStringUtility.ToFixed512OrDefault(formatted.ToString());
        }

        static string FormatArgument(in ShellMessageBoxRequest request, int argIndex, char placeholder, int flags, int width, int precision)
        {
            bool floatPlaceholder = placeholder is 'f' or 'F' or 'e' or 'E' or 'g' or 'G';
            string text = floatPlaceholder
                ? FormatFloat(GetFloat(request, argIndex), placeholder, precision)
                : FormatInteger(GetInt(request, argIndex), precision);

            if ((flags & 16) != 0 && floatPlaceholder && precision > 0 && text.IndexOf('.', StringComparison.Ordinal) < 0)
                text += ".";

            if (!text.StartsWith("-", StringComparison.Ordinal))
            {
                if ((flags & 2) != 0)
                    text = "+" + text;
                else if ((flags & 1) != 0)
                    text = " " + text;
            }

            if (width > 0 && text.Length < width)
            {
                bool left = (flags & 4) != 0;
                char pad = (flags & 8) != 0 && !left ? '0' : ' ';
                int padCount = width - text.Length;
                if (left)
                    text = text + new string(' ', padCount);
                else if (pad == '0' && (text.StartsWith("+", StringComparison.Ordinal) || text.StartsWith("-", StringComparison.Ordinal) || text.StartsWith(" ", StringComparison.Ordinal)))
                    text = text[0] + new string('0', padCount) + text.Substring(1);
                else
                    text = new string(pad, padCount) + text;
            }

            return text;
        }

        static string FormatFloat(float value, char placeholder, int precision)
        {
            string specifier = placeholder switch
            {
                'e' => "e",
                'E' => "E",
                'g' => "G",
                'G' => "G",
                _ => "F",
            };
            if (precision >= 0)
                specifier += precision.ToString(CultureInfo.InvariantCulture);
            return value.ToString(specifier, CultureInfo.InvariantCulture);
        }

        static string FormatInteger(int value, int precision)
        {
            string text = Math.Abs(value).ToString(CultureInfo.InvariantCulture);
            if (precision > text.Length)
                text = new string('0', precision - text.Length) + text;
            return value < 0 ? "-" + text : text;
        }

        static int ParseNumber(string message, ref int index, int fallback)
        {
            int start = index;
            while (index < message.Length && message[index] >= '0' && message[index] <= '9')
                index++;
            return index > start && int.TryParse(message.Substring(start, index - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : fallback;
        }

        static bool IsSupportedPlaceholder(char c)
            => c is 'd' or 'i' or 'f' or 'F' or 'e' or 'E' or 'g' or 'G';

        static int GetInt(in ShellMessageBoxRequest request, int index)
            => index switch
            {
                0 => request.Arg0Kind == (byte)MorrowindScriptValueKind.Float ? (int)request.Arg0Float : request.Arg0Int,
                1 => request.Arg1Kind == (byte)MorrowindScriptValueKind.Float ? (int)request.Arg1Float : request.Arg1Int,
                2 => request.Arg2Kind == (byte)MorrowindScriptValueKind.Float ? (int)request.Arg2Float : request.Arg2Int,
                3 => request.Arg3Kind == (byte)MorrowindScriptValueKind.Float ? (int)request.Arg3Float : request.Arg3Int,
                4 => request.Arg4Kind == (byte)MorrowindScriptValueKind.Float ? (int)request.Arg4Float : request.Arg4Int,
                5 => request.Arg5Kind == (byte)MorrowindScriptValueKind.Float ? (int)request.Arg5Float : request.Arg5Int,
                6 => request.Arg6Kind == (byte)MorrowindScriptValueKind.Float ? (int)request.Arg6Float : request.Arg6Int,
                7 => request.Arg7Kind == (byte)MorrowindScriptValueKind.Float ? (int)request.Arg7Float : request.Arg7Int,
                _ => throw new InvalidOperationException("[VVardenfell][MWScript] MessageBox format argument overflow."),
            };

        static float GetFloat(in ShellMessageBoxRequest request, int index)
            => index switch
            {
                0 => request.Arg0Kind == (byte)MorrowindScriptValueKind.Float ? request.Arg0Float : request.Arg0Int,
                1 => request.Arg1Kind == (byte)MorrowindScriptValueKind.Float ? request.Arg1Float : request.Arg1Int,
                2 => request.Arg2Kind == (byte)MorrowindScriptValueKind.Float ? request.Arg2Float : request.Arg2Int,
                3 => request.Arg3Kind == (byte)MorrowindScriptValueKind.Float ? request.Arg3Float : request.Arg3Int,
                4 => request.Arg4Kind == (byte)MorrowindScriptValueKind.Float ? request.Arg4Float : request.Arg4Int,
                5 => request.Arg5Kind == (byte)MorrowindScriptValueKind.Float ? request.Arg5Float : request.Arg5Int,
                6 => request.Arg6Kind == (byte)MorrowindScriptValueKind.Float ? request.Arg6Float : request.Arg6Int,
                7 => request.Arg7Kind == (byte)MorrowindScriptValueKind.Float ? request.Arg7Float : request.Arg7Int,
                _ => throw new InvalidOperationException("[VVardenfell][MWScript] MessageBox format argument overflow."),
            };
    }
}
