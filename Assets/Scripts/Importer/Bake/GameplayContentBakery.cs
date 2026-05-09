using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Core.Config;
using VVardenfell.Importer.Bsa;
using VVardenfell.Importer.Esm;

namespace VVardenfell.Importer.Bake
{
    internal static partial class GameplayContentBakery
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
        static readonly uint BkdtTag = EsmFourCC.Make('B', 'K', 'D', 'T');
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
        static readonly uint SchdTag = EsmFourCC.Make('S', 'C', 'H', 'D');
        static readonly uint SctxTag = EsmFourCC.Make('S', 'C', 'T', 'X');
        static readonly uint ScvrTag = EsmFourCC.Make('S', 'C', 'V', 'R');
        static readonly uint ScriTag = EsmFourCC.Make('S', 'C', 'R', 'I');
        static readonly uint SkdtTag = EsmFourCC.Make('S', 'K', 'D', 'T');
        static readonly uint SnamTag = EsmFourCC.Make('S', 'N', 'A', 'M');
        static readonly uint SpdtTag = EsmFourCC.Make('S', 'P', 'D', 'T');
        static readonly uint StrvTag = EsmFourCC.Make('S', 'T', 'R', 'V');
        static readonly uint TextTag = EsmFourCC.Make('T', 'E', 'X', 'T');
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


        const int ExteriorBorderCandidateDistanceMw = 512;
        const int ExteriorBorderVerticalDistanceMw = 384;


        sealed class DialogueAccumulator
        {
            public DialogueDef Def;
            public readonly List<DialogueInfoDef> Infos = new();
            public readonly List<List<DialogueConditionDef>> SelectRules = new();
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
            public readonly HashSet<string> InteriorCellIds = new(StringComparer.OrdinalIgnoreCase);
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


        public static IEnumerator Bake(MorrowindConfig config, BakeProgress progress, bool markDone = true)
        {

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
                using (var esm = new EsmReader(recordSourcePaths[i]))
                {
                    IndexInteriorCells(esm, state.InteriorCellIds);
                    currentDialogueId = ParseSourceIntoState(esm, state, currentDialogueId);
                }

                progress.Current = i + 1;
                yield return null;
            }

            progress.Label = "Building deterministic content arrays";
            progress.Current = recordSourcePaths.Length + 1;
            ApplyOpenMwFallbackGameSettings(state.GameSettings);
            GameplayContentData data = BuildContentData(state, config.InstallPath, recordSourcePaths);
            yield return null;

            progress.Label = "Writing gameplay content cache";
            progress.Current = recordSourcePaths.Length + 2;
            GameplayContentFile.Write(CachePaths.GameplayContent, data);
            RuntimeContentBlobFile.Write(CachePaths.RuntimeContentBlob, data);
            yield return null;

            progress.Label = "Writing gameplay validation report";
            progress.Current = recordSourcePaths.Length + 3;
            var manifest = GameplayContentManifest.FromSources(dependencySourcePaths);
            PopulateManifestCounts(manifest, data);
            manifest.Write(CachePaths.GameplayContentManifest);

            {
            }
            yield return null;

            progress.Label = "Gameplay content ready";
            progress.Current = recordSourcePaths.Length + 4;
            if (markDone)
                progress.Done = true;
        }


        static void ApplyOpenMwFallbackGameSettings(Dictionary<string, GenericRecordDef> gameSettings)
        {
            if (gameSettings == null)
                throw new InvalidOperationException("Cannot apply OpenMW fallback game settings without a GMST table.");

            AddFallbackStringGameSetting(gameSettings, "Blood_Model_0", "BloodSplat.nif");
            AddFallbackStringGameSetting(gameSettings, "Blood_Model_1", "BloodSplat2.nif");
            AddFallbackStringGameSetting(gameSettings, "Blood_Model_2", "BloodSplat3.nif");
            AddFallbackStringGameSetting(gameSettings, "Blood_Texture_0", "Tx_Blood.dds");
            AddFallbackStringGameSetting(gameSettings, "Blood_Texture_1", "Tx_Blood_White.dds");
            AddFallbackStringGameSetting(gameSettings, "Blood_Texture_2", "Tx_Blood_Gold.dds");
            AddOpenMwClassGenerationFallbackGameSettings(gameSettings);
        }

