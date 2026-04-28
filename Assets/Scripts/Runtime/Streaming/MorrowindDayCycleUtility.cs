using Unity.Mathematics;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Streaming
{
    public static class MorrowindDayCycleUtility
    {
        public struct Evaluation
        {
            public float SunPercent;
            public float3 SunDirectionToLight;
            public float3 AmbientColorRgb;
            public float3 SunColorRgb;
            public float3 FogColorRgb;
            public float FogDensity;
        }

        public static Evaluation Evaluate(in MorrowindDayCycleState state)
        {
            float gameHour = NormalizeGameHour(state.GameHour);

            return new Evaluation
            {
                SunPercent = ComputeSunPercent(gameHour, state.SunriseTime, state.SunriseDuration, state.SunsetTime, state.SunsetDuration),
                SunDirectionToLight = ComputeSunDirectionToLight(gameHour, state.SunriseTime, state.SunsetTime, state.SunsetDuration),
                AmbientColorRgb = EvaluateColor(
                    gameHour,
                    state,
                    state.AmbientPreSunriseTime,
                    state.AmbientPostSunriseTime,
                    state.AmbientPreSunsetTime,
                    state.AmbientPostSunsetTime,
                    state.AmbientSunriseColorRgb,
                    state.AmbientDayColorRgb,
                    state.AmbientSunsetColorRgb,
                    state.AmbientNightColorRgb),
                SunColorRgb = EvaluateColor(
                    gameHour,
                    state,
                    state.SunPreSunriseTime,
                    state.SunPostSunriseTime,
                    state.SunPreSunsetTime,
                    state.SunPostSunsetTime,
                    state.SunSunriseColorRgb,
                    state.SunDayColorRgb,
                    state.SunSunsetColorRgb,
                    state.SunNightColorRgb),
                FogColorRgb = EvaluateColor(
                    gameHour,
                    state,
                    state.FogPreSunriseTime,
                    state.FogPostSunriseTime,
                    state.FogPreSunsetTime,
                    state.FogPostSunsetTime,
                    state.FogSunriseColorRgb,
                    state.FogDayColorRgb,
                    state.FogSunsetColorRgb,
                    state.FogNightColorRgb),
                FogDensity = EvaluateFloat(
                    gameHour,
                    state,
                    state.FogPreSunriseTime,
                    state.FogPostSunriseTime,
                    state.FogPreSunsetTime,
                    state.FogPostSunsetTime,
                    state.ExteriorDayFogDensity,
                    state.ExteriorDayFogDensity,
                    state.ExteriorDayFogDensity,
                    state.ExteriorNightFogDensity),
            };
        }

        public static float NormalizeGameHour(float gameHour)
        {
            float normalized = gameHour % 24f;
            return normalized < 0f ? normalized + 24f : normalized;
        }

        public static void AdvanceHours(ref MorrowindDayCycleState state, float hours)
        {
            if (!(hours > 0f))
                return;

            float totalHours = NormalizeGameHour(state.GameHour) + hours;
            int elapsedDays = (int)math.floor(totalHours / 24f);

            state.GameHour = totalHours - elapsedDays * 24f;
            if (elapsedDays > 0)
                state.DaysPassed += elapsedDays;
        }

        public static float ComputeSunPercent(float gameHour, float sunriseTime, float sunriseDuration, float sunsetTime, float sunsetDuration)
        {
            float hour = NormalizeGameHour(gameHour);
            float dayStart = sunriseTime + math.max(0.0001f, sunriseDuration);
            float nightStart = sunsetTime + math.max(0.0001f, sunsetDuration);

            if (hour <= sunriseTime || hour >= nightStart)
                return 0f;

            if (hour <= dayStart)
                return math.saturate((hour - sunriseTime) / math.max(0.0001f, sunriseDuration));

            if (hour > sunsetTime)
                return math.saturate(1f - ((hour - sunsetTime) / math.max(0.0001f, sunsetDuration)));

            return 1f;
        }

        public static float3 ComputeSunDirectionToLight(float gameHour, float sunriseTime, float sunsetTime, float sunsetDuration)
        {
            float hour = NormalizeGameHour(gameHour);
            float adjustedHour = hour;
            float nightStart = sunsetTime + sunsetDuration;
            float adjustedNightStart = nightStart;

            if (hour < sunriseTime)
                adjustedHour += 24f;
            if (nightStart < sunriseTime)
                adjustedNightStart += 24f;

            float dayDuration = math.max(0.0001f, adjustedNightStart - sunriseTime);
            float nightDuration = math.max(0.0001f, 24f - dayDuration);
            float angle;
            if (adjustedHour < adjustedNightStart)
            {
                float t = (adjustedHour - sunriseTime) / dayDuration;
                angle = t * math.PI;
            }
            else
            {
                float t = (adjustedHour - adjustedNightStart) / nightDuration;
                angle = math.PI + t * math.PI;
            }

            return new float3(math.cos(angle), math.sin(angle), 0f);
        }

        static float3 EvaluateColor(
            float gameHour,
            in MorrowindDayCycleState state,
            float preSunriseTime,
            float postSunriseTime,
            float preSunsetTime,
            float postSunsetTime,
            float3 sunrise,
            float3 day,
            float3 sunset,
            float3 night)
        {
            float nightEnd = state.SunriseTime;
            float dayStart = state.SunriseTime + state.SunriseDuration;
            float dayEnd = state.SunsetTime;
            float nightStart = state.SunsetTime + state.SunsetDuration;

            if (gameHour < nightEnd - preSunriseTime || gameHour > nightStart + postSunsetTime)
                return night;

            if (gameHour >= nightEnd - preSunriseTime && gameHour <= dayStart + postSunriseTime)
            {
                float duration = dayStart + postSunriseTime - nightEnd + preSunriseTime;
                float middle = nightEnd - preSunriseTime + duration * 0.5f;

                if (gameHour <= middle)
                {
                    float factor = duration > 0f ? (middle - gameHour) / duration * 2f : 0f;
                    return math.lerp(sunrise, night, factor);
                }

                float fadeOutFactor = duration > 0f ? (gameHour - middle) / duration * 2f : 1f;
                return math.lerp(sunrise, day, fadeOutFactor);
            }

            if (gameHour > dayStart + postSunriseTime && gameHour < dayEnd - preSunsetTime)
                return day;

            if (gameHour >= dayEnd - preSunsetTime && gameHour <= nightStart + postSunsetTime)
            {
                float duration = nightStart + postSunsetTime - dayEnd + preSunsetTime;
                float middle = dayEnd - preSunsetTime + duration * 0.5f;

                if (gameHour <= middle)
                {
                    float factor = duration > 0f ? (middle - gameHour) / duration * 2f : 0f;
                    return math.lerp(sunset, day, factor);
                }

                float fadeOutFactor = duration > 0f ? (gameHour - middle) / duration * 2f : 1f;
                return math.lerp(sunset, night, fadeOutFactor);
            }

            return night;
        }

        static float EvaluateFloat(
            float gameHour,
            in MorrowindDayCycleState state,
            float preSunriseTime,
            float postSunriseTime,
            float preSunsetTime,
            float postSunsetTime,
            float sunrise,
            float day,
            float sunset,
            float night)
        {
            float nightEnd = state.SunriseTime;
            float dayStart = state.SunriseTime + state.SunriseDuration;
            float dayEnd = state.SunsetTime;
            float nightStart = state.SunsetTime + state.SunsetDuration;

            if (gameHour < nightEnd - preSunriseTime || gameHour > nightStart + postSunsetTime)
                return night;

            if (gameHour >= nightEnd - preSunriseTime && gameHour <= dayStart + postSunriseTime)
            {
                float duration = dayStart + postSunriseTime - nightEnd + preSunriseTime;
                float middle = nightEnd - preSunriseTime + duration * 0.5f;

                if (gameHour <= middle)
                {
                    float factor = duration > 0f ? (middle - gameHour) / duration * 2f : 0f;
                    return math.lerp(sunrise, night, factor);
                }

                float fadeOutFactor = duration > 0f ? (gameHour - middle) / duration * 2f : 1f;
                return math.lerp(sunrise, day, fadeOutFactor);
            }

            if (gameHour > dayStart + postSunriseTime && gameHour < dayEnd - preSunsetTime)
                return day;

            if (gameHour >= dayEnd - preSunsetTime && gameHour <= nightStart + postSunsetTime)
            {
                float duration = nightStart + postSunsetTime - dayEnd + preSunsetTime;
                float middle = dayEnd - preSunsetTime + duration * 0.5f;

                if (gameHour <= middle)
                {
                    float factor = duration > 0f ? (middle - gameHour) / duration * 2f : 0f;
                    return math.lerp(sunset, day, factor);
                }

                float fadeOutFactor = duration > 0f ? (gameHour - middle) / duration * 2f : 1f;
                return math.lerp(sunset, night, fadeOutFactor);
            }

            return night;
        }
    }
}
