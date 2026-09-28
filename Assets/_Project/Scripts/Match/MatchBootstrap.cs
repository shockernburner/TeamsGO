using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using ProjectFossil.Generation;
using ProjectFossil.Dinosaurs;

namespace ProjectFossil.Match
{
    // Place on a scene object. Generates the island, bakes NavMesh, spawns player and dinosaurs on Start.
    public class MatchBootstrap : MonoBehaviour
    {
        [Header("Island")]
        public IslandSettings islandSettings;
        public int seed = 0;
        public bool randomSeed = true;

        [Header("Player")]
        public GameObject playerPrefab;
        public float spawnHeightOffset = 3f;

        [Header("Dinosaurs")]
        public GameObject dinosaurPrefab;
        public int dinosaursPerSpawnZone = 2;

        public IslandData LastData { get; private set; }

        private void Start()
        {
            int usedSeed = randomSeed ? Random.Range(0, int.MaxValue) : seed;
            GenerateAndSpawn(usedSeed);
        }

        public void GenerateAndSpawn(int usedSeed)
        {
            if (islandSettings == null)
            {
                Debug.LogError("[MatchBootstrap] IslandSettings not assigned.");
                return;
            }

            IslandTerrainBuilder.DestroyExisting();

            var generator = new IslandGenerator(usedSeed, islandSettings);
            LastData = generator.Generate();
            var islandGO = IslandTerrainBuilder.Build(LastData);

            BakeNavMesh(islandGO);
            SpawnDinosaurs(LastData, islandGO.transform);
            SpawnPlayer(LastData);

            Debug.Log($"[MatchBootstrap] Seed {usedSeed} | {LastData.SpawnZones.Count} spawn zones | {LastData.PointsOfInterest.Count} POIs");
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
                }
            }
        }

        // ── Player spawning ────────────────────────────────────────────────────

        private void SpawnPlayer(IslandData data)
        {
            if (playerPrefab == null) return;

            Vector3 spawnPos = GetSpawnPosition(data);
            Instantiate(playerPrefab, spawnPos, Quaternion.identity);
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