        static void AddOpenMwClassGenerationFallbackGameSettings(Dictionary<string, GenericRecordDef> gameSettings)
        {
            AddFallbackStringGameSetting(gameSettings, "Question_1_Question", "Before you lies a creature you have never seen before. Its hind leg is caught in a hunter's trap, and you can tell that the leg is broken. What will you do?");
            AddFallbackStringGameSetting(gameSettings, "Question_1_AnswerOne", "Pull out you knife for a short and merciful kill.");
            AddFallbackStringGameSetting(gameSettings, "Question_1_AnswerTwo", "Reach into your herbal pouch to find something to ease its suffering before putting it to sleep.");
            AddFallbackStringGameSetting(gameSettings, "Question_1_AnswerThree", "Leave the leg alone, but take the time to observe and learn from this odd creature.");
            AddFallbackStringGameSetting(gameSettings, "Question_1_Sound", "Voice\\CharGen\\QA1.mp3");
            AddFallbackStringGameSetting(gameSettings, "Question_2_Question", "Your mother suggests you to help with work around your family household. Do you decide to...");
            AddFallbackStringGameSetting(gameSettings, "Question_2_AnswerOne", "Help your father with fence post replacement?");
            AddFallbackStringGameSetting(gameSettings, "Question_2_AnswerTwo", "Collect a few herbs in the forest for tonight's supper?");
            AddFallbackStringGameSetting(gameSettings, "Question_2_AnswerThree", "Help the fish to find their way into the kitchen?");
            AddFallbackStringGameSetting(gameSettings, "Question_2_Sound", "Voice\\CharGen\\QA2.mp3");
            AddFallbackStringGameSetting(gameSettings, "Question_3_Question", "Your brother teases you mercilessly in front of everyone with embarrassing details you would rather have forgotten.");
            AddFallbackStringGameSetting(gameSettings, "Question_3_AnswerOne", "Give him a black-eye and dare him to keep it up.");
            AddFallbackStringGameSetting(gameSettings, "Question_3_AnswerTwo", "Beat him to the punch and play it up -- if you can control the negative, then he can't embarrass you.");
            AddFallbackStringGameSetting(gameSettings, "Question_3_AnswerThree", "Wait until he sleeps, put his hand in a bowl of water and call everyone around the next day to see the results.");
            AddFallbackStringGameSetting(gameSettings, "Question_3_Sound", "Voice\\CharGen\\QA3.mp3");
            AddFallbackStringGameSetting(gameSettings, "Question_4_Question", "Rumor has it that the King's security console has a new tool at their disposal for sniffing out the truth, which could be used for reading minds.");
            AddFallbackStringGameSetting(gameSettings, "Question_4_AnswerOne", "You recoil at the thought of someone being able to read your thoughts. It's not that you think something that you'll ever act on it.");
            AddFallbackStringGameSetting(gameSettings, "Question_4_AnswerTwo", "For those who have done nothing, there is nothing to fear. This is just yet another utility for catching thieves, murderers and plots against the crown.");
            AddFallbackStringGameSetting(gameSettings, "Question_4_AnswerThree", "While you loath the idea of someone reading your mind, you accept that it does have its uses if tempered by law and common sense. You wouldn't believe the mind of a madman.");
            AddFallbackStringGameSetting(gameSettings, "Question_4_Sound", "Voice\\CharGen\\QA4.mp3");
            AddFallbackStringGameSetting(gameSettings, "Question_5_Question", "You are returning home from the market after acquiring supplies, and notice that one of the merchants had given you too much back in change.");
            AddFallbackStringGameSetting(gameSettings, "Question_5_AnswerOne", "How dreadful, what if it was you? You head back to the merchant.");
            AddFallbackStringGameSetting(gameSettings, "Question_5_AnswerTwo", "Happy days indeed. Put that extra money towards the needs of your family?");
            AddFallbackStringGameSetting(gameSettings, "Question_5_AnswerThree", "You win some and you lose some. In this case you have won and they have lost, and the oversight is the merchant's problem, not yours, right?");
            AddFallbackStringGameSetting(gameSettings, "Question_5_Sound", "Voice\\CharGen\\QA5.mp3");
            AddFallbackStringGameSetting(gameSettings, "Question_6_Question", "While at market, a noble yells out to the city watch about a theft. As you turn around a man bumps into you and drops a sack, startled he darts into the crowd.");
            AddFallbackStringGameSetting(gameSettings, "Question_6_AnswerOne", "You pick up the sack and head over to the noble to return his property?");
            AddFallbackStringGameSetting(gameSettings, "Question_6_AnswerTwo", "Just walk away, the last thing you need is someone thinking you had anything to do with it?");
            AddFallbackStringGameSetting(gameSettings, "Question_6_AnswerThree", "Finders, keepers... you whistle as you walk away with the bag hidden?");
            AddFallbackStringGameSetting(gameSettings, "Question_6_Sound", "Voice\\CharGen\\QA6.mp3");
            AddFallbackStringGameSetting(gameSettings, "Question_7_Question", "If it's one thing you hate, it's cleaning out the stalls. It has to be done before sunset before the critters return. On your way there an old friend greets you and offers to help if you promise to help them in the future.");
            AddFallbackStringGameSetting(gameSettings, "Question_7_AnswerOne", "You thank him for the offer but would rather get it over with and not make promises you can't keep?");
            AddFallbackStringGameSetting(gameSettings, "Question_7_AnswerTwo", "You reason that two pairs of hands are better than one regardless of the task?");
            AddFallbackStringGameSetting(gameSettings, "Question_7_AnswerThree", "You say that it sounds great and anything is better than cleaning the stalls?");
            AddFallbackStringGameSetting(gameSettings, "Question_7_Sound", "Voice\\CharGen\\QA7.mp3");
            AddFallbackStringGameSetting(gameSettings, "Question_8_Question", "You just climbed down the ladder after working on the roof. Your mother thanks you for the hard work but just at the moment you notice that a hammer is about to fall down on her head.");
            AddFallbackStringGameSetting(gameSettings, "Question_8_AnswerOne", "You lunge at your mother, pushing her out the way while the hammer falls on top of you?");
            AddFallbackStringGameSetting(gameSettings, "Question_8_AnswerTwo", "Try to use the ladder to intercept the hammer before it lands on her?");
            AddFallbackStringGameSetting(gameSettings, "Question_8_AnswerThree", "Warn her to take a step back?");
            AddFallbackStringGameSetting(gameSettings, "Question_8_Sound", "Voice\\CharGen\\QA8.mp3");
            AddFallbackStringGameSetting(gameSettings, "Question_9_Question", "It's the end of the week, and you have just got your wages for your hard work. You decide to take the quick way back home, darting into a alley only to be confronted by ruffians who demand that you empty your pockets.");
            AddFallbackStringGameSetting(gameSettings, "Question_9_AnswerOne", "You tell them to go pack sand, planting your feet and raising your fists?");
            AddFallbackStringGameSetting(gameSettings, "Question_9_AnswerTwo", "Acting dejected, you turn your wages over. You know that you can count on your friends to help you track these brigands down and recover what's yours?");
            AddFallbackStringGameSetting(gameSettings, "Question_9_AnswerThree", "Tossing the sack into the air, you charge the leader who's attention is squarely focused on the coins in flight?");
            AddFallbackStringGameSetting(gameSettings, "Question_9_Sound", "Voice\\CharGen\\QA9.mp3");
            AddFallbackStringGameSetting(gameSettings, "Question_10_Question", "Upon coming to town, you see a clown running at you with an angry mob following after him.");
            AddFallbackStringGameSetting(gameSettings, "Question_10_AnswerOne", "Stand your ground, you hate clowns?");
            AddFallbackStringGameSetting(gameSettings, "Question_10_AnswerTwo", "Move out of the way, letting fate decide the clown's fate?");
            AddFallbackStringGameSetting(gameSettings, "Question_10_AnswerThree", "Come to the clown's aid, clowns are people too?");
            AddFallbackStringGameSetting(gameSettings, "Question_10_Sound", "Voice\\CharGen\\QA10.mp3");
        }


