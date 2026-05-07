using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Streaming
{
    [UpdateInGroup(typeof(MorrowindEnvironmentSystemGroup))]
    [UpdateAfter(typeof(MorrowindTimeAdvanceSystem))]
    [UpdateBefore(typeof(LightingEnvironmentResolveSystem))]
    public partial struct MorrowindWeatherSelectionSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindTimeState>();
            systemState.RequireForUpdate<MorrowindWeatherState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
            systemState.RequireForUpdate<RuntimeWorldCellBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][ContentBlob] Weather selection requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;
            Entity weatherEntity = SystemAPI.GetSingletonEntity<MorrowindWeatherState>();
            ref var weather = ref SystemAPI.GetSingletonRW<MorrowindWeatherState>().ValueRW;
            var time = SystemAPI.GetSingleton<MorrowindTimeState>();
            DynamicBuffer<MorrowindWeatherChangeRequest> changeRequests = SystemAPI.GetBuffer<MorrowindWeatherChangeRequest>(weatherEntity);
            DynamicBuffer<MorrowindWeatherForceRequest> forceRequests = SystemAPI.GetBuffer<MorrowindWeatherForceRequest>(weatherEntity);
            DynamicBuffer<MorrowindRegionWeatherCacheEntry> regionWeather = SystemAPI.GetBuffer<MorrowindRegionWeatherCacheEntry>(weatherEntity);
            DynamicBuffer<MorrowindRegionWeatherOverrideEntry> regionOverrides = SystemAPI.GetBuffer<MorrowindRegionWeatherOverrideEntry>(weatherEntity);
            DynamicBuffer<MorrowindRegionWeatherOverrideRequest> regionOverrideRequests = SystemAPI.GetBuffer<MorrowindRegionWeatherOverrideRequest>(weatherEntity);

            bool interiorActive = SystemAPI.HasSingleton<InteriorTransitionState>() && SystemAPI.GetSingleton<InteriorTransitionState>().InteriorActive != 0;
            int regionHandleValue = ResolveCurrentRegionHandle(ref systemState, ref content);
            var settings = content.WeatherSettings;
            float hoursBetweenChanges = settings.HoursBetweenWeatherChanges > 0f ? settings.HoursBetweenWeatherChanges : 20f;
            var random = new Unity.Mathematics.Random(EnsureSeed(weather.RandomState));

            if (weather.Initialized == 0)
            {
                weather.CurrentWeather = RuntimeContentBlobUtility.ClampWeatherIndex(ref content, (int)WeatherKind.Clear);
                weather.NextWeather = -1;
                weather.QueuedWeather = -1;
                weather.Transition = 0f;
                weather.TransitionFactor = 0f;
                weather.Transitioning = 0;
                weather.HoursUntilNextChange = hoursBetweenChanges;
                weather.WeatherUpdateHoursRemaining = hoursBetweenChanges;
                weather.RegionHandleValue = regionHandleValue;
                weather.RandomState = random.state;
                weather.Initialized = 1;
            }

            ProcessForceRequests(ref weather, forceRequests, ref content);
            if (weather.ForcedWeather >= 0)
            {
                int forced = RuntimeContentBlobUtility.ClampWeatherIndex(ref content, weather.ForcedWeather);
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

            ProcessRegionOverrideRequests(ref weather, regionOverrideRequests, regionOverrides, regionWeather, ref content, regionHandleValue, ref random);
            ProcessChangeRequests(ref weather, changeRequests, regionWeather, ref content, regionHandleValue);

            if (interiorActive)
            {
                AdvanceWeatherUpdateTimer(ref weather, regionWeather, hoursBetweenChanges, time.LastAdvancedHours);
                AdvanceTransition(ref weather, ref content, SystemAPI.Time.DeltaTime, time.FastForwarding != 0, time.Paused != 0);
                weather.RandomState = random.state;
                return;
            }

            bool regionChanged = regionHandleValue != weather.RegionHandleValue;
            weather.RegionHandleValue = regionHandleValue;

            bool expiredWeather = AdvanceWeatherUpdateTimer(ref weather, regionWeather, hoursBetweenChanges, time.LastAdvancedHours);
            AdvanceTransition(ref weather, ref content, SystemAPI.Time.DeltaTime, time.FastForwarding != 0, time.Paused != 0);
            if (weather.Transitioning != 0)
                return;

            if (!regionChanged && !expiredWeather)
                return;

            int next = RuntimeContentBlobUtility.ClampWeatherIndex(ref content, GetRegionWeather(ref content, regionHandleValue, regionWeather, regionOverrides, ref random));
            weather.RandomState = random.state;
            AddWeatherTransition(ref weather, ref content, next);
        }

        int ResolveCurrentRegionHandle(ref SystemState systemState, ref RuntimeContentBlob content)
        {
            if (!SystemAPI.HasSingleton<StreamingConfig>())
                return 0;

            if (SystemAPI.HasSingleton<InteriorTransitionState>() && SystemAPI.GetSingleton<InteriorTransitionState>().InteriorActive != 0)
                return 0;

            var streaming = SystemAPI.GetSingleton<StreamingConfig>();
            var worldCellReference = SystemAPI.GetSingleton<RuntimeWorldCellBlobReference>();
            if (!worldCellReference.Blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][WorldCellBlob] Weather selection requires runtime world cell blob.");
            ref RuntimeWorldCellBlob worldCells = ref worldCellReference.Blob.Value;
            if (RuntimeWorldCellBlobUtility.TryGetExteriorCellIndex(ref worldCells, streaming.CameraCell, out int cellIndex))
            {
                ref RuntimeWorldCellDefBlob cell = ref RuntimeWorldCellBlobUtility.RequireCell(ref worldCells, cellIndex);
                if (cell.Environment.RegionIdHash != 0UL
                    && RuntimeContentBlobUtility.TryGetRegionHandleByIdHash(ref content, cell.Environment.RegionIdHash, out var regionHandle))
                return regionHandle.Value;
            }

            return 0;
        }

        static int SampleWeather(
            ref RuntimeContentBlob content,
            int regionHandleValue,
            DynamicBuffer<MorrowindRegionWeatherOverrideEntry> regionOverrides,
            ref Unity.Mathematics.Random random)
        {
            if (regionHandleValue <= 0)
                return MorrowindWeatherSelectionUtility.SampleFallbackExteriorWeather(ref random);

            if (TryGetRegionOverride(regionOverrides, regionHandleValue, out var weatherOverride))
            {
                return MorrowindWeatherSelectionUtility.SampleWeather(
                    weatherOverride.ClearChance,
                    weatherOverride.CloudyChance,
                    weatherOverride.FoggyChance,
                    weatherOverride.OvercastChance,
                    weatherOverride.RainChance,
                    weatherOverride.ThunderChance,
                    weatherOverride.AshChance,
                    weatherOverride.BlightChance,
                    weatherOverride.SnowChance,
                    weatherOverride.BlizzardChance,
                    ref random);
            }

            ref RuntimeRegionDefBlob region = ref RuntimeContentBlobUtility.Get(ref content, new RegionDefHandle { Value = regionHandleValue });
            return MorrowindWeatherSelectionUtility.SampleWeather(
                region.ClearChance,
                region.CloudyChance,
                region.FoggyChance,
                region.OvercastChance,
                region.RainChance,
                region.ThunderChance,
                region.AshChance,
                region.BlightChance,
                region.SnowChance,
                region.BlizzardChance,
                ref random);
        }

        static int GetRegionWeather(
            ref RuntimeContentBlob content,
            int regionHandleValue,
            DynamicBuffer<MorrowindRegionWeatherCacheEntry> regionWeather,
            DynamicBuffer<MorrowindRegionWeatherOverrideEntry> regionOverrides,
            ref Unity.Mathematics.Random random)
        {
            int region = math.max(0, regionHandleValue);
            for (int i = 0; i < regionWeather.Length; i++)
            {
                if (regionWeather[i].RegionHandleValue == region)
                    return regionWeather[i].Weather;
            }

            int weather = SampleWeather(ref content, region, regionOverrides, ref random);
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

        static void ProcessRegionOverrideRequests(
            ref MorrowindWeatherState weather,
            DynamicBuffer<MorrowindRegionWeatherOverrideRequest> requests,
            DynamicBuffer<MorrowindRegionWeatherOverrideEntry> regionOverrides,
            DynamicBuffer<MorrowindRegionWeatherCacheEntry> regionWeather,
            ref RuntimeContentBlob content,
            int activeRegion,
            ref Unity.Mathematics.Random random)
        {
            for (int i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                SetRegionOverride(regionOverrides, request);
                ClearRegionWeather(regionWeather, request.RegionHandleValue);
                if (request.RegionHandleValue != activeRegion || IsWeatherSupported(request, weather.CurrentWeather))
                    continue;

                int next = RuntimeContentBlobUtility.ClampWeatherIndex(ref content, SampleWeather(ref content, request.RegionHandleValue, regionOverrides, ref random));
                SetRegionWeather(regionWeather, request.RegionHandleValue, next);
                AddWeatherTransition(ref weather, ref content, next);
            }

            requests.Clear();
        }

        static void SetRegionOverride(
            DynamicBuffer<MorrowindRegionWeatherOverrideEntry> regionOverrides,
            in MorrowindRegionWeatherOverrideRequest request)
        {
            int region = math.max(0, request.RegionHandleValue);
            for (int i = 0; i < regionOverrides.Length; i++)
            {
                if (regionOverrides[i].RegionHandleValue != region)
                    continue;

                regionOverrides[i] = ToOverrideEntry(region, request);
                return;
            }

            regionOverrides.Add(ToOverrideEntry(region, request));
        }

        static MorrowindRegionWeatherOverrideEntry ToOverrideEntry(int regionHandleValue, in MorrowindRegionWeatherOverrideRequest request)
        {
            return new MorrowindRegionWeatherOverrideEntry
            {
                RegionHandleValue = regionHandleValue,
                ClearChance = request.ClearChance,
                CloudyChance = request.CloudyChance,
                FoggyChance = request.FoggyChance,
                OvercastChance = request.OvercastChance,
                RainChance = request.RainChance,
                ThunderChance = request.ThunderChance,
                AshChance = request.AshChance,
                BlightChance = request.BlightChance,
                SnowChance = request.SnowChance,
                BlizzardChance = request.BlizzardChance,
            };
        }

        static void ClearRegionWeather(DynamicBuffer<MorrowindRegionWeatherCacheEntry> regionWeather, int regionHandleValue)
        {
            int region = math.max(0, regionHandleValue);
            for (int i = regionWeather.Length - 1; i >= 0; i--)
            {
                if (regionWeather[i].RegionHandleValue == region)
                    regionWeather.RemoveAt(i);
            }
        }

        static bool TryGetRegionOverride(
            DynamicBuffer<MorrowindRegionWeatherOverrideEntry> regionOverrides,
            int regionHandleValue,
            out MorrowindRegionWeatherOverrideEntry weatherOverride)
        {
            int region = math.max(0, regionHandleValue);
            for (int i = 0; i < regionOverrides.Length; i++)
            {
                if (regionOverrides[i].RegionHandleValue == region)
                {
                    weatherOverride = regionOverrides[i];
                    return true;
                }
            }

            weatherOverride = default;
            return false;
        }

        static bool IsWeatherSupported(in MorrowindRegionWeatherOverrideRequest request, int weather)
        {
            return weather switch
            {
                (int)WeatherKind.Clear => request.ClearChance > 0,
                (int)WeatherKind.Cloudy => request.CloudyChance > 0,
                (int)WeatherKind.Foggy => request.FoggyChance > 0,
                (int)WeatherKind.Overcast => request.OvercastChance > 0,
                (int)WeatherKind.Rain => request.RainChance > 0,
                (int)WeatherKind.Thunderstorm => request.ThunderChance > 0,
                (int)WeatherKind.Ashstorm => request.AshChance > 0,
                (int)WeatherKind.Blight => request.BlightChance > 0,
                (int)WeatherKind.Snow => request.SnowChance > 0,
                (int)WeatherKind.Blizzard => request.BlizzardChance > 0,
                _ => false,
            };
        }

        static void ProcessForceRequests(ref MorrowindWeatherState weather, DynamicBuffer<MorrowindWeatherForceRequest> requests, ref RuntimeContentBlob content)
        {
            for (int i = 0; i < requests.Length; i++)
            {
                if (requests[i].Clear != 0)
                {
                    weather.ForcedWeather = -1;
                    continue;
                }

                weather.ForcedWeather = RuntimeContentBlobUtility.ClampWeatherIndex(ref content, requests[i].Weather);
            }

            requests.Clear();
        }

        static void ProcessChangeRequests(
            ref MorrowindWeatherState weather,
            DynamicBuffer<MorrowindWeatherChangeRequest> requests,
            DynamicBuffer<MorrowindRegionWeatherCacheEntry> regionWeather,
            ref RuntimeContentBlob content,
            int activeRegion)
        {
            for (int i = 0; i < requests.Length; i++)
            {
                int requested = RuntimeContentBlobUtility.ClampWeatherIndex(ref content, requests[i].Weather);
                SetRegionWeather(regionWeather, requests[i].RegionHandleValue, requested);
                if (requests[i].RegionHandleValue == activeRegion)
                    AddWeatherTransition(ref weather, ref content, requested);
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

        static void AddWeatherTransition(ref MorrowindWeatherState weather, ref RuntimeContentBlob content, int weatherIndex)
        {
            int next = RuntimeContentBlobUtility.ClampWeatherIndex(ref content, weatherIndex);
            if (weather.NextWeather < 0 && next != weather.CurrentWeather)
            {
                weather.NextWeather = next;
                weather.QueuedWeather = -1;
                weather.TransitionFactor = 1f;
                weather.Transition = 0f;
                weather.TransitionDelta = RuntimeContentBlobUtility.ResolveWeatherTransitionDelta(ref content, next);
                weather.Transitioning = 1;
                return;
            }

            if (weather.NextWeather >= 0 && next != weather.NextWeather)
                weather.QueuedWeather = next;
        }

        static void AdvanceTransition(ref MorrowindWeatherState weather, ref RuntimeContentBlob content, float elapsedSeconds, bool fastForward, bool paused)
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
                weather.CurrentWeather = RuntimeContentBlobUtility.ClampWeatherIndex(ref content, weather.QueuedWeather >= 0 ? weather.QueuedWeather : weather.NextWeather);
                weather.NextWeather = -1;
                weather.QueuedWeather = -1;
                weather.TransitionFactor = 0f;
                weather.Transition = 0f;
                weather.Transitioning = 0;
                return;
            }

            if (paused)
                return;

            weather.TransitionFactor -= math.max(0f, elapsedSeconds) * math.max(0.001f, weather.TransitionDelta);
            if (weather.TransitionFactor <= 0f)
            {
                weather.CurrentWeather = RuntimeContentBlobUtility.ClampWeatherIndex(ref content, weather.NextWeather);
                weather.NextWeather = weather.QueuedWeather;
                weather.QueuedWeather = -1;
                if (weather.NextWeather >= 0)
                {
                    weather.TransitionFactor = 1f;
                    weather.TransitionDelta = RuntimeContentBlobUtility.ResolveWeatherTransitionDelta(ref content, weather.NextWeather);
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

        static uint EnsureSeed(uint seed) => seed == 0u ? 1u : seed;
    }

    [UpdateInGroup(typeof(MorrowindEnvironmentSystemGroup))]
    [UpdateAfter(typeof(MorrowindWeatherSelectionSystem))]
    [UpdateBefore(typeof(LightingEnvironmentResolveSystem))]
    public partial struct MorrowindSkyWeatherResolveSystem : ISystem
    {
        public void OnCreate(ref SystemState systemState)
        {
            systemState.RequireForUpdate<MorrowindTimeState>();
            systemState.RequireForUpdate<MorrowindDayCycleState>();
            systemState.RequireForUpdate<MorrowindWeatherState>();
            systemState.RequireForUpdate<ActiveSkyWeatherState>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            var contentBlobReference = SystemAPI.GetSingleton<RuntimeContentBlobReference>();
            if (!contentBlobReference.Blob.IsCreated)
                throw new System.InvalidOperationException("[VVardenfell][ContentBlob] Sky weather resolve requires runtime content blob.");
            ref RuntimeContentBlob content = ref contentBlobReference.Blob.Value;
            var time = SystemAPI.GetSingleton<MorrowindTimeState>();
            var settings = SystemAPI.GetSingleton<MorrowindDayCycleState>();
            ref var weather = ref SystemAPI.GetSingletonRW<MorrowindWeatherState>().ValueRW;
            ref var sky = ref SystemAPI.GetSingletonRW<ActiveSkyWeatherState>().ValueRW;

            WeatherDefinitionDef current = ResolveWeather(ref content, weather.CurrentWeather);
            int nextIndex = weather.NextWeather >= 0 ? weather.NextWeather : -1;
            WeatherDefinitionDef next = nextIndex >= 0 ? ResolveWeather(ref content, nextIndex) : current;
            WeatherSettingsDef weatherSettings = content.WeatherSettings;
            var blended = MorrowindWeatherEvaluationUtility.Evaluate(settings, weatherSettings, current, weather.CurrentWeather, next, nextIndex, weather.Transition, time.GameHour);
            var eval = blended.Evaluation;
            WeatherDefinitionDef dominant = blended.DominantWeather;
            var masser = MorrowindDayCycleUtility.EvaluateMoon(weatherSettings.MasserMoon, time.DaysPassed, time.GameHour);
            var secunda = MorrowindDayCycleUtility.EvaluateMoon(weatherSettings.SecundaMoon, time.DaysPassed, time.GameHour);

            ResolveThunder(ref systemState, ref weather, current, next, blended, time.Paused != 0);

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

        static WeatherDefinitionDef ResolveWeather(ref RuntimeContentBlob content, int index)
        {
            if ((uint)index >= (uint)content.WeatherDefinitions.Length)
                throw new System.InvalidOperationException($"[VVardenfell][ContentBlob] Invalid weather definition index {index}; length {content.WeatherDefinitions.Length}.");
            return RuntimeContentBlobUtility.RequireWeatherDefinition(ref content, index);
        }

        static float3 ResolveStormDirection(in WeatherDefinitionDef weather)
        {
            if (weather.Kind == WeatherKind.Ashstorm || weather.Kind == WeatherKind.Blight)
                return math.normalize(new float3(-1f, 0f, -0.35f));
            return math.normalize(new float3(1f, 0f, 0.35f));
        }

        void ResolveThunder(ref SystemState systemState, ref MorrowindWeatherState weather, in WeatherDefinitionDef current, in WeatherDefinitionDef next, in MorrowindWeatherEvaluationUtility.WeatherBlend blend, bool paused)
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
