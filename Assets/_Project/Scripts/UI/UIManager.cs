using System.Collections.Generic;
using UnityEngine;
using ProjectFossil.Core;
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

        private GUIStyle _big, _center, _box, _panel, _small, _hint, _barText;

        // Auto-create alongside any MatchBootstrap so no scene edits are needed.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindFirstObjectByType<UIManager>() != null) return;
            if (FindFirstObjectByType<MatchBootstrap>() == null) return;
            new GameObject("UIManager").AddComponent<UIManager>();
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

        private void OnAttacked(WeaponStats weapon, Health target)
        {
            if (target == null)
                return; // swings at nothing are obvious; don't spam the feed
            if (!target.IsAlive)
                Push($"{weapon.Name}: killed it!");
            else
                Push($"{weapon.Name}: hit for {Mathf.RoundToInt(weapon.Damage)} (target {Mathf.CeilToInt(target.Current)}/{Mathf.CeilToInt(target.Max)})");
        }

        private void Push(string text) => _messages.Add((text, Time.unscaledTime + announcementSeconds));

        private void OnBalanceChanged(int balance, int delta)
        {
            if (delta > 0) Push($"+{delta} coins");
        }

        private void OnMatchEnded(MatchStats stats)
        {
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
                DrawDamageFlash();
                if (!_shopOpen) DrawCrosshair();
                DrawHud();
                DrawPrompt();
                if (_shopOpen) DrawShop();
            }
            else
            {
                DrawResults(_match.Stats);
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
            GUILayout.BeginArea(new Rect(10, 10, 230, 150), _panel);
            if (health != null) Bar("HP", health.Current, health.Max, new Color(0.85f, 0.2f, 0.2f));
            if (controller != null)
            {
                bool tired = controller.IsExhausted;
                Bar(tired ? "Tired" : "Stam", controller.Stamina, controller.maxStamina,
                    tired ? new Color(0.95f, 0.55f, 0.1f) : new Color(0.3f, 0.7f, 0.9f));
            }
            if (inv != null) GUILayout.Label($"Coins {inv.Wallet.Balance}    Time {FormatTime(state.Remaining)}", _small);

            if (!state.IsExtractionOpen)
                GUILayout.Label($"Extraction in {FormatTime(state.ExtractionOpensAt - state.Elapsed)}", _small);
            else if (state.IsExtracting)
                GUILayout.Label($"EXTRACTING {Mathf.CeilToInt(state.ExtractionHoldTime - state.ExtractionProgress)}s", _small);
            else
                GUILayout.Label("Extraction OPEN: find a green beacon", _small);

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
            const float slot = 52f;
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
                GUI.Box(r, label, _box);
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
                if (GUILayout.Button($"Buy ({offer.price})", GUILayout.Width(120)))
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

            var area = new Rect(Screen.width * 0.5f - 200, Screen.height * 0.5f - 160, 400, 320);
            GUILayout.BeginArea(area, _box);
            GUILayout.Label(Headline(stats.Result), _big);
            GUILayout.Space(8);
            GUILayout.Label($"Time survived: {FormatTime(stats.TimeSurvived)}");
            GUILayout.Label($"Coins earned: {stats.CoinsEarned}");
            GUILayout.Label($"Dinosaurs killed: {stats.DinosKilled}");
            GUILayout.Label($"Threats faced: {stats.ThreatsFaced}");
            GUILayout.Label($"Island seed: {stats.Seed}");
            GUILayout.Space(12);
            if (GUILayout.Button("Play again (new island)", GUILayout.Height(36)) && _bootstrap != null)
                _bootstrap.Restart(true);
            if (GUILayout.Button("Replay same island", GUILayout.Height(28)) && _bootstrap != null)
                _bootstrap.Restart(false);
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

        // Red edges when hurt; the side the hit came from glows stronger.
        private void DrawDamageFlash()
        {
            if (_damageFlash <= 0f) return;
            var old = GUI.color;
            float a = 0.35f * _damageFlash;
            float edge = Screen.width * 0.08f;
            GUI.color = new Color(0.8f, 0f, 0f, a * 0.15f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = new Color(0.9f, 0f, 0f, a * (_damageSide < 0f ? 1.6f : 0.8f));
            GUI.DrawTexture(new Rect(0, 0, edge, Screen.height), Texture2D.whiteTexture);
            GUI.color = new Color(0.9f, 0f, 0f, a * (_damageSide > 0f ? 1.6f : 0.8f));
            GUI.DrawTexture(new Rect(Screen.width - edge, 0, edge, Screen.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private void EnsureStyles()
        {
            if (_big != null) return;
            _big    = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            _center = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            _box    = new GUIStyle(GUI.skin.box)   { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = 13 };
            _panel  = new GUIStyle(GUI.skin.box)   { padding = new RectOffset(8, 8, 6, 6) };
            _small  = new GUIStyle(GUI.skin.label) { fontSize = 12, margin = new RectOffset(0, 0, 1, 1) };
            _hint   = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            _hint.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
            _barText = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleCenter,
                                                      padding = new RectOffset(0, 0, 0, 0) };
        }

        private static string MovementLabel(PlayerController c)
        {
            switch (c.Stance)
            {
                case Stance.Prone:     return "Crawling";
                case Stance.Crouching: return "Crouching";
            }
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
                default:                         return "Can't buy that.";
            }
        }
    }
}
