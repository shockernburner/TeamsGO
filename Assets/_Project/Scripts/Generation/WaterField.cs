using System.Collections.Generic;
using UnityEngine;
using ProjectFossil.Core;

namespace ProjectFossil.Generation
{
    // Builds the island's IslandWorld: the fine ground the terrain is drawn and walked on, and the water over it,
    // both on the same grid. The generator plans where lakes and streams go on its coarse map; this lays them on
    // the fine grid and shapes the ground round them so they hold together:
    //
    //   - a surface never falls faster than MaxSurfaceGrade between neighbours, so water is never a cliff, a curtain
    //     or a tilted sheet: streams run down slopes as gentle rapids and meet lakes and the sea at their level;
    //   - every dry vertex beside water stands at least Clearance above it, so no edge ever hangs in the air;
    //   - beds deepen away from the shore, and banks slope up from the water instead of standing as walls;
    //   - water that would need the ground cut more than MaxCut, and puddles too small to read as water, are left
    //     out.
    //
    // The drawn water, the shoreline grass, the NavMesh, swimming, wading and the camera all read the result.
    // Pure and deterministic for a seed, like the generator.
    public static class WaterField
    {
        public const float Clearance       = 0.15f; // metres dry ground beside water stands above it
        public const float MaxSurfaceGrade = 0.2f;  // metres a surface may fall per metre
        public const float ShoreDepth      = 0.25f; // metres of water at the shoreline...
        public const float DepthPerMetre   = 0.3f;  // ...deepening this much per metre out...
        public const float LakeDepth       = 2.4f;  // ...down to this in a lake: deep enough to swim, and to hide in
        public const float StreamDepth     = 0.7f;  // ...or this in a stream: everyone wades across
        public const float MaxCut          = 4f;    // metres the ground may be cut down to make a bed
        public const float BankSlope       = 0.55f; // banks rise at most this steeply from the water...
        public const float BankReach       = 14f;   // ...out to this many metres
        public const float LeveeSlope      = 0.5f;  // where the land falls away from water, it does so this gently...
        public const float LeveeReach      = 8f;    // ...for this many metres
        public const int   MinBodyVertices = 24;    // smaller pools are left out
        public const float StreamMargin    = 1.5f;  // metres a stream's surface reaches past its planned channel

        public static IslandWorld Build(IslandData data)
        {
            var s   = data.Settings;
            var h01 = TerrainDetail.Refine(data);
            int n   = h01.GetLength(0);
            float cell = s.worldSize / (n - 1);
            float sea  = s.seaLevel * s.maxHeight;

            var orig = new float[n * n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                    orig[z * n + x] = h01[z, x] * s.maxHeight;
            var ground = (float[])orig.Clone();
            var water  = new float[n * n];
            var depth  = new float[n * n]; // deepest the bed may lie below each surface
            for (int i = 0; i < water.Length; i++) water[i] = float.NaN;
            var isSea = new bool[n * n];
            for (int i = 0; i < isSea.Length; i++) isSea[i] = orig[i] < sea;

            LayLakes(data, water, depth, n, cell);
            LayStreams(data, water, depth, n, cell);
            for (int i = 0; i < water.Length; i++)
                if (!float.IsNaN(water[i]) && (water[i] <= sea + 0.05f || isSea[i])) water[i] = float.NaN; // the sea's

            // Settle the surfaces, then leave out what can't hold together; repeat until nothing more drops out.
            for (int pass = 0; pass < 8; pass++)
            {
                LimitGrade(water, isSea, n, cell, sea);
                bool changed = DropDeepCuts(water, orig);
                changed |= DropPuddles(water, n);
                if (!changed) break;
            }

            // Beds: deeper away from the shore, filled or cut to that shape, so a stream stays wadeable over the deep
            // channel the coarse map carved and a lake has no pits.
            var shore = DistanceToDry(water, n, cell);
            for (int i = 0; i < water.Length; i++)
                if (!float.IsNaN(water[i])) ground[i] = Bed(water[i], shore[i], depth[i], cell);

            ShapeBanks(water, ground, isSea, n, cell);

            // No edge may hang: every dry vertex stands clear of the water beside it. Raising dry ground only ever
            // helps its other neighbours, so one pass settles it.
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int i = z * n + x;
                    if (!float.IsNaN(water[i]) || isSea[i]) continue;
                    float top = HighestWetNeighbour(water, n, x, z);
                    if (!float.IsNegativeInfinity(top)) ground[i] = Mathf.Max(ground[i], top + Clearance);
                }

            for (int i = 0; i < ground.Length; i++) ground[i] = Mathf.Clamp(ground[i], 0f, s.maxHeight);
            return new IslandWorld(n, cell, sea, ground, water, FindRapids(water, n, cell));
        }

