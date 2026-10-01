using System;

namespace ProjectFossil.Core
{
    public enum DayTime : byte { Random = 0, Dawn, Day, Dusk, Night }
    public enum Weather : byte { Random = 0, Clear, Cloudy, Rain, Storm, Fog }

    public struct Conditions
    {
        public DayTime Time;
        public Weather Weather;
        public float   WindDegrees; // where the wind blows toward (clouds, rain slant)
    }

    // The time of day and weather of the current match. Players pick each one (or Random) before a match; Random
    // is rolled from the island seed, so everyone on a team gets the same sky. Online, the host sends its result
    // and joiners take it as an override.
    //
    // Weather is systemic, not just a look: rain drowns out footsteps, fog and night hide you from far off.
    public static class WorldConditions
    {
        public static DayTime ChosenTime    = DayTime.Random;
        public static Weather ChosenWeather = Weather.Random;

        public static Conditions Current { get; private set; } = new Conditions { Time = DayTime.Day, Weather = Weather.Clear };

        // Raised when a new match's sky is decided.
        public static event Action<Conditions> Changed;
        // Lightning struck this far away (metres); thunder follows at the speed of sound.
        public static event Action<float> Lightning;

        private static bool _hasOverride;
        private static Conditions _override;

        // Joiner: use the host's sky for the next island.
        public static void Override(DayTime time, Weather weather, float windDegrees)
        {
            _override = new Conditions { Time = time, Weather = weather, WindDegrees = windDegrees };
            _hasOverride = true;
        }

        public static void ClearOverride() => _hasOverride = false;

        // Called once the island is built. Uses the override if there is one, otherwise the choices and the seed.
        public static void Begin(int seed)
        {
            Current = _hasOverride ? _override : Resolve(seed, ChosenTime, ChosenWeather);
            _hasOverride = false;
            Changed?.Invoke(Current);
        }

        public static void RaiseLightning(float distance) => Lightning?.Invoke(distance);

        // Deterministic for a seed. Specific choices are kept; Random is rolled with day and clear skies most likely.
        public static Conditions Resolve(int seed, DayTime time, Weather weather)
        {
            var rng = new Random(seed ^ 0x5EA7C10D);
            var c = new Conditions
            {
                Time        = time,
                Weather     = weather,
                WindDegrees = (float)(rng.NextDouble() * 360.0),
            };
            double t = rng.NextDouble(), w = rng.NextDouble(); // always drawn, so one choice doesn't shift the other
            if (c.Time == DayTime.Random)
                c.Time = t < 0.18 ? DayTime.Dawn : t < 0.62 ? DayTime.Day : t < 0.82 ? DayTime.Dusk : DayTime.Night;
            if (c.Weather == Weather.Random)
                c.Weather = w < 0.34 ? Weather.Clear : w < 0.64 ? Weather.Cloudy : w < 0.8 ? Weather.Rain
                          : w < 0.9 ? Weather.Fog : Weather.Storm;
            return c;
        }

        // How far animals can see (1 = a clear day).
        public static float SightMultiplier => Sight(Current);
        // How far animals can hear you (1 = a quiet day).
        public static float HearingMultiplier => Hearing(Current);

        public static float Sight(Conditions c)
        {
            float t = c.Time == DayTime.Night ? 0.55f : c.Time == DayTime.Dawn || c.Time == DayTime.Dusk ? 0.85f : 1f;
            float w = c.Weather == Weather.Fog ? 0.6f : c.Weather == Weather.Storm ? 0.75f : c.Weather == Weather.Rain ? 0.85f : 1f;
            return t * w;
        }

        public static float Hearing(Conditions c) =>
            c.Weather == Weather.Storm ? 0.6f : c.Weather == Weather.Rain ? 0.8f : 1f;

        public static string Describe(Conditions c)
        {
            string time = c.Time switch
            {
                DayTime.Dawn => "Dawn", DayTime.Dusk => "Dusk", DayTime.Night => "Night", _ => "Midday",
            };
            string weather = c.Weather switch
            {
                Weather.Cloudy => "overcast", Weather.Rain => "rain", Weather.Storm => "thunderstorm",
                Weather.Fog => "thick fog", _ => "clear skies",
            };
            return $"{time}, {weather}";
        }

        // Shown in the menu next to each choice.
        public static string Label(DayTime t) => t == DayTime.Random ? "Random" : t.ToString();
        public static string Label(Weather w) => w == Weather.Random ? "Random" : w.ToString();
    }
}
