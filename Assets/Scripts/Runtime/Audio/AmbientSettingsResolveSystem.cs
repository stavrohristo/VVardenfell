using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Audio
{
    [UpdateInGroup(typeof(MorrowindAudioSimulationSystemGroup))]
    [UpdateAfter(typeof(AudioContextResolveSystem))]
    public partial class AmbientSettingsResolveSystem : SystemBase
    {
        static readonly ProfilerMarker k_SettingsResolve = new("VV.Audio.ResolveAmbientSettings");

        protected override void OnCreate()
        {
            RequireForUpdate<AmbientSettingsState>();
            RequireForUpdate<AudioTuningState>();
        }

        protected override void OnUpdate()
        {
            using var _ = k_SettingsResolve.Auto();

            var contentDb = RuntimeContentDatabase.Active;
            var tuning = SystemAPI.GetSingleton<AudioTuningState>();
            ref var state = ref SystemAPI.GetSingletonRW<AmbientSettingsState>().ValueRW;
            if (contentDb == null)
            {
                state.MinSecondsBetweenEnvironmentalSounds = math.max(0.05f, tuning.ExteriorAmbientFallbackMinSeconds);
                state.MaxSecondsBetweenEnvironmentalSounds = math.max(state.MinSecondsBetweenEnvironmentalSounds, tuning.ExteriorAmbientFallbackMaxSeconds);
                return;
            }

            var settings = contentDb.GetAmbientSettings();
            state.MinSecondsBetweenEnvironmentalSounds = math.max(
                0.05f,
                settings.MinSecondsBetweenEnvironmentalSounds * math.max(0f, tuning.ExteriorAmbientMinIntervalMultiplier));
            state.MaxSecondsBetweenEnvironmentalSounds = math.max(
                state.MinSecondsBetweenEnvironmentalSounds,
                settings.MaxSecondsBetweenEnvironmentalSounds * math.max(0f, tuning.ExteriorAmbientMaxIntervalMultiplier));
        }
    }
}
