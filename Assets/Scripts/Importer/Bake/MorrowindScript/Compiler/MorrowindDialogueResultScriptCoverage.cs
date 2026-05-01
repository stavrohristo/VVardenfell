using System;
using System.Collections.Generic;
using System.Text;
using VVardenfell.Core.Cache;

namespace VVardenfell.Importer.Bake
{
    internal static class MorrowindDialogueResultScriptCoverage
    {
        public static void Log(GameplayContentData data)
        {
            DialogueDef[] dialogues = data?.Dialogues;
            DialogueInfoDef[] infos = data?.DialogueInfos;
            if (infos == null || infos.Length == 0)
                return;

            var dialogueLookup = BuildDialogueLookup(dialogues);
            var carryableLookup = BuildCarryableLookup(data);
            var factionLookup = BuildFactionLookup(data);
            var infoOwners = BuildInfoOwners(dialogues, infos.Length);
            int scriptsWithResults = 0;
            int supportedScripts = 0;
            int unsupportedScripts = 0;
            int supportedLines = 0;
            int unsupportedLines = 0;
            var unsupportedRoots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var unsupportedDetails = new List<string>();

            for (int infoIndex = 0; infoIndex < infos.Length; infoIndex++)
            {
                string script = infos[infoIndex].ResultScript;
                if (string.IsNullOrWhiteSpace(script))
                    continue;

                scriptsWithResults++;
                bool scriptSupported = true;
                string[] lines = script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    string line = StripComment(lines[lineIndex]).Trim();
                    if (line.Length == 0)
                        continue;

                    if (TryValidateSupportedCommand(dialogueLookup, carryableLookup, factionLookup, line, out string reason))
                    {
                        supportedLines++;
                        continue;
                    }

                    scriptSupported = false;
                    unsupportedLines++;
                    string root = ExtractRoot(line);
                    unsupportedRoots[root] = unsupportedRoots.TryGetValue(root, out int count) ? count + 1 : 1;
                    unsupportedDetails.Add($"{DescribeInfo(dialogues, infos, infoOwners, infoIndex)}: line {lineIndex + 1}: {reason}");
                }

                if (scriptSupported)
                    supportedScripts++;
                else
                    unsupportedScripts++;
            }

            if (scriptsWithResults == 0)
                return;

            var summary = new StringBuilder(16 * 1024);
            summary.Append("[VVardenfell][Dialogue][ResultCoverage] infosWithResults=").Append(scriptsWithResults)
                .Append(" supported=").Append(supportedScripts)
                .Append(" unsupported=").Append(unsupportedScripts)
                .Append(" supportedLines=").Append(supportedLines)
                .Append(" unsupportedLines=").Append(unsupportedLines);

            if (unsupportedRoots.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine("Unsupported roots:");
                foreach (var pair in SortUnsupportedRoots(unsupportedRoots))
                    summary.Append("  ").Append(pair.Key).Append(": ").Append(pair.Value).AppendLine();
            }

            if (unsupportedDetails.Count > 0)
            {
                summary.AppendLine("Unsupported dialogue result scripts:");
                for (int i = 0; i < unsupportedDetails.Count; i++)
                    summary.Append("  ").Append(unsupportedDetails[i]).AppendLine();
            }

            UnityEngine.Debug.Log(summary.ToString());
        }

