using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using ProjectFossil.Core;

namespace ProjectFossil.Environment
{
    // Turns the match's time of day and weather (WorldConditions) into the sky: sun or moon, sky colour, ambient
    // light, haze, clouds, stars, rain, lightning, a head lamp at night, and flying reptiles overhead.
    // Presentation only; every machine builds the same sky from the same conditions.
    public class SkyDirector : MonoBehaviour
    {
        private const float BaseFog = 0.0052f;

        private struct Look
        {
            public float Elevation, Azimuth, Intensity;
            public Color Light, AmbientSky, AmbientEquator, AmbientGround, SkyTint, Fog, CloudTint;
            public float Exposure, Thickness, SunSize;
        }

        private Conditions _c;
        private bool       _active;
        private Light      _sun;
        private Material   _sky;
        private float      _sunIntensity, _skyExposure;
        // Rain, storms and fog: the procedural sky stays blue whatever its tint (that blue is the scattering
        // itself), so the camera draws a flat overcast colour instead, matched to the fog so the horizon melts away.
        private bool       _overcast;
        private Color      _overcastColor;
        private Camera     _paintedCam;
        private Color      _ambientSky;

        private SkyEffects _effects;
        private Pterosaurs _birds;
        private AtmosphereMood _mood;
        private Light      _lamp;
        private bool       _lampOn;

