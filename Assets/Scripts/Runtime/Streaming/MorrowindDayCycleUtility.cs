using System;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;

namespace VVardenfell.Runtime.Streaming
{
    public static class MorrowindDayCycleUtility
    {
        static readonly int[] k_DaysPerMonth = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

        public struct Evaluation
        {
            public float SunPercent;
            public float3 SunDirectionToLight;
            public float3 SkySunWorldDirection;
            public float3 AmbientColorRgb;
            public float3 SunColorRgb;
            public float3 FogColorRgb;
            public float3 SkyColorRgb;
            public float3 SunDiscColorRgb;
            public float FogDepth;
            public float MoonOpacity;
            public float StarOpacity;
            public byte IsNight;
        }

        public struct MoonEvaluation
        {
            public float3 WorldDirection;
            public float Alpha;
            public float ShadowBlend;
            public float Size;
            public int Phase;
            public float AngleFromHorizonDegrees;
        }

        public static Evaluation EvaluateClear(in MorrowindDayCycleState settings, float gameHour)
        {
            var clear = CreateFallbackClearWeather();
            var weatherSettings = CreateFallbackWeatherSettings(settings);
            return EvaluateWeather(settings, weatherSettings, clear, gameHour);
        }

        public static Evaluation EvaluateWeather(
            in MorrowindDayCycleState settings,
            in WeatherSettingsDef weatherSettings,
            in WeatherDefinitionDef weather,
            float gameHour)
        {
            float hour = NormalizeGameHour(gameHour);
            float sunPercent = ComputeSunPercent(hour, settings.SunriseTime, settings.SunriseDuration, settings.SunsetTime, settings.SunsetDuration);
            float3 skySunDirection = ComputeSunDirectionToLight(hour, settings.SunriseTime, settings.SunsetTime, settings.SunsetDuration);

            return new Evaluation
            {
                SunPercent = sunPercent,
                SunDirectionToLight = skySunDirection,
                SkySunWorldDirection = skySunDirection,
                AmbientColorRgb = EvaluateColor(hour, settings, weather.AmbientColor, settings.AmbientPreSunriseTime, settings.AmbientPostSunriseTime, settings.AmbientPreSunsetTime, settings.AmbientPostSunsetTime),
                SunColorRgb = EvaluateColor(hour, settings, weather.SunColor, settings.SunPreSunriseTime, settings.SunPostSunriseTime, settings.SunPreSunsetTime, settings.SunPostSunsetTime),
                FogColorRgb = EvaluateColor(hour, settings, weather.FogColor, settings.FogPreSunriseTime, settings.FogPostSunriseTime, settings.FogPreSunsetTime, settings.FogPostSunsetTime),
                SkyColorRgb = EvaluateColor(hour, settings, weather.SkyColor, settings.SkyPreSunriseTime, settings.SkyPostSunriseTime, settings.SkyPreSunsetTime, settings.SkyPostSunsetTime),
                SunDiscColorRgb = EvaluateSunDiscColor(hour, settings, weather),
                FogDepth = EvaluateFloat(hour, settings, weather.LandFogDayDepth, weather.LandFogDayDepth, weather.LandFogDayDepth, weather.LandFogNightDepth, settings.FogPreSunriseTime, settings.FogPostSunriseTime, settings.FogPreSunsetTime, settings.FogPostSunsetTime),
                MoonOpacity = ComputeMoonOpacity(hour, settings, weatherSettings) * math.max(0f, settings.MoonMaxOpacity),
                StarOpacity = ComputeStarOpacity(hour, settings, weatherSettings) * math.max(0f, settings.StarMaxOpacity),
                IsNight = sunPercent <= 0f ? (byte)1 : (byte)0,
            };
        }

