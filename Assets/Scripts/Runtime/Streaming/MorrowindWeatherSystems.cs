using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Streaming
{
    [UpdateInGroup(typeof(MorrowindEnvironmentSystemGroup))]
    [UpdateAfter(typeof(MorrowindTimeAdvanceSystem))]
    [UpdateBefore(typeof(LightingEnvironmentResolveSystem))]
    public partial class MorrowindWeatherSelectionSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindTimeState>();
            RequireForUpdate<MorrowindWeatherState>();
        }

        protected override void OnUpdate()
        {
            var contentDb = RuntimeContentDatabase.Active;
            ref var weather = ref SystemAPI.GetSingletonRW<MorrowindWeatherState>().ValueRW;
            var time = SystemAPI.GetSingleton<MorrowindTimeState>();

            bool interiorActive = SystemAPI.HasSingleton<InteriorTransitionState>() && SystemAPI.GetSingleton<InteriorTransitionState>().InteriorActive != 0;
            int regionHandleValue = ResolveCurrentRegionHandle(contentDb);
            var settings = contentDb?.Data?.WeatherSettings ?? default;
            float hoursBetweenChanges = settings.HoursBetweenWeatherChanges > 0f ? settings.HoursBetweenWeatherChanges : 20f;
            var random = new Unity.Mathematics.Random(EnsureSeed(weather.RandomState));

            if (weather.Initialized == 0)
            {
                weather.CurrentWeather = ClampWeatherIndex(SampleWeather(contentDb, regionHandleValue, ref random), contentDb);
                weather.NextWeather = weather.CurrentWeather;
                weather.Transition = 0f;
                weather.HoursUntilNextChange = hoursBetweenChanges;
                weather.RegionHandleValue = regionHandleValue;
                weather.RandomState = random.state;
                weather.Initialized = 1;
            }

            if (weather.ForcedWeather >= 0)
            {
                int forced = ClampWeatherIndex(weather.ForcedWeather, contentDb);
                weather.CurrentWeather = forced;
                weather.NextWeather = forced;
                weather.Transition = 0f;
                weather.Transitioning = 0;
                weather.HoursUntilNextChange = hoursBetweenChanges;
                weather.RegionHandleValue = regionHandleValue;
                return;
            }

            if (interiorActive)
            {
                weather.HoursUntilNextChange = math.max(0f, weather.HoursUntilNextChange - math.max(0f, time.LastAdvancedHours));
                weather.RandomState = random.state;
                return;
            }

            bool regionChanged = regionHandleValue != weather.RegionHandleValue;
            weather.RegionHandleValue = regionHandleValue;

            if (weather.Transitioning != 0)
            {
                float delta = time.FastForwarding != 0
                    ? 1f
                    : SystemAPI.Time.DeltaTime * math.max(0.001f, weather.TransitionDelta);
                weather.Transition = math.saturate(weather.Transition + delta);
                if (weather.Transition >= 1f)
                {
                    weather.CurrentWeather = ClampWeatherIndex(weather.NextWeather, contentDb);
                    weather.Transition = 0f;
                    weather.Transitioning = 0;
                    weather.HoursUntilNextChange = hoursBetweenChanges;
                }
                return;
            }

            weather.HoursUntilNextChange -= math.max(0f, time.LastAdvancedHours);
            if (!regionChanged && weather.HoursUntilNextChange > 0f)
                return;

            int next = SampleWeather(contentDb, regionHandleValue, ref random, weather.CurrentWeather);
            weather.RandomState = random.state;
            weather.HoursUntilNextChange = hoursBetweenChanges;
            if (next == weather.CurrentWeather)
                return;

            weather.NextWeather = next;
            weather.Transition = 0f;
            weather.TransitionDelta = ResolveTransitionDelta(contentDb, next);
            weather.Transitioning = 1;
        }

        int ResolveCurrentRegionHandle(RuntimeContentDatabase contentDb)
        {
            if (contentDb == null || !SystemAPI.HasSingleton<StreamingConfig>())
                return 0;

            if (SystemAPI.HasSingleton<InteriorTransitionState>() && SystemAPI.GetSingleton<InteriorTransitionState>().InteriorActive != 0)
                return 0;

            var streaming = SystemAPI.GetSingleton<StreamingConfig>();
            if (WorldResources.Cells.TryGetValue(streaming.CameraCell, out var cell)
                && cell != null
                && !string.IsNullOrWhiteSpace(cell.Environment.RegionId)
                && contentDb.TryGetRegionHandle(cell.Environment.RegionId, out var regionHandle))
                return regionHandle.Value;

            return 0;
        }

        static int SampleWeather(RuntimeContentDatabase contentDb, int regionHandleValue, ref Unity.Mathematics.Random random, int excludedWeather = -1)
        {
            if (contentDb == null)
                return MorrowindWeatherSelectionUtility.SampleFallbackExteriorWeather(ref random, excludedWeather);

            if (regionHandleValue <= 0)
                return MorrowindWeatherSelectionUtility.SampleFallbackExteriorWeather(ref random, excludedWeather);

            ref readonly var region = ref contentDb.Get(new RegionDefHandle { Value = regionHandleValue });
            return MorrowindWeatherSelectionUtility.SampleRegionWeather(region, ref random, excludedWeather);
        }

        static int ClampWeatherIndex(int index, RuntimeContentDatabase contentDb)
        {
            int count = contentDb?.Data?.WeatherDefinitions?.Length ?? 0;
            if (count <= 0)
                return 0;
            return math.clamp(index, 0, count - 1);
        }

        static float ResolveTransitionDelta(RuntimeContentDatabase contentDb, int weatherIndex)
        {
            var defs = contentDb?.Data?.WeatherDefinitions;
            if (defs == null || (uint)weatherIndex >= (uint)defs.Length)
                return 0.015f;
            return defs[weatherIndex].TransitionDelta > 0f ? defs[weatherIndex].TransitionDelta : 0.015f;
        }

        static uint EnsureSeed(uint seed) => seed == 0u ? 1u : seed;
    }

    [UpdateInGroup(typeof(MorrowindEnvironmentSystemGroup))]
    [UpdateAfter(typeof(MorrowindWeatherSelectionSystem))]
    [UpdateBefore(typeof(LightingEnvironmentResolveSystem))]
    public partial class MorrowindSkyWeatherResolveSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MorrowindTimeState>();
            RequireForUpdate<MorrowindDayCycleState>();
            RequireForUpdate<MorrowindWeatherState>();
            RequireForUpdate<ActiveSkyWeatherState>();
        }

        protected override void OnUpdate()
        {
            var contentDb = RuntimeContentDatabase.Active;
            var time = SystemAPI.GetSingleton<MorrowindTimeState>();
            var settings = SystemAPI.GetSingleton<MorrowindDayCycleState>();
            ref var weather = ref SystemAPI.GetSingletonRW<MorrowindWeatherState>().ValueRW;
            ref var sky = ref SystemAPI.GetSingletonRW<ActiveSkyWeatherState>().ValueRW;

            WeatherDefinitionDef current = ResolveWeather(contentDb, weather.CurrentWeather);
            WeatherDefinitionDef next = ResolveWeather(contentDb, weather.NextWeather);
            WeatherSettingsDef weatherSettings = contentDb?.Data?.WeatherSettings ?? MorrowindDayCycleUtility.CreateFallbackWeatherSettings(settings);
            var currentEval = MorrowindDayCycleUtility.EvaluateWeather(settings, weatherSettings, current, time.GameHour);
            var nextEval = MorrowindDayCycleUtility.EvaluateWeather(settings, weatherSettings, next, time.GameHour);
            var eval = MorrowindDayCycleUtility.Lerp(currentEval, nextEval, weather.Transition);
            WeatherDefinitionDef dominant = weather.Transition < 0.5f ? current : next;
            var masser = MorrowindDayCycleUtility.EvaluateMoon(weatherSettings.MasserMoon, time.DaysPassed, time.GameHour);
            var secunda = MorrowindDayCycleUtility.EvaluateMoon(weatherSettings.SecundaMoon, time.DaysPassed, time.GameHour);
            float cloudSpeed = math.lerp(current.CloudSpeed, next.CloudSpeed, weather.Transition);

            float precipitation = dominant.UsingPrecip != 0
                ? math.saturate(math.max(0f, dominant.CloudsMaximumPercent - dominant.RainThreshold) / math.max(0.0001f, 1f - dominant.RainThreshold))
                : 0f;
            precipitation *= math.max(0f, settings.PrecipitationIntensityScale);

            ResolveThunder(ref weather, dominant, precipitation);

            sky = new ActiveSkyWeatherState
            {
                SkyColorRgb = eval.SkyColorRgb,
                SunDiscColorRgb = eval.SunDiscColorRgb,
                SkySunWorldDirection = eval.SkySunWorldDirection,
                UnityLightDirection = eval.SunDirectionToLight,
                MasserWorldDirection = masser.WorldDirection,
                SecundaWorldDirection = secunda.WorldDirection,
                MoonOpacity = eval.MoonOpacity,
                MasserOpacity = masser.Alpha * eval.MoonOpacity,
                SecundaOpacity = secunda.Alpha * eval.MoonOpacity,
                MasserShadowBlend = masser.ShadowBlend,
                SecundaShadowBlend = secunda.ShadowBlend,
                MasserSize = masser.Size,
                SecundaSize = secunda.Size,
                MasserPhase = masser.Phase,
                SecundaPhase = secunda.Phase,
                StarOpacity = eval.StarOpacity,
                StarRotationDegrees = MorrowindDayCycleUtility.ComputeStarRotationDegrees(time.DaysPassed, time.GameHour),
                SunDiscOpacity = eval.SunPercent * math.saturate(math.lerp(current.GlareView, next.GlareView, weather.Transition)),
                CloudOpacity = math.lerp(current.CloudsMaximumPercent, next.CloudsMaximumPercent, weather.Transition),
                CloudSpeed = cloudSpeed,
                CloudUvOffset = MorrowindDayCycleUtility.ComputeCloudUvOffset(time.DaysPassed, time.GameHour, cloudSpeed),
                CurrentCloudTextureIndex = (int)current.Kind,
                NextCloudTextureIndex = (int)next.Kind,
                WindSpeed = math.lerp(current.WindSpeed, next.WindSpeed, weather.Transition),
                StormDirection = math.normalize(new float3(1f, 0f, 0.35f)),
                PrecipitationIntensity = precipitation,
                RainSpeed = dominant.RainSpeed,
                RainDiameter = dominant.RainDiameter,
                RainMinHeight = dominant.RainMinHeight,
                RainMaxHeight = dominant.RainMaxHeight,
                Glare = eval.SunPercent * math.lerp(current.GlareView, next.GlareView, weather.Transition),
                LightningBrightness = weather.LightningBrightness * math.max(0f, settings.LightningIntensityScale),
                ThunderSequence = weather.ThunderSequence,
                ThunderSoundIndex = weather.LastThunderSoundIndex,
                WeatherKind = (int)current.Kind,
                NextWeatherKind = (int)next.Kind,
                WeatherTransition = weather.Transition,
                IsNight = eval.IsNight,
                IsStorm = dominant.IsStorm,
                IsInterior = SystemAPI.HasSingleton<InteriorTransitionState>() && SystemAPI.GetSingleton<InteriorTransitionState>().InteriorActive != 0 ? (byte)1 : (byte)0,
            };
        }

        static WeatherDefinitionDef ResolveWeather(RuntimeContentDatabase contentDb, int index)
        {
            var defs = contentDb?.Data?.WeatherDefinitions;
            if (defs != null && (uint)index < (uint)defs.Length)
                return defs[index];
            return MorrowindDayCycleUtility.CreateFallbackClearWeather();
        }

        void ResolveThunder(ref MorrowindWeatherState weather, in WeatherDefinitionDef dominant, float precipitation)
        {
            weather.LightningBrightness = math.max(0f, weather.LightningBrightness - SystemAPI.Time.DeltaTime * math.max(0.1f, dominant.FlashDecrement));
            if (dominant.Kind != WeatherKind.Thunderstorm || precipitation <= dominant.ThunderThreshold || dominant.ThunderFrequency <= 0f)
                return;

            weather.SecondsUntilThunder -= SystemAPI.Time.DeltaTime;
            if (weather.SecondsUntilThunder > 0f)
                return;

            var random = new Unity.Mathematics.Random(weather.RandomState == 0u ? 1u : weather.RandomState);
            weather.SecondsUntilThunder = random.NextFloat(1f, math.max(1.1f, 1f / math.max(0.01f, dominant.ThunderFrequency)));
            weather.ThunderSequence++;
            weather.LastThunderSoundIndex = random.NextInt(4);
            weather.LightningBrightness = 1f;
            weather.RandomState = random.state;
        }
    }
}
