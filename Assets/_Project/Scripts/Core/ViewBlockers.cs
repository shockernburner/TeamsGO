using System.Collections.Generic;
using UnityEngine;

namespace ProjectFossil.Core
{
    // What the island tells the camera, the wind and the stealth rules about its vegetation: where the sea surface
    // is, which plants can get between the camera and the player, which plants and trees sway, and which ones a
    // player can hide in. Plants have no colliders (they're walk-through), so everything asks here instead.
    // The island decorator fills it and clears it for every new island.
    public static class ViewBlockers
    {
        public static float WaterHeight { get; private set; } = float.NegativeInfinity;

        private const float Cell = 8f;
        // Largest plant registered so far: queries look this far into neighbouring cells, so a wide plant whose
        // centre is a few metres off still hides when its leaves reach the camera.
        private static float _maxRadius;

        public struct Entry
        {
            public Vector3    Center;
            public float      Radius;
            public Renderer[] Renderers;  // null for things that sway but never hide (trees)
            public Transform  Root;       // what the wind turns; null = doesn't sway
            public Quaternion RestRotation;
            public float      Sway;       // wind strength multiplier (plants 1, trees small)
            public bool       Cover;      // a player inside it is hard to see
            public bool       Tree;       // canopy and trunk: counts toward how dense the forest is here
        }

        private static readonly Dictionary<long, List<Entry>> Grid = new Dictionary<long, List<Entry>>();

        public static int Count { get; private set; }

        public static void Clear()
        {
            Grid.Clear();
            Water.Clear();
            Count = 0;
            _maxRadius = 0f;
            WaterHeight = float.NegativeInfinity;
        }

        public static void SetWaterHeight(float y) => WaterHeight = y;

        // Rivers and lakes, as circles with their surface height (centre.y). Few enough to scan directly.
        private static readonly List<(Vector3 center, float radius)> Water = new List<(Vector3, float)>();

        public static void RegisterWater(Vector3 surfaceCenter, float radius)
        {
            if (radius > 0f) Water.Add((surfaceCenter, radius));
        }

        // The highest water surface over this spot: the sea, or a river or lake it lies in. -Infinity on dry land
        // away from the sea.
        public static float SurfaceAt(Vector3 p)
        {
            float s = WaterHeight;
            foreach (var (c, r) in Water)
            {
                if (c.y <= s) continue;
                float dx = c.x - p.x, dz = c.z - p.z;
                if (dx * dx + dz * dz <= r * r) s = c.y;
            }
            return s;
        }

        // Feet in the sea, a river or a lake.
        public static bool InWater(Vector3 feet)
        {
            if (feet.y < WaterHeight - 0.05f) return true;
            foreach (var (c, r) in Water)
            {
                if (feet.y > c.y + 0.05f) continue;
                float dx = c.x - feet.x, dz = c.z - feet.z;
                if (dx * dx + dz * dz <= r * r) return true;
            }
            return false;
        }

        // A walk-through plant: hides from the camera when in the way, sways, and gives cover if it's big enough.
        public static void Register(Vector3 center, float radius, Renderer[] renderers,
                                    Transform root = null, float sway = 1f, bool cover = false)
        {
            if (renderers == null || renderers.Length == 0) return;
            Add(new Entry
            {
                Center = center, Radius = radius, Renderers = renderers,
                Root = root, RestRotation = root != null ? root.localRotation : Quaternion.identity,
                Sway = sway, Cover = cover,
            });
        }

        // A tree: never hidden from the camera, but part of the forest a player can disappear into. `root` (may be
        // null) is what the wind leans.
        public static void RegisterTree(Vector3 center, Transform root, float sway)
        {
            Add(new Entry
            {
                Center = center, Radius = 0f, Root = root,
                RestRotation = root != null ? root.localRotation : Quaternion.identity,
                Sway = sway, Tree = true,
            });
        }

        // Every registered tree's position, e.g. to shade the forest on the map.
        public static void ForEachTree(System.Action<Vector3> visit)
        {
            foreach (var list in Grid.Values)
                foreach (var e in list)
                    if (e.Tree) visit(e.Center);
        }

