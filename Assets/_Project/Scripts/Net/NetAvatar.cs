using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using ProjectFossil.Core;
using ProjectFossil.Match;
using ProjectFossil.Player;

namespace ProjectFossil.Net
{
    // A teammate's body as everyone else sees it. Its owner keeps playing their normal local player; this follows
    // that player around (NetworkTransform, owner-driven) and is hidden on the owner's own screen.
    //
    // On the host it stands in for the teammate in the simulation: it is tagged Player, so dinosaurs see, hear,
    // chase and bite it (IDamageable, IStealthProfile), and each bite is passed to the owner's real health.
    // Teammates can also help each other through it (IInteractable, E): get a downed teammate back up, or use one
    // of your bandages or medkits on a hurt one.
    public class NetAvatar : NetworkBehaviour, IDamageable, IStealthProfile, IFriendly, IInteractable
    {
        private const float StatusInterval = 0.2f;
        public  const float ReviveSeconds  = 4f;    // stay beside them this long
        public  const float ReviveReach    = 2.6f;
        public  const float ReviveHealth   = 0.35f; // share of health they get back

        public static readonly List<NetAvatar> All = new List<NetAvatar>();

        private readonly SyncVar<string> _label      = new SyncVar<string>();
        private readonly SyncVar<bool>   _alive      = new SyncVar<bool>(true);
        private readonly SyncVar<float>  _noise      = new SyncVar<float>(1f);
        private readonly SyncVar<float>  _visibility = new SyncVar<float>(1f);
        private readonly SyncVar<bool>   _low        = new SyncVar<bool>();
        private readonly SyncVar<bool>   _down       = new SyncVar<bool>();
        private readonly SyncVar<float>  _health     = new SyncVar<float>(1f); // 0..1

        public string Label => _label.Value;
        public bool   IsAlive => _alive.Value;
        public float  NoiseMultiplier      => _noise.Value;
        public float  VisibilityMultiplier => _visibility.Value;
        public bool   IsDown => _down.Value;
        // Up and fighting: can revive others, counts toward boarding and leaving together.
        public bool   IsStanding => _alive.Value && !_down.Value;

        private CharacterController _body;
        private PlayerVisual _visual;
        private float _statusTimer;
        private bool  _left;
        private MatchManager _followedMatch;
        private MatchStats   _followedStats;
        private static GUIStyle _tag;
        private float _reviveProgress = -1f; // >= 0 while I'm getting this teammate up

