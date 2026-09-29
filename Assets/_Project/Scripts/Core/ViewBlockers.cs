using System.Collections.Generic;
using UnityEngine;

namespace ProjectFossil.Core
{
    // What the island tells the camera: where the sea surface is, and which plants can get between the camera and
    // the player. Plants have no colliders (they're walk-through), so the camera can't raycast them; it asks here.
    // Presentation only. The island decorator fills it and clears it for every new island.
    public static class ViewBlockers
    {
        public static float WaterHeight { get; private set; } = float.NegativeInfinity;

        private const float Cell = 8f;

        private struct Entry
        {
            public Vector3    Center;
            public float      Radius;
            public Renderer[] Renderers;
        }

        private static readonly Dictionary<long, List<Entry>> Grid = new Dictionary<long, List<Entry>>();

        public static int Count { get; private set; }

        public static void Clear()
        {
            Grid.Clear();
            Count = 0;
            WaterHeight = float.NegativeInfinity;
        }

        public static void SetWaterHeight(float y) => WaterHeight = y;

        public static void Register(Vector3 center, float radius, Renderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0) return;
            long key = Key(Mathf.FloorToInt(center.x / Cell), Mathf.FloorToInt(center.z / Cell));
            if (!Grid.TryGetValue(key, out var list)) Grid[key] = list = new List<Entry>();
            list.Add(new Entry { Center = center, Radius = radius, Renderers = renderers });
            Count++;
        }

        // Every plant whose rough sphere comes within `pad` of the segment a→b.
        public static void Overlapping(Vector3 a, Vector3 b, float pad, List<Renderer[]> results)
        {
            results.Clear();
            if (Count == 0) return;

            const float maxPlantRadius = 3f;
            float grow = pad + maxPlantRadius;
            int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - grow) / Cell), x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + grow) / Cell);
            int z0 = Mathf.FloorToInt((Mathf.Min(a.z, b.z) - grow) / Cell), z1 = Mathf.FloorToInt((Mathf.Max(a.z, b.z) + grow) / Cell);

            for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                if (!Grid.TryGetValue(Key(x, z), out var list)) continue;
                foreach (var e in list)
                {
                    float reach = e.Radius + pad;
                    if (DistanceSqToSegment(e.Center, a, b) <= reach * reach) results.Add(e.Renderers);
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
