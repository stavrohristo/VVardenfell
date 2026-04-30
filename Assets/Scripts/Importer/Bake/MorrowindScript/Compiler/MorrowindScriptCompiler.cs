using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using VVardenfell.Core.Cache;

namespace VVardenfell.Importer.Bake
{
    internal static class MorrowindScriptCompiler
    {
        public static void Build(
            GenericRecordDef[] scripts,
            SoundDef[] sounds,
            GenericRecordDef[] globals,
            DialogueDef[] dialogues,
            out MorrowindScriptProgramDef[] programs,
            out MorrowindScriptInstructionDef[] instructions,
            out MorrowindScriptLocalDef[] locals)
        {
            scripts ??= Array.Empty<GenericRecordDef>();
            var soundLookup = BuildSoundLookup(sounds);
            var globalLookup = BuildGlobalLookup(globals);
            var dialogueLookup = BuildDialogueLookup(dialogues);
            var programList = new List<MorrowindScriptProgramDef>(scripts.Length);
            var instructionList = new List<MorrowindScriptInstructionDef>(scripts.Length * 4);
            var localList = new List<MorrowindScriptLocalDef>();

            for (int i = 0; i < scripts.Length; i++)
            {
                CompileScript(
                    scripts[i],
                    i,
                    soundLookup,
                    globalLookup,
                    dialogueLookup,
                    programList,
                    instructionList,
                    localList);
            }

            programs = programList.ToArray();
            instructions = instructionList.ToArray();
            locals = localList.ToArray();
            LogCompileCoverage(programs);
        }

        static void CompileScript(
            in GenericRecordDef script,
            int scriptIndex,
            Dictionary<string, SoundDefHandle> sounds,
            Dictionary<string, (int Index, byte Kind)> globals,
            Dictionary<string, (int Index, DialogueDefType Type)> dialogues,
            List<MorrowindScriptProgramDef> programs,
            List<MorrowindScriptInstructionDef> instructions,
            List<MorrowindScriptLocalDef> locals)
        {
            int firstInstruction = instructions.Count;
            int firstLocal = locals.Count;
            int maxStack = 0;
            int stackDepth = 0;
            var localLookup = new Dictionary<string, (int Index, byte Kind)>(StringComparer.OrdinalIgnoreCase);
            var ifStack = new Stack<int>();

            if (string.IsNullOrWhiteSpace(script.Id))
            {
                programs.Add(CreateDisabled(script, scriptIndex, firstInstruction, firstLocal, "Missing script id.", MorrowindScriptProgramStatus.FailedInvalid));
                return;
            }

            string[] lines = (script.Text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = StripComment(lines[lineIndex]).Trim();
                if (line.Length == 0)
                    continue;

                if (StartsWithCommand(line, "begin") || StartsWithCommand(line, "end"))
                    continue;

                if (TryCompileLocalDeclaration(line, firstLocal, locals, localLookup))
                    continue;

                if (TryCompileIf(
                        line,
                        localLookup,
                        globals,
                        sounds,
                        dialogues,
                        instructions,
                        ifStack,
                        ref stackDepth,
                        ref maxStack,
                        out string ifFailure))
                {
                    continue;
                }

                if (ifFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, ifFailure, lineIndex);
                    return;
                }

                if (TryCompileEndif(line, instructions, ifStack, out string endifFailure))
                    continue;

                if (endifFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, endifFailure, lineIndex);
                    return;
                }

