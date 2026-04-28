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
        }

        protected override void OnUpdate()
        {
            var contentDb = RuntimeContentDatabase.Active;
            if (contentDb == null)
                return;

            var sky = SystemAPI.GetSingleton<ActiveSkyWeatherState>();
            var weather = SystemAPI.GetSingleton<MorrowindWeatherState>();
            ref var audio = ref SystemAPI.GetSingletonRW<WeatherAudioState>().ValueRW;
            ref var region = ref SystemAPI.GetSingletonRW<RegionAmbientState>().ValueRW;

            WeatherDefinitionDef dominant = ResolveWeather(contentDb, sky.WeatherTransition < 0.5f ? weather.CurrentWeather : weather.NextWeather);
            audio.ResolvedLoopSound = ResolveLoopSound(contentDb, dominant);

            if (sky.ThunderSequence != 0 && sky.ThunderSequence != audio.LastThunderSequence)
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

        static SoundDefHandle ResolveLoopSound(RuntimeContentDatabase contentDb, in WeatherDefinitionDef weather)
        {
            string id = !string.IsNullOrWhiteSpace(weather.RainLoopSoundId)
                ? weather.RainLoopSoundId
                : weather.AmbientLoopSoundId;
            if (!string.IsNullOrWhiteSpace(id) && contentDb.TryGetSoundHandle(id, out var handle))
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