        static bool TryValidateSupportedCommand(
            Dictionary<string, DialogueCompileInfo> dialogues,
            Dictionary<string, CarryableCompileInfo> carryables,
            Dictionary<string, FactionCompileInfo> factions,
            string line,
            out string reason)
        {
            reason = string.Empty;

            if (StartsWithCommand(line, "journal"))
            {
                string[] tokens = SplitCommandTokens(line);
                if (tokens.Length != 3)
                {
                    reason = $"Journal result requires literal quest id and integer stage: '{line}'.";
                    return false;
                }

                if (!int.TryParse(tokens[2], out _))
                {
                    reason = $"Journal result requires an integer stage: '{line}'.";
                    return false;
                }

                string id = tokens[1].Trim('"');
                if (!dialogues.TryGetValue(ContentId.NormalizeId(id), out var dialogue))
                {
                    reason = $"Journal result references unknown quest '{id}'.";
                    return false;
                }

                if (dialogue.Type != DialogueDefType.Journal)
                {
                    reason = $"Journal result references non-journal dialogue '{id}'.";
                    return false;
                }

                return true;
            }

            if (StartsWithCommand(line, "setjournalindex"))
            {
                string[] tokens = SplitCommandTokens(line);
                if (tokens.Length != 3)
                {
                    reason = $"SetJournalIndex result requires literal quest id and integer stage: '{line}'.";
                    return false;
                }

                if (!int.TryParse(tokens[2], out _))
                {
                    reason = $"SetJournalIndex result requires an integer stage: '{line}'.";
                    return false;
                }

                string id = tokens[1].Trim('"');
                if (!dialogues.TryGetValue(ContentId.NormalizeId(id), out var dialogue))
                {
                    reason = $"SetJournalIndex result references unknown quest '{id}'.";
                    return false;
                }

                if (dialogue.Type != DialogueDefType.Journal)
                {
                    reason = $"SetJournalIndex result references non-journal dialogue '{id}'.";
                    return false;
                }

                return true;
            }

            if (StartsWithCommand(line, "addtopic"))
            {
                string[] tokens = SplitCommandTokens(line);
                if (tokens.Length != 2)
                {
                    reason = $"AddTopic result requires one literal topic id: '{line}'.";
                    return false;
                }

                string id = tokens[1].Trim('"');
                if (!dialogues.TryGetValue(ContentId.NormalizeId(id), out var dialogue))
                {
                    reason = $"AddTopic result references unknown topic '{id}'.";
                    return false;
                }

                if (dialogue.Type != DialogueDefType.Topic)
                {
                    reason = $"AddTopic result references non-topic dialogue '{id}'.";
                    return false;
                }

                return true;
            }

            if (StartsWithCommand(line, "filljournal"))
            {
                string[] tokens = SplitCommandTokens(line);
                if (tokens.Length == 1)
                    return true;

                reason = $"FillJournal result takes no arguments in V1: '{line}'.";
                return false;
            }

            if (StartsWithCommand(line, "goodbye"))
            {
                string[] tokens = SplitCommandTokens(line);
                if (tokens.Length == 1)
                    return true;

                reason = $"Goodbye result takes no arguments: '{line}'.";
                return false;
            }

            if (StartsWithCommand(line, "choice"))
            {
                string[] tokens = SplitCommandTokens(line);
                if (tokens.Length < 3 || tokens.Length % 2 == 0)
                {
                    reason = $"Choice result requires text/value pairs: '{line}'.";
                    return false;
                }

                for (int i = 2; i < tokens.Length; i += 2)
                {
                    if (!int.TryParse(tokens[i], out _))
                    {
                        reason = $"Choice result requires integer choice values: '{line}'.";
                        return false;
                    }
                }

                return true;
            }

            if (StartsWithCommand(line, "showmap"))
            {
                string[] tokens = SplitCommandTokens(line);
                if (tokens.Length == 2 && !string.IsNullOrWhiteSpace(tokens[1]))
                    return true;

                reason = $"ShowMap result requires one non-empty cell-name prefix: '{line}'.";
                return false;
            }

            if (TryValidateInventoryCommand(carryables, line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

            if (TryValidateDispositionCommand(line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

            if (TryValidatePlayerFactionCommand(factions, line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

            if (TryValidatePlayerReputationCommand(line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

            reason = $"Unsupported V1 dialogue result command '{line}'.";
            return false;
        }

        static bool TryValidatePlayerReputationCommand(string line, out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(target, "player", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(command, "modreputation", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (tokens.Length == 2 && int.TryParse(tokens[1], out _))
                return true;

            reason = $"Player->ModReputation result requires one integer value: '{line}'.";
            return false;
        }

        static bool TryValidateDispositionCommand(string line, out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            bool mod = string.Equals(command, "moddisposition", StringComparison.OrdinalIgnoreCase);
            bool set = string.Equals(command, "setdisposition", StringComparison.OrdinalIgnoreCase);
            if (!mod && !set)
                return false;

            if (!string.IsNullOrWhiteSpace(target))
            {
                reason = $"Explicit disposition target '{target}' is not supported in dialogue result V1: '{line}'.";
                return false;
            }

            if (tokens.Length == 2 && int.TryParse(tokens[1], out _))
                return true;

            reason = $"{command} result requires one integer value: '{line}'.";
            return false;
        }

        static bool TryValidatePlayerFactionCommand(
            Dictionary<string, FactionCompileInfo> factions,
            string line,
            out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.IsNullOrWhiteSpace(target))
                return false;

            bool modRep = string.Equals(command, "modpcfacrep", StringComparison.OrdinalIgnoreCase);
            bool raiseRank = string.Equals(command, "pcraiserank", StringComparison.OrdinalIgnoreCase);
            bool joinFaction = string.Equals(command, "pcjoinfaction", StringComparison.OrdinalIgnoreCase);
            if (!modRep && !raiseRank && !joinFaction)
                return false;

            if (modRep)
            {
                if (tokens.Length != 2 && tokens.Length != 3)
                {
                    reason = $"ModPCFacRep result requires value and optional faction id: '{line}'.";
                    return false;
                }

                if (!int.TryParse(tokens[1], out _))
                {
                    reason = $"ModPCFacRep result requires integer value: '{line}'.";
                    return false;
                }

                if (tokens.Length == 3 && !KnownFaction(factions, tokens[2]))
                {
                    reason = $"ModPCFacRep result references unknown faction '{tokens[2]}'.";
                    return false;
                }

                return true;
            }

            if (tokens.Length > 2)
            {
                reason = $"{command} result takes at most one faction id: '{line}'.";
                return false;
            }

            if (tokens.Length == 2 && !KnownFaction(factions, tokens[1]))
            {
                reason = $"{command} result references unknown faction '{tokens[1]}'.";
                return false;
            }

            return true;
        }

        static bool TryValidateInventoryCommand(
            Dictionary<string, CarryableCompileInfo> carryables,
            string line,
            out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            NormalizeSeparatedExplicitCommand(tokens, out tokens);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            bool add = string.Equals(command, "additem", StringComparison.OrdinalIgnoreCase);
            bool remove = string.Equals(command, "removeitem", StringComparison.OrdinalIgnoreCase);
            if (!add && !remove)
                return false;

            if (!string.IsNullOrWhiteSpace(target)
                && !string.Equals(target, "player", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Explicit inventory target '{target}' is not supported in dialogue result V1: '{line}'.";
                return false;
            }

            if (tokens.Length != 3)
            {
                reason = $"{command} result requires item id and integer count: '{line}'.";
                return false;
            }

            if (!int.TryParse(tokens[2], out _))
            {
                reason = $"{command} result requires integer count: '{line}'.";
                return false;
            }

            string id = NormalizeGoldId(tokens[1]);
            if (!carryables.TryGetValue(ContentId.NormalizeId(id), out var carryable))
            {
                reason = $"{command} result references unknown carryable item '{tokens[1]}'.";
                return false;
            }

            if (remove && carryable.Kind == ContentReferenceKind.LeveledItem)
            {
                reason = "RemoveItem result does not support item leveled-list ids in dialogue result V1.";
                return false;
            }

            return true;
        }

        static Dictionary<string, DialogueCompileInfo> BuildDialogueLookup(DialogueDef[] dialogues)
        {
            var lookup = new Dictionary<string, DialogueCompileInfo>(StringComparer.OrdinalIgnoreCase);
            if (dialogues == null)
                return lookup;

            for (int i = 0; i < dialogues.Length; i++)
            {
                string id = ContentId.NormalizeId(dialogues[i].Id);
                if (string.IsNullOrEmpty(id))
                    continue;

                lookup[id] = new DialogueCompileInfo
                {
                    Type = dialogues[i].Type,
                };
            }

            return lookup;
        }

        static Dictionary<string, FactionCompileInfo> BuildFactionLookup(GameplayContentData data)
        {
            var lookup = new Dictionary<string, FactionCompileInfo>(StringComparer.OrdinalIgnoreCase);
            if (data?.Factions == null)
                return lookup;

            for (int i = 0; i < data.Factions.Length; i++)
            {
                string normalizedId = ContentId.NormalizeId(data.Factions[i].Id);
                if (!string.IsNullOrEmpty(normalizedId))
                    lookup[normalizedId] = new FactionCompileInfo();
            }

            return lookup;
        }

        static Dictionary<string, CarryableCompileInfo> BuildCarryableLookup(GameplayContentData data)
        {
            var lookup = new Dictionary<string, CarryableCompileInfo>(StringComparer.OrdinalIgnoreCase);
            if (data == null)
                return lookup;

            for (int i = 0; i < data.Items.Length; i++)
                AddCarryable(lookup, data.Items[i].Id, ContentReferenceKind.Item);
            for (int i = 0; i < data.Lights.Length; i++)
                AddCarryable(lookup, data.Lights[i].Id, ContentReferenceKind.Light);
            for (int i = 0; i < data.ItemLeveledLists.Length; i++)
                AddCarryable(lookup, data.ItemLeveledLists[i].Id, ContentReferenceKind.LeveledItem);
            return lookup;
        }

        static void AddCarryable(Dictionary<string, CarryableCompileInfo> lookup, string id, ContentReferenceKind kind)
        {
            string normalizedId = ContentId.NormalizeId(id);
            if (string.IsNullOrEmpty(normalizedId))
                return;

            lookup[normalizedId] = new CarryableCompileInfo
            {
                Kind = kind,
            };
        }

        static int[] BuildInfoOwners(DialogueDef[] dialogues, int infoCount)
        {
            var owners = new int[infoCount];
            for (int i = 0; i < owners.Length; i++)
                owners[i] = -1;

            if (dialogues == null)
                return owners;

            for (int dialogueIndex = 0; dialogueIndex < dialogues.Length; dialogueIndex++)
            {
                int start = dialogues[dialogueIndex].FirstInfoIndex;
                int end = Math.Min(infoCount, start + dialogues[dialogueIndex].InfoCount);
                for (int infoIndex = Math.Max(0, start); infoIndex < end; infoIndex++)
                    owners[infoIndex] = dialogueIndex;
            }

            return owners;
        }

        static string DescribeInfo(DialogueDef[] dialogues, DialogueInfoDef[] infos, int[] infoOwners, int infoIndex)
        {
            string dialogueId = "unknown-dialogue";
            if (infoOwners != null
                && (uint)infoIndex < (uint)infoOwners.Length
                && infoOwners[infoIndex] >= 0
                && dialogues != null
                && (uint)infoOwners[infoIndex] < (uint)dialogues.Length)
            {
                dialogueId = dialogues[infoOwners[infoIndex]].Id;
            }
            else if (infos != null && (uint)infoIndex < (uint)infos.Length && !string.IsNullOrWhiteSpace(infos[infoIndex].TopicId))
            {
                dialogueId = infos[infoIndex].TopicId;
            }

            string infoId = infos != null && (uint)infoIndex < (uint)infos.Length && !string.IsNullOrWhiteSpace(infos[infoIndex].Id)
                ? infos[infoIndex].Id
                : $"info:{infoIndex}";
            return $"{dialogueId}/{infoId}";
        }

        static string StripComment(string line)
        {
            int semicolon = line.IndexOf(';');
            return semicolon >= 0 ? line.Substring(0, semicolon) : line;
        }

        static bool StartsWithCommand(string line, string command)
            => line.Length >= command.Length
               && string.Compare(line, 0, command, 0, command.Length, StringComparison.OrdinalIgnoreCase) == 0
               && (line.Length == command.Length || char.IsWhiteSpace(line[command.Length]) || line[command.Length] == ',');

        static void ParseTargetCommand(string token, out string target, out string command)
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

        static void NormalizeSeparatedExplicitCommand(string[] tokens, out string[] normalized)
        {
            normalized = tokens;
            if (tokens == null || tokens.Length < 2 || !tokens[0].EndsWith("->", StringComparison.Ordinal))
                return;

            normalized = new string[tokens.Length - 1];
            normalized[0] = tokens[0] + tokens[1];
            for (int i = 2; i < tokens.Length; i++)
                normalized[i - 1] = tokens[i];
        }

        static string NormalizeGoldId(string itemId)
        {
            if (string.Equals(itemId, "gold_005", StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemId, "gold_010", StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemId, "gold_025", StringComparison.OrdinalIgnoreCase)
                || string.Equals(itemId, "gold_100", StringComparison.OrdinalIgnoreCase))
            {
                return "gold_001";
            }

            return itemId;
        }

        static bool KnownFaction(Dictionary<string, FactionCompileInfo> factions, string factionId)
            => factions != null && factions.ContainsKey(ContentId.NormalizeId(factionId));

        static string[] SplitCommandTokens(string line)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"')
                {
                    quoted = !quoted;
                    continue;
                }

                if (!quoted && (char.IsWhiteSpace(ch) || ch == ','))
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Length = 0;
                    }
                    continue;
                }

                current.Append(ch);
            }

            if (current.Length > 0)
                tokens.Add(current.ToString());
            return tokens.ToArray();
        }

        static string ExtractRoot(string line)
        {
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
                return "explicit-reference";

            int end = 0;
            while (end < line.Length && !char.IsWhiteSpace(line[end]) && line[end] != ',' && line[end] != '(')
                end++;

            return end <= 0 ? "unknown" : line.Substring(0, end).Trim('"');
        }

        static List<KeyValuePair<string, int>> SortUnsupportedRoots(Dictionary<string, int> roots)
        {
            var sorted = new List<KeyValuePair<string, int>>(roots);
            sorted.Sort((a, b) =>
            {
                int countComparison = b.Value.CompareTo(a.Value);
                return countComparison != 0 ? countComparison : string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
            });
            return sorted;
        }

        struct DialogueCompileInfo
        {
            public DialogueDefType Type;
        }

        struct CarryableCompileInfo
        {
            public ContentReferenceKind Kind;
        }

        struct FactionCompileInfo
        {
        }
    }
}
