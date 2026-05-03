using System;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;

namespace VVardenfell.Runtime.Shell
{
    static class RuntimeRestUtility
    {
        public const int MinHours = 1;
        public const int MaxHours = 24;
        const short StuntedMagickaEffectId = 136;

        public static int ClampHours(int hours) => math.clamp(hours, MinHours, MaxHours);

        public static bool NeedsHealing(in ActorVitalSet vitals)
            => vitals.CurrentHealth < vitals.ModifiedHealthBase
               || vitals.CurrentMagicka < vitals.ModifiedMagickaBase;

        public static float HealthPerSleepHour(in ActorAttributeSet attributes)
            => 0.1f * math.max(0f, attributes.Endurance);

        public static float MagickaPerSleepHour(RuntimeContentDatabase contentDb, in ActorAttributeSet attributes)
            => ReadGmst(contentDb, "fRestMagicMult", 0f) * math.max(0f, attributes.Intelligence);

        public static bool HasStuntedMagicka(DynamicBuffer<ActorActiveMagicEffect> activeEffects)
            => GetStuntedMagickaTimeLeftSeconds(activeEffects) != 0f;

        public static float GetStuntedMagickaTimeLeftSeconds(DynamicBuffer<ActorActiveMagicEffect> activeEffects)
        {
            float remainingTime = 0f;
            for (int i = 0; i < activeEffects.Length; i++)
            {
                var effect = activeEffects[i];
                if (!IsActiveStuntedMagickaEffect(effect))
                    continue;

                if (effect.DurationSeconds < 0f || effect.TimeLeftSeconds < 0f)
                    return -1f;

                remainingTime = math.max(remainingTime, effect.TimeLeftSeconds);
            }

            return remainingTime;
        }

        public static float MagickaRestoreHoursForSleep(float hours, float timeScale, DynamicBuffer<ActorActiveMagicEffect> activeEffects)
        {
            float remainingTime = GetStuntedMagickaTimeLeftSeconds(activeEffects);
            if (remainingTime < 0f)
                return 0f;
            if (remainingTime <= 0f)
                return hours;

            float effectiveTimeScale = timeScale == 0f ? 1f : timeScale;
            return math.max(0f, hours - remainingTime * effectiveTimeScale / 3600f);
        }

        public static void AdvanceTimedActiveMagicEffects(
            DynamicBuffer<ActorActiveMagicEffect> activeEffects,
            float gameHours,
            float timeScale)
        {
            float durationSeconds = math.max(0f, gameHours) * 3600f;
            if (timeScale != 0f)
                durationSeconds /= timeScale;

            if (durationSeconds <= 0f)
                return;

            for (int i = 0; i < activeEffects.Length; i++)
            {
                var effect = activeEffects[i];
                if (effect.Applied == 0 || effect.DurationSeconds < 0f || effect.TimeLeftSeconds <= 0f)
                    continue;

                effect.TimeLeftSeconds = math.max(0f, effect.TimeLeftSeconds - durationSeconds);
                if (effect.TimeLeftSeconds <= 0f)
                    effect.Applied = 0;

                activeEffects[i] = effect;
            }
        }

        public static int ComputeUntilHealedHours(
            RuntimeContentDatabase contentDb,
            in ActorVitalSet vitals,
            in ActorAttributeSet attributes,
            bool stuntedMagicka)
        {
            float healthPerHour = HealthPerSleepHour(attributes);
            float magickaPerHour = MagickaPerSleepHour(contentDb, attributes);
            float healthHours = healthPerHour > 0f
                ? (vitals.ModifiedHealthBase - vitals.CurrentHealth) / healthPerHour
                : 1f;
            float magickaHours = magickaPerHour > 0f && !stuntedMagicka
                ? (vitals.ModifiedMagickaBase - vitals.CurrentMagicka) / magickaPerHour
                : 1f;

            return ClampHours((int)Math.Ceiling(Math.Max(1f, Math.Max(healthHours, magickaHours))));
        }

        static bool IsActiveStuntedMagickaEffect(in ActorActiveMagicEffect effect)
        {
            return effect.Applied != 0
                   && effect.EffectId == StuntedMagickaEffectId
                   && effect.Magnitude > 0f
                   && (effect.DurationSeconds < 0f || effect.TimeLeftSeconds > 0f);
        }

        static float ReadGmst(RuntimeContentDatabase contentDb, string id, float fallback)
        {
            if (contentDb != null && contentDb.TryGetGameSettingFloat(id, out float value))
                return value;
            return fallback;
        }
    }
}
