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


        static WeatherSettingsDef BuildWeatherSettings(string installPath)
        {
            var config = LoadWeatherConfig(installPath);
            return new WeatherSettingsDef
            {
                SunriseTime = ReadWeatherFloat(config, "Weather_Sunrise_Time", 6f),
                SunsetTime = ReadWeatherFloat(config, "Weather_Sunset_Time", 18f),
                SunriseDuration = ReadWeatherFloat(config, "Weather_Sunrise_Duration", 2f),
                SunsetDuration = ReadWeatherFloat(config, "Weather_Sunset_Duration", 2f),
                HoursBetweenWeatherChanges = ReadWeatherFloat(config, "Weather_Hours_Between_Weather_Changes", 20f),
                PrecipGravity = ReadWeatherFloat(config, "Weather_Precip_Gravity", 575f),
                SunGlareFaderMax = ReadWeatherFloat(config, "Weather_Sun_Glare_Fader_Max", 0.5f),
                SunGlareFaderAngleMax = ReadWeatherFloat(config, "Weather_Sun_Glare_Fader_Angle_Max", 30f),
                SunGlareFaderColorRgba = ReadWeatherColor(config, "Weather_Sun_Glare_Fader_Color", PackRgba(222, 95, 39, 255)),
                SunPreSunriseTime = ReadWeatherFloat(config, "Weather_Sun_Pre-Sunrise_Time", 0f),
                SunPostSunriseTime = ReadWeatherFloat(config, "Weather_Sun_Post-Sunrise_Time", 0f),
                SunPreSunsetTime = ReadWeatherFloat(config, "Weather_Sun_Pre-Sunset_Time", 1f),
                SunPostSunsetTime = ReadWeatherFloat(config, "Weather_Sun_Post-Sunset_Time", 1.25f),
                AmbientPreSunriseTime = ReadWeatherFloat(config, "Weather_Ambient_Pre-Sunrise_Time", 0.5f),
                AmbientPostSunriseTime = ReadWeatherFloat(config, "Weather_Ambient_Post-Sunrise_Time", 2f),
                AmbientPreSunsetTime = ReadWeatherFloat(config, "Weather_Ambient_Pre-Sunset_Time", 1f),
                AmbientPostSunsetTime = ReadWeatherFloat(config, "Weather_Ambient_Post-Sunset_Time", 1.25f),
                FogPreSunriseTime = ReadWeatherFloat(config, "Weather_Fog_Pre-Sunrise_Time", 0.5f),
                FogPostSunriseTime = ReadWeatherFloat(config, "Weather_Fog_Post-Sunrise_Time", 1f),
                FogPreSunsetTime = ReadWeatherFloat(config, "Weather_Fog_Pre-Sunset_Time", 2f),
                FogPostSunsetTime = ReadWeatherFloat(config, "Weather_Fog_Post-Sunset_Time", 1f),
                SkyPreSunriseTime = ReadWeatherFloat(config, "Weather_Sky_Pre-Sunrise_Time", 0.5f),
                SkyPostSunriseTime = ReadWeatherFloat(config, "Weather_Sky_Post-Sunrise_Time", 0.5f),
                SkyPreSunsetTime = ReadWeatherFloat(config, "Weather_Sky_Pre-Sunset_Time", 1f),
                SkyPostSunsetTime = ReadWeatherFloat(config, "Weather_Sky_Post-Sunset_Time", 1f),
                StarsPostSunsetStart = ReadWeatherFloat(config, "Weather_Stars_Post-Sunset_Start", 1f),
                StarsPreSunriseFinish = ReadWeatherFloat(config, "Weather_Stars_Pre-Sunrise_Finish", 2f),
                StarsFadingDuration = ReadWeatherFloat(config, "Weather_Stars_Fading_Duration", 2f),
                MasserMoon = BuildMoonSettings(config, "Masser", 55f, 35f, 0.5f, 1f, 50f, 40f),
                SecundaMoon = BuildMoonSettings(config, "Secunda", 20f, 50f, 0.6f, 1.2f, 50f, 30f),
            };
        }

        static MoonSettingsDef BuildMoonSettings(
            WeatherConfigSource config,
            string name,
            float defaultSize,
            float defaultAxisOffset,
            float defaultSpeed,
            float defaultDailyIncrement,
            float defaultFadeStartAngle,
            float defaultFadeEndAngle)
        {
            return new MoonSettingsDef
            {
                Size = ReadWeatherFloat(config, $"Moons_{name}_Size", defaultSize),
                AxisOffset = ReadWeatherFloat(config, $"Moons_{name}_Axis_Offset", defaultAxisOffset),
                Speed = ReadWeatherFloat(config, $"Moons_{name}_Speed", defaultSpeed),
                DailyIncrement = ReadWeatherFloat(config, $"Moons_{name}_Daily_Increment", defaultDailyIncrement),
                FadeStartAngle = ReadWeatherFloat(config, $"Moons_{name}_Fade_Start_Angle", defaultFadeStartAngle),
                FadeEndAngle = ReadWeatherFloat(config, $"Moons_{name}_Fade_End_Angle", defaultFadeEndAngle),
                MoonShadowEarlyFadeAngle = ReadWeatherFloat(config, $"Moons_{name}_Moon_Shadow_Early_Fade_Angle", 0.5f),
                FadeInStart = ReadWeatherFloat(config, $"Moons_{name}_Fade_In_Start", 14f),
                FadeInFinish = ReadWeatherFloat(config, $"Moons_{name}_Fade_In_Finish", 15f),
                FadeOutStart = ReadWeatherFloat(config, $"Moons_{name}_Fade_Out_Start", 7f),
                FadeOutFinish = ReadWeatherFloat(config, $"Moons_{name}_Fade_Out_Finish", 10f),
            };
        }


        static WeatherDefinitionDef[] BuildWeatherDefinitions(string installPath)
        {
            var config = LoadWeatherConfig(installPath);
            var weather = new[]
            {
                BuildWeatherDefinition(config, WeatherKind.Clear, "Clear", stormWindSpeed: 0.7f, rainSpeed: 0f),
                BuildWeatherDefinition(config, WeatherKind.Cloudy, "Cloudy", stormWindSpeed: 0.7f, rainSpeed: 0f),
                BuildWeatherDefinition(config, WeatherKind.Foggy, "Foggy", stormWindSpeed: 0.7f, rainSpeed: 0f),
                BuildWeatherDefinition(config, WeatherKind.Overcast, "Overcast", stormWindSpeed: 0.7f, rainSpeed: 0f),
                BuildWeatherDefinition(config, WeatherKind.Rain, "Rain", stormWindSpeed: 0.7f, rainSpeed: 10f),
                BuildWeatherDefinition(config, WeatherKind.Thunderstorm, "Thunderstorm", stormWindSpeed: 0.7f, rainSpeed: 20f),
                BuildWeatherDefinition(config, WeatherKind.Ashstorm, "Ashstorm", stormWindSpeed: 0.7f, rainSpeed: 50f),
                BuildWeatherDefinition(config, WeatherKind.Blight, "Blight", stormWindSpeed: 0.7f, rainSpeed: 60f),
                BuildWeatherDefinition(config, WeatherKind.Snow, "Snow", stormWindSpeed: 0.7f, rainSpeed: 30f),
                BuildWeatherDefinition(config, WeatherKind.Blizzard, "Blizzard", stormWindSpeed: 0.7f, rainSpeed: 50f),
            };
            return weather;
        }


        static SkyWeatherVisualSettingsDef BuildSkyWeatherVisualSettings(string installPath)
        {
            var definitions = BuildWeatherDefinitions(installPath);
            return new SkyWeatherVisualSettingsDef
            {
                SunTexture = "textures/tx_sun_05.dds",
                SunGlareTexture = "textures/tx_sun_flash_grey_05.dds",
                StarTexture = "textures/tx_stars_01.dds",
                MasserShadowTexture = "textures/tx_mooncircle_full_m.dds",
                SecundaShadowTexture = "textures/tx_mooncircle_full_s.dds",
                RainDropTexture = "textures/tx_raindrop_01.dds",
                MasserPhaseTextures = BuildMoonPhaseTextures("masser"),
                SecundaPhaseTextures = BuildMoonPhaseTextures("secunda"),
                CloudTextures = definitions.Select(static def => def.CloudTexture ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                PrecipitationTextures = new[]
                {
                    "textures/tx_raindrop_01.dds",
                    "textures/tx_snow_01.dds",
                    "textures/tx_ashcloud_01.dds",
                    "textures/tx_blightcloud_01.dds",
                    "textures/tx_blizzard_01.dds",
                },
                PrecipitationEffectModels = new[]
                {
                    "meshes/raindrop.nif",
                    "meshes/snow.nif",
                    "meshes/ashcloud.nif",
                    "meshes/blightcloud.nif",
                    "meshes/blizzard.nif",
                },
            };
        }


        static string[] BuildMoonPhaseTextures(string moon)
        {
            return new[]
            {
                $"textures/tx_{moon}_full.dds",
                $"textures/tx_{moon}_three_wan.dds",
                $"textures/tx_{moon}_half_wan.dds",
                $"textures/tx_{moon}_one_wan.dds",
                $"textures/tx_{moon}_new.dds",
                $"textures/tx_{moon}_one_wax.dds",
                $"textures/tx_{moon}_half_wax.dds",
                $"textures/tx_{moon}_three_wax.dds",
            };
        }


        static WeatherDefinitionDef BuildWeatherDefinition(WeatherConfigSource config, WeatherKind kind, string name, float stormWindSpeed, float rainSpeed)
        {
            float windSpeed = ReadWeatherFloat(config, $"Weather_{name}_Wind_Speed", 0f);
            string rainLoop = NormalizeNone(ReadWeatherString(config, $"Weather_{name}_Rain_Loop_Sound_ID", kind == WeatherKind.Rain ? "Rain" : string.Empty));
            string ambientLoop = NormalizeNone(ReadWeatherString(config, $"Weather_{name}_Ambient_Loop_Sound_ID", string.Empty));
            return new WeatherDefinitionDef
            {
                Kind = kind,
                Id = name,
                CloudTexture = ReadWeatherString(config, $"Weather_{name}_Cloud_Texture", string.Empty),
                SkyColor = BuildWeatherColorSet(config, name, "Sky"),
                FogColor = BuildWeatherColorSet(config, name, "Fog"),
                AmbientColor = BuildWeatherColorSet(config, name, "Ambient"),
                SunColor = BuildWeatherColorSet(config, name, "Sun"),
                SunDiscSunsetColorRgba = ReadWeatherColor(config, $"Weather_{name}_Sun_Disc_Sunset_Color", PackRgba(128, 128, 128, 255)),
                LandFogDayDepth = ReadWeatherFloat(config, $"Weather_{name}_Land_Fog_Day_Depth", 1f),
                LandFogNightDepth = ReadWeatherFloat(config, $"Weather_{name}_Land_Fog_Night_Depth", 1f),
                WindSpeed = windSpeed,
                CloudSpeed = ReadWeatherFloat(config, $"Weather_{name}_Cloud_Speed", 1f),
                GlareView = ReadWeatherFloat(config, $"Weather_{name}_Glare_View", 0f),
                CloudsMaximumPercent = ReadWeatherFloat(config, $"Weather_{name}_Clouds_Maximum_Percent", 1f),
                TransitionDelta = ReadWeatherFloat(config, $"Weather_{name}_Transition_Delta", 0.015f),
                RainSpeed = rainSpeed,
                RainEntranceSpeed = ReadWeatherFloat(config, $"Weather_{name}_Rain_Entrance_Speed", 0f),
                RainMaxRaindrops = ReadWeatherInt(config, $"Weather_{name}_Max_Raindrops", 0),
                RainDiameter = ReadWeatherFloat(config, $"Weather_{name}_Rain_Diameter", 0f),
                RainThreshold = ReadWeatherFloat(config, $"Weather_{name}_Rain_Threshold", 1f),
                RainMinHeight = ReadWeatherFloat(config, $"Weather_{name}_Rain_Height_Min", 0f),
                RainMaxHeight = ReadWeatherFloat(config, $"Weather_{name}_Rain_Height_Max", 0f),
                UsingPrecip = ReadWeatherBool(config, $"Weather_{name}_Using_Precip", false) ? (byte)1 : (byte)0,
                IsStorm = windSpeed > stormWindSpeed ? (byte)1 : (byte)0,
                RainLoopSoundId = rainLoop,
                AmbientLoopSoundId = ambientLoop,
                ThunderFrequency = ReadWeatherFloat(config, $"Weather_{name}_Thunder_Frequency", 0f),
                ThunderThreshold = ReadWeatherFloat(config, $"Weather_{name}_Thunder_Threshold", 1f),
                FlashDecrement = ReadWeatherFloat(config, $"Weather_{name}_Flash_Decrement", 4f),
                ThunderSoundId0 = NormalizeNone(ReadWeatherString(config, $"Weather_{name}_Thunder_Sound_ID_0", string.Empty)),
                ThunderSoundId1 = NormalizeNone(ReadWeatherString(config, $"Weather_{name}_Thunder_Sound_ID_1", string.Empty)),
                ThunderSoundId2 = NormalizeNone(ReadWeatherString(config, $"Weather_{name}_Thunder_Sound_ID_2", string.Empty)),
                ThunderSoundId3 = NormalizeNone(ReadWeatherString(config, $"Weather_{name}_Thunder_Sound_ID_3", string.Empty)),
            };
        }


        static WeatherColorSetDef BuildWeatherColorSet(WeatherConfigSource config, string weatherName, string channel)
        {
            return new WeatherColorSetDef
            {
                SunriseRgba = ReadWeatherColor(config, $"Weather_{weatherName}_{channel}_Sunrise_Color", PackRgba(0, 0, 0, 255)),
                DayRgba = ReadWeatherColor(config, $"Weather_{weatherName}_{channel}_Day_Color", PackRgba(0, 0, 0, 255)),
                SunsetRgba = ReadWeatherColor(config, $"Weather_{weatherName}_{channel}_Sunset_Color", PackRgba(0, 0, 0, 255)),
                NightRgba = ReadWeatherColor(config, $"Weather_{weatherName}_{channel}_Night_Color", PackRgba(0, 0, 0, 255)),
            };
        }


        sealed class WeatherConfigSource
        {
            public MorrowindIniReader Ini;
            public readonly Dictionary<string, string> Fallbacks = new(StringComparer.OrdinalIgnoreCase);
        }


        static WeatherConfigSource LoadWeatherConfig(string installPath)
        {
            var source = new WeatherConfigSource();
            string iniPath = Path.Combine(installPath ?? string.Empty, "Morrowind.ini");
            if (File.Exists(iniPath))
                source.Ini = MorrowindIniReader.Read(iniPath);

            string openMwPath = Path.Combine(Directory.GetCurrentDirectory(), "OpenMW", "files", "openmw.cfg");
            if (File.Exists(openMwPath))
            {
                foreach (string rawLine in File.ReadLines(openMwPath))
                {
                    string line = rawLine?.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.OrdinalIgnoreCase))
                        continue;

                    const string prefix = "fallback=";
                    if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string payload = line.Substring(prefix.Length);
                    int comma = payload.IndexOf(',');
                    if (comma <= 0)
                        continue;

                    source.Fallbacks[payload.Substring(0, comma).Trim()] = payload.Substring(comma + 1).Trim();
                }
            }

            return source;
        }


        static string ReadWeatherString(WeatherConfigSource source, string key, string fallback)
        {
            if (source?.Ini != null)
            {
                string iniKey = key.StartsWith("Weather_", StringComparison.OrdinalIgnoreCase) ? key.Substring("Weather_".Length) : key;
                if (source.Ini.TryGetValue("Weather", iniKey.Replace('_', ' '), out string iniValue))
                    return iniValue;
                if (source.Ini.TryGetValue("Weather", iniKey, out iniValue))
                    return iniValue;
                if (source.Ini.TryGetValue("Weather", key, out iniValue))
                    return iniValue;
            }

            return source != null && source.Fallbacks.TryGetValue(key, out string value) ? value : fallback;
        }


        static float ReadWeatherFloat(WeatherConfigSource source, string key, float fallback)
        {
            string value = ReadWeatherString(source, key, string.Empty);
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ? parsed : fallback;
        }


        static int ReadWeatherInt(WeatherConfigSource source, string key, int fallback)
        {
            string value = ReadWeatherString(source, key, string.Empty);
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
        }


        static bool ReadWeatherBool(WeatherConfigSource source, string key, bool fallback)
        {
            string value = ReadWeatherString(source, key, string.Empty);
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                return parsed != 0;
            return bool.TryParse(value, out bool result) ? result : fallback;
        }


        static int ReadWeatherColor(WeatherConfigSource source, string key, int fallback)
        {
            string value = ReadWeatherString(source, key, string.Empty);
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            string[] parts = value.Split(',');
            if (parts.Length < 3)
                return fallback;

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int r)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int g)
                || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int b))
                return fallback;

            int a = 255;
            if (parts.Length > 3)
                int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out a);
            return PackRgba(r, g, b, a);
        }


        static int PackRgba(int r, int g, int b, int a)
        {
            uint packed = (uint)(byte)r | ((uint)(byte)g << 8) | ((uint)(byte)b << 16) | ((uint)(byte)a << 24);
            return unchecked((int)packed);
        }


        static string NormalizeNone(string value)
            => string.Equals(value, "None", StringComparison.OrdinalIgnoreCase) ? string.Empty : value ?? string.Empty;


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
            manifest.WeatherSettingsCount = 1;
            manifest.WeatherDefinitionCount = data.WeatherDefinitions?.Length ?? 0;
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
