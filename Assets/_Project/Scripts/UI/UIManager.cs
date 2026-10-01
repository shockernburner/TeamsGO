using System.Collections.Generic;
using UnityEngine;
using ProjectFossil.Core;
using ProjectFossil.Dinosaurs;
using ProjectFossil.Economy;
using ProjectFossil.Match;
using ProjectFossil.Player;

namespace ProjectFossil.UI
{
    // Placeholder IMGUI front end: HUD, announcements, interact prompt, shop and results screen.
    // Deliberately code-only so it needs no scene or prefab wiring; replace with real UI once the loop is fun.
    public class UIManager : MonoBehaviour
    {
        public float announcementSeconds = 4f;
        public float controlHintSeconds  = 60f;   // key hints show at the start of a match, then get out of the way
        public float damageFlashSeconds  = 0.45f;

        private MatchBootstrap   _bootstrap;
        private MatchManager     _match;
        private GameObject       _boundPlayer;
        private PlayerMenuInput  _menuInput;
        private PlayerInteractor _interactor;
        private PlayerCombat     _combat;
        private CurrencySystem   _boundWallet;
        private Health           _boundHealth;
        private float            _damageFlash;   // 0..1, fades after the player is hurt
        private float            _damageSide;    // -1 left, 0 front/back, +1 right

        private bool _shopOpen;
        private string _shopMessage;
        private Vector2 _shopScroll;

        private readonly List<(string text, float until)> _messages = new List<(string, float)>();

        // Dinosaurs recently hit by the player show a health bar over their heads for a few seconds.
        public float healthBarSeconds = 4f;
        private readonly Dictionary<DinosaurAI, float> _barsUntil = new Dictionary<DinosaurAI, float>();
        private readonly List<DinosaurAI> _barScratch = new List<DinosaurAI>();

        // Coins earned in the last moments, shown as "+N" beside the coin count.
        private int   _coinBurst;
        private float _coinBurstUntil;

        private GUIStyle _big, _center, _box, _slot, _panel, _small, _hint, _barText, _marker, _banner, _alert;
        private Texture2D _arrow, _vignette;

        // Fear on screen: dark red edges that close in with the danger level, smoothed so they breathe.
        private float _danger;
        // After a successful extraction the victory banner holds the screen while the helicopter climbs away.
        public float victorySeconds = 5.5f;
        public float deathFadeSeconds = 2.5f;
        private float _endedAt = float.NegativeInfinity;

        // Auto-create alongside any MatchBootstrap so no scene edits are needed.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindFirstObjectByType<UIManager>() != null) return;
            if (FindFirstObjectByType<MatchBootstrap>() == null) return;
            new GameObject("UIManager").AddComponent<UIManager>();
        }

        private void OnEnable()
        {
            DinosaurAI.Damaged += OnDinosaurDamaged;
            DinosaurAI.Killed  += OnDinosaurKilled;
        }

        private void OnDisable()
        {
            DinosaurAI.Damaged -= OnDinosaurDamaged;
            DinosaurAI.Killed  -= OnDinosaurKilled;
        }

        private void OnDinosaurDamaged(DinosaurAI dino, DamageInfo info)
        {
            if (dino != null && info.Source != null && info.Source == _boundPlayer)
                _barsUntil[dino] = Time.unscaledTime + healthBarSeconds;
        }

        private void OnDinosaurKilled(DinosaurAI dino, GameObject killer)
        {
            if (dino == null) return;
            _barsUntil.Remove(dino);
            if (killer != null && killer == _boundPlayer && dino.species != null)
                Push($"{dino.species.speciesName} down  +{dino.species.scoreValue} score");
        }

        // ── Binding ────────────────────────────────────────────────────────────

