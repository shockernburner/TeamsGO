using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using ProjectFossil.Audio;
using ProjectFossil.Core;
using ProjectFossil.Match;

namespace ProjectFossil.Net
{
    // What a player sees from launch to the menu (Docs/STORY.md): the studio logo on black, the story told in short
    // cards over a live island the camera slowly circles, read aloud by a narrator when the voice clips exist, then
    // the TETHER logo slams in on a roar and a music hit, PRIMAL under it, and the two ease up to the top of the
    // screen as the menu appears. Music runs under all of it (SoundSynth.IntroScore) and the visuals follow the
    // music's clock, so they never drift apart. A quiet loop plays behind the menu, and stops in a match. The first
    // launch plays it all; later launches start at the logo. Any key or click skips to the logo hit and the menu.
    public class TitleSequence : MonoBehaviour
    {
        public static TitleSequence Instance { get; private set; }
        // The menu waits until this is true.
        public static bool MenuReady => Instance == null || Instance._phase == Phase.Menu;

        private const float MenuFog = 0.0016f;
        private const string SeenKey = "ProjectFossil.IntroSeen";
        private const float StudioSeconds = 3f, LogoSeconds = 4f, RiseSeconds = 1.2f, FadeSeconds = 1f;
        private const float VoiceBreath = 0.8f;   // a card holds this long after its line is spoken
        private const float RingSeconds = 9f;     // the logo hit rings on under the menu before the loop takes over
        private const float MusicVolume = 0.85f, DuckedVolume = 0.5f, BedVolume = 0.5f;

        private enum Phase { Building, Studio, Story, Title, Menu }
        private enum Shot { Flight, Space, Dark }

        private Phase _phase = Phase.Building;
        private Phase _after = Phase.Studio;      // where Building goes once the island and the music are ready
        private float _t;                         // the intro's clock: seconds since the studio logo appeared
        private float _waited;
        private bool _skip;

        private MatchBootstrap _boot;
        private Camera _cam;
        private Vector3 _centre;
        private float _radius, _height, _angle, _lookHeight;

        private readonly List<(float seconds, string text, Shot shot, SoundSynth.Cue cue, AudioClip voice)> _cards =
            new List<(float, string, Shot, SoundSynth.Cue, AudioClip)>();
        private float[] _cardStart;
        private float _hitAt;                     // when the logo slams in, on the intro's clock
        private int _card;
        private float _cardTime;
        private int _spoken = -1;

        private AudioSource _music, _voice, _bed, _sting;
        private AudioClip _score, _bedClip, _roar;
        private Task<float[]> _scoreJob, _bedJob;
        private bool _hitPlayed;

        private Texture2D _studio, _tether, _primal;
        private GUIStyle _bigText, _story, _hint, _tag;
        private Vector2[] _stars;

        private void Awake()
        {
            Instance = this;
            _boot = GetComponent<MatchBootstrap>();
            _studio = Resources.Load<Texture2D>("Brand/VantwardGames");
            _tether = Resources.Load<Texture2D>("Brand/Tether");
            _primal = Resources.Load<Texture2D>("Brand/Primal");
            LoadStory();
            var rng = new System.Random(66);
            _stars = new Vector2[140];
            for (int i = 0; i < _stars.Length; i++) _stars[i] = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
            SettingsPanel.PlayIntro = PlayIntro;
            SetUpAudio();
            _after = PlayerPrefs.GetInt(SeenKey, 0) == 1 ? Phase.Title : Phase.Studio; // seen it: logo only
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SettingsPanel.PlayIntro = null;
        }

        // Each card: seconds | line | shot | music, and its narration (Story/Voice/LineNN) when it exists. A spoken
        // card holds until the line is finished, plus a breath.
        private void LoadStory()
        {
            var asset = Resources.Load<TextAsset>("Story/Intro");
            if (asset == null) return;
            foreach (var raw in asset.text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split('|');
                if (parts.Length < 2 || !float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                                                        System.Globalization.CultureInfo.InvariantCulture, out float s)) continue;
                string shot = parts.Length > 2 ? parts[2].Trim() : "";
                var cue = parts.Length > 3 && System.Enum.TryParse(parts[3].Trim(), true, out SoundSynth.Cue c) ? c
                        : shot == "space" ? SoundSynth.Cue.Stars : SoundSynth.Cue.Pad;
                var voice = Resources.Load<AudioClip>($"Story/Voice/Line{_cards.Count + 1:00}");
                if (voice != null) s = Mathf.Max(s, voice.length + VoiceBreath);
                _cards.Add((s, parts[1].Trim(), shot == "space" ? Shot.Space : shot == "dark" ? Shot.Dark : Shot.Flight, cue, voice));
            }
            _cardStart = new float[_cards.Count + 1];
            _cardStart[0] = StudioSeconds;
            for (int i = 0; i < _cards.Count; i++) _cardStart[i + 1] = _cardStart[i] + _cards[i].seconds;
            _hitAt = _cardStart[_cards.Count];
        }

