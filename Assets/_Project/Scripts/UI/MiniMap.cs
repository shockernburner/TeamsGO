using System.Collections.Generic;
using UnityEngine;
using ProjectFossil.Core;
using ProjectFossil.Economy;
using ProjectFossil.Generation;
using ProjectFossil.Match;

namespace ProjectFossil.UI
{
    // The small map in the bottom-right corner: sea and inland water in blue, open ground in sand and rock, forest
    // in greens that darken with how thick it is, shaded by the slopes. On top: the player as an arrow, unsearched
    // supply caches as yellow dots, and once extraction opens the beacons blinking green.
    //
    // The picture is drawn once per island from the generator's data and the trees the decorator placed.
    public class MiniMap
    {
        public float size   = 200f;  // pixels on screen
        public float margin = 14f;

        private static readonly Color Sea      = new Color(0.10f, 0.22f, 0.36f);
        private static readonly Color Inland   = new Color(0.20f, 0.42f, 0.60f);
        private static readonly Color Bare     = new Color(0.62f, 0.56f, 0.42f);
        private static readonly Color Rock     = new Color(0.45f, 0.43f, 0.40f);
        private static readonly Color LightWood = new Color(0.52f, 0.68f, 0.34f);
        private static readonly Color DeepWood = new Color(0.06f, 0.26f, 0.10f);
        private const float TreesForDeepWood = 7f; // trees within ForestRadius that count as the thickest forest
        private const float ForestRadius     = 12f; // metres

        private Texture2D _map, _dot, _arrow;
        private int _seed = int.MinValue;
        private readonly List<LootContainer> _caches = new List<LootContainer>();
        private float _nextCacheScan;

        public void Draw(IslandData data, MatchManager match, Transform player, Transform view)
        {
            var terrain = Terrain.activeTerrain;
            if (data == null || terrain == null || player == null) return;
            if (_map == null || _seed != data.Seed) { Build(data, terrain); _seed = data.Seed; }

            Vector3 origin = terrain.transform.position, extent = terrain.terrainData.size;
            var box = new Rect(Screen.width - size - margin, Screen.height - size - margin - 26f, size, size);

            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(box.x - 3f, box.y - 3f, box.width + 6f, box.height + 6f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.92f);
            GUI.DrawTexture(box, _map);

            Vector2 ToMap(Vector3 world)
            {
                float u = Mathf.Clamp01((world.x - origin.x) / extent.x), v = Mathf.Clamp01((world.z - origin.z) / extent.z);
                return new Vector2(box.x + u * box.width, box.y + (1f - v) * box.height); // north up
            }

            float t = Time.unscaledTime;

            // Supply caches still holding something: a soft yellow pulse.
            if (t >= _nextCacheScan)
            {
                _nextCacheScan = t + 1f;
                _caches.Clear();
                _caches.AddRange(Object.FindObjectsByType<LootContainer>(FindObjectsSortMode.None));
            }
            GUI.color = new Color(1f, 0.85f, 0.2f, 0.75f + 0.25f * Mathf.Sin(t * 3f));
            foreach (var c in _caches)
                if (c != null && !c.IsEmpty) Dot(ToMap(c.transform.position), 5f);

            // Extraction beacons blink once the way out is open.
            if (match != null && match.State != null && match.State.IsExtractionOpen && Mathf.Repeat(t, 0.8f) < 0.5f)
            {
                GUI.color = new Color(0.3f, 1f, 0.4f);
                foreach (var zone in match.ExtractionZones)
                    if (zone != null) Dot(ToMap(zone.transform.position), 9f);
            }

            // The player, pointing where the camera looks.
            Vector2 at = ToMap(player.position);
            Vector3 f = view != null ? view.forward : player.forward;
            float heading = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(heading, at);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(at.x - 7f, at.y - 8f, 14f, 16f), Arrow());
            GUI.matrix = m;
            GUI.color = old;
        }

        private void Dot(Vector2 at, float d) => GUI.DrawTexture(new Rect(at.x - d * 0.5f, at.y - d * 0.5f, d, d), DotTexture());