        private void Update()
        {
            if (_bootstrap == null) _bootstrap = FindFirstObjectByType<MatchBootstrap>();
            if (_bootstrap == null) return;

            if (_match != _bootstrap.Match)
            {
                if (_match != null)
                {
                    _match.Announced  -= Push;
                    _match.MatchEnded -= OnMatchEnded;
                }
                _match = _bootstrap.Match;
                if (_match != null)
                {
                    _match.Announced  += Push;
                    _match.MatchEnded += OnMatchEnded;
                }
            }

            if (_match != null && _match.Player != _boundPlayer) BindPlayer(_match.Player);

            _messages.RemoveAll(m => m.until < Time.unscaledTime);
            if (_damageFlash > 0f) _damageFlash = Mathf.Max(0f, _damageFlash - Time.deltaTime / damageFlashSeconds);

            float target = 0f;
            if (_match != null && _match.IsRunning && _boundPlayer != null)
            {
                target = DinosaurAI.DangerAt(_boundPlayer.transform.position);
                if (_match.IsFinalStand) target = Mathf.Max(target, 0.5f);
            }
            _danger = Mathf.MoveTowards(_danger, target, Time.deltaTime * (target > _danger ? 2f : 0.5f));
        }

        private void BindPlayer(GameObject player)
        {
            if (_menuInput != null)   _menuInput.ShopToggled -= ToggleShop;
            if (_boundWallet != null) _boundWallet.BalanceChanged -= OnBalanceChanged;
            if (_combat != null)      _combat.Attacked -= OnAttacked;
            if (_boundHealth != null) _boundHealth.Damaged -= OnPlayerDamaged;

            _boundPlayer = player;
            _menuInput   = player != null ? player.GetComponent<PlayerMenuInput>() : null;
            _interactor  = player != null ? player.GetComponent<PlayerInteractor>() : null;
            _combat      = player != null ? player.GetComponent<PlayerCombat>() : null;
            _boundWallet = _match.PlayerInventory != null ? _match.PlayerInventory.Wallet : null;
            _boundHealth = _match.PlayerHealth;
            _shopOpen    = false;
            _damageFlash = 0f;
            _messages.Clear();

            if (_boundHealth != null) _boundHealth.Damaged += OnPlayerDamaged;

            if (_menuInput != null)   _menuInput.ShopToggled += ToggleShop;
            if (_boundWallet != null) _boundWallet.BalanceChanged += OnBalanceChanged;
            if (_combat != null)      _combat.Attacked += OnAttacked;
        }

        private void OnDestroy()
        {
            if (_match != null)
            {
                _match.Announced  -= Push;
                _match.MatchEnded -= OnMatchEnded;
            }
            if (_menuInput != null)   _menuInput.ShopToggled -= ToggleShop;
            if (_boundWallet != null) _boundWallet.BalanceChanged -= OnBalanceChanged;
            if (_combat != null)      _combat.Attacked -= OnAttacked;
            if (_boundHealth != null) _boundHealth.Damaged -= OnPlayerDamaged;
        }

        private void OnPlayerDamaged(DamageInfo info)
        {
            _damageFlash = 1f;
            _damageSide  = 0f;
            if (info.Source != null && _boundPlayer != null)
            {
                Vector3 to = info.Source.transform.position - _boundPlayer.transform.position;
                float side = Vector3.Dot(_boundPlayer.transform.right, to.normalized);
                _damageSide = Mathf.Abs(side) > 0.35f ? Mathf.Sign(side) : 0f;
            }
        }

        // Hits show on the target's own health bar; kills are announced by OnDinosaurKilled.
        private void OnAttacked(WeaponStats weapon, Health target) { }

        private void Push(string text) => _messages.Add((text, Time.unscaledTime + announcementSeconds));

        private void OnBalanceChanged(int balance, int delta)
        {
            if (delta <= 0) return;
            _coinBurst = Time.unscaledTime < _coinBurstUntil ? _coinBurst + delta : delta;
            _coinBurstUntil = Time.unscaledTime + 2f;
            if (delta >= 10) Push($"+{delta} coins"); // loot and rewards; small per-hit coins only show by the counter
        }

