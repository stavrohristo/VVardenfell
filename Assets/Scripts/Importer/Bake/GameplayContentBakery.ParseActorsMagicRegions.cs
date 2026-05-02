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
                FirstSelectRuleIndex = -1,
            };
            var selectRules = new List<DialogueConditionDef>();
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
                        if (TryReadDialogueCondition(esm, info.Id, out var condition))
                            selectRules.Add(condition);
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
                    dialogue.SelectRules.RemoveAt(existingIndex);
                    dialogue.InfoIndexById.Remove(info.Id);
                    RebuildInfoIndex(dialogue);
                }
                return;
            }

            info.SelectRuleCount = selectRules.Count;
            if (dialogue.InfoIndexById.TryGetValue(info.Id, out int index))
            {
                dialogue.Infos[index] = info;
                dialogue.SelectRules[index] = selectRules;
            }
            else
            {
                dialogue.InfoIndexById[info.Id] = dialogue.Infos.Count;
                dialogue.Infos.Add(info);
                dialogue.SelectRules.Add(selectRules);
            }
        }

        static bool TryReadDialogueCondition(EsmReader esm, string infoId, out DialogueConditionDef condition)
        {
            condition = default;
            string rule = esm.ReadSubrecordString();
            if (rule.Length < 5)
                throw new InvalidDataException($"Invalid SCVR rule size {rule.Length} in INFO '{infoId}'.");

            if (rule[4] < '0' || rule[4] > '5')
                throw new InvalidDataException($"Invalid SCVR comparison operator '{(int)rule[4]}' in INFO '{infoId}'.");

            if (!TryResolveDialogueConditionFunction(rule, infoId, out byte function))
                return false;

            if (!esm.ReadSubrecordHeader(out var valueSub))
                throw new InvalidDataException($"SCVR rule in INFO '{infoId}' is missing INTV/FLTV value.");

            int intValue;
            float floatValue;
            byte valueKind;
            if (valueSub.Tag == EsmFourCC.Make('I', 'N', 'T', 'V'))
            {
                intValue = esm.ReadInt32();
                floatValue = intValue;
                valueKind = (byte)MorrowindScriptValueKind.Integer;
            }
            else if (valueSub.Tag == EsmFourCC.Make('F', 'L', 'T', 'V'))
            {
                floatValue = esm.ReadFloat();
                intValue = (int)floatValue;
                valueKind = (byte)MorrowindScriptValueKind.Float;
            }
            else
            {
                throw new InvalidDataException($"SCVR rule in INFO '{infoId}' has invalid value subrecord '{valueSub.TagString}'.");
            }

            if (esm.SubrecordBytesLeft > 0)
                esm.SkipSubrecord();

            byte index = 0;
            if (rule[0] >= '0' && rule[0] <= '9')
                index = (byte)(rule[0] - '0');

            condition = new DialogueConditionDef
            {
                Variable = rule.Length > 5 ? rule.Substring(5) : string.Empty,
                IntValue = intValue,
                FloatValue = floatValue,
                ValueKind = valueKind,
                Index = index,
                Function = function,
                Comparison = (byte)rule[4],
            };
            return true;
        }

        static bool TryResolveDialogueConditionFunction(string rule, string infoId, out byte function)
        {
            function = (byte)DialogueConditionFunction.None;
            if (rule[1] == '1')
            {
                if (!int.TryParse(rule.Substring(2, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int raw)
                    || raw < (int)DialogueConditionFunction.FacReactionLowest
                    || raw > (int)DialogueConditionFunction.PcWerewolfKills)
                {
                    throw new InvalidDataException($"Invalid SCVR function index in INFO '{infoId}'.");
                }

                function = (byte)raw;
                return true;
            }

            switch (rule[1])
            {
                case '2':
                    function = (byte)DialogueConditionFunction.Global;
                    return true;
                case '3':
                    function = (byte)DialogueConditionFunction.Local;
                    return true;
                case '4':
                    function = (byte)DialogueConditionFunction.Journal;
                    return true;
                case '5':
                    function = (byte)DialogueConditionFunction.Item;
                    return true;
                case '6':
                    function = (byte)DialogueConditionFunction.Dead;
                    return true;
                case '7':
                    function = (byte)DialogueConditionFunction.NotId;
                    return true;
                case '8':
                    function = (byte)DialogueConditionFunction.NotFaction;
                    return true;
                case '9':
                    function = (byte)DialogueConditionFunction.NotClass;
                    return true;
                case 'A':
                    function = (byte)DialogueConditionFunction.NotRace;
                    return true;
                case 'B':
                    function = (byte)DialogueConditionFunction.NotCell;
                    return true;
                case 'C':
                    function = (byte)DialogueConditionFunction.NotLocal;
                    return true;
                default:
                    throw new InvalidDataException($"Invalid SCVR function '{rule[1]}' in INFO '{infoId}'.");
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


        static GameplayContentData BuildContentData(State state, string installPath, string[] recordSourcePaths)
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
                WeatherSettings = BuildWeatherSettings(installPath),
                WeatherDefinitions = BuildWeatherDefinitions(installPath),
                SkyWeatherVisualSettings = BuildSkyWeatherVisualSettings(installPath),
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
            BuildDialogueArrays(state.Dialogues, out data.Dialogues, out data.DialogueInfos, out data.DialogueConditions);
            BuildContainerContentArrays(data.Containers, state.ContainerItems, out data.ContainerContentRanges, out data.ContainerItems);
            BuildItemEquipmentArrays(data.Items, state.ItemEquipment, out data.ItemEquipment, out data.ItemEquipmentBodyParts);
            BuildItemLeveledListArrays(state.ItemLeveledLists, out data.ItemLeveledLists, out data.ItemLeveledListEntries);
            BuildItemLeveledListArrays(state.CreatureLeveledLists, out data.CreatureLeveledLists, out data.CreatureLeveledListEntries);
            BuildSpellArrays(state.Spells, s_SpellEffects, out data.Spells, ref data.MagicEffectInstances);
            BuildEnchantmentArrays(state.Enchantments, s_EnchantmentEffects, out data.Enchantments, ref data.MagicEffectInstances);
            BuildRegionArrays(state.Regions, out data.Regions, out data.RegionSoundRefs);
            BuildExplicitRefTargetMap(recordSourcePaths, out var explicitRefTargets, out var ambiguousExplicitRefTargets);
            data.ExplicitRefTargets = BuildExplicitRefTargetArray(explicitRefTargets);
            MorrowindScriptCompiler.Build(
                data.Scripts,
                data.Sounds,
                data.Actors,
                data.Activators,
                data.Doors,
                data.Containers,
                data.Items,
                data.Lights,
                data.Statics,
                data.CreatureLeveledLists,
                data.ItemLeveledLists,
                data.Spells,
                data.Factions,
                data.Globals,
                data.Dialogues,
                data.DialogueInfos,
                explicitRefTargets,
                ambiguousExplicitRefTargets,
                out data.MorrowindScriptPrograms,
                out data.MorrowindScriptInstructions,
                out data.MorrowindScriptLocals,
                out data.MorrowindScriptMessages);
            MorrowindDialogueResultScriptCoverage.Log(data);

            s_SpellEffects.Clear();
            s_EnchantmentEffects.Clear();
            return data;
        }

        static void BuildExplicitRefTargetMap(
            string[] recordSourcePaths,
            out Dictionary<string, uint> explicitRefTargets,
            out HashSet<string> ambiguousExplicitRefTargets)
        {
            explicitRefTargets = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            ambiguousExplicitRefTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (recordSourcePaths == null)
                return;

            for (int i = 0; i < recordSourcePaths.Length; i++)
            {
                using var esm = new EsmReader(recordSourcePaths[i]);
                var cells = CellIndex.Enumerate(esm).ToArray();
                for (int cellIndex = 0; cellIndex < cells.Length; cellIndex++)
                {
                    var refs = CellReader.ReadReferences(esm, cells[cellIndex]);
                    for (int refIndex = 0; refIndex < refs.Count; refIndex++)
                    {
                        var reference = refs[refIndex];
                        if (reference.Deleted
                            || reference.FormId == 0u
                            || string.IsNullOrWhiteSpace(reference.BaseId))
                        {
                            continue;
                        }

                        string normalizedBaseId = ContentId.NormalizeId(reference.BaseId);
                        if (ambiguousExplicitRefTargets.Contains(normalizedBaseId))
                            continue;

                        if (!explicitRefTargets.TryGetValue(normalizedBaseId, out uint existingPlacedRefId))
                        {
                            explicitRefTargets.Add(normalizedBaseId, reference.FormId);
                            continue;
                        }

                        if (existingPlacedRefId == reference.FormId)
                            continue;

                        explicitRefTargets.Remove(normalizedBaseId);
                        ambiguousExplicitRefTargets.Add(normalizedBaseId);
                    }
                }
            }
        }

        static ExplicitRefTargetDef[] BuildExplicitRefTargetArray(Dictionary<string, uint> explicitRefTargets)
        {
            if (explicitRefTargets == null || explicitRefTargets.Count == 0)
                return Array.Empty<ExplicitRefTargetDef>();

            var entries = explicitRefTargets
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value != 0u)
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var result = new ExplicitRefTargetDef[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                result[i] = new ExplicitRefTargetDef
                {
                    Id = entries[i].Key,
                    PlacedRefId = entries[i].Value,
                };
            }

            return result;
        }


        }
    }
