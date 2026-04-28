using Unity.Collections;
using Unity.Entities;
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
            if (!SystemAPI.HasSingleton<MorrowindTimeState>())
            {
                Entity entity = EntityManager.CreateEntity();
                EntityManager.SetName(entity, "VVardenfell.TimeState");
                EntityManager.AddComponentData(entity, CreateDefaultTime());
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
            }
        }

        protected override void OnUpdate()
        {
        }

        public static MorrowindTimeState CreateDefaultTime()
        {
            var contentDb = RuntimeContentDatabase.Active;
            return new MorrowindTimeState
            {
                GameHour = ReadGlobalFloat(contentDb, "gamehour", 12f),
                DaysPassed = ReadGlobalInt(contentDb, "dayspassed", 1),
                Day = ReadGlobalInt(contentDb, "day", 16),
                Month = ReadGlobalInt(contentDb, "month", 7),
                Year = ReadGlobalInt(contentDb, "year", 427),
                TimeScale = ReadGlobalFloat(contentDb, "timescale", 30f),
                SimulationTimeScale = 1f,
            };
        }

        public static MorrowindWeatherState CreateDefaultWeather()
        {
            return new MorrowindWeatherState
            {
                CurrentWeather = 0,
                NextWeather = 0,
                Transition = 0f,
                TransitionDelta = 0.015f,
                HoursUntilNextChange = 20f,
                RandomState = 0x6E624EB7u,
                ForcedWeather = -1,
            };
        }

        static float ReadGlobalFloat(RuntimeContentDatabase contentDb, string id, float fallback)
        {
            if (contentDb != null && contentDb.TryGetGlobalHandle(id, out var handle) && handle.IsValid)
                return contentDb.GetGlobal(handle).Float0;
            return fallback;
        }

        static int ReadGlobalInt(RuntimeContentDatabase contentDb, string id, int fallback)
        {
            if (contentDb != null && contentDb.TryGetGlobalHandle(id, out var handle) && handle.IsValid)
                return contentDb.GetGlobal(handle).Int0;
            return fallback;
        }
    }
}