        // Music is composed on a worker thread while the island is built (both take a moment); the clips are made
        // on the main thread once the samples are ready.
        private void SetUpAudio()
        {
            _music = NewSource(false);
            _voice = NewSource(false);
            _bed   = NewSource(true);
            _sting = NewSource(false);
            _roar  = SoundLibrary.One("Roar");
            if (_roar == null) _roar = MakeClip("IntroRoar", SoundSynth.Roar(2));
            var lengths = new float[_cards.Count];
            var cues = new SoundSynth.Cue[_cards.Count];
            for (int i = 0; i < _cards.Count; i++) { lengths[i] = _cards[i].seconds; cues[i] = _cards[i].cue; }
            _scoreJob = Task.Run(() => SoundSynth.IntroScore(StudioSeconds, lengths, cues, RingSeconds));
            _bedJob   = Task.Run(() => SoundSynth.MenuLoop());
        }

        private AudioSource NewSource(bool loop)
        {
            var a = gameObject.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.loop = loop;
            a.spatialBlend = 0f;
            a.ignoreListenerPause = true; // the intro and the menu play even if a paused match left the listener paused
            return a;
        }

        private static AudioClip MakeClip(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, SoundSynth.SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // Settings' "Play intro": the whole thing again, from the studio logo.
        public void PlayIntro()
        {
            if (NetSession.Instance != null && !NetSession.Instance.InMenu) return;
            _bed.Stop();
            StartIntro(0f);
            Enter(Phase.Studio);
        }

        // ── Flow ──────────────────────────────────────────────────────────────

        private void Update()
        {
            if (_phase != Phase.Menu && _phase != Phase.Building && AnyKey()) _skip = true;
            CollectMusic();

            if (_phase == Phase.Building)
            {
                // The island first, behind black; then the music, given a few seconds at most.
                _waited += Time.unscaledDeltaTime;
                if (_waited > 0.1f && !IslandShown) ShowBackdrop();
                if (IslandShown && (_score != null || _waited > 6f))
                {
                    StartIntro(_after == Phase.Title ? _hitAt - 0.6f : 0f);
                    Enter(_after);
                }
            }
            else if (_phase != Phase.Menu)
            {
                // The intro's clock is the music's while it plays, so pictures, words and notes stay together.
                if (_music.isPlaying && _music.clip == _score) _t = _music.time;
                else _t += Time.unscaledDeltaTime;

                if (_skip) Skip();
                else if (_t < StudioSeconds) _phase = Phase.Studio;
                else if (_t < _hitAt) { _phase = Phase.Story; TrackCard(); }
                else if (_t < _hitAt + LogoSeconds + RiseSeconds) { _phase = Phase.Title; Hit(); }
                else Finish();
            }

            MenuMusic();
            // The flight is for the menu; a match brings its own camera.
            bool menu = NetSession.Instance == null || NetSession.Instance.ShowingMenu;
            if (_cam != null && _cam.gameObject.activeSelf != menu) _cam.gameObject.SetActive(menu);
            Orbit();
        }

        private void Enter(Phase phase) { _phase = phase; _skip = false; }

        private void StartIntro(float at)
        {
            _t = at;
            _hitPlayed = at > _hitAt;
            _spoken = -1;
            _card = 0;
            _cardTime = 0f;
            if (_score == null) return;
            _music.clip = _score;
            _music.volume = MusicVolume;
            _music.time = Mathf.Clamp(at, 0f, _score.length - 0.1f);
            _music.Play();
        }

        // Which card is up, how long it has been, and its narration.
        private void TrackCard()
        {
            int c = 0;
            while (c < _cards.Count - 1 && _t >= _cardStart[c + 1]) c++;
            _card = c;
            _cardTime = _t - _cardStart[c];
            if (_spoken != c)
            {
                _spoken = c;
                if (_cards[c].voice != null) { _voice.Stop(); _voice.clip = _cards[c].voice; _voice.Play(); }
            }
            // The music steps back while the narrator speaks.
            float target = _voice.isPlaying ? DuckedVolume : MusicVolume;
            _music.volume = Mathf.MoveTowards(_music.volume, target, Time.unscaledDeltaTime * 1.5f);
        }

        // The roar with the music's hit, as TETHER slams in.
        private void Hit()
        {
            _music.volume = MusicVolume;
            if (_hitPlayed) return;
            _hitPlayed = true;
            if (_roar != null) _sting.PlayOneShot(_roar, 0.9f);
        }

        // Any key: the story's music jumps to the hit, the narrator stops, and the menu comes up.
        private void Skip()
        {
            _voice.Stop();
            if (_t < _hitAt)
            {
                if (_score != null && _music.clip == _score) _music.time = _hitAt;
                Hit();
            }
            Finish();
        }

        // Played through or skipped: the menu, and not the long version again.
        private void Finish()
        {
            PlayerPrefs.SetInt(SeenKey, 1);
            PlayerPrefs.Save();
            Enter(Phase.Menu);
        }

        private void CollectMusic()
        {
            if (_score == null && _scoreJob != null && _scoreJob.IsCompleted)
            {
                if (_scoreJob.Status == TaskStatus.RanToCompletion) _score = MakeClip("IntroScore", _scoreJob.Result);
                _scoreJob = null;
            }
            if (_bedClip == null && _bedJob != null && _bedJob.IsCompleted)
            {
                if (_bedJob.Status == TaskStatus.RanToCompletion) _bedClip = MakeClip("MenuLoop", _bedJob.Result);
                _bedJob = null;
            }
        }

        // On the menu, the loop takes over once the logo hit has rung out; in a match all of it goes quiet.
        private void MenuMusic()
        {
            bool menu = _phase == Phase.Menu && (NetSession.Instance == null || NetSession.Instance.ShowingMenu);
            if (!menu)
            {
                if (_phase == Phase.Menu) // a match: fade everything out
                {
                    _music.volume = Mathf.MoveTowards(_music.volume, 0f, Time.unscaledDeltaTime * 1.5f);
                    _bed.volume = Mathf.MoveTowards(_bed.volume, 0f, Time.unscaledDeltaTime * 1.5f);
                    if (_music.volume <= 0f) _music.Stop();
                    if (_bed.volume <= 0f) _bed.Stop();
                }
                return;
            }
            bool ringing = _music.isPlaying && _music.clip == _score && _music.time < _score.length - 2f;
            if (!ringing && _bedClip != null && !_bed.isPlaying)
            {
                _bed.clip = _bedClip;
                _bed.volume = 0f;
                _bed.Play();
            }
            if (_bed.isPlaying) _bed.volume = Mathf.MoveTowards(_bed.volume, BedVolume, Time.unscaledDeltaTime * 0.25f);
        }

        private bool IslandShown => _cam != null && _cam.gameObject.activeSelf && IslandWorld.Current != null;

        private static bool AnyKey()
        {
            return (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) ||
                   (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
                   (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
        }

        // The island behind the menu, and the camera that circles it. Also called on the way back from a match.
        public void ShowBackdrop()
        {
            if (_boot == null || (NetSession.Instance != null && !NetSession.Instance.InMenu)) return; // a match started meanwhile
            int seed = Random.Range(0, int.MaxValue);
            var (centre, extent, top) = _boot.BuildPreviewIsland(seed);
            _centre = centre;
            _radius = extent * 0.42f;   // out over the coast, the whole island in view
            _height = top + 30f;          // above the highest peak
            _lookHeight = top * 0.15f;
            _angle  = Random.Range(0f, 360f);
            if (_cam == null)
            {
                var go = new GameObject("Title Camera") { tag = "MainCamera" };
                _cam = go.AddComponent<Camera>();
                _cam.fieldOfView = 50f;
                _cam.farClipPlane = 4000f;
            }
            _cam.gameObject.SetActive(true);
            Orbit();
        }

        // A match is starting (a tool can start one mid-intro): skip the rest.
        public void EndIntro()
        {
            _voice.Stop();
            if (_phase != Phase.Menu) Enter(Phase.Menu);
        }

        // Back on the menu after a match: the island and the slow flight return.
        public void ReturnToMenu()
        {
            Enter(Phase.Menu);
            ShowBackdrop();
        }

        private void Orbit()
        {
            if (_cam == null || !_cam.gameObject.activeSelf) return;
            _angle += Time.unscaledDeltaTime * 1.4f; // a full turn in about four minutes
            var dir = Quaternion.Euler(0f, _angle, 0f) * Vector3.forward;
            var pos = _centre + dir * _radius + Vector3.up * _height;
            _cam.transform.position = pos;
            _cam.transform.rotation = Quaternion.LookRotation((_centre + Vector3.up * _lookHeight) - pos);
            RenderSettings.fogDensity = MenuFog; // the sky's fog is set for walking; from up here it would hide the island
        }

        // ── Drawing ───────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EnsureStyles();
            GUI.depth = -10; // above the menu
            switch (_phase)
            {
                case Phase.Building:
                    Fill(Color.black, 1f);
                    break;
                case Phase.Studio:
                    Fill(Color.black, 1f);
                    DrawLogo(_studio, "VANTWARD GAMES", 0.38f, Screen.height * 0.5f,
                             Fade(_t, 0.2f, 0.2f + FadeSeconds * 0.8f, StudioSeconds - FadeSeconds * 0.8f, StudioSeconds), _bigText);
                    Hint();
                    break;
                case Phase.Story:
                    DrawStory();
                    Hint();
                    break;
                case Phase.Title:
                    DrawLogoReveal();
                    Hint();
                    break;
                case Phase.Menu:
                    if (NetSession.Instance != null && NetSession.Instance.ShowingMenu && !NetSession.Instance.ShowingSettings)
                    {
                        var (y, w) = MenuTitlePlace();
                        DrawTitle(y, w, 1f, 1f, 1f);
                    }
                    break;
            }
        }

        private static float VeilFor(Shot shot) => shot == Shot.Space ? 1f : shot == Shot.Dark ? 0.82f : 0.3f;

        private void DrawStory()
        {
            if (_card >= _cards.Count) { Fill(Color.black, 0.3f); return; }
            var (seconds, text, shot, _, _) = _cards[_card];
            // How dark the island is under the words; it eases between cards rather than cutting.
            float veil = VeilFor(shot);
            if (_cardTime < FadeSeconds)
                veil = Mathf.Lerp(_card > 0 ? VeilFor(_cards[_card - 1].shot) : 1f, veil, _cardTime / FadeSeconds);
            Fill(Color.black, veil);
            if (shot == Shot.Space) DrawStars();

            var c = GUI.color; GUI.color = new Color(1f, 1f, 1f, Fade(_cardTime, 0f, FadeSeconds, seconds - FadeSeconds, seconds));
            GUI.Label(new Rect(Screen.width * 0.12f, Screen.height * 0.64f, Screen.width * 0.76f, 120f), text, _story);
            GUI.color = c;
        }

        // Faint, twinkling stars over black.
        private void DrawStars()
        {
            var c = GUI.color;
            for (int i = 0; i < _stars.Length; i++)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.25f + 0.25f * Mathf.Sin(Time.unscaledTime * 1.7f + i * 2.3f));
                GUI.DrawTexture(new Rect(_stars[i].x * Screen.width, _stars[i].y * Screen.height * 0.6f, 2f, 2f), Texture2D.whiteTexture);
            }
            GUI.color = c;
        }

