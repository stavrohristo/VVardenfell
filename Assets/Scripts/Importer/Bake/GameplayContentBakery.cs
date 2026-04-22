using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using VVardenfell.Core.Cache;
using VVardenfell.Core.Config;
using VVardenfell.Importer.Bsa;
using VVardenfell.Importer.Esm;

namespace VVardenfell.Importer.Bake
{
    internal static class GameplayContentBakery
    {
        static readonly uint ActiTag = EsmFourCC.Make('A', 'C', 'T', 'I');
        static readonly uint AlchTag = EsmFourCC.Make('A', 'L', 'C', 'H');
        static readonly uint AppaTag = EsmFourCC.Make('A', 'P', 'P', 'A');
        static readonly uint ArmoTag = EsmFourCC.Make('A', 'R', 'M', 'O');
        static readonly uint BookTag = EsmFourCC.Make('B', 'O', 'O', 'K');
        static readonly uint CreaTag = EsmFourCC.Make('C', 'R', 'E', 'A');
        static readonly uint ClotTag = EsmFourCC.Make('C', 'L', 'O', 'T');
        static readonly uint ContTag = EsmFourCC.Make('C', 'O', 'N', 'T');
        static readonly uint DialTag = EsmFourCC.Make('D', 'I', 'A', 'L');
        static readonly uint DoorTag = EsmFourCC.Make('D', 'O', 'O', 'R');
        static readonly uint EnchTag = EsmFourCC.Make('E', 'N', 'C', 'H');
        static readonly uint IngrTag = EsmFourCC.Make('I', 'N', 'G', 'R');
        static readonly uint InfoTag = EsmFourCC.Make('I', 'N', 'F', 'O');
        static readonly uint LighTag = EsmFourCC.Make('L', 'I', 'G', 'H');
        static readonly uint LockTag = EsmFourCC.Make('L', 'O', 'C', 'K');
        static readonly uint MgefTag = EsmFourCC.Make('M', 'G', 'E', 'F');
        static readonly uint MiscTag = EsmFourCC.Make('M', 'I', 'S', 'C');
        static readonly uint NpcTag = EsmFourCC.Make('N', 'P', 'C', '_');
        static readonly uint ProbTag = EsmFourCC.Make('P', 'R', 'O', 'B');
        static readonly uint RegnTag = EsmFourCC.Make('R', 'E', 'G', 'N');
        static readonly uint RepaTag = EsmFourCC.Make('R', 'E', 'P', 'A');
        static readonly uint SounTag = EsmFourCC.Make('S', 'O', 'U', 'N');
        static readonly uint SpelTag = EsmFourCC.Make('S', 'P', 'E', 'L');
        static readonly uint WeapTag = EsmFourCC.Make('W', 'E', 'A', 'P');

        static readonly uint AnamTag = EsmFourCC.Make('A', 'N', 'A', 'M');
        static readonly uint AvfxTag = EsmFourCC.Make('A', 'V', 'F', 'X');
        static readonly uint BnamTag = EsmFourCC.Make('B', 'N', 'A', 'M');
        static readonly uint BsndTag = EsmFourCC.Make('B', 'S', 'N', 'D');
        static readonly uint BvfxTag = EsmFourCC.Make('B', 'V', 'F', 'X');
        static readonly uint CnamTag = EsmFourCC.Make('C', 'N', 'A', 'M');
        static readonly uint CsndTag = EsmFourCC.Make('C', 'S', 'N', 'D');
        static readonly uint CvfxTag = EsmFourCC.Make('C', 'V', 'F', 'X');
        static readonly uint DescTag = EsmFourCC.Make('D', 'E', 'S', 'C');
        static readonly uint EndtTag = EsmFourCC.Make('E', 'N', 'D', 'T');
        static readonly uint EnamTag = EsmFourCC.Make('E', 'N', 'A', 'M');
        static readonly uint FlagTag = EsmFourCC.Make('F', 'L', 'A', 'G');
        static readonly uint HsndTag = EsmFourCC.Make('H', 'S', 'N', 'D');
        static readonly uint HvfxTag = EsmFourCC.Make('H', 'V', 'F', 'X');
        static readonly uint InamTag = EsmFourCC.Make('I', 'N', 'A', 'M');
        static readonly uint IndxTag = EsmFourCC.Make('I', 'N', 'D', 'X');
        static readonly uint ItexTag = EsmFourCC.Make('I', 'T', 'E', 'X');
        static readonly uint LhdtTag = EsmFourCC.Make('L', 'H', 'D', 'T');
        static readonly uint MedtTag = EsmFourCC.Make('M', 'E', 'D', 'T');
        static readonly uint NnamTag = EsmFourCC.Make('N', 'N', 'A', 'M');
        static readonly uint NpdtTag = EsmFourCC.Make('N', 'P', 'D', 'T');
        static readonly uint OnamTag = EsmFourCC.Make('O', 'N', 'A', 'M');
        static readonly uint PnamTag = EsmFourCC.Make('P', 'N', 'A', 'M');
        static readonly uint PtexTag = EsmFourCC.Make('P', 'T', 'E', 'X');
        static readonly uint QstfTag = EsmFourCC.Make('Q', 'S', 'T', 'F');
        static readonly uint QstnTag = EsmFourCC.Make('Q', 'S', 'T', 'N');
        static readonly uint QstrTag = EsmFourCC.Make('Q', 'S', 'T', 'R');
        static readonly uint RdatTag = EsmFourCC.Make('R', 'D', 'A', 'T');
        static readonly uint RnamTag = EsmFourCC.Make('R', 'N', 'A', 'M');
        static readonly uint ScvrTag = EsmFourCC.Make('S', 'C', 'V', 'R');
        static readonly uint ScriTag = EsmFourCC.Make('S', 'C', 'R', 'I');
        static readonly uint SnamTag = EsmFourCC.Make('S', 'N', 'A', 'M');
        static readonly uint SpdtTag = EsmFourCC.Make('S', 'P', 'D', 'T');
        static readonly uint WdatTag = EsmFourCC.Make('W', 'E', 'A', 'T');
        static readonly uint XsclTag = EsmFourCC.Make('X', 'S', 'C', 'L');

