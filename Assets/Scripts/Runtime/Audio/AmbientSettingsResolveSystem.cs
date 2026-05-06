using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Audio
{
    [UpdateInGroup(typeof(MorrowindAudioSimulationSystemGroup))]
    public partial struct AmbientSettingsResolveSystem : ISystem
    {
        static readonly ProfilerMarker k_SettingsResolve = new("VV.Audio.ResolveAmbientSettings");

        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<AmbientSettingsState>();
            systemState.RequireForUpdate<AudioTuningState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
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