        private void Awake()
        {
            _body = GetComponent<CharacterController>();
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        public override void OnStartServer()
        {
            if (string.IsNullOrEmpty(_label.Value)) _label.Value = $"Survivor {Owner.ClientId + 1}";
        }

        // Host: the name its player chose (made unique by the session).
        public void SetLabel(string label)
        {
            if (!string.IsNullOrEmpty(label)) _label.Value = label;
        }

        public override void OnStartClient()
        {
            if (!All.Contains(this)) All.Add(this);
            if (IsOwner)
            {
                // My own body: the local player already shows me, and dinosaurs on the host chase that player.
                Placeholder.RemoveRenderers(transform);
                if (_body != null) _body.enabled = false;
                return;
            }
            var content = GameContent.LoadDefault();
            _visual = PlayerVisual.Attach(gameObject, content != null ? content.playerModel : null);
        }

        public override void OnStopClient() => All.Remove(this);
        public override void OnStopServer() => All.Remove(this);

        private void Update()
        {
            if (IsOwner) FollowLocalPlayer();
            else TickRevive();
            if (_visual != null) _visual.SetRemotePose(_low.Value || _down.Value, !_alive.Value);
            if (!IsOwner && _body != null && _body.enabled != _alive.Value) _body.enabled = _alive.Value;
        }

        // ── Owner ──────────────────────────────────────────────────────────────

        private void FollowLocalPlayer()
        {
            var match = NetSession.Instance != null ? NetSession.Instance.Match : null;
            if (match == null || match.Player == null) return;

            // A new island started (host restart): this body carries on for the new player.
            if (match != _followedMatch || match.Stats != _followedStats)
            {
                _followedMatch = match;
                _followedStats = match.Stats;
                _left = false;
            }
            if (_left) return;

            var t = match.Player.transform;
            transform.SetPositionAndRotation(t.position, t.rotation);

            if (match.State != null && match.State.Phase == MatchPhase.Ended)
            {
                _left = true;
                ServerLeave(match.State.Result == MatchResult.Extracted);
                return;
            }

            _statusTimer -= Time.deltaTime;
            if (_statusTimer > 0f) return;
            _statusTimer = StatusInterval;
            var pc = match.PlayerController;
            var hp = match.PlayerHealth;
            if (pc != null)
                ServerStatus(pc.NoiseMultiplier, pc.VisibilityMultiplier, pc.Stance != Stance.Standing,
                             hp != null && hp.IsDown, hp != null ? hp.Fraction : 1f);
        }

        [ServerRpc]
        private void ServerStatus(float noise, float visibility, bool low, bool down, float health)
        {
            _noise.Value      = Mathf.Clamp(noise, 0f, 4f);
            _visibility.Value = Mathf.Clamp(visibility, 0f, 2f);
            _low.Value        = low;
            _down.Value       = down;
            _health.Value     = Mathf.Clamp01(health);
        }

        // ── Helping a teammate (on the helper's machine) ───────────────────────

        public string Prompt
        {
            get
            {
                if (_down.Value) return $"Get {Label} up (stay close {Mathf.RoundToInt(ReviveSeconds)} s)";
                return $"Patch up {Label} (uses a healing item)";
            }
        }

        public bool CanInteract(GameObject interactor)
        {
            if (IsOwner || !IsClientInitialized || !_alive.Value || _reviveProgress >= 0f) return false;
            var match = NetSession.Instance != null ? NetSession.Instance.Match : null;
            if (match == null || !match.IsRunning || match.PlayerHealth == null) return false;
            if (!match.PlayerHealth.IsAlive || match.PlayerHealth.IsDown) return false;
            if (_down.Value) return true;
            return _health.Value < 0.98f && match.PlayerInventory != null && match.PlayerInventory.HasHealingItem;
        }

        public void Interact(GameObject interactor)
        {
            var match = NetSession.Instance.Match;
            if (_down.Value)
            {
                _reviveProgress = 0f;
                match.Announce($"Getting {Label} up. Stay close...");
                return;
            }
            if (match.PlayerInventory != null && match.PlayerInventory.TryTakeHealingItem(out float amount))
            {
                ServerHeal(amount, NetSession.Instance.PlayerName);
                match.Announce($"You patched up {Label}.");
            }
        }

        private void TickRevive()
        {
            if (_reviveProgress < 0f) return;
            var match = NetSession.Instance != null ? NetSession.Instance.Match : null;
            bool ok = match != null && match.IsRunning && match.Player != null && match.PlayerHealth != null &&
                      match.PlayerHealth.IsAlive && !match.PlayerHealth.IsDown && _alive.Value && _down.Value &&
                      Vector3.Distance(match.Player.transform.position, transform.position) <= ReviveReach;
            if (!ok)
            {
                if (match != null && _alive.Value && _down.Value) match.Announce($"You moved away from {Label}.");
                _reviveProgress = -1f;
                return;
            }
            _reviveProgress += Time.deltaTime;
            if (_reviveProgress < ReviveSeconds) return;
            _reviveProgress = -1f;
            ServerRevive(NetSession.Instance.PlayerName);
            match.Announce($"You got {Label} back up!");
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerRevive(string helper)
        {
            if (_alive.Value && _down.Value) OwnerRevive(Owner, helper);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerHeal(float amount, string helper)
        {
            if (_alive.Value) OwnerHeal(Owner, Mathf.Clamp(amount, 0f, 100f), helper);
        }

        [TargetRpc]
        private void OwnerRevive(NetworkConnection conn, string helper)
        {
            var match = NetSession.Instance != null ? NetSession.Instance.Match : null;
            if (match == null || match.PlayerHealth == null || !match.PlayerHealth.IsDown) return;
            match.PlayerHealth.Revive(ReviveHealth);
            match.Announce($"{helper} got you back up!");
        }

        [TargetRpc]
        private void OwnerHeal(NetworkConnection conn, float amount, string helper)
        {
            var match = NetSession.Instance != null ? NetSession.Instance.Match : null;
            if (match == null || match.PlayerHealth == null) return;
            match.PlayerHealth.Heal(amount);
            match.Announce($"{helper} patched you up (+{Mathf.RoundToInt(amount)} HP).");
        }

        // Died or flew out: the body drops (or vanishes aboard the helicopter) and dinosaurs lose interest.
        [ServerRpc]
        private void ServerLeave(bool extracted)
        {
            if (!_alive.Value) return;
            _alive.Value = false;
            if (NetSession.Instance != null) NetSession.Instance.OnAvatarLeft(this, extracted);
            StartCoroutine(DespawnAfter(extracted ? 0.5f : 6f));
        }

        private IEnumerator DespawnAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (IsSpawned) Despawn();
        }

        // ── Host: the simulation's view of this teammate ───────────────────────

        public void TakeDamage(DamageInfo info)
        {
            if (!IsServerInitialized || !_alive.Value || info.Amount <= 0f) return;
            NetworkObject source = info.Source != null ? info.Source.GetComponent<NetworkObject>() : null;
            OwnerHurt(Owner, info.Amount, info.Point, source);
        }

        [TargetRpc]
        private void OwnerHurt(NetworkConnection conn, float amount, Vector3 point, NetworkObject source)
        {
            var match = NetSession.Instance != null ? NetSession.Instance.Match : null;
            if (match == null || match.PlayerHealth == null) return;
            match.PlayerHealth.TakeDamage(new DamageInfo(amount, source != null ? source.gameObject : null, point));
        }

        [TargetRpc]
        public void OwnerCreditDamage(NetworkConnection conn, float amount)
        {
            var match = NetSession.Instance != null ? NetSession.Instance.Match : null;
            if (match != null) match.CreditDamage(amount);
        }

        [TargetRpc]
        public void OwnerCreditKill(NetworkConnection conn, string speciesName)
        {
            var match = NetSession.Instance != null ? NetSession.Instance.Match : null;
            if (match != null) match.CreditKill(NetDinosaur.FindSpecies(speciesName));
        }

        // ── Name tag ───────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (IsOwner || !IsClientInitialized) return;
            var cam = Camera.main;
            if (cam == null) return;
            var match = NetSession.Instance != null ? NetSession.Instance.Match : null;
            if (match == null || !match.IsRunning) return;

            Vector3 head = transform.position + Vector3.up * 2.2f;
            Vector3 screen = cam.WorldToScreenPoint(head);
            Vector2 p = ScreenMarker.Place(screen, Screen.width, Screen.height, 40f, out bool onScreen);
            float metres = match.Player != null ? Vector3.Distance(match.Player.transform.position, transform.position) : 0f;

            if (_tag == null)
            {
                _tag = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 13 };
            }
            bool down = _alive.Value && _down.Value;
            _tag.normal.textColor = !_alive.Value ? new Color(0.7f, 0.7f, 0.7f)
                                  : down ? new Color(1f, 0.4f, 0.35f) : new Color(0.55f, 1f, 0.6f);
            string text = !_alive.Value ? $"{Label} is gone"
                        : down ? $"{Label} DOWN  {Mathf.RoundToInt(metres)} m  (E to help)"
                               : $"{Label}  {Mathf.RoundToInt(metres)} m";
            if (!onScreen && _alive.Value) text = "◆ " + text;

            var rect = new Rect(p.x - 90f, Screen.height - p.y - 12f, 180f, 24f);
            var shadow = rect; shadow.x += 1f; shadow.y += 1f;
            var c = _tag.normal.textColor;
            _tag.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(shadow, text, _tag);
            _tag.normal.textColor = c;
            GUI.Label(rect, text, _tag);

            // A small health bar under the name, and the revive progress while I'm getting them up.
            if (_alive.Value && onScreen)
            {
                var bar = new Rect(p.x - 30f, rect.yMax, 60f, 4f);
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(bar, Texture2D.whiteTexture);
                GUI.color = down ? new Color(1f, 0.35f, 0.3f) : new Color(0.45f, 0.95f, 0.5f);
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(_health.Value), bar.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            if (_reviveProgress >= 0f)
            {
                float t = Mathf.Clamp01(_reviveProgress / ReviveSeconds);
                var back = new Rect(Screen.width * 0.5f - 120f, Screen.height * 0.62f, 240f, 14f);
                GUI.color = new Color(0f, 0f, 0f, 0.65f);
                GUI.DrawTexture(back, Texture2D.whiteTexture);
                GUI.color = new Color(1f, 0.85f, 0.4f);
                GUI.DrawTexture(new Rect(back.x, back.y, back.width * t, back.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(back.x, back.y - 22f, back.width, 20f), $"Getting {Label} up... stay close", _tag);
            }
        }
    }
}