        private void OnMatchEnded(MatchStats stats)
        {
            _endedAt  = Time.unscaledTime;
            _shopOpen = false;
            SetPlayerBlocked(true);
        }

        private void ToggleShop()
        {
            if (_match == null || !_match.IsRunning) return;
            _shopOpen    = !_shopOpen;
            _shopMessage = null;
            SetPlayerBlocked(_shopOpen);
        }

        private void SetPlayerBlocked(bool blocked)
        {
            if (_match != null && _match.PlayerController != null)
                _match.PlayerController.InputBlocked = blocked;
        }

        // ── Drawing ────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (_match == null || _match.State == null) return;
            EnsureStyles();

            if (_match.IsRunning)
            {
                DrawDanger();
                DrawHealthBars();
                DrawDamageFlash();
                if (!_shopOpen) DrawCrosshair();
                if (!_shopOpen) DrawBeaconMarker();
                DrawHud();
                DrawAlerts();
                DrawPrompt();
                if (_shopOpen) DrawShop();
            }
            else
            {
                // The end plays out like a film: the edges close in, the picture fades to black, then the results
                // and the menu come up on the black. Escaping gets a moment with the banner first.
                float since = Time.unscaledTime - _endedAt;
                bool won = _match.Stats != null && _match.Stats.Result == MatchResult.Extracted;
                float end = won ? victorySeconds : deathFadeSeconds;
                DrawEndFade(Mathf.Clamp01(since / end), Mathf.Clamp01((since - (end - 1.5f)) / 1.5f));
                if (since < end) { if (won) DrawVictory(since); }
                else DrawResults(_match.Stats);
            }

