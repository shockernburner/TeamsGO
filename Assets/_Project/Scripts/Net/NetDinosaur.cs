using System.Collections.Generic;
using UnityEngine;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using ProjectFossil.Core;
using ProjectFossil.Dinosaurs;
using ProjectFossil.Match;

namespace ProjectFossil.Net
{
    // One animal shared by the team. The host's copy runs the real DinosaurAI and NavMesh; everyone else gets a
    // puppet (DinosaurAI.IsRemote) that the host moves (NetworkTransform) and feeds with species, health, state
    // and bites. Hits a joiner lands on a puppet go to the host, which applies them to the real animal.
    [RequireComponent(typeof(DinosaurAI))]
    public class NetDinosaur : NetworkBehaviour
    {
        private const float MaxHitFromClient = 250f; // no weapon hits harder; anything bigger is a bad packet

        private readonly SyncVar<string> _species  = new SyncVar<string>();
        private readonly SyncVar<float>  _max      = new SyncVar<float>();
        private readonly SyncVar<float>  _health   = new SyncVar<float>();
        private readonly SyncVar<byte>   _state    = new SyncVar<byte>();
        private readonly SyncVar<bool>   _sniffing = new SyncVar<bool>();

        private DinosaurAI _ai;
        private bool _puppet;

        private void Awake()
        {
            _ai = GetComponent<DinosaurAI>();
            _species.OnChange  += OnSpeciesChanged;
            _health.OnChange   += OnHealthChanged;
            _max.OnChange      += OnHealthChanged;
            _state.OnChange    += OnStateChanged;
            _sniffing.OnChange += OnSniffingChanged;
        }

        // ── Host ───────────────────────────────────────────────────────────────

        // Called by the session right before it spawns this animal for everyone.
        public void PrepareForSpawn()
        {
            _species.Value = _ai.species != null ? _ai.species.name : "";
        }

        public override void OnStartServer()
        {
            _ai.AttackWindupStarted += OnWindup;
            _ai.AttackLanded        += OnBite;
        }

        public override void OnStopServer()
        {
            _ai.AttackWindupStarted -= OnWindup;
            _ai.AttackLanded        -= OnBite;
        }

        private void Update()
        {
            if (!IsServerInitialized || _ai == null) return;
            var h = _ai.Health;
            if (h != null && h.Pool != null)
            {
                if (!Mathf.Approximately(_max.Value, h.Max)) _max.Value = h.Max;
                if (!Mathf.Approximately(_health.Value, h.Current)) _health.Value = h.Current;
            }
            byte state = (byte)_ai.CurrentState;
            if (_state.Value != state) _state.Value = state;
            if (_sniffing.Value != _ai.IsSniffing) _sniffing.Value = _ai.IsSniffing;
        }

        private void OnWindup(float seconds) => ObserversWindup(seconds);
        private void OnBite() => ObserversBite();

        [ObserversRpc(ExcludeServer = true)]
        private void ObserversWindup(float seconds)
        {
            if (_puppet) _ai.PlayRemoteWindup(seconds);
        }

        [ObserversRpc(ExcludeServer = true)]
        private void ObserversBite()
        {
            if (_puppet) _ai.PlayRemoteBite();
        }

        // A joiner's hit, applied to the real animal as coming from that joiner's body (so it turns on them,
        // and the kill and coins are credited to them).
        [ServerRpc(RequireOwnership = false)]
        private void ServerHit(float amount, Vector3 point, NetworkConnection sender = null)
        {
            if (_ai == null || _ai.Health == null || !_ai.Health.IsAlive) return;
            var body = NetSession.Instance != null ? NetSession.Instance.AvatarOf(sender) : null;
            _ai.Health.TakeDamage(new DamageInfo(Mathf.Clamp(amount, 0f, MaxHitFromClient),
                                                 body != null ? body.gameObject : null, point));
        }

        // ── Joiner ─────────────────────────────────────────────────────────────

        public override void OnStartClient()
        {
            if (IsServerStarted) return; // the host's copy is the real animal
            _puppet = true;
            ApplySpecies(_species.Value);
            _ai.Health.Redirect = SendHit;
            MirrorHealth();
            _ai.SetRemoteState((DinosaurAI.State)_state.Value, _sniffing.Value);
        }

        public override void OnStopClient()
        {
            if (_puppet && _ai != null && _ai.Health != null) _ai.Health.Redirect = null;
        }

        private bool SendHit(DamageInfo info)
        {
            if (!_puppet || !IsClientInitialized) return false;
            ServerHit(info.Amount, info.Point);
            return true;
        }

        private void OnSpeciesChanged(string prev, string next, bool asServer)
        {
            if (!asServer && _puppet) ApplySpecies(next);
        }

        private void OnHealthChanged(float prev, float next, bool asServer)
        {
            if (!asServer && _puppet) MirrorHealth();
        }

        private void OnStateChanged(byte prev, byte next, bool asServer)
        {
            if (!asServer && _puppet) _ai.SetRemoteState((DinosaurAI.State)next, _sniffing.Value);
        }

        private void OnSniffingChanged(bool prev, bool next, bool asServer)
        {
            if (!asServer && _puppet) _ai.SetRemoteState((DinosaurAI.State)_state.Value, next);
        }

        private void MirrorHealth()
        {
            if (_max.Value > 0f) _ai.Health.Mirror(_health.Value, _max.Value);
        }

        private void ApplySpecies(string speciesName)
        {
            if (_ai.species != null || string.IsNullOrEmpty(speciesName)) return;
            _ai.species = FindSpecies(speciesName);
            if (_ai.species == null)
            {
                Debug.LogWarning($"[NetDinosaur] Species '{speciesName}' isn't loaded on this machine.", this);
                return;
            }
            // Model and hit flash are built from the species; build them now in case Start already ran.
            var visual = GetComponent<DinosaurVisual>();
            if (visual != null) visual.Build();
        }

        // ── Species by name ────────────────────────────────────────────────────

        private static readonly Dictionary<string, DinosaurSpecies> Species = new Dictionary<string, DinosaurSpecies>();

        public static DinosaurSpecies FindSpecies(string speciesName)
        {
            if (string.IsNullOrEmpty(speciesName)) return null;
            if (Species.TryGetValue(speciesName, out var found) && found != null) return found;

            GameContent.LoadDefault(); // pulls the wildlife table and threat catalog (and their species) into memory
            Species.Clear();
            foreach (var s in Resources.FindObjectsOfTypeAll<DinosaurSpecies>())
                if (s != null) Species[s.name] = s;
            return Species.TryGetValue(speciesName, out found) ? found : null;
        }
    }
}
