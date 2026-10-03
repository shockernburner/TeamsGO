using UnityEngine;

namespace ProjectFossil.Core
{
    // Off is the mic-free mode: no voice is sent and the dinosaurs never hear it (footsteps still count).
    public enum VoiceMode { Off, OpenMic, PushToTalk }

    // The player's own settings: graphics, sound, mouse and camera. Saved in PlayerPrefs, applied at start and on
    // every change. Gameplay code reads the values here (sensitivity, invert, field of view) instead of its own.
    public static class GameSettings
    {
        private const string Prefix = "ProjectFossil.Settings.";

        public static int   Quality      { get; private set; }
        public static bool  Fullscreen   { get; private set; }
        public static int   ResolutionIx { get; private set; } // index into Screen.resolutions, -1 = keep current
        public static float MasterVolume { get; private set; } // 0..1
        public static float Sensitivity  { get; private set; } // multiplier on the controller's own sensitivity
        public static bool  InvertY      { get; private set; }
        public static float FieldOfView  { get; private set; } // third-person camera, degrees
        public static bool  ShowFps      { get; private set; }
        public static VoiceMode Voice    { get; private set; } = VoiceMode.OpenMic;
        public static float VoiceVolume  { get; private set; } = 1f; // teammates' voices, 0..1

        public const float MinFov = 50f, MaxFov = 90f, DefaultFov = 60f;
        public const float MinSensitivity = 0.25f, MaxSensitivity = 3f;

        private static bool _loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LoadAtStart()
        {
            _loaded = false;
            Load();
        }

        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            Quality      = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "Quality", QualitySettings.GetQualityLevel()), 0, QualitySettings.names.Length - 1);
            Fullscreen   = PlayerPrefs.GetInt(Prefix + "Fullscreen", Screen.fullScreen ? 1 : 0) == 1;
            ResolutionIx = PlayerPrefs.GetInt(Prefix + "Resolution", -1);
            MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "Volume", 1f));
            Sensitivity  = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "Sensitivity", 1f), MinSensitivity, MaxSensitivity);
            InvertY      = PlayerPrefs.GetInt(Prefix + "InvertY", 0) == 1;
            FieldOfView  = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "Fov", DefaultFov), MinFov, MaxFov);
            ShowFps      = PlayerPrefs.GetInt(Prefix + "ShowFps", 0) == 1;
            Voice        = (VoiceMode)Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "Voice", (int)VoiceMode.OpenMic), 0, 2);
            VoiceVolume  = Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "VoiceVolume", 1f));
            Apply();
        }

        public static void Set(int quality, bool fullscreen, int resolution, float volume, float sensitivity,
                               bool invertY, float fov, bool showFps)
        {
            Load();
            bool screenChanged = fullscreen != Fullscreen || resolution != ResolutionIx;
            bool qualityChanged = quality != Quality;
            Quality = quality; Fullscreen = fullscreen; ResolutionIx = resolution; MasterVolume = volume;
            Sensitivity = sensitivity; InvertY = invertY; FieldOfView = fov; ShowFps = showFps;
            PlayerPrefs.SetInt(Prefix + "Quality", Quality);
            PlayerPrefs.SetInt(Prefix + "Fullscreen", Fullscreen ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "Resolution", ResolutionIx);
            PlayerPrefs.SetFloat(Prefix + "Volume", MasterVolume);
            PlayerPrefs.SetFloat(Prefix + "Sensitivity", Sensitivity);
            PlayerPrefs.SetInt(Prefix + "InvertY", InvertY ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "Fov", FieldOfView);
            PlayerPrefs.SetInt(Prefix + "ShowFps", ShowFps ? 1 : 0);
            PlayerPrefs.Save();
            AudioListener.volume = MasterVolume;
            if (qualityChanged) QualitySettings.SetQualityLevel(Quality, true);
            if (screenChanged) ApplyScreen();
        }

        public static void SetVoice(VoiceMode mode, float volume)
        {
            Load();
            Voice = mode; VoiceVolume = Mathf.Clamp01(volume);
            PlayerPrefs.SetInt(Prefix + "Voice", (int)Voice);
            PlayerPrefs.SetFloat(Prefix + "VoiceVolume", VoiceVolume);
            PlayerPrefs.Save();
        }

        private static void Apply()
        {
            AudioListener.volume = MasterVolume;
            if (QualitySettings.GetQualityLevel() != Quality) QualitySettings.SetQualityLevel(Quality, true);
            ApplyScreen();
        }

        // In the Editor the Game view owns the window; only a built game changes resolution and full screen.
        private static void ApplyScreen()
        {
            if (Application.isEditor) return;
            var all = Screen.resolutions;
            if (ResolutionIx >= 0 && ResolutionIx < all.Length)
            {
                var r = all[ResolutionIx];
                Screen.SetResolution(r.width, r.height, Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            }
            else Screen.fullScreenMode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        }
    }
}
