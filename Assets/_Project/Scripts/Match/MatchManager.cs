using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ProjectFossil.Core;
using ProjectFossil.Director;
using ProjectFossil.Dinosaurs;
using ProjectFossil.Economy;
using ProjectFossil.Generation;
using ProjectFossil.Player;

namespace ProjectFossil.Match
{
    // Scene-side match flow. Owns the pure MatchState, the Threat Director and its AI buyer,
    // and wires the player, loot, extraction zones and threat spawning together.
    public class MatchManager : MonoBehaviour
    {
        public const string PlayerTeam   = "players";
        public const string DirectorTeam = "director";

        public MatchState      State     { get; private set; }
        public MatchStats      Stats     { get; private set; }
        public ThreatDirector  Director  { get; private set; }
        public AIDirectorBrain Brain     { get; private set; }
        public GameContent     Content   { get; private set; }
        public AdaptiveDifficulty Difficulty { get; private set; }
        public ScoreModel      Score     { get; private set; }
        public SurvivorRank    Rank      { get; private set; }
        public int             BestScore { get; private set; }
        public int             LiveScore => Score != null && State != null ? Score.Total(State.Elapsed) : 0;
        public bool            IsRunning => State != null && State.Phase == MatchPhase.Active;

        public GameObject       Player          { get; private set; }
        public PlayerController PlayerController { get; private set; }
        public PlayerInventory  PlayerInventory { get; private set; }
        public Health           PlayerHealth    { get; private set; }

        public IReadOnlyList<ExtractionZone> ExtractionZones => _zones;

        public event Action<string>     Announced;
        public event Action<MatchStats> MatchEnded;
        // The stalker is loose (where it starts), and the last fight at the helicopter has begun.
        public event Action<Vector3>    StalkerReleased;
        public event Action             FinalStandStarted;

        // Online: announcements the whole team should hear (threats, the stalker), and a follower's request
        // for the host to send the final wave to their helicopter.
        public event Action<string>     TeamAnnounced;
        public event Action             FinalStandRequested;

        // A follower has joined someone else's island: the host runs the director, the wildlife and the
        // animals, so this match only keeps the clock, the loot and this player's own fight.
        public bool IsFollower { get; private set; }
        // Teammates on other machines (their bodies here). Only the host fills this; the director picks among
        // them and this player.
        public readonly List<Transform> Teammates = new List<Transform>();
        private const float TargetTurnSeconds = 45f;
        private readonly List<Transform> _candidates = new List<Transform>();
        private readonly HashSet<Transform> _finalStandsSent = new HashSet<Transform>();

        // Co-op hooks (the networking layer fills these in; solo leaves them empty).
        // Whether a teammate is still up to revive this player if they go down.
        public Func<bool> CanBeRevived;
        // How many living teammates stand within `radius` of a point (boarding together, leaving together).
        public Func<Vector3, float, int> TeammatesNear;
        // This player finished boarding: the helicopter at this pad leaves, with any teammates under it.
        public event Action<Vector3> LiftedOff;
        // This player's rescue flare came down here: the team should get the same pad.
        public event Action<Vector3> FlareDropped;

        public const float BleedOutSeconds = 60f;
        public float BleedOutLeft { get; private set; }
        private bool _leftWithTeam;

        public bool IsFinalStand { get; private set; }
        // The helicopter that took the player off the island (null until they extract).
        public ExtractionZone RescuedBy { get; private set; }

        private readonly List<ExtractionZone> _zones = new List<ExtractionZone>();
        private const float FallOutOfWorldY = -20f;

        // Survivor Rank and best score live on this machine between matches.
        private const string RatingKey    = "ProjectFossil.SurvivorRating";
        private const string BestScoreKey = "ProjectFossil.BestScore";

        private WildlifeSpawner _wildlife;
        private CoinTrickle     _hitCoins;

        private ThreatExecutor _executor;
        private Transform  _islandRoot;
        private ScentTrail _scent;
        private float      _lastScentWarning;
        private RNGService _flareRng;
        private Vector3 _playerSpawn;
        private bool    _stalkerSent;
        private readonly List<DinosaurAI> _stalkers = new List<DinosaurAI>();
        private PlayerMenuInput _menuInput;

        // ── Setup ──────────────────────────────────────────────────────────────

