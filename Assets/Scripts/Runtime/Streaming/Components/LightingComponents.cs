using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Components
{
    public struct MainLightSingleton : IComponentData
    {
        public UnityObjectRef<Light> Value;
    }

    public struct LightInstanceFlags : IComponentData
    {
        public byte Carry;
        public byte Negative;
        public byte Flicker;
        public byte FlickerSlow;
        public byte Pulse;
        public byte PulseSlow;
        public byte OffDefault;
    }

    public struct LightInstanceState : IComponentData
    {
        public byte Enabled;
        public float3 BaseColorRgb;
        public float BaseIntensity;
        public float BaseRange;
        public float CurrentIntensity;
        public float CurrentRange;
        public float AnimationTime;
    }

    public struct LightPresentationLink : IComponentData
    {
        public int Slot;
    }

    public struct MorrowindDayCycleState : IComponentData
    {
        public float GameHour;
        public int DaysPassed;
        public float GameHoursPerSecond;

        public float SunriseTime;
        public float SunsetTime;
        public float SunriseDuration;
        public float SunsetDuration;

        public float SunPreSunriseTime;
        public float SunPostSunriseTime;
        public float SunPreSunsetTime;
        public float SunPostSunsetTime;

        public float AmbientPreSunriseTime;
        public float AmbientPostSunriseTime;
        public float AmbientPreSunsetTime;
        public float AmbientPostSunsetTime;

        public float FogPreSunriseTime;
        public float FogPostSunriseTime;
        public float FogPreSunsetTime;
        public float FogPostSunsetTime;

        public float3 AmbientSunriseColorRgb;
        public float3 AmbientDayColorRgb;
        public float3 AmbientSunsetColorRgb;
        public float3 AmbientNightColorRgb;

        public float3 SunSunriseColorRgb;
        public float3 SunDayColorRgb;
        public float3 SunSunsetColorRgb;
        public float3 SunNightColorRgb;

        public float3 FogSunriseColorRgb;
        public float3 FogDayColorRgb;
        public float3 FogSunsetColorRgb;
        public float3 FogNightColorRgb;

        public float ExteriorSunIntensityScale;
        public float ExteriorDayFogDensity;
        public float ExteriorNightFogDensity;
    }

    public struct ActiveEnvironmentState : IComponentData
    {
        public float3 AmbientColorRgb;
        public float3 DirectionalColorRgb;
        public float3 DirectionalLightDirection;
        public float DirectionalIntensity;
        public float SunPercent;
        public float3 FogColorRgb;
        public float FogDensity;
        public float FogNearMeters;
        public float FogFarMeters;
        public int RegionHandleValue;
        public byte IsInterior;
    }

    public struct InteriorAmbientSourceAuthoring : IComponentData
    {
        public SoundDefHandle AmbientSound;
    }
}