        public static Evaluation Lerp(in Evaluation from, in Evaluation to, float t)
        {
            t = math.saturate(t);
            return new Evaluation
            {
                SunPercent = math.lerp(from.SunPercent, to.SunPercent, t),
                SunDirectionToLight = math.normalizesafe(math.lerp(from.SunDirectionToLight, to.SunDirectionToLight, t), from.SunDirectionToLight),
                SkySunWorldDirection = math.normalizesafe(math.lerp(from.SkySunWorldDirection, to.SkySunWorldDirection, t), from.SkySunWorldDirection),
                AmbientColorRgb = math.lerp(from.AmbientColorRgb, to.AmbientColorRgb, t),
                SunColorRgb = math.lerp(from.SunColorRgb, to.SunColorRgb, t),
                FogColorRgb = math.lerp(from.FogColorRgb, to.FogColorRgb, t),
                SkyColorRgb = math.lerp(from.SkyColorRgb, to.SkyColorRgb, t),
                SunDiscColorRgb = math.lerp(from.SunDiscColorRgb, to.SunDiscColorRgb, t),
                FogDepth = math.lerp(from.FogDepth, to.FogDepth, t),
                MoonOpacity = math.lerp(from.MoonOpacity, to.MoonOpacity, t),
                StarOpacity = math.lerp(from.StarOpacity, to.StarOpacity, t),
                IsNight = t < 0.5f ? from.IsNight : to.IsNight,
            };
        }

        public static float NormalizeGameHour(float gameHour)
        {
            float normalized = gameHour % 24f;
            return normalized < 0f ? normalized + 24f : normalized;
        }

        public static void AdvanceHours(ref MorrowindTimeState state, float hours)
        {
            state.LastAdvancedHours = math.max(0f, hours);
            if (!(hours > 0f))
                return;

            float totalHours = NormalizeGameHour(state.GameHour) + hours;
            int elapsedDays = (int)math.floor(totalHours / 24f);
            state.GameHour = totalHours - elapsedDays * 24f;
            if (elapsedDays <= 0)
                return;

            state.DaysPassed += elapsedDays;
            SetDay(ref state, state.Day + elapsedDays);
        }

        public static int GetDaysPerMonth(int month)
        {
            int normalized = math.clamp(month, 0, 11);
            return k_DaysPerMonth[normalized];
        }