        private void Build(IslandData data, Terrain terrain)
        {
            int res = data.Resolution;
            var s = data.Settings;
            float cell = s.worldSize / (res - 1);

            // Trees per heightmap cell, then summed over a small radius for density.
            var trees = new float[res, res];
            Vector3 origin = terrain.transform.position;
            ViewBlockers.ForEachTree(p =>
            {
                int x = Mathf.RoundToInt((p.x - origin.x) / cell), y = Mathf.RoundToInt((p.z - origin.z) / cell);
                if (x >= 0 && y >= 0 && x < res && y < res) trees[y, x] += 1f;
            });
            int r = Mathf.Max(1, Mathf.RoundToInt(ForestRadius / cell));

            if (_map != null) Object.Destroy(_map);
            _map = new Texture2D(res, res, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[res * res];
            var h = data.Heightmap;
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    Color c;
                    if (!data.LandMask[y, x])
                        c = h[y, x] > s.seaLevel ? Inland : Sea;
                    else
                    {
                        float count = 0f;
                        for (int dy = -r; dy <= r; dy++)
                            for (int dx = -r; dx <= r; dx++)
                            {
                                int yy = y + dy, xx = x + dx;
                                if (yy >= 0 && xx >= 0 && yy < res && xx < res) count += trees[yy, xx];
                            }
                        float steep = Slope(h, res, x, y, cell, s.maxHeight);
                        c = Color.Lerp(Bare, Rock, Mathf.InverseLerp(0.5f, 1.2f, steep));
                        if (count > 0f) c = Color.Lerp(LightWood, DeepWood, Mathf.Clamp01(count / TreesForDeepWood));
                        // Light from the north-west so hills stand out.
                        float dx2 = Height(h, res, x + 1, y) - Height(h, res, x - 1, y);
                        float dy2 = Height(h, res, x, y + 1) - Height(h, res, x, y - 1);
                        float shade = 1f + (dx2 - dy2) * s.maxHeight / (cell * 6f);
                        c *= Mathf.Clamp(shade, 0.65f, 1.25f);
                        c.a = 1f;
                    }
                    px[y * res + x] = c; // texture rows run bottom to top, like the heightmap's y (north)
                }
            }
            _map.SetPixels(px);
            _map.Apply();
        }

        private static float Height(float[,] h, int res, int x, int y) => h[Mathf.Clamp(y, 0, res - 1), Mathf.Clamp(x, 0, res - 1)];

        // Rise over run, in metres per metre.
        private static float Slope(float[,] h, int res, int x, int y, float cell, float maxHeight)
        {
            float gx = (Height(h, res, x + 1, y) - Height(h, res, x - 1, y)) * maxHeight / (2f * cell);
            float gy = (Height(h, res, x, y + 1) - Height(h, res, x, y - 1)) * maxHeight / (2f * cell);
            return Mathf.Sqrt(gx * gx + gy * gy);
        }

        private Texture2D DotTexture()
        {
            if (_dot != null) return _dot;
            const int n = 16;
            _dot = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x - n * 0.5f + 0.5f, y - n * 0.5f + 0.5f).magnitude / (n * 0.5f);
                    _dot.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * 4f)));
                }
            _dot.Apply();
            return _dot;
        }

        // A white arrowhead pointing up, with a dark edge so it reads on any ground.
        private Texture2D Arrow()
        {
            if (_arrow != null) return _arrow;
            const int w = 14, hgt = 16;
            _arrow = new Texture2D(w, hgt, TextureFormat.RGBA32, false);
            for (int y = 0; y < hgt; y++)
                for (int x = 0; x < w; x++)
                {
                    float fy = 1f - (float)y / (hgt - 1);          // 0 at the top (tip) of the GUI image
                    float half = fy * (w * 0.5f);                  // widens toward the base
                    float dx = Mathf.Abs(x + 0.5f - w * 0.5f);
                    bool inside = dx <= half && fy < 0.95f;
                    bool edge = inside && dx > half - 1.5f;
                    _arrow.SetPixel(x, y, inside ? (edge ? new Color(0f, 0f, 0f, 1f) : Color.white) : Color.clear);
                }
            _arrow.Apply();
            return _arrow;
        }
    }
}
