using System.Collections.Generic;
using UnityEngine;

namespace ProjectFossil.Generation
{
    // The shape of the drawn water surfaces, shared by the generator (which keeps their edges tucked under the
    // ground), the decorator (which draws them) and the tests. A river is a ribbon through its points with a
    // rounded end at each end; a lake is a disc.
    public static class WaterShape
    {
        public const int LakeRimPoints = 24;
        private const int CapRimPoints = 5;

        // Points on the rim of a river's drawn surface at point i: both sides, and round the cap past each end.
        public static void RiverRim(IslandSettings s, RiverPath river, int i, List<Vector3> into)
        {
            into.Clear();
            int n = river.Points.Count;
            Vector3 p = river.Points[i];
            Vector3 dir = river.Points[Mathf.Min(n - 1, i + 1)] - river.Points[Mathf.Max(0, i - 1)];
            float len = Mathf.Sqrt(dir.x * dir.x + dir.z * dir.z);
            if (len < 1e-4f) return;
            float w = s.RiverSurfaceHalfWidth(river.HalfWidths[i]);
            float fx = dir.x / len, fz = dir.z / len;
            into.Add(p + new Vector3(-fz * w, 0f, fx * w));
            into.Add(p - new Vector3(-fz * w, 0f, fx * w));
            if (i != 0 && i != n - 1) return;
            float sign = i == 0 ? -1f : 1f;
            for (int k = 1; k <= CapRimPoints; k++)
            {
                // Round the half circle from one side, through straight ahead, to the other.
                float a = k * Mathf.PI / (CapRimPoints + 1), c = Mathf.Cos(a), sn = Mathf.Sin(a) * sign;
                into.Add(p + new Vector3(-fz * c + fx * sn, 0f, fx * c + fz * sn) * w);
            }
        }

        public static void LakeRim(IslandSettings s, Lake lake, List<Vector3> into)
        {
            into.Clear();
            float r = s.LakeSurfaceRadius(lake.Radius);
            for (int k = 0; k < LakeRimPoints; k++)
            {
                float a = k * Mathf.PI * 2f / LakeRimPoints;
                into.Add(lake.Center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
        }

        // Whether a surface at height y may end over the ground there: the ground stands above it, or the sea
        // covers that ground at about the surface's height (a river's mouth).
        public static bool EdgeTucked(IslandSettings s, float ground, float y, float clearance, float step) =>
            ground >= y + clearance || (ground < s.seaLevel * s.maxHeight && s.seaLevel * s.maxHeight >= y - step);

        // The highest drawn water surface over q, counting only surfaces that reach at least `inset` metres past
        // it; float.MinValue where there is none.
        public static float SurfaceOver(IslandSettings s, IReadOnlyList<RiverPath> rivers, IReadOnlyList<Lake> lakes,
                                        Vector3 q, float inset)
        {
            float best = float.MinValue;
            foreach (var lake in lakes)
            {
                float r = s.LakeSurfaceRadius(lake.Radius) - inset, dx = q.x - lake.Center.x, dz = q.z - lake.Center.z;
                if (dx * dx + dz * dz < r * r) best = Mathf.Max(best, lake.Center.y);
            }
            foreach (var river in rivers)
            {
                int n = river.Points.Count;
                for (int k = 0; k < n; k++)
                {
                    Vector3 a = river.Points[k], b = river.Points[Mathf.Min(n - 1, k + 1)];
                    float ex = b.x - a.x, ez = b.z - a.z, len2 = ex * ex + ez * ez;
                    float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(((q.x - a.x) * ex + (q.z - a.z) * ez) / len2);
                    float px = a.x + ex * t - q.x, pz = a.z + ez * t - q.z;
                    float w = Mathf.Lerp(s.RiverSurfaceHalfWidth(river.HalfWidths[k]),
                                         s.RiverSurfaceHalfWidth(river.HalfWidths[Mathf.Min(n - 1, k + 1)]), t) - inset;
                    if (w > 0f && px * px + pz * pz < w * w) best = Mathf.Max(best, Mathf.Lerp(a.y, b.y, t));
                }
            }
            return best;
        }
    }
}
