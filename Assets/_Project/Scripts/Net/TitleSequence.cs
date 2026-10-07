using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using ProjectFossil.Core;
using ProjectFossil.Match;

namespace ProjectFossil.Net
{
    // What a player sees from launch to the menu (Docs/STORY.md): the studio logo on black, the story told in short
    // cards over a live island the camera slowly circles, then the TETHER logo slams in, PRIMAL under it, and the two
    // ease up to the top of the screen as the menu appears. The island keeps turning behind the menu, and returns
    // after a match. The first launch plays it all; later launches start at the logo. Any key or click skips.
    public class TitleSequence : MonoBehaviour
    {
        public static TitleSequence Instance { get; private set; }
        // The menu waits until this is true.
        public static bool MenuReady => Instance == null || Instance._phase == Phase.Menu;

        private const float MenuFog = 0.0016f;
        private const string SeenKey = "ProjectFossil.IntroSeen";
        private const float StudioSeconds = 3f, LogoSeconds = 4f, RiseSeconds = 1.2f, FadeSeconds = 1f;

        private enum Phase { Studio, Building, Story, Title, Menu }
        private enum Shot { Flight, Space, Dark }

        private Phase _phase = Phase.Studio;
        private float _phaseTime;
        private bool _skip;

        private MatchBootstrap _boot;
        private Camera _cam;
        private Vector3 _centre;
        private float _radius, _height, _angle, _lookHeight;

        private readonly List<(float seconds, string text, Shot shot)> _cards = new List<(float, string, Shot)>();
        private int _card;
        private float _cardTime;

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
            if (PlayerPrefs.GetInt(SeenKey, 0) == 1) StartCoroutine(BuildBackdrop(Phase.Title)); // seen it: logo only
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SettingsPanel.PlayIntro = null;
        }

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
                _cards.Add((s, parts[1].Trim(), shot == "space" ? Shot.Space : shot == "dark" ? Shot.Dark : Shot.Flight));
            }
        }

        // Settings' "Play intro": the whole thing again, from the studio logo.
        public void PlayIntro()
        {
            if (NetSession.Instance != null && !NetSession.Instance.InMenu) return;
            _card = 0;
            _cardTime = 0f;
            Enter(Phase.Studio);
        }

        // ── Flow ──────────────────────────────────────────────────────────────

        private void Update()
        {
            _phaseTime += Time.unscaledDeltaTime;
            if (_phase != Phase.Menu && _phase != Phase.Building && AnyKey()) _skip = true;

            switch (_phase)
            {
                case Phase.Studio:
                    if (_skip) { Finish(); break; }
                    if (_phaseTime > StudioSeconds)
                    {
                        if (IslandShown) Enter(Phase.Story); // replayed from Settings: the island is already there
                        else StartCoroutine(BuildBackdrop(Phase.Story));
                    }
                    break;
                case Phase.Story:
                    if (_skip) { Finish(); break; }
                    _cardTime += Time.unscaledDeltaTime;
                    if (_card < _cards.Count && _cardTime > _cards[_card].seconds) { _card++; _cardTime = 0f; }
                    if (_card >= _cards.Count) Enter(Phase.Title);
                    break;
                case Phase.Title:
                    if (_skip || _phaseTime > LogoSeconds + RiseSeconds) Finish();
                    break;
            }
            // The flight is for the menu; a match brings its own camera.
            bool menu = NetSession.Instance == null || NetSession.Instance.ShowingMenu;
            if (_cam != null && _cam.gameObject.activeSelf != menu) _cam.gameObject.SetActive(menu);
            Orbit();
        }

        private void Enter(Phase phase) { _phase = phase; _phaseTime = 0f; _skip = false; }

        // Skipped or played through: straight to the menu, with an island behind it, and not the long version again.
        private void Finish()
        {
            PlayerPrefs.SetInt(SeenKey, 1);
            PlayerPrefs.Save();
            if (IslandShown) Enter(Phase.Menu);
            else StartCoroutine(BuildBackdrop(Phase.Menu));
        }

        private bool IslandShown => _cam != null && IslandWorld.Current != null;

        private static bool AnyKey()
        {
            return (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) ||
                   (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
                   (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
        }

        // Two black frames first, so the screen is clear while the island is built (that takes a few seconds).
        private IEnumerator BuildBackdrop(Phase next)
        {
            Enter(Phase.Building);
            yield return null;
            yield return null;
            ShowBackdrop();
            Enter(next);
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
            StopAllCoroutines();
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
                case Phase.Studio:
                case Phase.Building:
                    Fill(Color.black, 1f);
                    if (_phase == Phase.Studio)
                        DrawLogo(_studio, "VANTWARD GAMES", 0.38f, Screen.height * 0.5f,
                                 Fade(_phaseTime, 0.2f, 0.2f + FadeSeconds * 0.8f, StudioSeconds - FadeSeconds * 0.8f, StudioSeconds), _bigText);
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
            var (seconds, text, shot) = _cards[_card];
            // How dark the island is under the words; it eases between cards rather than cutting.
            float veil = VeilFor(shot);
            if (_cardTime < FadeSeconds)
                veil = Mathf.Lerp(_card > 0 ? VeilFor(_cards[_card - 1].shot) : 1f, veil, _cardTime / FadeSeconds);
            Fill(Color.black, veil);
            if (shot == Shot.Space) DrawSpace(_cardTime / seconds);

            var c = GUI.color; GUI.color = new Color(1f, 1f, 1f, Fade(_cardTime, 0f, FadeSeconds, seconds - FadeSeconds, seconds));
            GUI.Label(new Rect(Screen.width * 0.12f, Screen.height * 0.64f, Screen.width * 0.76f, 120f), text, _story);
            GUI.color = c;
        }

        // Faint stars, and a bright streak crossing high and passing by: the asteroid that missed.
        private void DrawSpace(float t)
        {
            var c = GUI.color;
            for (int i = 0; i < _stars.Length; i++)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.25f + 0.25f * Mathf.Sin(Time.unscaledTime * 1.7f + i * 2.3f));
                GUI.DrawTexture(new Rect(_stars[i].x * Screen.width, _stars[i].y * Screen.height * 0.6f, 2f, 2f), Texture2D.whiteTexture);
            }
            float k = Mathf.Clamp01((t - 0.15f) / 0.55f); // crosses in the middle of the card
            if (k > 0f && k < 1f)
            {
                var head = new Vector2(Mathf.Lerp(-0.1f, 1.1f, k) * Screen.width, Mathf.Lerp(0.12f, 0.38f, k) * Screen.height);
                var dir = new Vector2(1.2f * Screen.width, 0.26f * Screen.height).normalized;
                const int Trail = 90;
                for (int i = 0; i < Trail; i++)
                {
                    var p = head - dir * i * 6f;
                    float size = Mathf.Lerp(9f, 1.5f, (float)i / Trail);
                    GUI.color = new Color(1f, 0.9f - i * 0.004f, 0.7f - i * 0.006f, 1f - (float)i / Trail);
                    GUI.DrawTexture(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), Texture2D.whiteTexture);
                }
            }
            GUI.color = c;
        }

        // TETHER slams in as its line draws left to right, PRIMAL fades in under it, then both ease up to the menu.
        private void DrawLogoReveal()
        {
            float t = _phaseTime;
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
