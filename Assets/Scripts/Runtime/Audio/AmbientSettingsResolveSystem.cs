using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Audio
{
    [UpdateInGroup(typeof(MorrowindAudioSimulationSystemGroup))]
    public partial class AmbientSettingsResolveSystem : SystemBase
    {
        static readonly ProfilerMarker k_SettingsResolve = new("VV.Audio.ResolveAmbientSettings");

        protected override void OnCreate()
        {
            RequireForUpdate<AmbientSettingsState>();
            RequireForUpdate<AudioTuningState>();
            RequireForUpdate<RuntimeContentBlobReference>();
        }

        protected override void OnUpdate()
        {
            using var _ = k_SettingsResolve.Auto();

            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;
            var tuning = SystemAPI.GetSingleton<AudioTuningState>();
            ref var state = ref SystemAPI.GetSingletonRW<AmbientSettingsState>().ValueRW;

            var settings = contentBlob.AmbientSettings;
            state.MinSecondsBetweenEnvironmentalSounds = math.max(
                0.05f,
                settings.MinSecondsBetweenEnvironmentalSounds * math.max(0f, tuning.ExteriorAmbientMinIntervalMultiplier));
            state.MaxSecondsBetweenEnvironmentalSounds = math.max(
                state.MinSecondsBetweenEnvironmentalSounds,
                settings.MaxSecondsBetweenEnvironmentalSounds * math.max(0f, tuning.ExteriorAmbientMaxIntervalMultiplier));
        }
    }
}