        static readonly HashSet<uint> ItemTags = new()
        {
            MiscTag,
            WeapTag,
            ArmoTag,
            ClotTag,
            BookTag,
            AlchTag,
            IngrTag,
            AppaTag,
            ProbTag,
            RepaTag,
            LockTag,
        };

        static readonly ProfilerMarker k_Bake = new("VV.Bake.GameplayContent");
        static readonly ProfilerMarker k_ParseSource = new("VV.Bake.GameplayContent.ParseSource");
        static readonly ProfilerMarker k_Validation = new("VV.Bake.GameplayContent.Validation");

        sealed class DialogueAccumulator
        {
            public DialogueDef Def;
            public readonly List<DialogueInfoDef> Infos = new();
            public readonly Dictionary<string, int> InfoIndexById = new(StringComparer.OrdinalIgnoreCase);
        }

        sealed class RegionAccumulator
        {
            public RegionDef Def;
            public readonly List<RegionSoundRefDef> SoundRefs = new();
        }

        sealed class State
        {
            public readonly Dictionary<string, ActorDef> Actors = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, BaseDef> Activators = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, BaseDef> Doors = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, BaseDef> Containers = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, BaseDef> Items = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, LightDef> Lights = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, SoundDef> Sounds = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, DialogueAccumulator> Dialogues = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, SpellDef> Spells = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, EnchantmentDef> Enchantments = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<int, MagicEffectDef> MagicEffects = new();
            public readonly Dictionary<string, RegionAccumulator> Regions = new(StringComparer.OrdinalIgnoreCase);
        }

        sealed class ValidationIssue
        {
            public bool IsError;
            public string Message;
        }

        public static IEnumerator Bake(MorrowindConfig config, BakeProgress progress, bool markDone = true)
        {
            using var _ = k_Bake.Auto();

            string[] sourcePaths = InstalledContentSources.ResolveGameplayRecordSources(config.InstallPath);
            if (sourcePaths.Length == 0)
            {
                progress.Error = "No gameplay-content sources were found under the configured install path.";
                progress.Done = markDone;
                yield break;
            }

            progress.Done = false;
            progress.Error = null;
            progress.Stage = "Gameplay Content";
            progress.Label = "Resolving load order";
            progress.Current = 0;
            progress.Total = sourcePaths.Length + 4;
            yield return null;

            var state = new State();
            string currentDialogueId = null;

            for (int i = 0; i < sourcePaths.Length; i++)
            {
                progress.Label = Path.GetFileName(sourcePaths[i]);
                using (k_ParseSource.Auto())
                using (var esm = new EsmReader(sourcePaths[i]))
                {
                    currentDialogueId = ParseSourceIntoState(esm, state, currentDialogueId);
                }

                progress.Current = i + 1;
                yield return null;
            }

            progress.Label = "Building deterministic content arrays";
            progress.Current = sourcePaths.Length + 1;
            GameplayContentData data = BuildContentData(state, config.InstallPath);
            yield return null;

            progress.Label = "Writing gameplay content cache";
            progress.Current = sourcePaths.Length + 2;
            GameplayContentFile.Write(CachePaths.GameplayContent, data);
            yield return null;

            progress.Label = "Writing gameplay validation report";
            progress.Current = sourcePaths.Length + 3;
            var manifest = GameplayContentManifest.FromSources(sourcePaths);
            PopulateManifestCounts(manifest, data);
            manifest.Write(CachePaths.GameplayContentManifest);

            using (k_Validation.Auto())
            {
                WriteValidationReport(config.InstallPath, data);
            }
            yield return null;

            progress.Label = "Gameplay content ready";
            progress.Current = sourcePaths.Length + 4;
            if (markDone)
                progress.Done = true;
        }

        static string ParseSourceIntoState(EsmReader esm, State state, string currentDialogueId)
        {
            esm.Seek(0);
            while (esm.ReadRecordHeader(out var rec))
            {
                switch (rec.Tag)
                {
                    case var tag when tag == ActiTag:
                        ParseBaseDefRecord(esm, rec.Tag, state.Activators);
                        break;
                    case var tag when tag == DoorTag:
                        ParseBaseDefRecord(esm, rec.Tag, state.Doors, isDoor: true);
                        break;
                    case var tag when tag == ContTag:
                        ParseBaseDefRecord(esm, rec.Tag, state.Containers, isContainer: true);
                        break;
                    case var tag when ItemTags.Contains(tag):
                        ParseBaseDefRecord(esm, rec.Tag, state.Items);
                        break;
                    case var tag when tag == LighTag:
                        ParseLightRecord(esm, state.Lights);
                        break;
                    case var tag when tag == SounTag:
                        ParseSoundRecord(esm, state.Sounds);
                        break;
                    case var tag when tag == NpcTag:
                        ParseActorRecord(esm, rec.Tag, ActorDefKind.Npc, state.Actors);
                        break;
                    case var tag when tag == CreaTag:
                        ParseActorRecord(esm, rec.Tag, ActorDefKind.Creature, state.Actors);
                        break;
                    case var tag when tag == DialTag:
                        currentDialogueId = ParseDialogueRecord(esm, state.Dialogues);
                        break;
                    case var tag when tag == InfoTag:
                        ParseDialogueInfoRecord(esm, state.Dialogues, currentDialogueId);
                        break;
                    case var tag when tag == SpelTag:
                        ParseSpellRecord(esm, state.Spells);
                        break;
                    case var tag when tag == EnchTag:
                        ParseEnchantmentRecord(esm, state.Enchantments);
                        break;
                    case var tag when tag == MgefTag:
                        ParseMagicEffectRecord(esm, state.MagicEffects);
                        break;
                    case var tag when tag == RegnTag:
                        ParseRegionRecord(esm, state.Regions);
                        break;
                    default:
                        esm.SkipRecord();
                        break;
                }
            }

            return currentDialogueId;
        }

