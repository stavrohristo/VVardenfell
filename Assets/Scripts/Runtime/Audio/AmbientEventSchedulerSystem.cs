using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Audio
{
    [UpdateInGroup(typeof(MorrowindAudioSimulationSystemGroup))]
    [UpdateAfter(typeof(RegionAmbientResolveSystem))]
    [UpdateAfter(typeof(AmbientSettingsResolveSystem))]
    public partial class AmbientEventSchedulerSystem : SystemBase
    {
        static readonly ProfilerMarker k_ScheduleAmbient = new("VV.Audio.ScheduleRegionAmbient");

        protected override void OnCreate()
        {
            RequireForUpdate<AudioContextState>();
            RequireForUpdate<RegionAmbientState>();
            RequireForUpdate<AmbientSchedulerState>();
            RequireForUpdate<AmbientSettingsState>();
            RequireForUpdate<RuntimeContentBlobReference>();
        }

        protected override void OnUpdate()
        {
            using var _ = k_ScheduleAmbient.Auto();

            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;

            ref var context = ref SystemAPI.GetSingletonRW<AudioContextState>().ValueRW;
            ref var regionState = ref SystemAPI.GetSingletonRW<RegionAmbientState>().ValueRW;
            ref var scheduler = ref SystemAPI.GetSingletonRW<AmbientSchedulerState>().ValueRW;
            var settings = SystemAPI.GetSingleton<AmbientSettingsState>();

            if (context.Mode != AudioPlaybackMode.World || !regionState.Region.IsValid)
            {
                scheduler.ActiveRegionHandleValue = 0;
                scheduler.Initialized = 0;
                return;
            }

            if (scheduler.ActiveRegionHandleValue != regionState.Region.Value || scheduler.Initialized == 0)
            {
                scheduler.ActiveRegionHandleValue = regionState.Region.Value;
                if (scheduler.Initialized == 0)
                {
                    scheduler.RandomState = CreateSeed(regionState.Region.Value);
                    scheduler.SecondsUntilNextAttempt = 0f;
                    scheduler.Initialized = 1;
                }
            }

            float dt = SystemAPI.Time.DeltaTime;
            scheduler.SecondsUntilNextAttempt -= dt;
            if (scheduler.SecondsUntilNextAttempt > 0f)
                return;

            var random = new Unity.Mathematics.Random(EnsureSeed(scheduler.RandomState));
            scheduler.SecondsUntilNextAttempt = SampleNextInterval(ref random, settings);

            ref var refs = ref RuntimeContentBlobUtility.GetRegionSoundRefs(ref contentBlob, regionState.Region, out int firstSoundRef, out int soundRefCount);
            if (soundRefCount > 0)
            {
                for (int i = 0; i < soundRefCount; i++)
                {
                    ref var candidate = ref refs[firstSoundRef + i];
                    int roll = random.NextInt(100);
                    if (roll >= candidate.Chance)
                        continue;
                    if (!RuntimeContentBlobUtility.TryGetSoundHandleByIdHash(ref contentBlob, RuntimeContentStableHash.HashId(candidate.SoundId.ToString()), out var handle) || !handle.IsValid)
                        continue;

                    regionState.PendingEventSound = handle;
                    regionState.EventSequence += 1u;
                    break;
                }
            }

            scheduler.RandomState = random.state;
        }

        static float SampleNextInterval(ref Unity.Mathematics.Random random, in AmbientSettingsState settings)
        {
            if (settings.MaxSecondsBetweenEnvironmentalSounds <= settings.MinSecondsBetweenEnvironmentalSounds)
                return settings.MinSecondsBetweenEnvironmentalSounds;

            return random.NextFloat(settings.MinSecondsBetweenEnvironmentalSounds, settings.MaxSecondsBetweenEnvironmentalSounds);
        }

        static uint CreateSeed(int regionHandleValue)
        {
            uint seed = (uint)regionHandleValue * 747796405u + 2891336453u;
            return EnsureSeed(seed);
        }

        static uint EnsureSeed(uint seed) => seed == 0u ? 1u : seed;
    }
}
