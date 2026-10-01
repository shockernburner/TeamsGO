using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using ProjectFossil.Core;
using ProjectFossil.Generation;
using ProjectFossil.Dinosaurs;
using ProjectFossil.Economy;
using ProjectFossil.Player;

namespace ProjectFossil.Match
{
    // Place on a scene object. Generates the island, bakes NavMesh, spawns player and dinosaurs,
    // then hands everything to the MatchManager on the same object.
    public class MatchBootstrap : MonoBehaviour
    {
        [Header("Content")]
        [Tooltip("Empty = Resources/GameContent")]
        public GameContent content;

        [Header("Island")]
        public IslandSettings islandSettings;
        public int seed = 0;
        public bool randomSeed = true;

        [Header("Player")]
        public GameObject playerPrefab;
        public float spawnHeightOffset = 3f;

        [Header("Dinosaurs")]
        public GameObject dinosaurPrefab;
        [Tooltip("Only used when GameContent has no wildlife table")]
        public int dinosaursPerSpawnZone = 2;

        public IslandData LastData { get; private set; }
        public MatchManager Match  { get; private set; }

        // Online hooks (the networking layer sets these; solo play never touches them).
        [System.NonSerialized] public bool holdStart;       // wait for a menu choice instead of starting at once
        [System.NonSerialized] public Vector3 spawnOffset;  // teammates land beside each other, not inside
        [System.NonSerialized] public int playerSlot;       // online client number: picks this player's outfit
        // Whether this machine may start the next island, and what to show instead when it can't.
        public System.Func<bool> CanRestart;
        public string RestartNote;
        // Raised after every new island is generated and the match has begun (seed).
        public event System.Action<int> Generated;

        private GameObject _player;

        public bool AllowsRestart => CanRestart == null || CanRestart();

        private void Start()
        {
            if (holdStart) return;
            StartSolo();
        }

        public void StartSolo()
        {
            int usedSeed = randomSeed ? Random.Range(0, int.MaxValue) : seed;
            GenerateAndSpawn(usedSeed);
        }

        // New island, new match. Used by the results screen.
        public void Restart(bool newSeed = true)
        {
            if (!AllowsRestart) return;
            int usedSeed = newSeed ? Random.Range(0, int.MaxValue) : (LastData != null ? LastData.Seed : seed);
            GenerateAndSpawn(usedSeed);
        }

        public void GenerateAndSpawn(int usedSeed)
        {
            if (islandSettings == null)
            {
                Debug.LogError("[MatchBootstrap] IslandSettings not assigned.");
                return;
            }

            if (content == null) content = GameContent.LoadDefault();
            if (content == null)
            {
                Debug.LogError("[MatchBootstrap] No GameContent assigned and none found at Resources/GameContent.");
                return;
            }

            if (_player != null)
            {
                _player.SetActive(false); // run OnDisable now so the old camera/cursor state doesn't linger
                Destroy(_player);
            }
            IslandTerrainBuilder.DestroyExisting();

            var generator = new IslandGenerator(usedSeed, islandSettings);
            LastData = generator.Generate();
            var islandGO = IslandTerrainBuilder.Build(LastData, null, showPoiMarkers: false);
            Physics.SyncTransforms(); // make the new terrain collider solid before anything is placed on it

            BakeNavMesh(islandGO);
            if (content.wildlife == null && !NetRole.IsFollower) SpawnDinosaurs(LastData, islandGO.transform); // otherwise the match spawns wildlife
            _player = SpawnPlayer(LastData);

            Match = GetComponent<MatchManager>();
            if (Match == null) Match = gameObject.AddComponent<MatchManager>();
            if (_player != null)
                Match.Begin(LastData, _player, content, dinosaurPrefab, islandGO.transform);

            WorldConditions.Begin(usedSeed); // sky and weather for this island (the sky reacts to it)
            if (_player != null) Match.Announce(ConditionsHint(WorldConditions.Current));

            Debug.Log($"[MatchBootstrap] Seed {usedSeed} | {LastData.SpawnZones.Count} spawn zones | {LastData.PointsOfInterest.Count} POIs | {WorldConditions.Describe(WorldConditions.Current)}");
            Generated?.Invoke(usedSeed);
        }

