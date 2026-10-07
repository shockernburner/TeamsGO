using UnityEngine;

namespace ProjectFossil.Core
{
    // The settings screen, drawn the same from the start menu and the in-match menu. Changes apply at once and are
    // saved; Draw returns true when the player presses Back.
    public static class SettingsPanel
    {
        private static GUIStyle _title, _label, _button;
        // Set by the title sequence while it exists: the start menu's settings then offer "Play intro".
        public static System.Action PlayIntro;

        public static bool Draw(Rect area, bool onStartMenu = false)
        {
            GameSettings.Load();
            EnsureStyles();
            bool back = false;
            int quality = GameSettings.Quality, res = GameSettings.ResolutionIx;
            bool full = GameSettings.Fullscreen, invert = GameSettings.InvertY, fps = GameSettings.ShowFps;
            float vol = GameSettings.MasterVolume, sens = GameSettings.Sensitivity, fov = GameSettings.FieldOfView;

            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("SETTINGS", _title);
            GUILayout.Space(8);

            var names = QualitySettings.names;
            Row("Graphics", () => { if (GUILayout.Button(names.Length > 0 ? names[Mathf.Clamp(quality, 0, names.Length - 1)] : "Default", _button)) quality = (quality + 1) % Mathf.Max(1, names.Length); });
            if (!Application.isEditor)
            {
                var all = Screen.resolutions;
                string resName = res >= 0 && res < all.Length ? $"{all[res].width} x {all[res].height}" : $"{Screen.width} x {Screen.height}";
                Row("Resolution", () => { if (GUILayout.Button(resName, _button) && all.Length > 0) res = (res + 1) % all.Length; });
                Row("Full screen", () => { full = GUILayout.Toggle(full, full ? " On" : " Off"); });
            }
            Row($"Volume  {Mathf.RoundToInt(vol * 100)}%", () => { vol = GUILayout.HorizontalSlider(vol, 0f, 1f); });
            Row($"Mouse  {sens:0.00}x", () => { sens = GUILayout.HorizontalSlider(sens, GameSettings.MinSensitivity, GameSettings.MaxSensitivity); });
            Row("Invert mouse Y", () => { invert = GUILayout.Toggle(invert, invert ? " On" : " Off"); });
            Row($"Field of view  {Mathf.RoundToInt(fov)}", () => { fov = Mathf.Round(GUILayout.HorizontalSlider(fov, GameSettings.MinFov, GameSettings.MaxFov)); });
            Row("Show FPS", () => { fps = GUILayout.Toggle(fps, fps ? " On" : " Off"); });
            var voice = GameSettings.Voice;
            float voiceVol = GameSettings.VoiceVolume;
            string voiceName = voice == VoiceMode.Off ? "Off (dinosaurs can't hear you)" : voice == VoiceMode.PushToTalk ? "Push to talk (V)" : "Open mic";
            Row("Voice chat", () => { if (GUILayout.Button(voiceName, _button)) voice = (VoiceMode)(((int)voice + 1) % 3); });
            Row($"Voice volume  {Mathf.RoundToInt(voiceVol * 100)}%", () => { voiceVol = GUILayout.HorizontalSlider(voiceVol, 0f, 1f); });
            if (voice != GameSettings.Voice || !Mathf.Approximately(voiceVol, GameSettings.VoiceVolume)) GameSettings.SetVoice(voice, voiceVol);

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (onStartMenu && PlayIntro != null && GUILayout.Button("Play intro", _button, GUILayout.Height(34)))
            {
                PlayIntro();
                back = true;
            }
            if (GUILayout.Button("Back", _button, GUILayout.Height(34))) back = true;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            if (quality != GameSettings.Quality || res != GameSettings.ResolutionIx || full != GameSettings.Fullscreen ||
                !Mathf.Approximately(vol, GameSettings.MasterVolume) || !Mathf.Approximately(sens, GameSettings.Sensitivity) ||
                invert != GameSettings.InvertY || !Mathf.Approximately(fov, GameSettings.FieldOfView) || fps != GameSettings.ShowFps)
                GameSettings.Set(quality, full, res, vol, sens, invert, fov, fps);
            return back;
        }

        private static void Row(string label, System.Action control)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(30));
            GUILayout.Label(label, _label, GUILayout.Width(150));
            GUILayout.BeginVertical();
            GUILayout.Space(8);
            control();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private static void EnsureStyles()
        {
            if (_title != null) return;
            _title  = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _title.normal.textColor = new Color(1f, 0.85f, 0.45f);
            _label  = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 14 };
        }
    }
}
