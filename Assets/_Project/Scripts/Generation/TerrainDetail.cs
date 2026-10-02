using UnityEngine;

namespace ProjectFossil.Generation
{
    // The generator's rules run on a coarse grid (about 4 m a cell on the default island). Drawn as is, the terrain
    // is all flat 4 m facets: hills look chiselled and their texture smeared. This builds the heightmap the terrain
    // is drawn and walked on: `terrainDetail` times finer, smoothed so slopes curve, with small bumps on open land.
    // Pure and deterministic for a seed, like the generator.
    public static class TerrainDetail
    {
        // Smoothing may lower open ground by at most this much. Near water it may not lower it at all, so the banks
        // the generator built above each surface's edge stay there.
        private const float MaxLowering = 0.2f; // metres

        public static float[,] Refine(IslandData data)
        {
            var s   = data.Settings;
            int k   = Mathf.Clamp(s.terrainDetail, 1, 4);
            int res = data.Resolution;
            if (k <= 1) return data.Heightmap;

            int fine  = (res - 1) * k + 1;
            var src   = data.Heightmap;
            // Bumps only well away from water: near it they could dip a bank below the surface beside it.
            var mask  = Erode(data.LandMask, res, BumpClearCells);
            var flat  = new float[fine, fine]; // straight interpolation of the coarse grid
            var land  = new float[fine, fine]; // 1 well away from water, 0 on or near it, blended between
            for (int y = 0; y < fine; y++)
            {
                float gy = (float)y / k;
                int   y0 = Mathf.Min((int)gy, res - 2);
                float ty = gy - y0;
                for (int x = 0; x < fine; x++)
                {
                    float gx = (float)x / k;
                    int   x0 = Mathf.Min((int)gx, res - 2);
                    float tx = gx - x0;
                    flat[y, x] = Lerp2(src[y0, x0], src[y0, x0 + 1], src[y0 + 1, x0], src[y0 + 1, x0 + 1], tx, ty);
                    land[y, x] = Lerp2(mask[y0, x0] ? 1f : 0f, mask[y0, x0 + 1] ? 1f : 0f,
                                       mask[y0 + 1, x0] ? 1f : 0f, mask[y0 + 1, x0 + 1] ? 1f : 0f, tx, ty);
                }
            }

            // Round off the facets: a box filter about one coarse cell wide, twice (close to a soft bell).
            var h = (float[,])flat.Clone();
            int r = Mathf.Max(1, k / 2);
            var tmp = new float[fine, fine];
            for (int pass = 0; pass < 2; pass++)
            {
                BoxPass(h, tmp, fine, r, true);
                BoxPass(tmp, h, fine, r, false);
            }

            float floor = MaxLowering / s.maxHeight;
            float amp   = s.terrainBumps / s.maxHeight;
            float cellM = s.worldSize / (fine - 1);
            var rng = new System.Random(data.Seed ^ 0x5EED);
            float ox = (float)rng.NextDouble() * 1000f, oz = (float)rng.NextDouble() * 1000f;
            for (int y = 0; y < fine; y++)
            {
                for (int x = 0; x < fine; x++)
                {
                    float v = Mathf.Max(h[y, x], flat[y, x] - floor * land[y, x]);
                    if (amp > 0f)
                    {
                        float w  = land[y, x] * land[y, x];
                        float wx = x * cellM, wz = y * cellM;
                        float n  = (Mathf.PerlinNoise(wx * 0.11f + ox, wz * 0.11f + oz) - 0.5f)
                                 + (Mathf.PerlinNoise(wx * 0.43f + oz, wz * 0.43f + ox) - 0.5f) * 0.5f;
                        v += n * amp * w;
                    }
                    h[y, x] = Mathf.Clamp01(v);
                }
            }
            return h;
        }

        private const int BumpClearCells = 3; // coarse cells (about 12 m on the default island)

        // Land only where everything within r cells is land.
        private static bool[,] Erode(bool[,] land, int res, int r)
        {
            var rows = new bool[res, res];
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    bool all = true;
                    for (int o = -r; o <= r && all; o++) all = land[y, Mathf.Clamp(x + o, 0, res - 1)];
                    rows[y, x] = all;
                }
            var result = new bool[res, res];
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    bool all = true;
                    for (int o = -r; o <= r && all; o++) all = rows[Mathf.Clamp(y + o, 0, res - 1), x];
                    result[y, x] = all;
                }
            return result;
        }

        private static float Lerp2(float a, float b, float c, float d, float tx, float ty) =>
            Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);

        // One direction of a box blur of radius r, clamped at the edges.
        private static void BoxPass(float[,] from, float[,] to, int n, int r, bool alongX)
        {
            float inv = 1f / (2 * r + 1);
            for (int a = 0; a < n; a++)
            {
                for (int b = 0; b < n; b++)
                {
                    float sum = 0f;
                    for (int o = -r; o <= r; o++)
                    {
                        int i = Mathf.Clamp(b + o, 0, n - 1);
                        sum += alongX ? from[a, i] : from[i, a];
                    }
                    if (alongX) to[a, b] = sum * inv; else to[b, a] = sum * inv;
                }
            }
        }
    }
}