        // TETHER slams in as its line draws left to right, PRIMAL fades in under it, then both ease up to the menu.
        private void DrawLogoReveal()
        {
            float t = _t - _hitAt;
            Fill(Color.black, Mathf.Lerp(0.65f, 0.3f, Mathf.Clamp01((t - LogoSeconds) / RiseSeconds)));
            float slam = Mathf.Clamp01(t / 0.35f);
            float scale = Mathf.Lerp(1.18f, 1f, 1f - (1f - slam) * (1f - slam));
            float wipe = Mathf.Clamp01(t / 0.9f);
            float primal = Mathf.Clamp01((t - 1.3f) / 1f);
            float rise = Mathf.Clamp01((t - LogoSeconds) / RiseSeconds);
            rise = rise * rise * (3f - 2f * rise);
            var (menuY, menuW) = MenuTitlePlace();
            DrawTitle(Mathf.Lerp(Screen.height * 0.45f, menuY, rise), Mathf.Lerp(0.55f, menuW, rise), scale, wipe, primal);
        }

        // Where the title sits above the menu box: centred in the gap, sized to fit it.
        private static (float y, float widthShare) MenuTitlePlace()
        {
            float gap = Mathf.Max(90f, Screen.height * 0.5f - NetSession.MenuTopOffset);
            float w = Mathf.Min(0.36f * Screen.width, gap * 0.8f / 0.42f); // TETHER over PRIMAL is about 0.42 of its width tall
            return (gap * 0.5f, w / Screen.width);
        }

