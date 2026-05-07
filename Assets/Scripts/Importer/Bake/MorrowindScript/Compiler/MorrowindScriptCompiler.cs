using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using static VVardenfell.Core.MorrowindCommandTextUtility;

namespace VVardenfell.Importer.Bake
{
    internal static class MorrowindScriptCompiler
    {
        struct IfBlockState
        {
            public int PendingFalseJump;
            public bool HasElse;
            public List<int> EndJumps;
        }

        struct DialogueCompileInfo
        {
            public int Index;
            public DialogueDefType Type;
            public int FirstInfoIndex;
            public int InfoCount;
        }

        struct ActorLocalCompileInfo
        {
            public int ActorHandleValue;
            public int LocalIndex;
            public byte ValueKind;
        }

        readonly struct MessageBoxToken
        {
            public MessageBoxToken(string text, bool quoted)
            {
                Text = text;
                Quoted = quoted;
            }

            public string Text { get; }
            public bool Quoted { get; }
        }

        public static void Build(
            GenericRecordDef[] scripts,
            SoundDef[] sounds,
            ActorDef[] actors,
            BaseDef[] activators,
            BaseDef[] doors,
            BaseDef[] containers,
            BaseDef[] items,
            LightDef[] lights,
            GenericRecordDef[] statics,
            ItemLeveledListDef[] creatureLeveledLists,
            ItemLeveledListDef[] itemLeveledLists,
            SpellDef[] spells,
            FactionDef[] factions,
            GenericRecordDef[] globals,
            DialogueDef[] dialogues,
            DialogueInfoDef[] dialogueInfos,
            RegionDef[] regions,
            MusicTrackDef[] musicTracks,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            out MorrowindScriptProgramDef[] programs,
            out MorrowindScriptInstructionDef[] instructions,
            out MorrowindScriptLocalDef[] locals,
            out MorrowindScriptMessageDef[] messages)
        {
            scripts ??= Array.Empty<GenericRecordDef>();
            var scriptLookup = BuildScriptLookup(scripts);
            var soundLookup = BuildSoundLookup(sounds);
            var actorLookup = BuildActorLookup(actors);
            var carryableLookup = BuildCarryableLookup(items, lights, itemLeveledLists);
            var placeAtSpawnableLookup = BuildPlaceAtSpawnableLookup(actors, items, lights);
            var explicitContentTargets = BuildExplicitContentTargetLookup(
                actors,
                activators,
                doors,
                containers,
                items,
                lights,
                statics,
                creatureLeveledLists,
                itemLeveledLists);
            var spellLookup = BuildSpellLookup(spells);
            var factionLookup = BuildFactionLookup(factions);
            var actorLocalLookup = BuildActorLocalLookup(actors, scripts);
            var globalLookup = BuildGlobalLookup(globals);
            var dialogueLookup = BuildDialogueLookup(dialogues);
            var regionLookup = BuildRegionLookup(regions);
            var musicLookup = BuildMusicTrackLookup(musicTracks);
            var programList = new List<MorrowindScriptProgramDef>(scripts.Length);
            var instructionList = new List<MorrowindScriptInstructionDef>(scripts.Length * 4);
            var localList = new List<MorrowindScriptLocalDef>();
            var messageList = new List<MorrowindScriptMessageDef>();

            for (int i = 0; i < scripts.Length; i++)
            {
                CompileScript(
                    scripts[i],
                    i,
                    scriptLookup,
                    soundLookup,
                    actorLookup,
                    carryableLookup,
                    placeAtSpawnableLookup,
                    spellLookup,
                    factionLookup,
                    actorLocalLookup,
                    globalLookup,
                    dialogueLookup,
                    regionLookup,
                    musicLookup,
                    dialogueInfos,
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
                    programList,
                    instructionList,
                    localList,
                    messageList);
            }

            programs = programList.ToArray();
            instructions = instructionList.ToArray();
            locals = localList.ToArray();
            messages = messageList.ToArray();
            LogCompileCoverage(programs);
        }

        static void CompileScript(
            in GenericRecordDef script,
            int scriptIndex,
            Dictionary<string, int> scripts,
            Dictionary<string, SoundDefHandle> sounds,
            Dictionary<string, int> actors,
            Dictionary<string, ContentReference> carryables,
            Dictionary<string, ContentReference> placeAtSpawnables,
            Dictionary<string, SpellDefHandle> spells,
            Dictionary<string, int> factions,
            Dictionary<string, ActorLocalCompileInfo> actorLocals,
            Dictionary<string, (int Index, byte Kind)> globals,
            Dictionary<string, DialogueCompileInfo> dialogues,
            Dictionary<string, RegionDefHandle> regions,
            Dictionary<string, MusicTrackDefHandle> musicTracks,
            DialogueInfoDef[] dialogueInfos,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptProgramDef> programs,
            List<MorrowindScriptInstructionDef> instructions,
            List<MorrowindScriptLocalDef> locals,
            List<MorrowindScriptMessageDef> messages)
        {
            int firstInstruction = instructions.Count;
            int firstLocal = locals.Count;
            int maxStack = 0;
            int stackDepth = 0;
            var localLookup = new Dictionary<string, (int Index, byte Kind)>(StringComparer.OrdinalIgnoreCase);
            var ifStack = new List<IfBlockState>();

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
                        actors,
                        carryables,
                        scripts,
                        spells,
                        factions,
                        actorLocals,
                        dialogues,
                        explicitRefTargets,
                        ambiguousExplicitRefTargets,
                        explicitContentTargets,
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

                if (TryCompileElseIf(
                        line,
                        localLookup,
                        globals,
                        sounds,
                        actors,
                        carryables,
                        scripts,
                        spells,
                        factions,
                        actorLocals,
                        dialogues,
                        explicitRefTargets,
                        ambiguousExplicitRefTargets,
                        explicitContentTargets,
                        instructions,
                        ifStack,
                        ref stackDepth,
                        ref maxStack,
                        out string elseIfFailure))
                {
                    continue;
                }

                if (elseIfFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, elseIfFailure, lineIndex);
                    return;
                }

                if (TryCompileElse(line, instructions, ifStack, out string elseFailure))
                    continue;

                if (elseFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, elseFailure, lineIndex);
                    return;
                }

                if (TryCompileEndif(line, instructions, ifStack, out string endifFailure))
                    continue;