        static void ParseBaseDefRecord(EsmReader esm, uint recordTag, Dictionary<string, BaseDef> target, bool isDoor = false, bool isContainer = false)
        {
            var def = new BaseDef { RecordTag = recordTag };
            bool deleted = false;

            while (esm.ReadSubrecordHeader(out var sub))
            {
                switch (sub.Tag)
                {
                    case var tag when tag == EsmFourCC.NAME:
                        def.Id = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.MODL:
                        def.Model = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.FNAM:
                        def.Name = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == ItexTag:
                        def.Icon = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == ScriTag:
                        def.ScriptId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == SnamTag:
                        def.SoundId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == AnamTag:
                        if (isDoor)
                            def.AuxSoundId = esm.ReadSubrecordString();
                        else
                            esm.SkipSubrecord();
                        break;
                    case var tag when tag == EsmFourCC.DATA:
                        if (isContainer && sub.Size >= 4)
                            def.Float0 = ReadSingle(esm.ReadSubrecordBytes(), 0);
                        else
                            esm.SkipSubrecord();
                        break;
                    case var tag when tag == FlagTag:
                        if (isContainer && sub.Size >= 4)
                            def.Int0 = ReadInt32(esm.ReadSubrecordBytes(), 0);
                        else
                            esm.SkipSubrecord();
                        break;
                    case var tag when tag == EnamTag:
                        def.EnchantId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.DELE:
                        esm.SkipSubrecord();
                        deleted = true;
                        break;
                    default:
                        esm.SkipSubrecord();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(def.Id))
                return;

            if (deleted)
            {
                target.Remove(def.Id);
                return;
            }

            def.ContentId = ContentId.FromTagAndId(recordTag, def.Id);
            target[def.Id] = def;
        }

        static void ParseLightRecord(EsmReader esm, Dictionary<string, LightDef> target)
        {
            var def = new LightDef { RecordTag = LighTag };
            bool deleted = false;

            while (esm.ReadSubrecordHeader(out var sub))
            {
                switch (sub.Tag)
                {
                    case var tag when tag == EsmFourCC.NAME:
                        def.Id = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.MODL:
                        def.Model = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.FNAM:
                        def.Name = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == ItexTag:
                        def.Icon = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == ScriTag:
                        def.ScriptId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == SnamTag:
                        def.SoundId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == LhdtTag:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 24)
                        {
                            def.Weight = ReadSingle(bytes, 0);
                            def.Value = ReadInt32(bytes, 4);
                            def.Duration = ReadInt32(bytes, 8);
                            def.Radius = ReadInt32(bytes, 12);
                            def.ColorRgba = ReadUInt32(bytes, 16);
                            def.Flags = ReadInt32(bytes, 20);
                        }
                        break;
                    }
                    case var tag when tag == EsmFourCC.DELE:
                        esm.SkipSubrecord();
                        deleted = true;
                        break;
                    default:
                        esm.SkipSubrecord();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(def.Id))
                return;

            if (deleted)
            {
                target.Remove(def.Id);
                return;
            }

            def.ContentId = ContentId.FromTagAndId(LighTag, def.Id);
            target[def.Id] = def;
        }

        static void ParseSoundRecord(EsmReader esm, Dictionary<string, SoundDef> target)
        {
            var def = new SoundDef();
            bool deleted = false;

            while (esm.ReadSubrecordHeader(out var sub))
            {
                switch (sub.Tag)
                {
                    case var tag when tag == EsmFourCC.NAME:
                        def.Id = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.FNAM:
                        def.SoundPath = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.DATA:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 3)
                        {
                            def.Volume = bytes[0];
                            def.MinRange = bytes[1];
                            def.MaxRange = bytes[2];
                        }
                        break;
                    }
                    case var tag when tag == EsmFourCC.DELE:
                        esm.SkipSubrecord();
                        deleted = true;
                        break;
                    default:
                        esm.SkipSubrecord();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(def.Id))
                return;

            if (deleted)
            {
                target.Remove(def.Id);
                return;
            }

            def.ContentId = ContentId.FromTagAndId(SounTag, def.Id);
            target[def.Id] = def;
        }

        static void ParseActorRecord(EsmReader esm, uint recordTag, ActorDefKind kind, Dictionary<string, ActorDef> target)
        {
            var def = new ActorDef
            {
                Kind = kind,
                RecordTag = recordTag,
                Scale = 1f,
            };
            bool deleted = false;

            while (esm.ReadSubrecordHeader(out var sub))
            {
                switch (sub.Tag)
                {
                    case var tag when tag == EsmFourCC.NAME:
                        def.Id = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.MODL:
                        def.Model = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.FNAM:
                        def.Name = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == ScriTag:
                        def.ScriptId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == RnamTag:
                        def.RaceId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == CnamTag:
                        if (kind == ActorDefKind.Npc && string.IsNullOrEmpty(def.ClassId))
                            def.ClassId = esm.ReadSubrecordString();
                        else if (kind == ActorDefKind.Creature && string.IsNullOrEmpty(def.OriginalId))
                            def.OriginalId = esm.ReadSubrecordString();
                        else
                            esm.SkipSubrecord();
                        break;
                    case var tag when tag == AnamTag:
                        if (kind == ActorDefKind.Npc)
                            def.FactionId = esm.ReadSubrecordString();
                        else
                            esm.SkipSubrecord();
                        break;
                    case var tag when tag == BnamTag:
                        if (kind == ActorDefKind.Npc)
                            def.HeadId = esm.ReadSubrecordString();
                        else
                            esm.SkipSubrecord();
                        break;
                    case var tag when tag == KnamTag:
                        if (kind == ActorDefKind.Npc)
                            def.HairId = esm.ReadSubrecordString();
                        else
                            esm.SkipSubrecord();
                        break;
                    case var tag when tag == FlagTag:
                        if (sub.Size >= 4)
                            def.Flags = ReadUInt32(esm.ReadSubrecordBytes(), 0);
                        else
                            esm.SkipSubrecord();
                        break;
                    case var tag when tag == NpdtTag:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (kind == ActorDefKind.Npc)
                        {
                            if (bytes.Length == 12)
                            {
                                def.Level = ReadInt16(bytes, 0);
                                def.Flags = bytes[11];
                            }
                            else if (bytes.Length >= 52)
                            {
                                def.Level = ReadInt16(bytes, 0);
                                def.Flags = bytes[51];
                            }
                        }
                        else if (bytes.Length >= 8)
                        {
                            def.Level = ReadInt32(bytes, 4);
                        }
                        break;
                    }
                    case var tag when tag == XsclTag:
                        if (sub.Size >= 4)
                            def.Scale = ReadSingle(esm.ReadSubrecordBytes(), 0);
                        else
                            esm.SkipSubrecord();
                        break;
                    case var tag when tag == EsmFourCC.DELE:
                        esm.SkipSubrecord();
                        deleted = true;
                        break;
                    default:
                        esm.SkipSubrecord();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(def.Id))
                return;

            if (deleted)
            {
                target.Remove(def.Id);
                return;
            }

            def.ContentId = ContentId.FromTagAndId(recordTag, def.Id);
            target[def.Id] = def;
        }

