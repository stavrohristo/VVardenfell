using Unity.Collections;
using Unity.Entities;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Bootstrap;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Streaming
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup))]
    public partial class MorrowindTimeBootstrapSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindTimeBootstrapRequest>();
            RequireForUpdate<RuntimeContentBlobReference>();
        }

        protected override void OnUpdate()
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][ContentBlob] Time bootstrap requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;

            if (!SystemAPI.HasSingleton<MorrowindTimeState>())
            {
                Entity entity = EntityManager.CreateEntity();
                EntityManager.SetName(entity, "VVardenfell.TimeState");
                EntityManager.AddComponentData(entity, CreateDefaultTime(ref content));
                EntityManager.AddBuffer<MorrowindTimeAdvanceRequest>(entity);
            }
            else
            {
                Entity entity = SystemAPI.GetSingletonEntity<MorrowindTimeState>();
                if (!EntityManager.HasBuffer<MorrowindTimeAdvanceRequest>(entity))
                    EntityManager.AddBuffer<MorrowindTimeAdvanceRequest>(entity);
            }

            if (!SystemAPI.HasSingleton<MorrowindWeatherState>())
            {
                Entity entity = EntityManager.CreateEntity();
                EntityManager.SetName(entity, "VVardenfell.WeatherState");
                EntityManager.AddComponentData(entity, CreateDefaultWeather());
                EntityManager.AddBuffer<MorrowindWeatherChangeRequest>(entity);
                EntityManager.AddBuffer<MorrowindWeatherForceRequest>(entity);
                EntityManager.AddBuffer<MorrowindRegionWeatherCacheEntry>(entity);
                EntityManager.AddBuffer<MorrowindRegionWeatherOverrideEntry>(entity);
                EntityManager.AddBuffer<MorrowindRegionWeatherOverrideRequest>(entity);
            }
            else
            {
                Entity entity = SystemAPI.GetSingletonEntity<MorrowindWeatherState>();
                if (!EntityManager.HasBuffer<MorrowindWeatherChangeRequest>(entity))
                    EntityManager.AddBuffer<MorrowindWeatherChangeRequest>(entity);
                if (!EntityManager.HasBuffer<MorrowindWeatherForceRequest>(entity))
                    EntityManager.AddBuffer<MorrowindWeatherForceRequest>(entity);
                if (!EntityManager.HasBuffer<MorrowindRegionWeatherCacheEntry>(entity))
                    EntityManager.AddBuffer<MorrowindRegionWeatherCacheEntry>(entity);
                if (!EntityManager.HasBuffer<MorrowindRegionWeatherOverrideEntry>(entity))
                    EntityManager.AddBuffer<MorrowindRegionWeatherOverrideEntry>(entity);
                if (!EntityManager.HasBuffer<MorrowindRegionWeatherOverrideRequest>(entity))
                    EntityManager.AddBuffer<MorrowindRegionWeatherOverrideRequest>(entity);
            }

            RuntimeBootstrapRequestUtility.Consume<MorrowindTimeBootstrapRequest>(EntityManager);
        }

        public static MorrowindTimeState CreateDefaultTime()
        {
            var contentBlob = RuntimeContentBlobReferenceUtility.RequireBlob("Time bootstrap");
            ref RuntimeContentBlob content = ref contentBlob.Value;
            return CreateDefaultTime(ref content);
        }

        public static MorrowindTimeState CreateDefaultTime(ref RuntimeContentBlob content)
        {
            return new MorrowindTimeState
            {
                GameHour = RuntimeContentBlobUtility.RequireGlobalFloatByIdHash(ref content, RuntimeContentKnownHashes.gamehour),
                DaysPassed = ResolveDaysPassed(ref content),
                Day = RuntimeContentBlobUtility.RequireGlobalIntByIdHash(ref content, RuntimeContentKnownHashes.day),
                Month = RuntimeContentBlobUtility.RequireGlobalIntByIdHash(ref content, RuntimeContentKnownHashes.month),
                Year = RuntimeContentBlobUtility.RequireGlobalIntByIdHash(ref content, RuntimeContentKnownHashes.year),
                TimeScale = RuntimeContentBlobUtility.RequireGlobalFloatByIdHash(ref content, RuntimeContentKnownHashes.timescale),
                SimulationTimeScale = 1,
            };
        }

        static int ResolveDaysPassed(ref RuntimeContentBlob content)
        {
            if (RuntimeContentBlobUtility.TryGetGlobalHandleByIdHash(ref content, RuntimeContentKnownHashes.dayspassed, out var handle)
                && handle.IsValid)
            {
                ref RuntimeGenericRecordDefBlob global = ref RuntimeContentBlobUtility.GetGlobal(ref content, handle);
                if (global.ValueKind == GenericRecordValueKind.Integer)
                    return global.Int0;
                if (global.ValueKind == GenericRecordValueKind.Float)
                    return (int)global.Float0;
                if (global.ValueKind == GenericRecordValueKind.None)
                    return global.Int0;
                throw new System.InvalidOperationException($"[VVardenfell][ContentBlob] Global hash {RuntimeContentKnownHashes.dayspassed} is not numeric.");
            }

            return 1;
        }

        public static MorrowindWeatherState CreateDefaultWeather()
        {
            return new MorrowindWeatherState
            {
                CurrentWeather = (int)WeatherKind.Clear,
                NextWeather = -1,
                QueuedWeather = -1,
                Transition = 0f,
                TransitionFactor = 0f,
                TransitionDelta = 0.015f,
                HoursUntilNextChange = 20f,
                WeatherUpdateHoursRemaining = 20f,
                RandomState = 0x6E624EB7u,
                ForcedWeather = -1,
            };
        }

    }
}
