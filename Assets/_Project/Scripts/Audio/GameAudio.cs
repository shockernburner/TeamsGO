using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ProjectFossil.Core;
using ProjectFossil.Director;
using ProjectFossil.Dinosaurs;
using ProjectFossil.Economy;
using ProjectFossil.Match;
using ProjectFossil.Player;

namespace ProjectFossil.Audio
{
    // Listens to the match and plays placeholder sounds from SoundSynth. Presentation only: nothing in the
    // simulation depends on it. Auto-created next to any MatchBootstrap, like the UI, so no scene edits are needed.
    public class GameAudio : MonoBehaviour
    {
        [Range(0f, 1f)] public float masterVolume   = 0.8f;
        [Range(0f, 1f)] public float ambienceVolume = 0.35f;

        [Header("Footsteps (metres per step)")]
        public float walkStride   = 1.7f;
        public float runStride    = 2.4f;
        public float crouchStride = 1.1f;
        public float crawlStride  = 0.9f;

        [Header("Dinosaur calls")]
        public float callCooldown  = 6f;   // per dinosaur, so a chase doesn't spam screeches
        public Vector2 idleCallGap = new Vector2(12f, 28f);
        public float bigDinoHealth = 200f; // at or above this, species use the big roar

        [Header("Distant roars")]            // the apex predator is out there long before the director sends it
        public float   firstDistantRoarAt = 75f;
        public Vector2 distantRoarGap     = new Vector2(60f, 110f);
        public float   distantRoarRange   = 140f;

        [Header("Fear")]
        public float sniffRange    = 60f;
        public Vector2 heartRate   = new Vector2(70f, 150f); // beats per minute, from first unease to a charge
        public float heartFrom     = 0.2f;                   // danger level where the heartbeat starts

        private const int Variants = 4;

        private AudioClip[] _steps, _softSteps, _screeches, _roars, _bites, _swings, _hits, _hurts, _sniffs;
        private AudioClip _coin, _sting, _chime, _ambience, _heartbeat, _rotor, _victory;
        private float _nextBeat;
        private readonly Dictionary<ExtractionZone, AudioSource> _rotors = new Dictionary<ExtractionZone, AudioSource>();

        private AudioSource _ui;       // 2D one-shots
        private AudioSource _feet;     // player footsteps (own source so pitch jitter doesn't bend other sounds)
        private AudioSource _ambient;  // 2D loop

        private MatchBootstrap _bootstrap;
        private MatchManager   _match;
        private ThreatDirector _director;
        private GameObject     _player;
        private PlayerController _controller;
        private PlayerCombat   _combat;
        private Health         _playerHealth;
        private CurrencySystem _wallet;

        private Vector3 _lastPlayerPos;
        private float   _stepDistance;
        private bool    _extractionWasOpen;
        private float   _dinoScanTimer;
        private float   _nextDistantRoar;

