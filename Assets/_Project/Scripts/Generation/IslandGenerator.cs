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
            _ridgeOffX    = _rng.NextFloat() * 10000f;
            _ridgeOffY    = _rng.NextFloat() * 10000f;
            _maskOffX     = _rng.NextFloat() * 10000f;
            _maskOffY     = _rng.NextFloat() * 10000f;

            int res = _settings.resolution;
            var heightmap = new float[res, res];
            var biomeMap  = new int[res, res];
            var landMask  = new bool[res, res];

            // 1. Relief: rolling base, ridged mountain chains where the mask allows, fine detail, island falloff.
            float maxRaw = 0.0001f;
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float nx = (float)x / (res - 1);
                    float ny = (float)y / (res - 1);
                    // Land rises gently from the coast toward the middle, so there are broad lowlands for rivers.
                    float h = Mathf.Max(0f, SampleRelief(nx, ny) * (0.45f + 0.55f * InlandProfile(nx, ny)) - IslandFalloff(nx, ny));
                    heightmap[y, x] = h;
                    if (h > maxRaw) maxRaw = h;
                }
            }

            // 2. Keep lowlands low and peaks sharp, then raise the volcano.
            Vector2 volcano = PickVolcanoCentre();
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float nx = (float)x / (res - 1);
                    float ny = (float)y / (res - 1);
                    float h = Mathf.Pow(heightmap[y, x] / maxRaw, _settings.lowlandPower) * 0.75f;
                    h += VolcanoAt(nx, ny, volcano);

                    var curve = _settings.heightRemap;
                    if (curve != null && curve.keys.Length > 0)
                        h = curve.Evaluate(Mathf.Clamp01(h));
                    heightmap[y, x] = Mathf.Clamp01(h);
                }
            }

            // 3. Rivers and lakes.
            var wetness = new float[res, res];
            var water   = new bool[res, res];
            var rivers  = new List<RiverPath>();
            var lakes   = new List<Lake>();
            CarveWater(heightmap, wetness, water, res, rivers, lakes);

            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                    landMask[y, x] = heightmap[y, x] > LandLevel && !water[y, x];

            // 4. Biomes from height and moisture (wetter near water).
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float nx       = (float)x / (res - 1);
                    float ny       = (float)y / (res - 1);
                    float moisture = Mathf.Clamp01(SampleMoisture(nx, ny) + wetness[y, x]);
                    biomeMap[y, x] = ClassifyBiome(heightmap[y, x], moisture);
                }
            }

            var pois       = PlacePOIs(heightmap, landMask, res);
            var spawnZones = PlaceSpawnZones(heightmap, biomeMap, landMask, res);

            return new IslandData(_rng.Seed, _settings, heightmap, biomeMap, landMask, pois, spawnZones, rivers, lakes);
        }

        public const float LandLevel = 0.05f;

        private float _ridgeOffX, _ridgeOffY, _maskOffX, _maskOffY;

        // ── Noise ────────────────────────────────────────────────────────────

        private float BaseScale => _settings.noiseScale * (_settings.resolution - 1);

        private float SampleRelief(float nx, float ny)
        {
            float baseH = FractalNoise(nx, ny, BaseScale, _heightOffX, _heightOffY);

            // Mountains only where a slow mask says so, so the island gets ranges and open lowlands, not even hills.
            float mask  = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.68f,
                              Mathf.PerlinNoise(nx * BaseScale * 0.7f + _maskOffX, ny * BaseScale * 0.7f + _maskOffY)));
            float ridge = RidgedNoise(nx, ny, BaseScale * _settings.ridgeScale, _ridgeOffX, _ridgeOffY);

            float detail = (FractalNoise(nx, ny, BaseScale * 8f, _heightOffY, _ridgeOffX, 3) - 0.5f) * 2f;

            return baseH * 0.8f + ridge * mask * _settings.ridgeStrength + detail * _settings.detailStrength;
        }

        private float SampleMoisture(float nx, float ny)
        {
            float m = FractalNoise(nx, ny, BaseScale * 0.6f, _moistureOffX, _moistureOffY);
            return Mathf.Clamp01((m - 0.5f) * _settings.moistureContrast + 0.5f);
        }

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

        // Sharp crests where plain noise crosses its midpoint: reads as ridgelines and mountain spines.
        private static float RidgedNoise(float nx, float ny, float scale, float offX, float offY, int octaves = 4)
        {
            float value = 0f, amplitude = 1f, frequency = 1f, maxValue = 0f, weight = 1f;
            for (int i = 0; i < octaves; i++)
            {
                float n = Mathf.PerlinNoise(nx * scale * frequency + offX + i * 5.1f, ny * scale * frequency + offY + i * 5.1f);
                n = 1f - Mathf.Abs(n * 2f - 1f);
                n *= n * weight;
                weight = Mathf.Clamp01(n * 1.5f);
                value    += n * amplitude;
                maxValue += amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
            }
            return value / maxValue;
        }

        // 1 in the island's heart, easing to 0 at the coast.
        private float InlandProfile(float nx, float ny)
        {
            float dx = (nx - 0.5f) * 2f, dy = (ny - 0.5f) * 2f;
            float r = Mathf.Sqrt(dx * dx + dy * dy) / (_settings.islandRadiusFraction * 2f * CoastWarp(nx, ny));
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 1.05f, r));
        }

        // Pushes the coastline in and out (bays and headlands) so the island isn't a circle.
        private float CoastWarp(float nx, float ny)
        {
            float n = FractalNoise(nx, ny, BaseScale * 1.2f, _maskOffY, _moistureOffX, 3);
            return 1f + (n - 0.5f) * 0.8f;
        }

        // Smooth radial falloff — pushes edges toward sea level
        private float IslandFalloff(float nx, float ny)
        {
            float dx   = (nx - 0.5f) * 2f;
            float dy   = (ny - 0.5f) * 2f;
            float dist = Mathf.Sqrt(dx * dx + dy * dy) / CoastWarp(nx, ny); // 0 at center, ~1.41 at corner
            float limit = _settings.islandRadiusFraction * 2f;
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(limit - 0.15f, limit + 0.35f, dist));
        }

        // ── Volcano ──────────────────────────────────────────────────────────

        // Somewhere inland, off centre, so it's a landmark you can navigate by.
        private Vector2 PickVolcanoCentre()
        {
            float angle = _rng.NextFloat() * Mathf.PI * 2f;
            float r     = Mathf.Lerp(0.08f, 0.22f, _rng.NextFloat());
            return new Vector2(0.5f + Mathf.Cos(angle) * r, 0.5f + Mathf.Sin(angle) * r);
        }

        // Concave cone with a crater at the top. Normalized height to add at (nx, ny).
        private float VolcanoAt(float nx, float ny, Vector2 centre)
        {
            float radius = _settings.volcanoRadius;
            if (radius <= 0f || _settings.volcanoHeight <= 0f) return 0f;
            float dx = nx - centre.x, dy = ny - centre.y;
            float d = Mathf.Sqrt(dx * dx + dy * dy) / radius;
            if (d >= 1f) return 0f;

            float cone = Mathf.Pow(1f - d, 1.8f) * _settings.volcanoHeight;
            const float craterEdge = 0.2f;
            if (d < craterEdge)
            {
                float t = 1f - d / craterEdge;
                cone -= _settings.craterDepth * t * t;
            }
            return cone;
        }

        // ── Rivers and lakes ─────────────────────────────────────────────────

        // Rivers run downhill from the hills to the sea with a water surface that only ever falls; the channel is
        // cut a little below it. Lakes fill low inland hollows. The decorator draws the water from Rivers/Lakes.
        private void CarveWater(float[,] heightmap, float[,] wetness, bool[,] water, int res,
                                List<RiverPath> rivers, List<Lake> lakes)
        {
            var s = _settings;
            float cell   = s.worldSize / (res - 1);
            float depth  = s.waterDepth / s.maxHeight;
            float margin = 0.4f / s.maxHeight; // water sits this far below the banks it runs between
            var carved = new float[res, res];
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                    carved[y, x] = float.MaxValue;

            var sources = new List<Vector2Int>();
            var raised  = new float[res, res]; // banks built up beside the water, so it never hangs over lower ground
            for (int r = 0; r < s.riverCount; r++)
            {
                if (!TryPickRiverSource(heightmap, res, sources, out var src)) break;
                sources.Add(src);
                var path = TraceRiver(heightmap, res, src);
                if (path.Count < 12) continue;

                // The water surface only ever falls.
                var surfaces = new float[path.Count];
                float surface = heightmap[src.y, src.x] - margin;
                for (int i = 0; i < path.Count; i++)
                {
                    var p = path[i];
                    surface = Mathf.Max(s.seaLevel, Mathf.Min(surface, heightmap[p.y, p.x] - margin));
                    surfaces[i] = surface;
                }

                // Water only where the river runs gently. Down a steep hillside it was a thin blue sheet lying on
                // the slope; those stretches stay dry ground and the river starts (or starts again) below them.
                int start = -1;
                for (int i = 0; i <= path.Count; i++)
                {
                    bool gentle = i < path.Count && Gradient(path, surfaces, i, cell) * s.maxHeight <= s.maxRiverGradient;
                    if (gentle) { if (start < 0) start = i; continue; }
                    if (start >= 0 && i - start >= 12)
                    {
                        var river = new RiverPath();
                        for (int k = start; k < i; k++)
                        {
                            var p = path[k];
                            float t = (float)k / (path.Count - 1);
                            float halfWidth = s.riverWidth * Mathf.Lerp(0.35f, 0.7f, t);
                            Stamp(carved, wetness, water, res, cell, p, halfWidth, surfaces[k] - depth, MaxAround(heightmap, res, p, 8));
                            Bank(raised, res, cell, p, halfWidth + (depth + margin) * s.maxHeight / Mathf.Max(0.05f, s.riverBankSlope) + 2f,
                                 surfaces[k] + margin);
                            river.Points.Add(new Vector3(p.x * cell, surfaces[k] * s.maxHeight, p.y * cell));
                            river.HalfWidths.Add(halfWidth);
                        }
                        river.Smooth(2);
                        rivers.Add(river);
                    }
                    start = -1;
                }
            }

            for (int l = 0; l < s.lakeCount; l++)
            {
                if (!TryPickLake(heightmap, water, res, out var centre)) break;
                float radius = Mathf.Lerp(s.lakeRadius.x, s.lakeRadius.y, _rng.NextFloat());
                float surface = Mathf.Max(s.seaLevel, MinAround(heightmap, res, centre, Mathf.CeilToInt(radius / cell)) - margin);
                Stamp(carved, wetness, water, res, cell, centre, radius, surface - depth, MaxAround(heightmap, res, centre, 12));
                lakes.Add(new Lake
                {
                    Center = new Vector3(centre.x * cell, surface * s.maxHeight, centre.y * cell),
                    Radius = radius,
                });
            }

            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    if (raised[y, x] > heightmap[y, x]) heightmap[y, x] = raised[y, x];
                    if (carved[y, x] < heightmap[y, x]) heightmap[y, x] = carved[y, x];
                }
        }

        // Drop of the water surface (normalized height) per metre around point i of a river's path.
        private static float Gradient(List<Vector2Int> path, float[] surfaces, int i, float cell)
        {
            int a = Mathf.Max(0, i - 3), b = Mathf.Min(path.Count - 1, i + 3);
            float run = Vector2Int.Distance(path[a], path[b]) * cell;
            return run > 0f ? (surfaces[a] - surfaces[b]) / run : 0f;
        }

        // Where the ground beside a river is lower than its surface (a river crossing a slope), raise it to a bank
        // a little above the water out to `reach`, easing back down to the old ground beyond.
        private void Bank(float[,] raised, int res, float cell, Vector2Int at, float reach, float top)
        {
            float slopePerMetre = _settings.riverBankSlope / _settings.maxHeight;
            int r = Mathf.CeilToInt((reach + 40f) / cell); // far enough to ease down a hillside
            for (int dy = -r; dy <= r; dy++)
            {
                int y = at.y + dy;
                if (y < 0 || y >= res) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = at.x + dx;
                    if (x < 0 || x >= res) continue;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) * cell;
                    float target = top - Mathf.Max(0f, d - reach) * slopePerMetre;
                    if (target > raised[y, x]) raised[y, x] = target;
                }
            }
        }

        // Lowers terrain around a point to a flat bed with sloping banks, and marks the ground around it wet.
        private void Stamp(float[,] carved, float[,] wetness, bool[,] water, int res, float cell, Vector2Int at,
                           float halfWidth, float bed, float terrainHeight)
        {
            var s = _settings;
            float slopePerMetre = s.riverBankSlope / s.maxHeight; // normalized height per metre of bank
            // Reach far enough that the bank meets the ground instead of ending in a wall.
            float bankReach = Mathf.Clamp((terrainHeight - bed) * s.maxHeight / Mathf.Max(0.05f, s.riverBankSlope) + 6f, 12f, 120f);
            float reach = halfWidth + bankReach;
            int r = Mathf.CeilToInt(reach / cell);

            for (int dy = -r; dy <= r; dy++)
            {
                int y = at.y + dy;
                if (y < 0 || y >= res) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = at.x + dx;
                    if (x < 0 || x >= res) continue;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) * cell;
                    if (d > reach) continue;

                    float target = bed + Mathf.Max(0f, d - halfWidth) * slopePerMetre;
                    if (target < carved[y, x]) carved[y, x] = target;
                    if (d <= halfWidth) water[y, x] = true;

                    float wet = s.riverMoisture * Mathf.Clamp01(1f - (d - halfWidth) / 40f);
                    if (wet > wetness[y, x]) wetness[y, x] = wet;
                }
            }
        }

        private static float MaxAround(float[,] h, int res, Vector2Int at, int radius)
        {
            float m = 0f;
            for (int y = Mathf.Max(0, at.y - radius); y <= Mathf.Min(res - 1, at.y + radius); y += 2)
                for (int x = Mathf.Max(0, at.x - radius); x <= Mathf.Min(res - 1, at.x + radius); x += 2)
                    if (h[y, x] > m) m = h[y, x];
            return m;
        }

        private static float MinAround(float[,] h, int res, Vector2Int at, int radius)
        {
            float m = float.MaxValue;
            for (int y = Mathf.Max(0, at.y - radius); y <= Mathf.Min(res - 1, at.y + radius); y++)
                for (int x = Mathf.Max(0, at.x - radius); x <= Mathf.Min(res - 1, at.x + radius); x++)
                    if ((x - at.x) * (x - at.x) + (y - at.y) * (y - at.y) <= radius * radius && h[y, x] < m) m = h[y, x];
            return m;
        }

        // Springs on hillsides: high enough for a long run to the sea, spread apart.
        private bool TryPickRiverSource(float[,] h, int res, List<Vector2Int> taken, out Vector2Int src)
        {
            var s = _settings;
            int minGap = res / 4;
            float mid = (res - 1) * 0.5f;
            bool found = false;
            float bestRadial = float.MaxValue;
            src = default;
            // Of a handful of valid spots, take the one furthest inland, for a long river.
            int candidates = 0;
            for (int attempt = 0; attempt < 800 && candidates < 12; attempt++)
            {
                int x = _rng.Next(res), y = _rng.Next(res);
                float v = h[y, x];
                if (v < s.riverSourceHeight.x || v > s.riverSourceHeight.y) continue;

                bool near = false;
                foreach (var t in taken)
                    if ((t.x - x) * (t.x - x) + (t.y - y) * (t.y - y) < minGap * minGap) { near = true; break; }
                if (near) continue;

                candidates++;
                var c = new Vector2Int(x, y);
                float radial = Radial(c, mid);
                if (radial < bestRadial) { bestRadial = radial; src = c; found = true; }
            }
            return found;
        }

        // Downhill walk with a pull toward the coast, so rivers climb out of dips instead of pooling forever.
        private List<Vector2Int> TraceRiver(float[,] h, int res, Vector2Int start)
        {
            var path    = new List<Vector2Int> { start };
            var visited = new HashSet<int> { start.y * res + start.x };
            var cur     = start;
            float centre = (res - 1) * 0.5f;
            int maxSteps = res * 3;

            for (int step = 0; step < maxSteps; step++)
            {
                if (h[cur.y, cur.x] < _settings.seaLevel) break; // reached the sea

                Vector2Int best = cur;
                bool moved = false;
                float bestScore = float.MaxValue;
                float curRadial = Radial(cur, centre);
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int x = cur.x + dx, y = cur.y + dy;
                        if (x < 0 || y < 0 || x >= res || y >= res) continue;
                        if (visited.Contains(y * res + x)) continue;

                        var n = new Vector2Int(x, y);
                        float outward = Radial(n, centre) - curRadial; // > 0 = toward the coast
                        float score = h[y, x] - outward * 0.002f + _rng.NextFloat() * 0.001f;
                        if (score < bestScore) { bestScore = score; best = n; moved = true; }
                    }
                }
                if (!moved) break; // boxed in by its own path

                visited.Add(best.y * res + best.x);
                // Mark neighbours too, so the river doesn't fold back beside itself.
                visited.Add(cur.y * res + best.x);
                visited.Add(best.y * res + cur.x);
                path.Add(best);
                cur = best;
            }
            return path;
        }

        private static float Radial(Vector2Int p, float centre)
        {
            float dx = p.x - centre, dy = p.y - centre;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        // Lakes sit in low, inland ground away from rivers.
        private bool TryPickLake(float[,] h, bool[,] water, int res, out Vector2Int centre)
        {
            float mid = (res - 1) * 0.5f;
            for (int attempt = 0; attempt < 300; attempt++)
            {
                int x = _rng.Next(res), y = _rng.Next(res);
                float v = h[y, x];
                if (v < 0.07f || v > 0.22f || water[y, x]) continue;
                var c = new Vector2Int(x, y);
                if (Radial(c, mid) > mid * 0.65f) continue;
                if (MaxAround(h, res, c, 8) - v > 0.05f) continue; // needs fairly flat ground, not a hillside
                centre = c;
                return true;
            }
            centre = default;
            return false;
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

                if (!landMask[gy, gx] || heightmap[gy, gx] < 0.07f || heightmap[gy, gx] > 0.45f) continue;

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