        // The terrain heightmap (0–1, [z, x]) for the world's ground.
        public static float[,] TerrainHeights(IslandWorld world, float maxHeight)
        {
            int n = world.Size;
            var h = new float[n, n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                    h[z, x] = Mathf.Clamp01(world.GroundAtVertex(x, z) / maxHeight);
            return h;
        }

        // The height the drawn water surface takes at a vertex: its own surface where it's wet; beside water, the
        // highest neighbouring surface (the ground there stands above it, so that edge tucks under the bank) or the
        // sea's level where it meets the sea. NaN where no water is drawn.
        public static float DrawnSurface(IslandWorld world, int x, int z)
        {
            float own = world.WaterAtVertex(x, z);
            if (!float.IsNaN(own)) return own;
            float top = float.NegativeInfinity;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= world.Size || nz >= world.Size) continue;
                    float w = world.WaterAtVertex(nx, nz);
                    if (!float.IsNaN(w) && w > top) top = w;
                }
            if (float.IsNegativeInfinity(top)) return float.NaN;
            return world.GroundAtVertex(x, z) < world.SeaLevel ? world.SeaLevel : top;
        }

        public static float Bed(float surface, float shoreDistance, float maxDepth, float cell) =>
            surface - Mathf.Min(maxDepth, ShoreDepth + DepthPerMetre * Mathf.Max(0f, shoreDistance - cell * 0.5f));

        public const float RapidsGrade = 0.1f;  // a surface falling this steeply runs fast enough to hear
        private const int  RapidsSpacing = 8;   // vertices between sound spots

        // Spots, a few metres apart, where a stream's surface runs down a slope.
        private static List<Vector3> FindRapids(float[] water, int n, float cell)
        {
            var spots = new List<Vector3>();
            for (int z = 1; z < n - 1; z += RapidsSpacing)
                for (int x = 1; x < n - 1; x += RapidsSpacing)
                {
                    float best = float.NaN; int bx = 0, bz = 0; float grade = 0f;
                    for (int dz = 0; dz < RapidsSpacing && z + dz < n - 1; dz++)
                        for (int dx = 0; dx < RapidsSpacing && x + dx < n - 1; dx++)
                        {
                            int i = (z + dz) * n + x + dx;
                            float w = water[i];
                            if (float.IsNaN(w) || float.IsNaN(water[i + 1]) || float.IsNaN(water[i + n])) continue;
                            float g = Mathf.Max(Mathf.Abs(water[i + 1] - w), Mathf.Abs(water[i + n] - w)) / cell;
                            if (g > grade) { grade = g; best = w; bx = x + dx; bz = z + dz; }
                        }
                    if (grade >= RapidsGrade) spots.Add(new Vector3(bx * cell, best, bz * cell));
                }
            return spots;
        }

        // ── Laying out the planned water ───────────────────────────────────────