        private class DinoTrack
        {
            public DinosaurAI Ai;
            public DinosaurAI.State LastState;
            public float NextCallTime;
            public float NextIdleCall;
            public float NextSniff;
        }
        private readonly Dictionary<DinosaurAI, DinoTrack> _dinos = new Dictionary<DinosaurAI, DinoTrack>();
        private readonly List<DinosaurAI> _gone = new List<DinosaurAI>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindFirstObjectByType<GameAudio>() != null) return;
            if (FindFirstObjectByType<MatchBootstrap>() == null) return;
            new GameObject("GameAudio").AddComponent<GameAudio>();
        }

        // ── Setup ──────────────────────────────────────────────────────────────

        private void Awake()
        {
            _steps     = Make("Step",     v => SoundSynth.Footstep(v, false));
            _softSteps = Make("SoftStep", v => SoundSynth.Footstep(v, true));
            _screeches = Make("Screech",  SoundSynth.Screech);
            _roars     = Make("Roar",     SoundSynth.Roar);
            _bites     = Make("Bite",     SoundSynth.Bite);
            _swings    = Make("Swing",    SoundSynth.Swing);
            _hits      = Make("Hit",      SoundSynth.Hit);
            _hurts     = Make("Hurt",     SoundSynth.Hurt);
            _coin      = Clip("Coin",     SoundSynth.Coin());
            _sting     = Clip("Sting",    SoundSynth.ThreatSting());
            _chime     = Clip("Chime",    SoundSynth.Chime());
            _ambience  = Clip("Ambience", SoundSynth.Ambience());
            _sniffs    = Make("Sniff",    SoundSynth.Sniff);
            _heartbeat = Clip("Heartbeat", SoundSynth.Heartbeat());
            _rotor     = Clip("Rotor",    SoundSynth.Rotor());
            _victory   = Clip("Victory",  SoundSynth.Victory());

            _ui = gameObject.AddComponent<AudioSource>();
            _ui.playOnAwake = false;
            _ui.spatialBlend = 0f;

            _feet = gameObject.AddComponent<AudioSource>();
            _feet.playOnAwake = false;
            _feet.spatialBlend = 0f;

            _ambient = gameObject.AddComponent<AudioSource>();
            _ambient.clip = _ambience;
            _ambient.loop = true;
            _ambient.spatialBlend = 0f;
            _ambient.volume = ambienceVolume * masterVolume;
        }

        private static AudioClip[] Make(string name, System.Func<int, float[]> recipe)
        {
            var clips = new AudioClip[Variants];
            for (int v = 0; v < Variants; v++) clips[v] = Clip($"{name}{v}", recipe(v));
            return clips;
        }

        private static AudioClip Clip(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, SoundSynth.SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnDestroy()
        {
            Unbind();
            DinosaurAI.Killed -= OnDinoKilled;
        }

        private void OnEnable()  => DinosaurAI.Killed += OnDinoKilled;
        private void OnDisable() => DinosaurAI.Killed -= OnDinoKilled;

        // ── Binding (the match restarts in place, so rebind whenever it changes) ──

        private void Update()
        {
            if (_bootstrap == null) _bootstrap = FindFirstObjectByType<MatchBootstrap>();
            if (_bootstrap == null) return;

            var match = _bootstrap.Match;
            if (match != _match || (match != null && (match.Player != _player || match.Director != _director)))
                Bind(match);
            if (_match == null) return;

            _ambient.volume = ambienceVolume * masterVolume;
            if (_match.IsRunning && !_ambient.isPlaying) _ambient.Play();

            UpdateFootsteps();
            UpdateDinosaurs();
            UpdateExtraction();
            UpdateDistantRoar();
            UpdateHeartbeat();
            UpdateRotors();
        }

        private void Bind(MatchManager match)
        {
            Unbind();
            _match      = match;
            if (_match == null) return;

            _player       = _match.Player;
            _controller   = _match.PlayerController;
            _playerHealth = _match.PlayerHealth;
            _combat       = _player != null ? _player.GetComponent<PlayerCombat>() : null;
            _wallet       = _match.PlayerInventory != null ? _match.PlayerInventory.Wallet : null;
            _director     = _match.Director;

            if (_combat != null)       _combat.Attacked += OnAttacked;
            if (_playerHealth != null) _playerHealth.Damaged += OnPlayerDamaged;
            if (_wallet != null)       _wallet.BalanceChanged += OnBalanceChanged;
            if (_director != null)     _director.OnThreatTriggered += OnThreat;
            _match.MatchEnded += OnMatchEnded;
            _match.StalkerReleased   += OnStalkerReleased;
            _match.FinalStandStarted += OnFinalStand;

            _lastPlayerPos     = _player != null ? _player.transform.position : Vector3.zero;
            _stepDistance      = 0f;
            _extractionWasOpen = false;
            _nextDistantRoar   = firstDistantRoarAt;
            _dinos.Clear();
        }

        private void Unbind()
        {
            if (_combat != null)       _combat.Attacked -= OnAttacked;
            if (_playerHealth != null) _playerHealth.Damaged -= OnPlayerDamaged;
            if (_wallet != null)       _wallet.BalanceChanged -= OnBalanceChanged;
            if (_director != null)     _director.OnThreatTriggered -= OnThreat;
            if (_match != null)
            {
                _match.MatchEnded        -= OnMatchEnded;
                _match.StalkerReleased   -= OnStalkerReleased;
                _match.FinalStandStarted -= OnFinalStand;
            }
            _rotors.Clear(); // their sources live on the helicopters, which go with the island
            _combat = null; _playerHealth = null; _wallet = null; _director = null; _match = null;
        }

        // ── Player ─────────────────────────────────────────────────────────────

        private void UpdateFootsteps()
        {
            if (_player == null || _controller == null || !_controller.enabled) return;

            Vector3 pos = _player.transform.position;
            Vector3 delta = pos - _lastPlayerPos;
            _lastPlayerPos = pos;
            delta.y = 0f;
            if (!_controller.IsGrounded || delta.sqrMagnitude > 25f) return; // airborne, or teleported (rescue)

            _stepDistance += delta.magnitude;
            float stride; float vol; bool soft;
            switch (_controller.Stance)
            {
                case Stance.Prone:     stride = crawlStride;  vol = 0.25f; soft = true;  break;
                case Stance.Crouching: stride = crouchStride; vol = 0.3f;  soft = true;  break;
                default:
                    stride = _controller.IsSprinting ? runStride : walkStride;
                    vol    = _controller.IsSprinting ? 0.75f : 0.5f;
                    soft   = false;
                    break;
            }
            if (_stepDistance < stride) return;
            _stepDistance = 0f;
            _feet.pitch = Random.Range(0.9f, 1.1f);
            _feet.PlayOneShot(Pick(soft ? _softSteps : _steps), vol * masterVolume);
        }

        private void OnAttacked(WeaponStats weapon, Health target)
        {
            Play2D(Pick(_swings), 0.6f, 1f);
            if (target != null) Play3D(Pick(_hits), target.transform.position, 0.9f, Random.Range(0.9f, 1.1f), 30f);
        }

        private void OnPlayerDamaged(DamageInfo info) => Play2D(Pick(_hurts), 0.8f, 1f);

        private void OnBalanceChanged(int balance, int delta)
        {
            if (delta > 0) Play2D(_coin, 0.6f, 1f);
        }

        // ── Match ──────────────────────────────────────────────────────────────

        private void OnThreat(ThreatEvent e)
        {
            Play2D(_sting, 0.9f, 1f);
            // A bought apex announces itself from where it will come from.
            if (e.Threat != null && e.Threat.speciesOverride is DinosaurSpecies sp && sp.maxHealth >= bigDinoHealth)
                StartCoroutine(RoarLater(e.Target.Position + RandomFlat() * e.Threat.spawnDistance, 1.2f));
        }

        private IEnumerator RoarLater(Vector3 position, float delay)
        {
            yield return new WaitForSeconds(delay);
            Play3D(Pick(_roars), position, 1f, 0.95f, 300f);
        }

        private void UpdateDistantRoar()
        {
            var state = _match.State;
            if (state == null || !_match.IsRunning || _player == null || state.Elapsed < _nextDistantRoar) return;
            _nextDistantRoar = state.Elapsed + Random.Range(distantRoarGap.x, distantRoarGap.y);

            foreach (var kv in _dinos) // a real one nearby already speaks for itself
                if (kv.Key != null && kv.Key.species != null && kv.Key.species.maxHealth >= bigDinoHealth &&
                    kv.Key.species.temperament == Temperament.Predator) return;

            Vector3 far = _player.transform.position + RandomFlat() * distantRoarRange;
            Play3D(Pick(_roars), far, 0.9f, Random.Range(0.8f, 0.92f), distantRoarRange * 2.8f);
        }

        private static Vector3 RandomFlat()
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }

        private void UpdateExtraction()
        {
            var state = _match.State;
            if (state == null) return;
            bool open = state.IsExtractionOpen;
            if (open && !_extractionWasOpen && _match.IsRunning) Play2D(_chime, 0.8f, 1f);
            _extractionWasOpen = open;
        }

        private void OnMatchEnded(MatchStats stats)
        {
            _ambient.Stop();
            if (stats.Result == MatchResult.Extracted) { Play2D(_victory, 1f, 1f); return; }
            // Low and slow if you didn't make it. Positional so the pitch doesn't touch the shared 2D source.
            if (_player != null) Play3D(_chime, _player.transform.position, 0.8f, 0.6f, 50f);
        }

        // The stalker announces itself from where it starts, far off.
        private void OnStalkerReleased(Vector3 position) => Play3D(Pick(_roars), position, 1f, 0.85f, 400f);

        private void OnFinalStand() => Play2D(_sting, 1f, 1.15f);

        // A heartbeat that comes in as predators close in and races when one charges. Silent when safe.
        private void UpdateHeartbeat()
        {
            if (_player == null || !_match.IsRunning || _heartbeat == null) return;
            float danger = DinosaurAI.DangerAt(_player.transform.position);
            if (_match.IsFinalStand) danger = Mathf.Max(danger, 0.6f);
            if (danger < heartFrom) { _nextBeat = Mathf.Min(_nextBeat, Time.time + 0.3f); return; }
            if (Time.time < _nextBeat) return;

            float k = Mathf.InverseLerp(heartFrom, 1f, danger);
            _nextBeat = Time.time + 60f / Mathf.Lerp(heartRate.x, heartRate.y, k);
            Play2D(_heartbeat, Mathf.Lerp(0.35f, 0.9f, k), 1f);
        }

        // Each helicopter carries a looping rotor sound while it's in the air.
        private void UpdateRotors()
        {
            foreach (var zone in _match.ExtractionZones)
            {
                if (zone == null || zone.Helicopter == null) continue;
                bool active = zone.HelicopterActive;
                if (!_rotors.TryGetValue(zone, out var src) || src == null)
                {
                    if (!active) continue;
                    src = zone.Helicopter.gameObject.AddComponent<AudioSource>();
                    src.clip         = _rotor;
                    src.loop         = true;
                    src.spatialBlend = 1f;
                    src.rolloffMode  = AudioRolloffMode.Linear;
                    src.minDistance  = 12f;
                    src.maxDistance  = 260f;
                    src.dopplerLevel = 0.4f;
                    _rotors[zone] = src;
                }
                src.volume = masterVolume;
                if (active && !src.isPlaying) src.Play();
            }
        }

        // ── Dinosaurs ──────────────────────────────────────────────────────────

        private void UpdateDinosaurs()
        {
            _dinoScanTimer -= Time.deltaTime;
            if (_dinoScanTimer <= 0f)
            {
                _dinoScanTimer = 0.5f;
                foreach (var ai in FindObjectsByType<DinosaurAI>(FindObjectsSortMode.None))
                {
                    if (_dinos.ContainsKey(ai)) continue;
                    ai.AttackLanded += () => OnDinoBite(ai);
                    _dinos[ai] = new DinoTrack
                    {
                        Ai = ai, LastState = ai.CurrentState,
                        NextIdleCall = Time.time + Random.Range(idleCallGap.x, idleCallGap.y),
                    };
                }
            }

            _gone.Clear();
            foreach (var kv in _dinos)
            {
                var d = kv.Value;
                if (d.Ai == null) { _gone.Add(kv.Key); continue; }

                var state = d.Ai.CurrentState;
                bool spotted = (state == DinosaurAI.State.Alert || state == DinosaurAI.State.Chase) &&
                               d.LastState != DinosaurAI.State.Alert && d.LastState != DinosaurAI.State.Chase;
                d.LastState = state;

                // A predator on a trail sniffs as it comes, so you can hear it working toward you.
                if (d.Ai.IsSniffing && Time.time >= d.NextSniff && _player != null &&
                    (d.Ai.transform.position - _player.transform.position).sqrMagnitude < sniffRange * sniffRange)
                {
                    bool big = d.Ai.species != null && d.Ai.species.maxHealth >= bigDinoHealth;
                    Play3D(Pick(_sniffs), d.Ai.transform.position, big ? 1f : 0.6f,
                           Random.Range(0.9f, 1.05f) * (big ? 0.75f : 1.2f), sniffRange);
                    d.NextSniff = Time.time + Random.Range(2.2f, 3.8f);
                }

                if (spotted && Time.time >= d.NextCallTime)
                {
                    Call(d.Ai, 1f);
                    d.NextCallTime = Time.time + callCooldown;
                }
                else if (state == DinosaurAI.State.Wander && Time.time >= d.NextIdleCall)
                {
                    Call(d.Ai, 0.35f); // distant chatter tells you what's around
                    d.NextIdleCall = Time.time + Random.Range(idleCallGap.x, idleCallGap.y);
                }
            }
            foreach (var g in _gone) _dinos.Remove(g);
        }

        private void Call(DinosaurAI ai, float volume)
        {
            bool big = ai.species != null && ai.species.maxHealth >= bigDinoHealth;
            // Plant-eaters bellow: the same voices, pitched down.
            float pitch = ai.species != null && ai.species.temperament != Temperament.Predator ? 0.7f : 1f;
            if (big) Play3D(Pick(_roars), ai.transform.position, volume, Random.Range(0.9f, 1.05f) * pitch, 220f);
            else     Play3D(Pick(_screeches), ai.transform.position, volume * 0.9f, Random.Range(0.9f, 1.15f) * pitch, 110f);
        }

        private void OnDinoBite(DinosaurAI ai)
        {
            if (ai != null) Play3D(Pick(_bites), ai.transform.position, 1f, Random.Range(0.9f, 1.1f), 40f);
        }

        private void OnDinoKilled(DinosaurAI ai, GameObject killer)
        {
            if (ai == null) return;
            bool big = ai.species != null && ai.species.maxHealth >= bigDinoHealth;
            Play3D(Pick(big ? _roars : _screeches), ai.transform.position, 0.8f, big ? 0.7f : 0.75f, big ? 180f : 80f);
        }

        // ── Playback ───────────────────────────────────────────────────────────

        private static AudioClip Pick(AudioClip[] set) => set[Random.Range(0, set.Length)];

        private void Play2D(AudioClip clip, float volume, float pitch)
        {
            if (clip == null) return;
            _ui.pitch = pitch;
            _ui.PlayOneShot(clip, volume * masterVolume);
        }

        // Positional one-shot that fades out linearly to silence at maxDistance.
        private void Play3D(AudioClip clip, Vector3 position, float volume, float pitch, float maxDistance)
        {
            if (clip == null) return;
            var go = new GameObject("Sfx_" + clip.name);
            go.transform.position = position;
            var src = go.AddComponent<AudioSource>();
            src.clip         = clip;
            src.volume       = volume * masterVolume;
            src.pitch        = pitch;
            src.spatialBlend = 1f;
            src.rolloffMode  = AudioRolloffMode.Linear;
            src.minDistance  = 4f;
            src.maxDistance  = maxDistance;
            src.dopplerLevel = 0f;
            src.Play();
            Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
        }
    }
}
