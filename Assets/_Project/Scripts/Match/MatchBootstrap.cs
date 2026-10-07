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

        // Set by the start menu. Whether the in-match menu may pause the game (solo only: in co-op the island
        // keeps going for everyone), and what "Leave match" does (back to the start menu).
        public System.Func<bool> CanPause;
        public System.Action LeaveRequested;
        public bool AllowsPause => CanPause == null || CanPause();

        // Takes the current island down: player, terrain with everything on it, and the match.
        public void Leave()
        {
            if (Match != null) Match.Abandon();
            if (_player != null)
            {
                _player.SetActive(false);
                Destroy(_player);
                _player = null;
            }
            IslandTerrainBuilder.DestroyExisting();
            IslandWorld.SetCurrent(null);
        }

        private void Start()
        {
            if (holdStart) return;
            StartSolo();
        }

        // On the start menu (and after leaving a match) there is no player and so no camera to hear with; Unity
        // then complained every frame. This ear stands in until a player arrives, and steps aside for theirs.
        private AudioListener _menuEar;

        private void Awake() => _menuEar = gameObject.AddComponent<AudioListener>(); // from the very first frame

        private void LateUpdate()
        {
            if (_menuEar == null) _menuEar = gameObject.AddComponent<AudioListener>();
            bool needed = _player == null || !_player.activeInHierarchy;
            if (_menuEar.enabled != needed) _menuEar.enabled = needed;
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

        // An island to look at behind the title and the menu: ground, water, plants and sky, no player and no match.
        // Returns its centre and size. The next GenerateAndSpawn replaces it.
        public (Vector3 centre, float extent, float top) BuildPreviewIsland(int previewSeed)
        {
            if (islandSettings == null) return (Vector3.zero, 0f, 0f);
            if (_player != null) Leave();
            IslandTerrainBuilder.DestroyExisting();
            var data = new IslandGenerator(previewSeed, islandSettings).Generate();
            IslandTerrainBuilder.Build(data, null, showPoiMarkers: false);
            Physics.SyncTransforms();
            // Golden light under a clear sky: the island at its most inviting, whatever the player picked.
            WorldConditions.Override(DayTime.Dusk, Weather.Clear, 210f);
            WorldConditions.Begin(previewSeed);
            float size = islandSettings.worldSize;
            return (new Vector3(size * 0.5f, 0f, size * 0.5f), size, islandSettings.maxHeight);
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

            BakeNavMesh(islandGO, LastData);
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

        private void BakeNavMesh(GameObject islandGO, IslandData data)
        {
            // Dinosaurs don't go into water deeper than they can wade: they walked lake beds out of sight and bit
            // swimmers from below. They wait on the shore instead, which makes a lake a place to hide. Streams are
            // shallow everywhere, so they stay crossable. The deep water comes from the same IslandWorld the water
            // is drawn from, in strips of blocks a few metres across.
            var world = IslandWorld.Current;
            if (world != null)
            {
                const int block = 4; // vertices (about 4 m)
                int blocks = (world.Size - 1) / block;
                var deepRoot = new GameObject("DeepWater").transform;
                deepRoot.SetParent(islandGO.transform, false);
                int area = NavMesh.GetAreaFromName("Not Walkable");
                for (int bz = 0; bz < blocks; bz++)
                {
                    int run = -1; float low = 0f, high = 0f;
                    for (int bx = 0; bx <= blocks; bx++)
                    {
                        bool deep = false; float bed = 0f, top = 0f;
                        if (bx < blocks)
                        {
                            // Deep if any vertex of the block is: the centre alone let dinosaurs wade chest-deep
                            // along every shore. The sea counts too: without it the whole sea floor round the island
                            // was walkable, and dinosaurs wandered off the beach to graze seven metres under the waves.
                            bed = float.MaxValue; top = float.MinValue;
                            for (int vz = bz * block; vz <= (bz + 1) * block; vz++)
                            for (int vx = bx * block; vx <= (bx + 1) * block; vx++)
                            {
                                float w = world.WaterAtVertex(vx, vz);
                                float g = world.GroundAtVertex(vx, vz);
                                float s = float.IsNaN(w) ? world.SeaLevel : Mathf.Max(w, world.SeaLevel);
                                bed = Mathf.Min(bed, g); top = Mathf.Max(top, s);
                                if (s - g > DinosaurWadeDepth) deep = true;
                            }
                        }
                        if (deep)
                        {
                            if (run < 0) { run = bx; low = bed; high = top; }
                            else { low = Mathf.Min(low, bed); high = Mathf.Max(high, top); }
                            continue;
                        }
                        if (run < 0) continue;
                        float x0 = run * block * world.Cell, x1 = bx * block * world.Cell;
                        float z0 = bz * block * world.Cell, z1 = (bz + 1) * block * world.Cell;
                        var go = new GameObject("Deep");
                        go.transform.SetParent(deepRoot, false);
                        go.transform.localPosition = new Vector3((x0 + x1) * 0.5f, (low + high) * 0.5f, (z0 + z1) * 0.5f);
                        var deepVolume = go.AddComponent<NavMeshModifierVolume>();
                        deepVolume.size   = new Vector3(x1 - x0, high - low + 3f, z1 - z0);
                        deepVolume.center = Vector3.zero;
                        deepVolume.area   = area;
                        run = -1;
                    }
                }
            }

            var surface = islandGO.GetComponent<NavMeshSurface>();
            if (surface == null)
                surface = islandGO.AddComponent<NavMeshSurface>();

            surface.collectObjects = CollectObjects.All;
            surface.useGeometry    = NavMeshCollectGeometry.PhysicsColliders;
            // Agents stand on the NavMesh, whose polygons are simplified and cut up to a metre under rounded hills;
            // the height mesh follows the real ground, so dinosaurs no longer sink to the knees on slopes.
            surface.buildHeightMesh = true;
            surface.BuildNavMesh();
        }

        // Water deeper than this keeps dinosaurs out (metres): a little over the deepest stream.
        public const float DinosaurWadeDepth = 0.9f;

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
            if (_menuEar != null) _menuEar.enabled = false; // the player's camera hears from this frame on

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
