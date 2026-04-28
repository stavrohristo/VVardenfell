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
            Entity weatherEntity = SystemAPI.GetSingletonEntity<MorrowindWeatherState>();
            ref var weather = ref SystemAPI.GetSingletonRW<MorrowindWeatherState>().ValueRW;
            var time = SystemAPI.GetSingleton<MorrowindTimeState>();
            DynamicBuffer<MorrowindWeatherChangeRequest> changeRequests = SystemAPI.GetBuffer<MorrowindWeatherChangeRequest>(weatherEntity);
            DynamicBuffer<MorrowindWeatherForceRequest> forceRequests = SystemAPI.GetBuffer<MorrowindWeatherForceRequest>(weatherEntity);
            DynamicBuffer<MorrowindRegionWeatherCacheEntry> regionWeather = SystemAPI.GetBuffer<MorrowindRegionWeatherCacheEntry>(weatherEntity);

            bool interiorActive = SystemAPI.HasSingleton<InteriorTransitionState>() && SystemAPI.GetSingleton<InteriorTransitionState>().InteriorActive != 0;
            int regionHandleValue = ResolveCurrentRegionHandle(contentDb);
            var settings = contentDb?.Data?.WeatherSettings ?? default;
            float hoursBetweenChanges = settings.HoursBetweenWeatherChanges > 0f ? settings.HoursBetweenWeatherChanges : 20f;
            var random = new Unity.Mathematics.Random(EnsureSeed(weather.RandomState));

            if (weather.Initialized == 0)
            {
                weather.CurrentWeather = ClampWeatherIndex(GetRegionWeather(contentDb, regionHandleValue, regionWeather, ref random), contentDb);
                weather.NextWeather = -1;
                weather.QueuedWeather = -1;
                weather.Transition = 0f;
                weather.TransitionFactor = 0f;
                weather.HoursUntilNextChange = hoursBetweenChanges;
                weather.WeatherUpdateHoursRemaining = hoursBetweenChanges;
                weather.RegionHandleValue = regionHandleValue;
                weather.RandomState = random.state;
                weather.Initialized = 1;
            }

            ProcessForceRequests(ref weather, forceRequests, contentDb);
            if (weather.ForcedWeather >= 0)
            {
                int forced = ClampWeatherIndex(weather.ForcedWeather, contentDb);
                weather.CurrentWeather = forced;
                weather.NextWeather = -1;
                weather.QueuedWeather = -1;
                weather.Transition = 0f;
                weather.TransitionFactor = 0f;
                weather.Transitioning = 0;
                weather.HoursUntilNextChange = hoursBetweenChanges;
                weather.WeatherUpdateHoursRemaining = hoursBetweenChanges;
                weather.RegionHandleValue = regionHandleValue;
                weather.RandomState = random.state;
                return;
            }

            ProcessChangeRequests(ref weather, changeRequests, regionWeather, contentDb, regionHandleValue);

            if (interiorActive)
            {
                AdvanceWeatherUpdateTimer(ref weather, regionWeather, hoursBetweenChanges, time.LastAdvancedHours);
                AdvanceTransition(ref weather, contentDb, SystemAPI.Time.DeltaTime, time.FastForwarding != 0);
                weather.RandomState = random.state;
                return;
            }

            bool regionChanged = regionHandleValue != weather.RegionHandleValue;
            weather.RegionHandleValue = regionHandleValue;

            bool expiredWeather = AdvanceWeatherUpdateTimer(ref weather, regionWeather, hoursBetweenChanges, time.LastAdvancedHours);
            AdvanceTransition(ref weather, contentDb, SystemAPI.Time.DeltaTime, time.FastForwarding != 0);
            if (weather.Transitioning != 0)
                return;

            if (!regionChanged && !expiredWeather)
                return;

            int next = ClampWeatherIndex(GetRegionWeather(contentDb, regionHandleValue, regionWeather, ref random), contentDb);
            weather.RandomState = random.state;
            AddWeatherTransition(ref weather, contentDb, next);
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

        static int SampleWeather(RuntimeContentDatabase contentDb, int regionHandleValue, ref Unity.Mathematics.Random random)
        {
            if (contentDb == null)
                return MorrowindWeatherSelectionUtility.SampleFallbackExteriorWeather(ref random);

            if (regionHandleValue <= 0)
                return MorrowindWeatherSelectionUtility.SampleFallbackExteriorWeather(ref random);

            ref readonly var region = ref contentDb.Get(new RegionDefHandle { Value = regionHandleValue });
            return MorrowindWeatherSelectionUtility.SampleRegionWeather(region, ref random);
        }

        static int GetRegionWeather(RuntimeContentDatabase contentDb, int regionHandleValue, DynamicBuffer<MorrowindRegionWeatherCacheEntry> regionWeather, ref Unity.Mathematics.Random random)
        {
            int region = math.max(0, regionHandleValue);
            for (int i = 0; i < regionWeather.Length; i++)
            {
                if (regionWeather[i].RegionHandleValue == region)
                    return regionWeather[i].Weather;
            }

            int weather = SampleWeather(contentDb, region, ref random);
            regionWeather.Add(new MorrowindRegionWeatherCacheEntry
            {
                RegionHandleValue = region,
                Weather = weather,
            });
            return weather;
        }

        static void SetRegionWeather(DynamicBuffer<MorrowindRegionWeatherCacheEntry> regionWeather, int regionHandleValue, int weather)
        {
            int region = math.max(0, regionHandleValue);
            for (int i = 0; i < regionWeather.Length; i++)
            {
                if (regionWeather[i].RegionHandleValue != region)
                    continue;

                regionWeather[i] = new MorrowindRegionWeatherCacheEntry
                {
                    RegionHandleValue = region,
                    Weather = weather,
                };
                return;
            }

            regionWeather.Add(new MorrowindRegionWeatherCacheEntry
            {
                RegionHandleValue = region,
                Weather = weather,
            });
        }

        static void ProcessForceRequests(ref MorrowindWeatherState weather, DynamicBuffer<MorrowindWeatherForceRequest> requests, RuntimeContentDatabase contentDb)
        {
            for (int i = 0; i < requests.Length; i++)
            {
                if (requests[i].Clear != 0)
                {
                    weather.ForcedWeather = -1;
                    continue;
                }

                weather.ForcedWeather = ClampWeatherIndex(requests[i].Weather, contentDb);
            }

            requests.Clear();
        }

        static void ProcessChangeRequests(
            ref MorrowindWeatherState weather,
            DynamicBuffer<MorrowindWeatherChangeRequest> requests,
            DynamicBuffer<MorrowindRegionWeatherCacheEntry> regionWeather,
            RuntimeContentDatabase contentDb,
            int activeRegion)
        {
            for (int i = 0; i < requests.Length; i++)
            {
                int requested = ClampWeatherIndex(requests[i].Weather, contentDb);
                SetRegionWeather(regionWeather, requests[i].RegionHandleValue, requested);
                if (requests[i].RegionHandleValue == activeRegion)
                    AddWeatherTransition(ref weather, contentDb, requested);
            }

            requests.Clear();
        }

        static bool AdvanceWeatherUpdateTimer(ref MorrowindWeatherState weather, DynamicBuffer<MorrowindRegionWeatherCacheEntry> regionWeather, float hoursBetweenChanges, float advancedHours)
        {
            float remaining = weather.WeatherUpdateHoursRemaining > 0f ? weather.WeatherUpdateHoursRemaining : weather.HoursUntilNextChange;
            remaining -= math.max(0f, advancedHours);
            bool expired = false;
            while (remaining <= 0f)
            {
                regionWeather.Clear();
                remaining += hoursBetweenChanges;
                expired = true;
            }

            weather.WeatherUpdateHoursRemaining = remaining;
            weather.HoursUntilNextChange = remaining;
            return expired;
        }

        static void AddWeatherTransition(ref MorrowindWeatherState weather, RuntimeContentDatabase contentDb, int weatherIndex)
        {
            int next = ClampWeatherIndex(weatherIndex, contentDb);
            if (weather.NextWeather < 0 && next != weather.CurrentWeather)
            {
                weather.NextWeather = next;
                weather.QueuedWeather = -1;
                weather.TransitionFactor = 1f;
                weather.Transition = 0f;
                weather.TransitionDelta = ResolveTransitionDelta(contentDb, next);
                weather.Transitioning = 1;
                return;
            }

            if (weather.NextWeather >= 0 && next != weather.NextWeather)
                weather.QueuedWeather = next;
        }

        static void AdvanceTransition(ref MorrowindWeatherState weather, RuntimeContentDatabase contentDb, float elapsedSeconds, bool fastForward)
        {
            if (weather.NextWeather < 0)
            {
                weather.TransitionFactor = 0f;
                weather.Transition = 0f;
                weather.Transitioning = 0;
                return;
            }

            if (fastForward)
            {
                weather.CurrentWeather = ClampWeatherIndex(weather.QueuedWeather >= 0 ? weather.QueuedWeather : weather.NextWeather, contentDb);
                weather.NextWeather = -1;
                weather.QueuedWeather = -1;
                weather.TransitionFactor = 0f;
                weather.Transition = 0f;
                weather.Transitioning = 0;
                return;
            }

            weather.TransitionFactor -= math.max(0f, elapsedSeconds) * math.max(0.001f, weather.TransitionDelta);
            if (weather.TransitionFactor <= 0f)
            {
                weather.CurrentWeather = ClampWeatherIndex(weather.NextWeather, contentDb);
                weather.NextWeather = weather.QueuedWeather;
                weather.QueuedWeather = -1;
                if (weather.NextWeather >= 0)
                {
                    weather.TransitionFactor = 1f;
                    weather.TransitionDelta = ResolveTransitionDelta(contentDb, weather.NextWeather);
                    weather.Transitioning = 1;
                }
                else
                {
                    weather.TransitionFactor = 0f;
                    weather.Transitioning = 0;
                }
            }

            weather.Transition = weather.NextWeather >= 0 ? math.saturate(1f - weather.TransitionFactor) : 0f;
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
            int nextIndex = weather.NextWeather >= 0 ? weather.NextWeather : -1;
            WeatherDefinitionDef next = nextIndex >= 0 ? ResolveWeather(contentDb, nextIndex) : current;
            WeatherSettingsDef weatherSettings = contentDb?.Data?.WeatherSettings ?? MorrowindDayCycleUtility.CreateFallbackWeatherSettings(settings);
            var blended = MorrowindWeatherEvaluationUtility.Evaluate(settings, weatherSettings, current, weather.CurrentWeather, next, nextIndex, weather.Transition, time.GameHour);
            var eval = blended.Evaluation;
            WeatherDefinitionDef dominant = blended.DominantWeather;
            var masser = MorrowindDayCycleUtility.EvaluateMoon(weatherSettings.MasserMoon, time.DaysPassed, time.GameHour);
            var secunda = MorrowindDayCycleUtility.EvaluateMoon(weatherSettings.SecundaMoon, time.DaysPassed, time.GameHour);

            ResolveThunder(ref weather, current, next, blended, time.Paused != 0);

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
                SunDiscOpacity = eval.SunPercent * math.saturate(blended.GlareView),
                CloudOpacity = blended.CloudOpacity,
                CloudSpeed = blended.CloudSpeed,
                CloudUvOffset = MorrowindDayCycleUtility.ComputeCloudUvOffset(time.DaysPassed, time.GameHour, blended.CloudSpeed),
                CurrentCloudTextureIndex = (int)current.Kind,
                NextCloudTextureIndex = (int)next.Kind,
                WindSpeed = blended.WindSpeed,
                StormDirection = ResolveStormDirection(dominant),
                PrecipitationIntensity = blended.PrecipitationIntensity,
                PrecipitationAlpha = blended.PrecipitationAlpha,
                PrecipitationKind = MorrowindWeatherEvaluationUtility.ResolvePrecipitationKind(dominant),
                RainSpeed = dominant.RainSpeed,
                RainEntranceSpeed = dominant.RainEntranceSpeed,
                RainDiameter = dominant.RainDiameter,
                RainMinHeight = dominant.RainMinHeight,
                RainMaxHeight = dominant.RainMaxHeight,
                RainMaxRaindrops = dominant.RainMaxRaindrops,
                Glare = eval.SunPercent * blended.GlareView,
                LightningBrightness = weather.LightningBrightness * math.max(0f, settings.LightningIntensityScale),
                ThunderSequence = weather.ThunderSequence,
                ThunderSoundIndex = weather.LastThunderSoundIndex,
                WeatherKind = (int)current.Kind,
                NextWeatherKind = (int)next.Kind,
                WeatherTransition = blended.CloudBlendFactor,
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

        static float3 ResolveStormDirection(in WeatherDefinitionDef weather)
        {
            if (weather.Kind == WeatherKind.Ashstorm || weather.Kind == WeatherKind.Blight)
                return math.normalize(new float3(-1f, 0f, -0.35f));
            return math.normalize(new float3(1f, 0f, 0.35f));
        }

        void ResolveThunder(ref MorrowindWeatherState weather, in WeatherDefinitionDef current, in WeatherDefinitionDef next, in MorrowindWeatherEvaluationUtility.WeatherBlend blend, bool paused)
        {
            float elapsedSeconds = SystemAPI.Time.DeltaTime;
            float flashDecrement = math.max(0.1f, blend.DominantWeather.FlashDecrement);
            if (!paused)
                weather.LightningBrightness = math.max(0f, weather.LightningBrightness - elapsedSeconds * flashDecrement);

            var random = new Unity.Mathematics.Random(weather.RandomState == 0u ? 1u : weather.RandomState);
            bool struck = MorrowindWeatherEvaluationUtility.TryResolveThunder(current, blend.ThunderCurrentRatio, elapsedSeconds, paused, ref random, out int currentSound, out float currentFlash);
            int nextSound = 0;
            float nextFlash = 0f;
            bool nextStruck = blend.ThunderNextRatio > 0f
                && MorrowindWeatherEvaluationUtility.TryResolveThunder(next, blend.ThunderNextRatio, elapsedSeconds, paused, ref random, out nextSound, out nextFlash);

            if (!struck && !nextStruck)
            {
                weather.RandomState = random.state;
                return;
            }

            weather.ThunderSequence++;
            weather.LastThunderSoundIndex = nextStruck ? nextSound : currentSound;
            weather.LightningBrightness = math.saturate(weather.LightningBrightness + (nextStruck ? nextFlash : currentFlash));
            weather.RandomState = random.state;
        }
    }
}
