using System;
using UnityEngine;

namespace ProjectFossil.Core
{
    // Photographed skies (CC0 HDRIs from Poly Haven) for each time of day and weather. The editor menu
    // Project Fossil > Art > Download Skies fetches them on this machine and fills this asset; without it the
    // game keeps its drawn sky. Each sky carries where its sun is and its horizon colour, so the sun light and the
    // haze match the photograph.
    public class SkyLibrary : ScriptableObject
    {
        [Serializable]
        public class Sky
        {
            public string   key;          // see KeyFor
            public Material material;     // Skybox/Panoramic over the HDRI
            public bool     hasSun;       // a clear sun (or moon) in the picture
            public Vector3  sunDirection; // towards the sun, world space
            public Color    horizon;      // average colour just above the horizon, for the haze
            public string   source;       // Poly Haven asset id
        }

        public Sky[] skies = new Sky[0];

        public const string ResourceName = "SkyLibrary";
        public static readonly string[] Keys = { "Clear", "Cloudy", "Overcast", "Dawn", "Dusk", "Night" };

        private static SkyLibrary _current;
        private static bool _loaded;

        public static SkyLibrary Current
        {
            get
            {
                if (!_loaded) { _current = Resources.Load<SkyLibrary>(ResourceName); _loaded = true; }
                return _current;
            }
        }

        public static void Reload() => _loaded = false;

        // Which photograph fits a match: night is night whatever the weather; rain, storms and fog are under a
        // grey lid; otherwise the time of day, with a cloudier sky on cloudy days.
        public static string KeyFor(DayTime time, Weather weather)
        {
            if (time == DayTime.Night) return "Night";
            if (weather == Weather.Rain || weather == Weather.Storm || weather == Weather.Fog) return "Overcast";
            if (time == DayTime.Dawn) return "Dawn";
            if (time == DayTime.Dusk) return "Dusk";
            return weather == Weather.Cloudy ? "Cloudy" : "Clear";
        }

        // The sky for a match, or a near substitute, or null.
        public Sky For(DayTime time, Weather weather)
        {
            string key = KeyFor(time, weather);
            var sky = Find(key);
            if (sky != null) return sky;
            string fallback = key == "Cloudy" ? "Clear" : key == "Clear" ? "Cloudy"
                            : key == "Dawn" ? "Dusk" : key == "Dusk" ? "Dawn"
                            : key == "Overcast" ? "Cloudy" : null;
            return fallback != null ? Find(fallback) : null;
        }

        public Sky Find(string key)
        {
            if (skies == null) return null;
            foreach (var s in skies)
                if (s != null && s.key == key && s.material != null) return s;
            return null;
        }
    }
}