            DrawMessages();
        }

        private void DrawHud()
        {
            var state = _match.State;
            var health = _match.PlayerHealth;
            var controller = _match.PlayerController;
            var inv = _match.PlayerInventory;

            // Compact status panel, top-left.
            GUILayout.BeginArea(new Rect(10, 10, 230, 168), _panel);
            if (health != null) Bar("HP", health.Current, health.Max, new Color(0.85f, 0.2f, 0.2f));
            if (controller != null)
            {
                bool tired = controller.IsExhausted;
                Bar(tired ? "Tired" : "Stam", controller.Stamina, controller.maxStamina,
                    tired ? new Color(0.95f, 0.55f, 0.1f) : new Color(0.3f, 0.7f, 0.9f));
            }
            if (inv != null)
            {
                string burst = Time.unscaledTime < _coinBurstUntil ? $" (+{_coinBurst})" : "";
                GUILayout.Label($"Coins {inv.Wallet.Balance}{burst}    Time {FormatTime(state.Remaining)}", _small);
            }
            string rank = _match.Rank != null ? $"    Rank {_match.Rank.Level}" : "";
            GUILayout.Label($"Score {_match.LiveScore}{rank}", _small);

            if (!state.IsExtractionOpen)
                GUILayout.Label($"Extraction in {FormatTime(state.ExtractionOpensAt - state.Elapsed)}", _small);
            else if (state.IsExtracting)
                GUILayout.Label($"EXTRACTING {Mathf.CeilToInt(state.ExtractionHoldTime - state.ExtractionProgress)}s", _small);
            else
                GUILayout.Label("Extraction OPEN: follow the green marker", _small);

            string weapon = _combat != null ? _combat.CurrentWeapon.Name : "";
            string move   = controller != null ? MovementLabel(controller) : "";
            GUILayout.Label($"{weapon}  |  {move}", _small);
            GUILayout.EndArea();

            if (state.Elapsed < controlHintSeconds || _shopOpen)
            {
                const string hints = "[Shift] Run on/off  [C] Crouch  [Z] Crawl  [Space] Jump/stand  " +
                                     "[LMB/F] Attack  [E] Interact  [Q] Heal  [Tab] Shop";
                GUI.Label(new Rect(0, Screen.height - 110, Screen.width, 22), hints, _hint);
            }

            if (inv != null) DrawInventory(inv.Inventory);
        }

        private void DrawInventory(Inventory inventory)
        {
            const float slot = 64f;
            float width = inventory.SlotCount * (slot + 4f);
            float x = (Screen.width - width) * 0.5f;
            float y = Screen.height - slot - 12f;

            for (int i = 0; i < inventory.SlotCount; i++)
            {
                var r = new Rect(x + i * (slot + 4f), y, slot, slot);
                string label = "";
                if (i < inventory.Stacks.Count)
                {
                    var s = inventory.Stacks[i];
                    label = s.Amount > 1 ? $"{s.Item.displayName}\nx{s.Amount}" : s.Item.displayName;
                }
                GUI.Box(r, label, _slot);
            }
        }

        private void DrawPrompt()
        {
            if (_shopOpen || _interactor == null) return;
            string prompt = _interactor.CurrentPrompt;
            if (string.IsNullOrEmpty(prompt)) return;
            GUI.Label(new Rect(0, Screen.height * 0.6f, Screen.width, 30), $"[E] {prompt}", _center);
        }

        private void DrawShop()
        {
            var inv = _match.PlayerInventory;
            var catalog = _match.Content != null ? _match.Content.shopCatalog : null;
            if (inv == null || catalog == null) return;

            var area = new Rect(Screen.width * 0.5f - 220, Screen.height * 0.5f - 200, 440, 400);
            GUILayout.BeginArea(area, _box);
            GUILayout.Label($"SHOP    Coins: {inv.Wallet.Balance}    Slots: {inv.Inventory.SlotCount}/{inv.Inventory.MaxSlotCount}", _big);

            _shopScroll = GUILayout.BeginScrollView(_shopScroll);
            foreach (var offer in catalog.offers)
            {
                var check = ShopService.Evaluate(offer, inv.Wallet, inv.Inventory);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{offer.displayName}", GUILayout.Width(250));
                GUI.enabled = check == ShopResult.Success;
                string buy = check == ShopResult.AlreadyOwned ? "Owned" : $"Buy ({offer.price})";
                if (GUILayout.Button(buy, GUILayout.Width(120)))
                {
                    var result = ShopService.TryBuy(offer, inv.Wallet, inv.Inventory);
                    _shopMessage = result == ShopResult.Success ? $"Bought {offer.displayName}" : Describe(result);
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(_shopMessage)) GUILayout.Label(_shopMessage);
            if (GUILayout.Button("Close [Tab]")) ToggleShop();
            GUILayout.EndArea();
        }

        private void DrawResults(MatchStats stats)
        {
            if (stats == null) return;

            var area = new Rect(Screen.width * 0.5f - 210, Screen.height * 0.5f - 245, 420, 490);
            GUILayout.BeginArea(area, _box);
            GUILayout.Label(Headline(stats.Result), _big);
            GUILayout.Label($"SCORE {stats.Score}" + (stats.NewBest ? "   NEW BEST!" : $"   (best {stats.BestScore})"), _big);
            GUILayout.Space(6);
            GUILayout.Label($"Survived {FormatTime(stats.TimeSurvived)}: {stats.SurvivalPoints}");
            GUILayout.Label($"Kills ({stats.DinosKilled}): {stats.KillPoints}");
            GUILayout.Label($"Damage dealt: {stats.DamagePoints}");
            GUILayout.Label($"Threats faced ({stats.ThreatsFaced}): {stats.ThreatPoints}");
            GUILayout.Label($"{ResultLabel(stats.Result)}: x{stats.ResultMultiplier:0.##}" +
                            (stats.MatesAboard > 0 ? $"  (with {stats.MatesAboard} teammate{(stats.MatesAboard > 1 ? "s" : "")} aboard)" : ""));
            GUILayout.Space(6);
            GUILayout.Label(RankLine(stats));
            GUILayout.Label($"Coins earned: {stats.CoinsEarned}    Island seed: {stats.Seed}");
            GUILayout.Space(12);
            if (_bootstrap != null && !_bootstrap.AllowsRestart)
            {
                GUILayout.Label(_bootstrap.RestartNote ?? "Waiting for the next island.", _big);
            }
            else
            {
                if (!string.IsNullOrEmpty(_bootstrap != null ? _bootstrap.RestartNote : null))
                    GUILayout.Label(_bootstrap.RestartNote, _small);
                if (GUILayout.Button("Play again (new island)", GUILayout.Height(36)) && _bootstrap != null)
                    _bootstrap.Restart(true);
                if (GUILayout.Button("Replay same island", GUILayout.Height(28)) && _bootstrap != null)
                    _bootstrap.Restart(false);
            }
            GUILayout.EndArea();
        }

        private void DrawMessages()
        {
            float y = 12f;
            for (int i = _messages.Count - 1; i >= 0 && i >= _messages.Count - 5; i--)
            {
                GUI.Label(new Rect(Screen.width - 330, y, 320, 24), _messages[i].text, _box);
                y += 27f;
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        // One-line bar: short label on the left, filled bar with the number on it.
        private void Bar(string label, float value, float max, Color color)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _small, GUILayout.Width(38));
            Rect r = GUILayoutUtility.GetRect(170, 14, GUILayout.ExpandWidth(true));
            r.y += 3f;
            GUI.DrawTexture(r, Texture2D.grayTexture);
            var old = GUI.color;
            GUI.color = color;
            Rect fill = r;
            fill.width *= max > 0f ? Mathf.Clamp01(value / max) : 0f;
            GUI.DrawTexture(fill, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(r, $"{Mathf.CeilToInt(value)}/{Mathf.CeilToInt(max)}", _barText);
            GUILayout.EndHorizontal();
        }

        private void DrawCrosshair()
        {
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.8f);
            GUI.DrawTexture(new Rect(cx - 1f, cy - 7f, 2f, 14f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 7f, cy - 1f, 14f, 2f), Texture2D.whiteTexture);
            GUI.color = old;
        }

        // Hurt: the screen's edges flush dark red for a moment, a little stronger on the side the hit came from.
        // Soft, like blood in the eyes, not painted bars; the red wedge already points at the attacker.
        private void DrawDamageFlash()
        {
            if (_damageFlash <= 0f) return;
            var old = GUI.color;
            float a = 0.5f * _damageFlash;
            var tex = VignetteTexture();
            GUI.color = new Color(0.55f, 0f, 0f, a);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), tex, ScaleMode.StretchToFill);
            if (_damageSide != 0f)
            {
                float w = Screen.width * 0.6f;
                float x = _damageSide < 0f ? -w * 0.5f : Screen.width - w * 0.5f;
                GUI.color = new Color(0.55f, 0f, 0f, a * 0.6f);
                GUI.DrawTexture(new Rect(x, 0, w, Screen.height), tex, ScaleMode.StretchToFill);
            }
            GUI.color = old;
        }

        // Once extraction opens: the nearest green beacon, marked on screen with its distance. Off screen, the marker
        // sits on the edge with an arrow pointing the way to turn.
        private void DrawBeaconMarker()
        {
            var cam = Camera.main;
            var state = _match.State;
            if (cam == null || !state.IsExtractionOpen || _match.PlayerController == null) return;

            Vector3 player = _match.PlayerController.transform.position;
            ExtractionZone nearest = null;
            float best = float.MaxValue;
            foreach (var zone in _match.ExtractionZones)
            {
                if (zone == null) continue;
                float d = Vector3.Distance(player, zone.transform.position);
                if (d < best) { best = d; nearest = zone; }
            }
            if (nearest == null) return;

            Vector3 sp = cam.WorldToScreenPoint(nearest.transform.position + Vector3.up * 3f);
            Vector2 at = ScreenMarker.Place(sp, Screen.width, Screen.height, 48f, out bool onScreen);
            float gx = at.x, gy = Screen.height - at.y; // GUI space: origin top-left

            var old = GUI.color;
            var green = new Color(0.35f, 1f, 0.45f);
            if (!onScreen)
            {
                Vector2 dir = at - new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                float angle = Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg + 90f; // arrow texture points up
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(angle, new Vector2(gx, gy));
                GUI.color = green;
                GUI.DrawTexture(new Rect(gx - 14f, gy - 14f, 28f, 28f), ArrowTexture());
                GUI.matrix = m;
                // Label sits on the inside of the arrow so it never runs off screen.
                gx -= Mathf.Sign(dir.x) * 46f * Mathf.Abs(dir.normalized.x);
                gy += Mathf.Sign(dir.y) * 30f * Mathf.Abs(dir.normalized.y);
            }
            else
            {
                GUI.color = green;
                GUI.DrawTexture(new Rect(gx - 6f, gy - 6f, 12f, 12f), Texture2D.whiteTexture);
                gy -= 22f;
            }
            GUI.color = old;
            GUI.Label(new Rect(gx - 60f, gy - 11f, 120f, 22f), $"BEACON {Mathf.RoundToInt(best)} m", _marker);
        }

        // Dark red closing in from the edges as danger rises; at its peak it pulses with the heartbeat.
        private void DrawDanger()
        {
            if (_danger <= 0.02f) return;
            float pulse = _danger > 0.6f ? 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * Mathf.Lerp(7f, 15f, _danger)) : 1f;
            var old = GUI.color;
            GUI.color = new Color(0.2f, 0f, 0f, Mathf.Clamp01(_danger * 0.2f * pulse)); // darkness at the edges, not a red wash
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), VignetteTexture(), ScaleMode.StretchToFill);
            GUI.color = old;
        }

        // Centre-screen warnings: something has your scent; the last stand at the helicopter.
        private void DrawAlerts()
        {
            var state = _match.State;
            float y = Screen.height * 0.18f;

            if (_match.PlayerHealth != null && _match.PlayerHealth.IsDown)
            {
                float t = Mathf.Max(0f, _match.BleedOutLeft);
                GUI.Label(new Rect(0f, y - 34f, Screen.width, 32f),
                          $"YOU'RE DOWN  {Mathf.CeilToInt(t)}s   Crawl to cover. A teammate can get you up.", _banner);
                y += 40f;
            }

            if (state.IsExtracting)
            {
                float left = state.ExtractionHoldTime - state.ExtractionProgress;
                float w = Mathf.Min(460f, Screen.width - 40f);
                var r = new Rect((Screen.width - w) * 0.5f, y, w, 34f);
                GUI.Label(new Rect(r.x, r.y - 34f, r.width, 32f), $"BOARDING: HOLD THE PAD  {Mathf.CeilToInt(left)}s", _banner);
                var old = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = new Color(0.3f, 0.95f, 0.4f);
                GUI.DrawTexture(new Rect(r.x + 3f, r.y + 3f, (r.width - 6f) * (state.ExtractionProgress / state.ExtractionHoldTime), r.height - 6f),
                                Texture2D.whiteTexture);
                GUI.color = old;
                return;
            }
            if (_match.IsFinalStand)
            {
                GUI.Label(new Rect(0, y, Screen.width, 32f), "GET BACK TO THE HELICOPTER", _banner);
                return;
            }

            if (_boundPlayer != null && DinosaurAI.IsBeingTracked(_boundPlayer.transform))
            {
                var c = _match.PlayerController;
                bool hidden = c != null && c.IsHidden;
                float a = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 4f);
                var old = _alert.normal.textColor;
                _alert.normal.textColor = new Color(1f, 0.35f, 0.25f, a);
                GUI.Label(new Rect(0, y, Screen.width, 26f),
                          hidden ? "It's sniffing around. STAY LOW. DON'T MOVE." : "SOMETHING HAS YOUR SCENT", _alert);
                _alert.normal.textColor = old;
            }
        }

        // Closing vignette, then black over everything.
        private void DrawEndFade(float vignette, float black)
        {
            var old = GUI.color;
            var full = new Rect(0, 0, Screen.width, Screen.height);
            if (vignette > 0f)
            {
                GUI.color = new Color(0f, 0f, 0f, vignette);
                GUI.DrawTexture(full, VignetteTexture());
            }
            if (black > 0f)
            {
                GUI.color = new Color(0f, 0f, 0f, black);
                GUI.DrawTexture(full, Texture2D.whiteTexture);
            }
            GUI.color = old;
        }

        // "YOU MADE IT OUT": the moment the helicopter lifts away with you on the ladder.
        private void DrawVictory(float t)
        {
            var old = GUI.color;
            float fadeIn = Mathf.Clamp01(t / 0.4f);
            GUI.color = new Color(1f, 0.95f, 0.7f, 0.18f * (1f - Mathf.Clamp01(t / 1.2f))); // flash
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, fadeIn);

            float scale = 1f + 0.25f * Mathf.Exp(-t * 4f) + 0.03f * Mathf.Sin(t * 6f);
            var matrix = GUI.matrix;
            var centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.3f);
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), centre);
            int size = _banner.fontSize;
            _banner.fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.width / 14f, 36f, 96f));
            GUI.Label(new Rect(0, centre.y - 60f, Screen.width, 120f), "YOU MADE IT OUT!", _banner);
            _banner.fontSize = size;
            GUI.matrix = matrix;

            var s = _match.Stats;
            GUI.Label(new Rect(0, centre.y + 70f, Screen.width, 30f),
                      $"SCORE {s.Score}" + (s.NewBest ? "   NEW BEST!" : "") + $"    {s.DinosKilled} kills    survived {FormatTime(s.TimeSurvived)}",
                      _center);
            GUI.color = old;
        }

        private Texture2D VignetteTexture()
        {
            if (_vignette != null) return _vignette;
            const int n = 64;
            _vignette = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx * 0.8f + dy * dy);
                float a = Mathf.Clamp01((d - 0.55f) / 0.6f);
                _vignette.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
            }
            _vignette.Apply();
            return _vignette;
        }

        private Texture2D ArrowTexture()
        {
            if (_arrow != null) return _arrow;
            const int n = 32;
            _arrow = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                float half = (n - 1 - y) * 0.5f * 0.9f; // row 0 is the bottom: wide base, point at the top
                for (int x = 0; x < n; x++)
                    px[y * n + x] = Mathf.Abs(x - (n - 1) * 0.5f) <= half ? new Color32(255, 255, 255, 255)
                                                                          : new Color32(255, 255, 255, 0);
            }
            _arrow.SetPixels32(px);
            _arrow.Apply();
            return _arrow;
        }

        // Small bars over dinosaurs the player hit recently.
        private void DrawHealthBars()
        {
            var cam = Camera.main;
            if (cam == null || _barsUntil.Count == 0) return;

            _barScratch.Clear();
            foreach (var kv in _barsUntil)
                if (kv.Key == null || kv.Value < Time.unscaledTime || !kv.Key.Health.IsAlive) _barScratch.Add(kv.Key);
            foreach (var d in _barScratch) _barsUntil.Remove(d);

            var old = GUI.color;
            foreach (var kv in _barsUntil)
            {
                var dino = kv.Key;
                var col = dino.GetComponent<Collider>();
                Vector3 top = col != null ? new Vector3(col.bounds.center.x, col.bounds.max.y + 0.4f, col.bounds.center.z)
                                          : dino.transform.position + Vector3.up * 2f;
                Vector3 sp = cam.WorldToScreenPoint(top);
                if (sp.z <= 0f) continue;

                float w = Mathf.Clamp(900f / sp.z, 36f, 90f);
                var r = new Rect(sp.x - w * 0.5f, Screen.height - sp.y - 6f, w, 6f);
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = new Color(0.9f, 0.25f, 0.2f);
                var fill = new Rect(r.x + 1f, r.y + 1f, (r.width - 2f) * dino.Health.Fraction, r.height - 2f);
                GUI.DrawTexture(fill, Texture2D.whiteTexture);
            }
            GUI.color = old;
        }

        private static string ResultLabel(MatchResult result)
        {
            switch (result)
            {
                case MatchResult.Extracted: return "Extraction bonus";
                case MatchResult.Died:      return "Died";
                default:                    return "Stranded";
            }
        }

        private static string RankLine(MatchStats s)
        {
            // 10 rating points = one rank, so the change reads as a percentage of a rank.
            int pct = Mathf.RoundToInt(s.RatingDelta * 10f);
            string change = pct >= 0 ? $"+{pct}%" : $"{pct}%";
            if (s.RankAfter > s.RankBefore) return $"Survivor rank UP: {s.RankBefore} to {s.RankAfter}. Islands get harder.";
            if (s.RankAfter < s.RankBefore) return $"Survivor rank down: {s.RankBefore} to {s.RankAfter}.";
            return $"Survivor rank {s.RankAfter} ({change} toward the next rank)";
        }

        private void EnsureStyles()
        {
            if (_big != null) return;
            _big    = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            _center = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            _box    = new GUIStyle(GUI.skin.box)   { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = 13 };
            _slot   = new GUIStyle(_box)           { fontSize = 11, padding = new RectOffset(2, 2, 2, 2) };
            _panel  = new GUIStyle(GUI.skin.box)   { padding = new RectOffset(8, 8, 6, 6) };
            _small  = new GUIStyle(GUI.skin.label) { fontSize = 12, margin = new RectOffset(0, 0, 1, 1) };
            _hint   = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            _hint.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
            _barText = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleCenter,
                                                      padding = new RectOffset(0, 0, 0, 0) };
            _marker = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _marker.normal.textColor = new Color(0.55f, 1f, 0.6f);
            _banner = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _banner.normal.textColor = new Color(1f, 0.92f, 0.45f);
            _alert  = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        private static string MovementLabel(PlayerController c)
        {
            if (c.IsHidden) return c.Stance == Stance.Prone ? "HIDDEN (crawling)" : "HIDDEN (crouching)";
            bool deep = c.Concealment >= ViewBlockers.HiddenAt;
            switch (c.Stance)
            {
                case Stance.Prone:     return c.InCover ? "Crawling (cover too thin)" : "Crawling";
                case Stance.Crouching: return c.InCover ? "Crouching (cover too thin)" : "Crouching";
            }
            if (deep)      return "Deep forest (crouch to hide)";
            if (c.InCover) return "Thin cover (find thicker forest)";
            if (c.IsExhausted) return "Out of breath";
            if (c.IsSprinting) return "Running";
            return c.RunToggled ? "Run on" : "Walking";
        }

        private static string FormatTime(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            return $"{(int)(seconds / 60f)}:{(int)(seconds % 60f):00}";
        }

        private static string Headline(MatchResult result)
        {
            switch (result)
            {
                case MatchResult.Extracted: return "EXTRACTED: you made it off the island";
                case MatchResult.Died:      return "YOU DIED";
                case MatchResult.Stranded:  return "STRANDED: time ran out";
                default:                    return "MATCH OVER";
            }
        }

        private static string Describe(ShopResult result)
        {
            switch (result)
            {
                case ShopResult.CannotAfford:    return "Not enough coins.";
                case ShopResult.InventoryFull:   return "Inventory full.";
                case ShopResult.MaxSlotsReached: return "Already at max slots.";
                case ShopResult.AlreadyOwned:    return "You already carry one.";
                default:                         return "Can't buy that.";
            }
        }
    }
}
