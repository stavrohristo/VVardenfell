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

        public float SkyPreSunriseTime;
        public float SkyPostSunriseTime;
        public float SkyPreSunsetTime;
        public float SkyPostSunsetTime;

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
        public float SkySunPitchOffsetDegrees;
        public float MainLightIntensityScale;
        public float MoonMaxOpacity;
        public float StarMaxOpacity;
        public float PrecipitationIntensityScale;
        public float LightningIntensityScale;
    }

    public struct MorrowindTimeState : IComponentData
    {
        public float GameHour;
        public int DaysPassed;
        public int Day;
        public int Month;
        public int Year;
        public float TimeScale;
        public float SimulationTimeScale;
        public byte Paused;
        public byte FastForwarding;
        public float LastAdvancedHours;
    }

    public enum MorrowindTimeAdvanceKind : byte
    {
        Normal = 0,
        Rest = 1,
        Sleep = 2,
        Travel = 3,
        Script = 4,
    }

    public struct MorrowindTimeAdvanceRequest : IBufferElementData
    {
        public float Hours;
        public byte Kind;
    }

    public struct MorrowindWeatherState : IComponentData
    {
        public int CurrentWeather;
        public int NextWeather;
        public float Transition;
        public float TransitionDelta;
        public float HoursUntilNextChange;
        public int RegionHandleValue;
        public uint RandomState;
        public int ForcedWeather;
        public float SecondsUntilThunder;
        public float LightningBrightness;
        public uint ThunderSequence;
        public int LastThunderSoundIndex;
        public byte Initialized;
        public byte Transitioning;
    }

    public struct ActiveSkyWeatherState : IComponentData
    {
        public float3 SkyColorRgb;
        public float3 SunDiscColorRgb;
        public float3 SkySunWorldDirection;
        public float3 UnityLightDirection;
        public float3 MasserWorldDirection;
        public float3 SecundaWorldDirection;
        public float MoonOpacity;
        public float MasserOpacity;
        public float SecundaOpacity;
        public float MasserShadowBlend;
        public float SecundaShadowBlend;
        public float MasserSize;
        public float SecundaSize;
        public int MasserPhase;
        public int SecundaPhase;
        public float StarOpacity;
        public float StarRotationDegrees;
        public float SunDiscOpacity;
        public float CloudOpacity;
        public float CloudSpeed;
        public float CloudUvOffset;
        public int CurrentCloudTextureIndex;
        public int NextCloudTextureIndex;
        public float WindSpeed;
        public float3 StormDirection;
        public float PrecipitationIntensity;
        public float RainSpeed;
        public float RainDiameter;
        public float RainMinHeight;
        public float RainMaxHeight;
        public float Glare;
        public float LightningBrightness;
        public uint ThunderSequence;
        public int ThunderSoundIndex;
        public int WeatherKind;
        public int NextWeatherKind;
        public float WeatherTransition;
        public byte IsNight;
        public byte IsStorm;
        public byte IsInterior;
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
