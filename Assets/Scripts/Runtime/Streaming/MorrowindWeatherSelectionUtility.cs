using Unity.Mathematics;
using VVardenfell.Core.Cache;

namespace VVardenfell.Runtime.Streaming
{
    public static class MorrowindWeatherSelectionUtility
    {
        public static int SampleRegionWeather(in RegionDef region, ref Random random, int excludedWeather = -1)
        {
            return SampleWeather(
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
                ref random,
                excludedWeather);
        }

        public static int SampleFallbackExteriorWeather(ref Random random, int excludedWeather = -1)
        {
            return SampleWeather(
                clear: 45,
                cloudy: 20,
                foggy: 5,
                overcast: 15,
                rain: 10,
                thunder: 5,
                ash: 0,
                blight: 0,
                snow: 0,
                blizzard: 0,
                ref random,
                excludedWeather);
        }

        public static int SampleWeather(
            int clear,
            int cloudy,
            int foggy,
            int overcast,
            int rain,
            int thunder,
            int ash,
            int blight,
            int snow,
            int blizzard,
            ref Random random,
            int excludedWeather = -1)
        {
            int total =
                Chance(clear, WeatherKind.Clear, excludedWeather) +
                Chance(cloudy, WeatherKind.Cloudy, excludedWeather) +
                Chance(foggy, WeatherKind.Foggy, excludedWeather) +
                Chance(overcast, WeatherKind.Overcast, excludedWeather) +
                Chance(rain, WeatherKind.Rain, excludedWeather) +
                Chance(thunder, WeatherKind.Thunderstorm, excludedWeather) +
                Chance(ash, WeatherKind.Ashstorm, excludedWeather) +
                Chance(blight, WeatherKind.Blight, excludedWeather) +
                Chance(snow, WeatherKind.Snow, excludedWeather) +
                Chance(blizzard, WeatherKind.Blizzard, excludedWeather);

            if (total <= 0 && excludedWeather >= 0)
                return SampleWeather(clear, cloudy, foggy, overcast, rain, thunder, ash, blight, snow, blizzard, ref random);

            if (total <= 0)
                return (int)WeatherKind.Clear;

            int roll = random.NextInt(total);
            return Pick(ref roll, clear, WeatherKind.Clear, excludedWeather)
                ?? Pick(ref roll, cloudy, WeatherKind.Cloudy, excludedWeather)
                ?? Pick(ref roll, foggy, WeatherKind.Foggy, excludedWeather)
                ?? Pick(ref roll, overcast, WeatherKind.Overcast, excludedWeather)
                ?? Pick(ref roll, rain, WeatherKind.Rain, excludedWeather)
                ?? Pick(ref roll, thunder, WeatherKind.Thunderstorm, excludedWeather)
                ?? Pick(ref roll, ash, WeatherKind.Ashstorm, excludedWeather)
                ?? Pick(ref roll, blight, WeatherKind.Blight, excludedWeather)
                ?? Pick(ref roll, snow, WeatherKind.Snow, excludedWeather)
                ?? Pick(ref roll, blizzard, WeatherKind.Blizzard, excludedWeather)
                ?? (int)WeatherKind.Clear;
        }

        static int Chance(int chance, WeatherKind kind, int excludedWeather)
            => excludedWeather == (int)kind ? 0 : math.max(0, chance);

        static int? Pick(ref int roll, int chance, WeatherKind kind, int excludedWeather)
        {
            if (excludedWeather == (int)kind || chance <= 0)
                return null;
            if (roll < chance)
                return (int)kind;
            roll -= chance;
            return null;
        }
    }
}