        public void Begin(IslandData island, GameObject player, GameContent content,
                          GameObject defaultDinosaurPrefab, Transform islandRoot)
        {
            Cleanup();

            IsFollower = NetRole.IsFollower;
            Content = content;
            State   = new MatchState(content.matchRules);
            Stats   = new MatchStats { Seed = island.Seed };
            Score   = new ScoreModel();
            Rank    = new SurvivorRank(PlayerPrefs.GetFloat(RatingKey, 0f));
            BestScore = PlayerPrefs.GetInt(BestScoreKey, 0);
            _hitCoins = new CoinTrickle(content.matchRules.coinsPerDamage);
            int level = Rank.Level;
            Stats.RankBefore = level;

            State.ExtractionOpened += OnExtractionOpened;
            State.SurvivalPayout   += OnSurvivalPayout;
            State.Ended            += OnEnded;

            BindPlayer(player);
            _islandRoot = islandRoot;
            _flareRng   = new RNGService(unchecked(island.Seed * 41 + 11));
            SpawnPointsOfInterest(island, islandRoot);

            Director  = new ThreatDirector(content.threatCatalog.threats);
            _executor = new ThreatExecutor(defaultDinosaurPrefab, islandRoot)
            {
                HealthMultiplier = SurvivorRank.DinosaurHealth(level),
                DamageMultiplier = SurvivorRank.DinosaurDamage(level),
            };
            Director.OnThreatTriggered += OnThreatTriggered;

            float baseline = SurvivorRank.DirectorBaseline(level);
            Difficulty = new AdaptiveDifficulty(content.directorSettings, baseline);
            var aiBuyer = new ThreatBuyer("ai-director", DirectorTeam, true, new CurrencySystem());
            // Offset the seed so director choices don't mirror island layout choices.
            Brain = new AIDirectorBrain(Director, content.directorSettings, aiBuyer,
                                        new RNGService(unchecked(island.Seed * 31 + 7)),
                                        content.matchRules.matchDuration, baseline);

            if (!IsFollower) SpawnWildlife(island, defaultDinosaurPrefab, islandRoot, level);

            DinosaurAI.Killed  += OnDinosaurKilled;
            DinosaurAI.Damaged += OnDinosaurDamaged;
            DinosaurAI.PickedUpScent += OnPickedUpScent;
            ExtractionZone.Noise += DinosaurAI.NoiseAt;
            ScentTrail.Active = _scent = IsFollower ? null : new ScentTrail();
            _lastScentWarning = float.NegativeInfinity;

            Announce(level > 0 ? $"Survivor rank {level}: the island fights harder. Survive, scavenge, extract."
                               : "Survive. Scavenge caches for coins. Extraction opens later.");
        }

        private void BindPlayer(GameObject player)
        {
            Player           = player;
            _playerSpawn     = player.transform.position;
            PlayerController = player.GetComponent<PlayerController>();
            PlayerInventory  = player.GetComponent<PlayerInventory>();
            PlayerHealth     = PlayerController != null ? PlayerController.Health : player.GetComponent<Health>();
            _menuInput       = player.GetComponent<PlayerMenuInput>();

            if (PlayerHealth != null) PlayerHealth.Died += OnPlayerDied;
            if (PlayerHealth != null)
            {
                PlayerHealth.CanGoDown = () => IsRunning && CanBeRevived != null && CanBeRevived();
                PlayerHealth.WentDown += OnPlayerDown;
                PlayerHealth.Revived  += OnPlayerRevived;
            }
            if (PlayerHealth != null) PlayerHealth.Damaged += OnPlayerDamaged;
            if (_menuInput != null)   _menuInput.UseItemPressed += OnUseItem;
        }

        private void SpawnPointsOfInterest(IslandData island, Transform parent)
        {
            // Separate stream from the generator so loot is seed-stable even if generation changes.
            var lootRng = new RNGService(unchecked(island.Seed * 17 + 3));

            foreach (var poi in island.PointsOfInterest)
            {
                Vector3 pos = SnapToGround(poi.WorldPos);
                switch (poi.Type)
                {
                    case POIType.ExtractionZone:
                        _zones.Add(ExtractionZone.Create(pos, Content.matchRules.extractionRadius, parent));
                        break;
                    case POIType.LootCache:
                        SpawnCache(pos, "Supply cache", Content.cacheLoot, lootRng, parent, 1f, Content.cacheModel);
                        break;
                    case POIType.Ruins:
                        SpawnCache(pos, "Ruin stash", Content.ruinsLoot, lootRng, parent, 1.6f, Content.ruinsModel);
                        break;
                }
            }
        }

