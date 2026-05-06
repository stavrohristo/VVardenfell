using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Audio
{
    [UpdateInGroup(typeof(MorrowindAudioSimulationSystemGroup), OrderLast = true)]
    public partial struct WeatherAudioResolveSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<ActiveSkyWeatherState>();
            systemState.RequireForUpdate<MorrowindWeatherState>();
            systemState.RequireForUpdate<RegionAmbientState>();
            systemState.RequireForUpdate<WeatherAudioState>();
            systemState.RequireForUpdate<WeatherRainAudioState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;

            var sky = SystemAPI.GetSingleton<ActiveSkyWeatherState>();
            var weather = SystemAPI.GetSingleton<MorrowindWeatherState>();
            ref var audio = ref SystemAPI.GetSingletonRW<WeatherAudioState>().ValueRW;
            ref var rainAudio = ref SystemAPI.GetSingletonRW<WeatherRainAudioState>().ValueRW;
            ref var region = ref SystemAPI.GetSingletonRW<RegionAmbientState>().ValueRW;

            bool interior = sky.IsInterior != 0;
            WeatherDefinitionDef current = ResolveWeather(ref contentBlob, weather.CurrentWeather);
            WeatherDefinitionDef next = weather.NextWeather >= 0 ? ResolveWeather(ref contentBlob, weather.NextWeather) : current;
            float blend = weather.NextWeather >= 0 ? Unity.Mathematics.math.saturate(weather.Transition) : 0f;
            WeatherDefinitionDef dominant = blend < 0.5f ? current : next;

            audio.ResolvedLoopSound = interior ? default : ResolveAmbientLoopSound(ref contentBlob, current);
            audio.ResolvedNextLoopSound = !interior && weather.NextWeather >= 0 ? ResolveAmbientLoopSound(ref contentBlob, next) : default;
            audio.CurrentLoopVolume = interior ? 0f : 1f - blend;
            audio.NextLoopVolume = interior ? 0f : blend;

            rainAudio.ResolvedLoopSound = interior ? default : ResolveRainLoopSound(ref contentBlob, current);
            rainAudio.ResolvedNextLoopSound = !interior && weather.NextWeather >= 0 ? ResolveRainLoopSound(ref contentBlob, next) : default;
            rainAudio.CurrentLoopVolume = interior ? 0f : 1f - blend;
            rainAudio.NextLoopVolume = interior ? 0f : blend;

            if (!interior && sky.ThunderSequence != 0 && sky.ThunderSequence != audio.LastThunderSequence)
            {
                string soundId = ResolveThunderSoundId(dominant, sky.ThunderSoundIndex);
                if (!string.IsNullOrWhiteSpace(soundId)
                    && RuntimeContentBlobUtility.TryGetSoundHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(soundId), out var thunderSound)
                    && thunderSound.IsValid)
                {
                    region.PendingEventSound = thunderSound;
                    region.EventSequence += 1u;
                }

                audio.LastThunderSequence = sky.ThunderSequence;
            }
        }

        static WeatherDefinitionDef ResolveWeather(ref RuntimeContentBlob contentBlob, int index)
            => RuntimeContentBlobUtility.RequireWeatherDefinition(ref contentBlob, index);

        static SoundDefHandle ResolveAmbientLoopSound(ref RuntimeContentBlob contentBlob, in WeatherDefinitionDef weather)
        {
            return ResolveLoopSound(ref contentBlob, weather.AmbientLoopSoundId);
        }

        static SoundDefHandle ResolveRainLoopSound(ref RuntimeContentBlob contentBlob, in WeatherDefinitionDef weather)
        {
            return ResolveLoopSound(ref contentBlob, weather.RainLoopSoundId);
        }

        static SoundDefHandle ResolveLoopSound(ref RuntimeContentBlob contentBlob, string soundId)
        {
            if (!string.IsNullOrWhiteSpace(soundId)
                && RuntimeContentBlobUtility.TryGetSoundHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(soundId), out var handle))
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