        private static string ConditionsHint(Conditions c)
        {
            string hint = c.Weather == Weather.Storm ? "The storm drowns out your footsteps."
                        : c.Weather == Weather.Rain  ? "Rain covers your footsteps."
                        : c.Weather == Weather.Fog   ? "In the fog, nothing sees far. Neither do you."
                        : c.Time == DayTime.Night    ? "In the dark they see less. Your lamp is on (L)."
                        : "";
            return $"{WorldConditions.Describe(c)}. {hint}".Trim();
        }

        // ── NavMesh ────────────────────────────────────────────────────────────

        private void BakeNavMesh(GameObject islandGO)
        {
            var surface = islandGO.GetComponent<NavMeshSurface>();
            if (surface == null)
                surface = islandGO.AddComponent<NavMeshSurface>();

            surface.collectObjects = CollectObjects.All;
            surface.useGeometry    = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
        }

        // ── Dinosaur spawning ──────────────────────────────────────────────────

        private void SpawnDinosaurs(IslandData data, Transform parent)
        {
            if (dinosaurPrefab == null) return;

            // Keep player spawn zone (index 0) clear of dinosaurs
            for (int zi = 1; zi < data.SpawnZones.Count; zi++)
            {
                var zone = data.SpawnZones[zi];
                for (int i = 0; i < dinosaursPerSpawnZone; i++)
                {
                    Vector2 offset = Random.insideUnitCircle * zone.WorldRadius * 0.8f;
                    Vector3 pos    = zone.WorldCenter + new Vector3(offset.x, 0f, offset.y);

                    // Snap to terrain height
                    if (NavMesh.SamplePosition(pos, out var hit, zone.WorldRadius, NavMesh.AllAreas))
                        pos = hit.position;

                    var dino = Instantiate(dinosaurPrefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), parent);
                    dino.name = $"Dino_Zone{zi}_{i}";
                    DinosaurAI.AnnounceCreated(dino.GetComponent<DinosaurAI>());
                }
            }
        }

        // ── Player spawning ────────────────────────────────────────────────────

        private GameObject SpawnPlayer(IslandData data)
        {
            if (playerPrefab == null) return null;

            Vector3 spawnPos = GetSpawnPosition(data) + spawnOffset;
            var player = Instantiate(playerPrefab, spawnPos, Quaternion.identity);

            // Gameplay components the prefab may not carry yet (inventory first: combat reads its weapon).
            EnsureComponent<PlayerInventory>(player);
            EnsureComponent<PlayerCombat>(player);
            EnsureComponent<PlayerInteractor>(player);
            EnsureComponent<PlayerMenuInput>(player);
            var look = BoughtArt.Survivor(playerSlot, content != null ? content.playerModel : null);
            PlayerVisual.Attach(player, look);
            var art = BoughtArt.Current;
            Debug.Log($"[MatchBootstrap] Player look: {(look != null && look.model != null ? look.model.name : "capsule")}; " +
                      $"bought art: {(art == null ? "none (run Project Fossil > Art > Set Up Bought Packs)" : $"{art.survivors?.Length ?? 0} survivors, helicopter {(art.helicopter != null ? art.helicopter.name : "none")}")}");
            return player;
        }

        private static T EnsureComponent<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private Vector3 GetSpawnPosition(IslandData data)
        {
            if (data.SpawnZones.Count > 0)
                return data.SpawnZones[0].WorldCenter + Vector3.up * spawnHeightOffset;

            float cx = data.Settings.worldSize * 0.5f;
            float cz = data.Settings.worldSize * 0.5f;
            int   gx = data.Resolution / 2;
            int   gy = data.Resolution / 2;
            float cy = data.Heightmap[gy, gx] * data.Settings.maxHeight + spawnHeightOffset;
            return new Vector3(cx, cy, cz);
        }
    }
}
