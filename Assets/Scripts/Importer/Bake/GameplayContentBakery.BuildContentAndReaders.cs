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
