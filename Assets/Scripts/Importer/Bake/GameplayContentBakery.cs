using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using VVardenfell.Core;
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
        static readonly uint BodyTag = EsmFourCC.Make('B', 'O', 'D', 'Y');
        static readonly uint BookTag = EsmFourCC.Make('B', 'O', 'O', 'K');
        static readonly uint BsgnTag = EsmFourCC.Make('B', 'S', 'G', 'N');
        static readonly uint ClasTag = EsmFourCC.Make('C', 'L', 'A', 'S');
        static readonly uint CreaTag = EsmFourCC.Make('C', 'R', 'E', 'A');
        static readonly uint ClotTag = EsmFourCC.Make('C', 'L', 'O', 'T');
        static readonly uint ContTag = EsmFourCC.Make('C', 'O', 'N', 'T');
        static readonly uint DialTag = EsmFourCC.Make('D', 'I', 'A', 'L');
        static readonly uint DoorTag = EsmFourCC.Make('D', 'O', 'O', 'R');
        static readonly uint EnchTag = EsmFourCC.Make('E', 'N', 'C', 'H');
        static readonly uint FactTag = EsmFourCC.Make('F', 'A', 'C', 'T');
        static readonly uint GlobTag = EsmFourCC.Make('G', 'L', 'O', 'B');
        static readonly uint GmstTag = EsmFourCC.Make('G', 'M', 'S', 'T');
        static readonly uint IngrTag = EsmFourCC.Make('I', 'N', 'G', 'R');
        static readonly uint InfoTag = EsmFourCC.Make('I', 'N', 'F', 'O');
        static readonly uint LevcTag = EsmFourCC.Make('L', 'E', 'V', 'C');
        static readonly uint LighTag = EsmFourCC.Make('L', 'I', 'G', 'H');
        static readonly uint LeviTag = EsmFourCC.Make('L', 'E', 'V', 'I');
        static readonly uint LockTag = EsmFourCC.Make('L', 'O', 'C', 'K');
        static readonly uint LtexTag = EsmFourCC.Make('L', 'T', 'E', 'X');
        static readonly uint MgefTag = EsmFourCC.Make('M', 'G', 'E', 'F');
        static readonly uint MiscTag = EsmFourCC.Make('M', 'I', 'S', 'C');
        static readonly uint NpcTag = EsmFourCC.Make('N', 'P', 'C', '_');
        static readonly uint PgrdTag = EsmFourCC.Make('P', 'G', 'R', 'D');
        static readonly uint ProbTag = EsmFourCC.Make('P', 'R', 'O', 'B');
        static readonly uint RaceTag = EsmFourCC.Make('R', 'A', 'C', 'E');
        static readonly uint RegnTag = EsmFourCC.Make('R', 'E', 'G', 'N');
        static readonly uint RepaTag = EsmFourCC.Make('R', 'E', 'P', 'A');
        static readonly uint ScptTag = EsmFourCC.Make('S', 'C', 'P', 'T');
        static readonly uint SkilTag = EsmFourCC.Make('S', 'K', 'I', 'L');
        static readonly uint SndgTag = EsmFourCC.Make('S', 'N', 'D', 'G');
        static readonly uint SounTag = EsmFourCC.Make('S', 'O', 'U', 'N');
        static readonly uint SpelTag = EsmFourCC.Make('S', 'P', 'E', 'L');
        static readonly uint SscrTag = EsmFourCC.Make('S', 'S', 'C', 'R');
        static readonly uint StatTag = EsmFourCC.Make('S', 'T', 'A', 'T');
        static readonly uint WeapTag = EsmFourCC.Make('W', 'E', 'A', 'P');

        static readonly uint AnamTag = EsmFourCC.Make('A', 'N', 'A', 'M');
        static readonly uint AidtTag = EsmFourCC.Make('A', 'I', 'D', 'T');
        static readonly uint AiWanderTag = EsmFourCC.Make('A', 'I', '_', 'W');
        static readonly uint AiTravelTag = EsmFourCC.Make('A', 'I', '_', 'T');
        static readonly uint AiEscortTag = EsmFourCC.Make('A', 'I', '_', 'E');
        static readonly uint AiFollowTag = EsmFourCC.Make('A', 'I', '_', 'F');
        static readonly uint AiActivateTag = EsmFourCC.Make('A', 'I', '_', 'A');
        static readonly uint AodtTag = EsmFourCC.Make('A', 'O', 'D', 'T');
        static readonly uint AvfxTag = EsmFourCC.Make('A', 'V', 'F', 'X');
        static readonly uint BnamTag = EsmFourCC.Make('B', 'N', 'A', 'M');
        static readonly uint BsndTag = EsmFourCC.Make('B', 'S', 'N', 'D');
        static readonly uint BvfxTag = EsmFourCC.Make('B', 'V', 'F', 'X');
        static readonly uint BydtTag = EsmFourCC.Make('B', 'Y', 'D', 'T');
        static readonly uint CnamTag = EsmFourCC.Make('C', 'N', 'A', 'M');
        static readonly uint CldtTag = EsmFourCC.Make('C', 'L', 'D', 'T');
        static readonly uint CndtTag = EsmFourCC.Make('C', 'N', 'D', 'T');
        static readonly uint CtdtTag = EsmFourCC.Make('C', 'T', 'D', 'T');
        static readonly uint CsndTag = EsmFourCC.Make('C', 'S', 'N', 'D');
        static readonly uint CvfxTag = EsmFourCC.Make('C', 'V', 'F', 'X');
        static readonly uint DescTag = EsmFourCC.Make('D', 'E', 'S', 'C');
        static readonly uint DnamTag = EsmFourCC.Make('D', 'N', 'A', 'M');
        static readonly uint DodtTag = EsmFourCC.Make('D', 'O', 'D', 'T');
        static readonly uint EndtTag = EsmFourCC.Make('E', 'N', 'D', 'T');
        static readonly uint EnamTag = EsmFourCC.Make('E', 'N', 'A', 'M');
        static readonly uint FadtTag = EsmFourCC.Make('F', 'A', 'D', 'T');
        static readonly uint FlagTag = EsmFourCC.Make('F', 'L', 'A', 'G');
        static readonly uint FltvTag = EsmFourCC.Make('F', 'L', 'T', 'V');
        static readonly uint HsndTag = EsmFourCC.Make('H', 'S', 'N', 'D');
        static readonly uint HvfxTag = EsmFourCC.Make('H', 'V', 'F', 'X');
        static readonly uint InamTag = EsmFourCC.Make('I', 'N', 'A', 'M');
        static readonly uint IndxTag = EsmFourCC.Make('I', 'N', 'D', 'X');
        static readonly uint IntvTag = EsmFourCC.Make('I', 'N', 'T', 'V');
        static readonly uint ItexTag = EsmFourCC.Make('I', 'T', 'E', 'X');
        static readonly uint LhdtTag = EsmFourCC.Make('L', 'H', 'D', 'T');
        static readonly uint MedtTag = EsmFourCC.Make('M', 'E', 'D', 'T');
        static readonly uint NnamTag = EsmFourCC.Make('N', 'N', 'A', 'M');
        static readonly uint NpdtTag = EsmFourCC.Make('N', 'P', 'D', 'T');
        static readonly uint OnamTag = EsmFourCC.Make('O', 'N', 'A', 'M');
        static readonly uint PnamTag = EsmFourCC.Make('P', 'N', 'A', 'M');
        static readonly uint NpcoTag = EsmFourCC.Make('N', 'P', 'C', 'O');
        static readonly uint NpcsTag = EsmFourCC.Make('N', 'P', 'C', 'S');
        static readonly uint PgrcTag = EsmFourCC.Make('P', 'G', 'R', 'C');
        static readonly uint PgrpTag = EsmFourCC.Make('P', 'G', 'R', 'P');
        static readonly uint PtexTag = EsmFourCC.Make('P', 'T', 'E', 'X');
        static readonly uint QstfTag = EsmFourCC.Make('Q', 'S', 'T', 'F');
        static readonly uint QstnTag = EsmFourCC.Make('Q', 'S', 'T', 'N');
        static readonly uint QstrTag = EsmFourCC.Make('Q', 'S', 'T', 'R');
        static readonly uint RadtTag = EsmFourCC.Make('R', 'A', 'D', 'T');
        static readonly uint RdatTag = EsmFourCC.Make('R', 'D', 'A', 'T');
        static readonly uint RnamTag = EsmFourCC.Make('R', 'N', 'A', 'M');
        static readonly uint ScvrTag = EsmFourCC.Make('S', 'C', 'V', 'R');
        static readonly uint ScriTag = EsmFourCC.Make('S', 'C', 'R', 'I');
        static readonly uint SnamTag = EsmFourCC.Make('S', 'N', 'A', 'M');
        static readonly uint SpdtTag = EsmFourCC.Make('S', 'P', 'D', 'T');
        static readonly uint StrvTag = EsmFourCC.Make('S', 'T', 'R', 'V');
        static readonly uint WdatTag = EsmFourCC.Make('W', 'E', 'A', 'T');
        static readonly uint WpdtTag = EsmFourCC.Make('W', 'P', 'D', 'T');
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
        const int ExteriorBorderCandidateDistanceMw = 512;
        const int ExteriorBorderVerticalDistanceMw = 384;

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

        sealed class ItemLeveledListAccumulator
        {
            public ItemLeveledListDef Def;
            public readonly List<ItemLeveledListEntryDef> Entries = new();
        }

        sealed class ItemEquipmentAccumulator
        {
            public ItemEquipmentDef Def;
            public readonly List<ItemEquipmentBodyPartDef> BodyParts = new();
        }

        sealed class ActorAccumulator
        {
            public ActorDef Def;
            public readonly List<ActorSpellDef> Spells = new();
            public readonly List<ContainerItemDef> InventoryItems = new();
            public readonly List<ActorAiPackageDef> AiPackages = new();
            public readonly List<ActorTravelDestinationDef> TravelDestinations = new();
        }

        sealed class PathGridAccumulator
        {
            public PathGridDef Def;
            public readonly List<PathGridPointDef> Points = new();
            public readonly List<int> RawConnectionTargets = new();
        }

        readonly struct NavigationEdgeDraft
        {
            public NavigationEdgeDraft(int fromNodeIndex, int toNodeIndex, float cost, PathGridNavigationEdgeKind kind)
            {
                FromNodeIndex = fromNodeIndex;
                ToNodeIndex = toNodeIndex;
                Cost = cost;
                Kind = kind;
            }

            public readonly int FromNodeIndex;
            public readonly int ToNodeIndex;
            public readonly float Cost;
            public readonly PathGridNavigationEdgeKind Kind;
        }

        sealed class NavigationUnionFind
        {
            readonly int[] _parent;
            readonly byte[] _rank;

            public NavigationUnionFind(int count)
            {
                _parent = new int[Math.Max(0, count)];
                _rank = new byte[_parent.Length];
                for (int i = 0; i < _parent.Length; i++)
                    _parent[i] = i;
            }

            public int Find(int value)
            {
                int parent = _parent[value];
                if (parent == value)
                    return value;

                int root = Find(parent);
                _parent[value] = root;
                return root;
            }

            public void Union(int a, int b)
            {
                if ((uint)a >= (uint)_parent.Length || (uint)b >= (uint)_parent.Length)
                    return;

                int rootA = Find(a);
                int rootB = Find(b);
                if (rootA == rootB)
                    return;

                if (_rank[rootA] < _rank[rootB])
                {
                    _parent[rootA] = rootB;
                }
                else if (_rank[rootA] > _rank[rootB])
                {
                    _parent[rootB] = rootA;
                }
                else
                {
                    _parent[rootB] = rootA;
                    _rank[rootA]++;
                }
            }
        }

        sealed class State
        {
            public readonly Dictionary<string, ActorAccumulator> Actors = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, BaseDef> Activators = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, BaseDef> Doors = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, BaseDef> Containers = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, List<ContainerItemDef>> ContainerItems = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, BaseDef> Items = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, ItemEquipmentAccumulator> ItemEquipment = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, LightDef> Lights = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, ItemLeveledListAccumulator> ItemLeveledLists = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, ItemLeveledListAccumulator> CreatureLeveledLists = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, SoundDef> Sounds = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, DialogueAccumulator> Dialogues = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, SpellDef> Spells = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, EnchantmentDef> Enchantments = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<int, MagicEffectDef> MagicEffects = new();
            public readonly Dictionary<string, RegionAccumulator> Regions = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, GenericRecordDef> GameSettings = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, GenericRecordDef> Globals = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, ClassDef> Classes = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, FactionDef> Factions = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, RaceDef> Races = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, GenericRecordDef> Birthsigns = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, GenericRecordDef> Skills = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, GenericRecordDef> Scripts = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, GenericRecordDef> StartScripts = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, GenericRecordDef> SoundGenerators = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, GenericRecordDef> LandTextures = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, GenericRecordDef> Statics = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, GenericRecordDef> BodyParts = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, ActorBodyPartDef> ActorBodyParts = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, PathGridAccumulator> PathGrids = new(StringComparer.OrdinalIgnoreCase);
        }

        sealed class ValidationIssue
        {
            public bool IsError;
            public string Message;
        }

        public static IEnumerator Bake(MorrowindConfig config, BakeProgress progress, bool markDone = true)
        {
            using var _ = k_Bake.Auto();

            string[] recordSourcePaths = InstalledContentSources.ResolveGameplayRecordSources(config.InstallPath);
            string[] dependencySourcePaths = InstalledContentSources.ResolveGameplayDependencySources(config.InstallPath);
            if (recordSourcePaths.Length == 0)
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
            progress.Total = recordSourcePaths.Length + 4;
            yield return null;

            var state = new State();
            string currentDialogueId = null;

            for (int i = 0; i < recordSourcePaths.Length; i++)
            {
                progress.Label = Path.GetFileName(recordSourcePaths[i]);
                using (k_ParseSource.Auto())
                using (var esm = new EsmReader(recordSourcePaths[i]))
                {
                    currentDialogueId = ParseSourceIntoState(esm, state, currentDialogueId);
                }

                progress.Current = i + 1;
                yield return null;
            }

            progress.Label = "Building deterministic content arrays";
            progress.Current = recordSourcePaths.Length + 1;
            GameplayContentData data = BuildContentData(state, config.InstallPath);
            yield return null;

            progress.Label = "Writing gameplay content cache";
            progress.Current = recordSourcePaths.Length + 2;
            GameplayContentFile.Write(CachePaths.GameplayContent, data);
            yield return null;

            progress.Label = "Writing gameplay validation report";
            progress.Current = recordSourcePaths.Length + 3;
            var manifest = GameplayContentManifest.FromSources(dependencySourcePaths);
            PopulateManifestCounts(manifest, data);
            manifest.Write(CachePaths.GameplayContentManifest);

            using (k_Validation.Auto())
            {
                WriteValidationReport(config.InstallPath, data);
            }
            yield return null;

            progress.Label = "Gameplay content ready";
            progress.Current = recordSourcePaths.Length + 4;
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
                    case var tag when tag == StatTag:
                        ParseGenericRecord(esm, rec.Tag, state.Statics);
                        break;
                    case var tag when tag == GmstTag:
                        ParseGenericRecord(esm, rec.Tag, state.GameSettings);
                        break;
                    case var tag when tag == GlobTag:
                        ParseGenericRecord(esm, rec.Tag, state.Globals);
                        break;
                    case var tag when tag == ClasTag:
                        ParseClassRecord(esm, state.Classes);
                        break;
                    case var tag when tag == FactTag:
                        ParseFactionRecord(esm, state.Factions);
                        break;
                    case var tag when tag == RaceTag:
                        ParseRaceRecord(esm, state.Races);
                        break;
                    case var tag when tag == BsgnTag:
                        ParseGenericRecord(esm, rec.Tag, state.Birthsigns);
                        break;
                    case var tag when tag == SkilTag:
                        ParseGenericRecord(esm, rec.Tag, state.Skills);
                        break;
                    case var tag when tag == ScptTag:
                        ParseGenericRecord(esm, rec.Tag, state.Scripts);
                        break;
                    case var tag when tag == SscrTag:
                        ParseGenericRecord(esm, rec.Tag, state.StartScripts);
                        break;
                    case var tag when tag == SndgTag:
                        ParseGenericRecord(esm, rec.Tag, state.SoundGenerators);
                        break;
                    case var tag when tag == LtexTag:
                        ParseGenericRecord(esm, rec.Tag, state.LandTextures);
                        break;
                    case var tag when tag == BodyTag:
                        ParseBodyPartRecord(esm, state.BodyParts, state.ActorBodyParts);
                        break;
                    case var tag when tag == PgrdTag:
                        ParsePathGridRecord(esm, state.PathGrids);
                        break;
                    case var tag when tag == DoorTag:
                        ParseBaseDefRecord(esm, rec.Tag, state.Doors, isDoor: true);
                        break;
                    case var tag when tag == ContTag:
                        ParseBaseDefRecord(esm, rec.Tag, state.Containers, isContainer: true, containerItems: state.ContainerItems);
                        break;
                    case var tag when ItemTags.Contains(tag):
                        ParseBaseDefRecord(esm, rec.Tag, state.Items, itemEquipment: state.ItemEquipment);
                        break;
                    case var tag when tag == LighTag:
                        ParseLightRecord(esm, state.Lights);
                        break;
                    case var tag when tag == LeviTag:
                        ParseLeveledListRecord(esm, state.ItemLeveledLists, LeviTag, InamTag);
                        break;
                    case var tag when tag == LevcTag:
                        ParseLeveledListRecord(esm, state.CreatureLeveledLists, LevcTag, CnamTag);
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

        static void ParseBaseDefRecord(
            EsmReader esm,
            uint recordTag,
            Dictionary<string, BaseDef> target,
            bool isDoor = false,
            bool isContainer = false,
            Dictionary<string, List<ContainerItemDef>> containerItems = null,
            Dictionary<string, ItemEquipmentAccumulator> itemEquipment = null)
        {
            var def = new BaseDef { RecordTag = recordTag };
            var parsedContainerItems = isContainer ? new List<ContainerItemDef>() : null;
            ItemEquipmentAccumulator equipment = CreateItemEquipmentAccumulator(recordTag);
            int pendingEquipmentPartIndex = -1;
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
                        else if (equipment != null)
                            ParseEquipmentData(recordTag, esm.ReadSubrecordBytes(), equipment);
                        else
                            esm.SkipSubrecord();
                        break;
                    case var tag when tag == AodtTag || tag == CtdtTag || tag == WpdtTag:
                        if (equipment != null)
                            ParseEquipmentData(recordTag, esm.ReadSubrecordBytes(), equipment);
                        else
                            esm.SkipSubrecord();
                        break;
                    case var tag when tag == FlagTag:
                        if (isContainer && sub.Size >= 4)
                            def.Int0 = ReadInt32(esm.ReadSubrecordBytes(), 0);
                        else
                            esm.SkipSubrecord();
                        break;
                    case var tag when tag == NpcoTag:
                        if (isContainer)
                            ParseContainerItemSubrecord(esm, parsedContainerItems);
                        else
                            esm.SkipSubrecord();
                        break;
                    case var tag when tag == EnamTag:
                        def.EnchantId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == IndxTag:
                    {
                        if (equipment == null || !IsEquipmentBodyPartRecord(recordTag))
                        {
                            esm.SkipSubrecord();
                            break;
                        }

                        int rawPart = ReadEquipmentPartIndex(esm.ReadSubrecordBytes());
                        pendingEquipmentPartIndex = AddEquipmentBodyPartPlaceholder(equipment, rawPart);
                        break;
                    }
                    case var tag when tag == BnamTag:
                    {
                        if (equipment == null || !TryReadEquipmentBodyPartId(esm, equipment, pendingEquipmentPartIndex, female: false))
                            esm.SkipSubrecord();
                        break;
                    }
                    case var tag when tag == CnamTag:
                    {
                        if (equipment == null || !TryReadEquipmentBodyPartId(esm, equipment, pendingEquipmentPartIndex, female: true))
                            esm.SkipSubrecord();
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
                if (isContainer && containerItems != null)
                    containerItems.Remove(def.Id);
                if (itemEquipment != null)
                    itemEquipment.Remove(def.Id);
                return;
            }

            def.ContentId = ContentId.FromTagAndId(recordTag, def.Id);
            target[def.Id] = def;
            if (isContainer && containerItems != null)
                containerItems[def.Id] = parsedContainerItems ?? new List<ContainerItemDef>();
            if (itemEquipment != null)
            {
                if (equipment != null)
                    itemEquipment[def.Id] = equipment;
                else
                    itemEquipment.Remove(def.Id);
            }
        }

        static ItemEquipmentAccumulator CreateItemEquipmentAccumulator(uint recordTag)
        {
            if (recordTag == WeapTag)
                return new ItemEquipmentAccumulator
                {
                    Def = new ItemEquipmentDef
                    {
                        Kind = ItemEquipmentKind.Weapon,
                        Slot = ItemEquipmentSlot.Weapon,
                        FirstBodyPartIndex = -1,
                    }
                };

            if (recordTag == ArmoTag)
                return new ItemEquipmentAccumulator
                {
                    Def = new ItemEquipmentDef
                    {
                        Kind = ItemEquipmentKind.Armor,
                        FirstBodyPartIndex = -1,
                    }
                };

            if (recordTag == ClotTag)
                return new ItemEquipmentAccumulator
                {
                    Def = new ItemEquipmentDef
                    {
                        Kind = ItemEquipmentKind.Clothing,
                        FirstBodyPartIndex = -1,
                    }
                };

            return null;
        }

        static bool IsEquipmentBodyPartRecord(uint recordTag) => recordTag == ArmoTag || recordTag == ClotTag;

        static int ReadEquipmentPartIndex(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return -1;
            if (bytes.Length >= 4)
                return ReadInt32(bytes, 0);
            if (bytes.Length >= 2)
                return ReadInt16(bytes, 0);
            return bytes[0];
        }

        static int AddEquipmentBodyPartPlaceholder(ItemEquipmentAccumulator equipment, int rawPart)
        {
            if (equipment == null || rawPart < 0 || rawPart > (int)ItemEquipmentPartReference.Tail)
                return -1;

            equipment.BodyParts.Add(new ItemEquipmentBodyPartDef
            {
                Part = (ItemEquipmentPartReference)rawPart,
            });
            return equipment.BodyParts.Count - 1;
        }

        static bool TryReadEquipmentBodyPartId(EsmReader esm, ItemEquipmentAccumulator equipment, int index, bool female)
        {
            if (equipment == null || index < 0 || index >= equipment.BodyParts.Count)
                return false;

            string bodyPartId = esm.ReadSubrecordString();
            var bodyPart = equipment.BodyParts[index];
            if (female)
                bodyPart.FemaleBodyPartId = bodyPartId;
            else
                bodyPart.MaleBodyPartId = bodyPartId;
            equipment.BodyParts[index] = bodyPart;
            return true;
        }

        static void ParseEquipmentData(uint recordTag, byte[] bytes, ItemEquipmentAccumulator equipment)
        {
            if (bytes == null || equipment == null)
                return;

            var def = equipment.Def;
            if (recordTag == ArmoTag)
            {
                if (bytes.Length >= 4)
                    def.Type = ReadInt32(bytes, 0);
                if (bytes.Length >= 8)
                    def.Weight = ReadSingle(bytes, 4);
                if (bytes.Length >= 12)
                    def.Value = ReadInt32(bytes, 8);
                if (bytes.Length >= 16)
                    def.Health = ReadInt32(bytes, 12);
                if (bytes.Length >= 20)
                    def.EnchantCapacity = ReadInt32(bytes, 16);
                if (bytes.Length >= 24)
                    def.Armor = ReadInt32(bytes, 20);
                def.Slot = MapArmorTypeToSlot(def.Type);
            }
            else if (recordTag == ClotTag)
            {
                if (bytes.Length >= 4)
                    def.Type = ReadInt32(bytes, 0);
                if (bytes.Length >= 8)
                    def.Weight = ReadSingle(bytes, 4);
                if (bytes.Length >= 10)
                    def.Value = ReadInt16(bytes, 8);
                if (bytes.Length >= 12)
                    def.EnchantCapacity = ReadInt16(bytes, 10);
                if (bytes.Length >= 16)
                    def.Value = ReadInt32(bytes, 8);
                def.Slot = MapClothingTypeToSlot(def.Type);
            }
            else if (recordTag == WeapTag)
            {
                if (bytes.Length >= 4)
                    def.Weight = ReadSingle(bytes, 0);
                if (bytes.Length >= 8)
                    def.Value = ReadInt32(bytes, 4);
                if (bytes.Length >= 10)
                    def.Type = ReadInt16(bytes, 8);
                if (bytes.Length >= 12)
                    def.Health = ReadInt16(bytes, 10);
                if (bytes.Length >= 22)
                    def.EnchantCapacity = ReadInt16(bytes, 20);
                if (bytes.Length >= 28)
                {
                    int chopMax = bytes[23];
                    int slashMax = bytes[25];
                    int thrustMax = bytes[27];
                    def.DamageMax = Math.Max(chopMax, Math.Max(slashMax, thrustMax));
                    def.DamageMin = Math.Min(bytes[22], Math.Min(bytes[24], bytes[26]));
                }
                def.Slot = ItemEquipmentSlot.Weapon;
            }

            equipment.Def = def;
        }

        static ItemEquipmentSlot MapArmorTypeToSlot(int type)
        {
            return type switch
            {
                0 => ItemEquipmentSlot.Helmet,
                1 => ItemEquipmentSlot.Cuirass,
                2 => ItemEquipmentSlot.LeftPauldron,
                3 => ItemEquipmentSlot.RightPauldron,
                4 => ItemEquipmentSlot.Greaves,
                5 => ItemEquipmentSlot.Boots,
                6 => ItemEquipmentSlot.LeftHand,
                7 => ItemEquipmentSlot.RightHand,
                8 => ItemEquipmentSlot.Shield,
                9 => ItemEquipmentSlot.LeftHand,
                10 => ItemEquipmentSlot.RightHand,
                _ => ItemEquipmentSlot.None,
            };
        }

        static ItemEquipmentSlot MapClothingTypeToSlot(int type)
        {
            return type switch
            {
                0 => ItemEquipmentSlot.Pants,
                1 => ItemEquipmentSlot.Shoes,
                2 => ItemEquipmentSlot.Shirt,
                3 => ItemEquipmentSlot.Belt,
                4 => ItemEquipmentSlot.Robe,
                5 => ItemEquipmentSlot.RightHand,
                6 => ItemEquipmentSlot.LeftHand,
                7 => ItemEquipmentSlot.Skirt,
                8 => ItemEquipmentSlot.Ring,
                9 => ItemEquipmentSlot.Amulet,
                _ => ItemEquipmentSlot.None,
            };
        }

        static void ParseContainerItemSubrecord(EsmReader esm, List<ContainerItemDef> items)
        {
            byte[] bytes = esm.ReadSubrecordBytes();
            if (bytes == null || bytes.Length < 36)
                return;

            int count = ReadInt32(bytes, 0);
            string itemId = ReadFixedString(bytes, 4, 32);
            if (string.IsNullOrWhiteSpace(itemId) || count <= 0)
                return;

            items.Add(new ContainerItemDef
            {
                ItemId = itemId,
                Count = count,
            });
        }

        static void ParseGenericRecord(EsmReader esm, uint recordTag, Dictionary<string, GenericRecordDef> target)
        {
            var def = new GenericRecordDef { RecordTag = recordTag };
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
                    case var tag when tag == EsmFourCC.MODL:
                        def.Model = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == ItexTag:
                        def.Icon = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == ScriTag:
                        def.ScriptId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == DescTag:
                        def.Text = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == StrvTag:
                        def.Text = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == IndxTag:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 4)
                            def.Int0 = ReadInt32(bytes, 0);
                        else if (bytes.Length >= 2)
                            def.Int0 = ReadInt16(bytes, 0);
                        else if (bytes.Length >= 1)
                            def.Int0 = bytes[0];
                        break;
                    }
                    case var tag when tag == IntvTag:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 4)
                            def.Int0 = ReadInt32(bytes, 0);
                        else if (bytes.Length >= 2)
                            def.Int0 = ReadInt16(bytes, 0);
                        else if (bytes.Length >= 1)
                            def.Int0 = bytes[0];
                        break;
                    }
                    case var tag when tag == FltvTag:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 4)
                            def.Float0 = ReadSingle(bytes, 0);
                        break;
                    }
                    case var tag when tag == FlagTag:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 4)
                            def.Flags = ReadUInt32(bytes, 0);
                        break;
                    }
                    case var tag when (tag == EsmFourCC.DATA || tag == MedtTag || tag == RdatTag || tag == WdatTag):
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 4)
                        {
                            def.Int1 = ReadInt32(bytes, 0);
                            def.Float0 = ReadSingle(bytes, 0);
                        }
                        if (bytes.Length >= 8)
                        {
                            def.Int2 = ReadInt32(bytes, 4);
                            def.Float1 = ReadSingle(bytes, 4);
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
                def.Id = BuildGeneratedRecordId(recordTag, def.Int0, target.Count);

            if (deleted)
            {
                target.Remove(def.Id);
                return;
            }

            def.ContentId = ContentId.FromTagAndId(recordTag, def.Id);
            target[def.Id] = def;
        }

        static void ParsePathGridRecord(EsmReader esm, Dictionary<string, PathGridAccumulator> target)
        {
            var def = new PathGridDef
            {
                RecordTag = PgrdTag,
                FirstPointIndex = -1,
                FirstConnectionIndex = -1,
            };
            var points = new List<PathGridPointDef>();
            var rawConnectionTargets = new List<int>();
            bool deleted = false;
            bool hasData = false;

            while (esm.ReadSubrecordHeader(out var sub))
            {
                switch (sub.Tag)
                {
                    case var tag when tag == EsmFourCC.NAME:
                        def.CellId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.DATA:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 12)
                        {
                            def.GridX = ReadInt32(bytes, 0);
                            def.GridY = ReadInt32(bytes, 4);
                            def.Granularity = ReadInt16(bytes, 8);
                            def.DeclaredPointCount = ReadUInt16(bytes, 10);
                            hasData = true;
                        }
                        break;
                    }
                    case var tag when tag == PgrpTag:
                        ReadPathGridPoints(esm.ReadSubrecordBytes(), points);
                        break;
                    case var tag when tag == PgrcTag:
                        ReadPathGridConnections(esm.ReadSubrecordBytes(), rawConnectionTargets);
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

            if (!hasData && string.IsNullOrWhiteSpace(def.CellId))
                return;

            def.Id = BuildPathGridId(def.CellId, def.GridX, def.GridY);
            def.IsExterior = string.IsNullOrWhiteSpace(def.CellId) ? (byte)1 : (byte)0;

            if (deleted)
            {
                target.Remove(def.Id);
                return;
            }

            def.ContentId = ContentId.FromTagAndId(PgrdTag, def.Id);
            var accumulator = new PathGridAccumulator
            {
                Def = def,
            };
            accumulator.Points.AddRange(points);
            accumulator.RawConnectionTargets.AddRange(rawConnectionTargets);
            target[def.Id] = accumulator;
        }

        static void ReadPathGridPoints(byte[] bytes, List<PathGridPointDef> points)
        {
            if (bytes == null || bytes.Length < 16)
                return;

            int count = bytes.Length / 16;
            for (int i = 0; i < count; i++)
            {
                int offset = i * 16;
                int sourceX = ReadInt32(bytes, offset);
                int sourceY = ReadInt32(bytes, offset + 4);
                int sourceZ = ReadInt32(bytes, offset + 8);
                points.Add(new PathGridPointDef
                {
                    SourceX = sourceX,
                    SourceY = sourceY,
                    SourceZ = sourceZ,
                    UnityX = sourceX * WorldScale.MwUnitsToMeters,
                    UnityY = sourceZ * WorldScale.MwUnitsToMeters,
                    UnityZ = sourceY * WorldScale.MwUnitsToMeters,
                    Autogenerated = bytes[offset + 12],
                    SourceConnectionCount = bytes[offset + 13],
                    FirstConnectionIndex = -1,
                });
            }
        }

        static void ReadPathGridConnections(byte[] bytes, List<int> rawConnectionTargets)
        {
            if (bytes == null || bytes.Length < 4)
                return;

            int count = bytes.Length / 4;
            for (int i = 0; i < count; i++)
                rawConnectionTargets.Add(ReadInt32(bytes, i * 4));
        }

        static string BuildPathGridId(string cellId, int gridX, int gridY)
        {
            if (!string.IsNullOrWhiteSpace(cellId))
                return ContentId.NormalizeId(cellId);

            return $"exterior:{gridX},{gridY}";
        }

        static void ParseBodyPartRecord(
            EsmReader esm,
            Dictionary<string, GenericRecordDef> genericTarget,
            Dictionary<string, ActorBodyPartDef> typedTarget)
        {
            var generic = new GenericRecordDef { RecordTag = BodyTag };
            var typed = new ActorBodyPartDef();
            bool hasBydt = false;
            bool deleted = false;

            while (esm.ReadSubrecordHeader(out var sub))
            {
                switch (sub.Tag)
                {
                    case var tag when tag == EsmFourCC.NAME:
                    {
                        string id = esm.ReadSubrecordString();
                        generic.Id = id;
                        typed.Id = id;
                        break;
                    }
                    case var tag when tag == EsmFourCC.MODL:
                    {
                        string model = esm.ReadSubrecordString();
                        generic.Model = model;
                        typed.Model = model;
                        break;
                    }
                    case var tag when tag == EsmFourCC.FNAM:
                    {
                        string race = esm.ReadSubrecordString();
                        generic.Name = race;
                        typed.RaceId = race;
                        break;
                    }
                    case var tag when tag == BydtTag:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 4)
                        {
                            typed.Part = (ActorBodyPartMeshPart)bytes[0];
                            typed.Vampire = bytes[1];
                            typed.Female = (byte)((bytes[2] & 0x01) != 0 ? 1 : 0);
                            typed.NotPlayable = (byte)((bytes[2] & 0x02) != 0 ? 1 : 0);
                            typed.Type = (ActorBodyPartMeshType)bytes[3];
                            generic.Int0 = bytes[0];
                            generic.Int1 = bytes[3];
                            generic.Flags = bytes[2];
                            generic.Int2 = bytes[1];
                            hasBydt = true;
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

            if (string.IsNullOrWhiteSpace(typed.Id))
                return;

            if (deleted)
            {
                genericTarget.Remove(typed.Id);
                typedTarget.Remove(typed.Id);
                return;
            }

            generic.ContentId = ContentId.FromTagAndId(BodyTag, typed.Id);
            typed.ContentId = generic.ContentId;
            typed.FirstPerson = (byte)(typed.Id.EndsWith("1st", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
            genericTarget[typed.Id] = generic;
            if (hasBydt)
                typedTarget[typed.Id] = typed;
        }

        static void ParseClassRecord(EsmReader esm, Dictionary<string, ClassDef> target)
        {
            var def = new ClassDef
            {
                RecordTag = ClasTag,
                MinorSkills = Array.Empty<int>(),
                MajorSkills = Array.Empty<int>(),
            };
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
                    case var tag when tag == DescTag:
                        def.Description = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == CldtTag:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 60)
                        {
                            def.FavoredAttribute0 = ReadInt32(bytes, 0);
                            def.FavoredAttribute1 = ReadInt32(bytes, 4);
                            def.Specialization = ReadInt32(bytes, 8);
                            var minor = new int[5];
                            var major = new int[5];
                            for (int i = 0; i < 5; i++)
                            {
                                int offset = 12 + i * 8;
                                minor[i] = ReadInt32(bytes, offset);
                                major[i] = ReadInt32(bytes, offset + 4);
                            }

                            def.MinorSkills = minor;
                            def.MajorSkills = major;
                            def.Playable = ReadInt32(bytes, 52);
                            def.Services = ReadInt32(bytes, 56);
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

            def.ContentId = ContentId.FromTagAndId(ClasTag, def.Id);
            target[def.Id] = def;
        }

        static void ParseRaceRecord(EsmReader esm, Dictionary<string, RaceDef> target)
        {
            var def = new RaceDef
            {
                RecordTag = RaceTag,
                SkillBonuses = Array.Empty<RaceSkillBonusDef>(),
                MaleAttributes = Array.Empty<int>(),
                FemaleAttributes = Array.Empty<int>(),
                PowerSpellIds = Array.Empty<string>(),
            };
            var powers = new List<string>();
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
                    case var tag when tag == DescTag:
                        def.Description = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == RadtTag:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 140)
                        {
                            var bonuses = new RaceSkillBonusDef[7];
                            for (int i = 0; i < bonuses.Length; i++)
                            {
                                int offset = i * 8;
                                bonuses[i] = new RaceSkillBonusDef
                                {
                                    Skill = ReadInt32(bytes, offset),
                                    Bonus = ReadInt32(bytes, offset + 4),
                                };
                            }

                            var maleAttributes = new int[8];
                            var femaleAttributes = new int[8];
                            for (int i = 0; i < 8; i++)
                            {
                                int offset = 56 + i * 8;
                                maleAttributes[i] = ReadInt32(bytes, offset);
                                femaleAttributes[i] = ReadInt32(bytes, offset + 4);
                            }

                            def.SkillBonuses = bonuses;
                            def.MaleAttributes = maleAttributes;
                            def.FemaleAttributes = femaleAttributes;
                            def.MaleHeight = ReadSingle(bytes, 120);
                            def.FemaleHeight = ReadSingle(bytes, 124);
                            def.MaleWeight = ReadSingle(bytes, 128);
                            def.FemaleWeight = ReadSingle(bytes, 132);
                            def.Flags = ReadInt32(bytes, 136);
                        }
                        break;
                    }
                    case var tag when tag == NpcsTag:
                    {
                        string spellId = esm.ReadSubrecordString();
                        if (!string.IsNullOrWhiteSpace(spellId))
                            powers.Add(spellId);
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

            def.PowerSpellIds = powers.ToArray();
            def.ContentId = ContentId.FromTagAndId(RaceTag, def.Id);
            target[def.Id] = def;
        }

        static void ParseFactionRecord(EsmReader esm, Dictionary<string, FactionDef> target)
        {
            var def = new FactionDef
            {
                RecordTag = FactTag,
                RankRequirements = Array.Empty<FactionRankRequirementDef>(),
                Skills = Array.Empty<int>(),
                RankNames = Array.Empty<string>(),
                Reactions = Array.Empty<FactionReactionDef>(),
            };
            var ranks = new List<string>(10);
            var reactionsByFaction = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string pendingReactionFaction = null;
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
                    case var tag when tag == RnamTag:
                        ranks.Add(esm.ReadSubrecordString());
                        break;
                    case var tag when tag == FadtTag:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 240)
                        {
                            def.FavoredAttribute0 = ReadInt32(bytes, 0);
                            def.FavoredAttribute1 = ReadInt32(bytes, 4);

                            var rankRequirements = new FactionRankRequirementDef[10];
                            for (int i = 0; i < rankRequirements.Length; i++)
                            {
                                int offset = 8 + i * 20;
                                rankRequirements[i] = new FactionRankRequirementDef
                                {
                                    Attribute1 = ReadInt32(bytes, offset),
                                    Attribute2 = ReadInt32(bytes, offset + 4),
                                    PrimarySkill = ReadInt32(bytes, offset + 8),
                                    FavoredSkill = ReadInt32(bytes, offset + 12),
                                    Reaction = ReadInt32(bytes, offset + 16),
                                };
                            }

                            var skills = new int[7];
                            for (int i = 0; i < skills.Length; i++)
                                skills[i] = ReadInt32(bytes, 208 + i * 4);

                            def.RankRequirements = rankRequirements;
                            def.Skills = skills;
                            def.Hidden = ReadInt32(bytes, 236);
                        }
                        break;
                    }
                    case var tag when tag == AnamTag:
                        pendingReactionFaction = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == IntvTag:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (!string.IsNullOrWhiteSpace(pendingReactionFaction) && bytes.Length >= 4)
                        {
                            int reaction = ReadInt32(bytes, 0);
                            if (!reactionsByFaction.TryGetValue(pendingReactionFaction, out int existing) || existing > reaction)
                                reactionsByFaction[pendingReactionFaction] = reaction;
                        }

                        pendingReactionFaction = null;
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

            def.RankNames = ranks.ToArray();
            def.Reactions = reactionsByFaction
                .OrderBy(pair => ContentId.NormalizeId(pair.Key), StringComparer.Ordinal)
                .Select(pair => new FactionReactionDef { FactionId = pair.Key, Reaction = pair.Value })
                .ToArray();
            def.ContentId = ContentId.FromTagAndId(FactTag, def.Id);
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

        static void ParseLeveledListRecord(
            EsmReader esm,
            Dictionary<string, ItemLeveledListAccumulator> target,
            uint recordTag,
            uint entryIdTag)
        {
            var def = new ItemLeveledListDef
            {
                FirstEntryIndex = -1,
            };
            var entries = new List<ItemLeveledListEntryDef>();
            string pendingEntryId = null;
            bool deleted = false;

            while (esm.ReadSubrecordHeader(out var sub))
            {
                switch (sub.Tag)
                {
                    case var tag when tag == EsmFourCC.NAME:
                        def.Id = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == EsmFourCC.DATA:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 4)
                            def.Flags = ReadInt32(bytes, 0);
                        break;
                    }
                    case var tag when tag == NnamTag:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 1)
                            def.ChanceNone = bytes[0];
                        break;
                    }
                    case var tag when tag == IndxTag:
                        esm.SkipSubrecord();
                        break;
                    case var tag when tag == entryIdTag:
                        pendingEntryId = esm.ReadSubrecordString();
                        break;
                    case var tag when tag == IntvTag:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (!string.IsNullOrWhiteSpace(pendingEntryId) && bytes.Length >= 2)
                        {
                            entries.Add(new ItemLeveledListEntryDef
                            {
                                ItemId = pendingEntryId,
                                Level = BitConverter.ToUInt16(bytes, 0),
                            });
                        }

                        pendingEntryId = null;
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

            def.ContentId = ContentId.FromTagAndId(recordTag, def.Id);
            target[def.Id] = new ItemLeveledListAccumulator
            {
                Def = def,
            };
            target[def.Id].Entries.AddRange(entries);
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

        static void ParseActorRecord(EsmReader esm, uint recordTag, ActorDefKind kind, Dictionary<string, ActorAccumulator> target)
        {
            var def = new ActorDef
            {
                Kind = kind,
                RecordTag = recordTag,
                Scale = 1f,
                FirstSpellIndex = -1,
                FirstInventoryIndex = -1,
                FirstAiPackageIndex = -1,
                FirstTravelDestinationIndex = -1,
                AiData = kind == ActorDefKind.Npc
                    ? new ActorAiDataDef { Hello = 30, Fight = 30, Flee = 30 }
                    : new ActorAiDataDef { Fight = 90, Flee = 20 },
            };
            var spells = new List<ActorSpellDef>();
            var inventoryItems = new List<ContainerItemDef>();
            var aiPackages = new List<ActorAiPackageDef>();
            var travelDestinations = new List<ActorTravelDestinationDef>();
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
                        {
                            uint flags = ReadUInt32(esm.ReadSubrecordBytes(), 0);
                            def.Flags = flags;
                            def.BloodType = (int)(((flags >> 8) & 0xFF) >> 2);
                        }
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
                                def.AutoCalculatedStats = 1;
                                def.Disposition = bytes[2];
                                def.Reputation = bytes[3];
                                def.Rank = bytes[4];
                                def.Gold = ReadInt32(bytes, 8);
                            }
                            else if (bytes.Length >= 52)
                            {
                                def.Level = ReadInt16(bytes, 0);
                                def.Attributes = ReadNpcAttributes(bytes, 2);
                                def.Skills = ReadNpcSkills(bytes, 10);
                                def.Vitals = new ActorVitalDef
                                {
                                    Health = ReadUInt16(bytes, 38),
                                    Magicka = ReadUInt16(bytes, 40),
                                    Fatigue = ReadUInt16(bytes, 42),
                                };
                                def.Disposition = bytes[44];
                                def.Reputation = bytes[45];
                                def.Rank = bytes[46];
                                def.Gold = ReadInt32(bytes, 48);
                            }
                        }
                        else if (bytes.Length >= 96)
                        {
                            def.CreatureType = ReadInt32(bytes, 0);
                            def.Level = ReadInt32(bytes, 4);
                            def.Attributes = ReadCreatureAttributes(bytes, 8);
                            def.Vitals = new ActorVitalDef
                            {
                                Health = ReadInt32(bytes, 40),
                                Magicka = ReadInt32(bytes, 44),
                                Fatigue = ReadInt32(bytes, 48),
                            };
                            def.SoulValue = ReadInt32(bytes, 52);
                            def.Combat = ReadInt32(bytes, 56);
                            def.Magic = ReadInt32(bytes, 60);
                            def.Stealth = ReadInt32(bytes, 64);
                            def.Gold = ReadInt32(bytes, 92);
                        }
                        else if (kind == ActorDefKind.Creature && bytes.Length >= 8)
                        {
                            def.CreatureType = ReadInt32(bytes, 0);
                            def.Level = ReadInt32(bytes, 4);
                        }
                        break;
                    }
                    case var tag when tag == NpcsTag:
                    {
                        string spellId = esm.ReadSubrecordString();
                        if (!string.IsNullOrWhiteSpace(spellId))
                        {
                            spells.Add(new ActorSpellDef
                            {
                                SpellId = spellId,
                            });
                        }
                        break;
                    }
                    case var tag when tag == NpcoTag:
                    {
                        if (TryReadContainerItem(esm.ReadSubrecordBytes(), out var item))
                            inventoryItems.Add(item);
                        break;
                    }
                    case var tag when tag == AidtTag:
                    {
                        byte[] bytes = esm.ReadSubrecordBytes();
                        if (bytes.Length >= 12)
                        {
                            def.AiData = new ActorAiDataDef
                            {
                                Hello = ReadUInt16(bytes, 0),
                                Fight = bytes[2],
                                Flee = bytes[3],
                                Alarm = bytes[4],
                                Services = ReadInt32(bytes, 8),
                            };
                        }
                        break;
                    }
                    case var tag when tag == AiWanderTag:
                    {
                        if (TryReadAiWanderPackage(esm.ReadSubrecordBytes(), out var package))
                            aiPackages.Add(package);
                        break;
                    }
                    case var tag when tag == AiTravelTag:
                    {
                        if (TryReadAiTravelPackage(esm.ReadSubrecordBytes(), out var package))
                            aiPackages.Add(package);
                        break;
                    }
                    case var tag when tag == AiEscortTag:
                    {
                        if (TryReadAiTargetPackage(esm.ReadSubrecordBytes(), ActorAiPackageType.Escort, out var package))
                            aiPackages.Add(package);
                        break;
                    }
                    case var tag when tag == AiFollowTag:
                    {
                        if (TryReadAiTargetPackage(esm.ReadSubrecordBytes(), ActorAiPackageType.Follow, out var package))
                            aiPackages.Add(package);
                        break;
                    }
                    case var tag when tag == AiActivateTag:
                    {
                        if (TryReadAiActivatePackage(esm.ReadSubrecordBytes(), out var package))
                            aiPackages.Add(package);
                        break;
                    }
                    case var tag when tag == CndtTag:
                    {
                        string cellName = esm.ReadSubrecordString();
                        if (aiPackages.Count > 0)
                        {
                            int index = aiPackages.Count - 1;
                            var package = aiPackages[index];
                            if (package.Type == ActorAiPackageType.Escort || package.Type == ActorAiPackageType.Follow)
                            {
                                package.CellName = cellName;
                                aiPackages[index] = package;
                            }
                        }
                        break;
                    }
                    case var tag when tag == DodtTag:
                    {
                        if (TryReadTravelDestination(esm.ReadSubrecordBytes(), out var destination))
                            travelDestinations.Add(destination);
                        break;
                    }
                    case var tag when tag == DnamTag:
                    {
                        string cellName = esm.ReadSubrecordString();
                        if (travelDestinations.Count > 0)
                        {
                            int index = travelDestinations.Count - 1;
                            var destination = travelDestinations[index];
                            destination.CellName = cellName;
                            travelDestinations[index] = destination;
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
            var accumulator = new ActorAccumulator
            {
                Def = def,
            };
            accumulator.Spells.AddRange(spells);
            accumulator.InventoryItems.AddRange(inventoryItems);
            accumulator.AiPackages.AddRange(aiPackages);
            accumulator.TravelDestinations.AddRange(travelDestinations);
            target[def.Id] = accumulator;
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
                Activators = OrderByNormalizedId(state.Activators).ToArray(),
                Doors = OrderByNormalizedId(state.Doors).ToArray(),
                Containers = OrderByNormalizedId(state.Containers).ToArray(),
                Items = OrderByNormalizedId(state.Items).ToArray(),
                Lights = OrderByNormalizedId(state.Lights).ToArray(),
                ItemLeveledLists = Array.Empty<ItemLeveledListDef>(),
                ItemLeveledListEntries = Array.Empty<ItemLeveledListEntryDef>(),
                CreatureLeveledLists = Array.Empty<ItemLeveledListDef>(),
                CreatureLeveledListEntries = Array.Empty<ItemLeveledListEntryDef>(),
                Sounds = OrderByNormalizedId(state.Sounds).ToArray(),
                MagicEffects = state.MagicEffects.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray(),
                MusicTracks = BuildMusicTrackDefs(installPath),
                AmbientSettings = BuildAmbientSettings(installPath),
                GameSettings = OrderByNormalizedId(state.GameSettings).ToArray(),
                Globals = OrderByNormalizedId(state.Globals).ToArray(),
                Classes = OrderByNormalizedId(state.Classes).ToArray(),
                Factions = OrderByNormalizedId(state.Factions).ToArray(),
                Races = OrderByNormalizedId(state.Races).ToArray(),
                Birthsigns = OrderByNormalizedId(state.Birthsigns).ToArray(),
                Skills = OrderByNormalizedId(state.Skills).ToArray(),
                Scripts = OrderByNormalizedId(state.Scripts).ToArray(),
                StartScripts = OrderByNormalizedId(state.StartScripts).ToArray(),
                SoundGenerators = OrderByNormalizedId(state.SoundGenerators).ToArray(),
                LandTextures = OrderByNormalizedId(state.LandTextures).ToArray(),
                Statics = OrderByNormalizedId(state.Statics).ToArray(),
                BodyParts = OrderByNormalizedId(state.BodyParts).ToArray(),
                ActorBodyParts = OrderByNormalizedId(state.ActorBodyParts).ToArray(),
            };

            BuildActorArrays(
                state.Actors,
                out data.Actors,
                out data.ActorSpells,
                out data.ActorInventoryItems,
                out data.ActorAiPackages,
                out data.ActorTravelDestinations);
            BuildPathGridArrays(state.PathGrids, out data.PathGrids, out data.PathGridPoints, out data.PathGridConnections);
            BuildPathGridNavigationArrays(
                ref data.PathGrids,
                data.PathGridPoints,
                data.PathGridConnections,
                out data.PathGridNavigationNodes,
                out data.PathGridNavigationEdges,
                out data.PathGridNavigationPortals,
                out data.PathGridNavigationAbstractEdges,
                out data.PathGridNavigationNeighbors);
            BuildDialogueArrays(state.Dialogues, out data.Dialogues, out data.DialogueInfos);
            BuildContainerContentArrays(data.Containers, state.ContainerItems, out data.ContainerContentRanges, out data.ContainerItems);
            BuildItemEquipmentArrays(data.Items, state.ItemEquipment, out data.ItemEquipment, out data.ItemEquipmentBodyParts);
            BuildItemLeveledListArrays(state.ItemLeveledLists, out data.ItemLeveledLists, out data.ItemLeveledListEntries);
            BuildItemLeveledListArrays(state.CreatureLeveledLists, out data.CreatureLeveledLists, out data.CreatureLeveledListEntries);
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

        static void BuildPathGridArrays(
            Dictionary<string, PathGridAccumulator> map,
            out PathGridDef[] pathGrids,
            out PathGridPointDef[] points,
            out PathGridConnectionDef[] connections)
        {
            var ordered = map.OrderBy(pair => ContentId.NormalizeId(pair.Key), StringComparer.Ordinal).ToArray();
            pathGrids = new PathGridDef[ordered.Length];
            var flatPoints = new List<PathGridPointDef>(ordered.Sum(pair => pair.Value.Points.Count));
            var flatConnections = new List<PathGridConnectionDef>(ordered.Sum(pair => pair.Value.RawConnectionTargets.Count));

            for (int i = 0; i < ordered.Length; i++)
            {
                var accumulator = ordered[i].Value;
                var def = accumulator.Def;
                def.FirstPointIndex = accumulator.Points.Count > 0 ? flatPoints.Count : -1;
                def.PointCount = accumulator.Points.Count;
                def.FirstConnectionIndex = accumulator.RawConnectionTargets.Count > 0 ? flatConnections.Count : -1;

                int rawConnectionIndex = 0;
                for (int pointIndex = 0; pointIndex < accumulator.Points.Count; pointIndex++)
                {
                    var point = accumulator.Points[pointIndex];
                    int available = Math.Max(0, accumulator.RawConnectionTargets.Count - rawConnectionIndex);
                    int connectionCount = Math.Min(point.SourceConnectionCount, available);
                    point.FirstConnectionIndex = connectionCount > 0 ? flatConnections.Count : -1;
                    point.ConnectionCount = connectionCount;

                    for (int j = 0; j < connectionCount; j++)
                    {
                        flatConnections.Add(new PathGridConnectionDef
                        {
                            FromPointIndex = pointIndex,
                            ToPointIndex = accumulator.RawConnectionTargets[rawConnectionIndex],
                        });
                        rawConnectionIndex++;
                    }

                    flatPoints.Add(point);
                }

                int unusedConnections = Math.Max(0, accumulator.RawConnectionTargets.Count - rawConnectionIndex);
                for (int j = 0; j < unusedConnections; j++)
                {
                    flatConnections.Add(new PathGridConnectionDef
                    {
                        FromPointIndex = -1,
                        ToPointIndex = accumulator.RawConnectionTargets[rawConnectionIndex + j],
                    });
                }

                def.ConnectionCount = accumulator.RawConnectionTargets.Count;
                pathGrids[i] = def;
            }

            points = flatPoints.ToArray();
            connections = flatConnections.ToArray();
        }

        static void BuildPathGridNavigationArrays(
            ref PathGridDef[] pathGrids,
            PathGridPointDef[] points,
            PathGridConnectionDef[] connections,
            out PathGridNavigationNodeDef[] navigationNodes,
            out PathGridNavigationEdgeDef[] navigationEdges,
            out PathGridNavigationPortalDef[] navigationPortals,
            out PathGridNavigationAbstractEdgeDef[] navigationAbstractEdges,
            out PathGridNavigationNeighborDef[] navigationNeighbors)
        {
            pathGrids ??= Array.Empty<PathGridDef>();
            points ??= Array.Empty<PathGridPointDef>();
            connections ??= Array.Empty<PathGridConnectionDef>();

            var nodeList = new List<PathGridNavigationNodeDef>(points.Length);
            for (int pathGridIndex = 0; pathGridIndex < pathGrids.Length; pathGridIndex++)
            {
                var pathGrid = pathGrids[pathGridIndex];
                pathGrid.FirstNavigationNodeIndex = pathGrid.PointCount > 0 ? nodeList.Count : -1;
                pathGrid.NavigationNodeCount = pathGrid.PointCount;
                pathGrid.FirstNavigationEdgeIndex = -1;
                pathGrid.NavigationEdgeCount = 0;
                pathGrid.FirstNavigationPortalIndex = -1;
                pathGrid.NavigationPortalCount = 0;
                pathGrid.FirstNavigationAbstractEdgeIndex = -1;
                pathGrid.NavigationAbstractEdgeCount = 0;
                pathGrid.FirstNavigationNeighborIndex = -1;
                pathGrid.NavigationNeighborCount = 0;
                pathGrid.NavigationComponentId = -1;

                for (int pointOffset = 0; pointOffset < pathGrid.PointCount; pointOffset++)
                {
                    int pointIndex = pathGrid.FirstPointIndex + pointOffset;
                    if ((uint)pointIndex >= (uint)points.Length)
                        continue;

                    var point = points[pointIndex];
                    nodeList.Add(new PathGridNavigationNodeDef
                    {
                        PathGridIndex = pathGridIndex,
                        PointIndex = pointOffset,
                        SourceX = point.SourceX,
                        SourceY = point.SourceY,
                        SourceZ = point.SourceZ,
                        UnityX = point.UnityX,
                        UnityY = point.UnityY,
                        UnityZ = point.UnityZ,
                        FirstEdgeIndex = -1,
                        ComponentId = -1,
                    });
                }

                pathGrids[pathGridIndex] = pathGrid;
            }

            var outgoing = new List<NavigationEdgeDraft>[nodeList.Count];
            for (int i = 0; i < outgoing.Length; i++)
                outgoing[i] = new List<NavigationEdgeDraft>();

            var edgeKeys = new HashSet<long>();
            for (int pathGridIndex = 0; pathGridIndex < pathGrids.Length; pathGridIndex++)
            {
                var pathGrid = pathGrids[pathGridIndex];
                if (pathGrid.FirstNavigationNodeIndex < 0)
                    continue;

                for (int connectionOffset = 0; connectionOffset < pathGrid.ConnectionCount; connectionOffset++)
                {
                    int connectionIndex = pathGrid.FirstConnectionIndex + connectionOffset;
                    if ((uint)connectionIndex >= (uint)connections.Length)
                        continue;

                    var connection = connections[connectionIndex];
                    if ((uint)connection.FromPointIndex >= (uint)pathGrid.NavigationNodeCount ||
                        (uint)connection.ToPointIndex >= (uint)pathGrid.NavigationNodeCount)
                    {
                        continue;
                    }

                    int fromNode = pathGrid.FirstNavigationNodeIndex + connection.FromPointIndex;
                    int toNode = pathGrid.FirstNavigationNodeIndex + connection.ToPointIndex;
                    AddNavigationEdge(outgoing, edgeKeys, nodeList, fromNode, toNode, PathGridNavigationEdgeKind.Authored);
                }
            }

            InferExteriorBorderNavigationEdges(pathGrids, nodeList, outgoing, edgeKeys);

            var union = new NavigationUnionFind(nodeList.Count);
            for (int fromNode = 0; fromNode < outgoing.Length; fromNode++)
            {
                for (int edgeIndex = 0; edgeIndex < outgoing[fromNode].Count; edgeIndex++)
                    union.Union(fromNode, outgoing[fromNode][edgeIndex].ToNodeIndex);
            }

            AssignNavigationComponents(ref pathGrids, nodeList, union);
            navigationEdges = FlattenNavigationEdges(ref pathGrids, nodeList, outgoing);
            BuildNavigationPortalsAndAbstractEdges(
                ref pathGrids,
                nodeList,
                outgoing,
                out navigationPortals,
                out navigationAbstractEdges,
                out navigationNeighbors);
            navigationNodes = nodeList.ToArray();
        }

        static void InferExteriorBorderNavigationEdges(
            PathGridDef[] pathGrids,
            List<PathGridNavigationNodeDef> nodes,
            List<NavigationEdgeDraft>[] outgoing,
            HashSet<long> edgeKeys)
        {
            var exteriorByCoord = new Dictionary<long, int>();
            for (int i = 0; i < pathGrids.Length; i++)
            {
                if (pathGrids[i].IsExterior != 0)
                    exteriorByCoord[PackExteriorPathGridKey(pathGrids[i].GridX, pathGrids[i].GridY)] = i;
            }

            for (int pathGridIndex = 0; pathGridIndex < pathGrids.Length; pathGridIndex++)
            {
                var pathGrid = pathGrids[pathGridIndex];
                if (pathGrid.IsExterior == 0 || pathGrid.NavigationNodeCount <= 0)
                    continue;

                TryInferExteriorBorderNavigationEdges(pathGrids, nodes, outgoing, edgeKeys, exteriorByCoord, pathGridIndex, 1, 0);
                TryInferExteriorBorderNavigationEdges(pathGrids, nodes, outgoing, edgeKeys, exteriorByCoord, pathGridIndex, 0, 1);
            }
        }

        static void TryInferExteriorBorderNavigationEdges(
            PathGridDef[] pathGrids,
            List<PathGridNavigationNodeDef> nodes,
            List<NavigationEdgeDraft>[] outgoing,
            HashSet<long> edgeKeys,
            Dictionary<long, int> exteriorByCoord,
            int pathGridIndex,
            int deltaX,
            int deltaY)
        {
            var a = pathGrids[pathGridIndex];
            if (!exteriorByCoord.TryGetValue(PackExteriorPathGridKey(a.GridX + deltaX, a.GridY + deltaY), out int neighborIndex))
                return;

            var b = pathGrids[neighborIndex];
            if (b.NavigationNodeCount <= 0)
                return;

            int borderX = deltaX != 0 ? Math.Max(a.GridX, b.GridX) * LandRecordSize.CellUnitsMw : 0;
            int borderY = deltaY != 0 ? Math.Max(a.GridY, b.GridY) * LandRecordSize.CellUnitsMw : 0;

            for (int aOffset = 0; aOffset < a.NavigationNodeCount; aOffset++)
            {
                int aNodeIndex = a.FirstNavigationNodeIndex + aOffset;
                var aNode = nodes[aNodeIndex];
                if (!IsNearExteriorBorder(aNode, borderX, borderY, deltaX, deltaY))
                    continue;

                for (int bOffset = 0; bOffset < b.NavigationNodeCount; bOffset++)
                {
                    int bNodeIndex = b.FirstNavigationNodeIndex + bOffset;
                    var bNode = nodes[bNodeIndex];
                    if (!IsNearExteriorBorder(bNode, borderX, borderY, deltaX, deltaY))
                        continue;

                    int dx = aNode.SourceX - bNode.SourceX;
                    int dy = aNode.SourceY - bNode.SourceY;
                    int dz = aNode.SourceZ - bNode.SourceZ;
                    if (Math.Abs(dz) > ExteriorBorderVerticalDistanceMw)
                        continue;

                    long planarDistanceSq = (long)dx * dx + (long)dy * dy;
                    if (planarDistanceSq > (long)ExteriorBorderCandidateDistanceMw * ExteriorBorderCandidateDistanceMw)
                        continue;

                    AddNavigationEdge(outgoing, edgeKeys, nodes, aNodeIndex, bNodeIndex, PathGridNavigationEdgeKind.ExteriorBorder);
                    AddNavigationEdge(outgoing, edgeKeys, nodes, bNodeIndex, aNodeIndex, PathGridNavigationEdgeKind.ExteriorBorder);

                    aNode.IsPortal = 1;
                    bNode.IsPortal = 1;
                    nodes[aNodeIndex] = aNode;
                    nodes[bNodeIndex] = bNode;
                }
            }
        }

        static bool IsNearExteriorBorder(PathGridNavigationNodeDef node, int borderX, int borderY, int deltaX, int deltaY)
        {
            if (deltaX != 0)
                return Math.Abs(node.SourceX - borderX) <= ExteriorBorderCandidateDistanceMw;
            if (deltaY != 0)
                return Math.Abs(node.SourceY - borderY) <= ExteriorBorderCandidateDistanceMw;
            return false;
        }

        static void AddNavigationEdge(
            List<NavigationEdgeDraft>[] outgoing,
            HashSet<long> edgeKeys,
            List<PathGridNavigationNodeDef> nodes,
            int fromNode,
            int toNode,
            PathGridNavigationEdgeKind kind)
        {
            if ((uint)fromNode >= (uint)outgoing.Length || (uint)toNode >= (uint)outgoing.Length)
                return;

            long key = PackNavigationEdgeKey(fromNode, toNode);
            if (!edgeKeys.Add(key))
                return;

            outgoing[fromNode].Add(new NavigationEdgeDraft(fromNode, toNode, NavigationDistance(nodes[fromNode], nodes[toNode]), kind));
        }

        static float NavigationDistance(PathGridNavigationNodeDef a, PathGridNavigationNodeDef b)
        {
            float dx = a.UnityX - b.UnityX;
            float dy = a.UnityY - b.UnityY;
            float dz = a.UnityZ - b.UnityZ;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        static long PackNavigationEdgeKey(int fromNode, int toNode)
            => ((long)fromNode << 32) ^ (uint)toNode;

        static long PackExteriorPathGridKey(int gridX, int gridY)
            => ((long)gridX << 32) ^ (uint)gridY;

        static void AssignNavigationComponents(
            ref PathGridDef[] pathGrids,
            List<PathGridNavigationNodeDef> nodes,
            NavigationUnionFind union)
        {
            var componentByRoot = new Dictionary<int, int>();
            for (int i = 0; i < nodes.Count; i++)
            {
                int root = union.Find(i);
                if (!componentByRoot.TryGetValue(root, out int componentId))
                {
                    componentId = componentByRoot.Count;
                    componentByRoot[root] = componentId;
                }

                var node = nodes[i];
                node.ComponentId = componentId;
                nodes[i] = node;
            }

            for (int i = 0; i < pathGrids.Length; i++)
            {
                var pathGrid = pathGrids[i];
                if (pathGrid.FirstNavigationNodeIndex >= 0 && pathGrid.NavigationNodeCount > 0)
                    pathGrid.NavigationComponentId = nodes[pathGrid.FirstNavigationNodeIndex].ComponentId;
                pathGrids[i] = pathGrid;
            }
        }

        static PathGridNavigationEdgeDef[] FlattenNavigationEdges(
            ref PathGridDef[] pathGrids,
            List<PathGridNavigationNodeDef> nodes,
            List<NavigationEdgeDraft>[] outgoing)
        {
            var flat = new List<PathGridNavigationEdgeDef>(outgoing.Sum(list => list.Count));
            for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                var node = nodes[nodeIndex];
                node.FirstEdgeIndex = outgoing[nodeIndex].Count > 0 ? flat.Count : -1;
                node.EdgeCount = outgoing[nodeIndex].Count;
                nodes[nodeIndex] = node;

                var pathGrid = pathGrids[node.PathGridIndex];
                if (outgoing[nodeIndex].Count > 0)
                {
                    if (pathGrid.FirstNavigationEdgeIndex < 0)
                        pathGrid.FirstNavigationEdgeIndex = flat.Count;
                    pathGrid.NavigationEdgeCount += outgoing[nodeIndex].Count;
                }

                for (int i = 0; i < outgoing[nodeIndex].Count; i++)
                {
                    var edge = outgoing[nodeIndex][i];
                    flat.Add(new PathGridNavigationEdgeDef
                    {
                        FromNodeIndex = edge.FromNodeIndex,
                        ToNodeIndex = edge.ToNodeIndex,
                        Cost = edge.Cost,
                        Kind = edge.Kind,
                    });
                }

                pathGrids[node.PathGridIndex] = pathGrid;
            }

            return flat.ToArray();
        }

        static void BuildNavigationPortalsAndAbstractEdges(
            ref PathGridDef[] pathGrids,
            List<PathGridNavigationNodeDef> nodes,
            List<NavigationEdgeDraft>[] outgoing,
            out PathGridNavigationPortalDef[] portals,
            out PathGridNavigationAbstractEdgeDef[] abstractEdges,
            out PathGridNavigationNeighborDef[] neighbors)
        {
            var portalList = new List<PathGridNavigationPortalDef>();
            var portalByNode = new Dictionary<int, int>();
            for (int pathGridIndex = 0; pathGridIndex < pathGrids.Length; pathGridIndex++)
            {
                var pathGrid = pathGrids[pathGridIndex];
                pathGrid.FirstNavigationPortalIndex = -1;
                pathGrid.NavigationPortalCount = 0;

                for (int localNode = 0; localNode < pathGrid.NavigationNodeCount; localNode++)
                {
                    int nodeIndex = pathGrid.FirstNavigationNodeIndex + localNode;
                    if ((uint)nodeIndex >= (uint)nodes.Count || nodes[nodeIndex].IsPortal == 0)
                        continue;

                    if (pathGrid.FirstNavigationPortalIndex < 0)
                        pathGrid.FirstNavigationPortalIndex = portalList.Count;

                    int portalIndex = portalList.Count;
                    portalByNode[nodeIndex] = portalIndex;
                    portalList.Add(new PathGridNavigationPortalDef
                    {
                        PathGridIndex = pathGridIndex,
                        NodeIndex = nodeIndex,
                        PointIndex = nodes[nodeIndex].PointIndex,
                        FirstAbstractEdgeIndex = -1,
                        ComponentId = nodes[nodeIndex].ComponentId,
                    });
                    pathGrid.NavigationPortalCount++;
                }

                pathGrids[pathGridIndex] = pathGrid;
            }

            var outgoingAbstract = new List<PathGridNavigationAbstractEdgeDef>[portalList.Count];
            for (int i = 0; i < outgoingAbstract.Length; i++)
                outgoingAbstract[i] = new List<PathGridNavigationAbstractEdgeDef>();
            var abstractKeys = new HashSet<long>();

            for (int nodeIndex = 0; nodeIndex < outgoing.Length; nodeIndex++)
            {
                for (int edgeIndex = 0; edgeIndex < outgoing[nodeIndex].Count; edgeIndex++)
                {
                    var edge = outgoing[nodeIndex][edgeIndex];
                    if (edge.Kind != PathGridNavigationEdgeKind.ExteriorBorder)
                        continue;
                    if (!portalByNode.TryGetValue(edge.FromNodeIndex, out int fromPortal) ||
                        !portalByNode.TryGetValue(edge.ToNodeIndex, out int toPortal))
                    {
                        continue;
                    }

                    AddAbstractEdge(outgoingAbstract, abstractKeys, fromPortal, toPortal, edge.Cost, PathGridNavigationEdgeKind.ExteriorBorder);
                }
            }

            for (int pathGridIndex = 0; pathGridIndex < pathGrids.Length; pathGridIndex++)
            {
                var pathGrid = pathGrids[pathGridIndex];
                if (pathGrid.NavigationPortalCount <= 1)
                    continue;

                int firstPortal = pathGrid.FirstNavigationPortalIndex;
                int endPortal = firstPortal + pathGrid.NavigationPortalCount;
                for (int fromPortal = firstPortal; fromPortal < endPortal; fromPortal++)
                {
                    for (int toPortal = firstPortal; toPortal < endPortal; toPortal++)
                    {
                        if (fromPortal == toPortal)
                            continue;

                        float cost = FindAuthoredPathCost(
                            pathGridIndex,
                            portalList[fromPortal].NodeIndex,
                            portalList[toPortal].NodeIndex,
                            nodes,
                            outgoing);
                        if (!float.IsPositiveInfinity(cost))
                            AddAbstractEdge(outgoingAbstract, abstractKeys, fromPortal, toPortal, cost, PathGridNavigationEdgeKind.IntraPathGrid);
                    }
                }
            }

            var flatAbstract = new List<PathGridNavigationAbstractEdgeDef>(outgoingAbstract.Sum(list => list.Count));
            for (int portalIndex = 0; portalIndex < portalList.Count; portalIndex++)
            {
                var portal = portalList[portalIndex];
                portal.FirstAbstractEdgeIndex = outgoingAbstract[portalIndex].Count > 0 ? flatAbstract.Count : -1;
                portal.AbstractEdgeCount = outgoingAbstract[portalIndex].Count;
                portalList[portalIndex] = portal;

                var pathGrid = pathGrids[portal.PathGridIndex];
                if (outgoingAbstract[portalIndex].Count > 0)
                {
                    if (pathGrid.FirstNavigationAbstractEdgeIndex < 0)
                        pathGrid.FirstNavigationAbstractEdgeIndex = flatAbstract.Count;
                    pathGrid.NavigationAbstractEdgeCount += outgoingAbstract[portalIndex].Count;
                }

                flatAbstract.AddRange(outgoingAbstract[portalIndex]);
                pathGrids[portal.PathGridIndex] = pathGrid;
            }

            abstractEdges = flatAbstract.ToArray();
            portals = portalList.ToArray();
            neighbors = BuildNavigationNeighbors(ref pathGrids, portals, abstractEdges);
        }

        static void AddAbstractEdge(
            List<PathGridNavigationAbstractEdgeDef>[] outgoing,
            HashSet<long> keys,
            int fromPortal,
            int toPortal,
            float cost,
            PathGridNavigationEdgeKind kind)
        {
            if ((uint)fromPortal >= (uint)outgoing.Length || (uint)toPortal >= (uint)outgoing.Length)
                return;

            long key = PackNavigationEdgeKey(fromPortal, toPortal);
            if (!keys.Add(key))
                return;

            outgoing[fromPortal].Add(new PathGridNavigationAbstractEdgeDef
            {
                FromPortalIndex = fromPortal,
                ToPortalIndex = toPortal,
                Cost = cost,
                Kind = kind,
            });
        }

        static float FindAuthoredPathCost(
            int pathGridIndex,
            int startNode,
            int goalNode,
            List<PathGridNavigationNodeDef> nodes,
            List<NavigationEdgeDraft>[] outgoing)
        {
            if (startNode == goalNode)
                return 0f;

            var dist = new float[nodes.Count];
            var closed = new bool[nodes.Count];
            var open = new List<int>();
            for (int i = 0; i < dist.Length; i++)
                dist[i] = float.PositiveInfinity;

            dist[startNode] = 0f;
            open.Add(startNode);
            while (open.Count > 0)
            {
                int bestOpenIndex = 0;
                float bestCost = dist[open[0]];
                for (int i = 1; i < open.Count; i++)
                {
                    float cost = dist[open[i]];
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestOpenIndex = i;
                    }
                }

                int current = open[bestOpenIndex];
                open.RemoveAt(bestOpenIndex);
                if (closed[current])
                    continue;

                if (current == goalNode)
                    return dist[current];

                closed[current] = true;
                for (int edgeIndex = 0; edgeIndex < outgoing[current].Count; edgeIndex++)
                {
                    var edge = outgoing[current][edgeIndex];
                    if (edge.Kind != PathGridNavigationEdgeKind.Authored)
                        continue;

                    int toNode = edge.ToNodeIndex;
                    if ((uint)toNode >= (uint)nodes.Count ||
                        nodes[toNode].PathGridIndex != pathGridIndex ||
                        closed[toNode])
                    {
                        continue;
                    }

                    float tentative = dist[current] + edge.Cost;
                    if (tentative >= dist[toNode])
                        continue;

                    dist[toNode] = tentative;
                    open.Add(toNode);
                }
            }

            return float.PositiveInfinity;
        }

        static PathGridNavigationNeighborDef[] BuildNavigationNeighbors(
            ref PathGridDef[] pathGrids,
            PathGridNavigationPortalDef[] portals,
            PathGridNavigationAbstractEdgeDef[] abstractEdges)
        {
            var map = new Dictionary<long, PathGridNavigationNeighborDef>();
            for (int i = 0; i < abstractEdges.Length; i++)
            {
                var edge = abstractEdges[i];
                if (edge.Kind != PathGridNavigationEdgeKind.ExteriorBorder)
                    continue;
                if ((uint)edge.FromPortalIndex >= (uint)portals.Length ||
                    (uint)edge.ToPortalIndex >= (uint)portals.Length)
                {
                    continue;
                }

                int fromPathGrid = portals[edge.FromPortalIndex].PathGridIndex;
                int toPathGrid = portals[edge.ToPortalIndex].PathGridIndex;
                long key = PackNavigationEdgeKey(fromPathGrid, toPathGrid);
                if (map.TryGetValue(key, out var neighbor))
                {
                    neighbor.BorderEdgeCount++;
                    neighbor.MinCost = Math.Min(neighbor.MinCost, edge.Cost);
                    map[key] = neighbor;
                }
                else
                {
                    map[key] = new PathGridNavigationNeighborDef
                    {
                        PathGridIndex = fromPathGrid,
                        NeighborPathGridIndex = toPathGrid,
                        BorderEdgeCount = 1,
                        MinCost = edge.Cost,
                    };
                }
            }

            var result = map.Values
                .OrderBy(value => value.PathGridIndex)
                .ThenBy(value => value.NeighborPathGridIndex)
                .ToArray();

            int cursor = 0;
            for (int i = 0; i < pathGrids.Length; i++)
            {
                var pathGrid = pathGrids[i];
                pathGrid.FirstNavigationNeighborIndex = -1;
                pathGrid.NavigationNeighborCount = 0;
                while (cursor < result.Length && result[cursor].PathGridIndex < i)
                    cursor++;
                int start = cursor;
                while (cursor < result.Length && result[cursor].PathGridIndex == i)
                    cursor++;
                int count = cursor - start;
                if (count > 0)
                {
                    pathGrid.FirstNavigationNeighborIndex = start;
                    pathGrid.NavigationNeighborCount = count;
                }

                pathGrids[i] = pathGrid;
            }

            return result;
        }

        static void BuildActorArrays(
            Dictionary<string, ActorAccumulator> map,
            out ActorDef[] actors,
            out ActorSpellDef[] spells,
            out ContainerItemDef[] inventoryItems,
            out ActorAiPackageDef[] aiPackages,
            out ActorTravelDestinationDef[] travelDestinations)
        {
            var ordered = map.OrderBy(pair => ContentId.NormalizeId(pair.Key), StringComparer.Ordinal).ToArray();
            actors = new ActorDef[ordered.Length];
            var flatSpells = new List<ActorSpellDef>(ordered.Sum(pair => pair.Value.Spells.Count));
            var flatItems = new List<ContainerItemDef>(ordered.Sum(pair => pair.Value.InventoryItems.Count));
            var flatAiPackages = new List<ActorAiPackageDef>(ordered.Sum(pair => pair.Value.AiPackages.Count));
            var flatTravelDestinations = new List<ActorTravelDestinationDef>(ordered.Sum(pair => pair.Value.TravelDestinations.Count));

            for (int i = 0; i < ordered.Length; i++)
            {
                var accumulator = ordered[i].Value;
                var def = accumulator.Def;
                def.FirstSpellIndex = accumulator.Spells.Count > 0 ? flatSpells.Count : -1;
                def.SpellCount = accumulator.Spells.Count;
                def.FirstInventoryIndex = accumulator.InventoryItems.Count > 0 ? flatItems.Count : -1;
                def.InventoryCount = accumulator.InventoryItems.Count;
                def.FirstAiPackageIndex = accumulator.AiPackages.Count > 0 ? flatAiPackages.Count : -1;
                def.AiPackageCount = accumulator.AiPackages.Count;
                def.FirstTravelDestinationIndex = accumulator.TravelDestinations.Count > 0 ? flatTravelDestinations.Count : -1;
                def.TravelDestinationCount = accumulator.TravelDestinations.Count;
                actors[i] = def;
                flatSpells.AddRange(accumulator.Spells);
                flatItems.AddRange(accumulator.InventoryItems);
                flatAiPackages.AddRange(accumulator.AiPackages);
                flatTravelDestinations.AddRange(accumulator.TravelDestinations);
            }

            spells = flatSpells.ToArray();
            inventoryItems = flatItems.ToArray();
            aiPackages = flatAiPackages.ToArray();
            travelDestinations = flatTravelDestinations.ToArray();
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

        static void BuildContainerContentArrays(
            BaseDef[] containers,
            Dictionary<string, List<ContainerItemDef>> itemMap,
            out ContainerContentRangeDef[] ranges,
            out ContainerItemDef[] items)
        {
            ranges = new ContainerContentRangeDef[containers?.Length ?? 0];
            var flatItems = new List<ContainerItemDef>();

            for (int i = 0; i < ranges.Length; i++)
            {
                string id = containers[i].Id ?? string.Empty;
                if (!itemMap.TryGetValue(id, out var containerItems) || containerItems == null || containerItems.Count == 0)
                {
                    ranges[i] = new ContainerContentRangeDef
                    {
                        FirstItemIndex = -1,
                        ItemCount = 0,
                    };
                    continue;
                }

                ranges[i] = new ContainerContentRangeDef
                {
                    FirstItemIndex = flatItems.Count,
                    ItemCount = containerItems.Count,
                };
                flatItems.AddRange(containerItems);
            }

            items = flatItems.ToArray();
        }

        static void BuildItemEquipmentArrays(
            BaseDef[] items,
            Dictionary<string, ItemEquipmentAccumulator> equipmentMap,
            out ItemEquipmentDef[] equipmentDefs,
            out ItemEquipmentBodyPartDef[] bodyPartDefs)
        {
            var defs = new List<ItemEquipmentDef>();
            var flatBodyParts = new List<ItemEquipmentBodyPartDef>();
            if (items == null || equipmentMap == null || equipmentMap.Count == 0)
            {
                equipmentDefs = Array.Empty<ItemEquipmentDef>();
                bodyPartDefs = Array.Empty<ItemEquipmentBodyPartDef>();
                return;
            }

            for (int i = 0; i < items.Length; i++)
            {
                string id = items[i].Id ?? string.Empty;
                if (!equipmentMap.TryGetValue(id, out var equipment) || equipment == null)
                    continue;

                var def = equipment.Def;
                def.Item = ItemDefHandle.FromIndex(i);
                def.FirstBodyPartIndex = equipment.BodyParts.Count > 0 ? flatBodyParts.Count : -1;
                def.BodyPartCount = equipment.BodyParts.Count;
                defs.Add(def);

                for (int partIndex = 0; partIndex < equipment.BodyParts.Count; partIndex++)
                {
                    var bodyPart = equipment.BodyParts[partIndex];
                    bodyPart.Item = def.Item;
                    flatBodyParts.Add(bodyPart);
                }
            }

            equipmentDefs = defs.ToArray();
            bodyPartDefs = flatBodyParts.ToArray();
        }

        static void BuildItemLeveledListArrays(
            Dictionary<string, ItemLeveledListAccumulator> map,
            out ItemLeveledListDef[] defs,
            out ItemLeveledListEntryDef[] entries)
        {
            var ordered = map.OrderBy(pair => ContentId.NormalizeId(pair.Key), StringComparer.Ordinal).ToArray();
            defs = new ItemLeveledListDef[ordered.Length];
            var flatEntries = new List<ItemLeveledListEntryDef>(ordered.Sum(pair => pair.Value.Entries.Count));

            for (int i = 0; i < ordered.Length; i++)
            {
                var def = ordered[i].Value.Def;
                def.FirstEntryIndex = flatEntries.Count;
                def.EntryCount = ordered[i].Value.Entries.Count;
                defs[i] = def;
                flatEntries.AddRange(ordered[i].Value.Entries);
            }

            entries = flatEntries.ToArray();
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

        static AmbientSettingsDef BuildAmbientSettings(string installPath)
        {
            const float defaultMinSeconds = 1f;
            const float defaultMaxSeconds = 5f;

            string iniPath = Path.Combine(installPath ?? string.Empty, "Morrowind.ini");
            if (!File.Exists(iniPath))
            {
                return new AmbientSettingsDef
                {
                    MinSecondsBetweenEnvironmentalSounds = defaultMinSeconds,
                    MaxSecondsBetweenEnvironmentalSounds = defaultMaxSeconds,
                };
            }

            var ini = MorrowindIniReader.Read(iniPath);
            float minSeconds = ReadIniFloat(ini, "Weather", "Minimum Time Between Environmental Sounds", defaultMinSeconds);
            float maxSeconds = ReadIniFloat(ini, "Weather", "Maximum Time Between Environmental Sounds", defaultMaxSeconds);
            minSeconds = ClampPositiveSeconds(minSeconds, defaultMinSeconds);
            maxSeconds = ClampPositiveSeconds(maxSeconds, defaultMaxSeconds);
            if (maxSeconds < minSeconds)
                maxSeconds = minSeconds;

            return new AmbientSettingsDef
            {
                MinSecondsBetweenEnvironmentalSounds = minSeconds,
                MaxSecondsBetweenEnvironmentalSounds = maxSeconds,
            };
        }

        static float ReadIniFloat(MorrowindIniReader ini, string section, string key, float fallback)
        {
            string value = ini.GetValueOrDefault(section, key, string.Empty);
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                ? parsed
                : fallback;
        }

        static float ClampPositiveSeconds(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                return fallback;
            return value;
        }

        static string BuildGeneratedRecordId(uint recordTag, int primaryIndex, int sequence)
        {
            string tag = FourCcToString(recordTag).ToLowerInvariant();
            return primaryIndex != 0
                ? $"{tag}:{primaryIndex}"
                : $"{tag}:record-{sequence}";
        }

        static string FourCcToString(uint tag)
        {
            Span<char> chars = stackalloc char[4];
            chars[0] = (char)(tag & 0xFF);
            chars[1] = (char)((tag >> 8) & 0xFF);
            chars[2] = (char)((tag >> 16) & 0xFF);
            chars[3] = (char)((tag >> 24) & 0xFF);
            return new string(chars);
        }

        static void PopulateManifestCounts(GameplayContentManifest manifest, GameplayContentData data)
        {
            manifest.ActorCount = data.Actors.Length;
            manifest.ActivatorCount = data.Activators.Length;
            manifest.DoorCount = data.Doors.Length;
            manifest.ContainerCount = data.Containers.Length;
            manifest.ItemCount = data.Items.Length;
            manifest.LightCount = data.Lights.Length;
            manifest.ItemLeveledListCount = data.ItemLeveledLists.Length;
            manifest.ItemLeveledListEntryCount = data.ItemLeveledListEntries.Length;
            manifest.CreatureLeveledListCount = data.CreatureLeveledLists.Length;
            manifest.CreatureLeveledListEntryCount = data.CreatureLeveledListEntries.Length;
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
            manifest.AmbientSettingsCount = 1;
            manifest.GameSettingCount = data.GameSettings.Length;
            manifest.GlobalCount = data.Globals.Length;
            manifest.ClassCount = data.Classes.Length;
            manifest.FactionCount = data.Factions.Length;
            manifest.RaceCount = data.Races.Length;
            manifest.BirthsignCount = data.Birthsigns.Length;
            manifest.SkillCount = data.Skills.Length;
            manifest.ScriptCount = data.Scripts.Length;
            manifest.StartScriptCount = data.StartScripts.Length;
            manifest.SoundGeneratorCount = data.SoundGenerators.Length;
            manifest.LandTextureCount = data.LandTextures.Length;
            manifest.StaticCount = data.Statics.Length;
            manifest.BodyPartCount = data.ActorBodyParts?.Length > 0 ? data.ActorBodyParts.Length : data.BodyParts.Length;
            manifest.PathGridCount = data.PathGrids.Length;
            manifest.PathGridNavigationNodeCount = data.PathGridNavigationNodes.Length;
            manifest.PathGridNavigationEdgeCount = data.PathGridNavigationEdges.Length;
            manifest.PathGridNavigationPortalCount = data.PathGridNavigationPortals.Length;
            manifest.PathGridNavigationAbstractEdgeCount = data.PathGridNavigationAbstractEdges.Length;
            manifest.PathGridNavigationNeighborCount = data.PathGridNavigationNeighbors.Length;
        }

        static void WriteValidationReport(string installPath, GameplayContentData data)
        {
            var issues = new List<ValidationIssue>(256);
            var soundIds = new HashSet<string>(data.Sounds.Select(sound => ContentId.NormalizeId(sound.Id)), StringComparer.OrdinalIgnoreCase);
            var spellIds = new HashSet<string>(data.Spells.Select(spell => ContentId.NormalizeId(spell.Id)), StringComparer.OrdinalIgnoreCase);
            var placeableIds = new HashSet<string>(GameplayContentReferenceIndex.BuildPlaceableIndex(data).Keys, StringComparer.OrdinalIgnoreCase);
            var assetIndex = BuildAssetIndex(installPath);

            ValidateBaseDefs("Activator", data.Activators, soundIds, assetIndex, issues);
            ValidateBaseDefs("Door", data.Doors, soundIds, assetIndex, issues);
            ValidateBaseDefs("Container", data.Containers, soundIds, assetIndex, issues);
            ValidateBaseDefs("Item", data.Items, soundIds, assetIndex, issues);
            ValidateLights(data.Lights, soundIds, assetIndex, issues);
            ValidateActors(data, spellIds, placeableIds, assetIndex, issues);
            ValidatePathGrids(data.PathGrids, data.PathGridPoints, data.PathGridConnections, issues);
            ValidatePathGridNavigation(data, issues);
            ValidateGenericRecords("Static", data.Statics, assetIndex, issues);
            ValidateGenericRecords("BodyPart", data.BodyParts, assetIndex, issues);
            ValidateActorBodyParts(data.ActorBodyParts, assetIndex, issues);
            ValidateSounds(data.Sounds, assetIndex, issues);
            ValidateDialogue(data.Dialogues, data.DialogueInfos, issues);
            ValidateMagicEffects(data.MagicEffects, soundIds, assetIndex, issues);
            ValidateRegions(data.Regions, data.RegionSoundRefs, soundIds, issues);
            ValidateAmbientSettings(data.AmbientSettings, issues);

            Directory.CreateDirectory(Path.GetDirectoryName(CachePaths.GameplayValidationReport) ?? string.Empty);
            using var writer = new StreamWriter(CachePaths.GameplayValidationReport, false, Encoding.UTF8);
            writer.WriteLine("VVardenfell Gameplay Content Validation");
            writer.WriteLine($"Generated: {DateTime.UtcNow:O}");
            WriteRecordCounts(writer, data);
            WriteActorStatCoverage(writer, data);
            WritePathGridCoverage(writer, data);
            writer.WriteLine($"AmbientSettings.MinSeconds={data.AmbientSettings.MinSecondsBetweenEnvironmentalSounds:0.###}, AmbientSettings.MaxSeconds={data.AmbientSettings.MaxSecondsBetweenEnvironmentalSounds:0.###}");
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

        static void WriteRecordCounts(StreamWriter writer, GameplayContentData data)
        {
            writer.WriteLine("Record counts:");
            writer.WriteLine($"  TES3=world-owned, CELL=world-owned, LAND=world-owned");
            writer.WriteLine($"  GMST={data.GameSettings.Length}, GLOB={data.Globals.Length}, CLAS={data.Classes.Length}, FACT={data.Factions.Length}, RACE={data.Races.Length}, BSGN={data.Birthsigns.Length}, SKIL={data.Skills.Length}");
            writer.WriteLine($"  MGEF={data.MagicEffects.Length}, SCPT={data.Scripts.Length}, SSCR={data.StartScripts.Length}, REGN={data.Regions.Length}, SOUN={data.Sounds.Length}, SNDG={data.SoundGenerators.Length}, LTEX={data.LandTextures.Length}");
            writer.WriteLine($"  STAT={data.Statics.Length}, ACTI={data.Activators.Length}, DOOR={data.Doors.Length}, CONT={data.Containers.Length}, LIGH={data.Lights.Length}");
            writer.WriteLine($"  LOCK={CountBaseByTag(data.Items, LockTag)}, PROB={CountBaseByTag(data.Items, ProbTag)}, REPA={CountBaseByTag(data.Items, RepaTag)}, MISC={CountBaseByTag(data.Items, MiscTag)}, WEAP={CountBaseByTag(data.Items, WeapTag)}, ARMO={CountBaseByTag(data.Items, ArmoTag)}, CLOT={CountBaseByTag(data.Items, ClotTag)}, BOOK={CountBaseByTag(data.Items, BookTag)}, ALCH={CountBaseByTag(data.Items, AlchTag)}, APPA={CountBaseByTag(data.Items, AppaTag)}, INGR={CountBaseByTag(data.Items, IngrTag)}");
            writer.WriteLine($"  BODY={data.BodyParts.Length}, typed BODY={data.ActorBodyParts.Length}, NPC_={CountActorsByKind(data.Actors, ActorDefKind.Npc)}, CREA={CountActorsByKind(data.Actors, ActorDefKind.Creature)}, NPCS actor spells={data.ActorSpells.Length}, NPCO actor items={data.ActorInventoryItems.Length}, AI packages={data.ActorAiPackages.Length}, transport destinations={data.ActorTravelDestinations.Length}, LEVI={data.ItemLeveledLists.Length}, LEVI entries={data.ItemLeveledListEntries.Length}, LEVC={data.CreatureLeveledLists.Length}, LEVC entries={data.CreatureLeveledListEntries.Length}");
            writer.WriteLine($"  SPEL={data.Spells.Length}, ENCH={data.Enchantments.Length}, DIAL={data.Dialogues.Length}, INFO={data.DialogueInfos.Length}, PGRD={data.PathGrids.Length}, PGRD points={data.PathGridPoints.Length}, PGRD connections={data.PathGridConnections.Length}, HPG nodes={data.PathGridNavigationNodes.Length}, HPG edges={data.PathGridNavigationEdges.Length}, HPG portals={data.PathGridNavigationPortals.Length}, HPG abstract edges={data.PathGridNavigationAbstractEdges.Length}, HPG neighbors={data.PathGridNavigationNeighbors.Length}");
        }

        static int CountBaseByTag(BaseDef[] defs, uint tag)
        {
            int count = 0;
            for (int i = 0; i < defs.Length; i++)
            {
                if (defs[i].RecordTag == tag)
                    count++;
            }
            return count;
        }

        static int CountActorsByKind(ActorDef[] defs, ActorDefKind kind)
        {
            int count = 0;
            for (int i = 0; i < defs.Length; i++)
            {
                if (defs[i].Kind == kind)
                    count++;
            }
            return count;
        }

        static void WriteActorStatCoverage(StreamWriter writer, GameplayContentData data)
        {
            int npcManual = 0;
            int npcAuto = 0;
            int npcWithSpells = 0;
            int npcWithInventory = 0;
            int npcWithAiPackages = 0;
            int npcWithTravelDestinations = 0;
            int creaturesWithVitals = 0;
            int creaturesWithSpells = 0;
            int creaturesWithInventory = 0;
            int creaturesWithAiPackages = 0;
            int creaturesWithTravelDestinations = 0;

            for (int i = 0; i < data.Actors.Length; i++)
            {
                var actor = data.Actors[i];
                if (actor.Kind == ActorDefKind.Npc)
                {
                    if (actor.AutoCalculatedStats != 0)
                        npcAuto++;
                    else
                        npcManual++;
                    if (actor.SpellCount > 0)
                        npcWithSpells++;
                    if (actor.InventoryCount > 0)
                        npcWithInventory++;
                    if (actor.AiPackageCount > 0)
                        npcWithAiPackages++;
                    if (actor.TravelDestinationCount > 0)
                        npcWithTravelDestinations++;
                }
                else
                {
                    if (actor.Vitals.Health > 0 || actor.Vitals.Magicka > 0 || actor.Vitals.Fatigue > 0)
                        creaturesWithVitals++;
                    if (actor.SpellCount > 0)
                        creaturesWithSpells++;
                    if (actor.InventoryCount > 0)
                        creaturesWithInventory++;
                    if (actor.AiPackageCount > 0)
                        creaturesWithAiPackages++;
                    if (actor.TravelDestinationCount > 0)
                        creaturesWithTravelDestinations++;
                }
            }

            writer.WriteLine($"Actor stat coverage: NPC manual={npcManual}, NPC autocalc={npcAuto}, NPC with spells={npcWithSpells}, NPC with inventory={npcWithInventory}, NPC with AI packages={npcWithAiPackages}, NPC with travel destinations={npcWithTravelDestinations}, CREA with vitals={creaturesWithVitals}, CREA with spells={creaturesWithSpells}, CREA with inventory={creaturesWithInventory}, CREA with AI packages={creaturesWithAiPackages}, CREA with travel destinations={creaturesWithTravelDestinations}");
        }

        static void WritePathGridCoverage(StreamWriter writer, GameplayContentData data)
        {
            int interiorCount = 0;
            int exteriorCount = 0;
            int authoredEdges = 0;
            int exteriorBorderEdges = 0;
            int intraAbstractEdges = 0;
            int exteriorAbstractEdges = 0;
            int isolatedExteriorPathGrids = 0;
            var components = new HashSet<int>();
            for (int i = 0; i < data.PathGrids.Length; i++)
            {
                if (data.PathGrids[i].IsExterior != 0)
                {
                    exteriorCount++;
                    if (data.PathGrids[i].NavigationNeighborCount <= 0)
                        isolatedExteriorPathGrids++;
                }
                else
                {
                    interiorCount++;
                }
            }

            for (int i = 0; i < data.PathGridNavigationNodes.Length; i++)
            {
                if (data.PathGridNavigationNodes[i].ComponentId >= 0)
                    components.Add(data.PathGridNavigationNodes[i].ComponentId);
            }

            for (int i = 0; i < data.PathGridNavigationEdges.Length; i++)
            {
                if (data.PathGridNavigationEdges[i].Kind == PathGridNavigationEdgeKind.ExteriorBorder)
                    exteriorBorderEdges++;
                else if (data.PathGridNavigationEdges[i].Kind == PathGridNavigationEdgeKind.Authored)
                    authoredEdges++;
            }

            for (int i = 0; i < data.PathGridNavigationAbstractEdges.Length; i++)
            {
                if (data.PathGridNavigationAbstractEdges[i].Kind == PathGridNavigationEdgeKind.ExteriorBorder)
                    exteriorAbstractEdges++;
                else if (data.PathGridNavigationAbstractEdges[i].Kind == PathGridNavigationEdgeKind.IntraPathGrid)
                    intraAbstractEdges++;
            }

            writer.WriteLine($"Pathgrid coverage: interior={interiorCount}, exterior={exteriorCount}, points={data.PathGridPoints.Length}, connections={data.PathGridConnections.Length}");
            writer.WriteLine($"Pathgrid HPG coverage: nodes={data.PathGridNavigationNodes.Length}, fine edges={data.PathGridNavigationEdges.Length}, authored edges={authoredEdges}, inferred exterior border edges={exteriorBorderEdges}, portals={data.PathGridNavigationPortals.Length}, abstract edges={data.PathGridNavigationAbstractEdges.Length}, abstract intra={intraAbstractEdges}, abstract exterior={exteriorAbstractEdges}, components={components.Count}, neighbors={data.PathGridNavigationNeighbors.Length}, isolated exterior pathgrids={isolatedExteriorPathGrids}");
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

        static void ValidateActors(
            GameplayContentData data,
            HashSet<string> spellIds,
            HashSet<string> placeableIds,
            HashSet<string> assetIndex,
            List<ValidationIssue> issues)
        {
            var defs = data.Actors ?? Array.Empty<ActorDef>();
            for (int i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                string family = def.Kind == ActorDefKind.Npc ? "NPC" : "Creature";
                ValidateAssetPath(family, def.Id, "model", def.Model, assetIndex, issues);
                ValidateActorStats(family, def, issues);
                ValidateActorSpellRange(family, def, data.ActorSpells, spellIds, issues);
                ValidateActorInventoryRange(family, def, data.ActorInventoryItems, placeableIds, issues);
                ValidateActorAiPackageRange(family, def, data.ActorAiPackages, issues);
                ValidateActorTravelDestinationRange(family, def, data.ActorTravelDestinations, issues);
            }
        }

        static void ValidatePathGrids(
            PathGridDef[] pathGrids,
            PathGridPointDef[] points,
            PathGridConnectionDef[] connections,
            List<ValidationIssue> issues)
        {
            pathGrids ??= Array.Empty<PathGridDef>();
            points ??= Array.Empty<PathGridPointDef>();
            connections ??= Array.Empty<PathGridConnectionDef>();

            for (int i = 0; i < pathGrids.Length; i++)
            {
                var pathGrid = pathGrids[i];
                if (pathGrid.PointCount != pathGrid.DeclaredPointCount)
                {
                    issues.Add(new ValidationIssue
                    {
                        IsError = false,
                        Message = $"Pathgrid '{pathGrid.Id}' declared {pathGrid.DeclaredPointCount} point(s) but parsed {pathGrid.PointCount}.",
                    });
                }

                if (pathGrid.PointCount > 0
                    && (pathGrid.FirstPointIndex < 0 || pathGrid.FirstPointIndex + pathGrid.PointCount > points.Length))
                {
                    issues.Add(new ValidationIssue
                    {
                        IsError = true,
                        Message = $"Pathgrid '{pathGrid.Id}' has an out-of-range point window ({pathGrid.FirstPointIndex}, {pathGrid.PointCount}).",
                    });
                    continue;
                }

                if (pathGrid.ConnectionCount > 0
                    && (pathGrid.FirstConnectionIndex < 0 || pathGrid.FirstConnectionIndex + pathGrid.ConnectionCount > connections.Length))
                {
                    issues.Add(new ValidationIssue
                    {
                        IsError = true,
                        Message = $"Pathgrid '{pathGrid.Id}' has an out-of-range connection window ({pathGrid.FirstConnectionIndex}, {pathGrid.ConnectionCount}).",
                    });
                }

                int expectedConnections = 0;
                for (int pointOffset = 0; pointOffset < pathGrid.PointCount; pointOffset++)
                {
                    int pointIndex = pathGrid.FirstPointIndex + pointOffset;
                    var point = points[pointIndex];
                    expectedConnections += point.SourceConnectionCount;

                    if (IsInvalidFloat(point.UnityX) || IsInvalidFloat(point.UnityY) || IsInvalidFloat(point.UnityZ))
                    {
                        issues.Add(new ValidationIssue
                        {
                            IsError = true,
                            Message = $"Pathgrid '{pathGrid.Id}' point {pointOffset} has invalid Unity coordinates.",
                        });
                    }

                    if (point.ConnectionCount != point.SourceConnectionCount)
                    {
                        issues.Add(new ValidationIssue
                        {
                            IsError = true,
                            Message = $"Pathgrid '{pathGrid.Id}' point {pointOffset} declared {point.SourceConnectionCount} connection(s) but parsed {point.ConnectionCount}.",
                        });
                    }

                    if (point.ConnectionCount > 0
                        && (point.FirstConnectionIndex < 0 || point.FirstConnectionIndex + point.ConnectionCount > connections.Length))
                    {
                        issues.Add(new ValidationIssue
                        {
                            IsError = true,
                            Message = $"Pathgrid '{pathGrid.Id}' point {pointOffset} has an out-of-range connection window ({point.FirstConnectionIndex}, {point.ConnectionCount}).",
                        });
                        continue;
                    }

                    ValidateExteriorPathGridPoint(pathGrid, point, pointOffset, issues);
                }

                if (expectedConnections != pathGrid.ConnectionCount)
                {
                    issues.Add(new ValidationIssue
                    {
                        IsError = true,
                        Message = $"Pathgrid '{pathGrid.Id}' expected {expectedConnections} connection target(s) from point counts but parsed {pathGrid.ConnectionCount}.",
                    });
                }

                for (int connectionOffset = 0; connectionOffset < pathGrid.ConnectionCount; connectionOffset++)
                {
                    int connectionIndex = pathGrid.FirstConnectionIndex + connectionOffset;
                    if (connectionIndex < 0 || connectionIndex >= connections.Length)
                        continue;

                    var connection = connections[connectionIndex];
                    if (connection.FromPointIndex < 0 || connection.FromPointIndex >= pathGrid.PointCount)
                    {
                        issues.Add(new ValidationIssue
                        {
                            IsError = true,
                            Message = $"Pathgrid '{pathGrid.Id}' connection {connectionOffset} has invalid source point index {connection.FromPointIndex}.",
                        });
                    }

                    if (connection.ToPointIndex < 0 || connection.ToPointIndex >= pathGrid.PointCount)
                    {
                        issues.Add(new ValidationIssue
                        {
                            IsError = true,
                            Message = $"Pathgrid '{pathGrid.Id}' connection {connectionOffset} targets out-of-range point index {connection.ToPointIndex}.",
                        });
                    }
                }
            }
        }

        static void ValidatePathGridNavigation(GameplayContentData data, List<ValidationIssue> issues)
        {
            var pathGrids = data.PathGrids ?? Array.Empty<PathGridDef>();
            var nodes = data.PathGridNavigationNodes ?? Array.Empty<PathGridNavigationNodeDef>();
            var edges = data.PathGridNavigationEdges ?? Array.Empty<PathGridNavigationEdgeDef>();
            var portals = data.PathGridNavigationPortals ?? Array.Empty<PathGridNavigationPortalDef>();
            var abstractEdges = data.PathGridNavigationAbstractEdges ?? Array.Empty<PathGridNavigationAbstractEdgeDef>();
            var neighbors = data.PathGridNavigationNeighbors ?? Array.Empty<PathGridNavigationNeighborDef>();
            var fineEdgeKeys = new HashSet<long>();
            var abstractEdgeKeys = new HashSet<long>();

            for (int i = 0; i < pathGrids.Length; i++)
            {
                var pathGrid = pathGrids[i];
                ValidateNavigationWindow($"Pathgrid '{pathGrid.Id}' node", pathGrid.FirstNavigationNodeIndex, pathGrid.NavigationNodeCount, nodes.Length, issues);
                ValidateNavigationWindow($"Pathgrid '{pathGrid.Id}' fine edge", pathGrid.FirstNavigationEdgeIndex, pathGrid.NavigationEdgeCount, edges.Length, issues);
                ValidateNavigationWindow($"Pathgrid '{pathGrid.Id}' portal", pathGrid.FirstNavigationPortalIndex, pathGrid.NavigationPortalCount, portals.Length, issues);
                ValidateNavigationWindow($"Pathgrid '{pathGrid.Id}' abstract edge", pathGrid.FirstNavigationAbstractEdgeIndex, pathGrid.NavigationAbstractEdgeCount, abstractEdges.Length, issues);
                ValidateNavigationWindow($"Pathgrid '{pathGrid.Id}' neighbor", pathGrid.FirstNavigationNeighborIndex, pathGrid.NavigationNeighborCount, neighbors.Length, issues);
            }

            for (int i = 0; i < nodes.Length; i++)
            {
                var node = nodes[i];
                if ((uint)node.PathGridIndex >= (uint)pathGrids.Length)
                {
                    issues.Add(new ValidationIssue { IsError = true, Message = $"HPG node {i} has invalid pathgrid index {node.PathGridIndex}." });
                    continue;
                }

                if (node.PointIndex < 0 || node.PointIndex >= pathGrids[node.PathGridIndex].PointCount)
                    issues.Add(new ValidationIssue { IsError = true, Message = $"HPG node {i} has invalid point index {node.PointIndex} for pathgrid '{pathGrids[node.PathGridIndex].Id}'." });
                if (node.ComponentId < 0)
                    issues.Add(new ValidationIssue { IsError = true, Message = $"HPG node {i} has invalid component id {node.ComponentId}." });
                if (IsInvalidFloat(node.UnityX) || IsInvalidFloat(node.UnityY) || IsInvalidFloat(node.UnityZ))
                    issues.Add(new ValidationIssue { IsError = true, Message = $"HPG node {i} has invalid Unity coordinates." });
                ValidateNavigationWindow($"HPG node {i} fine edge", node.FirstEdgeIndex, node.EdgeCount, edges.Length, issues);
            }

            for (int i = 0; i < edges.Length; i++)
            {
                var edge = edges[i];
                if ((uint)edge.FromNodeIndex >= (uint)nodes.Length || (uint)edge.ToNodeIndex >= (uint)nodes.Length)
                {
                    issues.Add(new ValidationIssue { IsError = true, Message = $"HPG edge {i} has invalid node range {edge.FromNodeIndex}->{edge.ToNodeIndex}." });
                    continue;
                }

                if (!fineEdgeKeys.Add(PackNavigationEdgeKey(edge.FromNodeIndex, edge.ToNodeIndex)))
                    issues.Add(new ValidationIssue { IsError = true, Message = $"HPG edge {i} duplicates {edge.FromNodeIndex}->{edge.ToNodeIndex}." });
                if (IsInvalidFloat(edge.Cost) || edge.Cost < 0f)
                    issues.Add(new ValidationIssue { IsError = true, Message = $"HPG edge {i} has invalid cost {edge.Cost}." });
                if (edge.Kind == PathGridNavigationEdgeKind.ExteriorBorder &&
                    !AreAdjacentExteriorPathGrids(pathGrids, nodes[edge.FromNodeIndex].PathGridIndex, nodes[edge.ToNodeIndex].PathGridIndex))
                {
                    issues.Add(new ValidationIssue { IsError = true, Message = $"HPG exterior border edge {i} does not connect adjacent exterior pathgrids." });
                }
            }

            for (int i = 0; i < portals.Length; i++)
            {
                var portal = portals[i];
                if ((uint)portal.NodeIndex >= (uint)nodes.Length || (uint)portal.PathGridIndex >= (uint)pathGrids.Length)
                {
                    issues.Add(new ValidationIssue { IsError = true, Message = $"HPG portal {i} has invalid node/pathgrid index." });
                    continue;
                }

                if (nodes[portal.NodeIndex].IsPortal == 0)
                    issues.Add(new ValidationIssue { IsError = true, Message = $"HPG portal {i} points at non-portal node {portal.NodeIndex}." });
                ValidateNavigationWindow($"HPG portal {i} abstract edge", portal.FirstAbstractEdgeIndex, portal.AbstractEdgeCount, abstractEdges.Length, issues);
            }

            for (int i = 0; i < abstractEdges.Length; i++)
            {
                var edge = abstractEdges[i];
                if ((uint)edge.FromPortalIndex >= (uint)portals.Length || (uint)edge.ToPortalIndex >= (uint)portals.Length)
                {
                    issues.Add(new ValidationIssue { IsError = true, Message = $"HPG abstract edge {i} has invalid portal range {edge.FromPortalIndex}->{edge.ToPortalIndex}." });
                    continue;
                }

                if (!abstractEdgeKeys.Add(PackNavigationEdgeKey(edge.FromPortalIndex, edge.ToPortalIndex)))
                    issues.Add(new ValidationIssue { IsError = true, Message = $"HPG abstract edge {i} duplicates {edge.FromPortalIndex}->{edge.ToPortalIndex}." });
                if (IsInvalidFloat(edge.Cost) || edge.Cost < 0f)
                    issues.Add(new ValidationIssue { IsError = true, Message = $"HPG abstract edge {i} has invalid cost {edge.Cost}." });
                if (edge.Kind == PathGridNavigationEdgeKind.ExteriorBorder &&
                    !AreAdjacentExteriorPathGrids(pathGrids, portals[edge.FromPortalIndex].PathGridIndex, portals[edge.ToPortalIndex].PathGridIndex))
                {
                    issues.Add(new ValidationIssue { IsError = true, Message = $"HPG abstract exterior edge {i} does not connect adjacent exterior pathgrids." });
                }
            }

            for (int i = 0; i < neighbors.Length; i++)
            {
                var neighbor = neighbors[i];
                if ((uint)neighbor.PathGridIndex >= (uint)pathGrids.Length ||
                    (uint)neighbor.NeighborPathGridIndex >= (uint)pathGrids.Length)
                {
                    issues.Add(new ValidationIssue { IsError = true, Message = $"HPG neighbor {i} has invalid pathgrid range {neighbor.PathGridIndex}->{neighbor.NeighborPathGridIndex}." });
                    continue;
                }

                if (!AreAdjacentExteriorPathGrids(pathGrids, neighbor.PathGridIndex, neighbor.NeighborPathGridIndex))
                    issues.Add(new ValidationIssue { IsError = true, Message = $"HPG neighbor {i} does not connect adjacent exterior pathgrids." });
                if (neighbor.BorderEdgeCount <= 0 || IsInvalidFloat(neighbor.MinCost) || neighbor.MinCost < 0f)
                    issues.Add(new ValidationIssue { IsError = true, Message = $"HPG neighbor {i} has invalid count/cost." });
            }
        }

        static void ValidateNavigationWindow(string label, int first, int count, int total, List<ValidationIssue> issues)
        {
            if (count < 0)
            {
                issues.Add(new ValidationIssue { IsError = true, Message = $"{label} window has negative count {count}." });
                return;
            }

            if (count == 0)
                return;

            if (first < 0 || first + count > total)
                issues.Add(new ValidationIssue { IsError = true, Message = $"{label} window is out of range ({first}, {count}) over {total}." });
        }

        static bool AreAdjacentExteriorPathGrids(PathGridDef[] pathGrids, int aIndex, int bIndex)
        {
            if ((uint)aIndex >= (uint)pathGrids.Length || (uint)bIndex >= (uint)pathGrids.Length)
                return false;

            var a = pathGrids[aIndex];
            var b = pathGrids[bIndex];
            if (a.IsExterior == 0 || b.IsExterior == 0)
                return false;

            int dx = Math.Abs(a.GridX - b.GridX);
            int dy = Math.Abs(a.GridY - b.GridY);
            return dx + dy == 1;
        }

        static void ValidateExteriorPathGridPoint(
            PathGridDef pathGrid,
            PathGridPointDef point,
            int pointOffset,
            List<ValidationIssue> issues)
        {
            if (pathGrid.IsExterior == 0)
                return;

            int minX = (pathGrid.GridX - 1) * LandRecordSize.CellUnitsMw;
            int maxX = (pathGrid.GridX + 2) * LandRecordSize.CellUnitsMw;
            int minY = (pathGrid.GridY - 1) * LandRecordSize.CellUnitsMw;
            int maxY = (pathGrid.GridY + 2) * LandRecordSize.CellUnitsMw;
            if (point.SourceX < minX || point.SourceX > maxX || point.SourceY < minY || point.SourceY > maxY)
            {
                issues.Add(new ValidationIssue
                {
                    IsError = false,
                    Message = $"Exterior pathgrid '{pathGrid.Id}' point {pointOffset} is far outside declared grid ({pathGrid.GridX}, {pathGrid.GridY}) at source ({point.SourceX}, {point.SourceY}, {point.SourceZ}).",
                });
            }
        }

        static void ValidateActorStats(string family, ActorDef def, List<ValidationIssue> issues)
        {
            if (def.Kind == ActorDefKind.Npc && def.AutoCalculatedStats != 0)
                return;

            if (def.Level <= 0)
            {
                issues.Add(new ValidationIssue
                {
                    IsError = false,
                    Message = $"{family} '{def.Id}' has non-positive level {def.Level}.",
                });
            }

            if (def.Kind == ActorDefKind.Npc)
            {
                if (!HasAnyAttribute(def.Attributes) || !HasAnySkill(def.Skills))
                {
                    issues.Add(new ValidationIssue
                    {
                        IsError = true,
                        Message = $"NPC '{def.Id}' is missing manual NPDT attributes or skills.",
                    });
                }

                if (def.Vitals.Health <= 0 || def.Vitals.Fatigue <= 0)
                {
                    issues.Add(new ValidationIssue
                    {
                        IsError = false,
                        Message = $"NPC '{def.Id}' has incomplete manual vitals health={def.Vitals.Health}, magicka={def.Vitals.Magicka}, fatigue={def.Vitals.Fatigue}.",
                    });
                }
            }
            else if (!HasAnyAttribute(def.Attributes) || def.Vitals.Health <= 0 || def.Vitals.Fatigue <= 0)
            {
                issues.Add(new ValidationIssue
                {
                    IsError = true,
                    Message = $"Creature '{def.Id}' is missing NPDT attributes or vitals.",
                });
            }
        }

        static void ValidateActorSpellRange(
            string family,
            ActorDef def,
            ActorSpellDef[] actorSpells,
            HashSet<string> spellIds,
            List<ValidationIssue> issues)
        {
            if (def.SpellCount <= 0)
                return;

            if (actorSpells == null || def.FirstSpellIndex < 0 || def.FirstSpellIndex + def.SpellCount > actorSpells.Length)
            {
                issues.Add(new ValidationIssue
                {
                    IsError = true,
                    Message = $"{family} '{def.Id}' has an out-of-range actor spell window ({def.FirstSpellIndex}, {def.SpellCount}).",
                });
                return;
            }

            for (int i = 0; i < def.SpellCount; i++)
            {
                string spellId = actorSpells[def.FirstSpellIndex + i].SpellId;
                if (spellIds.Contains(ContentId.NormalizeId(spellId)))
                    continue;

                issues.Add(new ValidationIssue
                {
                    IsError = true,
                    Message = $"{family} '{def.Id}' references missing spell '{spellId}'.",
                });
            }
        }

        static void ValidateActorInventoryRange(
            string family,
            ActorDef def,
            ContainerItemDef[] actorInventoryItems,
            HashSet<string> placeableIds,
            List<ValidationIssue> issues)
        {
            if (def.InventoryCount <= 0)
                return;

            if (actorInventoryItems == null || def.FirstInventoryIndex < 0 || def.FirstInventoryIndex + def.InventoryCount > actorInventoryItems.Length)
            {
                issues.Add(new ValidationIssue
                {
                    IsError = true,
                    Message = $"{family} '{def.Id}' has an out-of-range actor inventory window ({def.FirstInventoryIndex}, {def.InventoryCount}).",
                });
                return;
            }

            for (int i = 0; i < def.InventoryCount; i++)
            {
                var item = actorInventoryItems[def.FirstInventoryIndex + i];
                if (item.Count == 0)
                {
                    issues.Add(new ValidationIssue
                    {
                        IsError = false,
                        Message = $"{family} '{def.Id}' has inventory item '{item.ItemId}' with count 0.",
                    });
                }

                if (placeableIds.Contains(ContentId.NormalizeId(item.ItemId)))
                    continue;

                issues.Add(new ValidationIssue
                {
                    IsError = true,
                    Message = $"{family} '{def.Id}' references missing inventory item '{item.ItemId}'.",
                });
            }
        }

        static void ValidateActorAiPackageRange(
            string family,
            ActorDef def,
            ActorAiPackageDef[] actorAiPackages,
            List<ValidationIssue> issues)
        {
            if (def.AiPackageCount <= 0)
                return;

            if (actorAiPackages == null || def.FirstAiPackageIndex < 0 || def.FirstAiPackageIndex + def.AiPackageCount > actorAiPackages.Length)
            {
                issues.Add(new ValidationIssue
                {
                    IsError = true,
                    Message = $"{family} '{def.Id}' has an out-of-range actor AI package window ({def.FirstAiPackageIndex}, {def.AiPackageCount}).",
                });
                return;
            }

            for (int i = 0; i < def.AiPackageCount; i++)
            {
                var package = actorAiPackages[def.FirstAiPackageIndex + i];
                if (!IsKnownAiPackageType(package.Type))
                {
                    issues.Add(new ValidationIssue
                    {
                        IsError = true,
                        Message = $"{family} '{def.Id}' has actor AI package with unknown type {(int)package.Type}.",
                    });
                }

                if (RequiresAiTarget(package.Type) && string.IsNullOrWhiteSpace(package.TargetId))
                {
                    issues.Add(new ValidationIssue
                    {
                        IsError = false,
                        Message = $"{family} '{def.Id}' has {package.Type} AI package with no target id/name.",
                    });
                }
            }
        }

        static void ValidateActorTravelDestinationRange(
            string family,
            ActorDef def,
            ActorTravelDestinationDef[] actorTravelDestinations,
            List<ValidationIssue> issues)
        {
            if (def.TravelDestinationCount <= 0)
                return;

            if (actorTravelDestinations == null || def.FirstTravelDestinationIndex < 0 || def.FirstTravelDestinationIndex + def.TravelDestinationCount > actorTravelDestinations.Length)
            {
                issues.Add(new ValidationIssue
                {
                    IsError = true,
                    Message = $"{family} '{def.Id}' has an out-of-range actor travel destination window ({def.FirstTravelDestinationIndex}, {def.TravelDestinationCount}).",
                });
                return;
            }

            for (int i = 0; i < def.TravelDestinationCount; i++)
            {
                var destination = actorTravelDestinations[def.FirstTravelDestinationIndex + i];
                if (IsInvalidFloat(destination.PosX) || IsInvalidFloat(destination.PosY) || IsInvalidFloat(destination.PosZ)
                    || IsInvalidFloat(destination.RotX) || IsInvalidFloat(destination.RotY) || IsInvalidFloat(destination.RotZ))
                {
                    issues.Add(new ValidationIssue
                    {
                        IsError = true,
                        Message = $"{family} '{def.Id}' has actor travel destination with invalid position or rotation values.",
                    });
                }
            }
        }

        static bool IsKnownAiPackageType(ActorAiPackageType type)
            => type == ActorAiPackageType.Wander
            || type == ActorAiPackageType.Travel
            || type == ActorAiPackageType.Follow
            || type == ActorAiPackageType.Escort
            || type == ActorAiPackageType.Activate;

        static bool RequiresAiTarget(ActorAiPackageType type)
            => type == ActorAiPackageType.Follow
            || type == ActorAiPackageType.Escort
            || type == ActorAiPackageType.Activate;

        static bool IsInvalidFloat(float value)
            => float.IsNaN(value) || float.IsInfinity(value);

        static bool HasAnyAttribute(ActorAttributeDef attributes)
            => attributes.Strength != 0
            || attributes.Intelligence != 0
            || attributes.Willpower != 0
            || attributes.Agility != 0
            || attributes.Speed != 0
            || attributes.Endurance != 0
            || attributes.Personality != 0
            || attributes.Luck != 0;

        static bool HasAnySkill(ActorSkillDef skills)
            => skills.Block != 0
            || skills.Armorer != 0
            || skills.MediumArmor != 0
            || skills.HeavyArmor != 0
            || skills.BluntWeapon != 0
            || skills.LongBlade != 0
            || skills.Axe != 0
            || skills.Spear != 0
            || skills.Athletics != 0
            || skills.Enchant != 0
            || skills.Destruction != 0
            || skills.Alteration != 0
            || skills.Illusion != 0
            || skills.Conjuration != 0
            || skills.Mysticism != 0
            || skills.Restoration != 0
            || skills.Alchemy != 0
            || skills.Unarmored != 0
            || skills.Security != 0
            || skills.Sneak != 0
            || skills.Acrobatics != 0
            || skills.LightArmor != 0
            || skills.ShortBlade != 0
            || skills.Marksman != 0
            || skills.Mercantile != 0
            || skills.Speechcraft != 0
            || skills.HandToHand != 0;

        static void ValidateGenericRecords(string family, GenericRecordDef[] defs, HashSet<string> assetIndex, List<ValidationIssue> issues)
        {
            for (int i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                ValidateAssetPath(family, def.Id, "model", def.Model, assetIndex, issues);
                ValidateAssetPath(family, def.Id, "icon", def.Icon, assetIndex, issues);
            }
        }

        static void ValidateActorBodyParts(ActorBodyPartDef[] defs, HashSet<string> assetIndex, List<ValidationIssue> issues)
        {
            defs ??= Array.Empty<ActorBodyPartDef>();
            for (int i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                ValidateAssetPath("TypedBodyPart", def.Id, "model", def.Model, assetIndex, issues);
                if ((byte)def.Part > (byte)ActorBodyPartMeshPart.Tail)
                {
                    issues.Add(new ValidationIssue
                    {
                        IsError = true,
                        Message = $"Typed body part '{def.Id}' has unknown mesh part {(byte)def.Part}.",
                    });
                }

                if ((byte)def.Type > (byte)ActorBodyPartMeshType.Armor)
                {
                    issues.Add(new ValidationIssue
                    {
                        IsError = true,
                        Message = $"Typed body part '{def.Id}' has unknown mesh type {(byte)def.Type}.",
                    });
                }
            }
        }

        static void ValidateSounds(SoundDef[] defs, HashSet<string> assetIndex, List<ValidationIssue> issues)
        {
            for (int i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                ValidateSoundAssetPath(def, assetIndex, issues);
            }
        }

        static void ValidateSoundAssetPath(SoundDef def, HashSet<string> assetIndex, List<ValidationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(def.SoundPath))
                return;

            string corrected = VVardenfell.Core.Config.SoundPathResolver.Correct(def.SoundPath).Replace('\\', '/');
            if (assetIndex.Contains(corrected))
                return;

            string mp3Fallback = VVardenfell.Core.Config.SoundPathResolver.ChangeExtension(corrected, ".mp3").Replace('\\', '/');
            if (assetIndex.Contains(mp3Fallback))
                return;

            issues.Add(new ValidationIssue
            {
                IsError = false,
                Message = $"Sound '{def.Id}' references missing sound file '{def.SoundPath}' (resolved as '{corrected}').",
            });
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

        static void ValidateAmbientSettings(AmbientSettingsDef settings, List<ValidationIssue> issues)
        {
            if (settings.MinSecondsBetweenEnvironmentalSounds <= 0f)
            {
                issues.Add(new ValidationIssue
                {
                    IsError = true,
                    Message = "Ambient settings have a non-positive minimum time between environmental sounds.",
                });
            }

            if (settings.MaxSecondsBetweenEnvironmentalSounds < settings.MinSecondsBetweenEnvironmentalSounds)
            {
                issues.Add(new ValidationIssue
                {
                    IsError = true,
                    Message = "Ambient settings have a maximum time between environmental sounds lower than the minimum.",
                });
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
        static ushort ReadUInt16(byte[] bytes, int offset) => BitConverter.ToUInt16(bytes, offset);
        static int ReadInt32(byte[] bytes, int offset) => BitConverter.ToInt32(bytes, offset);
        static uint ReadUInt32(byte[] bytes, int offset) => BitConverter.ToUInt32(bytes, offset);
        static float ReadSingle(byte[] bytes, int offset) => BitConverter.ToSingle(bytes, offset);

        static bool TryReadContainerItem(byte[] bytes, out ContainerItemDef item)
        {
            item = default;
            if (bytes == null || bytes.Length < 5)
                return false;

            item = new ContainerItemDef
            {
                Count = ReadInt32(bytes, 0),
                ItemId = ReadFixedString(bytes, 4, bytes.Length - 4),
            };
            return !string.IsNullOrWhiteSpace(item.ItemId);
        }

        static bool TryReadAiWanderPackage(byte[] bytes, out ActorAiPackageDef package)
        {
            package = default;
            if (bytes == null || bytes.Length < 14)
                return false;

            package = new ActorAiPackageDef
            {
                Type = ActorAiPackageType.Wander,
                WanderDistance = ReadInt16(bytes, 0),
                Duration = ReadInt16(bytes, 2),
                TimeOfDay = bytes[4],
                Idle0 = bytes[5],
                Idle1 = bytes[6],
                Idle2 = bytes[7],
                Idle3 = bytes[8],
                Idle4 = bytes[9],
                Idle5 = bytes[10],
                Idle6 = bytes[11],
                Idle7 = bytes[12],
                ShouldRepeat = bytes[13],
            };
            return true;
        }

        static bool TryReadAiTravelPackage(byte[] bytes, out ActorAiPackageDef package)
        {
            package = default;
            if (bytes == null || bytes.Length < 13)
                return false;

            package = new ActorAiPackageDef
            {
                Type = ActorAiPackageType.Travel,
                X = ReadSingle(bytes, 0),
                Y = ReadSingle(bytes, 4),
                Z = ReadSingle(bytes, 8),
                ShouldRepeat = bytes[12],
            };
            return true;
        }

        static bool TryReadAiTargetPackage(byte[] bytes, ActorAiPackageType type, out ActorAiPackageDef package)
        {
            package = default;
            if (bytes == null || bytes.Length < 47)
                return false;

            package = new ActorAiPackageDef
            {
                Type = type,
                X = ReadSingle(bytes, 0),
                Y = ReadSingle(bytes, 4),
                Z = ReadSingle(bytes, 8),
                Duration = ReadInt16(bytes, 12),
                TargetId = ReadFixedString(bytes, 14, 32),
                ShouldRepeat = bytes[46],
            };
            return true;
        }

        static bool TryReadAiActivatePackage(byte[] bytes, out ActorAiPackageDef package)
        {
            package = default;
            if (bytes == null || bytes.Length < 33)
                return false;

            package = new ActorAiPackageDef
            {
                Type = ActorAiPackageType.Activate,
                TargetId = ReadFixedString(bytes, 0, 32),
                ShouldRepeat = bytes[32],
            };
            return true;
        }

        static bool TryReadTravelDestination(byte[] bytes, out ActorTravelDestinationDef destination)
        {
            destination = default;
            if (bytes == null || bytes.Length < 24)
                return false;

            destination = new ActorTravelDestinationDef
            {
                PosX = ReadSingle(bytes, 0),
                PosY = ReadSingle(bytes, 4),
                PosZ = ReadSingle(bytes, 8),
                RotX = ReadSingle(bytes, 12),
                RotY = ReadSingle(bytes, 16),
                RotZ = ReadSingle(bytes, 20),
            };
            return true;
        }

        static ActorAttributeDef ReadNpcAttributes(byte[] bytes, int offset)
        {
            return new ActorAttributeDef
            {
                Strength = bytes[offset],
                Intelligence = bytes[offset + 1],
                Willpower = bytes[offset + 2],
                Agility = bytes[offset + 3],
                Speed = bytes[offset + 4],
                Endurance = bytes[offset + 5],
                Personality = bytes[offset + 6],
                Luck = bytes[offset + 7],
            };
        }

        static ActorAttributeDef ReadCreatureAttributes(byte[] bytes, int offset)
        {
            return new ActorAttributeDef
            {
                Strength = ReadInt32(bytes, offset),
                Intelligence = ReadInt32(bytes, offset + 4),
                Willpower = ReadInt32(bytes, offset + 8),
                Agility = ReadInt32(bytes, offset + 12),
                Speed = ReadInt32(bytes, offset + 16),
                Endurance = ReadInt32(bytes, offset + 20),
                Personality = ReadInt32(bytes, offset + 24),
                Luck = ReadInt32(bytes, offset + 28),
            };
        }

        static ActorSkillDef ReadNpcSkills(byte[] bytes, int offset)
        {
            return new ActorSkillDef
            {
                Block = bytes[offset],
                Armorer = bytes[offset + 1],
                MediumArmor = bytes[offset + 2],
                HeavyArmor = bytes[offset + 3],
                BluntWeapon = bytes[offset + 4],
                LongBlade = bytes[offset + 5],
                Axe = bytes[offset + 6],
                Spear = bytes[offset + 7],
                Athletics = bytes[offset + 8],
                Enchant = bytes[offset + 9],
                Destruction = bytes[offset + 10],
                Alteration = bytes[offset + 11],
                Illusion = bytes[offset + 12],
                Conjuration = bytes[offset + 13],
                Mysticism = bytes[offset + 14],
                Restoration = bytes[offset + 15],
                Alchemy = bytes[offset + 16],
                Unarmored = bytes[offset + 17],
                Security = bytes[offset + 18],
                Sneak = bytes[offset + 19],
                Acrobatics = bytes[offset + 20],
                LightArmor = bytes[offset + 21],
                ShortBlade = bytes[offset + 22],
                Marksman = bytes[offset + 23],
                Mercantile = bytes[offset + 24],
                Speechcraft = bytes[offset + 25],
                HandToHand = bytes[offset + 26],
            };
        }

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
