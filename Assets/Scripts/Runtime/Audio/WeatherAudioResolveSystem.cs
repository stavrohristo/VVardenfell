using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Audio
{
    [UpdateInGroup(typeof(MorrowindAudioSimulationSystemGroup), OrderLast = true)]
    public partial class WeatherAudioResolveSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<ActiveSkyWeatherState>();
            RequireForUpdate<MorrowindWeatherState>();
            RequireForUpdate<RegionAmbientState>();
            RequireForUpdate<WeatherAudioState>();
            RequireForUpdate<WeatherRainAudioState>();
        }

        protected override void OnUpdate()
        {
            var contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
                return;

            var sky = SystemAPI.GetSingleton<ActiveSkyWeatherState>();
            var weather = SystemAPI.GetSingleton<MorrowindWeatherState>();
            ref var audio = ref SystemAPI.GetSingletonRW<WeatherAudioState>().ValueRW;
            ref var rainAudio = ref SystemAPI.GetSingletonRW<WeatherRainAudioState>().ValueRW;
            ref var region = ref SystemAPI.GetSingletonRW<RegionAmbientState>().ValueRW;

            bool interior = sky.IsInterior != 0;
            WeatherDefinitionDef current = ResolveWeather(contentDb, weather.CurrentWeather);
            WeatherDefinitionDef next = weather.NextWeather >= 0 ? ResolveWeather(contentDb, weather.NextWeather) : current;
            float blend = weather.NextWeather >= 0 ? Unity.Mathematics.math.saturate(weather.Transition) : 0f;
            WeatherDefinitionDef dominant = blend < 0.5f ? current : next;

            audio.ResolvedLoopSound = interior ? default : ResolveAmbientLoopSound(contentDb, current);
            audio.ResolvedNextLoopSound = !interior && weather.NextWeather >= 0 ? ResolveAmbientLoopSound(contentDb, next) : default;
            audio.CurrentLoopVolume = interior ? 0f : 1f - blend;
            audio.NextLoopVolume = interior ? 0f : blend;

            rainAudio.ResolvedLoopSound = interior ? default : ResolveRainLoopSound(contentDb, current);
            rainAudio.ResolvedNextLoopSound = !interior && weather.NextWeather >= 0 ? ResolveRainLoopSound(contentDb, next) : default;
            rainAudio.CurrentLoopVolume = interior ? 0f : 1f - blend;
            rainAudio.NextLoopVolume = interior ? 0f : blend;

            if (!interior && sky.ThunderSequence != 0 && sky.ThunderSequence != audio.LastThunderSequence)
            {
                string soundId = ResolveThunderSoundId(dominant, sky.ThunderSoundIndex);
                if (!string.IsNullOrWhiteSpace(soundId) && contentDb.TryGetSoundHandle(soundId, out var thunderSound) && thunderSound.IsValid)
                {
                    region.PendingEventSound = thunderSound;
                    region.EventSequence += 1u;
                }

                audio.LastThunderSequence = sky.ThunderSequence;
            }
        }

        static WeatherDefinitionDef ResolveWeather(RuntimeContentDatabase contentDb, int index)
        {
            var defs = contentDb?.Data?.WeatherDefinitions;
            if (defs != null && (uint)index < (uint)defs.Length)
                return defs[index];
            return MorrowindDayCycleUtility.CreateFallbackClearWeather();
        }

        static SoundDefHandle ResolveAmbientLoopSound(RuntimeContentDatabase contentDb, in WeatherDefinitionDef weather)
        {
            return ResolveLoopSound(contentDb, weather.AmbientLoopSoundId);
        }

        static SoundDefHandle ResolveRainLoopSound(RuntimeContentDatabase contentDb, in WeatherDefinitionDef weather)
        {
            return ResolveLoopSound(contentDb, weather.RainLoopSoundId);
        }

        static SoundDefHandle ResolveLoopSound(RuntimeContentDatabase contentDb, string soundId)
        {
            if (!string.IsNullOrWhiteSpace(soundId) && contentDb.TryGetSoundHandle(soundId, out var handle))
                return handle;
            return default;
        }

        static string ResolveThunderSoundId(in WeatherDefinitionDef weather, int index)
        {
            return (index & 3) switch
            {
                0 => weather.ThunderSoundId0,
                1 => weather.ThunderSoundId1,
                2 => weather.ThunderSoundId2,
                _ => weather.ThunderSoundId3,
            };
        }
    }
}
