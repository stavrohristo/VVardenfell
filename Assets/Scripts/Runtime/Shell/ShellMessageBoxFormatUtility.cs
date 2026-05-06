using System;
using Unity.Collections;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Shell
{
    static class ShellMessageBoxFormatUtility
    {
        public static FixedString512Bytes Format(in ShellMessageBoxRequest request)
        {
            if (request.ArgCount == 0 || request.Body.IsEmpty)
                return request.Body;

            var formatted = default(FixedString512Bytes);
            int argIndex = 0;
            for (int i = 0; i < request.Body.Length; i++)
            {
                byte c = request.Body[i];
                if (c != (byte)'%')
                {
                    formatted.Append((char)c);
                    continue;
                }

                if (++i >= request.Body.Length)
                    break;

                if (request.Body[i] == (byte)'%')
                {
                    formatted.Append('%');
                    continue;
                }

                int flags = 0;
                while (i < request.Body.Length)
                {
                    byte flag = request.Body[i];
                    if (flag == (byte)'-')
                        flags |= 4;
                    else if (flag == (byte)'+')
                        flags |= 2;
                    else if (flag == (byte)' ')
                        flags |= 1;
                    else if (flag == (byte)'0')
                        flags |= 8;
                    else if (flag == (byte)'#')
                        flags |= 16;
                    else
                        break;
                    i++;
                }

                int width = ParseNumber(request.Body, ref i, -1);
                int precision = -1;
                if (i < request.Body.Length && request.Body[i] == (byte)'.')
                {
                    i++;
                    precision = ParseNumber(request.Body, ref i, 0);
                }

                if (i >= request.Body.Length)
                    break;

                byte placeholder = request.Body[i];
                if (!IsSupportedPlaceholder(placeholder))
                {
                    formatted.Append((char)placeholder);
                    continue;
                }

                if (argIndex >= request.ArgCount)
                    throw new InvalidOperationException("[VVardenfell][MWScript] MessageBox format argument underflow.");

                var argument = FormatArgument(request, argIndex++, placeholder, flags, precision);
                AppendPadded(ref formatted, argument, flags, width);
            }

            return formatted;
        }

        static FixedString128Bytes FormatArgument(in ShellMessageBoxRequest request, int argIndex, byte placeholder, int flags, int precision)
        {
            bool floatPlaceholder = placeholder is (byte)'f' or (byte)'F' or (byte)'e' or (byte)'E' or (byte)'g' or (byte)'G';
            var text = default(FixedString128Bytes);
            if (floatPlaceholder)
                AppendFloat(ref text, GetFloat(request, argIndex), precision >= 0 ? precision : 6, (flags & 16) != 0);
            else
                AppendInteger(ref text, GetInt(request, argIndex), precision);

            if (!StartsWithSign(text))
            {
                if ((flags & 2) != 0)
                    Prepend(ref text, '+');
                else if ((flags & 1) != 0)
                    Prepend(ref text, ' ');
            }

            return text;
        }

        static void AppendPadded(ref FixedString512Bytes output, FixedString128Bytes text, int flags, int width)
        {
            int padCount = math.max(0, width - text.Length);
            bool left = (flags & 4) != 0;
            bool zero = (flags & 8) != 0 && !left;

            if (padCount == 0 || left)
            {
                output.Append(text);
                AppendRepeated(ref output, ' ', padCount);
                return;
            }

            if (zero && StartsWithSign(text))
            {
                output.Append((char)text[0]);
                AppendRepeated(ref output, '0', padCount);
                for (int i = 1; i < text.Length; i++)
                    output.Append((char)text[i]);
                return;
            }

            AppendRepeated(ref output, zero ? '0' : ' ', padCount);
            output.Append(text);
        }

        static void AppendInteger(ref FixedString128Bytes output, int value, int precision)
        {
            if (value < 0)
                output.Append('-');

            uint magnitude = value == int.MinValue ? 2147483648u : (uint)math.abs(value);
            var digits = default(FixedString64Bytes);
            AppendUnsigned(ref digits, magnitude);

            int zeroCount = precision > digits.Length ? precision - digits.Length : 0;
            for (int i = 0; i < zeroCount; i++)
                output.Append('0');
            output.Append(digits);
        }

        static void AppendFloat(ref FixedString128Bytes output, float value, int precision, bool forceDecimal)
        {
            precision = math.clamp(precision, 0, 6);
            if (value < 0f)
            {
                output.Append('-');
                value = -value;
            }

            int scale = Pow10(precision);
            int scaled = (int)math.round(value * scale);
            int whole = scale == 0 ? scaled : scaled / scale;
            int fraction = scale == 0 ? 0 : scaled - whole * scale;
            output.Append(whole);

            if (precision <= 0)
            {
                if (forceDecimal)
                    output.Append('.');
                return;
            }

            output.Append('.');
            int divisor = scale / 10;
            while (divisor > 0)
            {
                output.Append((char)('0' + fraction / divisor % 10));
                divisor /= 10;
            }
        }

        static void AppendUnsigned(ref FixedString64Bytes output, uint value)
        {
            if (value == 0)
            {
                output.Append('0');
                return;
            }

            var reversed = default(FixedString64Bytes);
            while (value > 0)
            {
                reversed.Append((char)('0' + value % 10));
                value /= 10;
            }

            for (int i = reversed.Length - 1; i >= 0; i--)
                output.Append((char)reversed[i]);
        }

        static int ParseNumber(FixedString512Bytes message, ref int index, int fallback)
        {
            int value = 0;
            int start = index;
            while (index < message.Length && message[index] >= (byte)'0' && message[index] <= (byte)'9')
            {
                value = value * 10 + message[index] - (byte)'0';
                index++;
            }

            return index > start ? value : fallback;
        }

        static bool IsSupportedPlaceholder(byte c)
            => c is (byte)'d' or (byte)'i' or (byte)'f' or (byte)'F' or (byte)'e' or (byte)'E' or (byte)'g' or (byte)'G';

        static bool StartsWithSign(FixedString128Bytes text)
            => text.Length > 0 && (text[0] == (byte)'+' || text[0] == (byte)'-' || text[0] == (byte)' ');

        static void Prepend(ref FixedString128Bytes text, char c)
        {
            var result = default(FixedString128Bytes);
            result.Append(c);
            result.Append(text);
            text = result;
        }

        static void AppendRepeated(ref FixedString512Bytes output, char c, int count)
        {
            for (int i = 0; i < count; i++)
                output.Append(c);
        }

        static int Pow10(int value)
        {
            int result = 1;
            for (int i = 0; i < value; i++)
                result *= 10;
            return result;
        }

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