        // TETHER with PRIMAL under it, centred at y, widthShare of the screen wide. wipe reveals TETHER left to right.
        private void DrawTitle(float y, float widthShare, float scale, float wipe, float primalAlpha)
        {
            var c = GUI.color;
            if (_tether == null)
            {
                GUI.color = new Color(1f, 0.85f, 0.45f, wipe);
                GUI.Label(new Rect(0, y - 40f, Screen.width, 60f), "TETHER", _bigText);
                GUI.color = new Color(1f, 1f, 1f, primalAlpha);
                GUI.Label(new Rect(0, y + 18f, Screen.width, 30f), "P R I M A L", _tag);
                GUI.color = c;
                return;
            }
            float w = Screen.width * widthShare;
            float tw = w * scale, th = tw * _tether.height / _tether.width;
            float pw = w * 0.47f, ph = _primal != null ? pw * _primal.height / _primal.width : 0f;
            float top = y - (th + ph * 0.9f) * 0.5f;
            GUI.DrawTextureWithTexCoords(new Rect((Screen.width - tw) * 0.5f, top, tw * wipe, th), _tether, new Rect(0f, 0f, wipe, 1f));
            if (_primal != null && primalAlpha > 0f)
            {
                GUI.color = new Color(1f, 1f, 1f, primalAlpha);
                GUI.DrawTexture(new Rect((Screen.width - pw) * 0.5f, top + th * 0.98f, pw, ph), _primal, ScaleMode.ScaleToFit, true);
                GUI.color = c;
            }
        }