        static readonly uint KnamTag = EsmFourCC.Make('K', 'N', 'A', 'M');

        static string ParseDialogueRecord(EsmReader esm, Dictionary<string, DialogueAccumulator> target)
        {
            string id = null;
            string stringId = null;
            DialogueDefType type = DialogueDefType.Unknown;
            bool deleted = false;

            while (esm.ReadSubrecordHeader(out var sub))
            {
                switch (sub.Tag)
                {
                    case var tag when tag == EsmFourCC.NAME:
                        id = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.FNAM:
                        stringId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.DATA:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length > 0)
                            type = (DialogueDefType)bytes[0];
                        break;
                    }
                    case var tag when tag == EsmFourCC.DELE:
                        esm.SkipSubrecord();
                        deleted = true;
                        break;
                    default:
                        esm.SkipSubrecord();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(id))
                return null;

            if (deleted)
            {
                target.Remove(id);
                return null;
            }

            if (!target.TryGetValue(id, out var accumulator))
            {
                accumulator = new DialogueAccumulator();
                target[id] = accumulator;
            }

            accumulator.Def = new DialogueDef
            {
                ContentId = ContentId.FromTagAndId(DialTag, id),
                Id = id,
                StringId = string.IsNullOrWhiteSpace(stringId) ? id : stringId,
                Type = type,
                FirstInfoIndex = 0,
                InfoCount = accumulator.Infos.Count,
            };
            return id;
        }

        static void ParseDialogueInfoRecord(EsmReader esm, Dictionary<string, DialogueAccumulator> dialogues, string currentDialogueId)
        {
            if (string.IsNullOrWhiteSpace(currentDialogueId))
            {
                esm.SkipRecord();
                return;
            }

            if (!dialogues.TryGetValue(currentDialogueId, out var dialogue))
            {
                dialogue = new DialogueAccumulator
                {
                    Def = new DialogueDef
                    {
                        ContentId = ContentId.FromTagAndId(DialTag, currentDialogueId),
                        Id = currentDialogueId,
                        StringId = currentDialogueId,
                        Type = DialogueDefType.Unknown,
                    },
                };
                dialogues[currentDialogueId] = dialogue;
            }

            var info = new DialogueInfoDef
            {
                TopicId = currentDialogueId,
                Rank = -1,
                Gender = -1,
                PcRank = -1,
            };
            bool deleted = false;

            while (esm.ReadSubrecordHeader(out var sub))
            {
                switch (sub.Tag)
                {
                    case var tag when tag == InamTag:
                        info.Id = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == PnamTag:
                        info.PrevId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == NnamTag:
                        info.NextId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.DATA:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 12)
                        {
                            info.Type = ReadInt32(bytes, 0);
                            info.DispositionOrJournalIndex = ReadInt32(bytes, 4);
                            info.Rank = unchecked((sbyte)bytes[8]);
                            info.Gender = unchecked((sbyte)bytes[9]);
                            info.PcRank = unchecked((sbyte)bytes[10]);
                        }
                        break;
                    }
                    case var tag when tag == OnamTag:
                        info.ActorId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == RnamTag:
                        info.RaceId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == CnamTag:
                        info.ClassId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.FNAM:
                    {
                        string faction = esm.ReadSubrecordString();
                        if (string.Equals(faction, "FFFF", StringComparison.OrdinalIgnoreCase))
                            info.FactionLess = true;
                        else
                            info.FactionId = faction;
                        break;
                    }
                    case var tag when tag == AnamTag:
                        info.CellId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == DnamTag:
                        info.PcFactionId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == SnamTag:
                        info.SoundFile = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.NAME:
                        info.Response = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == ScvrTag:
                        info.SelectRuleCount += 1;
                        esm.SkipSubrecord();
                        break;
                    case var tag when tag == BnamTag:
                        info.ResultScript = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == QstnTag:
                        esm.SkipSubrecord();
                        info.QuestStatus = 1;
                        break;
                    case var tag when tag == QstfTag:
                        esm.SkipSubrecord();
                        info.QuestStatus = 2;
                        break;
                    case var tag when tag == QstrTag:
                        esm.SkipSubrecord();
                        info.QuestStatus = 3;
                        break;
                    case var tag when tag == EsmFourCC.DELE:
                        esm.SkipSubrecord();
                        deleted = true;
                        break;
                    default:
                        esm.SkipSubrecord();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(info.Id))
                return;

            info.ContentId = ContentId.FromTagAndId(InfoTag, $"{currentDialogueId}:{info.Id}");
            if (deleted)
            {
                if (dialogue.InfoIndexById.TryGetValue(info.Id, out int existingIndex))
                {
                    dialogue.Infos.RemoveAt(existingIndex);
                    dialogue.InfoIndexById.Remove(info.Id);
                    RebuildInfoIndex(dialogue);
                }
                return;
            }

            if (dialogue.InfoIndexById.TryGetValue(info.Id, out int index))
            {
                dialogue.Infos[index] = info;
            }
            else
            {
                dialogue.InfoIndexById[info.Id] = dialogue.Infos.Count;
                dialogue.Infos.Add(info);
            }
        }

        static readonly uint DnamTag = EsmFourCC.Make('D', 'N', 'A', 'M');

        static void ParseSpellRecord(EsmReader esm, Dictionary<string, SpellDef> target)
        {
            var def = new SpellDef();
            var effects = new List<MagicEffectInstanceDef>(8);
            bool deleted = false;

            while (esm.ReadSubrecordHeader(out var sub))
            {
                switch (sub.Tag)
                {
                    case var tag when tag == EsmFourCC.NAME:
                        def.Id = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.FNAM:
                        def.Name = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == SpdtTag:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 12)
                        {
                            def.SpellType = ReadInt32(bytes, 0);
                            def.Cost = ReadInt32(bytes, 4);
                            def.Flags = ReadInt32(bytes, 8);
                        }
                        break;
                    }
                    case var tag when tag == EnamTag:
                        effects.Add(ReadMagicEffectInstance(esm.ReadSubrecordBytes()));
                        break;
                    case var tag when tag == EsmFourCC.DELE:
                        esm.SkipSubrecord();
                        deleted = true;
                        break;
                    default:
                        esm.SkipSubrecord();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(def.Id))
                return;

            if (deleted)
            {
                target.Remove(def.Id);
                return;
            }

            def.ContentId = ContentId.FromTagAndId(SpelTag, def.Id);
            def.EffectStartIndex = -1;
            def.EffectCount = effects.Count;
            target[def.Id] = def;
            s_SpellEffects[ContentId.NormalizeId(def.Id)] = effects;
        }

        static void ParseEnchantmentRecord(EsmReader esm, Dictionary<string, EnchantmentDef> target)
        {
            var def = new EnchantmentDef();
            var effects = new List<MagicEffectInstanceDef>(8);
            bool deleted = false;

            while (esm.ReadSubrecordHeader(out var sub))
            {
                switch (sub.Tag)
                {
                    case var tag when tag == EsmFourCC.NAME:
                        def.Id = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EndtTag:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 16)
                        {
                            def.EnchantmentType = ReadInt32(bytes, 0);
                            def.Cost = ReadInt32(bytes, 4);
                            def.Charge = ReadInt32(bytes, 8);
                            def.Flags = ReadInt32(bytes, 12);
                        }
                        break;
                    }
                    case var tag when tag == EnamTag:
                        effects.Add(ReadMagicEffectInstance(esm.ReadSubrecordBytes()));
                        break;
                    case var tag when tag == EsmFourCC.DELE:
                        esm.SkipSubrecord();
                        deleted = true;
                        break;
                    default:
                        esm.SkipSubrecord();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(def.Id))
                return;

            if (deleted)
            {
                target.Remove(def.Id);
                return;
            }

            def.ContentId = ContentId.FromTagAndId(EnchTag, def.Id);
            def.EffectStartIndex = -1;
            def.EffectCount = effects.Count;
            target[def.Id] = def;
            s_EnchantmentEffects[ContentId.NormalizeId(def.Id)] = effects;
        }

        static void ParseMagicEffectRecord(EsmReader esm, Dictionary<int, MagicEffectDef> target)
        {
            var def = new MagicEffectDef { Index = -1 };

            while (esm.ReadSubrecordHeader(out var sub))
            {
                switch (sub.Tag)
                {
                    case var tag when tag == IndxTag:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 4)
                            def.Index = ReadInt32(bytes, 0);
                        break;
                    }
                    case var tag when tag == MedtTag:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 36)
                        {
                            def.School = ReadInt32(bytes, 0);
                            def.BaseCost = ReadSingle(bytes, 4);
                            def.Flags = ReadInt32(bytes, 8);
                            def.Red = ReadInt32(bytes, 12);
                            def.Green = ReadInt32(bytes, 16);
                            def.Blue = ReadInt32(bytes, 20);
                            def.SizeX = ReadSingle(bytes, 24);
                            def.Speed = ReadSingle(bytes, 28);
                            def.SizeCap = ReadSingle(bytes, 32);
                        }
                        break;
                    }
                    case var tag when tag == ItexTag:
                        def.Icon = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == PtexTag:
                        def.ParticleTexture = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == CvfxTag:
                        def.CastingObjectId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == BvfxTag:
                        def.BoltObjectId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == HvfxTag:
                        def.HitObjectId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == AvfxTag:
                        def.AreaObjectId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == CsndTag:
                        def.CastSoundId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == BsndTag:
                        def.BoltSoundId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == HsndTag:
                        def.HitSoundId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == AsndTag:
                        def.AreaSoundId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == DescTag:
                        def.Description = esm.ReadSubrecordString();
                        break;
                    default:
                        esm.SkipSubrecord();
                        break;
                }
            }

            if (def.Index < 0)
                return;

            def.ContentId = ContentId.FromTagAndId(MgefTag, def.Index.ToString());
            target[def.Index] = def;
        }

        static readonly uint AsndTag = EsmFourCC.Make('A', 'S', 'N', 'D');

        static void ParseRegionRecord(EsmReader esm, Dictionary<string, RegionAccumulator> target)
        {
            string id = null;
            string name = null;
            string sleepListId = null;
            int mapColor = 0;
            byte clear = 0, cloudy = 0, foggy = 0, overcast = 0, rain = 0, thunder = 0, ash = 0, blight = 0, snow = 0, blizzard = 0;
            bool deleted = false;
            var sounds = new List<RegionSoundRefDef>(8);

            while (esm.ReadSubrecordHeader(out var sub))
            {
                switch (sub.Tag)
                {
                    case var tag when tag == EsmFourCC.NAME:
                        id = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.FNAM:
                        name = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == BnamTag:
                        sleepListId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == CnamTag:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 4)
                            mapColor = ReadInt32(bytes, 0);
                        break;
                    }
                    case var tag when tag == WdatTag:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 10)
                        {
                            clear = bytes[0];
                            cloudy = bytes[1];
                            foggy = bytes[2];
                            overcast = bytes[3];
                            rain = bytes[4];
                            thunder = bytes[5];
                            ash = bytes[6];
                            blight = bytes[7];
                            snow = bytes[8];
                            blizzard = bytes[9];
                        }
                        break;
                    }
                    case var tag when tag == SnamTag:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 33)
                        {
                            string soundId = ReadFixedString(bytes, 0, 32);
                            byte chance = bytes[32];
                            sounds.Add(new RegionSoundRefDef { SoundId = soundId, Chance = chance });
                        }
                        break;
                    }
                    case var tag when tag == EsmFourCC.DELE:
                        esm.SkipSubrecord();
                        deleted = true;
                        break;
                    default:
                        esm.SkipSubrecord();
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(id))
                return;

            if (deleted)
            {
                target.Remove(id);
                return;
            }

            var def = new RegionDef
            {
                ContentId = ContentId.FromTagAndId(RegnTag, id),
                Id = id,
                Name = name,
                SleepListId = sleepListId,
                MapColorRgba = mapColor,
                ClearChance = clear,
                CloudyChance = cloudy,
                FoggyChance = foggy,
                OvercastChance = overcast,
                RainChance = rain,
                ThunderChance = thunder,
                AshChance = ash,
                BlightChance = blight,
                SnowChance = snow,
                BlizzardChance = blizzard,
                SoundRefStartIndex = -1,
                SoundRefCount = sounds.Count,
            };

            target[id] = new RegionAccumulator
            {
                Def = def,
            };
            target[id].SoundRefs.AddRange(sounds);
        }

        static readonly Dictionary<string, List<MagicEffectInstanceDef>> s_SpellEffects = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, List<MagicEffectInstanceDef>> s_EnchantmentEffects = new(StringComparer.OrdinalIgnoreCase);

        static GameplayContentData BuildContentData(State state, string installPath)
        {
            var data = new GameplayContentData
            {
                Actors = OrderByNormalizedId(state.Actors).ToArray(),
                Activators = OrderByNormalizedId(state.Activators).ToArray(),
                Doors = OrderByNormalizedId(state.Doors).ToArray(),
                Containers = OrderByNormalizedId(state.Containers).ToArray(),
                Items = OrderByNormalizedId(state.Items).ToArray(),
                Lights = OrderByNormalizedId(state.Lights).ToArray(),
                Sounds = OrderByNormalizedId(state.Sounds).ToArray(),
                MagicEffects = state.MagicEffects.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray(),
                MusicTracks = BuildMusicTrackDefs(installPath),
            };

            BuildDialogueArrays(state.Dialogues, out data.Dialogues, out data.DialogueInfos);
            BuildSpellArrays(state.Spells, s_SpellEffects, out data.Spells, ref data.MagicEffectInstances);
            BuildEnchantmentArrays(state.Enchantments, s_EnchantmentEffects, out data.Enchantments, ref data.MagicEffectInstances);
            BuildRegionArrays(state.Regions, out data.Regions, out data.RegionSoundRefs);

            s_SpellEffects.Clear();
            s_EnchantmentEffects.Clear();
            return data;
        }

        static T[] OrderByNormalizedId<T>(Dictionary<string, T> map)
        {
            return map
                .OrderBy(pair => ContentId.NormalizeId(pair.Key), StringComparer.Ordinal)
                .Select(pair => pair.Value)
                .ToArray();
        }

        static void BuildDialogueArrays(
            Dictionary<string, DialogueAccumulator> map,
            out DialogueDef[] dialogues,
            out DialogueInfoDef[] infos)
        {
            var ordered = map.OrderBy(pair => ContentId.NormalizeId(pair.Key), StringComparer.Ordinal).ToArray();
            var dialogueList = new List<DialogueDef>(ordered.Length);
            var infoList = new List<DialogueInfoDef>(ordered.Sum(pair => pair.Value.Infos.Count));

            foreach (var pair in ordered)
            {
                var def = pair.Value.Def;
                def.FirstInfoIndex = infoList.Count;
                def.InfoCount = pair.Value.Infos.Count;
                dialogueList.Add(def);
                infoList.AddRange(pair.Value.Infos);
            }

            dialogues = dialogueList.ToArray();
            infos = infoList.ToArray();
        }

        static void BuildSpellArrays(
            Dictionary<string, SpellDef> map,
            Dictionary<string, List<MagicEffectInstanceDef>> effectMap,
            out SpellDef[] defs,
            ref MagicEffectInstanceDef[] effectInstances)
        {
            var ordered = map.OrderBy(pair => ContentId.NormalizeId(pair.Key), StringComparer.Ordinal).ToArray();
            var output = new SpellDef[ordered.Length];
            var effects = effectInstances != null ? new List<MagicEffectInstanceDef>(effectInstances) : new List<MagicEffectInstanceDef>();

            for (int i = 0; i < ordered.Length; i++)
            {
                var def = ordered[i].Value;
                if (effectMap.TryGetValue(ContentId.NormalizeId(def.Id), out var spellEffects) && spellEffects.Count > 0)
                {
                    def.EffectStartIndex = effects.Count;
                    def.EffectCount = spellEffects.Count;
                    effects.AddRange(spellEffects);
                }
                else
                {
                    def.EffectStartIndex = -1;
                    def.EffectCount = 0;
                }

                output[i] = def;
            }

            defs = output;
            effectInstances = effects.ToArray();
        }

        static void BuildEnchantmentArrays(
            Dictionary<string, EnchantmentDef> map,
            Dictionary<string, List<MagicEffectInstanceDef>> effectMap,
            out EnchantmentDef[] defs,
            ref MagicEffectInstanceDef[] effectInstances)
        {
            var ordered = map.OrderBy(pair => ContentId.NormalizeId(pair.Key), StringComparer.Ordinal).ToArray();
            var output = new EnchantmentDef[ordered.Length];
            var effects = effectInstances != null ? new List<MagicEffectInstanceDef>(effectInstances) : new List<MagicEffectInstanceDef>();

            for (int i = 0; i < ordered.Length; i++)
            {
                var def = ordered[i].Value;
                if (effectMap.TryGetValue(ContentId.NormalizeId(def.Id), out var enchantEffects) && enchantEffects.Count > 0)
                {
                    def.EffectStartIndex = effects.Count;
                    def.EffectCount = enchantEffects.Count;
                    effects.AddRange(enchantEffects);
                }
                else
                {
                    def.EffectStartIndex = -1;
                    def.EffectCount = 0;
                }

                output[i] = def;
            }

            defs = output;
            effectInstances = effects.ToArray();
        }

        static void BuildRegionArrays(
            Dictionary<string, RegionAccumulator> map,
            out RegionDef[] defs,
            out RegionSoundRefDef[] sounds)
        {
            var ordered = map.OrderBy(pair => ContentId.NormalizeId(pair.Key), StringComparer.Ordinal).ToArray();
            var regionDefs = new RegionDef[ordered.Length];
            var soundRefs = new List<RegionSoundRefDef>(ordered.Sum(pair => pair.Value.SoundRefs.Count));

            for (int i = 0; i < ordered.Length; i++)
            {
                var def = ordered[i].Value.Def;
                def.SoundRefStartIndex = soundRefs.Count;
                def.SoundRefCount = ordered[i].Value.SoundRefs.Count;
                regionDefs[i] = def;
                soundRefs.AddRange(ordered[i].Value.SoundRefs);
            }

            defs = regionDefs;
            sounds = soundRefs.ToArray();
        }

        static MusicTrackDef[] BuildMusicTrackDefs(string installPath)
        {
            string[] tracks = InstalledContentSources.ResolveMusicTracks(installPath);
            var results = new MusicTrackDef[tracks.Length];
            string musicRoot = Path.Combine(installPath ?? string.Empty, "Data Files", "Music");

            for (int i = 0; i < tracks.Length; i++)
            {
                string relative = Path.GetRelativePath(musicRoot, tracks[i]).Replace('\\', '/');
                MusicTrackCategory category = relative.StartsWith("Battle/", StringComparison.OrdinalIgnoreCase)
                    ? MusicTrackCategory.Battle
                    : relative.StartsWith("Special/", StringComparison.OrdinalIgnoreCase)
                        ? MusicTrackCategory.Special
                        : MusicTrackCategory.Explore;
                results[i] = new MusicTrackDef
                {
                    ContentId = ContentId.FromTagAndId(EsmFourCC.Make('M', 'U', 'S', 'C'), relative),
                    RelativePath = relative,
                    Category = category,
                };
            }

            Array.Sort(results, (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.RelativePath, b.RelativePath));
            return results;
        }

        static void PopulateManifestCounts(GameplayContentManifest manifest, GameplayContentData data)
        {
            manifest.ActorCount = data.Actors.Length;
            manifest.ActivatorCount = data.Activators.Length;
            manifest.DoorCount = data.Doors.Length;
            manifest.ContainerCount = data.Containers.Length;
            manifest.ItemCount = data.Items.Length;
            manifest.LightCount = data.Lights.Length;
            manifest.SoundCount = data.Sounds.Length;
            manifest.DialogueCount = data.Dialogues.Length;
            manifest.DialogueInfoCount = data.DialogueInfos.Length;
            manifest.SpellCount = data.Spells.Length;
            manifest.EnchantmentCount = data.Enchantments.Length;
            manifest.MagicEffectCount = data.MagicEffects.Length;
            manifest.MagicEffectInstanceCount = data.MagicEffectInstances.Length;
            manifest.RegionCount = data.Regions.Length;
            manifest.RegionSoundRefCount = data.RegionSoundRefs.Length;
            manifest.MusicTrackCount = data.MusicTracks.Length;
        }

        static void WriteValidationReport(string installPath, GameplayContentData data)
        {
            var issues = new List<ValidationIssue>(256);
            var soundIds = new HashSet<string>(data.Sounds.Select(sound => ContentId.NormalizeId(sound.Id)), StringComparer.OrdinalIgnoreCase);
            var assetIndex = BuildAssetIndex(installPath);

            ValidateBaseDefs("Activator", data.Activators, soundIds, assetIndex, issues);
            ValidateBaseDefs("Door", data.Doors, soundIds, assetIndex, issues);
            ValidateBaseDefs("Container", data.Containers, soundIds, assetIndex, issues);
            ValidateBaseDefs("Item", data.Items, soundIds, assetIndex, issues);
            ValidateLights(data.Lights, soundIds, assetIndex, issues);
            ValidateActors(data.Actors, assetIndex, issues);
            ValidateSounds(data.Sounds, assetIndex, issues);
            ValidateDialogue(data.Dialogues, data.DialogueInfos, issues);
            ValidateMagicEffects(data.MagicEffects, soundIds, assetIndex, issues);
            ValidateRegions(data.Regions, data.RegionSoundRefs, soundIds, issues);

            Directory.CreateDirectory(Path.GetDirectoryName(CachePaths.GameplayValidationReport) ?? string.Empty);
            using var writer = new StreamWriter(CachePaths.GameplayValidationReport, false, Encoding.UTF8);
            writer.WriteLine("VVardenfell Gameplay Content Validation");
            writer.WriteLine($"Generated: {DateTime.UtcNow:O}");
            writer.WriteLine($"Actors={data.Actors.Length}, Lights={data.Lights.Length}, Sounds={data.Sounds.Length}, Dialogues={data.Dialogues.Length}, Infos={data.DialogueInfos.Length}");
            writer.WriteLine();

            if (issues.Count == 0)
            {
                writer.WriteLine("No validation issues detected.");
                return;
            }

            int errorCount = 0;
            int warningCount = 0;
            foreach (var issue in issues)
            {
                if (issue.IsError)
                    errorCount++;
                else
                    warningCount++;

                writer.WriteLine(issue.IsError ? $"ERROR: {issue.Message}" : $"WARN: {issue.Message}");
            }

            writer.WriteLine();
            writer.WriteLine($"Summary: {errorCount} error(s), {warningCount} warning(s)");
        }

        static HashSet<string> BuildAssetIndex(string installPath)
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string dataFilesPath = Path.Combine(installPath ?? string.Empty, "Data Files");
            if (Directory.Exists(dataFilesPath))
            {
                foreach (string file in Directory.GetFiles(dataFilesPath, "*.*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(dataFilesPath, file).Replace('\\', '/');
                    results.Add(relative);
                }

                foreach (string bsaPath in Directory.GetFiles(dataFilesPath, "*.bsa", SearchOption.TopDirectoryOnly))
                {
                    using var bsa = BsaArchive.Open(bsaPath);
                    for (int i = 0; i < bsa.Entries.Length; i++)
                        results.Add(bsa.Entries[i].Name.Replace('\\', '/'));
                }
            }

            return results;
        }

        static void ValidateBaseDefs(string family, BaseDef[] defs, HashSet<string> soundIds, HashSet<string> assetIndex, List<ValidationIssue> issues)
        {
            for (int i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                ValidateAssetPath(family, def.Id, "model", def.Model, assetIndex, issues);
                ValidateAssetPath(family, def.Id, "icon", def.Icon, assetIndex, issues);
                ValidateLinkedSound(family, def.Id, "sound", def.SoundId, soundIds, issues);
                ValidateLinkedSound(family, def.Id, "aux sound", def.AuxSoundId, soundIds, issues);
            }
        }

        static void ValidateLights(LightDef[] defs, HashSet<string> soundIds, HashSet<string> assetIndex, List<ValidationIssue> issues)
        {
            for (int i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                ValidateAssetPath("Light", def.Id, "model", def.Model, assetIndex, issues);
                ValidateAssetPath("Light", def.Id, "icon", def.Icon, assetIndex, issues);
                ValidateLinkedSound("Light", def.Id, "sound", def.SoundId, soundIds, issues);
            }
        }

        static void ValidateActors(ActorDef[] defs, HashSet<string> assetIndex, List<ValidationIssue> issues)
        {
            for (int i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                ValidateAssetPath(def.Kind == ActorDefKind.Npc ? "NPC" : "Creature", def.Id, "model", def.Model, assetIndex, issues);
            }
        }

        static void ValidateSounds(SoundDef[] defs, HashSet<string> assetIndex, List<ValidationIssue> issues)
        {
            for (int i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                ValidateAssetPath("Sound", def.Id, "sound file", def.SoundPath, assetIndex, issues);
            }
        }

        static void ValidateDialogue(DialogueDef[] dialogues, DialogueInfoDef[] infos, List<ValidationIssue> issues)
        {
            var dialogueIds = new HashSet<string>(dialogues.Select(dialogue => ContentId.NormalizeId(dialogue.Id)), StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < infos.Length; i++)
            {
                if (!dialogueIds.Contains(ContentId.NormalizeId(infos[i].TopicId)))
                {
                    issues.Add(new ValidationIssue
                    {
                        IsError = true,
                        Message = $"Dialogue info '{infos[i].Id}' references missing dialogue '{infos[i].TopicId}'.",
                    });
                }
            }
        }

        static void ValidateMagicEffects(MagicEffectDef[] defs, HashSet<string> soundIds, HashSet<string> assetIndex, List<ValidationIssue> issues)
        {
            for (int i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                string id = def.Index.ToString();
                ValidateAssetPath("MagicEffect", id, "icon", def.Icon, assetIndex, issues);
                ValidateAssetPath("MagicEffect", id, "particle", def.ParticleTexture, assetIndex, issues);
                ValidateLinkedSound("MagicEffect", id, "cast sound", def.CastSoundId, soundIds, issues);
                ValidateLinkedSound("MagicEffect", id, "bolt sound", def.BoltSoundId, soundIds, issues);
                ValidateLinkedSound("MagicEffect", id, "hit sound", def.HitSoundId, soundIds, issues);
                ValidateLinkedSound("MagicEffect", id, "area sound", def.AreaSoundId, soundIds, issues);
            }
        }

        static void ValidateRegions(RegionDef[] regions, RegionSoundRefDef[] refs, HashSet<string> soundIds, List<ValidationIssue> issues)
        {
            for (int i = 0; i < regions.Length; i++)
            {
                int start = regions[i].SoundRefStartIndex;
                int count = regions[i].SoundRefCount;
                for (int j = 0; j < count; j++)
                {
                    int index = start + j;
                    if (index < 0 || index >= refs.Length)
                    {
                        issues.Add(new ValidationIssue
                        {
                            IsError = true,
                            Message = $"Region '{regions[i].Id}' has an out-of-range sound reference window ({start}, {count}).",
                        });
                        break;
                    }

                    ValidateLinkedSound("Region", regions[i].Id, "region sound", refs[index].SoundId, soundIds, issues);
                }
            }
        }

        static void ValidateAssetPath(string family, string id, string field, string relativePath, HashSet<string> assetIndex, List<ValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return;

            string normalized = relativePath.Replace('\\', '/').Trim();
            if (!normalized.Contains('.') || assetIndex.Contains(normalized))
                return;

            issues.Add(new ValidationIssue
            {
                IsError = false,
                Message = $"{family} '{id}' references missing {field} asset '{relativePath}'.",
            });
        }

        static void ValidateLinkedSound(string family, string id, string field, string soundId, HashSet<string> soundIds, List<ValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(soundId))
                return;

            if (soundIds.Contains(ContentId.NormalizeId(soundId)))
                return;

            issues.Add(new ValidationIssue
            {
                IsError = true,
                Message = $"{family} '{id}' references missing {field} '{soundId}'.",
            });
        }

        static void RebuildInfoIndex(DialogueAccumulator dialogue)
        {
            dialogue.InfoIndexById.Clear();
            for (int i = 0; i < dialogue.Infos.Count; i++)
                dialogue.InfoIndexById[dialogue.Infos[i].Id] = i;
        }

        static MagicEffectInstanceDef ReadMagicEffectInstance(byte[] bytes)
        {
            var result = new MagicEffectInstanceDef();
            if (bytes == null || bytes.Length < 24)
                return result;

            result.EffectId = ReadInt16(bytes, 0);
            result.Skill = unchecked((sbyte)bytes[2]);
            result.Attribute = unchecked((sbyte)bytes[3]);
            result.Range = ReadInt32(bytes, 4);
            result.Area = ReadInt32(bytes, 8);
            result.Duration = ReadInt32(bytes, 12);
            result.MagnitudeMin = ReadInt32(bytes, 16);
            result.MagnitudeMax = ReadInt32(bytes, 20);
            return result;
        }

        static short ReadInt16(byte[] bytes, int offset) => BitConverter.ToInt16(bytes, offset);
        static int ReadInt32(byte[] bytes, int offset) => BitConverter.ToInt32(bytes, offset);
        static uint ReadUInt32(byte[] bytes, int offset) => BitConverter.ToUInt32(bytes, offset);
        static float ReadSingle(byte[] bytes, int offset) => BitConverter.ToSingle(bytes, offset);

        static string ReadFixedString(byte[] bytes, int offset, int count)
        {
            int end = offset;
            int limit = Math.Min(bytes.Length, offset + count);
            while (end < limit && bytes[end] != 0)
                end++;
            return Encoding.ASCII.GetString(bytes, offset, end - offset);
        }
    }
}
