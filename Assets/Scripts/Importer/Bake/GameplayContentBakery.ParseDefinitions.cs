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
                    case var tag when tag == SctxTag:
                        def.Text = esm.ReadSubrecordString();
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


        static void IndexInteriorCells(EsmReader esm, HashSet<string> target)
        {
            foreach (var cell in CellIndex.Enumerate(esm))
            {
                if (!cell.IsInterior || string.IsNullOrWhiteSpace(cell.Name))
                    continue;

                target.Add(ContentId.NormalizeId(cell.Name));
            }
        }


        static void ParsePathGridRecord(EsmReader esm, Dictionary<string, PathGridAccumulator> target, HashSet<string> interiorCellIds)
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

            bool isInterior = IsInteriorPathGrid(def, interiorCellIds);
            def.Id = BuildPathGridId(def.CellId, def.GridX, def.GridY, isInterior);
            def.IsExterior = isInterior ? (byte)0 : (byte)1;

            if (deleted)
            {
                target.Remove(def.Id);
                return;
            }

            ApplyPathGridCoordinateSpace(points, def.GridX, def.GridY, isInterior);
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
                    Autogenerated = bytes[offset + 12],
                    SourceConnectionCount = bytes[offset + 13],
                    FirstConnectionIndex = -1,
                });
            }
        }


        static void ApplyPathGridCoordinateSpace(List<PathGridPointDef> points, int gridX, int gridY, bool isInterior)
        {
            if (points.Count == 0)
                return;

            int originX = isInterior ? 0 : gridX * LandRecordSize.CellUnitsMw;
            int originY = isInterior ? 0 : gridY * LandRecordSize.CellUnitsMw;
            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                point.SourceX += originX;
                point.SourceY += originY;
                SetPathGridPointUnityPosition(ref point);
                points[i] = point;
            }
        }


        static void SetPathGridPointUnityPosition(ref PathGridPointDef point)
        {
            point.UnityX = point.SourceX * WorldScale.MwUnitsToMeters;
            point.UnityY = point.SourceZ * WorldScale.MwUnitsToMeters;
            point.UnityZ = point.SourceY * WorldScale.MwUnitsToMeters;
        }


        static void ReadPathGridConnections(byte[] bytes, List<int> rawConnectionTargets)
        {
            if (bytes == null || bytes.Length < 4)
                return;

            int count = bytes.Length / 4;
            for (int i = 0; i < count; i++)
                rawConnectionTargets.Add(ReadInt32(bytes, i * 4));
        }


        static bool IsInteriorPathGrid(in PathGridDef def, HashSet<string> interiorCellIds)
        {
            if (def.GridX != 0 || def.GridY != 0 || string.IsNullOrWhiteSpace(def.CellId))
                return false;

            return interiorCellIds != null && interiorCellIds.Contains(ContentId.NormalizeId(def.CellId));
        }


        static string BuildPathGridId(string cellId, int gridX, int gridY, bool isInterior)
        {
            if (isInterior)
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


        }
    }
