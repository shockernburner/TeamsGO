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
    public class NetAvatar : NetworkBehaviour, IDamageable, IStealthProfile, IFriendly
    {
        private const float StatusInterval = 0.2f;

        public static readonly List<NetAvatar> All = new List<NetAvatar>();

        private readonly SyncVar<string> _label      = new SyncVar<string>();
        private readonly SyncVar<bool>   _alive      = new SyncVar<bool>(true);
        private readonly SyncVar<float>  _noise      = new SyncVar<float>(1f);
        private readonly SyncVar<float>  _visibility = new SyncVar<float>(1f);
        private readonly SyncVar<bool>   _low        = new SyncVar<bool>();

        public string Label => _label.Value;
        public bool   IsAlive => _alive.Value;
        public float  NoiseMultiplier      => _noise.Value;
        public float  VisibilityMultiplier => _visibility.Value;

        private CharacterController _body;
        private PlayerVisual _visual;
        private float _statusTimer;
        private bool  _left;
        private MatchManager _followedMatch;
        private MatchStats   _followedStats;
        private static GUIStyle _tag;

        private void Awake()
        {
            _body = GetComponent<CharacterController>();
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        public override void OnStartServer()
        {
            _label.Value = $"Survivor {Owner.ClientId + 1}";
        }

        public override void OnStartClient()
        {
            if (!All.Contains(this)) All.Add(this);
            if (IsOwner)
            {
                // My own body: the local player already shows me, and dinosaurs on the host chase that player.
                foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
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
            if (_visual != null) _visual.SetRemotePose(_low.Value, !_alive.Value);
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
            if (pc != null) ServerStatus(pc.NoiseMultiplier, pc.VisibilityMultiplier, pc.Stance != Stance.Standing);
        }

        [ServerRpc]
        private void ServerStatus(float noise, float visibility, bool low)
        {
            _noise.Value      = Mathf.Clamp(noise, 0f, 4f);
            _visibility.Value = Mathf.Clamp(visibility, 0f, 2f);
            _low.Value        = low;
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
            _tag.normal.textColor = _alive.Value ? new Color(0.55f, 1f, 0.6f) : new Color(1f, 0.45f, 0.4f);
            string text = _alive.Value ? $"{Label}  {Mathf.RoundToInt(metres)} m" : $"{Label} is down";
            if (!onScreen) text = (_alive.Value ? "◆ " : "") + text;

            var rect = new Rect(p.x - 90f, Screen.height - p.y - 12f, 180f, 24f);
            var shadow = rect; shadow.x += 1f; shadow.y += 1f;
            var c = _tag.normal.textColor;
            _tag.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(shadow, text, _tag);
            _tag.normal.textColor = c;
            GUI.Label(rect, text, _tag);
        }
    }
}
