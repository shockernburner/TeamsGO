using System.Collections.Generic;
using UnityEngine;
using ProjectFossil.Economy;
using ProjectFossil.Match;
using ProjectFossil.Player;

namespace ProjectFossil.UI
{
    // Placeholder IMGUI front end: HUD, announcements, interact prompt, shop and results screen.
    // Deliberately code-only so it needs no scene or prefab wiring; replace with real UI once the loop is fun.
    public class UIManager : MonoBehaviour
    {
        public float announcementSeconds = 6f;

        private MatchBootstrap   _bootstrap;
        private MatchManager     _match;
        private GameObject       _boundPlayer;
        private PlayerMenuInput  _menuInput;
        private PlayerInteractor _interactor;
        private PlayerCombat     _combat;
        private CurrencySystem   _boundWallet;

        private bool _shopOpen;
        private string _shopMessage;
        private Vector2 _shopScroll;

        private readonly List<(string text, float until)> _messages = new List<(string, float)>();

        private GUIStyle _big, _center, _box;

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
        }

        private void BindPlayer(GameObject player)
        {
            if (_menuInput != null)   _menuInput.ShopToggled -= ToggleShop;
            if (_boundWallet != null) _boundWallet.BalanceChanged -= OnBalanceChanged;

            _boundPlayer = player;
            _menuInput   = player != null ? player.GetComponent<PlayerMenuInput>() : null;
            _interactor  = player != null ? player.GetComponent<PlayerInteractor>() : null;
            _combat      = player != null ? player.GetComponent<PlayerCombat>() : null;
            _boundWallet = _match.PlayerInventory != null ? _match.PlayerInventory.Wallet : null;
            _shopOpen    = false;
            _messages.Clear();

            if (_menuInput != null)   _menuInput.ShopToggled += ToggleShop;
            if (_boundWallet != null) _boundWallet.BalanceChanged += OnBalanceChanged;
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

            GUILayout.BeginArea(new Rect(12, 12, 300, 240), _box);
            if (health != null)     Bar("Health",  health.Current, health.Max, new Color(0.85f, 0.2f, 0.2f));
            if (controller != null) Bar("Stamina", controller.Stamina, controller.maxStamina, new Color(0.3f, 0.7f, 0.9f));
            if (inv != null)        GUILayout.Label($"Coins: {inv.Wallet.Balance}", _big);
            GUILayout.Label($"Time left: {FormatTime(state.Remaining)}");

            if (!state.IsExtractionOpen)
                GUILayout.Label($"Extraction opens in {FormatTime(state.ExtractionOpensAt - state.Elapsed)}");
            else if (state.IsExtracting)
                GUILayout.Label($"EXTRACTING... {Mathf.CeilToInt(state.ExtractionHoldTime - state.ExtractionProgress)}s", _big);
            else
                GUILayout.Label("Extraction OPEN: reach a green beacon");

            if (_combat != null) GUILayout.Label($"Weapon: {_combat.CurrentWeapon.Name}");
            GUILayout.Label("[Tab] Shop   [Q] Heal   [LMB] Attack   [E] Interact");
            GUILayout.EndArea();

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
                GUI.Label(new Rect(Screen.width - 432, y, 420, 28), _messages[i].text, _box);
                y += 32f;
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private void Bar(string label, float value, float max, Color color)
        {
            GUILayout.Label($"{label}: {Mathf.CeilToInt(value)}/{Mathf.CeilToInt(max)}");
            Rect r = GUILayoutUtility.GetRect(260, 10);
            GUI.DrawTexture(r, Texture2D.grayTexture);
            var old = GUI.color;
            GUI.color = color;
            r.width *= max > 0f ? Mathf.Clamp01(value / max) : 0f;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private void EnsureStyles()
        {
            if (_big != null) return;
            _big    = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            _center = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            _box    = new GUIStyle(GUI.skin.box)   { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = 13 };
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
