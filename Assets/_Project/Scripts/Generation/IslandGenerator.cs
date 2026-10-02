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
            var filled  = FillHollows(heightmap, res);
            var riverCells = new HashSet<int>();
            var raised  = new float[res, res]; // banks built up beside the water, so it never hangs over lower ground
            for (int r = 0; r < s.riverCount; r++)
            {
                if (!TryPickRiverSource(heightmap, res, sources, out var src)) break;
                sources.Add(src);
                var path = TraceRiver(filled, res, src);
                // Draining the same ground, rivers meet: one ends where it joins another, instead of running on
                // down the same channel as a second ribbon of water.
                for (int i = 0; i < path.Count; i++)
                    if (NearRiver(riverCells, res, path[i])) { path.RemoveRange(i, path.Count - i); break; }
                foreach (var c in path) riverCells.Add(c.y * res + c.x);
                if (path.Count < 12) continue;

                // The water surface only ever falls. Where the ground runs gently it falls with it. Down a slope a
                // tilted ribbon of water looked like a blue tarp, so there the stream is a run of level pools, each
                // cut a little into the slope, with a small fall down to the next.
                var surfaces = new float[path.Count];
                var slopes   = new float[path.Count];
                float surface = heightmap[src.y, src.x] - margin;
                float step = StreamStep / s.maxHeight;
                for (int i = 0; i < path.Count; i++)
                {
                    var p = path[i];
                    float ground = heightmap[p.y, p.x];
                    slopes[i] = GroundGradient(heightmap, path, i, cell) * s.maxHeight;
                    if (slopes[i] <= s.maxRiverGradient) surface = Mathf.Min(surface, ground - margin);
                    else if (ground - margin < surface) surface = ground - margin - step; // over the lip: a fall
                    surface = Mathf.Max(s.seaLevel, surface);
                    surfaces[i] = surface;
                }

                // No water where the slope is a cliff, nor where the course climbs out of a hollow: there the
                // channel would be a dry trench with water floating in it.
                int start = -1;
                for (int i = 0; i <= path.Count; i++)
                {
                    bool wet = i < path.Count && slopes[i] <= MaxStreamGradient
                            && (heightmap[path[i].y, path[i].x] - surfaces[i]) * s.maxHeight <= MaxRiverCut;
                    if (wet) { if (start < 0) start = i; continue; }
                    if (start >= 0 && i - start >= 12)
                    {
                        var river = new RiverPath();
                        for (int k = start; k < i; k++)
                        {
                            var p = path[k];
                            float t = (float)k / (path.Count - 1);
                            // Streams on the slopes are narrow; rivers widen on the flats towards the sea.
                            float narrow = Mathf.InverseLerp(s.maxRiverGradient, 0.5f, slopes[k]);
                            float halfWidth = s.riverWidth * Mathf.Lerp(0.35f, 0.7f, t) * Mathf.Lerp(1f, 0.35f, narrow);
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
                if (!TryPickLake(heightmap, water, res, cell, out var centre)) break;
                float radius = Mathf.Lerp(s.lakeRadius.x, s.lakeRadius.y, _rng.NextFloat());
                float surface = Mathf.Max(s.seaLevel, MinAround(heightmap, res, centre, Mathf.CeilToInt(radius / cell)) - margin);
                Stamp(carved, wetness, water, res, cell, centre, radius, surface - depth, MaxAround(heightmap, res, centre, 12));
                // A rim like a river's banks: on the downhill side of a hollow the ground fell away below the
                // surface, and the lake hung there in the air.
                Bank(raised, res, cell, centre, radius + (depth + margin) * s.maxHeight / Mathf.Max(0.05f, s.riverBankSlope) + 2f,
                     surface + margin);
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

            KeepContainedWater(heightmap, water, res, rivers, lakes);
        }

        // Water must never hang over lower ground. A river's surface steps down along its course, and where one
        // river's or a lake's hollow cut into another's bank, the edge of the drawn surface stood over lower ground
        // as a flat sheet in the air. So a low rim is raised round every surface, then any surface whose edge still
        // hangs is lowered until its edges sit under the ground or just over other water; a stretch that would have
        // to drop too far stays dry, and so does a lake. Dropping water uncovers edges that lay over it, so this
        // repeats until nothing changes. The water mask is then rebuilt from what
        // remains, so the map and everything else agree with what is drawn.
        private void KeepContainedWater(float[,] heightmap, bool[,] water, int res, List<RiverPath> rivers, List<Lake> lakes)
        {
            float cell = _settings.worldSize / (res - 1);
            RaiseRims(heightmap, res, cell, rivers, lakes);
            bool settled = false;
            for (int pass = 0; pass < 40 && !settled; pass++)
                settled = !ContainWaterOnce(heightmap, res, cell, rivers, lakes);
            if (!settled) DropHangingWater(heightmap, res, cell, rivers, lakes);

            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                    water[y, x] = false;
            foreach (var river in rivers)
                for (int i = 0; i < river.Points.Count; i++)
                    MarkWater(water, res, cell, river.Points[i], river.HalfWidths[i]);
            foreach (var lake in lakes)
                MarkWater(water, res, cell, lake.Center, lake.Radius);
        }

        private const int MinRiverRun = 8;
        private const float MaxRiverCut = 5f; // metres the ground over a river's course may stand above its surface
        private const float StreamStep = 2f;  // metres a stream's pool is cut into the slope below the last one's lip
        private const float MaxStreamGradient = 0.7f; // steeper than this is a cliff: no water
        private const float MinWaterDepth = 0.7f; // metres of water left over the bed after lowering a surface
        // Metres the ground under a surface's edge stands above it. The fine terrain never smooths ground near
        // water below this grid's, so the edge stays tucked under on the drawn ground too.
        public const float BankClearance = 0.1f;
        // An edge may lie over other water up to this far below it: a small step, like a riffle where streams meet.
        public const float WaterStep = 0.3f;
        public const float CoverInset = 0.5f;

        // Banks are built before the channels are cut, and one water's hollow can cut into another's bank; a
        // river's ends and smoothed bends also reach past the banks built for its grid steps. So once all water is
        // in place, every surface gets a low rim of ground just past its drawn edge, easing down outside it. Cells
        // in or beside any water's channel are left alone, so no channel is filled.
        private void RaiseRims(float[,] heightmap, int res, float cell, List<RiverPath> rivers, List<Lake> lakes)
        {
            var s = _settings;
            float bankRun = (s.waterDepth + RimHeight) / Mathf.Max(0.05f, s.riverBankSlope); // channel edge to bank top
            var channel = new bool[res, res];
            foreach (var river in rivers)
                for (int i = 0; i < river.Points.Count; i++)
                    MarkWater(channel, res, cell, river.Points[i], river.HalfWidths[i] + bankRun);
            foreach (var lake in lakes)
                MarkWater(channel, res, cell, lake.Center, lake.Radius + bankRun);

            var rim = new float[res, res];
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                    rim[y, x] = float.MinValue;
            foreach (var river in rivers)
                for (int i = 0; i < river.Points.Count; i++)
                    Rim(rim, res, cell, river.Points[i], s.RiverSurfaceHalfWidth(river.HalfWidths[i]));
            foreach (var lake in lakes)
                Rim(rim, res, cell, lake.Center, s.LakeSurfaceRadius(lake.Radius));

            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                    if (!channel[y, x] && rim[y, x] > heightmap[y, x]) heightmap[y, x] = rim[y, x];
        }

        private const float RimHeight = 0.4f; // metres a rim stands above its water

        private void Rim(float[,] rim, int res, float cell, Vector3 centre, float edge)
        {
            var s = _settings;
            float top = (centre.y + RimHeight) / s.maxHeight, fall = s.riverBankSlope / s.maxHeight;
            float flat = edge + cell; // level out one cell past the edge, so the edge's corners all stand on it
            float reach = flat + RimHeight * 4f / Mathf.Max(0.05f, s.riverBankSlope);
            int cx = Mathf.RoundToInt(centre.x / cell), cz = Mathf.RoundToInt(centre.z / cell), r = Mathf.CeilToInt(reach / cell);
            for (int z = Mathf.Max(0, cz - r); z <= Mathf.Min(res - 1, cz + r); z++)
                for (int x = Mathf.Max(0, cx - r); x <= Mathf.Min(res - 1, cx + r); x++)
                {
                    float dx = x * cell - centre.x, dz = z * cell - centre.z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d > reach) continue;
                    float target = top - Mathf.Max(0f, d - flat) * fall;
                    if (target > rim[z, x]) rim[z, x] = target;
                }
        }

        private bool ContainWaterOnce(float[,] heightmap, int res, float cell, List<RiverPath> rivers, List<Lake> lakes)
        {
            var s = _settings;
            float maxDrop = s.waterDepth - MinWaterDepth;
            var oldRivers = rivers.ToArray();
            var oldLakes  = lakes.ToArray();
            var rim = new List<Vector3>();
            bool changed = false;

            var kept = new List<RiverPath>();
            foreach (var river in oldRivers)
            {
                int n = river.Points.Count;
                RiverPath part = null;
                float y = float.MaxValue;
                for (int i = 0; i <= n; i++)
                {
                    bool ok = false;
                    if (i < n)
                    {
                        float original = river.Points[i].y;
                        WaterShape.RiverRim(s, river, i, rim);
                        y = Mathf.Min(y, Mathf.Min(original, Ceiling(heightmap, res, cell, rim, original, oldRivers, oldLakes)));
                        ok = original - y <= maxDrop;
                        // Lowering one point can leave a steep step from the one before it, drawn as a tilted
                        // sheet of water: the river breaks there instead, each side with its own rounded end.
                        if (ok && part != null && part.Points.Count > 0)
                        {
                            var prev = part.Points[part.Points.Count - 1];
                            var pi = river.Points[i];
                            float run = Mathf.Sqrt((pi.x - prev.x) * (pi.x - prev.x) + (pi.z - prev.z) * (pi.z - prev.z));
                            float wasDrop = river.Points[i - 1].y - original; // a stream's own falls stay
                            if (prev.y - y > Mathf.Max(wasDrop, 0f) + s.maxRiverGradient * Mathf.Max(run, 0.5f) + 1e-3f)
                            {
                                if (part.Points.Count >= MinRiverRun) kept.Add(part);
                                part = null;
                                changed = true;
                            }
                        }
                        if (original - y > 1e-4f) changed = true;
                    }
                    if (ok)
                    {
                        part ??= new RiverPath();
                        var p = river.Points[i];
                        part.Points.Add(new Vector3(p.x, y, p.z));
                        part.HalfWidths.Add(river.HalfWidths[i]);
                        continue;
                    }
                    if (i < n) changed = true;
                    if (part != null && part.Points.Count >= MinRiverRun) kept.Add(part);
                    else if (part != null) changed = true;
                    part = null;
                    y = float.MaxValue;
                }
            }
            rivers.Clear();
            rivers.AddRange(kept);

            for (int l = lakes.Count - 1; l >= 0; l--)
            {
                var lake = lakes[l];
                WaterShape.LakeRim(s, lake, rim);
                float surface = Mathf.Min(lake.Center.y, Ceiling(heightmap, res, cell, rim, lake.Center.y, oldRivers, oldLakes));
                if (lake.Center.y - surface > maxDrop) { lakes.RemoveAt(l); changed = true; continue; }
                if (lake.Center.y - surface > 1e-4f) changed = true;
                lake.Center.y = surface;
                lakes[l] = lake;
            }
            return changed;
        }

        // Rarely the passes above keep shifting; then any water still hanging is simply left out.
        private void DropHangingWater(float[,] h, int res, float cell, List<RiverPath> rivers, List<Lake> lakes)
        {
            var rim = new List<Vector3>();
            bool hangs;
            do
            {
                hangs = false;
                for (int r = rivers.Count - 1; r >= 0 && !hangs; r--)
                    for (int i = 0; i < rivers[r].Points.Count; i++)
                    {
                        WaterShape.RiverRim(_settings, rivers[r], i, rim);
                        float y = rivers[r].Points[i].y;
                        if (Ceiling(h, res, cell, rim, y, rivers, lakes) < y - 1e-4f) { rivers.RemoveAt(r); hangs = true; break; }
                    }
                for (int l = lakes.Count - 1; l >= 0 && !hangs; l--)
                {
                    WaterShape.LakeRim(_settings, lakes[l], rim);
                    float y = lakes[l].Center.y;
                    if (Ceiling(h, res, cell, rim, y, rivers, lakes) < y - 1e-4f) { lakes.RemoveAt(l); hangs = true; }
                }
            } while (hangs);
        }

        // Highest a surface at height y may stand so that none of its rim points hang: each must be under ground
        // or over other water no more than a small step down.
        private float Ceiling(float[,] h, int res, float cell, List<Vector3> rim, float y,
                              IReadOnlyList<RiverPath> rivers, IReadOnlyList<Lake> lakes)
        {
            float ceiling = float.MaxValue;
            foreach (var q in rim)
            {
                float ground = GroundAt(h, res, cell, q);
                if (WaterShape.EdgeTucked(_settings, ground, y, BankClearance, WaterStep)) continue;
                float cover = WaterShape.SurfaceOver(_settings, rivers, lakes, q, CoverInset);
                if (cover >= y - WaterStep) continue;
                // Low enough to sit inside the bank, or to step down onto the water there.
                ceiling = Mathf.Min(ceiling, Mathf.Max(ground - BankClearance, cover + WaterStep));
            }
            return ceiling;
        }

        // Ground height in metres at a world point, between grid points.
        private float GroundAt(float[,] h, int res, float cell, Vector3 p)
        {
            float gx = Mathf.Clamp(p.x / cell, 0f, res - 1.001f), gz = Mathf.Clamp(p.z / cell, 0f, res - 1.001f);
            int x0 = (int)gx, z0 = (int)gz;
            float tx = gx - x0, tz = gz - z0;
            float v = Mathf.Lerp(Mathf.Lerp(h[z0, x0], h[z0, x0 + 1], tx), Mathf.Lerp(h[z0 + 1, x0], h[z0 + 1, x0 + 1], tx), tz);
            return v * _settings.maxHeight;
        }

        private static void MarkWater(bool[,] water, int res, float cell, Vector3 centre, float radius)
        {
            int cx = Mathf.RoundToInt(centre.x / cell), cz = Mathf.RoundToInt(centre.z / cell);
            int r = Mathf.CeilToInt(radius / cell);
            for (int z = Mathf.Max(0, cz - r); z <= Mathf.Min(res - 1, cz + r); z++)
                for (int x = Mathf.Max(0, cx - r); x <= Mathf.Min(res - 1, cx + r); x++)
                {
                    float dx = (x - centre.x / cell) * cell, dz = (z - centre.z / cell) * cell;
                    if (dx * dx + dz * dz <= radius * radius) water[z, x] = true;
                }
        }

        // Fall of the ground (normalized height) per metre around point i of a path, over a few cells.
        private static float GroundGradient(float[,] h, List<Vector2Int> path, int i, float cell)
        {
            int a = Mathf.Max(0, i - 2), b = Mathf.Min(path.Count - 1, i + 2);
            if (a == b) return 0f;
            float dist = 0f;
            for (int k = a + 1; k <= b; k++)
            {
                int dx = path[k].x - path[k - 1].x, dy = path[k].y - path[k - 1].y;
                dist += Mathf.Sqrt(dx * dx + dy * dy) * cell;
            }
            return Mathf.Max(0f, h[path[a].y, path[a].x] - h[path[b].y, path[b].x]) / Mathf.Max(dist, 1e-3f);
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

        // Rivers follow the way water would really drain: downhill over the ground with every hollow filled to
        // its brim, so a river crosses a hollow and leaves by its lowest edge, all the way to the sea. Walking the
        // bare ground, rivers were trapped in hollows and coiled round in a few metres, a knot of water ribbons.
        // The water itself still follows the real ground: in a hollow it drops to the floor, and where it would
        // run in a trench up to the brim, that stretch stays dry (see CarveWater).
        private List<Vector2Int> TraceRiver(float[,] filled, int res, Vector2Int start)
        {
            var path = new List<Vector2Int> { start };
            var cur  = start;
            for (int step = 0; step < res * 4; step++)
            {
                if (filled[cur.y, cur.x] < _settings.seaLevel) break; // reached the sea
                Vector2Int best = cur;
                float lowest = filled[cur.y, cur.x];
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cur.x + dx, y = cur.y + dy;
                        if ((dx == 0 && dy == 0) || x < 0 || y < 0 || x >= res || y >= res) continue;
                        if (filled[y, x] < lowest) { lowest = filled[y, x]; best = new Vector2Int(x, y); }
                    }
                if (best.x == cur.x && best.y == cur.y) break; // the island's edge
                path.Add(best);
                cur = best;
            }
            return path;
        }

        // The ground with every hollow filled just past level, so each cell drains to a lower neighbour and on
        // to the sea (priority flood, inward from the sea and the map's edge).
        private float[,] FillHollows(float[,] h, int res)
        {
            const float rise = 1e-6f; // a fall too small to see, so filled hollows still drain one way
            var filled = new float[res, res];
            var done   = new bool[res, res];
            var open   = new CellHeap();
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                    if (h[y, x] < _settings.seaLevel || x == 0 || y == 0 || x == res - 1 || y == res - 1)
                    {
                        filled[y, x] = h[y, x];
                        done[y, x] = true;
                        open.Push(h[y, x], y * res + x);
                    }
            while (open.Count > 0)
            {
                open.Pop(out float level, out int at);
                int cx = at % res, cy = at / res;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= res || y >= res || done[y, x]) continue;
                        done[y, x] = true;
                        filled[y, x] = Mathf.Max(h[y, x], level + rise);
                        open.Push(filled[y, x], y * res + x);
                    }
            }
            return filled;
        }

        private static bool NearRiver(HashSet<int> cells, int res, Vector2Int p)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = p.x + dx, y = p.y + dy;
                    if (x >= 0 && y >= 0 && x < res && y < res && cells.Contains(y * res + x)) return true;
                }
            return false;
        }

        // A min-heap of grid cells by height.
        private class CellHeap
        {
            private readonly List<float> _keys  = new List<float>();
            private readonly List<int>   _cells = new List<int>();
            public int Count => _keys.Count;

            public void Push(float key, int cell)
            {
                _keys.Add(key); _cells.Add(cell);
                int i = _keys.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (_keys[parent] <= _keys[i]) break;
                    Swap(i, parent); i = parent;
                }
            }

            public void Pop(out float key, out int cell)
            {
                key = _keys[0]; cell = _cells[0];
                int last = _keys.Count - 1;
                _keys[0] = _keys[last]; _cells[0] = _cells[last];
                _keys.RemoveAt(last); _cells.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < _keys.Count && _keys[l] < _keys[m]) m = l;
                    if (r < _keys.Count && _keys[r] < _keys[m]) m = r;
                    if (m == i) break;
                    Swap(i, m); i = m;
                }
            }

            private void Swap(int a, int b)
            {
                (_keys[a], _keys[b]) = (_keys[b], _keys[a]);
                (_cells[a], _cells[b]) = (_cells[b], _cells[a]);
            }
        }

        private static float Radial(Vector2Int p, float centre)
        {
            float dx = p.x - centre, dy = p.y - centre;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        // Lakes sit in low, inland ground away from rivers.
        private bool TryPickLake(float[,] h, bool[,] water, int res, float cell, out Vector2Int centre)
        {
            float mid = (res - 1) * 0.5f;
            // Keep clear of rivers: a lake's hollow cut into a river's bank left the river hanging over it.
            var s = _settings;
            int clear = Mathf.CeilToInt((s.lakeRadius.y + 2f * (s.waterDepth + 0.4f) / Mathf.Max(0.05f, s.riverBankSlope) + s.riverWidth) / cell);
            for (int attempt = 0; attempt < 300; attempt++)
            {
                int x = _rng.Next(res), y = _rng.Next(res);
                float v = h[y, x];
                if (v < 0.07f || v > 0.22f || water[y, x]) continue;
                var c = new Vector2Int(x, y);
                if (Radial(c, mid) > mid * 0.65f) continue;
                if (MaxAround(h, res, c, 8) - v > 0.05f) continue; // needs fairly flat ground, not a hillside
                if (AnyWaterAround(water, res, c, clear)) continue;
                centre = c;
                return true;
            }
            centre = default;
            return false;
        }

        private static bool AnyWaterAround(bool[,] water, int res, Vector2Int c, int r)
        {
            for (int y = Mathf.Max(0, c.y - r); y <= Mathf.Min(res - 1, c.y + r); y++)
                for (int x = Mathf.Max(0, c.x - r); x <= Mathf.Min(res - 1, c.x + r); x++)
                    if (water[y, x] && (x - c.x) * (x - c.x) + (y - c.y) * (y - c.y) <= r * r) return true;
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