        private static void LayLakes(IslandData data, float[] water, float[] depth, int n, float cell)
        {
            int li = 0;
            foreach (var lake in data.Lakes)
            {
                // A wobbly shore, its own for each lake, rather than a drawn circle.
                var rng = new System.Random(data.Seed * 31 + li++);
                float ox = (float)rng.NextDouble() * 100f, oz = (float)rng.NextDouble() * 100f;
                float r = lake.Radius, reach = r * 1.3f;
                Span(lake.Center.x, lake.Center.z, reach, n, cell, out int x0, out int x1, out int z0, out int z1);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = x * cell - lake.Center.x, dz = z * cell - lake.Center.z;
                        float d = Mathf.Sqrt(dx * dx + dz * dz);
                        if (d > reach) continue;
                        float a = Mathf.Atan2(dz, dx);
                        float wobble = Mathf.PerlinNoise(Mathf.Cos(a) * 1.3f + ox, Mathf.Sin(a) * 1.3f + oz) - 0.5f;
                        if (d > r * (1f + 0.45f * wobble)) continue;
                        Lower(water, depth, z * n + x, lake.Center.y, LakeDepth);
                    }
            }
        }

        private static void LayStreams(IslandData data, float[] water, float[] depth, int n, float cell)
        {
            foreach (var river in data.Rivers)
            {
                var pts = river.Points;
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    Vector3 a = pts[i], b = pts[i + 1];
                    float wa = river.HalfWidths[i] + StreamMargin, wb = river.HalfWidths[i + 1] + StreamMargin;
                    float reach = Mathf.Max(wa, wb);
                    float minX = Mathf.Min(a.x, b.x) - reach, maxX = Mathf.Max(a.x, b.x) + reach;
                    float minZ = Mathf.Min(a.z, b.z) - reach, maxZ = Mathf.Max(a.z, b.z) + reach;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt(minX / cell)), x1 = Mathf.Min(n - 1, Mathf.CeilToInt(maxX / cell));
                    int z0 = Mathf.Max(0, Mathf.FloorToInt(minZ / cell)), z1 = Mathf.Min(n - 1, Mathf.CeilToInt(maxZ / cell));
                    float abx = b.x - a.x, abz = b.z - a.z;
                    float len2 = Mathf.Max(1e-6f, abx * abx + abz * abz);
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            float px = x * cell - a.x, pz = z * cell - a.z;
                            float t  = Mathf.Clamp01((px * abx + pz * abz) / len2);
                            float ex = px - abx * t, ez = pz - abz * t;
                            float hw = Mathf.Lerp(wa, wb, t);
                            if (ex * ex + ez * ez > hw * hw) continue;
                            Lower(water, depth, z * n + x, Mathf.Lerp(a.y, b.y, t), StreamDepth);
                        }
                }
            }
        }

        // Water finds the lowest level that reaches a spot; where a stream runs into a lake, the lake's depth wins.
        private static void Lower(float[] water, float[] depth, int i, float surface, float maxDepth)
        {
            if (float.IsNaN(water[i]) || surface < water[i]) water[i] = surface;
            depth[i] = Mathf.Max(depth[i], maxDepth);
        }

        private static void Span(float cx, float cz, float r, int n, float cell, out int x0, out int x1, out int z0, out int z1)
        {
            x0 = Mathf.Max(0, Mathf.FloorToInt((cx - r) / cell)); x1 = Mathf.Min(n - 1, Mathf.CeilToInt((cx + r) / cell));
            z0 = Mathf.Max(0, Mathf.FloorToInt((cz - r) / cell)); z1 = Mathf.Min(n - 1, Mathf.CeilToInt((cz + r) / cell));
        }

        // ── Holding together ───────────────────────────────────────────────────

        private static readonly int[]   Dx   = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[]   Dz   = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private static readonly float[] Step = { 1f, 1f, 1f, 1f, 1.41421356f, 1.41421356f, 1.41421356f, 1.41421356f };

        // Lowers surfaces until none stands more than MaxSurfaceGrade per metre above a wet or sea neighbour.
        // Lowest first, so each surface is settled once.
        private static void LimitGrade(float[] water, bool[] isSea, int n, float cell, float sea)
        {
            var heap = new MinHeap();
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int i = z * n + x;
                    if (!float.IsNaN(water[i])) { heap.Push(water[i], i); continue; }
                    if (!isSea[i]) continue;
                    // The sea counts where it touches inland water: streams run down to meet it.
                    for (int k = 0; k < 8; k++)
                    {
                        int nx = x + Dx[k], nz = z + Dz[k];
                        if (nx < 0 || nz < 0 || nx >= n || nz >= n || float.IsNaN(water[nz * n + nx])) continue;
                        heap.Push(sea, -1 - i);
                        break;
                    }
                }
            while (heap.Count > 0)
            {
                heap.Pop(out float key, out int code);
                int i = code < 0 ? -1 - code : code;
                if (code >= 0 && key > water[i]) continue; // stale
                int x = i % n, z = i / n;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    int j = nz * n + nx;
                    if (float.IsNaN(water[j])) continue;
                    float cap = key + MaxSurfaceGrade * Step[k] * cell;
                    if (cap < water[j]) { water[j] = cap; heap.Push(cap, j); }
                }
            }
        }

        // Leaves out water whose bed would have to be cut deep into the ground: that's a slot through a hill.
        private static bool DropDeepCuts(float[] water, float[] orig)
        {
            bool changed = false;
            for (int i = 0; i < water.Length; i++)
            {
                if (float.IsNaN(water[i])) continue;
                // Only the cut below the shoreline counts; the bed's own depth is fine.
                if (orig[i] - water[i] > MaxCut) { water[i] = float.NaN; changed = true; }
            }
            return changed;
        }

        // Leaves out bodies of water too small to read as anything but a glitch.
        private static bool DropPuddles(float[] water, int n)
        {
            var seen = new bool[water.Length];
            var stack = new Stack<int>();
            var body = new List<int>();
            bool changed = false;
            for (int start = 0; start < water.Length; start++)
            {
                if (seen[start] || float.IsNaN(water[start])) continue;
                body.Clear();
                stack.Push(start); seen[start] = true;
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    body.Add(i);
                    int x = i % n, z = i / n;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + Dx[k], nz = z + Dz[k];
                        if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                        int j = nz * n + nx;
                        if (seen[j] || float.IsNaN(water[j])) continue;
                        seen[j] = true; stack.Push(j);
                    }
                }
                if (body.Count >= MinBodyVertices) continue;
                foreach (int i in body) water[i] = float.NaN;
                changed = true;
            }
            return changed;
        }

        // Metres from each wet vertex to the nearest dry one (0 on dry ground).
        private static float[] DistanceToDry(float[] water, int n, float cell)
        {
            var dist = new float[water.Length];
            var heap = new MinHeap();
            for (int i = 0; i < water.Length; i++)
            {
                if (!float.IsNaN(water[i])) { dist[i] = float.MaxValue; continue; }
                dist[i] = 0f;
                // Only the shoreline seeds the search; dry ground further off never reaches the water first.
                int x = i % n, z = i / n;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n || float.IsNaN(water[nz * n + nx])) continue;
                    heap.Push(0f, i);
                    break;
                }
            }
            while (heap.Count > 0)
            {
                heap.Pop(out float d, out int i);
                if (d > dist[i]) continue;
                int x = i % n, z = i / n;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    int j = nz * n + nx;
                    float nd = d + Step[k] * cell;
                    if (nd < dist[j]) { dist[j] = nd; heap.Push(nd, j); }
                }
            }
            return dist;
        }

        // Shapes the dry ground near water: no higher than a bank rising gently from it (so no walls of earth over a
        // stream), and no lower than a gentle levee falling away from it (so no water perched on a drop).
        private static void ShapeBanks(float[] water, float[] ground, bool[] isSea, int n, float cell)
        {
            var dist = new float[water.Length];
            var near = new float[water.Length]; // surface of the nearest water
            var heap = new MinHeap();
            for (int i = 0; i < water.Length; i++)
            {
                dist[i] = float.MaxValue;
                if (float.IsNaN(water[i])) continue;
                dist[i] = 0f; near[i] = water[i];
                heap.Push(0f, i);
            }
            float reach = Mathf.Max(BankReach, LeveeReach);
            while (heap.Count > 0)
            {
                heap.Pop(out float d, out int i);
                if (d > dist[i]) continue;
                int x = i % n, z = i / n;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    int j = nz * n + nx;
                    float nd = d + Step[k] * cell;
                    if (nd > reach || nd >= dist[j]) continue;
                    dist[j] = nd; near[j] = near[i];
                    heap.Push(nd, j);
                }
            }
            for (int i = 0; i < water.Length; i++)
            {
                if (!float.IsNaN(water[i]) || isSea[i] || dist[i] == float.MaxValue) continue;
                float out1 = Mathf.Max(0f, dist[i] - cell);
                if (dist[i] <= BankReach)
                    ground[i] = Mathf.Min(ground[i], near[i] + Clearance + BankSlope * out1);
                if (dist[i] <= LeveeReach)
                    ground[i] = Mathf.Max(ground[i], near[i] + Clearance - LeveeSlope * out1);
            }
        }

        private static float HighestWetNeighbour(float[] water, int n, int x, int z)
        {
            float top = float.NegativeInfinity;
            for (int k = 0; k < 8; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                float w = water[nz * n + nx];
                if (!float.IsNaN(w) && w > top) top = w;
            }
            return top;
        }

        // A binary min-heap of (key, vertex).
        private sealed class MinHeap
        {
            private readonly List<float> _keys = new List<float>();
            private readonly List<int>   _vals = new List<int>();
            public int Count => _keys.Count;

            public void Push(float key, int val)
            {
                _keys.Add(key); _vals.Add(val);
                int i = _keys.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (_keys[p] <= _keys[i]) break;
                    Swap(i, p); i = p;
                }
            }

            public void Pop(out float key, out int val)
            {
                key = _keys[0]; val = _vals[0];
                int last = _keys.Count - 1;
                _keys[0] = _keys[last]; _vals[0] = _vals[last];
                _keys.RemoveAt(last); _vals.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < _keys.Count && _keys[l] < _keys[m]) m = l;
                    if (r < _keys.Count && _keys[r] < _keys[m]) m = r;
                    if (m == i) break;
                    Swap(i, m); i = m;
                }
            }

            private void Swap(int a, int b)
            {
                (_keys[a], _keys[b]) = (_keys[b], _keys[a]);
                (_vals[a], _vals[b]) = (_vals[b], _vals[a]);
            }
        }
    }
}