        private static void SpawnCache(Vector3 pos, string label, LootTable table, RNGService rng, Transform parent, float size,
                                       ModelDefinition model)
        {
            if (table == null) return;

            var go = Placeholder.Primitive(PrimitiveType.Cube);
            go.name = label;
            go.transform.SetParent(parent, false);
            // Sink into the slope until the downhill corners touch: centred on the ground, a box on a steep hillside
            // hung half in the air.
            float drop = 0f;
            var world = IslandWorld.Current;
            if (world != null)
            {
                float h = size * 0.5f, centre = world.GroundAt(pos);
                for (int i = 0; i < 4; i++)
                    drop = Mathf.Max(drop, centre - world.GroundAt(pos + new Vector3(i < 2 ? -h : h, 0f, i % 2 == 0 ? -h : h)));
                drop = Mathf.Min(drop, size * 0.6f);
            }
            go.transform.position   = pos + Vector3.up * (size * 0.5f - drop);
            go.transform.localScale = Vector3.one * size;

            // A chest (or whatever the content names) in place of the cube; the cube's collider stays for interaction.
            if (model != null && model.HasModel)
            {
                go.GetComponent<Renderer>().enabled = false;
                var visual = ModelFit.Spawn(model, go.transform);
                visual.transform.localPosition += Vector3.down * 0.5f; // cube pivot is its centre; the model's is its base
                // Turn from the position, not the loot RNG, so loot rolls don't depend on whether art is set up.
                visual.transform.localRotation = Quaternion.Euler(0f, Mathf.Repeat(pos.x * 13.7f + pos.z * 7.3f, 360f), 0f);
            }

            // Thin marker pole so caches can be spotted from a distance; hidden once the cache is emptied.
            var pole = Placeholder.Primitive(PrimitiveType.Cylinder);
            pole.name = LootContainer.BeaconName;
            Destroy(pole.GetComponent<Collider>());
            pole.transform.SetParent(go.transform, false);
            pole.transform.localScale    = new Vector3(0.08f, 3f / size, 0.08f);
            pole.transform.localPosition = new Vector3(0f, 3.5f / size, 0f);
            pole.GetComponent<Renderer>().material.color = new Color(1f, 0.8f, 0.2f);

            var container = go.AddComponent<LootContainer>();
            container.containerName = label.ToLowerInvariant();
            container.Fill(table.Roll(rng));
        }

        // Animals from the wildlife table. Without one, the bootstrap has already placed the default prefab's species.
        private void SpawnWildlife(IslandData island, GameObject prefab, Transform parent, int level)
        {
            _wildlife = null;
            if (Content.wildlife == null || prefab == null) return;
            _wildlife = new WildlifeSpawner(Content.wildlife, prefab, parent, island,
                                            SurvivorRank.WildlifeCount(level),
                                            SurvivorRank.DinosaurHealth(level),
                                            SurvivorRank.DinosaurDamage(level));
            _wildlife.SpawnInitial();
        }

