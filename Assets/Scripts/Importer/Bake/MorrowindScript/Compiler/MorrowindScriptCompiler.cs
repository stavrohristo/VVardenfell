using System;
using System.Collections.Generic;
using System.Globalization;
using VVardenfell.Core.Cache;

namespace VVardenfell.Importer.Bake
{
    internal static class MorrowindScriptCompiler
    {
        public static void Build(
            GenericRecordDef[] scripts,
            SoundDef[] sounds,
            GenericRecordDef[] globals,
            out MorrowindScriptProgramDef[] programs,
            out MorrowindScriptInstructionDef[] instructions,
            out MorrowindScriptLocalDef[] locals)
        {
            scripts ??= Array.Empty<GenericRecordDef>();
            var soundLookup = BuildSoundLookup(sounds);
            var globalLookup = BuildGlobalLookup(globals);
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
                    programList,
                    instructionList,
                    localList);
            }

            LogSummary(programList, instructionList);
            programs = programList.ToArray();
            instructions = instructionList.ToArray();
            locals = localList.ToArray();
        }

        static void CompileScript(
            in GenericRecordDef script,
            int scriptIndex,
            Dictionary<string, SoundDefHandle> sounds,
            Dictionary<string, (int Index, byte Kind)> globals,
            List<MorrowindScriptProgramDef> programs,
            List<MorrowindScriptInstructionDef> instructions,
            List<MorrowindScriptLocalDef> locals)
        {
            int firstInstruction = instructions.Count;
            int firstLocal = locals.Count;
            int maxStack = 0;
            int stackDepth = 0;
            var localLookup = new Dictionary<string, (int Index, byte Kind)>(StringComparer.OrdinalIgnoreCase);
            bool emittedAudio = false;

            if (string.IsNullOrWhiteSpace(script.Id))
            {
                programs.Add(CreateDisabled(script, scriptIndex, firstInstruction, firstLocal, "Missing script id.", MorrowindScriptProgramStatus.FailedInvalid));
                return;
            }

            string[] lines = (script.Text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            bool containsAudioCommand = ContainsAudioCommand(lines);
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = StripComment(lines[lineIndex]).Trim();
                if (line.Length == 0)
                    continue;

                if (StartsWithCommand(line, "begin") || StartsWithCommand(line, "end"))
                    continue;

                if (TryCompileLocalDeclaration(line, firstLocal, locals, localLookup))
                    continue;

                if (StartsWithCommand(line, "return"))
                {
                    if (containsAudioCommand && !emittedAudio)
                        continue;

                    instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.Return });
                    continue;
                }

                if (TryCompileSet(line, localLookup, globals, instructions, ref stackDepth, ref maxStack, out string setFailure))
                {
                    stackDepth = Math.Max(0, stackDepth - 1);
                    continue;
                }

                if (setFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, setFailure, lineIndex);
                    return;
                }

                if (TryCompileAudio(line, sounds, instructions, out string audioFailure))
                {
                    emittedAudio = true;
                    continue;
                }

                if (audioFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, audioFailure, lineIndex);
                    return;
                }

                if (containsAudioCommand && IsIgnorableAudioScriptGuard(line))
                    continue;

                DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, $"Unsupported command '{line}'.", lineIndex);
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

        static bool TryCompileSet(
            string line,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
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
            string literalText = rest.Substring(toIndex + 2).Trim();
            if (!TryParseLiteral(literalText, out int intValue, out float floatValue, out bool isFloat))
            {
                failure = $"Set command only supports numeric literals in V1: '{literalText}'.";
                return false;
            }

            EmitPushLiteral(instructions, intValue, floatValue, isFloat, ref stackDepth, ref maxStack);

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
            instructions.RemoveAt(instructions.Count - 1);
            stackDepth = Math.Max(0, stackDepth - 1);
            return false;
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

        static bool IsIgnorableAudioScriptGuard(string line)
        {
            return StartsWithCommand(line, "if")
                || StartsWithCommand(line, "endif")
                || StartsWithCommand(line, "else")
                || StartsWithCommand(line, "elseif");
        }

        static bool ContainsAudioCommand(string[] lines)
        {
            if (lines == null)
                return false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = StripComment(lines[i]).Trim();
                line = StripExplicitReferencePrefix(line);
                string[] tokens = SplitCommandTokens(line);
                if (tokens.Length > 0 && TryMapAudioKind(tokens[0], out _))
                    return true;
            }

            return false;
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

        static byte ResolveGlobalKind(in GenericRecordDef global)
        {
            if (!string.IsNullOrWhiteSpace(global.Name) && global.Name[0] == 'f')
                return (byte)MorrowindScriptValueKind.Float;

            return (byte)MorrowindScriptValueKind.Integer;
        }

        static void LogSummary(List<MorrowindScriptProgramDef> programs, List<MorrowindScriptInstructionDef> instructions)
        {
            int compiled = 0;
            int disabled = 0;
            int failed = 0;
            int compiledAudio = 0;
            int disabledAudioNamed = 0;
            var disabledSamples = new List<string>(12);

            for (int i = 0; i < programs.Count; i++)
            {
                var program = programs[i];
                var status = (MorrowindScriptProgramStatus)program.Status;
                if (status == MorrowindScriptProgramStatus.Compiled)
                {
                    compiled++;
                    if (ProgramHasAudio(program, instructions))
                        compiledAudio++;
                }
                else if (status == MorrowindScriptProgramStatus.DisabledUnsupported)
                {
                    disabled++;
                    if (LooksLikeAudioScript(program))
                    {
                        disabledAudioNamed++;
                        if (disabledSamples.Count < 12)
                            disabledSamples.Add($"{program.Id}: {program.DisabledReason}");
                    }
                }
                else if (status == MorrowindScriptProgramStatus.FailedInvalid)
                {
                    failed++;
                }
            }

            UnityEngine.Debug.Log(
                $"[VVardenfell][MWScript][BakeDiag] scripts={programs.Count} compiled={compiled} compiledAudio={compiledAudio} disabledUnsupported={disabled} disabledAudioNamed={disabledAudioNamed} failedInvalid={failed}"
                + (disabledSamples.Count == 0 ? string.Empty : "\n  " + string.Join("\n  ", disabledSamples)));
        }

        static bool ProgramHasAudio(in MorrowindScriptProgramDef program, List<MorrowindScriptInstructionDef> instructions)
        {
            if (program.FirstInstructionIndex < 0 || program.InstructionCount <= 0)
                return false;

            int end = Math.Min(instructions.Count, program.FirstInstructionIndex + program.InstructionCount);
            for (int i = program.FirstInstructionIndex; i < end; i++)
            {
                if (instructions[i].Opcode == (byte)MorrowindScriptOpcode.EmitAudioRequest)
                    return true;
            }

            return false;
        }

        static bool LooksLikeAudioScript(in MorrowindScriptProgramDef program)
        {
            string id = program.Id ?? string.Empty;
            string reason = program.DisabledReason ?? string.Empty;
            return id.IndexOf("sound", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("amb", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("sound", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("Play", StringComparison.OrdinalIgnoreCase) >= 0;
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

        static string NormalizeToken(string token)
        {
            return (token ?? string.Empty).Trim().Trim(',');
        }
    }
}
