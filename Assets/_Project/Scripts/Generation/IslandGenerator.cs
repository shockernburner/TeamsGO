using System.Collections.Generic;
using UnityEngine;
using ProjectFossil.Core;

namespace ProjectFossil.Generation
{
    public class IslandGenerator
    {
        private readonly RNGService _rng;
        private readonly IslandSettings _settings;

        // Noise offsets derived from seed — different per instance
        private float _heightOffX, _heightOffY;
        private float _moistureOffX, _moistureOffY;

        public int Seed => _rng.Seed;

        public IslandGenerator(int seed, IslandSettings settings)
        {
            _rng = new RNGService(seed);
            _settings = settings;
        }

        public IslandData Generate()
        {
            _heightOffX   = _rng.NextFloat() * 10000f;
            _heightOffY   = _rng.NextFloat() * 10000f;
            _moistureOffX = _rng.NextFloat() * 10000f;
            _moistureOffY = _rng.NextFloat() * 10000f;

            int res = _settings.resolution;
            var heightmap = new float[res, res];
            var biomeMap  = new int[res, res];
            var landMask  = new bool[res, res];

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float nx = (float)x / (res - 1);
                    float ny = (float)y / (res - 1);

                    float h = SampleHeight(nx, ny);
                    h = Mathf.Clamp01(h - IslandFalloff(nx, ny));

                    var curve = _settings.heightRemap;
                    if (curve != null && curve.keys.Length > 0)
                        h = curve.Evaluate(h);

                    heightmap[y, x] = h;
                    landMask[y, x]  = h > 0.05f;
                }
            }

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float nx       = (float)x / (res - 1);
                    float ny       = (float)y / (res - 1);
                    float moisture = SampleMoisture(nx, ny);
                    biomeMap[y, x] = ClassifyBiome(heightmap[y, x], moisture);
                }
            }

            var pois       = PlacePOIs(heightmap, landMask, res);
            var spawnZones = PlaceSpawnZones(heightmap, biomeMap, landMask, res);

            return new IslandData(_rng.Seed, _settings, heightmap, biomeMap, landMask, pois, spawnZones);
        }

        // ── Noise ────────────────────────────────────────────────────────────

        private float SampleHeight(float nx, float ny)
            => FractalNoise(nx, ny, _settings.noiseScale * (_settings.resolution - 1), _heightOffX, _heightOffY);

        private float SampleMoisture(float nx, float ny)
            => FractalNoise(nx, ny, _settings.noiseScale * (_settings.resolution - 1) * 0.6f, _moistureOffX, _moistureOffY);

        private static float FractalNoise(float nx, float ny, float scale,
            float offX, float offY, int octaves = 5, float persistence = 0.5f, float lacunarity = 2f)
        {
            float value = 0f, amplitude = 1f, frequency = 1f, maxValue = 0f;
            for (int i = 0; i < octaves; i++)
            {
                value    += Mathf.PerlinNoise(nx * scale * frequency + offX + i * 2.3f,
                                              ny * scale * frequency + offY + i * 2.3f) * amplitude;
                maxValue += amplitude;
                amplitude  *= persistence;
                frequency  *= lacunarity;
            }
            return value / maxValue;
        }

        // Smooth radial falloff — pushes edges toward sea level
        private float IslandFalloff(float nx, float ny)
        {
            float dx   = (nx - 0.5f) * 2f;
            float dy   = (ny - 0.5f) * 2f;
            float dist = Mathf.Sqrt(dx * dx + dy * dy); // 0 at center, ~1.41 at corner
            float limit = _settings.islandRadiusFraction * 2f;
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(limit - 0.15f, limit + 0.35f, dist));
        }

        // ── Biome classification ──────────────────────────────────────────────

        private int ClassifyBiome(float height, float moisture)
        {
            var biomes = _settings.biomes;
            if (biomes == null || biomes.Count == 0) return 0;

            // First pass: exact match
            for (int i = 0; i < biomes.Count; i++)
            {
                var b = biomes[i];
                if (b == null) continue;
                if (height   >= b.minHeight   && height   <= b.maxHeight &&
                    moisture >= b.minMoisture && moisture <= b.maxMoisture)
                    return i;
            }

            // Fallback: nearest biome centre by height
            int   best     = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < biomes.Count; i++)
            {
                var b = biomes[i];
                if (b == null) continue;
                float mid = (b.minHeight + b.maxHeight) * 0.5f;
                float d   = Mathf.Abs(height - mid);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        // ── POI placement ────────────────────────────────────────────────────

        private List<PointOfInterest> PlacePOIs(float[,] heightmap, bool[,] landMask, int res)
        {
            var result        = new List<PointOfInterest>();
            float normSpacing = _settings.minPOISpacing / _settings.worldSize;
            int   gridSpacing = Mathf.Max(1, Mathf.RoundToInt(normSpacing * res));

            Place(POIType.ExtractionZone, _settings.extractionZoneCount,  0.08f, 0.55f, gridSpacing,     heightmap, landMask, res, result);
            Place(POIType.LootCache,      _settings.lootCacheCount,        0.05f, 0.90f, gridSpacing / 2, heightmap, landMask, res, result);
            Place(POIType.Ruins,          _settings.ruinsCount,            0.05f, 0.80f, gridSpacing / 2, heightmap, landMask, res, result);

            return result;
        }

        private void Place(POIType type, int count, float minH, float maxH, int minGrid,
            float[,] heightmap, bool[,] landMask, int res, List<PointOfInterest> results)
        {
            int attempts = count * 80;
            int placed   = 0;

            for (int i = 0; i < attempts && placed < count; i++)
            {
                int gx = _rng.Next(res);
                int gy = _rng.Next(res);
                float h = heightmap[gy, gx];

                if (!landMask[gy, gx] || h < minH || h > maxH) continue;

                bool tooClose = false;
                foreach (var e in results)
                {
                    int dx = e.GridPos.x - gx, dy = e.GridPos.y - gy;
                    if (dx * dx + dy * dy < minGrid * minGrid) { tooClose = true; break; }
                }
                if (tooClose) continue;

                float worldX = ((float)gx / (res - 1)) * _settings.worldSize;
                float worldZ = ((float)gy / (res - 1)) * _settings.worldSize;
                float worldY = h * _settings.maxHeight;

                results.Add(new PointOfInterest
                {
                    GridPos  = new Vector2Int(gx, gy),
                    WorldPos = new Vector3(worldX, worldY, worldZ),
                    Type     = type
                });
                placed++;
            }
        }

        // ── Spawn zone placement ──────────────────────────────────────────────

        private List<SpawnZone> PlaceSpawnZones(float[,] heightmap, int[,] biomeMap, bool[,] landMask, int res)
        {
            var zones      = new List<SpawnZone>();
            float worldR   = _settings.worldSize * 0.07f;
            int   gridR    = Mathf.Max(2, Mathf.RoundToInt((worldR / _settings.worldSize) * res));
            int   attempts = _settings.spawnZoneCount * 40;
            int   placed   = 0;

            for (int i = 0; i < attempts && placed < _settings.spawnZoneCount; i++)
            {
                int gx = _rng.Next(gridR, res - gridR);
                int gy = _rng.Next(gridR, res - gridR);

                if (!landMask[gy, gx] || heightmap[gy, gx] < 0.06f) continue;

                bool tooClose = false;
                foreach (var z in zones)
                {
                    int dx = z.Center.x - gx, dy = z.Center.y - gy;
                    if (dx * dx + dy * dy < gridR * gridR * 9) { tooClose = true; break; }
                }
                if (tooClose) continue;

                float worldX = ((float)gx / (res - 1)) * _settings.worldSize;
                float worldZ = ((float)gy / (res - 1)) * _settings.worldSize;
                float worldY = heightmap[gy, gx] * _settings.maxHeight;

                zones.Add(new SpawnZone
                {
                    Center      = new Vector2Int(gx, gy),
                    WorldCenter = new Vector3(worldX, worldY, worldZ),
                    WorldRadius = worldR,
                    BiomeIndex  = biomeMap[gy, gx],
                    Weight      = 1f
                });
                placed++;
            }

            return zones;
        }
    }
}