        // Draws a logo centred at centreY, widthShare of the screen wide, or text when it's missing.
        private static void DrawLogo(Texture2D tex, string fallback, float widthShare, float centreY, float alpha, GUIStyle style)
        {
            var c = GUI.color; GUI.color = new Color(1f, 1f, 1f, alpha);
            if (tex != null)
            {
                float w = Screen.width * widthShare, h = w * tex.height / tex.width;
                GUI.DrawTexture(new Rect((Screen.width - w) * 0.5f, centreY - h * 0.5f, w, h), tex, ScaleMode.ScaleToFit, true);
            }
            else GUI.Label(new Rect(0, centreY - 30f, Screen.width, 60f), fallback, style);
            GUI.color = c;
        }

        private void Hint()
        {
            var c = GUI.color; GUI.color = new Color(1f, 1f, 1f, 0.45f);
            GUI.Label(new Rect(0, Screen.height - 34f, Screen.width - 20f, 24f), "Press any key to skip", _hint);
            GUI.color = c;
        }

        private static void Fill(Color colour, float alpha)
        {
            var c = GUI.color; GUI.color = new Color(colour.r, colour.g, colour.b, alpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = c;
        }

        // 0 before inStart, rises to 1 at inEnd, holds, falls to 0 between outStart and outEnd.
        private static float Fade(float t, float inStart, float inEnd, float outStart, float outEnd)
        {
            if (t < inEnd) return Mathf.Clamp01((t - inStart) / Mathf.Max(0.01f, inEnd - inStart));
            if (t > outStart) return Mathf.Clamp01(1f - (t - outStart) / Mathf.Max(0.01f, outEnd - outStart));
            return 1f;
        }

        private void EnsureStyles()
        {
            if (_bigText != null) return;
            _bigText = new GUIStyle(GUI.skin.label) { fontSize = 46, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _bigText.normal.textColor = new Color(0.92f, 0.9f, 0.85f);
            _story = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            _story.normal.textColor = new Color(0.95f, 0.93f, 0.88f);
            _hint = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleRight };
            _hint.normal.textColor = Color.white;
            _tag = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _tag.normal.textColor = new Color(0.95f, 0.93f, 0.88f);
        }
    }
}