                if (endifFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, endifFailure, lineIndex);
                    return;
                }

                if (StartsWithCommand(line, "return"))
                {
                    instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.Return });
                    continue;
                }

                if (TryCompilePlayerSkillCommand(
                        line,
                        localLookup,
                        globals,
                        sounds,
                        actors,
                        carryables,
                        spells,
                        factions,
                        actorLocals,
                        dialogues,
                        explicitRefTargets,
                        ambiguousExplicitRefTargets,
                        explicitContentTargets,
                        instructions,
                        ref stackDepth,
                        ref maxStack,
                        out string playerSkillFailure))
                {
                    stackDepth = Math.Max(0, stackDepth - 1);
                    continue;
                }

                if (playerSkillFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, playerSkillFailure, lineIndex);
                    return;
                }

                if (TryCompilePlayerAttributeCommand(
                        line,
                        localLookup,
                        globals,
                        sounds,
                        actors,
                        carryables,
                        spells,
                        factions,
                        actorLocals,
                        dialogues,
                        explicitRefTargets,
                        ambiguousExplicitRefTargets,
                        explicitContentTargets,
                        instructions,
                        ref stackDepth,
                        ref maxStack,
                        out string playerAttributeFailure))
                {
                    stackDepth = Math.Max(0, stackDepth - 1);
                    continue;
                }

                if (playerAttributeFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, playerAttributeFailure, lineIndex);
                    return;
                }

                if (TryCompileSet(line, localLookup, globals, sounds, actors, carryables, spells, factions, actorLocals, dialogues, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, ref stackDepth, ref maxStack, out string setFailure))
                {
                    stackDepth = Math.Max(0, stackDepth - 1);
                    continue;
                }

                if (setFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, setFailure, lineIndex);
                    return;
                }

                if (TryCompilePayFine(line, instructions, out string payFineFailure))
                    continue;

                if (payFineFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, payFineFailure, lineIndex);
                    return;
                }

                if (TryCompileSetPCCrimeLevel(
                        line,
                        localLookup,
                        globals,
                        sounds,
                        actors,
                        carryables,
                        spells,
                        factions,
                        actorLocals,
                        dialogues,
                        explicitRefTargets,
                        ambiguousExplicitRefTargets,
                        explicitContentTargets,
                        instructions,
                        ref stackDepth,
                        ref maxStack,
                        out string setPCCrimeLevelFailure))
                {
                    stackDepth = Math.Max(0, stackDepth - 1);
                    continue;
                }

                if (setPCCrimeLevelFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, setPCCrimeLevelFailure, lineIndex);
                    return;
                }

                if (TryCompileModPlayerReputation(line, instructions, out string reputationFailure))
                {
                    continue;
                }

                if (reputationFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, reputationFailure, lineIndex);
                    return;
                }

                if (TryCompilePlayerFactionCommand(line, factions, instructions, out string playerFactionFailure))
                {
                    continue;
                }

                if (playerFactionFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, playerFactionFailure, lineIndex);
                    return;
                }

                if (TryCompileFactionReactionCommand(line, factions, instructions, out string factionReactionFailure))
                {
                    continue;
                }

                if (factionReactionFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, factionReactionFailure, lineIndex);
                    return;
                }

                if (TryCompileRaiseRank(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, out string raiseRankFailure))
                {
                    continue;
                }

                if (raiseRankFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, raiseRankFailure, lineIndex);
                    return;
                }

                if (TryCompileJournal(line, dialogues, dialogueInfos, instructions, out string journalFailure))
                {
                    continue;
                }

                if (journalFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, journalFailure, lineIndex);
                    return;
                }

                if (TryCompileSetJournalIndex(line, dialogues, instructions, out string setJournalIndexFailure))
                {
                    continue;
                }

                if (setJournalIndexFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, setJournalIndexFailure, lineIndex);
                    return;
                }

                if (TryCompileTopicCommand(line, dialogues, instructions, out string topicFailure))
                {
                    continue;
                }

                if (topicFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, topicFailure, lineIndex);
                    return;
                }

                if (TryCompileStartScript(line, scripts, instructions, out string startScriptFailure))
                {
                    continue;
                }

                if (startScriptFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, startScriptFailure, lineIndex);
                    return;
                }

                if (TryCompileStopScript(line, script.Id, scripts, instructions, out string stopScriptFailure))
                {
                    continue;
                }

                if (stopScriptFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, stopScriptFailure, lineIndex);
                    return;
                }

                if (TryCompileDontSaveObject(line, instructions, out string dontSaveObjectFailure))
                {
                    continue;
                }

                if (dontSaveObjectFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, dontSaveObjectFailure, lineIndex);
                    return;
                }

                if (TryCompileRefStateCommand(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, out string refStateFailure))
                {
                    continue;
                }

                if (refStateFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, refStateFailure, lineIndex);
                    return;
                }

                if (TryCompileLockStateCommand(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, out string lockStateFailure))
                {
                    continue;
                }

                if (lockStateFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, lockStateFailure, lineIndex);
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

                if (TryCompileOnActivateStatement(line, instructions, out string onActivateStatementFailure))
                    continue;

                if (onActivateStatementFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, onActivateStatementFailure, lineIndex);
                    return;
                }

                if (TryCompileRotate(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, out string rotateFailure))
                {
                    continue;
                }

                if (rotateFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, rotateFailure, lineIndex);
                    return;
                }

                if (TryCompileSetAngle(
                        line,
                        localLookup,
                        globals,
                        sounds,
                        actors,
                        carryables,
                        spells,
                        factions,
                        actorLocals,
                        dialogues,
                        explicitRefTargets,
                        ambiguousExplicitRefTargets,
                        explicitContentTargets,
                        instructions,
                        ref stackDepth,
                        ref maxStack,
                        out string setAngleFailure))
                {
                    continue;
                }

                if (setAngleFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, setAngleFailure, lineIndex);
                    return;
                }

                if (TryCompileSetAtStart(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, out string setAtStartFailure))
                {
                    continue;
                }

                if (setAtStartFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, setAtStartFailure, lineIndex);
                    return;
                }

                if (TryCompileSetPos(
                        line,
                        localLookup,
                        globals,
                        sounds,
                        actors,
                        carryables,
                        spells,
                        factions,
                        actorLocals,
                        dialogues,
                        explicitRefTargets,
                        ambiguousExplicitRefTargets,
                        explicitContentTargets,
                        instructions,
                        ref stackDepth,
                        ref maxStack,
                        out string setPosFailure))
                {
                    continue;
                }

                if (setPosFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, setPosFailure, lineIndex);
                    return;
                }

                if (TryCompileMoveCommand(
                        line,
                        "moveworld",
                        MorrowindScriptOpcode.MoveWorld,
                        localLookup,
                        globals,
                        sounds,
                        actors,
                        carryables,
                        spells,
                        factions,
                        actorLocals,
                        dialogues,
                        explicitRefTargets,
                        ambiguousExplicitRefTargets,
                        explicitContentTargets,
                        instructions,
                        ref stackDepth,
                        ref maxStack,
                        out string moveWorldFailure))
                {
                    continue;
                }

                if (moveWorldFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, moveWorldFailure, lineIndex);
                    return;
                }

                if (TryCompileMoveCommand(
                        line,
                        "move",
                        MorrowindScriptOpcode.Move,
                        localLookup,
                        globals,
                        sounds,
                        actors,
                        carryables,
                        spells,
                        factions,
                        actorLocals,
                        dialogues,
                        explicitRefTargets,
                        ambiguousExplicitRefTargets,
                        explicitContentTargets,
                        instructions,
                        ref stackDepth,
                        ref maxStack,
                        out string moveFailure))
                {
                    continue;
                }

                if (moveFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, moveFailure, lineIndex);
                    return;
                }

                if (TryCompilePosition(
                        line,
                        localLookup,
                        globals,
                        sounds,
                        actors,
                        carryables,
                        spells,
                        factions,
                        actorLocals,
                        dialogues,
                        explicitRefTargets,
                        ambiguousExplicitRefTargets,
                        explicitContentTargets,
                        instructions,
                        ref stackDepth,
                        ref maxStack,
                        out string positionFailure))
                {
                    stackDepth = Math.Max(0, stackDepth - 1);
                    continue;
                }

                if (positionFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, positionFailure, lineIndex);
                    return;
                }

                if (TryCompilePositionCell(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, out string positionCellFailure))
                {
                    continue;
                }

                if (positionCellFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, positionCellFailure, lineIndex);
                    return;
                }

                if (TryCompilePlaceAtPC(line, placeAtSpawnables, instructions, out string placeAtPCFailure))
                {
                    continue;
                }

                if (placeAtPCFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, placeAtPCFailure, lineIndex);
                    return;
                }

                if (TryCompileAiWander(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, out string aiWanderFailure))
                {
                    continue;
                }

                if (aiWanderFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, aiWanderFailure, lineIndex);
                    return;
                }

                if (TryCompileAiTravel(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, out string aiTravelFailure))
                {
                    continue;
                }

                if (aiTravelFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, aiTravelFailure, lineIndex);
                    return;
                }

                if (TryCompileAiActivate(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, messages, instructions, out string aiActivateFailure))
                {
                    continue;
                }

                if (aiActivateFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, aiActivateFailure, lineIndex);
                    return;
                }

                if (TryCompileAiFollow(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, out string aiFollowFailure))
                {
                    continue;
                }

                if (aiFollowFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, aiFollowFailure, lineIndex);
                    return;
                }

                if (TryCompileAiEscort(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, out string aiEscortFailure))
                {
                    continue;
                }

                if (aiEscortFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, aiEscortFailure, lineIndex);
                    return;
                }

                if (TryCompileAiFollowCell(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, out string aiFollowCellFailure))
                {
                    continue;
                }

                if (aiFollowCellFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, aiFollowCellFailure, lineIndex);
                    return;
                }

                if (TryCompileStartCombat(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, out string startCombatFailure))
                {
                    continue;
                }

                if (startCombatFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, startCombatFailure, lineIndex);
                    return;
                }

                if (TryCompileStopCombat(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, out string stopCombatFailure))
                {
                    continue;
                }

                if (stopCombatFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, stopCombatFailure, lineIndex);
                    return;
                }

                if (TryCompileMovementFlagCommand(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, out string movementFlagFailure))
                {
                    continue;
                }

                if (movementFlagFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, movementFlagFailure, lineIndex);
                    return;
                }

                if (TryCompileActorAiSetting(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, out string actorAiSettingFailure))
                {
                    continue;
                }

                if (actorAiSettingFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, actorAiSettingFailure, lineIndex);
                    return;
                }

                if (TryCompileDisposition(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, out string dispositionFailure))
                {
                    continue;
                }

                if (dispositionFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, dispositionFailure, lineIndex);
                    return;
                }

                if (TryCompileSetHealth(
                        line,
                        localLookup,
                        globals,
                        sounds,
                        actors,
                        carryables,
                        spells,
                        factions,
                        actorLocals,
                        dialogues,
                        explicitRefTargets,
                        ambiguousExplicitRefTargets,
                        explicitContentTargets,
                        instructions,
                        ref stackDepth,
                        ref maxStack,
                        out string setHealthFailure))
                {
                    stackDepth = Math.Max(0, stackDepth - 1);
                    continue;
                }

                if (setHealthFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, setHealthFailure, lineIndex);
                    return;
                }

                if (TryCompileResurrect(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, out string resurrectFailure))
                {
                    continue;
                }

                if (resurrectFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, resurrectFailure, lineIndex);
                    return;
                }

                if (TryCompileHurtStandingActor(
                        line,
                        localLookup,
                        globals,
                        sounds,
                        actors,
                        carryables,
                        spells,
                        factions,
                        actorLocals,
                        dialogues,
                        explicitRefTargets,
                        ambiguousExplicitRefTargets,
                        explicitContentTargets,
                        instructions,
                        ref stackDepth,
                        ref maxStack,
                        out string hurtStandingActorFailure))
                {
                    stackDepth = Math.Max(0, stackDepth - 1);
                    continue;
                }

                if (hurtStandingActorFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, hurtStandingActorFailure, lineIndex);
                    return;
                }

                if (TryCompileAnimationGroupCommand(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, messages, instructions, out string animationGroupFailure))
                {
                    continue;
                }

                if (animationGroupFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, animationGroupFailure, lineIndex);
                    return;
                }

                if (TryCompileFallCommand(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, out string fallFailure))
                {
                    continue;
                }

                if (fallFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, fallFailure, lineIndex);
                    return;
                }

                if (TryCompileActorSpellCommand(line, spells, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, out string actorSpellFailure))
                {
                    continue;
                }

                if (actorSpellFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, actorSpellFailure, lineIndex);
                    return;
                }

                if (TryCompileCast(line, spells, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, out string castFailure))
                {
                    continue;
                }

                if (castFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, castFailure, lineIndex);
                    return;
                }

                if (TryCompileForceGreeting(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, out string forceGreetingFailure))
                {
                    continue;
                }

                if (forceGreetingFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, forceGreetingFailure, lineIndex);
                    return;
                }

                if (TryCompileInventoryMutation(
                        line,
                        localLookup,
                        globals,
                        sounds,
                        actors,
                        carryables,
                        spells,
                        factions,
                        actorLocals,
                        dialogues,
                        explicitRefTargets,
                        ambiguousExplicitRefTargets,
                        explicitContentTargets,
                        instructions,
                        ref stackDepth,
                        ref maxStack,
                        out string inventoryFailure))
                {
                    continue;
                }

                if (inventoryFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, inventoryFailure, lineIndex);
                    return;
                }

                if (TryCompileMessageBox(
                        line,
                        localLookup,
                        globals,
                        sounds,
                        actors,
                        carryables,
                        spells,
                        factions,
                        actorLocals,
                        dialogues,
                        explicitRefTargets,
                        ambiguousExplicitRefTargets,
                        explicitContentTargets,
                        messages,
                        instructions,
                        ref stackDepth,
                        ref maxStack,
                        out string messageBoxFailure))
                {
                    continue;
                }

                if (messageBoxFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, messageBoxFailure, lineIndex);
                    return;
                }

                if (TryCompileShowRestMenu(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, out string showRestMenuFailure))
                {
                    continue;
                }

                if (showRestMenuFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, showRestMenuFailure, lineIndex);
                    return;
                }

                if (TryCompileShowMap(line, messages, instructions, out string showMapFailure))
                {
                    continue;
                }

                if (showMapFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, showMapFailure, lineIndex);
                    return;
                }

                if (TryCompileScreenFade(line, instructions, out string screenFadeFailure))
                {
                    continue;
                }

                if (screenFadeFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, screenFadeFailure, lineIndex);
                    return;
                }

                if (TryCompileShellControl(line, instructions, out string shellControlFailure))
                {
                    continue;
                }

                if (shellControlFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, shellControlFailure, lineIndex);
                    return;
                }

                if (TryCompileChangeWeather(line, regions, instructions, out string changeWeatherFailure))
                {
                    continue;
                }

                if (changeWeatherFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, changeWeatherFailure, lineIndex);
                    return;
                }

                if (TryCompileModRegion(line, regions, instructions, out string modRegionFailure))
                {
                    continue;
                }

                if (modRegionFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, modRegionFailure, lineIndex);
                    return;
                }

                if (TryCompilePlayBink(line, messages, instructions, out string playBinkFailure))
                {
                    continue;
                }

                if (playBinkFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, playBinkFailure, lineIndex);
                    return;
                }

                if (TryCompileStreamMusic(line, musicTracks, messages, instructions, out string streamMusicFailure))
                {
                    continue;
                }

                if (streamMusicFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, streamMusicFailure, lineIndex);
                    return;
                }

                if (TryCompileWakeUpPC(line, instructions, out string wakeUpPCFailure))
                {
                    continue;
                }

                if (wakeUpPCFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, wakeUpPCFailure, lineIndex);
                    return;
                }

                if (TryCompileSay(line, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, messages, instructions, out string sayFailure))
                {
                    continue;
                }

                if (sayFailure != null)
                {
                    DisableUnsupported(script, scriptIndex, firstInstruction, firstLocal, instructions, locals, programs, sayFailure, lineIndex);
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

            programs.Add(new MorrowindScriptProgramDef
            {
                Id = script.Id,
                SourceScriptIndex = scriptIndex,
                Status = (byte)MorrowindScriptProgramStatus.DisabledUnsupported,
                DisabledReason = $"line {lineIndex + 1}: {reason}",
                FirstInstructionIndex = firstInstruction,
                InstructionCount = 0,
                FirstLocalIndex = locals.Count == firstLocal ? -1 : firstLocal,
                LocalCount = locals.Count - firstLocal,
                MaxStack = 1,
            });
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
            if (!TryReadLocalDeclaration(line, out string name, out byte valueKind))
                return false;

            if (lookup.ContainsKey(name))
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
            Dictionary<string, int> actorLookup,
            Dictionary<string, ContentReference> carryableLookup,
            Dictionary<string, int> scriptLookup,
            Dictionary<string, SpellDefHandle> spellLookup,
            Dictionary<string, int> factionLookup,
            Dictionary<string, ActorLocalCompileInfo> actorLocalLookup,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            List<IfBlockState> ifStack,
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

            if (!TryCompileCondition(
                    condition,
                    localLookup,
                    globalLookup,
                    soundLookup,
                    actorLookup,
                    carryableLookup,
                    scriptLookup,
                    spellLookup,
                    factionLookup,
                    actorLocalLookup,
                    dialogueLookup,
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
                    instructions,
                    ref stackDepth,
                    ref maxStack,
                    out failure))
                return false;

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.JumpIfZero,
                Int0 = 0,
            });
            stackDepth = Math.Max(0, stackDepth - 1);
            ifStack.Add(new IfBlockState
            {
                PendingFalseJump = instructions.Count - 1,
                EndJumps = new List<int>(),
            });
            return true;
        }

        static bool TryCompileElseIf(
            string line,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, int> actorLookup,
            Dictionary<string, ContentReference> carryableLookup,
            Dictionary<string, int> scriptLookup,
            Dictionary<string, SpellDefHandle> spellLookup,
            Dictionary<string, int> factionLookup,
            Dictionary<string, ActorLocalCompileInfo> actorLocalLookup,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            List<IfBlockState> ifStack,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            if (!StartsWithCommand(line, "elseif"))
                return false;

            if (ifStack.Count == 0)
                return TryCompileIf(
                    "if" + line.Substring("elseif".Length),
                    localLookup,
                    globalLookup,
                    soundLookup,
                    actorLookup,
                    carryableLookup,
                    scriptLookup,
                    spellLookup,
                    factionLookup,
                    actorLocalLookup,
                    dialogueLookup,
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
                    instructions,
                    ifStack,
                    ref stackDepth,
                    ref maxStack,
                    out failure);

            int frameIndex = ifStack.Count - 1;
            var frame = ifStack[frameIndex];
            if (frame.HasElse)
            {
                failure = "elseif after else is not valid.";
                return false;
            }

            string condition = TrimEnclosingParentheses(line.Substring("elseif".Length).Trim());
            if (string.IsNullOrWhiteSpace(condition))
            {
                failure = "elseif statement is missing a condition.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.Jump,
                Int0 = 0,
            });
            frame.EndJumps.Add(instructions.Count - 1);
            PatchJump(instructions, frame.PendingFalseJump, instructions.Count);

            if (!TryCompileCondition(
                    condition,
                    localLookup,
                    globalLookup,
                    soundLookup,
                    actorLookup,
                    carryableLookup,
                    scriptLookup,
                    spellLookup,
                    factionLookup,
                    actorLocalLookup,
                    dialogueLookup,
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
                    instructions,
                    ref stackDepth,
                    ref maxStack,
                    out failure))
                return false;

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.JumpIfZero,
                Int0 = 0,
            });
            stackDepth = Math.Max(0, stackDepth - 1);
            frame.PendingFalseJump = instructions.Count - 1;
            ifStack[frameIndex] = frame;
            return true;
        }

        static bool TryCompileElse(
            string line,
            List<MorrowindScriptInstructionDef> instructions,
            List<IfBlockState> ifStack,
            out string failure)
        {
            failure = null;
            if (!StartsWithCommand(line, "else"))
                return false;

            if (ifStack.Count == 0)
            {
                return true;
            }

            int frameIndex = ifStack.Count - 1;
            var frame = ifStack[frameIndex];
            if (frame.HasElse)
            {
                failure = "duplicate else in if block.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.Jump,
                Int0 = 0,
            });
            frame.EndJumps.Add(instructions.Count - 1);
            PatchJump(instructions, frame.PendingFalseJump, instructions.Count);
            frame.PendingFalseJump = -1;
            frame.HasElse = true;
            ifStack[frameIndex] = frame;
            return true;
        }

        static bool TryCompileEndif(
            string line,
            List<MorrowindScriptInstructionDef> instructions,
            List<IfBlockState> ifStack,
            out string failure)
        {
            failure = null;
            if (!StartsWithCommand(line, "endif"))
                return false;

            if (ifStack.Count == 0)
            {
                return true;
            }

            int frameIndex = ifStack.Count - 1;
            var frame = ifStack[frameIndex];
            ifStack.RemoveAt(frameIndex);

            if (frame.PendingFalseJump >= 0)
                PatchJump(instructions, frame.PendingFalseJump, instructions.Count);

            for (int i = 0; i < frame.EndJumps.Count; i++)
                PatchJump(instructions, frame.EndJumps[i], instructions.Count);

            return true;
        }

        static void PatchJump(List<MorrowindScriptInstructionDef> instructions, int jumpIndex, int targetIndex)
        {
            var jump = instructions[jumpIndex];
            jump.Int0 = targetIndex - jumpIndex - 1;
            instructions[jumpIndex] = jump;
        }

        static bool TryCompileCondition(
            string condition,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, int> actorLookup,
            Dictionary<string, ContentReference> carryableLookup,
            Dictionary<string, int> scriptLookup,
            Dictionary<string, SpellDefHandle> spellLookup,
            Dictionary<string, int> factionLookup,
            Dictionary<string, ActorLocalCompileInfo> actorLocalLookup,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            if (condition.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(condition, out string explicitTargetId, out string explicitCondition))
                {
                    failure = $"Invalid explicit reference condition '{condition}'.";
                    return false;
                }

                MorrowindScriptRefTargetMode targetMode;
                uint targetPlacedRefId = 0u;
                if (ContentId.NormalizeId(explicitTargetId).Equals("player", StringComparison.OrdinalIgnoreCase))
                {
                    targetMode = MorrowindScriptRefTargetMode.Player;
                }
                else
                {
                    if (!TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetPlacedRefId, out failure))
                        return false;
                }

                return TryCompileConditionCore(
                    explicitCondition,
                    localLookup,
                    globalLookup,
                    soundLookup,
                    actorLookup,
                    carryableLookup,
                    scriptLookup,
                    spellLookup,
                    factionLookup,
                    actorLocalLookup,
                    dialogueLookup,
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
                    targetMode,
                    targetPlacedRefId,
                    instructions,
                    ref stackDepth,
                    ref maxStack,
                    out failure);
            }

            return TryCompileConditionCore(
                condition,
                localLookup,
                globalLookup,
                soundLookup,
                actorLookup,
                carryableLookup,
                scriptLookup,
                spellLookup,
                factionLookup,
                actorLocalLookup,
                dialogueLookup,
                explicitRefTargets,
                ambiguousExplicitRefTargets,
                explicitContentTargets,
                MorrowindScriptRefTargetMode.Self,
                0u,
                instructions,
                ref stackDepth,
                ref maxStack,
                out failure);
        }

        static bool TryCompileConditionCore(
            string condition,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, int> actorLookup,
            Dictionary<string, ContentReference> carryableLookup,
            Dictionary<string, int> scriptLookup,
            Dictionary<string, SpellDefHandle> spellLookup,
            Dictionary<string, int> factionLookup,
            Dictionary<string, ActorLocalCompileInfo> actorLocalLookup,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            MorrowindScriptRefTargetMode refTargetMode,
            uint refTargetPlacedRefId,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            string[] tokens = SplitConditionTokens(condition);
            if (tokens.Length == 0)
            {
                failure = "if statement is missing a condition.";
                return false;
            }

            int comparisonIndex = FindComparisonToken(tokens);
            if (comparisonIndex < 0
                && TryCompileScriptRunningExpression(tokens, scriptLookup, instructions, ref stackDepth, ref maxStack, out failure))
            {
                return true;
            }
            if (failure != null)
                return false;

            if (comparisonIndex < 0)
                return TryCompileArithmeticTokensForTarget(tokens, 0, tokens.Length, localLookup, globalLookup, soundLookup, actorLookup, carryableLookup, spellLookup, factionLookup, actorLocalLookup, dialogueLookup, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, refTargetMode, refTargetPlacedRefId, instructions, ref stackDepth, ref maxStack, out failure);

            if (FindComparisonToken(tokens, comparisonIndex + 1) >= 0)
            {
                failure = $"Condition has more than one comparison operator: '{condition}'.";
                return false;
            }

            if (!TryCompileConditionOperand(tokens, 0, comparisonIndex, scriptLookup, localLookup, globalLookup, soundLookup, actorLookup, carryableLookup, spellLookup, factionLookup, actorLocalLookup, dialogueLookup, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, refTargetMode, refTargetPlacedRefId, instructions, ref stackDepth, ref maxStack, out failure))
                return false;

            if (!TryCompileConditionOperand(
                    tokens,
                    comparisonIndex + 1,
                    tokens.Length - comparisonIndex - 1,
                    scriptLookup,
                    localLookup,
                    globalLookup,
                    soundLookup,
                    actorLookup,
                    carryableLookup,
                    spellLookup,
                    factionLookup,
                    actorLocalLookup,
                    dialogueLookup,
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
                    MorrowindScriptRefTargetMode.Self,
                    0u,
                    instructions,
                    ref stackDepth,
                    ref maxStack,
                    out failure))
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

        static bool TryReadLocalDeclaration(string line, out string name, out byte valueKind)
        {
            name = null;
            valueKind = 0;

            int index = 0;
            SkipWhitespaceAndDeclarationSeparators(line, ref index);
            string type = ReadDeclarationNameToken(line, ref index);
            if (string.IsNullOrEmpty(type))
                return false;

            valueKind = type.Equals("float", StringComparison.OrdinalIgnoreCase)
                ? (byte)MorrowindScriptValueKind.Float
                : (type.Equals("short", StringComparison.OrdinalIgnoreCase) || type.Equals("long", StringComparison.OrdinalIgnoreCase))
                    ? (byte)MorrowindScriptValueKind.Integer
                    : (byte)0;
            if (valueKind == 0)
                return false;

            SkipWhitespaceAndDeclarationSeparators(line, ref index);
            name = ReadDeclarationNameToken(line, ref index);
            return !string.IsNullOrWhiteSpace(name);
        }

        static void SkipWhitespaceAndDeclarationSeparators(string line, ref int index)
        {
            while (index < line.Length && (char.IsWhiteSpace(line[index]) || line[index] == ':'))
                index++;
        }

        static string ReadDeclarationNameToken(string line, ref int index)
        {
            if (index >= line.Length)
                return null;

            if (line[index] == '"')
            {
                int start = ++index;
                while (index < line.Length && line[index] != '"')
                    index++;

                string quoted = line.Substring(start, index - start);
                if (index < line.Length)
                    index++;
                return quoted;
            }

            int tokenStart = index;
            while (index < line.Length && IsMorrowindScriptNameCharacter(line[index]))
                index++;

            return index == tokenStart ? null : line.Substring(tokenStart, index - tokenStart);
        }

        static bool IsMorrowindScriptNameCharacter(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '`' || c == '\'';
        }

        static bool TryCompileConditionOperand(
            string[] tokens,
            int start,
            int count,
            Dictionary<string, int> scriptLookup,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, int> actorLookup,
            Dictionary<string, ContentReference> carryableLookup,
            Dictionary<string, SpellDefHandle> spellLookup,
            Dictionary<string, int> factionLookup,
            Dictionary<string, ActorLocalCompileInfo> actorLocalLookup,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            MorrowindScriptRefTargetMode refTargetMode,
            uint refTargetPlacedRefId,
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

            if (TryCompileScriptRunningExpression(SliceTokens(tokens, start, count), scriptLookup, instructions, ref stackDepth, ref maxStack, out failure))
                return true;
            if (failure != null)
                return false;

            return TryCompileArithmeticTokensForTarget(tokens, start, count, localLookup, globalLookup, soundLookup, actorLookup, carryableLookup, spellLookup, factionLookup, actorLocalLookup, dialogueLookup, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, refTargetMode, refTargetPlacedRefId, instructions, ref stackDepth, ref maxStack, out failure);
        }

        static string[] SliceTokens(string[] tokens, int start, int count)
        {
            var result = new string[count];
            Array.Copy(tokens, start, result, 0, count);
            return result;
        }

        static bool TryCompileScriptRunningExpression(
            string[] tokens,
            Dictionary<string, int> scriptLookup,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            if (tokens.Length < 2 || !tokens[0].Equals("scriptrunning", StringComparison.OrdinalIgnoreCase))
                return false;

            string scriptId = string.Join(" ", tokens, 1, tokens.Length - 1).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(scriptId))
            {
                failure = "ScriptRunning requires one script id.";
                return false;
            }

            if (scriptLookup == null || !scriptLookup.TryGetValue(ContentId.NormalizeId(scriptId), out int programIndex))
            {
                failure = $"ScriptRunning references unknown script '{scriptId}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.ScriptRunning,
                Int0 = programIndex,
            });
            stackDepth++;
            maxStack = Math.Max(maxStack, stackDepth);
            return true;
        }

        static bool TryCompileExpression(
            string[] tokens,
            int start,
            int count,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, int> actorLookup,
            Dictionary<string, ContentReference> carryableLookup,
            Dictionary<string, SpellDefHandle> spellLookup,
            Dictionary<string, int> factionLookup,
            Dictionary<string, ActorLocalCompileInfo> actorLocalLookup,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            MorrowindScriptRefTargetMode refTargetMode,
            uint refTargetPlacedRefId,
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

            if (count == 1
                && tokens[start].Equals("xbox", StringComparison.OrdinalIgnoreCase))
            {
                EmitPushLiteral(instructions, 0, 0f, false, ref stackDepth, ref maxStack);
                return true;
            }

            if (count >= 2
                && tokens[start].Equals("random", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryCompileArithmeticTokens(
                        tokens,
                        start + 1,
                        count - 1,
                        localLookup,
                        globalLookup,
                        soundLookup,
                        actorLookup,
                        carryableLookup,
                        spellLookup,
                        factionLookup,
                        actorLocalLookup,
                        dialogueLookup,
                        explicitRefTargets,
                        ambiguousExplicitRefTargets,
                        explicitContentTargets,
                        instructions,
                        ref stackDepth,
                        ref maxStack,
                        out failure))
                {
                    return false;
                }

                instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.Random });
                return true;
            }

            if (count == 1
                && tokens[start].Equals("getdisabled", StringComparison.OrdinalIgnoreCase))
            {
                if (refTargetMode == MorrowindScriptRefTargetMode.Player)
                {
                    failure = "GetDisabled does not support explicit Player target in MWScript V1.";
                    return false;
                }

                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)MorrowindScriptOpcode.GetDisabled,
                    Operand0 = (byte)refTargetMode,
                    Int0 = unchecked((int)refTargetPlacedRefId),
                });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (count >= 2
                && tokens[start].Equals("getitemcount", StringComparison.OrdinalIgnoreCase))
            {
                string itemId = NormalizeGoldId(string.Join(" ", tokens, start + 1, count - 1).Trim().Trim('"'));
                if (string.IsNullOrWhiteSpace(itemId))
                {
                    failure = "GetItemCount requires one item id.";
                    return false;
                }

                if (!carryableLookup.TryGetValue(ContentId.NormalizeId(itemId), out var content) || !content.IsValid)
                {
                    failure = $"GetItemCount references unknown carryable '{itemId}'.";
                    return false;
                }

                if (content.Kind == ContentReferenceKind.LeveledItem)
                {
                    failure = $"GetItemCount cannot query leveled item list '{itemId}' in MWScript V1.";
                    return false;
                }

                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)MorrowindScriptOpcode.GetItemCount,
                    Operand0 = (byte)refTargetMode,
                    Operand1 = (short)content.Kind,
                    Int0 = unchecked((int)refTargetPlacedRefId),
                    Int1 = content.HandleValue,
                });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (refTargetMode == MorrowindScriptRefTargetMode.Player)
            {
                if (count >= 2
                    && tokens[start].Equals("hassoulgem", StringComparison.OrdinalIgnoreCase))
                {
                    string soulId = string.Join(" ", tokens, start + 1, count - 1).Trim().Trim('"');
                    if (string.IsNullOrWhiteSpace(soulId))
                    {
                        failure = "HasSoulGem requires one creature id.";
                        return false;
                    }

                    if (!actorLookup.TryGetValue(ContentId.NormalizeId(soulId), out int actorIndex))
                    {
                        failure = $"HasSoulGem references unknown actor '{soulId}'.";
                        return false;
                    }

                    instructions.Add(new MorrowindScriptInstructionDef
                    {
                        Opcode = (byte)MorrowindScriptOpcode.HasSoulGem,
                        Int0 = ActorDefHandle.FromIndex(actorIndex).Value,
                    });
                    stackDepth++;
                    maxStack = Math.Max(maxStack, stackDepth);
                    return true;
                }

                if (count >= 2
                    && tokens[start].Equals("getitemcount", StringComparison.OrdinalIgnoreCase))
                {
                    string itemId = NormalizeGoldId(string.Join(" ", tokens, start + 1, count - 1).Trim().Trim('"'));
                    if (string.IsNullOrWhiteSpace(itemId))
                    {
                        failure = "GetItemCount requires one item id.";
                        return false;
                    }

                    if (!carryableLookup.TryGetValue(ContentId.NormalizeId(itemId), out var content) || !content.IsValid)
                    {
                        failure = $"GetItemCount references unknown carryable '{itemId}'.";
                        return false;
                    }

                    if (content.Kind == ContentReferenceKind.LeveledItem)
                    {
                        failure = $"GetItemCount cannot query leveled item list '{itemId}' in MWScript V1.";
                        return false;
                    }

                    instructions.Add(new MorrowindScriptInstructionDef
                    {
                        Opcode = (byte)MorrowindScriptOpcode.GetPlayerItemCount,
                        Operand0 = (byte)content.Kind,
                        Int0 = content.HandleValue,
                    });
                    stackDepth++;
                    maxStack = Math.Max(maxStack, stackDepth);
                    return true;
                }

                if (count >= 2
                    && tokens[start].Equals("getspell", StringComparison.OrdinalIgnoreCase))
                {
                    string spellId = string.Join(" ", tokens, start + 1, count - 1).Trim().Trim('"');
                    if (string.IsNullOrWhiteSpace(spellId))
                    {
                        failure = "GetSpell requires one spell id.";
                        return false;
                    }

                    if (!spellLookup.TryGetValue(ContentId.NormalizeId(spellId), out var spell) || !spell.IsValid)
                    {
                        failure = $"GetSpell references unknown spell '{spellId}'.";
                        return false;
                    }

                    instructions.Add(new MorrowindScriptInstructionDef
                    {
                        Opcode = (byte)MorrowindScriptOpcode.GetPlayerSpell,
                        Int0 = spell.Value,
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

                if (TryCompileGetActorVitalExpression(
                        tokens,
                        start,
                        count,
                        MorrowindScriptRefTargetMode.Player,
                        0u,
                        instructions,
                        ref stackDepth,
                        ref maxStack))
                {
                    return true;
                }

                if (TryCompileGetActorAttributeExpression(
                        tokens,
                        start,
                        count,
                        MorrowindScriptRefTargetMode.Player,
                        0u,
                        instructions,
                        ref stackDepth,
                        ref maxStack))
                {
                    return true;
                }

                if (TryCompileGetEffectExpression(
                        tokens,
                        start,
                        count,
                        MorrowindScriptRefTargetMode.Player,
                        0u,
                        spellLookup,
                        instructions,
                        ref stackDepth,
                        ref maxStack,
                        out failure))
                {
                    return true;
                }
                if (failure != null)
                    return false;

                if (TryCompileGetActorAiSettingExpression(
                        tokens,
                        start,
                        count,
                        MorrowindScriptRefTargetMode.Player,
                        0u,
                        instructions,
                        ref stackDepth,
                        ref maxStack))
                {
                    return true;
                }

                if (TryCompileGetDispositionExpression(tokens, start, count, MorrowindScriptRefTargetMode.Player, 0u, instructions, ref stackDepth, ref maxStack))
                    return true;

                if (count == 1 && TryMapDiseaseOpcode(tokens[start], out MorrowindScriptOpcode playerDiseaseOpcode))
                {
                    instructions.Add(new MorrowindScriptInstructionDef
                    {
                        Opcode = (byte)playerDiseaseOpcode,
                        Operand0 = (byte)MorrowindScriptRefTargetMode.Player,
                    });
                    stackDepth++;
                    maxStack = Math.Max(maxStack, stackDepth);
                    return true;
                }

                if (TryCompileGetRaceExpression(
                        tokens,
                        start,
                        count,
                        MorrowindScriptRefTargetMode.Player,
                        0u,
                        instructions,
                        ref stackDepth,
                        ref maxStack,
                        out failure))
                {
                    return true;
                }
                if (failure != null)
                    return false;

                if (TryCompileSayDoneExpression(tokens, start, count, MorrowindScriptRefTargetMode.Player, 0u, instructions, ref stackDepth, ref maxStack))
                    return true;

                if (TryCompileGetDistanceExpression(tokens, start, count, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, refTargetMode, refTargetPlacedRefId, instructions, ref stackDepth, ref maxStack, out failure))
                    return true;
                if (failure != null)
                    return false;

                if (TryCompileGetPosExpression(tokens, start, count, refTargetMode, refTargetPlacedRefId, instructions, ref stackDepth, ref maxStack, out failure))
                    return true;
                if (failure != null)
                    return false;

                if (TryCompileGetTargetExpression(tokens, start, count, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, refTargetMode, refTargetPlacedRefId, instructions, ref stackDepth, ref maxStack, out failure))
                    return true;
                if (failure != null)
                    return false;

                if (TryCompileActorLineOfSightExpression(tokens, start, count, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, refTargetMode, refTargetPlacedRefId, instructions, ref stackDepth, ref maxStack, out failure))
                    return true;
                if (failure != null)
                    return false;

                if (TryCompileActorEventExpression(tokens, start, count, carryableLookup, MorrowindScriptRefTargetMode.Player, 0u, instructions, ref stackDepth, ref maxStack, out failure))
                    return true;
                if (failure != null)
                    return false;

                if (TryCompileGetPCRankExpression(tokens, start, count, factionLookup, instructions, ref stackDepth, ref maxStack, out failure))
                    return true;
                if (failure != null)
                    return false;

                if (TryCompilePlayerFactionStatusExpression(tokens, start, count, factionLookup, instructions, ref stackDepth, ref maxStack, out failure))
                    return true;
                if (failure != null)
                    return false;

                if (TryCompileGetPlayerSkillExpression(tokens, start, count, instructions, ref stackDepth, ref maxStack))
                    return true;

                failure = $"Explicit Player expression '{string.Join(" ", tokens, start, count)}' is not supported in MWScript V1.";
                return false;
            }

            if (count == 1 && TryMapDiseaseOpcode(tokens[start], out MorrowindScriptOpcode diseaseOpcode))
            {
                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)diseaseOpcode,
                    Operand0 = (byte)refTargetMode,
                    Int0 = unchecked((int)refTargetPlacedRefId),
                });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (TryCompileGetRaceExpression(
                    tokens,
                    start,
                    count,
                    refTargetMode,
                    refTargetPlacedRefId,
                    instructions,
                    ref stackDepth,
                    ref maxStack,
                    out failure))
            {
                return true;
            }
            if (failure != null)
                return false;

            if (TryCompileSayDoneExpression(tokens, start, count, refTargetMode, refTargetPlacedRefId, instructions, ref stackDepth, ref maxStack))
                return true;

            if (TryCompileGetActorVitalExpression(
                    tokens,
                    start,
                    count,
                    refTargetMode,
                    refTargetPlacedRefId,
                    instructions,
                    ref stackDepth,
                    ref maxStack))
            {
                return true;
            }

            if (TryCompileGetActorAttributeExpression(
                    tokens,
                    start,
                    count,
                    refTargetMode,
                    refTargetPlacedRefId,
                    instructions,
                    ref stackDepth,
                    ref maxStack))
            {
                return true;
            }

            if (TryCompileGetEffectExpression(
                    tokens,
                    start,
                    count,
                    refTargetMode,
                    refTargetPlacedRefId,
                    spellLookup,
                    instructions,
                    ref stackDepth,
                    ref maxStack,
                    out failure))
            {
                return true;
            }
            if (failure != null)
                return false;

            if (TryCompileGetActorAiSettingExpression(
                    tokens,
                    start,
                    count,
                    refTargetMode,
                    refTargetPlacedRefId,
                    instructions,
                    ref stackDepth,
                    ref maxStack))
            {
                return true;
            }

            if (TryCompileGetDispositionExpression(tokens, start, count, refTargetMode, refTargetPlacedRefId, instructions, ref stackDepth, ref maxStack))
                return true;

            if (count >= 2
                && tokens[start].Equals("getspell", StringComparison.OrdinalIgnoreCase))
            {
                string spellId = string.Join(" ", tokens, start + 1, count - 1).Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(spellId))
                {
                    failure = "GetSpell requires one spell id.";
                    return false;
                }

                if (!spellLookup.TryGetValue(ContentId.NormalizeId(spellId), out var spell) || !spell.IsValid)
                {
                    failure = $"GetSpell references unknown spell '{spellId}'.";
                    return false;
                }

                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)MorrowindScriptOpcode.GetSpell,
                    Operand0 = (byte)refTargetMode,
                    Int0 = unchecked((int)refTargetPlacedRefId),
                    Int1 = spell.Value,
                });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (count == 1
                && tokens[start].Equals("getcurrentaipackage", StringComparison.OrdinalIgnoreCase))
            {
                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)MorrowindScriptOpcode.GetCurrentAiPackage,
                    Operand0 = (byte)refTargetMode,
                    Int0 = unchecked((int)refTargetPlacedRefId),
                });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (count == 1
                && tokens[start].Equals("getaipackagedone", StringComparison.OrdinalIgnoreCase))
            {
                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)MorrowindScriptOpcode.GetAiPackageDone,
                    Operand0 = (byte)refTargetMode,
                    Int0 = unchecked((int)refTargetPlacedRefId),
                });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (count == 1
                && tokens[start].Equals("ondeath", StringComparison.OrdinalIgnoreCase))
            {
                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)MorrowindScriptOpcode.GetOnDeath,
                    Operand0 = (byte)refTargetMode,
                    Int0 = unchecked((int)refTargetPlacedRefId),
                });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (count == 2
                && tokens[start].Equals("getangle", StringComparison.OrdinalIgnoreCase))
            {
                string axis = NormalizeToken(tokens[start + 1]).Trim('"');
                if (!TryMapRotateAxis(axis, out byte axisIndex))
                {
                    failure = $"GetAngle axis must be X, Y, or Z in MWScript V1: '{string.Join(" ", tokens, start, count)}'.";
                    return false;
                }

                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)MorrowindScriptOpcode.GetAngle,
                    Operand0 = (byte)refTargetMode,
                    Operand1 = axisIndex,
                    Int0 = unchecked((int)refTargetPlacedRefId),
                });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (count == 2
                && tokens[start].Equals("getstartingangle", StringComparison.OrdinalIgnoreCase))
            {
                string axis = NormalizeToken(tokens[start + 1]).Trim('"');
                if (!TryMapRotateAxis(axis, out byte axisIndex))
                {
                    failure = $"GetStartingAngle axis must be X, Y, or Z in MWScript V1: '{string.Join(" ", tokens, start, count)}'.";
                    return false;
                }

                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)MorrowindScriptOpcode.GetStartingAngle,
                    Operand0 = (byte)refTargetMode,
                    Operand1 = axisIndex,
                    Int0 = unchecked((int)refTargetPlacedRefId),
                });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (count == 1
                && tokens[start].Equals("getlocked", StringComparison.OrdinalIgnoreCase))
            {
                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)MorrowindScriptOpcode.GetLocked,
                    Operand0 = (byte)refTargetMode,
                    Int0 = unchecked((int)refTargetPlacedRefId),
                });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (count == 1
                && tokens[start].Equals("getstandingpc", StringComparison.OrdinalIgnoreCase))
            {
                if (refTargetMode == MorrowindScriptRefTargetMode.Player)
                {
                    failure = $"GetStandingPC does not support explicit Player target in MWScript V1: '{string.Join(" ", tokens, start, count)}'.";
                    return false;
                }

                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)MorrowindScriptOpcode.GetStandingPC,
                    Operand0 = (byte)refTargetMode,
                    Int0 = unchecked((int)refTargetPlacedRefId),
                });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (TryCompileGetDistanceExpression(tokens, start, count, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, refTargetMode, refTargetPlacedRefId, instructions, ref stackDepth, ref maxStack, out failure))
                return true;
            if (failure != null)
                return false;

            if (TryCompileGetPosExpression(tokens, start, count, refTargetMode, refTargetPlacedRefId, instructions, ref stackDepth, ref maxStack, out failure))
                return true;
            if (failure != null)
                return false;

            if (TryCompileGetTargetExpression(tokens, start, count, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, refTargetMode, refTargetPlacedRefId, instructions, ref stackDepth, ref maxStack, out failure))
                return true;
            if (failure != null)
                return false;

            if (TryCompileActorLineOfSightExpression(tokens, start, count, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, refTargetMode, refTargetPlacedRefId, instructions, ref stackDepth, ref maxStack, out failure))
                return true;
            if (failure != null)
                return false;

            if (TryCompileActorEventExpression(tokens, start, count, carryableLookup, refTargetMode, refTargetPlacedRefId, instructions, ref stackDepth, ref maxStack, out failure))
                return true;
            if (failure != null)
                return false;

            if (refTargetMode == MorrowindScriptRefTargetMode.PlacedRef)
            {
                failure = $"Explicit reference expression '{string.Join(" ", tokens, start, count)}' is not supported in MWScript V1.";
                return false;
            }

            if (TryCompileGetPCRankExpression(tokens, start, count, factionLookup, instructions, ref stackDepth, ref maxStack, out failure))
                return true;
            if (failure != null)
                return false;

            if (TryCompilePlayerFactionStatusExpression(tokens, start, count, factionLookup, instructions, ref stackDepth, ref maxStack, out failure))
                return true;
            if (failure != null)
                return false;

            if (TryCompileGetPlayerSkillExpression(tokens, start, count, instructions, ref stackDepth, ref maxStack))
                return true;

            if (count == 1
                && tokens[start].Equals("getpccrimelevel", StringComparison.OrdinalIgnoreCase))
            {
                instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.GetPCCrimeLevel });
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

            if (count >= 2
                && tokens[start].Equals("getpccell", StringComparison.OrdinalIgnoreCase))
            {
                string cellName = string.Join(" ", tokens, start + 1, count - 1).Trim();
                if (string.IsNullOrWhiteSpace(cellName))
                {
                    failure = "GetPCCell requires one cell name prefix.";
                    return false;
                }

                ulong hash = HashStringPrefix(cellName);
                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)MorrowindScriptOpcode.GetPCCell,
                    Int0 = cellName.Length,
                    Int1 = unchecked((int)(hash & 0xFFFFFFFFu)),
                    Int2 = unchecked((int)(hash >> 32)),
                });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (count >= 2
                && tokens[start].Equals("getdeadcount", StringComparison.OrdinalIgnoreCase))
            {
                string actorId = string.Join(" ", tokens, start + 1, count - 1).Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(actorId))
                {
                    failure = "GetDeadCount requires one actor id.";
                    return false;
                }

                if (!actorLookup.TryGetValue(ContentId.NormalizeId(actorId), out int actorIndex))
                {
                    failure = $"GetDeadCount references unknown actor '{actorId}'.";
                    return false;
                }

                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)MorrowindScriptOpcode.GetDeadCount,
                    Int0 = actorIndex,
                });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (count != 1)
            {
                if (refTargetMode == MorrowindScriptRefTargetMode.Self
                    && TryResolveActorLocalTarget(actorLocalLookup, string.Join(" ", tokens, start, count), out var actorLocal))
                {
                    instructions.Add(new MorrowindScriptInstructionDef
                    {
                        Opcode = (byte)MorrowindScriptOpcode.GetActorLocal,
                        Int0 = actorLocal.ActorHandleValue,
                        Int1 = actorLocal.LocalIndex,
                    });
                    stackDepth++;
                    maxStack = Math.Max(maxStack, stackDepth);
                    return true;
                }

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

            if (token.Equals("getcurrentweather", StringComparison.OrdinalIgnoreCase))
            {
                instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.GetCurrentWeather });
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

            if (token.Equals("getbuttonpressed", StringComparison.OrdinalIgnoreCase))
            {
                instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.GetButtonPressed });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (token.Equals("getpcsleep", StringComparison.OrdinalIgnoreCase))
            {
                instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.GetPCSleep });
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

            if (refTargetMode == MorrowindScriptRefTargetMode.Self
                && TryResolveActorLocalTarget(actorLocalLookup, token, out var actorLocalValue))
            {
                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)MorrowindScriptOpcode.GetActorLocal,
                    Int0 = actorLocalValue.ActorHandleValue,
                    Int1 = actorLocalValue.LocalIndex,
                });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            failure = $"Unsupported expression '{token}'.";
            return false;
        }

        static bool TryCompileGetPlayerSkillExpression(
            string[] tokens,
            int start,
            int count,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack)
        {
            if (count != 1
                || !MorrowindActorSkillTextUtility.TryResolveGetSkillExpression(tokens[start], out byte skill))
            {
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.GetPlayerSkill,
                Operand0 = skill,
            });
            stackDepth++;
            maxStack = Math.Max(maxStack, stackDepth);
            return true;
        }

        static bool TryCompileGetActorAttributeExpression(
            string[] tokens,
            int start,
            int count,
            MorrowindScriptRefTargetMode targetMode,
            uint targetPlacedRefId,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack)
        {
            if (count != 1
                || !MorrowindActorAttributeTextUtility.TryResolveGetAttributeExpression(tokens[start], out byte attribute))
            {
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.GetActorAttribute,
                Operand0 = (byte)targetMode,
                Operand1 = attribute,
                Int0 = unchecked((int)targetPlacedRefId),
            });
            stackDepth++;
            maxStack = Math.Max(maxStack, stackDepth);
            return true;
        }

        static bool TryCompileGetActorVitalExpression(
            string[] tokens,
            int start,
            int count,
            MorrowindScriptRefTargetMode targetMode,
            uint targetPlacedRefId,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack)
        {
            if (count != 1 || !TryResolveGetActorVitalExpression(tokens[start], out byte vitalKind))
                return false;

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.GetHealth,
                Operand0 = (byte)targetMode,
                Operand1 = vitalKind,
                Int0 = unchecked((int)targetPlacedRefId),
            });
            stackDepth++;
            maxStack = Math.Max(maxStack, stackDepth);
            return true;
        }

        static bool TryCompileGetEffectExpression(
            string[] tokens,
            int start,
            int count,
            MorrowindScriptRefTargetMode targetMode,
            uint targetPlacedRefId,
            Dictionary<string, SpellDefHandle> spellLookup,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            if (count < 1
                || (!tokens[start].Equals("geteffect", StringComparison.OrdinalIgnoreCase)
                    && !tokens[start].Equals("getspelleffects", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            bool spellEffects = tokens[start].Equals("getspelleffects", StringComparison.OrdinalIgnoreCase);
            if (count < 2)
            {
                failure = spellEffects
                    ? $"GetSpellEffects requires one spell id in MWScript V1: '{string.Join(" ", tokens, start, count)}'."
                    : $"GetEffect requires one magic effect GMST id in MWScript V1: '{string.Join(" ", tokens, start, count)}'.";
                return false;
            }

            if (spellEffects)
            {
                string spellId = string.Join(" ", tokens, start + 1, count - 1).Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(spellId))
                {
                    failure = $"GetSpellEffects requires one spell id in MWScript V1: '{string.Join(" ", tokens, start, count)}'.";
                    return false;
                }

                if (!spellLookup.TryGetValue(ContentId.NormalizeId(spellId), out var spellHandle) || !spellHandle.IsValid)
                {
                    failure = $"GetSpellEffects references unknown spell '{spellId}'.";
                    return false;
                }

                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)MorrowindScriptOpcode.GetSpellEffects,
                    Operand0 = (byte)targetMode,
                    Int0 = unchecked((int)targetPlacedRefId),
                    Int1 = spellHandle.Value,
                });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (count != 2)
            {
                failure = $"GetEffect requires one magic effect GMST id in MWScript V1: '{string.Join(" ", tokens, start, count)}'.";
                return false;
            }

            string effectId = NormalizeToken(tokens[start + 1]).Trim('"');
            if (!MorrowindMagicEffectTextUtility.TryResolveEffectId(effectId, out short effectIndex))
            {
                failure = $"GetEffect references unknown magic effect GMST id '{effectId}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.GetEffect,
                Operand0 = (byte)targetMode,
                Int0 = unchecked((int)targetPlacedRefId),
                Int1 = effectIndex,
            });
            stackDepth++;
            maxStack = Math.Max(maxStack, stackDepth);
            return true;
        }

        static bool TryCompileGetActorAiSettingExpression(
            string[] tokens,
            int start,
            int count,
            MorrowindScriptRefTargetMode targetMode,
            uint targetPlacedRefId,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack)
        {
            if (count != 1 || !TryResolveGetActorAiSettingExpression(tokens[start], out byte settingKind))
                return false;

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.GetActorAiSetting,
                Operand0 = (byte)targetMode,
                Operand1 = settingKind,
                Int0 = unchecked((int)targetPlacedRefId),
            });
            stackDepth++;
            maxStack = Math.Max(maxStack, stackDepth);
            return true;
        }

        static bool TryCompileGetDispositionExpression(
            string[] tokens,
            int start,
            int count,
            MorrowindScriptRefTargetMode targetMode,
            uint targetPlacedRefId,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack)
        {
            if (count != 1 || !tokens[start].Equals("getdisposition", StringComparison.OrdinalIgnoreCase))
                return false;

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.GetDisposition,
                Operand0 = (byte)targetMode,
                Int0 = unchecked((int)targetPlacedRefId),
            });
            stackDepth++;
            maxStack = Math.Max(maxStack, stackDepth);
            return true;
        }

        static bool TryCompileGetRaceExpression(
            string[] tokens,
            int start,
            int count,
            MorrowindScriptRefTargetMode targetMode,
            uint targetPlacedRefId,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            if (count < 2 || !tokens[start].Equals("getrace", StringComparison.OrdinalIgnoreCase))
                return false;

            string raceId = string.Join(" ", tokens, start + 1, count - 1).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(raceId))
            {
                failure = "GetRace requires one race id.";
                return false;
            }

            ulong raceHash = HashStringPrefix(ContentId.NormalizeId(raceId));
            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.GetRace,
                Operand0 = (byte)targetMode,
                Int0 = unchecked((int)targetPlacedRefId),
                Int1 = unchecked((int)(raceHash & 0xFFFFFFFFu)),
                Int2 = unchecked((int)(raceHash >> 32)),
            });
            stackDepth++;
            maxStack = Math.Max(maxStack, stackDepth);
            return true;
        }

        static bool TryResolveGetActorAiSettingExpression(string token, out byte settingKind)
        {
            settingKind = 0;
            string normalized = NormalizeToken(token);
            if (string.Equals(normalized, "gethello", StringComparison.OrdinalIgnoreCase))
                settingKind = (byte)MorrowindScriptActorAiSettingKind.Hello;
            else if (string.Equals(normalized, "getfight", StringComparison.OrdinalIgnoreCase))
                settingKind = (byte)MorrowindScriptActorAiSettingKind.Fight;
            else if (string.Equals(normalized, "getflee", StringComparison.OrdinalIgnoreCase))
                settingKind = (byte)MorrowindScriptActorAiSettingKind.Flee;
            else if (string.Equals(normalized, "getalarm", StringComparison.OrdinalIgnoreCase))
                settingKind = (byte)MorrowindScriptActorAiSettingKind.Alarm;

            return settingKind != 0;
        }

        static bool TryCompileSayDoneExpression(
            string[] tokens,
            int start,
            int count,
            MorrowindScriptRefTargetMode targetMode,
            uint targetPlacedRefId,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack)
        {
            if (count != 1 || !tokens[start].Equals("saydone", StringComparison.OrdinalIgnoreCase))
                return false;

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.SayDone,
                Operand0 = (byte)targetMode,
                Int0 = unchecked((int)targetPlacedRefId),
            });
            stackDepth++;
            maxStack = Math.Max(maxStack, stackDepth);
            return true;
        }

        static bool TryResolveGetActorVitalExpression(string token, out byte vitalKind)
        {
            vitalKind = 0;
            string normalized = NormalizeToken(token);
            if (string.Equals(normalized, "gethealth", StringComparison.OrdinalIgnoreCase))
                vitalKind = 1;
            else if (string.Equals(normalized, "getmagicka", StringComparison.OrdinalIgnoreCase))
                vitalKind = 2;
            else if (string.Equals(normalized, "getfatigue", StringComparison.OrdinalIgnoreCase))
                vitalKind = 3;

            return vitalKind != 0;
        }

        static bool TryCompilePlayerSkillCommand(
            string line,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, int> actorLookup,
            Dictionary<string, ContentReference> carryableLookup,
            Dictionary<string, SpellDefHandle> spellLookup,
            Dictionary<string, int> factionLookup,
            Dictionary<string, ActorLocalCompileInfo> actorLocalLookup,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid player skill command '{line}'.";
                    return false;
                }
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length == 0
                || !MorrowindActorSkillTextUtility.TryResolveSkillCommand(tokens[0], out byte skill, out byte mutation))
            {
                return false;
            }

            if (!TryResolveOptionalPlayerOrExplicitTarget(
                    explicitTargetId,
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
                    out var targetMode,
                    out uint targetRefKey,
                    out failure))
                return false;

            if (tokens.Length < 2)
            {
                failure = $"{tokens[0]} requires one value expression in MWScript V2: '{line}'.";
                return false;
            }

            string expressionText = string.Join(" ", tokens, 1, tokens.Length - 1);
            if (!TryCompileArithmeticExpression(
                    expressionText,
                    localLookup,
                    globalLookup,
                    soundLookup,
                    actorLookup,
                    carryableLookup,
                    spellLookup,
                    factionLookup,
                    actorLocalLookup,
                    dialogueLookup,
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
                    instructions,
                    ref stackDepth,
                    ref maxStack,
                    out failure))
            {
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.PlayerSkillMutation,
                Operand0 = skill,
                Operand1 = mutation,
                Int0 = 1,
            });
            return true;
        }

        static bool TryCompilePlayerAttributeCommand(
            string line,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, int> actorLookup,
            Dictionary<string, ContentReference> carryableLookup,
            Dictionary<string, SpellDefHandle> spellLookup,
            Dictionary<string, int> factionLookup,
            Dictionary<string, ActorLocalCompileInfo> actorLocalLookup,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid player attribute command '{line}'.";
                    return false;
                }
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length == 0
                || !MorrowindActorAttributeTextUtility.TryResolveAttributeCommand(tokens[0], out byte attribute, out byte mutation))
            {
                return false;
            }

            if (!TryResolveOptionalPlayerOrExplicitTarget(
                    explicitTargetId,
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
                    out var targetMode,
                    out uint targetRefKey,
                    out failure))
                return false;

            if (tokens.Length < 2)
            {
                failure = $"{tokens[0]} requires one value expression in MWScript V2: '{line}'.";
                return false;
            }

            string expressionText = string.Join(" ", tokens, 1, tokens.Length - 1);
            if (!TryCompileArithmeticExpression(
                    expressionText,
                    localLookup,
                    globalLookup,
                    soundLookup,
                    actorLookup,
                    carryableLookup,
                    spellLookup,
                    factionLookup,
                    actorLocalLookup,
                    dialogueLookup,
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
                    instructions,
                    ref stackDepth,
                    ref maxStack,
                    out failure))
            {
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.ActorAttributeMutation,
                Operand0 = (byte)targetMode,
                Operand1 = attribute,
                Int0 = unchecked((int)targetRefKey),
                Int1 = 1,
                Int2 = mutation,
            });
            return true;
        }

        static bool TryResolveOptionalPlayerOrExplicitTarget(
            string explicitTargetId,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            out MorrowindScriptRefTargetMode targetMode,
            out uint targetRefKey,
            out string failure)
        {
            failure = null;
            targetMode = MorrowindScriptRefTargetMode.Self;
            targetRefKey = 0u;
            if (string.IsNullOrWhiteSpace(explicitTargetId))
                return true;

            if (IsPlayerTarget(explicitTargetId))
            {
                targetMode = MorrowindScriptRefTargetMode.Player;
                return true;
            }

            return TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetRefKey, out failure);
        }

        static bool TryCompilePayFine(
            string line,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid PayFine command '{line}'.";
                    return false;
                }
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length == 0 || !tokens[0].Equals("payfine", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(explicitTargetId) && !IsPlayerTarget(explicitTargetId))
            {
                failure = $"PayFine target '{explicitTargetId}' is not supported in MWScript V1: '{line}'.";
                return false;
            }

            if (tokens.Length != 1)
            {
                failure = $"PayFine takes no arguments in MWScript V1: '{line}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.PayFine });
            return true;
        }

        static bool TryCompileSetPCCrimeLevel(
            string line,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, int> actorLookup,
            Dictionary<string, ContentReference> carryableLookup,
            Dictionary<string, SpellDefHandle> spellLookup,
            Dictionary<string, int> factionLookup,
            Dictionary<string, ActorLocalCompileInfo> actorLocalLookup,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            if (!tokens[0].Equals("setpccrimelevel", StringComparison.OrdinalIgnoreCase))
                return false;

            if (tokens.Length < 2)
            {
                failure = $"SetPCCrimeLevel requires one bounty expression: '{line}'.";
                return false;
            }

            string expressionText = string.Join(" ", tokens, 1, tokens.Length - 1);
            if (!TryCompileArithmeticExpression(
                    expressionText,
                    localLookup,
                    globalLookup,
                    soundLookup,
                    actorLookup,
                    carryableLookup,
                    spellLookup,
                    factionLookup,
                    actorLocalLookup,
                    dialogueLookup,
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
                    instructions,
                    ref stackDepth,
                    ref maxStack,
                    out failure))
            {
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.SetPCCrimeLevel });
            return true;
        }

        static bool TryCompileModPlayerReputation(
            string line,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid ModReputation command '{line}'.";
                    return false;
                }
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length == 0 || !string.Equals(tokens[0], "modreputation", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(explicitTargetId) && !IsPlayerTarget(explicitTargetId))
            {
                failure = $"ModReputation supports only Player target in MWScript V2: '{line}'.";
                return false;
            }

            if (tokens.Length != 2 || !int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                failure = $"ModReputation requires one literal integer value in MWScript V2: '{line}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.ModPlayerReputation,
                Int0 = value,
            });
            return true;
        }

        static bool TryCompilePlayerFactionCommand(
            string line,
            Dictionary<string, int> factionLookup,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid player faction command '{line}'.";
                    return false;
                }
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length == 0)
                return false;

            string command = NormalizeToken(tokens[0]);
            bool modRep = string.Equals(command, "modpcfacrep", StringComparison.OrdinalIgnoreCase);
            bool raiseRank = string.Equals(command, "pcraiserank", StringComparison.OrdinalIgnoreCase);
            bool joinFaction = string.Equals(command, "pcjoinfaction", StringComparison.OrdinalIgnoreCase);
            bool expel = string.Equals(command, "pcexpell", StringComparison.OrdinalIgnoreCase);
            bool clearExpelled = string.Equals(command, "pcclearexpelled", StringComparison.OrdinalIgnoreCase);
            if (!modRep && !raiseRank && !joinFaction && !expel && !clearExpelled)
                return false;

            if (!string.IsNullOrWhiteSpace(explicitTargetId) && !IsPlayerTarget(explicitTargetId))
            {
                failure = $"{tokens[0]} supports only Player target in MWScript V2: '{line}'.";
                return false;
            }

            int value = 0;
            string factionId = null;
            if (modRep)
            {
                if ((tokens.Length != 2 && tokens.Length != 3)
                    || !int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                {
                    failure = $"ModPCFacRep requires one literal integer value and optional faction id in MWScript V2: '{line}'.";
                    return false;
                }

                factionId = tokens.Length == 3 ? tokens[2] : null;
            }
            else
            {
                if (tokens.Length > 2)
                {
                    failure = $"{tokens[0]} accepts at most one faction id in MWScript V2: '{line}'.";
                    return false;
                }

                factionId = tokens.Length == 2 ? tokens[1] : null;
            }

            int factionIndex = -1;
            if (!string.IsNullOrWhiteSpace(factionId))
            {
                factionId = factionId.Trim('"');
                if (!factionLookup.TryGetValue(ContentId.NormalizeId(factionId), out factionIndex))
                {
                    failure = $"{tokens[0]} references unknown faction '{factionId}'.";
                    return false;
                }
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.PlayerFactionMutation,
                Operand0 = (byte)(modRep
                    ? 1
                    : raiseRank
                        ? 2
                        : joinFaction
                            ? 3
                            : expel
                                ? 4
                                : 5),
                Int0 = factionIndex,
                Int1 = value,
            });
            return true;
        }

        static bool TryCompileFactionReactionCommand(
            string line,
            Dictionary<string, int> factionLookup,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                string commandLine = StripExplicitReferencePrefix(line);
                if (!StartsWithCommand(commandLine, "modfactionreaction")
                    && !StartsWithCommand(commandLine, "setfactionreaction"))
                    return false;

                failure = $"Explicit faction reaction commands are not supported in MWScript V1: '{line}'.";
                return false;
            }

            string[] tokens = SplitConditionTokens(line);
            if (tokens.Length == 0)
                return false;

            string command = NormalizeToken(tokens[0]);
            bool isMod = string.Equals(command, "modfactionreaction", StringComparison.OrdinalIgnoreCase);
            bool isSet = string.Equals(command, "setfactionreaction", StringComparison.OrdinalIgnoreCase);
            if (!isMod && !isSet)
                return false;

            if (tokens.Length != 4)
            {
                failure = $"{tokens[0]} requires source faction, target faction, and literal integer value in MWScript V1: '{line}'.";
                return false;
            }

            if (!TryResolveFactionIndex(tokens[1], factionLookup, out int sourceFactionIndex, out string sourceFactionId))
            {
                failure = $"{tokens[0]} references unknown source faction '{sourceFactionId}'.";
                return false;
            }

            if (!TryResolveFactionIndex(tokens[2], factionLookup, out int targetFactionIndex, out string targetFactionId))
            {
                failure = $"{tokens[0]} references unknown target faction '{targetFactionId}'.";
                return false;
            }

            if (!int.TryParse(NormalizeToken(tokens[3]), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                failure = $"{tokens[0]} value must be a literal integer in MWScript V1: '{line}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.ModFactionReaction,
                Operand0 = isMod ? (byte)1 : (byte)0,
                Int0 = sourceFactionIndex,
                Int1 = targetFactionIndex,
                Int2 = value,
            });
            return true;
        }

        static bool TryResolveFactionIndex(
            string token,
            Dictionary<string, int> factionLookup,
            out int factionIndex,
            out string factionId)
        {
            factionIndex = -1;
            factionId = NormalizeToken(token).Trim('"');
            return !string.IsNullOrWhiteSpace(factionId)
                   && factionLookup != null
                   && factionLookup.TryGetValue(ContentId.NormalizeId(factionId), out factionIndex);
        }

        static bool TryCompileRaiseRank(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (!TrySplitOptionalExplicitCommand(
                    line,
                    "raiserank",
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
                    out var targetMode,
                    out uint targetRefKey,
                    out string commandLine,
                    out failure))
            {
                return false;
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length != 1)
            {
                failure = $"RaiseRank takes no arguments in MWScript V2: '{line}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.ActorRaiseRank,
                Operand0 = (byte)targetMode,
                Int0 = unchecked((int)targetRefKey),
            });
            return true;
        }

        static bool TryCompileSet(
            string line,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, int> actorLookup,
            Dictionary<string, ContentReference> carryableLookup,
            Dictionary<string, SpellDefHandle> spellLookup,
            Dictionary<string, int> factionLookup,
            Dictionary<string, ActorLocalCompileInfo> actorLocalLookup,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
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
                    actorLookup,
                    carryableLookup,
                    spellLookup,
                    factionLookup,
                    actorLocalLookup,
                    dialogueLookup,
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
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

            if (TryResolveActorLocalTarget(actorLocalLookup, target, out var actorLocal))
            {
                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)(actorLocal.ValueKind == (byte)MorrowindScriptValueKind.Float ? MorrowindScriptOpcode.SetActorLocalFloat : MorrowindScriptOpcode.SetActorLocalInt),
                    Int0 = actorLocal.ActorHandleValue,
                    Int1 = actorLocal.LocalIndex,
                });
                return true;
            }

            // OpenMW treats bad vanilla Set targets as compiler warnings and skips the assignment.
            // avSlavehunter in Morrowind.esm relies on that behavior for its undeclared "counter" target.
            instructions.RemoveRange(firstExpressionInstruction, instructions.Count - firstExpressionInstruction);
            stackDepth = stackDepthBeforeExpression;
            return true;
        }

        static bool TryCompileArithmeticExpression(
            string expression,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, int> actorLookup,
            Dictionary<string, ContentReference> carryableLookup,
            Dictionary<string, SpellDefHandle> spellLookup,
            Dictionary<string, int> factionLookup,
            Dictionary<string, ActorLocalCompileInfo> actorLocalLookup,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            string trimmedExpression = TrimEnclosingParentheses(expression);
            bool explicitPlayerExpression = trimmedExpression.StartsWith("player->", StringComparison.OrdinalIgnoreCase);
            string normalizedExpression = explicitPlayerExpression
                ? trimmedExpression.Substring("player->".Length).TrimStart()
                : trimmedExpression;

            if (normalizedExpression.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (FindTopLevelArithmeticOperator(normalizedExpression) >= 0)
                {
                    failure = $"Explicit reference arithmetic expressions are not supported in MWScript V2: '{expression}'.";
                    return false;
                }

                if (!TrySplitExplicitReference(normalizedExpression, out string explicitTargetId, out string explicitExpression))
                {
                    failure = $"Invalid explicit reference expression '{expression}'.";
                    return false;
                }

                if (!TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out var targetMode, out uint targetRefKey, out failure))
                    return false;

                string[] explicitTokens = SplitExpressionTokens(explicitExpression);
                if (explicitTokens.Length == 0)
                {
                    failure = "Set expression is missing.";
                    return false;
                }

                return TryCompileExpression(
                    explicitTokens,
                    0,
                    explicitTokens.Length,
                    localLookup,
                    globalLookup,
                    soundLookup,
                    actorLookup,
                    carryableLookup,
                    spellLookup,
                    factionLookup,
                    actorLocalLookup,
                    dialogueLookup,
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
                    targetMode,
                    targetRefKey,
                    instructions,
                    ref stackDepth,
                    ref maxStack,
                    out failure);
            }

            string[] tokens = SplitExpressionTokens(normalizedExpression);
            if (tokens.Length == 0)
            {
                failure = "Set expression is missing.";
                return false;
            }

            if (explicitPlayerExpression)
            {
                if (FindArithmeticOperator(tokens, 0, tokens.Length, 0) >= 0
                    || FindArithmeticOperator(tokens, 0, tokens.Length, 1) >= 0)
                {
                    failure = $"Explicit Player arithmetic expressions are not supported in MWScript V2: '{expression}'.";
                    return false;
                }

                return TryCompileExpression(
                    tokens,
                    0,
                    tokens.Length,
                    localLookup,
                    globalLookup,
                    soundLookup,
                    actorLookup,
                    carryableLookup,
                    spellLookup,
                    factionLookup,
                    actorLocalLookup,
                    dialogueLookup,
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
                    MorrowindScriptRefTargetMode.Player,
                    0u,
                    instructions,
                    ref stackDepth,
                    ref maxStack,
                    out failure);
            }

            return TryCompileArithmeticTokens(tokens, 0, tokens.Length, localLookup, globalLookup, soundLookup, actorLookup, carryableLookup, spellLookup, factionLookup, actorLocalLookup, dialogueLookup, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, instructions, ref stackDepth, ref maxStack, out failure);
        }

        static int FindTopLevelArithmeticOperator(string expression)
        {
            bool inQuote = false;
            int parenthesisDepth = 0;
            for (int i = 0; i < expression.Length; i++)
            {
                char c = expression[i];
                if (c == '"')
                {
                    inQuote = !inQuote;
                    continue;
                }

                if (inQuote || c == '-' && i + 1 < expression.Length && expression[i + 1] == '>')
                    continue;

                if (c == '(')
                {
                    parenthesisDepth++;
                    continue;
                }

                if (c == ')')
                {
                    parenthesisDepth--;
                    continue;
                }

                if (parenthesisDepth != 0)
                    continue;

                if (c == '+' || c == '*' || c == '/')
                    return i;

                if (c == '-' && !IsExplicitExpressionMinusUnary(expression, i))
                    return i;
            }

            return -1;
        }

        static bool IsExplicitExpressionMinusUnary(string expression, int index)
        {
            for (int i = index - 1; i >= 0; i--)
            {
                char c = expression[i];
                if (char.IsWhiteSpace(c))
                    continue;

                return c == '(' || c == '+' || c == '-' || c == '*' || c == '/' || c == '>';
            }

            return true;
        }

        static bool TryCompileArithmeticTokens(
            string[] tokens,
            int start,
            int count,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, int> actorLookup,
            Dictionary<string, ContentReference> carryableLookup,
            Dictionary<string, SpellDefHandle> spellLookup,
            Dictionary<string, int> factionLookup,
            Dictionary<string, ActorLocalCompileInfo> actorLocalLookup,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
            => TryCompileArithmeticTokensForTarget(
                tokens,
                start,
                count,
                localLookup,
                globalLookup,
                soundLookup,
                actorLookup,
                carryableLookup,
                spellLookup,
                factionLookup,
                actorLocalLookup,
                dialogueLookup,
                explicitRefTargets,
                ambiguousExplicitRefTargets,
                explicitContentTargets,
                MorrowindScriptRefTargetMode.Self,
                0u,
                instructions,
                ref stackDepth,
                ref maxStack,
                out failure);

        static bool TryCompileArithmeticTokensForTarget(
            string[] tokens,
            int start,
            int count,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, int> actorLookup,
            Dictionary<string, ContentReference> carryableLookup,
            Dictionary<string, SpellDefHandle> spellLookup,
            Dictionary<string, int> factionLookup,
            Dictionary<string, ActorLocalCompileInfo> actorLocalLookup,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            MorrowindScriptRefTargetMode refTargetMode,
            uint refTargetPlacedRefId,
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
                if (!TryCompileArithmeticTokensForTarget(tokens, start + 1, count - 1, localLookup, globalLookup, soundLookup, actorLookup, carryableLookup, spellLookup, factionLookup, actorLocalLookup, dialogueLookup, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, refTargetMode, refTargetPlacedRefId, instructions, ref stackDepth, ref maxStack, out failure))
                    return false;

                if (tokens[start] == "-")
                    instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.Negate });
                return true;
            }

            if (tokens[start] == "(" && HasSingleOuterParenthesisPair(tokens, start, count))
            {
                return TryCompileArithmeticTokensForTarget(tokens, start + 1, count - 2, localLookup, globalLookup, soundLookup, actorLookup, carryableLookup, spellLookup, factionLookup, actorLocalLookup, dialogueLookup, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, refTargetMode, refTargetPlacedRefId, instructions, ref stackDepth, ref maxStack, out failure);
            }

            int operatorIndex = FindArithmeticOperator(tokens, start, count, 0);
            if (operatorIndex < 0)
                operatorIndex = FindArithmeticOperator(tokens, start, count, 1);

            if (operatorIndex < 0)
                return TryCompileExpression(
                    tokens,
                    start,
                    count,
                    localLookup,
                    globalLookup,
                    soundLookup,
                    actorLookup,
                    carryableLookup,
                    spellLookup,
                    factionLookup,
                    actorLocalLookup,
                    dialogueLookup,
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
                    refTargetMode,
                    refTargetPlacedRefId,
                    instructions,
                    ref stackDepth,
                    ref maxStack,
                    out failure);

            if (!TryCompileArithmeticTokensForTarget(tokens, start, operatorIndex - start, localLookup, globalLookup, soundLookup, actorLookup, carryableLookup, spellLookup, factionLookup, actorLocalLookup, dialogueLookup, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, refTargetMode, refTargetPlacedRefId, instructions, ref stackDepth, ref maxStack, out failure))
                return false;

            if (!TryCompileArithmeticTokensForTarget(tokens, operatorIndex + 1, start + count - operatorIndex - 1, localLookup, globalLookup, soundLookup, actorLookup, carryableLookup, spellLookup, factionLookup, actorLocalLookup, dialogueLookup, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, refTargetMode, refTargetPlacedRefId, instructions, ref stackDepth, ref maxStack, out failure))
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
            int parenthesisDepth = 0;
            for (int i = end - 1; i >= start; i--)
            {
                string token = tokens[i];
                if (token == ")")
                {
                    parenthesisDepth++;
                    continue;
                }

                if (token == "(")
                {
                    parenthesisDepth--;
                    continue;
                }

                if (parenthesisDepth != 0)
                    continue;

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
            return previous == "+" || previous == "-" || previous == "*" || previous == "/" || previous == "(";
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

        static bool TryCompileJournal(
            string line,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            DialogueInfoDef[] dialogueInfos,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            string commandLine = line;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out string explicitTargetId, out commandLine)
                    || !StartsWithCommand(commandLine, "journal"))
                    return false;

                if (!ContentId.NormalizeId(explicitTargetId).Equals("player", StringComparison.OrdinalIgnoreCase))
                {
                    failure = $"Explicit Journal target '{explicitTargetId}' is not supported in MWScript V1: '{line}'.";
                    return false;
                }
            }

            if (!StartsWithCommand(commandLine, "journal"))
                return false;

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length != 3)
            {
                failure = $"Journal command requires quest id and integer stage in MWScript V1: '{line}'.";
                return false;
            }

            string journalId = NormalizeToken(tokens[1]).Trim('"');
            string normalizedJournalId = ContentId.NormalizeId(journalId);
            if (!dialogueLookup.TryGetValue(normalizedJournalId, out var dialogue))
            {
                failure = $"Journal references unknown journal '{journalId}'.";
                return false;
            }

            if (dialogue.Type != DialogueDefType.Journal)
            {
                failure = $"Journal references non-journal dialogue '{journalId}'.";
                return false;
            }

            if (!int.TryParse(NormalizeToken(tokens[2]), NumberStyles.Integer, CultureInfo.InvariantCulture, out int stage))
            {
                failure = $"Journal command requires a literal integer stage in MWScript V1: '{line}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.Journal,
                Operand0 = ResolveJournalQuestStatus(dialogue, dialogueInfos, stage, out int infoIndex),
                Int0 = dialogue.Index,
                Int1 = stage,
                Int2 = infoIndex,
            });
            return true;
        }

        static bool TryCompileSetJournalIndex(
            string line,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            string commandLine = line;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out string explicitTargetId, out commandLine)
                    || !StartsWithCommand(commandLine, "setjournalindex"))
                    return false;

                if (!ContentId.NormalizeId(explicitTargetId).Equals("player", StringComparison.OrdinalIgnoreCase))
                {
                    failure = $"Explicit SetJournalIndex target '{explicitTargetId}' is not supported in MWScript V1: '{line}'.";
                    return false;
                }
            }

            if (!StartsWithCommand(commandLine, "setjournalindex"))
                return false;

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length != 3)
            {
                failure = $"SetJournalIndex command requires quest id and integer stage in MWScript V1: '{line}'.";
                return false;
            }

            string journalId = NormalizeToken(tokens[1]).Trim('"');
            string normalizedJournalId = ContentId.NormalizeId(journalId);
            if (!dialogueLookup.TryGetValue(normalizedJournalId, out var dialogue))
            {
                failure = $"SetJournalIndex references unknown journal '{journalId}'.";
                return false;
            }

            if (dialogue.Type != DialogueDefType.Journal)
            {
                failure = $"SetJournalIndex references non-journal dialogue '{journalId}'.";
                return false;
            }

            if (!int.TryParse(NormalizeToken(tokens[2]), NumberStyles.Integer, CultureInfo.InvariantCulture, out int stage))
            {
                failure = $"SetJournalIndex command requires a literal integer stage in MWScript V1: '{line}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.SetJournalIndex,
                Int0 = dialogue.Index,
                Int1 = stage,
                Int2 = -1,
            });
            return true;
        }

        static byte ResolveJournalQuestStatus(
            in DialogueCompileInfo dialogue,
            DialogueInfoDef[] dialogueInfos,
            int stage,
            out int infoIndex)
        {
            infoIndex = -1;
            dialogueInfos ??= Array.Empty<DialogueInfoDef>();
            int end = Math.Min(dialogueInfos.Length, dialogue.FirstInfoIndex + dialogue.InfoCount);
            for (int i = dialogue.FirstInfoIndex; i < end; i++)
            {
                if (i < 0 || dialogueInfos[i].DispositionOrJournalIndex != stage)
                    continue;

                infoIndex = i;
                return dialogueInfos[i].QuestStatus;
            }

            return 0;
        }

        static bool TryCompileTopicCommand(
            string line,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            string commandLine = StripExplicitReferencePrefix(line);
            if (StartsWithCommand(commandLine, "addtopic"))
            {
                if (line.IndexOf("->", StringComparison.Ordinal) >= 0
                    && (!TrySplitExplicitReference(line, out string targetId, out commandLine) || !IsPlayerTarget(targetId)))
                {
                    failure = $"AddTopic supports only explicit Player references in MWScript V1: '{line}'.";
                    return false;
                }

                string[] tokens = SplitCommandTokens(commandLine);
                if (tokens.Length != 2)
                {
                    failure = $"AddTopic requires one literal topic id in MWScript V1: '{line}'.";
                    return false;
                }

                string topicId = NormalizeToken(tokens[1]).Trim('"');
                string normalizedTopicId = ContentId.NormalizeId(topicId);
                if (!dialogueLookup.TryGetValue(normalizedTopicId, out var dialogue))
                {
                    failure = $"AddTopic references unknown topic '{topicId}'.";
                    return false;
                }

                if (dialogue.Type != DialogueDefType.Topic)
                {
                    failure = $"AddTopic references non-topic dialogue '{topicId}'.";
                    return false;
                }

                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)MorrowindScriptOpcode.AddTopic,
                    Int0 = dialogue.Index,
                });
                return true;
            }

            if (StartsWithCommand(commandLine, "filljournal"))
            {
                if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
                {
                    failure = $"Explicit FillJournal references are not supported in MWScript V1: '{line}'.";
                    return false;
                }

                string[] tokens = SplitCommandTokens(line);
                if (tokens.Length != 1)
                {
                    failure = $"FillJournal takes no arguments in MWScript V1: '{line}'.";
                    return false;
                }

                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)MorrowindScriptOpcode.FillJournal,
                    Int0 = -1,
                });
                return true;
            }

            return false;
        }

        static bool TryCompileStartScript(
            string line,
            Dictionary<string, int> scriptLookup,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                string commandLine = StripExplicitReferencePrefix(line);
                if (!StartsWithCommand(commandLine, "startscript"))
                    return false;

                failure = $"Explicit StartScript references are not supported in MWScript V1: '{line}'.";
                return false;
            }

            if (!StartsWithCommand(line, "startscript"))
                return false;

            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 2)
            {
                failure = $"StartScript command requires one script id in MWScript V1: '{line}'.";
                return false;
            }

            string targetScriptId = NormalizeToken(tokens[1]).Trim('"');
            if (string.IsNullOrWhiteSpace(targetScriptId)
                || scriptLookup == null
                || !scriptLookup.TryGetValue(ContentId.NormalizeId(targetScriptId), out int programIndex))
            {
                failure = $"StartScript references unknown script '{targetScriptId}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.StartScript,
                Int0 = MorrowindScriptProgramDefHandle.FromIndex(programIndex).Value,
                Int1 = programIndex,
            });
            return true;
        }

        static bool TryCompileStopScript(
            string line,
            string currentScriptId,
            Dictionary<string, int> scriptLookup,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                string commandLine = StripExplicitReferencePrefix(line);
                if (!StartsWithCommand(commandLine, "stopscript"))
                    return false;

                failure = $"Explicit StopScript references are not supported in MWScript V1: '{line}'.";
                return false;
            }

            if (!StartsWithCommand(line, "stopscript"))
                return false;

            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 1)
            {
                instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.StopScript });
                return true;
            }

            if (tokens.Length != 2)
            {
                failure = $"StopScript command requires zero arguments or one script id in MWScript V1: '{line}'.";
                return false;
            }

            string targetScriptId = NormalizeToken(tokens[1]).Trim('"');
            if (ContentId.NormalizeId(targetScriptId).Equals(ContentId.NormalizeId(currentScriptId), StringComparison.OrdinalIgnoreCase))
            {
                instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.StopScript });
                return true;
            }

            if (scriptLookup == null || !scriptLookup.TryGetValue(ContentId.NormalizeId(targetScriptId), out int programIndex))
            {
                failure = $"StopScript references unknown script '{targetScriptId}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.StopScript,
                Operand0 = 1,
                Int0 = programIndex,
            });
            return true;
        }

        static bool TryCompileDontSaveObject(
            string line,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                string commandLine = StripExplicitReferencePrefix(line);
                if (!StartsWithCommand(commandLine, "dontsaveobject"))
                    return false;

                failure = $"Explicit DontSaveObject references are not supported in MWScript V1: '{line}'.";
                return false;
            }

            if (!StartsWithCommand(line, "dontsaveobject"))
                return false;

            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 1)
            {
                failure = $"DontSaveObject takes no arguments in MWScript V1: '{line}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.Nop });
            return true;
        }

        static bool TryCompileRefStateCommand(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            var targetMode = MorrowindScriptRefTargetMode.Self;
            uint targetPlacedRefId = 0u;
            string commandLine = line;
            string sourceLine = line;
            string explicitTargetId = null;

            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid explicit reference command '{line}'.";
                    return false;
                }
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length == 0)
                return false;

            string command = NormalizeToken(tokens[0]);
            byte disabled;
            if (command.Equals("enable", StringComparison.OrdinalIgnoreCase))
                disabled = 0;
            else if (command.Equals("disable", StringComparison.OrdinalIgnoreCase))
                disabled = 1;
            else
                return false;

            if (explicitTargetId != null)
            {
                if (!TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetPlacedRefId, out failure))
                    return false;
            }

            if (tokens.Length == 2 && explicitTargetId == null)
            {
                string targetId = tokens[1].Trim('"');
                if (!TryResolveExplicitRefTarget(targetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetPlacedRefId, out failure))
                    return false;
            }
            else if (tokens.Length != 1)
            {
                failure = $"Enable/Disable commands do not support arguments in MWScript V1: '{sourceLine}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.RequestSetDisabled,
                Operand0 = (byte)targetMode,
                Operand1 = disabled,
                Int0 = unchecked((int)targetPlacedRefId),
            });
            return true;
        }

        static bool TryCompileLockStateCommand(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            const int defaultLockLevelSentinel = int.MinValue;
            failure = null;
            var targetMode = MorrowindScriptRefTargetMode.Self;
            uint targetPlacedRefId = 0u;
            string commandLine = line;
            string explicitTargetId = null;

            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid lock state command '{line}'.";
                    return false;
                }
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length == 0)
                return false;

            string command = NormalizeToken(tokens[0]);
            bool lockCommand = command.Equals("lock", StringComparison.OrdinalIgnoreCase);
            bool unlockCommand = command.Equals("unlock", StringComparison.OrdinalIgnoreCase);
            if (!lockCommand && !unlockCommand)
                return false;

            if (explicitTargetId != null
                && !TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetPlacedRefId, out failure))
            {
                return false;
            }

            int lockLevel = defaultLockLevelSentinel;
            if (lockCommand)
            {
                if (tokens.Length > 2)
                {
                    failure = $"Lock command supports at most one literal integer level in MWScript V2: '{line}'.";
                    return false;
                }

                if (tokens.Length == 2
                    && !int.TryParse(NormalizeToken(tokens[1]), NumberStyles.Integer, CultureInfo.InvariantCulture, out lockLevel))
                {
                    failure = $"Lock command level must be a literal integer in MWScript V2: '{line}'.";
                    return false;
                }
            }
            else if (tokens.Length != 1)
            {
                failure = $"Unlock command takes no arguments in MWScript V2: '{line}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.RequestLockState,
                Operand0 = (byte)targetMode,
                Operand1 = (short)(lockCommand ? 1 : 2),
                Int0 = unchecked((int)targetPlacedRefId),
                Int1 = lockLevel,
            });
            return true;
        }

        static bool TryCompileMovementFlagCommand(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            var targetMode = MorrowindScriptRefTargetMode.Self;
            uint targetPlacedRefId = 0u;
            string commandLine = line;
            string explicitTargetId = null;

            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid movement flag command '{line}'.";
                    return false;
                }
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length == 0)
                return false;

            string command = NormalizeToken(tokens[0]);
            bool enable;
            if (command.Equals("forcesneak", StringComparison.OrdinalIgnoreCase))
                enable = true;
            else if (command.Equals("clearforcesneak", StringComparison.OrdinalIgnoreCase))
                enable = false;
            else
                return false;

            if (tokens.Length != 1)
            {
                failure = $"{command} command takes no arguments in MWScript V1: '{line}'.";
                return false;
            }

            if (explicitTargetId != null)
            {
                if (IsPlayerTarget(explicitTargetId))
                {
                    targetMode = MorrowindScriptRefTargetMode.Player;
                }
                else
                {
                    if (!TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetPlacedRefId, out failure))
                        return false;
                }
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.SetMovementFlag,
                Operand0 = (byte)targetMode,
                Operand1 = (short)MorrowindScriptMovementFlagKind.ForceSneak,
                Int0 = unchecked((int)targetPlacedRefId),
                Int1 = enable ? 1 : 0,
            });
            return true;
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

        static bool TryCompileOnActivateStatement(
            string line,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (!StartsWithCommand(line, "onactivate"))
                return false;

            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                failure = $"Explicit OnActivate statements are not supported in MWScript V2: '{line}'.";
                return false;
            }

            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 1)
            {
                failure = $"OnActivate statement takes no arguments in MWScript V2: '{line}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.OnActivateStatement });
            return true;
        }

        static bool TryCompileRotate(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            var targetMode = MorrowindScriptRefTargetMode.Self;
            uint targetPlacedRefId = 0u;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid explicit Rotate command '{line}'.";
                    return false;
                }
            }

            bool rotateWorld = StartsWithCommand(commandLine, "rotateworld");
            if (!rotateWorld && !StartsWithCommand(commandLine, "rotate"))
                return false;

            if (explicitTargetId != null)
            {
                if (!TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetPlacedRefId, out failure))
                    return false;
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length != 3)
            {
                failure = $"Rotate command requires axis and speed in MWScript V2: '{line}'.";
                return false;
            }

            string axis = NormalizeToken(tokens[1]).Trim('"');
            if (!TryMapRotateAxis(axis, out byte axisIndex))
            {
                failure = $"Rotate command has unsupported axis '{tokens[1]}'.";
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
                Operand0 = (byte)targetMode,
                Operand1 = axisIndex,
                Int0 = unchecked((int)targetPlacedRefId),
                Int1 = rotateWorld ? 1 : 0,
                Float0 = speed,
            });
            return true;
        }

        static bool TryCompileSetAngle(
            string line,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, int> actorLookup,
            Dictionary<string, ContentReference> carryableLookup,
            Dictionary<string, SpellDefHandle> spellLookup,
            Dictionary<string, int> factionLookup,
            Dictionary<string, ActorLocalCompileInfo> actorLocalLookup,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            var targetMode = MorrowindScriptRefTargetMode.Self;
            uint targetPlacedRefId = 0u;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid explicit SetAngle command '{line}'.";
                    return false;
                }
            }

            if (!StartsWithCommand(commandLine, "setangle"))
                return false;

            if (explicitTargetId != null)
            {
                if (!TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetPlacedRefId, out failure))
                    return false;
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length < 3)
            {
                failure = $"SetAngle command requires axis and angle in MWScript V2: '{line}'.";
                return false;
            }

            string axis = NormalizeToken(tokens[1]).Trim('"');
            if (!TryMapRotateAxis(axis, out byte axisIndex))
            {
                failure = $"SetAngle command has unsupported axis '{tokens[1]}'.";
                return false;
            }

            string expression = string.Join(" ", tokens, 2, tokens.Length - 2);
            int firstExpressionInstruction = instructions.Count;
            int stackDepthBeforeExpression = stackDepth;
            if (!TryCompileArithmeticExpression(
                    expression,
                    localLookup,
                    globalLookup,
                    soundLookup,
                    actorLookup,
                    carryableLookup,
                    spellLookup,
                    factionLookup,
                    actorLocalLookup,
                    dialogueLookup,
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
                    instructions,
                    ref stackDepth,
                    ref maxStack,
                    out failure))
            {
                instructions.RemoveRange(firstExpressionInstruction, instructions.Count - firstExpressionInstruction);
                stackDepth = stackDepthBeforeExpression;
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.SetAngle,
                Operand0 = (byte)targetMode,
                Operand1 = axisIndex,
                Int0 = unchecked((int)targetPlacedRefId),
            });
            return true;
        }

        static bool TryCompileSetAtStart(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            var targetMode = MorrowindScriptRefTargetMode.Self;
            uint targetPlacedRefId = 0u;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid explicit SetAtStart command '{line}'.";
                    return false;
                }
            }

            if (!StartsWithCommand(commandLine, "setatstart"))
                return false;

            if (explicitTargetId != null
                && !TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetPlacedRefId, out failure))
            {
                return false;
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length != 1)
            {
                failure = $"SetAtStart takes no arguments in MWScript V2: '{line}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.SetAtStart,
                Operand0 = (byte)targetMode,
                Int0 = unchecked((int)targetPlacedRefId),
            });
            return true;
        }

        static bool TryCompileAnimationGroupCommand(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptMessageDef> messages,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            var targetMode = MorrowindScriptRefTargetMode.Self;
            uint targetPlacedRefId = 0u;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid animation group command '{line}'.";
                    return false;
                }
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length == 0)
                return false;

            string command = NormalizeToken(tokens[0]);
            bool play = command.Equals("playgroup", StringComparison.OrdinalIgnoreCase);
            bool loop = command.Equals("loopgroup", StringComparison.OrdinalIgnoreCase);
            if (!play && !loop)
                return false;

            if (explicitTargetId != null)
            {
                if (IsPlayerTarget(explicitTargetId))
                {
                    targetMode = MorrowindScriptRefTargetMode.Player;
                }
                else if (!TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetPlacedRefId, out failure))
                {
                    return false;
                }
            }

            int minCount = play ? 2 : 3;
            int maxCount = play ? 3 : 4;
            if (tokens.Length < minCount || tokens.Length > maxCount)
            {
                failure = play
                    ? $"PlayGroup requires group name and optional mode in MWScript V1: '{line}'."
                    : $"LoopGroup requires group name, loop count, and optional mode in MWScript V1: '{line}'.";
                return false;
            }

            string group = NormalizeToken(tokens[1]).Trim('"');
            if (string.IsNullOrWhiteSpace(group))
            {
                failure = $"{command} group name is empty in MWScript V1: '{line}'.";
                return false;
            }

            int loops = 0;
            if (loop && (!int.TryParse(NormalizeToken(tokens[2]), NumberStyles.Integer, CultureInfo.InvariantCulture, out loops) || loops < 0))
            {
                failure = $"LoopGroup loop count must be a non-negative literal integer in MWScript V1: '{line}'.";
                return false;
            }

            int mode = 0;
            int modeTokenIndex = play ? 2 : 3;
            if (tokens.Length > modeTokenIndex
                && (!int.TryParse(NormalizeToken(tokens[modeTokenIndex]), NumberStyles.Integer, CultureInfo.InvariantCulture, out mode) || mode < 0 || mode > 2))
            {
                failure = $"{command} mode must be literal 0, 1, or 2 in MWScript V1: '{line}'.";
                return false;
            }

            int groupMessageIndex = messages.Count;
            messages.Add(new MorrowindScriptMessageDef { Text = group });
            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.PlayAnimationGroup,
                Operand0 = (byte)targetMode,
                Operand1 = (short)(play ? MorrowindScriptAnimationGroupOperation.Play : MorrowindScriptAnimationGroupOperation.Loop),
                Int0 = unchecked((int)targetPlacedRefId),
                Int1 = groupMessageIndex,
                Int2 = loops,
                Float0 = mode,
            });
            return true;
        }

        static bool TryCompileFallCommand(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (!TrySplitOptionalExplicitCommand(line, "fall", explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out _, out _, out string commandLine, out failure))
                return false;

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length != 1)
            {
                failure = $"Fall takes no arguments in MWScript V1: '{line}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef { Opcode = (byte)MorrowindScriptOpcode.Nop });
            return true;
        }

        static bool TryCompilePositionCell(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            var targetMode = MorrowindScriptRefTargetMode.Self;
            uint targetPlacedRefId = 0u;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid explicit PositionCell command '{line}'.";
                    return false;
                }
            }

            if (!StartsWithCommand(commandLine, "positioncell"))
                return false;

            if (explicitTargetId != null)
            {
                if (!TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetPlacedRefId, out failure))
                    return false;
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length != 6)
            {
                failure = $"PositionCell command requires x, y, z, z-rotation, and cell id in MWScript V2: '{line}'.";
                return false;
            }

            if (!float.TryParse(NormalizeToken(tokens[1]), NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(NormalizeToken(tokens[2]), NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                || !float.TryParse(NormalizeToken(tokens[3]), NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
                || !float.TryParse(NormalizeToken(tokens[4]), NumberStyles.Float, CultureInfo.InvariantCulture, out float zRotMinutes))
            {
                failure = $"PositionCell command has invalid coordinates or rotation: '{line}'.";
                return false;
            }

            string cellId = NormalizeToken(tokens[5]).Trim('"');
            if (string.IsNullOrWhiteSpace(cellId))
            {
                failure = $"PositionCell command has empty cell id: '{line}'.";
                return false;
            }

            ulong cellHash = HashInteriorCellId(cellId);
            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.PositionCell,
                Operand0 = (byte)targetMode,
                Int0 = unchecked((int)targetPlacedRefId),
                Int1 = unchecked((int)(cellHash & 0xFFFFFFFFu)),
                Int2 = unchecked((int)(cellHash >> 32)),
                Float0 = x * WorldScale.MwUnitsToMeters,
                Float1 = z * WorldScale.MwUnitsToMeters,
                Float2 = y * WorldScale.MwUnitsToMeters,
                Float3 = zRotMinutes,
            });
            return true;
        }

        static bool TryCompilePosition(
            string line,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, int> actorLookup,
            Dictionary<string, ContentReference> carryableLookup,
            Dictionary<string, SpellDefHandle> spellLookup,
            Dictionary<string, int> factionLookup,
            Dictionary<string, ActorLocalCompileInfo> actorLocalLookup,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            var targetMode = MorrowindScriptRefTargetMode.Self;
            uint targetPlacedRefId = 0u;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid explicit Position command '{line}'.";
                    return false;
                }
            }

            if (!StartsWithCommand(commandLine, "position"))
                return false;

            if (explicitTargetId != null)
            {
                if (!TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetPlacedRefId, out failure))
                    return false;
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length != 5)
            {
                failure = $"Position command requires x, y, z, and z-rotation in MWScript V2: '{line}'.";
                return false;
            }

            if (!float.TryParse(NormalizeToken(tokens[1]), NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(NormalizeToken(tokens[2]), NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                || !float.TryParse(NormalizeToken(tokens[3]), NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                failure = $"Position command has invalid coordinates: '{line}'.";
                return false;
            }

            int firstExpressionInstruction = instructions.Count;
            int stackDepthBeforeExpression = stackDepth;
            if (!TryCompileArithmeticExpression(
                    tokens[4],
                    localLookup,
                    globalLookup,
                    soundLookup,
                    actorLookup,
                    carryableLookup,
                    spellLookup,
                    factionLookup,
                    actorLocalLookup,
                    dialogueLookup,
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
                    instructions,
                    ref stackDepth,
                    ref maxStack,
                    out failure))
            {
                instructions.RemoveRange(firstExpressionInstruction, instructions.Count - firstExpressionInstruction);
                stackDepth = stackDepthBeforeExpression;
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.Position,
                Operand0 = (byte)targetMode,
                Int0 = unchecked((int)targetPlacedRefId),
                Float0 = x * WorldScale.MwUnitsToMeters,
                Float1 = z * WorldScale.MwUnitsToMeters,
                Float2 = y * WorldScale.MwUnitsToMeters,
            });
            return true;
        }

        static bool TryCompileSetPos(
            string line,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, int> actorLookup,
            Dictionary<string, ContentReference> carryableLookup,
            Dictionary<string, SpellDefHandle> spellLookup,
            Dictionary<string, int> factionLookup,
            Dictionary<string, ActorLocalCompileInfo> actorLocalLookup,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            var targetMode = MorrowindScriptRefTargetMode.Self;
            uint targetPlacedRefId = 0u;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid explicit SetPos command '{line}'.";
                    return false;
                }
            }

            if (!StartsWithCommand(commandLine, "setpos"))
                return false;

            if (explicitTargetId != null)
            {
                if (!TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetPlacedRefId, out failure))
                    return false;
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length != 3)
            {
                failure = $"SetPos command requires axis and position in MWScript V2: '{line}'.";
                return false;
            }

            string axis = NormalizeToken(tokens[1]).Trim('"');
            if (!TryMapRotateAxis(axis, out byte axisIndex))
            {
                failure = $"SetPos command has unsupported axis '{tokens[1]}'.";
                return false;
            }

            int firstExpressionInstruction = instructions.Count;
            int stackDepthBeforeExpression = stackDepth;
            if (!TryCompileArithmeticExpression(
                    tokens[2],
                    localLookup,
                    globalLookup,
                    soundLookup,
                    actorLookup,
                    carryableLookup,
                    spellLookup,
                    factionLookup,
                    actorLocalLookup,
                    dialogueLookup,
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
                    instructions,
                    ref stackDepth,
                    ref maxStack,
                    out failure))
            {
                instructions.RemoveRange(firstExpressionInstruction, instructions.Count - firstExpressionInstruction);
                stackDepth = stackDepthBeforeExpression;
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.SetPos,
                Operand0 = (byte)targetMode,
                Operand1 = axisIndex,
                Int0 = unchecked((int)targetPlacedRefId),
            });
            return true;
        }

        static bool TryCompileMoveCommand(
            string line,
            string command,
            MorrowindScriptOpcode opcode,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, int> actorLookup,
            Dictionary<string, ContentReference> carryableLookup,
            Dictionary<string, SpellDefHandle> spellLookup,
            Dictionary<string, int> factionLookup,
            Dictionary<string, ActorLocalCompileInfo> actorLocalLookup,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            var targetMode = MorrowindScriptRefTargetMode.Self;
            uint targetPlacedRefId = 0u;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid explicit {command} command '{line}'.";
                    return false;
                }
            }

            if (!StartsWithCommand(commandLine, command))
                return false;

            if (explicitTargetId != null)
            {
                if (!TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetPlacedRefId, out failure))
                    return false;
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length != 3)
            {
                failure = $"{command} command requires axis and speed in MWScript V2: '{line}'.";
                return false;
            }

            string axis = NormalizeToken(tokens[1]).Trim('"');
            if (!TryMapRotateAxis(axis, out byte axisIndex))
            {
                failure = $"{command} command has unsupported axis '{tokens[1]}'.";
                return false;
            }

            int firstExpressionInstruction = instructions.Count;
            int stackDepthBeforeExpression = stackDepth;
            if (!TryCompileArithmeticExpression(
                    tokens[2],
                    localLookup,
                    globalLookup,
                    soundLookup,
                    actorLookup,
                    carryableLookup,
                    spellLookup,
                    factionLookup,
                    actorLocalLookup,
                    dialogueLookup,
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
                    instructions,
                    ref stackDepth,
                    ref maxStack,
                    out failure))
            {
                instructions.RemoveRange(firstExpressionInstruction, instructions.Count - firstExpressionInstruction);
                stackDepth = stackDepthBeforeExpression;
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)opcode,
                Operand0 = (byte)targetMode,
                Operand1 = axisIndex,
                Int0 = unchecked((int)targetPlacedRefId),
            });
            return true;
        }

        static bool TryCompilePlaceAtPC(
            string line,
            Dictionary<string, ContentReference> placeAtSpawnables,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                string commandLine = StripExplicitReferencePrefix(line);
                if (!StartsWithCommand(commandLine, "placeatpc"))
                    return false;

                failure = $"Explicit PlaceAtPC references are not supported in MWScript V1: '{line}'.";
                return false;
            }

            if (!StartsWithCommand(line, "placeatpc"))
                return false;

            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 5)
            {
                failure = $"PlaceAtPC command requires content id, count, distance, and direction in MWScript V1: '{line}'.";
                return false;
            }

            string contentId = tokens[1].Trim('"');
            if (!placeAtSpawnables.TryGetValue(ContentId.NormalizeId(contentId), out var content) || !content.IsValid)
            {
                failure = $"PlaceAtPC command references unknown or unsupported spawnable content '{contentId}'.";
                return false;
            }

            if (!int.TryParse(NormalizeToken(tokens[2]), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
            {
                failure = $"PlaceAtPC command requires a literal integer count: '{line}'.";
                return false;
            }

            if (count < 0)
            {
                failure = $"PlaceAtPC command count must be non-negative: '{line}'.";
                return false;
            }

            if (!float.TryParse(NormalizeToken(tokens[3]), NumberStyles.Float, CultureInfo.InvariantCulture, out float distance))
            {
                failure = $"PlaceAtPC command requires a literal numeric distance: '{line}'.";
                return false;
            }

            if (!int.TryParse(NormalizeToken(tokens[4]), NumberStyles.Integer, CultureInfo.InvariantCulture, out int direction)
                || direction < 0
                || direction > 3)
            {
                failure = $"PlaceAtPC command direction must be a literal integer from 0 to 3: '{line}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.PlaceAtPC,
                Operand0 = (byte)content.Kind,
                Int0 = content.HandleValue,
                Int1 = count,
                Int2 = direction,
                Float0 = Math.Max(0f, distance) * WorldScale.MwUnitsToMeters,
            });
            return true;
        }

        static bool TryCompileAiWander(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (!TrySplitOptionalExplicitCommand(line, "aiwander", explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out var targetMode, out uint targetPlacedRefId, out string commandLine, out failure))
                return false;

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length < 4)
            {
                failure = $"AiWander command requires distance, duration, and time-of-day in MWScript V2: '{line}'.";
                return false;
            }

            if (!float.TryParse(NormalizeToken(tokens[1]), NumberStyles.Float, CultureInfo.InvariantCulture, out float range))
            {
                failure = $"AiWander command has invalid wander distance: '{line}'.";
                return false;
            }

            if (!float.TryParse(NormalizeToken(tokens[2]), NumberStyles.Float, CultureInfo.InvariantCulture, out float duration))
            {
                failure = $"AiWander command has invalid duration: '{line}'.";
                return false;
            }

            for (int i = 2; i < tokens.Length; i++)
            {
                if (!float.TryParse(NormalizeToken(tokens[i]), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    failure = $"AiWander command has invalid numeric argument: '{line}'.";
                    return false;
                }
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.AiWander,
                Operand0 = (byte)targetMode,
                Operand1 = ResolveAiWanderRepeat(tokens),
                Int0 = unchecked((int)targetPlacedRefId),
                Int1 = PackAiWanderIdleChances(tokens, 4),
                Int2 = PackAiWanderIdleChances(tokens, 8),
                Float0 = math.max(0f, range) * WorldScale.MwUnitsToMeters,
                Float1 = math.max(0f, duration),
            });
            return true;
        }

        static int PackAiWanderIdleChances(string[] tokens, int startIndex)
        {
            int packed = 0;
            for (int i = 0; i < 4; i++)
            {
                int tokenIndex = startIndex + i;
                int chance = 0;
                if ((uint)tokenIndex < (uint)tokens.Length
                    && float.TryParse(NormalizeToken(tokens[tokenIndex]), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                {
                    chance = (int)math.clamp(math.round(value), 0f, 100f);
                }

                packed |= (chance & 0xFF) << (i * 8);
            }

            return packed;
        }

        static bool TryCompileAiTravel(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (!TrySplitOptionalExplicitCommand(line, "aitravel", explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out var targetMode, out uint targetPlacedRefId, out string commandLine, out failure))
                return false;

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length < 4)
            {
                failure = $"AiTravel command requires x, y, and z in MWScript V2: '{line}'.";
                return false;
            }

            if (!float.TryParse(NormalizeToken(tokens[1]), NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(NormalizeToken(tokens[2]), NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                || !float.TryParse(NormalizeToken(tokens[3]), NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                failure = $"AiTravel command has invalid coordinates: '{line}'.";
                return false;
            }

            for (int i = 4; i < tokens.Length; i++)
            {
                if (!float.TryParse(NormalizeToken(tokens[i]), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    failure = $"AiTravel command has invalid reset argument: '{line}'.";
                    return false;
                }
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.AiTravel,
                Operand0 = (byte)targetMode,
                Operand1 = tokens.Length > 4 ? (short)1 : (short)0,
                Int0 = unchecked((int)targetPlacedRefId),
                Float0 = x * WorldScale.MwUnitsToMeters,
                Float1 = z * WorldScale.MwUnitsToMeters,
                Float2 = y * WorldScale.MwUnitsToMeters,
            });
            return true;
        }

        static bool TryCompileAiFollow(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (!TrySplitOptionalExplicitCommand(line, "aifollow", explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out var targetMode, out uint targetPlacedRefId, out string commandLine, out failure))
                return false;

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length < 6)
            {
                failure = $"AiFollow command requires target actor, duration, x, y, and z in MWScript V2: '{line}'.";
                return false;
            }

            if (!TryResolveDistanceTarget(tokens[1].Trim('"'), explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out var followTargetMode, out uint followTargetRefKey, out failure))
                return false;

            if (!TryParseAiFollowNumbers(tokens, 2, line, out float duration, out float x, out float y, out float z, out failure))
                return false;

            for (int i = 6; i < tokens.Length; i++)
            {
                if (!float.TryParse(NormalizeToken(tokens[i]), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    failure = $"AiFollow command has invalid reset argument: '{line}'.";
                    return false;
                }
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.AiFollow,
                Operand0 = (byte)targetMode,
                Operand1 = tokens.Length > 6 ? (short)1 : (short)0,
                Int0 = unchecked((int)targetPlacedRefId),
                Int1 = unchecked((int)followTargetRefKey),
                Int2 = (int)followTargetMode,
                Float0 = duration,
                Float1 = x * WorldScale.MwUnitsToMeters,
                Float2 = z * WorldScale.MwUnitsToMeters,
                Float3 = y * WorldScale.MwUnitsToMeters,
            });
            return true;
        }

        static bool TryCompileAiActivate(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptMessageDef> messages,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (!TrySplitOptionalExplicitCommand(line, "aiactivate", explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out var targetMode, out uint targetPlacedRefId, out string commandLine, out failure))
                return false;

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length < 2)
            {
                failure = $"AiActivate command requires target id in MWScript V2: '{line}'.";
                return false;
            }

            string targetId = NormalizeToken(tokens[1]).Trim('"');
            if (string.IsNullOrWhiteSpace(targetId))
            {
                failure = $"AiActivate command has empty target id: '{line}'.";
                return false;
            }

            for (int i = 2; i < tokens.Length; i++)
            {
                if (!float.TryParse(NormalizeToken(tokens[i]), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    failure = $"AiActivate command has invalid reset argument: '{line}'.";
                    return false;
                }
            }

            int targetIdIndex = messages.Count;
            messages.Add(new MorrowindScriptMessageDef { Text = targetId });
            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.AiActivate,
                Operand0 = (byte)targetMode,
                Operand1 = tokens.Length > 2 ? (short)1 : (short)0,
                Int0 = unchecked((int)targetPlacedRefId),
                Int1 = targetIdIndex,
            });
            return true;
        }

        static bool TryCompileAiEscort(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (!TrySplitOptionalExplicitCommand(line, "aiescort", explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out var targetMode, out uint targetPlacedRefId, out string commandLine, out failure))
                return false;

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length < 6)
            {
                failure = $"AiEscort command requires target actor, duration, x, y, and z in MWScript V2: '{line}'.";
                return false;
            }

            if (!TryResolveDistanceTarget(tokens[1].Trim('"'), explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out var escortTargetMode, out uint escortTargetRefKey, out failure))
                return false;

            if (!TryParseAiFollowNumbers(tokens, 2, line, out float duration, out float x, out float y, out float z, out failure))
                return false;

            for (int i = 6; i < tokens.Length; i++)
            {
                if (!float.TryParse(NormalizeToken(tokens[i]), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    failure = $"AiEscort command has invalid reset argument: '{line}'.";
                    return false;
                }
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.AiEscort,
                Operand0 = (byte)targetMode,
                Operand1 = tokens.Length > 6 ? (short)1 : (short)0,
                Int0 = unchecked((int)targetPlacedRefId),
                Int1 = unchecked((int)escortTargetRefKey),
                Int2 = (int)escortTargetMode,
                Float0 = duration,
                Float1 = x * WorldScale.MwUnitsToMeters,
                Float2 = z * WorldScale.MwUnitsToMeters,
                Float3 = y * WorldScale.MwUnitsToMeters,
            });
            return true;
        }

        static bool TryCompileAiFollowCell(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (!TrySplitOptionalExplicitCommand(line, "aifollowcell", explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out var targetMode, out uint targetPlacedRefId, out string commandLine, out failure))
                return false;

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length < 7)
            {
                failure = $"AiFollowCell command requires target actor, cell id, duration, x, y, and z in MWScript V2: '{line}'.";
                return false;
            }

            if (!IsPlayerTarget(tokens[1]))
            {
                failure = $"AiFollowCell command supports only Player follow target in MWScript V2: '{line}'.";
                return false;
            }

            string cellId = NormalizeToken(tokens[2]).Trim('"');
            if (string.IsNullOrWhiteSpace(cellId))
            {
                failure = $"AiFollowCell command has empty cell id: '{line}'.";
                return false;
            }

            if (!TryParseAiFollowNumbers(tokens, 3, line, out float duration, out float x, out float y, out float z, out failure))
                return false;

            for (int i = 7; i < tokens.Length; i++)
            {
                if (!float.TryParse(NormalizeToken(tokens[i]), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    failure = $"AiFollowCell command has invalid reset argument: '{line}'.";
                    return false;
                }
            }

            ulong cellHash = HashInteriorCellId(cellId);
            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.AiFollowCell,
                Operand0 = (byte)targetMode,
                Operand1 = tokens.Length > 7 ? (short)1 : (short)0,
                Int0 = unchecked((int)targetPlacedRefId),
                Int1 = unchecked((int)(cellHash & 0xFFFFFFFFu)),
                Int2 = unchecked((int)(cellHash >> 32)),
                Float0 = duration,
                Float1 = x * WorldScale.MwUnitsToMeters,
                Float2 = z * WorldScale.MwUnitsToMeters,
                Float3 = y * WorldScale.MwUnitsToMeters,
            });
            return true;
        }

        static bool TryCompileStopCombat(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (!TrySplitOptionalExplicitCommand(line, "stopcombat", explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out var targetMode, out uint targetPlacedRefId, out string commandLine, out failure))
                return false;

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length < 1 || tokens.Length > 2)
            {
                failure = $"StopCombat command takes at most one ignored string argument in MWScript V2: '{line}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.StopCombat,
                Operand0 = (byte)targetMode,
                Int0 = unchecked((int)targetPlacedRefId),
            });
            return true;
        }

        static bool TryCompileStartCombat(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (!TrySplitOptionalExplicitCommand(line, "startcombat", explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out var targetMode, out uint targetPlacedRefId, out string commandLine, out failure))
                return false;

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length != 2)
            {
                failure = $"StartCombat command requires one target in MWScript V2: '{line}'.";
                return false;
            }

            if (!TryResolveCastTarget(tokens[1], explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out var combatTargetMode, out uint combatTargetRefKey, out failure))
                return false;

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.StartCombat,
                Operand0 = (byte)targetMode,
                Operand1 = (short)combatTargetMode,
                Int0 = unchecked((int)targetPlacedRefId),
                Int1 = unchecked((int)combatTargetRefKey),
            });
            return true;
        }

        static bool TryCompileActorAiSetting(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            var targetMode = MorrowindScriptRefTargetMode.Self;
            uint targetPlacedRefId = 0u;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid actor AI setting command '{line}'.";
                    return false;
                }
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length == 0)
                return false;

            string commandToken = tokens[0];

            if (!TryMapActorAiSettingCommand(commandToken, out var settingKind, out bool isMod))
                return false;

            if (explicitTargetId != null)
            {
                if (!TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetPlacedRefId, out failure))
                    return false;
            }

            if (tokens.Length != 2 || !int.TryParse(NormalizeToken(tokens[1]), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                failure = $"{commandToken} command requires one integer value in MWScript V2: '{line}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.SetActorAiSetting,
                Operand0 = (byte)targetMode,
                Operand1 = (short)settingKind,
                Int0 = unchecked((int)targetPlacedRefId),
                Int1 = value,
                Int2 = isMod ? 1 : 0,
            });
            return true;
        }

        static bool TryMapActorAiSettingCommand(
            string command,
            out MorrowindScriptActorAiSettingKind settingKind,
            out bool isMod)
        {
            settingKind = 0;
            isMod = false;
            if (string.Equals(command, "sethello", StringComparison.OrdinalIgnoreCase))
            {
                settingKind = MorrowindScriptActorAiSettingKind.Hello;
                return true;
            }

            if (string.Equals(command, "modhello", StringComparison.OrdinalIgnoreCase))
            {
                settingKind = MorrowindScriptActorAiSettingKind.Hello;
                isMod = true;
                return true;
            }

            if (string.Equals(command, "setfight", StringComparison.OrdinalIgnoreCase))
            {
                settingKind = MorrowindScriptActorAiSettingKind.Fight;
                return true;
            }

            if (string.Equals(command, "modfight", StringComparison.OrdinalIgnoreCase))
            {
                settingKind = MorrowindScriptActorAiSettingKind.Fight;
                isMod = true;
                return true;
            }

            if (string.Equals(command, "setflee", StringComparison.OrdinalIgnoreCase))
            {
                settingKind = MorrowindScriptActorAiSettingKind.Flee;
                return true;
            }

            if (string.Equals(command, "modflee", StringComparison.OrdinalIgnoreCase))
            {
                settingKind = MorrowindScriptActorAiSettingKind.Flee;
                isMod = true;
                return true;
            }

            if (string.Equals(command, "setalarm", StringComparison.OrdinalIgnoreCase))
            {
                settingKind = MorrowindScriptActorAiSettingKind.Alarm;
                return true;
            }

            if (string.Equals(command, "modalarm", StringComparison.OrdinalIgnoreCase))
            {
                settingKind = MorrowindScriptActorAiSettingKind.Alarm;
                isMod = true;
                return true;
            }

            return false;
        }

        static bool TryCompileDisposition(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            var targetMode = MorrowindScriptRefTargetMode.Self;
            uint targetPlacedRefId = 0u;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid disposition command '{line}'.";
                    return false;
                }
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length == 0)
                return false;

            bool isMod = string.Equals(tokens[0], "moddisposition", StringComparison.OrdinalIgnoreCase);
            bool isSet = string.Equals(tokens[0], "setdisposition", StringComparison.OrdinalIgnoreCase);
            if (!isMod && !isSet)
                return false;

            if (explicitTargetId != null)
            {
                if (!TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetPlacedRefId, out failure))
                    return false;
            }

            if (tokens.Length != 2 || !int.TryParse(NormalizeToken(tokens[1]), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                failure = $"{tokens[0]} command requires one integer value in MWScript V2: '{line}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.SetDisposition,
                Operand0 = (byte)targetMode,
                Int0 = unchecked((int)targetPlacedRefId),
                Int1 = value,
                Int2 = isMod ? 1 : 0,
            });
            return true;
        }

        static bool TryCompileSetHealth(
            string line,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, int> actorLookup,
            Dictionary<string, ContentReference> carryableLookup,
            Dictionary<string, SpellDefHandle> spellLookup,
            Dictionary<string, int> factionLookup,
            Dictionary<string, ActorLocalCompileInfo> actorLocalLookup,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            var targetMode = MorrowindScriptRefTargetMode.Self;
            uint targetPlacedRefId = 0u;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid SetHealth command '{line}'.";
                    return false;
                }
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length == 0 || !TryResolveActorVitalCommand(tokens[0], out byte vitalKind, out bool isMod))
                return false;

            if (explicitTargetId != null)
            {
                if (IsPlayerTarget(explicitTargetId))
                {
                    targetMode = MorrowindScriptRefTargetMode.Player;
                }
                else
                {
                    if (!TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetPlacedRefId, out failure))
                        return false;
                }
            }

            if (tokens.Length < 2)
            {
                failure = $"{tokens[0]} command requires one value expression in MWScript V2: '{line}'.";
                return false;
            }

            string expressionText = string.Join(" ", tokens, 1, tokens.Length - 1);
            if (!TryCompileArithmeticExpression(
                    expressionText,
                    localLookup,
                    globalLookup,
                    soundLookup,
                    actorLookup,
                    carryableLookup,
                    spellLookup,
                    factionLookup,
                    actorLocalLookup,
                    dialogueLookup,
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
                    instructions,
                    ref stackDepth,
                    ref maxStack,
                    out failure))
            {
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.SetHealth,
                Operand0 = (byte)targetMode,
                Operand1 = vitalKind,
                Int0 = unchecked((int)targetPlacedRefId),
                Int1 = 1,
                Int2 = isMod ? 1 : 0,
            });
            return true;
        }

        static bool TryCompileResurrect(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (!TrySplitOptionalExplicitCommand(
                    line,
                    "resurrect",
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
                    out var targetMode,
                    out uint targetPlacedRefId,
                    out string commandLine,
                    out failure))
            {
                return false;
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length != 1)
            {
                failure = $"Resurrect takes no arguments in MWScript V1: '{line}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.Resurrect,
                Operand0 = (byte)targetMode,
                Int0 = unchecked((int)targetPlacedRefId),
            });
            return true;
        }

        static bool TryCompileHurtStandingActor(
            string line,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, int> actorLookup,
            Dictionary<string, ContentReference> carryableLookup,
            Dictionary<string, SpellDefHandle> spellLookup,
            Dictionary<string, int> factionLookup,
            Dictionary<string, ActorLocalCompileInfo> actorLocalLookup,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            var targetMode = MorrowindScriptRefTargetMode.Self;
            uint targetPlacedRefId = 0u;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid HurtStandingActor command '{line}'.";
                    return false;
                }
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length == 0 || !tokens[0].Equals("hurtstandingactor", StringComparison.OrdinalIgnoreCase))
                return false;

            if (explicitTargetId != null
                && !TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetPlacedRefId, out failure))
            {
                return false;
            }

            if (tokens.Length < 2)
            {
                failure = $"HurtStandingActor requires one health-per-second expression in MWScript V2: '{line}'.";
                return false;
            }

            string expressionText = string.Join(" ", tokens, 1, tokens.Length - 1);
            if (!TryCompileArithmeticExpression(
                    expressionText,
                    localLookup,
                    globalLookup,
                    soundLookup,
                    actorLookup,
                    carryableLookup,
                    spellLookup,
                    factionLookup,
                    actorLocalLookup,
                    dialogueLookup,
                    explicitRefTargets,
                    ambiguousExplicitRefTargets,
                    explicitContentTargets,
                    instructions,
                    ref stackDepth,
                    ref maxStack,
                    out failure))
            {
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.HurtStandingActor,
                Operand0 = (byte)targetMode,
                Int0 = unchecked((int)targetPlacedRefId),
            });
            return true;
        }

        static bool TryResolveActorVitalCommand(string command, out byte vitalKind, out bool isMod)
        {
            vitalKind = 0;
            isMod = false;
            string normalized = NormalizeToken(command);
            if (string.Equals(normalized, "sethealth", StringComparison.OrdinalIgnoreCase))
                vitalKind = 1;
            else if (string.Equals(normalized, "setmagicka", StringComparison.OrdinalIgnoreCase))
                vitalKind = 2;
            else if (string.Equals(normalized, "setfatigue", StringComparison.OrdinalIgnoreCase))
                vitalKind = 3;
            else if (string.Equals(normalized, "modcurrenthealth", StringComparison.OrdinalIgnoreCase))
            {
                vitalKind = 1;
                isMod = true;
            }
            else if (string.Equals(normalized, "modhealth", StringComparison.OrdinalIgnoreCase))
            {
                vitalKind = 1;
                isMod = true;
            }
            else if (string.Equals(normalized, "modcurrentmagicka", StringComparison.OrdinalIgnoreCase))
            {
                vitalKind = 2;
                isMod = true;
            }
            else if (string.Equals(normalized, "modmagicka", StringComparison.OrdinalIgnoreCase))
            {
                vitalKind = 2;
                isMod = true;
            }
            else if (string.Equals(normalized, "modcurrentfatigue", StringComparison.OrdinalIgnoreCase))
            {
                vitalKind = 3;
                isMod = true;
            }
            else if (string.Equals(normalized, "modfatigue", StringComparison.OrdinalIgnoreCase))
            {
                vitalKind = 3;
                isMod = true;
            }

            return vitalKind != 0;
        }

        static bool TryCompileActorSpellCommand(
            string line,
            Dictionary<string, SpellDefHandle> spellLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            var targetMode = MorrowindScriptRefTargetMode.Self;
            uint targetPlacedRefId = 0u;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid AddSpell/RemoveSpell command '{line}'.";
                    return false;
                }
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length == 0)
                return false;

            bool add = string.Equals(tokens[0], "addspell", StringComparison.OrdinalIgnoreCase);
            bool remove = string.Equals(tokens[0], "removespell", StringComparison.OrdinalIgnoreCase);
            if (!add && !remove)
                return false;

            if (explicitTargetId != null)
            {
                if (IsPlayerTarget(explicitTargetId))
                {
                    targetMode = MorrowindScriptRefTargetMode.Player;
                }
                else
                {
                    if (!TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetPlacedRefId, out failure))
                        return false;
                }
            }

            if (tokens.Length != 2)
            {
                failure = $"{tokens[0]} command requires one spell id in MWScript V2: '{line}'.";
                return false;
            }

            string spellId = NormalizeToken(tokens[1]).Trim('"');
            if (string.IsNullOrWhiteSpace(spellId))
            {
                failure = $"{tokens[0]} command requires a non-empty spell id in MWScript V2: '{line}'.";
                return false;
            }

            if (spellLookup == null || !spellLookup.TryGetValue(ContentId.NormalizeId(spellId), out var spellHandle) || !spellHandle.IsValid)
            {
                failure = $"{tokens[0]} references unknown spell '{spellId}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)(add ? MorrowindScriptOpcode.AddSpell : MorrowindScriptOpcode.RemoveSpell),
                Operand0 = (byte)targetMode,
                Int0 = unchecked((int)targetPlacedRefId),
                Int1 = spellHandle.Value,
            });
            return true;
        }

        static bool TryCompileCast(
            string line,
            Dictionary<string, SpellDefHandle> spellLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            var casterMode = MorrowindScriptRefTargetMode.Self;
            uint casterRefKey = 0u;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid Cast command '{line}'.";
                    return false;
                }
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length == 0 || !string.Equals(tokens[0], "cast", StringComparison.OrdinalIgnoreCase))
                return false;

            if (explicitTargetId != null)
            {
                if (IsPlayerTarget(explicitTargetId))
                {
                    failure = $"Cast caster cannot be explicit Player in MWScript V2: '{line}'.";
                    return false;
                }

                if (!TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out casterMode, out casterRefKey, out failure))
                    return false;
            }

            if (tokens.Length != 3)
            {
                failure = $"Cast command requires one spell id and one target id in MWScript V2: '{line}'.";
                return false;
            }

            string spellId = NormalizeToken(tokens[1]).Trim('"');
            if (string.IsNullOrWhiteSpace(spellId))
            {
                failure = $"Cast command requires a non-empty spell id in MWScript V2: '{line}'.";
                return false;
            }

            if (spellLookup == null || !spellLookup.TryGetValue(ContentId.NormalizeId(spellId), out var spellHandle) || !spellHandle.IsValid)
            {
                failure = $"Cast references unknown spell '{spellId}'.";
                return false;
            }

            if (!TryResolveCastTarget(tokens[2], explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out var targetMode, out uint targetRefKey, out failure))
                return false;

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.Cast,
                Operand0 = (byte)casterMode,
                Operand1 = (short)targetMode,
                Int0 = unchecked((int)casterRefKey),
                Int1 = unchecked((int)targetRefKey),
                Int2 = spellHandle.Value,
            });
            return true;
        }

        static bool TryCompileForceGreeting(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            var targetMode = MorrowindScriptRefTargetMode.Self;
            uint targetRefKey = 0u;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid ForceGreeting command '{line}'.";
                    return false;
                }
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length == 0 || !string.Equals(tokens[0], "forcegreeting", StringComparison.OrdinalIgnoreCase))
                return false;

            if (tokens.Length != 1)
            {
                failure = $"ForceGreeting command takes no arguments in MWScript V2: '{line}'.";
                return false;
            }

            if (explicitTargetId != null)
            {
                if (IsPlayerTarget(explicitTargetId))
                {
                    failure = $"ForceGreeting target cannot be explicit Player in MWScript V2: '{line}'.";
                    return false;
                }

                if (!TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetRefKey, out failure))
                    return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.ForceGreeting,
                Operand0 = (byte)targetMode,
                Int0 = unchecked((int)targetRefKey),
            });
            return true;
        }

        static bool TryResolveCastTarget(
            string targetId,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            out MorrowindScriptRefTargetMode targetMode,
            out uint targetRefKey,
            out string failure)
        {
            failure = null;
            targetMode = MorrowindScriptRefTargetMode.Self;
            targetRefKey = 0u;
            if (IsPlayerTarget(targetId))
            {
                targetMode = MorrowindScriptRefTargetMode.Player;
                return true;
            }

            return TryResolveExplicitRefTarget(targetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetRefKey, out failure);
        }

        static bool TryCompileInventoryMutation(
            string line,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, int> actors,
            Dictionary<string, ContentReference> carryables,
            Dictionary<string, SpellDefHandle> spellLookup,
            Dictionary<string, int> factionLookup,
            Dictionary<string, ActorLocalCompileInfo> actorLocalLookup,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            var targetMode = MorrowindScriptRefTargetMode.Self;
            uint targetPlacedRefId = 0u;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid inventory command '{line}'.";
                    return false;
                }
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length == 0)
                return false;

            bool add = string.Equals(tokens[0], "additem", StringComparison.OrdinalIgnoreCase);
            bool remove = string.Equals(tokens[0], "removeitem", StringComparison.OrdinalIgnoreCase);
            bool removeSoulGem = string.Equals(tokens[0], "removesoulgem", StringComparison.OrdinalIgnoreCase);
            bool drop = string.Equals(tokens[0], "drop", StringComparison.OrdinalIgnoreCase);
            if (!add && !remove && !removeSoulGem && !drop)
                return false;

            if (explicitTargetId != null)
            {
                if (IsPlayerTarget(explicitTargetId))
                {
                    targetMode = MorrowindScriptRefTargetMode.Player;
                }
                else
                {
                    if (!TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetPlacedRefId, out failure))
                        return false;
                }
            }

            if (removeSoulGem)
            {
                if (targetMode != MorrowindScriptRefTargetMode.Player)
                {
                    failure = $"RemoveSoulGem supports only explicit Player target in MWScript V2: '{line}'.";
                    return false;
                }

                if (tokens.Length < 2 || tokens.Length > 3)
                {
                    failure = $"RemoveSoulGem command requires a creature id and at most one ignored integer count in MWScript V2: '{line}'.";
                    return false;
                }

                if (tokens.Length == 3
                    && !int.TryParse(NormalizeToken(tokens[2]), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    failure = $"RemoveSoulGem optional count must be a literal integer in MWScript V2: '{line}'.";
                    return false;
                }

                string soulId = tokens[1].Trim('"');
                if (string.IsNullOrWhiteSpace(soulId))
                {
                    failure = $"RemoveSoulGem command requires a creature id in MWScript V2: '{line}'.";
                    return false;
                }

                if (!actors.TryGetValue(ContentId.NormalizeId(soulId), out int actorIndex))
                {
                    failure = $"RemoveSoulGem references unknown actor '{soulId}'.";
                    return false;
                }

                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)MorrowindScriptOpcode.RequestInventoryMutation,
                    Operand0 = (byte)targetMode,
                    Int1 = ActorDefHandle.FromIndex(actorIndex).Value,
                    Int2 = 1,
                    Float0 = 2f,
                });
                return true;
            }

            if (tokens.Length < 3)
            {
                failure = $"{tokens[0]} command requires item id and count in MWScript V2: '{line}'.";
                return false;
            }

            string itemId = NormalizeGoldId(tokens[1].Trim('"'));
            if (!carryables.TryGetValue(ContentId.NormalizeId(itemId), out var content) || !content.IsValid)
            {
                failure = $"{tokens[0]} command references unknown carryable '{itemId}'.";
                return false;
            }

            if (content.Kind == ContentReferenceKind.LeveledItem)
            {
                failure = $"{tokens[0]} command does not support item leveled-list ids in MWScript V2: '{line}'.";
                return false;
            }

            int count = 0;
            bool countFromStack = false;
            if (tokens.Length == 3
                && int.TryParse(NormalizeToken(tokens[2]), NumberStyles.Integer, CultureInfo.InvariantCulture, out count))
            {
                if (drop && count < 0)
                {
                    failure = $"Drop count must be non-negative in MWScript V2: '{line}'.";
                    return false;
                }
            }
            else
            {
                string countExpression = string.Join(" ", tokens, 2, tokens.Length - 2);
                int expressionStart = instructions.Count;
                int expressionStackDepth = stackDepth;
                if (!TryCompileArithmeticExpression(
                        countExpression,
                        localLookup,
                        globalLookup,
                        soundLookup,
                        actors,
                        carryables,
                        spellLookup,
                        factionLookup,
                        actorLocalLookup,
                        dialogueLookup,
                        explicitRefTargets,
                        ambiguousExplicitRefTargets,
                        explicitContentTargets,
                        instructions,
                        ref stackDepth,
                        ref maxStack,
                        out failure))
                {
                    instructions.RemoveRange(expressionStart, instructions.Count - expressionStart);
                    stackDepth = expressionStackDepth;
                    return false;
                }

                countFromStack = true;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)(drop ? MorrowindScriptOpcode.Drop : MorrowindScriptOpcode.RequestInventoryMutation),
                Operand0 = (byte)targetMode,
                Operand1 = (short)content.Kind,
                Int0 = unchecked((int)targetPlacedRefId),
                Int1 = content.HandleValue,
                Int2 = count,
                Float0 = remove ? 1f : 0f,
                Float1 = countFromStack ? 1f : 0f,
            });
            if (countFromStack)
            {
                stackDepth = Math.Max(0, stackDepth - 1);
            }

            return true;
        }

        static bool TryCompileMessageBox(
            string line,
            Dictionary<string, (int Index, byte Kind)> localLookup,
            Dictionary<string, (int Index, byte Kind)> globalLookup,
            Dictionary<string, SoundDefHandle> soundLookup,
            Dictionary<string, int> actorLookup,
            Dictionary<string, ContentReference> carryableLookup,
            Dictionary<string, SpellDefHandle> spellLookup,
            Dictionary<string, int> factionLookup,
            Dictionary<string, ActorLocalCompileInfo> actorLocalLookup,
            Dictionary<string, DialogueCompileInfo> dialogueLookup,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptMessageDef> messages,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                string commandLine = StripExplicitReferencePrefix(line);
                if (!StartsWithCommand(commandLine, "messagebox"))
                    return false;

                failure = $"Explicit MessageBox references are not supported in MWScript V1: '{line}'.";
                return false;
            }

            if (!StartsWithCommand(line, "messagebox"))
                return false;

            if (!TryParseMessageBoxTokens(line, out var tokens, out failure))
                return false;

            if (tokens.Count < 2
                || !string.Equals(tokens[0].Text, "messagebox", StringComparison.OrdinalIgnoreCase)
                || !tokens[1].Quoted
                || string.IsNullOrWhiteSpace(tokens[1].Text))
            {
                failure = $"MessageBox requires one literal message string in MWScript V1: '{line}'.";
                return false;
            }

            if (!TryGetMessageBoxFormatArgCount(tokens[1].Text, out int argCount, out failure))
            {
                failure = $"{failure}: '{line}'.";
                return false;
            }

            if (argCount > 8)
            {
                failure = $"MessageBox supports at most 8 format arguments in MWScript V2: '{line}'.";
                return false;
            }

            int argStart = 2;
            int buttonStart = argStart + argCount;
            if (tokens.Count < buttonStart)
            {
                failure = $"MessageBox format string requires {argCount} argument(s) in MWScript V2: '{line}'.";
                return false;
            }

            for (int i = 0; i < argCount; i++)
            {
                var arg = tokens[argStart + i];
                if (arg.Quoted)
                {
                    failure = $"MessageBox format argument {i + 1} must be a numeric expression in MWScript V2: '{line}'.";
                    return false;
                }

                string[] argTokens = SplitCommandTokens(arg.Text);
                if (!TryCompileExpression(
                        argTokens,
                        0,
                        argTokens.Length,
                        localLookup,
                        globalLookup,
                        soundLookup,
                        actorLookup,
                        carryableLookup,
                        spellLookup,
                        factionLookup,
                        actorLocalLookup,
                        dialogueLookup,
                        explicitRefTargets,
                        ambiguousExplicitRefTargets,
                        explicitContentTargets,
                        MorrowindScriptRefTargetMode.Self,
                        0u,
                        instructions,
                        ref stackDepth,
                        ref maxStack,
                        out failure))
                {
                    failure = $"MessageBox format argument {i + 1} is unsupported: {failure}";
                    return false;
                }
            }

            int quotedButtonCount = 0;
            for (int i = buttonStart; i < tokens.Count; i++)
            {
                if (tokens[i].Quoted)
                    quotedButtonCount++;
            }

            int buttonCount = Math.Min(10, quotedButtonCount);
            int messageIndex = messages.Count;
            messages.Add(new MorrowindScriptMessageDef
            {
                Text = tokens[1].Text,
            });

            int firstButtonIndex = messages.Count;
            for (int i = 0; i < buttonCount; i++)
            {
                messages.Add(new MorrowindScriptMessageDef
                {
                    Text = tokens[buttonStart + i].Text,
                });
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.RequestMessageBox,
                Operand0 = (byte)buttonCount,
                Operand1 = (short)argCount,
                Int0 = messageIndex,
                Int1 = firstButtonIndex,
            });
            stackDepth = Math.Max(0, stackDepth - argCount);
            return true;
        }

        static bool TryParseMessageBoxTokens(string line, out List<MessageBoxToken> tokens, out string failure)
        {
            tokens = new List<MessageBoxToken>();
            failure = null;
            int i = 0;
            while (i < line.Length)
            {
                while (i < line.Length && (char.IsWhiteSpace(line[i]) || line[i] == ','))
                    i++;
                if (i >= line.Length)
                    break;

                if (line[i] == '"')
                {
                    i++;
                    int start = i;
                    while (i < line.Length && line[i] != '"')
                        i++;
                    if (i >= line.Length)
                    {
                        failure = $"MessageBox has an unterminated literal string: '{line}'.";
                        return false;
                    }

                    tokens.Add(new MessageBoxToken(line.Substring(start, i - start), true));
                    i++;
                    continue;
                }

                int tokenStart = i;
                while (i < line.Length && !char.IsWhiteSpace(line[i]) && line[i] != ',')
                    i++;
                string token = NormalizeToken(line.Substring(tokenStart, i - tokenStart));
                if (!string.IsNullOrWhiteSpace(token))
                    tokens.Add(new MessageBoxToken(token, false));
            }

            return true;
        }

        static bool TryGetMessageBoxFormatArgCount(string message, out int argCount, out string failure)
        {
            argCount = 0;
            failure = null;
            for (int i = 0; i < message.Length; i++)
            {
                if (message[i] != '%')
                    continue;

                if (++i >= message.Length)
                {
                    failure = "MessageBox format placeholder is incomplete";
                    return false;
                }

                if (message[i] == '%')
                    continue;

                while (i < message.Length && (message[i] == '-' || message[i] == '+' || message[i] == ' ' || message[i] == '0' || message[i] == '#'))
                    i++;
                while (i < message.Length && char.IsDigit(message[i]))
                    i++;
                if (i < message.Length && message[i] == '.')
                {
                    i++;
                    while (i < message.Length && char.IsDigit(message[i]))
                        i++;
                }

                if (i >= message.Length)
                {
                    failure = "MessageBox format placeholder is incomplete";
                    return false;
                }

                char placeholder = message[i];
                if (placeholder is 'd' or 'i' or 'f' or 'F' or 'e' or 'E' or 'g' or 'G')
                {
                    argCount++;
                    continue;
                }

                if (placeholder is 's' or 'S')
                    failure = "MessageBox string format placeholders are not supported in MWScript V2";
                else
                    failure = $"MessageBox format placeholder '%{placeholder}' is not supported in MWScript V2";
                return false;
            }

            return true;
        }

        static bool TryCompileShowMap(
            string line,
            List<MorrowindScriptMessageDef> messages,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                string commandLine = StripExplicitReferencePrefix(line);
                if (!StartsWithCommand(commandLine, "showmap"))
                    return false;

                failure = $"Explicit ShowMap references are not supported in MWScript V1: '{line}'.";
                return false;
            }

            if (!StartsWithCommand(line, "showmap"))
                return false;

            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 2 || string.IsNullOrWhiteSpace(tokens[1]))
            {
                failure = $"ShowMap requires one literal cell name prefix in MWScript V1: '{line}'.";
                return false;
            }

            int messageIndex = messages.Count;
            messages.Add(new MorrowindScriptMessageDef
            {
                Text = tokens[1],
            });
            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.ShowMap,
                Int0 = messageIndex,
            });
            return true;
        }

        static bool TryCompileShowRestMenu(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            var targetMode = MorrowindScriptRefTargetMode.Self;
            uint targetRefKey = 0u;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                commandLine = StripExplicitReferencePrefix(line);
                if (!StartsWithCommand(commandLine, "showrestmenu"))
                    return false;

                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid explicit ShowRestMenu command '{line}'.";
                    return false;
                }
            }

            if (!StartsWithCommand(commandLine, "showrestmenu"))
                return false;

            if (explicitTargetId != null
                && !TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetRefKey, out failure))
            {
                return false;
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length != 1)
            {
                failure = $"ShowRestMenu takes no arguments in MWScript V1: '{line}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.ShowRestMenu,
                Operand0 = (byte)targetMode,
                Int0 = unchecked((int)targetRefKey),
            });
            return true;
        }

        static bool TryCompileWakeUpPC(
            string line,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                string commandLine = StripExplicitReferencePrefix(line);
                if (!StartsWithCommand(commandLine, "wakeuppc"))
                    return false;

                failure = $"Explicit WakeUpPC references are not supported in MWScript V1: '{line}'.";
                return false;
            }

            if (!StartsWithCommand(line, "wakeuppc"))
                return false;

            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 1)
            {
                failure = $"WakeUpPC takes no arguments in MWScript V1: '{line}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.WakeUpPC,
            });
            return true;
        }

        static bool TryCompileScreenFade(
            string line,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                string commandLine = StripExplicitReferencePrefix(line);
                if (!StartsWithCommand(commandLine, "fadeout") && !StartsWithCommand(commandLine, "fadein"))
                    return false;

                failure = $"Explicit screen fade references are not supported in MWScript V1: '{line}'.";
                return false;
            }

            bool fadeOut = StartsWithCommand(line, "fadeout");
            if (!fadeOut && !StartsWithCommand(line, "fadein"))
                return false;

            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 2 || !float.TryParse(NormalizeToken(tokens[1]), NumberStyles.Float, CultureInfo.InvariantCulture, out float duration))
            {
                failure = $"Screen fade commands require one literal duration in MWScript V1: '{line}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.ScreenFade,
                Operand0 = fadeOut ? (byte)1 : (byte)0,
                Float0 = duration,
            });
            return true;
        }

        static bool TryCompileShellControl(
            string line,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                string commandLine = StripExplicitReferencePrefix(line);
                if (!TryResolveShellControl(commandLine, out _, out _, out _))
                    return false;

                failure = $"Explicit shell control references are not supported in MWScript V1: '{line}'.";
                return false;
            }

            if (!TryResolveShellControl(line, out byte operation, out byte enabled, out byte menuKind))
                return false;

            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 1)
            {
                failure = $"Shell control command takes no arguments in MWScript V1: '{line}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.ShellControl,
                Operand0 = operation,
                Int0 = enabled,
                Int1 = menuKind,
            });
            return true;
        }

        static bool TryCompileChangeWeather(
            string line,
            Dictionary<string, RegionDefHandle> regions,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                string commandLine = StripExplicitReferencePrefix(line);
                if (!StartsWithCommand(commandLine, "changeweather"))
                    return false;

                failure = $"Explicit ChangeWeather references are not supported in MWScript V1: '{line}'.";
                return false;
            }

            if (!StartsWithCommand(line, "changeweather"))
                return false;

            string[] tokens = SplitConditionTokens(line);
            if (tokens.Length != 3)
            {
                failure = $"ChangeWeather requires region id and literal weather index in MWScript V1: '{line}'.";
                return false;
            }

            string regionId = NormalizeToken(tokens[1]).Trim('"');
            if (string.IsNullOrWhiteSpace(regionId))
            {
                failure = "ChangeWeather requires one region id.";
                return false;
            }

            if (regions == null || !regions.TryGetValue(ContentId.NormalizeId(regionId), out var region) || !region.IsValid)
            {
                failure = $"ChangeWeather references unknown region '{regionId}'.";
                return false;
            }

            if (!int.TryParse(NormalizeToken(tokens[2]), NumberStyles.Integer, CultureInfo.InvariantCulture, out int weather)
                || weather < 0
                || weather > (int)WeatherKind.Blizzard)
            {
                failure = $"ChangeWeather weather index must be 0-{(int)WeatherKind.Blizzard} in MWScript V1: '{line}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.ChangeWeather,
                Int0 = region.Value,
                Int1 = weather,
            });
            return true;
        }

        static bool TryCompileModRegion(
            string line,
            Dictionary<string, RegionDefHandle> regions,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                string commandLine = StripExplicitReferencePrefix(line);
                if (!StartsWithCommand(commandLine, "modregion"))
                    return false;

                failure = $"Explicit ModRegion references are not supported in MWScript V1: '{line}'.";
                return false;
            }

            if (!StartsWithCommand(line, "modregion"))
                return false;

            string[] tokens = SplitConditionTokens(line);
            if (tokens.Length < 3 || tokens.Length > 12)
            {
                failure = $"ModRegion requires region id and one to ten literal weather chances in MWScript V1: '{line}'.";
                return false;
            }

            string regionId = NormalizeToken(tokens[1]).Trim('"');
            if (string.IsNullOrWhiteSpace(regionId))
            {
                failure = "ModRegion requires one region id.";
                return false;
            }

            if (regions == null || !regions.TryGetValue(ContentId.NormalizeId(regionId), out var region) || !region.IsValid)
            {
                failure = $"ModRegion references unknown region '{regionId}'.";
                return false;
            }

            Span<int> chances = stackalloc int[10];
            chances.Clear();
            int chanceCount = tokens.Length - 2;
            for (int i = 0; i < chanceCount; i++)
            {
                if (!int.TryParse(NormalizeToken(tokens[i + 2]), NumberStyles.Integer, CultureInfo.InvariantCulture, out int chance))
                {
                    failure = $"ModRegion weather chances must be literal integers in MWScript V1: '{line}'.";
                    return false;
                }

                chances[i] = Math.Clamp(chance, 0, 100);
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.ModRegion,
                Operand0 = (byte)chances[8],
                Operand1 = (short)chances[9],
                Int0 = region.Value,
                Int1 = PackChances(chances[0], chances[1], chances[2], chances[3]),
                Int2 = PackChances(chances[4], chances[5], chances[6], chances[7]),
            });
            return true;
        }

        static int PackChances(int a, int b, int c, int d)
            => (a & 0xFF)
               | ((b & 0xFF) << 8)
               | ((c & 0xFF) << 16)
               | ((d & 0xFF) << 24);

        static bool TryCompilePlayBink(
            string line,
            List<MorrowindScriptMessageDef> messages,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                string commandLine = StripExplicitReferencePrefix(line);
                if (!StartsWithCommand(commandLine, "playbink"))
                    return false;

                failure = $"Explicit PlayBink references are not supported in MWScript V1: '{line}'.";
                return false;
            }

            if (!StartsWithCommand(line, "playbink"))
                return false;

            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 3)
            {
                failure = $"PlayBink requires a literal movie path and allow-skip integer in MWScript V1: '{line}'.";
                return false;
            }

            string movieName = NormalizeToken(tokens[1]).Trim('"').Trim();
            if (string.IsNullOrWhiteSpace(movieName))
            {
                failure = "PlayBink requires one non-empty movie path.";
                return false;
            }

            if (!int.TryParse(NormalizeToken(tokens[2]), NumberStyles.Integer, CultureInfo.InvariantCulture, out int allowSkipping))
            {
                failure = $"PlayBink allow-skip flag must be a literal integer in MWScript V1: '{line}'.";
                return false;
            }

            int movieIndex = messages.Count;
            messages.Add(new MorrowindScriptMessageDef { Text = movieName });
            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.PlayBink,
                Int0 = movieIndex,
                Int1 = allowSkipping != 0 ? 1 : 0,
            });
            return true;
        }

        static bool TryCompileStreamMusic(
            string line,
            Dictionary<string, MusicTrackDefHandle> musicTracks,
            List<MorrowindScriptMessageDef> messages,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                string commandLine = StripExplicitReferencePrefix(line);
                if (!StartsWithCommand(commandLine, "streammusic"))
                    return false;

                failure = $"Explicit StreamMusic references are not supported in MWScript V1: '{line}'.";
                return false;
            }

            if (!StartsWithCommand(line, "streammusic"))
                return false;

            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length != 2)
            {
                failure = $"StreamMusic requires one literal music path in MWScript V1: '{line}'.";
                return false;
            }

            string musicPath = NormalizeMusicPath(NormalizeToken(tokens[1]).Trim('"'));
            if (string.IsNullOrWhiteSpace(musicPath))
            {
                failure = "StreamMusic requires one non-empty music path.";
                return false;
            }

            int trackValue = 0;
            int pathIndex = -1;
            if (musicTracks != null && musicTracks.TryGetValue(musicPath, out var track) && track.IsValid)
                trackValue = track.Value;
            else
            {
                pathIndex = messages.Count;
                messages.Add(new MorrowindScriptMessageDef { Text = musicPath });
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.StreamMusic,
                Int0 = trackValue,
                Int1 = pathIndex,
            });
            return true;
        }

        static bool TryResolveShellControl(string line, out byte operation, out byte enabled, out byte menuKind)
        {
            operation = 0;
            enabled = 0;
            menuKind = 0;
            string[] tokens = SplitCommandTokens(line);
            if (tokens.Length == 0)
                return false;

            string command = NormalizeToken(tokens[0]);
            if (string.Equals(command, "disableplayercontrols", StringComparison.OrdinalIgnoreCase))
            {
                operation = 3;
                enabled = 0;
                return true;
            }

            if (string.Equals(command, "enableplayercontrols", StringComparison.OrdinalIgnoreCase))
            {
                operation = 3;
                enabled = 1;
                return true;
            }

            if (string.Equals(command, "disableplayerfighting", StringComparison.OrdinalIgnoreCase))
            {
                operation = 6;
                enabled = 0;
                return true;
            }

            if (string.Equals(command, "enableplayerfighting", StringComparison.OrdinalIgnoreCase))
            {
                operation = 6;
                enabled = 1;
                return true;
            }

            if (string.Equals(command, "disableplayerjumping", StringComparison.OrdinalIgnoreCase))
            {
                operation = 7;
                enabled = 0;
                return true;
            }

            if (string.Equals(command, "enableplayerjumping", StringComparison.OrdinalIgnoreCase))
            {
                operation = 7;
                enabled = 1;
                return true;
            }

            if (string.Equals(command, "disableplayerlooking", StringComparison.OrdinalIgnoreCase))
            {
                operation = 14;
                enabled = 0;
                return true;
            }

            if (string.Equals(command, "enableplayerlooking", StringComparison.OrdinalIgnoreCase))
            {
                operation = 14;
                enabled = 1;
                return true;
            }

            if (string.Equals(command, "disableplayermagic", StringComparison.OrdinalIgnoreCase))
            {
                operation = 8;
                enabled = 0;
                return true;
            }

            if (string.Equals(command, "enableplayermagic", StringComparison.OrdinalIgnoreCase))
            {
                operation = 8;
                enabled = 1;
                return true;
            }

            if (string.Equals(command, "disableplayerviewswitch", StringComparison.OrdinalIgnoreCase))
            {
                operation = 9;
                enabled = 0;
                return true;
            }

            if (string.Equals(command, "enableplayerviewswitch", StringComparison.OrdinalIgnoreCase))
            {
                operation = 9;
                enabled = 1;
                return true;
            }

            if (string.Equals(command, "disablevanitymode", StringComparison.OrdinalIgnoreCase))
            {
                operation = 11;
                enabled = 0;
                return true;
            }

            if (string.Equals(command, "enablevanitymode", StringComparison.OrdinalIgnoreCase))
            {
                operation = 11;
                enabled = 1;
                return true;
            }

            if (string.Equals(command, "disablerest", StringComparison.OrdinalIgnoreCase))
            {
                operation = 10;
                enabled = 0;
                return true;
            }

            if (string.Equals(command, "enablerest", StringComparison.OrdinalIgnoreCase))
            {
                operation = 10;
                enabled = 1;
                return true;
            }

            if (string.Equals(command, "disableteleporting", StringComparison.OrdinalIgnoreCase))
            {
                operation = 4;
                enabled = 0;
                return true;
            }

            if (string.Equals(command, "enableteleporting", StringComparison.OrdinalIgnoreCase))
            {
                operation = 4;
                enabled = 1;
                return true;
            }

            if (TryResolveShellMenuControl(command, out enabled, out menuKind))
            {
                operation = 5;
                return true;
            }

            return false;
        }

        static bool TryResolveShellMenuControl(string command, out byte enabled, out byte menuKind)
        {
            enabled = 0;
            menuKind = 0;
            bool enable = command.StartsWith("enable", StringComparison.OrdinalIgnoreCase);
            bool disable = command.StartsWith("disable", StringComparison.OrdinalIgnoreCase);
            if (!enable && !disable)
                return false;

            string suffix = command.Substring(enable ? "enable".Length : "disable".Length);
            if (string.Equals(suffix, "inventorymenu", StringComparison.OrdinalIgnoreCase))
                menuKind = 1;
            else if (string.Equals(suffix, "statsmenu", StringComparison.OrdinalIgnoreCase))
                menuKind = 2;
            else if (string.Equals(suffix, "magicmenu", StringComparison.OrdinalIgnoreCase))
                menuKind = 3;
            else if (string.Equals(suffix, "mapmenu", StringComparison.OrdinalIgnoreCase))
                menuKind = 4;
            else if (string.Equals(suffix, "namemenu", StringComparison.OrdinalIgnoreCase))
                menuKind = 5;
            else if (string.Equals(suffix, "racemenu", StringComparison.OrdinalIgnoreCase))
                menuKind = 6;
            else if (string.Equals(suffix, "classmenu", StringComparison.OrdinalIgnoreCase))
                menuKind = 7;
            else if (string.Equals(suffix, "birthmenu", StringComparison.OrdinalIgnoreCase))
                menuKind = 8;
            else if (string.Equals(suffix, "statreviewmenu", StringComparison.OrdinalIgnoreCase))
                menuKind = 9;
            else
                return false;

            enabled = enable ? (byte)1 : (byte)0;
            return true;
        }

        static bool TryCompileSay(
            string line,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            List<MorrowindScriptMessageDef> messages,
            List<MorrowindScriptInstructionDef> instructions,
            out string failure)
        {
            failure = null;
            var targetMode = MorrowindScriptRefTargetMode.Self;
            uint targetPlacedRefId = 0u;
            string commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid Say command '{line}'.";
                    return false;
                }
            }

            string[] tokens = SplitCommandTokens(commandLine);
            if (tokens.Length == 0 || !string.Equals(tokens[0], "say", StringComparison.OrdinalIgnoreCase))
                return false;

            if (explicitTargetId != null)
            {
                if (IsPlayerTarget(explicitTargetId))
                {
                    targetMode = MorrowindScriptRefTargetMode.Player;
                }
                else
                {
                    if (!TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetPlacedRefId, out failure))
                        return false;
                }
            }

            if (tokens.Length != 3)
            {
                failure = $"Say command requires one voice path and one subtitle string in MWScript V1: '{line}'.";
                return false;
            }

            string voicePath = NormalizeToken(tokens[1]).Trim('"');
            string subtitle = tokens[2] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(voicePath))
            {
                failure = $"Say command requires a non-empty voice path in MWScript V1: '{line}'.";
                return false;
            }

            int voicePathIndex = messages.Count;
            messages.Add(new MorrowindScriptMessageDef { Text = voicePath });
            int subtitleIndex = messages.Count;
            messages.Add(new MorrowindScriptMessageDef { Text = subtitle });
            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.Say,
                Operand0 = (byte)targetMode,
                Int0 = unchecked((int)targetPlacedRefId),
                Int1 = voicePathIndex,
                Int2 = subtitleIndex,
            });
            return true;
        }

        static bool TryParseAiFollowNumbers(
            string[] tokens,
            int start,
            string line,
            out float duration,
            out float x,
            out float y,
            out float z,
            out string failure)
        {
            duration = 0f;
            x = 0f;
            y = 0f;
            z = 0f;
            failure = null;
            if (!float.TryParse(NormalizeToken(tokens[start]), NumberStyles.Float, CultureInfo.InvariantCulture, out duration)
                || !float.TryParse(NormalizeToken(tokens[start + 1]), NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                || !float.TryParse(NormalizeToken(tokens[start + 2]), NumberStyles.Float, CultureInfo.InvariantCulture, out y)
                || !float.TryParse(NormalizeToken(tokens[start + 3]), NumberStyles.Float, CultureInfo.InvariantCulture, out z))
            {
                failure = $"AiFollow command has invalid duration or coordinates: '{line}'.";
                return false;
            }

            return true;
        }

        static bool TryCompileGetDistanceExpression(
            string[] tokens,
            int start,
            int count,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            MorrowindScriptRefTargetMode sourceMode,
            uint sourceRefKey,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            if (count < 2 || !tokens[start].Equals("getdistance", StringComparison.OrdinalIgnoreCase))
                return false;

            string targetId = string.Join(" ", tokens, start + 1, count - 1).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(targetId))
            {
                failure = $"GetDistance requires one target id in MWScript V1: '{string.Join(" ", tokens, start, count)}'.";
                return false;
            }

            if (!TryResolveDistanceTarget(targetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out var targetMode, out uint targetRefKey, out failure))
                return false;

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.GetDistance,
                Operand0 = (byte)sourceMode,
                Operand1 = (short)targetMode,
                Int0 = unchecked((int)sourceRefKey),
                Int1 = unchecked((int)targetRefKey),
            });
            stackDepth++;
            maxStack = Math.Max(maxStack, stackDepth);
            return true;
        }

        static bool TryCompileGetPosExpression(
            string[] tokens,
            int start,
            int count,
            MorrowindScriptRefTargetMode targetMode,
            uint targetRefKey,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            if (count < 1 || !tokens[start].Equals("getpos", StringComparison.OrdinalIgnoreCase))
                return false;

            if (count != 2)
            {
                failure = $"GetPos requires one axis in MWScript V1: '{string.Join(" ", tokens, start, count)}'.";
                return false;
            }

            string axis = NormalizeToken(tokens[start + 1]).Trim('"');
            if (!TryMapRotateAxis(axis, out byte axisIndex))
            {
                failure = $"GetPos axis must be X, Y, or Z in MWScript V1: '{string.Join(" ", tokens, start, count)}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.GetPos,
                Operand0 = (byte)targetMode,
                Operand1 = axisIndex,
                Int0 = unchecked((int)targetRefKey),
            });
            stackDepth++;
            maxStack = Math.Max(maxStack, stackDepth);
            return true;
        }

        static bool TryCompileGetTargetExpression(
            string[] tokens,
            int start,
            int count,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            MorrowindScriptRefTargetMode sourceMode,
            uint sourceRefKey,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            if (count < 1 || !tokens[start].Equals("gettarget", StringComparison.OrdinalIgnoreCase))
                return false;

            if (count < 2)
            {
                failure = $"GetTarget requires one target id in MWScript V1: '{string.Join(" ", tokens, start, count)}'.";
                return false;
            }

            string targetId = string.Join(" ", tokens, start + 1, count - 1).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(targetId))
            {
                failure = $"GetTarget requires one target id in MWScript V1: '{string.Join(" ", tokens, start, count)}'.";
                return false;
            }

            if (!TryResolveDistanceTarget(targetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out var targetMode, out uint targetRefKey, out failure))
                return false;

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.GetTarget,
                Operand0 = (byte)sourceMode,
                Operand1 = (short)targetMode,
                Int0 = unchecked((int)sourceRefKey),
                Int1 = unchecked((int)targetRefKey),
            });
            stackDepth++;
            maxStack = Math.Max(maxStack, stackDepth);
            return true;
        }

        static bool TryCompileActorLineOfSightExpression(
            string[] tokens,
            int start,
            int count,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            MorrowindScriptRefTargetMode sourceMode,
            uint sourceRefKey,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            if (count < 1)
                return false;

            bool isGetLos = tokens[start].Equals("getlos", StringComparison.OrdinalIgnoreCase);
            bool isGetDetected = tokens[start].Equals("getdetected", StringComparison.OrdinalIgnoreCase);
            if (!isGetLos && !isGetDetected)
                return false;

            if (count < 2)
            {
                failure = $"{tokens[start]} requires one actor id in MWScript V1: '{string.Join(" ", tokens, start, count)}'.";
                return false;
            }

            string targetId = string.Join(" ", tokens, start + 1, count - 1).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(targetId))
            {
                failure = $"{tokens[start]} requires one actor id in MWScript V1: '{string.Join(" ", tokens, start, count)}'.";
                return false;
            }

            if (!TryResolveDistanceTarget(targetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out var targetMode, out uint targetRefKey, out failure))
                return false;

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)(isGetLos ? MorrowindScriptOpcode.GetLOS : MorrowindScriptOpcode.GetDetected),
                Operand0 = (byte)sourceMode,
                Operand1 = (short)targetMode,
                Int0 = unchecked((int)sourceRefKey),
                Int1 = unchecked((int)targetRefKey),
            });
            stackDepth++;
            maxStack = Math.Max(maxStack, stackDepth);
            return true;
        }

        static bool TryCompileActorEventExpression(
            string[] tokens,
            int start,
            int count,
            Dictionary<string, ContentReference> carryableLookup,
            MorrowindScriptRefTargetMode targetMode,
            uint targetRefKey,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            if (count < 1)
                return false;

            if (count == 1 && TryMapActorEventOpcode(tokens[start], out var opcode))
            {
                instructions.Add(new MorrowindScriptInstructionDef
                {
                    Opcode = (byte)opcode,
                    Operand0 = (byte)targetMode,
                    Int0 = unchecked((int)targetRefKey),
                });
                stackDepth++;
                maxStack = Math.Max(maxStack, stackDepth);
                return true;
            }

            if (!tokens[start].Equals("hitonme", StringComparison.OrdinalIgnoreCase))
                return false;

            if (count < 2)
            {
                failure = $"HitOnMe requires one hit object id in MWScript V1: '{string.Join(" ", tokens, start, count)}'.";
                return false;
            }

            string objectId = string.Join(" ", tokens, start + 1, count - 1).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(objectId))
            {
                failure = $"HitOnMe requires one hit object id in MWScript V1: '{string.Join(" ", tokens, start, count)}'.";
                return false;
            }

            if (carryableLookup == null || !carryableLookup.TryGetValue(ContentId.NormalizeId(objectId), out var content) || !content.IsValid)
            {
                failure = $"HitOnMe references unknown carryable '{objectId}'.";
                return false;
            }

            if (content.Kind == ContentReferenceKind.LeveledItem)
            {
                failure = $"HitOnMe cannot compare leveled item list '{objectId}' in MWScript V1.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.HitOnMe,
                Operand0 = (byte)targetMode,
                Operand1 = (short)content.Kind,
                Int0 = unchecked((int)targetRefKey),
                Int1 = content.HandleValue,
            });
            stackDepth++;
            maxStack = Math.Max(maxStack, stackDepth);
            return true;
        }

        static bool TryMapActorEventOpcode(string token, out MorrowindScriptOpcode opcode)
        {
            if (token.Equals("getattacked", StringComparison.OrdinalIgnoreCase))
            {
                opcode = MorrowindScriptOpcode.GetAttacked;
                return true;
            }

            if (token.Equals("onmurder", StringComparison.OrdinalIgnoreCase))
            {
                opcode = MorrowindScriptOpcode.OnMurder;
                return true;
            }

            if (token.Equals("onknockout", StringComparison.OrdinalIgnoreCase))
            {
                opcode = MorrowindScriptOpcode.OnKnockout;
                return true;
            }

            opcode = default;
            return false;
        }

        static bool TryCompileGetPCRankExpression(
            string[] tokens,
            int start,
            int count,
            Dictionary<string, int> factionLookup,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            if (count < 1 || !tokens[start].Equals("getpcrank", StringComparison.OrdinalIgnoreCase))
                return false;

            if (count < 2)
            {
                failure = "GetPCRank requires one faction id.";
                return false;
            }

            string factionId = string.Join(" ", tokens, start + 1, count - 1).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(factionId))
            {
                failure = "GetPCRank requires one faction id.";
                return false;
            }

            if (!factionLookup.TryGetValue(ContentId.NormalizeId(factionId), out int factionIndex))
            {
                failure = $"GetPCRank references unknown faction '{factionId}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.GetPCRank,
                Int0 = factionIndex,
            });
            stackDepth++;
            maxStack = Math.Max(maxStack, stackDepth);
            return true;
        }

        static bool TryCompilePlayerFactionStatusExpression(
            string[] tokens,
            int start,
            int count,
            Dictionary<string, int> factionLookup,
            List<MorrowindScriptInstructionDef> instructions,
            ref int stackDepth,
            ref int maxStack,
            out string failure)
        {
            failure = null;
            if (count < 1 || !tokens[start].Equals("pcexpelled", StringComparison.OrdinalIgnoreCase))
                return false;

            if (count < 2)
            {
                failure = "PCExpelled requires one faction id.";
                return false;
            }

            string factionId = string.Join(" ", tokens, start + 1, count - 1).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(factionId))
            {
                failure = "PCExpelled requires one faction id.";
                return false;
            }

            if (!factionLookup.TryGetValue(ContentId.NormalizeId(factionId), out int factionIndex))
            {
                failure = $"PCExpelled references unknown faction '{factionId}'.";
                return false;
            }

            instructions.Add(new MorrowindScriptInstructionDef
            {
                Opcode = (byte)MorrowindScriptOpcode.PCExpelled,
                Int0 = factionIndex,
            });
            stackDepth++;
            maxStack = Math.Max(maxStack, stackDepth);
            return true;
        }

        static bool TryResolveDistanceTarget(
            string targetId,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            out MorrowindScriptRefTargetMode targetMode,
            out uint targetRefKey,
            out string failure)
        {
            if (IsPlayerTarget(targetId))
            {
                targetMode = MorrowindScriptRefTargetMode.Player;
                targetRefKey = 0u;
                failure = null;
                return true;
            }

            return TryResolveExplicitRefTarget(targetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetRefKey, out failure);
        }

        static bool TrySplitOptionalExplicitCommand(
            string line,
            string command,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            out MorrowindScriptRefTargetMode targetMode,
            out uint targetPlacedRefId,
            out string commandLine,
            out string failure)
        {
            failure = null;
            targetMode = MorrowindScriptRefTargetMode.Self;
            targetPlacedRefId = 0u;
            commandLine = line;
            string explicitTargetId = null;
            if (line.IndexOf("->", StringComparison.Ordinal) >= 0)
            {
                if (!TrySplitExplicitReference(line, out explicitTargetId, out commandLine))
                {
                    failure = $"Invalid explicit {command} command '{line}'.";
                    return false;
                }
            }

            if (!StartsWithCommand(commandLine, command))
                return false;

            if (explicitTargetId == null)
                return true;

            if (!TryResolveExplicitRefTarget(explicitTargetId, explicitRefTargets, ambiguousExplicitRefTargets, explicitContentTargets, out targetMode, out targetPlacedRefId, out failure))
                return false;
            return true;
        }

        static short ResolveAiWanderRepeat(string[] tokens)
        {
            int optionalArgCount = tokens.Length - 4;
            if (optionalArgCount <= 0)
                return 0;
            if (optionalArgCount <= 8)
                return 1;

            return int.TryParse(NormalizeToken(tokens[12]), NumberStyles.Integer, CultureInfo.InvariantCulture, out int repeat) && repeat != 0
                ? (short)1
                : (short)0;
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
            if (kind == MorrowindScriptAudioKind.PlayLoopSound3DVP
                || kind == MorrowindScriptAudioKind.PlaySound3DVP
                || kind == MorrowindScriptAudioKind.PlaySoundVP)
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
            else if (tokens.Length != 2)
            {
                failure = $"{command} requires exactly one sound id in MWScript V1: '{line}'.";
                return false;
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

        static bool TryMapRotateAxis(string axis, out byte axisIndex)
        {
            if (axis.Equals("x", StringComparison.OrdinalIgnoreCase))
            {
                axisIndex = 0;
                return true;
            }

            if (axis.Equals("y", StringComparison.OrdinalIgnoreCase))
            {
                axisIndex = 1;
                return true;
            }

            if (axis.Equals("z", StringComparison.OrdinalIgnoreCase))
            {
                axisIndex = 2;
                return true;
            }

            axisIndex = 0;
            return false;
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

        static bool TryMapDiseaseOpcode(string token, out MorrowindScriptOpcode opcode)
        {
            if (token.Equals("getcommondisease", StringComparison.OrdinalIgnoreCase))
            {
                opcode = MorrowindScriptOpcode.GetCommonDisease;
                return true;
            }

            if (token.Equals("getblightdisease", StringComparison.OrdinalIgnoreCase))
            {
                opcode = MorrowindScriptOpcode.GetBlightDisease;
                return true;
            }

            opcode = default;
            return false;
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
            else if (command.Equals("playsoundvp", StringComparison.OrdinalIgnoreCase))
                kind = MorrowindScriptAudioKind.PlaySoundVP;
            else if (command.Equals("playsound3d", StringComparison.OrdinalIgnoreCase))
                kind = MorrowindScriptAudioKind.PlaySound3D;
            else if (command.Equals("playsound3dvp", StringComparison.OrdinalIgnoreCase))
                kind = MorrowindScriptAudioKind.PlaySound3DVP;
            else if (command.Equals("playloopsound3d", StringComparison.OrdinalIgnoreCase))
                kind = MorrowindScriptAudioKind.PlayLoopSound3D;
            else if (command.Equals("playloopsound3dvp", StringComparison.OrdinalIgnoreCase))
                kind = MorrowindScriptAudioKind.PlayLoopSound3DVP;
            else if (command.Equals("stopsound", StringComparison.OrdinalIgnoreCase))
                kind = MorrowindScriptAudioKind.StopSound;
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

        static Dictionary<string, int> BuildScriptLookup(GenericRecordDef[] scripts)
        {
            var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (scripts == null)
                return lookup;

            for (int i = 0; i < scripts.Length; i++)
            {
                string normalizedId = ContentId.NormalizeId(scripts[i].Id);
                if (!string.IsNullOrEmpty(normalizedId))
                    lookup[normalizedId] = i;
            }

            return lookup;
        }

        static Dictionary<string, int> BuildActorLookup(ActorDef[] actors)
        {
            var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (actors == null)
                return lookup;

            for (int i = 0; i < actors.Length; i++)
            {
                string normalizedId = ContentId.NormalizeId(actors[i].Id);
                if (!string.IsNullOrEmpty(normalizedId))
                    lookup[normalizedId] = i;

                string normalizedOriginalId = ContentId.NormalizeId(actors[i].OriginalId);
                if (!string.IsNullOrEmpty(normalizedOriginalId) && !lookup.ContainsKey(normalizedOriginalId))
                    lookup[normalizedOriginalId] = i;
            }

            return lookup;
        }

        static Dictionary<string, ContentReference> BuildCarryableLookup(
            BaseDef[] items,
            LightDef[] lights,
            ItemLeveledListDef[] itemLeveledLists)
        {
            var lookup = new Dictionary<string, ContentReference>(StringComparer.OrdinalIgnoreCase);
            if (items != null)
            {
                for (int i = 0; i < items.Length; i++)
                    AddContentReference(lookup, items[i].Id, ContentReferenceKind.Item, ItemDefHandle.FromIndex(i).Value);
            }

            if (lights != null)
            {
                for (int i = 0; i < lights.Length; i++)
                    AddContentReference(lookup, lights[i].Id, ContentReferenceKind.Light, LightDefHandle.FromIndex(i).Value);
            }

            if (itemLeveledLists != null)
            {
                for (int i = 0; i < itemLeveledLists.Length; i++)
                    AddContentReference(lookup, itemLeveledLists[i].Id, ContentReferenceKind.LeveledItem, ItemLeveledListDefHandle.FromIndex(i).Value);
            }

            return lookup;
        }

        static Dictionary<string, ContentReference> BuildPlaceAtSpawnableLookup(
            ActorDef[] actors,
            BaseDef[] items,
            LightDef[] lights)
        {
            var lookup = new Dictionary<string, ContentReference>(StringComparer.OrdinalIgnoreCase);
            if (actors != null)
            {
                for (int i = 0; i < actors.Length; i++)
                {
                    AddContentReference(lookup, actors[i].Id, ContentReferenceKind.Actor, ActorDefHandle.FromIndex(i).Value);
                    AddContentReference(lookup, actors[i].OriginalId, ContentReferenceKind.Actor, ActorDefHandle.FromIndex(i).Value);
                }
            }

            if (items != null)
            {
                for (int i = 0; i < items.Length; i++)
                    AddContentReference(lookup, items[i].Id, ContentReferenceKind.Item, ItemDefHandle.FromIndex(i).Value);
            }

            if (lights != null)
            {
                for (int i = 0; i < lights.Length; i++)
                    AddContentReference(lookup, lights[i].Id, ContentReferenceKind.Light, LightDefHandle.FromIndex(i).Value);
            }

            return lookup;
        }

        static Dictionary<string, ContentReference> BuildExplicitContentTargetLookup(
            ActorDef[] actors,
            BaseDef[] activators,
            BaseDef[] doors,
            BaseDef[] containers,
            BaseDef[] items,
            LightDef[] lights,
            GenericRecordDef[] statics,
            ItemLeveledListDef[] creatureLeveledLists,
            ItemLeveledListDef[] itemLeveledLists)
        {
            var lookup = new Dictionary<string, ContentReference>(StringComparer.OrdinalIgnoreCase);
            if (actors != null)
            {
                for (int i = 0; i < actors.Length; i++)
                {
                    AddContentReference(lookup, actors[i].Id, ContentReferenceKind.Actor, ActorDefHandle.FromIndex(i).Value);
                    AddContentReference(lookup, actors[i].OriginalId, ContentReferenceKind.Actor, ActorDefHandle.FromIndex(i).Value);
                }
            }

            if (activators != null)
            {
                for (int i = 0; i < activators.Length; i++)
                    AddContentReference(lookup, activators[i].Id, ContentReferenceKind.Activator, ActivatorDefHandle.FromIndex(i).Value);
            }

            if (doors != null)
            {
                for (int i = 0; i < doors.Length; i++)
                    AddContentReference(lookup, doors[i].Id, ContentReferenceKind.Door, DoorDefHandle.FromIndex(i).Value);
            }

            if (containers != null)
            {
                for (int i = 0; i < containers.Length; i++)
                    AddContentReference(lookup, containers[i].Id, ContentReferenceKind.Container, ContainerDefHandle.FromIndex(i).Value);
            }

            if (items != null)
            {
                for (int i = 0; i < items.Length; i++)
                    AddContentReference(lookup, items[i].Id, ContentReferenceKind.Item, ItemDefHandle.FromIndex(i).Value);
            }

            if (lights != null)
            {
                for (int i = 0; i < lights.Length; i++)
                    AddContentReference(lookup, lights[i].Id, ContentReferenceKind.Light, LightDefHandle.FromIndex(i).Value);
            }

            if (statics != null)
            {
                for (int i = 0; i < statics.Length; i++)
                    AddContentReference(lookup, statics[i].Id, ContentReferenceKind.Static, GenericRecordDefHandle.FromIndex(i).Value);
            }

            if (creatureLeveledLists != null)
            {
                for (int i = 0; i < creatureLeveledLists.Length; i++)
                    AddContentReference(lookup, creatureLeveledLists[i].Id, ContentReferenceKind.LeveledCreature, CreatureLeveledListDefHandle.FromIndex(i).Value);
            }

            if (itemLeveledLists != null)
            {
                for (int i = 0; i < itemLeveledLists.Length; i++)
                    AddContentReference(lookup, itemLeveledLists[i].Id, ContentReferenceKind.LeveledItem, ItemLeveledListDefHandle.FromIndex(i).Value);
            }

            return lookup;
        }

        static void AddContentReference(Dictionary<string, ContentReference> lookup, string id, ContentReferenceKind kind, int handleValue)
        {
            string normalizedId = ContentId.NormalizeId(id);
            if (string.IsNullOrEmpty(normalizedId) || handleValue <= 0)
                return;

            lookup[normalizedId] = new ContentReference
            {
                Kind = kind,
                HandleValue = handleValue,
            };
        }

        static Dictionary<string, SpellDefHandle> BuildSpellLookup(SpellDef[] spells)
        {
            var lookup = new Dictionary<string, SpellDefHandle>(StringComparer.OrdinalIgnoreCase);
            if (spells == null)
                return lookup;

            for (int i = 0; i < spells.Length; i++)
                lookup[ContentId.NormalizeId(spells[i].Id)] = SpellDefHandle.FromIndex(i);
            return lookup;
        }

        static Dictionary<string, int> BuildFactionLookup(FactionDef[] factions)
        {
            var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (factions == null)
                return lookup;

            for (int i = 0; i < factions.Length; i++)
            {
                AddFactionLookupAlias(lookup, factions[i].Id, i);
                AddFactionLookupAlias(lookup, factions[i].Name, i);
                AddFactionLookupAlias(lookup, "House " + factions[i].Id, i);
                AddFactionLookupAlias(lookup, "Great House " + factions[i].Id, i);
            }
            return lookup;
        }

        static void AddFactionLookupAlias(Dictionary<string, int> lookup, string id, int index)
        {
            string normalizedId = ContentId.NormalizeId(id);
            if (!string.IsNullOrEmpty(normalizedId))
                lookup[normalizedId] = index;
        }

        static Dictionary<string, ActorLocalCompileInfo> BuildActorLocalLookup(ActorDef[] actors, GenericRecordDef[] scripts)
        {
            var lookup = new Dictionary<string, ActorLocalCompileInfo>(StringComparer.OrdinalIgnoreCase);
            if (actors == null || scripts == null)
                return lookup;

            var scriptLocals = new Dictionary<string, Dictionary<string, (int Index, byte Kind)>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < scripts.Length; i++)
            {
                string scriptId = ContentId.NormalizeId(scripts[i].Id);
                if (string.IsNullOrEmpty(scriptId))
                    continue;

                var locals = ExtractScriptLocalDeclarations(scripts[i].Text);
                if (locals.Count > 0)
                    scriptLocals[scriptId] = locals;
            }

            for (int i = 0; i < actors.Length; i++)
            {
                ref readonly var actor = ref actors[i];
                string scriptId = ContentId.NormalizeId(actor.ScriptId);
                if (string.IsNullOrEmpty(scriptId) || !scriptLocals.TryGetValue(scriptId, out var locals))
                    continue;

                AddActorLocalTargets(lookup, actor.Id, ActorDefHandle.FromIndex(i).Value, locals);
                AddActorLocalTargets(lookup, actor.OriginalId, ActorDefHandle.FromIndex(i).Value, locals);
            }

            return lookup;
        }

        static Dictionary<string, (int Index, byte Kind)> ExtractScriptLocalDeclarations(string scriptText)
        {
            var locals = new Dictionary<string, (int Index, byte Kind)>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(scriptText))
                return locals;

            string[] lines = scriptText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = StripComment(lines[i]).Trim();
                if (!TryReadLocalDeclaration(line, out string name, out byte valueKind))
                    continue;

                if (locals.ContainsKey(name))
                    continue;

                locals[name] = (locals.Count, valueKind);
            }

            return locals;
        }

        static void AddActorLocalTargets(
            Dictionary<string, ActorLocalCompileInfo> lookup,
            string actorId,
            int actorHandleValue,
            Dictionary<string, (int Index, byte Kind)> locals)
        {
            string normalizedActorId = ContentId.NormalizeId(actorId);
            if (string.IsNullOrEmpty(normalizedActorId))
                return;

            foreach (var pair in locals)
            {
                lookup[$"{normalizedActorId}.{ContentId.NormalizeId(pair.Key)}"] = new ActorLocalCompileInfo
                {
                    ActorHandleValue = actorHandleValue,
                    LocalIndex = pair.Value.Index,
                    ValueKind = pair.Value.Kind,
                };
            }
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

        static Dictionary<string, DialogueCompileInfo> BuildDialogueLookup(DialogueDef[] dialogues)
        {
            var lookup = new Dictionary<string, DialogueCompileInfo>(StringComparer.OrdinalIgnoreCase);
            if (dialogues == null)
                return lookup;

            for (int i = 0; i < dialogues.Length; i++)
            {
                lookup[ContentId.NormalizeId(dialogues[i].Id)] = new DialogueCompileInfo
                {
                    Index = i,
                    Type = dialogues[i].Type,
                    FirstInfoIndex = dialogues[i].FirstInfoIndex,
                    InfoCount = dialogues[i].InfoCount,
                };
            }
            return lookup;
        }

        static Dictionary<string, RegionDefHandle> BuildRegionLookup(RegionDef[] regions)
        {
            var lookup = new Dictionary<string, RegionDefHandle>(StringComparer.OrdinalIgnoreCase);
            if (regions == null)
                return lookup;

            for (int i = 0; i < regions.Length; i++)
            {
                var handle = RegionDefHandle.FromIndex(i);
                string normalizedId = ContentId.NormalizeId(regions[i].Id);
                if (!string.IsNullOrEmpty(normalizedId))
                    lookup[normalizedId] = handle;

                string normalizedName = ContentId.NormalizeId(regions[i].Name);
                if (!string.IsNullOrEmpty(normalizedName))
                    lookup[normalizedName] = handle;
            }

            return lookup;
        }

        static Dictionary<string, MusicTrackDefHandle> BuildMusicTrackLookup(MusicTrackDef[] tracks)
        {
            var lookup = new Dictionary<string, MusicTrackDefHandle>(StringComparer.OrdinalIgnoreCase);
            var fileNames = new Dictionary<string, MusicTrackDefHandle>(StringComparer.OrdinalIgnoreCase);
            var duplicateFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (tracks == null)
                return lookup;

            for (int i = 0; i < tracks.Length; i++)
            {
                var handle = MusicTrackDefHandle.FromIndex(i);
                string relativePath = NormalizeMusicPath(tracks[i].RelativePath);
                if (string.IsNullOrWhiteSpace(relativePath))
                    continue;

                lookup[relativePath] = handle;
                string fileName = NormalizeMusicPath(System.IO.Path.GetFileName(relativePath));
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;

                if (fileNames.ContainsKey(fileName))
                    duplicateFileNames.Add(fileName);
                else
                    fileNames[fileName] = handle;
            }

            foreach (var pair in fileNames)
            {
                if (!duplicateFileNames.Contains(pair.Key))
                    lookup[pair.Key] = pair.Value;
            }

            return lookup;
        }

        static string NormalizeMusicPath(string path)
        {
            path = (path ?? string.Empty).Trim().Trim('"').Replace('\\', '/');
            const string prefix = "Music/";
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                path = path.Substring(prefix.Length);
            return path;
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

                if (c == '(' || c == ')')
                {
                    tokens.Add(expression.Substring(i, 1));
                    i++;
                    expectValue = c == '(';
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
                       && expression[i] != '/'
                       && expression[i] != '('
                       && expression[i] != ')')
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

        static bool HasSingleOuterParenthesisPair(string[] tokens, int start, int count)
        {
            if (count < 2 || tokens[start] != "(" || tokens[start + count - 1] != ")")
                return false;

            int depth = 0;
            int end = start + count;
            for (int i = start; i < end; i++)
            {
                if (tokens[i] == "(")
                    depth++;
                else if (tokens[i] == ")")
                {
                    depth--;
                    if (depth == 0 && i < end - 1)
                        return false;
                    if (depth < 0)
                        return false;
                }
            }

            return depth == 0;
        }

        static bool TryResolveExplicitRefTarget(
            string targetId,
            IReadOnlyDictionary<string, uint> explicitRefTargets,
            ISet<string> ambiguousExplicitRefTargets,
            IReadOnlyDictionary<string, ContentReference> explicitContentTargets,
            out MorrowindScriptRefTargetMode targetMode,
            out uint targetRefKey,
            out string failure)
        {
            targetMode = MorrowindScriptRefTargetMode.PlacedRef;
            targetRefKey = 0u;
            failure = null;
            string normalizedId = ContentId.NormalizeId(targetId);
            if (normalizedId.Equals("player", StringComparison.OrdinalIgnoreCase))
            {
                targetMode = MorrowindScriptRefTargetMode.Player;
                targetRefKey = 0u;
                return true;
            }

            if (explicitRefTargets != null && explicitRefTargets.TryGetValue(normalizedId, out uint targetPlacedRefId) && targetPlacedRefId != 0u)
            {
                targetMode = MorrowindScriptRefTargetMode.PlacedRef;
                targetRefKey = targetPlacedRefId;
                return true;
            }

            if (explicitContentTargets != null
                && explicitContentTargets.TryGetValue(normalizedId, out var content)
                && content.IsValid)
            {
                targetMode = MorrowindScriptRefTargetMode.ActiveContentRef;
                targetRefKey = unchecked((uint)PackActiveContentRef(content));
                return true;
            }

            failure = ambiguousExplicitRefTargets != null && ambiguousExplicitRefTargets.Contains(normalizedId)
                ? $"Explicit reference id '{targetId}' resolves to multiple placed refs and no known placeable content target."
                : $"Explicit reference id '{targetId}' does not resolve to a unique baked placed ref or known placeable content target.";
            return false;
        }

        static int PackActiveContentRef(ContentReference content)
            => ((int)content.Kind << 24) | (content.HandleValue & 0x00FFFFFF);

        static string NormalizeSetExpressionReferences(string expression)
        {
            expression = TrimEnclosingParentheses(expression);
            if (expression.StartsWith("player->", StringComparison.OrdinalIgnoreCase))
                return expression.Substring("player->".Length).TrimStart();
            return expression;
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

        static bool TryResolveActorLocalTarget(
            Dictionary<string, ActorLocalCompileInfo> actorLocals,
            string target,
            out ActorLocalCompileInfo actorLocal)
        {
            actorLocal = default;
            if (actorLocals == null || !TrySplitActorLocalTarget(target, out string actorId, out string localName))
                return false;

            string key = $"{ContentId.NormalizeId(actorId)}.{ContentId.NormalizeId(localName)}";
            return actorLocals.TryGetValue(key, out actorLocal);
        }

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

        static ulong HashStringPrefix(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0UL;

            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c >= 'A' && c <= 'Z')
                    c = (char)(c + 32);
                hash ^= c;
                hash *= 1099511628211UL;
            }

            return hash == 0UL ? 1UL : hash;
        }

        static ulong HashInteriorCellId(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0UL;

            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c >= 'A' && c <= 'Z')
                    c = (char)(c + 32);
                hash ^= c;
                hash *= 1099511628211UL;
            }

            return hash == 0UL ? 1UL : hash;
        }
    }
}