        public static void SetDay(ref MorrowindTimeState state, int day)
        {
            if (day < 1)
                day = 1;

            int month = math.clamp(state.Month, 0, 11);
            while (day > GetDaysPerMonth(month))
            {
                day -= GetDaysPerMonth(month);
                if (month < 11)
                {
                    month++;
                }
                else
                {
                    month = 0;
                    state.Year++;
                }
            }

            state.Day = day;
            state.Month = month;
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

        public static MoonEvaluation EvaluateMoon(in MoonSettingsDef settings, int gameDay, float gameHour)
        {
            var moon = NormalizeMoonSettings(settings);
            float hour = NormalizeGameHour(gameHour);
            float angle = MoonAngle(moon, math.max(1, gameDay), hour);
            float axisRadians = math.radians(moon.AxisOffset);
            float angleRadians = math.radians(angle);
            float3 direction = math.mul(quaternion.AxisAngle(new float3(1f, 0f, 0f), -angleRadians), new float3(0f, 0f, 1f));
            direction = math.mul(quaternion.AxisAngle(new float3(0f, 1f, 0f), axisRadians), direction);
            return new MoonEvaluation
            {
                WorldDirection = math.normalizesafe(direction, new float3(0f, 0f, 1f)),
                Alpha = EarlyMoonShadowAlpha(moon, angle) * MoonHourlyAlpha(moon, hour),
                ShadowBlend = MoonShadowBlend(moon, angle),
                Size = math.max(0.01f, moon.Size),
                Phase = MoonPhase(moon, math.max(1, gameDay), hour),
                AngleFromHorizonDegrees = angle,
            };
        }

        public static MoonEvaluation EvaluateFallbackMasserMoon(int gameDay, float gameHour)
            => EvaluateMoon(CreateFallbackMasserMoon(), gameDay, gameHour);

        public static MoonEvaluation EvaluateFallbackSecundaMoon(int gameDay, float gameHour)
            => EvaluateMoon(CreateFallbackSecundaMoon(), gameDay, gameHour);

        public static float ComputeStarRotationDegrees(int daysPassed, float gameHour)
        {
            float totalHours = math.max(0f, daysPassed * 24f + NormalizeGameHour(gameHour));
            return math.fmod(totalHours * 360f / 96f, 360f);
        }

        public static float ComputeCloudUvOffset(int daysPassed, float gameHour, float cloudSpeed)
        {
            float totalGameSeconds = math.max(0f, daysPassed * 24f + NormalizeGameHour(gameHour)) * 3600f;
            return totalGameSeconds * math.max(0f, cloudSpeed) / 400f;
        }

        public static float3 DecodeRgb(int value)
        {
            uint rgba = unchecked((uint)value);
            return new float3(
                (rgba & 0xFFu) / 255f,
                ((rgba >> 8) & 0xFFu) / 255f,
                ((rgba >> 16) & 0xFFu) / 255f);
        }

        public static WeatherDefinitionDef CreateFallbackClearWeather()
        {
            return new WeatherDefinitionDef
            {
                Kind = WeatherKind.Clear,
                Id = "Clear",
                SkyColor = new WeatherColorSetDef { SunriseRgba = Rgb(117, 141, 164), DayRgba = Rgb(95, 135, 203), SunsetRgba = Rgb(56, 89, 129), NightRgba = Rgb(9, 10, 11) },
                FogColor = new WeatherColorSetDef { SunriseRgba = Rgb(255, 189, 157), DayRgba = Rgb(206, 227, 255), SunsetRgba = Rgb(255, 189, 157), NightRgba = Rgb(9, 10, 11) },
                AmbientColor = new WeatherColorSetDef { SunriseRgba = Rgb(47, 66, 96), DayRgba = Rgb(137, 140, 160), SunsetRgba = Rgb(68, 75, 96), NightRgba = Rgb(32, 35, 42) },
                SunColor = new WeatherColorSetDef { SunriseRgba = Rgb(242, 159, 119), DayRgba = Rgb(255, 252, 238), SunsetRgba = Rgb(255, 114, 79), NightRgba = Rgb(59, 97, 176) },
                SunDiscSunsetColorRgba = Rgb(255, 189, 157),
                LandFogDayDepth = 0.69f,
                LandFogNightDepth = 0.69f,
                WindSpeed = 0.1f,
                CloudSpeed = 1.25f,
                GlareView = 1f,
                CloudsMaximumPercent = 1f,
                TransitionDelta = 0.015f,
            };
        }

        public static WeatherSettingsDef CreateFallbackWeatherSettings(in MorrowindDayCycleState settings)
        {
            return new WeatherSettingsDef
            {
                SunriseTime = settings.SunriseTime,
                SunsetTime = settings.SunsetTime,
                SunriseDuration = settings.SunriseDuration,
                SunsetDuration = settings.SunsetDuration,
                HoursBetweenWeatherChanges = 20f,
                SunPreSunriseTime = settings.SunPreSunriseTime,
                SunPostSunriseTime = settings.SunPostSunriseTime,
                SunPreSunsetTime = settings.SunPreSunsetTime,
                SunPostSunsetTime = settings.SunPostSunsetTime,
                AmbientPreSunriseTime = settings.AmbientPreSunriseTime,
                AmbientPostSunriseTime = settings.AmbientPostSunriseTime,
                AmbientPreSunsetTime = settings.AmbientPreSunsetTime,
                AmbientPostSunsetTime = settings.AmbientPostSunsetTime,
                FogPreSunriseTime = settings.FogPreSunriseTime,
                FogPostSunriseTime = settings.FogPostSunriseTime,
                FogPreSunsetTime = settings.FogPreSunsetTime,
                FogPostSunsetTime = settings.FogPostSunsetTime,
                SkyPreSunriseTime = settings.AmbientPreSunriseTime,
                SkyPostSunriseTime = settings.AmbientPostSunriseTime,
                SkyPreSunsetTime = settings.AmbientPreSunsetTime,
                SkyPostSunsetTime = settings.AmbientPostSunsetTime,
                StarsPostSunsetStart = 1f,
                StarsPreSunriseFinish = 2f,
                StarsFadingDuration = 2f,
                MasserMoon = CreateFallbackMasserMoon(),
                SecundaMoon = CreateFallbackSecundaMoon(),
            };
        }

        public static MoonSettingsDef CreateFallbackMasserMoon()
        {
            return new MoonSettingsDef
            {
                Size = 55f,
                AxisOffset = 35f,
                Speed = 0.5f,
                DailyIncrement = 1f,
                FadeStartAngle = 50f,
                FadeEndAngle = 40f,
                MoonShadowEarlyFadeAngle = 0.5f,
                FadeInStart = 14f,
                FadeInFinish = 15f,
                FadeOutStart = 7f,
                FadeOutFinish = 10f,
            };
        }

        public static MoonSettingsDef CreateFallbackSecundaMoon()
        {
            return new MoonSettingsDef
            {
                Size = 20f,
                AxisOffset = 50f,
                Speed = 0.6f,
                DailyIncrement = 1.2f,
                FadeStartAngle = 50f,
                FadeEndAngle = 30f,
                MoonShadowEarlyFadeAngle = 0.5f,
                FadeInStart = 14f,
                FadeInFinish = 15f,
                FadeOutStart = 7f,
                FadeOutFinish = 10f,
            };
        }

        static float3 EvaluateColor(float gameHour, in MorrowindDayCycleState settings, WeatherColorSetDef colors, float preSunriseTime, float postSunriseTime, float preSunsetTime, float postSunsetTime)
            => EvaluateFloat3(gameHour, settings, DecodeRgb(colors.SunriseRgba), DecodeRgb(colors.DayRgba), DecodeRgb(colors.SunsetRgba), DecodeRgb(colors.NightRgba), preSunriseTime, postSunriseTime, preSunsetTime, postSunsetTime);

        static float3 EvaluateSunDiscColor(float gameHour, in MorrowindDayCycleState settings, in WeatherDefinitionDef weather)
        {
            float3 normal = EvaluateColor(gameHour, settings, weather.SunColor, settings.SunPreSunriseTime, settings.SunPostSunriseTime, settings.SunPreSunsetTime, settings.SunPostSunsetTime);
            float sunsetWeight = 1f - math.abs(NormalizeGameHour(gameHour) - settings.SunsetTime) / math.max(0.0001f, settings.SunsetDuration);
            return math.lerp(normal, DecodeRgb(weather.SunDiscSunsetColorRgba), math.saturate(sunsetWeight));
        }

        static float3 EvaluateFloat3(float gameHour, in MorrowindDayCycleState settings, float3 sunrise, float3 day, float3 sunset, float3 night, float preSunriseTime, float postSunriseTime, float preSunsetTime, float postSunsetTime)
        {
            float valueX = EvaluateFloat(gameHour, settings, sunrise.x, day.x, sunset.x, night.x, preSunriseTime, postSunriseTime, preSunsetTime, postSunsetTime);
            float valueY = EvaluateFloat(gameHour, settings, sunrise.y, day.y, sunset.y, night.y, preSunriseTime, postSunriseTime, preSunsetTime, postSunsetTime);
            float valueZ = EvaluateFloat(gameHour, settings, sunrise.z, day.z, sunset.z, night.z, preSunriseTime, postSunriseTime, preSunsetTime, postSunsetTime);
            return new float3(valueX, valueY, valueZ);
        }

        static float EvaluateFloat(float gameHour, in MorrowindDayCycleState settings, float sunrise, float day, float sunset, float night, float preSunriseTime, float postSunriseTime, float preSunsetTime, float postSunsetTime)
        {
            float nightEnd = settings.SunriseTime;
            float dayStart = settings.SunriseTime + settings.SunriseDuration;
            float dayEnd = settings.SunsetTime;
            float nightStart = settings.SunsetTime + settings.SunsetDuration;

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

        static float ComputeMoonOpacity(float gameHour, in MorrowindDayCycleState settings, in WeatherSettingsDef weatherSettings)
            => 1f - ComputeSunPercent(gameHour, settings.SunriseTime, settings.SunriseDuration, settings.SunsetTime, settings.SunsetDuration);

        static float ComputeStarOpacity(float gameHour, in MorrowindDayCycleState settings, in WeatherSettingsDef weatherSettings)
        {
            float hour = NormalizeGameHour(gameHour);
            float nightStart = settings.SunsetTime + settings.SunsetDuration + weatherSettings.StarsPostSunsetStart;
            float nightEnd = settings.SunriseTime - weatherSettings.StarsPreSunriseFinish;
            float fadeDuration = math.max(0.0001f, weatherSettings.StarsFadingDuration);

            if (hour >= nightStart || hour <= nightEnd)
                return 1f;
            if (hour > nightEnd && hour < settings.SunriseTime)
                return math.saturate((settings.SunriseTime - hour) / fadeDuration);
            if (hour > settings.SunsetTime && hour < nightStart)
                return math.saturate((hour - settings.SunsetTime) / fadeDuration);
            return 0f;
        }

        static MoonSettingsDef NormalizeMoonSettings(MoonSettingsDef settings)
        {
            if (settings.Size <= 0f)
            {
                settings.Size = 55f;
                settings.AxisOffset = 35f;
                settings.Speed = 0.5f;
                settings.DailyIncrement = 1f;
                settings.FadeStartAngle = 50f;
                settings.FadeEndAngle = 40f;
                settings.MoonShadowEarlyFadeAngle = 0.5f;
                settings.FadeInStart = 14f;
                settings.FadeInFinish = 15f;
                settings.FadeOutStart = 7f;
                settings.FadeOutFinish = 10f;
            }

            settings.Speed = math.max(settings.Speed, 180f / 23f / 15f);
            settings.DailyIncrement = math.fmod(settings.DailyIncrement, 24f);
            settings.MoonShadowEarlyFadeAngle = math.max(0.0001f, settings.MoonShadowEarlyFadeAngle);
            settings.FadeStartAngle = math.max(settings.FadeStartAngle, settings.FadeEndAngle + 0.0001f);
            settings.FadeInFinish = math.max(settings.FadeInFinish, settings.FadeInStart + 0.0001f);
            settings.FadeOutFinish = math.max(settings.FadeOutFinish, settings.FadeOutStart + 0.0001f);
            return settings;
        }

        static float MoonAngle(in MoonSettingsDef moon, int gameDay, float gameHour)
        {
            float moonRiseHourToday = MoonRiseHour(moon, gameDay);
            float moonRiseAngleToday = 0f;

            if (gameHour < moonRiseHourToday)
            {
                float moonRiseHourYesterday = moonRiseHourToday - moon.DailyIncrement;
                if (moonRiseHourYesterday < 24f)
                {
                    float moonShadowEarlyFadeAngle1 = moon.FadeEndAngle - moon.MoonShadowEarlyFadeAngle;
                    float timeToVisible = moonShadowEarlyFadeAngle1 / MoonRotation(moon, 1f);
                    float cycleOffset = moonRiseHourYesterday + timeToVisible > 24f ? moon.DailyIncrement : 0f;
                    float moonRiseAngleYesterday = MoonRotation(moon, 24f - (moonRiseHourYesterday + cycleOffset));
                    if (moonRiseAngleYesterday < 180f)
                        moonRiseAngleToday = MoonRotation(moon, gameHour) + moonRiseAngleYesterday;
                }
            }
            else
            {
                moonRiseAngleToday = MoonRotation(moon, gameHour - moonRiseHourToday);
            }

            return moonRiseAngleToday >= 180f ? 0f : moonRiseAngleToday;
        }

        static float MoonPhaseHour(in MoonSettingsDef moon, int gameDay)
        {
            if (!MoonVisible(moon, gameDay, 0f))
                return 0f;

            float moonShadowEarlyFadeAngle2 = 180f - moon.FadeEndAngle + moon.MoonShadowEarlyFadeAngle;
            float midnightAngle = MoonAngle(moon, gameDay, 0f);
            return ((moonShadowEarlyFadeAngle2 - midnightAngle) / MoonRotation(moon, 1f)) + math.max(moon.DailyIncrement, 0f);
        }

        static float MoonRiseHour(in MoonSettingsDef moon, int gameDay)
        {
            if (moon.DailyIncrement == 0f)
                return 0f;

            const int startDay = 16;
            float incrementOffset = (24f - math.abs(24f / moon.DailyIncrement)) * math.floor((gameDay + startDay) / 24f);
            return moon.DailyIncrement + math.fmod((gameDay - 1 + startDay - incrementOffset) * moon.DailyIncrement, 24f);
        }

        static float MoonRotation(in MoonSettingsDef moon, float hours) => 15f * moon.Speed * hours;

        static int MoonPhase(in MoonSettingsDef moon, int gameDay, float gameHour)
        {
            int phase = gameHour < MoonPhaseHour(moon, gameDay)
                ? (gameDay / 3) % 8
                : ((gameDay + 1) / 3) % 8;
            return math.clamp(phase, 0, 7);
        }

        static bool MoonVisible(in MoonSettingsDef moon, int gameDay, float gameHour)
            => MoonHourlyAlpha(moon, gameHour) > 0f && EarlyMoonShadowAlpha(moon, MoonAngle(moon, gameDay, gameHour)) > 0f;

        static float MoonShadowBlend(in MoonSettingsDef moon, float angle)
        {
            float fadeAngle = math.max(0.0001f, moon.FadeStartAngle - moon.FadeEndAngle);
            float fadeEndAngle2 = 180f - moon.FadeEndAngle;
            float fadeStartAngle2 = 180f - moon.FadeStartAngle;
            if (angle >= moon.FadeEndAngle && angle < moon.FadeStartAngle)
                return (angle - moon.FadeEndAngle) / fadeAngle;
            if (angle >= moon.FadeStartAngle && angle < fadeStartAngle2)
                return 1f;
            if (angle >= fadeStartAngle2 && angle < fadeEndAngle2)
                return (fadeEndAngle2 - angle) / fadeAngle;
            return 0f;
        }

        static float MoonHourlyAlpha(in MoonSettingsDef moon, float gameHour)
        {
            const float oneMinute = 0.0167f;
            float adjustedFadeOutFinish = moon.FadeOutFinish - oneMinute;
            if (gameHour >= moon.FadeOutStart && gameHour < adjustedFadeOutFinish)
                return (adjustedFadeOutFinish - gameHour) / math.max(0.0001f, adjustedFadeOutFinish - moon.FadeOutStart);
            if (gameHour >= adjustedFadeOutFinish && gameHour < moon.FadeInStart)
                return 0f;
            if (gameHour >= moon.FadeInStart && gameHour < moon.FadeInFinish)
                return (gameHour - moon.FadeInStart) / math.max(0.0001f, moon.FadeInFinish - moon.FadeInStart);
            return 1f;
        }

        static float EarlyMoonShadowAlpha(in MoonSettingsDef moon, float angle)
        {
            float moonShadowEarlyFadeAngle1 = moon.FadeEndAngle - moon.MoonShadowEarlyFadeAngle;
            float fadeEndAngle2 = 180f - moon.FadeEndAngle;
            float moonShadowEarlyFadeAngle2 = fadeEndAngle2 + moon.MoonShadowEarlyFadeAngle;
            if (angle >= moonShadowEarlyFadeAngle1 && angle < moon.FadeEndAngle)
                return (angle - moonShadowEarlyFadeAngle1) / moon.MoonShadowEarlyFadeAngle;
            if (angle >= moon.FadeEndAngle && angle < fadeEndAngle2)
                return 1f;
            if (angle >= fadeEndAngle2 && angle < moonShadowEarlyFadeAngle2)
                return (moonShadowEarlyFadeAngle2 - angle) / moon.MoonShadowEarlyFadeAngle;
            return 0f;
        }

        static int Rgb(int r, int g, int b)
        {
            uint packed = (uint)(byte)r | ((uint)(byte)g << 8) | ((uint)(byte)b << 16) | (255u << 24);
            return unchecked((int)packed);
        }
    }
}