        static void AddFallbackStringGameSetting(
            Dictionary<string, GenericRecordDef> gameSettings,
            string id,
            string value)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("OpenMW fallback GMST id is empty.");
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"OpenMW fallback GMST '{id}' has no string value.");
            if (gameSettings.TryGetValue(id, out var existing))
            {
                if (existing.ValueKind == GenericRecordValueKind.None && !string.IsNullOrWhiteSpace(existing.Text))
                {
                    existing.ValueKind = GenericRecordValueKind.String;
                    gameSettings[id] = existing;
                }

                return;
            }

            gameSettings[id] = new GenericRecordDef
            {
                ContentId = ContentId.FromTagAndId(GmstTag, id),
                RecordTag = GmstTag,
                Id = id,
                Text = value,
                ValueKind = GenericRecordValueKind.String,
            };
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
                        ParsePathGridRecord(esm, state.PathGrids, state.InteriorCellIds);
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
                    case var tag when tag == BkdtTag:
                    {
                        if (recordTag != BookTag || sub.Size < 20)
                        {
                            esm.SkipSubrecord();
                            break;
                        }

                        byte[] bytes = esm.ReadSubrecordBytes();
                        def.Float0 = ReadSingle(bytes, 0);
                        def.Int0 = ReadInt32(bytes, 4);
                        def.Int1 = ReadInt32(bytes, 8);
                        def.Int2 = ReadInt32(bytes, 12);
                        def.Int3 = ReadInt32(bytes, 16);
                        break;
                    }
                    case var tag when tag == TextTag:
                        if (recordTag == BookTag)
                            def.Text = esm.ReadSubrecordString();
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
                if (bytes.Length >= 16)
                    def.WeaponSpeed = ReadSingle(bytes, 12);
                if (bytes.Length >= 20)
                    def.WeaponReach = ReadSingle(bytes, 16);
                if (bytes.Length >= 22)
                    def.EnchantCapacity = ReadInt16(bytes, 20);
                if (bytes.Length >= 28)
                {
                    def.ChopMin = bytes[22];
                    def.ChopMax = bytes[23];
                    def.SlashMin = bytes[24];
                    def.SlashMax = bytes[25];
                    def.ThrustMin = bytes[26];
                    def.ThrustMax = bytes[27];
                    def.DamageMax = Math.Max(def.ChopMax, Math.Max(def.SlashMax, def.ThrustMax));
                    def.DamageMin = Math.Min(def.ChopMin, Math.Min(def.SlashMin, def.ThrustMin));
                }
                if (bytes.Length >= 32)
                    def.WeaponFlags = ReadUInt32(bytes, 28);
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
            if (string.IsNullOrWhiteSpace(itemId) || count == 0)
                return;

            items.Add(new ContainerItemDef
            {
                ItemId = itemId,
                Count = count,
            });
        }


    }
}
