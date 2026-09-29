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
        private PlayerMenuInput _menuInput;

        // ── Setup ──────────────────────────────────────────────────────────────

        public void Begin(IslandData island, GameObject player, GameContent content,
                          GameObject defaultDinosaurPrefab, Transform islandRoot)
        {
            Cleanup();

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

            SpawnWildlife(island, defaultDinosaurPrefab, islandRoot, level);

            DinosaurAI.Killed  += OnDinosaurKilled;
            DinosaurAI.Damaged += OnDinosaurDamaged;
            DinosaurAI.PickedUpScent += OnPickedUpScent;
            ExtractionZone.Noise += DinosaurAI.NoiseAt;
            ScentTrail.Active = _scent = new ScentTrail();
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

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = label;
            go.transform.SetParent(parent, false);
            go.transform.position   = pos + Vector3.up * (size * 0.5f);
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
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
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
            foreach (var zone in _zones)
            {
                zone.SetOpen(open);
                // Boarding starts once the helicopter is down.
                if (Player != null && zone.HelicopterLanded && zone.Contains(Player.transform.position)) inZone = true;
            }

            State.Tick(Time.deltaTime, inZone);
            if (!IsRunning) return;

            RescueIfFallenThroughWorld();

            // The player's scent: a mark every couple of seconds, none while wading (water breaks the trail).
            if (_scent != null && Player != null && PlayerHealth != null && PlayerHealth.IsAlive)
            {
                Vector3 p = Player.transform.position;
                _scent.Tick(p, State.Elapsed, !ViewBlockers.InWater(p));
            }

            ThreatTarget? target = null;
            if (Player != null && PlayerHealth != null && PlayerHealth.IsAlive)
                target = new ThreatTarget(PlayerTeam, Player.transform.position, Player.transform);

            float healthFraction = PlayerHealth != null ? PlayerHealth.Fraction : 1f;
            Brain.Intensity = Difficulty.Tick(Time.deltaTime, State.Elapsed, healthFraction);
            Brain.Tick(Time.deltaTime, State.Elapsed, target);

            _wildlife?.Tick(State.Elapsed, Player != null ? Player.transform.position : (Vector3?)null);
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
            _executor.Execute(e, Content.directorSettings.ScaledSpawnCount(e.Threat.spawnCount, Difficulty.Intensity));
            Announce(e.Threat.announcement);
        }

        private void OnPickedUpScent(DinosaurAI dino)
        {
            if (dino == null || dino.species == null || State == null) return;
            if (State.Elapsed - _lastScentWarning < 30f) return; // one warning per stalk, not per sniff
            _lastScentWarning = State.Elapsed;
            Announce("Something has your scent. Hide, or wade through water.");
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

            float waterY = ViewBlockers.WaterHeight;
            for (int tries = 0; tries < 16; tries++)
            {
                float angle = _flareRng.NextFloat() * Mathf.PI * 2f;
                float dist  = Mathf.Lerp(rules.flareDistance.x, rules.flareDistance.y, _flareRng.NextFloat());
                Vector3 c = p + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;
                if (!NavMesh.SamplePosition(c, out var hit, 12f, NavMesh.AllAreas)) continue;
                if (hit.position.y < waterY + 0.5f) continue; // not in the shallows

                var zone = ExtractionZone.Create(hit.position, rules.extractionRadius, _islandRoot);
                zone.SetOpen(true);
                _zones.Add(zone);
                distance = Flat(hit.position - p).magnitude;
                return true;
            }
            return false;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

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

        private void OnPlayerDamaged(DamageInfo info)
        {
            if (IsRunning) Difficulty.ReportPlayerHurt(State.Elapsed);
        }

        private void OnPlayerDied(DamageInfo info) => State?.ReportPlayerDied();

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
            RecordScore(result);

            if (PlayerController != null && PlayerController.enabled)
                PlayerController.InputBlocked = true;

            MatchEnded?.Invoke(Stats);
        }

        private void RecordScore(MatchResult result)
        {
            float t = State.Elapsed;
            Stats.SurvivalPoints   = Score.SurvivalPoints(t);
            Stats.KillPoints       = Score.KillPoints;
            Stats.DamagePoints     = Score.DamagePoints;
            Stats.ThreatPoints     = Score.ThreatPoints;
            Stats.ResultMultiplier = ScoreModel.Multiplier(result);
            Stats.Score            = Score.Total(t, result);

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
            if (_menuInput != null)   _menuInput.UseItemPressed -= OnUseItem;
            if (State != null)
            {
                State.ExtractionOpened -= OnExtractionOpened;
                State.SurvivalPayout   -= OnSurvivalPayout;
                State.Ended            -= OnEnded;
            }

            _zones.Clear(); // the zone objects live under the island root and die with it
            State = null;
        }

        private void OnDestroy() => Cleanup();
    }
}