        private float _nextLightning;
        private float _flash;          // 0..1, fades after each strike

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<SkyDirector>() != null) return;
            new GameObject("Sky").AddComponent<SkyDirector>();
        }

        private void OnEnable()  => WorldConditions.Changed += Apply;
        private void OnDisable() => WorldConditions.Changed -= Apply;

        private void Awake()
        {
            _effects = gameObject.AddComponent<SkyEffects>();
            _birds   = gameObject.AddComponent<Pterosaurs>();
            _mood    = gameObject.AddComponent<AtmosphereMood>();
        }

        // ── A new match's sky ──────────────────────────────────────────────────

        private void Apply(Conditions c)
        {
            _c = c;
            _active = true;
            var look = LookFor(c);

            _sun = FindSun();
            if (_sun != null)
            {
                _sun.transform.rotation = Quaternion.Euler(look.Elevation, look.Azimuth, 0f);
                _sun.color = look.Light;
                _sun.intensity = _sunIntensity = look.Intensity;
                _sun.shadowStrength = c.Weather == Weather.Clear ? 0.9f : c.Weather == Weather.Fog ? 0.5f : 0.3f; // soft under cloud
                RenderSettings.sun = _sun;
            }

            RenderSettings.ambientMode         = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor     = _ambientSky = look.AmbientSky;
            RenderSettings.ambientEquatorColor = look.AmbientEquator;
            RenderSettings.ambientGroundColor  = look.AmbientGround;

            RenderSettings.fog        = true;
            RenderSettings.fogMode    = FogMode.ExponentialSquared;
            RenderSettings.fogColor   = look.Fog;
            RenderSettings.fogDensity = BaseFog * FogFactor(c);

            ApplySkybox(look);
            // Under cloud the sky is a grey lid, not blue. Overcast looked like a clear day with more clouds.
            _overcast = c.Weather != Weather.Clear;
            _overcastColor = Color.Lerp(look.Fog, Color.white, c.Weather == Weather.Cloudy ? 0.22f : 0.06f);
            _effects.Apply(c, look.CloudTint, look.Fog);
            _birds.Apply(c);
            _mood.Apply(c, _sun, look.Light, look.Fog);

            _lampOn = c.Time == DayTime.Night;
            _nextLightning = Time.time + Random.Range(6f, 14f);
        }

        private static float FogFactor(Conditions c)
        {
            float f = c.Weather switch
            {
                Weather.Cloudy => 1.25f,
                Weather.Rain   => 2f,
                Weather.Storm  => 2.6f,
                Weather.Fog    => 4.2f,
                _              => 1f,
            };
            return c.Time == DayTime.Dawn ? f * 1.35f : f; // morning mist
        }

        private void ApplySkybox(Look look)
        {
            var current = RenderSettings.skybox;
            if (current == null || !current.HasProperty("_Exposure")) return; // not the procedural sky
            if (_sky == null || current != _sky)
            {
                _sky = new Material(current) { name = "Sky (match)" };
                RenderSettings.skybox = _sky;
            }
            _sky.SetFloat("_Exposure", _skyExposure = look.Exposure);
            if (_sky.HasProperty("_SkyTint"))             _sky.SetColor("_SkyTint", look.SkyTint);
            if (_sky.HasProperty("_GroundColor"))         _sky.SetColor("_GroundColor", look.Fog * 0.8f);
            if (_sky.HasProperty("_AtmosphereThickness")) _sky.SetFloat("_AtmosphereThickness", look.Thickness);
            if (_sky.HasProperty("_SunSize"))             _sky.SetFloat("_SunSize", look.SunSize);
        }

        // The time of day sets the light; the weather dims and greys it.
        private static Look LookFor(Conditions c)
        {
            Look l;
            switch (c.Time)
            {
                case DayTime.Dawn:
                    l = new Look
                    {
                        Elevation = 12f, Azimuth = 80f, Intensity = 0.95f,
                        Light = new Color(1f, 0.7f, 0.48f),
                        AmbientSky = new Color(0.56f, 0.52f, 0.58f), AmbientEquator = new Color(0.52f, 0.45f, 0.4f),
                        AmbientGround = new Color(0.24f, 0.21f, 0.18f),
                        SkyTint = new Color(0.62f, 0.48f, 0.5f), Fog = new Color(0.66f, 0.56f, 0.52f),
                        CloudTint = new Color(1f, 0.78f, 0.66f), Exposure = 1.15f, Thickness = 1.6f, SunSize = 0.05f,
                    };
                    break;
                case DayTime.Dusk:
                    l = new Look
                    {
                        Elevation = 9f, Azimuth = 255f, Intensity = 0.85f,
                        Light = new Color(1f, 0.56f, 0.32f),
                        AmbientSky = new Color(0.5f, 0.42f, 0.48f), AmbientEquator = new Color(0.5f, 0.38f, 0.32f),
                        AmbientGround = new Color(0.2f, 0.16f, 0.14f),
                        SkyTint = new Color(0.72f, 0.42f, 0.36f), Fog = new Color(0.58f, 0.44f, 0.4f),
                        CloudTint = new Color(1f, 0.62f, 0.48f), Exposure = 1.05f, Thickness = 1.9f, SunSize = 0.06f,
                    };
                    break;
                case DayTime.Night:
                    // Moonlight: the sky's sun disc becomes the moon.
                    l = new Look
                    {
                        Elevation = 38f, Azimuth = 140f, Intensity = 0.35f,
                        Light = new Color(0.55f, 0.66f, 0.95f),
                        AmbientSky = new Color(0.14f, 0.17f, 0.27f), AmbientEquator = new Color(0.1f, 0.12f, 0.17f),
                        AmbientGround = new Color(0.05f, 0.05f, 0.07f),
                        SkyTint = new Color(0.22f, 0.27f, 0.42f), Fog = new Color(0.07f, 0.09f, 0.13f),
                        CloudTint = new Color(0.32f, 0.36f, 0.46f), Exposure = 0.2f, Thickness = 0.6f, SunSize = 0.035f,
                    };
                    break;
                default:
                    l = new Look
                    {
                        Elevation = 55f, Azimuth = -30f, Intensity = 1.25f,
                        Light = new Color(1f, 0.94f, 0.84f),
                        AmbientSky = new Color(0.64f, 0.72f, 0.74f), AmbientEquator = new Color(0.54f, 0.58f, 0.48f),
                        AmbientGround = new Color(0.32f, 0.3f, 0.23f),
                        SkyTint = new Color(0.5f, 0.5f, 0.5f), Fog = new Color(0.58f, 0.66f, 0.62f),
                        CloudTint = Color.white, Exposure = 1.3f, Thickness = 1f, SunSize = 0.04f,
                    };
                    break;
            }

            float dim = c.Weather switch
            {
                Weather.Cloudy => 0.5f, Weather.Rain => 0.42f, Weather.Storm => 0.28f, Weather.Fog => 0.55f, _ => 1f,
            };
            float grey = c.Weather switch
            {
                Weather.Cloudy => 0.6f, Weather.Rain => 0.6f, Weather.Storm => 0.75f, Weather.Fog => 0.7f, _ => 0f,
            };
            float ambientDim = c.Weather == Weather.Storm ? 0.62f : c.Weather == Weather.Rain ? 0.78f : c.Weather == Weather.Cloudy ? 0.92f : 1f;

            l.Intensity *= dim;
            l.Exposure  *= Mathf.Lerp(1f, 0.55f, grey);
            l.AmbientSky     = Greyed(l.AmbientSky, grey * 0.6f) * ambientDim;
            l.AmbientEquator = Greyed(l.AmbientEquator, grey * 0.6f) * ambientDim;
            l.AmbientGround  *= ambientDim;
            l.SkyTint   = Greyed(l.SkyTint, grey);
            l.Fog       = Greyed(l.Fog, grey * 0.8f) * (c.Weather == Weather.Storm ? 0.7f : 1f);
            l.CloudTint = Greyed(l.CloudTint, grey * 0.7f) * Mathf.Lerp(1f, 0.4f, grey); // rain clouds are dark
            if (c.Weather == Weather.Fog && c.Time != DayTime.Night) l.Fog = Color.Lerp(l.Fog, new Color(0.72f, 0.74f, 0.72f), 0.5f);
            return l;
        }

        private static Color Greyed(Color c, float amount)
        {
            float g = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            return Color.Lerp(c, new Color(g, g, g, c.a), amount);
        }

        private static Light FindSun()
        {
            var sun = RenderSettings.sun;
            if (sun != null && sun.type == LightType.Directional) return sun;
#if UNITY_6000_5_OR_NEWER
            foreach (var l in FindObjectsByType<Light>())
#else
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
#endif
                if (l.type == LightType.Directional) return l;
            return null;
        }

        // ── Every frame ────────────────────────────────────────────────────────

        private void Update()
        {
            if (!_active) return;
            var cam = Camera.main;
            if (cam != null)
            {
                PaintBackground(cam);
                _effects.Follow(cam.transform);
                _birds.Follow(cam.transform);
                _mood.Follow(cam.transform, Time.deltaTime);
                UpdateLamp(cam);
            }
            UpdateLightning();
        }

        private void PaintBackground(Camera cam)
        {
            if (_paintedCam != null && _paintedCam != cam && _paintedCam.clearFlags == CameraClearFlags.SolidColor)
                _paintedCam.clearFlags = CameraClearFlags.Skybox;
            if (_overcast)
            {
                cam.clearFlags      = CameraClearFlags.SolidColor;
                cam.backgroundColor = _overcastColor + Color.white * (LightningPulse * 0.35f);
                _paintedCam = cam;
            }
            else if (_paintedCam == cam)
            {
                cam.clearFlags = CameraClearFlags.Skybox;
                _paintedCam = null;
            }
        }

        // Night: a lamp on the head, L to switch it. It helps you see, nothing more (animals don't notice it yet).
        private void UpdateLamp(Camera cam)
        {
            var kb = Keyboard.current;
            if (kb != null && kb.lKey.wasPressedThisFrame) _lampOn = !_lampOn;

            if (_lamp == null && _lampOn)
            {
                var go = new GameObject("HeadLamp");
                _lamp = go.AddComponent<Light>();
                _lamp.type = LightType.Spot;
                _lamp.range = 30f;
                _lamp.spotAngle = 62f;
                _lamp.innerSpotAngle = 30f;
                _lamp.intensity = 4f;
                _lamp.color = new Color(1f, 0.9f, 0.72f);
                _lamp.shadows = LightShadows.None;
            }
            if (_lamp == null) return;
            if (_lamp.transform.parent != cam.transform)
            {
                _lamp.transform.SetParent(cam.transform, false);
                _lamp.transform.localPosition = new Vector3(0.12f, -0.05f, 0.1f);
                _lamp.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);
            }
            if (_lamp.enabled != _lampOn) _lamp.enabled = _lampOn;
        }

        // Two quick pulses, then gone.
        private float LightningPulse => _flash > 0.6f ? 1f : _flash > 0.45f ? 0.2f : _flash;

        // Storms: a strike every so often, flashing the sky and the ground, thunder following.
        private void UpdateLightning()
        {
            if (_c.Weather == Weather.Storm && Time.time >= _nextLightning)
            {
                _nextLightning = Time.time + Random.Range(7f, 22f);
                _flash = 1f;
                WorldConditions.RaiseLightning(Random.Range(250f, 2500f));
            }
            if (_flash <= 0f) return;

            _flash = Mathf.Max(0f, _flash - Time.deltaTime * 3.5f);
            float pulse = LightningPulse;
            if (_sun != null) _sun.intensity = _sunIntensity + pulse * 2.2f;
            RenderSettings.ambientSkyColor = _ambientSky + Color.white * (pulse * 0.6f);
            if (_sky != null) _sky.SetFloat("_Exposure", _skyExposure + pulse * 1.4f);
        }
    }
}
