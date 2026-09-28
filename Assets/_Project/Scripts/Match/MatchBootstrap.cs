using UnityEngine;
using ProjectFossil.Generation;

namespace ProjectFossil.Match
{
    // Place on a scene object. Generates the island and spawns the player on Start.
    public class MatchBootstrap : MonoBehaviour
    {
        [Header("Island")]
        public IslandSettings islandSettings;
        public int seed = 0;
        public bool randomSeed = true;

        [Header("Player")]
        public GameObject playerPrefab;
        public float spawnHeightOffset = 3f;

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
            IslandTerrainBuilder.Build(LastData);

            SpawnPlayer(LastData);

            Debug.Log($"[MatchBootstrap] Seed {usedSeed} | {LastData.SpawnZones.Count} spawn zones | {LastData.PointsOfInterest.Count} POIs");
        }

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

            // Fallback: island centre at a safe height
            float cx = data.Settings.worldSize * 0.5f;
            float cz = data.Settings.worldSize * 0.5f;
            int   gx = data.Resolution / 2;
            int   gy = data.Resolution / 2;
            float cy = data.Heightmap[gy, gx] * data.Settings.maxHeight + spawnHeightOffset;
            return new Vector3(cx, cy, cz);
        }
    }
}