        private static Vector3 SnapToGround(Vector3 pos)
        {
            // The island's own ground: a ray from above landed caches on tree trunks and rocks.
            var world = IslandWorld.Current;
            if (world != null) return new Vector3(pos.x, world.GroundAt(pos), pos.z);
            if (Physics.Raycast(pos + Vector3.up * 500f, Vector3.down, out var hit, 1000f,
                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return hit.point;
            return pos;
        }

        // ── Loop ───────────────────────────────────────────────────────────────

        private void Update()
        {
            if (!IsRunning) return;

            bool open = State.IsExtractionOpen;
            bool inZone = false;
            float boarding = 1f;
            bool down = PlayerHealth != null && PlayerHealth.IsDown;
            foreach (var zone in _zones)
            {
                zone.SetOpen(open);
                // Boarding starts once the helicopter is down (and you're on your feet).
                if (Player != null && !down && zone.HelicopterLanded && zone.Contains(Player.transform.position))
                {
                    inZone = true;
                    // Each teammate holding the pad with you speeds boarding up by half.
                    int mates = TeammatesNear != null ? TeammatesNear(zone.transform.position, zone.radius) : 0;
                    boarding = 1f + 0.5f * mates;
                }
            }

            State.Tick(Time.deltaTime, inZone, boarding);
            if (!IsRunning) return;
            TickBleedOut();
            if (!IsRunning) return;

            if (IsFollower)
            {
                if (!IsFinalStand && State.IsExtracting) RequestFinalStand();
                RescueIfFallenThroughWorld();
                return;
            }

            if (!_stalkerSent && State.Elapsed >= Content.matchRules.stalkerAt) ReleaseStalker();
            if (!IsFinalStand && State.IsExtracting) BeginFinalStand();

            RescueIfFallenThroughWorld();

            // The player's scent: a mark every couple of seconds, none while wading (water breaks the trail) or
            // while hidden low in thick forest (the undergrowth smothers it).
            if (_scent != null && Player != null && PlayerHealth != null && PlayerHealth.IsAlive)
            {
                Vector3 p = Player.transform.position;
                bool hidden = PlayerController != null && PlayerController.IsHidden;
                _scent.Tick(p, State.Elapsed, !hidden && !IslandWorld.Wet(p));
            }

            ThreatTarget? target = PickDirectorTarget();

            float healthFraction = PlayerHealth != null ? PlayerHealth.Fraction : 1f;
            Brain.Intensity = Difficulty.Tick(Time.deltaTime, State.Elapsed, healthFraction);
            Brain.Tick(Time.deltaTime, State.Elapsed, target);

            _wildlife?.Tick(State.Elapsed, Player != null ? Player.transform.position : (Vector3?)null);
        }

        // Solo it's always the player. With teammates the director takes turns, so nobody gets every threat.
        private ThreatTarget? PickDirectorTarget()
        {
            _candidates.Clear();
            if (Player != null && PlayerHealth != null && PlayerHealth.IsAlive) _candidates.Add(Player.transform);
            Teammates.RemoveAll(t => t == null);
            foreach (var t in Teammates)
            {
                var d = t.GetComponentInParent<IDamageable>();
                if (d == null || d.IsAlive) _candidates.Add(t);
            }
            if (_candidates.Count == 0) return null;
            var who = _candidates[(int)(State.Elapsed / TargetTurnSeconds) % _candidates.Count];
            return new ThreatTarget(PlayerTeam, who.position, who);
        }

        // Safety net: if the player ever drops through the terrain, put them back at their drop point.
        private void RescueIfFallenThroughWorld()
        {
            if (Player == null || Player.transform.position.y > FallOutOfWorldY) return;

            var cc = Player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false; // CharacterController overrides direct position changes
            Player.transform.position = _playerSpawn;
            if (cc != null) cc.enabled = true;
            Announce("You fell out of the world and were put back at your drop point.");
        }

        public void Announce(string message)
        {
            if (!string.IsNullOrEmpty(message)) Announced?.Invoke(message);
        }

        // ── Events ─────────────────────────────────────────────────────────────

        private void OnThreatTriggered(ThreatEvent e)
        {
            Stats.ThreatsFaced++;
            Score.AddThreatFaced();
            // The scent trail is this player's; threats sent at a teammate come straight for them instead.
            bool teammate = e.Target.Focus != null && (Player == null || e.Target.Focus != Player.transform);
            _executor.Execute(e, Content.directorSettings.ScaledSpawnCount(e.Threat.spawnCount, Difficulty.Intensity),
                              forceHunt: teammate);
            Announce(e.Threat.announcement);
            TeamAnnounced?.Invoke(e.Threat.announcement);
        }

        // ── Scripted beats ─────────────────────────────────────────────────────

        private ThreatEvent? ScriptedThreat(string threatId)
        {
            if (Player == null || PlayerHealth == null || !PlayerHealth.IsAlive) return null;
            return ScriptedThreat(threatId, Player.transform);
        }

        private ThreatEvent? ScriptedThreat(string threatId, Transform who)
        {
            var threat = Director != null ? Director.Find(threatId) : null;
            if (threat == null || who == null || State == null) return null;
            return new ThreatEvent
            {
                Threat = threat,
                Target = new ThreatTarget(PlayerTeam, who.position, who),
                Time   = State.Elapsed,
            };
        }

        // Early on, the island's apex predator starts working toward the player by scent, long before the director
        // can afford to send one. It's heard, then smelled, and only then seen.
        private void ReleaseStalker()
        {
            _stalkerSent = true;
            var e = ScriptedThreat(Content.matchRules.stalkerThreatId);
            if (e == null) return;
            var spawned = _executor.Execute(e.Value, 1, Content.matchRules.stalkerDistance);
            _stalkers.AddRange(spawned);
            if (spawned.Count == 0) return;
            StalkerReleased?.Invoke(spawned[0].transform.position);
            const string roar = "A roar, far off. Something big has started sniffing the air.";
            Announce(roar);
            TeamAnnounced?.Invoke(roar);
        }

        // Boarding takes a while and the rotors are loud: everything nearby comes for the pad. Hold it.
        private void BeginFinalStand()
        {
            IsFinalStand = true;
            SendFinalWave(Player.transform);
            FinalStandStarted?.Invoke();
            Announce("They heard the rotors. HOLD THE PAD until you're aboard!");
        }

        // A teammate on another machine started boarding: their pad gets its own wave (once each).
        public void BeginFinalStandFor(Transform teammate)
        {
            if (!IsRunning || IsFollower || teammate == null || teammate == (Player != null ? Player.transform : null)) return;
            if (!_finalStandsSent.Add(teammate)) return;
            SendFinalWave(teammate);
            Announce("A teammate is boarding. The rotors are drawing everything to their pad.");
        }

        private void SendFinalWave(Transform who)
        {
            var rules = Content.matchRules;
            var e = ScriptedThreat(rules.finalWaveThreatId, who);
            if (e != null)
            {
                int count = Content.directorSettings.ScaledSpawnCount(e.Value.Threat.spawnCount, Difficulty.Intensity);
                _executor.Execute(e.Value, count, rules.finalWaveDistance, forceHunt: true);
            }
            foreach (var s in _stalkers)
                if (s != null && s.CurrentState != DinosaurAI.State.Dead) s.Hunt(who);
        }

        // Follower: the pad fight is the same here, but the host sends the animals.
        private void RequestFinalStand()
        {
            IsFinalStand = true;
            FinalStandStarted?.Invoke();
            FinalStandRequested?.Invoke();
            Announce("They heard the rotors. HOLD THE PAD until you're aboard!");
        }

        private void OnPickedUpScent(DinosaurAI dino)
        {
            if (dino == null || dino.species == null || State == null) return;
            if (State.Elapsed - _lastScentWarning < 30f) return; // one warning per stalk, not per sniff
            _lastScentWarning = State.Elapsed;
            Announce("Something has your scent. Get low in thick forest, or wade through water.");
        }

        private void OnExtractionOpened()
        {
            Announce("Extraction open: helicopters are landing. Their noise draws dinosaurs.");
            if (TryDropRescueFlare(out float distance))
                Announce($"The beacons are far off. A rescue flare landed {Mathf.RoundToInt(distance)} m away.");
        }

        // Two beacons on a big island can both be a long run away; add one within reach of where the player is.
        private bool TryDropRescueFlare(out float distance)
        {
            distance = 0f;
            var rules = Content.matchRules;
            if (Player == null || _flareRng == null || rules.flareIfFartherThan <= 0f) return false;

            Vector3 p = Player.transform.position;
            foreach (var zone in _zones)
                if (zone != null && Flat(zone.transform.position - p).magnitude <= rules.flareIfFartherThan) return false;

            float waterY = IslandWorld.Sea;
            for (int tries = 0; tries < 16; tries++)
            {
                float angle = _flareRng.NextFloat() * Mathf.PI * 2f;
                float dist  = Mathf.Lerp(rules.flareDistance.x, rules.flareDistance.y, _flareRng.NextFloat());
                Vector3 c = p + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;
                if (!NavMesh.SamplePosition(c, out var hit, 12f, NavMesh.AllAreas)) continue;
                if (hit.position.y < waterY + 0.5f || IslandWorld.Wet(hit.position + Vector3.up * 0.3f)) continue; // not in the shallows

                var zone = ExtractionZone.Create(hit.position, rules.extractionRadius, _islandRoot);
                zone.SetOpen(true);
                _zones.Add(zone);
                distance = Flat(hit.position - p).magnitude;
                FlareDropped?.Invoke(hit.position);
                return true;
            }
            return false;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // A teammate's rescue flare: the same pad appears here, so the team can leave from it together.
        public void AddSharedPad(Vector3 position)
        {
            if (!IsRunning || _islandRoot == null) return;
            foreach (var zone in _zones)
                if (zone != null && Flat(zone.transform.position - position).magnitude < 10f) return; // have it already
            var pad = ExtractionZone.Create(position, Content.matchRules.extractionRadius, _islandRoot);
            pad.SetOpen(State != null && State.IsExtractionOpen);
            _zones.Add(pad);
            if (Player != null)
                Announce($"A teammate's rescue flare landed {Mathf.RoundToInt(Flat(position - Player.transform.position).magnitude)} m away. Leave together for a bonus.");
        }

        private void OnSurvivalPayout(int amount)
        {
            if (PlayerInventory != null) PlayerInventory.Wallet.Earn(amount);
        }

        private void OnDinosaurKilled(DinosaurAI dino, GameObject killer)
        {
            if (!IsRunning || killer == null || killer != Player) return;
            Stats.DinosKilled++;
            Difficulty.ReportKill(State.Elapsed);
            if (dino.species == null) return;
            Score.AddKill(dino.species.scoreValue);
            if (PlayerInventory != null) PlayerInventory.Wallet.Earn(dino.species.killReward);
        }

        // Every hit pays a little and counts toward the score.
        private void OnDinosaurDamaged(DinosaurAI dino, DamageInfo info)
        {
            if (!IsRunning || info.Source == null || info.Source != Player) return;
            Score.AddDamage(info.Amount);
            int coins = _hitCoins.AddDamage(info.Amount);
            if (coins > 0 && PlayerInventory != null) PlayerInventory.Wallet.Earn(coins);
        }

        // Online: the host saw this player's hits land on its animals and passes the credit back.
        public void CreditDamage(float amount)
        {
            if (!IsRunning || amount <= 0f) return;
            Score.AddDamage(amount);
            int coins = _hitCoins.AddDamage(amount);
            if (coins > 0 && PlayerInventory != null) PlayerInventory.Wallet.Earn(coins);
        }

        public void CreditKill(DinosaurSpecies species)
        {
            if (!IsRunning) return;
            Stats.DinosKilled++;
            if (species == null) return;
            Score.AddKill(species.scoreValue);
            if (PlayerInventory != null) PlayerInventory.Wallet.Earn(species.killReward);
        }

        private void OnPlayerDamaged(DamageInfo info)
        {
            if (IsRunning) Difficulty.ReportPlayerHurt(State.Elapsed);
        }

        private void OnPlayerDied(DamageInfo info) => State?.ReportPlayerDied();

        // ── Down and revive (co-op) ────────────────────────────────────────────

        private void OnPlayerDown()
        {
            BleedOutLeft = BleedOutSeconds;
            Announce($"You're down! Crawl to cover. A teammate can get you up (E). {Mathf.RoundToInt(BleedOutSeconds)} s.");
            TeamAnnounced?.Invoke($"{PlayerName} is down! Get to them and press E.");
        }

        private void OnPlayerRevived()
        {
            BleedOutLeft = 0f;
            Announce("You're back up. Stay close to your team.");
        }

        private void TickBleedOut()
        {
            if (PlayerHealth == null || !PlayerHealth.IsDown) return;
            BleedOutLeft -= Time.deltaTime;
            // Nobody left to come for you (or too late): that's it.
            bool hope = CanBeRevived != null && CanBeRevived();
            if (BleedOutLeft <= 0f || !hope)
                PlayerHealth.TakeDamage(new DamageInfo(PlayerHealth.Max * 10f, null, Player.transform.position));
        }

        // ── Leaving together (co-op) ───────────────────────────────────────────

        // Shown to teammates in shared announcements.
        public string PlayerName = "A teammate";

        // A teammate's helicopter at `pad` took off: if this player is standing under it, they're aboard too.
        public void TeamLiftOff(Vector3 pad)
        {
            if (!IsRunning || Player == null || (PlayerHealth != null && PlayerHealth.IsDown)) return;
            foreach (var zone in _zones)
            {
                if (zone == null || !zone.HelicopterLanded) continue;
                if ((zone.transform.position - pad).sqrMagnitude > 25f) continue;
                if (!zone.Contains(Player.transform.position)) return;
                _leftWithTeam = true;
                State.ExtractNow();
                return;
            }
        }

        private void OnUseItem()
        {
            if (!IsRunning || PlayerInventory == null) return;
            if (!PlayerInventory.TryUseHealingItem(PlayerHealth))
                Announce("Nothing to heal with (or already at full health).");
        }

        private void OnEnded(MatchResult result)
        {
            Stats.Result       = result;
            Stats.TimeSurvived = State.Elapsed;
            Stats.CoinsEarned  = PlayerInventory != null ? PlayerInventory.Wallet.TotalEarned : 0;

            if (result == MatchResult.Extracted && Player != null)
                foreach (var zone in _zones)
                    if (zone != null && zone.HelicopterLanded && zone.Contains(Player.transform.position))
                    {
                        RescuedBy = zone;
                        Stats.MatesAboard = TeammatesNear != null ? TeammatesNear(zone.transform.position, zone.radius) : 0;
                        // Taken along by a teammate's take-off: they're aboard even if their body already left.
                        if (_leftWithTeam) Stats.MatesAboard = Mathf.Max(1, Stats.MatesAboard);
                        break;
                    }
            RecordScore(result);

            if (PlayerController != null && PlayerController.enabled)
                PlayerController.InputBlocked = true;

            if (RescuedBy != null)
            {
                RescuedBy.LiftOff(Player.transform);
                if (!_leftWithTeam) LiftedOff?.Invoke(RescuedBy.transform.position);
                if (Stats.MatesAboard > 0)
                    Announce(Stats.MatesAboard == 1 ? "You left together: +25% team bonus!" : $"{Stats.MatesAboard + 1} of you aboard: +{Stats.MatesAboard * 25}% team bonus!");
            }

            MatchEnded?.Invoke(Stats);
        }

        private void RecordScore(MatchResult result)
        {
            float t = State.Elapsed;
            Stats.SurvivalPoints   = Score.SurvivalPoints(t);
            Stats.KillPoints       = Score.KillPoints;
            Stats.DamagePoints     = Score.DamagePoints;
            Stats.ThreatPoints     = Score.ThreatPoints;
            Stats.ResultMultiplier = ScoreModel.Multiplier(result) *
                                     (result == MatchResult.Extracted ? ScoreModel.TeamFactor(Stats.MatesAboard) : 1f);
            Stats.Score            = Score.Total(t, result, Stats.MatesAboard);

            Stats.NewBest   = Stats.Score > BestScore;
            if (Stats.NewBest) BestScore = Stats.Score;
            Stats.BestScore = BestScore;

            Stats.RatingDelta = Rank.Apply(Stats.Score, result);
            Stats.RankAfter   = Rank.Level;

            PlayerPrefs.SetFloat(RatingKey, Rank.Rating);
            PlayerPrefs.SetInt(BestScoreKey, BestScore);
            PlayerPrefs.Save();
        }

        // ── Teardown ───────────────────────────────────────────────────────────

        private void Cleanup()
        {
            DinosaurAI.Killed  -= OnDinosaurKilled;
            DinosaurAI.Damaged -= OnDinosaurDamaged;
            DinosaurAI.PickedUpScent -= OnPickedUpScent;
            ExtractionZone.Noise -= DinosaurAI.NoiseAt;
            if (ScentTrail.Active == _scent) ScentTrail.Active = null;
            _scent = null;
            if (Director != null)     Director.OnThreatTriggered -= OnThreatTriggered;
            if (PlayerHealth != null) PlayerHealth.Died -= OnPlayerDied;
            if (PlayerHealth != null) PlayerHealth.Damaged -= OnPlayerDamaged;
            if (PlayerHealth != null) { PlayerHealth.WentDown -= OnPlayerDown; PlayerHealth.Revived -= OnPlayerRevived; }
            _leftWithTeam = false;
            BleedOutLeft  = 0f;
            if (_menuInput != null)   _menuInput.UseItemPressed -= OnUseItem;
            if (State != null)
            {
                State.ExtractionOpened -= OnExtractionOpened;
                State.SurvivalPayout   -= OnSurvivalPayout;
                State.Ended            -= OnEnded;
            }

            _zones.Clear(); // the zone objects live under the island root and die with it
            _stalkers.Clear();
            _finalStandsSent.Clear();
            Teammates.Clear();
            _stalkerSent = false;
            IsFinalStand = false;
            RescuedBy    = null;
            State = null;
        }

        private void OnDestroy() => Cleanup();
    }
}
