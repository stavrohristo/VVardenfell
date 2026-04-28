using Unity.Mathematics;
using VVardenfell.Core;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Streaming
{
    public static class MorrowindWeatherEvaluationUtility
    {
        public struct WeatherBlend
        {
            public MorrowindDayCycleUtility.Evaluation Evaluation;
            public WeatherDefinitionDef DominantWeather;
            public int DominantWeatherIndex;
            public float BlendFactor;
            public float CloudBlendFactor;
            public float GlareView;
            public float CloudOpacity;
            public float CloudSpeed;
            public float WindSpeed;
            public float PrecipitationAlpha;
            public float PrecipitationIntensity;
            public float ThunderCurrentRatio;
            public float ThunderNextRatio;
        }

        public static WeatherBlend Evaluate(
            in MorrowindDayCycleState settings,
            in WeatherSettingsDef weatherSettings,
            in WeatherDefinitionDef current,
            int currentIndex,
            in WeatherDefinitionDef next,
            int nextIndex,
            float transitionBlend,
            float gameHour)
        {
            float blend = nextIndex >= 0 ? math.saturate(transitionBlend) : 0f;
            var currentEval = MorrowindDayCycleUtility.EvaluateWeather(settings, weatherSettings, current, gameHour);
            var nextEval = nextIndex >= 0
                ? MorrowindDayCycleUtility.EvaluateWeather(settings, weatherSettings, next, gameHour)
                : currentEval;
            var eval = MorrowindDayCycleUtility.Lerp(currentEval, nextEval, blend);

            float precipitationAlpha = ResolvePrecipitationAlpha(current, next, nextIndex >= 0, blend, out bool useNextPrecip);
            WeatherDefinitionDef dominant = useNextPrecip ? next : current;
            int dominantIndex = useNextPrecip ? nextIndex : currentIndex;
            float precipitation = dominant.UsingPrecip != 0 ? precipitationAlpha : 0f;

            return new WeatherBlend
            {
                Evaluation = eval,
                DominantWeather = dominant,
                DominantWeatherIndex = dominantIndex,
                BlendFactor = blend,
                CloudBlendFactor = nextIndex >= 0 ? CloudBlendFactor(next, blend) : 0f,
                GlareView = ResolveGlareView(current, next, nextIndex >= 0, blend),
                CloudOpacity = math.lerp(current.CloudsMaximumPercent, nextIndex >= 0 ? next.CloudsMaximumPercent : current.CloudsMaximumPercent, blend),
                CloudSpeed = math.lerp(current.CloudSpeed, nextIndex >= 0 ? next.CloudSpeed : current.CloudSpeed, blend),
                WindSpeed = math.lerp(ResolveWindSpeed(current), ResolveWindSpeed(nextIndex >= 0 ? next : current), blend),
                PrecipitationAlpha = precipitationAlpha,
                PrecipitationIntensity = precipitation * math.max(0f, settings.PrecipitationIntensityScale),
                ThunderCurrentRatio = nextIndex >= 0 ? 1f - blend : 1f,
                ThunderNextRatio = nextIndex >= 0 ? blend : 0f,
            };
        }

        public static float CloudBlendFactor(in WeatherDefinitionDef next, float transitionBlend)
            => math.saturate(transitionBlend / math.max(0.0001f, next.CloudsMaximumPercent));

        public static float ResolveGlareView(in WeatherDefinitionDef current, in WeatherDefinitionDef next, bool inTransition, float transitionBlend)
        {
            if (!inTransition)
                return current.GlareView;

            if (transitionBlend < next.CloudsMaximumPercent)
            {
                float t = transitionBlend / math.max(0.0001f, next.CloudsMaximumPercent);
                return math.lerp(current.GlareView, next.GlareView, math.saturate(t));
            }

            return next.GlareView;
        }

        public static float ResolvePrecipitationAlpha(
            in WeatherDefinitionDef current,
            in WeatherDefinitionDef next,
            bool inTransition,
            float transitionBlend,
            out bool useNext)
        {
            useNext = false;
            if (!inTransition)
                return current.UsingPrecip != 0 ? 1f : 0f;

            float threshold = next.RainThreshold <= 0f ? 0.5f : math.saturate(next.RainThreshold);
            if (transitionBlend < threshold)
                return current.UsingPrecip != 0 ? math.saturate(1f - transitionBlend / math.max(0.0001f, threshold)) : 0f;

            useNext = true;
            return next.UsingPrecip != 0 ? math.saturate((transitionBlend - threshold) / math.max(0.0001f, 1f - threshold)) : 0f;
        }

        public static float ResolveWindSpeed(in WeatherDefinitionDef weather)
        {
            float target = math.min(8f * math.max(0f, weather.WindSpeed), 70f);
            return weather.UsingPrecip != 0 ? target * 0.5f : target;
        }

        public static int ResolvePrecipitationKind(in WeatherDefinitionDef weather)
            => weather.UsingPrecip != 0 ? (int)weather.Kind : (int)WeatherKind.Clear;

        public static float ResolveOpenMwRainEmissionRate(float maxRaindrops, float rainEntranceSpeed)
            => math.max(0f, maxRaindrops) / math.max(0.1f, rainEntranceSpeed) * 20f;

        public static float ResolveOpenMwRainAlpha(float precipitationAlpha)
            => math.saturate(precipitationAlpha) * 0.6f;

        public static float ResolveRainPresentationWorldScale(float worldScale)
            => worldScale > 0f ? worldScale : WorldScale.MwUnitsToMeters;

        public static float3 ResolveOpenMwRainVelocity(float rainSpeed, float windSpeed, float worldScale)
        {
            float scaledRainSpeed = rainSpeed > 100f
                ? rainSpeed * ResolveRainPresentationWorldScale(worldScale)
                : rainSpeed;
            scaledRainSpeed = math.max(4f, scaledRainSpeed);
            float angle = -math.atan(math.max(0f, windSpeed) / 50f);
            return new float3(0f, scaledRainSpeed * math.sin(angle), -scaledRainSpeed / math.max(0.001f, math.cos(angle)));
        }

        public static float ResolveRainPresentationSize(float worldScale, float particleSizeScale)
            => math.max(0.025f, 10f * ResolveRainPresentationWorldScale(worldScale) * math.max(0.01f, particleSizeScale));

        public static void ResolveOpenMwRainSizeRange(float worldScale, float particleSizeScale, out float minSize, out float maxSize)
        {
            float scale = ResolveRainPresentationWorldScale(worldScale) * math.max(0.01f, particleSizeScale);
            minSize = math.max(0.02f, 5f * scale);
            maxSize = math.max(minSize, 15f * scale);
        }

        public static float ResolveRainPresentationRange(float morrowindUnits, float worldScale, float fallbackMeters)
            => morrowindUnits > 0f ? morrowindUnits * ResolveRainPresentationWorldScale(worldScale) : fallbackMeters;

        public static bool TryResolveThunder(
            in WeatherDefinitionDef weather,
            float transitionRatio,
            float elapsedSeconds,
            bool paused,
            ref Unity.Mathematics.Random random,
            out int soundIndex,
            out float flash)
        {
            soundIndex = 0;
            flash = 0f;
            if (paused || weather.Kind != WeatherKind.Thunderstorm || weather.ThunderFrequency <= 0f)
                return false;

            if (transitionRatio < weather.ThunderThreshold)
                return false;

            float scale = (transitionRatio - weather.ThunderThreshold) / math.max(0.0001f, 1f - weather.ThunderThreshold);
            float chance = ((weather.ThunderFrequency * 10f) / 60f) * math.max(0f, elapsedSeconds) * math.saturate(scale);
            if (random.NextFloat() > chance)
                return false;

            soundIndex = random.NextInt(4);
            flash = 1f - soundIndex * 0.25f;
            return true;
        }
    }
}
