using System;
using System.Collections.Generic;
using System.Globalization;
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
            var globalLookup = BuildGlobalLookup(data);
            var localLookup = BuildScriptLocalLookup(data);
            var actorLocalLookup = BuildActorLocalLookup(data);
            var scriptLookup = BuildScriptProgramLookup(data);
            var actorLookup = BuildActorLookup(data);
            var explicitRefLookup = BuildExplicitRefLookup(data);
            var spellLookup = BuildSpellLookup(data);
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

                    if (TryValidateSupportedCommand(dialogueLookup, carryableLookup, factionLookup, globalLookup, localLookup, actorLocalLookup, scriptLookup, actorLookup, explicitRefLookup, spellLookup, line, out string reason))
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
            Dictionary<string, GlobalCompileInfo> globals,
            HashSet<string> scriptLocals,
            Dictionary<string, HashSet<string>> actorLocals,
            Dictionary<string, ScriptProgramCompileInfo> scripts,
            Dictionary<string, ActorCompileInfo> actors,
            Dictionary<string, ExplicitRefCompileInfo> explicitRefs,
            Dictionary<string, SpellCompileInfo> spells,
            string line,
            out string reason)
        {
            reason = string.Empty;

            if (TryValidateJournalCommand(dialogues, line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

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

            if (TryValidateTargetedAddTopicCommand(dialogues, line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

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

            if (StartsWithCommand(line, "forcegreeting"))
            {
                string[] tokens = SplitCommandTokens(line);
                if (tokens.Length == 1)
                    return true;

                reason = $"ForceGreeting result takes no arguments in dialogue result V1: '{line}'.";
                return false;
            }

            if (StartsWithCommand(line, "choice"))
            {
                if (!TryParseChoicePairs(line, out _))
                {
                    reason = $"Choice result requires text/value pairs: '{line}'.";
                    return false;
                }

                return true;
            }

            if (StartsWithCommand(line, "showmap"))
            {
                string cellNamePrefix = ExtractCommandArgumentText(line, "showmap").Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(cellNamePrefix))
                    return true;

                reason = $"ShowMap result requires one non-empty cell-name prefix: '{line}'.";
                return false;
            }

            if (TryValidateMessageBoxCommand(line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

            if (TryValidateClearInfoActorCommand(line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

            if (TryValidateRefStateCommand(explicitRefs, line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

            if (TryValidatePositionCellCommand(explicitRefs, line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

            if (TryValidateInventoryCommand(carryables, actors, explicitRefs, line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

            if (TryValidatePlayerSpellCommand(spells, line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

            if (TryValidateDispositionCommand(actors, line, out reason))
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

            if (TryValidatePlayerCrimeCommand(line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

            if (TryValidatePlayerSkillCommand(line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

            if (TryValidatePlayerAttributeCommand(line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

            if (TryValidateFactionReactionCommand(factions, line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

            if (TryValidateStartScriptCommand(scripts, line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

            if (TryValidateSetCommand(globals, scriptLocals, actorLocals, line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

            if (TryValidateActorAiSettingCommand(actors, line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

            if (TryValidateCombatTargetCommand(actors, line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

            if (TryValidateAiWanderCommand(actors, line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

            if (TryValidateAiTravelCommand(actors, line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

            if (TryValidateAiFollowCommand(actors, line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

            if (TryValidateAiFollowCellCommand(actors, line, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
                return false;

            reason = $"Unsupported V1 dialogue result command '{line}'.";
            return false;
        }

        static bool TryValidateStartScriptCommand(
            Dictionary<string, ScriptProgramCompileInfo> scripts,
            string line,
            out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "startscript", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(target))
            {
                reason = $"Explicit StartScript target '{target}' is not supported in dialogue result V1: '{line}'.";
                return false;
            }

            if (tokens.Length != 2)
            {
                reason = $"StartScript result requires one literal script id: '{line}'.";
                return false;
            }

            string scriptId = tokens[1].Trim('"');
            if (!scripts.TryGetValue(ContentId.NormalizeId(scriptId), out var script))
            {
                reason = $"StartScript result references unknown script '{scriptId}'.";
                return false;
            }

            if (script.Status != MorrowindScriptProgramStatus.Compiled)
            {
                reason = string.IsNullOrWhiteSpace(script.DisabledReason)
                    ? $"StartScript result references non-compiled script '{scriptId}'."
                    : $"StartScript result references non-compiled script '{scriptId}': {script.DisabledReason}";
                return false;
            }

            return true;
        }

        static bool TryValidateClearInfoActorCommand(string line, out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "clearinfoactor", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(target))
            {
                reason = $"Explicit ClearInfoActor target '{target}' is not supported in dialogue result V1: '{line}'.";
                return false;
            }

            if (tokens.Length == 1)
                return true;

            reason = $"ClearInfoActor result takes no arguments in dialogue result V1: '{line}'.";
            return false;
        }

        static bool TryValidateRefStateCommand(
            Dictionary<string, ExplicitRefCompileInfo> explicitRefs,
            string line,
            out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            bool enable = string.Equals(command, "enable", StringComparison.OrdinalIgnoreCase);
            bool disable = string.Equals(command, "disable", StringComparison.OrdinalIgnoreCase);
            if (!enable && !disable)
                return false;

            if (tokens.Length != 1)
            {
                reason = $"{command} result takes no arguments in dialogue result V1: '{line}'.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(target) && !KnownExplicitRef(explicitRefs, target))
            {
                reason = $"Explicit {command} target '{target}' is not a unique baked placed ref in dialogue result V1: '{line}'.";
                return false;
            }

            return true;
        }

        static bool TryValidatePositionCellCommand(
            Dictionary<string, ExplicitRefCompileInfo> explicitRefs,
            string line,
            out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "positioncell", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(target) && !KnownExplicitRef(explicitRefs, target))
            {
                reason = $"Explicit PositionCell target '{target}' is not a unique baked placed ref in dialogue result V1: '{line}'.";
                return false;
            }

            if (tokens.Length != 6)
            {
                reason = $"PositionCell result requires x, y, z, z-rotation, and cell id in dialogue result V1: '{line}'.";
                return false;
            }

            if (!float.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                || !float.TryParse(tokens[2], NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                || !float.TryParse(tokens[3], NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                || !float.TryParse(tokens[4], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                reason = $"PositionCell result has invalid coordinates or rotation in dialogue result V1: '{line}'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(tokens[5]))
            {
                reason = $"PositionCell result requires a non-empty cell id in dialogue result V1: '{line}'.";
                return false;
            }

            return true;
        }

        static bool TryValidateFactionReactionCommand(
            Dictionary<string, FactionCompileInfo> factions,
            string line,
            out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            bool mod = string.Equals(command, "modfactionreaction", StringComparison.OrdinalIgnoreCase);
            bool set = string.Equals(command, "setfactionreaction", StringComparison.OrdinalIgnoreCase);
            if (!mod && !set)
                return false;

            if (!string.IsNullOrWhiteSpace(target))
            {
                reason = $"Explicit faction reaction target '{target}' is not supported in dialogue result V1: '{line}'.";
                return false;
            }

            if (tokens.Length != 4)
            {
                reason = $"{command} result requires source faction, target faction, and integer value: '{line}'.";
                return false;
            }

            if (!KnownFaction(factions, tokens[1]))
            {
                reason = $"{command} result references unknown source faction '{tokens[1]}'.";
                return false;
            }

            if (!KnownFaction(factions, tokens[2]))
            {
                reason = $"{command} result references unknown target faction '{tokens[2]}'.";
                return false;
            }

            if (!int.TryParse(tokens[3], out _))
            {
                reason = $"{command} result requires an integer value: '{line}'.";
                return false;
            }

            return true;
        }

        static bool TryValidateActorAiSettingCommand(
            Dictionary<string, ActorCompileInfo> actors,
            string line,
            out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!IsActorAiSettingCommand(command))
                return false;

            if (!string.IsNullOrWhiteSpace(target))
            {
                if (!KnownActor(actors, target))
                {
                    reason = $"Explicit AI setting target '{target}' is not a known actor in dialogue result V1: '{line}'.";
                    return false;
                }
            }

            if (tokens.Length == 2 && int.TryParse(tokens[1], out _))
                return true;

            reason = $"{command} result requires one integer value: '{line}'.";
            return false;
        }

        static bool IsActorAiSettingCommand(string command)
            => string.Equals(command, "sethello", StringComparison.OrdinalIgnoreCase)
               || string.Equals(command, "modhello", StringComparison.OrdinalIgnoreCase)
               || string.Equals(command, "setfight", StringComparison.OrdinalIgnoreCase)
               || string.Equals(command, "modfight", StringComparison.OrdinalIgnoreCase)
               || string.Equals(command, "setflee", StringComparison.OrdinalIgnoreCase)
               || string.Equals(command, "modflee", StringComparison.OrdinalIgnoreCase)
               || string.Equals(command, "setalarm", StringComparison.OrdinalIgnoreCase)
               || string.Equals(command, "modalarm", StringComparison.OrdinalIgnoreCase);

        static bool TryValidateCombatTargetCommand(
            Dictionary<string, ActorCompileInfo> actors,
            string line,
            out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string commandTarget, out string command);
            bool startCombat = string.Equals(command, "startcombat", StringComparison.OrdinalIgnoreCase);
            bool stopCombat = string.Equals(command, "stopcombat", StringComparison.OrdinalIgnoreCase);
            if (!startCombat && !stopCombat)
                return false;

            if (!string.IsNullOrWhiteSpace(commandTarget) && !KnownActor(actors, commandTarget))
            {
                reason = $"Explicit {command} target '{commandTarget}' is not a known actor in dialogue result V1: '{line}'.";
                return false;
            }

            if (startCombat)
            {
                if (tokens.Length != 2)
                {
                    reason = $"StartCombat result requires one target actor id: '{line}'.";
                    return false;
                }

                if (!IsPlayerTarget(tokens[1]) && !KnownActor(actors, tokens[1]))
                {
                    reason = $"StartCombat result target '{tokens[1]}' is not Player or a known actor in dialogue result V1: '{line}'.";
                    return false;
                }

                return true;
            }

            if (tokens.Length == 1 || (tokens.Length == 2 && IsPlayerTarget(tokens[1])))
                return true;

            reason = $"StopCombat result takes no arguments in dialogue result V1: '{line}'.";
            return false;
        }

        static bool TryValidateAiWanderCommand(
            Dictionary<string, ActorCompileInfo> actors,
            string line,
            out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "aiwander", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(target))
            {
                if (!KnownActor(actors, target))
                {
                    reason = $"Explicit AiWander target '{target}' is not a known actor in dialogue result V1: '{line}'.";
                    return false;
                }
            }

            if (tokens.Length < 4)
            {
                reason = $"AiWander result requires distance, duration, and time-of-day: '{line}'.";
                return false;
            }

            for (int i = 1; i < tokens.Length; i++)
            {
                if (!float.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    reason = $"AiWander result requires numeric arguments: '{line}'.";
                    return false;
                }
            }

            return true;
        }

        static bool TryValidateAiTravelCommand(
            Dictionary<string, ActorCompileInfo> actors,
            string line,
            out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "aitravel", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(target))
            {
                if (!KnownActor(actors, target))
                {
                    reason = $"Explicit AiTravel target '{target}' is not a known actor in dialogue result V1: '{line}'.";
                    return false;
                }
            }

            if (tokens.Length < 4)
            {
                reason = $"AiTravel result requires x, y, and z: '{line}'.";
                return false;
            }

            for (int i = 1; i < tokens.Length; i++)
            {
                if (!float.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    reason = $"AiTravel result requires numeric arguments: '{line}'.";
                    return false;
                }
            }

            return true;
        }

        static bool TryValidateAiFollowCommand(
            Dictionary<string, ActorCompileInfo> actors,
            string line,
            out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "aifollow", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(target))
            {
                if (!KnownActor(actors, target))
                {
                    reason = $"Explicit AiFollow target '{target}' is not a known actor in dialogue result V1: '{line}'.";
                    return false;
                }
            }

            if (tokens.Length < 6)
            {
                reason = $"AiFollow result requires target actor, duration, x, y, and z: '{line}'.";
                return false;
            }

            if (!IsPlayerTarget(tokens[1]) && !KnownActor(actors, tokens[1]))
            {
                reason = $"AiFollow result requires Player or a known actor follow target in dialogue result V1: '{line}'.";
                return false;
            }

            for (int i = 2; i < tokens.Length; i++)
            {
                if (!float.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    reason = $"AiFollow result requires numeric duration, coordinates, and reset arguments: '{line}'.";
                    return false;
                }
            }

            return true;
        }

        static bool TryValidateAiFollowCellCommand(
            Dictionary<string, ActorCompileInfo> actors,
            string line,
            out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "aifollowcell", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(target))
            {
                if (!KnownActor(actors, target))
                {
                    reason = $"Explicit AiFollowCell target '{target}' is not a known actor in dialogue result V1: '{line}'.";
                    return false;
                }
            }

            if (tokens.Length < 7)
            {
                reason = $"AiFollowCell result requires target actor, cell id, duration, x, y, and z: '{line}'.";
                return false;
            }

            if (!IsPlayerTarget(tokens[1]) && !KnownActor(actors, tokens[1]))
            {
                reason = $"AiFollowCell result requires Player or a known actor follow target in dialogue result V1: '{line}'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(tokens[2]))
            {
                reason = $"AiFollowCell result requires a non-empty cell id: '{line}'.";
                return false;
            }

            for (int i = 3; i < tokens.Length; i++)
            {
                if (!float.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    reason = $"AiFollowCell result requires numeric duration, coordinates, and reset arguments: '{line}'.";
                    return false;
                }
            }

            return true;
        }

        static bool TryValidateSetCommand(
            Dictionary<string, GlobalCompileInfo> globals,
            HashSet<string> scriptLocals,
            Dictionary<string, HashSet<string>> actorLocals,
            string line,
            out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0 || !string.Equals(tokens[0], "set", StringComparison.OrdinalIgnoreCase))
                return false;

            if (tokens.Length < 4)
            {
                reason = $"Set result supports only 'set <local-or-global> to <literal-number>' or self +/- literal in dialogue result V1: '{line}'.";
                return false;
            }

            string target = tokens[1];
            if (target.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                reason = $"Explicit Set targets are not supported in dialogue result V1: '{line}'.";
                return false;
            }

            bool validExpression = tokens.Length == 4
                && string.Equals(tokens[2], "to", StringComparison.OrdinalIgnoreCase)
                && float.TryParse(tokens[3], NumberStyles.Float, CultureInfo.InvariantCulture, out _);
            if (!validExpression)
            {
                validExpression = TryValidateSetArithmeticExpression(tokens, out reason);
                if (!validExpression && string.IsNullOrEmpty(reason))
                    reason = $"Set result supports only 'set <local-or-global> to <literal-number>' or self +/- literal in dialogue result V1: '{line}'.";
            }

            if (!validExpression)
            {
                if (string.IsNullOrEmpty(reason))
                    reason = $"Set result requires a literal numeric value in dialogue result V1: '{line}'.";
                return false;
            }

            string normalizedTarget = ContentId.NormalizeId(target);
            if (globals.ContainsKey(normalizedTarget) || scriptLocals.Contains(normalizedTarget))
                return true;

            if (TrySplitActorLocalTarget(target, out string actorId, out string localName)
                && actorLocals != null
                && actorLocals.TryGetValue(ContentId.NormalizeId(actorId), out var locals)
                && locals.Contains(ContentId.NormalizeId(localName)))
            {
                return true;
            }

            reason = $"Set result target '{target}' is not a baked global or known script local.";
            return false;
        }

        static bool TryValidateSetArithmeticExpression(string[] tokens, out string reason)
        {
            reason = string.Empty;
            if (tokens.Length != 6
                || !string.Equals(tokens[2], "to", StringComparison.OrdinalIgnoreCase)
                || tokens[1].IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            string target = tokens[1];
            if (!string.Equals(tokens[3], target, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Set result expression must use the same target value in dialogue result V1: '{string.Join(" ", tokens)}'.";
                return false;
            }

            if (!string.Equals(tokens[4], "+", StringComparison.Ordinal)
                && !string.Equals(tokens[4], "-", StringComparison.Ordinal))
            {
                reason = $"Set result expression supports only + or - in dialogue result V1: '{string.Join(" ", tokens)}'.";
                return false;
            }

            if (!float.TryParse(tokens[5], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                reason = $"Set result expression requires a literal numeric operand in dialogue result V1: '{string.Join(" ", tokens)}'.";
                return false;
            }

            return true;
        }

        static bool TryValidateJournalCommand(
            Dictionary<string, DialogueCompileInfo> dialogues,
            string line,
            out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "journal", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(target) && !IsPlayerTarget(target))
            {
                reason = $"Journal result supports only Player explicit target in dialogue result V1: '{line}'.";
                return false;
            }

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

        static bool TryValidateTargetedAddTopicCommand(
            Dictionary<string, DialogueCompileInfo> dialogues,
            string line,
            out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "addtopic", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(target))
                return false;

            if (!IsPlayerTarget(target))
            {
                reason = $"AddTopic result supports only Player explicit target in dialogue result V1: '{line}'.";
                return false;
            }

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

        static bool TryValidatePlayerReputationCommand(string line, out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "modreputation", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(target) && !IsPlayerTarget(target)))
            {
                return false;
            }

            if (tokens.Length == 2 && int.TryParse(tokens[1], out _))
                return true;

            reason = "ModReputation result requires one integer value.";
            return false;
        }

        static bool TryValidatePlayerSkillCommand(string line, out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!IsPlayerSkillModCommand(command)
                || (!string.IsNullOrWhiteSpace(target) && !IsPlayerTarget(target)))
            {
                return false;
            }

            if (tokens.Length == 2 && int.TryParse(tokens[1], out _))
                return true;

            reason = $"{command} result requires one integer value.";
            return false;
        }

        static bool IsPlayerSkillModCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command)
                || command.Length <= 3
                || !command.StartsWith("mod", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string skill = command.Substring(3);
            return string.Equals(skill, "block", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "armorer", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "mediumarmor", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "heavyarmor", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "bluntweapon", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "longblade", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "axe", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "spear", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "athletics", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "enchant", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "destruction", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "alteration", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "illusion", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "conjuration", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "mysticism", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "restoration", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "alchemy", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "unarmored", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "security", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "sneak", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "acrobatics", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "lightarmor", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "shortblade", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "marksman", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "mercantile", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "speechcraft", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(skill, "handtohand", StringComparison.OrdinalIgnoreCase);
        }

        static bool TryValidatePlayerAttributeCommand(string line, out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            if (!string.Equals(command, "modstrength", StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(target) && !IsPlayerTarget(target)))
            {
                return false;
            }

            if (tokens.Length == 2 && int.TryParse(tokens[1], out _))
                return true;

            reason = "ModStrength result requires one integer value.";
            return false;
        }

        static bool TryValidateMessageBoxCommand(string line, out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0 || !string.Equals(tokens[0], "messagebox", StringComparison.OrdinalIgnoreCase))
                return false;

            if (tokens.Length == 2 && !string.IsNullOrWhiteSpace(tokens[1]))
                return true;

            reason = $"MessageBox result supports one literal message string in dialogue result V1: '{line}'.";
            return false;
        }

        static bool TryValidateDispositionCommand(
            Dictionary<string, ActorCompileInfo> actors,
            string line,
            out string reason)
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
                if (!KnownActor(actors, target))
                {
                    reason = $"Explicit disposition target '{target}' is not a known actor in dialogue result V1: '{line}'.";
                    return false;
                }
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
            bool expell = string.Equals(command, "pcexpell", StringComparison.OrdinalIgnoreCase);
            bool clearExpelled = string.Equals(command, "pcclearexpelled", StringComparison.OrdinalIgnoreCase);
            if (!modRep && !raiseRank && !joinFaction && !expell && !clearExpelled)
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
            Dictionary<string, ActorCompileInfo> actors,
            Dictionary<string, ExplicitRefCompileInfo> explicitRefs,
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
                && !IsPlayerTarget(target)
                && !KnownActor(actors, target)
                && !KnownExplicitRef(explicitRefs, target))
            {
                reason = $"Explicit inventory target '{target}' is not a known actor or unique baked placed ref in dialogue result V1: '{line}'.";
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

        static bool TryValidatePlayerSpellCommand(
            Dictionary<string, SpellCompileInfo> spells,
            string line,
            out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            ParseTargetCommand(tokens[0], out string target, out string command);
            bool add = string.Equals(command, "addspell", StringComparison.OrdinalIgnoreCase);
            bool remove = string.Equals(command, "removespell", StringComparison.OrdinalIgnoreCase);
            if (!add && !remove)
                return false;

            if (!string.Equals(target, "player", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"{command} result supports only explicit Player target in dialogue result V1: '{line}'.";
                return false;
            }

            if (tokens.Length != 2)
            {
                reason = $"{command} result requires one spell id: '{line}'.";
                return false;
            }

            string spellId = tokens[1].Trim('"');
            if (!KnownSpell(spells, spellId))
            {
                reason = $"{command} result references unknown spell '{spellId}'.";
                return false;
            }

            return true;
        }

        static bool TryValidatePlayerCrimeCommand(string line, out string reason)
        {
            reason = string.Empty;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            if (!string.Equals(tokens[0], "setpccrimelevel", StringComparison.OrdinalIgnoreCase))
                return false;

            if (tokens.Length != 2 || !int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                reason = $"SetPCCrimeLevel result requires one integer bounty value in dialogue result V1: '{line}'.";
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

        static Dictionary<string, ActorCompileInfo> BuildActorLookup(GameplayContentData data)
        {
            var lookup = new Dictionary<string, ActorCompileInfo>(StringComparer.OrdinalIgnoreCase);
            if (data?.Actors == null)
                return lookup;

            for (int i = 0; i < data.Actors.Length; i++)
            {
                AddActorId(lookup, data.Actors[i].Id);
                AddActorId(lookup, data.Actors[i].OriginalId);
            }

            return lookup;
        }

        static void AddActorId(Dictionary<string, ActorCompileInfo> lookup, string actorId)
        {
            string normalizedId = ContentId.NormalizeId(actorId);
            if (!string.IsNullOrEmpty(normalizedId))
                lookup[normalizedId] = new ActorCompileInfo();
        }

        static Dictionary<string, ExplicitRefCompileInfo> BuildExplicitRefLookup(GameplayContentData data)
        {
            var lookup = new Dictionary<string, ExplicitRefCompileInfo>(StringComparer.OrdinalIgnoreCase);
            if (data?.ExplicitRefTargets == null)
                return lookup;

            for (int i = 0; i < data.ExplicitRefTargets.Length; i++)
            {
                string normalizedId = ContentId.NormalizeId(data.ExplicitRefTargets[i].Id);
                if (!string.IsNullOrEmpty(normalizedId) && data.ExplicitRefTargets[i].PlacedRefId != 0u)
                    lookup[normalizedId] = new ExplicitRefCompileInfo();
            }

            return lookup;
        }

        static Dictionary<string, SpellCompileInfo> BuildSpellLookup(GameplayContentData data)
        {
            var lookup = new Dictionary<string, SpellCompileInfo>(StringComparer.OrdinalIgnoreCase);
            if (data?.Spells == null)
                return lookup;

            for (int i = 0; i < data.Spells.Length; i++)
            {
                string normalizedId = ContentId.NormalizeId(data.Spells[i].Id);
                if (!string.IsNullOrEmpty(normalizedId))
                    lookup[normalizedId] = new SpellCompileInfo();
            }

            return lookup;
        }

        static Dictionary<string, GlobalCompileInfo> BuildGlobalLookup(GameplayContentData data)
        {
            var lookup = new Dictionary<string, GlobalCompileInfo>(StringComparer.OrdinalIgnoreCase);
            if (data?.Globals == null)
                return lookup;

            for (int i = 0; i < data.Globals.Length; i++)
            {
                string normalizedId = ContentId.NormalizeId(data.Globals[i].Id);
                if (!string.IsNullOrEmpty(normalizedId))
                    lookup[normalizedId] = new GlobalCompileInfo();
            }

            return lookup;
        }

        static HashSet<string> BuildScriptLocalLookup(GameplayContentData data)
        {
            var lookup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (data?.MorrowindScriptLocals == null)
                return lookup;

            for (int i = 0; i < data.MorrowindScriptLocals.Length; i++)
            {
                string normalizedId = ContentId.NormalizeId(data.MorrowindScriptLocals[i].Name);
                if (!string.IsNullOrEmpty(normalizedId))
                    lookup.Add(normalizedId);
            }

            return lookup;
        }

        static Dictionary<string, HashSet<string>> BuildActorLocalLookup(GameplayContentData data)
        {
            var lookup = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            if (data?.Actors == null || data.MorrowindScriptPrograms == null || data.MorrowindScriptLocals == null)
                return lookup;

            var scriptLocalsById = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < data.MorrowindScriptPrograms.Length; i++)
            {
                ref readonly var program = ref data.MorrowindScriptPrograms[i];
                string scriptId = ContentId.NormalizeId(program.Id);
                if (string.IsNullOrEmpty(scriptId) || program.LocalCount <= 0 || program.FirstLocalIndex < 0)
                    continue;

                var locals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int end = Math.Min(data.MorrowindScriptLocals.Length, program.FirstLocalIndex + program.LocalCount);
                for (int local = program.FirstLocalIndex; local < end; local++)
                {
                    string localId = ContentId.NormalizeId(data.MorrowindScriptLocals[local].Name);
                    if (!string.IsNullOrEmpty(localId))
                        locals.Add(localId);
                }

                if (locals.Count > 0)
                    scriptLocalsById[scriptId] = locals;
            }

            for (int i = 0; i < data.Actors.Length; i++)
            {
                ref readonly var actor = ref data.Actors[i];
                string scriptId = ContentId.NormalizeId(actor.ScriptId);
                if (string.IsNullOrEmpty(scriptId) || !scriptLocalsById.TryGetValue(scriptId, out var locals))
                    continue;

                AddActorLocalId(lookup, actor.Id, locals);
                AddActorLocalId(lookup, actor.OriginalId, locals);
            }

            return lookup;
        }

        static void AddActorLocalId(Dictionary<string, HashSet<string>> lookup, string actorId, HashSet<string> locals)
        {
            string normalizedActorId = ContentId.NormalizeId(actorId);
            if (!string.IsNullOrEmpty(normalizedActorId))
                lookup[normalizedActorId] = locals;
        }

        static Dictionary<string, ScriptProgramCompileInfo> BuildScriptProgramLookup(GameplayContentData data)
        {
            var lookup = new Dictionary<string, ScriptProgramCompileInfo>(StringComparer.OrdinalIgnoreCase);
            if (data?.MorrowindScriptPrograms == null)
                return lookup;

            for (int i = 0; i < data.MorrowindScriptPrograms.Length; i++)
            {
                string normalizedId = ContentId.NormalizeId(data.MorrowindScriptPrograms[i].Id);
                if (string.IsNullOrEmpty(normalizedId))
                    continue;

                lookup[normalizedId] = new ScriptProgramCompileInfo
                {
                    Status = (MorrowindScriptProgramStatus)data.MorrowindScriptPrograms[i].Status,
                    DisabledReason = data.MorrowindScriptPrograms[i].DisabledReason,
                };
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

        static string ExtractCommandArgumentText(string line, string command)
        {
            if (line == null || line.Length <= command.Length)
                return string.Empty;

            return line.Substring(command.Length).Trim().TrimStart(',');
        }

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

        static bool KnownActor(Dictionary<string, ActorCompileInfo> actors, string actorId)
            => actors != null && actors.ContainsKey(ContentId.NormalizeId(actorId));

        static bool KnownExplicitRef(Dictionary<string, ExplicitRefCompileInfo> explicitRefs, string id)
            => explicitRefs != null && explicitRefs.ContainsKey(ContentId.NormalizeId(id));

        static bool KnownSpell(Dictionary<string, SpellCompileInfo> spells, string spellId)
            => spells != null && spells.ContainsKey(ContentId.NormalizeId(spellId));

        static bool IsPlayerTarget(string target)
            => string.Equals((target ?? string.Empty).Trim().Trim('"'), "player", StringComparison.OrdinalIgnoreCase);

        static bool TrySplitActorLocalTarget(string target, out string actorId, out string localName)
        {
            actorId = string.Empty;
            localName = string.Empty;
            if (string.IsNullOrWhiteSpace(target))
                return false;

            int dot = target.LastIndexOf('.');
            if (dot <= 0 || dot >= target.Length - 1)
                return false;

            actorId = target.Substring(0, dot).Trim().Trim('"');
            localName = target.Substring(dot + 1).Trim().Trim('"');
            return actorId.Length > 0 && localName.Length > 0;
        }

        static bool TryParseChoicePairs(string line, out List<ChoicePair> choices)
        {
            choices = new List<ChoicePair>();
            string text = ExtractCommandArgumentText(line, "choice");
            int index = 0;
            while (true)
            {
                SkipChoiceSeparators(text, ref index);
                if (index >= text.Length)
                    break;

                string choiceText;
                if (text[index] == '"')
                {
                    index++;
                    int textStart = index;
                    while (index < text.Length && text[index] != '"')
                        index++;

                    if (index >= text.Length)
                        return false;

                    choiceText = text.Substring(textStart, index - textStart);
                    index++;
                }
                else
                {
                    int textStart = index;
                    while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] != ',')
                        index++;

                    if (textStart == index)
                        return false;

                    choiceText = text.Substring(textStart, index - textStart);
                }

                SkipChoiceSeparators(text, ref index);

                int valueStart = index;
                if (index < text.Length && (text[index] == '+' || text[index] == '-'))
                    index++;
                while (index < text.Length && char.IsDigit(text[index]))
                    index++;

                if (valueStart == index
                    || !int.TryParse(text.Substring(valueStart, index - valueStart), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                {
                    return false;
                }

                choices.Add(new ChoicePair
                {
                    Text = choiceText,
                    Value = value,
                });
            }

            return choices.Count > 0;
        }

        static void SkipChoiceSeparators(string text, ref int index)
        {
            while (index < text.Length && (char.IsWhiteSpace(text[index]) || text[index] == ','))
                index++;
        }

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

        struct ActorCompileInfo
        {
        }

        struct ExplicitRefCompileInfo
        {
        }

        struct SpellCompileInfo
        {
        }

        struct GlobalCompileInfo
        {
        }

        struct ScriptProgramCompileInfo
        {
            public MorrowindScriptProgramStatus Status;
            public string DisabledReason;
        }

        struct ChoicePair
        {
            public string Text;
            public int Value;
        }
    }
}
