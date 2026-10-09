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
        private AudioClip _coin, _sting, _ambience, _heartbeat, _rotor, _victory, _nightAmbience, _rainLoop;
        private AudioClip[] _thunders, _skyCalls;
        private AudioClip[] _growls, _breaths;   // recorded only; null without the sound library
        private AudioClip _stormLoop, _windLoop, _lament, _squelch;
        private AudioClip[] _radio;     // recorded pilot calls; null without them
        private float _nextCoin;
        private AudioSource _rain;      // 2D loop, louder in a storm
        private AudioSource _wind;      // 2D loop, storms only
        private AudioClip _fallLoop;
        private readonly AudioSource[] _falls = new AudioSource[3]; // 3D loops on the nearest fast streams
        private float _nextFallPick;
        private float _rainTarget, _windTarget;
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
            if (FindAnyObjectByType<GameAudio>() != null) return;
            if (FindAnyObjectByType<MatchBootstrap>() == null) return;
            new GameObject("GameAudio").AddComponent<GameAudio>();
        }

        // ── Setup ──────────────────────────────────────────────────────────────

        private void Awake()
        {
            _steps     = SoundLibrary.Get("Step")     ?? Make("Step",     v => SoundSynth.Footstep(v, false));
            _softSteps = SoundLibrary.Get("SoftStep") ?? Make("SoftStep", v => SoundSynth.Footstep(v, true));
            _screeches = SoundLibrary.Get("Screech") ?? Make("Screech", SoundSynth.Screech);
            _roars     = SoundLibrary.Get("Roar")    ?? Make("Roar",    SoundSynth.Roar);
            _growls    = SoundLibrary.Get("Growl");
            _breaths   = SoundLibrary.Get("Breath");
            _bites     = SoundLibrary.Get("Bite")  ?? Make("Bite",     SoundSynth.Bite);
            _swings    = SoundLibrary.Get("Swing") ?? Make("Swing",    SoundSynth.Swing);
            _hits      = SoundLibrary.Get("Hit")   ?? Make("Hit",      SoundSynth.Hit);
            _hurts     = SoundLibrary.Get("Hurt")  ?? Make("Hurt",     SoundSynth.Hurt);
            _coin      = Clip("Coin",     SoundSynth.Coin());
            _sting     = Clip("Sting",    SoundSynth.ThreatSting());
            _ambience  = SoundLibrary.One("DayAmbience") ?? Clip("Ambience", SoundSynth.Ambience());
            _sniffs    = Make("Sniff",    SoundSynth.Sniff);
            _heartbeat = Clip("Heartbeat", SoundSynth.Heartbeat());
            _rotor     = SoundLibrary.One("Rotor") ?? Clip("Rotor", SoundSynth.Rotor());
            _victory   = SoundLibrary.One("Victory") ?? Clip("Victory", SoundSynth.Victory());
            _nightAmbience = SoundLibrary.One("NightAmbience") ?? Clip("NightAmbience", SoundSynth.NightAmbience());
            _rainLoop  = SoundLibrary.One("RainLoop") ?? Clip("Rain", SoundSynth.Rain());
            _stormLoop = SoundLibrary.One("StormLoop") ?? _rainLoop;
            _windLoop  = SoundLibrary.One("WindLoop");
            _thunders  = SoundLibrary.Get("Thunder") ?? Make("Thunder", SoundSynth.Thunder);
            // Real screeches pitched up read as distant flyers; the synthesized calls only when there are none.
            _skyCalls  = SoundLibrary.Get("SkyCall") ?? SoundLibrary.Get("Screech") ?? Make("SkyCall", SoundSynth.SkyCall);
            _lament    = SoundLibrary.One("Death") ?? Clip("Lament", SoundSynth.Lament());
            _squelch   = Clip("Squelch", SoundSynth.RadioSquelch());
            _radio     = SoundLibrary.Get("Radio");

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

            _rain = gameObject.AddComponent<AudioSource>();
            _rain.clip = _rainLoop;
            _rain.loop = true;
            _rain.spatialBlend = 0f;
            _rain.volume = 0f;
            _rain.playOnAwake = false;

            _fallLoop = SoundLibrary.One("WaterfallLoop") ?? Clip("Waterfall", SoundSynth.Waterfall());
            for (int i = 0; i < _falls.Length; i++)
            {
                var go = new GameObject($"Waterfall sound {i}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.clip         = _fallLoop;
                src.loop         = true;
                src.spatialBlend = 1f;
                src.rolloffMode  = AudioRolloffMode.Linear;
                src.minDistance  = 3f;
                src.maxDistance  = 40f;
                src.volume       = 0f;
                src.playOnAwake  = false;
                src.timeSamples  = i * 9000; // out of step, so near falls don't phase
                _falls[i] = src;
            }

            _wind = gameObject.AddComponent<AudioSource>();
            _wind.clip = _windLoop;
            _wind.loop = true;
            _wind.spatialBlend = 0f;
            _wind.volume = 0f;
            _wind.playOnAwake = false;
        }

        // ── Weather and sky ────────────────────────────────────────────────────

        private void OnConditions(Conditions c)
        {
            // Insects at night, birds and wind by day.
            var clip = c.Time == DayTime.Night ? _nightAmbience : _ambience;
            if (_ambient.clip != clip) { _ambient.clip = clip; if (_ambient.isPlaying) _ambient.Play(); }
            _rainTarget = c.Weather == Weather.Storm ? 0.75f : c.Weather == Weather.Rain ? 0.45f : 0f;
            _windTarget = c.Weather == Weather.Storm ? 0.45f : 0f;
            // A storm has its own recording (heavier rain with thunder rolling in it); plain rain is steadier.
            var rainClip = c.Weather == Weather.Storm ? _stormLoop : _rainLoop;
            if (_rain.clip != rainClip) { _rain.clip = rainClip; if (_rain.isPlaying) _rain.Play(); }
        }

        // Thunder arrives later the farther away the strike: about 3 s per kilometre.
        private void OnLightning(float distance)
        {
            float delay = distance / 343f;
            float volume = Mathf.Lerp(1f, 0.45f, Mathf.InverseLerp(200f, 2000f, distance));
            StartCoroutine(PlayLater(Pick(_thunders), delay, volume, Random.Range(0.85f, 1.05f)));
        }

        private System.Collections.IEnumerator PlayLater(AudioClip clip, float delay, float volume, float pitch)
        {
            yield return new WaitForSeconds(delay);
            Play2D(clip, volume, pitch);
        }

        private void OnSkyCall(Vector3 position) =>
            Play3D(Pick(_skyCalls), position, 0.7f, Random.Range(1.35f, 1.6f), 260f);

        private void UpdateRain()
        {
            float k = _match != null && _match.IsRunning ? 1f : 0.5f;
            Fade(_rain, _rainTarget * k);
            if (_windLoop != null) Fade(_wind, _windTarget * k);
        }

        private void Fade(AudioSource src, float target)
        {
            src.volume = Mathf.MoveTowards(src.volume, target * masterVolume, Time.deltaTime * 0.3f);
            if (src.volume > 0.001f && !src.isPlaying) src.Play();
            else if (src.volume <= 0.001f && src.isPlaying) src.Stop();
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

        private void OnEnable()
        {
            DinosaurAI.Killed += OnDinoKilled;
            WorldConditions.Changed   += OnConditions;
            WorldConditions.Lightning += OnLightning;
            AmbientEvents.SkyCall     += OnSkyCall;
        }

        private void OnDisable()
        {
            DinosaurAI.Killed -= OnDinoKilled;
            WorldConditions.Changed   -= OnConditions;
            WorldConditions.Lightning -= OnLightning;
            AmbientEvents.SkyCall     -= OnSkyCall;
        }

        // ── Binding (the match restarts in place, so rebind whenever it changes) ──

        private void Update()
        {
            if (_bootstrap == null) _bootstrap = FindAnyObjectByType<MatchBootstrap>();
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
            UpdateRain();
            UpdateFalls();
        }

        // The nearest few stretches of fast stream each get a looping rush; the rest are too far to hear.
        private void UpdateFalls()
        {
            var world = IslandWorld.Current;
            if (world == null) return;
            var falls = world.Rapids;
            var cam = Camera.main;
            if (Time.time >= _nextFallPick && cam != null)
            {
                _nextFallPick = Time.time + 0.5f;
                Vector3 at = cam.transform.position;
                for (int k = 0; k < _falls.Length; k++)
                {
                    int best = -1; float bestD = 45f * 45f;
                    for (int i = 0; i < falls.Count; i++)
                    {
                        float d = (falls[i] - at).sqrMagnitude;
                        if (d >= bestD) continue;
                        bool taken = false;
                        for (int j = 0; j < k; j++)
                            if (_falls[j].volume > 0f && (_falls[j].transform.position - falls[i]).sqrMagnitude < 0.01f) taken = true;
                        if (!taken) { best = i; bestD = d; }
                    }
                    if (best >= 0) _falls[k].transform.position = falls[best];
                    _falls[k].volume = best >= 0 ? 0.4f * masterVolume * (_match.IsRunning ? 1f : 0.5f) : 0f;
                }
            }
            foreach (var src in _falls)
            {
                if (src.volume > 0f && !src.isPlaying) src.Play();
                else if (src.volume <= 0f && src.isPlaying) src.Stop();
            }
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
            // Coins arrive on every hit; a ding each time turns a fight into an arcade. Soft, and at most every 2 s.
            if (delta <= 0 || Time.time < _nextCoin) return;
            _nextCoin = Time.time + 2f;
            Play2D(_coin, 0.18f, 1f);
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
            if (open && !_extractionWasOpen && _match.IsRunning) StartCoroutine(RadioCall());
            _extractionWasOpen = open;
        }

        private void OnMatchEnded(MatchStats stats)
        {
            _ambient.Stop();
            if (stats.Result == MatchResult.Extracted) { Play2D(_victory, 1f, 1f); return; }
            // A last few heartbeats and a low swell if you didn't make it.
            Play2D(_lament, 0.9f, 1f);
        }

        // The pilot calls in: static opens the channel, then the recorded line if there is one. The words are on
        // screen too (UIManager), so the call works without a recording.
        private IEnumerator RadioCall()
        {
            Play2D(_squelch, 0.5f, 1f);
            if (_radio == null) yield break;
            yield return new WaitForSeconds(0.35f);
            Play2D(Pick(_radio), 1f, 1f);
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
                // After the end the helicopter flies on behind the results; let it fade into the distance.
                src.volume = _match.IsRunning ? masterVolume : Mathf.MoveTowards(src.volume, 0f, Time.deltaTime * 0.25f);
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
#if UNITY_6000_5_OR_NEWER
                foreach (var ai in FindObjectsByType<DinosaurAI>())
#else
                foreach (var ai in FindObjectsByType<DinosaurAI>(FindObjectsSortMode.None))
#endif
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
                    // Close in, the big one's breathing carries over the sniffing.
                    bool breathe = big && _breaths != null && Random.value < 0.5f;
                    if (breathe)
                        Play3D(Pick(_breaths), d.Ai.transform.position, 1f, Random.Range(0.85f, 1f), sniffRange * 0.6f);
                    else
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
                    Call(d.Ai, 0.35f, idle: true); // distant chatter tells you what's around
                    d.NextIdleCall = Time.time + Random.Range(idleCallGap.x, idleCallGap.y);
                }
            }
            foreach (var g in _gone) _dinos.Remove(g);
        }

        private void Call(DinosaurAI ai, float volume, bool idle = false)
        {
            bool big = ai.species != null && ai.species.maxHealth >= bigDinoHealth;
            // Plant-eaters bellow: the same voices, pitched down.
            float pitch = ai.species != null && ai.species.temperament != Temperament.Predator ? 0.7f : 1f;
            // Recorded growls for the chatter of animals going about their business: low for the big ones.
            if (idle && _growls != null)
            {
                Play3D(Pick(_growls), ai.transform.position, volume, Random.Range(0.9f, 1.05f) * pitch * (big ? 0.8f : 1.2f),
                       big ? 180f : 90f);
                return;
            }
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
