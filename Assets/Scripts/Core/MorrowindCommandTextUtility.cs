using System;
using System.Collections.Generic;

namespace VVardenfell.Core
{
    public static class MorrowindCommandTextUtility
    {
        public static bool StartsWithCommand(string line, string command)
        {
            if (line == null || command == null || !line.StartsWith(command, StringComparison.OrdinalIgnoreCase))
                return false;
            return line.Length == command.Length || char.IsWhiteSpace(line[command.Length]) || line[command.Length] == ',';
        }

        public static string ExtractCommandArgumentText(string line, string command)
        {
            if (line == null || command == null || line.Length <= command.Length)
                return string.Empty;

            return line.Substring(command.Length).Trim().TrimStart(',');
        }

        public static void ParseTargetCommand(string token, out string target, out string command)
        {
            int arrow = token.IndexOf("->", StringComparison.Ordinal);
            if (arrow < 0)
            {
                target = string.Empty;
                command = token;
                return;
            }

            target = token.Substring(0, arrow).Trim().Trim('"');
            command = token.Substring(arrow + 2).Trim();
        }

        public static string[] SplitCommandTokens(string line)
        {
            var tokens = new List<string>();
            int i = 0;
            while (line != null && i < line.Length)
            {
                while (i < line.Length && (char.IsWhiteSpace(line[i]) || line[i] == ','))
                    i++;
                if (i >= line.Length)
                    break;

                if (line[i] == '"')
                {
                    int start = ++i;
                    while (i < line.Length && line[i] != '"')
                        i++;
                    tokens.Add(line.Substring(start, i - start));
                    if (i < line.Length)
                        i++;
                    continue;
                }

                int tokenStart = i;
                while (i < line.Length && !char.IsWhiteSpace(line[i]) && line[i] != ',')
                    i++;
                string token = NormalizeToken(line.Substring(tokenStart, i - tokenStart));
                if (!string.IsNullOrWhiteSpace(token))
                    tokens.Add(token);
            }

            return tokens.ToArray();
        }

        public static string StripExplicitReferencePrefix(string line)
        {
            int arrow = line.IndexOf("->", StringComparison.Ordinal);
            return arrow < 0 ? line : line.Substring(arrow + 2).TrimStart();
        }

        public static bool TrySplitExplicitReference(string line, out string targetId, out string commandOrExpression)
        {
            targetId = null;
            commandOrExpression = null;
            int arrow = line.IndexOf("->", StringComparison.Ordinal);
            if (arrow < 0)
                return false;

            targetId = NormalizeExplicitRefId(line.Substring(0, arrow));
            commandOrExpression = line.Substring(arrow + 2).TrimStart();
            return targetId.Length > 0 && commandOrExpression.Length > 0;
        }

        public static string NormalizeExplicitRefId(string targetId)
        {
            targetId = NormalizeToken(targetId);
            if (targetId.Length >= 2 && targetId[0] == '"' && targetId[targetId.Length - 1] == '"')
                targetId = targetId.Substring(1, targetId.Length - 2);
            return targetId.Trim();
        }

        public static string NormalizeToken(string token)
        {
            return (token ?? string.Empty).Trim().Trim(',');
        }

        public static bool IsPlayerTarget(string target)
            => string.Equals(NormalizeToken(target).Trim('"'), "player", StringComparison.OrdinalIgnoreCase);

        public static bool IsOkButton(string value)
            => string.Equals(NormalizeToken(value).Trim('"'), "ok", StringComparison.OrdinalIgnoreCase);
    }
}
