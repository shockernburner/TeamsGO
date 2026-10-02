using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ProjectFossil.Core;
using ProjectFossil.Dinosaurs;
using ProjectFossil.Match;

namespace ProjectFossil.UI
{
    // The film look, set up at runtime so no scene or asset edits are needed: filmic (ACES) tone mapping, a little
    // more contrast and colour, bloom on bright highlights (sun on water, the helicopter's lights) and a soft
    // vignette. It also carries fear: as predators close in, colour drains out and the vignette tightens and
    // reddens; in the last stand at the helicopter it stays tight. Presentation only.
    public class CinematicLook : MonoBehaviour
    {
        [Header("Calm")]
        public float exposure   = 0.55f;  // stops; ACES darkens midtones, this brings them back
        public float contrast   = 8f;
        public float saturation = 12f;
        public float bloom      = 0.6f;
        public float vignette   = 0.22f;

        [Header("Danger (at full)")]
        public float dangerSaturation = -15f;
        public float dangerVignette   = 0.34f;
        public Color dangerTint       = new Color(0.1f, 0.01f, 0.01f); // reads as closing darkness on camera, not red paint

        private MatchBootstrap   _bootstrap;
        private Volume           _volume;
        private ColorAdjustments _color;
        private Vignette         _vignette;
        private SplitToning      _split;
        private float            _moodSaturation; // added to `saturation` for the time and weather
        private Camera           _camera;
        private float            _danger;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<CinematicLook>() != null) return;
            if (FindAnyObjectByType<MatchBootstrap>() == null) return;
            new GameObject("CinematicLook").AddComponent<CinematicLook>();
        }

        private void Awake()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.value = TonemappingMode.ACES;

            _color = profile.Add<ColorAdjustments>(true);
            _color.postExposure.value = exposure;
            _color.contrast.value     = contrast;
            _color.saturation.value   = saturation;
            _color.colorFilter.value  = new Color(1f, 0.97f, 0.92f); // a touch of warm afternoon

            var glow = profile.Add<Bloom>(true);
            glow.threshold.value = 1f;
            glow.intensity.value = bloom;
            glow.scatter.value   = 0.7f;

            // Each time of day gets its own colour: warm light and cool shade by day, rose and violet at dawn,
            // amber and blue at dusk, moonlit blue at night. Set per match from the conditions (Mood).
            _split = profile.Add<SplitToning>(true);
            Mood(WorldConditions.Current);

            _vignette = profile.Add<Vignette>(true);
            _vignette.intensity.value  = vignette;
            _vignette.smoothness.value = 0.45f;
            _vignette.color.value      = Color.black;

            _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal      = true;
            _volume.priority      = 50f; // over any sample-scene volume
            _volume.sharedProfile = profile;
        }

        private void OnEnable()  => WorldConditions.Changed += Mood;
        private void OnDisable() => WorldConditions.Changed -= Mood;

        private void Mood(Conditions c)
        {
            if (_split == null) return;
            Color shadows, highlights;
            switch (c.Time)
            {
                case DayTime.Dawn:  shadows = new Color(0.47f, 0.46f, 0.6f);  highlights = new Color(0.66f, 0.54f, 0.48f); break;
                case DayTime.Dusk:  shadows = new Color(0.44f, 0.47f, 0.6f);  highlights = new Color(0.7f, 0.52f, 0.38f);  break;
                case DayTime.Night: shadows = new Color(0.4f, 0.48f, 0.64f);  highlights = new Color(0.5f, 0.53f, 0.6f);   break;
                default:            shadows = new Color(0.46f, 0.53f, 0.56f); highlights = new Color(0.6f, 0.55f, 0.46f);  break;
            }
            bool grey = c.Weather == Weather.Rain || c.Weather == Weather.Storm || c.Weather == Weather.Fog;
            if (grey)
            {
                var mid = new Color(0.5f, 0.5f, 0.5f);
                shadows    = Color.Lerp(shadows, new Color(0.46f, 0.5f, 0.54f), 0.6f);
                highlights = Color.Lerp(highlights, mid, 0.7f);
            }
            _split.shadows.value    = shadows;
            _split.highlights.value = highlights;
            _split.balance.value    = c.Time == DayTime.Night ? 30f : 0f;
            _moodSaturation = grey ? -12f : c.Time == DayTime.Night ? -20f : c.Time == DayTime.Day ? 0f : 6f;
        }

        private void Update()
        {
            // The player's camera comes and goes with each match; post-processing is off on new cameras.
            var cam = Camera.main;
            if (cam != null && cam != _camera)
            {
                _camera = cam;
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null)
                {
                    data.renderPostProcessing = true;
                    // Sun shafts and mist soften where they meet the ground, which needs the scene's depth.
                    data.requiresDepthTexture = true;
                    // MSAA is off in the pipeline asset, so leaves and branches shimmered with jagged edges.
                    // SMAA smooths them for a small cost and without the smear of temporal AA on swaying foliage.
                    data.antialiasing        = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                    data.antialiasingQuality = AntialiasingQuality.High;
                }
            }

            if (_bootstrap == null) _bootstrap = FindAnyObjectByType<MatchBootstrap>();
            var match = _bootstrap != null ? _bootstrap.Match : null;
            float target = 0f;
            if (match != null && match.IsRunning && match.Player != null)
            {
                target = DinosaurAI.DangerAt(match.Player.transform.position);
                if (match.IsFinalStand) target = Mathf.Max(target, 0.5f);
            }
            _danger = Mathf.MoveTowards(_danger, target, Time.deltaTime * (target > _danger ? 1.5f : 0.4f));

            _color.saturation.value = Mathf.Lerp(saturation + _moodSaturation, dangerSaturation, _danger);
            _vignette.intensity.value = Mathf.Lerp(vignette, dangerVignette, _danger);
            _vignette.color.value = Color.Lerp(Color.black, dangerTint, _danger);
        }
    }
}