        // Every inland water disc (rivers and lakes), as registered: centre at the water surface, and radius.
        public static void ForEachWater(System.Action<Vector3, float> visit)
        {
            foreach (var (c, r) in Water) visit(c, r);
        }

        private static void Add(Entry e)
        {
            long key = Key(Mathf.FloorToInt(e.Center.x / Cell), Mathf.FloorToInt(e.Center.z / Cell));
            if (!Grid.TryGetValue(key, out var list)) Grid[key] = list = new List<Entry>();
            list.Add(e);
            Count++;
            if (e.Radius > _maxRadius) _maxRadius = e.Radius;
        }

        // Every plant whose rough sphere comes within `pad` of the segment a→b.
        public static void Overlapping(Vector3 a, Vector3 b, float pad, List<Renderer[]> results)
        {
            results.Clear();
            if (Count == 0) return;

            float grow = pad + _maxRadius;
            int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - grow) / Cell), x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + grow) / Cell);
            int z0 = Mathf.FloorToInt((Mathf.Min(a.z, b.z) - grow) / Cell), z1 = Mathf.FloorToInt((Mathf.Max(a.z, b.z) + grow) / Cell);

            for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                if (!Grid.TryGetValue(Key(x, z), out var list)) continue;
                foreach (var e in list)
                {
                    if (e.Renderers == null) continue;
                    float reach = e.Radius + pad;
                    if (DistanceSqToSegment(e.Center, a, b) <= reach * reach) results.Add(e.Renderers);
                }
            }
        }

        // How hidden someone standing at `position` is, 0 (open ground) to 1 (deep in thick forest). Trunks and
        // canopy around them count most, big plants close by add to it, and standing inside one adds more. A lone
        // bush in a field is only partial cover; it takes a proper stand of trees and undergrowth to vanish.
        public const float TreeReach  = 7f;
        public const float PlantReach = 3f;
        private const float FullCover = 3f;

        public static float Concealment(Vector3 position)
        {
            if (Count == 0) return 0f;
            float score = 0f;
            int cx = Mathf.FloorToInt(position.x / Cell), cz = Mathf.FloorToInt(position.z / Cell);
            for (int x = cx - 1; x <= cx + 1; x++)
            for (int z = cz - 1; z <= cz + 1; z++)
            {
                if (!Grid.TryGetValue(Key(x, z), out var list)) continue;
                foreach (var e in list)
                {
                    float dx = e.Center.x - position.x, dz = e.Center.z - position.z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (e.Tree)
                    {
                        if (d < TreeReach) score += 1f - d / TreeReach;
                    }
                    else if (e.Cover)
                    {
                        if (d < e.Radius * 0.8f) score += 1f;                    // inside it
                        if (d < PlantReach)      score += 0.6f * (1f - d / PlantReach);
                    }
                }
            }
            return Mathf.Clamp01(score / FullCover);
        }

        // Concealment at which someone keeping low counts as hidden, and at which the HUD calls it cover at all.
        public const float HiddenAt = 0.7f;
        public const float CoverAt  = 0.3f;

        public static bool InCover(Vector3 position) => Concealment(position) >= CoverAt;

        // Calls `visit` for every swaying thing whose centre lies within `radius` (flat distance) of `center`.
        // The visitor gets the grid list and the index, so it can read the entry without copying the whole list.
        public delegate void SwayVisitor(in Entry entry);

        public static void ForEachSwaying(Vector3 center, float radius, SwayVisitor visit)
        {
            if (Count == 0 || visit == null) return;
            int x0 = Mathf.FloorToInt((center.x - radius) / Cell), x1 = Mathf.FloorToInt((center.x + radius) / Cell);
            int z0 = Mathf.FloorToInt((center.z - radius) / Cell), z1 = Mathf.FloorToInt((center.z + radius) / Cell);
            float r2 = radius * radius;
            for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                if (!Grid.TryGetValue(Key(x, z), out var list)) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    var e = list[i];
                    if (e.Root == null) continue;
                    float dx = e.Center.x - center.x, dz = e.Center.z - center.z;
                    if (dx * dx + dz * dz <= r2) visit(in e);
                }
            }
        }

        public static float DistanceSqToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len = ab.sqrMagnitude;
            float t = len > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len) : 0f;
            return (a + ab * t - p).sqrMagnitude;
        }

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
    }
}