                if (StartsWithCommand(line, "else") || StartsWithCommand(line, "elseif"))
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, "else/elseif are not supported in MWScript V2.", lineIndex);
                    return;
                }

                if (StartsWithCommand(line, "return"))
                {
                    instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.Return });
                    continue;
                }

                if (TryCompileSet(line, localLookup, globals, sounds, dialogues, instructions, ref stackDepth, ref maxStack, out string setFailure))
                {
                    stackDepth = Math.Max(0, stackDepth - 1);
                    continue;
                }

                if (setFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, setFailure, lineIndex);
                    return;
                }

                if (TryCompileActivate(line, instructions, out string activateFailure))
                {
                    continue;
                }

                if (activateFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, activateFailure, lineIndex);
                    return;
                }

                if (TryCompileRotate(line, instructions, out string rotateFailure))
                {
                    continue;
                }

                if (rotateFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, rotateFailure, lineIndex);
                    return;
                }

                if (TryCompileAudio(line, sounds, instructions, out string audioFailure))
                {
                    continue;
                }

                if (audioFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, audioFailure, lineIndex);
                    return;
                }

                DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, $"Unsupported command '{line}'.", lineIndex);
                return;
            }

            if (ifStack.Count > 0)
            {
                DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, "Unclosed if block.", lines.Length - 1);
                return;
            }

            programs.Add(new MorrowindScriptProgramDef
            {
                Id = script.Id,
                SourceScriptIndex = scriptIndex,
                Status = (byte)MorrowindScriptProgramStatus.Compiled,
                DisabledReason = string.Empty,
                FirstInstructionIndex = instructions.Count == firstInstruction ? -1 : firstInstruction,
                InstructionCount = instructions.Count - firstInstruction,
                FirstLocalIndex = locals.Count == firstLocal ? -1 : firstLocal,
                LocalCount = locals.Count - firstLocal,
                MaxStack = Math.Max(1, maxStack),
            });
        }

        static void DisableUnsupported(
            in GenericRecordDef script,
            int scriptIndex,
            int firstInstruction,
            int firstLocal,
            List<MorrowindScriptInstructionDef> instructions,
            List<MorrowindScriptLocalDef> locals,
            List<MorrowindScriptProgramDef> programs,
            string reason,
            int lineIndex)
        {
            if (instructions.Count > firstInstruction)
                instructions.RemoveRange(firstInstruction, instructions.Count - firstInstruction);
            if (locals.Count > firstLocal)
                locals.RemoveRange(firstLocal, locals.Count - firstLocal);

            programs.Add(CreateDisabled(
                script,
                scriptIndex,
                firstInstruction,
                firstLocal,
                $"line {lineIndex + 1}: {reason}",
                MorrowindScriptProgramStatus.DisabledUnsupported));
        }

        static MorrowindScriptProgramDef CreateDisabled(
            in GenericRecordDef script,
            int scriptIndex,
            int firstInstruction,
            int firstLocal,
            string reason,
            MorrowindScriptProgramStatus status)
        {
            return new MorrowindScriptProgramDef
            {
                Id = script.Id,
                SourceScriptIndex = scriptIndex,
                Status = (byte)status,
                DisabledReason = reason ?? string.Empty,
                FirstInstructionIndex = firstInstruction,
                InstructionCount = 0,
                FirstLocalIndex = firstLocal,
                LocalCount = 0,
                MaxStack = 1,
            };
        }

        static void LogCompileCoverage(MorrowindScriptProgramDef[] programs)
        {
            if (programs == null || programs.Length == 0)
                return;

            int compiled = 0;
            int disabledUnsupported = 0;
            int failedInvalid = 0;
            var unsupportedRoots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < programs.Length; i++)
            {
                var status = (MorrowindScriptProgramStatus)programs[i].Status;
                if (status == MorrowindScriptProgramStatus.Compiled)
                {
                    compiled++;
                    continue;
                }

                if (status == MorrowindScriptProgramStatus.DisabledUnsupported)
                {
                    disabledUnsupported++;
                    string root = ExtractUnsupportedRoot(programs[i].DisabledReason);
                    if (!string.IsNullOrWhiteSpace(root))
                        unsupportedRoots[root] = unsupportedRoots.TryGetValue(root, out int count) ? count + 1 : 1;
                    continue;
                }

                if (status == MorrowindScriptProgramStatus.FailedInvalid)
                    failedInvalid++;
            }

            var summary = new StringBuilder(16 * 1024);
            summary.Append("[VVardenfell][MWScript][Coverage] scripts=").Append(programs.Length)
                .Append(" compiled=").Append(compiled)
                .Append(" disabledUnsupported=").Append(disabledUnsupported)
                .Append(" failedInvalid=").Append(failedInvalid);

            if (unsupportedRoots.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine("Unsupported roots:");
                foreach (var pair in SortUnsupportedRoots(unsupportedRoots))
                    summary.Append("  ").Append(pair.Key).Append(": ").Append(pair.Value).AppendLine();
            }

            if (disabledUnsupported > 0 || failedInvalid > 0)
            {
                summary.AppendLine("Unsupported scripts:");
                for (int i = 0; i < programs.Length; i++)
                {
                    var status = (MorrowindScriptProgramStatus)programs[i].Status;
                    if (status != MorrowindScriptProgramStatus.DisabledUnsupported && status != MorrowindScriptProgramStatus.FailedInvalid)
                        continue;

                    summary.Append("  ")
                        .Append(string.IsNullOrWhiteSpace(programs[i].Id) ? $"script:{i}" : programs[i].Id)
                        .Append(": ")
                        .Append(programs[i].DisabledReason)
                        .AppendLine();
                }
            }

            UnityEngine.Debug.Log(summary.ToString());
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

        static string ExtractUnsupportedRoot(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return string.Empty;

            if (TryExtractQuotedReasonPayload(reason, "Unsupported command '", out string command)
                || TryExtractQuotedReasonPayload(reason, "Unsupported expression '", out command)
                || TryExtractQuotedReasonPayload(reason, "Set command only supports numeric literals in V1: '", out command)
                || TryExtractQuotedReasonPayload(reason, "Unsupported comparison operator '", out command))
            {
                string[] tokens = SplitCommandTokens(command);
                return tokens.Length == 0 ? command.Trim() : NormalizeToken(tokens[0]);
            }

            if (reason.IndexOf("else/elseif", StringComparison.OrdinalIgnoreCase) >= 0)
                return "else/elseif";
            if (reason.IndexOf("Explicit reference", StringComparison.OrdinalIgnoreCase) >= 0)
                return "explicit-reference";

            int colon = reason.IndexOf(':');
            string root = colon >= 0 ? reason.Substring(colon + 1).Trim() : reason.Trim();
            int space = root.IndexOf(' ');
            return space > 0 ? root.Substring(0, space) : root;
        }

        static bool TryExtractQuotedReasonPayload(string reason, string marker, out string payload)
        {
            payload = null;
            int start = reason.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return false;

            start += marker.Length;
            int end = reason.IndexOf('\'', start);
            if (end < 0)
                return false;

            payload = reason.Substring(start, end - start);
            return true;
        }

        static bool TryCompileLocalDeclaration(
            string line,
            int firstLocal,
            List<MorrowindScriptLocalDef> locals,
            Dictionary<string, (int Index, byte Kind)> lookup)
        {
            string[] tokens = SplitWhitespace(line);
            if (tokens.Length != 2)
                return false;

            byte valueKind = tokens[0].Equals("float", StringComparison.OrdinalIgnoreCase)
                ? (byte)MorrowindScriptValueKind.Float
                : (tokens[0].Equals("short", StringComparison.OrdinalIgnoreCase) || tokens[0].Equals("long", StringComparison.OrdinalIgnoreCase))
                    ? (byte)MorrowindScriptValueKind.Integer
                    : (byte)0;
            if (valueKind == 0)
                return false;

            string name = tokens[1].Trim();
            if (string.IsNullOrWhiteSpace(name) || lookup.ContainsKey(name))
                return true;

            lookup[name] = (locals.Count - firstLocal, valueKind);
            locals.Add(new MorrowindScriptLocalDef
            {
                Name = name,
                ValueKind = valueKind,
            });
            return true;
        }

        static bool TryCompileIf(
            string line,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, (int Index, DialogueDefType Type)> dialogueLookup,
            List<MorrowindScriptInstructionDef> instructions,
            Stack<int> ifStack,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            if (!StartsWithCommand(line, "if"))
                return false;

            string condition = TrimEnclosingParentheses(line.Substring(2).Trim());
            if (string.IsNullOrWhiteSpace(condition))
            {
                failure = "if statement is missing a condition.";
                return false;
            }

            if (!TryCompileCondition(condition, localLookup, globalLookup, soundLookup, dialogueLookup, instructions, ref stackDepth, ref maxStack, out failure))
                return false;

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.JumpIfZero,
                Int0 = 0,
            });
            stackDepth = Math.Max(0, stackDepth - 1);
            ifStack.Push(instructions.Count - 1);
            return true;
        }

        static bool TryCompileEndif(
            string line,
            List<MorrowindScriptInstructionDef> instructions,
            Stack<int> ifStack,
            out string failure)
        {
            failure = null;
            if (!StartsWithCommand(line, "endif"))
                return false;

            if (ifStack.Count == 0)
            {
                failure = "endif without matching if.";
                return false;
            }

            int jumpIndex = ifStack.Pop();
            var jump = instructions[jumpIndex];
            jump.Int0 = instructions.Count - jumpIndex - 1;
            instructions[jumpIndex] = jump;
            return true;
        }

        static bool TryCompileCondition(
            string condition,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, (int Index, DialogueDefType Type)> dialogueLookup,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            if (condition.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                failure = $"Explicit reference conditions are not supported in MWScript V2: '{condition}'.";
                return false;
            }

            string[] tokens = SplitConditionTokens(condition);
            if (tokens.Length == 0)
            {
                failure = "if statement is missing a condition.";
                return false;
            }

            int comparisonIndex = FindComparisonToken(tokens);
            if (comparisonIndex < 0)
                return TryCompileExpression(tokens, 0, tokens.Length, localLookup, globalLookup, soundLookup, dialogueLookup, instructions, ref stackDepth, ref maxStack, out failure);

            if (FindComparisonToken(tokens, comparisonIndex + 1) >= 0)
            {
                failure = $"Condition has more than one comparison operator: '{condition}'.";
                return false;
            }

            if (!TryCompileExpression(tokens, 0, comparisonIndex, localLookup, globalLookup, soundLookup, dialogueLookup, instructions, ref stackDepth, ref maxStack, out failure))
                return false;

            if (!TryCompileExpression(tokens, comparisonIndex + 1, tokens.Length - comparisonIndex - 1, localLookup, globalLookup, soundLookup, dialogueLookup, instructions, ref stackDepth, ref maxStack, out failure))
                return false;

            if (!TryMapComparisonOpcode(tokens[comparisonIndex], out MorrowindScriptOpcode opcode))
            {
                failure = $"Unsupported comparison operator '{tokens[comparisonIndex]}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)opcode });
            stackDepth = Math.Max(0, stackDepth - 1);
            return true;
        }

        static bool TryCompileExpression(
            string[] tokens,
            int start,
            int count,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, (int Index, DialogueDefType Type)> dialogueLookup,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            if (count <= 0)
            {
                failure = "Expression is missing.";
                return false;
            }

            if (count == 2
                && tokens[start].Equals("getdistance", StringComparison.OrdinalIgnoreCase)
                && tokens[start + 1].Equals("player", StringComparison.OrdinalIgnoreCase))
            {
                instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.GetDistancePlayer });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (count == 2
                && tokens[start].Equals("getsoundplaying", StringComparison.OrdinalIgnoreCase))
            {
                string soundId = NormalizeToken(tokens[start + 1]).Trim('"');
                if (!soundLookup.TryGetValue(ContentId.NormalizeId(soundId), out SoundDefHandle sound) || !sound.IsValid)
                {
                    failure = $"GetSoundPlaying references unknown sound '{soundId}'.";
                    return false;
                }

                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)MorrowindScriptOpcode.GetSoundPlaying,
                    Int0 = sound.Value,
                });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (count == 2
                && tokens[start].Equals("getjournalindex", StringComparison.OrdinalIgnoreCase))
            {
                string journalId = NormalizeToken(tokens[start + 1]).Trim('"');
                string normalizedJournalId = ContentId.NormalizeId(journalId);
                if (!dialogueLookup.TryGetValue(normalizedJournalId, out var dialogue))
                {
                    failure = $"GetJournalIndex references unknown journal '{journalId}'.";
                    return false;
                }

                if (dialogue.Type != DialogueDefType.Journal)
                {
                    failure = $"GetJournalIndex references non-journal dialogue '{journalId}'.";
                    return false;
                }

                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)MorrowindScriptOpcode.GetJournalIndex,
                    Int0 = dialogue.Index,
                });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (count != 1)
            {
                failure = $"Unsupported expression '{string.Join(" ", tokens, start, count)}'.";
                return false;
            }

            string token = tokens[start];
            if (token.Equals("getsecondspassed", StringComparison.OrdinalIgnoreCase))
            {
                instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.GetSecondsPassed });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (TryParseLiteral(token, out int intValue, out float floatValue, out bool isFloat))
            {
                EmitPushLiteral(instructions, intValue, floatValue, isFloat, ref stackDepth, ref maxStack);
                return true;
            }

            if (localLookup.TryGetValue(token, out var local))
            {
                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)MorrowindScriptOpcode.GetLocal,
                    Int0 = local.Index,
                });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            string normalizedToken = ContentId.NormalizeId(token);
            if (globalLookup.TryGetValue(normalizedToken, out var global))
            {
                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)MorrowindScriptOpcode.GetGlobal,
                    Int0 = global.Index,
                });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (token.Equals("cellchanged", StringComparison.OrdinalIgnoreCase))
            {
                instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.GetCellChanged });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (token.Equals("menumode", StringComparison.OrdinalIgnoreCase))
            {
                instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.GetMenuMode });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (token.Equals("onactivate", StringComparison.OrdinalIgnoreCase))
            {
                instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.GetOnActivate });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            failure = $"Unsupported expression '{token}'.";
            return false;
        }

        static bool TryCompileSet(
            string line,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, (int Index, DialogueDefType Type)> dialogueLookup,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            if (!StartsWithCommand(line, "set"))
                return false;

            string rest = line.Substring(3).Trim();
            int toIndex = IndexOfWord(rest, "to");
            if (toIndex < 0)
            {
                failure = "Set command is missing 'to'.";
                return false;
            }

            string target = rest.Substring(0, toIndex).Trim();
            string expressionText = TrimEnclosingParentheses(rest.Substring(toIndex + 2).Trim());
            int firstExpressionInstruction = instructions.Count;
            int stackDepthBeforeExpression = stackDepth;
            if (!TryCompileArithmeticExpression(
                    expressionText,
                    localLookup,
                    globalLookup,
                    soundLookup,
                    dialogueLookup,
                    instructions,
                    ref stackDepth,
                    ref maxStack,
                    out failure))
            {
                return false;
            }

            if (localLookup.TryGetValue(target, out var local))
            {
                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)(local.Kind == (byte)MorrowindScriptValueKind.Float ? MorrowindScriptOpcode.SetLocalFloat : MorrowindScriptOpcode.SetLocalInt),
                    Int0 = local.Index,
                });
                return true;
            }

            string normalizedTarget = ContentId.NormalizeId(target);
            if (globalLookup.TryGetValue(normalizedTarget, out var global))
            {
                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)(global.Kind == (byte)MorrowindScriptValueKind.Float ? MorrowindScriptOpcode.SetGlobalFloat : MorrowindScriptOpcode.SetGlobalInt),
                    Int0 = global.Index,
                });
                return true;
            }

            failure = $"Set target '{target}' is not a declared local or baked global.";
            instructions.RemoveRange(firstExpressionInstruction, instructions.Count - firstExpressionInstruction);
            stackDepth = stackDepthBeforeExpression;
            return false;
        }

        static bool TryCompileArithmeticExpression(
            string expression,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, (int Index, DialogueDefType Type)> dialogueLookup,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            string normalizedExpression = NormalizeSetExpressionReferences(expression);
            if (normalizedExpression.IndexOf('(') >= 0 || normalizedExpression.IndexOf(')') >= 0)
            {
                failure = $"Nested parenthesized set expressions are not supported in MWScript V2: '{expression}'.";
                return false;
            }

            if (normalizedExpression.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                failure = $"Explicit reference expressions are not supported in MWScript V2: '{expression}'.";
                return false;
            }

            string[] tokens = SplitExpressionTokens(normalizedExpression);
            if (tokens.Length == 0)
            {
                failure = "Set expression is missing.";
                return false;
            }

            return TryCompileArithmeticTokens(tokens, 0, tokens.Length, localLookup, globalLookup, soundLookup, dialogueLookup, instructions, ref stackDepth, ref maxStack, out failure);
        }

        static bool TryCompileArithmeticTokens(
            string[] tokens,
            int start,
            int count,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, (int Index, DialogueDefType Type)> dialogueLookup,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            if (count <= 0)
            {
                failure = "Expression is missing.";
                return false;
            }

            if (tokens[start] == "+" || tokens[start] == "-")
            {
                if (!TryCompileArithmeticTokens(tokens, start + 1, count - 1, localLookup, globalLookup, soundLookup, dialogueLookup, instructions, ref stackDepth, ref maxStack, out failure))
                    return false;

                if (tokens[start] == "-")
                    instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.Negate });
                return true;
            }

            int operatorIndex = FindArithmeticOperator(tokens, start, count, 0);
            if (operatorIndex < 0)
                operatorIndex = FindArithmeticOperator(tokens, start, count, 1);

            if (operatorIndex < 0)
                return TryCompileExpression(tokens, start, count, localLookup, globalLookup, soundLookup, dialogueLookup, instructions, ref stackDepth, ref maxStack, out failure);

            if (!TryCompileArithmeticTokens(tokens, start, operatorIndex - start, localLookup, globalLookup, soundLookup, dialogueLookup, instructions, ref stackDepth, ref maxStack, out failure))
                return false;

            if (!TryCompileArithmeticTokens(tokens, operatorIndex + 1, start + count - operatorIndex - 1, localLookup, globalLookup, soundLookup, dialogueLookup, instructions, ref stackDepth, ref maxStack, out failure))
                return false;

            if (!TryMapArithmeticOpcode(tokens[operatorIndex], out MorrowindScriptOpcode opcode))
            {
                failure = $"Unsupported arithmetic operator '{tokens[operatorIndex]}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)opcode });
            stackDepth = Math.Max(0, stackDepth - 1);
            return true;
        }

        static int FindArithmeticOperator(string[] tokens, int start, int count, int precedence)
        {
            int end = start + count;
            for (int i = end - 1; i >= start; i--)
            {
                string token = tokens[i];
                if (precedence == 0 && (token == "+" || token == "-") && !IsUnaryArithmeticOperator(tokens, start, i))
                    return i;
                if (precedence == 1 && (token == "*" || token == "/"))
                    return i;
            }

            return -1;
        }

        static bool IsUnaryArithmeticOperator(string[] tokens, int start, int index)
        {
            if (index == start)
                return true;

            string previous = tokens[index - 1];
            return previous == "+" || previous == "-" || previous == "*" || previous == "/";
        }

        static bool TryMapArithmeticOpcode(string op, out MorrowindScriptOpcode opcode)
        {
            switch (op)
            {
                case "+":
                    opcode = MorrowindScriptOpcode.Add;
                    return true;
                case "-":
                    opcode = MorrowindScriptOpcode.Subtract;
                    return true;
                case "*":
                    opcode = MorrowindScriptOpcode.Multiply;
                    return true;
                case "/":
                    opcode = MorrowindScriptOpcode.Divide;
                    return true;
                default:
                    opcode = default;
                    return false;
            }
        }

        static bool TryCompileActivate(
            string line,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (!StartsWithCommand(line, "activate"))
                return false;

            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                failure = $"Explicit Activate references are not supported in MWScript V2: '{line}'.";
                return false;
            }

            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 1)
            {
                failure = $"Activate command only supports implicit self activation in MWScript V2: '{line}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.Activate });
            return true;
        }

        static bool TryCompileRotate(
            string line,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (!StartsWithCommand(line, "rotate"))
                return false;

            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                failure = $"Explicit Rotate references are not supported in MWScript V2: '{line}'.";
                return false;
            }

            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 3)
            {
                failure = $"Rotate command requires axis and speed in MWScript V2: '{line}'.";
                return false;
            }

            string axis = NormalizeToken(tokens[1]).Trim('"');
            if (!axis.Equals("x", StringComparison.OrdinalIgnoreCase))
            {
                failure = $"Rotate command currently supports only the implicit local X axis in MWScript V2: '{line}'.";
                return false;
            }

            if (!float.TryParse(NormalizeToken(tokens[2]), NumberStyles.Float, CultureInfo.InvariantCulture, out float speed))
            {
                failure = $"Rotate command has invalid speed '{tokens[2]}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.Rotate,
                Operand0 = 0,
                Float0 = speed,
            });
            return true;
        }

        static bool TryCompileAudio(
            string line,
            Dictionary<string, SoundDefHandle> sounds,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            line = StripExplicitReferencePrefix(line);
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            string command = NormalizeToken(tokens[0]);
            if (!TryMapAudioKind(command, out MorrowindScriptAudioKind kind))
                return false;

            if (tokens.Length < 2)
            {
                failure = $"{command} requires a sound id.";
                return false;
            }

            string soundId = NormalizeToken(tokens[1]).Trim('"');
            if (!sounds.TryGetValue(ContentId.NormalizeId(soundId), out SoundDefHandle sound) || !sound.IsValid)
            {
                failure = $"{command} references unknown sound '{soundId}'.";
                return false;
            }

            float volume = 1f;
            float pitch = 1f;
            if (kind == MorrowindScriptAudioKind.PlayLoopSound3DVP || kind == MorrowindScriptAudioKind.PlaySound3DVP)
            {
                if (tokens.Length >= 3 && !float.TryParse(NormalizeToken(tokens[2]), NumberStyles.Float, CultureInfo.InvariantCulture, out volume))
                {
                    failure = $"{command} has invalid volume '{tokens[2]}'.";
                    return false;
                }

                if (tokens.Length >= 4 && !float.TryParse(NormalizeToken(tokens[3]), NumberStyles.Float, CultureInfo.InvariantCulture, out pitch))
                {
                    failure = $"{command} has invalid pitch '{tokens[3]}'.";
                    return false;
                }
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.EmitAudioRequest,
                Operand0 = (byte)kind,
                Int0 = sound.Value,
                Float0 = volume,
                Float1 = pitch,
            });
            return true;
        }

        static bool TryMapComparisonOpcode(string op, out MorrowindScriptOpcode opcode)
        {
            switch (op)
            {
                case "==":
                case "=":
                    opcode = MorrowindScriptOpcode.CompareEqual;
                    return true;
                case "!=":
                    opcode = MorrowindScriptOpcode.CompareNotEqual;
                    return true;
                case "<":
                    opcode = MorrowindScriptOpcode.CompareLess;
                    return true;
                case "<=":
                    opcode = MorrowindScriptOpcode.CompareLessOrEqual;
                    return true;
                case ">":
                    opcode = MorrowindScriptOpcode.CompareGreater;
                    return true;
                case ">=":
                    opcode = MorrowindScriptOpcode.CompareGreaterOrEqual;
                    return true;
                default:
                    opcode = default;
                    return false;
            }
        }

        static void EmitPushLiteral(
            List<MorrowindScriptInstructionDef> instructions,
            int intValue,
            float floatValue,
            bool isFloat,
            ref int stackDepth,
            ref int maxStack)
        {
            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)(isFloat ? MorrowindScriptOpcode.PushFloat : MorrowindScriptOpcode.PushInt),
                Int0 = intValue,
                Float0 = floatValue,
            });
            stackDepth++;
            maxStack = Math.Max(maxStack, stackDepth);
        }

        static bool TryParseLiteral(string text, out int intValue, out float floatValue, out bool isFloat)
        {
            intValue = 0;
            floatValue = 0f;
            isFloat = text.IndexOf('.') >= 0;
            if (isFloat)
            {
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue))
                    return false;
                intValue = (int)floatValue;
                return true;
            }

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
                return false;
            floatValue = intValue;
            return true;
        }

        static bool TryMapAudioKind(string command, out MorrowindScriptAudioKind kind)
        {
            command = NormalizeToken(command);
            if (command.Equals("playsound", StringComparison.OrdinalIgnoreCase))
                kind = MorrowindScriptAudioKind.PlaySound;
            else if (command.Equals("playsound3d", StringComparison.OrdinalIgnoreCase))
                kind = MorrowindScriptAudioKind.PlaySound3D;
            else if (command.Equals("playsound3dvp", StringComparison.OrdinalIgnoreCase))
                kind = MorrowindScriptAudioKind.PlaySound3DVP;
            else if (command.Equals("playloopsound3d", StringComparison.OrdinalIgnoreCase))
                kind = MorrowindScriptAudioKind.PlayLoopSound3D;
            else if (command.Equals("playloopsound3dvp", StringComparison.OrdinalIgnoreCase))
                kind = MorrowindScriptAudioKind.PlayLoopSound3DVP;
            else
            {
                kind = MorrowindScriptAudioKind.None;
                return false;
            }

            return true;
        }

        static Dictionary<string, SoundDefHandle> BuildSoundLookup(SoundDef[] sounds)
        {
            var lookup = new Dictionary<string, SoundDefHandle>(StringComparer.OrdinalIgnoreCase);
            if (sounds == null)
                return lookup;

            for (int i = 0; i < sounds.Length; i++)
                lookup[ContentId.NormalizeId(sounds[i].Id)] = SoundDefHandle.FromIndex(i);
            return lookup;
        }

        static Dictionary<string, (int Index, byte Kind)> BuildGlobalLookup(GenericRecordDef[] globals)
        {
            var lookup = new Dictionary<string, (int Index, byte Kind)>(StringComparer.OrdinalIgnoreCase);
            if (globals == null)
                return lookup;

            for (int i = 0; i < globals.Length; i++)
                lookup[ContentId.NormalizeId(globals[i].Id)] = (i, ResolveGlobalKind(globals[i]));
            return lookup;
        }

        static Dictionary<string, (int Index, DialogueDefType Type)> BuildDialogueLookup(DialogueDef[] dialogues)
        {
            var lookup = new Dictionary<string, (int Index, DialogueDefType Type)>(StringComparer.OrdinalIgnoreCase);
            if (dialogues == null)
                return lookup;

            for (int i = 0; i < dialogues.Length; i++)
                lookup[ContentId.NormalizeId(dialogues[i].Id)] = (i, dialogues[i].Type);
            return lookup;
        }

        static byte ResolveGlobalKind(in GenericRecordDef global)
        {
            if (!string.IsNullOrWhiteSpace(global.Name) && global.Name[0] == 'f')
                return (byte)MorrowindScriptValueKind.Float;

            return (byte)MorrowindScriptValueKind.Integer;
        }

        static string StripComment(string line)
        {
            int quoteDepth = 0;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"')
                    quoteDepth ^= 1;
                else if (line[i] == ';' && quoteDepth == 0)
                    return line.Substring(0, i);
            }
            return line;
        }

        static bool StartsWithCommand(string line, string command)
        {
            if (!line.StartsWith(command, StringComparison.OrdinalIgnoreCase))
                return false;
            return line.Length == command.Length || char.IsWhiteSpace(line[command.Length]);
        }

        static int IndexOfWord(string text, string word)
        {
            int index = CultureInfo.InvariantCulture.CompareInfo.IndexOf(text, word, CompareOptions.IgnoreCase);
            while (index >= 0)
            {
                bool left = index == 0 || char.IsWhiteSpace(text[index - 1]);
                int after = index + word.Length;
                bool right = after == text.Length || char.IsWhiteSpace(text[after]);
                if (left && right)
                    return index;
                index = CultureInfo.InvariantCulture.CompareInfo.IndexOf(text, word, index + 1, CompareOptions.IgnoreCase);
            }

            return -1;
        }

        static string[] SplitWhitespace(string line)
        {
            return line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        }

        static string[] SplitConditionTokens(string condition)
        {
            var tokens = new List<string>();
            int i = 0;
            while (i < condition.Length)
            {
                char c = condition[i];
                if (char.IsWhiteSpace(c) || c == ',' || c == '(' || c == ')')
                {
                    i++;
                    continue;
                }

                if (c == '<' || c == '>' || c == '=' || c == '!')
                {
                    if (i + 1 < condition.Length && condition[i + 1] == '=')
                    {
                        tokens.Add(condition.Substring(i, 2));
                        i += 2;
                    }
                    else
                    {
                        tokens.Add(condition.Substring(i, 1));
                        i++;
                    }
                    continue;
                }

                if (c == '"')
                {
                    int quotedTokenStart = ++i;
                    while (i < condition.Length && condition[i] != '"')
                        i++;

                    string quotedToken = condition.Substring(quotedTokenStart, i - quotedTokenStart);
                    if (!string.IsNullOrWhiteSpace(quotedToken))
                        tokens.Add(quotedToken);
                    if (i < condition.Length)
                        i++;
                    continue;
                }

                int tokenStart = i;
                while (i < condition.Length
                       && !char.IsWhiteSpace(condition[i])
                       && condition[i] != ','
                       && condition[i] != '('
                       && condition[i] != ')'
                       && condition[i] != '<'
                       && condition[i] != '>'
                       && condition[i] != '='
                       && condition[i] != '!')
                {
                    i++;
                }

                string token = NormalizeToken(condition.Substring(tokenStart, i - tokenStart));
                if (!string.IsNullOrWhiteSpace(token))
                    tokens.Add(token);
            }

            return tokens.ToArray();
        }

        static string[] SplitExpressionTokens(string expression)
        {
            var tokens = new List<string>();
            int i = 0;
            bool expectValue = true;
            while (i < expression.Length)
            {
                char c = expression[i];
                if (char.IsWhiteSpace(c) || c == ',')
                {
                    i++;
                    continue;
                }

                if ((c == '-' || c == '+')
                    && expectValue
                    && i + 1 < expression.Length
                    && (char.IsDigit(expression[i + 1]) || expression[i + 1] == '.'))
                {
                    int signedNumberStart = i++;
                    while (i < expression.Length && (char.IsDigit(expression[i]) || expression[i] == '.'))
                        i++;
                    tokens.Add(expression.Substring(signedNumberStart, i - signedNumberStart));
                    expectValue = false;
                    continue;
                }

                if (c == '+' || c == '-' || c == '*' || c == '/')
                {
                    tokens.Add(expression.Substring(i, 1));
                    i++;
                    expectValue = true;
                    continue;
                }

                if (c == '"')
                {
                    int quotedTokenStart = ++i;
                    while (i < expression.Length && expression[i] != '"')
                        i++;

                    string quotedToken = expression.Substring(quotedTokenStart, i - quotedTokenStart);
                    if (!string.IsNullOrWhiteSpace(quotedToken))
                    {
                        tokens.Add(quotedToken);
                        expectValue = false;
                    }

                    if (i < expression.Length)
                        i++;
                    continue;
                }

                int tokenStart = i;
                while (i < expression.Length
                       && !char.IsWhiteSpace(expression[i])
                       && expression[i] != ','
                       && expression[i] != '+'
                       && expression[i] != '-'
                       && expression[i] != '*'
                       && expression[i] != '/')
                {
                    i++;
                }

                string token = NormalizeToken(expression.Substring(tokenStart, i - tokenStart));
                if (!string.IsNullOrWhiteSpace(token))
                {
                    tokens.Add(token);
                    expectValue = false;
                }
            }

            return tokens.ToArray();
        }

        static int FindComparisonToken(string[] tokens, int start = 0)
        {
            for (int i = Math.Max(0, start); i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (token == "="
                    || token == "=="
                    || token == "!="
                    || token == "<"
                    || token == "<="
                    || token == ">"
                    || token == ">=")
                {
                    return i;
                }
            }

            return -1;
        }

        static string TrimEnclosingParentheses(string text)
        {
            text = (text ?? string.Empty).Trim();
            while (text.Length >= 2 && text[0] == '(' && text[text.Length - 1] == ')' && HasSingleOuterParenthesisPair(text))
                text = text.Substring(1, text.Length - 2).Trim();
            return text;
        }

        static bool HasSingleOuterParenthesisPair(string text)
        {
            int depth = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '(')
                    depth++;
                else if (text[i] == ')')
                {
                    depth--;
                    if (depth == 0 && i < text.Length - 1)
                        return false;
                    if (depth < 0)
                        return false;
                }
            }

            return depth == 0;
        }

        static string[] SplitCommandTokens(string line)
        {
            var tokens = new List<string>();
            int i = 0;
            while (i < line.Length)
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
                    while (i < line.Length && line[i] == ',')
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

        static string StripExplicitReferencePrefix(string line)
        {
            int arrow = line.IndexOf("->", StringComparison.Ordinal);
            return arrow < 0 ? line : line.Substring(arrow + 2).TrimStart();
        }

        static string NormalizeSetExpressionReferences(string expression)
        {
            expression = TrimEnclosingParentheses(expression);
            if (expression.StartsWith("player->", StringComparison.OrdinalIgnoreCase))
                return expression.Substring("player->".Length).TrimStart();
            return expression;
        }

        static string NormalizeToken(string token)
        {
            return (token ?? string.Empty).Trim().Trim(',');
        }
    }
}
